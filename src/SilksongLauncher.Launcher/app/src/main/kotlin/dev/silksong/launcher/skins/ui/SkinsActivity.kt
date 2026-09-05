package dev.silksong.launcher.skins.ui

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import dev.silksong.launcher.R
import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.SelectedGameStore
import dev.silksong.launcher.skins.contracts.SkinImportCode
import java.util.concurrent.Executor
import java.util.concurrent.Executors

internal data class SkinActivityHostBinding(val services: SkinLibraryUiServices, val provider: SkinDocumentProvider, val worker: Executor)

/** Launcher-only surface: production factories are read-only and install no launch/session authority. */
class SkinsActivity : Activity() {
    private lateinit var profile: GameProfile
    private lateinit var session: SkinLibrarySession
    private lateinit var packs: LinearLayout
    private var dialog: AlertDialog? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val retained = lastNonConfigurationInstance as? SkinLibrarySession
        profile = SelectedGameStore(this).get()
        if (!SkinLibraryService.isVisible(profile)) { retained?.close(); finish(); return }
        setContentView(R.layout.activity_skins)
        packs = findViewById(R.id.skins_packs)
        val application = applicationContext
        val binding = hostBinding
        require(binding == null || binding.services.profile == profile) { "Host service uses another profile" }
        session = retained ?: SkinLibrarySession(
            binding?.services ?: SkinLibraryUiServices.production(filesDir, profile),
            SkinSafInputs(binding?.provider ?: AndroidSkinDocumentProvider(application.contentResolver)),
            { SelectedGameStore(application).get() },
            binding?.worker ?: Executors.newSingleThreadExecutor { action -> Thread(action, "skin-library-worker") },
            { action -> Handler(Looper.getMainLooper()).post(action) },
        )
        if (binding != null) findViewById<TextView>(R.id.skins_availability).setText(R.string.skins_host_preview)
        findViewById<Button>(R.id.skins_refresh).setOnClickListener { SkinLibrarySession.retryPendingCleanup(); session.refresh() }
        findViewById<Button>(R.id.skins_back).setOnClickListener { session.close(); finish() }
        findViewById<Button>(R.id.skins_prepare_file).setOnClickListener { pick(false) }
        findViewById<Button>(R.id.skins_prepare_folder).setOnClickListener { pick(true) }
        findViewById<Button>(R.id.skins_import_all).setOnClickListener { session.importAll() }
        findViewById<Button>(R.id.skins_cancel).setOnClickListener { session.cancel() }
        findViewById<Button>(R.id.skins_advance_mode).setOnClickListener { session.advanceMode() }
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

