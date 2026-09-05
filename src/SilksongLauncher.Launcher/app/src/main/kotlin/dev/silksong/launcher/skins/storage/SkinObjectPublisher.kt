package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.contracts.BuiltSkin
import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.ByteArrayOutputStream
import java.io.File
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.util.Base64
import java.util.concurrent.atomic.AtomicLong

class SkinObjectPublisher(
    private val objectRepository: SkinObjectRepository,
    private val receiptRepository: SkinImportReceiptRepository,
    private val fs: SkinFileSystem,
) {
    private val paths = objectRepository.paths
    private val durable = DurableDirectoryPublisher(fs)

    init {
        require(paths.profileRoot == receiptRepository.paths.profileRoot) { "Skin repositories use different profile roots" }
    }

    constructor(
        paths: SkinPaths,
        fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
    ) : this(
        SkinObjectRepository(paths, fileSystem),
        SkinImportReceiptRepository(paths, fileSystem),
        fileSystem,
    )

    fun publish(built: BuiltSkin): SkinResult<PublishedSkin> {
        val stagingOwner = try {
            stagingOwner(built.ephemeralRoot)
        } catch (error: Exception) {
            return SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Build staging has no fixed owner: ${error.message}")
        }
        val receiptDestination = paths.importReceiptRoot(built.importReceiptSha256)
        val objectDestination = paths.objectRoot(built.treeSha256)
        val receiptStage = File(
            built.ephemeralRoot.parentFile,
            "receipt-${built.importReceiptSha256.take(12)}-${System.nanoTime()}-${NEXT.incrementAndGet()}",
        )
        val created = mutableListOf<File>()
        var ownership: PublicationOwnership? = null
        var outcome: SkinResult<PublishedSkin>? = null
        try {
            fs.requireContained(built.ephemeralRoot, paths.profileRoot)
            createDirectory(receiptStage, stagingOwner)
            writeNew(File(receiptStage, "import-receipt.json"), built.importReceiptBytes, stagingOwner)

            val receiptIdentity = preparedIdentity(receiptStage)
            val objectIdentity = preparedIdentity(built.ephemeralRoot)
            ownership = createOwnershipRecord(built, receiptIdentity, objectIdentity)

            val receiptPublication = publishTracked(
                receiptStage,
                receiptDestination,
                receiptIdentity,
                ownership.receipt,
            ) { root ->
                when (val verified = receiptRepository.verify(root, built.importReceiptSha256)) {
                    is SkinResult.Ok -> SkinResult.Ok(Unit)
                    is SkinResult.Error -> verified
                }
            }
            if (receiptPublication.newlyCreated) created += receiptDestination

            val objectPublication = publishTracked(
                built.ephemeralRoot,
                objectDestination,
                objectIdentity,
                ownership.objectRoot,
            ) { root ->
                when (val verified = objectRepository.verify(root, built.treeSha256)) {
                    is SkinResult.Ok -> if (verified.value.id == built.id) {
                        SkinResult.Ok(Unit)
                    } else {
                        SkinResult.Error(SkinImportCode.OBJECT_CORRUPT, "Published object ID mismatch")
                    }
                    is SkinResult.Error -> verified
                }
            }
            if (objectPublication.newlyCreated) created += objectDestination
            outcome = SkinResult.Ok(
                PublishedSkin(
                    id = built.id,
                    candidateKey = built.candidateKey,
                    name = built.name,
                    contentSha256 = built.contentSha256,
                    treeSha256 = built.treeSha256,
                    manifestSha256 = built.manifestSha256,
                    importReceiptSha256 = built.importReceiptSha256,
                    objectRoot = objectDestination,
                    newlyCreatedRoots = created.toList(),
                ),
            )
        } catch (failure: PublicationFailure) {
            outcome = cleanupAfterFailure(ownership, failure.result)
        } catch (error: Exception) {
            outcome = cleanupAfterFailure(
                ownership,
                SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Skin publication failed: ${error.message}"),
            )
        } finally {
            var stagingCleanupError: Exception? = null
            for (target in listOf(receiptStage, built.ephemeralRoot)) {
                try {
                    cleanupEphemeral(target, stagingOwner)
                } catch (error: Exception) {
                    if (stagingCleanupError == null) stagingCleanupError = error
                }
            }
            if (stagingCleanupError != null) {
                val plan = ownership
                var immutableCleanupError: Exception? = null
                if (outcome is SkinResult.Ok && plan != null) {
                    try {
                        cleanupPlannedRoots(plan, emptySet())
                    } catch (error: Exception) {
                        immutableCleanupError = error
                    }
                }
                outcome = SkinResult.Error(
                    SkinImportCode.DURABILITY_UNAVAILABLE,
                    "Owned publication staging cleanup failed: ${stagingCleanupError.message}" +
                        (immutableCleanupError?.let { "; immutable cleanup failed: ${it.message}" } ?: ""),
                )
            }
        }
        return requireNotNull(outcome)
    }

    fun discardUnreferenced(
        published: PublishedSkin,
        referencedDigests: Set<String>,
    ): SkinResult<Unit> = try {
        val roots = published.newlyCreatedRoots
        if (roots.map { it.absoluteFile.normalize() }.toSet().size != roots.size) {
            throw IllegalStateException("Newly-created publication roots contain aliases")
        }
        require(referencedDigests.all(DIGEST::matches)) { "Referenced publication digest is invalid" }
        val retainedDigests = retainedDigests(readOwnershipRecords(), referencedDigests)
        val deletions = mutableListOf<Pair<File, SkinNodeIdentity>>()
        for (root in roots) {
            val digest = root.name
            if (!DIGEST.matches(digest)) throw IllegalStateException("Published cleanup root has no digest identity")
            val kind = when (root.absoluteFile.normalize()) {
                paths.objectRoot(published.treeSha256).absoluteFile.normalize() -> RootKind.OBJECT
                paths.importReceiptRoot(published.importReceiptSha256).absoluteFile.normalize() -> RootKind.RECEIPT
                else -> throw IllegalStateException("Published cleanup root is not owned by this publication")
            }
            val expectedDigest = if (kind == RootKind.OBJECT) published.treeSha256 else published.importReceiptSha256
            if (digest != expectedDigest) throw IllegalStateException("Published cleanup digest mismatch")
            if (digest in retainedDigests || !fs.exists(root)) continue
            verifyOwnedRoot(root, kind, digest, published.id, published.candidateKey)
            val before = fs.identity(root)
            fs.requireContained(root, paths.profileRoot)
            if (fs.identity(root) != before) throw IllegalStateException("Published cleanup root identity changed")
            deletions += root to before
        }
        // Validate the complete operation-local plan before deleting either immutable root.
        for ((root, before) in deletions) {
            fs.requireContained(root, paths.profileRoot)
            require(fs.identity(root) == before) { "Published cleanup root identity changed" }
        }
        for ((root, _) in deletions) {
            fs.deleteContained(root, paths.profileRoot)
            root.parentFile?.let(fs::syncDirectory)
        }
        // Ownership records are settled only by explicit process recovery, never a failed CAS.
        SkinResult.Ok(Unit)
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Unreferenced publication cleanup failed: ${error.message}")
    }

    fun recoverOwnedPublications(referencedDigests: Set<String>): SkinResult<Unit> {
        return try {
            if (referencedDigests.any { !DIGEST.matches(it) }) {
                throw IllegalArgumentException("Referenced publication digest is invalid")
            }
            val pending = mutableListOf<File>()
            val records = readOwnershipRecords(pending)
            val retainedDigests = retainedDigests(records, referencedDigests)
            for (record in records) {
                for (planned in listOf(record.objectRoot, record.receipt)) {
                    val destination = destination(planned)
                    if (planned.digest !in retainedDigests && fs.exists(destination) &&
                        fs.identity(destination) == planned.identity
                    ) {
                        verifyOwnedRoot(destination, planned.kind, planned.digest, record.id, record.candidateKey)
                    }
                }
            }
            for (staging in pending) fs.deleteContained(staging, paths.profileRoot)
            if (pending.isNotEmpty()) fs.syncDirectory(paths.publicationCleanup)
            for (record in records) {
                cleanupPlannedRoots(record, retainedDigests)
                fs.requireContained(record.recordRoot, paths.profileRoot)
                fs.deleteContained(record.recordRoot, paths.profileRoot)
                fs.syncDirectory(paths.publicationCleanup)
            }
            if (fs.exists(paths.publicationCleanup)) {
                fs.requireContained(paths.publicationCleanup, paths.profileRoot)
                fs.syncDirectory(paths.publicationCleanup)
            }
            SkinResult.Ok(Unit)
        } catch (error: Exception) {
            SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Owned publication recovery failed: ${error.message}")
        }
    }

    private fun preparedIdentity(staging: File): SkinNodeIdentity = when (
        val prepared = durable.prepare(staging, paths.profileRoot)
    ) {
        is SkinResult.Ok -> prepared.value
        is SkinResult.Error -> throw PublicationFailure(prepared)
    }

    private fun publishTracked(
        staging: File,
        destination: File,
        preparedIdentity: SkinNodeIdentity,
        planned: PlannedRoot,
        verifier: (File) -> SkinResult<Unit>,
    ): DirectoryPublication = when (
        val result = durable.publishTracked(staging, destination, paths.profileRoot, verifier, preparedIdentity)
    ) {
        is DirectoryPublicationResult.Success -> result.publication
        is DirectoryPublicationResult.Failure -> {
            result.movedRoot?.let { moved ->
                if (moved.root != destination.absoluteFile.normalize() || moved.identity != planned.identity) {
                    throw IllegalStateException("Durable publisher reported an unexpected moved-root identity")
                }
            }
            throw PublicationFailure(result.error)
        }
    }

    private fun createOwnershipRecord(
        built: BuiltSkin,
        receiptIdentity: SkinNodeIdentity,
        objectIdentity: SkinNodeIdentity,
    ): PublicationOwnership {
        require(DIGEST.matches(built.candidateKey)) { "Candidate key is invalid" }
        require(DIGEST.matches(built.importReceiptSha256) && DIGEST.matches(built.treeSha256)) {
            "Publication digest is invalid"
        }
        require(!receiptIdentity.regularFile && !objectIdentity.regularFile) { "Publication staging root is not a directory" }
        ensureCleanupRoot()
        if (fs.listBounded(paths.publicationCleanup, MAX_OWNERSHIP_RECORDS).size >= MAX_OWNERSHIP_RECORDS) {
            throw IllegalStateException("Publication cleanup record bound exceeded")
        }
        val nonce = "${System.nanoTime()}-${NEXT.incrementAndGet()}-${built.importReceiptSha256.take(12)}-${built.treeSha256.take(12)}"
        val recordRoot = File(
            paths.publicationCleanup,
            "publication-$nonce",
        ).absoluteFile.normalize()
        val stagingRoot = File(
            paths.publicationCleanup,
            "pending-$nonce",
        ).absoluteFile.normalize()
        val ownership = PublicationOwnership(
            recordRoot,
            built.id,
            built.candidateKey,
            PlannedRoot(RootKind.RECEIPT, built.importReceiptSha256, receiptIdentity),
            PlannedRoot(RootKind.OBJECT, built.treeSha256, objectIdentity),
        )
        var staged = false
        var moved = false
        try {
            fs.requireContained(stagingRoot, paths.profileRoot, allowMissingLeaf = true)
            fs.requireContained(recordRoot, paths.profileRoot, allowMissingLeaf = true)
            fs.createDirectory(stagingRoot)
            staged = true
            fs.requireContained(stagingRoot, paths.profileRoot)
            val plan = File(stagingRoot, PLAN_FILE)
            fs.requireContained(plan, paths.profileRoot, allowMissingLeaf = true)
            fs.writeNew(plan, encodeOwnership(ownership))
            fs.requireContained(plan, paths.profileRoot)
            fs.syncFile(plan)
            fs.syncDirectory(stagingRoot)
            fs.atomicMove(stagingRoot, recordRoot)
            moved = true
            syncBarrier(paths.publicationCleanup)
            return ownership
        } catch (error: Exception) {
            val cleanup = if (moved) recordRoot else stagingRoot
            if (staged && fs.exists(cleanup)) {
                fs.deleteContained(cleanup, paths.profileRoot)
                fs.syncDirectory(paths.publicationCleanup)
            }
            throw error
        }
    }

    private fun ensureCleanupRoot() {
        fs.requireContained(paths.staging, paths.profileRoot)
        fs.requireContained(paths.publicationCleanup, paths.profileRoot, allowMissingLeaf = true)
        if (!fs.exists(paths.publicationCleanup)) {
            fs.createDirectory(paths.publicationCleanup)
            fs.requireContained(paths.publicationCleanup, paths.profileRoot)
            fs.syncDirectory(paths.publicationCleanup)
            syncBarrier(paths.staging)
        } else {
            val identity = fs.identity(paths.publicationCleanup)
            if (identity.regularFile) throw IllegalStateException("Publication cleanup owner is not a directory")
        }
    }

    private fun syncBarrier(start: File) {
        var cursor: File? = start
        while (cursor != null && cursor.toPath().startsWith(paths.profileRoot.toPath())) {
            fs.requireContained(cursor, paths.profileRoot)
            fs.syncDirectory(cursor)
            if (cursor == paths.profileRoot) return
            cursor = cursor.parentFile
        }
        throw IllegalStateException("Publication cleanup barrier does not reach profile root")
    }

    private fun cleanupAfterFailure(
        ownership: PublicationOwnership?,
        failure: SkinResult.Error,
    ): SkinResult.Error {
        if (ownership == null) return failure
        return try {
            cleanupPlannedRoots(ownership, emptySet())
            failure
        } catch (error: Exception) {
            SkinResult.Error(
                SkinImportCode.DURABILITY_UNAVAILABLE,
                "Owned immutable publication cleanup failed: ${error.message}; original failure: ${failure.detail}",
            )
        }
    }

    private fun cleanupPlannedRoots(ownership: PublicationOwnership, referencedDigests: Set<String>) {
        for (planned in listOf(ownership.objectRoot, ownership.receipt)) {
            if (planned.digest in referencedDigests) continue
            val destination = destination(planned)
            val parent = requireNotNull(destination.parentFile) { "Owned publication root has no parent" }
            if (!fs.exists(destination)) {
                // A previous attempt may have deleted the directory and then
                // failed its naming-parent barrier. Sync the nearest existing
                // naming ancestor, which also handles a crash before shard
                // directories were created.
                syncAbsentDestination(destination)
                continue
            }
            fs.requireContained(destination, paths.profileRoot)
            if (fs.identity(destination) != planned.identity) continue
            verifyOwnedRoot(destination, planned.kind, planned.digest, ownership.id, ownership.candidateKey)
            fs.requireContained(destination, paths.profileRoot)
            if (fs.identity(destination) != planned.identity) {
                throw IllegalStateException("Owned immutable publication identity changed")
            }
            fs.deleteContained(destination, paths.profileRoot)
            fs.syncDirectory(parent)
        }
    }

    private fun syncAbsentDestination(destination: File) {
        val profile = paths.profileRoot.absoluteFile.normalize()
        val target = destination.absoluteFile.normalize()
        if (!target.toPath().startsWith(profile.toPath()) || target == profile) {
            throw IllegalStateException("Absent publication destination escapes profile")
        }
        var ancestor = requireNotNull(target.parentFile) { "Absent publication destination has no parent" }
        while (!fs.exists(ancestor)) {
            ancestor = ancestor.parentFile
                ?: throw IllegalStateException("Absent publication destination has no existing owner")
            if (!ancestor.toPath().startsWith(profile.toPath())) {
                throw IllegalStateException("Absent publication destination escapes profile")
            }
        }
        fs.requireContained(ancestor, profile)
        fs.syncDirectory(ancestor)
    }

    private fun verifyOwnedRoot(
        root: File,
        kind: RootKind,
        digest: String,
        id: String,
        candidateKey: String,
    ) {
        when (kind) {
            RootKind.OBJECT -> when (val verified = objectRepository.verify(root, digest)) {
                is SkinResult.Error -> throw IllegalStateException(verified.detail)
                is SkinResult.Ok -> if (verified.value.id != id) throw IllegalStateException("Object owner changed")
            }
            RootKind.RECEIPT -> when (val verified = receiptRepository.verify(root, digest)) {
                is SkinResult.Error -> throw IllegalStateException(verified.detail)
                is SkinResult.Ok -> if (verified.value.candidateKey != candidateKey) {
                    throw IllegalStateException("Receipt owner changed")
                }
            }
        }
    }

    private fun destination(planned: PlannedRoot): File = when (planned.kind) {
        RootKind.OBJECT -> paths.objectRoot(planned.digest)
        RootKind.RECEIPT -> paths.importReceiptRoot(planned.digest)
    }.absoluteFile.normalize()

    private fun readOwnershipRecords(pendingRoots: MutableList<File>? = null): List<PublicationOwnership> {
        if (!fs.exists(paths.publicationCleanup)) return emptyList()
        fs.requireContained(paths.publicationCleanup, paths.profileRoot)
        val roots = fs.listBounded(paths.publicationCleanup, MAX_OWNERSHIP_RECORDS).sortedBy { it.name }
        if (roots.size > MAX_OWNERSHIP_RECORDS) {
            throw IllegalStateException("Publication cleanup record bound exceeded")
        }
        val pending = roots.filter { it.name.startsWith(PENDING_PREFIX) }
        val records = roots.filter { it.name.startsWith(RECORD_PREFIX) }
        if (pending.size + records.size != roots.size) {
            throw IllegalStateException("Publication cleanup record name is invalid")
        }
        for (staging in pending) {
            requireImmediateCleanupChild(staging)
            val identity = fs.identity(staging)
            if (identity.regularFile) throw IllegalStateException("Pending publication record is not a directory")
            val children = fs.listBounded(staging, 1)
            for (child in children) {
                fs.requireContained(child, paths.profileRoot)
                val childIdentity = fs.identity(child)
                require(child.name == PLAN_FILE && childIdentity.regularFile && childIdentity.size <= MAX_PLAN_BYTES) {
                    "Pending publication record shape is invalid"
                }
            }
            pendingRoots?.add(staging)
        }
        return records.map { record ->
            requireImmediateCleanupChild(record)
            readOwnershipRecord(record)
        }
    }

    private fun requireImmediateCleanupChild(path: File) {
        val normalized = path.absoluteFile.normalize()
        if (normalized.parentFile != paths.publicationCleanup.absoluteFile.normalize() ||
            !SAFE_RECORD_NAME.matches(normalized.name)
        ) {
            throw IllegalStateException("Publication cleanup child is invalid")
        }
        fs.requireContained(normalized, paths.profileRoot)
    }

    private fun retainedDigests(
        records: List<PublicationOwnership>,
        referencedDigests: Set<String>,
    ): Set<String> = buildSet {
        addAll(referencedDigests)
        for (record in records) {
            for (planned in listOf(record.receipt, record.objectRoot)) {
                val destination = destination(planned)
                if (fs.exists(destination)) {
                    fs.requireContained(destination, paths.profileRoot)
                    if (fs.identity(destination) != planned.identity) add(planned.digest)
                }
            }
        }
    }

    private fun readOwnershipRecord(recordRoot: File): PublicationOwnership {
        fs.requireContained(recordRoot, paths.profileRoot)
        val rootIdentity = fs.identity(recordRoot)
        if (rootIdentity.regularFile) throw IllegalStateException("Publication cleanup record is not a directory")
        val children = fs.listBounded(recordRoot, 1)
        if (children.size != 1 || children.single().name != PLAN_FILE) {
            throw IllegalStateException("Publication cleanup record shape is invalid")
        }
        val plan = children.single()
        fs.requireContained(plan, paths.profileRoot)
        val identity = fs.identity(plan)
        if (!identity.regularFile || identity.size > MAX_PLAN_BYTES) {
            throw IllegalStateException("Publication cleanup plan is invalid")
        }
        val bytes = readBounded(plan, identity, MAX_PLAN_BYTES)
        if (fs.identity(recordRoot) != rootIdentity) throw IllegalStateException("Publication cleanup record identity changed")
        return decodeOwnership(recordRoot.absoluteFile.normalize(), bytes)
    }

    private fun readBounded(file: File, identity: SkinNodeIdentity, maximum: Long): ByteArray {
        val output = ByteArrayOutputStream(minOf(identity.size, maximum).toInt())
        fs.openNoFollow(file).use { input ->
            val buffer = ByteArray(4096)
            var count = 0L
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                count += read
                if (count > maximum || count > identity.size) throw IllegalStateException("Publication cleanup plan exceeds bound")
                output.write(buffer, 0, read)
            }
            if (count != identity.size) throw IllegalStateException("Publication cleanup plan size changed")
        }
        if (fs.identity(file) != identity) throw IllegalStateException("Publication cleanup plan identity changed")
        return output.toByteArray()
    }

    private fun encodeOwnership(ownership: PublicationOwnership): ByteArray {
        val text = listOf(
            PLAN_VERSION,
            "id\t${encodeText(ownership.id)}",
            "candidate\t${ownership.candidateKey}",
            encodeRoot("receipt", ownership.receipt),
            encodeRoot("object", ownership.objectRoot),
        ).joinToString("\n", postfix = "\n")
        val bytes = text.toByteArray(Charsets.UTF_8)
        require(bytes.size <= MAX_PLAN_BYTES) { "Publication cleanup plan exceeds bound" }
        return bytes
    }

    private fun encodeRoot(label: String, root: PlannedRoot): String =
        "$label\t${root.digest}\t${encodeText(root.identity.fileKey)}\t${root.identity.size}\t${root.identity.regularFile}"

    private fun decodeOwnership(recordRoot: File, bytes: ByteArray): PublicationOwnership {
        val text = StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
        val lines = text.split('\n')
        if (lines.size != 6 || lines.last().isNotEmpty() || lines[0] != PLAN_VERSION) {
            throw IllegalStateException("Publication cleanup plan format is invalid")
        }
        val id = parseScalar(lines[1], "id", decode = true)
        val candidateKey = parseScalar(lines[2], "candidate", decode = false)
        if (!DIGEST.matches(candidateKey)) throw IllegalStateException("Publication candidate key is invalid")
        val receipt = parseRoot(lines[3], "receipt", RootKind.RECEIPT)
        val objectRoot = parseRoot(lines[4], "object", RootKind.OBJECT)
        return PublicationOwnership(recordRoot, id, candidateKey, receipt, objectRoot)
    }

    private fun parseScalar(line: String, label: String, decode: Boolean): String {
        val fields = line.split('\t')
        if (fields.size != 2 || fields[0] != label) throw IllegalStateException("Publication cleanup scalar is invalid")
        return if (decode) decodeText(fields[1]) else fields[1]
    }

    private fun parseRoot(line: String, label: String, kind: RootKind): PlannedRoot {
        val fields = line.split('\t')
        if (fields.size != 5 || fields[0] != label || !DIGEST.matches(fields[1])) {
            throw IllegalStateException("Publication cleanup root is invalid")
        }
        val fileKey = decodeText(fields[2])
        if (fileKey.isEmpty() || fileKey.toByteArray(Charsets.UTF_8).size > MAX_IDENTITY_BYTES) {
            throw IllegalStateException("Publication cleanup identity is invalid")
        }
        val size = fields[3].toLongOrNull()?.takeIf { it >= 0 }
            ?: throw IllegalStateException("Publication cleanup identity size is invalid")
        val regular = when (fields[4]) {
            "false" -> false
            "true" -> true
            else -> throw IllegalStateException("Publication cleanup identity type is invalid")
        }
        if (regular) throw IllegalStateException("Publication cleanup root identity is not a directory")
        return PlannedRoot(kind, fields[1], SkinNodeIdentity(fileKey, size, regular))
    }

    private fun encodeText(value: String): String {
        val bytes = value.toByteArray(Charsets.UTF_8)
        require(bytes.size <= MAX_IDENTITY_BYTES) { "Publication cleanup text exceeds bound" }
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    }

    private fun decodeText(value: String): String {
        val bytes = try {
            Base64.getUrlDecoder().decode(value)
        } catch (error: IllegalArgumentException) {
            throw IllegalStateException("Publication cleanup text encoding is invalid", error)
        }
        if (bytes.size > MAX_IDENTITY_BYTES) throw IllegalStateException("Publication cleanup text exceeds bound")
        return StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes))
            .toString()
    }

    private fun cleanupEphemeral(target: File, owner: File) {
        if (fs.exists(target)) fs.deleteContained(target, owner)
    }

    private fun createDirectory(directory: File, owner: File) {
        fs.requireContained(directory, owner, allowMissingLeaf = true)
        fs.createDirectory(directory)
        fs.requireContained(directory, owner)
    }

    private fun writeNew(file: File, bytes: ByteArray, owner: File) {
        fs.requireContained(file, owner, allowMissingLeaf = true)
        fs.writeNew(file, bytes)
        fs.requireContained(file, owner)
    }

    private fun stagingOwner(path: File): File {
        var cursor: File? = path.absoluteFile.normalize()
        while (cursor != null && cursor.name != "staging") cursor = cursor.parentFile
        val owner = requireNotNull(cursor) { "No SkinPaths.staging ancestor" }
        require(owner == paths.staging.absoluteFile.normalize()) { "Build staging uses another profile" }
        return owner
    }

    private data class PlannedRoot(
        val kind: RootKind,
        val digest: String,
        val identity: SkinNodeIdentity,
    )

    private data class PublicationOwnership(
        val recordRoot: File,
        val id: String,
        val candidateKey: String,
        val receipt: PlannedRoot,
        val objectRoot: PlannedRoot,
    )

    private class PublicationFailure(val result: SkinResult.Error) : RuntimeException(result.detail)
    private enum class RootKind { OBJECT, RECEIPT }

    private companion object {
        const val PLAN_FILE = "plan"
        const val PLAN_VERSION = "skin-publication-cleanup-v1"
        const val MAX_PLAN_BYTES = 16 * 1024L
        const val MAX_IDENTITY_BYTES = 4096
        const val MAX_OWNERSHIP_RECORDS = 128
        const val RECORD_PREFIX = "publication-"
        const val PENDING_PREFIX = "pending-"
        val SAFE_RECORD_NAME = Regex("[A-Za-z0-9-]{1,160}")
        val DIGEST = Regex("[0-9a-f]{64}")
        val NEXT = AtomicLong()
    }
}
