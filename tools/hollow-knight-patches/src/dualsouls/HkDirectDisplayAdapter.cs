#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DualSouls.DualScreen;
using UnityEngine;

namespace HollowKnightPatches
{
    /// <summary>
    /// Owns the direct-display transport for the existing Dual Souls runtime.
    /// It creates no game UI; the reference partials continue to own every
    /// HUD, frame, page, overlay, and transition decision.
    /// </summary>
    public sealed class HkDirectDisplayAdapter : MonoBehaviour, IDirectDisplayContent
    {
        const int DisplayIndex = 1;
        const int AttributeLayer = 3;
        const int HudLayer = 6;
        const int TutorialLayer = 7;
        const int FallbackWidth = 1240;
        const int FallbackHeight = 1080;
        const string SettingsFile = "game-settings.txt";
        const string EnabledKey = "dualscreen_enabled";

        static HkDirectDisplayAdapter _instance;

        readonly List<Touch> _touches = new List<Touch>(4);
        readonly List<DirectDisplayContact> _contacts =
            new List<DirectDisplayContact>(4);
        readonly DirectDisplayGestureTracker _gestures =
            new DirectDisplayGestureTracker();
        readonly List<Camera> _extraCameras = new List<Camera>(2);

        DirectDisplayPresentation _presentation;
        DirectDisplayHost _host;
        HKDualScreen _reference;
        int _displayCount;
        bool _shutdown;
        bool _contentDisposed;

        internal Camera AttributeCamera =>
            _presentation != null ? _presentation.Camera : null;
        internal Camera HudCamera =>
            _presentation != null ? _presentation.OverlayCamera : null;
        internal int PanelWidth =>
            _presentation != null && _presentation.Width > 0
                ? _presentation.Width : FallbackWidth;
        internal int PanelHeight =>
            _presentation != null && _presentation.Height > 0
                ? _presentation.Height : FallbackHeight;
        internal int TargetDisplayIndex => DisplayIndex;
        internal bool IsTransportActive => _host != null && _host.IsActive;

        internal int TouchCount => _gestures.TouchCount;
        internal float TouchX => _gestures.TouchX;
        internal float TouchY => _gestures.TouchY;
        internal float T0X => _gestures.T0X;
        internal float T0Y => _gestures.T0Y;
        internal float T1X => _gestures.T1X;
        internal float T1Y => _gestures.T1Y;
        internal int TapSequence => _gestures.TapSequence;
        internal int CleanTapSequence => _gestures.CleanTapSequence;
        internal float CleanTapX => _gestures.CleanTapX;
        internal float CleanTapY => _gestures.CleanTapY;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Bootstrap()
        {
            if (!IsProductionEnabled() || _instance != null) return;

            var go = new GameObject("__HollowKnightDualSoulsTransport__");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HkDirectDisplayAdapter>();
        }

        public static bool IsProductionEnabled()
        {
            try
            {
                string path = Path.Combine(
                    Application.persistentDataPath,
                    SettingsFile);
                if (!File.Exists(path)) return false;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    int separator = line.IndexOf('=');
                    if (separator <= 0 || separator == line.Length - 1) continue;
                    string key = line.Substring(0, separator).Trim();
                    if (!string.Equals(key, EnabledKey,
                                       StringComparison.OrdinalIgnoreCase))
                        continue;
                    string value = line.Substring(separator + 1).Trim();
                    return value == "1" ||
                        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HK DualSouls] settings read failed: " + e.Message);
            }
            return false;
        }

        static int ReadPositiveConfigInt(string key, int fallback)
        {
            return fallback;
        }

        static void TryStep(Action step, List<Exception> failures)
        {
            try { step(); }
            catch (Exception e) { failures.Add(e); }
        }

        static void LogFailures(string context, List<Exception> failures)
        {
            if (failures.Count > 0)
                Debug.LogError(new AggregateException(context, failures));
        }

        void Start()
        {
            DirectDisplayTouch.ConfigureTargetDisplay(DisplayIndex);
            DirectDisplayTouch.Enabled = false;

            _presentation = new DirectDisplayPresentation(
                transform,
                DisplayIndex,
                AttributeLayer,
                HudLayer,
                FallbackWidth,
                FallbackHeight,
                ReadPositiveConfigInt);
            _host = new DirectDisplayHost(
                requestActivation: RequestActivation,
                setPresentationVisible: visible =>
                {
                    if (_presentation != null) _presentation.SetVisible(visible);
                },
                setTouchFenceActive: SetTouchFenceActive,
                releasePresentation: ReleasePresentation);
            _host.AttachContent(this);
            _host.SetEnabled(true);

            _displayCount = Display.displays.Length;
            Display.onDisplaysUpdated += OnDisplaysUpdated;
            _host.SetDisplayPresent(_displayCount > DisplayIndex);
        }

