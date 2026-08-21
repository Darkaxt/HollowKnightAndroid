// SettingsStore — thin SharedPreferences wrapper for the launcher's
// user-tweakable behaviour flags. Kept deliberately tiny: one prefs
// file, raw key/value access, no observer plumbing — Settings UI
// reads/writes directly and the consumers (LauncherActivity) re-read
// on every relevant lifecycle event.
//
// Default for both flags is OFF so the launcher behaves identically
// to its current manual-only operation until the user opts in. We
// store them under a dedicated `launcher_settings` prefs file rather
// than mixing into TokenStore's `launcher_tokens` to keep secrets
// and behaviour flags on separate disk-level lifecycles (clearing
// the token shouldn't wipe sync preferences, and vice-versa).

package dev.silksong.launcher

import android.content.Context
import android.content.SharedPreferences
import java.io.File

class SettingsStore(context: Context) {

    private val prefs: SharedPreferences =
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var autoPull: Boolean
        get() = prefs.getBoolean(KEY_AUTO_PULL, false)
        set(value) { prefs.edit().putBoolean(KEY_AUTO_PULL, value).apply() }

    var autoPush: Boolean
        get() = prefs.getBoolean(KEY_AUTO_PUSH, false)
        set(value) { prefs.edit().putBoolean(KEY_AUTO_PUSH, value).apply() }

    /**
     * When enabled, an OnGUI overlay rendered in the Unity game shows
     * live FPS / CPU / GPU / battery stats in the top-left corner.
     * Read from the launcher process here for the UI; the actual
     * overlay lives in PerfOverlay.cs (Unity side) which reads the
     * SAME SharedPreferences key via JNI at startup. We share the
     * underlying prefs file because the launcher process
     * (`:launcher`) and the game process (default) share package
     * data even though they run in separate processes.
     */
    var perfOverlay: Boolean
        get() = prefs.getBoolean(KEY_PERF_OVERLAY, false)
        set(value) { prefs.edit().putBoolean(KEY_PERF_OVERLAY, value).apply() }

    /**
     * When enabled, skip Silksong's startup intro (studio logos +
     * opening quote) and go straight to loading the main menu. Read
     * via JNI from the game process at boot from this SAME prefs file;
     * the actual skip lives in IntroSkipper.cs (Unity side) which
     * short-circuits StartManager's intro animator to the loading
     * state. The unavoidable loading/save-icon screen still shows
     * briefly while the menu finishes loading. Default OFF (vanilla
     * intro). Takes effect on next game launch.
     */
    var skipIntro: Boolean
        get() = prefs.getBoolean(KEY_SKIP_INTRO, false)
        set(value) { prefs.edit().putBoolean(KEY_SKIP_INTRO, value).apply() }

    /**
     * Render resolution scale. Silksong's render target is created
     * at this resolution (when not [RenderResolution.NATIVE]) and
     * the display engine scales it up to the panel size in
     * hardware. Lower = less fragment shader work, lower GPU
     * power, easier to sustain target framerate. Pixel art / 2D
     * games like Silksong tolerate downscale well — typical user
     * won't notice 900p vs native on a 1080p-class display, and
     * 720p only slightly softens edges.
     *
     * Applied on the Unity side by ResolutionConfigurator.cs which
     * reads the same prefs file via JNI at boot and calls
     * Screen.SetResolution before any scene loads. Setting takes
     * effect on next game launch. Defaults to 720p — the measured
     * battery/thermal sweet spot (~18% lower power vs native at a
     * locked 120fps, with only mild softening on 2D art).
     */
    var renderResolution: RenderResolution
        get() = RenderResolution.fromKey(prefs.getString(KEY_RENDER_RESOLUTION, RenderResolution.P720.key))
        set(value) { prefs.edit().putString(KEY_RENDER_RESOLUTION, value.key).apply() }

