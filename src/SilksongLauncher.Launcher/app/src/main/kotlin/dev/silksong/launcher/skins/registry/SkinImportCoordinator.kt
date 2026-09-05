package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.BuiltSkin
import dev.silksong.launcher.skins.contracts.CandidatePreparationResult
import dev.silksong.launcher.skins.contracts.PreparedSkinCandidate
import dev.silksong.launcher.skins.contracts.PublishedSkin
import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.importing.SkinNormalizer
import dev.silksong.launcher.skins.importing.SkinObjectBuilder
import dev.silksong.launcher.skins.importing.SkinQuarantine
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaBudgets
import dev.silksong.launcher.skins.quota.SkinQuotaReservation
import dev.silksong.launcher.skins.session.LeaseMutationGate
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinObjectPublisher
import dev.silksong.launcher.skins.storage.SkinPaths
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.listBounded
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.util.UUID


data class CandidatePreparationSummary(
    val rawPrefixHex: String,
    val candidateKey: String?,
    val name: String?,
    val code: SkinImportCode,
    val detail: String,
)

data class SkinPreparationHandle(
    val handleId: UUID,
    val candidates: List<CandidatePreparationSummary>,
)

internal enum class SkinPreparationHandleState {
    OPEN,
    CLAIMED,
    CLOSED,
}

internal class SkinPreparationRecord(
    val handle: SkinPreparationHandle,
    val stagingOwner: File,
    private var state: SkinPreparationHandleState,
    private val retained: List<CandidatePreparationResult>,
) {
    @Synchronized
    fun claim(): SkinResult<List<CandidatePreparationResult>> = when (state) {
        SkinPreparationHandleState.OPEN -> {
            state = SkinPreparationHandleState.CLAIMED
            SkinResult.Ok(retained)
        }
        SkinPreparationHandleState.CLAIMED,
        SkinPreparationHandleState.CLOSED,
        -> blocked("Skin preparation handle is not open")
    }

    @Synchronized
    fun cancel(): SkinResult<File> = when (state) {
        SkinPreparationHandleState.OPEN -> {
            state = SkinPreparationHandleState.CLOSED
            SkinResult.Ok(stagingOwner)
        }
        SkinPreparationHandleState.CLAIMED,
        SkinPreparationHandleState.CLOSED,
        -> blocked("Skin preparation handle is not open")
    }

    @Synchronized
    fun closeClaimed() {
        require(state == SkinPreparationHandleState.CLAIMED) { "Only a claimed handle can close" }
        state = SkinPreparationHandleState.CLOSED
    }

    @Synchronized
    fun isActive(): Boolean = state != SkinPreparationHandleState.CLOSED

    private fun blocked(detail: String) = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, detail)
}

data class CandidateImportResult(
    val rawPrefix: ByteArray,
    val code: SkinImportCode,
    val published: PublishedSkin?,
    val detail: String,
)

internal enum class SkinHandleDisposition { RETAINED, CLEANED, CLEANUP_PENDING }
internal fun interface SkinHandleCleanupRetry { fun retry(): SkinResult<Unit> }
internal data class SkinHandleOperation<T>(
    val result: SkinResult<T>,
    val disposition: SkinHandleDisposition,
    val cleanupRetry: SkinHandleCleanupRetry? = null,
)

/** Internal operation boundary used only inside a scoped coordinator binding. */
internal interface SkinImportCoordinatorOperations {
    fun mutationGate(): LeaseMutationGate
    fun normalize(archive: QuarantinedArchive): SkinResult<List<CandidatePreparationResult>>
    fun verify(prepared: PreparedSkinCandidate): SkinResult<Unit>
    fun build(prepared: PreparedSkinCandidate, id: String): SkinResult<BuiltSkin>
    fun discard(built: BuiltSkin): SkinResult<Unit>
    fun publish(built: BuiltSkin): SkinResult<PublishedSkin>
    fun recoverRegistry(): SkinResult<RegistryHead>
    fun commitRegistry(expected: RegistryHead, operationId: UUID, mutation: RegistryMutation): SkinResult<RegistryHead>
    fun referenceSnapshot(): SkinResult<Set<String>>
    fun discardUnreferenced(published: PublishedSkin, referencedDigests: Set<String>): SkinResult<Unit>
    fun settlePublications(referencedDigests: Set<String>): SkinResult<Unit>
}

