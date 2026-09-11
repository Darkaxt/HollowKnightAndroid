package dev.silksong.launcher.skins.library

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.documents.SkinManifestDocument
import dev.silksong.launcher.skins.quota.SkinQuota
import dev.silksong.launcher.skins.registry.SkinRegistryStore
import dev.silksong.launcher.skins.storage.*
import java.io.File
import java.nio.channels.FileChannel
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.nio.file.StandardOpenOption.CREATE
import java.nio.file.StandardOpenOption.WRITE
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.locks.ReentrantLock

/** One Kotlin writer, shared by launcher controls and JNI. No managed file writer or legacy dual-read. */
class SkinLibraryStore(val paths: SkinPaths, internal val fs: SkinFileSystem = AndroidSkinFileSystem(),
    internal val catalog: CatalogPathSet = CatalogPathSet.requirePinned()) {
    val profileId: String = catalog.profile.profileId
    private val processLock = locks.computeIfAbsent(paths.root.absolutePath) { ReentrantLock(true) }
    private val authority = File(paths.root, "library.json")
    private val backup = File(paths.root, "library.backup.json")
    internal val quota by lazy { SkinQuota(paths.root, fs) }
    internal val objects by lazy { SkinObjectRepository(paths, fs, catalog) }
    internal val receipts by lazy { SkinImportReceiptRepository(paths, fs, catalog) }
    init { require(paths.profileRoot.name == profileId && paths.profileRoot.parentFile?.name == "profiles") { "Wrong skin profile owner" } }

    internal fun startRuntime(): SkinResult<SkinLibraryDocument> = changeRotation { renewRotation(it) }
    private fun renewRotation(value: SkinLibraryDocument) = value.copy(
        rotationRun = if (value.mode == LibraryMode.ROTATE) UUID.randomUUID().toString().replace("-", "") else null,
        lastDeath = 0, pendingPackId = null, queuedDeathOccurrences = emptyList())
    private fun changeRotation(action: (SkinLibraryDocument) -> SkinLibraryDocument): SkinResult<SkinLibraryDocument> = locked(nonblocking = true) {
        val old = readLocked(); val next = action(old); SkinLibraryCodec.validate(next)
        if (old != next) commit(next)
        SkinResult.Ok(next)
    }
    internal fun confirmDeath(run: String, occurrence: Long): SkinResult<SkinLibraryDocument> = changeRotation { value ->
        require(value.mode == LibraryMode.ROTATE && value.rotationRun == run && occurrence > 0) { "Death belongs to a retired rotation run" }
        if (occurrence <= value.lastDeath || occurrence in value.queuedDeathOccurrences) value
        else if (value.pendingPackId != null) {
            require(value.queuedDeathOccurrences.size < SkinLibraryCodec.MAX_QUEUED_DEATHS) { "Death rotation backlog is full" }
            require(occurrence > (value.queuedDeathOccurrences.lastOrNull() ?: value.lastDeath)) { "Death occurrence order changed" }
            value.copy(queuedDeathOccurrences = value.queuedDeathOccurrences + occurrence)
        } else activateOccurrence(value, occurrence)
    }
    private fun activateOccurrence(value: SkinLibraryDocument, occurrence: Long): SkinLibraryDocument {
        val ring = value.eligiblePackIds
        val index = ring.indexOf(value.selectedPackId)
        val next = if (ring.isEmpty()) null else ring[if (index < 0) 0 else (index + 1) % ring.size]
        return value.copy(lastDeath = occurrence, pendingPackId = next.takeUnless { it == value.selectedPackId })
    }
    internal fun cancelRotation(run: String): SkinResult<SkinLibraryDocument> = changeRotation { value ->
        if (value.rotationRun == run) renewRotation(value) else value
    }
    internal fun finishRotation(config: String, run: String, occurrence: Long, id: String, tree: String): Boolean =
        changeRotation { value ->
            require(configurationIdentity(value) == config && value.rotationRun == run && value.lastDeath == occurrence &&
                value.pendingPackId == id && value.packs.single { it.id == id }.treeSha256 == tree) { "Applied successor belongs to retired configuration" }
            val completed = value.copy(selectedPackId = id, pendingPackId = null)
            val nextOccurrence = completed.queuedDeathOccurrences.firstOrNull()
            if (nextOccurrence == null) completed
            else activateOccurrence(completed.copy(queuedDeathOccurrences = completed.queuedDeathOccurrences.drop(1)), nextOccurrence)
        } is SkinResult.Ok

    fun read(nonblocking: Boolean = false): SkinResult<SkinLibraryDocument> = locked(nonblocking) { SkinResult.Ok(readLocked()) }
    fun select(id: String): SkinResult<Unit> = mutate { value ->
        val pack = value.packs.singleOrNull { it.id == id } ?: error("Selected pack was removed; refresh and retry")
        requireVerified(pack)
        renewRotation(value.copy(selectedPackId = id))
    }
    fun setEligibility(id: String, eligible: Boolean): SkinResult<Unit> = mutate { value ->
        require(value.packs.any { it.id == id }) { "Pack was removed; refresh and retry" }
        value.copy(eligiblePackIds = if (eligible) (value.eligiblePackIds + id).distinct() else value.eligiblePackIds - id)
    }
    fun advanceMode(): SkinResult<Unit> = mutate { value ->
        val mode = when (value.mode) { LibraryMode.OFF -> LibraryMode.ON; LibraryMode.ON -> LibraryMode.ROTATE; LibraryMode.ROTATE -> LibraryMode.OFF }
        if (mode != LibraryMode.OFF) {
            val pack = value.packs.singleOrNull { it.id == value.selectedPackId } ?: error("Select a pack before turning skins ON")
            requireVerified(pack)
        }
        renewRotation(value.copy(mode = mode))
    }
    fun remove(id: String): SkinResult<Unit> = mutate { value ->
        checkEditable(value, id)
        require(value.packs.any { it.id == id }) { "Pack was removed; refresh and retry" }
        value.copy(packs = value.packs.filterNot { it.id == id }, selectedPackId = value.selectedPackId.takeUnless { it == id }, eligiblePackIds = value.eligiblePackIds - id)
    }
    internal fun install(pack: LibraryPack, replacing: LibraryPack? = null): SkinResult<Unit> = mutate { value ->
        requireVerified(pack)
        if (replacing == null) {
            val existing = value.packs.singleOrNull { it.candidateKey == pack.candidateKey }
            if (existing != null) { require(existing == pack) { "Candidate already imported with another identity" }; value }
            else { require(value.packs.none { it.id == pack.id }) { "Pack ID already exists" }; value.copy(packs = value.packs + pack) }
        } else {
            checkEditable(value, replacing.id)
            require(value.packs.singleOrNull { it.id == replacing.id } == replacing) { "Replacement target changed; refresh and confirm again" }
            require(pack.id == replacing.id) { "Replacement ID differs" }
            value.copy(packs = value.packs.map { if (it.id == replacing.id) pack else it })
        }
    }
    internal fun checkEditable(value: SkinLibraryDocument, id: String) {
        check(value.mode == LibraryMode.OFF || (value.selectedPackId != id && value.pendingPackId != id)) { "Selected or pending skin is in use: turn skins OFF, then retry replacement/removal" }
    }
    private fun mutate(action: (SkinLibraryDocument) -> SkinLibraryDocument): SkinResult<Unit> = locked {
        val old = readLocked(); val next = action(old); SkinLibraryCodec.validate(next)
        if (next != old) commit(next)
        SkinResult.Ok(Unit)
    }

    /** Explicit recovery, also the always-reachable OFF path. Never requires import capacity or reads legacy state. */
    fun recoverOff(): SkinResult<Unit> = locked {
        if (fs.exists(authority)) {
            fs.requireContained(authority, paths.profileRoot)
            require(fs.isRegularFile(authority)) { "Unsafe library recovery target" }
            if (fs.identity(authority).size !in 1..SkinLibraryCodec.MAX_BYTES.toLong()) {
                // Preserve the one corrupt file by rename, without allocating another oversized copy.
                val invalid = File(paths.root, "library.invalid.json")
                fs.requireContained(invalid, paths.profileRoot, allowMissingLeaf = true)
                if (fs.exists(invalid)) require(fs.isRegularFile(invalid))
                fs.atomicMove(authority, invalid); fs.syncDirectory(paths.root)
            }
        }
        val current = if (fs.exists(authority)) boundedRead(authority, SkinLibraryCodec.MAX_BYTES) else null
        val good = current?.let { runCatching { SkinLibraryCodec.decode(it, profileId) }.getOrNull() }
        val saved = if (good == null && fs.exists(backup)) runCatching { SkinLibraryCodec.decode(boundedRead(backup, SkinLibraryCodec.MAX_BYTES), profileId) }.getOrNull() else null
        if (current != null && good == null) writeAtomic(File(paths.root, "library.invalid.json"), current)
        val next = renewRotation((good ?: saved ?: SkinLibraryDocument()).copy(mode = LibraryMode.OFF))
        commit(next, preserveBackup = good != null)
        SkinResult.Ok(Unit)
    }
    internal fun requireVerified(pack: LibraryPack): SkinManifestDocument {
        val manifest = objects.verify(pack.treeSha256).required()
        val receipt = receipts.verify(pack.receiptSha256).required()
        require(manifest.id == pack.id && receipt.candidateKey == pack.candidateKey) { "Published pack/receipt identity differs from library" }
        return manifest
    }
    internal fun readLocked(): SkinLibraryDocument {
        if (!fs.exists(authority)) {
            require(!fs.exists(backup) && !fs.exists(File(paths.root, "library.invalid.json"))) { "Library publication is incomplete; recover configuration to OFF" }
            val legacy = File(paths.root, "registry")
            val document = if (catalog.profile.legacyMigrationAllowed && fs.exists(legacy)) {
                val old = SkinRegistryStore(paths.root, quota, fs).snapshotForLibrary().required().document
                val packs = old.packs.sortedBy { it.id }.map { p -> LibraryPack(p.id, p.name, p.author, p.candidateKey, p.treeSha256, p.importReceiptSha256).also(::requireVerified) }
                SkinLibraryDocument(selectedPackId = old.activation.selectedPackId, packs = packs,
                    eligiblePackIds = old.packs.filter { it.rotationEligible }.sortedBy { it.id }.map { it.id })
            } else SkinLibraryDocument()
            commit(document)
        }
        val bytes = boundedRead(authority, SkinLibraryCodec.MAX_BYTES)
        val value = SkinLibraryCodec.decode(bytes, profileId)
        // Also retries a previous uncertain rename barrier before admitting a configuration to runtime.
        fs.syncFile(authority); fs.syncDirectory(paths.root)
        return value
    }
    internal fun configurationIdentity(document: SkinLibraryDocument) = SkinIdentity.sha256(SkinLibraryCodec.encode(document, profileId))
    private fun commit(document: SkinLibraryDocument, preserveBackup: Boolean = true) {
        val bytes = SkinLibraryCodec.encode(document, profileId)
        val old = if (fs.exists(authority)) runCatching { boundedRead(authority, SkinLibraryCodec.MAX_BYTES).also { SkinLibraryCodec.decode(it, profileId) } }.getOrNull() else null
        if (preserveBackup && old != null) writeAtomic(backup, old)
        else if (!fs.exists(backup)) writeAtomic(backup, bytes)
        try { writeAtomic(authority, bytes) }
        catch (failure: Exception) {
            if (old != null) try { writeAtomic(authority, old) } catch (rollback: Exception) {
                throw IllegalStateException("Configuration publication and rollback failed; recover OFF and retry: ${failure.message}; ${rollback.message}", failure)
            }
            throw IllegalStateException("Configuration publication failed; refresh before retry: ${failure.message}", failure)
        }
    }
    internal fun writeAtomic(file: File, bytes: ByteArray) {
        require(file.parentFile == paths.root && bytes.size <= SkinLibraryCodec.MAX_BYTES)
        fs.requireContained(file, paths.profileRoot, allowMissingLeaf = true)
        if (fs.exists(file)) require(fs.isRegularFile(file)) { "Configuration target is not a regular file" }
        val temporary = File(paths.root, ".library-${UUID.randomUUID()}.tmp")
        try {
            fs.writeNew(temporary, bytes); fs.syncFile(temporary)
            fs.atomicMove(temporary, file); fs.syncDirectory(paths.root)
        } finally {
            if (fs.exists(temporary)) fs.deleteContained(temporary, paths.root)
        }
    }
    internal fun boundedRead(file: File, maximum: Int): ByteArray {
        fs.requireContained(file, paths.profileRoot)
        require(fs.isRegularFile(file)) { "Expected a regular library file" }
        val before = fs.identity(file)
        require(before.size in 1..maximum.toLong()) { "Library file exceeds bound" }
        val bytes = fs.openNoFollow(file).use { input ->
            val output = java.io.ByteArrayOutputStream(); val buffer = ByteArray(8192)
            while (output.size() <= maximum) {
                val count = input.read(buffer, 0, minOf(buffer.size, maximum + 1 - output.size()))
                if (count < 0) break
                if (count == 0) continue
                output.write(buffer, 0, count)
            }
            output.toByteArray()
        }
        require(bytes.size.toLong() == before.size && fs.identity(file) == before) { "Library file changed while reading" }
        return bytes
    }
    internal fun <T> locked(nonblocking: Boolean = false, action: () -> SkinResult<T>): SkinResult<T> {
        if (nonblocking) { if (!processLock.tryLock()) return busy() } else processLock.lock()
        try {
            if (processLock.holdCount > 1) return action()
            ensureRoot()
            val lock = File(paths.root, "library.lock")
            fs.requireContained(lock, paths.profileRoot, allowMissingLeaf = true)
            FileChannel.open(lock.toPath(), WRITE, CREATE, NOFOLLOW_LINKS).use { channel ->
                val held = if (nonblocking) channel.tryLock() ?: return busy() else channel.lock()
                held.use { return action() }
            }
        } catch (error: Exception) {
            if (error is java.nio.channels.OverlappingFileLockException) return busy()
            if (error is LibraryOperationFailure) return error.result
            return SkinResult.Error(if (error is IllegalArgumentException) SkinImportCode.DOCUMENT_INVALID else SkinImportCode.DURABILITY_UNAVAILABLE,
                (error.message ?: "Skin library unavailable").take(1024))
        } finally { processLock.unlock() }
    }
    private fun ensureRoot() {
        fs.requireContained(paths.profileRoot, paths.profileRoot)
        ensureDirectory(paths.root, paths.profileRoot)
    }
    internal fun ensureDirectory(file: File, owner: File = paths.profileRoot) {
        fs.requireContained(file, owner, allowMissingLeaf = true)
        if (!fs.exists(file)) { fs.createDirectory(file); fs.syncDirectory(file); fs.syncDirectory(requireNotNull(file.parentFile)) }
        fs.requireContained(file, owner); require(fs.isDirectory(file)) { "Expected a skin directory" }
    }
    internal fun recordObservation(config: String, activeId: String, activeTree: String, status: String, detail: String): Boolean =
        locked(nonblocking = true) {
            require(config == configurationIdentity(readLocked())) { "Observation belongs to an older configuration" }
            require(status in OBSERVATION_STATUSES && activeId.length <= 64 && activeId.matches(Regex("[a-z0-9._-]*")))
            require((activeId.isEmpty() && activeTree.isEmpty()) || (activeId.isNotEmpty() && SkinLibraryCodec.digest(activeTree)))
            val observation = com.google.gson.JsonObject().apply {
                addProperty("configSha256", config); addProperty("activePackId", activeId); addProperty("activeTreeSha256", activeTree)
                addProperty("status", status); addProperty("detail", detail.take(1024)); addProperty("observedAtMillis", System.currentTimeMillis())
            }
            writeAtomic(File(paths.root, "library.observation.json"), observation.toString().toByteArray(Charsets.UTF_8))
            SkinResult.Ok(Unit)
        } is SkinResult.Ok
    internal fun lastObservation(document: SkinLibraryDocument): String {
        val file = File(paths.root, "library.observation.json")
        if (!fs.exists(file)) return "No game-process report yet; launch ${if (profileId == "silksong") "Silksong" else "Hollow Knight"}, then refresh"
        return try {
            val value = SkinLibraryCodec.strictJson(boundedRead(file, 16384), 16384).asJsonObject
            val status = value["status"].asString; require(status in OBSERVATION_STATUSES)
            val stale = value["configSha256"].asString != configurationIdentity(document)
            "Last game report${if (stale) " (older configuration)" else ""}: $status · ${value["activePackId"].asString.take(64)} · ${value["detail"].asString.take(1024)} · ${value["observedAtMillis"].asLong} ms UTC; refresh to retry status"
        } catch (error: Exception) { "Last game report unreadable: ${error.message?.take(256)}" }
    }
    private fun busy() = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, "Skin library is busy; retry at the next poll")
    companion object {
        private val locks = ConcurrentHashMap<String, ReentrantLock>()
        private val OBSERVATION_STATUSES = setOf("Applied", "Restored", "Unchanged", "AwaitingTargets", "Cancelled", "Rejected", "Failed", "RestoreFailed", "Blocked")
        internal fun production(context: android.content.Context, profile: dev.silksong.launcher.profiles.GameProfile): SkinLibraryStore {
            val skinProfile = dev.silksong.launcher.skins.catalog.SkinCatalogProfiles.require(profile)
            // Load the exact packaged per-game asset before constructing any catalog-dependent builder/repository.
            val catalog = dev.silksong.launcher.skins.catalog.SkinCatalogPaths.load(context.assets, skinProfile).required()
            val fs = AndroidSkinFileSystem(); val files = context.filesDir.absoluteFile
            val paths = dev.silksong.launcher.profiles.ProfilePaths(files, profile)
            for (directory in listOf(requireNotNull(paths.root.parentFile), paths.root)) {
                fs.requireContained(directory, files, allowMissingLeaf = true)
                if (!fs.exists(directory)) { fs.createDirectory(directory); fs.syncDirectory(requireNotNull(directory.parentFile)) }
            }
            return SkinLibraryStore(SkinPaths(paths.root), fs, catalog)
        }
    }
}
internal fun <T> SkinResult<T>.required(): T = when (this) {
    is SkinResult.Ok -> value
    is SkinResult.Error -> throw LibraryOperationFailure(this)
}
internal class LibraryOperationFailure(val result: SkinResult.Error) : IllegalStateException("${result.code}: ${result.detail}")