        void RequestActivation()
        {
            if (_shutdown || _host == null || _host.IsDisposed) return;
            StartCoroutine(Bringup());
        }

        IEnumerator Bringup()
        {
            var presentation = _presentation;
            var host = _host;
            if (presentation == null || host == null) yield break;

            yield return presentation.Bringup();
            if (_shutdown || host.IsDisposed || !ReferenceEquals(_host, host) ||
                !ReferenceEquals(_presentation, presentation))
                yield break;

            bool present = Display.displays.Length > DisplayIndex;
            host.SetDisplayPresent(present);
            if (!present || !presentation.Ready)
            {
                host.SetPresentationReady(false);
                yield break;
            }

            if (_reference == null)
            {
                _reference = HKDualScreen.EnsureStarted();
                _reference.BindDirectDisplay(this);
            }

            host.SetPresentationReady(
                true,
                presentation.Width,
                presentation.Height);
            Debug.Log("[HK DualSouls] direct display ready " +
                      presentation.Width + "x" + presentation.Height);
        }

        void Update()
        {
            if (_host == null || !_host.IsActive || _presentation == null ||
                !_presentation.Ready)
                return;

            DirectDisplayTouch.CollectTargetDisplay(_touches);
            _contacts.Clear();
            float width = Mathf.Max(1f, PanelWidth);
            float height = Mathf.Max(1f, PanelHeight);
            for (int i = 0; i < _touches.Count; i++)
            {
                Touch touch = _touches[i];
                _contacts.Add(new DirectDisplayContact(
                    touch.fingerId,
                    Mathf.Clamp01(touch.position.x / width),
                    Mathf.Clamp01(1f - touch.position.y / height)));
            }
            _gestures.Update(
                _contacts,
                Time.unscaledTime,
                DirectDisplayTouch.TargetCanceledThisFrame);

            DirectDisplayTouch.InstallFence(gameObject);
        }

        void OnDisplaysUpdated()
        {
            int current = Display.displays.Length;
            _displayCount = current;
            bool present = current > DisplayIndex;
            var failures = new List<Exception>();

            if (!present)
            {
                TryStep(() =>
                {
                    if (_host != null) _host.SetDisplayPresent(false);
                }, failures);
                TryStep(() =>
                {
                    if (_presentation != null) _presentation.MarkUnavailable();
                }, failures);
            }
            else
            {
                TryStep(() =>
                {
                    if (_host != null) _host.SetDisplayPresent(true);
                }, failures);
            }
            LogFailures("[HK DualSouls] display update failed", failures);
        }

        void OnApplicationPause(bool paused)
        {
            var failures = new List<Exception>();
            bool present = Display.displays.Length > DisplayIndex;
            TryStep(() =>
            {
                if (_host != null) _host.SetDisplayPresent(present);
            }, failures);
            if (!present)
            {
                TryStep(() =>
                {
                    if (_presentation != null) _presentation.MarkUnavailable();
                }, failures);
            }
            TryStep(() =>
            {
                if (_host != null) _host.SetPaused(paused);
            }, failures);
            LogFailures("[HK DualSouls] pause transition failed", failures);
        }

        internal void SetProductEnabled(bool enabled)
        {
            if (_host != null) _host.SetEnabled(enabled);
        }

