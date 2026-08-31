// DsPresentation — the second screen itself: one display, two composition
// roles, and the discipline that keeps both roles from touching the game.
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
// Isolation from the game is enforced at both ends. Our cameras each render
// exactly one owned layer, and both layers are cleared from every other
// camera's mask -- on a slow sweep, because Unity has no "a camera was created"
// event and the game spawns cameras for cutscenes and bosses. The reverse
// direction (the game drawing our layers) is the one that would be visible to
// the player, so it is the one that gets swept.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DsPresentation
{
    /// <summary>Unity's index for the second panel.</summary>
    public const int DISPLAY = 1;

    // Layers 3 and 6 are the only unnamed layers in Silksong's TagManager.
    // Content/frame/pages/HUD share 6; overlays/fade share 3. Do not assign any
    // other layer to companion composition without new source evidence.
    public const int CONTENT_LAYER = 6;
    public const int OVERLAY_LAYER = 3;
    public const int LAYER = CONTENT_LAYER; // temporary compatibility for dormant sources
    const int OWNED_LAYER_MASK = (1 << CONTENT_LAYER) | (1 << OVERLAY_LAYER);

    // The AYN Thor's panel, used only until the real size is known.
    const int FALLBACK_W = 1240;
    const int FALLBACK_H = 1080;

    public Camera Camera { get; private set; }
    public Canvas Canvas { get; private set; }
    /// <summary>Content/frame/page/HUD camera, retained as Camera for compatibility.</summary>
    public Camera OverlayCamera { get; private set; }
    public Canvas OverlayCanvas { get; private set; }
    /// <summary>Content/frame/page/HUD root. Fills the panel.</summary>
    public RectTransform Root { get; private set; }
    /// <summary>Contextual overlay and fade root. Fills the panel.</summary>
    public RectTransform OverlayRoot { get; private set; }
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
    CanvasScaler _overlayScaler;
    bool _bringupInProgress;
    int _availabilityRevision;
    float _nextSweep;

    public DsPresentation(Transform parent) { _parent = parent; }

    /// <summary>
    /// Bring up the display and build the rig. Yields until the panel is live.
    /// </summary>
    public IEnumerator Bringup()
    {
        // Multiple startup/hot-plug coroutines share this instance. Wait for
        // the current owner without a cancellation timeout; if it failed, the
        // next waiter becomes the owner and retries against current displays.
        while (_bringupInProgress) yield return null;
        if (Ready) yield break;

        _bringupInProgress = true;
        try
        {
            var displays = Display.displays;
            if (displays.Length <= DISPLAY)
            {
                MarkUnavailable();
                Debug.Log("[DualScreen] only " + displays.Length + " display(s); second screen not available");
                yield break;
            }

            int availabilityRevision = _availabilityRevision;
            try { displays[DISPLAY].Activate(); }
            catch (Exception e)
            {
                MarkUnavailable();
                Debug.LogError("[DualScreen] could not activate display " + DISPLAY + ": " + e);
                yield break;
            }

            // Activate is asynchronous and has no readiness signal on this
            // platform. This is a required settle interval, not a cancellation
            // timeout; a reattach always repeats it before drawing.
            float settle = DsConfig.Int("settle_ms", 1500) / 1000f;
            float until = Time.realtimeSinceStartup + settle;
            while (Time.realtimeSinceStartup < until) yield return null;

            // The panel may detach during the settle interval. Refresh the
            // array before indexing or measuring it.
            displays = Display.displays;
            if (displays.Length <= DISPLAY)
            {
                MarkUnavailable();
                Debug.Log("[DualScreen] second display disappeared during activation");
                yield break;
            }

            // A detach/reattach while Activate was settling belongs to a new
            // display generation. This owner activated the old generation, so
            // leave Ready false and let the serialized waiter activate and
            // settle the current one itself.
            if (availabilityRevision != _availabilityRevision)
            {
                Debug.Log("[DualScreen] display changed during activation; retrying current generation");
                yield break;
            }

            MeasurePanel(displays[DISPLAY]);

            if (Camera == null)
            {
                Build();
            }
            else
            {
                // Re-use the one rig, but refresh both canvases and camera
                // isolation after the display was activated again.
                var panelSize = new Vector2(Width, Height);
                if (_scaler != null) _scaler.referenceResolution = panelSize;
                if (_overlayScaler != null) _overlayScaler.referenceResolution = panelSize;
                SweepCameras(force: true);
            }

            Ready = true;

            Debug.Log("[DualScreen] second screen up: " + Width + "x" + Height +
                      " (" + ((float)Width / Height).ToString("F2") + ":1)" +
                      " canvas=" + Canvas.renderMode + " layers=" +
                      CONTENT_LAYER + "/" + OVERLAY_LAYER);
        }
        finally
        {
            _bringupInProgress = false;
        }
    }

    // renderingWidth/Height are always 0x0 here, so take systemWidth/Height when
    // they are populated and fall back to what Android says the panel is.
    void MeasurePanel(Display d)
    {
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
        camGo.layer = CONTENT_LAYER;
        Camera = camGo.AddComponent<Camera>();
        Camera.targetDisplay = DISPLAY;
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = Color.black;
        Camera.cullingMask = 1 << CONTENT_LAYER;
        Camera.orthographic = true;
        Camera.nearClipPlane = -100f;
        Camera.farClipPlane = 100f;
        Camera.allowHDR = false;
        Camera.allowMSAA = false;
        Camera.useOcclusionCulling = false;
        Camera.depth = -50f;

        var overlayCamGo = new GameObject("DsOverlayCamera");
        overlayCamGo.transform.SetParent(_parent, false);
        overlayCamGo.layer = OVERLAY_LAYER;
        OverlayCamera = overlayCamGo.AddComponent<Camera>();
        OverlayCamera.targetDisplay = DISPLAY;
        OverlayCamera.clearFlags = CameraClearFlags.Depth;
        OverlayCamera.cullingMask = 1 << OVERLAY_LAYER;
        OverlayCamera.orthographic = true;
        OverlayCamera.nearClipPlane = -100f;
        OverlayCamera.farClipPlane = 100f;
        OverlayCamera.allowHDR = false;
        OverlayCamera.allowMSAA = false;
        OverlayCamera.useOcclusionCulling = false;
        OverlayCamera.depth = -40f;

        Canvas = BuildCanvas("DsCanvas", "DsRoot", CONTENT_LAYER, Camera,
                             out RectTransform contentRoot, out _scaler);
        Root = contentRoot;
        OverlayCanvas = BuildCanvas("DsOverlayCanvas", "DsOverlayRoot",
                                    OVERLAY_LAYER, OverlayCamera,
                                    out RectTransform overlayRoot, out _overlayScaler);
        OverlayRoot = overlayRoot;
        UiCamera = Camera;

        SweepCameras(force: true);
        SetVisible(false); // the host applies pause/presence/readiness together
    }

    Canvas BuildCanvas(string canvasName, string rootName, int layer, Camera camera,
                       out RectTransform root, out CanvasScaler scaler)
    {
        var canvasGo = new GameObject(canvasName);
        canvasGo.transform.SetParent(_parent, false);
        canvasGo.layer = layer;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;

        // Both roles use the same measured panel geometry so an overlay cannot
        // drift from the content it covers on a differently sized display.
        scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Width, Height);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // No GraphicRaycaster and no EventSystem: gesture acquisition remains
        // isolated in DsInput and cannot collide with the game's event system.
        var rootGo = new GameObject(rootName);
        rootGo.layer = layer;
        root = rootGo.AddComponent<RectTransform>();
        root.SetParent(canvasGo.transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        return canvas;
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

        for (int i = 0; i < count; i++)
        {
            var c = _camBuf[i];
            if (c == null || IsOwnedCamera(c)) continue;
            if ((c.cullingMask & OWNED_LAYER_MASK) == 0) continue;
            c.cullingMask &= ~OWNED_LAYER_MASK;
            Debug.Log("[DualScreen] cleared owned layers from camera '" + c.name + "'");
        }
    }

    bool IsOwnedCamera(Camera camera)
    {
        return camera == Camera || camera == OverlayCamera;
    }

    Camera[] _camBuf;

    /// <summary>Stop drawing without tearing the rig down (pause, panel lost).</summary>
    public void SetVisible(bool visible)
    {
        if (Camera != null && Camera.enabled != visible) Camera.enabled = visible;
        if (OverlayCamera != null && OverlayCamera.enabled != visible) OverlayCamera.enabled = visible;
        if (Canvas != null && Canvas.enabled != visible) Canvas.enabled = visible;
        if (OverlayCanvas != null && OverlayCanvas.enabled != visible) OverlayCanvas.enabled = visible;
    }

    public void MarkUnavailable()
    {
        _availabilityRevision++;
        Ready = false;
        SetVisible(false);
    }

    public void Destroy()
    {
        if (OverlayCanvas != null) UnityEngine.Object.Destroy(OverlayCanvas.gameObject);
        if (Canvas != null) UnityEngine.Object.Destroy(Canvas.gameObject);
        if (OverlayCamera != null) UnityEngine.Object.Destroy(OverlayCamera.gameObject);
        if (Camera != null) UnityEngine.Object.Destroy(Camera.gameObject);
        Canvas = null;
        OverlayCanvas = null;
        Camera = null;
        OverlayCamera = null;
        Root = null;
        OverlayRoot = null;
        _scaler = null;
        _overlayScaler = null;
        UiCamera = null;
        Ready = false;
        _bringupInProgress = false;
    }
}
#endif
