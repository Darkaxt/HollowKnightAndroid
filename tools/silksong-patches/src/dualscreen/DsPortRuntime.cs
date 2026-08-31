// DsPortRuntime — composition state owned below the host transport lifecycle.
//
// DualScreenV2 remains responsible for display bring-up, hot-plug, pause,
// input fencing, and teardown ordering. Stage 1 owns only root visibility,
// idle state, scene invalidation revision, and idempotent disposal. Frame, HUD,
// pages, overlays, fade behavior, and resident-object adapters arrive later.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.SceneManagement;

public sealed class DsPortRuntime
{
    DsPortLayers _layers;
    int _sceneHandle;
    bool _disposed;

    public int SceneRevision { get; private set; }
    public bool IsIdle { get; private set; }
    public bool IsVisible { get; private set; }

    public DsPortRuntime(DsPresentation presentation)
    {
        _layers = new DsPortLayers(presentation);
        _sceneHandle = SceneManager.GetActiveScene().handle;
        IsVisible = true;
    }

    public void Tick(float dt)
    {
        if (_disposed) return;
        int activeSceneHandle = SceneManager.GetActiveScene().handle;
        if (activeSceneHandle == _sceneHandle) return;
        _sceneHandle = activeSceneHandle;
        SceneRevision++;
    }

    public void SetIdle(bool idle)
    {
        if (_disposed) return;
        IsIdle = idle;
    }

    // Stage 1 has no composition consumer. Do not retain gestures for later
    // frames: a later module must handle each gesture synchronously or drop it.
    public void OnGesture(DsGesture gesture) { }

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
        _layers.Dispose();
        _layers = null;
    }
}
#endif
