package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaBudgets
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.DurableDirectoryPublisher
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.listBounded
import dev.silksong.launcher.skins.storage.requireContained
import java.io.ByteArrayOutputStream
import java.io.File
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.security.MessageDigest
import java.util.UUID

class SkinRegistryStore(
    skinsRoot: File,
    private val quota: SkinQuotaAdmission,
    private val fs: SkinFileSystem = AndroidSkinFileSystem(),
    private val lockManager: SkinLockManager = SkinLockManager(skinsRoot),
) {
    internal val root = skinsRoot.absoluteFile.normalize()
    private val staging = File(root, "staging")
    private val registry = File(root, "registry")
    private val generations = File(registry, "generations")
    private val publisher = DurableDirectoryPublisher(fs)

    init {
        require(root.name == "skins" && root.parentFile?.name == SkinRegistryAuthority.PROFILE_ID) {
            "Registry root must be the exact Hollow Knight profile skins child"
        }
        require(root.parentFile != null && root != root.parentFile) { "Registry root has no profile owner" }
        require(lockManager.root == root) { "Registry lock manager is bound to another profile" }
        require(quota.root.absoluteFile.normalize() == root) { "Registry quota authority is bound to another profile" }
        require(staging != registry && staging != generations && registry != generations) { "Registry paths alias" }
    }

    fun recover(): SkinResult<RegistryHead> = withQuota(SkinQuotaBudgets.REGISTRY_PUBLICATION) {
        recoverForCoordinator()
    }

    internal fun recoverForCoordinator(): SkinResult<RegistryHead> = locked { recoverLocked() }

    /** Validates the same bounded authority as recovery, but never persists layout or pointers. */
    internal fun snapshotForLibrary(): SkinResult<RegistryHead> = locked {
        try {
            fs.requireContained(root, root)
            require(fs.isDirectory(root) && !fs.isSymbolicLink(root)) { "Unsafe skin root" }
            for (directory in listOf(registry, generations)) {
                if (fs.exists(directory)) {
                    fs.requireContained(directory, root)
                    require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "Unsafe registry layout" }
                }
            }
            recoverLocked(layoutReady = true, readOnly = true)
        } catch (error: Exception) {
            corrupt("Cannot read registry snapshot: ${error.message}")
        }
    }

    fun commit(
        expected: RegistryHead,
        operationId: UUID,
        writer: String,
        mutation: RegistryMutation,
    ): SkinResult<RegistryHead> = withQuota(SkinQuotaBudgets.REGISTRY_PUBLICATION) {
        commitAdmitted(expected, operationId, writer, mutation)
    }

    internal fun commitAdmittedForCoordinator(
        expected: RegistryHead,
        operationId: UUID,
        writer: String,
        mutation: RegistryMutation,
    ): SkinResult<RegistryHead> = commitAdmitted(expected, operationId, writer, mutation)

    internal fun referenceSnapshotForCoordinator(): SkinResult<Set<String>> = locked {
        ensureLayout()?.let { return@locked it }
        val heads = mutableListOf<RegistryHead>()
        for (name in listOf("current", "previous", "next")) {
            when (val result = readPointer(name, historyDepth = HISTORY_WINDOW - 1)) {
                is SkinResult.Error -> return@locked result
                is SkinResult.Ok -> when (val pointer = result.value) {
                    PointerState.Malformed -> return@locked corrupt("Registry $name pointer is malformed")
                    PointerState.Missing -> if (name == "current") {
                        return@locked corrupt("Registry current pointer is absent")
                    }
                    is PointerState.Valid -> heads += pointer.head
                }
            }
        }
        val referenced = linkedSetOf<String>()
        heads.distinctBy { "${it.sequence}:${it.generationId}:${it.sha256}" }.forEach { head ->
            head.document.packs.forEach { pack ->
                referenced += pack.treeSha256
                referenced += pack.importReceiptSha256
            }
            addVisualReferences(referenced, head.document.activation.active)
            val interlock = head.document.activation.rotationInterlock
            addVisualReferences(referenced, interlock.prior?.active)
            addVisualReferences(referenced, interlock.target?.active)
        }
        SkinResult.Ok(referenced)
    }

    private fun commitAdmitted(
        expected: RegistryHead,
        operationId: UUID,
        writer: String,
        mutation: RegistryMutation,
    ): SkinResult<RegistryHead> = locked {
        ensureLayout()?.let { return@locked it }
        if (!mutation.hasRegistryAuthority()) {
            return@locked conflict("Registry mutation lacks activation authority")
        }
        validateExpected(expected)?.let { return@locked it }
        val current = when (val recovered = recoverLocked(layoutReady = true)) {
            is SkinResult.Error -> return@locked recovered
            is SkinResult.Ok -> recovered.value
        }
        val operation = operationId.toString()
        retryIdempotent(current, expected, operation, writer, mutation)?.let { return@locked it }
        if (!sameHead(current, expected)) {
            return@locked conflict("Registry head changed")
        }
        if (operation == SkinRegistryAuthority.GENESIS_ID) {
            return@locked conflict("Registry operation ID was already used")
        }
        when (val retained = retainedOperationUsed(operation, current)) {
            is SkinResult.Error -> return@locked retained
            is SkinResult.Ok -> if (retained.value) {
                return@locked conflict("Registry operation ID was already used")
            }
        }
        val mutated = when (val result = applyMutation(mutation, current.document)) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        if (mutated == current.document) return@locked SkinResult.Ok(current)
        if (!preservesStoreAuthority(current.document, mutated)) {
            return@locked conflict("Mutation changed store-owned registry authority")
        }
        val sequence = try {
            Math.addExact(current.sequence, 1L)
        } catch (_: ArithmeticException) {
            return@locked corrupt("Registry sequence overflow")
        }
        val child = mutated.copy(
            schemaVersion = SkinRegistryAuthority.SCHEMA_VERSION,
            generationId = operation,
            sequence = sequence,
            parentGenerationId = current.generationId,
            operationId = operation,
            writer = writer,
            profileId = SkinRegistryAuthority.PROFILE_ID,
            gameVersion = SkinRegistryAuthority.GAME_VERSION,
            catalogId = SkinRegistryAuthority.CATALOG_ID,
            catalogSha256 = SkinRegistryAuthority.CATALOG_SHA256,
        )
        val published = when (val result = publishGeneration(child)) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        return@locked when (val pointers = publishCommitPointers(current, published)) {
            is SkinResult.Error -> pointers
            is SkinResult.Ok -> SkinResult.Ok(published)
        }
    }

    private fun recoverLocked(layoutReady: Boolean = false, readOnly: Boolean = false): SkinResult<RegistryHead> {
        if (!layoutReady) ensureLayout()?.let { return it }
        val current = when (val result = readPointer("current")) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (current is PointerState.Valid) return SkinResult.Ok(current.head)
        val next = when (val result = readPointer("next")) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        val previous = when (val result = readPointer("previous")) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }

        if (next is PointerState.Valid) {
            if (next.head.sequence == 0L &&
                (current is PointerState.Missing || current is PointerState.Malformed) &&
                previous is PointerState.Missing
            ) {
                return recoverExistingGenesis(next.head, removeNext = true, readOnly = readOnly)
            }
            if (previous is PointerState.Valid && isImmediateChild(next.head, previous.head)) {
                return SkinResult.Ok(next.head)
            }
        }
        if (previous is PointerState.Valid) return SkinResult.Ok(previous.head)

        if (current is PointerState.Missing && next is PointerState.Missing && previous is PointerState.Missing) {
            return recoverOrPublishGenesis(readOnly)
        }
        return SkinResult.Error(
            SkinImportCode.REGISTRY_UNRECOVERABLE,
            "No valid bounded registry pointer can be recovered",
        )
    }

    private fun recoverOrPublishGenesis(readOnly: Boolean = false): SkinResult<RegistryHead> {
        val entries = try {
            if (readOnly && !fs.exists(generations)) emptyList() else {
                fs.requireContained(generations, root)
                fs.listBounded(generations, MAX_GENERATION_SCAN)
            }
        } catch (error: Exception) {
            return corrupt("Cannot inspect genesis evidence: ${error.message}")
        }
        if (entries.isEmpty()) {
            if (readOnly) {
                val document = SkinRegistryAuthority.genesis()
                val bytes = when (val result = SkinRegistryDocumentCodec.canonical(document)) {
                    is SkinResult.Error -> return result
                    is SkinResult.Ok -> result.value
                }
                return SkinResult.Ok(RegistryHead(document.generationId, document.sequence, digest(bytes), document))
            }
            val genesis = when (val result = publishGeneration(SkinRegistryAuthority.genesis())) {
                is SkinResult.Error -> return result
                is SkinResult.Ok -> result.value
            }
            val reference = RegistryPointer(genesis.sequence, genesis.generationId, genesis.sha256)
            writePointer("next", reference)?.let { return it }
            return establishGenesis(genesis, removeNext = true)
        }
        if (entries.size != 1 || entries.size > MAX_GENERATION_SCAN) {
            return genesisCorrupt("Genesis evidence is not unique")
        }
        val head = when (val result = loadUnreferencedGeneration(entries.single())) {
            is SkinResult.Error -> return genesisCorrupt(result.detail)
            is SkinResult.Ok -> result.value
        }
        return recoverExistingGenesis(head, removeNext = false, readOnly = readOnly)
    }

    private fun recoverExistingGenesis(head: RegistryHead, removeNext: Boolean, readOnly: Boolean = false): SkinResult<RegistryHead> {
        val entries = try {
            fs.listBounded(generations, MAX_GENERATION_SCAN)
        } catch (error: Exception) {
            return genesisCorrupt("Cannot bound genesis evidence: ${error.message}")
        }
        if (entries.size != 1 || head.document != SkinRegistryAuthority.genesis()) {
            return genesisCorrupt("Committed sequence-zero evidence is not the unique genesis")
        }
        return if (readOnly) SkinResult.Ok(head) else establishGenesis(head, removeNext)
    }

    private fun establishGenesis(head: RegistryHead, removeNext: Boolean): SkinResult<RegistryHead> {
        if (head.document != SkinRegistryAuthority.genesis()) return genesisCorrupt("Invalid deterministic genesis")
        val reference = RegistryPointer(head.sequence, head.generationId, head.sha256)
        writePointer("current", reference)?.let { return it }
        if (removeNext) removePointer("next")?.let { return it }
        return SkinResult.Ok(head)
    }

    private fun publishGeneration(value: SkinRegistryDocument): SkinResult<RegistryHead> {
        val bytes = when (val result = SkinRegistryDocumentCodec.canonical(value)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        val digest = digest(bytes)
        val digestBytes = "$digest\n".toByteArray(StandardCharsets.US_ASCII)
        val reference = RegistryPointer(value.sequence, value.generationId, digest)
        val stage = File(staging, "registry-${value.generationId}")
        val destination = File(generations, reference.directoryName)
        try {
            fs.requireContained(stage, root, allowMissingLeaf = true)
            if (fs.exists(stage)) fs.deleteContained(stage, root)
            fs.createDirectory(stage)
            fs.writeNew(File(stage, REGISTRY_JSON), bytes)
            fs.writeNew(File(stage, REGISTRY_SHA256), digestBytes)
        } catch (error: Exception) {
            return unavailable("Cannot stage registry generation: ${error.message}")
        }
        val result = publisher.publishDetailed(stage, destination, root) { published ->
            when (val loaded = loadGeneration(reference, published)) {
                is SkinResult.Error -> loaded
                is SkinResult.Ok -> if (loaded.value.document == value) SkinResult.Ok(Unit)
                else corrupt("Published generation bytes changed")
            }
        }
        return when (result) {
            is SkinResult.Error -> result
            is SkinResult.Ok -> loadGeneration(reference, result.value.root)
        }
    }

    private fun publishCommitPointers(old: RegistryHead, child: RegistryHead): SkinResult<Unit> {
        writePointer("next", RegistryPointer(child.sequence, child.generationId, child.sha256))?.let { return it }
        writePointer("previous", RegistryPointer(old.sequence, old.generationId, old.sha256))?.let { return it }
        writePointer("current", RegistryPointer(child.sequence, child.generationId, child.sha256))?.let { return it }
        removePointer("next")?.let { return it }
        return SkinResult.Ok(Unit)
    }

    private fun readPointer(
        name: String,
        historyDepth: Int = 0,
    ): SkinResult<PointerState> {
        require(name in POINTER_NAMES)
        val file = File(registry, name)
        if (!fs.exists(file)) return SkinResult.Ok(PointerState.Missing)
        val bytes = try {
            readStable(file, MAX_POINTER_BYTES)
        } catch (error: Exception) {
            return corrupt("Registry pointer identity or bytes changed: ${error.message}")
        }
        val reference = RegistryPointerCodec.parse(bytes) ?: return SkinResult.Ok(PointerState.Malformed)
        val directory = File(generations, reference.directoryName)
        if (!fs.exists(directory)) return SkinResult.Ok(PointerState.Malformed)
        return when (val loaded = loadGeneration(reference, directory, historyDepth)) {
            is SkinResult.Error -> loaded
            is SkinResult.Ok -> SkinResult.Ok(PointerState.Valid(loaded.value))
        }
    }

    private fun loadUnreferencedGeneration(
        directory: File,
        historyDepth: Int = 0,
    ): SkinResult<RegistryHead> {
        val parsedName = RegistryPointerCodec.parseDirectoryName(directory.name)
            ?: return corrupt("Unbounded generation directory name")
        val sidecar = try {
            readStable(File(directory, REGISTRY_SHA256), 65)
        } catch (error: Exception) {
            return corrupt("Cannot read generation digest: ${error.message}")
        }
        val digest = parseDigestSidecar(sidecar) ?: return corrupt("Invalid generation digest sidecar")
        return loadGeneration(
            RegistryPointer(parsedName.first, parsedName.second, digest),
            directory,
            historyDepth,
        )
    }

    private fun loadGeneration(
        reference: RegistryPointer,
        directory: File,
        historyDepth: Int = 0,
    ): SkinResult<RegistryHead> = try {
        require(directory.absoluteFile.normalize() == File(generations, reference.directoryName).absoluteFile.normalize()) {
            "Generation path does not match pointer"
        }
        fs.requireContained(directory, root)
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "Generation is not a no-alias directory" }
        val directoryIdentity = fs.identity(directory)
        val children = fs.list(directory)
        require(children.size == 3 && children.map(File::getName).toSet() == GENERATION_FILES) {
            "Generation file set is not exact"
        }
        val marker = File(directory, COMPLETE)
        require(fs.isRegularFile(marker) && fs.identity(marker).size == 0L) { "Generation completion marker is invalid" }
        val bytes = readStable(File(directory, REGISTRY_JSON), MAX_DOCUMENT_BYTES)
        val sidecar = readStable(File(directory, REGISTRY_SHA256), 65)
        val observedDigest = digest(bytes)
        require(parseDigestSidecar(sidecar) == observedDigest && observedDigest == reference.sha256) {
            "Generation digest evidence does not match"
        }
        val decoded = when (val result = SkinRegistryDocumentCodec.parse(bytes)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        require(decoded.generationId == reference.generationId && decoded.sequence == reference.sequence) {
            "Generation identity does not match its bounded name"
        }
        if (decoded.sequence > 0 && historyDepth < HISTORY_WINDOW - 1) {
            val parentId = requireNotNull(decoded.parentGenerationId)
            val parentDirectory = File(
                generations,
                RegistryPointer(decoded.sequence - 1, parentId, ZERO_DIGEST).directoryName,
            )
            require(fs.exists(parentDirectory)) { "Generation parent evidence is absent" }
            val parentSidecar = readStable(File(parentDirectory, REGISTRY_SHA256), 65)
            val parentDigest = parseDigestSidecar(parentSidecar)
                ?: throw IllegalStateException("Generation parent digest is invalid")
            val parent = when (
                val result = loadGeneration(
                    RegistryPointer(decoded.sequence - 1, parentId, parentDigest),
                    parentDirectory,
                    historyDepth + 1,
                )
            ) {
                is SkinResult.Error -> return result
                is SkinResult.Ok -> result.value
            }
            requireImmediateChildRelationship(
                RegistryHead(decoded.generationId, decoded.sequence, observedDigest, decoded),
                parent,
            )
        }
        require(fs.identity(directory) == directoryIdentity) { "Generation directory identity changed" }
        SkinResult.Ok(RegistryHead(decoded.generationId, decoded.sequence, observedDigest, decoded))
    } catch (error: Exception) {
        corrupt("Invalid registry generation: ${error.message}")
    }

    private fun writePointer(name: String, reference: RegistryPointer): SkinResult.Error? = try {
        require(name in POINTER_NAMES) { "Unbounded registry pointer name" }
        val file = File(registry, name)
        val bytes = RegistryPointerCodec.canonical(reference)
        fs.requireContained(file, root, allowMissingLeaf = true)
        if (fs.exists(file)) {
            require(fs.isRegularFile(file) && !fs.isSymbolicLink(file)) { "Pointer is not a no-alias regular file" }
            fs.openOutput(file, createNew = false).use { it.write(bytes) }
        } else {
            fs.writeNew(file, bytes)
        }
        fs.syncFile(file)
        fs.syncDirectory(registry)
        require(readStable(file, MAX_POINTER_BYTES).contentEquals(bytes)) { "Pointer changed after durability barrier" }
        null
    } catch (error: Exception) {
        unavailable("Cannot durably publish $name registry pointer: ${error.message}")
    }

    private fun removePointer(name: String): SkinResult.Error? = try {
        require(name in POINTER_NAMES)
        val file = File(registry, name)
        if (fs.exists(file)) {
            fs.requireContained(file, root)
            val identity = fs.identity(file)
            require(identity.regularFile && fs.isRegularFile(file) && !fs.isSymbolicLink(file)) {
                "Pointer removal target is unsafe"
            }
            require(fs.identity(file) == identity) { "Pointer identity changed before removal" }
            fs.deleteContained(file, root)
            fs.syncDirectory(registry)
        }
        null
    } catch (error: Exception) {
        unavailable("Cannot durably remove $name registry pointer: ${error.message}")
    }

    private fun ensureLayout(): SkinResult.Error? = try {
        require(fs.exists(root) && fs.isDirectory(root) && !fs.isSymbolicLink(root)) {
            "Skin root is not an existing no-alias directory"
        }
        fs.requireContained(root, root)
        ensureDirectory(staging, root)
        ensureDirectory(registry, root)
        ensureDirectory(generations, registry)
        null
    } catch (error: Exception) {
        unavailable("Cannot establish registry layout: ${error.message}")
    }

    private fun ensureDirectory(directory: File, parent: File) {
        fs.requireContained(parent, root)
        fs.requireContained(directory, root, allowMissingLeaf = true)
        if (!fs.exists(directory)) {
            fs.createDirectory(directory)
            fs.syncDirectory(directory)
            fs.syncDirectory(parent)
        }
        fs.requireContained(directory, root)
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "Unsafe registry directory" }
    }

    private fun readStable(file: File, maximum: Int): ByteArray {
        fs.requireContained(file, root)
        require(fs.isRegularFile(file) && !fs.isSymbolicLink(file)) { "Expected no-alias regular file" }
        val before = fs.identity(file)
        require(before.regularFile && before.size in 0..maximum.toLong()) { "Registry file exceeds bound" }
        val output = ByteArrayOutputStream(before.size.toInt())
        fs.openNoFollow(file).use { input ->
            val buffer = ByteArray(8192)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                if (count == 0) continue
                require(output.size() + count <= maximum) { "Registry file exceeds bound" }
                output.write(buffer, 0, count)
            }
        }
        require(fs.identity(file) == before) { "Registry file identity changed while read" }
        return output.toByteArray()
    }

    private fun retryIdempotent(
        current: RegistryHead,
        expected: RegistryHead,
        operation: String,
        writer: String,
        mutation: RegistryMutation,
    ): SkinResult<RegistryHead>? {
        val sequence = try {
            Math.addExact(expected.sequence, 1L)
        } catch (_: ArithmeticException) {
            return null
        }
        val candidateDirectory = File(
            generations,
            RegistryPointer(sequence, operation, ZERO_DIGEST).directoryName,
        )
        if (!fs.exists(candidateDirectory)) return null
        val child = when (val result = loadUnreferencedGeneration(candidateDirectory)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (child.generationId != operation || child.sequence != sequence || child.document.operationId != operation) {
            return conflict("Operation ID was reused by another registry generation")
        }
        if (!sameHead(current, expected) && !sameHead(current, child)) return null

        val actualParent = when (val result = loadImmediateParent(child)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (!sameHead(actualParent, expected)) {
            return conflict("Idempotent retry parent does not exactly match the expected registry head")
        }
        val mutated = when (val result = applyMutation(mutation, expected.document)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (!preservesStoreAuthority(expected.document, mutated)) return conflict("Mutation changed store authority")
        val candidate = mutated.copy(
            schemaVersion = SkinRegistryAuthority.SCHEMA_VERSION,
            generationId = operation,
            sequence = sequence,
            parentGenerationId = expected.generationId,
            operationId = operation,
            writer = writer,
            profileId = SkinRegistryAuthority.PROFILE_ID,
            gameVersion = SkinRegistryAuthority.GAME_VERSION,
            catalogId = SkinRegistryAuthority.CATALOG_ID,
            catalogSha256 = SkinRegistryAuthority.CATALOG_SHA256,
        )
        val bytes = when (val result = SkinRegistryDocumentCodec.canonical(candidate)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (digest(bytes) != child.sha256 || candidate != child.document) {
            return conflict("Operation ID was reused with different registry bytes")
        }
        when (val retained = retainedOperationUsed(operation, current, allowed = child)) {
            is SkinResult.Error -> return retained
            is SkinResult.Ok -> if (retained.value) {
                return conflict("Registry operation ID was already used before its retry generation")
            }
        }
        return when (val pointers = publishCommitPointers(actualParent, child)) {
            is SkinResult.Error -> pointers
            is SkinResult.Ok -> SkinResult.Ok(child)
        }
    }

    private fun retainedOperationUsed(
        operation: String,
        current: RegistryHead,
        allowed: RegistryHead? = null,
    ): SkinResult<Boolean> {
        val roots = mutableListOf(current)
        for (name in listOf("next", "previous")) {
            when (val result = readPointer(name, historyDepth = HISTORY_WINDOW - 1)) {
                is SkinResult.Error -> return result
                is SkinResult.Ok -> if (result.value is PointerState.Valid) roots += result.value.head
            }
        }
        val floorSequence = maxOf(0L, current.sequence - (HISTORY_WINDOW - 1L))
        val seen = mutableSetOf<String>()
        for (rootHead in roots) {
            var head = rootHead
            var depth = 0
            while (depth < HISTORY_WINDOW) {
                val identity = "${head.sequence}:${head.generationId}:${head.sha256}"
                if (!seen.add(identity)) break
                if (head.document.operationId == operation && (allowed == null || !sameHead(head, allowed))) {
                    return SkinResult.Ok(true)
                }
                if (head.sequence == 0L || head.sequence <= floorSequence) break
                head = when (val parent = loadImmediateParent(head)) {
                    is SkinResult.Error -> return parent
                    is SkinResult.Ok -> parent.value
                }
                depth++
            }
        }
        return SkinResult.Ok(false)
    }

    private fun loadImmediateParent(child: RegistryHead): SkinResult<RegistryHead> {
        if (child.sequence <= 0L) return corrupt("Registry generation has no immediate parent")
        val parentId = child.document.parentGenerationId
            ?: return corrupt("Registry generation omits its immediate parent")
        val parentDirectory = File(
            generations,
            RegistryPointer(child.sequence - 1L, parentId, ZERO_DIGEST).directoryName,
        )
        if (!fs.exists(parentDirectory)) return corrupt("Registry generation parent evidence is absent")
        val parent = when (
            val result = loadUnreferencedGeneration(
                parentDirectory,
                historyDepth = HISTORY_WINDOW - 1,
            )
        ) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        return try {
            requireImmediateChildRelationship(child, parent)
            SkinResult.Ok(parent)
        } catch (error: Exception) {
            corrupt("Invalid registry parent relationship: ${error.message}")
        }
    }

    private fun sameHead(left: RegistryHead, right: RegistryHead): Boolean {
        if (left.generationId != right.generationId || left.sequence != right.sequence || left.sha256 != right.sha256 ||
            left.document != right.document
        ) {
            return false
        }
        val leftBytes = when (val result = SkinRegistryDocumentCodec.canonical(left.document)) {
            is SkinResult.Error -> return false
            is SkinResult.Ok -> result.value
        }
        val rightBytes = when (val result = SkinRegistryDocumentCodec.canonical(right.document)) {
            is SkinResult.Error -> return false
            is SkinResult.Ok -> result.value
        }
        return leftBytes.contentEquals(rightBytes)
    }

    private fun applyMutation(
        mutation: RegistryMutation,
        current: SkinRegistryDocument,
    ): SkinResult<SkinRegistryDocument> = try {
        mutation.apply(current)
    } catch (error: Exception) {
        conflict("Registry mutation failed: ${error.message}")
    }

    private fun validateExpected(expected: RegistryHead): SkinResult.Error? {
        if (expected.generationId != expected.document.generationId || expected.sequence != expected.document.sequence) {
            return conflict("Expected registry identity is inconsistent")
        }
        val bytes = when (val result = SkinRegistryDocumentCodec.canonical(expected.document)) {
            is SkinResult.Error -> return conflict("Expected registry document is invalid")
            is SkinResult.Ok -> result.value
        }
        if (digest(bytes) != expected.sha256) return conflict("Expected registry digest is inconsistent")
        return null
    }

    private fun preservesStoreAuthority(before: SkinRegistryDocument, after: SkinRegistryDocument): Boolean =
        after.copy(packs = before.packs, activation = before.activation) == before

    private fun requireImmediateChildRelationship(child: RegistryHead, parent: RegistryHead) {
        require(isImmediateChild(child, parent)) { "Generation parent is not immediately adjacent" }
        val interlock = child.document.activation.rotationInterlock
        if (interlock.state == InterlockState.ARMED) {
            require(interlock.baseGenerationSha256 == parent.sha256) {
                "Armed interlock base digest does not match its parent"
            }
        } else if (interlock.state == InterlockState.ROLLBACK_FAILED) {
            val expectedArmed = interlock.copy(
                state = InterlockState.ARMED,
                originalFailure = null,
                rollbackFailure = null,
            )
            require(parent.document.activation.rotationInterlock == expectedArmed) {
                "Rollback failure is not the exact child of its armed interlock"
            }
        }
    }

    private fun isImmediateChild(child: RegistryHead, parent: RegistryHead): Boolean =
        child.sequence > 0L && parent.sequence == child.sequence - 1L &&
            child.document.parentGenerationId == parent.generationId

    private fun parseDigestSidecar(bytes: ByteArray): String? {
        if (bytes.size != 65 || bytes.last() != '\n'.code.toByte()) return null
        val value = decodeAscii(bytes.copyOf(bytes.size - 1)) ?: return null
        return value.takeIf { DIGEST.matches(it) }
    }

    private fun decodeAscii(bytes: ByteArray): String? = try {
        StandardCharsets.US_ASCII.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(java.nio.ByteBuffer.wrap(bytes))
            .toString()
    } catch (_: Exception) {
        null
    }

    private fun digest(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun addVisualReferences(referenced: MutableSet<String>, visual: ActiveVisual?) {
        if (visual is ActiveVisual.Pack) {
            referenced += visual.treeSha256
            referenced += visual.importReceiptSha256
        }
    }

    private fun <T> withQuota(
        request: SkinQuotaRequest,
        action: () -> SkinResult<T>,
    ): SkinResult<T> {
        val reservation = when (val result = quota.reserve(request)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        return try {
            action()
        } finally {
            reservation.release()
        }
    }

    private fun <T> locked(action: () -> SkinResult<T>): SkinResult<T> = try {
        lockManager.withSessionThenRegistry(action)
    } catch (error: Exception) {
        unavailable("Cannot acquire ordered skin registry locks: ${error.message}")
    }

    private fun conflict(detail: String) = SkinResult.Error(SkinImportCode.REGISTRY_CONFLICT, detail)
    private fun corrupt(detail: String) = SkinResult.Error(SkinImportCode.REGISTRY_CORRUPT, detail)
    private fun genesisCorrupt(detail: String) = SkinResult.Error(SkinImportCode.REGISTRY_GENESIS_CORRUPT, detail)
    private fun unavailable(detail: String) = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, detail)

    private sealed interface PointerState {
        data object Missing : PointerState
        data object Malformed : PointerState
        data class Valid(val head: RegistryHead) : PointerState
    }

    private companion object {
        const val REGISTRY_JSON = "registry.json"
        const val REGISTRY_SHA256 = "registry.sha256"
        const val COMPLETE = ".complete"
        const val MAX_POINTER_BYTES = 128
        const val MAX_DOCUMENT_BYTES = 8 * 1024 * 1024
        const val MAX_GENERATION_SCAN = 64
        const val HISTORY_WINDOW = 8
        val POINTER_NAMES = setOf("current", "previous", "next")
        val GENERATION_FILES = setOf(REGISTRY_JSON, REGISTRY_SHA256, COMPLETE)
        val DIGEST = Regex("[0-9a-f]{64}")
        val ZERO_DIGEST = "0".repeat(64)
    }
}

internal data class RegistryPointer(
    val sequence: Long,
    val generationId: String,
    val sha256: String,
) {
    val directoryName: String
        get() = "rg-${sequence.toString().padStart(20, '0')}-$generationId"
}

internal object RegistryPointerCodec {
    private val directoryPattern = Regex(
        "rg-([0-9]{20})-([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
    )
    private val digest = Regex("[0-9a-f]{64}")

    fun canonical(pointer: RegistryPointer): ByteArray {
        require(pointer.sequence >= 0 && pointer.sequence.toString().length <= 20)
        require(parseDirectoryName(pointer.directoryName) == (pointer.sequence to pointer.generationId))
        require(digest.matches(pointer.sha256))
        return "${pointer.directoryName}\n${pointer.sha256}\n".toByteArray(StandardCharsets.US_ASCII)
    }

    fun parse(bytes: ByteArray): RegistryPointer? {
        return try {
            if (bytes.size > 128 || bytes.any { (it.toInt() and 0xff) > 0x7f }) return null
            val lines = bytes.toString(StandardCharsets.US_ASCII).split('\n')
            if (lines.size != 3 || lines[2].isNotEmpty() || !digest.matches(lines[1])) return null
            val (sequence, id) = parseDirectoryName(lines[0]) ?: return null
            RegistryPointer(sequence, id, lines[1]).also {
                if (!canonical(it).contentEquals(bytes)) return null
            }
        } catch (_: Exception) {
            null
        }
    }

    fun parseDirectoryName(value: String): Pair<Long, String>? {
        return try {
            val match = directoryPattern.matchEntire(value) ?: return null
            val sequenceText = match.groupValues[1]
            val sequence = sequenceText.toLong()
            if (sequence.toString().padStart(20, '0') != sequenceText) return null
            val id = match.groupValues[2]
            if (UUID.fromString(id).toString() != id) return null
            sequence to id
        } catch (_: Exception) {
            null
        }
    }
}
