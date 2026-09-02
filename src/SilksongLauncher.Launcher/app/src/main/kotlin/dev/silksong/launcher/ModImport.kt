// Installing a mod from a folder selected through Android's document picker.
package dev.silksong.launcher

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.DocumentsContract
import java.io.Closeable
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.security.MessageDigest
import java.text.Normalizer
import java.util.ArrayDeque
import java.util.Locale
import java.util.Properties
import java.util.UUID
import java.util.concurrent.CancellationException

object ModImport {
    private const val MAX_FILES = 4_000
    private const val MAX_BYTES = 512L * 1024 * 1024
    private const val MAX_NODES = 8_000
    private const val MAX_DEPTH = 64
    private const val MAX_TRANSACTIONS = 128
    private const val MAX_COMPONENT_BYTES = 255
    private const val MAX_DOCUMENT_ID_BYTES = 4 * 1024
    private const val MAX_CURSOR_METADATA_BYTES = 4 * 1024 * 1024
    private const val MAX_METADATA_BYTES = 16 * 1024
    private const val MAX_VERIFIED_FILES = MAX_FILES + 1
    private const val MAX_VERIFIED_BYTES = MAX_BYTES + MAX_METADATA_BYTES
    private const val MAX_VERIFIED_NODES = MAX_NODES + 1
    private const val MAX_TRANSACTION_NODES = MAX_NODES + 3
    private const val MAX_TRANSACTION_DEPTH = MAX_DEPTH + 1
    private const val TRANSACTION_DIR = ".dualsouls-mod-import"
    private const val MANIFEST = "transaction.properties"
    private const val IDENTITY = ".dualsouls-import-v1.properties"
    private val RESERVED_NAMES = setOf(
        "config",
        "disabled.txt",
        "disabled-assemblies.txt",
        "readme.txt",
        TRANSACTION_DIR,
    )

    data class Result(val name: String, val files: Int, val plugins: Int)

    data class Progress(val files: Int = 0, val bytes: Long = 0, val message: String = "")

    internal data class PriorTarget(
        val present: Boolean,
        val identity: String? = null,
        val digest: String? = null,
    )

    internal data class ImportPlan(
        val displayName: String,
        val targetName: String,
        val identity: String,
        val target: File,
        val requiresConfirmation: Boolean,
        val prior: PriorTarget,
    )

    internal data class Transaction(
        val root: File,
        val staging: File,
        val backup: File,
        val plan: ImportPlan,
        var payloadDigest: String? = null,
    )

    internal data class DocumentEntry(
        val documentId: String,
        val displayName: String,
        val directory: Boolean,
        val declaredSize: Long,
    )

    internal interface DocumentSource {
        fun children(documentId: String): List<DocumentEntry>
        fun open(documentId: String): InputStream
    }

    internal data class ImportState(val busy: Boolean, val progress: Progress)

    internal class ImportCoordinator {
        private var active = false
        private var current = Progress()

        @Synchronized
        fun begin(): Session? {
            if (active) return null
            active = true
            current = Progress(message = "starting")
            return Session(this)
        }

        @Synchronized
        fun snapshot(): ImportState = ImportState(active, current)

        private fun update(progress: Progress) = synchronized(this) {
            if (active) current = progress
        }

        private fun finish() = synchronized(this) {
            active = false
            current = Progress()
        }

        internal class Session(private val owner: ImportCoordinator) : Closeable {
            private var closed = false

            fun progress(value: Progress) {
                if (!closed) owner.update(value)
            }

            override fun close() {
                if (closed) return
                closed = true
                owner.finish()
            }
        }
    }

    private val coordinator = ImportCoordinator()

    fun intent(): Intent = DepotLocation.pickIntent()

    fun isBusy(): Boolean = coordinator.snapshot().busy

    internal fun validateComponent(name: String, allowMetadata: Boolean = false): String {
        if (name.isEmpty() || name == "." || name == "..") {
            throw IOException("a selected folder contains an invalid empty or dot name")
        }
        if (name.any { it == '/' || it == '\\' || Character.isISOControl(it) }) {
            throw IOException("a selected folder name contains a separator or control character")
        }
        val normalized = Normalizer.normalize(name, Normalizer.Form.NFC)
        if (normalized.isEmpty() || normalized == "." || normalized == "..") {
            throw IOException("a selected folder contains an invalid normalized name")
        }
        if (normalized.toByteArray(Charsets.UTF_8).size > MAX_COMPONENT_BYTES) {
            throw IOException("a selected folder name is too long")
        }
        if (!allowMetadata && normalized.equals(IDENTITY, ignoreCase = true)) {
            throw IOException("a selected folder uses launcher-owned import metadata")
        }
        return normalized
    }

    internal fun collisionKey(name: String, allowMetadata: Boolean = false): String =
        validateComponent(name, allowMetadata).lowercase(Locale.ROOT)

    internal fun validateDocumentId(id: String, maxBytes: Int = MAX_DOCUMENT_ID_BYTES): String {
        if (id.isBlank() || id.any(Character::isISOControl)) {
            throw IOException("the selected provider returned an invalid document id")
        }
        if (maxBytes < 1 || id.toByteArray(Charsets.UTF_8).size > maxBytes) {
            throw IOException("the selected provider returned a document id that is too long")
        }
        return id
    }