internal class BoundSkinImportCoordinatorOperations(
    private val gate: () -> LeaseMutationGate,
    private val normalizer: SkinNormalizer,
    private val builder: SkinObjectBuilder,
    private val publisher: SkinObjectPublisher,
    private val registryStore: SkinRegistryStore,
) : SkinImportCoordinatorOperations {
    override fun mutationGate(): LeaseMutationGate = gate()
    override fun normalize(archive: QuarantinedArchive): SkinResult<List<CandidatePreparationResult>> =
        normalizer.prepare(archive)
    override fun verify(prepared: PreparedSkinCandidate): SkinResult<Unit> = builder.verifyPrepared(prepared)
    override fun build(prepared: PreparedSkinCandidate, id: String): SkinResult<BuiltSkin> = builder.build(prepared, id)
    override fun discard(built: BuiltSkin): SkinResult<Unit> = builder.discard(built)
    override fun publish(built: BuiltSkin): SkinResult<PublishedSkin> = publisher.publish(built)
    override fun recoverRegistry(): SkinResult<RegistryHead> = registryStore.recoverForCoordinator()
    override fun commitRegistry(
        expected: RegistryHead,
        operationId: UUID,
        mutation: RegistryMutation,
    ): SkinResult<RegistryHead> = registryStore.commitAdmittedForCoordinator(
        expected,
        operationId,
        "import-coordinator",
        mutation,
    )
    override fun referenceSnapshot(): SkinResult<Set<String>> = registryStore.referenceSnapshotForCoordinator()
    override fun discardUnreferenced(
        published: PublishedSkin,
        referencedDigests: Set<String>,
    ): SkinResult<Unit> = publisher.discardUnreferenced(published, referencedDigests)
    override fun settlePublications(referencedDigests: Set<String>): SkinResult<Unit> =
        publisher.recoverOwnedPublications(referencedDigests)
}

internal data class SkinImportCoordinatorDependencies(
    val paths: SkinPaths,
    val fileSystem: SkinFileSystem,
    val lockManager: SkinLockManager,
    val quota: SkinQuotaAdmission,
    val quarantine: SkinQuarantine,
    val operations: SkinImportCoordinatorOperations,
) {
    internal val handleStaging = SkinImportHandleStaging(paths, fileSystem)

    init {
        require(paths.root.absoluteFile.normalize() == lockManager.root) { "Coordinator lock authority uses another profile" }
        require(paths.root.absoluteFile.normalize() == quota.root.absoluteFile.normalize()) {
            "Coordinator quota authority uses another profile"
        }
    }
}

/**
 * Process-only authority for import preparation records. There is deliberately no production binding or caller yet.
 */
object SkinImportCoordinator {
    private val monitor = Any()
    private var dependencies: SkinImportCoordinatorDependencies? = null
    private var startupRecovered = false
    private var bindingEpoch = Any()
    private var activeOperations = 0
    private val records = linkedMapOf<UUID, SkinPreparationRecord>()

    internal fun <T> withTestBinding(binding: SkinImportCoordinatorDependencies, action: () -> T): T {
        synchronized(monitor) {
            require(dependencies == null && activeOperations == 0 && records.isEmpty()) {
                "Skin import coordinator is already bound or retains process records"
            }
            dependencies = binding
            bindingEpoch = Any()
            startupRecovered = false
        }
        return try {
            action()
        } finally {
            synchronized(monitor) {
                require(activeOperations == 0) { "Skin import coordinator operation is still active" }
                require(records.values.none(SkinPreparationRecord::isActive)) {
                    "Skin import coordinator binding retains an open or claimed handle"
                }
                records.clear()
                startupRecovered = false
                dependencies = null
            }
        }
    }

