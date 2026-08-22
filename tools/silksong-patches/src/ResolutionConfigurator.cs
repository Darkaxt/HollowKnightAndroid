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
     *
     * ALWAYS landscape. Android reports this panel as 1080x1920 -- portrait,
     * the orientation the hardware is mounted in -- so deriving the target's
     * orientation from the panel produces a portrait render target for a game
     * that only ever runs landscape. The long side is the width here, full
     * stop, and the same assumption is what ResolutionGuard enforces for the
     * resolutions the game's own menu offers.
     */
    const string PREF_DEFAULT_APPLIED = "SilksongAndroidDefaultRes";
    const int DEFAULT_SHORT_SIDE = 720;

    static void ApplyDefaultResolution()
    {
        try
        {
            ResolutionGuard.Install();
            ResolutionMenuOptions.Install();

            if (PlayerPrefs.GetInt(PREF_DEFAULT_APPLIED, 0) != 0)
            {
                Debug.Log($"[ResolutionConfigurator] resolution is the game's: {Screen.width}x{Screen.height}");
                return;
            }

            int longSide, shortSide;
            if (!ResolutionMenuOptions.TryPanel(out longSide, out shortSide))
            {
                // No usable geometry yet. Not marked as decided, so the next
                // launch gets another go rather than silently keeping whatever
                // Unity picked.
                Debug.LogWarning("[ResolutionConfigurator] no panel geometry yet; leaving the resolution alone");
                return;
            }

            // A panel already at or below the default is left alone: there is
            // nothing to save, and scaling UP would be worse than doing nothing.
            if (shortSide > DEFAULT_SHORT_SIDE)
            {
                // Derived through the same pair of helpers the menu uses, so
                // this lands exactly on one of its rows rather than a pixel
                // beside it.
                int width = ResolutionMenuOptions.WidthFor(longSide, shortSide, DEFAULT_SHORT_SIDE);
                Screen.SetResolution(width, DEFAULT_SHORT_SIDE, true);
                Debug.Log(
                    $"[ResolutionConfigurator] first run: defaulting to {width}x{DEFAULT_SHORT_SIDE} " +
                    $"(panel {longSide}x{shortSide}). Change it in the game's video options.");
            }
            else
            {
                Debug.Log(
                    $"[ResolutionConfigurator] first run: panel is {longSide}x{shortSide}, " +
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
 * Puts the resolutions the player actually wants into the game's own menu.
 *
 * Silksong builds its resolution list from Screen.resolutions
 * (MenuResolutionSetting.RefreshAvailableResolutions). On Android that array
 * describes the panel as it is mounted rather than as it is held -- one entry,
 * 1080x1920, portrait -- so the menu offers a single unusable option. The one
 * thing that saves it is a fallback in RefreshCurrentIndex: a current
 * resolution missing from the list gets prepended. That is why the menu showed
 * exactly two entries, whichever resolution happened to be running plus the
 * portrait one, and why choosing 1080p made 720p disappear.
 *
 * So the list is replaced with the sizes this panel can sensibly render: its
 * native landscape mode, then 900 and 720, each keeping the panel's aspect
 * ratio. Everything downstream is the game's own code and needs no help --
 * PushUpdateOptionList formats the labels, ApplySettings indexes the same array
 * we wrote, and Unity persists the result.
 *
 * The array is private, so it is set by reflection. The alternative was to
 * rewrite the menu, and this is the smaller lie: one field, restored to the
 * shape the game already expects, with every method that reads it left alone.
 * It is re-applied whenever the pane is opened, because RefreshControls runs on
 * enable and overwrites it -- and it fails silently, leaving the game's own
 * behaviour, if the field ever stops being there.
 *
 * Entries carry the CURRENT refresh rate, so the running resolution matches one
 * of them exactly (Resolution.Equals compares the rate too). Without that,
 * RefreshCurrentIndex would decide the current mode is missing and prepend a
 * near-duplicate of a row already on screen.
 */
public class ResolutionMenuOptions : MonoBehaviour
{
    const float CHECK_SECONDS = 0.25f;
    // Scaled-down short sides, offered when the panel is taller than each. Not
    // a fixed set of sizes: they are derived from the panel's own shape, so a
    // 21:9 outer screen and a 6:5 inner one each get their own widths.
    //
    // The range is deliberately wide. This runs on everything from a 720p
    // handheld to a 1440p foldable, the whole point of a lower resolution is
    // battery, and only the player knows what they are trading. Each is
    // dropped when the panel is not taller than it, so a 1080p phone sees
    // three of these and a 720p one sees none.
    static readonly int[] ShortSides = { 1440, 1200, 1080, 900, 810, 720, 600, 540 };
    // Enough for any real panel -- and a device that enumerates more modes
    // than this is enumerating refresh variants we have already collapsed.
    const int MaxNativeEntries = 12;

    static ResolutionMenuOptions _instance;
    static System.Reflection.FieldInfo _field;
    static bool _lookedUp;
    static bool _warned;

    float _next;

    public static void Install()
    {
        if (_instance != null) return;
        var go = new GameObject("__ResolutionMenuOptions__");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ResolutionMenuOptions>();
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + CHECK_SECONDS;

        try
        {
            var ui = UIManager.instance;
            if (ui == null) return;
            var opt = ui.resolutionOption;
            if (opt == null || !opt.isActiveAndEnabled) return;

            var field = Field();
            if (field == null) return;

            var wanted = BuildList(opt.currentRes);
            if (wanted == null || wanted.Length == 0) return;

            // Asked of the array itself rather than of the labels beside it.
            // Comparing list LENGTHS would answer wrongly on a panel small
            // enough that our list is one entry, which is exactly the size the
            // game's own list is -- and then this would never run at all.
            if (Same(field.GetValue(opt) as Resolution[], wanted)) return;

            field.SetValue(opt, wanted);
            opt.RefreshCurrentIndex();
            opt.PushUpdateOptionList();
            // Public, and the only route to the protected UpdateText that makes
            // the new label actually appear.
            opt.SetOptionTo(opt.selectedOptionIndex);
        }
        catch (System.Exception e)
        {
            // Once. A failure here leaves the game's own menu working, so it is
            // not worth a line every quarter second.
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[ResolutionMenuOptions] leaving the game's own list alone: " + e.Message);
        }
    }

    static bool Same(Resolution[] a, Resolution[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i].width != b[i].width || a[i].height != b[i].height) return false;
        return true;
    }

    static System.Reflection.FieldInfo Field()
    {
        if (_lookedUp) return _field;
        _lookedUp = true;
        _field = typeof(UnityEngine.UI.MenuResolutionSetting).GetField(
            "availableResolutions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (_field == null)
            Debug.LogWarning("[ResolutionMenuOptions] no availableResolutions field; menu left as the game built it");
        return _field;
    }

    /// <summary>
    /// The panel's largest mode, as landscape. False if Unity reports nothing.
    ///
    /// Shared with the first-run default in ResolutionConfigurator, so that the
    /// resolution chosen at boot is derived exactly the way the menu's entries
    /// are. When these drifted apart -- the default rounding one way and the
    /// menu the other -- a panel whose aspect is not tidy got a boot resolution
    /// one pixel off every row in its own menu, and the game prepended a
    /// near-duplicate to say so.
    /// </summary>
    public static bool TryPanel(out int longSide, out int shortSide)
    {
        longSide = 0;
        shortSide = 0;

        var modes = Screen.resolutions;
        for (int i = 0; i < modes.Length; i++)
        {
            int lo = Mathf.Max(modes[i].width, modes[i].height);
            int sh = Mathf.Min(modes[i].width, modes[i].height);
            if (lo > longSide) { longSide = lo; shortSide = sh; }
        }

        var c = Screen.currentResolution;
        int clo = Mathf.Max(c.width, c.height), csh = Mathf.Min(c.width, c.height);
        if (clo > longSide) { longSide = clo; shortSide = csh; }

        return longSide > 0 && shortSide > 0;
    }

    /// <summary>
    /// The width that pairs with <paramref name="targetShort"/> on this panel.
    ///
    /// Even, because the arithmetic lands on an odd number whenever the aspect
    /// is not tidy and an odd render target is legal but awkward.
    /// </summary>
    public static int WidthFor(int longSide, int shortSide, int targetShort)
    {
        int w = Mathf.RoundToInt((float)longSide * targetShort / Mathf.Max(shortSide, 1));
        if ((w & 1) != 0) w++;
        return w;
    }

    /// <summary>
    /// Every mode the panel reports, plus the scaled-down ones, all landscape.
    ///
    /// The panel's own modes are kept in full rather than reduced to the
    /// largest. A foldable has two displays with genuinely different shapes --
    /// a squarish inner one and a long narrow cover -- and Android reports
    /// whichever is open; on a device with several modes, "the biggest" is not
    /// the only one worth offering. Folding changes the array, and this is
    /// recomputed while the pane is open, so the list follows.
    ///
    /// The scaled sizes hold the panel's aspect ratio rather than assuming
    /// 16:9, because on the shapes above 16:9 is simply wrong.
    /// </summary>
    static Resolution[] BuildList(Resolution current)
    {
        var natives = new System.Collections.Generic.List<Resolution>();

        int bestLong, bestShort;
        if (!TryPanel(out bestLong, out bestShort)) return null;

        var modes = Screen.resolutions;
        for (int i = 0; i < modes.Length; i++)
            AddSize(natives, modes[i].width, modes[i].height, current);

        // What is running need not be in that array at all, and it is the one
        // entry the menu cannot do without: RefreshCurrentIndex prepends a
        // duplicate of it otherwise.
        var c = Screen.currentResolution;
        AddSize(natives, c.width, c.height, current);

        // Largest first, then capped -- before the scaled entries are added, so
        // a device with a long mode list cannot crowd them out.
        natives.Sort(ByArea);
        if (natives.Count > MaxNativeEntries)
            natives.RemoveRange(MaxNativeEntries, natives.Count - MaxNativeEntries);

        for (int i = 0; i < ShortSides.Length; i++)
        {
            int target = ShortSides[i];
            if (target >= bestShort) continue;
            int w = WidthFor(bestLong, bestShort, target);
            // A panel whose short side is 904 would otherwise be offered
            // "2306x900" next to its own "2316x904": two names for the same
            // picture, one of them wrong. Anything within a few percent of a
            // size already in the list is not a choice, it is noise.
            if (TooClose(natives, w, target)) continue;
            AddSize(natives, w, target, current);
        }

        natives.Sort(ByArea);
        return natives.ToArray();
    }

    /// <summary>Is this size close enough to one already listed to be indistinguishable?</summary>
    static bool TooClose(System.Collections.Generic.List<Resolution> list, int width, int height)
    {
        const float Tolerance = 0.03f;
        for (int i = 0; i < list.Count; i++)
        {
            float dw = Mathf.Abs(list[i].width - width) / (float)Mathf.Max(list[i].width, 1);
            float dh = Mathf.Abs(list[i].height - height) / (float)Mathf.Max(list[i].height, 1);
            if (dw < Tolerance && dh < Tolerance) return true;
        }
        return false;
    }

    static int ByArea(Resolution a, Resolution b)
    {
        return (b.width * b.height).CompareTo(a.width * a.height);
    }

    /// <summary>Adds one size as landscape, if that size is not already there.</summary>
    static void AddSize(System.Collections.Generic.List<Resolution> into,
                        int width, int height, Resolution current)
    {
        if (width <= 0 || height <= 0) return;

        // Landscape, always: the game only runs landscape, and Android reports
        // some panels the way they are mounted rather than the way they are
        // held. See ResolutionGuard.
        int w = Mathf.Max(width, height), h = Mathf.Min(width, height);
        for (int i = 0; i < into.Count; i++)
            if (into[i].width == w && into[i].height == h) return;

        into.Add(Make(w, h, current));
    }

    static Resolution Make(int width, int height, Resolution current)
    {
        return new Resolution
        {
            width = width,
            height = height,
            refreshRateRatio = current.refreshRateRatio,
        };
    }
}

/**
 * Turns a portrait resolution back into the landscape one the player meant.
 *
 * Android reports this hardware the way it is mounted, not the way it is held:
 * Screen.resolutions on the Thor contains 1080x1920, portrait. Silksong's own
 * video options build their list straight from that array
 * (MenuResolutionSetting.RefreshAvailableResolutions), so the menu offers
 * "1080 x 1920" and there is no 1920x1080 in it at all. Choosing it hands
 * Screen.SetResolution a portrait target for a game that only runs landscape.
 *
 * The full-resolution option therefore could not be reached from the menu --
 * which is what the launcher's old "Native" setting used to provide, by
 * setting nothing at all and letting Unity keep the panel's real landscape
 * mode.
 *
 * So the resolution is transposed after the fact rather than the menu being
 * rewritten. The game owns that list and rebuilds it whenever the pane opens;
 * fighting it there would mean reaching into a private array through
 * reflection and losing to the next refresh. Watching the outcome instead is
 * both smaller and harder to get wrong: a portrait render target is always
 * wrong here, whoever asked for it.
 *
 * The last correction is remembered so that a device which genuinely refuses
 * to leave portrait is asked exactly once rather than every tick.
 */
public class ResolutionGuard : MonoBehaviour
{
    const float CHECK_SECONDS = 0.5f;

    static ResolutionGuard _instance;
    float _next;
    int _lastW, _lastH;

    public static void Install()
    {
        if (_instance != null) return;
        var go = new GameObject("__ResolutionGuard__");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ResolutionGuard>();
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + CHECK_SECONDS;

        int w = Screen.width, h = Screen.height;
        if (h <= w) return;

        // Already tried this exact one. Either the correction did not take or
        // something is re-applying it, and repeating it every half second would
        // turn a cosmetic problem into a flickering one.
        if (w == _lastW && h == _lastH) return;
        _lastW = w; _lastH = h;

        Debug.Log($"[ResolutionGuard] {w}x{h} is portrait; using {h}x{w}");
        Screen.SetResolution(h, w, true);
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
