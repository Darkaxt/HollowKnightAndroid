// HkMapScreen — the Hollow Knight Map tab on the shared DsShell frame.
//
// Silksong's presentation (DsMapScreen): one map surface filling the body,
// the area name in the header, FULL MAP / AREA MAP and MARKERS in the header,
// a fading RESET and zoom slider over the bottom-right, and marker mode taking
// the tab strip over with the unlocked pins and the bin. The content is
// HkMapView's private render of a clone of the game's own map.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using DualSouls.Mods;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;

namespace HollowKnightPatches.Lower
{
    public sealed class HkMapScreen : IDsScreen, IDsHeaderTitle, IDsActionBar, IDsTabStrip, IDsPresentationSuspend
    {
        enum State { Idle, NoMap, Map }

        const float MapBottomPad = 4f;
        const float SymbolSize = 260f;
        const float ControlsHold = 3f, ControlsFade = 0.6f;
        const float SliderInsetX = 10f, SliderInsetY = 12f;

        HkMapView _view;
        RectTransform _mapPanel, _noMapBox;
        RawImage _raw;
        Image _symbol;
        TmpText _noMapText;
        Rect _mapRect;                 // layout space, top-left origin
        State _state = State.Idle;
        bool _stateApplied, _buttonsShown;

        DsZoomSlider _slider;
        Image _sliderTrack, _sliderThumb;
        bool _sliderGesture;
        float _lastTouch;

        bool _markerMode, _erasing;
        int _markerPick = -1;
        readonly List<int> _stripTypes = new List<int>();

        string _zoneName = "", _zoneFor;
        float _nextHeader, _nextSymbolHunt;

        // Bench travel: tap a bench pin to arm, tap the same pin again to go.
        string _benchArmed;
        float _benchArmedUntil;
        string _toast;
        float _toastUntil;

        readonly Action _toggleMode, _enterMarkers, _exitMarkers, _resetView;

        public HkMapScreen()
        {
            Last = this;
            _toggleMode = ToggleMode;
            _resetView = ResetView;
            _enterMarkers = () => SetMarkerMode(true);
            _exitMarkers = () => SetMarkerMode(false);
        }

        public string Id { get { return "map"; } }
        public string Title { get { return "Map"; } }
        public bool Available { get { return DsFrame.InGame; } }

        /// <summary>The inventory's own Map and Quill art, once resident.</summary>
        public static Sprite TabArt() { return HkSprites.Find("inv_item_map_quill_combined"); }

        // ── build ───────────────────────────────────────────────────────────