    fun recoverOrphansOnProcessStart(): SkinResult<Int> = withBoundOperation { binding ->
        withGate(binding) {
            if (isStartupRecovered()) return@withGate SkinResult.Ok(0)
            when (val recovered = recoverOrphansLocked(binding)) {
                is SkinResult.Error -> recovered
                is SkinResult.Ok -> {
                    markStartupRecovered()
                    recovered
                }
            }
        }
    }

    fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle> = withBoundOperation { binding ->
        withGateAndRecovery(binding) {
            prepareLocked(binding, input)
        }
    }

    fun commitImport(handleId: UUID): SkinResult<List<CandidateImportResult>> = commitImportWithOwnership(handleId).result

    internal fun commitImportWithOwnership(handleId: UUID): SkinHandleOperation<List<CandidateImportResult>> = observeHandle { terminal ->
        withBoundOperation { binding ->
            withGateAndRecovery(binding) {
                val record = record(handleId) ?: return@withGateAndRecovery blocked("Unknown skin preparation handle")
                val retained = when (val claim = record.claim()) {
                    is SkinResult.Error -> return@withGateAndRecovery claim
                    is SkinResult.Ok -> claim.value
                }
                finishClaimed(binding, record, terminal) { commitImportLocked(binding, retained) }
            }
        }
    }

    fun commitReplace(
        handleId: UUID,
        sourceCandidateKey: String,
        targetId: String,
        expectedGenerationSha256: String,
        expectedTree: String,
        expectedReceipt: String,
    ): SkinResult<CandidateImportResult> = commitReplaceWithOwnership(handleId, sourceCandidateKey, targetId,
        expectedGenerationSha256, expectedTree, expectedReceipt).result

    internal fun commitReplaceWithOwnership(
        handleId: UUID,
        sourceCandidateKey: String,
        targetId: String,
        expectedGenerationSha256: String,
        expectedTree: String,
        expectedReceipt: String,
    ): SkinHandleOperation<CandidateImportResult> = observeHandle { terminal ->
        withBoundOperation { binding ->
            withGateAndRecovery(binding) {
                val record = record(handleId) ?: return@withGateAndRecovery blocked("Unknown skin preparation handle")
                val retained = when (val claim = record.claim()) {
                    is SkinResult.Error -> return@withGateAndRecovery claim
                    is SkinResult.Ok -> claim.value
                }
                finishClaimed(binding, record, terminal) {
                    commitReplaceLocked(binding, retained, sourceCandidateKey, targetId,
                        expectedGenerationSha256, expectedTree, expectedReceipt)
                }
            }
        }
    }

    fun cancel(handleId: UUID): SkinResult<Unit> = cancelWithOwnership(handleId).result

    internal fun cancelWithOwnership(handleId: UUID): SkinHandleOperation<Unit> = observeHandle { terminal ->
        withBoundOperation { binding ->
            withGateAndRecovery(binding) {
                val record = record(handleId) ?: return@withGateAndRecovery blocked("Unknown skin preparation handle")
                when (val cancelled = record.cancel()) {
                    is SkinResult.Error -> cancelled
                    is SkinResult.Ok -> {
                        val retry = cleanupRetry(binding, handleId)
                        val outcome = cleanupOwner(binding, cancelled.value)
                        forgetTerminal(record)
                        terminal(outcome, retry)
                        outcome
                    }
                }
            }
        }
    }

