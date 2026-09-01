// DualScreenV2 — Silksong bootstrap, settings and content integration around
// the shared direct-display transport lifecycle.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using DualSouls.DualScreen;
using UnityEngine;

public class DualScreenV2 : MonoBehaviour
{
    const string KEY_DUAL_SCREEN = "dualscreen_enabled";
    const float IDLE_GRACE = 0.75f;

    public static DualScreenV2 Instance { get; private set; }

    DsPresentation _screen;
    DirectDisplayHost _host;
    DsPortRuntime _port;
    PortContent _portContent;
    DsInput _input;
    DsTestCard _card;
    int _displayCount;
    float _nextFence;
    float _idleSince = -1f;
    bool _everInGame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!ShouldRun()) return;
        var go = new GameObject("__DualScreenV2__");
        DontDestroyOnLoad(go);
        go.AddComponent<DualScreenV2>();
    }

    public static bool ShouldRun()
    {
        return SilksongPatches.Settings.GetBool(KEY_DUAL_SCREEN, true);
    }

    void Start()
    {
        Instance = this;
        DsTouch.Enabled = false;

        _screen = new DsPresentation(transform);
        _host = new DirectDisplayHost(
            requestActivation: RequestActivation,
            setPresentationVisible: visible =>
            {
                if (_screen != null) _screen.SetVisible(visible);
            },
            setTouchFenceActive: SetTouchFenceActive,
            releasePresentation: ReleasePresentation);

        // Subscribe before publishing initial presence so a single-display
        // startup stays resident and a later presence generation can activate.
        _displayCount = Display.displays.Length;
        Display.onDisplaysUpdated += OnDisplaysUpdated;
        _host.SetDisplayPresent(_displayCount > DsPresentation.DISPLAY);
    }

    void RequestActivation()
    {
        if (_host == null || _host.IsDisposed) return;
        StartCoroutine(Bringup());
    }

    /// <summary>
    /// Activates and settles the retained presentation. The shared host admits
    /// no active state, content, or input fence until readiness is published.
    /// </summary>
    IEnumerator Bringup()
    {
        var screen = _screen;
        var host = _host;
        if (screen == null || host == null) yield break;

        yield return screen.Bringup();

        // Shutdown may dispose both retained owners while Unity is settling
        // display activation. The old coroutine must not publish into a new or
        // released host/presentation generation when it resumes.
        if (host.IsDisposed || !ReferenceEquals(_host, host) ||
            !ReferenceEquals(_screen, screen)) yield break;

        bool present = Display.displays.Length > DsPresentation.DISPLAY;
        host.SetDisplayPresent(present);
        if (!present || !screen.Ready)
        {
            host.SetPresentationReady(false);
            Debug.Log("[DualScreen] no second display; dormant");
            yield break;
        }

        // Content is created only after the presentation has measured and built
        // its two roots. The diagnostic test card remains a one-flag transport
        // probe and intentionally has no game-content adapter.
        if (_port == null && _card == null)
        {
            if (DsConfig.Bool("testcard", false))
            {
                _card = new DsTestCard(screen.Root, screen.Width, screen.Height);
            }
            else
            {
                _input = new DsInput();
                _port = new DsPortRuntime(screen);
                _portContent = new PortContent(_port);
                host.AttachContent(_portContent);
            }
        }

        host.SetPresentationReady(true, screen.Width, screen.Height);
        Debug.Log("[DualScreen] ready");
    }

    void Update()
    {
        if (_host == null || !_host.IsActive || _screen == null || !_screen.Ready)
            return;

        // The game creates cameras and event systems throughout its scene
        // lifecycle, so preserve the proven periodic sweep/fence maintenance.
        _screen.SweepCameras();
        if (DsTouch.Enabled && Time.unscaledTime >= _nextFence)
        {
            _nextFence = Time.unscaledTime + 0.25f;
            DsTouch.InstallFence(gameObject);
        }

        float dt = Time.unscaledDeltaTime;
        if (_card != null)
        {
            _card.Tick();
            return;
        }
        if (_port == null) return;

        DsProbe.MaybeRun();

        if (_input != null)
        {
            _input.Poll();
            var gestures = _input.Gestures;
            for (int i = 0; i < gestures.Count; i++)
                _port.OnGesture(gestures[i]);
        }

        // Preserve the transition grace only when leaving gameplay. Before the
        // first save is entered, and whenever gameplay returns, change at once.
        bool inGame = DsGameData.InGame;
        if (inGame)
        {
            _idleSince = -1f;
            _everInGame = true;
        }
        else if (_idleSince < 0f)
        {
            _idleSince = Time.unscaledTime;
        }

        bool settled = !_everInGame || Time.unscaledTime - _idleSince >= IDLE_GRACE;
        _port.SetIdle(!inGame && settled);
        _port.Tick(dt);
    }

    void OnDisplaysUpdated()
    {
        int now = Display.displays.Length;
        if (now == _displayCount) return;

        Debug.Log("[DualScreen] displays changed: " + _displayCount + " -> " + now);
        _displayCount = now;
        bool present = now > DsPresentation.DISPLAY;

        if (!present)
        {
            // Host deactivation establishes touch/content/presentation order;
            // then the presentation invalidates the in-flight generation.
            if (_host != null) _host.SetDisplayPresent(false);
            if (_screen != null) _screen.MarkUnavailable();
        }
        else if (_host != null)
        {
            _host.SetDisplayPresent(true);
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (_host == null) return;

        bool present = Display.displays.Length > DsPresentation.DISPLAY;
        if (!present)
        {
            _host.SetDisplayPresent(false);
            if (_screen != null) _screen.MarkUnavailable();
        }
        else
        {
            _host.SetDisplayPresent(true);
        }
        // Publish current presence before resuming so a display lost while the
        // app was backgrounded cannot become briefly active on stale readiness.
        _host.SetPaused(paused);
    }

    void SetTouchFenceActive(bool active)
    {
        DsTouch.Enabled = active;
        if (active) DsTouch.InstallFence(gameObject);
        else DsTouch.RemoveFence();
    }

    void ReleasePresentation()
    {
        if (_screen == null) return;
        _screen.Dispose();
        _screen = null;
    }

    void OnApplicationQuit() { Shutdown(); }
    void OnDestroy() { Shutdown(); }

    void Shutdown()
    {
        if (Instance != this) return;
        Instance = null;
        try { Display.onDisplaysUpdated -= OnDisplaysUpdated; } catch { }

        if (_host != null)
        {
            _host.Dispose();
            _host = null;
        }
        else
        {
            DsTouch.RemoveFence();
            ReleasePresentation();
        }

        _portContent = null;
        _port = null;
        _input = null;
        _card = null;
    }

    sealed class PortContent : IDirectDisplayContent
    {
        DsPortRuntime _port;

        public PortContent(DsPortRuntime port)
        {
            _port = port;
        }

        public void SetTransportActive(bool active)
        {
            if (_port != null) _port.SetVisible(active);
        }

        public void OnPanelGeometry(float width, float height)
        {
            // DsPortRuntime is constructed only after DsPresentation has already
            // measured the panel and exposes those exact roots and dimensions.
        }

        public void Dispose()
        {
            if (_port == null) return;
            _port.Dispose();
            _port = null;
        }
    }
}
#endif