    private fun pick(folder: Boolean) {
        if (!sameProfile() || !session.state.canImport || session.state.busy) return
        val intent = if (folder) Intent(Intent.ACTION_OPEN_DOCUMENT_TREE)
        else Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*")
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        try { startActivityForResult(intent, if (folder) FOLDER else FILE) }
        catch (error: Exception) { findViewById<TextView>(R.id.skins_feedback).text = error.message }
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
        findViewById<TextView>(R.id.skins_feedback).text = screen.message
        findViewById<Button>(R.id.skins_refresh).isEnabled = !screen.busy
        findViewById<Button>(R.id.skins_prepare_file).isEnabled = screen.canImport && !screen.busy && screen.handles.isEmpty()
        findViewById<Button>(R.id.skins_prepare_folder).isEnabled = screen.canImport && !screen.busy && screen.handles.isEmpty()
        findViewById<Button>(R.id.skins_import_all).isEnabled = screen.canImport && !screen.busy && screen.handles.isNotEmpty()
        findViewById<Button>(R.id.skins_cancel).isEnabled = screen.handles.isNotEmpty() || screen.busy || screen.cleanupPending
        findViewById<Button>(R.id.skins_advance_mode).isEnabled = screen.canAdvance && !screen.busy
        val prepared = findViewById<LinearLayout>(R.id.skins_prepared)
        prepared.removeAllViews()
        screen.handles.forEach { handle -> handle.candidates.forEach { candidate ->
            addText(prepared, "${candidate.name.orEmpty()} · ${candidate.rawPrefixHex}\n${candidate.code} · ${candidate.detail}\n${candidate.candidateKey.orEmpty()}")
        } }
        packs.removeAllViews()
        val state = screen.library
        val status = findViewById<TextView>(R.id.skins_status)
        if (state == null) {
            status.text = getString(R.string.skins_read_error, screen.refreshError?.code?.name ?: "UNKNOWN",
                screen.refreshError?.detail ?: "Library status has not been read")
            return
        }
        val none = getString(R.string.skins_none)
        status.text = getString(R.string.skins_status, state.mode, state.activePackId ?: getString(R.string.skins_vanilla),
            state.selectedPackId ?: none, state.rotationOrder.joinToString(" → ").ifEmpty { none },
            state.interlock, state.originalFailure ?: none, state.rollbackFailure ?: none, state.leaseObservation)
        if (state.packs.isEmpty()) addText(packs, getString(R.string.skins_empty))
        state.packs.forEach { pack ->
            addText(packs, getString(R.string.skins_pack_details, pack.name, pack.author, pack.id,
                pack.candidateKey, pack.treeSha256, pack.importReceiptSha256, yesNo(pack.selected), yesNo(pack.rotationEligible)))
            val receipt = pack.receipt
            val error = receipt.error
            if (error != null) addText(packs, getString(R.string.skins_receipt_error, error.code.name, error.detail))
            else {
                addText(packs, getString(R.string.skins_receipt_details, receipt.archiveName ?: none,
                    receipt.sourceStatus ?: none, receipt.warnings.joinToString("\n").ifEmpty { none }))
                if (receipt.omittedWarnings > 0) addText(packs, getString(R.string.skins_more_warnings, receipt.omittedWarnings))
            }
            val target = SkinReplaceTarget(pack.id, state.generationSha256, pack.treeSha256, pack.importReceiptSha256)
            addButton(getString(R.string.skins_select, pack.name), screen.canEdit && !screen.busy && !pack.selected) { session.select(target) }
            addButton(getString(if (pack.rotationEligible) R.string.skins_exclude else R.string.skins_include, pack.name), screen.canEdit && !screen.busy) {
                session.eligibility(target, !pack.rotationEligible)
            }
            addButton(getString(R.string.skins_replace, pack.name), screen.canImport && !screen.busy && pack.selected && screen.handles.any {
                it.candidates.any { candidate -> candidate.code == SkinImportCode.OK && candidate.candidateKey != null }
            }) { chooseSource(screen, pack.name, target) }
        }
    }
    private fun chooseSource(screen: SkinScreenState, targetName: String, target: SkinReplaceTarget) {
        val preparationOwner = screen.preparationOwner ?: return
        val sources = screen.handles.flatMap { handle -> handle.candidates.filter {
            it.code == SkinImportCode.OK && it.candidateKey != null
        }.map { handle.handleId to it } }
        if (sources.isEmpty()) return
        dialog = AlertDialog.Builder(this).setTitle(R.string.skins_choose_source)
            .setItems(sources.map { "${it.second.name} · ${it.second.rawPrefixHex} · ${it.second.candidateKey}" }.toTypedArray()) { _, index ->
                if (!acceptsCallback()) return@setItems
                val (handle, source) = sources[index]
                dialog = AlertDialog.Builder(this).setTitle(R.string.skins_confirm)
                    .setMessage(getString(R.string.skins_confirm_replace, targetName, source.name,
                        source.candidateKey, target.generationSha256, target.treeSha256, target.receiptSha256))
                    .setPositiveButton(R.string.skins_confirm) { _, _ -> if (acceptsCallback()) session.replace(handle, requireNotNull(source.candidateKey), target) }
                    .setNegativeButton(android.R.string.cancel) { _, _ -> if (acceptsCallback()) session.cancel(preparationOwner) }
                    .setOnCancelListener { if (acceptsCallback()) session.cancel(preparationOwner) }.show()
            }
            .setNegativeButton(android.R.string.cancel) { _, _ -> if (acceptsCallback()) session.cancel(preparationOwner) }
            .setOnCancelListener { if (acceptsCallback()) session.cancel(preparationOwner) }.show()
    }
    private fun yesNo(value: Boolean) = getString(if (value) R.string.skins_yes else R.string.skins_no)
    private fun addButton(label: String, enabled: Boolean, action: () -> Unit) {
        packs.addView(Button(this).apply {
            text = label; isEnabled = enabled; minHeight = (48 * resources.displayMetrics.density).toInt()
            layoutParams = LinearLayout.LayoutParams(-1, -2)
            setOnClickListener { if (sameProfile()) action() }
        })
    }
    private fun addText(parent: LinearLayout, value: String) {
        parent.addView(TextView(this).apply {
            text = value; textSize = 15f; setTextColor(getColor(R.color.text_primary)); setTextIsSelectable(true)
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = (16 * resources.displayMetrics.density).toInt() }
        })
    }
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