        internal Camera CreateRoleCamera(
            string name,
            int layer,
            int depth,
            CameraClearFlags clearFlags)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = layer;
            var camera = go.AddComponent<Camera>();
            camera.targetDisplay = DisplayIndex;
            camera.orthographic = true;
            camera.aspect = (float)PanelWidth / PanelHeight;
            camera.cullingMask = 1 << layer;
            camera.clearFlags = clearFlags;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = -100f;
            camera.farClipPlane = 100f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.depth = depth;
            _extraCameras.Add(camera);
            return camera;
        }

        internal void ConfigureSharedRoleCameras()
        {
            ConfigureRoleCamera(AttributeCamera, AttributeLayer, 99,
                                CameraClearFlags.Nothing);
            ConfigureRoleCamera(HudCamera, HudLayer, 100,
                                CameraClearFlags.Nothing);
        }

        static void ConfigureRoleCamera(
            Camera camera,
            int layer,
            int depth,
            CameraClearFlags clearFlags)
        {
            if (camera == null) return;
            camera.gameObject.layer = layer;
            camera.targetDisplay = DisplayIndex;
            camera.orthographic = true;
            camera.cullingMask = 1 << layer;
            camera.clearFlags = clearFlags;
            camera.backgroundColor = Color.black;
            camera.depth = depth;
        }

        internal void RenderBlackSafetyFrame(Camera clearCamera)
        {
            if (clearCamera == null) return;
            bool wasEnabled = clearCamera.enabled;
            CameraClearFlags oldFlags = clearCamera.clearFlags;
            int oldMask = clearCamera.cullingMask;
            try
            {
                clearCamera.enabled = false;
                clearCamera.targetDisplay = DisplayIndex;
                clearCamera.rect = new Rect(0f, 0f, 1f, 1f);
                clearCamera.clearFlags = CameraClearFlags.SolidColor;
                clearCamera.backgroundColor = Color.black;
                clearCamera.cullingMask = 0;
                clearCamera.Render();
            }
            finally
            {
                clearCamera.clearFlags = oldFlags;
                clearCamera.cullingMask = oldMask;
                clearCamera.enabled = wasEnabled;
            }
        }

        void SetTouchFenceActive(bool active)
        {
            DirectDisplayTouch.Enabled = active;
            if (active) DirectDisplayTouch.InstallFence(gameObject);
            else
            {
                _gestures.Cancel();
                _touches.Clear();
                _contacts.Clear();
                DirectDisplayTouch.RemoveFence();
            }
        }

        public void SetTransportActive(bool active)
        {
            try
            {
                if (_reference != null) _reference.SetDirectDisplayActive(active);
            }
            finally
            {
                if (!active && _reference != null)
                    RenderBlackSafetyFrame(_reference.ClearCamera);
            }
        }

        public void OnPanelGeometry(float width, float height)
        {
            if (_reference != null)
                _reference.OnDirectPanelGeometry(width, height);
        }

        public void Dispose()
        {
            if (_contentDisposed) return;
            if (_reference != null)
                _reference.ShutdownDirectDisplayAndRestore();
            if (!_contentDisposed) OnReferenceTeardownComplete(_reference);
        }

        internal void OnReferenceTeardownComplete(HKDualScreen reference)
        {
            if (_contentDisposed) return;
            if (_reference != null && reference != null &&
                !ReferenceEquals(_reference, reference))
                throw new InvalidOperationException(
                    "unexpected Hollow Knight teardown owner");
            _reference = null;
            for (int i = 0; i < _extraCameras.Count; i++)
                if (_extraCameras[i] != null)
                    Destroy(_extraCameras[i].gameObject);
            _extraCameras.Clear();
            _contentDisposed = true;
        }

        void ReleasePresentation()
        {
            if (_presentation == null) return;
            _presentation.Dispose();
            _presentation = null;
        }

        void OnApplicationQuit() { Shutdown(); }
        void OnDestroy() { Shutdown(); }

        void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            if (ReferenceEquals(_instance, this)) _instance = null;

            var failures = new List<Exception>();
            TryStep(() => Display.onDisplaysUpdated -= OnDisplaysUpdated, failures);
            if (_host != null) TryStep(() => _host.Dispose(), failures);
            _host = null;
            TryStep(() => DirectDisplayTouch.RemoveFence(), failures);
            TryStep(ReleasePresentation, failures);
            LogFailures("[HK DualSouls] shutdown failed", failures);
        }
    }

    /// <summary>
    /// Replaces the native blitter's backdrop brightness multiply while the
    /// existing Dual Souls capture camera continues to choose scene content.
    /// </summary>
    public sealed class HkBackdropDimmer : MonoBehaviour
    {
        Material _material;
        float _brightness = 1f;
        int _blurFactor = 1;

        public float Brightness
        {
            get { return _brightness; }
            set { _brightness = Mathf.Clamp01(value); }
        }

        public int BlurFactor
        {
            get { return _blurFactor; }
            set { _blurFactor = Mathf.Max(1, value); }
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Unlit/Texture");
                if (shader != null) _material = new Material(shader);
            }

            if (_material != null)
                _material.color = new Color(
                    _brightness, _brightness, _brightness, 1f);

            int divisor = Mathf.Max(1, _blurFactor);
            if (divisor == 1)
            {
                if (_material != null) Graphics.Blit(source, destination, _material);
                else Graphics.Blit(source, destination);
                return;
            }

            RenderTexture reduced = null;
            try
            {
                reduced = RenderTexture.GetTemporary(
                    Mathf.Max(1, source.width / divisor),
                    Mathf.Max(1, source.height / divisor),
                    0,
                    source.format);
                reduced.filterMode = FilterMode.Bilinear;
                if (_material != null) Graphics.Blit(source, reduced, _material);
                else Graphics.Blit(source, reduced);
                Graphics.Blit(reduced, destination);
            }
            finally
            {
                if (reduced != null) RenderTexture.ReleaseTemporary(reduced);
            }
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            _material = null;
        }
    }
}
#endif
