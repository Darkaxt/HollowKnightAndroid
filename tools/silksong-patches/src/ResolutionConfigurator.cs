// ResolutionConfigurator — the frame cap, and a sensible starting resolution.
//
// ── the resolution ──────────────────────────────────────────────────────────
//
// This used to be a launcher setting: pick 720p/900p/1080p/native in Settings,
// and every boot would force it with Screen.SetResolution. That is gone, and
// the reason it could go is worth writing down, because it was not obvious.
//
// Unity persists the resolution ON ANDROID. After a Screen.SetResolution the
// player prefs carry
//
//     Screenmanager Resolution Width  = 1280
//     Screenmanager Resolution Height = 720
//     Screenmanager Fullscreen mode   = 1
//
// and the engine restores them on the next launch. So there is nothing to
// re-apply: forcing it every boot was not making it stick, it was overwriting
// whatever the player had chosen since.
//
// What is left is a default. 720p, applied exactly once, on a device that has
// never run this before -- roughly half the pixels of a 1080p panel, which is
// most of a battery saving on art that tolerates the downscale. After that the
// game owns it, including through its own resolution menu, and nothing here
// touches it again.
//
// Nothing is clamped any more either. The old code refused to set anything at
// or above the panel's short dimension, which meant the highest modes were
// unreachable by design; the panel's own modes are exactly what the game's
// menu offers, and they should work.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

public static class ResolutionConfigurator
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        ApplyFrameRate();
        ApplyDefaultResolution();
    }

    /**
     * The frame cap, held rather than set, and vsync held off with it.
     *
     * The game stores its cap in PlayerPrefs as VidTFR and offers four values:
     * -1 ("off"), 30, 60 and 120, plus the panel's own rate when that exceeds
     * 120. -1 means "uncapped" on a desktop, but Unity reads a negative target
     * as its mobile default of 30, so the option meant to remove the limit is
     * the one that imposes the worst one.
     *
     * The other half is VSync, and it is the larger one. The game's VSync
     * option does not merely enable vsync -- MenuSetting.UpdateSetting, case
     * VSync, does this when it is switched on:
     *
     *     Platform.Current.VSyncCount = 1;
     *     Application.targetFrameRate = -1;
     *     UIManager.instance.DisableFrameCapSetting();
     *
     * So turning vsync on sets the same -1 sentinel, and disables the frame cap
     * control so it cannot be set back. On Android that is a request for 30 fps
     * whatever the cap says -- and vsync is ON by default, both in
     * GameSettings (LoadInt("VidVSync", ref vSync, 1)) and in the project's own
     * QualitySettings.asset (vSyncCount: 1).
     *
     * There is no vsync-on path that reaches 120 here, because Unity ignores
     * targetFrameRate entirely while vSyncCount is non-zero and the game only
     * ever pairs vsync with -1. Holding vsync off and driving the rate from
     * targetFrameRate is the only arrangement in which the cap means anything,
     * which is why this does not try to preserve a vsync choice.
     *
     * THREE approaches failed before this one. Setting targetFrameRate once at
     * startup loses, because the game re-applies its own settings from menu
     * code. Writing VidTFR instead loses too: measured on the device, the patch
     * logged "frame cap -1 -> 120" every launch and the prefs file still read
     * -1 afterwards, because the game writes its in-memory value back after
     * ours. So both values are GUARDED instead -- see FrameCapHolder, which
     * needs no stored state and no timer to do it. The prefs are still written,
     * because it costs nothing and makes the menu agree when it is read fresh.
     *
     * A deliberate 30 or 60 is still respected -- that is a real choice about
     * battery. Only the sentinel is corrected.
     */
    const string PREF_FRAME_CAP = "VidTFR";
    const string PREF_VSYNC = "VidVSync";

    /**
     * The largest cap the game will accept, worked out the way it works it out.
     *
     * Its list is a fixed set topped at 120, plus the panel's own rate when
     * that is higher -- and it derives that rate by taking the maximum over
     * every mode in Screen.resolutions, not the mode currently in use. Those
     * two can differ: a 120 Hz panel can report a 60 Hz current mode while
     * still offering 120. Asking the same question the same way is what keeps
     * our answer inside the set it will accept, on hardware nobody here has.
     */
    static int LargestAcceptedCap()
    {
        int max = 0;
        foreach (var r in Screen.resolutions)
        {
            int hz = (int)Mathf.Round((float)r.refreshRateRatio.value);
            if (hz > max) max = hz;
        }
        if (max <= 0) max = (int)Mathf.Round((float)Screen.currentResolution.refreshRateRatio.value);
        if (max <= 0) max = 60;
        // Above 120 the panel's exact rate joins the list; at or below, the
        // list stops at 120 and the game clamps to it anyway.
        return max > 120 ? max : 120;
    }

    static void ApplyFrameRate()
    {
        try
        {
            int cap = LargestAcceptedCap();

            int stored = PlayerPrefs.GetInt(PREF_FRAME_CAP, -1);
            if (stored <= 0)
            {
                // Still written, so the options menu shows something true when
                // it reads the pref fresh. Not relied on: see the note above.
                PlayerPrefs.SetInt(PREF_FRAME_CAP, cap);
                Debug.Log($"[ResolutionConfigurator] frame cap {stored} -> {cap} (held at {cap})");
            }
            else
            {
                Debug.Log($"[ResolutionConfigurator] frame cap {stored} (kept; largest is {cap})");
            }

            // Likewise cosmetic, and for a better reason: with vsync showing as
            // on, the menu disables the frame cap control, so a player cannot
            // change the cap without first turning off a setting we are already
            // ignoring.
            if (PlayerPrefs.GetInt(PREF_VSYNC, 1) != 0) PlayerPrefs.SetInt(PREF_VSYNC, 0);
            PlayerPrefs.Save();

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = stored > 0 ? stored : cap;

            FrameCapHolder.Install(cap);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ResolutionConfigurator] couldn't set the frame rate: " + ex.Message);
        }
    }

    /**
     * 720p, once, on a device that has never run this before.
     *
     * The marker is ours rather than Unity's. Unity writes its own
     * "Screenmanager Resolution Width" on first run too -- with the panel's
     * native size -- so its presence says nothing about whether anybody has
     * chosen anything. A key only this code writes is the only way to tell
     * "never been here" from "been here, and the player picked native".
     *
     * After this has run once it never runs again, and the resolution belongs
     * to the game: its own menu writes Screenmanager Resolution Width/Height,
     * Unity restores them at boot, and nothing here interferes.
     */
    const string PREF_DEFAULT_APPLIED = "SilksongAndroidDefaultRes";
    const int DEFAULT_SHORT_SIDE = 720;

    static void ApplyDefaultResolution()
    {
        try
        {
            if (PlayerPrefs.GetInt(PREF_DEFAULT_APPLIED, 0) != 0)
            {
                Debug.Log($"[ResolutionConfigurator] resolution is the game's: {Screen.width}x{Screen.height}");
                return;
            }

            var native = Screen.currentResolution;
            int shortNative = Mathf.Min(native.width, native.height);
            int longNative = Mathf.Max(native.width, native.height);

            // A panel already at or below the default is left alone: there is
            // nothing to save and scaling UP would be worse than doing nothing.
            if (shortNative > DEFAULT_SHORT_SIDE)
            {
                float ratio = (float)DEFAULT_SHORT_SIDE / shortNative;
                int targetLong = Mathf.RoundToInt(longNative * ratio);
                bool landscape = native.width >= native.height;
                int width = landscape ? targetLong : DEFAULT_SHORT_SIDE;
                int height = landscape ? DEFAULT_SHORT_SIDE : targetLong;

                Screen.SetResolution(width, height, true);
                Debug.Log(
                    $"[ResolutionConfigurator] first run: defaulting to {width}x{height} " +
                    $"(panel {native.width}x{native.height}). Change it in the game's video options.");
            }
            else
            {
                Debug.Log(
                    $"[ResolutionConfigurator] first run: panel is {native.width}x{native.height}, " +
                    "already at or below the default; leaving it alone");
            }

            // Written whether or not the resolution was changed. The question
            // it answers is "has the default been decided", and it has been.
            PlayerPrefs.SetInt(PREF_DEFAULT_APPLIED, 1);
            PlayerPrefs.Save();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ResolutionConfigurator] couldn't set the resolution: " + ex.Message);
        }
    }
}

