// SettingsActivity — a tiny standalone Activity for the launcher's
// user-tweakable behaviour flags. Currently:
//
//   Auto-pull before game launch — when enabled AND a Steam token is
//     on file, tapping Launch runs a pull FIRST and only then starts
//     the game, so the player always boots on their freshest cloud
//     save — the same model Steam uses. analyzePull downloads files
//     where cloud is strictly newer; if a local save is newer than the
//     cloud, the conflict dialog asks the user to keep local or keep
//     remote (or cancel, which aborts the launch).
//
//   Auto-push on game exit — when enabled AND a Steam token is on
//     file, the launcher runs a `safe push` the next time it gains
//     focus after the user returned from playing the game. A conflict
//     (cloud newer than local) raises the same keep-local / keep-remote
//     dialog as the manual Push button; Cancel aborts, nothing uploads.
//
//   Show perf overlay — toggles the in-game OnGUI HUD.
//
// All toggles default to OFF so the launcher behaves identically to
// the manual-button-only baseline until the user opts in. Persisted
// via SettingsStore (SharedPreferences).

package dev.silksong.launcher

import android.app.Activity
import android.os.Bundle
import android.widget.Button
import android.widget.Switch

class SettingsActivity : Activity() {

    private lateinit var settings: SettingsStore
    private lateinit var swAutoPull: Switch
    private lateinit var swAutoPush: Switch
    private lateinit var swPerfOverlay: Switch
    private lateinit var swSkipIntro: Switch
    private lateinit var swDualScreen: Switch

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_settings)
        settings = SettingsStore(this)

        swAutoPull = findViewById(R.id.sw_auto_pull)
        swAutoPush = findViewById(R.id.sw_auto_push)
        swPerfOverlay = findViewById(R.id.sw_perf_overlay)
        swSkipIntro = findViewById(R.id.sw_skip_intro)
        swDualScreen = findViewById(R.id.sw_dual_screen)

        val btnBack: Button = findViewById(R.id.btn_settings_back)

        swAutoPull.isChecked = settings.autoPull
        swAutoPush.isChecked = settings.autoPush
        swPerfOverlay.isChecked = settings.perfOverlay
        swSkipIntro.isChecked = settings.skipIntro
        swDualScreen.isChecked = settings.dualScreen

        // Persist on every toggle — no separate Save button; the
        // settings screen is tiny enough that "click to toggle" is
        // its own commit. setOnCheckedChangeListener fires for the
        // initial isChecked = ... assignment too, so set listeners
        // AFTER seeding the initial state to avoid logging spurious
        // writes.
        swAutoPull.setOnCheckedChangeListener { _, checked ->
            settings.autoPull = checked
            LauncherLog.log("Settings: auto-pull → $checked")
        }
        swAutoPush.setOnCheckedChangeListener { _, checked ->
            settings.autoPush = checked
            LauncherLog.log("Settings: auto-push → $checked")
        }
        swPerfOverlay.setOnCheckedChangeListener { _, checked ->
            settings.perfOverlay = checked
            LauncherLog.log("Settings: perf overlay → $checked (takes effect on next game launch)")
        }
        swSkipIntro.setOnCheckedChangeListener { _, checked ->
            settings.skipIntro = checked
            LauncherLog.log("Settings: skip intro → $checked (takes effect on next game launch)")
        }
        swDualScreen.setOnCheckedChangeListener { _, checked ->
            settings.dualScreen = checked
            LauncherLog.log("Settings: dual screen → $checked (next game launch; requires DualScreen build)")
        }

        btnBack.setOnClickListener { finish() }
    }
}
