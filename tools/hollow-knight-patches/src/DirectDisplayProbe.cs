#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DualSouls.DualScreen;
using UnityEngine;
using UnityEngine.UI;

namespace HollowKnightPatches
{
    /// <summary>
    /// Opt-in display-1 transport diagnostic. It deliberately contains no
    /// gameplay integration and owns only its runtime-authored diagnostic UI.
    /// </summary>
    public sealed class DirectDisplayProbe : MonoBehaviour
    {
        const string CONFIG_FILE = "hollow_knight_direct_display_probe";
        const int DISPLAY_INDEX = 1;
        const int CONTENT_LAYER = 6;
        const int OVERLAY_LAYER = 7;
        const int FALLBACK_WIDTH = 1240;
        const int FALLBACK_HEIGHT = 1080;

        static readonly Dictionary<string, string> EmptyConfig =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, string> _config = EmptyConfig;
        static DirectDisplayProbe _instance;

        DirectDisplayPresentation _presentation;
        DirectDisplayHost _host;
        DiagnosticContent _content;
        int _displayCount;
        float _nextFenceMaintenance;
        bool _shutdown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Bootstrap()
        {
            if (!ShouldRun()) return;
            if (_instance != null) return;

            var go = new GameObject("__HollowKnightDirectDisplayProbe__");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DirectDisplayProbe>();
        }

        public static bool ShouldRun()
        {
            string path = Path.Combine(
                Application.persistentDataPath,
                CONFIG_FILE);

            try
            {
                if (!File.Exists(path)) return false;
                if (!TryParseConfig(File.ReadAllLines(path), out var parsed))
                    return false;

                // Valid opt-in markers are enabled=1 and enabled=true.
                if (!parsed.TryGetValue("enabled", out string enabled))
                    return false;
                if (enabled != "1" &&
                    !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
                    return false;

                _config = parsed;
                return true;
            }
            catch (Exception e)
            {
                _config = EmptyConfig;
                Debug.LogWarning("[HK DirectDisplayProbe] config read failed: " +
                                 e.Message);
                return false;
            }
        }

        static bool TryParseConfig(
            string[] lines,
            out Dictionary<string, string> parsed)
        {
            parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0 || separator == line.Length - 1)
                    return false;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key.Length == 0 || value.Length == 0 || parsed.ContainsKey(key))
                    return false;
                parsed.Add(key, value);
            }