    private fun prepareLocked(
        binding: SkinImportCoordinatorDependencies,
        input: SkinImportInput,
    ): SkinResult<SkinPreparationHandle> {
        val reservation = when (
            val admitted = binding.quota.reserve(SkinQuotaBudgets.IMPORT_PREPARATION)
        ) {
            is SkinResult.Error -> return admitted
            is SkinResult.Ok -> admitted.value
        }
        var owner: File? = null
        var transferred = false
        var outcome: SkinResult<SkinPreparationHandle>? = null
        try {
            val handleId = UUID.randomUUID()
            owner = binding.handleStaging.createOwner(handleId)
            val archive = when (val copied = binding.quarantine.copy(input, owner)) {
                is SkinResult.Error -> throw PreparationFailure(copied)
                is SkinResult.Ok -> copied.value
            }
            val retained = when (val normalized = binding.operations.normalize(archive)) {
                is SkinResult.Error -> throw PreparationFailure(normalized)
                is SkinResult.Ok -> normalized.value
            }
            if (retained.size > SkinLimits.V1.candidates) {
                throw PreparationFailure(
                    SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Prepared candidate count exceeds the V1 bound"),
                )
            }
            val summaries = retained.map { summary(it, binding, input.displayName) }
            val handle = SkinPreparationHandle(handleId, summaries)
            val actualLengths = binding.handleStaging.measureRegularFiles(owner)
            reservation.transfer(owner, SkinQuotaBudgets.importStaging(actualLengths))
            transferred = true
            synchronized(monitor) {
                check(dependencies === binding) { "Coordinator binding changed during prepare" }
                check(records.putIfAbsent(handleId, SkinPreparationRecord(handle, owner, SkinPreparationHandleState.OPEN, retained)) == null) {
                    "Random handle UUID collided"
                }
            }
            outcome = SkinResult.Ok(handle)
        } catch (failure: PreparationFailure) {
            outcome = failure.error
        } catch (error: Exception) {
            outcome = unavailable("Skin preparation failed: ${error.message}")
        } finally {
            if (outcome !is SkinResult.Ok) {
                owner?.let { owned ->
                    when (val cleanup = cleanupOwner(binding, owned)) {
                        is SkinResult.Error -> outcome = cleanup
                        is SkinResult.Ok -> Unit
                    }
                }
            }
            if (!transferred) {
                try {
                    reservation.release()
                } catch (error: Exception) {
                    outcome = unavailable("Preparation quota release is ambiguous: ${error.message}")
                }
            }
        }
        return requireNotNull(outcome)
    }

    private fun commitImportLocked(
        binding: SkinImportCoordinatorDependencies,
        retained: List<CandidatePreparationResult>,
    ): SkinResult<List<CandidateImportResult>> {
        val results = mutableListOf<CandidateImportResult>()
        var head: RegistryHead? = null
        for (candidateResult in retained) {
            when (candidateResult) {
                is CandidatePreparationResult.Rejected -> results += CandidateImportResult(
                    candidateResult.rawPrefix.copyOf(),
                    candidateResult.code,
                    null,
                    candidateResult.detail,
                )
                is CandidatePreparationResult.Ready -> {
                    val candidate = candidateResult.candidate
                    when (val verified = binding.operations.verify(candidate)) {
                        is SkinResult.Error -> {
                            if (isOuterFailure(verified.code)) return verified
                            results += candidateFailure(candidate, verified)
                            continue
                        }
                        is SkinResult.Ok -> Unit
                    }
                    val reservation = when (val admitted = reserveCandidate(binding, candidate)) {
                        is SkinResult.Error -> {
                            if (admitted.code == SkinImportCode.PROFILE_QUOTA_EXCEEDED) return admitted
                            results += candidateFailure(candidate, admitted)
                            continue
                        }
                        is SkinResult.Ok -> admitted.value
                    }
                    try {
                        val current = head ?: when (val recovered = binding.operations.recoverRegistry()) {
                            is SkinResult.Error -> return recovered
                            is SkinResult.Ok -> recovered.value
                        }
                        val owners = current.document.packs.filter { it.candidateKey == candidate.candidateKey }
                        if (owners.size > 1) return corrupt("Candidate key has duplicate registry owners")
                        val owner = owners.singleOrNull()
                        val id = owner?.id ?: "local-${candidate.candidateKey.take(58)}"
                        val imported = importCandidateAdmitted(binding, current, candidate, id, owner)
                        when (imported) {
                            is CandidateCommit.OuterFailure -> return imported.error
                            is CandidateCommit.Result -> {
                                results += imported.result
                                head = imported.head ?: current
                            }
                        }
                    } finally {
                        reservation.release()
                    }
                }
            }
        }
        return SkinResult.Ok(results)
    }