        public void Build(RectTransform host)
        {
            var layout = DsLayout.Current;
            float x = DsTheme.Pad, y = DsTheme.Pad;
            float w = layout.Width - DsTheme.Pad * 2f;
            float h = layout.Body.height - DsTheme.Pad - MapBottomPad;
            _mapRect = layout.InBody(new Rect(x, y, w, h));

            _mapPanel = DsWidgets.Box(host, "map", DsTheme.Ground).rectTransform;
            DsWidgets.Place(_mapPanel, x, y, w, h);

            var rawRt = DsWidgets.Rect(_mapPanel, "render");
            DsWidgets.Stretch(rawRt);
            _raw = rawRt.gameObject.AddComponent<RawImage>();
            _raw.raycastTarget = false;
            _raw.color = Color.white;

            _view = new HkMapView(host);
            _view.SetViewport(w, h);

            _noMapBox = DsWidgets.Rect(_mapPanel, "no-map");
            DsWidgets.Stretch(_noMapBox);
            _symbol = DsWidgets.Icon(_noMapBox, "symbol", null, Color.white);
            _noMapText = DsWidgets.Label(_noMapBox, "no-map-text", "", DsTheme.RowSize, DsTheme.InkDim, TmpAlign.Center);
            if (_noMapText != null)
                DsWidgets.Place(_noMapText.rectTransform, 0f, h * 0.5f + SymbolSize * 0.55f, w, 60f);

            // A soft four-edge fade over the map's hard cut, as on Silksong.
            var fade = DsWidgets.Icon(_mapPanel, "edge-fade", DsTheme.EdgeFade, Color.white);
            fade.type = Image.Type.Sliced;
            fade.useSpriteMesh = false;
            fade.preserveAspect = false;
            fade.raycastTarget = false;
            DsWidgets.Stretch(fade.rectTransform);

            Sprite track = DsSliderArt.Track, thumb = DsSliderArt.Thumb;
            if (track != null && thumb != null)
            {
                _slider = new DsZoomSlider(
                    new Rect(w - SliderInsetX - DsZoomSlider.TrackWidth, SliderInsetY,
                             DsZoomSlider.TrackWidth, h - SliderInsetY * 2f),
                    HkMapView.MinZoom, HkMapView.MaxZoom);
                _sliderTrack = DsWidgets.Icon(_mapPanel, "zoom-track", track, Color.white);
                _sliderTrack.type = Image.Type.Sliced;
                _sliderTrack.useSpriteMesh = false;
                _sliderTrack.preserveAspect = false;
                DsWidgets.Place(_sliderTrack.rectTransform, _slider.Track);
                _sliderThumb = DsWidgets.Icon(_mapPanel, "zoom-thumb", thumb, Color.white);
                _sliderThumb.useSpriteMesh = false;
                _sliderThumb.preserveAspect = false;
            }

            Apply(State.Idle, true);
            PaintSlider();
        }

        // ── lifecycle ───────────────────────────────────────────────────────

        public void OnShow()
        {
            if (_view == null) return;
            _view.Show();
            _raw.texture = _view.Texture;
            _nextHeader = 0f;
        }

        public void OnHide()
        {
            _markerMode = false;
            _sliderGesture = false;
            if (_slider != null) _slider.Release();
            _benchArmed = null;
            if (_view != null) _view.Hide();
            if (_raw != null) _raw.texture = null;
        }

        public void SuspendPresentation()
        {
            // Pause keeps the view, frame, zoom and marker choice.
            _sliderGesture = false;
            if (_slider != null) _slider.Release();
            if (_view != null) _view.Suspend();
        }

        // ── per frame ───────────────────────────────────────────────────────

        public void Tick(float dt)
        {
            if (_view == null) return;
            State want;
            if (!DsFrame.InGame) want = State.Idle;
            else
            {
                // A view that is owned but still setting up keeps the map
                // surface (the last frame, or black) rather than flashing the
                // No-Map symbol for the frame or two a setup takes.
                _view.Tick(dt);
                want = _view.SelectedAvailable ? State.Map : (_view.Map != null ? State.NoMap : State.Idle);
            }
            Apply(want, false);
            if (_state != State.Map && _markerMode) _markerMode = false;
            RefreshHeader();
            PaintSlider();
        }

        /// <summary>QA (#37): "found/total" per collectible kind.</summary>
        public static string ChecklistReport()
        {
            ChecklistArea totals;
            HkCollectibles.Checklist(null, out totals);
            var sb = new System.Text.StringBuilder();
            foreach (var k in HkCollectibles.Kinds)
                sb.Append(k.Key).Append('=').Append(totals.TotalOf(k.Key) - totals.MissingOf(k.Key)).Append('/').Append(totals.TotalOf(k.Key)).Append(' ');
            return sb.ToString().TrimEnd();
        }

        public int PinsShown { get { return _view != null ? _view.PinsShown : 0; } }

        /// <summary>The most recently built page (QA mapdump).</summary>
        public static HkMapScreen Last;

        /// <summary>QA: the on-screen image that shows the map texture.</summary>
        public string RawDump()
        {
            if (_raw == null) return "raw=null";
            var rt = _raw.rectTransform;
            return "raw active=" + _raw.gameObject.activeInHierarchy + " enabled=" + _raw.enabled +
                   " tex=" + (_raw.texture == null ? "null" : _raw.texture == (_view != null ? _view.Texture : null) ? "view" : "other") +
                   " color=" + _raw.color + " size=" + rt.rect.size + " state=" + _state +
                   " panel=" + (_mapPanel != null && _mapPanel.gameObject.activeInHierarchy);
        }

