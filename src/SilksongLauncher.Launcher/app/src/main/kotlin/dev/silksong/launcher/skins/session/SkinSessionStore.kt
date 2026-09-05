package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaBudgets
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.registry.SkinLockManager
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.DurableDirectoryPublisher
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.requireContained
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.channels.SeekableByteChannel
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID

/**
 * Persists immutable lease transitions and performs bounded restart recovery under the ordered session/registry locks.
 * Acquisition and production launch wiring deliberately remain outside this store.
 */
class SkinSessionStore(
    skinsRoot: File,
    private val fs: SkinFileSystem = AndroidSkinFileSystem(),
    private val lockManager: SkinLockManager = SkinLockManager(skinsRoot),
    private val targetProcess: SkinTargetProcess? = null,
    private val quota: SkinQuotaAdmission,
) {
    internal val root = skinsRoot.absoluteFile.normalize()
    private val sessions = File(root, "sessions")
    private val staging = File(root, "staging")
    private val stagingFs = SkinSessionStagingFileSystem(fs, root, staging)
    private val publisher = DurableDirectoryPublisher(stagingFs)

    init {
        require(root.name == "skins" && root.parentFile?.name == PROFILE_ID) {
            "Session root must be the exact Hollow Knight profile skins child"
        }
        require(root.parentFile != null && root != root.parentFile) { "Session root has no profile owner" }
        require(lockManager.root == root) { "Session lock manager is bound to another profile" }
        require(quota.root.absoluteFile.normalize() == root) { "Session quota authority is bound to another profile" }
    }

    fun mutationGate(liveness: ProcessIdentityAuthority): LeaseMutationGate {
        val reservation = when (val result = quota.reserve(SkinQuotaBudgets.SESSION_RECOVERY)) {
            is SkinResult.Error -> return LeaseMutationGate.UNKNOWN
            is SkinResult.Ok -> result.value
        }
        return try {
            mutationGateForCoordinator(liveness)
        } finally {
            reservation.release()
        }
    }

    internal fun mutationGateForCoordinator(liveness: ProcessIdentityAuthority): LeaseMutationGate = when (
        val result = locked { recoverLocked(liveness) }
    ) {
        is SkinResult.Error -> LeaseMutationGate.UNKNOWN
        is SkinResult.Ok -> result.value.gate
    }

    fun recover(liveness: ProcessIdentityAuthority): SkinResult<LeaseHead?> = withQuota(
        SkinQuotaBudgets.SESSION_RECOVERY,
    ) {
        when (val result = locked { recoverLocked(liveness) }) {
            is SkinResult.Error -> result
            is SkinResult.Ok -> SkinResult.Ok(result.value.head)
        }
    }

    /** Ordered-lock coordinator seam for reserving one durable, never-decreasing launch sequence. */
    internal fun reserveSequenceForCoordinator(): SkinResult<Long> = locked {
        ensureLayout()?.let { return@locked it }
        val plan = when (val result = readStructuralRecoveryPlan()) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        recoverSequence(plan.sequence)?.let { return@locked it }
        val current = plan.sequence.effective
        confirmSequenceAuthority(current)?.let { return@locked it }
        val next = try {
            current?.let { Math.addExact(it, 1L) } ?: 0L
        } catch (_: ArithmeticException) {
            return@locked blocked("Session sequence is exhausted")
        }
        publishSequence(next)?.let { return@locked it }
        SkinResult.Ok(next)
    }

    /** Publishes the hash-only acquisition authorization before descriptor evidence can exist. */
    internal fun prepareAcquisitionForCoordinator(intent: SkinAcquisitionIntent): SkinResult<Unit> = locked {
        ensureLayout()?.let { return@locked it }
        val bytes = try {
            SkinAcquisitionIntentCodec.canonical(intent)
        } catch (error: Exception) {
            return@locked blocked("Acquisition intent is invalid: ${error.message}")
        }
        val durable = File(sessions, ACQUISITION_INTENT)
        val temporary = File(sessions, "$ACQUISITION_INTENT.tmp")
        try {
            fs.requireContained(durable, root, allowMissingLeaf = true)
            fs.requireContained(temporary, root, allowMissingLeaf = true)
            require(!fs.exists(durable) && !fs.exists(temporary)) { "Acquisition intent already exists" }
            fs.writeNew(temporary, bytes)
            fs.syncFile(temporary)
            fs.atomicMove(temporary, durable)
            fs.syncDirectory(sessions)
            require(readStable(durable, MAX_ACQUISITION_INTENT_BYTES).contentEquals(bytes)) {
                "Acquisition intent changed after durability barrier"
            }
            SkinResult.Ok(Unit)
        } catch (error: Exception) {
            unavailable("Cannot durably prepare acquisition intent: ${error.message}")
        }
    }

    /** Advances only the exact prepared authorization after the descriptor publication barrier. */
    internal fun advanceAcquisitionForCoordinator(prepared: SkinAcquisitionIntent): SkinResult<SkinAcquisitionIntent> = locked {
        if (prepared.phase != SkinAcquisitionPhase.PREPARED) return@locked blocked("Acquisition intent phase is invalid")
        val durable = File(sessions, ACQUISITION_INTENT)
        val temporary = File(sessions, "$ACQUISITION_INTENT.tmp")
        val preparedBytes = try {
            SkinAcquisitionIntentCodec.canonical(prepared)
        } catch (error: Exception) {
            return@locked blocked("Acquisition intent is invalid: ${error.message}")
        }
        val advanced = prepared.copy(phase = SkinAcquisitionPhase.DESCRIPTOR_DURABLE)
        val advancedBytes = SkinAcquisitionIntentCodec.canonical(advanced)
        try {
            fs.requireContained(durable, root)
            fs.requireContained(temporary, root, allowMissingLeaf = true)
            require(readStable(durable, MAX_ACQUISITION_INTENT_BYTES).contentEquals(preparedBytes)) {
                "Prepared acquisition intent changed before phase advance"
            }
            require(!fs.exists(temporary)) { "Acquisition intent temporary already exists" }
            fs.writeNew(temporary, advancedBytes)
            fs.syncFile(temporary)
            fs.atomicMove(temporary, durable)
            fs.syncDirectory(sessions)
            require(readStable(durable, MAX_ACQUISITION_INTENT_BYTES).contentEquals(advancedBytes)) {
                "Acquisition intent changed after phase barrier"
            }
            SkinResult.Ok(advanced)
        } catch (error: Exception) {
            unavailable("Cannot durably advance acquisition intent: ${error.message}")
        }
    }

    /** Clears only an exact descriptor-durable authorization after pending or terminal lease barriers. */
    internal fun completeAcquisitionForCoordinator(
        intent: SkinAcquisitionIntent,
        exactHead: LeaseHead,
    ): SkinResult<Unit> = locked {
        if (intent.phase != SkinAcquisitionPhase.DESCRIPTOR_DURABLE) {
            return@locked blocked("Acquisition intent is not descriptor-durable")
        }
        val record = when (val loaded = loadState(exactHead, stateDirectory(exactHead.descriptorId, exactHead.leaseId, exactHead.transitionSequence))) {
            is SkinResult.Error -> return@locked loaded
            is SkinResult.Ok -> loaded.value
        }
        if (!matchesAcquisitionIntent(record.document, intent)) {
            return@locked blocked("Lease state does not match acquisition intent")
        }
        when (exactHead.state) {
            LeaseState.LAUNCH_PENDING -> when (val active = readPointer(sessions, ACTIVE)) {
                is SkinResult.Error -> return@locked active
                is SkinResult.Ok -> if (active.value !is PointerState.Valid || active.value.head != exactHead) {
                    return@locked blocked("Pending acquisition lacks its exact active barrier")
                }
            }
            LeaseState.CLOSED -> {
                when (val active = readPointer(sessions, ACTIVE)) {
                    is SkinResult.Error -> return@locked active
                    is SkinResult.Ok -> if (active.value !is PointerState.Missing) {
                        return@locked blocked("Closed acquisition retained active authority")
                    }
                }
                when (val current = readPointer(leaseRoot(exactHead.descriptorId), CURRENT)) {
                    is SkinResult.Error -> return@locked current
                    is SkinResult.Ok -> if (current.value !is PointerState.Valid || current.value.head != exactHead) {
                        return@locked blocked("Closed acquisition lacks its exact current barrier")
                    }
                }
            }
            LeaseState.GAME_OWNED -> return@locked blocked("Claimed acquisition intent cannot be completed by coordinator")
        }
        removeAcquisitionIntent(intent)
    }

    /** Test/coordinator seam only. It has no production launch or acquisition caller in this slice. */
    internal fun establishPendingForCoordinator(
        handle: SkinLaunchHandle,
        launcherOwner: ProcessIdentity,
        registryGenerationId: String,
        registrySha256: String,
    ): SkinResult<LeaseHead> = locked {
        ensureLayout()?.let { return@locked it }
        val tokenSha256 = validateHandle(handle) ?: return@locked blocked("Launch handle is invalid")
        val pending = LeaseStateDocument(
            schemaVersion = SCHEMA_VERSION,
            leaseId = handle.leaseId,
            leaseTokenSha256 = tokenSha256,
            profileId = PROFILE_ID,
            sessionSequence = handle.sessionSequence,
            transitionSequence = 0,
            transitionId = deterministicId(
                "pending",
                handle.descriptorId.toString(),
                handle.descriptorSha256,
                handle.leaseId.toString(),
                tokenSha256,
                handle.sessionSequence.toString(),
                registryGenerationId,
                registrySha256,
                ownerKey(launcherOwner),
            ),
            parentTransitionId = null,
            state = LeaseState.LAUNCH_PENDING,
            descriptorId = handle.descriptorId,
            descriptorSha256 = handle.descriptorSha256,
            registryGenerationId = registryGenerationId,
            registrySha256 = registrySha256,
            launcherOwner = launcherOwner,
            gameOwner = null,
            closeReason = null,
        )
        try {
            SkinLeaseStateCodec.canonical(pending)
        } catch (error: Exception) {
            return@locked blocked("Pending lease authority is invalid: ${error.message}")
        }
        val head = SkinLeaseStateCodec.head(pending)
        when (val active = readPointer(sessions, ACTIVE)) {
            is SkinResult.Error -> return@locked active
            is SkinResult.Ok -> when (val value = active.value) {
                PointerState.Missing -> Unit
                is PointerState.Valid -> {
                    if (value.head != head) return@locked blocked("Another durable lease is active")
                }
                PointerState.Malformed -> return@locked corrupt("Active lease pointer is malformed")
            }
        }
        when (val result = publishState(pending)) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> if (result.value.head != head) return@locked corrupt("Published pending lease head changed")
        }
        establishZero(head)?.let { return@locked it }
        when (val result = casPointer(sessions, ACTIVE, expected = null, desired = head)) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> SkinResult.Ok(head)
        }
    }

    fun claim(handle: SkinLaunchHandle, gameOwner: ProcessIdentity): SkinResult<LeaseHead> = withQuota(
        SkinQuotaBudgets.SESSION_CLAIM,
    ) {
        locked { claimLocked(handle, gameOwner) }
    }

    private fun claimLocked(handle: SkinLaunchHandle, gameOwner: ProcessIdentity): SkinResult<LeaseHead> {
        ensureLayout()?.let { return it }
        val tokenSha256 = validateHandle(handle) ?: return blocked("Launch handle is invalid")
        if (!validOwner(gameOwner)) return blocked("Game owner is not exact")
        val active = when (val result = loadActive()) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        } ?: return retryClaimAfterActiveBarrier(handle, tokenSha256, gameOwner)
        if (!matchesHandle(active.document, handle, tokenSha256)) return blocked("Active lease does not match handle")
        if (active.head.state == LeaseState.GAME_OWNED) return confirmClaimRetry(active, gameOwner)
        if (active.head.state != LeaseState.LAUNCH_PENDING) return blocked("Active lease cannot be claimed")
        val child = active.document.copy(
            transitionSequence = 1,
            transitionId = deterministicId("claim", active.head.sha256, ownerKey(gameOwner)),
            parentTransitionId = active.document.transitionId,
            state = LeaseState.GAME_OWNED,
            gameOwner = gameOwner,
            closeReason = null,
        )
        advance(active, child)?.let { return it }
        val owned = SkinLeaseStateCodec.head(child)
        return when (val result = casPointer(sessions, ACTIVE, active.head, owned)) {
            is SkinResult.Error -> result
            is SkinResult.Ok -> SkinResult.Ok(owned)
        }
    }

    fun close(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead> = withQuota(
        SkinQuotaBudgets.SESSION_CLOSE,
    ) {
        locked { closeLocked(handle, reason) }
    }

    internal fun closeForCoordinator(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead> =
        locked { closeLocked(handle, reason) }

    private fun closeLocked(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead> {
        ensureLayout()?.let { return it }
        val tokenSha256 = validateHandle(handle) ?: return blocked("Launch handle is invalid")
        if (!validReason(reason)) return blocked("Close reason is invalid")
        val active = when (val result = loadActive()) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (active == null) return retryClosedAfterActiveBarrier(handle, tokenSha256, reason)
        if (!matchesHandle(active.document, handle, tokenSha256)) return blocked("Active lease does not match handle")
        return closeFromExactActive(active, reason)
    }

    /** Exact-head seam used only after bounded recovery has reconstructed the active pointer. */
    internal fun closeExactHeadForRecovery(expected: LeaseHead, reason: String): SkinResult<LeaseHead> = locked {
        ensureLayout()?.let { return@locked it }
        if (!validReason(reason)) return@locked blocked("Close reason is invalid")
        val active = when (val result = loadActive()) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        } ?: return@locked blocked("No active lease matches recovery head")
        if (active.head != expected) return@locked blocked("Recovery head does not exactly match active lease")
        closeFromExactActive(active, reason)
    }

    private fun recoverLocked(liveness: ProcessIdentityAuthority): SkinResult<RecoveryOutcome> {
        val plan = when (val result = readStructuralRecoveryPlan()) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        recoverSequence(plan.sequence)?.let { return it }
        abandonOrphanedAcquisition(plan)?.let { return it }
        clearIntentWithoutMatchingDescriptor(plan)?.let { return it }
        plan.descriptors.forEach { descriptor ->
            canonicalizeDescriptorPointers(descriptor)?.let { return it }
        }
        reconcileActivePointer(plan)?.let { return it }
        clearEstablishedAcquisitionIntent(plan)?.let { return it }

        val active = plan.descriptors.singleOrNull { it.head.document.state != LeaseState.CLOSED }
            ?: return SkinResult.Ok(RecoveryOutcome(null, LeaseMutationGate.CLEAR))
        return when (active.head.document.state) {
            LeaseState.LAUNCH_PENDING -> recoverPending(active.head, liveness)
            LeaseState.GAME_OWNED -> recoverOwned(active.head, liveness)
            LeaseState.CLOSED -> error("Closed descriptor cannot be the active recovery head")
        }
    }

    private fun recoverPending(head: StateRecord, liveness: ProcessIdentityAuthority): SkinResult<RecoveryOutcome> {
        val owner = expectedOwner(liveness, head.document.launcherOwner)
        val presence = exactTargetPresence(liveness)
        return when {
            owner == ExpectedOwnerLiveness.DefinitivelyDead && presence == ExactProcessPresence.Absent -> {
                when (val closed = closeExactHeadForRecovery(head.head, RECOVERY_LAUNCHER_DEAD)) {
                    is SkinResult.Error -> closed
                    is SkinResult.Ok -> SkinResult.Ok(RecoveryOutcome(null, LeaseMutationGate.CLEAR))
                }
            }
            owner is ExpectedOwnerLiveness.Alive || presence is ExactProcessPresence.Present -> {
                SkinResult.Ok(RecoveryOutcome(head.head, LeaseMutationGate.ACTIVE))
            }
            else -> SkinResult.Ok(RecoveryOutcome(head.head, LeaseMutationGate.UNKNOWN))
        }
    }

    private fun recoverOwned(head: StateRecord, liveness: ProcessIdentityAuthority): SkinResult<RecoveryOutcome> {
        val gameOwner = head.document.gameOwner
            ?: return corrupt("Game-owned recovery head has no exact owner")
        return when (expectedOwner(liveness, gameOwner)) {
            is ExpectedOwnerLiveness.Alive -> SkinResult.Ok(RecoveryOutcome(head.head, LeaseMutationGate.ACTIVE))
            ExpectedOwnerLiveness.DefinitivelyDead -> when (
                val closed = closeExactHeadForRecovery(head.head, RECOVERY_GAME_OWNER_DEAD)
            ) {
                is SkinResult.Error -> closed
                is SkinResult.Ok -> SkinResult.Ok(RecoveryOutcome(null, LeaseMutationGate.CLEAR))
            }
            ExpectedOwnerLiveness.Unknown -> SkinResult.Ok(RecoveryOutcome(head.head, LeaseMutationGate.UNKNOWN))
        }
    }

    private fun expectedOwner(
        liveness: ProcessIdentityAuthority,
        expected: ProcessIdentity,
    ): ExpectedOwnerLiveness = try {
        when (val result = liveness.expectedOwner(expected)) {
            is ExpectedOwnerLiveness.Alive -> result.takeIf { it.identity == expected } ?: ExpectedOwnerLiveness.Unknown
            ExpectedOwnerLiveness.DefinitivelyDead -> result
            ExpectedOwnerLiveness.Unknown -> result
        }
    } catch (_: Exception) {
        ExpectedOwnerLiveness.Unknown
    }

    private fun exactTargetPresence(liveness: ProcessIdentityAuthority): ExactProcessPresence {
        val target = targetProcess ?: return ExactProcessPresence.Unknown
        return try {
            when (val result = liveness.exactProcess(target.packageName, target.processName)) {
                is ExactProcessPresence.Present -> result.takeIf { validOwner(it.identity) } ?: ExactProcessPresence.Unknown
                ExactProcessPresence.Absent -> result
                ExactProcessPresence.Unknown -> result
            }
        } catch (_: Exception) {
            ExactProcessPresence.Unknown
        }
    }

    private fun readStructuralRecoveryPlan(): SkinResult<StructuralRecoveryPlan> {
        return try {
        require(fs.exists(root) && fs.isDirectory(root) && !fs.isSymbolicLink(root)) {
            "Skin root is not an existing no-alias directory"
        }
        fs.requireContained(root, root)
        if (!fs.exists(sessions)) {
            return SkinResult.Ok(
                StructuralRecoveryPlan(
                    emptyList(),
                    null,
                    sessionsPresent = false,
                    SessionSequencePlan(null, null, null),
                    AcquisitionRecoveryPlan(null, null),
                    orphanedAcquisition = null,
                    tombstonedAcquisition = null,
                ),
            )
        }
        requireNoAliasDirectory(sessions, root, "Session recovery root")
        val identity = fs.identity(sessions)
        val children = directChildren(sessions)
        val sequence = readSessionSequencePlan()
        val acquisition = readAcquisitionRecoveryPlan()
        val descriptorDirectories = children.filter { child -> child.name !in SESSION_FIXED_FILES }
        require(descriptorDirectories.size <= MAX_RECOVERY_DESCRIPTORS) { "Session descriptor bound exceeded" }
        descriptorDirectories.forEach { directory ->
            require(parseCanonicalUuid(directory.name) != null) { "Session descriptor directory name is invalid" }
            requireNoAliasDirectory(directory, sessions, "Session descriptor")
        }
        var orphanedAcquisition: OrphanedAcquisition? = null
        val descriptors = descriptorDirectories.sortedBy(File::getName).mapNotNull { directory ->
            val candidate = acquisition.effective
                ?.takeIf { it.descriptorId.toString() == directory.name }
                ?.let { readOrphanedAcquisition(directory, it) }
            if (candidate == null) {
                readDescriptorRecoveryPlan(directory)
            } else {
                require(orphanedAcquisition == null) { "More than one orphaned acquisition is present" }
                orphanedAcquisition = candidate
                null
            }
        }
        val tombstonedAcquisition = acquisition.effective?.let { expected ->
            requireNoAliasDirectory(staging, root, "Acquisition recovery staging")
            val tombstone = acquisitionTombstone(expected)
            fs.requireContained(tombstone, staging, allowMissingLeaf = true)
            if (fs.exists(tombstone)) {
                requireNoAliasDirectory(tombstone, staging, "Acquisition recovery tombstone")
                requireNotNull(readOrphanedAcquisition(tombstone, expected, relocated = true)) {
                    "Acquisition recovery tombstone does not contain exact orphan evidence"
                }
            } else {
                null
            }
        }
        require(orphanedAcquisition == null || tombstonedAcquisition == null) {
            "Acquisition source and deterministic tombstone both exist"
        }
        val validatedSequences = descriptors.map(DescriptorRecoveryPlan::sessionSequence) +
            listOfNotNull(
                orphanedAcquisition?.descriptor?.sessionSequence,
                tombstonedAcquisition?.descriptor?.sessionSequence,
            )
        require(validatedSequences.toSet().size == validatedSequences.size) {
            "Session descriptor sequences are not unique"
        }
        if (validatedSequences.isNotEmpty()) {
            val highestReserved = sequence.effective
                ?: throw RecoveryEvidenceException("Session sequence authority is absent behind descriptors")
            require(validatedSequences.all { it <= highestReserved }) {
                "Session sequence authority lags a validated descriptor"
            }
        }
        val intent = acquisition.effective
        val matchingDescriptor = intent?.let { expected ->
            descriptors.singleOrNull { it.head.document.descriptorId == expected.descriptorId }
        }
        if (intent != null) {
            when {
                orphanedAcquisition != null -> {
                    require(matchingDescriptor == null) { "Acquisition has duplicate descriptor evidence" }
                    require(descriptorDirectories.count { it.name == intent.descriptorId.toString() } == 1) {
                        "Orphaned acquisition descriptor identity is ambiguous"
                    }
                    require(descriptors.all { it.head.document.state == LeaseState.CLOSED }) {
                        "Orphaned acquisition conflicts with an existing active lease"
                    }
                }
                tombstonedAcquisition != null -> {
                    require(matchingDescriptor == null) { "Tombstoned acquisition has live descriptor evidence" }
                    require(descriptorDirectories.none { it.name == intent.descriptorId.toString() }) {
                        "Tombstoned acquisition retained its sessions source"
                    }
                    require(descriptors.all { it.head.document.state == LeaseState.CLOSED }) {
                        "Tombstoned acquisition conflicts with an existing active lease"
                    }
                }
                matchingDescriptor != null -> {
                    require(intent.phase == SkinAcquisitionPhase.DESCRIPTOR_DURABLE) {
                        "Established acquisition descriptor lacks its durable phase"
                    }
                    require(matchesAcquisitionIntent(matchingDescriptor.head.document, intent)) {
                        "Established acquisition descriptor mismatches its intent"
                    }
                    require(matchingDescriptor.head.document.state != LeaseState.GAME_OWNED) {
                        "Claimed lease retained acquisition intent"
                    }
                }
                else -> require(descriptors.all { it.head.document.state == LeaseState.CLOSED }) {
                    "Descriptor-free acquisition intent conflicts with an existing active lease"
                }
            }
        }
        require(fs.identity(sessions) == identity) { "Session recovery root identity changed" }

        val nonClosed = descriptors.filter { it.head.document.state != LeaseState.CLOSED }
        require(nonClosed.size <= 1) { "More than one non-closed lease is present" }
        val recordsByHead = descriptors.flatMap(DescriptorRecoveryPlan::records).associateBy(StateRecord::head)
        require(recordsByHead.size == descriptors.sumOf { it.records.size }) { "Duplicate lease head identity is present" }
        val active = qualifiedGlobalPointer(readPointerFile(sessions, ACTIVE), recordsByHead, "active")
        val desiredActive = nonClosed.singleOrNull()?.head
        if (active != null && desiredActive != null) {
            require(sameLease(active.head, desiredActive.head)) { "Active pointer crosses the resolved lease" }
        }
        val activeTemporary = qualifiedGlobalPointer(readPointerFile(sessions, "$ACTIVE.tmp"), recordsByHead, "active.tmp")
        require(activeTemporary == null || activeTemporary.head == desiredActive?.head) {
            "Active temporary pointer does not match the resolved head"
        }
        if (desiredActive == null) {
            require(activeTemporary == null) { "Closed recovery has an unexplained active temporary pointer" }
        }
        SkinResult.Ok(
            StructuralRecoveryPlan(
                descriptors,
                active,
                sessionsPresent = true,
                sequence,
                acquisition,
                orphanedAcquisition,
                tombstonedAcquisition,
            ),
        )
        } catch (error: RecoveryEvidenceException) {
            corrupt(error.message ?: "Invalid session recovery evidence")
        } catch (error: Exception) {
            corrupt("Invalid session recovery evidence: ${error.message}")
        }
    }

    private fun readAcquisitionRecoveryPlan(): AcquisitionRecoveryPlan {
        fun read(name: String): SkinAcquisitionIntent? {
            val file = File(sessions, name)
            fs.requireContained(file, root, allowMissingLeaf = true)
            if (!fs.exists(file)) return null
            val bytes = readStable(file, MAX_ACQUISITION_INTENT_BYTES)
            return recoveryValue(SkinAcquisitionIntentCodec.parse(bytes), "Acquisition intent is invalid")
        }

        val durable = read(ACQUISITION_INTENT)
        val temporary = read("$ACQUISITION_INTENT.tmp")
        if (durable != null && temporary != null) {
            require(durable.phase == SkinAcquisitionPhase.PREPARED) {
                "Acquisition intent durable phase cannot have a temporary successor"
            }
            require(temporary.phase == SkinAcquisitionPhase.DESCRIPTOR_DURABLE) {
                "Acquisition intent temporary phase is not its exact successor"
            }
            require(durable.copy(phase = temporary.phase) == temporary) {
                "Acquisition intent temporary binding mismatches"
            }
        } else if (temporary != null) {
            require(temporary.phase == SkinAcquisitionPhase.PREPARED) {
                "Orphan acquisition intent temporary has an invalid phase"
            }
        }
        return AcquisitionRecoveryPlan(durable, temporary)
    }

    private fun readOrphanedAcquisition(
        directory: File,
        intent: SkinAcquisitionIntent,
        relocated: Boolean = false,
    ): OrphanedAcquisition? {
        val identity = fs.identity(directory)
        val children = directChildren(directory)
        val names = children.map(File::getName).toSet()
        val descriptorOnly = names == ACQUISITION_DESCRIPTOR_FILES
        val pendingWithoutPointer = names == DESCRIPTOR_DIRECTORY_FILES && run {
            val lease = File(directory, LEASE_DIRECTORY)
            requireNoAliasDirectory(lease, directory, "Acquisition lease directory")
            val leaseNames = directChildren(lease).map(File::getName).toSet()
            leaseNames == setOf(STATES_DIRECTORY) || leaseNames == setOf(STATES_DIRECTORY, "$NEXT.tmp")
        }
        if (!descriptorOnly && !pendingWithoutPointer) return null
        requireEmptyMarker(File(directory, COMPLETE), "Acquisition descriptor completion marker")
        val descriptorBytes = readStable(File(directory, DESCRIPTOR_JSON), MAX_DESCRIPTOR_BYTES)
        val descriptorSha256 = parseDigestSidecar(readStable(File(directory, DESCRIPTOR_SHA256), 65))
            ?: throw RecoveryEvidenceException("Acquisition descriptor digest sidecar is invalid")
        require(descriptorSha256 == intent.descriptorSha256 && descriptorSha256 == sha256(descriptorBytes)) {
            "Acquisition descriptor digest mismatches intent"
        }
        val catalog = CatalogPathSet.requirePinned().revalidate()
        val descriptor = recoveryValue(
            SkinLaunchDescriptorCodec.parse(
                descriptorBytes,
                descriptorSha256,
                DescriptorExpectations(
                    intent.descriptorId,
                    SkinLaunchDescriptorCodec.PROFILE_ID,
                    SkinLaunchDescriptorCodec.GAME_VERSION,
                    HollowKnightCatalogPaths.CATALOG_ID,
                    catalog.sha256,
                    intent.leaseId,
                ),
            ),
            "Orphaned launch descriptor is invalid",
        )
        require(
            descriptor.sessionSequence == intent.sessionSequence &&
                descriptor.registryGenerationId == intent.registryGenerationId &&
                descriptor.registryGenerationSha256 == intent.registrySha256 &&
                descriptor.leaseTokenSha256 == intent.leaseTokenSha256,
        ) { "Orphaned launch descriptor mismatches acquisition intent" }
        val pending = if (pendingWithoutPointer) {
            require(intent.phase == SkinAcquisitionPhase.DESCRIPTOR_DURABLE) {
                "Unpointed pending state lacks descriptor-durable authorization"
            }
            val lease = File(directory, LEASE_DIRECTORY)
            val states = File(lease, STATES_DIRECTORY)
            requireNoAliasDirectory(states, lease, "Acquisition lease states")
            val stateDirectories = directChildren(states)
            require(stateDirectories.size <= 1) { "Unpointed acquisition has too many states" }
            if (stateDirectories.isEmpty()) {
                require(!fs.exists(File(lease, "$NEXT.tmp"))) {
                    "Unpointed acquisition has a pointer without state evidence"
                }
                null
            } else {
                val record = recoveryValue(
                    readStateDirectory(stateDirectories.single(), enforceCanonicalLocation = !relocated),
                    "Unpointed acquisition state is invalid",
                )
                require(record.document.state == LeaseState.LAUNCH_PENDING && record.document.transitionSequence == 0L) {
                    "Unpointed acquisition state is not transition zero"
                }
                require(matchesAcquisitionIntent(record.document, intent)) {
                    "Unpointed acquisition state mismatches intent"
                }
                val nextTemporary = recoveryValue(readPointerFile(lease, "$NEXT.tmp"), "Unpointed next temporary is invalid")
                require(nextTemporary is PointerState.Missing || nextTemporary is PointerState.Valid && nextTemporary.head == record.head) {
                    "Unpointed next temporary mismatches transition zero"
                }
                record
            }
        } else {
            null
        }
        require(fs.identity(directory) == identity) { "Orphaned acquisition descriptor identity changed" }
        return OrphanedAcquisition(directory, descriptor, pending)
    }

    private fun readDescriptorRecoveryPlan(directory: File): DescriptorRecoveryPlan {
        val descriptorId = requireNotNull(parseCanonicalUuid(directory.name))
        val identity = fs.identity(directory)
        val children = directChildren(directory)
        require(children.map(File::getName).toSet() == DESCRIPTOR_DIRECTORY_FILES) {
            "Descriptor directory file set is not exact"
        }
        requireEmptyMarker(File(directory, COMPLETE), "Descriptor completion marker")
        val lease = File(directory, LEASE_DIRECTORY)
        requireNoAliasDirectory(lease, directory, "Descriptor lease directory")
        val leasePlan = readLeaseRecoveryPlan(descriptorId, lease)

        val descriptorBytes = readStable(File(directory, DESCRIPTOR_JSON), MAX_DESCRIPTOR_BYTES)
        val descriptorSha256 = parseDigestSidecar(readStable(File(directory, DESCRIPTOR_SHA256), 65))
            ?: throw RecoveryEvidenceException("Descriptor digest sidecar is invalid")
        require(descriptorSha256 == sha256(descriptorBytes)) { "Descriptor digest mismatches bytes" }
        val catalog = CatalogPathSet.requirePinned().revalidate()
        val expected = DescriptorExpectations(
            descriptorId = descriptorId,
            profileId = SkinLaunchDescriptorCodec.PROFILE_ID,
            gameVersion = SkinLaunchDescriptorCodec.GAME_VERSION,
            catalogId = HollowKnightCatalogPaths.CATALOG_ID,
            catalogSha256 = catalog.sha256,
            leaseId = leasePlan.head.document.leaseId,
        )
        val descriptor = recoveryValue(
            SkinLaunchDescriptorCodec.parse(descriptorBytes, descriptorSha256, expected),
            "Launch descriptor is invalid",
        )
        leasePlan.records.forEach { record ->
            val state = record.document
            require(state.descriptorId == descriptor.descriptorId && state.descriptorSha256 == descriptorSha256) {
                "Lease state descriptor linkage mismatches"
            }
            require(state.leaseId == descriptor.leaseId && state.leaseTokenSha256 == descriptor.leaseTokenSha256) {
                "Lease state token identity mismatches descriptor"
            }
            require(state.sessionSequence == descriptor.sessionSequence) { "Lease session sequence mismatches descriptor" }
            require(
                state.registryGenerationId == descriptor.registryGenerationId &&
                    state.registrySha256 == descriptor.registryGenerationSha256,
            ) { "Lease registry identity mismatches descriptor" }
        }
        require(fs.identity(directory) == identity) { "Descriptor directory identity changed" }
        return DescriptorRecoveryPlan(
            leasePlan.records,
            leasePlan.head,
            leasePlan.pointers,
            lease,
            leasePlan.installNextBeforeRepair,
            leasePlan.previousFallbackRepair,
            descriptor.sessionSequence,
        )
    }

    private fun readLeaseRecoveryPlan(descriptorId: UUID, lease: File): LeaseRecoveryPlan {
        val identity = fs.identity(lease)
        val children = directChildren(lease)
        require(children.map(File::getName).all { it in LEASE_DIRECTORY_FILES }) { "Lease directory file set is not exact" }
        val states = File(lease, STATES_DIRECTORY)
        requireNoAliasDirectory(states, lease, "Lease states directory")
        val stateIdentity = fs.identity(states)
        val stateDirectories = directChildren(states)
        require(stateDirectories.isNotEmpty()) { "Descriptor has no complete lease state" }
        require(stateDirectories.size <= MAX_RECOVERY_STATES) { "Lease state bound exceeded" }
        stateDirectories.forEach { requireNoAliasDirectory(it, states, "Lease state") }
        val records = stateDirectories.map { directory ->
            recoveryValue(readStateDirectory(directory), "Lease state is invalid")
        }.sortedBy { it.document.transitionSequence }
        require(fs.identity(states) == stateIdentity) { "Lease states directory identity changed" }
        require(records.map { it.document.transitionSequence } == records.indices.map(Int::toLong)) {
            "Lease transition sequences are not exactly contiguous"
        }
        require(records.map { it.document.transitionId }.toSet().size == records.size) {
            "Lease transition IDs are not unique"
        }
        require(records.all { it.document.descriptorId == descriptorId }) { "Lease state crosses descriptor identity" }
        require(records.map { it.document.leaseId }.toSet().size == 1) { "Descriptor contains multiple lease identities" }
        records.zipWithNext().forEach { (parent, child) ->
            try {
                SkinLeaseStateCodec.requireImmediateChild(child.document, parent.document)
            } catch (error: Exception) {
                throw RecoveryEvidenceException("Lease state chain is invalid: ${error.message}")
            }
        }

        val current = qualifiedLeasePointer(readPointerFile(lease, CURRENT), records, CURRENT)
        val previous = qualifiedLeasePointer(readPointerFile(lease, PREVIOUS), records, PREVIOUS)
        val next = qualifiedLeasePointer(readPointerFile(lease, NEXT), records, NEXT)
        val currentTemporary = qualifiedLeasePointer(readPointerFile(lease, "$CURRENT.tmp"), records, "current.tmp")
        val previousTemporary = qualifiedLeasePointer(readPointerFile(lease, "$PREVIOUS.tmp"), records, "previous.tmp")
        val nextTemporary = qualifiedLeasePointer(readPointerFile(lease, "$NEXT.tmp"), records, "next.tmp")
        val pointers = ResolvedPointers(current, previous, next)
        val durableIntent = readPreviousFallbackIntent(lease, REPAIR_INTENT)
        val temporaryIntent = readPreviousFallbackIntent(lease, "$REPAIR_INTENT.tmp")
        require(durableIntent == null || temporaryIntent == null) { "Repair intent has duplicate durable evidence" }
        val finalRecord = records.last()
        val expectedFallbackIntent = PreviousFallbackIntent(finalRecord.head, exactParent(records, finalRecord)?.head)
        val previousOnlyFallback = current == null && previous == finalRecord && next == null
        val fallbackIntermediate = current == finalRecord && previous == finalRecord && next == null
        val canonicalFinal = current == finalRecord && previous == exactParent(records, finalRecord) && next == null
        val intent = durableIntent ?: temporaryIntent
        if (intent != null) {
            require(intent == expectedFallbackIntent) { "Previous-only repair intent mismatches the exact target" }
            require(previousOnlyFallback || fallbackIntermediate || canonicalFinal) {
                "Previous-only repair intent does not qualify this pointer tuple"
            }
            require(temporaryIntent == null || previousOnlyFallback) {
                "Temporary repair intent cannot authorize a published current pointer"
            }
        }
        val resolved = if (fallbackIntermediate) {
            require(durableIntent == expectedFallbackIntent) {
                "Previous-only fallback intermediate lacks durable exact repair intent"
            }
            finalRecord
        } else {
            resolveHead(records, pointers)
        }
        val resumableClose = if (resolved != finalRecord) {
            finalRecord.takeIf { child ->
                records.getOrNull(records.lastIndex - 1) == resolved &&
                    current == resolved && previous == exactParent(records, resolved) && next == null &&
                    currentTemporary == null && previousTemporary == null &&
                    durableIntent == null && temporaryIntent == null &&
                    (nextTemporary == null || nextTemporary == child) &&
                    exactRecoveryCloseChild(resolved, child)
            }
        } else {
            null
        }
        require(resolved == finalRecord || resumableClose != null) {
            "Resolved lease head does not terminate its complete state chain"
        }
        require(resumableClose == null || next == null) { "Recovery-close resume has conflicting next authority" }
        val head = resumableClose ?: resolved
        val installNextBeforeRepair = resumableClose != null
        if (installNextBeforeRepair) {
            require(intent == null) { "Recovery-close resume carries unrelated repair intent" }
        }
        require(currentTemporary == null || currentTemporary == head) { "current.tmp does not match its recovery target" }
        require(previousTemporary == null || previousTemporary == exactParent(records, head)) {
            "previous.tmp does not match its recovery target"
        }
        require(nextTemporary == null || (installNextBeforeRepair && nextTemporary == head)) {
            "next.tmp does not match its recovery target"
        }
        require(fs.identity(lease) == identity) { "Lease directory identity changed" }
        val fallbackRepair = if (previousOnlyFallback || fallbackIntermediate || (intent != null && canonicalFinal)) {
            PreviousFallbackRepair(expectedFallbackIntent)
        } else {
            null
        }
        return LeaseRecoveryPlan(records, head, pointers, installNextBeforeRepair, fallbackRepair)
    }

    private fun resolveHead(records: List<StateRecord>, pointers: ResolvedPointers): StateRecord {
        val current = pointers.current
        val previous = pointers.previous
        val next = pointers.next
        if (next == null) {
            if (current != null) {
                require(previous == exactParent(records, current)) { "Current pointer lacks its exact previous parent" }
                return current
            }
            return previous ?: throw RecoveryEvidenceException("Lease pointer tuple has no resolvable head")
        }
        if (next.document.transitionSequence == 0L && current == null && previous == null) return next
        if (current == null) throw RecoveryEvidenceException("Next pointer has no qualified current pointer")
        if (next == current) {
            require(previous == exactParent(records, current)) { "Same-state next tuple lacks the exact parent" }
            return next
        }
        require(next.document.transitionSequence == current.document.transitionSequence + 1L) {
            "Next pointer is not the immediate child sequence"
        }
        require(next.document.parentTransitionId == current.document.transitionId) {
            "Next pointer is not the immediate child identity"
        }
        val currentParent = exactParent(records, current)
        require(previous == current || previous == currentParent) { "Next tuple previous pointer is not qualified" }
        return next
    }

    private fun exactRecoveryCloseChild(parent: StateRecord, child: StateRecord): Boolean {
        val reason = when (parent.document.state) {
            LeaseState.LAUNCH_PENDING -> RECOVERY_LAUNCHER_DEAD
            LeaseState.GAME_OWNED -> RECOVERY_GAME_OWNER_DEAD
            LeaseState.CLOSED -> return false
        }
        return child.document.state == LeaseState.CLOSED && child.document.closeReason == reason &&
            child.document.transitionId == deterministicId("close", parent.head.sha256, reason) &&
            child.document.parentTransitionId == parent.document.transitionId
    }

    private fun readPreviousFallbackIntent(parent: File, name: String): PreviousFallbackIntent? {
        require(name == REPAIR_INTENT || name == "$REPAIR_INTENT.tmp")
        val file = File(parent, name)
        fs.requireContained(file, root, allowMissingLeaf = true)
        if (!fs.exists(file)) return null
        return parsePreviousFallbackIntent(readStable(file, MAX_REPAIR_INTENT_BYTES))
            ?: throw RecoveryEvidenceException("$name is malformed")
    }

    private fun canonicalPreviousFallbackIntent(value: PreviousFallbackIntent): ByteArray = buildString {
        append(REPAIR_INTENT_HEADER)
        append('\n')
        append(LeasePointerCodec.canonical(value.head).toString(StandardCharsets.US_ASCII))
        if (value.previous == null) {
            append("ABSENT\n")
        } else {
            append("PRESENT\n")
            append(LeasePointerCodec.canonical(value.previous).toString(StandardCharsets.US_ASCII))
        }
    }.toByteArray(StandardCharsets.US_ASCII).also {
        require(it.size <= MAX_REPAIR_INTENT_BYTES) { "Repair intent exceeds its bound" }
    }

    private fun parsePreviousFallbackIntent(bytes: ByteArray): PreviousFallbackIntent? {
        return try {
        if (bytes.size > MAX_REPAIR_INTENT_BYTES || bytes.any { (it.toInt() and 0xff) > 0x7f }) return null
        val lines = bytes.toString(StandardCharsets.US_ASCII).split('\n')
        if (lines.firstOrNull() != REPAIR_INTENT_HEADER || lines.lastOrNull()?.isNotEmpty() != false) return null
        val head = LeasePointerCodec.parse(pointerLines(lines, 1)) ?: return null
        val value = when (lines.getOrNull(6)) {
            "ABSENT" -> {
                if (lines.size != 8) return null
                PreviousFallbackIntent(head, null)
            }
            "PRESENT" -> {
                if (lines.size != 13) return null
                val previous = LeasePointerCodec.parse(pointerLines(lines, 7)) ?: return null
                PreviousFallbackIntent(head, previous)
            }
            else -> return null
        }
        value.takeIf { canonicalPreviousFallbackIntent(it).contentEquals(bytes) }
        } catch (_: Exception) {
            null
        }
    }

    private fun pointerLines(lines: List<String>, start: Int): ByteArray =
        lines.subList(start, start + 5).joinToString(separator = "\n", postfix = "\n")
            .toByteArray(StandardCharsets.US_ASCII)

    private fun exactParent(records: List<StateRecord>, child: StateRecord): StateRecord? =
        if (child.document.transitionSequence == 0L) null else records.getOrNull(child.document.transitionSequence.toInt() - 1)

    private fun qualifiedLeasePointer(
        pointer: SkinResult<PointerState>,
        records: List<StateRecord>,
        label: String,
    ): StateRecord? = when (val value = recoveryValue(pointer, "$label pointer cannot be read")) {
        PointerState.Missing -> null
        PointerState.Malformed -> throw RecoveryEvidenceException("$label pointer is malformed")
        is PointerState.Valid -> records.singleOrNull { it.head == value.head }
            ?: throw RecoveryEvidenceException("$label pointer does not name an exact state")
    }

    private fun qualifiedGlobalPointer(
        pointer: SkinResult<PointerState>,
        records: Map<LeaseHead, StateRecord>,
        label: String,
    ): StateRecord? = when (val value = recoveryValue(pointer, "$label pointer cannot be read")) {
        PointerState.Missing -> null
        PointerState.Malformed -> throw RecoveryEvidenceException("$label pointer is malformed")
        is PointerState.Valid -> records[value.head]
            ?: throw RecoveryEvidenceException("$label pointer does not name known exact state evidence")
    }

    private fun canonicalizeDescriptorPointers(plan: DescriptorRecoveryPlan): SkinResult.Error? {
        var effectiveNext = plan.pointers.next
        if (plan.installNextBeforeRepair) {
            repairPointer(plan.leaseDirectory, NEXT, effectiveNext?.head, plan.head.head)?.let { return it }
            effectiveNext = plan.head
        } else if (effectiveNext == plan.head && plan.pointers.current != plan.head) {
            repairPointer(plan.leaseDirectory, NEXT, plan.head.head, plan.head.head)?.let { return it }
        }
        plan.previousFallbackRepair?.let { repair ->
            ensurePreviousFallbackIntent(plan.leaseDirectory, repair.intent)?.let { return it }
        }

        val desiredPrevious = exactParent(plan.records, plan.head)
        val nextAdvancesCurrent = effectiveNext == plan.head && plan.pointers.current != null &&
            plan.pointers.current != plan.head
        val repairs = if (nextAdvancesCurrent) {
            listOf(
                PointerRepair(PREVIOUS, plan.pointers.previous?.head, desiredPrevious?.head),
                PointerRepair(CURRENT, plan.pointers.current?.head, plan.head.head),
                PointerRepair(NEXT, effectiveNext?.head, null),
            )
        } else {
            listOf(
                PointerRepair(CURRENT, plan.pointers.current?.head, plan.head.head),
                PointerRepair(PREVIOUS, plan.pointers.previous?.head, desiredPrevious?.head),
                PointerRepair(NEXT, effectiveNext?.head, null),
            )
        }
        repairs.forEach { repair ->
            repairPointer(plan.leaseDirectory, repair.name, repair.actual, repair.desired)?.let { return it }
        }
        return plan.previousFallbackRepair?.let { repair ->
            removePreviousFallbackIntent(plan.leaseDirectory, repair.intent)
        }
    }

    private fun ensurePreviousFallbackIntent(
        parent: File,
        expected: PreviousFallbackIntent,
    ): SkinResult.Error? = try {
        val file = File(parent, REPAIR_INTENT)
        val temporary = File(parent, "$REPAIR_INTENT.tmp")
        val bytes = canonicalPreviousFallbackIntent(expected)
        fs.requireContained(file, root, allowMissingLeaf = true)
        fs.requireContained(temporary, root, allowMissingLeaf = true)
        if (fs.exists(file)) {
            require(readStable(file, MAX_REPAIR_INTENT_BYTES).contentEquals(bytes)) { "Repair intent changed before retry" }
            fs.syncFile(file)
            fs.syncDirectory(parent)
        } else {
            if (fs.exists(temporary)) {
                require(readStable(temporary, MAX_REPAIR_INTENT_BYTES).contentEquals(bytes)) {
                    "Repair intent temporary differs"
                }
            } else {
                fs.writeNew(temporary, bytes)
            }
            fs.syncFile(temporary)
            fs.atomicMove(temporary, file)
            fs.syncDirectory(parent)
        }
        require(readStable(file, MAX_REPAIR_INTENT_BYTES).contentEquals(bytes)) {
            "Repair intent changed after durability barrier"
        }
        null
    } catch (error: Exception) {
        unavailable("Cannot durably establish previous-only repair intent: ${error.message}")
    }

    private fun removePreviousFallbackIntent(
        parent: File,
        expected: PreviousFallbackIntent,
    ): SkinResult.Error? = try {
        listOf("$REPAIR_INTENT.tmp", REPAIR_INTENT).forEach { name ->
            val file = File(parent, name)
            fs.requireContained(file, root, allowMissingLeaf = true)
            if (fs.exists(file)) {
                require(readStable(file, MAX_REPAIR_INTENT_BYTES).contentEquals(canonicalPreviousFallbackIntent(expected))) {
                    "Repair intent changed before removal"
                }
                fs.deleteContained(file, root)
                fs.syncDirectory(parent)
            }
        }
        null
    } catch (error: Exception) {
        unavailable("Cannot durably remove previous-only repair intent: ${error.message}")
    }

    private fun reconcileActivePointer(plan: StructuralRecoveryPlan): SkinResult.Error? {
        if (!plan.sessionsPresent) return null
        val desired = plan.descriptors.singleOrNull { it.head.document.state != LeaseState.CLOSED }?.head?.head
        return repairPointer(sessions, ACTIVE, plan.active?.head, desired)
    }

    private fun repairPointer(
        parent: File,
        name: String,
        actual: LeaseHead?,
        desired: LeaseHead?,
    ): SkinResult.Error? {
        val result = when {
            desired != null -> writePointer(parent, name, desired, setOf(actual, desired))
            actual != null -> removePointer(parent, name, expected = actual, acceptMissing = true)
            else -> try {
                fs.syncDirectory(parent)
                null
            } catch (error: Exception) {
                unavailable("Cannot durably confirm absent $name lease pointer: ${error.message}")
            }
        }
        val error = result?.let(::recoveryMutationError)
        if (error != null) return error
        return if (desired == null) null else removeMatchingPointerTemporary(parent, name, desired)
    }

    private fun removeMatchingPointerTemporary(
        parent: File,
        name: String,
        expected: LeaseHead,
    ): SkinResult.Error? {
        val temporaryName = "$name.tmp"
        val pointer = when (val result = readPointerFile(parent, temporaryName)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        return when (pointer) {
            PointerState.Missing -> null
            PointerState.Malformed -> corrupt("$temporaryName is malformed")
            is PointerState.Valid -> {
                if (pointer.head != expected) return corrupt("$temporaryName differs from its exact target")
                try {
                    fs.deleteContained(File(parent, temporaryName), root)
                    fs.syncDirectory(parent)
                    null
                } catch (error: Exception) {
                    unavailable("Cannot durably remove $temporaryName: ${error.message}")
                }
            }
        }
    }

    private fun recoveryMutationError(error: SkinResult.Error): SkinResult.Error =
        if (error.code == SkinImportCode.LIFECYCLE_BLOCKED) corrupt(error.detail) else error

    private fun readPointerFile(parent: File, name: String): SkinResult<PointerState> {
        require(name in POINTER_FILES)
        val file = File(parent, name)
        return try {
            fs.requireContained(parent, root)
            fs.requireContained(file, root, allowMissingLeaf = true)
            if (!fs.exists(file)) return SkinResult.Ok(PointerState.Missing)
            val bytes = readStable(file, MAX_POINTER_BYTES)
            val head = LeasePointerCodec.parse(bytes) ?: return SkinResult.Ok(PointerState.Malformed)
            SkinResult.Ok(PointerState.Valid(head))
        } catch (error: Exception) {
            corrupt("Lease pointer cannot be read: ${error.message}")
        }
    }

    private fun readSessionSequencePlan(): SessionSequencePlan {
        val durable = readSequenceEvidence(SEQUENCE)
        val temporary = readSequenceEvidence("$SEQUENCE.tmp")
        val effective = when {
            temporary == null -> durable
            durable == null -> {
                require(temporary == 0L) { "Virgin session sequence temporary is not zero" }
                temporary
            }
            else -> {
                val expected = try {
                    Math.addExact(durable, 1L)
                } catch (_: ArithmeticException) {
                    throw RecoveryEvidenceException("Session sequence temporary exceeds its bound")
                }
                require(temporary == expected) { "Session sequence temporary is not the exact successor" }
                temporary
            }
        }
        return SessionSequencePlan(durable, temporary, effective)
    }

    private fun readSequenceEvidence(name: String): Long? {
        require(name == SEQUENCE || name == "$SEQUENCE.tmp")
        val file = File(sessions, name)
        fs.requireContained(file, root, allowMissingLeaf = true)
        if (!fs.exists(file)) return null
        val bytes = readStable(file, MAX_SEQUENCE_BYTES)
        return parseSequence(bytes)
            ?: throw RecoveryEvidenceException("$name is malformed or non-canonical")
    }

    private fun acquisitionTombstone(intent: SkinAcquisitionIntent): File {
        val fingerprint = sha256(
            SkinAcquisitionIntentCodec.canonical(intent.copy(phase = SkinAcquisitionPhase.PREPARED)),
        )
        return File(staging, "$ACQUISITION_TOMBSTONE_PREFIX$fingerprint")
    }

    private fun matchesAcquisitionIntent(
        document: LeaseStateDocument,
        intent: SkinAcquisitionIntent,
    ): Boolean = document.descriptorId == intent.descriptorId &&
        document.descriptorSha256 == intent.descriptorSha256 &&
        document.leaseId == intent.leaseId &&
        document.leaseTokenSha256 == intent.leaseTokenSha256 &&
        document.sessionSequence == intent.sessionSequence &&
        document.registryGenerationId == intent.registryGenerationId &&
        document.registrySha256 == intent.registrySha256 &&
        document.launcherOwner == intent.launcherOwner

    private fun removeAcquisitionIntent(expected: SkinAcquisitionIntent): SkinResult<Unit> {
        val durable = File(sessions, ACQUISITION_INTENT)
        val temporary = File(sessions, "$ACQUISITION_INTENT.tmp")
        val bytes = SkinAcquisitionIntentCodec.canonical(expected)
        return try {
            fs.requireContained(durable, root)
            fs.requireContained(temporary, root, allowMissingLeaf = true)
            require(!fs.exists(temporary)) { "Acquisition intent temporary remains before completion" }
            require(readStable(durable, MAX_ACQUISITION_INTENT_BYTES).contentEquals(bytes)) {
                "Acquisition intent changed before completion"
            }
            fs.deleteContained(durable, root)
            fs.syncDirectory(sessions)
            require(!fs.exists(durable) && !fs.exists(temporary)) { "Acquisition intent remained after completion" }
            SkinResult.Ok(Unit)
        } catch (error: Exception) {
            unavailable("Cannot durably complete acquisition intent: ${error.message}")
        }
    }

    private fun abandonOrphanedAcquisition(plan: StructuralRecoveryPlan): SkinResult.Error? {
        val source = plan.orphanedAcquisition
        val alreadyTombstoned = plan.tombstonedAcquisition
        if (source == null && alreadyTombstoned == null) return null
        val intent = requireNotNull(plan.acquisition.effective)
        val tombstone = acquisitionTombstone(intent)
        val expected = source ?: requireNotNull(alreadyTombstoned)
        return try {
            requireNoAliasDirectory(staging, root, "Acquisition recovery staging")
            fs.requireContained(tombstone, staging, allowMissingLeaf = true)
            if (source != null) {
                require(source.directory.parentFile?.absoluteFile?.normalize() == sessions) {
                    "Orphaned acquisition source escaped sessions"
                }
                require(readOrphanedAcquisition(source.directory, intent) == source) {
                    "Orphaned acquisition changed before quarantine"
                }
                require(!fs.exists(tombstone)) { "Deterministic acquisition tombstone already exists" }
                fs.atomicMove(source.directory, tombstone)
            } else {
                require(!fs.exists(File(sessions, intent.descriptorId.toString()))) {
                    "Tombstoned acquisition source reappeared"
                }
                require(alreadyTombstoned?.directory?.absoluteFile?.normalize() == tombstone.absoluteFile.normalize()) {
                    "Recovered acquisition tombstone path changed"
                }
            }
            fs.syncDirectory(sessions)
            fs.syncDirectory(staging)
            require(!fs.exists(File(sessions, intent.descriptorId.toString()))) {
                "Acquisition source remained after quarantine barrier"
            }
            requireNoAliasDirectory(tombstone, staging, "Acquisition recovery tombstone")
            val recovered = requireNotNull(readOrphanedAcquisition(tombstone, intent, relocated = true)) {
                "Acquisition tombstone changed after quarantine barriers"
            }
            require(recovered.descriptor == expected.descriptor && recovered.pending == expected.pending) {
                "Acquisition tombstone contents changed across quarantine"
            }
            removeRecoveryIntentFiles(plan.acquisition)
        } catch (error: Exception) {
            unavailable("Cannot durably quarantine orphaned acquisition: ${error.message}")
        }
    }

    private fun clearIntentWithoutMatchingDescriptor(plan: StructuralRecoveryPlan): SkinResult.Error? {
        val intent = plan.acquisition.effective ?: return null
        if (plan.orphanedAcquisition != null || plan.tombstonedAcquisition != null) return null
        if (plan.descriptors.any { it.head.document.descriptorId == intent.descriptorId }) return null
        return try {
            removeRecoveryIntentFiles(plan.acquisition)
        } catch (error: Exception) {
            unavailable("Cannot durably clear acquisition intent without descriptor: ${error.message}")
        }
    }

    private fun clearEstablishedAcquisitionIntent(plan: StructuralRecoveryPlan): SkinResult.Error? {
        val intent = plan.acquisition.effective ?: return null
        val descriptor = plan.descriptors.singleOrNull { it.head.document.descriptorId == intent.descriptorId } ?: return null
        return try {
            require(readDescriptorRecoveryPlan(File(sessions, intent.descriptorId.toString())) == descriptor) {
                "Established acquisition changed before intent completion"
            }
            when (descriptor.head.document.state) {
                LeaseState.LAUNCH_PENDING -> {
                    val active = recoveryValue(readPointer(sessions, ACTIVE), "Active pointer changed before intent completion")
                    require(active is PointerState.Valid && active.head == descriptor.head.head) {
                        "Pending acquisition lacks its exact active barrier"
                    }
                }
                LeaseState.CLOSED -> {
                    require(recoveryValue(readPointer(sessions, ACTIVE), "Active pointer changed before intent completion") is PointerState.Missing) {
                        "Closed acquisition retained active authority"
                    }
                }
                LeaseState.GAME_OWNED -> error("Claimed lease retained acquisition intent")
            }
            removeRecoveryIntentFiles(plan.acquisition)
        } catch (error: Exception) {
            unavailable("Cannot durably complete recovered acquisition intent: ${error.message}")
        }
    }

    private fun removeRecoveryIntentFiles(plan: AcquisitionRecoveryPlan): SkinResult.Error? {
        listOf("$ACQUISITION_INTENT.tmp" to plan.temporary, ACQUISITION_INTENT to plan.durable).forEach { (name, expected) ->
            val file = File(sessions, name)
            fs.requireContained(file, root, allowMissingLeaf = true)
            if (expected == null) {
                require(!fs.exists(file)) { "Acquisition intent appeared before recovery mutation" }
            } else {
                require(
                    readStable(file, MAX_ACQUISITION_INTENT_BYTES)
                        .contentEquals(SkinAcquisitionIntentCodec.canonical(expected)),
                ) { "Acquisition intent changed before recovery mutation" }
                fs.deleteContained(file, root)
                fs.syncDirectory(sessions)
            }
        }
        return null
    }

    private fun recoverSequence(plan: SessionSequencePlan): SkinResult.Error? {
        val temporaryValue = plan.temporary ?: return null
        val expectedBytes = canonicalSequence(temporaryValue)
        val temporary = File(sessions, "$SEQUENCE.tmp")
        val durable = File(sessions, SEQUENCE)
        return try {
            plan.durable?.let { current ->
                require(readStable(durable, MAX_SEQUENCE_BYTES).contentEquals(canonicalSequence(current))) {
                    "Session sequence changed before temporary recovery"
                }
                fs.syncFile(durable)
                fs.syncDirectory(sessions)
                require(readStable(durable, MAX_SEQUENCE_BYTES).contentEquals(canonicalSequence(current))) {
                    "Session sequence changed across its retry barrier"
                }
            } ?: require(!fs.exists(durable)) { "Virgin session sequence appeared before recovery" }
            require(readStable(temporary, MAX_SEQUENCE_BYTES).contentEquals(expectedBytes)) {
                "Session sequence temporary changed before recovery"
            }
            fs.syncFile(temporary)
            fs.atomicMove(temporary, durable)
            fs.syncDirectory(sessions)
            require(readStable(durable, MAX_SEQUENCE_BYTES).contentEquals(expectedBytes)) {
                "Recovered session sequence changed after durability barrier"
            }
            null
        } catch (error: Exception) {
            unavailable("Cannot durably recover session sequence: ${error.message}")
        }
    }

    private fun confirmSequenceAuthority(expected: Long?): SkinResult.Error? = try {
        val durable = File(sessions, SEQUENCE)
        val temporary = File(sessions, "$SEQUENCE.tmp")
        require(!fs.exists(temporary)) { "Session sequence temporary remained after recovery" }
        if (expected == null) {
            require(!fs.exists(durable)) { "Session sequence appeared before reservation" }
        } else {
            val bytes = canonicalSequence(expected)
            require(readStable(durable, MAX_SEQUENCE_BYTES).contentEquals(bytes)) {
                "Session sequence changed before reservation"
            }
            fs.syncFile(durable)
            fs.syncDirectory(sessions)
            require(readStable(durable, MAX_SEQUENCE_BYTES).contentEquals(bytes)) {
                "Session sequence changed across its reservation barrier"
            }
        }
        null
    } catch (error: Exception) {
        unavailable("Cannot durably confirm session sequence: ${error.message}")
    }

    private fun publishSequence(value: Long): SkinResult.Error? = try {
        val durable = File(sessions, SEQUENCE)
        val temporary = File(sessions, "$SEQUENCE.tmp")
        val bytes = canonicalSequence(value)
        fs.requireContained(temporary, root, allowMissingLeaf = true)
        require(!fs.exists(temporary)) { "Session sequence temporary already exists" }
        fs.writeNew(temporary, bytes)
        fs.syncFile(temporary)
        fs.atomicMove(temporary, durable)
        fs.syncDirectory(sessions)
        require(readStable(durable, MAX_SEQUENCE_BYTES).contentEquals(bytes)) {
            "Session sequence changed after durability barrier"
        }
        null
    } catch (error: Exception) {
        unavailable("Cannot durably reserve session sequence: ${error.message}")
    }

    private fun canonicalSequence(value: Long): ByteArray {
        require(value >= 0L) { "Negative session sequence" }
        return "$value\n".toByteArray(StandardCharsets.US_ASCII)
    }

    private fun parseSequence(bytes: ByteArray): Long? {
        return try {
            if (bytes.isEmpty() || bytes.size > MAX_SEQUENCE_BYTES || bytes.last() != '\n'.code.toByte() ||
                bytes.any { (it.toInt() and 0xff) > 0x7f }
            ) {
                null
            } else {
                val text = bytes.copyOf(bytes.size - 1).toString(StandardCharsets.US_ASCII)
                if (!UNSIGNED_DECIMAL.matches(text)) {
                    null
                } else {
                    text.toLong().takeIf { it >= 0L && canonicalSequence(it).contentEquals(bytes) }
                }
            }
        } catch (_: Exception) {
            null
        }
    }

    private fun directChildren(directory: File): List<File> {
        val children = fs.list(directory)
        require(children.map(File::getName).toSet().size == children.size) { "Directory has duplicate child names" }
        children.forEach { child ->
            require(child.absoluteFile.normalize().parentFile == directory.absoluteFile.normalize()) {
                "Directory child escapes its exact parent"
            }
            fs.requireContained(child, root)
        }
        return children
    }

    private fun requireNoAliasDirectory(directory: File, owner: File, label: String) {
        fs.requireContained(directory, owner)
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "$label is not a no-alias directory" }
    }

    private fun requireEmptyMarker(file: File, label: String) {
        fs.requireContained(file, root)
        require(fs.isRegularFile(file) && !fs.isSymbolicLink(file) && fs.identity(file).size == 0L) {
            "$label is invalid"
        }
    }

    private fun parseCanonicalUuid(value: String): UUID? = try {
        UUID.fromString(value).takeIf { it.toString() == value && UUID_TEXT.matches(value) }
    } catch (_: Exception) {
        null
    }

    private fun sameLease(left: LeaseHead, right: LeaseHead): Boolean =
        left.descriptorId == right.descriptorId && left.leaseId == right.leaseId

    private fun <T> recoveryValue(result: SkinResult<T>, label: String): T = when (result) {
        is SkinResult.Ok -> result.value
        is SkinResult.Error -> throw RecoveryEvidenceException("$label: ${result.detail}")
    }

    private fun closeFromExactActive(parent: StateRecord, reason: String): SkinResult<LeaseHead> {
        if (parent.document.state !in setOf(LeaseState.LAUNCH_PENDING, LeaseState.GAME_OWNED)) {
            return blocked("Closed lease is terminal")
        }
        val child = parent.document.copy(
            transitionSequence = Math.addExact(parent.document.transitionSequence, 1L),
            transitionId = deterministicId("close", parent.head.sha256, reason),
            parentTransitionId = parent.document.transitionId,
            state = LeaseState.CLOSED,
            closeReason = reason,
        )
        advance(parent, child)?.let { return it }
        val closed = SkinLeaseStateCodec.head(child)
        return when (val result = casPointer(sessions, ACTIVE, parent.head, desired = null)) {
            is SkinResult.Error -> result
            is SkinResult.Ok -> SkinResult.Ok(closed)
        }
    }

    private fun retryClaimAfterActiveBarrier(
        handle: SkinLaunchHandle,
        tokenSha256: String,
        gameOwner: ProcessIdentity,
    ): SkinResult<LeaseHead> {
        val current = when (val result = loadCurrent(handle.descriptorId, handle.leaseId)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value ?: return blocked("No active lease matches handle")
        }
        if (current.document.state != LeaseState.GAME_OWNED || !matchesHandle(current.document, handle, tokenSha256)) {
            return blocked("No active lease matches handle")
        }
        return confirmClaimRetry(current, gameOwner)
    }

    private fun confirmClaimRetry(owned: StateRecord, gameOwner: ProcessIdentity): SkinResult<LeaseHead> {
        if (owned.document.gameOwner != gameOwner) return blocked("Game owner differs from claimed lease")
        val parent = when (val result = loadParent(owned)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (owned.document.transitionId != deterministicId("claim", parent.head.sha256, ownerKey(gameOwner))) {
            return blocked("Claim retry transition does not exactly match")
        }
        return confirmClaimActiveAfterLocalTransition(parent.head, owned.head).mapToHead(owned.head)
    }

    private fun confirmClaimActiveAfterLocalTransition(parent: LeaseHead, owned: LeaseHead): SkinResult<Unit> {
        val actual = when (val result = readPointer(sessions, ACTIVE)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        return when (actual) {
            PointerState.Malformed -> corrupt("Active lease pointer is malformed")
            PointerState.Missing -> writePointer(sessions, ACTIVE, owned, setOf(null, owned)).asUnit()
            is PointerState.Valid -> when (actual.head) {
                parent -> writePointer(sessions, ACTIVE, owned, setOf(parent, owned)).asUnit()
                owned -> syncExistingPointer(sessions, ACTIVE, owned)
                else -> blocked("Active lease pointer mismatches claimed transition")
            }
        }
    }

    private fun retryClosedAfterActiveBarrier(
        handle: SkinLaunchHandle,
        tokenSha256: String,
        reason: String,
    ): SkinResult<LeaseHead> {
        val closed = when (val result = loadCurrent(handle.descriptorId, handle.leaseId)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value ?: return blocked("No active lease matches handle")
        }
        if (closed.document.state != LeaseState.CLOSED || !matchesHandle(closed.document, handle, tokenSha256)) {
            return blocked("No active lease matches handle")
        }
        val parent = when (val result = loadParent(closed)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (closed.document.closeReason != reason || closed.document.transitionId != deterministicId("close", parent.head.sha256, reason)) {
            return blocked("Close retry transition does not exactly match")
        }
        return casPointer(sessions, ACTIVE, parent.head, desired = null).mapToHead(closed.head)
    }

    private fun advance(parent: StateRecord, child: LeaseStateDocument): SkinResult.Error? {
        try {
            SkinLeaseStateCodec.requireImmediateChild(child, parent.document)
        } catch (error: Exception) {
            return blocked("Lease transition is invalid: ${error.message}")
        }
        val childHead = SkinLeaseStateCodec.head(child)
        when (val published = publishState(child)) {
            is SkinResult.Error -> return published
            is SkinResult.Ok -> if (published.value.head != childHead) return corrupt("Published child lease head changed")
        }
        val current = when (val result = readPointer(leaseRoot(parent.head.descriptorId), CURRENT)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        when (current) {
            PointerState.Missing, PointerState.Malformed -> return corrupt("Lease current pointer is absent or malformed")
            is PointerState.Valid -> if (current.head != parent.head && current.head != childHead) {
                return blocked("Lease current pointer does not match transition")
            }
        }
        writePointer(leaseRoot(parent.head.descriptorId), NEXT, childHead, setOf(null, childHead))?.let { return it }
        writePointer(
            leaseRoot(parent.head.descriptorId),
            PREVIOUS,
            parent.head,
            existingMustBelongTo = parent.head,
        )?.let { return it }
        writePointer(leaseRoot(parent.head.descriptorId), CURRENT, childHead, setOf(parent.head, childHead))?.let { return it }
        removePointer(leaseRoot(parent.head.descriptorId), NEXT, expected = childHead)?.let { return it }
        return null
    }

    private fun establishZero(head: LeaseHead): SkinResult.Error? {
        val lease = leaseRoot(head.descriptorId)
        val current = when (val result = readPointer(lease, CURRENT)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (current !is PointerState.Missing && (current !is PointerState.Valid || current.head != head)) {
            return blocked("Descriptor already has a different lease state")
        }
        writePointer(lease, NEXT, head, setOf(null, head))?.let { return it }
        removePointer(lease, PREVIOUS, expected = null, permitAnySameLease = head)?.let { return it }
        writePointer(lease, CURRENT, head, setOf(null, head))?.let { return it }
        removePointer(lease, NEXT, expected = head)?.let { return it }
        return null
    }

    private fun publishState(value: LeaseStateDocument): SkinResult<StateRecord> {
        val bytes = try {
            SkinLeaseStateCodec.canonical(value)
        } catch (error: Exception) {
            return blocked("Lease state is invalid: ${error.message}")
        }
        val head = SkinLeaseStateCodec.head(value)
        val stage = File(staging, "lease-${value.descriptorId}-${value.leaseId}-${value.transitionSequence}")
        val destination = stateDirectory(value.descriptorId, value.leaseId, value.transitionSequence)
        try {
            stagingFs.requireContained(stage, root, allowMissingLeaf = true)
            if (stagingFs.exists(stage)) stagingFs.deleteContained(stage, root)
            stagingFs.createDirectory(stage)
            stagingFs.writeNew(File(stage, LEASE_JSON), bytes)
            stagingFs.writeNew(File(stage, LEASE_SHA256), "${head.sha256}\n".toByteArray(StandardCharsets.US_ASCII))
        } catch (error: Exception) {
            return unavailable("Cannot stage lease state: ${error.message}")
        }
        var verified: StateRecord? = null
        return when (val published = publisher.publishDetailed(stage, destination, root) { directory ->
            when (val loaded = loadState(head, directory)) {
                is SkinResult.Error -> loaded
                is SkinResult.Ok -> if (loaded.value.document == value) {
                    verified = loaded.value
                    SkinResult.Ok(Unit)
                } else {
                    corrupt("Published lease state bytes changed")
                }
            }
        }) {
            is SkinResult.Error -> published
            is SkinResult.Ok -> verified?.let { SkinResult.Ok(it) }
                ?: corrupt("Published lease state was not verified")
        }
    }

    private fun loadActive(): SkinResult<StateRecord?> = when (val pointer = readPointer(sessions, ACTIVE)) {
        is SkinResult.Error -> pointer
        is SkinResult.Ok -> when (val value = pointer.value) {
            PointerState.Missing -> SkinResult.Ok(null)
            PointerState.Malformed -> corrupt("Active lease pointer is malformed")
            is PointerState.Valid -> loadState(value.head)
        }
    }

    private fun loadCurrent(descriptorId: UUID, leaseId: UUID): SkinResult<StateRecord?> = when (
        val pointer = readPointer(leaseRoot(descriptorId), CURRENT)
    ) {
        is SkinResult.Error -> pointer
        is SkinResult.Ok -> when (val value = pointer.value) {
            PointerState.Missing -> SkinResult.Ok(null)
            PointerState.Malformed -> corrupt("Lease current pointer is malformed")
            is PointerState.Valid -> {
                if (value.head.descriptorId != descriptorId || value.head.leaseId != leaseId) {
                    corrupt("Lease current pointer crosses descriptor or lease")
                } else {
                    loadState(value.head)
                }
            }
        }
    }

    private fun loadParent(child: StateRecord): SkinResult<StateRecord> {
        if (child.document.transitionSequence == 0L) return corrupt("Transition zero has no parent")
        val directory = stateDirectory(child.document.descriptorId, child.document.leaseId, child.document.transitionSequence - 1L)
        val parent = when (val result = readStateDirectory(directory)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        val verified = when (val result = validateStateChain(parent)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        return try {
            SkinLeaseStateCodec.requireImmediateChild(child.document, verified.document)
            SkinResult.Ok(verified)
        } catch (error: Exception) {
            corrupt("Lease parent relationship is invalid: ${error.message}")
        }
    }

    private fun loadState(expected: LeaseHead, directory: File = stateDirectory(
        expected.descriptorId,
        expected.leaseId,
        expected.transitionSequence,
    )): SkinResult<StateRecord> {
        val record = when (val result = readStateDirectory(directory)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        if (record.head != expected) return corrupt("Lease state does not exactly match pointer")
        return validateStateChain(record)
    }

    private fun validateStateChain(record: StateRecord): SkinResult<StateRecord> {
        if (record.document.transitionSequence == 0L) return SkinResult.Ok(record)
        return when (val parent = loadParent(record)) {
            is SkinResult.Error -> parent
            is SkinResult.Ok -> SkinResult.Ok(record)
        }
    }

    private fun readStateDirectory(
        directory: File,
        enforceCanonicalLocation: Boolean = true,
    ): SkinResult<StateRecord> = try {
        fs.requireContained(directory, root)
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "Lease state is not a no-alias directory" }
        val identity = fs.identity(directory)
        val children = fs.list(directory)
        require(children.size == 3 && children.map(File::getName).toSet() == STATE_FILES) { "Lease state file set is not exact" }
        val complete = File(directory, COMPLETE)
        require(fs.isRegularFile(complete) && !fs.isSymbolicLink(complete) && fs.identity(complete).size == 0L) {
            "Lease state completion marker is invalid"
        }
        val bytes = readStable(File(directory, LEASE_JSON), MAX_STATE_BYTES)
        val digest = parseDigestSidecar(readStable(File(directory, LEASE_SHA256), 65))
            ?: return corrupt("Lease state digest sidecar is invalid")
        require(digest == sha256(bytes)) { "Lease state digest mismatches bytes" }
        val document = when (val parsed = SkinLeaseStateCodec.parse(bytes)) {
            is SkinResult.Error -> return corrupt("Lease state document is invalid: ${parsed.detail}")
            is SkinResult.Ok -> parsed.value
        }
        val head = LeaseHead(document.descriptorId, document.leaseId, document.transitionSequence, document.state, digest)
        val canonicalDirectory = stateDirectory(head.descriptorId, head.leaseId, head.transitionSequence)
        if (enforceCanonicalLocation) {
            require(directory.absoluteFile.normalize() == canonicalDirectory) {
                "Lease state directory does not match its identity"
            }
        } else {
            require(directory.name == canonicalDirectory.name) {
                "Relocated lease state directory does not match its identity"
            }
        }
        require(fs.identity(directory) == identity) { "Lease state directory identity changed" }
        SkinResult.Ok(StateRecord(head, document))
    } catch (error: Exception) {
        corrupt("Invalid lease state: ${error.message}")
    }

    private fun readPointer(parent: File, name: String): SkinResult<PointerState> {
        require(name in POINTER_NAMES)
        return readPointerFile(parent, name)
    }

    private fun writePointer(
        parent: File,
        name: String,
        desired: LeaseHead,
        acceptedExisting: Set<LeaseHead?> = emptySet(),
        existingMustBelongTo: LeaseHead? = null,
    ): SkinResult.Error? {
        return try {
        require(name in POINTER_NAMES)
        ensureDirectory(parent, root)
        val current = when (val result = readPointer(parent, name)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        when (current) {
            PointerState.Malformed -> return corrupt("Lease pointer is malformed")
            is PointerState.Valid -> {
                if (current.head == desired) {
                    return syncExistingPointer(parent, name, desired).errorOrNull()
                }
                if (existingMustBelongTo != null &&
                    (current.head.descriptorId != existingMustBelongTo.descriptorId || current.head.leaseId != existingMustBelongTo.leaseId)
                ) {
                    return blocked("Lease pointer crosses descriptor or lease")
                }
                if (current.head !in acceptedExisting && existingMustBelongTo == null) {
                    return blocked("Lease pointer differs from transition expectation")
                }
            }
            PointerState.Missing -> if (null !in acceptedExisting && existingMustBelongTo == null) {
                return blocked("Lease pointer is absent")
            }
        }
        val file = File(parent, name)
        val temporary = File(parent, "$name.tmp")
        val bytes = LeasePointerCodec.canonical(desired)
        fs.requireContained(temporary, root, allowMissingLeaf = true)
        if (fs.exists(temporary)) {
            require(fs.isRegularFile(temporary) && !fs.isSymbolicLink(temporary)) { "Lease pointer temporary is unsafe" }
            require(readStable(temporary, MAX_POINTER_BYTES).contentEquals(bytes)) { "Lease pointer temporary differs" }
        } else {
            fs.writeNew(temporary, bytes)
        }
        fs.syncFile(temporary)
        fs.atomicMove(temporary, file)
        fs.syncDirectory(parent)
        require(readStable(file, MAX_POINTER_BYTES).contentEquals(bytes)) { "Lease pointer changed after durability barrier" }
        null
        } catch (error: Exception) {
            unavailable("Cannot durably replace $name lease pointer: ${error.message}")
        }
    }

    private fun syncExistingPointer(parent: File, name: String, expected: LeaseHead): SkinResult<Unit> = try {
        val file = File(parent, name)
        require(readStable(file, MAX_POINTER_BYTES).contentEquals(LeasePointerCodec.canonical(expected))) {
            "Lease pointer changed before retry barrier"
        }
        fs.syncFile(file)
        fs.syncDirectory(parent)
        require(readStable(file, MAX_POINTER_BYTES).contentEquals(LeasePointerCodec.canonical(expected))) {
            "Lease pointer changed after retry barrier"
        }
        SkinResult.Ok(Unit)
    } catch (error: Exception) {
        unavailable("Cannot durably confirm $name lease pointer: ${error.message}")
    }

    private fun casPointer(
        parent: File,
        name: String,
        expected: LeaseHead?,
        desired: LeaseHead?,
    ): SkinResult<Unit> {
        val actual = when (val result = readPointer(parent, name)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        when (actual) {
            PointerState.Malformed -> return corrupt("CAS lease pointer is malformed")
            PointerState.Missing -> if (expected != null) {
                if (desired == null) return removePointer(parent, name, expected = expected, acceptMissing = true).asUnit()
                return blocked("CAS lease pointer is absent")
            }
            is PointerState.Valid -> {
                if (actual.head == desired) return syncExistingPointer(parent, name, requireNotNull(desired))
                if (actual.head != expected) return blocked("CAS lease pointer mismatches expected head")
            }
        }
        return if (desired == null) {
            removePointer(parent, name, expected = expected).asUnit()
        } else {
            writePointer(parent, name, desired, acceptedExisting = setOf(expected, desired)).asUnit()
        }
    }

    private fun removePointer(
        parent: File,
        name: String,
        expected: LeaseHead?,
        permitAnySameLease: LeaseHead? = null,
        acceptMissing: Boolean = false,
    ): SkinResult.Error? {
        return try {
        require(name in POINTER_NAMES)
        ensureDirectory(parent, root)
        val value = when (val result = readPointer(parent, name)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        when (value) {
            PointerState.Malformed -> return corrupt("Lease pointer is malformed")
            PointerState.Missing -> {
                if (!acceptMissing && expected != null) return blocked("Lease pointer is absent")
                fs.syncDirectory(parent)
                return null
            }
            is PointerState.Valid -> {
                if (expected != null && value.head != expected) return blocked("Lease pointer differs from expected head")
                if (permitAnySameLease != null &&
                    (value.head.descriptorId != permitAnySameLease.descriptorId || value.head.leaseId != permitAnySameLease.leaseId)
                ) {
                    return blocked("Lease pointer crosses descriptor or lease")
                }
            }
        }
        val file = File(parent, name)
        fs.requireContained(file, root)
        fs.deleteContained(file, root)
        fs.syncDirectory(parent)
        null
        } catch (error: Exception) {
            unavailable("Cannot durably remove $name lease pointer: ${error.message}")
        }
    }

    private fun ensureLayout(): SkinResult.Error? = try {
        require(fs.exists(root) && fs.isDirectory(root) && !fs.isSymbolicLink(root)) {
            "Skin root is not an existing no-alias directory"
        }
        fs.requireContained(root, root)
        ensureDirectory(sessions, root)
        ensureDirectory(staging, root)
        null
    } catch (error: Exception) {
        unavailable("Cannot establish session layout: ${error.message}")
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
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "Unsafe session directory" }
    }

    private fun readStable(file: File, maximum: Int): ByteArray {
        fs.requireContained(file, root)
        require(fs.isRegularFile(file) && !fs.isSymbolicLink(file)) { "Expected no-alias regular file" }
        val before = fs.identity(file)
        require(before.regularFile && before.size in 0..maximum.toLong()) { "Lease file exceeds bound" }
        val output = ByteArrayOutputStream(before.size.toInt())
        fs.openNoFollow(file).use { input ->
            val buffer = ByteArray(8192)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                if (count == 0) continue
                require(output.size() + count <= maximum) { "Lease file exceeds bound" }
                output.write(buffer, 0, count)
            }
        }
        require(fs.identity(file) == before) { "Lease file identity changed while read" }
        return output.toByteArray()
    }

    private fun matchesHandle(document: LeaseStateDocument, handle: SkinLaunchHandle, tokenSha256: String): Boolean =
        document.leaseId == handle.leaseId && document.leaseTokenSha256 == tokenSha256 &&
            document.descriptorId == handle.descriptorId && document.descriptorSha256 == handle.descriptorSha256 &&
            document.sessionSequence == handle.sessionSequence && document.profileId == PROFILE_ID

    private fun validateHandle(handle: SkinLaunchHandle): String? = try {
        require(handle.sessionSequence >= 0L) { "Negative session sequence" }
        require(handle.descriptorSha256.matches(DIGEST)) { "Invalid descriptor digest" }
        require(handle.descriptorPath == "sessions/${handle.descriptorId}/descriptor.json") { "Descriptor path mismatches descriptor ID" }
        SkinLeaseStateCodec.rawTokenSha256(handle.leaseToken)
    } catch (_: Exception) {
        null
    }

    private fun validOwner(owner: ProcessIdentity): Boolean = SkinLeaseStateCodec.validOwner(owner)

    private fun validReason(value: String): Boolean = CLOSE_REASON.matches(value)

    private fun stateDirectory(descriptorId: UUID, leaseId: UUID, sequence: Long): File {
        require(sequence in 0L..2L && sequence.toString().length <= 20) { "Lease transition sequence is unbounded" }
        return File(File(leaseRoot(descriptorId), "states"), "ls-${sequence.toString().padStart(20, '0')}-$leaseId")
    }

    private fun leaseRoot(descriptorId: UUID): File = File(File(sessions, descriptorId.toString()), "lease")

    private fun deterministicId(vararg parts: String): UUID = UUID.nameUUIDFromBytes(
        parts.joinToString(separator = "\u0000").toByteArray(StandardCharsets.US_ASCII),
    )

    private fun ownerKey(owner: ProcessIdentity): String = "${owner.uid}:${owner.pid}:${owner.processStartToken}"

    private fun parseDigestSidecar(bytes: ByteArray): String? {
        if (bytes.size != 65 || bytes.last() != '\n'.code.toByte() || bytes.any { (it.toInt() and 0xff) > 0x7f }) return null
        val value = bytes.copyOf(64).toString(StandardCharsets.US_ASCII)
        return value.takeIf { DIGEST.matches(it) }
    }

    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

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
        unavailable("Cannot acquire ordered skin locks: ${error.message}")
    }

    private fun SkinResult<Unit>.mapToHead(head: LeaseHead): SkinResult<LeaseHead> = when (this) {
        is SkinResult.Error -> this
        is SkinResult.Ok -> SkinResult.Ok(head)
    }

    private fun SkinResult<Unit>.errorOrNull(): SkinResult.Error? = when (this) {
        is SkinResult.Error -> this
        is SkinResult.Ok -> null
    }

    private fun SkinResult.Error?.asUnit(): SkinResult<Unit> = this ?: SkinResult.Ok(Unit)

    private fun blocked(detail: String) = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, detail)
    private fun corrupt(detail: String) = SkinResult.Error(SkinImportCode.SESSION_RECOVERY_AMBIGUOUS, detail)
    private fun unavailable(detail: String) = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, detail)

    private data class RecoveryOutcome(val head: LeaseHead?, val gate: LeaseMutationGate)

    private data class StructuralRecoveryPlan(
        val descriptors: List<DescriptorRecoveryPlan>,
        val active: StateRecord?,
        val sessionsPresent: Boolean,
        val sequence: SessionSequencePlan,
        val acquisition: AcquisitionRecoveryPlan,
        val orphanedAcquisition: OrphanedAcquisition?,
        val tombstonedAcquisition: OrphanedAcquisition?,
    )

    private data class OrphanedAcquisition(
        val directory: File,
        val descriptor: SkinLaunchDescriptor,
        val pending: StateRecord?,
    )

    private data class AcquisitionRecoveryPlan(
        val durable: SkinAcquisitionIntent?,
        val temporary: SkinAcquisitionIntent?,
    ) {
        val effective: SkinAcquisitionIntent? get() = temporary ?: durable
    }

    private data class SessionSequencePlan(
        val durable: Long?,
        val temporary: Long?,
        val effective: Long?,
    )

    private data class DescriptorRecoveryPlan(
        val records: List<StateRecord>,
        val head: StateRecord,
        val pointers: ResolvedPointers,
        val leaseDirectory: File,
        val installNextBeforeRepair: Boolean,
        val previousFallbackRepair: PreviousFallbackRepair?,
        val sessionSequence: Long,
    )

    private data class LeaseRecoveryPlan(
        val records: List<StateRecord>,
        val head: StateRecord,
        val pointers: ResolvedPointers,
        val installNextBeforeRepair: Boolean,
        val previousFallbackRepair: PreviousFallbackRepair?,
    )

    private data class PreviousFallbackIntent(
        val head: LeaseHead,
        val previous: LeaseHead?,
    )

    private data class PreviousFallbackRepair(
        val intent: PreviousFallbackIntent,
    )

    private data class PointerRepair(
        val name: String,
        val actual: LeaseHead?,
        val desired: LeaseHead?,
    )

    private data class ResolvedPointers(
        val current: StateRecord?,
        val previous: StateRecord?,
        val next: StateRecord?,
    )

    private class RecoveryEvidenceException(message: String) : IllegalStateException(message)

    private data class StateRecord(val head: LeaseHead, val document: LeaseStateDocument)

    private sealed interface PointerState {
        data object Missing : PointerState
        data object Malformed : PointerState
        data class Valid(val head: LeaseHead) : PointerState
    }

    private companion object {
        const val PROFILE_ID = "hollow-knight"
        const val SCHEMA_VERSION = 1
        const val ACTIVE = "active"
        const val ACQUISITION_INTENT = "acquisition.intent"
        const val ACQUISITION_TOMBSTONE_PREFIX = "recovery-acquisition-"
        const val SEQUENCE = "sequence"
        const val CURRENT = "current"
        const val PREVIOUS = "previous"
        const val NEXT = "next"
        const val LEASE_JSON = "lease.json"
        const val LEASE_SHA256 = "lease.sha256"
        const val DESCRIPTOR_JSON = "descriptor.json"
        const val DESCRIPTOR_SHA256 = "descriptor.sha256"
        const val LEASE_DIRECTORY = "lease"
        const val STATES_DIRECTORY = "states"
        const val REPAIR_INTENT = "repair.intent"
        const val REPAIR_INTENT_HEADER = "PREVIOUS_ONLY_V1"
        const val COMPLETE = ".complete"
        const val RECOVERY_LAUNCHER_DEAD = "RECOVERY_LAUNCHER_DEAD"
        const val RECOVERY_GAME_OWNER_DEAD = "RECOVERY_GAME_OWNER_DEAD"
        const val MAX_RECOVERY_DESCRIPTORS = 64
        const val MAX_RECOVERY_STATES = 3
        const val MAX_DESCRIPTOR_BYTES = 8 * 1024 * 1024
        const val MAX_STATE_BYTES = 64 * 1024
        const val MAX_POINTER_BYTES = 256
        const val MAX_SEQUENCE_BYTES = 20
        const val MAX_ACQUISITION_INTENT_BYTES = 1024
        const val MAX_REPAIR_INTENT_BYTES = 1024
        val STATE_FILES = setOf(LEASE_JSON, LEASE_SHA256, COMPLETE)
        val POINTER_NAMES = setOf(ACTIVE, CURRENT, PREVIOUS, NEXT)
        val POINTER_FILES = POINTER_NAMES + POINTER_NAMES.map { "$it.tmp" }
        val SESSION_FIXED_FILES = setOf(
            ACTIVE,
            "$ACTIVE.tmp",
            SEQUENCE,
            "$SEQUENCE.tmp",
            ACQUISITION_INTENT,
            "$ACQUISITION_INTENT.tmp",
        )
        val ACQUISITION_DESCRIPTOR_FILES = setOf(DESCRIPTOR_JSON, DESCRIPTOR_SHA256, COMPLETE)
        val DESCRIPTOR_DIRECTORY_FILES = ACQUISITION_DESCRIPTOR_FILES + LEASE_DIRECTORY
        val LEASE_DIRECTORY_FILES = setOf(
            STATES_DIRECTORY,
            CURRENT,
            PREVIOUS,
            NEXT,
            "$CURRENT.tmp",
            "$PREVIOUS.tmp",
            "$NEXT.tmp",
            REPAIR_INTENT,
            "$REPAIR_INTENT.tmp",
        )
        val DIGEST = Regex("[0-9a-f]{64}")
        val UNSIGNED_DECIMAL = Regex("0|[1-9][0-9]*")
        val UUID_TEXT = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
        val CLOSE_REASON = Regex("[A-Z][A-Z0-9_]{0,127}")
    }
}

/** Revalidates every staging operation through the caller-supplied no-alias security authority. */
internal class SkinSessionStagingFileSystem(
    private val delegate: SkinFileSystem,
    profileRoot: File,
    stagingRoot: File,
) : SkinFileSystem, SkinFileSystemSecurity {
    private val security = delegate as? SkinFileSystemSecurity
        ?: throw IllegalArgumentException("Skin filesystem security capability is unavailable")
    private val profile = profileRoot.absoluteFile.normalize()
    private val staging = stagingRoot.absoluteFile.normalize()

    override fun createDirectory(path: File) {
        revalidate(path, allowMissingLeaf = true)
        delegate.createDirectory(path)
    }

    override fun writeNew(path: File, bytes: ByteArray) {
        revalidate(path, allowMissingLeaf = true)
        delegate.writeNew(path, bytes)
    }

    override fun syncFile(path: File) {
        revalidate(path, allowMissingLeaf = false)
        delegate.syncFile(path)
    }

    override fun syncDirectory(path: File) {
        revalidate(path, allowMissingLeaf = false)
        delegate.syncDirectory(path)
    }

    override fun atomicMove(source: File, target: File) {
        revalidate(source, allowMissingLeaf = false)
        revalidate(target, allowMissingLeaf = true)
        delegate.atomicMove(source, target)
    }

    override fun openNoFollow(path: File): InputStream {
        revalidate(path, allowMissingLeaf = false)
        return delegate.openNoFollow(path)
    }

    override fun identity(path: File): SkinNodeIdentity {
        revalidate(path, allowMissingLeaf = false)
        return delegate.identity(path)
    }

    override fun list(path: File): List<File> {
        revalidate(path, allowMissingLeaf = false)
        return delegate.list(path)
    }

    override fun deleteContained(path: File, owner: File) {
        revalidate(path, allowMissingLeaf = false)
        delegate.deleteContained(path, owner)
    }

    override fun exists(file: File): Boolean {
        revalidate(file, allowMissingLeaf = true)
        return security.exists(file)
    }

    override fun isDirectory(file: File): Boolean {
        revalidate(file, allowMissingLeaf = false)
        return security.isDirectory(file)
    }

    override fun isRegularFile(file: File): Boolean {
        revalidate(file, allowMissingLeaf = false)
        return security.isRegularFile(file)
    }

    override fun isSymbolicLink(file: File): Boolean {
        revalidate(file, allowMissingLeaf = false)
        return security.isSymbolicLink(file)
    }

    override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
        security.requireContained(path, owner, allowMissingLeaf)
    }

    override fun sameFile(left: File, right: File): Boolean {
        revalidate(left, allowMissingLeaf = false)
        revalidate(right, allowMissingLeaf = false)
        return security.sameFile(left, right)
    }

    override fun openOutput(file: File, createNew: Boolean): OutputStream {
        revalidate(file, allowMissingLeaf = createNew)
        return security.openOutput(file, createNew)
    }

    override fun openSeekableNoFollow(file: File): SeekableByteChannel {
        revalidate(file, allowMissingLeaf = false)
        return security.openSeekableNoFollow(file)
    }

    private fun revalidate(path: File, allowMissingLeaf: Boolean) {
        val target = path.absoluteFile.normalize()
        val stage = stagingDirectory(target) ?: return
        security.requireContained(target, profile, allowMissingLeaf)
        if (stage != target) {
            security.requireContained(stage, profile)
            require(security.exists(stage) && security.isDirectory(stage) && !security.isSymbolicLink(stage)) {
                "Lease staging directory is unsafe"
            }
            security.requireContained(target, profile, allowMissingLeaf)
        }
    }

    private fun stagingDirectory(path: File): File? {
        if (!path.toPath().startsWith(staging.toPath()) || path == staging) return null
        val relative = staging.toPath().relativize(path.toPath())
        if (relative.nameCount == 0) return null
        return File(staging, relative.getName(0).toString())
    }
}

internal object LeasePointerCodec {
    private val DIGEST = Regex("[0-9a-f]{64}")
    private val UUID_TEXT = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")

    fun canonical(head: LeaseHead): ByteArray {
        require(head.transitionSequence in 0L..2L && head.transitionSequence.toString().length <= 20)
        require(UUID_TEXT.matches(head.descriptorId.toString()) && UUID_TEXT.matches(head.leaseId.toString()))
        require(DIGEST.matches(head.sha256))
        return buildString {
            append(head.descriptorId)
            append('\n')
            append(head.leaseId)
            append('\n')
            append(head.transitionSequence.toString().padStart(20, '0'))
            append('\n')
            append(head.state.name)
            append('\n')
            append(head.sha256)
            append('\n')
        }.toByteArray(StandardCharsets.US_ASCII)
    }

    fun parse(bytes: ByteArray): LeaseHead? {
        return try {
        if (bytes.size > 256 || bytes.any { (it.toInt() and 0xff) > 0x7f }) return null
        val lines = bytes.toString(StandardCharsets.US_ASCII).split('\n')
        if (lines.size != 6 || lines.last().isNotEmpty()) return null
        val descriptor = lines[0]
        val lease = lines[1]
        val sequence = lines[2]
        val state = enumValues<LeaseState>().singleOrNull { it.name == lines[3] } ?: return null
        if (!UUID_TEXT.matches(descriptor) || !UUID_TEXT.matches(lease) || !DIGEST.matches(lines[4])) return null
        val descriptorId = UUID.fromString(descriptor)
        val leaseId = UUID.fromString(lease)
        if (descriptorId.toString() != descriptor || leaseId.toString() != lease || !Regex("[0-9]{20}").matches(sequence)) return null
        val value = sequence.toLong()
        if (value !in 0L..2L || value.toString().padStart(20, '0') != sequence) return null
        LeaseHead(descriptorId, leaseId, value, state, lines[4]).also {
            if (!canonical(it).contentEquals(bytes)) return null
        }
        } catch (_: Exception) {
            null
        }
    }
}