    internal class DocumentMetadataBudget(
        private val maxDocumentIdBytes: Int = MAX_DOCUMENT_ID_BYTES,
        private val maxMetadataBytes: Int = MAX_CURSOR_METADATA_BYTES,
    ) {
        private var metadataBytes = 0

        init {
            require(maxDocumentIdBytes > 0 && maxMetadataBytes >= 0)
        }

        fun entry(
            documentId: String,
            displayName: String,
            directory: Boolean,
            declaredSize: Long,
        ): DocumentEntry {
            val id = validateDocumentId(documentId, maxDocumentIdBytes)
            val name = validateComponent(displayName)
            val idBytes = id.toByteArray(Charsets.UTF_8).size
            val nameBytes = name.toByteArray(Charsets.UTF_8).size
            if (idBytes > maxMetadataBytes - metadataBytes) throw metadataLimitExceeded()
            val afterId = metadataBytes + idBytes
            if (nameBytes > maxMetadataBytes - afterId) throw metadataLimitExceeded()
            metadataBytes = afterId + nameBytes
            return DocumentEntry(id, name, directory, declaredSize.coerceAtLeast(0L))
        }

        private fun metadataLimitExceeded() = IOException(
            "the selected provider returned too much cursor metadata (over $maxMetadataBytes bytes)",
        )
    }

    internal fun uniqueExistingName(names: List<String>, requested: String): String? {
        val key = collisionKey(requested)
        val matches = names.filter { collisionKey(it) == key }
        if (matches.size > 1) throw IOException("multiple existing mod names collide with $requested")
        return matches.singleOrNull()
    }

    internal fun resolveRecoveryTarget(mods: File, liveNames: List<String>, requested: String): File {
        val name = uniqueExistingName(liveNames, requested) ?: validateComponent(requested)
        val target = File(mods, name).canonicalFile
        if (!isStrictlyContained(target, mods.canonicalFile)) {
            throw IOException("legacy target escapes mods")
        }
        return target
    }

    internal fun safeFolderName(name: String): String {
        val normalized = Normalizer.normalize(name, Normalizer.Form.NFC)
        val sanitized = normalized.replace(Regex("[^A-Za-z0-9._ -]"), "_").trim().take(96)
        return sanitized.takeUnless { it.isEmpty() || it == "." || it == ".." } ?: "mod"
    }

    private fun isStrictlyContained(candidate: File, owner: File): Boolean {
        val canonicalOwner = owner.canonicalFile
        val canonicalCandidate = candidate.canonicalFile
        val prefix = canonicalOwner.path.trimEnd(File.separatorChar) + File.separator
        return canonicalCandidate.path.startsWith(prefix)
    }

    internal fun resolveDestination(parent: File, component: String, staging: File): File {
        val safe = validateComponent(component)
        val canonicalStaging = staging.canonicalFile
        val canonicalParent = parent.canonicalFile
        if (canonicalParent != canonicalStaging && !isStrictlyContained(canonicalParent, canonicalStaging)) {
            throw IOException("the selected folder resolves outside import staging")
        }
        val destination = File(canonicalParent, safe).canonicalFile
        if (!isStrictlyContained(destination, canonicalStaging)) {
            throw IOException("the selected folder name escapes import staging")
        }
        return destination
    }

    internal fun requireSourceOutsideDestination(source: File, mods: File) {
        val canonicalSource = source.canonicalFile
        val canonicalMods = mods.canonicalFile
        val sourcePrefix = canonicalSource.path.trimEnd(File.separatorChar) + File.separator
        if (canonicalMods == canonicalSource || canonicalMods.path.startsWith(sourcePrefix)) {
            throw IOException("the app's mods folder is inside the selected folder; pick the mod's own folder")
        }
    }

    internal fun requireUniqueDirectPath(root: File, entry: File, seen: MutableSet<String>): File {
        if (Files.isSymbolicLink(entry.toPath())) {
            throw IOException("symbolic links are not accepted in imported mod folders")
        }
        val canonicalRoot = root.canonicalFile
        val canonical = entry.canonicalFile
        if (!isStrictlyContained(canonical, canonicalRoot)) {
            throw IOException("an imported path escapes the selected folder")
        }
        if (!seen.add(canonical.path)) {
            throw IOException("an imported path repeats through an alias")
        }
        return canonical
    }

    internal class StreamBudget(
        private val maxFiles: Int = MAX_FILES,
        private val maxBytes: Long = MAX_BYTES,
        private val maxNodes: Int = MAX_NODES,
        private val maxDepth: Int = MAX_DEPTH,
    ) {
        var files: Int = 0
            private set
        var bytes: Long = 0
            private set
        var nodes: Int = 0
            private set

        internal fun visit(depth: Int) {
            if (depth < 0 || depth > maxDepth || ++nodes > maxNodes) throw limitExceeded()
        }

        internal fun beginFile(declaredSize: Long) {
            files++
            if (files > maxFiles) throw limitExceeded()
            if (declaredSize > 0 && declaredSize > maxBytes - bytes) throw limitExceeded()
        }

        internal fun accept(count: Int) {
            if (count < 0 || count.toLong() > maxBytes - bytes) throw limitExceeded()
            bytes += count
        }

        private fun limitExceeded() = IOException(
            "that folder is too large or deep to be a mod (over " +
                "${maxBytes / (1024 * 1024)} MB, $maxFiles files, $maxNodes entries, or depth $maxDepth)",
        )
    }

    internal fun copyChecked(
        input: InputStream,
        output: OutputStream,
        declaredSize: Long,
        budget: StreamBudget,
        checkCancelled: () -> Unit = {},
    ) {
        budget.beginFile(declaredSize)
        val buffer = ByteArray(1 shl 16)
        while (true) {
            checkCancelled()
            val count = input.read(buffer)
            if (count < 0) return
            if (count == 0) {
                val one = input.read()
                if (one < 0) return
                budget.accept(1)
                output.write(one)
            } else {
                budget.accept(count)
                output.write(buffer, 0, count)
            }
        }
    }