        void Apply(State s, bool force)
        {
            // Re-read every tick: the RenderTexture is released on OnHide and
            // recreated on OnShow, so the handle can change.
            if (_raw != null && _view != null && _raw.texture != _view.Texture) _raw.texture = _view.Texture;
            _buttonsShown = s == State.Map || (s == State.NoMap && _view != null && _view.HasAnyMap);
            if (!force && s == _state && _stateApplied) return;
            _state = s;
            _stateApplied = true;
            DsWidgets.SetActive(_raw, s == State.Map);
            DsWidgets.SetActive(_noMapBox, s != State.Map);
        }

        void RefreshHeader()
        {
            if (Time.unscaledTime < _nextHeader) return;
            _nextHeader = Time.unscaledTime + 0.5f;
            if (_state != State.Map) { HuntSymbol(); return; }
            string zone = HkMapView.CurrentZone();
            if (zone != _zoneFor) { _zoneFor = zone; _zoneName = ZoneName(zone); }
        }

        void PaintSlider()
        {
            if (_slider == null) return;
            if (_state != State.Map) _slider.Release();
            else if (_slider.Held) _lastTouch = Time.unscaledTime;
            float alpha = _state == State.Map ? ControlsAlpha() : 0f;
            DsWidgets.SetActive(_sliderTrack, alpha > 0f);
            DsWidgets.SetActive(_sliderThumb, alpha > 0f);
            if (alpha <= 0f) return;
            var ink = new Color(1f, 1f, 1f, alpha);
            _sliderTrack.color = ink;
            _sliderThumb.color = ink;
            DsWidgets.Place(_sliderThumb.rectTransform, _slider.Thumb(_view.ZoomLevel));
        }

        float ControlsAlpha()
        {
            if (_lastTouch <= 0f) return 0f;
            float since = Time.unscaledTime - _lastTouch;
            if (since <= ControlsHold) return 1f;
            if (since >= ControlsHold + ControlsFade) return 0f;
            return 1f - (since - ControlsHold) / ControlsFade;
        }

