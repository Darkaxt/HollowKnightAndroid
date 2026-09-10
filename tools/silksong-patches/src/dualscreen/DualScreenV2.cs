// DualScreenV2 — Silksong bootstrap, settings and content integration around
// the shared direct-display transport lifecycle.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using DualSouls.DualScreen;
using UnityEngine;

[DefaultExecutionOrder(10000)]
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
    DsHudReleaseState _releaseState;
    DsHudReleasePump _releasePump;
    int _displayCount;
    float _nextFence;
    float _idleSince = -1f;
    bool _everInGame;
    GameManager _gameManager;
    DsHudManagerCallbacks _managerCallbacks;
    GameManager.GameStateEvent _stateHandler;
    GameManager.PausedEvent _pauseHandler;
    Action _unloadHandler;
    GameManager.EnterSceneEvent _finishedHandler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!ShouldRun() || !ReferenceEquals(Instance, null) || DsHudReleasePump.BlocksReplacement) return;
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
        if (DsHudReleasePump.BlocksReplacement) { Destroy(gameObject); return; }
        Instance = this;
        _managerCallbacks = new DsHudManagerCallbacks(() => GameManager.SilentInstance,
            () => { if (_port != null) _port.RestoreHud(); }, BeforeNativeUnload, OnFinishedEnteringScene);
        BindGameManager();
        DsTouch.Enabled = false;

        // This owner has no routing/input/activation loop. Keeping the presentation
        // beneath it prevents destruction of V2 itself from destroying native roots
        // while a failed restoration still needs a later Unity update to retry.
        _releaseState = new DsHudReleaseState(
            () => { if (_port != null) _port.RestoreHud(); },
            () => { if (_portContent != null) _portContent.Dispose(); else if (_port != null) _port.Dispose(); },
            () => { if (_screen != null) _screen.Dispose(); },
            RetireReleasedOwners);
        _releasePump = DsHudReleasePump.Create(_releaseState);
        _screen = new DsPresentation(_releasePump.transform);
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
        if (_releaseState == null || !_releaseState.CanRoute || _host == null || _host.IsDisposed) return;
        StartCoroutine(Bringup());
    }

    /// <summary>
    /// Activates and settles the retained presentation. The shared host admits
    /// no active state, content, or input fence until readiness is published.
    /// </summary>
    IEnumerator Bringup()
    {
        if (_releaseState == null || !_releaseState.CanRoute) yield break;
        var screen = _screen;
        var host = _host;
        if (screen == null || host == null) yield break;

        yield return screen.Bringup();

        // Shutdown may dispose both retained owners while Unity is settling
        // display activation. The old coroutine must not publish into a new or
        // released host/presentation generation when it resumes.
        if (!_releaseState.CanRoute || host.IsDisposed || !ReferenceEquals(_host, host) ||
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
                if (_managerCallbacks.TransitionPending) _port.BeforeSceneTransition();
                _portContent = new PortContent(_port);
                host.AttachContent(_portContent);
            }
        }

        host.SetPresentationReady(true, screen.Width, screen.Height);
        Debug.Log("[DualScreen] ready");
    }

    void Update()
    {
        if (_releaseState == null || !_releaseState.CanRoute) return;
        BindGameManager();
        if (_host != null) _host.SetEnabled(ShouldRun());
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

    void LateUpdate()
    {
        if (_releaseState == null || !_releaseState.CanRoute) return;
        if (_port == null) return;
        if (_host == null || !_host.IsActive || _screen == null || !_screen.Ready)
        { _port.RestoreHud(); return; }
        _port.LateTick();
    }

    void BindGameManager()
    {
        var current = GameManager.SilentInstance;
        if (ReferenceEquals(current, _gameManager)) return;
        if (_port != null) _port.RestoreHud();
        UnbindGameManager();
        _gameManager = current;
        var subscription = _managerCallbacks.Bind(current, current == null || current.IsInSceneTransition);
        if (_gameManager == null) return;
        // Store these exact native delegate instances. Their subscription closes
        // over THIS manager, and checks SilentInstance again at invocation time.
        _stateHandler = state => subscription.State(
            state == GlobalEnums.GameState.EXITING_LEVEL || state == GlobalEnums.GameState.LOADING,
            state == GlobalEnums.GameState.PLAYING);
        _pauseHandler = paused => subscription.Pause(paused);
        _unloadHandler = subscription.Unloading;
        _finishedHandler = () => subscription.Finished();
        _gameManager.GameStateChange += _stateHandler;
        _gameManager.GamePausedChange += _pauseHandler;
        _gameManager.UnloadingLevel += _unloadHandler;
        _gameManager.OnFinishedEnteringScene += _finishedHandler;
    }

    void UnbindGameManager()
    {
        // Use managed identity even when Unity marks the old owner destroyed.
        if (_managerCallbacks != null) _managerCallbacks.Unbind();
        if (ReferenceEquals(_gameManager, null)) return;
        _gameManager.GameStateChange -= _stateHandler;
        _gameManager.GamePausedChange -= _pauseHandler;
        _gameManager.UnloadingLevel -= _unloadHandler;
        _gameManager.OnFinishedEnteringScene -= _finishedHandler;
        _gameManager = null;
        _stateHandler = null;
        _pauseHandler = null;
        _unloadHandler = null;
        _finishedHandler = null;
    }

    void BeforeNativeUnload()
    {
        // Exact 1.0.29980 managed seam: UnloadingLevel precedes transition
        // UnloadScene, direct/additive loads, and Quit_To_Menu.ActivateAsync.
        // GameStateChange(EXITING_LEVEL) gives the earlier normal-path boundary.
        // A completion or sceneUnloaded callback cannot provide this guarantee.
        if (_port != null) _port.BeforeSceneTransition();
    }

    void OnFinishedEnteringScene()
    {
        // Completion only REARMS routing; it is never used as pre-unload proof.
        if (_port != null) _port.FinishedEnteringScene();
    }

    void OnDisplaysUpdated()
    {
        if (_releaseState == null || !_releaseState.CanRoute) return;
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
        if (_releaseState == null || !_releaseState.CanRoute) return;
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
        if (_releaseState != null) _releaseState.ReleasePresentation();
    }

    void OnApplicationQuit() { Shutdown(); }
    void OnDestroy() { Shutdown(); }

    void Shutdown()
    {
        if (_releaseState == null || _releaseState.Completed) return;
        bool first = !_releaseState.ShutdownRequested;
        try { Display.onDisplaysUpdated -= OnDisplaysUpdated; } catch { }
        UnbindGameManager();
        _releaseState.RequestShutdown(() =>
        {
            if (_host != null) _host.Dispose();
            else { DsTouch.RemoveFence(); ReleasePresentation(); }
        });
        if (first && _releaseState.LastFailure != null)
            Debug.LogError("[DualScreen] native restoration pending; independent release owner retained: " + _releaseState.LastFailure);
    }

    void RetireReleasedOwners()
    {
        // Called only AFTER restore/content disposal/enclosing release succeeded.
        // The release pump can call this managed method even after V2.OnDestroy.
        if (ReferenceEquals(Instance, this)) Instance = null;
        _screen = null;
        _host = null;
        _portContent = null;
        _port = null;
        _input = null;
        _card = null;
        _releasePump = null;
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

// A Silksong-only restoration/release owner. It never activates a transport,
// discovers/reroutes HUD, polls input, or ticks companion content.
public sealed class DsHudReleasePump : MonoBehaviour
{
    static DsHudReleasePump _owner;
    DsHudReleaseState _state;
    float _nextRetry;
    public static bool BlocksReplacement => !ReferenceEquals(_owner, null) &&
        _owner._state != null && _owner._state.BlocksReplacement;

    public static DsHudReleasePump Create(DsHudReleaseState state)
    {
        if (BlocksReplacement) throw new InvalidOperationException("Silksong HUD release owner still retained");
        var go = new GameObject("__DsHudReleaseOwner__");
        DontDestroyOnLoad(go);
        var pump = go.AddComponent<DsHudReleasePump>();
        pump._state = state;
        _owner = pump;
        return pump;
    }

    void Update()
    {
        if (_state == null) return;
        if (_state.Pending && Time.unscaledTime >= _nextRetry)
        {
            _nextRetry = Time.unscaledTime + 0.25f;
            _state.Retry();
        }
        if (!_state.Completed) return;
        if (ReferenceEquals(_owner, this)) _owner = null;
        // Native survivors have left this independent parent before this point.
        Destroy(gameObject);
    }
}
#endif