    private data class DirectWork(val source: File, val destination: File, val depth: Int)

    internal fun copyDirectTree(
        from: File,
        into: File,
        budget: StreamBudget,
        checkCancelled: () -> Unit = {},
        onProgress: (Progress) -> Unit = {},
    ) {
        val canonicalRoot = from.canonicalFile
        if (!canonicalRoot.isDirectory || Files.isSymbolicLink(from.toPath())) {
            throw IOException("the selected mod folder is not a regular directory")
        }
        val seen = linkedSetOf(canonicalRoot.path)
        val pending = ArrayDeque<DirectWork>()
        budget.visit(0)
        pending.add(DirectWork(canonicalRoot, into.canonicalFile, 0))
        while (pending.isNotEmpty()) {
            checkCancelled()
            val work = pending.removeFirst()
            val children = work.source.listFiles()
                ?: throw IOException("could not list ${work.source}")
            val names = HashSet<String>()
            for (child in children.sortedBy { it.name }) {
                val component = validateComponent(child.name)
                if (!names.add(collisionKey(component))) {
                    throw IOException("a folder contains colliding names: $component")
                }
                val depth = work.depth + 1
                budget.visit(depth)
                val source = requireUniqueDirectPath(canonicalRoot, child, seen)
                val destination = resolveDestination(work.destination, component, into)
                when {
                    source.isDirectory -> {
                        if (!destination.mkdirs() && !destination.isDirectory) {
                            throw IOException("could not create $destination")
                        }
                        pending.add(DirectWork(source, destination, depth))
                    }
                    source.isFile -> {
                        if (destination.exists()) throw IOException("duplicate import destination: $destination")
                        source.inputStream().use { input ->
                            destination.outputStream().use { output ->
                                copyChecked(input, output, source.length(), budget, checkCancelled)
                            }
                        }
                        onProgress(Progress(budget.files, budget.bytes, component))
                    }
                    else -> throw IOException("unsupported entry in selected folder: $source")
                }
            }
        }
    }

    private data class DocumentWork(val id: String, val destination: File, val depth: Int)

    internal fun copyDocumentTree(
        source: DocumentSource,
        rootId: String,
        into: File,
        budget: StreamBudget,
        checkCancelled: () -> Unit = {},
        onProgress: (Progress) -> Unit = {},
    ) {
        val validatedRootId = validateDocumentId(rootId)
        val seenIds = linkedSetOf(validatedRootId)
        val pending = ArrayDeque<DocumentWork>()
        budget.visit(0)
        pending.add(DocumentWork(validatedRootId, into.canonicalFile, 0))
        while (pending.isNotEmpty()) {
            checkCancelled()
            val work = pending.removeFirst()
            val names = HashSet<String>()
            for (entry in source.children(work.id)) {
                val documentId = validateDocumentId(entry.documentId)
                if (!seenIds.add(documentId)) {
                    throw IOException("the selected provider repeated a document id")
                }
                val component = validateComponent(entry.displayName)
                if (!names.add(collisionKey(component))) {
                    throw IOException("the selected provider returned colliding names: $component")
                }
                val depth = work.depth + 1
                budget.visit(depth)
                val destination = resolveDestination(work.destination, component, into)
                if (entry.directory) {
                    if (!destination.mkdirs() && !destination.isDirectory) {
                        throw IOException("could not create $destination")
                    }
                    pending.add(DocumentWork(documentId, destination, depth))
                } else {
                    if (destination.exists()) throw IOException("duplicate import destination: $destination")
                    source.open(documentId).use { input ->
                        destination.outputStream().use { output ->
                            copyChecked(input, output, entry.declaredSize, budget, checkCancelled)
                        }
                    }
                    onProgress(Progress(budget.files, budget.bytes, component))
                }
            }
        }
    }

    private class AndroidDocumentSource(
        private val context: Context,
        private val tree: Uri,
    ) : DocumentSource {
        private var rows = 0
        private val metadata = DocumentMetadataBudget()

        override fun children(documentId: String): List<DocumentEntry> {
            val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, documentId)
            val projection = arrayOf(
                DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE,
                DocumentsContract.Document.COLUMN_SIZE,
            )
            val out = ArrayList<DocumentEntry>()
            context.contentResolver.query(children, projection, null, null, null)?.use { cursor ->
                while (cursor.moveToNext()) {
                    if (++rows > MAX_NODES) throw IOException("the selected provider returned too many rows")
                    val id = cursor.getString(0) ?: ""
                    val name = cursor.getString(1) ?: ""
                    val mime = cursor.getString(2)
                    val size = if (cursor.isNull(3)) 0L else cursor.getLong(3)
                    val entry = metadata.entry(
                        id,
                        name,
                        mime == DocumentsContract.Document.MIME_TYPE_DIR,
                        size,
                    )
                    out.add(entry)
                }
            } ?: throw IOException("that folder could not be read")
            return out
        }