    private fun commitReplaceLocked(
        binding: SkinImportCoordinatorDependencies,
        retained: List<CandidatePreparationResult>,
        sourceCandidateKey: String,
        targetId: String,
        expectedGenerationSha256: String,
        expectedTree: String,
        expectedReceipt: String,
    ): SkinResult<CandidateImportResult> {
        if (!DIGEST.matches(sourceCandidateKey) || !ID.matches(targetId) ||
            !DIGEST.matches(expectedGenerationSha256) || !DIGEST.matches(expectedTree) || !DIGEST.matches(expectedReceipt)
        ) {
            return invalid("Replace request identity is malformed")
        }
        val matching = retained.filterIsInstance<CandidatePreparationResult.Ready>()
            .filter { it.candidate.candidateKey == sourceCandidateKey }
        if (matching.isEmpty()) return SkinResult.Error(SkinImportCode.NO_CANDIDATE, "Replace source candidate is absent")
        if (matching.size != 1) return invalid("Replace source candidate is duplicated")
        val candidate = matching.single().candidate
        val reservation = when (val admitted = reserveCandidate(binding, candidate)) {
            is SkinResult.Error -> return admitted
            is SkinResult.Ok -> admitted.value
        }
        return try {
            val head = when (val recovered = binding.operations.recoverRegistry()) {
                is SkinResult.Error -> return recovered
                is SkinResult.Ok -> recovered.value
            }
            if (head.sha256 != expectedGenerationSha256) return conflict("Replace registry generation changed")
            val target = head.document.packs.singleOrNull { it.id == targetId }
                ?: return conflict("Replace target is absent")
            if (target.treeSha256 != expectedTree || target.importReceiptSha256 != expectedReceipt) {
                return conflict("Replace target changed")
            }
            if (sourceCandidateKey == target.candidateKey) {
                return SkinResult.Error(
                    SkinImportCode.CANDIDATE_ALREADY_INSTALLED,
                    "Replace requires a new source candidate key; use ordinary import for the exact owner",
                )
            }
            val otherOwner = head.document.packs.singleOrNull { it.candidateKey == sourceCandidateKey && it.id != targetId }
            if (otherOwner != null) {
                return SkinResult.Error(
                    SkinImportCode.CANDIDATE_ALREADY_INSTALLED,
                    "Replace source candidate is already owned by ${otherOwner.id}",
                )
            }
            when (val verified = binding.operations.verify(candidate)) {
                is SkinResult.Error -> return if (isOuterFailure(verified.code)) verified
                else SkinResult.Ok(candidateFailure(candidate, verified))
                is SkinResult.Ok -> Unit
            }
            when (
                val result = publishAndCommit(
                    binding,
                    head,
                    candidate,
                    targetId,
                    replace = Triple(targetId, expectedTree, expectedReceipt),
                )
            ) {
                is CandidateCommit.OuterFailure -> result.error
                is CandidateCommit.Result -> SkinResult.Ok(result.result)
            }
        } finally {
            reservation.release()
        }
    }

    private fun importCandidateAdmitted(
        binding: SkinImportCoordinatorDependencies,
        head: RegistryHead,
        candidate: PreparedSkinCandidate,
        id: String,
        owner: RegistryPack?,
    ): CandidateCommit {
        val built = when (val result = binding.operations.build(candidate, id)) {
            is SkinResult.Error -> {
                if (isOuterFailure(result.code)) return CandidateCommit.OuterFailure(result)
                return CandidateCommit.Result(candidateFailure(candidate, result), null)
            }
            is SkinResult.Ok -> result.value
        }
        if (owner != null) {
            val result = if (owner.treeSha256 == built.treeSha256 && owner.contentSha256 == built.contentSha256) {
                CandidateImportResult(
                    candidate.rawPrefix.copyOf(),
                    SkinImportCode.OK,
                    PublishedSkin(
                        owner.id,
                        owner.candidateKey,
                        owner.name,
                        owner.contentSha256,
                        owner.treeSha256,
                        built.manifestSha256,
                        owner.importReceiptSha256,
                        binding.paths.objectRoot(owner.treeSha256),
                        emptyList(),
                    ),
                    "Candidate is already installed by its exact owner",
                )
            } else {
                CandidateImportResult(
                    candidate.rawPrefix.copyOf(),
                    SkinImportCode.REIMPORT_CHANGED,
                    null,
                    "Candidate owner content changed",
                )
            }
            when (val discarded = binding.operations.discard(built)) {
                is SkinResult.Error -> return CandidateCommit.OuterFailure(discarded)
                is SkinResult.Ok -> Unit
            }
            return CandidateCommit.Result(result, null)
        }
        return publishAndCommit(binding, head, candidate, id, built = built)
    }