/**
 * Keeps vsync off and the frame cap off the broken sentinel.
 *
 * Neither value can be set once and left. The game re-applies both from menu
 * code (MenuSetting.UpdateSetting), from Platform.SetTargetFrameRate, and from
 * Platform.RestoreFrameRate after a video; QualitySettings.SetQualityLevel also
 * resets vSyncCount to the project default, which is 1. None of those is
 * reachable from here -- the patches are an additional assembly compiled
 * against the game, not a rewrite of it -- so there is nothing to subscribe to
 * and the values have to be guarded rather than assigned.
 *
 * The guard is two engine property reads per frame and nothing else. There is
 * deliberately no timer and no PlayerPrefs lookup: an earlier version polled
 * the pref every three seconds, which was both more expensive per check and
 * wrong, because it read the stored cap to decide whether to act and our own
 * startup code had just written a positive value into it -- so it returned
 * early forever and never corrected anything.
 *
 * The test that works needs no stored state at all. The game applies the
 * player's chosen cap by assigning Application.targetFrameRate directly, so a
 * positive value already IS their choice, whatever it is, and is left alone. A
 * value at or below zero can only be the "off" sentinel, which Unity reads on
 * Android as 30. Replacing just that is the whole job, and it happens within a
 * frame rather than within three seconds.
 */
public class FrameCapHolder : MonoBehaviour
{
    static FrameCapHolder _instance;
    int _cap;

    public static void Install(int cap)
    {
        if (_instance != null) { _instance._cap = cap; return; }

        var go = new GameObject("__FrameCapHolder__");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<FrameCapHolder>();
        _instance._cap = cap;
    }

    void Update()
    {
        // Off, always. The game's vsync option is not a vsync option: switching
        // it on also sets the target to -1 and disables the frame cap control,
        // so on Android it means 30 fps and no way back to the menu that would
        // change it.
        if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;

        if (Application.targetFrameRate <= 0) Application.targetFrameRate = _cap;
    }
}
#endif
