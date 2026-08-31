// DsPortRuntime — composition state owned below the host transport lifecycle.
//
// DualScreenV2 remains responsible for display bring-up, hot-plug, pause,
// input fencing, and teardown ordering. Stage 1 owns only root visibility,
// idle state, scene invalidation revision, and idempotent disposal. Stage 2
// attaches the resident frame/tab composition; HUD/page content/overlays remain
// assigned to their later modules.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.SceneManagement;

public sealed class DsPortRuntime
{
    DsPortLayers _layers;
    DsPortFrame _frame;
    int _sceneHandle;
    bool _disposed;

    public int SceneRevision { get; private set; }
    public bool IsIdle { get; private set; }
    public bool IsVisible { get; private set; }

    public DsPortRuntime(DsPresentation presentation)
    {
        _layers = new DsPortLayers(presentation);
        _frame = new DsPortFrame(_layers);
        _sceneHandle = SceneManager.GetActiveScene().handle;
        IsVisible = true;
    }

    public void Tick(float dt)
    {
        if (_disposed) return;
        int activeSceneHandle = SceneManager.GetActiveScene().handle;
        if (activeSceneHandle != _sceneHandle)
        {
            _sceneHandle = activeSceneHandle;
            SceneRevision++;
            _frame.InvalidateResidentSources();
        }
        _frame.Tick(dt);
    }

    public void SetIdle(bool idle)
    {
        if (_disposed) return;
        IsIdle = idle;
    }

    // Gestures are still handled synchronously. Stage 2 consumes only bottom
    // tab taps; every page-specific gesture remains unhandled until its module.
    public void OnGesture(DsGesture gesture)
    {
        if (_disposed) return;
        _frame.OnGesture(gesture);
    }

    public void SetVisible(bool visible)
    {
        if (_disposed || IsVisible == visible) return;
        IsVisible = visible;
        _layers.SetVisible(visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frame.Dispose();
        _frame = null;
        _layers.Dispose();
        _layers = null;
    }
}
#endif