    private fun publishAndCommit(
        binding: SkinImportCoordinatorDependencies,
        head: RegistryHead,
        candidate: PreparedSkinCandidate,
        id: String,
        replace: Triple<String, String, String>? = null,
        built: BuiltSkin? = null,
    ): CandidateCommit {
        val actualBuilt = built ?: when (val result = binding.operations.build(candidate, id)) {
            is SkinResult.Error -> return if (isOuterFailure(result.code)) CandidateCommit.OuterFailure(result)
            else CandidateCommit.Result(candidateFailure(candidate, result), null)
            is SkinResult.Ok -> result.value
        }
        val published = when (val result = binding.operations.publish(actualBuilt)) {
            is SkinResult.Error -> return if (isOuterFailure(result.code)) CandidateCommit.OuterFailure(result)
            else CandidateCommit.Result(candidateFailure(candidate, result), null)
            is SkinResult.Ok -> result.value
        }
        val mutation = if (replace == null) {
            SkinRegistryMutations().install(published)
        } else {
            SkinRegistryMutations().replace(replace.first, replace.second, replace.third, published)
        }
        val committed = binding.operations.commitRegistry(head, UUID.randomUUID(), mutation)
        return when (committed) {
            is SkinResult.Error -> {
                if (isOuterFailure(committed.code)) return CandidateCommit.OuterFailure(committed)
                val references = when (val snapshot = binding.operations.referenceSnapshot()) {
                    is SkinResult.Error -> return CandidateCommit.OuterFailure(snapshot)
                    is SkinResult.Ok -> snapshot.value
                }
                when (val cleanup = binding.operations.discardUnreferenced(published, references)) {
                    is SkinResult.Error -> CandidateCommit.OuterFailure(cleanup)
                    is SkinResult.Ok -> CandidateCommit.Result(candidateFailure(candidate, committed), null)
                }
            }
            is SkinResult.Ok -> {
                val references = when (val snapshot = binding.operations.referenceSnapshot()) {
                    is SkinResult.Error -> return CandidateCommit.Result(
                        CandidateImportResult(
                            candidate.rawPrefix.copyOf(),
                            SkinImportCode.OK,
                            published,
                            "Registry commit is durable; publication ownership settlement is deferred: ${snapshot.detail}",
                        ),
                        committed.value,
                    )
                    is SkinResult.Ok -> snapshot.value
                }
                val settlement = binding.operations.settlePublications(references)
                val detail = if (settlement is SkinResult.Error) {
                    "Registry commit is durable; publication ownership settlement is deferred: ${settlement.detail}"
                } else {
                    "Imported and committed"
                }
                CandidateCommit.Result(
                    CandidateImportResult(candidate.rawPrefix.copyOf(), SkinImportCode.OK, published, detail),
                    committed.value,
                )
            }
        }
    }