    /**
     * Dual-screen support. When enabled, the Unity side (DualScreenV2, one of
     * the patches compiled on the device) activates a secondary display and
     * draws its own inventory / crest / tasks / journal / map screens on it,
     * built from the game's own data and driven by touch. Read via JNI from the
     * game process at boot from this SAME prefs file. Defaults ON, so the
     * switch is for turning it OFF; it is inert on a device with no second
     * display.
     */
    var dualScreen: Boolean
        get() = prefs.getBoolean(KEY_DUAL_SCREEN, true)
        set(value) { prefs.edit().putBoolean(KEY_DUAL_SCREEN, value).apply() }

    /**
     * Hands the game the settings it needs, as a file it can read.
     *
     * The launcher runs in its own process, so the game cannot read these
     * preferences directly -- cross-process SharedPreferences means
     * MODE_MULTI_PROCESS, which is deprecated because it does not reliably
     * work. Written to the app's external files directory because that is
     * what Unity reports as Application.persistentDataPath on this platform,
     * so the patch side needs no package name and no path convention.
     *
     * Only what the GAME acts on. The cloud-save toggles are the launcher's
     * own business and are deliberately not here: a setting the game cannot
     * use is a setting somebody will eventually try to make it use.
     *
     * Written whole every time, immediately before launch, so it cannot drift
     * from what the user last chose.
     */
    fun exportForGame(context: Context) {
        val dir = context.getExternalFilesDir(null) ?: return
        val text = buildString {
            append(KEY_PERF_OVERLAY).append('=').append(perfOverlay).append('\n')
            append(KEY_SKIP_INTRO).append('=').append(skipIntro).append('\n')
            append(KEY_DUAL_SCREEN).append('=').append(dualScreen).append('\n')
            append(KEY_RENDER_RESOLUTION).append('=').append(renderResolution.key).append('\n')
        }
        try {
            val out = File(dir, "game-settings.txt")
            val tmp = File(dir, "game-settings.txt.part")
            tmp.writeText(text)
            if (!tmp.renameTo(out)) {
                tmp.delete()
                throw java.io.IOException("rename to $out")
            }
            LauncherLog.log("settings for the game: ${text.replace('\n', ' ').trim()}")
        } catch (t: Throwable) {
            // Not fatal: the patches fall back to defaults, which is the
            // behaviour the game had before any of this existed.
            LauncherLog.log("could not write the game's settings", t)
        }
    }

    private companion object {
        const val PREFS_NAME = "launcher_settings"
        const val KEY_AUTO_PULL = "auto_pull"
        const val KEY_AUTO_PUSH = "auto_push"
        const val KEY_PERF_OVERLAY = "perf_overlay"
        const val KEY_SKIP_INTRO = "skip_intro"
        const val KEY_RENDER_RESOLUTION = "render_resolution"
        const val KEY_DUAL_SCREEN = "dualscreen_enabled"
    }
}

/**
 * Target render resolution. The display panel always shows full
 * native resolution; this controls what resolution Unity's render
 * target is created at. Smaller = fewer pixels to shade per frame,
 * which is roughly a quadratic perf win (and a similar battery
 * saving) at the cost of edge sharpness.
 *
 * The string [key] values double as the cross-process protocol the
 * Unity-side ResolutionConfigurator.cs reads via JNI — keep in
 * lock-step with the constant strings in that file.
 */
enum class RenderResolution(val key: String) {
    /** Don't override — use the device's full native resolution. */
    NATIVE("native"),
    /** ~1080p (1080-pixel short dim; long dim scales by display aspect). */
    P1080("1080p"),
    /** ~900p — ~30%-fewer pixels than native 1080-class displays. */
    P900("900p"),
    /** ~720p — ~55%-fewer pixels; big GPU/battery saving, mild softness. */
    P720("720p");

    companion object {
        fun fromKey(key: String?): RenderResolution =
            entries.firstOrNull { it.key == key } ?: NATIVE
    }
}