            return true;
        }

        static int ReadPositiveConfigInt(string key, int fallback)
        {
            if (_config.TryGetValue(key, out string raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture,
                             out int value) && value > 0)
                return value;
            return fallback;
        }

        static void TryStep(Action step, List<Exception> failures)
        {
            try { step(); }
            catch (Exception e) { failures.Add(e); }
        }

        static void LogFailures(string context, List<Exception> failures)
        {
            if (failures.Count == 0) return;
            Debug.LogError(new AggregateException(context, failures));
        }

        void Start()
        {
            DirectDisplayTouch.ConfigureTargetDisplay(DISPLAY_INDEX);
            DirectDisplayTouch.Enabled = false;

            _presentation = new DirectDisplayPresentation(
                transform,
                DISPLAY_INDEX,
                CONTENT_LAYER,
                OVERLAY_LAYER,
                FALLBACK_WIDTH,
                FALLBACK_HEIGHT,
                ReadPositiveConfigInt);
            _host = new DirectDisplayHost(
                requestActivation: RequestActivation,
                setPresentationVisible: visible =>
                {
                    if (_presentation != null) _presentation.SetVisible(visible);
                },
                setTouchFenceActive: SetTouchFenceActive,
                releasePresentation: ReleasePresentation);

            // Subscribe before initial presence is published so the probe stays
            // resident and can react to a later display generation.
            _displayCount = Display.displays.Length;
            Display.onDisplaysUpdated += OnDisplaysUpdated;
            _host.SetDisplayPresent(_displayCount > DISPLAY_INDEX);
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

            // Display activation settles asynchronously. Shutdown or object
            // replacement during that yield invalidates the old continuation.
            if (host.IsDisposed || !ReferenceEquals(_host, host) ||
                !ReferenceEquals(_presentation, presentation)) yield break;

            bool present = Display.displays.Length > DISPLAY_INDEX;
            host.SetDisplayPresent(present);
            if (!present || !presentation.Ready)
            {
                host.SetPresentationReady(false);
                yield break;
            }

            // Build diagnostic content only after the shared presentation has
            // activated, settled, measured the panel, and created its root.
            if (_content == null &&
                !TryAttachDiagnosticContent(presentation, host)) yield break;

            host.SetPresentationReady(true, presentation.Width, presentation.Height);
            Debug.Log("[HK DirectDisplayProbe] transport ready");
        }

        bool TryAttachDiagnosticContent(
            DirectDisplayPresentation presentation,
            DirectDisplayHost host)
        {
            DiagnosticContent candidate = null;
            try
            {
                candidate = new DiagnosticContent(
                    presentation.Root,
                    CONTENT_LAYER,
                    presentation.Width,
                    presentation.Height);
                host.AttachContent(candidate);
                _content = candidate;
                return true;
            }
            catch (Exception attachFailure)
            {
                var failures = new List<Exception> { attachFailure };
                if (candidate != null)
                    TryStep(() => candidate.Dispose(), failures);
                // False readiness clears this presence generation's activation
                // ownership, allowing the next observation to retry safely.
                TryStep(() => host.SetPresentationReady(false), failures);
                LogFailures("[HK DirectDisplayProbe] diagnostic attach failed", failures);
                return false;
            }
        }

        void Update()
        {
            if (_host == null || !_host.IsActive || _presentation == null ||
                !_presentation.Ready)
                return;

            _presentation.SweepCameras();
            if (DirectDisplayTouch.Enabled &&
                Time.unscaledTime >= _nextFenceMaintenance)
            {
                _nextFenceMaintenance = Time.unscaledTime +
                    ReadPositiveConfigInt("fence_ms", 250) / 1000f;
                DirectDisplayTouch.InstallFence(gameObject);
            }
        }

        void OnDisplaysUpdated()
        {
            int currentCount = Display.displays.Length;
            if (currentCount != _displayCount)
            {
                Debug.Log("[HK DirectDisplayProbe] displays changed: " +
                          _displayCount + " -> " + currentCount);
                _displayCount = currentCount;
            }

            bool present = currentCount > DISPLAY_INDEX;
            var host = _host;
            var presentation = _presentation;
            var failures = new List<Exception>();
            if (!present)
            {
                TryStep(() =>
                {
                    if (host != null) host.SetDisplayPresent(false);
                }, failures);
                TryStep(() =>
                {
                    if (presentation != null) presentation.MarkUnavailable();
                }, failures);
            }
            else
            {
                TryStep(() =>
                {
                    if (host != null) host.SetDisplayPresent(true);
                }, failures);
            }
            LogFailures("[HK DirectDisplayProbe] display update failed", failures);
        }

        void OnApplicationPause(bool paused)
        {
            var host = _host;
            var presentation = _presentation;
            var failures = new List<Exception>();
            bool present = Display.displays.Length > DISPLAY_INDEX;
            if (!present)
            {
                TryStep(() =>
                {
                    if (host != null) host.SetDisplayPresent(false);
                }, failures);
                TryStep(() =>
                {
                    if (presentation != null) presentation.MarkUnavailable();
                }, failures);
            }
            else
            {
                TryStep(() =>
                {
                    if (host != null) host.SetDisplayPresent(true);
                }, failures);
            }

            // Publish current presence before resume so stale readiness cannot
            // become briefly active after a display was lost in the background.
            TryStep(() =>
            {
                if (host != null) host.SetPaused(paused);
            }, failures);
            LogFailures("[HK DirectDisplayProbe] pause transition failed", failures);
        }

        void SetTouchFenceActive(bool active)
        {
            DirectDisplayTouch.Enabled = active;
            if (active) DirectDisplayTouch.InstallFence(gameObject);
            else DirectDisplayTouch.RemoveFence();
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
            _content = null;
            TryStep(() => DirectDisplayTouch.RemoveFence(), failures);
            TryStep(ReleasePresentation, failures);
            LogFailures("[HK DirectDisplayProbe] shutdown failed", failures);
        }

        sealed class DiagnosticContent : IDirectDisplayContent
        {
            GameObject _ownedRoot;
            RectTransform _root;
            RectTransform _card;
            Text _title;
            Text _subtitle;

            public DiagnosticContent(
                RectTransform parent,
                int layer,
                float width,
                float height)
            {
                try
                {
                    _ownedRoot = new GameObject("HKDirectDisplayDiagnostic");
                    _ownedRoot.layer = layer;
                    _root = _ownedRoot.AddComponent<RectTransform>();
                    _root.SetParent(parent, false);
                    Stretch(_root);

                    AddPanel(_root, "Background", layer,
                             new Color(0.018f, 0.025f, 0.035f, 1f),
                             Vector2.zero, Vector2.one);
                    _card = AddPanel(_root, "DiagnosticCard", layer,
                                     new Color(0.075f, 0.105f, 0.135f, 1f),
                                     new Vector2(0.12f, 0.20f),
                                     new Vector2(0.88f, 0.80f));
                    AddPanel(_card, "TopRule", layer,
                             new Color(0.20f, 0.82f, 0.92f, 1f),
                             new Vector2(0f, 0.91f), Vector2.one);
                    AddPanel(_card, "BottomRule", layer,
                             new Color(0.20f, 0.82f, 0.92f, 1f),
                             Vector2.zero, new Vector2(1f, 0.09f));
                    AddPanel(_card, "CenterMarker", layer,
                             new Color(0.88f, 0.93f, 0.96f, 1f),
                             new Vector2(0.48f, 0.35f),
                             new Vector2(0.52f, 0.65f));

                    Font font = null;
                    try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                    catch (Exception) { }
                    if (font != null)
                    {
                        _title = AddText(_card, "Title", layer, font,
                                         "HOLLOW KNIGHT DIRECT DISPLAY",
                                         new Vector2(0.06f, 0.57f),
                                         new Vector2(0.94f, 0.86f));
                        _subtitle = AddText(_card, "Subtitle", layer, font,
                                            "TRANSPORT ONLY",
                                            new Vector2(0.06f, 0.14f),
                                            new Vector2(0.94f, 0.43f));
                    }

                    OnPanelGeometry(width, height);
                    _root.gameObject.SetActive(false);
                }
                catch (Exception constructionFailure)
                {
                    var failures = new List<Exception> { constructionFailure };
                    TryStep(Dispose, failures);
                    throw new AggregateException(
                        "Direct-display diagnostic construction failed", failures);
                }
            }

            static RectTransform AddPanel(
                RectTransform parent,
                string name,
                int layer,
                Color color,
                Vector2 anchorMin,
                Vector2 anchorMax)
            {
                var panelObject = new GameObject(name);
                panelObject.layer = layer;
                var panel = panelObject.AddComponent<RectTransform>();
                panel.SetParent(parent, false);
                panel.anchorMin = anchorMin;
                panel.anchorMax = anchorMax;
                panel.offsetMin = Vector2.zero;
                panel.offsetMax = Vector2.zero;
                var image = panelObject.AddComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
                return panel;
            }

            static Text AddText(
                RectTransform parent,
                string name,
                int layer,
                Font font,
                string value,
                Vector2 anchorMin,
                Vector2 anchorMax)
            {
                var textObject = new GameObject(name);
                textObject.layer = layer;
                var rect = textObject.AddComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var text = textObject.AddComponent<Text>();
                text.font = font;
                text.text = value;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(0.88f, 0.93f, 0.96f, 1f);
                text.raycastTarget = false;
                return text;
            }

            static void Stretch(RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            public void SetTransportActive(bool active)
            {
                if (_root != null) _root.gameObject.SetActive(active);
            }

            public void OnPanelGeometry(float width, float height)
            {
                if (_root == null) return;
                float shortSide = Mathf.Max(1f, Mathf.Min(width, height));
                float margin = Mathf.Max(8f, shortSide * 0.018f);
                if (_card != null)
                {
                    _card.offsetMin = new Vector2(margin, margin);
                    _card.offsetMax = new Vector2(-margin, -margin);
                }

                int titleSize = Mathf.Max(20, Mathf.RoundToInt(shortSide * 0.047f));
                if (_title != null) _title.fontSize = titleSize;
                if (_subtitle != null)
                    _subtitle.fontSize = Mathf.Max(16, Mathf.RoundToInt(titleSize * 0.62f));
            }

            public void Dispose()
            {
                if (_ownedRoot == null) return;
                UnityEngine.Object.Destroy(_ownedRoot);
                _ownedRoot = null;
                _root = null;
                _card = null;
                _title = null;
                _subtitle = null;
            }
        }
    }
}
#endif