        /// <summary>The game's own No-Map symbol, found once it is resident.</summary>
        void HuntSymbol()
        {
            if (_symbol == null || _symbol.sprite != null) return;
            if (Time.unscaledTime < _nextSymbolHunt) return;
            _nextSymbolHunt = Time.unscaledTime + 5f;
            Sprite sprite = null;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Sprite>();
                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i];
                    if (c != null && c.name == "No_Map_symbol" && c.rect.width > 0 && c.rect.height > 0) { sprite = c; break; }
                }
            }
            catch { }
            if (sprite == null)
            {
                if (_noMapText != null && _noMapText.text.Length == 0) _noMapText.text = "No map of this area";
                return;
            }
            DsWidgets.FitCentred(_symbol, sprite, SymbolSize, SymbolSize);
            _symbol.color = Color.white;
            if (_noMapText != null) _noMapText.text = "";
        }

        /// <summary>HK's own "Map Zones" sheet; sub-zones fall back to their area's key.</summary>
        static string ZoneName(string z)
        {
            if (string.IsNullOrEmpty(z)) return "";
            try
            {
                string v = Lookup(z);
                if (v != null) return v;
                string rz = HkMapView.ResolveMapArea(z);
                if (rz != z) { v = Lookup(rz); if (v != null) return v; }
            }
            catch { }
            switch (z)
            {
                case "TOWN": return "Dirtmouth";
                case "KINGS_PASS": return "King's Pass";
                case "CROSSROADS": case "SHAMAN_TEMPLE": return "Forgotten Crossroads";
                case "GREEN_PATH": return "Greenpath";
                case "WASTES": case "QUEENS_STATION": return "Fungal Wastes";
                case "FOG_CANYON": case "MONOMON_ARCHIVE": return "Fog Canyon";
                case "CLIFFS": return "Howling Cliffs";
                case "MINES": case "PEAK": return "Crystal Peak";
                case "DEEPNEST": case "BEASTS_DEN": return "Deepnest";
                case "CITY": case "KINGS_STATION": case "SOUL_SOCIETY": case "LURIENS_TOWER": return "City of Tears";
                case "WATERWAYS": case "GODSEEKER_WASTE": return "Royal Waterways";
                case "ABYSS": return "Ancient Basin";
                case "ROYAL_GARDENS": return "Queen's Gardens";
                case "RESTING_GROUNDS": return "Resting Grounds";
                case "OUTSKIRTS": case "HIVE": case "COLOSSEUM": return "Kingdom's Edge";
                case "WHITE_PALACE": return "White Palace";
                default: return "";
            }
        }

        static string Lookup(string key)
        {
            if (!TeamCherry.Localization.Language.Has(key, "Map Zones")) return null;
            string v = TeamCherry.Localization.Language.Get(key, "Map Zones");
            if (string.IsNullOrEmpty(v) || v == key || v.Contains("#!#")) return null;
            return v.Replace("<br>", " ").Trim();
        }

        // ── header, actions, strip ──────────────────────────────────────────

        public string HeaderTitle
        {
            get
            {
                if (_toast != null && Time.unscaledTime < _toastUntil) return _toast;
                if (_state != State.Map || _view == null || _view.WorldMode) return null;
                return _zoneName;
            }
        }

        public void CollectActions(List<DsAction> into)
        {
            if (_view == null) return;
            if (!_buttonsShown) return;
            if (_markerMode)
            {
                into.Add(new DsAction("EXIT", _exitMarkers));
                return;
            }
            into.Add(new DsAction(_view.WorldMode ? "AREA MAP" : "FULL MAP", _toggleMode));
            if (_state == State.Map && HkMapView.AnyMarkerUnlocked())
                into.Add(new DsAction("MARKERS", _enterMarkers));
            float alpha = ControlsAlpha();
            if (_state == State.Map && alpha > 0f && _view.ViewMoved)
                into.Add(new DsAction("RESET", _resetView, false, DsActionPlace.Pane, alpha));
        }

        public Rect ActionPane
        {
            get
            {
                const float w = 220f, margin = 24f;
                float h = DsActionBar.PaneBand(1);
                var body = DsLayout.Current.Body;
                float right = body.xMax - margin;
                if (_slider != null) right = Mathf.Min(right, _mapRect.x + _slider.Hit.xMin);
                return new Rect(right - w, body.yMax - h - margin, w, h);
            }
        }

        void ToggleMode()
        {
            if (_view == null) return;
            _view.SetWorldMode(!_view.WorldMode);
            _benchArmed = null;
        }

        void ResetView()
        {
            if (_view != null) _view.ResetAnimated();
            _lastTouch = 0f;
        }

        void SetMarkerMode(bool on)
        {
            _markerMode = on && _state == State.Map && HkMapView.AnyMarkerUnlocked();
            if (!_markerMode) return;
            // Pins go anywhere, so marker mode works on the whole map.
            if (!_view.WorldMode) _view.SetWorldMode(true);
            _erasing = false;
            if (_markerPick < 0 || !HkMapView.MarkerUnlocked(_markerPick))
            {
                _markerPick = -1;
                for (int i = 0; i < HkMapView.MarkerTypes; i++)
                    if (HkMapView.MarkerUnlocked(i)) { _markerPick = i; break; }
            }
        }

        public bool StripOverride { get { return _markerMode; } }

        public void CollectStrip(List<DsStripItem> into)
        {
            _stripTypes.Clear();
            for (int i = 0; i < HkMapView.MarkerTypes; i++)
            {
                if (!HkMapView.MarkerUnlocked(i)) continue;
                int left = HkMapView.MarkerSpare(i);
                into.Add(new DsStripItem
                {
                    Icon = _view != null ? _view.MarkerSprite(i) : null,
                    Badge = left.ToString(),
                    Selected = !_erasing && _markerPick == i,
                    Dim = left <= 0,
                });
                _stripTypes.Add(i);
            }
            into.Add(new DsStripItem { Icon = DsTrashArt.Sprite, Selected = _erasing });
        }

        public void OnStripSelect(int index)
        {
            if (index >= 0 && index < _stripTypes.Count)
            {
                _erasing = false;
                _markerPick = _stripTypes[index];
                return;
            }
            _erasing = true;
        }

        // ── input ───────────────────────────────────────────────────────────

        public void OnGesture(DsGesture g)
        {
            if (_view == null || _state != State.Map) return;
            Vector2 p = DsFrame.ToLayout(g.Position);
            bool inMap = _mapRect.Contains(p);

            if (g.Type == DsGestureType.Down) _view.StopMotion();
            if (SliderGesture(g, p)) return;
            if (inMap) _lastTouch = Time.unscaledTime;

            switch (g.Type)
            {
                case DsGestureType.Tap:
                    if (!inMap) return;
                    Vector3 world;
                    if (!_view.ViewportToWorld(ToViewport(p), out world)) return;
                    if (_markerMode) _view.PlaceOrRemoveMarker(world, _markerPick, _erasing);
                    else BenchTap(world);
                    break;
                case DsGestureType.Drag:
                    if (inMap) _view.Pan(g.Delta);
                    break;
                case DsGestureType.Fling:
                    if (inMap) _view.Fling(g.Delta);
                    break;
                case DsGestureType.Pinch:
                    if (inMap) _view.Zoom(g.Scale, ToViewport(p));
                    break;
            }
        }

        Vector2 ToViewport(Vector2 layoutPoint)
        {
            return new Vector2((layoutPoint.x - _mapRect.x) / Mathf.Max(1f, _mapRect.width),
                               1f - (layoutPoint.y - _mapRect.y) / Mathf.Max(1f, _mapRect.height));
        }

        bool SliderGesture(DsGesture g, Vector2 layoutPoint)
        {
            if (_slider == null) return false;
            Vector2 local = layoutPoint - _mapRect.position;
            if (g.Type == DsGestureType.Down)
            {
                _slider.Release();
                _sliderGesture = ControlsAlpha() > 0f && _slider.Grab(local, _view.ZoomLevel);
            }
            else if (g.Type == DsGestureType.Pinch)
            {
                _slider.Release();
                _sliderGesture = false;
            }
            if (!_sliderGesture) return false;
            _lastTouch = Time.unscaledTime;
            if (g.Type == DsGestureType.Drag && _slider.Held) _view.SetZoom(_slider.Drag(local));
            else if (g.Type == DsGestureType.Up) _slider.Release();
            return true;
        }

        /// <summary>
        /// Bench travel, under the same gate as the world-space map: the tweak
        /// runtime is ready and its menu is showing. Recorded benches only.
        /// </summary>
        void BenchTap(Vector3 world)
        {
            if (!global::HkStageHooks.TweaksAvailable || !global::HkStageHooks.TweaksMenuVisible) return;
            string scene = _view.BenchPinAt(world);
            if (string.IsNullOrEmpty(scene)) return;
            if (!global::HkStageHooks.IsBenchRecorded(scene))
            {
                Toast("Sit on that bench once to enable travel", 3.5f);
                return;
            }
            if (_benchArmed == scene && Time.unscaledTime < _benchArmedUntil)
            {
                _benchArmed = null;
                Toast("Traveling...", 2f);
                try { global::HkStageHooks.BenchWarp(scene); }
                catch (Exception e) { Toast(e.Message, 3f); }
                return;
            }
            _benchArmed = scene;
            _benchArmedUntil = Time.unscaledTime + 4f;
            Toast("Travel to " + scene + "? Tap the pin again", 4f);
        }

        void Toast(string text, float seconds)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + seconds;
        }
    }
}
#endif