    private fun reserveCandidate(
        binding: SkinImportCoordinatorDependencies,
        candidate: PreparedSkinCandidate,
    ): SkinResult<SkinQuotaReservation> = try {
        binding.quota.reserve(
            SkinQuotaBudgets.importCandidate(
                candidate.payloads.map { it.length },
                candidate.importReceiptBytes.size.toLong(),
            ),
        )
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.PROFILE_QUOTA_EXCEEDED, "Candidate quota request is invalid: ${error.message}")
    }

    private fun <T> finishClaimed(
        binding: SkinImportCoordinatorDependencies,
        record: SkinPreparationRecord,
        terminal: (SkinResult<Unit>, SkinHandleCleanupRetry) -> Unit,
        action: () -> SkinResult<T>,
    ): SkinResult<T> {
        var outcome = try {
            action()
        } catch (error: Exception) {
            unavailable("Claimed skin import failed: ${error.message}")
        }
        val retry = cleanupRetry(binding, record.handle.handleId)
        val cleanup = cleanupOwner(binding, record.stagingOwner)
        record.closeClaimed()
        forgetTerminal(record)
        terminal(cleanup, retry)
        if (cleanup is SkinResult.Error) outcome = cleanup
        return outcome
    }

    /** Operation-local evidence, never a terminal-record cache and never inferred from an error code. */
    private fun <T> observeHandle(
        action: ((SkinResult<Unit>, SkinHandleCleanupRetry) -> Unit) -> SkinResult<T>,
    ): SkinHandleOperation<T> {
        var disposition = SkinHandleDisposition.RETAINED
        var retry: SkinHandleCleanupRetry? = null
        val result = action { cleanup, ticket ->
            disposition = if (cleanup is SkinResult.Ok) SkinHandleDisposition.CLEANED else SkinHandleDisposition.CLEANUP_PENDING
            if (cleanup is SkinResult.Error) retry = ticket
        }
        return SkinHandleOperation(result, disposition, retry)
    }

    /** Minted only for the exact terminal owner. No payload or filesystem path leaves this closure. */
    private fun cleanupRetry(binding: SkinImportCoordinatorDependencies, handleId: UUID): SkinHandleCleanupRetry {
        val epoch = synchronized(monitor) { bindingEpoch }
        val owner = binding.paths.importHandleOwner(handleId)
        val ownerKey = runCatching { binding.fileSystem.identity(owner).fileKey }.getOrNull()
        val parentKey = runCatching { binding.fileSystem.identity(binding.paths.importHandles).fileKey }.getOrNull()
        return SkinHandleCleanupRetry {
            withBoundOperation { current ->
                if (current !== binding || synchronized(monitor) { bindingEpoch !== epoch }) {
                    return@withBoundOperation blocked("Cleanup ticket belongs to another coordinator binding")
                }
                withGate(current) {
                    val fs = current.fileSystem
                    if (record(handleId) != null || ownerKey == null || parentKey == null ||
                        fs.identity(current.paths.importHandles).fileKey != parentKey ||
                        (fs.exists(owner) && fs.identity(owner).fileKey != ownerKey)
                    ) return@withGate unavailable("Original cleanup owner evidence is unavailable or changed")
                    // Existing bounded cleanup validates all evidence before deletion and syncs absence.
                    cleanupOwner(current, owner)
                }
            }
        }
    }

    private fun cleanupOwner(binding: SkinImportCoordinatorDependencies, owner: File): SkinResult<Unit> = try {
        binding.handleStaging.cleanup(owner)
        SkinResult.Ok(Unit)
    } catch (error: Exception) {
        unavailable("Import handle cleanup is ambiguous: ${error.message}")
    }

    private fun recoverOrphansLocked(binding: SkinImportCoordinatorDependencies): SkinResult<Int> = try {
        SkinResult.Ok(binding.handleStaging.recoverOrphans())
    } catch (error: Exception) {
        unavailable("Import handle orphan recovery is ambiguous: ${error.message}")
    }

    private fun summary(
        result: CandidatePreparationResult,
        binding: SkinImportCoordinatorDependencies,
        inputName: String?,
    ): CandidatePreparationSummary = when (result) {
        is CandidatePreparationResult.Ready -> CandidatePreparationSummary(
            result.candidate.rawPrefix.toHex(),
            result.candidate.candidateKey,
            result.candidate.name,
            SkinImportCode.OK,
            "Ready",
        )
        is CandidatePreparationResult.Rejected -> CandidatePreparationSummary(
            result.rawPrefix.toHex(),
            null,
            null,
            result.code,
            publicDetail(result.detail, binding, inputName),
        )
    }

    private fun publicDetail(
        detail: String,
        binding: SkinImportCoordinatorDependencies,
        inputName: String?,
    ): String {
        val forbidden = listOfNotNull(
            binding.paths.staging.absolutePath,
            binding.paths.staging.path,
            inputName?.takeIf(String::isNotBlank),
        )
        return if (forbidden.any { detail.contains(it, ignoreCase = true) }) {
            "Candidate preparation rejected"
        } else {
            detail.take(512)
        }
    }

    private fun candidateFailure(candidate: PreparedSkinCandidate, error: SkinResult.Error) = CandidateImportResult(
        candidate.rawPrefix.copyOf(),
        error.code,
        null,
        error.detail,
    )

    private fun isOuterFailure(code: SkinImportCode): Boolean = code in setOf(
        SkinImportCode.DURABILITY_UNAVAILABLE,
        SkinImportCode.REGISTRY_CORRUPT,
        SkinImportCode.REGISTRY_GENESIS_CORRUPT,
        SkinImportCode.REGISTRY_RECOVERY_AMBIGUOUS,
        SkinImportCode.REGISTRY_UNRECOVERABLE,
        SkinImportCode.SESSION_RECOVERY_AMBIGUOUS,
        SkinImportCode.PROFILE_QUOTA_EXCEEDED,
        SkinImportCode.LIFECYCLE_BLOCKED,
        SkinImportCode.INDETERMINATE,
    )

    private fun isStartupRecovered(): Boolean = synchronized(monitor) { startupRecovered }

    private fun markStartupRecovered() = synchronized(monitor) {
        startupRecovered = true
    }

    private fun forgetTerminal(record: SkinPreparationRecord) = synchronized(monitor) {
        check(!record.isActive()) { "Cannot forget an active preparation" }
        // Ambiguous cleanup remains owned on disk (and by quarantine's pending reservation mechanism).
        records.remove(record.handle.handleId, record)
    }

    private fun record(handleId: UUID): SkinPreparationRecord? = synchronized(monitor) { records[handleId] }

    private fun <T> withGateAndRecovery(
        binding: SkinImportCoordinatorDependencies,
        action: () -> SkinResult<T>,
    ): SkinResult<T> = withGate(binding) {
        if (!isStartupRecovered()) {
            when (val recovered = recoverOrphansLocked(binding)) {
                is SkinResult.Error -> return@withGate recovered
                is SkinResult.Ok -> markStartupRecovered()
            }
        }
        action()
    }

    private fun <T> withGate(
        binding: SkinImportCoordinatorDependencies,
        action: () -> SkinResult<T>,
    ): SkinResult<T> = when (val gate = binding.operations.mutationGate()) {
        LeaseMutationGate.CLEAR -> action()
        LeaseMutationGate.ACTIVE -> blocked("Skin mutation is blocked by an active session lease")
        LeaseMutationGate.UNKNOWN -> blocked("Skin mutation is blocked because session lease state is unknown")
    }

    private fun <T> withBoundOperation(
        action: (SkinImportCoordinatorDependencies) -> SkinResult<T>,
    ): SkinResult<T> {
        val binding = synchronized(monitor) {
            val current = dependencies ?: return unavailable("Skin import coordinator is unbound")
            activeOperations++
            current
        }
        return try {
            binding.lockManager.withSessionThenRegistry { action(binding) }
        } catch (error: Exception) {
            unavailable("Cannot run ordered skin import operation: ${error.message}")
        } finally {
            synchronized(monitor) { activeOperations-- }
        }
    }

    private class PreparationFailure(val error: SkinResult.Error) : RuntimeException(error.detail)

    private sealed interface CandidateCommit {
        data class Result(val result: CandidateImportResult, val head: RegistryHead?) : CandidateCommit
        data class OuterFailure(val error: SkinResult.Error) : CandidateCommit
    }

    private fun invalid(detail: String) = SkinResult.Error(SkinImportCode.INVALID_INPUT, detail)
    private fun blocked(detail: String) = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, detail)
    private fun conflict(detail: String) = SkinResult.Error(SkinImportCode.REGISTRY_CONFLICT, detail)
    private fun corrupt(detail: String) = SkinResult.Error(SkinImportCode.REGISTRY_CORRUPT, detail)
    private fun unavailable(detail: String) = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, detail)
    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private val DIGEST = Regex("[0-9a-f]{64}")
    private val ID = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
    private val ZERO_DIGEST = "0".repeat(64)
}
