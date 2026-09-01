// Shared direct-display presentation technology. This is deliberately limited
// to Unity display activation, measured geometry, private render layers and the
// owned camera/canvas rig. Game content remains in each consumer assembly.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DualSouls.DualScreen
{
    public class DirectDisplayPresentation : IDisposable
    {
        public Camera Camera { get; private set; }
        public Canvas Canvas { get; private set; }
        public Camera OverlayCamera { get; private set; }
        public Canvas OverlayCanvas { get; private set; }
        public RectTransform Root { get; private set; }
        public RectTransform OverlayRoot { get; private set; }
        public bool Ready { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public int DisplayIndex { get; private set; }
        public int ContentLayer { get; private set; }
        public int OverlayLayer { get; private set; }

        /// <summary>
        /// The latest measured panel size, retained statically for compatibility
        /// with direct-display coordinate helpers in existing consumers.
        /// </summary>
        public static int PanelW { get; private set; }
        public static int PanelH { get; private set; }

        public static Vector2 ToLayout(Vector2 panelPoint)
        {
            return new Vector2(panelPoint.x, PanelH - panelPoint.y);
        }

        /// <summary>The camera used by ScreenSpaceCamera canvas hit testing.</summary>
        public static Camera UiCamera { get; private set; }
        static DirectDisplayPresentation _compatibilityOwner;

        readonly Transform _parent;
        readonly int _fallbackWidth;
        readonly int _fallbackHeight;
        readonly int _ownedLayerMask;
        readonly Func<string, int, int> _readConfigInt;
        CanvasScaler _scaler;
        CanvasScaler _overlayScaler;
        bool _bringupInProgress;
        bool _disposed;
        int _availabilityRevision;
        float _nextSweep;
        Camera[] _camBuf;

        public DirectDisplayPresentation(
            Transform parent,
            int displayIndex,
            int contentLayer,
            int overlayLayer,
            int fallbackWidth,
            int fallbackHeight,
            Func<string, int, int> readConfigInt)
        {
            if (displayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(displayIndex));
            if (contentLayer < 0 || contentLayer > 31)
                throw new ArgumentOutOfRangeException(nameof(contentLayer));
            if (overlayLayer < 0 || overlayLayer > 31)
                throw new ArgumentOutOfRangeException(nameof(overlayLayer));
            if (contentLayer == overlayLayer)
                throw new ArgumentException("Direct-display layers must be distinct.", nameof(overlayLayer));
            if (fallbackWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(fallbackWidth));
            if (fallbackHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(fallbackHeight));

            _parent = parent;
            DisplayIndex = displayIndex;
            ContentLayer = contentLayer;
            OverlayLayer = overlayLayer;
            _fallbackWidth = fallbackWidth;
            _fallbackHeight = fallbackHeight;
            _ownedLayerMask = (1 << contentLayer) | (1 << overlayLayer);
            _readConfigInt = readConfigInt;
        }

        int PositiveConfigInt(string key, int fallback)
        {
            int value = _readConfigInt != null ? _readConfigInt(key, fallback) : fallback;
            return value > 0 ? value : fallback;
        }

        /// <summary>
        /// Activates the configured display and builds the owned rig. Multiple
        /// startup/hot-plug coroutines serialize on this one presentation. The
        /// settle interval is required by Unity activation and is not a timeout.
        /// </summary>
        public IEnumerator Bringup()
        {
            while (_bringupInProgress) yield return null;
            if (_disposed || Ready) yield break;

            _bringupInProgress = true;
            try
            {
                var displays = Display.displays;
                if (displays.Length <= DisplayIndex)
                {
                    MarkUnavailable();
                    Debug.Log("[DualScreen] only " + displays.Length +
                              " display(s); target display not available");
                    yield break;
                }

                int availabilityRevision = _availabilityRevision;
                try { displays[DisplayIndex].Activate(); }
                catch (Exception e)
                {
                    MarkUnavailable();
                    Debug.LogError("[DualScreen] could not activate display " +
                                   DisplayIndex + ": " + e);
                    yield break;
                }

                // Activate is asynchronous and exposes no usable readiness
                // signal on the proven Android target. This settle interval is
                // required before drawing and must not be turned into a timeout.
                float settle = PositiveConfigInt("settle_ms", 1500) / 1000f;
                float until = Time.realtimeSinceStartup + settle;
                while (Time.realtimeSinceStartup < until) yield return null;

                // Teardown can run while activation is settling. Never touch a
                // refreshed display or rebuild this presentation after release.
                if (_disposed) yield break;

                displays = Display.displays;
                if (displays.Length <= DisplayIndex)
                {
                    MarkUnavailable();
                    Debug.Log("[DualScreen] target display disappeared during activation");
                    yield break;
                }

                // Detach/reattach during the settle belongs to a new presence
                // generation; a waiter must activate and settle that generation.
                if (availabilityRevision != _availabilityRevision)
                {
                    Debug.Log("[DualScreen] display changed during activation; retrying current generation");
                    yield break;
                }

                MeasurePanel(displays[DisplayIndex]);

                if (Camera == null)
                {
                    Build();
                }
                else
                {
                    var panelSize = new Vector2(Width, Height);
                    if (_scaler != null) _scaler.referenceResolution = panelSize;
                    if (_overlayScaler != null) _overlayScaler.referenceResolution = panelSize;
                    SweepCameras(force: true);
                }

                PublishCompatibilityState();
                Ready = true;
                Debug.Log("[DualScreen] display " + DisplayIndex + " up: " +
                          Width + "x" + Height + " (" +
                          ((float)Width / Height).ToString("F2") + ":1)" +
                          " canvas=" + Canvas.renderMode + " layers=" +
                          ContentLayer + "/" + OverlayLayer);
            }
            finally
            {
                _bringupInProgress = false;
            }
        }

        // renderingWidth/Height are 0x0 on the proven Android target. Prefer
        // system geometry, then rendering geometry, then the injected fallback.
        void MeasurePanel(Display display)
        {
            int width = display.systemWidth;
            int height = display.systemHeight;
            if (width <= 0 || height <= 0)
            {
                width = display.renderingWidth;
                height = display.renderingHeight;
            }
            if (width <= 0 || height <= 0)
            {
                width = PositiveConfigInt("panel_w", _fallbackWidth);
                height = PositiveConfigInt("panel_h", _fallbackHeight);
                Debug.LogWarning("[DualScreen] panel size unknown, assuming " +
                                 width + "x" + height);
            }

            Width = width;
            Height = height;
        }

        void PublishCompatibilityState()
        {
            _compatibilityOwner = this;
            PanelW = Width;
            PanelH = Height;
            UiCamera = Camera;
        }

        void Build()
        {
            var camGo = new GameObject("DirectDisplayCamera");
            camGo.transform.SetParent(_parent, false);
            camGo.layer = ContentLayer;
            Camera = camGo.AddComponent<Camera>();
            Camera.targetDisplay = DisplayIndex;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.cullingMask = 1 << ContentLayer;
            Camera.orthographic = true;
            Camera.nearClipPlane = -100f;
            Camera.farClipPlane = 100f;
            Camera.allowHDR = false;
            Camera.allowMSAA = false;
            Camera.useOcclusionCulling = false;
            Camera.depth = -50f;

            var overlayCamGo = new GameObject("DirectDisplayOverlayCamera");
            overlayCamGo.transform.SetParent(_parent, false);
            overlayCamGo.layer = OverlayLayer;
            OverlayCamera = overlayCamGo.AddComponent<Camera>();
            OverlayCamera.targetDisplay = DisplayIndex;
            OverlayCamera.clearFlags = CameraClearFlags.Depth;
            OverlayCamera.cullingMask = 1 << OverlayLayer;
            OverlayCamera.orthographic = true;
            OverlayCamera.nearClipPlane = -100f;
            OverlayCamera.farClipPlane = 100f;
            OverlayCamera.allowHDR = false;
            OverlayCamera.allowMSAA = false;
            OverlayCamera.useOcclusionCulling = false;
            OverlayCamera.depth = -40f;

            Canvas = BuildCanvas("DirectDisplayCanvas", "DirectDisplayRoot",
                                 ContentLayer, Camera, out RectTransform contentRoot,
                                 out _scaler);
            Root = contentRoot;
            OverlayCanvas = BuildCanvas("DirectDisplayOverlayCanvas",
                                        "DirectDisplayOverlayRoot", OverlayLayer,
                                        OverlayCamera, out RectTransform overlayRoot,
                                        out _overlayScaler);
            OverlayRoot = overlayRoot;

            SweepCameras(force: true);
            SetVisible(false);
        }

        Canvas BuildCanvas(
            string canvasName,
            string rootName,
            int layer,
            Camera camera,
            out RectTransform root,
            out CanvasScaler scaler)
        {
            var canvasGo = new GameObject(canvasName);
            canvasGo.transform.SetParent(_parent, false);
            canvasGo.layer = layer;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;

            scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Gesture acquisition is isolated outside uGUI, so this rig owns
            // neither a GraphicRaycaster nor an EventSystem.
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

        /// <summary>Keep both private layers off every non-owned camera.</summary>
        public void SweepCameras(bool force = false)
        {
            if (_disposed) return;
            if (!force && Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime +
                PositiveConfigInt("sweep_ms", 500) / 1000f;

            int count = Camera.allCamerasCount;
            if (count <= 0) return;
            if (_camBuf == null || _camBuf.Length < count)
                _camBuf = new Camera[count + 4];
            Camera.GetAllCameras(_camBuf);

            for (int i = 0; i < count; i++)
            {
                var camera = _camBuf[i];
                if (camera == null || IsOwnedCamera(camera)) continue;
                if ((camera.cullingMask & _ownedLayerMask) == 0) continue;
                camera.cullingMask &= ~_ownedLayerMask;
                Debug.Log("[DualScreen] cleared owned layers from camera '" +
                          camera.name + "'");
            }
        }

        bool IsOwnedCamera(Camera camera)
        {
            return camera == Camera || camera == OverlayCamera;
        }

        public void SetVisible(bool visible)
        {
            if (Camera != null && Camera.enabled != visible) Camera.enabled = visible;
            if (OverlayCamera != null && OverlayCamera.enabled != visible)
                OverlayCamera.enabled = visible;
            if (Canvas != null && Canvas.enabled != visible) Canvas.enabled = visible;
            if (OverlayCanvas != null && OverlayCanvas.enabled != visible)
                OverlayCanvas.enabled = visible;
        }

        public void MarkUnavailable()
        {
            if (_disposed) return;
            _availabilityRevision++;
            Ready = false;
            SetVisible(false);
        }

        public void Destroy()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _availabilityRevision++;
            SetVisible(false);
            if (ReferenceEquals(_compatibilityOwner, this))
            {
                _compatibilityOwner = null;
                PanelW = 0;
                PanelH = 0;
                UiCamera = null;
            }
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
            _camBuf = null;
            Ready = false;
            _bringupInProgress = false;
        }
    }
}
#endif
