// DsPortRuntime — frame and live HUD ownership below the host transport lifecycle.
// Companion-page visibility is independent of transport and native HUD eligibility.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.SceneManagement;

public sealed class DsPortRuntime
{
    DsPortLayers _layers;
    DsPortFrame _frame;
    DsPortHud _hud;
    DsPortOverlays _overlays;
    DsPortProgress _progress;
    DsPortMap _map;
    System.Func<DsGesture, bool> _modsGestureConsumer;
    bool _transitionBoundary;
    bool _pagesVisible = true;
    int _sceneHandle;
    bool _disposed;

    public int SceneRevision { get; private set; }
    public bool IsIdle { get; private set; }
    public bool IsVisible { get; private set; }

    public DsPortRuntime(DsPresentation presentation)
    {
        _layers = new DsPortLayers(presentation);
        _frame = new DsPortFrame(_layers);
        _frame.TabPressed += OnPageTabPressed;
        _hud = new DsPortHud(_layers, _frame);
        _overlays = new DsPortOverlays(_layers);
        _progress = new DsPortProgress(_frame);
        _map = new DsPortMap(_frame);
        _sceneHandle = SceneManager.GetActiveScene().handle;
        IsVisible = true;
    }

    public void Tick(float dt)
    {
        if (_disposed) return;
        _hud.CheckEligibility(IsVisible, _transitionBoundary);
        int activeSceneHandle = SceneManager.GetActiveScene().handle;
        if (activeSceneHandle != _sceneHandle)
        {
            _sceneHandle = activeSceneHandle;
            SceneRevision++;
            RestoreHud();
            _overlays.ClearScenery();
            _frame.InvalidateResidentSources();
        }
        _frame.Tick(dt);
    }

    public void SetIdle(bool idle)
    {
        if (_disposed) return;
        IsIdle = idle;
    }

    // Task100 binds its actual modal/gear consumer here. No Mods UI, settings or
    // action behavior is implemented by this batch, and null never swallows input.
    public void SetModsGestureConsumer(System.Func<DsGesture, bool> consumer)
    {
        if (!_disposed) _modsGestureConsumer = consumer;
    }

    public void OnGesture(DsGesture gesture)
    {
        if (_disposed || !IsVisible) return;
        _progress.ObserveGesture(gesture);
        DsPortGesturePrecedence.Consume(
            () => _overlays.OnGesture(gesture, IsVisible),
            () => _modsGestureConsumer != null && _modsGestureConsumer(gesture),
            () => _frame.TryConsumeGesture(gesture),
            () => _pagesVisible && (_frame.SelectedRole == DsPageRole.Map ? _map.OnGesture(gesture) : _progress.OnGesture(gesture)));
    }

    public void LateTick()
    {
        if (_disposed) return;
        _hud.LateTick(IsVisible, _transitionBoundary);
        _progress.Tick(IsVisible && _pagesVisible && !_transitionBoundary &&
            _hud.SampleEligibility(IsVisible, _transitionBoundary).CanRoute);
        _map.Tick(IsVisible && _pagesVisible && !_transitionBoundary &&
            _hud.SampleEligibility(IsVisible, _transitionBoundary).CanRoute);
        _overlays.LateTick(IsVisible, _hud.SampleEligibility(IsVisible, _transitionBoundary).CanRoute);
    }

    public void BeforeSceneTransition()
    {
        if (_disposed) return;
        _transitionBoundary = true;
        RestoreHud();
        _progress.Invalidate();
        _map.Invalidate();
        _overlays.ClearScenery();
    }

    public void FinishedEnteringScene()
    {
        if (_disposed) return;
        _transitionBoundary = false;
        _progress.Invalidate();
        _map.Invalidate();
    }

    public void RestoreHud()
    {
        if (_disposed) return;
        // Existing V2 inactive/release callbacks also retry exact Map and owned
        // page retirement when normal page Tick has stopped. Keep the runtime
        // and its parents retained until every family has finished restoration.
        try { _hud.Restore(); }
        finally
        {
            try { _map.Invalidate(); }
            finally
            {
                try { _progress.Invalidate(); }
                finally { _overlays.RestoreNative(); }
            }
        }
    }

    void OnPageTabPressed(DsPageRole role)
    {
        SetPagesVisible(DsPortFrameState.PagesVisibleAfterTab(_pagesVisible, role == _frame.SelectedRole));
    }

    public void SetPagesVisible(bool visible)
    {
        if (_disposed) return;
        if (_pagesVisible != visible) { _map.Invalidate(); _progress.Invalidate(); }
        _pagesVisible = visible;
        _layers.Pages.gameObject.SetActive(IsVisible && visible);
        // No HUD restore: page-only hide/selection doesn't shut transport down.
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        if (IsVisible != visible) { _map.Invalidate(); _progress.Invalidate(); }
        if (!visible) _overlays.RestoreNative();
        if (!visible) _hud.RestoreBefore(() => _layers.SetVisible(false));
        else
        {
            _layers.SetVisible(true);
            _layers.Pages.gameObject.SetActive(_pagesVisible);
        }
        IsVisible = visible;
        _overlays.LateTick(visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _hud.Dispose();
        _map.Dispose();
        _progress.Dispose();
        _overlays.Dispose();
        _modsGestureConsumer = null;
        _frame.Dispose();
        _frame.TabPressed -= OnPageTabPressed;
        _overlays = null;
        _hud = null;
        _frame = null;
        _layers.Dispose();
        _layers = null;
        _disposed = true;
    }
}
#endif
