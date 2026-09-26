// DualScreenV2 — Silksong bootstrap and game integration around the shared
// direct-display transport and the v2 companion shell.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using DualSouls.DualScreen;
using DualSouls.Mods.Silksong;
using DualSouls.Skins.Silksong.Runtime;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public class DualScreenV2 : MonoBehaviour
{
    const string KEY_DUAL_SCREEN = "dualscreen_enabled";
    const float IDLE_GRACE = 0.75f;

    public static DualScreenV2 Instance { get; private set; }

    DsPresentation _screen;
    DirectDisplayHost _host;
    DsShell _shell;
    ShellContent _shellContent;
    DsInput _input;
    DsTestCard _card;
    DsHudReleaseState _releaseState;
    DsHudReleasePump _releasePump;
    int _displayCount;
    float _idleSince = -1f;
    bool _everInGame;
    float _nextFontRetry;
    bool _fontReady;
    GameManager _gameManager;
    DsHudManagerCallbacks _managerCallbacks;
    GameManager.GameStateEvent _stateHandler;
    GameManager.PausedEvent _pauseHandler;
    Action _unloadHandler;
    GameManager.EnterSceneEvent _finishedHandler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SilksongProcessStartup.Run(
            SilksongModsRuntime.EnsureStarted,
            () => ShouldRun() && ReferenceEquals(Instance, null) &&
                  !DsHudReleasePump.BlocksReplacement,
            () =>
            {
                var go = new GameObject("__DualScreenV2__");
                DontDestroyOnLoad(go);
                go.AddComponent<DualScreenV2>();
            });
    }

    public static bool ShouldRun()
    {
        return SilksongPatches.Settings.GetBool(KEY_DUAL_SCREEN, true);
    }

    void Start()
    {
        if (DsHudReleasePump.BlocksReplacement) { Destroy(gameObject); return; }
        Instance = this;
        _managerCallbacks = new DsHudManagerCallbacks(
            () => GameManager.SilentInstance,
            () => { if (_shellContent != null) _shellContent.RestoreNative(); },
            BeforeNativeUnload,
            OnFinishedEnteringScene);
        BindGameManager();
        DsTouch.Stop();

        // Keep restoration and release beneath this replaceable owner. A failed
        // native restoration can then retry after Unity destroys this component.
        _releaseState = new DsHudReleaseState(
            () => { if (_shellContent != null) _shellContent.RestoreNative(); },
            () =>
            {
                if (_shellContent != null) _shellContent.Dispose();
                else if (_shell != null) _shell.Dispose();
            },
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

        // Subscribe before publishing initial presence so single-display startup
        // remains resident and can acquire a panel attached later.
        _displayCount = Display.displays.Length;
        Display.onDisplaysUpdated += OnDisplaysUpdated;
        _host.SetDisplayPresent(_displayCount > DsPresentation.DISPLAY);
    }

    void RequestActivation()
    {
        if (_releaseState == null || !_releaseState.CanRoute ||
            _host == null || _host.IsDisposed) return;
        StartCoroutine(Bringup());
    }

    IEnumerator Bringup()
    {
        if (_releaseState == null || !_releaseState.CanRoute) yield break;
        var screen = _screen;
        var host = _host;
        if (screen == null || host == null) yield break;

        int oldWidth = screen.Width;
        int oldHeight = screen.Height;
        yield return screen.Bringup();

        // Shutdown may release both owners while display activation is settling.
        if (!_releaseState.CanRoute || host.IsDisposed ||
            !ReferenceEquals(_host, host) || !ReferenceEquals(_screen, screen))
            yield break;

        bool present = Display.displays.Length > DsPresentation.DISPLAY;
        host.SetDisplayPresent(present);
        if (!present || !screen.Ready)
        {
            host.SetPresentationReady(false);
            Debug.Log("[DualScreen] no usable second display; dormant");
            yield break;
        }

        bool resized = oldWidth > 0 && oldHeight > 0 &&
                       (oldWidth != screen.Width || oldHeight != screen.Height);
        if (_shell == null && _card == null)
        {
            if (DsConfig.Bool("testcard", false))
            {
                _card = new DsTestCard(screen.Root, screen.Width, screen.Height);
            }
            else
            {
                _input = new DsInput();
                BuildShell(null);
                _shellContent = new ShellContent(_shell);
                if (_managerCallbacks.TransitionPending)
                    _shellContent.BeforeSceneTransition();
                host.AttachContent(_shellContent);
            }
        }
        else if (resized)
        {
            if (_card != null)
            {
                ClearRoot();
                _card = new DsTestCard(screen.Root, screen.Width, screen.Height);
            }
            else
            {
                RebuildShell();
            }
        }

        host.SetPresentationReady(true, screen.Width, screen.Height);
        Debug.Log("[DualScreen] ready");
    }

    void BuildShell(string preferredId)
    {
        _shell = new DsShell(_screen.Root);
        RegisterScreens(_shell);
        _shell.Finish(preferredId ?? DsConfig.Str("screen", "map"));
    }

    static void RegisterScreens(DsShell shell)
    {
        shell.Register(new DsInventoryScreen(), InventoryPaneList.PaneTypes.Inv);
        shell.Register(new DsLoadoutScreen(), InventoryPaneList.PaneTypes.Tools);
        shell.Register(new DsTasksScreen(), InventoryPaneList.PaneTypes.Quests);
        shell.Register(new DsJournalScreen(), InventoryPaneList.PaneTypes.Journal);
        shell.Register(new DsMapScreen(), InventoryPaneList.PaneTypes.Map);
    }

    void Update()
    {
        if (_releaseState == null || !_releaseState.CanRoute) return;
        BindGameManager();
        if (_host != null) _host.SetEnabled(ShouldRun());
        if (_host == null || !_host.IsActive || _screen == null || !_screen.Ready)
            return;

        // The SurfaceView is both geometry authority and input boundary. Losing
        // it retires this readiness generation even if Unity still reports display 1.
        if (!DsTouch.Ready)
        {
            _host.SetPresentationReady(false);
            _screen.MarkUnavailable();
            _host.SetDisplayPresent(Display.displays.Length > DsPresentation.DISPLAY);
            return;
        }

        _screen.SweepCameras();

        float dt = Time.unscaledDeltaTime;
        if (_card != null) { _card.Tick(); return; }
        if (_shell == null) return;

        if (_shell.LayoutChanged)
        {
            if (_input != null) { _input.Cancel(); DispatchGestures(); }
            RebuildShell();
        }

        DsProbe.MaybeRun();
        DsProbe.MaybeDumpSprites();
        DsProbe.MaybeDumpFonts();

        // The game's fonts arrive after this persistent owner. Rebuild once when
        // they become available rather than leaving startup fallback text in place.
        if (!_fontReady && Time.unscaledTime >= _nextFontRetry)
        {
            _nextFontRetry = Time.unscaledTime + 2f;
            DsTheme.ForgetFont();
            if (DsTheme.HasFont)
            {
                _fontReady = true;
                Debug.Log("[DualScreen] fonts found — rebuilding shell");
                RebuildShell();
            }
        }

        if (_input != null)
        {
            _input.Poll();
            DispatchGestures();
        }

        // Leaving gameplay gets a short grace because scene loads briefly have
        // no hero. Entering gameplay and initial title-card selection are immediate.
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
        _shell.SetIdle(!inGame && settled);
        _shell.Tick(dt);
    }

    void RebuildShell()
    {
        if (_screen == null || _screen.Root == null) return;
        string keep = _shell != null ? _shell.ActiveId : null;
        if (_shellContent != null) _shellContent.Replace(null);
        else if (_shell != null) _shell.Dispose();
        _shell = null;
        ClearRootChildren();
        BuildShell(keep);
        if (_shellContent != null) _shellContent.Replace(_shell);
    }

    void ClearRoot()
    {
        if (_shellContent != null) _shellContent.Replace(null);
        else if (_shell != null) _shell.Dispose();
        _shell = null;
        _card = null;
        ClearRootChildren();
    }

    void ClearRootChildren()
    {
        if (_screen == null || _screen.Root == null) return;
        var root = _screen.Root;
        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);
    }

    void DispatchGestures()
    {
        if (_input == null || _shell == null) return;
        var gestures = _input.Gestures;
        for (int i = 0; i < gestures.Count; i++) _shell.OnGesture(gestures[i]);
    }

    void BindGameManager()
    {
        var current = GameManager.SilentInstance;
        if (ReferenceEquals(current, _gameManager)) return;
        if (_shellContent != null) _shellContent.BeforeSceneTransition();
        UnbindGameManager();
        _gameManager = current;
        var subscription = _managerCallbacks.Bind(
            current, current == null || current.IsInSceneTransition);
        if (_gameManager == null) return;
        _stateHandler = state => subscription.State(
            state == GlobalEnums.GameState.EXITING_LEVEL ||
            state == GlobalEnums.GameState.LOADING,
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
        // This managed seam precedes transition unloads, direct/additive loads,
        // and Quit_To_Menu. Completion only rearms the retained shell.
        if (_shellContent != null) _shellContent.BeforeSceneTransition();
    }

    void OnFinishedEnteringScene()
    {
        if (_shellContent != null) _shellContent.FinishedEnteringScene();
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
        if (_releaseState == null || !_releaseState.CanRoute || _host == null) return;

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
        // Presence is published first so resume cannot briefly reactivate stale
        // readiness for a panel lost while the app was backgrounded.
        _host.SetPaused(paused);
    }

    void SetTouchFenceActive(bool active)
    {
        if (active)
        {
            if (!DsTouch.Begin())
                throw new InvalidOperationException(
                    "Secondary display input could not be captured");
            return;
        }

        DsTouch.Stop();
        if (_input == null) return;
        _input.Cancel();
        DispatchGestures();
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
            else { DsTouch.Stop(); ReleasePresentation(); }
        });
        if (first && _releaseState.LastFailure != null)
            Debug.LogError("[DualScreen] native restoration pending; " +
                           "independent release owner retained: " +
                           _releaseState.LastFailure);
    }

    void RetireReleasedOwners()
    {
        if (ReferenceEquals(Instance, this)) Instance = null;
        _screen = null;
        _host = null;
        _shellContent = null;
        _shell = null;
        _input = null;
        _card = null;
        _releasePump = null;
    }

    sealed class ShellContent : IDirectDisplayContent
    {
        DsShell _shell;
        bool _transportActive;
        bool _transitioning;
        bool _disposed;

        public ShellContent(DsShell shell)
        {
            _shell = shell;
        }

        public void Replace(DsShell shell)
        {
            if (_disposed) return;
            if (_shell != null && !ReferenceEquals(_shell, shell))
            {
                _shell.SetVisible(false);
                _shell.Dispose();
            }
            _shell = shell;
            Apply();
        }

        public void SetTransportActive(bool active)
        {
            if (_disposed) return;
            _transportActive = active;
            Apply();
        }

        public void BeforeSceneTransition()
        {
            if (_disposed) return;
            _transitioning = true;
            Apply();
        }

        public void FinishedEnteringScene()
        {
            if (_disposed) return;
            _transitioning = false;
            Apply();
        }

        public void RestoreNative()
        {
            if (_shell != null) _shell.RestoreNative();
        }

        public void OnPanelGeometry(float width, float height)
        {
            // DsShell detects a changed canvas layout and rebuilds on Update.
        }

        void Apply()
        {
            if (_shell == null) return;
            _shell.SetTransitioning(_transitioning);
            _shell.SetVisible(_transportActive);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transportActive = false;
            _transitioning = true;
            if (_shell != null)
            {
                _shell.SetVisible(false);
                _shell.Dispose();
                _shell = null;
            }
        }
    }
}

// A Silksong-only restoration/release owner. It never activates a transport,
// discovers or reroutes HUD objects, polls input, or ticks companion content.
public sealed class DsHudReleasePump : MonoBehaviour
{
    static DsHudReleasePump _owner;
    DsHudReleaseState _state;
    float _nextRetry;

    public static bool BlocksReplacement => !ReferenceEquals(_owner, null) &&
        _owner._state != null && _owner._state.BlocksReplacement;

    public static DsHudReleasePump Create(DsHudReleaseState state)
    {
        if (BlocksReplacement)
            throw new InvalidOperationException("Silksong HUD release owner still retained");
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
        Destroy(gameObject);
    }
}
#endif
