package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.registry.CandidateImportResult
import dev.silksong.launcher.skins.registry.SkinImportCoordinator
import dev.silksong.launcher.skins.registry.SkinPreparationHandle
import dev.silksong.launcher.skins.registry.SkinHandleDisposition
import dev.silksong.launcher.skins.registry.SkinHandleOperation
import dev.silksong.launcher.skins.registry.SkinHandleCleanupRetry
import dev.silksong.launcher.skins.contracts.SkinLimits
import java.util.UUID

internal data class SkinImportSummary(
    val rawPrefixHex: String,
    val code: SkinImportCode,
    val installedId: String?,
    val detail: String,
    val warnings: List<String>,
)
internal data class SkinReplaceTarget(
    val id: String,
    val generationSha256: String,
    val treeSha256: String,
    val receiptSha256: String,
)
internal data class SkinReplaceRequest(
    val handleId: UUID,
    val sourceCandidateKey: String,
    val target: SkinReplaceTarget,
)

internal data class SkinImportAttempt<T>(
    val result: SkinResult<T>,
    val disposition: SkinHandleDisposition = if (result is SkinResult.Ok) SkinHandleDisposition.CLEANED else SkinHandleDisposition.RETAINED,
)

/** All returned values are summaries/handles. No BuiltSkin, PublishedSkin, staging or registry authority escapes. */
internal interface SkinImportService {
    val available: Boolean
    fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle>
    fun commitImport(handleId: UUID): SkinResult<List<SkinImportSummary>>
    fun commitReplace(request: SkinReplaceRequest): SkinResult<SkinImportSummary>
    fun importAttempt(handleId: UUID): SkinImportAttempt<List<SkinImportSummary>> = SkinImportAttempt(commitImport(handleId))
    fun replaceAttempt(request: SkinReplaceRequest): SkinImportAttempt<SkinImportSummary> = SkinImportAttempt(commitReplace(request))
    fun cancel(handleId: UUID): SkinResult<Unit>
}

internal object UnavailableSkinImportService : SkinImportService {
    override val available = false
    override fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle> = unavailable()
    override fun commitImport(handleId: UUID): SkinResult<List<SkinImportSummary>> = unavailable()
    override fun commitReplace(request: SkinReplaceRequest): SkinResult<SkinImportSummary> = unavailable()
    override fun cancel(handleId: UUID): SkinResult<Unit> = unavailable()
    private fun unavailable() = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE,
        "Production imports are blocked by H4-STORAGE-RETENTION-001 / H4-STORAGE-GC-002")
}

/** Host-only adapter; it neither creates nor promotes any coordinator binding or mutation gate. */
internal class CoordinatorSkinImportService private constructor(
    private val receipts: SkinReceiptSummaryReader,
) : SkinImportService {
    override val available = true
    // Only unresolved cleanup tickets are retained, never terminal outcomes or preparation payloads.
    private val pendingCleanup = linkedMapOf<UUID, SkinHandleCleanupRetry>()
    @Synchronized override fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle> =
        if (pendingCleanup.size >= SkinLimits.V1.candidates) cleanupBound() else SkinImportCoordinator.prepare(input)
    override fun commitImport(handleId: UUID) = importAttempt(handleId).result
    override fun commitReplace(request: SkinReplaceRequest) = replaceAttempt(request).result

    @Synchronized override fun importAttempt(handleId: UUID): SkinImportAttempt<List<SkinImportSummary>> {
        if (!canAttempt(handleId)) return SkinImportAttempt(cleanupBound())
        val operation = SkinImportCoordinator.commitImportWithOwnership(handleId)
        remember(handleId, operation)
        val result = when (val outcome = operation.result) {
            is SkinResult.Error -> outcome
            is SkinResult.Ok -> SkinResult.Ok(outcome.value.map(::summary))
        }
        return SkinImportAttempt(result, operation.disposition)
    }
    @Synchronized override fun replaceAttempt(request: SkinReplaceRequest): SkinImportAttempt<SkinImportSummary> {
        if (!canAttempt(request.handleId)) return SkinImportAttempt(cleanupBound())
        val operation = SkinImportCoordinator.commitReplaceWithOwnership(request.handleId, request.sourceCandidateKey,
            request.target.id, request.target.generationSha256, request.target.treeSha256, request.target.receiptSha256)
        remember(request.handleId, operation)
        val result = when (val outcome = operation.result) {
            is SkinResult.Error -> outcome
            is SkinResult.Ok -> SkinResult.Ok(summary(outcome.value))
        }
        return SkinImportAttempt(result, operation.disposition)
    }
    @Synchronized override fun cancel(handleId: UUID): SkinResult<Unit> {
        pendingCleanup[handleId]?.let { ticket ->
            return ticket.retry().also { if (it is SkinResult.Ok) pendingCleanup.remove(handleId) }
        }
        if (!canAttempt(handleId)) return cleanupBound()
        val operation = SkinImportCoordinator.cancelWithOwnership(handleId)
        remember(handleId, operation)
        return operation.result
    }
    private fun canAttempt(handleId: UUID) = handleId !in pendingCleanup && pendingCleanup.size < SkinLimits.V1.candidates
    private fun remember(handleId: UUID, operation: SkinHandleOperation<*>) {
        if (operation.disposition == SkinHandleDisposition.CLEANUP_PENDING) {
            pendingCleanup[handleId] = requireNotNull(operation.cleanupRetry)
        }
    }
    private fun cleanupBound() = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, "Pending cleanup must be retried before another handle operation")

    private fun summary(result: CandidateImportResult): SkinImportSummary {
        val receipt = result.published?.let { receipts.read(it.candidateKey, it.importReceiptSha256) }
        val warningLines = receipt?.let {
            it.warnings + listOfNotNull(
                it.error?.let { error -> "${error.code}: ${error.detail}" },
                if (it.omittedWarnings > 0) "${it.omittedWarnings} additional receipt warnings" else null,
            )
        }.orEmpty()
        return SkinImportSummary(
            result.rawPrefix.joinToString("") { "%02x".format(it.toInt() and 0xff) },
            result.code, result.published?.id, result.detail, warningLines,
        )
    }

    companion object {
        /** Caller must already own a scoped host coordinator binding; an unbound singleton still fails closed. */
        internal fun forHostTests(
            profile: GameProfile,
            receipts: SkinReceiptSummaryReader = SkinReceiptSummaryReader.unavailable,
        ): SkinImportService {
            require(SkinLibraryService.isVisible(profile)) { "Imports require the exact Hollow Knight profile" }
            return CoordinatorSkinImportService(receipts)
        }
    }
}
