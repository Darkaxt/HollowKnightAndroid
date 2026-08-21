// DsPresentation — the second screen itself: one display, one camera, one
// canvas, and the discipline that keeps all three from touching the game.
//
// This replaces the whole of V1's transport. There is no RenderTexture, no
// AsyncGPUReadback, no shared memory, no Java. Unity presents to the panel
// directly, measured at no cost to the main screen: 120.2 fps with p99 and max
// present intervals both equal to the 8.33 ms refresh and zero missed vsyncs
// over 253 frames, with a camera and canvas live on display 1.
//
// Three things about Unity's Android multi-display are not obvious, and each
// one cost a debugging session during M0:
//
//   * Activate() is ASYNCHRONOUS. Rendering to the display a few frames after
//     it returns took the graphics thread down with SIGBUS (BUS_ADRALN).
//     Waiting about a second first has been reliable.
//
//   * There is NO readiness flag to wait on instead. Display.renderingWidth
//     and renderingHeight read 0x0 forever on this platform, even while Unity
//     is demonstrably rendering to the panel at 1240x1080. A guard that waited
//     for them left the panel black. systemWidth/Height do eventually populate,
//     but only AFTER rendering starts, so they cannot be waited on either.
//
//   * The panel's size therefore has to be discovered rather than asked for:
//     systemWidth/Height once they appear, else Android's own report, else a
//     configured fallback.
//
// Isolation from the game is enforced at both ends. Our camera renders exactly
// one layer, and that layer is cleared from every other camera's mask -- on a
// slow sweep, because Unity has no "a camera was created" event and the game
// spawns cameras for cutscenes and bosses. The reverse direction (the game
// drawing our layer) is the one that would be visible to the player, so it is
// the one that gets swept.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DsPresentation
{
    /// <summary>Unity's index for the second panel.</summary>
    public const int DISPLAY = 1;

    // Layers 3 and 6 are unnamed in the game's TagManager, so 6 is ours. The
    // game renders its HUD and menus on layer 5 ("UI") and its world on the
    // rest; nothing of the game's is ever moved here.
    public const int LAYER = 6;

    // The AYN Thor's panel, used only until the real size is known.
    const int FALLBACK_W = 1240;
    const int FALLBACK_H = 1080;

    public Camera Camera { get; private set; }
    public Canvas Canvas { get; private set; }
    /// <summary>Where screens build their UI. Fills the panel.</summary>
    public RectTransform Root { get; private set; }
    public bool Ready { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// The panel's size, statically reachable for coordinate maths.
    ///
    /// Touches arrive in panel pixels with y up from the bottom-left, and our
    /// layout is authored in the same units with y DOWN from the top-left, so
    /// converting between them needs the height and nothing else.
    /// </summary>
    public static int PanelW { get; private set; }
    public static int PanelH { get; private set; }

    /// <summary>Panel touch point -> layout point (origin top-left, y down).</summary>
    public static Vector2 ToLayout(Vector2 panelPoint)
    {
        return new Vector2(panelPoint.x, PanelH - panelPoint.y);
    }

    /// <summary>
    /// The camera the canvas renders through. uGUI hit-testing needs it: with a
    /// ScreenSpaceCamera canvas, passing null to
    /// RectTransformUtility.ScreenPointToLocalPointInRectangle silently returns
    /// the wrong point, which is why taps did nothing at first.
    /// </summary>
    public static Camera UiCamera { get; private set; }

    readonly Transform _parent;
    CanvasScaler _scaler;
    float _nextSweep;

    public DsPresentation(Transform parent) { _parent = parent; }

    /// <summary>
    /// Bring up the display and build the rig. Yields until the panel is live.
    /// </summary>
    public IEnumerator Bringup()
    {
        var displays = Display.displays;
        if (displays.Length <= DISPLAY)
        {
            // Not an error: a device with one screen is a device with one
            // screen, and everything here simply does not run.
            Debug.Log("[DualScreen] only " + displays.Length + " display(s); second screen not available");
            yield break;
        }

        try { displays[DISPLAY].Activate(); }
        catch (Exception e)
        {
            Debug.LogError("[DualScreen] could not activate display " + DISPLAY + ": " + e);
            yield break;
        }

        // See the header: there is nothing to poll, so this is a timer. It is
        // deliberately generous -- it happens once, at startup, while the game
        // is still loading, and the failure it prevents is a native crash.
        float settle = DsConfig.Int("settle_ms", 1500) / 1000f;
        float until = Time.realtimeSinceStartup + settle;
        while (Time.realtimeSinceStartup < until) yield return null;

        MeasurePanel();

        if (Camera == null)
        {
            Build();
        }
        else
        {
            // Re-acquiring a panel we have already built for -- an unplug and
            // replug. Build() must NOT run twice: it creates the camera and the
            // canvas outright, so a second call leaves two of each, both
            // drawing, and the screens' UI attached to the orphaned one.
            // Keeping the rig also keeps everything built on it.
            if (_scaler != null) _scaler.referenceResolution = new Vector2(Width, Height);
            SweepCameras(force: true);
        }

        Ready = true;

        Debug.Log("[DualScreen] second screen up: " + Width + "x" + Height +
                  " (" + ((float)Width / Height).ToString("F2") + ":1)" +
                  " canvas=" + Canvas.renderMode + " layer=" + LAYER);
    }

    // renderingWidth/Height are always 0x0 here, so take systemWidth/Height when
    // they are populated and fall back to what Android says the panel is.
    void MeasurePanel()
    {
        var d = Display.displays[DISPLAY];
        int w = d.systemWidth, h = d.systemHeight;
        if (w <= 0 || h <= 0) { w = d.renderingWidth; h = d.renderingHeight; }
        if (w <= 0 || h <= 0)
        {
            w = DsConfig.Int("panel_w", FALLBACK_W);
            h = DsConfig.Int("panel_h", FALLBACK_H);
            Debug.LogWarning("[DualScreen] panel size unknown, assuming " + w + "x" + h);
        }
        Width = w;
        Height = h;
        PanelW = w;
        PanelH = h;
    }

    void Build()
    {
        var camGo = new GameObject("DsCamera");
        camGo.transform.SetParent(_parent, false);
        camGo.layer = LAYER;
        Camera = camGo.AddComponent<Camera>();
        Camera.targetDisplay = DISPLAY;
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = Color.black;
        Camera.cullingMask = 1 << LAYER;    // ours and only ours
        Camera.orthographic = true;
        Camera.nearClipPlane = -100f;
        Camera.farClipPlane = 100f;
        Camera.allowHDR = false;
        Camera.allowMSAA = false;
        Camera.useOcclusionCulling = false;
        Camera.depth = -50f;

        var canvasGo = new GameObject("DsCanvas");
        canvasGo.transform.SetParent(_parent, false);
        canvasGo.layer = LAYER;
        Canvas = canvasGo.AddComponent<Canvas>();

        // ScreenSpaceCamera by default rather than Overlay, because the map
        // screen will need world-space content composited with the UI and only
        // a camera-rendered canvas can do that. Overlay is kept one flag away
        // in case a device disagrees.
        if (DsConfig.Str("canvasmode", "camera") == "overlay")
        {
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.targetDisplay = DISPLAY;
            UiCamera = null;            // overlay canvases hit-test against null
        }
        else
        {
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.worldCamera = Camera;
            Canvas.planeDistance = 1f;
            UiCamera = Camera;
        }

        // Authored at the panel's own size, so a different second screen scales
        // rather than clips. matchWidthOrHeight 0.5 splits the difference on a
        // panel with a different aspect.
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Width, Height);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        _scaler = scaler;

        // No GraphicRaycaster and no EventSystem: we own every rect on this
        // screen and hit-test them ourselves, so a raycaster would only add a
        // way to collide with the game's event system.

        var rootGo = new GameObject("DsRoot");
        rootGo.layer = LAYER;
        Root = rootGo.AddComponent<RectTransform>();
        Root.SetParent(canvasGo.transform, false);
        Root.anchorMin = Vector2.zero;
        Root.anchorMax = Vector2.one;
        Root.offsetMin = Vector2.zero;
        Root.offsetMax = Vector2.zero;

        SweepCameras(force: true);
    }

    /// <summary>
    /// Keep our layer off every other camera.
    ///
    /// The game hard-assigns culling masks in places -- GameCameras.MoveMenuToHUDCamera
    /// sets hudCamera.cullingMask = 32 outright, on a delayed Invoke -- and
    /// spawns cameras during cutscenes and boss fights. Since Unity offers no
    /// notification when a Camera appears, this runs on a slow sweep. A camera
    /// created between two sweeps could show our layer for a frame; that is the
    /// residual risk, and it is why the sweep is cheap enough to run often.
    /// </summary>
    public void SweepCameras(bool force = false)
    {
        if (!force && Time.unscaledTime < _nextSweep) return;
        _nextSweep = Time.unscaledTime + DsConfig.Int("sweep_ms", 500) / 1000f;

        // Camera.allCameras allocates a fresh array on every read; the
        // count-plus-buffer form does not, and this runs forever.
        int count = Camera.allCamerasCount;
        if (count <= 0) return;
        if (_camBuf == null || _camBuf.Length < count) _camBuf = new Camera[count + 4];
        Camera.GetAllCameras(_camBuf);

        int mask = 1 << LAYER;
        for (int i = 0; i < count; i++)
        {
            var c = _camBuf[i];
            if (c == null || c == Camera) continue;
            if ((c.cullingMask & mask) == 0) continue;
            c.cullingMask &= ~mask;
            Debug.Log("[DualScreen] cleared layer " + LAYER + " from camera '" + c.name + "'");
        }
    }

    Camera[] _camBuf;

    /// <summary>Stop drawing without tearing the rig down (pause, panel lost).</summary>
    public void SetVisible(bool visible)
    {
        if (Camera != null && Camera.enabled != visible) Camera.enabled = visible;
        if (Canvas != null && Canvas.enabled != visible) Canvas.enabled = visible;
    }

    public void Destroy()
    {
        if (Canvas != null) UnityEngine.Object.Destroy(Canvas.gameObject);
        if (Camera != null) UnityEngine.Object.Destroy(Camera.gameObject);
        Canvas = null; Camera = null; Root = null;
        Ready = false;
    }
}
#endif
