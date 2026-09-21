package dev.silksong.launcher.skins.ui

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import dev.silksong.launcher.R
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.registry.CandidatePreparationSummary
import java.util.concurrent.Executor
import java.util.concurrent.Executors

internal data class SkinActivityHostBinding(val services: SkinLibraryUiServices, val provider: SkinDocumentProvider, val worker: Executor)

/** Touch-first controls for the single Kotlin library authority; runtime reports remain observations only. */
class SkinsActivity : Activity() {
    private lateinit var profile: GameProfile
    private lateinit var session: SkinLibrarySession
    private lateinit var packs: LinearLayout
    private lateinit var prepared: LinearLayout
    private var dialog: AlertDialog? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val retained = lastNonConfigurationInstance as? SkinLibrarySession
        profile = SelectedGameStore(this).get()
        if (!SkinLibraryService.isVisible(profile)) { retained?.close(); finish(); return }
        setContentView(R.layout.activity_skins)
        val profilePresentation = SkinProfilePresentation.require(profile)
        findViewById<TextView>(R.id.skins_title).setText(profilePresentation.title)
        findViewById<TextView>(R.id.skins_availability).setText(profilePresentation.guidance)
        packs = findViewById(R.id.skins_packs)
        prepared = findViewById(R.id.skins_prepared)
        val application = applicationContext
        val binding = hostBinding
        require(binding == null || binding.services.profile == profile) { "Host service uses another profile" }
        session = retained ?: SkinLibrarySession(
            binding?.services ?: SkinLibraryUiServices.production(application, profile),
            SkinSafInputs(binding?.provider ?: AndroidSkinDocumentProvider(application.contentResolver)),
            { SelectedGameStore(application).get() },
            binding?.worker ?: Executors.newSingleThreadExecutor { action -> Thread(action, "skin-library-worker") },
            { action -> Handler(Looper.getMainLooper()).post(action) },
        )
        findViewById<Button>(R.id.skins_back).setOnClickListener { if (acceptsCallback()) { session.close(); finish() } }
        findViewById<Button>(R.id.skins_import).setOnClickListener { if (acceptsCallback()) showImportSource() }
        findViewById<Button>(R.id.skins_import_all).setOnClickListener { if (acceptsCallback()) session.importAll() }
        findViewById<Button>(R.id.skins_cancel).setOnClickListener { if (acceptsCallback()) session.cancel() }
        findViewById<Button>(R.id.skins_library_details).setOnClickListener { if (acceptsCallback()) showLibraryDetails(session.state) }
    }

    override fun onStart() { super.onStart(); if (::session.isInitialized) session.attach(::render) }
    override fun onResume() {
        super.onResume()
        if (!::session.isInitialized || isFinishing) return
        if (!sameProfile()) { session.close(); finish(); return }
        SkinLibrarySession.retryPendingCleanup()
        session.refresh()
    }
    override fun onStop() { if (::session.isInitialized) session.detach(); super.onStop() }
    override fun onRetainNonConfigurationInstance(): Any? = if (::session.isInitialized) session else null
    override fun onDestroy() {
        dialog?.dismiss(); dialog = null
        if (::session.isInitialized) { session.detach(); if (!isChangingConfigurations) session.close() }
        super.onDestroy()
    }
    @Deprecated("Platform Activity back handling")
    override fun onBackPressed() { if (::session.isInitialized) session.close(); super.onBackPressed() }

    private fun sameProfile() = SelectedGameStore(this).get() == profile
    private fun acceptsCallback() = !isDestroyed && !isFinishing && sameProfile()

    private fun showImportSource() {
        if (!sameProfile() || !session.state.canImport || session.state.busy || session.state.handles.isNotEmpty()) return
        dialog = AlertDialog.Builder(this)
            .setTitle(R.string.skins_import_source_title)
            .setItems(arrayOf(getString(R.string.skins_choose_archive), getString(R.string.skins_choose_folder))) { _, which ->
                if (acceptsCallback()) pick(folder = which == 1)
            }
            .setNegativeButton(android.R.string.cancel, null)
            .show()
    }

    private fun pick(folder: Boolean) {
        if (!sameProfile() || !session.state.canImport || session.state.busy) return
        val intent = if (folder) Intent(Intent.ACTION_OPEN_DOCUMENT_TREE)
        else Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*")
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        try { startActivityForResult(intent, if (folder) FOLDER else FILE) }
        catch (error: Exception) { showNotice(error.message.orEmpty()) }
    }

    @Deprecated("Platform Activity result handling")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode !in listOf(FILE, FOLDER) || resultCode != RESULT_OK) return
        if (!::session.isInitialized || !acceptsCallback()) return
        data?.data?.let { session.prepare(it.toString(), requestCode == FOLDER) }
    }

    private fun render(screen: SkinScreenState) {
        if (isFinishing || isDestroyed || !sameProfile()) return
        showNotice(screen.notice?.let(::noticeText) ?: screen.message)
        findViewById<Button>(R.id.skins_import).isEnabled = screen.canImport && !screen.busy && screen.handles.isEmpty()
        findViewById<Button>(R.id.skins_import_all).isEnabled = screen.canImport && !screen.busy && screen.handles.isNotEmpty()
        findViewById<Button>(R.id.skins_cancel).isEnabled = screen.handles.isNotEmpty() || screen.busy || screen.cleanupPending
        findViewById<Button>(R.id.skins_library_details).isEnabled =
            screen.library != null || screen.refreshError != null || session.canRecover
        renderPrepared(screen)
        renderLibrary(screen)
    }

    private fun noticeText(notice: SkinNotice): String {
        val resource = when (notice.kind) {
            SkinNoticeKind.CLEANUP_PENDING -> R.string.skins_notice_cleanup_pending
            SkinNoticeKind.PREPARATION_ACTIVE -> R.string.skins_notice_preparation_active
            SkinNoticeKind.PREPARING -> R.string.skins_notice_preparing
            SkinNoticeKind.PREPARED -> R.string.skins_notice_prepared
            SkinNoticeKind.NO_FILES -> R.string.skins_notice_no_files
            SkinNoticeKind.IMPORT_COMPLETE -> R.string.skins_notice_import_complete
            SkinNoticeKind.RECOVERED_OFF -> R.string.skins_notice_recovered
            SkinNoticeKind.MUTATION_UNAVAILABLE -> R.string.skins_notice_mutation_unavailable
            SkinNoticeKind.OPERATION_COMPLETE -> R.string.skins_notice_operation_complete
            SkinNoticeKind.PREPARATION_CANCELLED -> R.string.skins_notice_cancelled
            SkinNoticeKind.PROFILE_CHANGED -> R.string.skins_notice_profile_changed
            SkinNoticeKind.ERROR -> R.string.skins_notice_error
        }
        return getString(resource, *notice.arguments.toTypedArray())
    }

    private fun showNotice(message: String) {
        val notice = findViewById<TextView>(R.id.skins_notice)
        notice.text = message.lineSequence().take(3).joinToString("\n")
        notice.visibility = if (notice.text.isBlank()) View.GONE else View.VISIBLE
    }

    private fun renderPrepared(screen: SkinScreenState) {
        val section = findViewById<View>(R.id.skins_prepared_section)
        section.visibility = if (screen.handles.isEmpty()) View.GONE else View.VISIBLE
        prepared.removeAllViews()
        screen.handles.flatMap { it.candidates }.forEach { candidate ->
            val row = LayoutInflater.from(this).inflate(R.layout.item_skin_candidate, prepared, false)
            val ready = candidate.code == SkinImportCode.OK && candidate.candidateKey != null
            val name = candidate.name?.takeIf(String::isNotBlank) ?: getString(R.string.skins_candidate_unnamed)
            row.findViewById<TextView>(R.id.skin_candidate_summary).text = getString(
                if (ready) R.string.skins_candidate_ready else R.string.skins_candidate_issue,
                name,
            )
            row.findViewById<Button>(R.id.skin_candidate_details).setOnClickListener { showCandidateDetails(name, candidate) }
            prepared.addView(row)
        }
    }

    private fun showCandidateDetails(name: String, candidate: CandidatePreparationSummary) {
        if (!acceptsCallback()) return
        dialog = AlertDialog.Builder(this)
            .setTitle(name)
            .setMessage(getString(
                R.string.skins_candidate_details,
                name,
                candidate.code.name,
                candidate.candidateKey ?: getString(R.string.skins_none),
                candidate.rawPrefixHex,
                candidate.detail,
            ))
            .setPositiveButton(R.string.skins_close, null)
            .show()
    }

    private fun renderLibrary(screen: SkinScreenState) {
        packs.removeAllViews()
        val state = screen.library
        if (state == null) {
            findViewById<TextView>(R.id.skins_requested).text = getString(
                R.string.skins_read_error,
                screen.refreshError?.code?.name ?: getString(R.string.skins_none),
                screen.refreshError?.detail ?: getString(R.string.skins_loading),
            )
            findViewById<TextView>(R.id.skins_runtime).setText(R.string.skins_runtime_unavailable)
            findViewById<View>(R.id.skins_empty).visibility = View.GONE
            return
        }
        val library = SkinPresentationMapper.library(state)
        findViewById<TextView>(R.id.skins_requested).text = getString(
            R.string.skins_requested,
            library.requestedMode,
            library.selectedPackName ?: getString(R.string.skins_none),
        )
        findViewById<TextView>(R.id.skins_runtime).text = when (library.runtime.kind) {
            SkinRuntimeKind.NONE -> getString(R.string.skins_runtime_none)
            SkinRuntimeKind.UNAVAILABLE -> getString(R.string.skins_runtime_unavailable)
            SkinRuntimeKind.REPORT -> getString(
                if (library.runtime.olderConfiguration) R.string.skins_runtime_report_old else R.string.skins_runtime_report,
                library.runtime.status,
            )
        }
        findViewById<View>(R.id.skins_empty).visibility = if (library.packs.isEmpty()) View.VISIBLE else View.GONE
        library.packs.forEach { addPack(screen, state, it) }
    }

    private fun addPack(screen: SkinScreenState, state: SkinLibraryViewState, item: SkinPackPresentation) {
        val row = LayoutInflater.from(this).inflate(R.layout.item_skin_pack, packs, false)
        val pack = item.row
        row.findViewById<TextView>(R.id.skin_pack_name).text = pack.name
        row.findViewById<TextView>(R.id.skin_pack_author).text = getString(
            R.string.skins_pack_author,
            pack.author.ifBlank { getString(R.string.skins_unknown) },
        )
        row.findViewById<TextView>(R.id.skin_pack_state).setText(when (item.badge) {
            SkinPackBadge.INSTALLED -> R.string.skins_badge_installed
            SkinPackBadge.ROTATION_INCLUDED -> R.string.skins_badge_rotation
            SkinPackBadge.SELECTED_OFF -> R.string.skins_badge_selected_off
            SkinPackBadge.ENABLED -> R.string.skins_badge_enabled
            SkinPackBadge.ROTATING -> R.string.skins_badge_rotating
            SkinPackBadge.PENDING -> R.string.skins_badge_pending
        })
        row.findViewById<View>(R.id.skin_pack_delete_reason).visibility =
            if (item.deleteBlock == null) View.GONE else View.VISIBLE
        val target = SkinReplaceTarget(pack.id, state.generationSha256, pack.treeSha256, pack.importReceiptSha256)
        row.findViewById<Button>(R.id.skin_pack_delete).apply {
            isEnabled = screen.canEdit && !screen.busy && state.simplifiedAuthority && item.deleteBlock == null
            setOnClickListener { confirmDelete(pack.name, target) }
        }
        row.findViewById<Button>(R.id.skin_pack_details).setOnClickListener { showPackDetails(screen, state, item, target) }
        packs.addView(row)
    }

    private fun confirmDelete(name: String, target: SkinReplaceTarget) {
        if (!acceptsCallback()) return
        dialog = AlertDialog.Builder(this)
            .setTitle(getString(R.string.skins_delete_title, name))
            .setMessage(R.string.skins_delete_detail)
            .setPositiveButton(R.string.skins_delete) { _, _ -> if (acceptsCallback()) session.remove(target) }
            .setNegativeButton(android.R.string.cancel, null)
            .show()
    }

    private fun showPackDetails(
        screen: SkinScreenState,
        state: SkinLibraryViewState,
        item: SkinPackPresentation,
        target: SkinReplaceTarget,
    ) {
        if (!acceptsCallback()) return
        val pack = item.row
        val receipt = pack.receipt
        val none = getString(R.string.skins_none)
        val receiptText = receipt.error?.let { getString(R.string.skins_receipt_error, it.code.name, it.detail) }
            ?: getString(
                R.string.skins_receipt_details,
                receipt.archiveName ?: none,
                receipt.sourceStatus ?: none,
                receipt.warnings.joinToString("\n").ifEmpty { none },
            ) + if (receipt.omittedWarnings > 0) "\n${getString(R.string.skins_more_warnings, receipt.omittedWarnings)}" else ""
        val detail = getString(
            R.string.skins_pack_details,
            pack.id,
            pack.candidateKey,
            pack.treeSha256,
            pack.importReceiptSha256,
            yesNo(pack.selected),
            yesNo(pack.id == state.pendingPackId),
            yesNo(pack.rotationEligible),
            receiptText,
        )
        val canReplace = screen.canImport && !screen.busy && (pack.selected || state.simplifiedAuthority) && readySources(screen).isNotEmpty()
        val shown = AlertDialog.Builder(this)
            .setTitle(pack.name)
            .setMessage(detail)
            .setPositiveButton(R.string.skins_replace) { _, _ -> if (acceptsCallback()) chooseSource(screen, pack.name, target) }
            .setNegativeButton(R.string.skins_close, null)
            .show()
        shown.getButton(AlertDialog.BUTTON_POSITIVE).isEnabled = canReplace
        dialog = shown
    }

    private fun showLibraryDetails(screen: SkinScreenState) {
        if (!acceptsCallback()) return
        val state = screen.library
        val none = getString(R.string.skins_none)
        val recovery = getString(if (session.canRecover) R.string.skins_recovery_required else R.string.skins_recovery_clear)
        val message = (if (state == null) {
            getString(
                R.string.skins_read_error,
                screen.refreshError?.code?.name ?: none,
                screen.refreshError?.detail ?: getString(R.string.skins_loading),
            )
        } else {
            getString(
                R.string.skins_library_details,
                state.mode,
                state.selectedPackId ?: none,
                state.pendingPackId ?: none,
                state.rotationOrder.joinToString(" → ").ifEmpty { none },
                state.runtimeObservation ?: none,
                state.interlock,
                state.originalFailure ?: none,
                state.rollbackFailure ?: none,
                state.leaseObservation,
            )
        }) + "\n" + recovery
        dialog = AlertDialog.Builder(this)
            .setTitle(R.string.skins_library_details_title)
            .setMessage(message)
            .setNeutralButton(R.string.skins_refresh) { _, _ -> if (acceptsCallback()) session.refresh() }
            .setNegativeButton(R.string.skins_close, null)
            .show()
    }

    private fun readySources(screen: SkinScreenState) = screen.handles.flatMap { handle ->
        handle.candidates.filter { it.code == SkinImportCode.OK && it.candidateKey != null }.map { handle.handleId to it }
    }

    private fun chooseSource(screen: SkinScreenState, targetName: String, target: SkinReplaceTarget) {
        val preparationOwner = screen.preparationOwner ?: return
        val sources = readySources(screen)
        if (sources.isEmpty()) return
        dialog = AlertDialog.Builder(this).setTitle(R.string.skins_choose_source)
            .setItems(sources.map { "${it.second.name} · ${it.second.rawPrefixHex} · ${it.second.candidateKey}" }.toTypedArray()) { _, index ->
                if (!acceptsCallback()) return@setItems
                val (handle, source) = sources[index]
                dialog = AlertDialog.Builder(this).setTitle(R.string.skins_confirm)
                    .setMessage(getString(R.string.skins_confirm_replace, targetName, source.name,
                        source.candidateKey, target.generationSha256, target.treeSha256, target.receiptSha256))
                    .setPositiveButton(R.string.skins_confirm) { _, _ ->
                        if (acceptsCallback()) session.replace(handle, requireNotNull(source.candidateKey), target)
                    }
                    .setNegativeButton(android.R.string.cancel) { _, _ -> if (acceptsCallback()) session.cancel(preparationOwner) }
                    .setOnCancelListener { if (acceptsCallback()) session.cancel(preparationOwner) }
                    .show()
            }
            .setNegativeButton(android.R.string.cancel) { _, _ -> if (acceptsCallback()) session.cancel(preparationOwner) }
            .setOnCancelListener { if (acceptsCallback()) session.cancel(preparationOwner) }
            .show()
    }

    private fun yesNo(value: Boolean) = getString(if (value) R.string.skins_yes else R.string.skins_no)

    companion object {
        private const val FILE = 4501
        private const val FOLDER = 4502
        @Volatile private var hostBinding: SkinActivityHostBinding? = null
        internal fun <T> withHostBinding(binding: SkinActivityHostBinding, action: () -> T): T {
            check(hostBinding == null) { "Host Activity binding already present" }
            hostBinding = binding
            return try { action() } finally { hostBinding = null }
        }
    }
}