        override fun open(documentId: String): InputStream {
            val file = DocumentsContract.buildDocumentUriUsingTree(tree, documentId)
            return context.contentResolver.openInputStream(file)
                ?: throw IOException("could not read a selected document")
        }
    }

    private fun sha256(value: String): String =
        MessageDigest.getInstance("SHA-256").digest(value.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }

    private fun reserved(name: String): Boolean {
        val lower = name.lowercase(Locale.ROOT)
        return lower in RESERVED_NAMES ||
            (lower.startsWith(".") && (lower.endsWith(".incoming") || lower.endsWith(".backup")))
    }

    internal fun plan(displayName: String, sourceKey: String, mods: File): ImportPlan {
        val normalized = validateComponent(displayName)
        val identity = sha256(sourceKey)
        val safe = safeFolderName(normalized)
        if (reserved(normalized) || reserved(safe)) throw IOException("that folder name is reserved by the launcher")
        val base = if (safe == normalized) safe else "$safe-${identity.take(12)}"
        val existingNames = if (mods.isDirectory) {
            val entries = mods.listFiles() ?: throw IOException("could not inspect existing mod names")
            if (entries.size > MAX_NODES) throw IOException("mod library has too many top-level entries")
            entries.map { validateComponent(it.name) }
        } else {
            emptyList()
        }

        fun candidate(name: String): ImportPlan? {
            if (reserved(name)) throw IOException("that folder name is reserved by the launcher")
            val targetName = uniqueExistingName(existingNames, name) ?: name
            val target = File(mods, targetName).canonicalFile
            if (!isStrictlyContained(target, mods.canonicalFile)) throw IOException("mod destination escapes its owner")
            if (!target.exists()) {
                return ImportPlan(
                    normalized,
                    targetName,
                    identity,
                    target,
                    false,
                    PriorTarget(present = false),
                )
            }
            if (!target.isDirectory || Files.isSymbolicLink(target.toPath())) {
                throw IOException("the mod destination is not a regular folder: $targetName")
            }
            val found = readIdentity(target)
            return when {
                found == identity -> ImportPlan(
                    normalized,
                    targetName,
                    identity,
                    target,
                    false,
                    capturePrior(target, found),
                )
                found == null -> ImportPlan(
                    normalized,
                    targetName,
                    identity,
                    target,
                    true,
                    capturePrior(target, null),
                )
                else -> null
            }
        }

        candidate(base)?.let { return it }
        val collisionName = "$safe-$identity"
        return candidate(collisionName)
            ?: throw IOException("a different verified import already owns the collision-resistant destination")
    }

    internal fun transactionRoot(mods: File): File {
        val parent = requireNotNull(mods.canonicalFile.parentFile) { "mods folder has no parent" }
        val root = File(parent, TRANSACTION_DIR).canonicalFile
        if (root == mods.canonicalFile || isStrictlyContained(root, mods.canonicalFile)) {
            throw IOException("mod transactions must be outside the plugin namespace")
        }
        if (root.parentFile != parent) throw IOException("mod transaction root escapes app storage")
        return root
    }

    private fun propertiesOf(vararg values: Pair<String, String>): Properties =
        Properties().apply { values.forEach { (key, value) -> setProperty(key, value) } }

    private fun publishProperties(file: File, properties: Properties) {
        file.parentFile?.mkdirs()
        val part = File(file.parentFile, "${file.name}.part")
        if (part.exists() && !part.delete()) throw IOException("could not clear interrupted metadata at $part")
        part.writer(Charsets.UTF_8).use { properties.store(it, null) }
        try {
            Files.move(part.toPath(), file.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
        } catch (_: AtomicMoveNotSupportedException) {
            Files.move(part.toPath(), file.toPath(), REPLACE_EXISTING)
        }
        if (!file.isFile || part.exists()) throw IOException("could not publish metadata at $file")
    }

    internal fun writeIdentity(target: File, plan: ImportPlan) {
        if (!target.isDirectory) throw IOException("cannot identify a missing mod folder")
        publishProperties(
            File(target, IDENTITY),
            propertiesOf(
                "version" to "1",
                "identity" to plan.identity,
                "displayName" to plan.displayName,
                "targetName" to plan.targetName,
            ),
        )
    }

    private fun readProperties(file: File): Properties? {
        if (!file.isFile || Files.isSymbolicLink(file.toPath()) || file.length() > MAX_METADATA_BYTES) return null
        return runCatching { Properties().apply { file.reader(Charsets.UTF_8).use(::load) } }.getOrNull()
    }

    private fun readIdentity(target: File): String? {
        val values = readProperties(File(target, IDENTITY)) ?: return null
        if (values.getProperty("version") != "1") return null
        return values.getProperty("identity")?.takeIf { it.matches(Regex("[0-9a-f]{64}")) }
    }

    private fun capturePrior(target: File, identity: String?): PriorTarget {
        if (!target.isDirectory || Files.isSymbolicLink(target.toPath())) {
            throw IOException("the existing mod destination is not a regular folder")
        }
        val digest = digestOwnedTree(target)
        if (!target.isDirectory || Files.isSymbolicLink(target.toPath()) || readIdentity(target) != identity) {
            throw IOException("the existing mod destination changed while it was inspected")
        }
        return PriorTarget(present = true, identity = identity, digest = digest)
    }

    private fun matchesPrior(target: File, prior: PriorTarget): Boolean {
        if (!prior.present) return !target.exists()
        if (!target.isDirectory || Files.isSymbolicLink(target.toPath())) return false
        if (readIdentity(target) != prior.identity) return false
        val expected = prior.digest ?: return false
        return runCatching { digestOwnedTree(target) == expected }.getOrDefault(false)
    }

    private fun writeManifest(transaction: Transaction) {
        publishProperties(
            File(transaction.root, MANIFEST),
            propertiesOf(
                "version" to "2",
                "targetName" to transaction.plan.targetName,
                "displayName" to transaction.plan.displayName,
                "identity" to transaction.plan.identity,
                "priorPresent" to transaction.plan.prior.present.toString(),
                "priorIdentity" to transaction.plan.prior.identity.orEmpty(),
                "priorDigest" to transaction.plan.prior.digest.orEmpty(),
                "payloadDigest" to transaction.payloadDigest.orEmpty(),
            ),
        )
    }

    internal fun beginTransaction(mods: File, plan: ImportPlan): Transaction {
        val root = transactionRoot(mods)
        if (!root.mkdirs() && !root.isDirectory) throw IOException("could not create mod transaction storage")
        val existing = root.listFiles() ?: throw IOException("could not inspect mod transaction storage")
        if (existing.size >= MAX_TRANSACTIONS) throw IOException("too many unfinished mod import transactions")
        val owned = File(root, UUID.randomUUID().toString().replace("-", ""))
        if (!owned.mkdir()) throw IOException("could not create a mod import transaction")
        val transaction = Transaction(owned, File(owned, "staging"), File(owned, "backup"), plan)
        writeManifest(transaction)
        return transaction
    }

    private fun readTransaction(mods: File, root: File): Transaction? {
        val values = readProperties(File(root, MANIFEST)) ?: return null
        if (values.getProperty("version") != "2") throw IOException("unsupported mod transaction metadata")
        val targetName = validateComponent(values.getProperty("targetName") ?: "")
        if (reserved(targetName)) throw IOException("mod transaction names a reserved destination")
        val displayName = validateComponent(values.getProperty("displayName") ?: "")
        val identity = values.getProperty("identity") ?: ""
        if (!identity.matches(Regex("[0-9a-f]{64}"))) throw IOException("invalid mod transaction identity")
        val target = File(mods, targetName).canonicalFile
        if (!isStrictlyContained(target, mods.canonicalFile)) throw IOException("mod transaction target escapes mods")
        val priorPresent = when (values.getProperty("priorPresent")) {
            "true" -> true
            "false" -> false
            else -> throw IOException("invalid mod transaction prior-target state")
        }
        val priorIdentity = values.getProperty("priorIdentity").orEmpty().takeIf { it.isNotEmpty() }
        if (priorIdentity != null && !priorIdentity.matches(Regex("[0-9a-f]{64}"))) {
            throw IOException("invalid mod transaction prior identity")
        }
        val priorDigest = values.getProperty("priorDigest").orEmpty().takeIf { it.isNotEmpty() }
        if (priorDigest != null && !priorDigest.matches(Regex("[0-9a-f]{64}"))) {
            throw IOException("invalid mod transaction prior digest")
        }
        if (priorPresent != (priorDigest != null) || (!priorPresent && priorIdentity != null)) {
            throw IOException("inconsistent mod transaction prior-target state")
        }
        val payload = values.getProperty("payloadDigest").orEmpty().takeIf { it.isNotEmpty() }
        if (payload != null && !payload.matches(Regex("[0-9a-f]{64}"))) {
            throw IOException("invalid mod transaction payload digest")
        }
        val prior = PriorTarget(priorPresent, priorIdentity, priorDigest)
        val plan = ImportPlan(displayName, targetName, identity, target, false, prior)
        return Transaction(root, File(root, "staging"), File(root, "backup"), plan, payload)
    }

    /** Same-filesystem promotion with explicit no-replace behavior. */
    private fun moveSameFilesystem(source: File, destination: File): Boolean = try {
        Files.move(source.toPath(), destination.toPath())
        true
    } catch (_: IOException) {
        false
    }

    private data class OwnedEntry(val file: File, val depth: Int)

    private fun inspectOwnedTree(root: File, maxNodes: Int, maxDepth: Int): List<OwnedEntry>? {
        if (!root.exists()) return emptyList()
        val canonicalRoot = root.canonicalFile
        val pending = ArrayDeque<OwnedEntry>()
        val out = ArrayList<OwnedEntry>()
        val seen = HashSet<String>()
        pending.add(OwnedEntry(root, 0))
        while (pending.isNotEmpty()) {
            val entry = pending.removeFirst()
            if (entry.depth > maxDepth || out.size >= maxNodes) return null
            val symbolic = Files.isSymbolicLink(entry.file.toPath())
            val canonical = entry.file.canonicalFile
            if (entry.depth > 0 && !isStrictlyContained(canonical, canonicalRoot)) return null
            if (!seen.add(canonical.path)) return null
            out += entry
            if (entry.file.isDirectory && !symbolic) {
                val children = entry.file.listFiles() ?: return null
                for (child in children) pending.add(OwnedEntry(child, entry.depth + 1))
            }
        }
        return out
    }

    internal fun deleteOwnedTree(
        root: File,
        maxNodes: Int = MAX_TRANSACTION_NODES,
        maxDepth: Int = MAX_TRANSACTION_DEPTH,
    ): Boolean {
        val entries = runCatching { inspectOwnedTree(root, maxNodes, maxDepth) }.getOrNull() ?: return false
        for (entry in entries.sortedByDescending { it.depth }) {
            if (entry.file.exists() && !entry.file.delete()) return false
        }
        return !root.exists()
    }

    internal fun digestOwnedTree(
        root: File,
        budget: StreamBudget = StreamBudget(
            maxFiles = MAX_VERIFIED_FILES,
            maxBytes = MAX_VERIFIED_BYTES,
            maxNodes = MAX_VERIFIED_NODES,
            maxDepth = MAX_DEPTH,
        ),
        checkCancelled: () -> Unit = {},
    ): String {
        val entries = inspectOwnedTree(root, MAX_VERIFIED_NODES, MAX_DEPTH)
            ?: throw IOException("tree cannot be verified within import bounds")
        val canonicalRoot = root.canonicalFile.toPath()
        val digest = MessageDigest.getInstance("SHA-256")
        for (entry in entries.sortedBy { it.file.canonicalPath }) {
            checkCancelled()
            val file = entry.file
            budget.visit(entry.depth)
            if (Files.isSymbolicLink(file.toPath())) {
                throw IOException("tree cannot be verified because it contains a symbolic link")
            }
            val relative = canonicalRoot.relativize(file.canonicalFile.toPath()).toString()
            digest.update((if (file.isDirectory) 0 else 1).toByte())
            digest.update(relative.toByteArray(Charsets.UTF_8))
            digest.update(0.toByte())
            if (file.isFile) {
                val length = file.length()
                budget.beginFile(length)
                digest.update(length.toString().toByteArray(Charsets.UTF_8))
                digest.update(0.toByte())
                file.inputStream().use { input ->
                    val buffer = ByteArray(1 shl 16)
                    while (true) {
                        checkCancelled()
                        val count = input.read(buffer)
                        if (count < 0) break
                        if (count == 0) {
                            val one = input.read()
                            if (one < 0) break
                            budget.accept(1)
                            digest.update(one.toByte())
                        } else {
                            budget.accept(count)
                            digest.update(buffer, 0, count)
                        }
                    }
                }
            } else if (!file.isDirectory) {
                throw IOException("tree cannot be verified because it contains an unsupported entry")
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun restoreExact(
        target: File,
        backup: File,
        move: (File, File) -> Boolean,
    ): Boolean {
        if (!backup.exists() || target.exists()) return false
        val expected = runCatching { digestOwnedTree(backup) }.getOrNull() ?: return false
        runCatching { move(backup, target) }
        if (!target.exists() || backup.exists()) return false
        return runCatching { digestOwnedTree(target) == expected }.getOrDefault(false)
    }

    private fun seal(transaction: Transaction, checkCancelled: () -> Unit) {
        if (readIdentity(transaction.staging) != transaction.plan.identity) {
            throw IOException("staged mod identity is missing or invalid")
        }
        transaction.payloadDigest = digestOwnedTree(transaction.staging, checkCancelled = checkCancelled)
        writeManifest(transaction)
    }

    private fun verifiedPayload(
        transaction: Transaction,
        target: File,
        checkCancelled: () -> Unit = {},
    ): Boolean {
        val expected = transaction.payloadDigest ?: return false
        if (readIdentity(target) != transaction.plan.identity) return false
        return try {
            digestOwnedTree(target, checkCancelled = checkCancelled) == expected
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: Throwable) {
            false
        }
    }

    private fun restorePrior(
        transaction: Transaction,
        move: (File, File) -> Boolean,
    ): Boolean {
        val target = transaction.plan.target
        val backup = transaction.backup
        val prior = transaction.plan.prior
        if (!prior.present) return !target.exists() && !backup.exists()
        if (!backup.exists()) return matchesPrior(target, prior)
        if (!matchesPrior(backup, prior) || target.exists()) return false
        if (!move(backup, target) || backup.exists()) return false
        return matchesPrior(target, prior)
    }

    private fun canDiscardWithoutTargetRecovery(transaction: Transaction): Boolean =
        !transaction.backup.exists() &&
            (transaction.staging.exists() || transaction.payloadDigest == null)

    private fun rejectChangedBeforeMutation(transaction: Transaction): Nothing {
        if (transaction.backup.exists() || !transaction.staging.exists()) {
            throw IOException("mod destination changed after replacement began")
        }
        if (!deleteOwnedTree(transaction.root)) {
            throw IOException("mod destination changed and owned import staging could not be cleaned")
        }
        throw IOException("mod destination changed after import preparation")
    }

    internal fun replaceStaged(
        transaction: Transaction,
        checkCancelled: () -> Unit = {},
        move: (File, File) -> Boolean = ::moveSameFilesystem,
    ) {
        val target = transaction.plan.target
        val staging = transaction.staging
        val backup = transaction.backup
        val prior = transaction.plan.prior
        check(staging.isDirectory) { "mod replacement staging is missing" }
        seal(transaction, checkCancelled)
        if (!backup.exists() && !matchesPrior(target, prior)) {
            rejectChangedBeforeMutation(transaction)
        }
        try {
            if (backup.exists()) throw IOException("mod replacement backup already exists")
            if (prior.present) {
                if (!move(target, backup) || target.exists() || !backup.exists()) {
                    throw IOException("could not secure the prior mod before replacement")
                }
                if (!matchesPrior(backup, prior)) {
                    throw IOException("the secured prior mod changed before promotion")
                }
            }
            if (target.exists()) {
                throw IOException("the mod destination appeared before promotion")
            }
            if (!move(staging, target) || staging.exists() || !target.isDirectory ||
                !verifiedPayload(transaction, target, checkCancelled)
            ) {
                throw IOException("could not promote and verify the staged mod replacement")
            }
            if (backup.exists() && !deleteOwnedTree(backup)) {
                LauncherLog.log("mods: replacement installed; verified backup cleanup is pending at $backup")
                return
            }
            if (!deleteOwnedTree(transaction.root)) {
                LauncherLog.log("mods: replacement installed; transaction cleanup is pending at ${transaction.root}")
            }
        } catch (failure: Throwable) {
            if (!restorePrior(transaction, move)) {
                throw IOException(
                    "could not restore and verify the previous mod state after replacement failed; " +
                        "recovery transaction remains at ${transaction.root}",
                    failure,
                )
            }
            val stagingRemoved = deleteOwnedTree(staging)
            if (!stagingRemoved || !deleteOwnedTree(transaction.root)) {
                throw IOException("mod replacement failed; prior state was restored but cleanup failed", failure)
            }
            if (failure is CancellationException) throw failure
            throw IOException("could not promote staged mod; previous mod state was restored", failure)
        }
    }

    private fun reconcileLegacyTransactions(mods: File) {
        if (!mods.isDirectory) return
        val entries = mods.listFiles() ?: throw IOException("could not inspect legacy mod transactions")
        if (entries.size > MAX_NODES) throw IOException("mod library has too many top-level entries")
        data class Legacy(var incoming: File? = null, var backup: File? = null)
        val groups = linkedMapOf<String, Pair<String, Legacy>>()
        val pattern = Regex("^\\.(.+)\\.(incoming|backup)$", RegexOption.IGNORE_CASE)
        for (entry in entries) {
            val match = pattern.matchEntire(entry.name) ?: continue
            if (!entry.isDirectory || Files.isSymbolicLink(entry.toPath())) {
                throw IOException("legacy mod transaction is not a regular directory: ${entry.name}")
            }
            val targetName = validateComponent(match.groupValues[1])
            if (reserved(targetName)) throw IOException("legacy mod transaction names a reserved destination")
            val key = collisionKey(targetName)
            val group = groups.getOrPut(key) { targetName to Legacy() }
            if (group.first != targetName) throw IOException("legacy mod transactions contain colliding names")
            when (match.groupValues[2].lowercase(Locale.ROOT)) {
                "incoming" -> if (group.second.incoming == null) group.second.incoming = entry else {
                    throw IOException("multiple legacy staging directories name the same mod")
                }
                "backup" -> if (group.second.backup == null) group.second.backup = entry else {
                    throw IOException("multiple legacy backups name the same mod")
                }
            }
        }
        val liveNames = entries.filter { pattern.matchEntire(it.name) == null }.map { validateComponent(it.name) }
        for ((_, value) in groups) {
            val (targetName, remnants) = value
            val target = resolveRecoveryTarget(mods, liveNames, targetName)
            val backup = remnants.backup
            if (backup != null) {
                if (target.exists()) {
                    throw IOException("ambiguous legacy mod replacement has both target and backup")
                }
                if (!restoreExact(target, backup, ::moveSameFilesystem)) {
                    throw IOException("could not restore and verify legacy mod replacement")
                }
            }
            val incoming = remnants.incoming
            if (incoming != null && !deleteOwnedTree(incoming)) {
                throw IOException("could not clean abandoned legacy mod staging")
            }
        }
    }

    @Synchronized
    internal fun reconcileTransactions(
        mods: File,
        coordinator: ImportCoordinator = this.coordinator,
        importOwner: Boolean = false,
    ) {
        if (coordinator.snapshot().busy && !importOwner) {
            throw IOException("a mod import is in progress")
        }
        reconcileLegacyTransactions(mods)
        val root = transactionRoot(mods)
        if (!root.exists()) return
        if (!root.isDirectory || Files.isSymbolicLink(root.toPath())) {
            throw IOException("mod transaction storage is not a regular directory")
        }
        val entries = root.listFiles() ?: throw IOException("could not inspect mod transaction storage")
        if (entries.size > MAX_TRANSACTIONS) throw IOException("too many unfinished mod import transactions")
        for (owned in entries.sortedBy { it.name }) {
            if (!owned.isDirectory || Files.isSymbolicLink(owned.toPath()) ||
                !owned.name.matches(Regex("[0-9a-f]{32}"))
            ) {
                throw IOException("ambiguous file in mod transaction storage: ${owned.name}")
            }
            val transaction = readTransaction(mods, owned)
            if (transaction == null) {
                val children = owned.listFiles() ?: throw IOException("could not inspect incomplete transaction")
                if (children.any {
                        it.name != "$MANIFEST.part" || !it.isFile || Files.isSymbolicLink(it.toPath())
                    }
                ) {
                    throw IOException("ambiguous mod transaction without a manifest: ${owned.name}")
                }
                if (!deleteOwnedTree(owned)) throw IOException("could not clean incomplete mod transaction")
                continue
            }
            val children = owned.listFiles() ?: throw IOException("could not inspect mod transaction")
            for (child in children) {
                when (child.name) {
                    MANIFEST, "$MANIFEST.part" -> {
                        if (!child.isFile || Files.isSymbolicLink(child.toPath())) {
                            throw IOException("mod transaction metadata is not a regular file")
                        }
                    }
                    "staging", "backup" -> {
                        if (!child.isDirectory || Files.isSymbolicLink(child.toPath())) {
                            throw IOException("mod transaction payload is not a regular directory")
                        }
                    }
                    else -> throw IOException("ambiguous file in mod transaction: ${child.name}")
                }
            }
            val target = transaction.plan.target
            val staging = transaction.staging
            val backup = transaction.backup
            val prior = transaction.plan.prior
            when {
                backup.exists() -> {
                    if (!prior.present || !matchesPrior(backup, prior)) {
                        throw IOException("interrupted mod replacement backup cannot be verified")
                    }
                    when {
                        !target.exists() -> {
                            if (!restorePrior(transaction, ::moveSameFilesystem)) {
                                throw IOException("could not restore and verify interrupted mod replacement")
                            }
                            if (!deleteOwnedTree(staging) || !deleteOwnedTree(owned)) {
                                throw IOException("restored interrupted mod replacement but cleanup failed")
                            }
                        }
                        !staging.exists() && verifiedPayload(transaction, target) -> {
                            if (!deleteOwnedTree(backup) || !deleteOwnedTree(owned)) {
                                throw IOException("could not clean a completed mod replacement")
                            }
                        }
                        else -> throw IOException("ambiguous mod replacement has both target and backup")
                    }
                }
                canDiscardWithoutTargetRecovery(transaction) -> {
                    if (!deleteOwnedTree(owned)) {
                        throw IOException("could not clean abandoned mod import transaction")
                    }
                }
                transaction.payloadDigest != null && target.exists() -> {
                    if (!verifiedPayload(transaction, target)) {
                        throw IOException("completed mod import target cannot be verified")
                    }
                    if (!deleteOwnedTree(owned)) throw IOException("could not clean completed mod transaction")
                }
                else -> throw IOException("mod transaction prior target can no longer be verified")
            }
        }
        if (root.listFiles().orEmpty().isEmpty() && !root.delete()) {
            throw IOException("could not remove empty mod transaction storage")
        }
    }

    internal fun boundedFiles(root: File, excludedTopLevel: Set<String> = emptySet()): List<File> {
        if (!root.isDirectory) return emptyList()
        val excluded = excludedTopLevel.mapTo(HashSet()) { collisionKey(it) }
        val canonicalRoot = root.canonicalFile
        val seen = linkedSetOf(canonicalRoot.path)
        val pending = ArrayDeque<Pair<File, Int>>()
        val files = ArrayList<File>()
        var nodes = 1
        pending.add(canonicalRoot to 0)
        while (pending.isNotEmpty()) {
            val (directory, depth) = pending.removeFirst()
            if (depth > MAX_DEPTH) throw IOException("mod library is too deep")
            val children = directory.listFiles() ?: throw IOException("could not inspect mod library")
            val names = HashSet<String>()
            for (child in children.sortedBy { it.name }) {
                if (++nodes > MAX_NODES) throw IOException("mod library has too many entries")
                val component = validateComponent(child.name, allowMetadata = true)
                val key = collisionKey(component, allowMetadata = true)
                if (!names.add(key)) throw IOException("mod library has colliding names")
                val canonical = requireUniqueDirectPath(canonicalRoot, child, seen)
                if (canonical.isDirectory) {
                    if (depth == 0 && key in excluded) continue
                    pending.add(canonical to depth + 1)
                } else if (canonical.isFile) files += canonical
                else throw IOException("mod library contains an unsupported entry")
            }
        }
        return files
    }

    internal fun prepare(context: Context, uri: Uri, mods: File): ImportPlan =
        prepare(context, uri, mods, importOwner = false)

    private fun prepare(
        context: Context,
        uri: Uri,
        mods: File,
        importOwner: Boolean,
    ): ImportPlan {
        reconcileTransactions(mods, importOwner = importOwner)
        val id = runCatching { DocumentsContract.getTreeDocumentId(uri) }.getOrNull()
            ?: throw IOException("that folder has no document identity")
        val name = folderName(context, uri, id) ?: throw IOException("that folder has no name")
        return plan(name, "${uri.scheme}|${uri.authority}|$id", mods)
    }

    class ReplacementConfirmationRequired internal constructor(internal val plan: ImportPlan) :
        IOException("Replacing ${plan.targetName} requires confirmation because its import identity is unknown")

    internal fun copy(
        context: Context,
        uri: Uri,
        mods: File,
        requested: ImportPlan? = null,
        confirmed: Boolean = false,
        checkCancelled: () -> Unit = {},
        onProgress: (Progress) -> Unit = {},
    ): Result {
        val session = coordinator.begin() ?: throw IOException("another mod import is already running")
        var transaction: Transaction? = null
        try {
            val current = prepare(context, uri, mods, importOwner = true)
            val selected = requested ?: current
            if (selected.identity != current.identity || selected.targetName != current.targetName ||
                selected.prior != current.prior
            ) {
                throw IOException("the mod import destination changed; select the folder again")
            }
            if (current.requiresConfirmation && !confirmed) throw ReplacementConfirmationRequired(current)
            transaction = beginTransaction(mods, current)
            if (!transaction.staging.mkdir()) throw IOException("could not create mod import staging")
            val budget = StreamBudget()
            fun progress(value: Progress) {
                session.progress(value)
                onProgress(value)
            }
            val direct = DepotLocation.pathFor(uri)
            if (direct != null && direct.isDirectory) {
                requireSourceOutsideDestination(direct, mods)
                copyDirectTree(
                    direct,
                    transaction.staging,
                    budget,
                    checkCancelled = checkCancelled,
                    onProgress = ::progress,
                )
            } else {
                val id = DocumentsContract.getTreeDocumentId(uri)
                copyDocumentTree(
                    AndroidDocumentSource(context, uri),
                    id,
                    transaction.staging,
                    budget,
                    checkCancelled = checkCancelled,
                    onProgress = ::progress,
                )
            }
            if (budget.files == 0) throw IOException("there is nothing in that folder")
            writeIdentity(transaction.staging, current)
            replaceStaged(transaction, checkCancelled = checkCancelled)
            val plugins = boundedFiles(current.target).count { it.extension.equals("dll", true) }
            LauncherLog.log(
                "mods: imported ${current.targetName} -- ${budget.files} file(s), " +
                    "$plugins assembly/assemblies",
            )
            return Result(current.displayName, budget.files, plugins)
        } catch (failure: Throwable) {
            val active = transaction
            if (active != null && active.root.exists() &&
                canDiscardWithoutTargetRecovery(active) && !deleteOwnedTree(active.root)
            ) {
                throw IOException("mod import failed and transaction cleanup could not be verified", failure)
            }
            throw failure
        } finally {
            session.close()
        }
    }

    private fun folderName(context: Context, uri: Uri, id: String): String? {
        val document = runCatching { DocumentsContract.buildDocumentUriUsingTree(uri, id) }.getOrNull()
        if (document != null) {
            runCatching {
                context.contentResolver.query(
                    document,
                    arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME),
                    null,
                    null,
                    null,
                )?.use { if (it.moveToFirst()) it.getString(0) else null }
            }.getOrNull()?.let { if (it.isNotBlank()) return it }
        }
        return id.substringAfterLast(':').substringAfterLast('/').ifBlank { null }
    }
}
