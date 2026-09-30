package dev.silksong.launcher.skins.library

import com.google.gson.JsonObject
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
    internal val catalog: CatalogPathSet = CatalogPathSet.requirePinned(),
    private val publicationAllowed: () -> Boolean = { true }) {
    val profileId: String = catalog.profile.profileId
    private val processLock = locks.computeIfAbsent(paths.root.absolutePath) { ReentrantLock(true) }
    private val authority = File(paths.root, "library.json")
    private val backup = File(paths.root, "library.backup.json")
    internal val quota by lazy { SkinQuota(paths.root, fs) }
    internal val objects by lazy { SkinObjectRepository(paths, fs, catalog) }
    internal val receipts by lazy { SkinImportReceiptRepository(paths, fs, catalog) }
    init { require(paths.profileRoot.name == profileId && paths.profileRoot.parentFile?.name == "profiles") { "Wrong skin profile owner" } }

    internal fun startRuntime(): SkinResult<SkinLibraryDocument> = changeRotation { renewRotation(it.copy(activeSaveSlot = null)) }
    /** Reads only configuration. Slot authority is the typed loaded-game identity, never save contents. */
    internal fun bindSave(slot: Int): SkinResult<SkinLibraryDocument> = changeRotation { value ->
        require(slot in -1..4) { "Unsupported loaded save slot" }
        val nextSlot = slot.takeUnless { it == -1 }
        if (value.activeSaveSlot == nextSlot) value else {
            val affinity = value.saveAffinities.singleOrNull { it.slot == nextSlot }
            val staged = nextSlot != null && value.unboundSelectionPending
            val selected = if (staged) value.selectedPackId.takeUnless { value.mode == LibraryMode.OFF } else affinity?.packId
            // Admit typed save authority independently of object health. Runtime reads and
            // successful outcomes still verify it; a corrupt choice must be recoverable for THIS slot.
            val mode = when {
                nextSlot == null || staged -> value.mode
                value.mode == LibraryMode.ROTATE -> LibraryMode.ROTATE
                selected == null -> LibraryMode.OFF
                else -> LibraryMode.ON
            }
            renewRotation(value.copy(activeSaveSlot = nextSlot,
                unboundSelectionPending = nextSlot == null && value.unboundSelectionPending,
                selectedPackId = if (nextSlot == null) value.selectedPackId else selected, mode = mode))
        }
    }
    private fun confirmedAffinity(value: SkinLibraryDocument, pack: LibraryPack?): SkinLibraryDocument {
        val slot = value.activeSaveSlot ?: return value
        val affinity = SaveSkinAffinity(slot, pack?.id, pack?.treeSha256)
        return value.copy(saveAffinities = (value.saveAffinities.filterNot { it.slot == slot } + affinity).sortedBy { it.slot })
    }
    private fun renewRotation(value: SkinLibraryDocument): SkinLibraryDocument {
        require(value.configurationGeneration < Long.MAX_VALUE) { "Configuration generation exhausted; recover configuration" }
        return value.copy(configurationGeneration = value.configurationGeneration + 1,
            rotationRun = if (value.mode == LibraryMode.ROTATE) UUID.randomUUID().toString().replace("-", "") else null,
            lastDeath = 0, pendingPackId = null, pendingVanilla = false, queuedDeathOccurrences = emptyList())
    }
    private fun changeRotation(action: (SkinLibraryDocument) -> SkinLibraryDocument): SkinResult<SkinLibraryDocument> = locked(nonblocking = true) {
        val old = readLocked(); val next = action(old); SkinLibraryCodec.validate(next)
        if (old != next) commit(next)
        SkinResult.Ok(next)
    }
    internal fun confirmDeath(run: String, occurrence: Long): SkinResult<SkinLibraryDocument> = changeRotation { value ->
        require(value.mode == LibraryMode.ROTATE && value.rotationRun == run && occurrence > 0) { "Death belongs to a retired rotation run" }
        if (occurrence <= value.lastDeath || occurrence in value.queuedDeathOccurrences) value
        else if (hasPendingSuccessor(value)) {
            require(value.queuedDeathOccurrences.size < SkinLibraryCodec.MAX_QUEUED_DEATHS) { "Death rotation backlog is full" }
            require(occurrence > (value.queuedDeathOccurrences.lastOrNull() ?: value.lastDeath)) { "Death occurrence order changed" }
            value.copy(queuedDeathOccurrences = value.queuedDeathOccurrences + occurrence)
        } else activateOccurrence(value, occurrence)
    }
    private fun activateOccurrence(value: SkinLibraryDocument, occurrence: Long): SkinLibraryDocument {
        val currentIndex = value.eligiblePackIds.indexOf(value.selectedPackId)
        val nextPackId = when {
            value.selectedPackId == null -> value.eligiblePackIds.firstOrNull()
            currentIndex < 0 -> value.eligiblePackIds.firstOrNull()
            currentIndex + 1 < value.eligiblePackIds.size -> value.eligiblePackIds[currentIndex + 1]
            else -> null
        }
        val nextIsVanilla = nextPackId == null && value.selectedPackId != null
        return value.copy(lastDeath = occurrence, pendingPackId = nextPackId, pendingVanilla = nextIsVanilla)
    }
    private fun hasPendingSuccessor(value: SkinLibraryDocument) = value.pendingPackId != null || value.pendingVanilla
    internal fun cancelDeath(run: String, occurrence: Long): SkinResult<SkinLibraryDocument> = changeRotation { value ->
        require(value.mode == LibraryMode.ROTATE && value.rotationRun == run && occurrence > 0) { "Death belongs to a retired rotation run" }
        when {
            occurrence < value.lastDeath -> value
            occurrence in value.queuedDeathOccurrences ->
                value.copy(queuedDeathOccurrences = value.queuedDeathOccurrences - occurrence)
            occurrence == value.lastDeath && hasPendingSuccessor(value) -> {
                val cancelled = value.copy(pendingPackId = null, pendingVanilla = false)
                val next = cancelled.queuedDeathOccurrences.firstOrNull()
                if (next == null) cancelled
                else activateOccurrence(cancelled.copy(queuedDeathOccurrences = cancelled.queuedDeathOccurrences.drop(1)), next)
            }
            else -> value
        }
    }
    internal fun cancelRotation(run: String): SkinResult<SkinLibraryDocument> = changeRotation { value ->
        if (value.rotationRun == run) renewRotation(value) else value
    }
    internal fun finishRotation(config: String, run: String, occurrence: Long, id: String, tree: String): Boolean =
        changeRotation { value -> finishRotationDocument(value, config, run, occurrence, id, tree) } is SkinResult.Ok
    private fun finishRotationDocument(value: SkinLibraryDocument, config: String, run: String,
        occurrence: Long, id: String, tree: String): SkinLibraryDocument {
        require(configurationIdentity(value) == config && value.rotationRun == run && value.lastDeath == occurrence) {
            "Applied successor belongs to retired configuration"
        }
        val selected = if (value.pendingVanilla) {
            require(id.isEmpty() && tree.isEmpty()) { "Default successor report contains a pack identity" }
            null
        } else {
            require(value.pendingPackId == id && value.packs.single { it.id == id }.treeSha256 == tree) {
                "Applied successor belongs to retired configuration"
            }
            id
        }
        require(hasPendingSuccessor(value)) { "Rotation has no frozen successor" }
        val completed = value.copy(selectedPackId = selected, pendingPackId = null, pendingVanilla = false)
        val nextOccurrence = completed.queuedDeathOccurrences.firstOrNull()
        return if (nextOccurrence == null) completed
        else activateOccurrence(completed.copy(queuedDeathOccurrences = completed.queuedDeathOccurrences.drop(1)), nextOccurrence)
    }

    fun read(nonblocking: Boolean = false): SkinResult<SkinLibraryDocument> = locked(nonblocking) { SkinResult.Ok(readLocked()) }
    fun setMode(expectedConfiguration: String, mode: LibraryMode): SkinResult<Unit> = mutateExpected(expectedConfiguration) { value ->
        if (value.mode == mode) value.copy(unboundSelectionPending = value.activeSaveSlot == null)
        else {
            if (mode == LibraryMode.ON) {
                val pack = requireNotNull(value.packs.singleOrNull { it.id == value.selectedPackId }) {
                    "Select a pack before turning skins ON"
                }
                requireVerified(pack)
            } else if (mode == LibraryMode.ROTATE) {
                value.selectedPackId?.let { selected ->
                    requireVerified(value.packs.single { it.id == selected })
                }
            }
            renewRotation(value.copy(mode = mode))
        }
    }
    fun setSpriteScope(expectedConfiguration: String, scope: SpriteScope): SkinResult<Unit> = mutateExpected(expectedConfiguration) { value ->
        if (value.spriteScope == scope) value
        else {
            if (value.mode == LibraryMode.ON) {
                val pack = requireNotNull(value.packs.singleOrNull { it.id == value.selectedPackId }) {
                    "Selected pack was removed; refresh and retry"
                }
                requireVerified(pack)
            } else if (value.mode == LibraryMode.ROTATE) {
                value.selectedPackId?.let { selected ->
                    requireVerified(value.packs.single { it.id == selected })
                }
            }
            renewRotation(value.copy(spriteScope = scope))
        }
    }
    fun confirmPack(expectedConfiguration: String, id: String): SkinResult<Unit> = mutateExpected(expectedConfiguration) { value ->
        val pack = requireNotNull(value.packs.singleOrNull { it.id == id }) { "Pack was removed; refresh and retry" }
        requireVerified(pack)
        val changed = when (value.mode) {
            LibraryMode.OFF, LibraryMode.ON -> value.copy(selectedPackId = id, unboundSelectionPending = value.activeSaveSlot == null)
            LibraryMode.ROTATE -> value.copy(eligiblePackIds = if (id in value.eligiblePackIds)
                value.eligiblePackIds - id else value.eligiblePackIds + id)
        }
        if (changed == value) value else renewRotation(changed)
    }
    fun select(id: String): SkinResult<Unit> = mutate { value ->
        val pack = value.packs.singleOrNull { it.id == id } ?: error("Selected pack was removed; refresh and retry")
        requireVerified(pack)
        renewRotation(value.copy(selectedPackId = id, unboundSelectionPending = value.activeSaveSlot == null))
    }
    fun enable(id: String): SkinResult<Unit> = mutate { value ->
        val pack = requireNotNull(value.packs.singleOrNull { it.id == id }) { "Pack was removed; refresh and retry" }
        requireVerified(pack)
        renewRotation(value.copy(
            mode = if (value.mode == LibraryMode.OFF) LibraryMode.ON else value.mode,
            selectedPackId = id,
            unboundSelectionPending = value.activeSaveSlot == null,
        ))
    }
    fun disable(id: String): SkinResult<Unit> = mutate { value ->
        require(value.packs.any { it.id == id }) { "Pack was removed; refresh and retry" }
        require(value.selectedPackId == id && value.mode != LibraryMode.OFF) { "Only the selected enabled skin can be disabled" }
        renewRotation(value.copy(mode = LibraryMode.OFF))
    }
    fun setEligibility(id: String, eligible: Boolean): SkinResult<Unit> = mutate { value ->
        require(value.packs.any { it.id == id }) { "Pack was removed; refresh and retry" }
        val changed = value.copy(eligiblePackIds = if (eligible) (value.eligiblePackIds + id).distinct() else value.eligiblePackIds - id)
        if (changed == value) value else renewRotation(changed)
    }
    fun advanceMode(): SkinResult<Unit> = mutate { value ->
        val mode = when (value.mode) { LibraryMode.OFF -> LibraryMode.ON; LibraryMode.ON -> LibraryMode.ROTATE; LibraryMode.ROTATE -> LibraryMode.OFF }
        if (mode == LibraryMode.ON) {
            require(value.packs.any { it.id == value.selectedPackId }) { "Select a pack before turning skins ON" }
        }
        renewRotation(value.copy(mode = mode))
    }
    fun remove(id: String): SkinResult<Unit> = mutate { value ->
        checkEditable(value, id)
        require(value.packs.any { it.id == id }) { "Pack was removed; refresh and retry" }
        renewRotation(value.copy(packs = value.packs.filterNot { it.id == id }, selectedPackId = value.selectedPackId.takeUnless { it == id },
            eligiblePackIds = value.eligiblePackIds - id, saveAffinities = value.saveAffinities.filterNot { it.packId == id }))
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
            value.copy(packs = value.packs.map { if (it.id == replacing.id) pack else it },
                saveAffinities = value.saveAffinities.filterNot { it.packId == replacing.id })
        }
    }
    internal fun checkEditable(value: SkinLibraryDocument, id: String) {
        check(value.mode == LibraryMode.OFF || (value.selectedPackId != id && value.pendingPackId != id)) { "Selected or pending skin is in use: turn skins OFF, then retry replacement/removal" }
    }
    private fun stageUnboundIntent(old: SkinLibraryDocument, next: SkinLibraryDocument): SkinLibraryDocument =
        if (next.activeSaveSlot == null && (next.mode != old.mode || next.selectedPackId != old.selectedPackId))
            next.copy(unboundSelectionPending = true) else next
    private fun mutateExpected(expectedConfiguration: String, action: (SkinLibraryDocument) -> SkinLibraryDocument): SkinResult<Unit> = locked {
        val old = readLocked()
        require(SkinLibraryCodec.digest(expectedConfiguration) && configurationIdentity(old) == expectedConfiguration) {
            "Skin configuration changed; refresh and retry"
        }
        val next = stageUnboundIntent(old, action(old))
        SkinLibraryCodec.validate(next)
        if (next != old) commit(next)
        SkinResult.Ok(Unit)
    }
    private fun mutate(action: (SkinLibraryDocument) -> SkinLibraryDocument): SkinResult<Unit> = locked {
        val old = readLocked(); val next = stageUnboundIntent(old, action(old)); SkinLibraryCodec.validate(next)
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
        val recovered = good ?: saved ?: SkinLibraryDocument()
        val next = renewRotation(recovered.copy(mode = LibraryMode.OFF, unboundSelectionPending = recovered.activeSaveSlot == null))
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
                val mode = LibraryMode.valueOf(old.activation.mode.name)
                SkinLibraryDocument(mode = mode, selectedPackId = old.activation.selectedPackId, packs = packs,
                    eligiblePackIds = old.packs.filter { it.rotationEligible }.sortedBy { it.id }.map { it.id },
                    unboundSelectionPending = mode != LibraryMode.OFF && old.activation.selectedPackId != null,
                    spriteScope = SkinLibraryCodec.legacySpriteScope(mode, profileId))
            } else SkinLibraryDocument()
            commit(document)
        }
        val bytes = boundedRead(authority, SkinLibraryCodec.MAX_BYTES)
        val value = SkinLibraryCodec.decode(bytes, profileId)
        if (SkinLibraryCodec.strictJson(bytes).asJsonObject["schemaVersion"].asInt == 1) commit(value)
        // Also retries a previous uncertain rename barrier before admitting a configuration to runtime.
        fs.syncFile(authority); fs.syncDirectory(paths.root)
        return value
    }
    internal fun configurationIdentity(document: SkinLibraryDocument) = SkinIdentity.sha256(SkinLibraryCodec.encode(document.copy(saveAffinities = emptyList()), profileId))
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
        check(publicationAllowed()) { "Retired skin runtime operation cannot publish configuration or outcome" }
        require(file.parentFile == paths.root && bytes.size <= SkinLibraryCodec.MAX_BYTES)
        fs.requireContained(file, paths.profileRoot, allowMissingLeaf = true)
        if (fs.exists(file)) require(fs.isRegularFile(file)) { "Configuration target is not a regular file" }
        val temporary = File(paths.root, ".library-${UUID.randomUUID()}.tmp")
        try {
            fs.writeNew(temporary, bytes); fs.syncFile(temporary)
            check(publicationAllowed()) { "Retired skin runtime operation cannot publish configuration or outcome" }
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
    internal fun recordObservation(config: String, activeId: String, activeTree: String,
        status: String, detail: String, saveSlot: Int? = null): Boolean = locked(nonblocking = true) {
        val document = readLocked()
        require(document.activeSaveSlot == saveSlot) { "Observation belongs to another loaded save" }
        require(config == configurationIdentity(document)) { "Observation belongs to an older configuration" }
        val operation = operationContext(document, config, verify = status in SUCCESS_STATUSES)
        require(operation.generation == 0L) { "Rotation observation requires run and occurrence correlation" }
        val active = resolveActive(document, activeId, activeTree, verify = status in SUCCESS_STATUSES)
        validateOutcome(operation, active, status)
        val completed = if (successfulOutcome(operation, status)) confirmedAffinity(document, active.pack) else document
        if (completed != document) commit(completed)
        publishObservation(operation, active, status, detail, configurationIdentity(completed))
        SkinResult.Ok(Unit)
    } is SkinResult.Ok

    internal fun recordRotationObservation(config: String, run: String, occurrence: Long,
        activeId: String, activeTree: String, status: String, detail: String, saveSlot: Int? = null): Boolean =
        locked(nonblocking = true) {
            val current = readLocked()
            require(current.activeSaveSlot == saveSlot) { "Rotation report belongs to another loaded save" }
            val direct = config == configurationIdentity(current) && current.rotationRun == run &&
                current.lastDeath == occurrence && occurrence > 0 && hasPendingSuccessor(current)
            val requested = if (direct) current else {
                require(fs.exists(backup)) { "Rotation report belongs to retired work" }
                val previous = SkinLibraryCodec.decode(boundedRead(backup, SkinLibraryCodec.MAX_BYTES), profileId)
                require(previous.activeSaveSlot == saveSlot && config == configurationIdentity(previous) && previous.rotationRun == run &&
                    previous.lastDeath == occurrence && occurrence > 0 && hasPendingSuccessor(previous)) {
                    "Rotation report belongs to retired work"
                }
                previous
            }
            val operation = operationContext(requested, config, verify = status in SUCCESS_STATUSES)
            require(operation.generation == occurrence && operation.rotationRun == run) {
                "Rotation report belongs to retired work"
            }
            val active = resolveActive(requested, activeId, activeTree, verify = status in SUCCESS_STATUSES)
            validateOutcome(operation, active, status)
            val completes = rotationCompletes(operation, status)
            val completed = if (completes) {
                confirmedAffinity(finishRotationDocument(requested, config, run, occurrence,
                    operation.pack?.id.orEmpty(), operation.pack?.treeSha256.orEmpty()), active.pack).also(SkinLibraryCodec::validate)
            } else requested
            val resulting = when {
                direct && completes -> completed.also { commit(it) }
                direct -> requested
                else -> {
                    require(completes && completed == current) { "Rotation report belongs to retired work" }
                    current
                }
            }
            publishObservation(operation, active, status, detail, configurationIdentity(resulting))
            SkinResult.Ok(Unit)
        } is SkinResult.Ok

    internal fun menuEvidence(document: SkinLibraryDocument): SkinMenuEvidence {
        val config = configurationIdentity(document)
        val operation = operationContext(document, config, verify = false)
        val file = File(paths.root, OBSERVATION_FILE)
        if (!fs.exists(file)) return SkinMenuEvidence("PENDING", operation, null)
        return try {
            val observation = readObservationFile(file)
            val requestMatches = currentObservationMatches(document, operation, config, observation)
            val completedRotationMatches = completedRotationObservationMatches(document, config, observation)
            val matches = requestMatches || completedRotationMatches
            val state = when {
                !matches -> "STALE"
                observation["status"].asString == "AwaitingTargets" -> "PENDING"
                else -> "TERMINAL"
            }
            SkinMenuEvidence(state, operation, observation)
        } catch (_: Exception) {
            SkinMenuEvidence("UNREADABLE", operation, null)
        }
    }

    private fun currentObservationMatches(document: SkinLibraryDocument, operation: SkinOperationContext,
        config: String, observation: JsonObject): Boolean {
        val correlates = observation["profileId"].asString == profileId &&
            observation["saveSlot"].asInt == operation.saveSlot &&
            observation["operationId"].asString == operation.id &&
            observation["operationGeneration"].asLong == operation.generation &&
            observation["operationKind"].asString == operation.kind &&
            observation["featureId"].asString == operation.featureId &&
            observation["rotationRun"].asString == operation.rotationRun
        if (!correlates) return false
        require(observation["resultingConfigSha256"].asString == config) {
            "Matching observation has another resulting configuration"
        }
        requireObservationIdentities(document, operation, observation)
        return true
    }

    private fun completedRotationObservationMatches(document: SkinLibraryDocument, config: String,
        observation: JsonObject): Boolean {
        val generation = observation["operationGeneration"].asLong
        if (hasPendingSuccessor(document) || observation["featureId"].asString != FEATURE_DEATH_ROTATION ||
            observation["resultingConfigSha256"].asString != config ||
            observation["rotationRun"].asString != document.rotationRun.orEmpty() ||
            generation <= 0 || generation != document.lastDeath || !fs.exists(backup)) return false
        val previous = runCatching {
            SkinLibraryCodec.decode(boundedRead(backup, SkinLibraryCodec.MAX_BYTES), profileId)
        }.getOrNull() ?: return false
        val previousConfig = configurationIdentity(previous)
        val operation = runCatching { operationContext(previous, previousConfig) }.getOrNull() ?: return false
        if (observation["operationId"].asString != previousConfig ||
            observation["operationGeneration"].asLong != operation.generation ||
            observation["operationKind"].asString != operation.kind ||
            observation["featureId"].asString != operation.featureId ||
            observation["rotationRun"].asString != operation.rotationRun ||
            !rotationCompletes(operation, observation["status"].asString)) return false
        requireObservationIdentities(previous, operation, observation)
        val completed = confirmedAffinity(finishRotationDocument(previous, previousConfig, operation.rotationRun,
            operation.generation, operation.pack?.id.orEmpty(), operation.pack?.treeSha256.orEmpty()), operation.pack)
        return completed == document
    }

    internal fun lastObservation(document: SkinLibraryDocument): String {
        val evidence = menuEvidence(document)
        val observation = evidence.observation
        if (observation == null) return if (evidence.state == "UNREADABLE")
            "Last game report unreadable; refresh after the next runtime outcome"
        else "No game-process report yet; launch ${if (profileId == "silksong") "Silksong" else "Hollow Knight"}, then refresh"
        val stale = evidence.state == "STALE"
        return "Last game report${if (stale) " (older configuration)" else ""}: " +
            "${observation["status"].asString} · ${observation["activePackId"].asString.take(64)} · " +
            "${observation["detail"].asString.take(1024)} · ${observation["observedAtMillis"].asLong} ms UTC; refresh to retry status"
    }

    private fun operationContext(document: SkinLibraryDocument, config: String, verify: Boolean = true): SkinOperationContext {
        val rotating = hasPendingSuccessor(document)
        val pack = when {
            document.pendingPackId != null -> document.packs.single { it.id == document.pendingPackId }
            rotating -> null
            document.mode == LibraryMode.OFF -> null
            document.selectedPackId != null -> document.packs.single { it.id == document.selectedPackId }
            else -> null
        }
        if (verify) pack?.let(::requireVerified)
        val feature = if (rotating) FEATURE_DEATH_ROTATION
            else if (pack == null) FEATURE_LIVE_DEFAULT else FEATURE_LIVE_IMPORTED
        val kind = if (rotating) {
            if (pack == null) "ROTATE_DEFAULT" else "ROTATE_IMPORTED"
        } else if (pack == null) "RESTORE_DEFAULT" else "APPLY_IMPORTED"
        return SkinOperationContext(
            id = config,
            generation = if (rotating) document.lastDeath else 0,
            kind = kind,
            featureId = feature,
            rotationRun = if (rotating) requireNotNull(document.rotationRun) else "",
            pack = pack,
            saveSlot = document.activeSaveSlot ?: -1,
        )
    }

    private fun resolveActive(document: SkinLibraryDocument, id: String, tree: String, verify: Boolean = true): SkinActiveIdentity {
        require((id.isEmpty() && tree.isEmpty()) ||
            (id.matches(Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")) && SkinLibraryCodec.digest(tree))) {
            "Active skin identity is invalid"
        }
        if (id.isEmpty()) return SkinActiveIdentity(null)
        val pack = document.packs.singleOrNull { it.id == id && it.treeSha256 == tree }
            ?: if (!verify) previousPack(id, tree) else null
        requireNotNull(pack) { "Active imported skin is absent from the reported configuration" }
        if (verify) requireVerified(pack)
        return SkinActiveIdentity(pack)
    }

    private fun previousPack(id: String, tree: String): LibraryPack? = if (!fs.exists(backup)) null else
        runCatching { SkinLibraryCodec.decode(boundedRead(backup, SkinLibraryCodec.MAX_BYTES), profileId)
            .packs.singleOrNull { it.id == id && it.treeSha256 == tree } }.getOrNull()

    private fun requireObservationIdentities(document: SkinLibraryDocument,
        operation: SkinOperationContext, observation: JsonObject) {
        val resolved = persistedIdentity(document, observation, "resolved")
        require(resolved.pack == operation.pack) { "Observation resolved identity differs from its operation" }
        val active = persistedIdentity(document, observation, "active")
        validateOutcome(operation, active, observation["status"].asString)
    }

    private fun persistedIdentity(document: SkinLibraryDocument, observation: JsonObject,
        prefix: String): SkinActiveIdentity {
        if (observation["${prefix}Kind"].asString == "DEFAULT") return SkinActiveIdentity(null)
        val pack = document.packs.singleOrNull {
            it.id == observation["${prefix}PackId"].asString &&
                it.treeSha256 == observation["${prefix}TreeSha256"].asString &&
                it.receiptSha256 == observation["${prefix}ReceiptSha256"].asString
        } ?: if (observation["status"].asString !in SUCCESS_STATUSES)
            previousPack(observation["${prefix}PackId"].asString, observation["${prefix}TreeSha256"].asString)?.takeIf {
                it.receiptSha256 == observation["${prefix}ReceiptSha256"].asString
            } else null
        requireNotNull(pack) { "Observation imported identity is absent from its configuration" }
        if (observation["status"].asString in SUCCESS_STATUSES) requireVerified(pack)
        return SkinActiveIdentity(pack)
    }

    private fun successfulOutcome(operation: SkinOperationContext, status: String) = when (operation.kind) {
        "APPLY_IMPORTED", "ROTATE_IMPORTED" -> status == "Applied" || status == "Unchanged"
        "RESTORE_DEFAULT", "ROTATE_DEFAULT" -> status == "Restored" || status == "Unchanged"
        else -> false
    }
    private fun validateOutcome(operation: SkinOperationContext, active: SkinActiveIdentity, status: String) {
        require(status in OBSERVATION_STATUSES) { "Unknown skin outcome" }
        val successful = when (operation.kind) {
            "APPLY_IMPORTED", "ROTATE_IMPORTED" -> status == "Applied" || status == "Unchanged"
            "RESTORE_DEFAULT", "ROTATE_DEFAULT" -> status == "Restored" || status == "Unchanged"
            else -> false
        }
        if (successful) require(active.pack == operation.pack) { "Successful outcome does not match the resolved target" }
        if (operation.kind.endsWith("_DEFAULT"))
            require(status != "Applied") { "Default restoration cannot report an imported apply" }
        else require(status != "Restored") { "Imported apply cannot report a default restoration" }
    }

    private fun rotationCompletes(operation: SkinOperationContext, status: String) =
        when (operation.kind) {
            "ROTATE_IMPORTED" -> status == "Applied" || status == "Unchanged"
            "ROTATE_DEFAULT" -> status == "Restored" || status == "Unchanged"
            else -> false
        }

    private fun publishObservation(operation: SkinOperationContext, active: SkinActiveIdentity,
        status: String, detail: String, resultingConfig: String) {
        val target = operation.pack
        val activePack = active.pack
        val observation = JsonObject().apply {
            addProperty("schemaVersion", 1)
            addProperty("profileId", profileId)
            addProperty("saveSlot", operation.saveSlot)
            addProperty("featureId", operation.featureId)
            addProperty("operationId", operation.id)
            addProperty("operationGeneration", operation.generation)
            addProperty("operationKind", operation.kind)
            addProperty("rotationRun", operation.rotationRun)
            addProperty("requestConfigSha256", operation.id)
            addProperty("resultingConfigSha256", resultingConfig)
            addProperty("resolvedKind", if (target == null) "DEFAULT" else "IMPORTED")
            addProperty("resolvedPackId", target?.id.orEmpty())
            addProperty("resolvedTreeSha256", target?.treeSha256.orEmpty())
            addProperty("resolvedReceiptSha256", target?.receiptSha256.orEmpty())
            addProperty("activeKind", if (activePack == null) "DEFAULT" else "IMPORTED")
            addProperty("activePackId", activePack?.id.orEmpty())
            addProperty("activeTreeSha256", activePack?.treeSha256.orEmpty())
            addProperty("activeReceiptSha256", activePack?.receiptSha256.orEmpty())
            addProperty("status", status)
            addProperty("detail", redactDetail(detail))
            addProperty("observedAtMillis", System.currentTimeMillis())
        }
        val file = File(paths.root, OBSERVATION_FILE)
        val previous = if (fs.exists(file)) runCatching { readObservationFile(file) }.getOrNull() else null
        if (previous != null && OBSERVATION_PAYLOAD_FIELDS.all { previous[it] == observation[it] }) {
            // A previous rename may have succeeded before its durability barrier failed.
            fs.syncFile(file); fs.syncDirectory(paths.root)
            return
        }
        val bytes = observation.toString().toByteArray(Charsets.UTF_8)
        require(bytes.size <= MAX_OBSERVATION_BYTES) { "Skin observation exceeds byte bound" }
        writeAtomic(file, bytes)
    }

    private fun readObservationFile(file: File): JsonObject {
        val value = SkinLibraryCodec.strictJson(boundedRead(file, MAX_OBSERVATION_BYTES), MAX_OBSERVATION_BYTES).asJsonObject
        require(value.keySet() == OBSERVATION_FIELDS) { "Invalid skin observation fields" }
        require(OBSERVATION_STRING_FIELDS.all {
            value[it].isJsonPrimitive && value[it].asJsonPrimitive.isString
        }) { "Invalid skin observation string fields" }
        require(value["schemaVersion"].toString() == "1" && value["profileId"].asString == profileId)
        require(value["saveSlot"].toString().matches(Regex("-1|[0-4]")))
        require(value["featureId"].asString in OBSERVATION_FEATURES)
        require(SkinLibraryCodec.digest(value["operationId"].asString) &&
            value["requestConfigSha256"].asString == value["operationId"].asString &&
            SkinLibraryCodec.digest(value["resultingConfigSha256"].asString))
        val generationText = value["operationGeneration"].toString()
        require(generationText.matches(Regex("0|[1-9][0-9]{0,18}")))
        val generation = generationText.toLong()
        val feature = value["featureId"].asString
        val kind = value["operationKind"].asString
        val run = value["rotationRun"].asString
        if (feature == FEATURE_DEATH_ROTATION) {
            require(generation > 0 && kind in setOf("ROTATE_IMPORTED", "ROTATE_DEFAULT") &&
                run.matches(Regex("[0-9a-f]{32}")))
        } else {
            require(generation == 0L && run.isEmpty() &&
                ((feature == FEATURE_LIVE_IMPORTED && kind == "APPLY_IMPORTED") ||
                    (feature == FEATURE_LIVE_DEFAULT && kind == "RESTORE_DEFAULT")))
        }
        validatePersistedIdentity(value, "resolved")
        validatePersistedIdentity(value, "active")
        require(value["resolvedKind"].asString == if (kind.endsWith("_DEFAULT")) "DEFAULT" else "IMPORTED")
        require(value["status"].asString in OBSERVATION_STATUSES && value["detail"].asString.length <= 1024)
        val observedAt = value["observedAtMillis"].toString()
        require(observedAt.matches(Regex("[1-9][0-9]{0,18}")))
        return value
    }

    private fun validatePersistedIdentity(value: JsonObject, prefix: String) {
        val title = prefix.replaceFirstChar(Char::uppercaseChar)
        val kind = value["${prefix}Kind"].asString
        val id = value["${prefix}PackId"].asString
        val tree = value["${prefix}TreeSha256"].asString
        val receipt = value["${prefix}ReceiptSha256"].asString
        if (kind == "DEFAULT") require(id.isEmpty() && tree.isEmpty() && receipt.isEmpty())
        else require(kind == "IMPORTED" &&
            id.matches(Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")) &&
            SkinLibraryCodec.digest(tree) && SkinLibraryCodec.digest(receipt)) {
            "$title skin identity is invalid"
        }
    }

    private fun redactDetail(detail: String): String = PATH_IN_DETAIL
        .replace(detail.take(4096), "[REDACTED_PATH]")
        .take(1024)
    private fun busy() = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, "Skin library is busy; retry at the next poll")
    companion object {
        private val locks = ConcurrentHashMap<String, ReentrantLock>()
        private const val OBSERVATION_FILE = "library.observation.json"
        private const val MAX_OBSERVATION_BYTES = 16 * 1024
        private const val FEATURE_LIVE_IMPORTED = "SKIN-LIVE-IMPORTED"
        private const val FEATURE_LIVE_DEFAULT = "SKIN-LIVE-DEFAULT"
        private const val FEATURE_DEATH_ROTATION = "SKIN-DEATH-ROTATION"
        private val OBSERVATION_FEATURES = setOf(FEATURE_LIVE_IMPORTED, FEATURE_LIVE_DEFAULT, FEATURE_DEATH_ROTATION)
        private val SUCCESS_STATUSES = setOf("Applied", "Restored", "Unchanged")
        private val OBSERVATION_STATUSES = setOf("Applied", "Restored", "Unchanged", "AwaitingTargets", "Cancelled", "Rejected", "Failed", "RestoreFailed", "Blocked")
        private val OBSERVATION_FIELDS = setOf(
            "schemaVersion", "profileId", "saveSlot", "featureId", "operationId", "operationGeneration",
            "operationKind", "rotationRun", "requestConfigSha256", "resultingConfigSha256",
            "resolvedKind", "resolvedPackId", "resolvedTreeSha256", "resolvedReceiptSha256",
            "activeKind", "activePackId", "activeTreeSha256", "activeReceiptSha256",
            "status", "detail", "observedAtMillis",
        )
        private val OBSERVATION_STRING_FIELDS = OBSERVATION_FIELDS - setOf("schemaVersion", "saveSlot", "operationGeneration", "observedAtMillis")
        private val OBSERVATION_PAYLOAD_FIELDS = OBSERVATION_FIELDS - "observedAtMillis"
        private val PATH_IN_DETAIL = Regex("""(?im)(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\|//|/)[^\r\n]*""")
        internal fun production(context: android.content.Context, profile: dev.silksong.launcher.profiles.GameProfile,
            publicationAllowed: () -> Boolean = { true }): SkinLibraryStore {
            val skinProfile = dev.silksong.launcher.skins.catalog.SkinCatalogProfiles.require(profile)
            // Load the exact packaged per-game asset before constructing any catalog-dependent builder/repository.
            val catalog = dev.silksong.launcher.skins.catalog.SkinCatalogPaths.load(context.assets, skinProfile).required()
            val fs = AndroidSkinFileSystem(); val files = context.filesDir.absoluteFile
            val paths = dev.silksong.launcher.profiles.ProfilePaths(files, profile)
            for (directory in listOf(requireNotNull(paths.root.parentFile), paths.root)) {
                fs.requireContained(directory, files, allowMissingLeaf = true)
                if (!fs.exists(directory)) { fs.createDirectory(directory); fs.syncDirectory(requireNotNull(directory.parentFile)) }
            }
            return SkinLibraryStore(SkinPaths(paths.root), fs, catalog, publicationAllowed)
        }
    }
}

internal data class SkinOperationContext(
    val id: String,
    val generation: Long,
    val kind: String,
    val featureId: String,
    val rotationRun: String,
    val pack: LibraryPack?,
    val saveSlot: Int,
)
internal data class SkinActiveIdentity(val pack: LibraryPack?)
internal data class SkinMenuEvidence(
    val state: String,
    val operation: SkinOperationContext,
    val observation: JsonObject?,
)

internal fun <T> SkinResult<T>.required(): T = when (this) {
    is SkinResult.Ok -> value
    is SkinResult.Error -> throw LibraryOperationFailure(this)
}
internal class LibraryOperationFailure(val result: SkinResult.Error) : IllegalStateException("${result.code}: ${result.detail}")
