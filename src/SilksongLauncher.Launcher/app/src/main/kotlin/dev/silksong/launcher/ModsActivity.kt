// ModsActivity — what is installed, what worked, and what to do about it.
//
// The screen exists because of one property of this design: mods are compiled
// into the game rather than loaded by it, so a mod that cannot work fails at
// BUILD time. That is a real advantage over a runtime loader -- the failure is
// a line of text before anything is built, rather than a crash twenty minutes
// later on a handheld with no console -- but only if somebody is shown it.
//
// So each plugin is listed with what the weaver made of it: how many patches
// it applied, and every one it could not. The toggle takes effect the next
// time the game starts and costs nothing -- every plugin here is already
// compiled in, and the switch only decides whether its gate is opened. Adding
// or removing a file is the change that needs a rebuild, and the screen says
// so rather than letting the next launch quietly be the old build.

package dev.silksong.launcher

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.profiles.SelectedGameStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import kotlin.coroutines.coroutineContext

class ModsActivity : Activity() {

    private companion object {
        const val PICK_FOLDER = 41
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private lateinit var list: LinearLayout
    private lateinit var mods: File
    private lateinit var importButton: Button
    private lateinit var importProgress: TextView
    private lateinit var rebuildButton: Button
    private lateinit var closeButton: Button
    private lateinit var mutationSession: ModStateMutationSession
    private var initialized = false
    private var importRunning = false
    private var mutationRunning = false
    private var refreshRequest = 0
    private val profile by lazy { SelectedGameStore(this).get() }
    private val buildPaths by lazy {
        ProfileBuildPaths(
            filesDir,
            requireNotNull(getExternalFilesDir(null)) { "No external files directory" },
            profile,
        )
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        mods = Mods.dir(this)
        setContentView(buildUi())
        mutationSession = ModStateMutationSession(
            DurableDisabledStateMutations.coordinator,
            scope,
            onPendingChanged = { busy ->
                if (!isFinishing && !isDestroyed) setMutationBusy(busy)
            },
            onCompleted = { relative, enabled, result ->
                if (!isFinishing && !isDestroyed) {
                    result.onSuccess {
                        LauncherLog.log("mods: $relative ${if (enabled) "enabled" else "disabled"}")
                    }.onFailure { failure ->
                        LauncherLog.log("mods: could not update $relative", failure)
                        Toast.makeText(
                            this,
                            "Could not update the mod switch: ${failure.message}",
                            Toast.LENGTH_LONG,
                        ).show()
                    }
                    // A failure reloads the durable value instead of leaving the
                    // optimistic switch position on screen.
                    refresh()
                }
            },
        )
        initialize()
    }

    override fun onResume() {
        super.onResume()
        // The folder is edited from outside this app -- over USB, or in a file
        // manager -- so what was on screen a minute ago is not evidence of
        // anything. Do not reconcile the transaction currently owned by this
        // activity; completion refreshes the list after the atomic promotion.
        if (initialized && !importRunning && !mutationRunning) refresh()
    }

    private fun initialize() {
        setImportBusy(true, "Checking mod storage…")
        scope.launch {
            val failure = withContext(Dispatchers.IO) {
                runCatching { Mods.ensure(mods, buildPaths.modStateRoot) }.exceptionOrNull()
            }
            if (isFinishing || isDestroyed) return@launch
            setImportBusy(false)
            if (failure != null) {
                showImportFailure(failure)
                return@launch
            }
            initialized = true
            refresh()
        }
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    private fun buildUi(): View {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0D0A0B"))
            setPadding(dp(20), dp(20), dp(20), dp(20))
        }

        root.addView(TextView(this).apply {
            text = "Mods — ${profile.displayName}"
            setTextColor(Color.WHITE)
            textSize = 22f
            setTypeface(typeface, Typeface.BOLD)
        })

        root.addView(TextView(this).apply {
            text = "BepInEx 5 plugins, compiled into the selected game. Install one below, or put " +
                "DLLs in\n" + mods.absolutePath +
                "\nAdding, removing, or replacing one requires rebuilding ${profile.displayName}; " +
                "switches and config apply on the next launch."
            setTextColor(Color.parseColor("#7A6E71"))
            textSize = 12f
            setPadding(0, dp(4), 0, dp(12))
        })

        // The default way in. A mod arrives as a folder -- extracted from a
        // download, on the device it was downloaded to -- and the alternative
        // is asking somebody to find Android/data in a file manager that may
        // not show it.
        importButton = button("Install a mod from a folder", primary = true) { pick() }
        root.addView(
            importButton,
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { bottomMargin = dp(4) },
        )
        importProgress = TextView(this).apply {
            setTextColor(Color.parseColor("#7A6E71"))
            textSize = 11f
            visibility = View.GONE
            setPadding(0, 0, 0, dp(8))
        }
        root.addView(importProgress)

        val scroll = ScrollView(this).apply { isFillViewport = true }
        list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(list)
        root.addView(
            scroll,
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f),
        )

        val buttons = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.END
            setPadding(0, dp(12), 0, 0)
        }
        rebuildButton = button("Rebuild", primary = true) { rebuild() }
        buttons.addView(
            rebuildButton,
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginEnd = dp(10)
            },
        )
        closeButton = button("Close") { close() }
        buttons.addView(
            closeButton,
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
        )
        root.addView(buttons)

        return root
    }

    private fun refresh() {
        if (!initialized || importRunning || mutationRunning) return
        val request = ++refreshRequest
        scope.launch {
            val result = withContext(Dispatchers.IO) {
                ModsDisplayCoordinator(
                    ProductionModsDisplaySource(
                        mods,
                        buildPaths.modStateRoot,
                        GenerationPublisher(buildPaths.profilePaths),
                        assets,
                    ),
                ).load()
            }
            if (isFinishing || isDestroyed || request != refreshRequest) return@launch
            render(result)
        }
    }

    private fun render(result: ModsDisplayCoordinator.Result) {
        list.removeAllViews()
        if (result is ModsDisplayCoordinator.Result.Failed) {
            LauncherLog.log("mods: display refresh failed", result.failure)
            list.addView(note("Mods could not be inspected: ${result.message}"))
            return
        }
        val model = (result as ModsDisplayCoordinator.Result.Ready).model
        if (model.rows.isEmpty()) {
            list.addView(note("Nothing installed yet. Install a mod from a folder, above."))
            list.addView(
                note(
                    "Transpilers, patch targets chosen at runtime and Reflection.Emit " +
                        "cannot work here; everything else generally does.",
                ),
            )
            return
        }

        if (model.stale) {
            list.addView(
                note(
                    "A mod has been added, replaced or removed since the last build. " +
                        "Rebuild to apply it. Switching one on or off does not need one.",
                ),
            )
        }

        for (row in model.rows) {
            list.addView(row(row.relative, row.report, row.enabled, row.built))
        }
    }

    private fun row(relative: String, report: Mods.Plugin?, enabled: Boolean, built: Boolean): View {
        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), dp(10), dp(12), dp(10))
            setBackgroundColor(Color.parseColor("#161112"))
        }

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        header.addView(
            TextView(this).apply {
                text = report?.title ?: relative
                setTextColor(Color.WHITE)
                textSize = 15f
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
        )
        // Whether this file is in the game as it stands. The dot carries the
        // answer and the word says what the dot means, because a colour on its
        // own is no answer to somebody who cannot tell these two apart.
        header.addView(
            TextView(this).apply {
                text = if (built) "● built" else "● not built"
                setTextColor(Color.parseColor(if (built) "#6FCF7A" else "#C88A94"))
                textSize = 11f
                setPadding(0, 0, dp(8), 0)
            },
        )
        @Suppress("DEPRECATION")
        header.addView(
            Switch(this).apply {
                isChecked = enabled
                setOnCheckedChangeListener { _, checked ->
                    mutationSession.submit(buildPaths.modStateRoot, relative, checked)
                }
            },
        )
        card.addView(header)

        card.addView(
            TextView(this).apply {
                text = summary(relative, report, enabled)
                setTextColor(Color.parseColor("#7A6E71"))
                textSize = 11f
            },
        )

        for (issue in report?.issues.orEmpty()) {
            card.addView(
                TextView(this).apply {
                    text = "• $issue"
                    setTextColor(Color.parseColor("#C88A94"))
                    textSize = 11f
                    setPadding(0, dp(2), 0, 0)
                },
            )
        }

        val wrapper = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, 0, 0, dp(10))
        }
        wrapper.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        return wrapper
    }

    private fun summary(relative: String, report: Mods.Plugin?, enabled: Boolean): String {
        if (!enabled) return "$relative — off"
        if (report == null) return "$relative — not built yet"
        val version = if (report.version.isEmpty()) "" else " v${report.version}"
        val patches = if (report.patched == 1) "1 patch" else "${report.patched} patches"
        return when (report.status) {
            "Ok" -> "$relative$version — $patches applied"
            "Partial" -> "$relative$version — $patches applied, with problems"
            else -> "$relative$version — not built"
        }
    }

    private fun note(text: String) = TextView(this).apply {
        this.text = text
        setTextColor(Color.parseColor("#7A6E71"))
        textSize = 12f
        setPadding(0, 0, 0, dp(10))
    }

    /**
     * Back to the build screen, which already knows how to do the least work
     * that would apply the change.
     */
    private fun rebuild() {
        if (!canNavigateNow()) return
        try {
            startActivity(Intent(this, SetupActivity::class.java))
            finish()
        } catch (t: Throwable) {
            Toast.makeText(this, "Could not open the build screen: ${t.message}", Toast.LENGTH_LONG).show()
        }
    }

    private fun close() {
        if (canNavigateNow()) finish()
    }

    private fun canNavigateNow(): Boolean {
        if (!::mutationSession.isInitialized || mutationSession.canNavigate()) return true
        Toast.makeText(this, "Finishing the mod switch first", Toast.LENGTH_SHORT).show()
        return false
    }

    @Suppress("DEPRECATION", "OVERRIDE_DEPRECATION")
    override fun onBackPressed() {
        if (canNavigateNow()) super.onBackPressed()
    }

    // ── installing one ─────────────────────────────────────────────────────

    private fun pick() {
        if (!canNavigateNow()) return
        if (importRunning || ModImport.isBusy()) {
            Toast.makeText(this, "A mod import is already running", Toast.LENGTH_SHORT).show()
            return
        }
        try {
            startActivityForResult(ModImport.intent(), PICK_FOLDER)
        } catch (t: Throwable) {
            Toast.makeText(this, "No folder picker on this device: ${t.message}", Toast.LENGTH_LONG).show()
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != PICK_FOLDER) return
        val uri = data?.data
        if (resultCode != RESULT_OK || uri == null) return
        import(uri)
    }

    private sealed interface ImportOutcome {
        data class Installed(val result: ModImport.Result) : ImportOutcome
        data class Confirm(val plan: ModImport.ImportPlan) : ImportOutcome
        data class Failed(val failure: Throwable) : ImportOutcome
    }

    private fun import(
        uri: android.net.Uri,
        requested: ModImport.ImportPlan? = null,
        confirmed: Boolean = false,
    ) {
        if (importRunning || ModImport.isBusy()) {
            Toast.makeText(this, "A mod import is already running", Toast.LENGTH_SHORT).show()
            return
        }
        setImportBusy(true, "Preparing the selected folder…")
        scope.launch {
            val outcome = withContext(Dispatchers.IO) {
                try {
                    val plan = requested ?: ModImport.prepare(this@ModsActivity, uri, mods)
                    if (plan.requiresConfirmation && !confirmed) {
                        ImportOutcome.Confirm(plan)
                    } else {
                        ImportOutcome.Installed(
                            ModImport.copy(
                                this@ModsActivity,
                                uri,
                                mods,
                                requested = plan,
                                confirmed = confirmed,
                                checkCancelled = { coroutineContext.ensureActive() },
                            ) { progress ->
                                scope.launch {
                                    if (!isFinishing && !isDestroyed) {
                                        setImportBusy(
                                            true,
                                            "${progress.files} files, ${progress.bytes / 1024} KB — ${progress.message}",
                                        )
                                    }
                                }
                            },
                        )
                    }
                } catch (confirmation: ModImport.ReplacementConfirmationRequired) {
                    ImportOutcome.Confirm(confirmation.plan)
                } catch (failure: Throwable) {
                    ImportOutcome.Failed(failure)
                }
            }
            if (isFinishing || isDestroyed) return@launch
            setImportBusy(false)
            when (outcome) {
                is ImportOutcome.Installed -> {
                    refresh()
                    offerRebuild(outcome.result)
                }
                is ImportOutcome.Confirm -> confirmReplacement(uri, outcome.plan)
                is ImportOutcome.Failed -> showImportFailure(outcome.failure)
            }
        }
    }

    private fun setImportBusy(busy: Boolean, message: String = "") {
        importRunning = busy
        updateActionAvailability()
        importProgress.visibility = if (busy) View.VISIBLE else View.GONE
        importProgress.text = message
    }

    private fun setMutationBusy(busy: Boolean) {
        mutationRunning = busy
        updateActionAvailability()
    }

    private fun updateActionAvailability() {
        importButton.isEnabled = !importRunning && !mutationRunning
        rebuildButton.isEnabled = !mutationRunning
        closeButton.isEnabled = !mutationRunning
    }

    private fun confirmReplacement(uri: android.net.Uri, plan: ModImport.ImportPlan) {
        android.app.AlertDialog.Builder(this)
            .setTitle("Replace ${plan.targetName}?")
            .setMessage(
                "That existing folder has no verified import identity. Replace it only if it is " +
                    "the earlier copy of ${plan.displayName}.",
            )
            .setPositiveButton("Replace") { _, _ -> import(uri, plan, confirmed = true) }
            .setNegativeButton("Cancel", null)
            .show()
    }

    private fun showImportFailure(failure: Throwable) {
        LauncherLog.log("mods: import failed: $failure")
        android.app.AlertDialog.Builder(this)
            .setTitle("That folder could not be installed")
            .setMessage(failure.message ?: failure.toString())
            .setPositiveButton("OK", null)
            .show()
    }

    /**
     * The offer, not the decision -- as on the launch path. A rebuild is
     * several minutes, and somebody installing three mods in a row wants to be
     * asked once at the end rather than three times.
     */
    private fun offerRebuild(result: ModImport.Result) {
        val what = if (result.plugins == 1) "1 assembly" else "${result.plugins} assemblies"
        android.app.AlertDialog.Builder(this)
            .setTitle("Installed ${result.name}")
            .setMessage(
                "$what copied in.\n\n" +
                    "Mods are compiled into the game, so it has to be built again before this " +
                    "one does anything. That takes a few minutes; only what changed is redone.\n\n" +
                    "Rebuild now, or carry on and do it later?",
            )
            .setPositiveButton("Rebuild now") { _, _ -> rebuild() }
            .setNegativeButton("Later", null)
            .show()
    }

    private fun button(label: String, primary: Boolean = false, onClick: () -> Unit) =
        Button(this).apply {
            text = label
            setOnClickListener { onClick() }
            backgroundTintList = android.content.res.ColorStateList.valueOf(
                Color.parseColor(if (primary) "#7D3341" else "#B4AEB2"),
            )
            setTextColor(if (primary) Color.WHITE else Color.parseColor("#0D0A0B"))
            setPadding(paddingLeft, dp(12), paddingRight, dp(12))
        }

    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
}
