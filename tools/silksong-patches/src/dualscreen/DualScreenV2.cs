// DualScreenV2 — the second screen's entry point and its lifetime.
//
// The port's second screen used to be a mirror: a capture camera copied the
// game's HUD into a RenderTexture, a GPU readback pulled it into managed
// memory, a worker thread flipped it into a 22 MB shared file, and Java copied
// that into a Bitmap in an Android Presentation. It showed the game's inventory
// only while the inventory was open, letterboxed, because that menu is authored
// for 16:9 and this panel is 1240x1080. All of that is gone.
//
// Unity renders to the panel itself (see DsPresentation), and what it renders
// is ours: a UI built for this screen's shape, from the game's own data. This
// file owns three things and delegates the rest:
//
//   * whether the second screen runs at all,
//   * bringing the panel up and keeping it up across pause, resume and hot-plug,
//   * keeping the two screens' input apart (DsTouch).

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

public class DualScreenV2 : MonoBehaviour
{
    const string KEY_DUAL_SCREEN = "dualscreen_enabled";

    public static DualScreenV2 Instance { get; private set; }

    DsPresentation _screen;
    DsShell _shell;
    DsInput _input;
    DsTestCard _card;
    bool _paused;
    int _displayCount;
    float _nextFence;
    float _nextFontRetry;
    int _fontRevision = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!ShouldRun()) return;
        var go = new GameObject("__DualScreenV2__");
        DontDestroyOnLoad(go);
        go.AddComponent<DualScreenV2>();
    }

    /// <summary>
    /// The second screen runs unless the launcher's setting turns it off.
    /// </summary>
    public static bool ShouldRun()
    {
        return SilksongPatches.Settings.GetBool(KEY_DUAL_SCREEN, true);
    }

    IEnumerator Start()
    {
        Instance = this;
        DsTouch.Enabled = false;      // nothing is filtered until the panel is actually live

        // Watch for displays coming and going BEFORE trying to use one, and
        // keep watching whether or not there is one now.
        //
        // This used to be subscribed only after a successful bringup, which
        // meant a device that started with no second screen never noticed one
        // being attached: the component switched itself off and the panel
        // stayed dead until the app was restarted. On a handheld whose panel
        // detaches, that is a normal thing to do rather than an edge case.
        _displayCount = Display.displays.Length;
        Display.onDisplaysUpdated += OnDisplaysUpdated;

        yield return Bringup();
    }

    /// <summary>
    /// Try to take the second panel. Safe to run again later: with no second
    /// display it does nothing at all and leaves the game exactly as it would
    /// be on a single-screen device.
    /// </summary>
    IEnumerator Bringup()
    {
        if (_screen != null && _screen.Ready) yield break;

        var screen = _screen ?? new DsPresentation(transform);
        yield return screen.Bringup();

        if (!screen.Ready)
        {
            // No second display, or it would not activate. Do nothing at all
            // rather than half of something -- and stay resident, because a
            // panel may still turn up.
            Debug.Log("[DualScreen] no second display; dormant");
            yield break;
        }

        _screen = screen;

        // The test card is the M1 rig: corner markers, a sweeping bar and a
        // touch crosshair, which is how "the surface covers the panel" and
        // "input is separated" were verified. Kept one flag away, because it is
        // the fastest way to tell a rendering problem from a content problem.
        if (_shell == null && _card == null)
        {
            if (DsConfig.Bool("testcard", false))
            {
                _card = new DsTestCard(_screen.Root, _screen.Width, _screen.Height);
            }
            else
            {
                _input = new DsInput();
                BuildShell();
            }
        }

        // Only now does input need separating, and only now is it safe to take
        // touches away from the game.
        DsTouch.Enabled = true;
        DsTouch.InstallFence(gameObject);

        Debug.Log("[DualScreen] ready");
    }

    void BuildShell()
    {
        _shell = new DsShell(_screen.Root, _screen.Width, _screen.Height);
        RegisterScreens(_shell);
        _shell.Finish(DsConfig.Str("screen", "map"));
        _fontRevision = DsTheme.FontRevision;
    }

    // One place, so the rebuild-on-font-found path cannot drift from startup.
    //
    // Registration order is the shared semantic bottom-tab order. Map remains
    // the default by ID, but sits at the right-hand edge like the approved
    // product language requires.
    static void RegisterScreens(DsShell shell)
    {
        shell.Register(new DsInventoryScreen());
        shell.Register(new DsLoadoutScreen());
        shell.Register(new DsTasksScreen());
        shell.Register(new DsJournalScreen());
        shell.Register(new DsMapScreen());
    }

    void Update()
    {
        if (_screen == null || !_screen.Ready) return;

        // The game creates cameras for cutscenes and bosses, and hard-assigns
        // culling masks in places, so our layer is swept off everything else
        // continuously rather than once. Rate-limited inside.
        _screen.SweepCameras();

        // Event systems are rebuilt with scenes, so the fence has to keep being
        // re-applied. InstallFence's fast path is a static read plus a
        // comparison, but the rate limit keeps even that off the hot path.
        if (Time.unscaledTime >= _nextFence)
        {
            _nextFence = Time.unscaledTime + 0.25f;
            DsTouch.InstallFence(gameObject);
        }

        float dt = Time.unscaledDeltaTime;

        if (_card != null) { _card.Tick(); return; }
        if (_shell == null) return;

        DsProbe.MaybeRun();

        // The complete game font set is not resident when we start. DsTheme
        // first exposes any safe fallback it can find, then upgrades to the
        // proper display/prose pair as Addressables load. Existing TMP labels
        // retain their assigned font, so rebuild whenever that choice changes.
        if (Time.unscaledTime >= _nextFontRetry)
        {
            _nextFontRetry = Time.unscaledTime + 2f;
            int revision = DsTheme.FontRevision;
            if (DsTheme.HasFont && revision != _fontRevision)
            {
                Debug.Log("[DualScreen] game fonts changed — rebuilding shell");
                RebuildShell();
                _fontRevision = DsTheme.FontRevision;
            }
        }

        if (_input != null)
        {
            _input.Poll();
            var gestures = _input.Gestures;
            for (int i = 0; i < gestures.Count; i++) _shell.OnGesture(gestures[i]);
        }

        // Outside a save, the panel shows the game's title instead of the tabs.
        //
        // The grace applies only to LEAVING gameplay. DsGameData.InGame goes
        // false for a few frames during any scene load -- there is no hero
        // mid-transition -- and slamming the title card up every time the player
        // walks through a door would be worse than the thing it replaces. But
        // before the first time we have ever been in game there is nothing to
        // protect, and waiting there just means opening on an empty Inventory
        // for the length of the grace. Returning to gameplay is always
        // immediate.
        bool inGame = DsGameData.InGame;
        if (inGame) { _idleSince = -1f; _everInGame = true; }
        else if (_idleSince < 0f) _idleSince = Time.unscaledTime;

        bool settled = !_everInGame || Time.unscaledTime - _idleSince >= IDLE_GRACE;
        _shell.SetIdle(!inGame && settled);

        _shell.Tick(dt);
    }

    const float IDLE_GRACE = 0.75f;
    float _idleSince = -1f;
    bool _everInGame;

    // Rebuild when a better game font becomes resident. This is cheaper and
    // simpler than teaching every widget to swap its font independently, and
    // normally happens only as startup fallbacks give way to the menu fonts.
    void RebuildShell()
    {
        string keep = _shell != null ? _shell.ActiveId : null;
        var root = _screen.Root;
        for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
        _shell = new DsShell(root, _screen.Width, _screen.Height);
        RegisterScreens(_shell);
        _shell.Finish(keep ?? DsConfig.Str("screen", "map"));
    }

    // The panel is driven by touch alone, deliberately. The shoulder buttons
    // were briefly wired to change tabs, and that is wrong: L1/R1 are the
    // game's own bindings and the second screen must never take an input the
    // player is using to play. Nothing here reads the gamepad.

    // Hot-plug. On a handheld whose second panel can be detached this is not an
    // edge case, and neither the old implementation nor the first draft of the
    // plan handled it: show() was one-shot, so unplugging left the game pushing
    // frames at a dead surface and replugging brought nothing back.
    void OnDisplaysUpdated()
    {
        int now = Display.displays.Length;
        if (now == _displayCount) return;
        Debug.Log("[DualScreen] displays changed: " + _displayCount + " -> " + now);
        _displayCount = now;

        if (now <= DsPresentation.DISPLAY)
        {
            // The panel went away. Stop drawing and stop stealing input, so the
            // game behaves exactly as it would on a single-screen device.
            SetActive(false);
        }
        else
        {
            // A panel appeared -- either back after an unplug, or for the first
            // time on a device that started without one.
            StartCoroutine(Reacquire());
        }
    }

    IEnumerator Reacquire()
    {
        // Re-activating is the same asynchronous business as the first time, so
        // it gets the same settling period before anything is drawn.
        yield return Bringup();
        if (_screen != null && _screen.Ready) SetActive(true);
    }

    void SetActive(bool on)
    {
        if (_screen != null) _screen.SetVisible(on);
        DsTouch.Enabled = on;
        if (!on) DsTouch.RemoveFence();
        else DsTouch.InstallFence(gameObject);
    }

    // A live panel over the launcher, or over whatever the user switched to,
    // looks broken. Stop drawing while backgrounded and resume on return.
    void OnApplicationPause(bool paused)
    {
        _paused = paused;
        if (_screen == null || !_screen.Ready) return;
        SetActive(!paused);
    }

    void OnApplicationQuit() { Shutdown(); }
    void OnDestroy() { Shutdown(); }

    void Shutdown()
    {
        if (Instance != this) return;
        Instance = null;
        try { Display.onDisplaysUpdated -= OnDisplaysUpdated; } catch { }
        // Give the game its input back before anything else, so a teardown can
        // never leave the player unable to press a menu.
        DsTouch.RemoveFence();
        if (_screen != null) { _screen.Destroy(); _screen = null; }
        _card = null;
    }
}
#endif
