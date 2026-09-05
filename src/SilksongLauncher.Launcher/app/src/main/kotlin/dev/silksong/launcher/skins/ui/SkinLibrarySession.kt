package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.registry.SkinPreparationHandle
import java.util.UUID
import java.util.concurrent.Executor
import java.util.concurrent.ExecutorService
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

internal data class SkinScreenState(
    val library: SkinLibraryViewState? = null,
    val busy: Boolean = false,
    val message: String = "",
    val refreshError: SkinResult.Error? = null,
    val preparationOwner: UUID? = null,
    val handles: List<SkinPreparationHandle> = emptyList(),
    val cleanupPending: Boolean = false,
    val canImport: Boolean = false,
    val canEdit: Boolean = false,
    val canAdvance: Boolean = false,
)

/** Retained across configuration changes. All IO, hashing and cleanup are serialized off the UI thread. */
internal class SkinLibrarySession(
    private val services: SkinLibraryUiServices,
    private val saf: SkinSafInputs,
    private val currentProfile: () -> GameProfile,
    private val worker: Executor,
    private val dispatch: (() -> Unit) -> Unit,
) {
    @Volatile var state = SkinScreenState()
        private set
    private var workflow = SkinImportWorkflow(services.imports)
    private val ended = AtomicBoolean(false)
    private val pendingJobs = AtomicInteger(0)
    private val viewLock = Any()
    private var viewEpoch = 0L
    private var observer: ((SkinScreenState) -> Unit)? = null

    fun attach(observer: (SkinScreenState) -> Unit) {
        synchronized(viewLock) { viewEpoch++; this.observer = observer }
        emit()
    }
    fun detach() { synchronized(viewLock) { viewEpoch++; observer = null } }

    fun refresh() = submit { readLibrary(preserveMessage = true) }

    fun prepare(document: String, folder: Boolean) = submit {
        if (!admitUi(services.imports.available)) return@submit
        if (workflow.handles().isNotEmpty()) { state = state.copy(message = "Import or cancel the current preparation first"); return@submit }
        state = state.copy(preparationOwner = UUID.randomUUID())
        val inputs = if (folder) saf.folder(document) else when (val file = saf.file(document)) {
            is SkinResult.Error -> file
            is SkinResult.Ok -> SkinResult.Ok(listOf(file.value))
        }
        when (inputs) {
            is SkinResult.Error -> report(inputs)
            is SkinResult.Ok -> when (val result = workflow.prepare(inputs.value) { completed, total ->
                state = state.copy(message = "Preparing $completed / $total"); emit()
            }) {
                is SkinResult.Error -> report(result)
                is SkinResult.Ok -> state = state.copy(message = result.value.joinToString("\n") { source ->
                    when (val prepared = source.result) {
                        is SkinResult.Error -> "${source.displayName}: ${prepared.code} · ${prepared.detail}"
                        is SkinResult.Ok -> "${source.displayName}: ${prepared.value.candidates.size} candidate results; review before importing"
                    }
                }.ifEmpty { "No immediate regular files were found" })
            }
        }
    }

    fun importAll() = submit {
        if (!admitUi(services.imports.available)) return@submit
        when (val result = workflow.importAll()) {
            is SkinResult.Error -> report(result)
            is SkinResult.Ok -> state = state.copy(message = result.value.joinToString("\n") { outcome ->
                outcome.error?.let { "${it.code} · ${it.detail}; refresh library before any retry" }
                    ?: outcome.results.joinToString("\n", transform = ::importText)
            })
        }
        readLibrary(preserveMessage = true)
    }

    /** Called only after the dialog confirms this immutable source/target pair. */
    fun replace(handleId: UUID, sourceKey: String, target: SkinReplaceTarget) = submit {
        if (!admitUi(services.imports.available)) return@submit
        when (val confirmation = workflow.confirmation(handleId, sourceKey, target)) {
            is SkinResult.Error -> report(confirmation)
            is SkinResult.Ok -> when (val result = workflow.replace(confirmation.value)) {
                is SkinResult.Error -> report(result)
                is SkinResult.Ok -> state = state.copy(message = importText(result.value))
            }
        }
        readLibrary(preserveMessage = true)
    }

    fun select(target: SkinReplaceTarget) = edit { services.mutations.select(target) }
    fun eligibility(target: SkinReplaceTarget, eligible: Boolean) = edit { services.mutations.eligibility(target, eligible) }
    private fun edit(action: () -> SkinResult<Unit>) = submit {
        if (!admitUi(services.mutations.available)) return@submit
        report(action())
        readLibrary(preserveMessage = true)
    }
    fun advanceMode() = submit {
        if (!admitUi(services.modeAvailable)) return@submit
        report(SkinLibraryController(services.mode).advanceMode())
        readLibrary(preserveMessage = true)
    }

    /** Dialog cancellation is scoped to its captured batch, checked on the serialized worker. */
    fun cancel(preparationOwner: UUID) = submit(force = true) {
        if (state.preparationOwner != preparationOwner) return@submit
        cleanup()
        if (!state.cleanupPending) {
            workflow = SkinImportWorkflow(services.imports)
            state = state.copy(preparationOwner = null)
        }
    }

    fun cancel() {
        workflow.requestCancel() // Never waits for provider IO or a coordinator lock on the caller thread.
        submit(force = true) {
            cleanup()
            if (!state.cleanupPending) {
                workflow = SkinImportWorkflow(services.imports)
                state = state.copy(preparationOwner = null)
            }
        }
    }

    fun close() {
        if (!ended.compareAndSet(false, true)) return
        detach()
        workflow.requestCancel()
        retryCleanup()
    }

    private fun retryCleanup() = submit(force = true, allowClosed = true) {
        cleanup()
        synchronized(cleanupOwners) {
            if (state.cleanupPending) cleanupOwners += this else cleanupOwners -= this
        }
    }
    private fun cleanup() {
        val result = try { workflow.cancel() } catch (error: Exception) {
            SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Cleanup transport failed: ${error.message}")
        }
        state = state.copy(cleanupPending = result is SkinResult.Error || workflow.handles().isNotEmpty())
        report(result, "Preparation cancelled")
    }

    private fun admitUi(available: Boolean): Boolean {
        // Fresh observation is only a UI preflight. Each injected mutation service must gate independently.
        readLibrary()
        if (!available || state.library?.leaseObservation != "CLEAR") {
            state = state.copy(message = "Mutation unavailable: service disabled or session ACTIVE/UNKNOWN")
            return false
        }
        return true
    }
    private fun readLibrary(preserveMessage: Boolean = false) {
        val result = try { services.read() } catch (error: Exception) {
            SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Library snapshot refresh failed: ${error.message}")
        }
        when (result) {
            is SkinResult.Error -> {
                state = state.copy(library = null, canImport = false, canEdit = false, canAdvance = false,
                    refreshError = result, message = if (preserveMessage) state.message else "")
            }
            is SkinResult.Ok -> {
                val clear = result.value.leaseObservation == "CLEAR"
                state = state.copy(library = result.value, refreshError = null,
                    canImport = clear && services.imports.available,
                    canEdit = clear && services.mutations.available,
                    canAdvance = clear && services.modeAvailable,
                    message = if (preserveMessage) state.message else "")
            }
        }
    }
    private fun report(result: SkinResult<*>, success: String = "Library operation completed; no live apply is claimed") {
        state = state.copy(message = when (result) {
            is SkinResult.Error -> "${result.code} · ${result.detail}"
            is SkinResult.Ok -> success
        })
    }
    private fun importText(result: SkinImportSummary) =
        "${result.rawPrefixHex}: ${result.code} · ${result.installedId.orEmpty()} · ${result.detail}" +
            if (result.warnings.isEmpty()) "" else "\n" + result.warnings.joinToString("\n")

    private fun submit(force: Boolean = false, allowClosed: Boolean = false, action: () -> Unit) {
        if ((ended.get() && !allowClosed) || (state.busy && !force)) return
        pendingJobs.incrementAndGet()
        state = state.copy(busy = true)
        emit()
        worker.execute {
            try {
                if (!allowClosed && (ended.get() || currentProfile() != services.profile)) {
                    state = state.copy(message = "Selected profile changed or this screen closed; operation cancelled")
                } else action()
            } catch (error: Exception) {
                state = state.copy(canImport = false, canEdit = false, canAdvance = false)
                report(SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Library operation failed: ${error.message}"))
            } finally {
                state = state.copy(busy = pendingJobs.decrementAndGet() != 0, handles = workflow.handles())
                emit()
                if (ended.get() && !state.busy && !state.cleanupPending) (worker as? ExecutorService)?.shutdown()
            }
        }
    }
    private fun emit() {
        val epoch = synchronized(viewLock) { viewEpoch }
        val snapshot = state
        dispatch {
            synchronized(viewLock) { if (epoch == viewEpoch) observer?.invoke(snapshot) }
        }
    }

    companion object {
        // Summary-only owners whose cleanup was independently blocked; no Activity or provider grant is retained.
        private val cleanupOwners = mutableSetOf<SkinLibrarySession>()
        fun retryPendingCleanup() {
            synchronized(cleanupOwners) { cleanupOwners.toList() }.forEach { it.retryCleanup() }
        }
    }
}
