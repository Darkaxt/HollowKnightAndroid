// DualScreenV2 — the second screen's entry point and its lifetime.
//
// The port's second screen used to be a mirror: a capture camera copied the
// game's HUD into a RenderTexture, a GPU readback pulled it into managed
// memory, a worker thread flipped it into a 22 MB shared file, and Java copied
// that into a Bitmap in an Android Presentation. It showed the game's inventory
// only while the inventory was open, letterboxed, because that menu is authored
// for 16:9 and this panel is 1240x1080. All of that is gone.
//
// Unity renders to the panel itself (see DsPresentation). This entry point owns
// transport lifecycle and delegates composition to DsPortRuntime:
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
    DsPortRuntime _port;
    DsInput _input;
    DsTestCard _card;
    bool _paused;
    bool _displayPresent;
    int _displayCount;
    float _nextFence;

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
        _displayPresent = _displayCount > DsPresentation.DISPLAY;
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
        var screen = _screen;
        if (screen == null)
        {
            screen = new DsPresentation(transform);
            _screen = screen; // retain before yielding so every caller shares one rig
        }
        yield return screen.Bringup();

        _displayPresent = Display.displays.Length > DsPresentation.DISPLAY;

        if (!screen.Ready)
        {
            // No second display, or it would not activate. Do nothing at all
            // rather than half of something -- and stay resident, because a
            // panel may still turn up.
            Debug.Log("[DualScreen] no second display; dormant");
            ApplyActiveState();
            yield break;
        }

        // The test card is the M1 rig: corner markers, a sweeping bar and a
        // touch crosshair, which is how "the surface covers the panel" and
        // "input is separated" were verified. Kept one flag away, because it is
        // the fastest way to tell a rendering problem from a content problem.
        if (_port == null && _card == null)
        {
            if (DsConfig.Bool("testcard", false))
            {
                _card = new DsTestCard(_screen.Root, _screen.Width, _screen.Height);
            }
            else
            {
                _input = new DsInput();
                _port = new DsPortRuntime(_screen);
            }
        }

        // Transport readiness is necessary but not sufficient: a paused app or
        // a panel lost during bring-up must remain dark and must not fence input.
        ApplyActiveState();

        Debug.Log("[DualScreen] ready");
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
        if (DsTouch.Enabled && Time.unscaledTime >= _nextFence)
        {
            _nextFence = Time.unscaledTime + 0.25f;
            DsTouch.InstallFence(gameObject);
        }

        float dt = Time.unscaledDeltaTime;

        if (_card != null) { _card.Tick(); return; }
        if (_port == null) return;

        DsProbe.MaybeRun();

        if (_input != null)
        {
            _input.Poll();
            var gestures = _input.Gestures;
            for (int i = 0; i < gestures.Count; i++) _port.OnGesture(gestures[i]);
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
        _port.SetIdle(!inGame && settled);

        _port.Tick(dt);
    }

    const float IDLE_GRACE = 0.75f;
    float _idleSince = -1f;
    bool _everInGame;

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
        _displayPresent = now > DsPresentation.DISPLAY;

        if (!_displayPresent)
        {
            // The panel went away. Stop drawing and stop stealing input, so the
            // game behaves exactly as it would on a single-screen device.
            if (_screen != null) _screen.MarkUnavailable();
            ApplyActiveState();
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
    }

    void ApplyActiveState()
    {
        bool active = !_paused && _displayPresent && _screen != null && _screen.Ready;
        if (_screen != null) _screen.SetVisible(active);
        if (_port != null) _port.SetVisible(active);
        DsTouch.Enabled = active;
        if (!active) DsTouch.RemoveFence();
        else DsTouch.InstallFence(gameObject);
    }

    // A live panel over the launcher, or over whatever the user switched to,
    // looks broken. Stop drawing while backgrounded and resume on return.
    void OnApplicationPause(bool paused)
    {
        _paused = paused;
        _displayPresent = Display.displays.Length > DsPresentation.DISPLAY;
        if (!_displayPresent && _screen != null) _screen.MarkUnavailable();
        ApplyActiveState();
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
        if (_port != null) { _port.Dispose(); _port = null; }
        if (_screen != null) { _screen.Destroy(); _screen = null; }
        _input = null;
        _card = null;
    }
}
#endif
