package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.SkinImportInput
import dev.silksong.launcher.skins.registry.SkinPreparationHandle
import dev.silksong.launcher.skins.registry.SkinHandleDisposition
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

internal data class SkinPreparationOutcome(val displayName: String?, val result: SkinResult<SkinPreparationHandle>)
internal data class SkinImportOutcome(
    val handleId: UUID,
    val results: List<SkinImportSummary>,
    val error: SkinResult.Error? = null,
)
internal class SkinReplaceConfirmation internal constructor(val request: SkinReplaceRequest)

/**
 * One batch's summary-only ownership. IO methods are worker-only and serialized; requestCancel is nonblocking.
 * Retain this owner across Activity recreation. A failed cancel retains its handle for an explicit retry;
 * callers must not discard the owner while handles remain. The coordinator alone owns private preparation.
 */
internal class SkinImportWorkflow(private val service: SkinImportService) {
    private val cancelRequested = AtomicBoolean(false)
    private val owned = linkedMapOf<UUID, SkinPreparationHandle>()
    private val confirmations = mutableSetOf<SkinReplaceConfirmation>()
    private val uncertain = mutableSetOf<UUID>()

    fun requestCancel() { cancelRequested.set(true) }

    @Synchronized fun handles(): List<SkinPreparationHandle> = owned.values.toList()

    @Synchronized fun prepare(
        inputs: List<SkinImportInput>,
        progress: (completed: Int, total: Int) -> Unit = { _, _ -> },
    ): SkinResult<List<SkinPreparationOutcome>> {
        if (!service.available) return unavailable()
        if (owned.isNotEmpty() || cancelRequested.get()) return blocked("This preparation owner is not empty or was cancelled")
        if (inputs.size > SkinLimits.V1.providerRows) return SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Too many inputs")
        val results = mutableListOf<SkinPreparationOutcome>()
        progress(0, inputs.size)
        for ((index, input) in inputs.withIndex()) {
            if (cancelRequested.get()) break
            val result = service.prepare(input)
            if (result is SkinResult.Ok) owned[result.value.handleId] = result.value
            results += SkinPreparationOutcome(input.displayName, result)
            progress(index + 1, inputs.size)
        }
        if (cancelRequested.get()) {
            val cleanup = cancel()
            return if (cleanup is SkinResult.Error) cleanup else blocked("Preparation cancelled; owned handles released")
        }
        return SkinResult.Ok(results)
    }

    @Synchronized fun importAll(): SkinResult<List<SkinImportOutcome>> {
        if (cancelRequested.get()) return blocked("Preparation was cancelled")
        if (!service.available) return unavailable()
        val outcomes = mutableListOf<SkinImportOutcome>()
        for (handle in owned.values.toList()) {
            if (cancelRequested.get()) break
            if (handle.handleId in uncertain) {
                outcomes += SkinImportOutcome(handle.handleId, emptyList(), blocked("Prior outcome is uncertain; refresh and cancel, do not retry commit"))
                continue
            }
            val attempt = service.importAttempt(handle.handleId)
            if (attempt.disposition == SkinHandleDisposition.CLEANED) forget(handle.handleId)
            else uncertain += handle.handleId
            when (val result = attempt.result) {
                is SkinResult.Error -> {
                    outcomes += SkinImportOutcome(handle.handleId, emptyList(), result)
                }
                is SkinResult.Ok -> {
                    outcomes += SkinImportOutcome(handle.handleId, result.value)
                }
            }
        }
        if (cancelRequested.get()) {
            val cleanup = cancel()
            if (cleanup is SkinResult.Error) owned.keys.forEach {
                outcomes += SkinImportOutcome(it, emptyList(), cleanup)
            }
        }
        return SkinResult.Ok(outcomes)
    }

    @Synchronized fun confirmation(
        handleId: UUID,
        sourceCandidateKey: String?,
        target: SkinReplaceTarget,
    ): SkinResult<SkinReplaceConfirmation> {
        if (cancelRequested.get() || handleId in uncertain) return blocked("Preparation is not available for Replace")
        val handle = owned[handleId] ?: return blocked("Preparation is not owned by this screen")
        if (sourceCandidateKey == null || handle.candidates.count {
            it.code == SkinImportCode.OK && it.candidateKey == sourceCandidateKey
        } != 1) return SkinResult.Error(SkinImportCode.INVALID_INPUT, "Explicitly choose one Ready source candidate")
        return SkinResult.Ok(SkinReplaceConfirmation(SkinReplaceRequest(handleId, sourceCandidateKey, target)).also {
            confirmations += it
        })
    }

    @Synchronized fun replace(confirmation: SkinReplaceConfirmation): SkinResult<SkinImportSummary> {
        if (cancelRequested.get() || !confirmations.remove(confirmation)) return blocked("Replace confirmation is no longer valid")
        val request = confirmation.request
        if (request.handleId !in owned || request.handleId in uncertain) return blocked("Replace preparation is no longer available")
        val attempt = service.replaceAttempt(request)
        if (attempt.disposition == SkinHandleDisposition.CLEANED) forget(request.handleId)
        else uncertain += request.handleId
        return attempt.result
    }

    @Synchronized fun cancel(): SkinResult<Unit> {
        requestCancel()
        confirmations.clear()
        var failure: SkinResult.Error? = null
        for (handleId in owned.keys.toList()) {
            when (val result = service.cancel(handleId)) {
                is SkinResult.Error -> if (failure == null) failure = result
                is SkinResult.Ok -> forget(handleId)
            }
        }
        return failure ?: SkinResult.Ok(Unit)
    }

    private fun forget(handleId: UUID) {
        owned.remove(handleId)
        uncertain.remove(handleId)
        confirmations.removeAll { it.request.handleId == handleId }
    }
    private fun blocked(detail: String) = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, detail)
    private fun unavailable() = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Skin import service is unavailable")
}
