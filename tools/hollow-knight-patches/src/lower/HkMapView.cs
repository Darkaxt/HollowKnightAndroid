// HkMapView — the Hollow Knight map behind the lower-screen Map page.
//
// A private clone of the game's own map (GameManager.gameMap) lives far from
// everything else on the shell's content layer, and a private orthographic
// camera renders it into a RenderTexture the page shows in a RawImage. The
// clone is driven by the game's own GameMap methods (SetupMap, QuickMap<Area>,
// WorldMap, PositionCompass, SetupMapMarkers) exactly as the device-proven
// world-space implementation did; only the presentation changed.
//
// Cost: the camera is enabled only on frames where something changed (pose,
// setup, compass, markers) plus a slow safety refresh, so the RenderTexture
// simply holds the last frame the rest of the time.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using DualSouls.DualScreen;
using DualSouls.Mods;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace HollowKnightPatches.Lower
{
    sealed class HkMapView
    {
        public const float MinZoom = 0.25f; // one minimum for both games (#65; Silksong is canonical)
        public const float MaxZoom = 6f;
        /// <summary>One equal margin around the fitted area, in fractions of the half-height.</summary>
        const float Margin = 0.08f;
        const float RefreshSeconds = 0.5f;
        const float ResetSeconds = 0.35f;
        static readonly Vector3 FarAway = new Vector3(20000f, 20000f, 0f);
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        static FieldInfo _fiPd, _fiGm, _fiDisplaying, _fiOX, _fiOY, _fiSW, _fiSH;
        static MethodInfo _miSceneChanged;
        static bool _reflected;

        readonly Transform _owner;
        GameObject _rig, _clone;
        GameMap _map, _realMap;
        UnityEngine.Object _source;
        Camera _cam;
        RenderTexture _rt;
        int _rtW = 64, _rtH = 64;
        float _aspect = 1f;
        int _builtFrame;

        // ── map state (ported from HKDualScreen.Bottom.Map) ──
        bool _needsSetup, _contentVisible, _worldMode, _available, _anyAvailable;
        string _lastZone = "", _lastScene = "";
        bool _compassPending, _lastCompassEquipped;
        int _lastScenesMapped = -1, _lastPinStamp = -1, _lastSgStamp = int.MinValue, _stowStamp = -1;
        bool _lastMapAvail;
        int _setupFails;
        float _compassFade = 1f, _compassAlphaApplied = -1f;
        GameObject _compassRsFor;
        Renderer[] _compassRs;
        readonly List<Material> _ownedMaterials = new List<Material>();
        float _nextPoll;

        // ── #37 collectible pins ──
        CollectiblePins _pins;
        HkCollectibles.MapIndex _pinIndex;
        int _pinStamp = int.MinValue;
        bool _pinsDirty;

        // ── fit ──
        Bounds _areaB;
        bool _areaBValid, _fallbackValid;
        string _areaBFor;
        int _areaBTries;
        Vector3 _fallbackCenter;
        float _fallbackOrtho = 8f;

        // ── view ──
        float _zoom = 1f;
        Vector2 _pan, _fling;
        float _resetT = 1f, _resetZoom = 1f;
        Vector2 _resetPan;
        Vector3 _posed;
        float _posedOrtho = -1f;
        float _layoutH = 1f;

        // ── render scheduling ──
        bool _presenting;
        int _forceFrames;
        float _nextRefresh;
        Vector3 _compassPos;
        bool _compassOn;

        public HkMapView(Transform owner) { _owner = owner; Last = this; }

        /// <summary>The most recently built view (QA mapdump).</summary>
        public static HkMapView Last;

        /// <summary>QA: what the lower map camera is pointed at and what it can see.</summary>
        public string Dump()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("clone=").Append(_clone != null ? (_clone.activeSelf ? "on" : "off") : "null")
              .Append(" available=").Append(_available).Append(" any=").Append(_anyAvailable)
              .Append(" setup=").Append(_needsSetup).Append(" content=").Append(_contentVisible)
              .Append(" presenting=").Append(_presenting).Append(" world=").Append(_worldMode)
              .Append(" zone=").Append(_lastZone).Append(" scene=").Append(_lastScene);
            if (_cam != null)
                sb.Append("\ncam enabled=").Append(_cam.enabled).Append(" pos=").Append(_cam.transform.position)
                  .Append(" ortho=").Append(_cam.orthographicSize.ToString("0.00")).Append(" rt=").Append(_rt != null ? _rt.width + "x" + _rt.height : "null");
            sb.Append("\nfit areaB=").Append(_areaBValid ? _areaB.ToString() : "none").Append(" fallback=").Append(_fallbackValid)
              .Append(" for=").Append(_areaBFor);
            if (_clone != null)
            {
                int total = 0, active = 0, onLayer = 0, opaque = 0;
                Bounds vis = new Bounds(); bool any = false;
                foreach (var r in _clone.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    total++;
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    active++;
                    if (r.gameObject.layer == Layer) onLayer++;
                    if (r.color.a > 0.05f) { opaque++; if (any) vis.Encapsulate(r.bounds); else { vis = r.bounds; any = true; } }
                }
                sb.Append("\nsprites total=").Append(total).Append(" active=").Append(active).Append(" onLayer=").Append(onLayer)
                  .Append(" visibleAlpha=").Append(opaque).Append(" bounds=").Append(any ? vis.ToString() : "none")
                  .Append(" clonePos=").Append(_clone.transform.position).Append(" scale=").Append(_clone.transform.lossyScale);
                var area = AreaObjFor(_map, _lastZone);
                sb.Append("\narea=").Append(area != null ? area.name + (area.activeInHierarchy ? " on" : " off") : "null");
                if (area != null)
                {
                    int rooms = 0, roomsOn = 0;
                    for (int i = 0; i < area.transform.childCount; i++) { rooms++; if (area.transform.GetChild(i).gameObject.activeSelf) roomsOn++; }
                    sb.Append(" rooms=").Append(roomsOn).Append('/').Append(rooms);
                }
            }
            sb.Append("\nrt drawn=").Append(DrawnPixels());
            if (_cam != null && _rt != null)
            {
                try { _cam.targetTexture = _rt; _cam.Render(); sb.Append(" afterRender=").Append(DrawnPixels()); }
                catch (Exception e) { sb.Append(" render failed: ").Append(e.GetType().Name); }
                sb.Append(" mask=").Append(_cam.cullingMask).Append(" layer=").Append(Layer);
            }
            return sb.ToString();
        }

        /// <summary>QA: RenderTexture pixels that differ from the background.</summary>
        string DrawnPixels()
        {
            if (_rt == null || !_rt.IsCreated()) return "no-rt";
            var prev = RenderTexture.active;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = _rt;
                tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, false);
                tex.Apply(false);
                Color32 g = DsTheme.Ground;
                int n = 0;
                foreach (var p in tex.GetPixels32())
                    if (Mathf.Abs(p.r - g.r) > 8 || Mathf.Abs(p.g - g.g) > 8 || Mathf.Abs(p.b - g.b) > 8) n++;
                return n + "/" + (_rt.width * _rt.height);
            }
            catch (Exception e) { return "read failed: " + e.GetType().Name; }
            finally { RenderTexture.active = prev; UnityEngine.Object.Destroy(tex); }
        }

        public RenderTexture Texture { get { return _rt; } }
        public bool WorldMode { get { return _worldMode; } }
        public bool HasAnyMap { get { return _anyAvailable; } }
        public bool ContentReady { get { return _clone != null && _available && _contentVisible && !_needsSetup; } }
        public float ZoomLevel { get { return _zoom; } }
        public bool ViewMoved { get { return _pan.sqrMagnitude > 1e-6f || Mathf.Abs(_zoom - 1f) > 0.001f; } }
        /// <summary>The selected view exists and is owned, ready or still being set up.</summary>
        public bool SelectedAvailable { get { return _clone != null && _available; } }

        public static string CurrentZone()
        {
            try { var gm = GameManager.UnsafeInstance; return gm != null ? gm.GetCurrentMapZone() : ""; }
            catch { return ""; }
        }

        public GameMap Map { get { return _map; } }

        // ── lifetime ────────────────────────────────────────────────────────

        /// <summary>The page's map rect, in layout units. Sizes the RenderTexture in pixels.</summary>
        public void SetViewport(float layoutW, float layoutH)
        {
            float scale = 1f;
            Vector2 layout = DsFrame.LayoutSize;
            if (DirectDisplayPresentation.PanelW > 0 && layout.x > 0f) scale = DirectDisplayPresentation.PanelW / layout.x;
            _rtW = Mathf.Clamp(Mathf.RoundToInt(layoutW * scale), 64, 4096);
            _rtH = Mathf.Clamp(Mathf.RoundToInt(layoutH * scale), 64, 4096);
            _aspect = layoutW / Mathf.Max(1f, layoutH);
            _layoutH = Mathf.Max(1f, layoutH);
        }

        void EnsureRig()
        {
            if (_rig != null) return;
            _rig = new GameObject("HkMapRig");
            UnityEngine.Object.DontDestroyOnLoad(_rig);
            _rig.transform.position = FarAway;
            _rig.layer = Layer;

            var camGo = new GameObject("HkMapCamera");
            camGo.transform.SetParent(_rig.transform, false);
            camGo.layer = Layer;
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = 8f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = DsTheme.Ground;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -100f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 1000f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            _cam.enabled = false;
            // It renders the private content layer into the page's texture;
            // keep the on-screen layer sweeps from clearing its mask.
            DirectDisplayPresentation.AddPrivateCamera(_cam);

            // Every teardown path of the page (the shell rebuilding destroys
            // its hosts) disposes the rig through this, so nothing leaks.
            var anchor = new GameObject("HkMapLifetime");
            anchor.transform.SetParent(_owner, false);
            anchor.AddComponent<HkMapLifetime>().View = this;
        }

        static int Layer { get { int l = DsFrame.ContentLayer; return l > 0 ? l : 3; } }

        void EnsureTexture()
        {
            if (_rt != null && (_rt.width != _rtW || _rt.height != _rtH)) ReleaseTexture();
            if (_rt != null && _rt.IsCreated()) return;
            if (_rt == null)
            {
                _rt = new RenderTexture(_rtW, _rtH, 16, RenderTextureFormat.Default);
                _rt.name = "HkMapRT";
                _rt.filterMode = FilterMode.Bilinear;
                _rt.antiAliasing = 1;
            }
            _rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(true, true, DsTheme.Ground);
            RenderTexture.active = prev;
            if (_cam != null) _cam.targetTexture = _rt;
            _forceFrames = Mathf.Max(_forceFrames, 1);
        }

        void ReleaseTexture()
        {
            if (_cam != null) { _cam.enabled = false; _cam.targetTexture = null; }
            if (_rt == null) return;
            try { _rt.Release(); } catch { }
            UnityEngine.Object.Destroy(_rt);
            _rt = null;
        }

        public void Show()
        {
            EnsureRig();
            EnsureTexture();
            _presenting = true;
            if (_clone != null)
            {
                if (!_clone.activeSelf) _clone.SetActive(true);
                // Equal stamp: the cached clone is still exact and only the
                // compass needs re-anchoring (GameMap.Update did not run while
                // it was inactive). Different: re-run the deferred setup.
                if (_contentVisible && ContentStamp() != _stowStamp) _needsSetup = true;
                _compassPending = true;
            }
            _forceFrames = Mathf.Max(_forceFrames, 2);
        }

        /// <summary>Stop rendering without changing anything else (pause).</summary>
        public void Suspend()
        {
            // Not _presenting: the shell resumes with Tick alone, no OnShow.
            // Tick does not run while paused, so the camera simply stays off.
            _fling = Vector2.zero;
            if (_cam != null) _cam.enabled = false;
            _forceFrames = Mathf.Max(_forceFrames, 1);
        }

        public void Hide()
        {
            _presenting = false;
            _fling = Vector2.zero;
            if (_clone != null)
            {
                _stowStamp = ContentStamp();
                if (_clone.activeSelf) _clone.SetActive(false);
            }
            ReleaseTexture();
        }

        public void Dispose()
        {
            ReleaseTexture();
            DestroyClone();
            if (_cam != null) DirectDisplayPresentation.RemovePrivateCamera(_cam);
            if (_rig != null) { UnityEngine.Object.Destroy(_rig); _rig = null; }
            _cam = null;
        }

        void DestroyClone()
        {
            for (int i = 0; i < _ownedMaterials.Count; i++)
                if (_ownedMaterials[i] != null) UnityEngine.Object.Destroy(_ownedMaterials[i]);
            _ownedMaterials.Clear();
            if (_pins != null) _pins.Clear();   // they die with the clone
            _pinIndex = null; _pinsDirty = false;
            if (_clone != null) UnityEngine.Object.Destroy(_clone);
            _clone = null; _map = null; _realMap = null; _source = null;
            _compassRsFor = null; _compassRs = null; _compassAlphaApplied = -1f;
            _contentVisible = false; _areaBValid = false; _fallbackValid = false; _areaBFor = null;
        }

        // ── clone ───────────────────────────────────────────────────────────

        static void Reflect()
        {
            if (_reflected) return;
            _reflected = true;
            var t = typeof(GameMap);
            _fiPd = t.GetField("pd", Private);
            _fiGm = t.GetField("gm", Private);
            _fiDisplaying = t.GetField("displayingCompass", Private);
            _fiOX = t.GetField("originOffsetX", Private);
            _fiOY = t.GetField("originOffsetY", Private);
            _fiSW = t.GetField("sceneWidth", Private);
            _fiSH = t.GetField("sceneHeight", Private);
            _miSceneChanged = t.GetMethod("OnActiveSceneChanged", Private);
        }

        bool EnsureClone()
        {
            var gm = GameManager.UnsafeInstance;
            if (gm == null) return _clone != null;
            var src = gm.gameMap;                        // null until the game first builds its map
            if (src == null) return _clone != null;
            if (_clone != null && !ReferenceEquals(_source, src)) DestroyClone();   // save reload rebuilt it
            if (_clone != null) return true;
            if (HeroController.UnsafeInstance == null) return false;               // the clone's Start needs the hero
            EnsureRig();
            Reflect();

            _clone = UnityEngine.Object.Instantiate(src, _rig.transform);
            _clone.name = "HkMapClone";
            _source = src;
            _map = _clone.GetComponent<GameMap>();
            _clone.transform.localPosition = Vector3.zero;
            SetLayerRecursive(_clone.transform, Layer);
            // Freeze the clone's FSMs so they cannot fight the real map and
            // inventory; GameMap's own Start/Update still run (compass).
            var fsms = _clone.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int i = 0; i < fsms.Length; i++) fsms[i].enabled = false;
            _clone.SetActive(true);
            // After activation, so Awake has certainly run: the clone's Awake subscribed OnActiveSceneChanged, which calls
            // gm.RefreshTilemapInfo(null) on the REAL GameManager mid-transition
            // and can latch the wrong scene's tilemap. The real map owns that.
            try
            {
                if (_miSceneChanged != null && _map != null)
                    UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= (UnityAction<Scene, Scene>)Delegate.CreateDelegate(
                        typeof(UnityAction<Scene, Scene>), _map, _miSceneChanged);
                else if (_miSceneChanged == null)
                    Debug.LogWarning("[HkMap] GameMap.OnActiveSceneChanged not found; clone stays subscribed");
            }
            catch (Exception e) { Debug.LogWarning("[HkMap] clone scene-unsub failed: " + e.Message); }

            _needsSetup = true;
            _contentVisible = false; _areaBValid = false; _fallbackValid = false; _areaBFor = null;
            _lastZone = ""; _lastScene = ""; _lastScenesMapped = -1; _lastPinStamp = -1;
            _lastSgStamp = int.MinValue; _compassPending = false; _setupFails = 0;
            _builtFrame = Time.frameCount;
            Debug.Log("[HkMap] map clone built");
            return true;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        // ── per frame ───────────────────────────────────────────────────────

        /// <summary>Drive the clone and the camera. True while the selected view is showing.</summary>
        public bool Tick(float dt)
        {
            if (!EnsureClone() || _map == null)
            {
                _available = _anyAvailable = false;
                if (_cam != null) _cam.enabled = false;
                return false;
            }
            if (_presenting && !_clone.activeSelf) _clone.SetActive(true);

            _anyAvailable = HasAnyMapOwned();
            _available = _worldMode ? _anyAvailable : HasMapForCurrentZone();
            if (!_available)
            {
                // The camera simply stops; nothing in the clone needs hiding,
                // because nothing but this camera can see it.
                _contentVisible = false;
                if (_cam != null) _cam.enabled = false;
                return false;
            }
            if (!_contentVisible) _needsSetup = true;

            Maintain();
            if (!ContentReady) { if (_cam != null) _cam.enabled = false; return false; }

            Fit();
            Animate(dt);
            Pose();
            Present();
            return true;
        }

        void Maintain()
        {
            var m = _map;
            var gm = GameManager.UnsafeInstance;
            var pd = PlayerData.instance;
            if (gm == null || pd == null) return;

            if (_needsSetup)
            {
                // One frame after the clone exists so its Start has run (hero).
                if (Time.frameCount <= _builtFrame) return;
                try
                {
                    // Savestate loads swap the singletons the clone latched in Start.
                    try { if (_fiPd != null) _fiPd.SetValue(m, pd); if (_fiGm != null) _fiGm.SetValue(m, gm); } catch { }
                    m.SetupMap();
                    SetupQuickMap(m);
                    SetLayerRecursive(_clone.transform, Layer);
                    _needsSetup = false; _contentVisible = true; _setupFails = 0;
                    _areaBTries = 3;
                    ResetView();
                    _lastCompassEquipped = pd.equippedCharm_2;
                    _lastPinStamp = PinStamp();
                    _lastMapAvail = HasMapForCurrentZone();
                    // This setup already revealed everything mapped so far.
                    _lastScenesMapped = pd.scenesMapped != null ? pd.scenesMapped.Count + (pd.mapAllRooms ? 1000000 : 0) : -1;
                    _forceFrames = Mathf.Max(_forceFrames, 3);
                    _pinsDirty = true; _nextPoll = 0f;
                }
                catch (Exception e)
                {
                    if (++_setupFails == 300) Debug.LogWarning("[HkMap] SetupMap still failing: " + e);
                }
                return;
            }

            string z = "", sc = "";
            try { z = gm.GetCurrentMapZone(); } catch { }
            sc = gm.sceneName ?? "";
            bool inTrans = false; try { inTrans = gm.IsInSceneTransition; } catch { }
            bool playing = false; try { playing = gm.gameState == GlobalEnums.GameState.PLAYING; } catch { }

            // Area change: full deferred setup. Sub-zones sharing one quick map do not count.
            if (ResolveMapArea(z) != ResolveMapArea(_lastZone)) { _needsSetup = true; _lastZone = z; }

            if (inTrans || !playing)
            {
                // Hold the compass hidden and frozen for the whole unsettled window.
                try { if (_fiDisplaying != null) _fiDisplaying.SetValue(m, false); } catch { }
                _compassFade = Mathf.MoveTowards(_compassFade, 0f, Time.unscaledDeltaTime * 14f);
                try { if (m.compassIcon != null && m.compassIcon.activeSelf) m.compassIcon.SetActive(false); } catch { }
                _compassPending = true;
            }
            else
            {
                if (sc != _lastScene || _compassPending)
                {
                    bool tilemapReady = false; try { tilemapReady = gm.tilemap != null; } catch { }
                    if (tilemapReady)
                    {
                        MirrorRoomState(m);
                        try { m.LevelReady(); m.PositionCompass(false); }
                        catch (Exception e) { Debug.LogWarning("[HkMap] compass re-anchor: " + e.Message); }
                        _lastScene = sc;
                        _compassPending = false;
                        _pinsDirty = true;   // rooms just left wrote their pickups to SceneData
                    }
                }
                // Re-assert tracking on HK's own condition (Wayward Compass, not in an interior).
                try
                {
                    if (pd.equippedCharm_2 && !m.inRoom)
                    {
                        if (_fiDisplaying != null) _fiDisplaying.SetValue(m, true);
                        if (m.compassIcon != null && !m.compassIcon.activeSelf) m.compassIcon.SetActive(true);
                    }
                }
                catch { }
                _compassFade = Mathf.MoveTowards(_compassFade, 1f, Time.unscaledDeltaTime * 6f);
            }
            SetCompassAlpha(m, _compassFade);

            if (pd.equippedCharm_2 != _lastCompassEquipped)
            {
                _lastCompassEquipped = pd.equippedCharm_2;
                MirrorRoomState(m);
                try { m.PositionCompass(false); } catch { }
                _forceFrames = Mathf.Max(_forceFrames, 1);
            }

            // Every QuickMap<Area>/WorldMap force-enables these; keep them honest.
            try
            {
                bool hs = !string.IsNullOrEmpty(pd.shadeScene) && pd.shadeScene != "None";
                bool hg = pd.hasDreamGate && !string.IsNullOrEmpty(pd.dreamGateScene);
                if (m.shadeMarker != null && m.shadeMarker.activeSelf != hs) m.shadeMarker.SetActive(hs);
                if (m.dreamGateMarker != null && m.dreamGateMarker.activeSelf != hg) m.dreamGateMarker.SetActive(hg);
            }
            catch { }

            // The rest is PlayerData polling; twice a second is plenty.
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;

            int cs = HkCollectibles.Stamp();
            if (_pinsDirty || cs != _pinStamp) { _pinStamp = cs; _pinsDirty = false; RefreshPins(); }

            int sg = 17;
            try { sg = (pd.shadeScene ?? "").GetHashCode() * 31 + (pd.dreamGateScene ?? "").GetHashCode() + (int)pd.dreamGateX; } catch { }
            if (_lastSgStamp != int.MinValue && sg != _lastSgStamp) _needsSetup = true;
            _lastSgStamp = sg;

            // Resting with the Quill fills in rooms: scenesMapped grows.
            int scm = pd.scenesMapped != null ? pd.scenesMapped.Count + (pd.mapAllRooms ? 1000000 : 0) : -1;
            if (scm >= 0 && scm != _lastScenesMapped)
            {
                _lastScenesMapped = scm;
                _needsSetup = true;
                return;
            }

            int ps = PinStamp();
            if (ps == _lastPinStamp) return;
            _lastPinStamp = ps;
            // Buying an area map: only the FULL path reveals rooms.
            bool mapNow = HasMapForCurrentZone();
            if (mapNow != _lastMapAvail) { _lastMapAvail = mapNow; _needsSetup = true; return; }
            try { m.SetupMap(true); } catch { }
            RefreshMarkers();
        }

        /// <summary>Re-place the native markers after a PlayerData marker change, and re-gate pins.</summary>
        public void RefreshMarkers()
        {
            if (_map == null || _clone == null) return;
            try { _map.SetupMapMarkers(); } catch (Exception e) { Debug.LogWarning("[HkMap] SetupMapMarkers: " + e.Message); }
            try { GateMarkers(_clone); } catch { }
            SetLayerRecursive(_clone.transform, Layer);
            _lastPinStamp = PinStamp();
            _forceFrames = Mathf.Max(_forceFrames, 2);
        }

        // ── #37 collectible pins ────────────────────────────────────────────

        void RefreshPins()
        {
            var table = HkCollectibles.Table;
            if (table == null || _map == null || _clone == null) return;
            try
            {
                if (_pins == null) _pins = new CollectiblePins(table);
                if (_pinIndex == null || _pinIndex.Root != _clone) _pinIndex = new HkCollectibles.MapIndex(_map);
                int sortLayer = 0, sortOrder = 0;
                var cr = _map.compassIcon != null ? _map.compassIcon.GetComponentInChildren<SpriteRenderer>(true) : null;
                if (cr != null) { sortLayer = cr.sortingLayerID; sortOrder = cr.sortingOrder; }
                _pins.Refresh(_pinIndex.RoomOf, HkCollectibles.Collected, _clone.transform, HkCollectibles.PinSize,
                              _clone.transform.position.z - 0.5f, Layer, sortLayer, sortOrder);
                LastPinsShown = _pins.Shown;
                _forceFrames = Mathf.Max(_forceFrames, 1);
            }
            catch (Exception e) { Debug.LogWarning("[HkMap] pins: " + e.Message); }
        }

        /// <summary>Pins currently showing (QA).</summary>
        public int PinsShown { get { return _pins != null ? _pins.Shown : 0; } }

        /// <summary>Pins after the latest refresh of any view (QA command).</summary>
        public static int LastPinsShown;

        // ── framing ─────────────────────────────────────────────────────────

        void Fit()
        {
            string area = _worldMode ? "__WORLD__" : ResolveMapArea(_lastZone);
            if (_areaBFor == area && _areaBTries <= 0) return;
            if (_areaBTries > 0) _areaBTries--;
            Bounds b;
            bool measured = _worldMode ? TryWorldBounds(_map, out b) : TryAreaObjectBounds(AreaObjFor(_map, _lastZone), out b);
            if (measured)
            {
                _areaB = b; _areaBValid = true; _fallbackValid = false;
            }
            else if (TryRendererBounds(out _fallbackCenter, out _fallbackOrtho))
            {
                _areaBValid = false; _fallbackValid = true;
            }
            // Recorded even when nothing measured, so an unmeasurable view is
            // retried only through _areaBTries, never every frame.
            _areaBFor = area;
            _forceFrames = Mathf.Max(_forceFrames, 1);
        }

        bool FitFrame(out Vector3 centre, out float baseOrtho)
        {
            centre = _clone.transform.position; baseOrtho = 8f;
            if (_areaBValid)
            {
                var tr = _clone.transform;
                centre = tr.TransformPoint(_areaB.center);
                Vector3 e = Vector3.Scale(_areaB.extents, tr.lossyScale);
                float hh = Mathf.Max(0.05f, 1f - Margin), hw = Mathf.Max(0.05f, _aspect - Margin);
                baseOrtho = Mathf.Max(0.5f, Mathf.Max(e.y / hh, e.x / hw));
                return true;
            }
            if (_fallbackValid) { centre = _fallbackCenter; baseOrtho = Mathf.Max(0.5f, _fallbackOrtho); return true; }
            return false;
        }

        void Animate(float dt)
        {
            if (_resetT < 1f)
            {
                _resetT = Mathf.Min(1f, _resetT + dt / ResetSeconds);
                float k = 1f - Mathf.Pow(1f - _resetT, 3f);
                _zoom = Mathf.Lerp(_resetZoom, 1f, k);
                _pan = Vector2.Lerp(_resetPan, Vector2.zero, k);
                if (_resetT >= 1f) { _zoom = 1f; _pan = Vector2.zero; }
            }
            if (_fling.sqrMagnitude > 0f)
            {
                Pan(_fling * dt);
                _fling *= Mathf.Exp(-5f * dt);
                if (_fling.sqrMagnitude < 900f) _fling = Vector2.zero;
            }
        }

        void Pose()
        {
            Vector3 centre; float baseOrtho;
            FitFrame(out centre, out baseOrtho);
            // Silksong's pan rule (#65): half a frame of reach at zoom 1 and below, growing as it zooms in.
            float reach = Mathf.Max(0.5f, 1f - 1f / Mathf.Max(_zoom, 1f));
            float maxY = Mathf.Max(0f, baseOrtho) * reach, maxX = maxY * Mathf.Max(_aspect, 0f);
            _pan = new Vector2(Mathf.Clamp(_pan.x, -maxX, maxX), Mathf.Clamp(_pan.y, -maxY, maxY));
            float ortho = Mathf.Max(0.05f, baseOrtho / _zoom);
            var pos = new Vector3(centre.x + _pan.x, centre.y + _pan.y, _clone.transform.position.z - 100f);
            if (_cam == null) return;
            if ((pos - _posed).sqrMagnitude > 1e-8f || Mathf.Abs(ortho - _posedOrtho) > 1e-5f)
            {
                _cam.transform.position = pos;
                _cam.transform.rotation = Quaternion.identity;
                _cam.orthographicSize = ortho;
                _cam.aspect = _aspect;
                _posed = pos; _posedOrtho = ortho;
                _forceFrames = Mathf.Max(_forceFrames, 1);
            }
        }

        void Present()
        {
            if (_cam == null) return;
            if (!_presenting) { _cam.enabled = false; return; }
            EnsureTexture();

            bool dirty = _forceFrames > 0 || Time.unscaledTime >= _nextRefresh;
            try
            {
                var c = _map.compassIcon;
                if (c != null)
                {
                    bool on = c.activeInHierarchy;
                    Vector3 p = c.transform.position;
                    if (on != _compassOn || (p - _compassPos).sqrMagnitude > 1e-6f) dirty = true;
                    _compassOn = on; _compassPos = p;
                }
            }
            catch { }
            if (_forceFrames > 0) _forceFrames--;
            if (dirty) _nextRefresh = Time.unscaledTime + RefreshSeconds;
            _cam.enabled = dirty;
        }

        // ── input ───────────────────────────────────────────────────────────

        float WorldPerLayout(float layoutH)
        {
            return _cam != null ? 2f * _cam.orthographicSize / Mathf.Max(1f, layoutH) : 0f;
        }

        /// <summary>Drag. Layout units, y up, as gestures are.</summary>
        public void Pan(Vector2 layoutDelta)
        {
            _resetT = 1f;
            _pan -= layoutDelta * WorldPerLayout(_layoutH);
        }

        public void Fling(Vector2 layoutVelocity) { _resetT = 1f; _fling = layoutVelocity; }

        public void StopMotion() { _fling = Vector2.zero; _resetT = 1f; }

        /// <summary>Pinch about a viewport point, which stays where it is.</summary>
        public void Zoom(float factor, Vector2 uv)
        {
            if (!(factor > 0f) || _cam == null) return;
            _resetT = 1f;
            float old = _zoom;
            _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
            float f = _zoom / old;
            if (Mathf.Abs(f - 1f) < 1e-5f) return;
            float o = _cam.orthographicSize, o2 = o / f;
            _pan += new Vector2((uv.x - 0.5f) * 2f * (o - o2) * _aspect, (uv.y - 0.5f) * 2f * (o - o2));
        }

        public void SetZoom(float zoom)
        {
            if (!(zoom > 0f)) return;
            _resetT = 1f;
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        }

        public void ResetView() { _zoom = 1f; _pan = Vector2.zero; _fling = Vector2.zero; _resetT = 1f; }

        public void ResetAnimated()
        {
            _fling = Vector2.zero;
            _resetZoom = _zoom; _resetPan = _pan; _resetT = 0f;
        }

        public void SetWorldMode(bool world)
        {
            if (_worldMode == world) return;
            _worldMode = world;
            _needsSetup = true;
            _areaBValid = false; _fallbackValid = false; _areaBFor = null; _areaBTries = 3;
            ResetView();
        }

        public bool ViewportToWorld(Vector2 uv, out Vector3 world)
        {
            world = Vector3.zero;
            if (_cam == null || !ContentReady) return false;
            world = _cam.ViewportToWorldPoint(new Vector3(uv.x, uv.y, 100f));
            return true;
        }

        // ── game rules (ported verbatim in behaviour) ───────────────────────

        public static bool HasAnyMapOwned()
        {
            try { var pd = PlayerData.instance; return pd != null && pd.hasMap; } catch { return false; }
        }

        public static bool HasMapForCurrentZone()
        {
            try
            {
                var pd = PlayerData.instance;
                if (pd == null || !pd.hasMap) return false;
                var g = GameManager.UnsafeInstance;
                string z = g != null ? g.GetCurrentMapZone() : "";
                switch (z)
                {
                    case "TOWN": case "KINGS_PASS": return pd.mapDirtmouth;
                    case "CROSSROADS": case "SHAMAN_TEMPLE": return pd.mapCrossroads;
                    case "GREEN_PATH": return pd.mapGreenpath;
                    case "FOG_CANYON": case "MONOMON_ARCHIVE": return pd.mapFogCanyon;
                    case "WASTES": case "QUEENS_STATION": return pd.mapFungalWastes;
                    case "ROYAL_GARDENS": return pd.mapRoyalGardens;
                    case "CITY": case "KINGS_STATION": case "SOUL_SOCIETY": case "LURIENS_TOWER": return pd.mapCity;
                    case "WATERWAYS": case "GODSEEKER_WASTE": return pd.mapWaterways;
                    case "MINES": return pd.mapMines;
                    case "DEEPNEST": case "BEASTS_DEN": return pd.mapDeepnest;
                    case "CLIFFS": return pd.mapCliffs;
                    case "OUTSKIRTS": case "HIVE": case "COLOSSEUM": return pd.mapOutskirts;
                    case "RESTING_GROUNDS": return pd.mapRestingGrounds;
                    case "ABYSS": return pd.mapAbyss;
                    default: return false;
                }
            }
            catch { return false; }
        }

        public static string ResolveMapArea(string z)
        {
            switch (z)
            {
                case "KINGS_STATION": case "SOUL_SOCIETY": case "LURIENS_TOWER": return "CITY";
                case "SHAMAN_TEMPLE": return "CROSSROADS";
                case "BEASTS_DEN": return "DEEPNEST";
                case "MONOMON_ARCHIVE": return "FOG_CANYON";
                case "QUEENS_STATION": return "WASTES";
                case "HIVE": case "COLOSSEUM": return "OUTSKIRTS";
                case "GODSEEKER_WASTE": return "WATERWAYS";
                case "KINGS_PASS": return "TOWN";
                default: return z ?? "";
            }
        }

        static void DisableAllAreas(GameMap m)
        {
            SetOff(m.areaAncientBasin); SetOff(m.areaCity); SetOff(m.areaCliffs); SetOff(m.areaCrossroads);
            SetOff(m.areaCrystalPeak); SetOff(m.areaDeepnest); SetOff(m.areaFogCanyon); SetOff(m.areaFungalWastes);
            SetOff(m.areaGreenpath); SetOff(m.areaKingdomsEdge); SetOff(m.areaQueensGardens);
            SetOff(m.areaRestingGrounds); SetOff(m.areaDirtmouth); SetOff(m.areaWaterways);
        }

        static void SetOff(GameObject go) { if (go != null) go.SetActive(false); }

        void SetupQuickMap(GameMap m)
        {
            var pd = PlayerData.instance;
            var gm = GameManager.UnsafeInstance;
            string z = "";
            try { z = gm.GetCurrentMapZone(); } catch { }
            try
            {
                if (_worldMode)
                {
                    // WorldMap enables owned roots but never disables stale ones.
                    DisableAllAreas(m);
                    m.WorldMap();
                }
                else
                {
                    switch (z)
                    {
                        case "ABYSS": m.QuickMapAncientBasin(); break;
                        case "CITY": case "KINGS_STATION": case "SOUL_SOCIETY": case "LURIENS_TOWER": m.QuickMapCity(); break;
                        case "CLIFFS": m.QuickMapCliffs(); break;
                        case "CROSSROADS": case "SHAMAN_TEMPLE": m.QuickMapCrossroads(); break;
                        case "MINES": m.QuickMapCrystalPeak(); break;
                        case "DEEPNEST": case "BEASTS_DEN": m.QuickMapDeepnest(); break;
                        case "FOG_CANYON": case "MONOMON_ARCHIVE": m.QuickMapFogCanyon(); break;
                        case "WASTES": case "QUEENS_STATION": m.QuickMapFungalWastes(); break;
                        case "GREEN_PATH": m.QuickMapGreenpath(); break;
                        case "OUTSKIRTS": case "HIVE": case "COLOSSEUM": m.QuickMapKingdomsEdge(); break;
                        case "ROYAL_GARDENS": m.QuickMapQueensGardens(); break;
                        case "RESTING_GROUNDS": m.QuickMapRestingGrounds(); break;
                        case "TOWN": case "KINGS_PASS": m.QuickMapDirtmouth(); break;
                        case "WATERWAYS": case "GODSEEKER_WASTE": m.QuickMapWaterways(); break;
                        default: DisableAllAreas(m); break;
                    }
                }
                m.StopPan();

                // Dreamer pins: shown iff the Dreamer pin is owned; a defeated dreamer's child hides.
                if (m.dreamerPins != null)
                {
                    bool guard = false; try { guard = pd != null && pd.hasPinGuardian; } catch { }
                    m.dreamerPins.SetActive(guard);
                    if (guard)
                    {
                        var t = m.dreamerPins.transform;
                        for (int i = 0; i < t.childCount; i++)
                        {
                            var ch = t.GetChild(i);
                            string dn = ch.name;
                            bool dead = (NameHas(dn, "lurien") && pd.lurienDefeated)
                                     || (NameHas(dn, "monomon") && pd.monomonDefeated)
                                     || ((NameHas(dn, "herrah") || NameHas(dn, "hegemol")) && pd.hegemolDefeated);
                            ch.gameObject.SetActive(!dead);
                        }
                    }
                }

                bool hasShade = false, hasGate = false;
                try { hasShade = pd != null && !string.IsNullOrEmpty(pd.shadeScene) && pd.shadeScene != "None"; } catch { }
                // The ability gates the dream gate: a recorded scene alone is not proof.
                try { hasGate = pd != null && pd.hasDreamGate && !string.IsNullOrEmpty(pd.dreamGateScene); } catch { }
                if (m.shadeMarker != null) m.shadeMarker.SetActive(hasShade);
                if (m.dreamGateMarker != null) m.dreamGateMarker.SetActive(hasGate);
                // READ-ONLY placement from the recorded positions. PositionCompass(true)
                // and PositionDreamGateMarker WRITE the save, so they are never called.
                if (hasShade && m.shadeMarker != null)
                {
                    try { m.shadeMarker.transform.localPosition = pd.shadeMapPos; } catch { }
                    try
                    {
                        var srs = m.shadeMarker.GetComponentsInChildren<SpriteRenderer>(true);
                        for (int i = 0; i < srs.Length; i++)
                        {
                            var sr = srs[i];
                            if (sr == null) continue;
                            sr.gameObject.SetActive(true); sr.enabled = true;
                            if (NameHas(sr.gameObject.name, "fader")) continue;
                            var c = sr.color; if (c.a < 0.99f) { c.a = 1f; sr.color = c; }
                        }
                    }
                    catch { }
                }
                if (hasGate && m.dreamGateMarker != null)
                    try { m.dreamGateMarker.transform.localPosition = pd.dreamgateMapPos; } catch { }
                MirrorRoomState(m);
                try { m.PositionCompass(false); } catch { }
                if (m.flamePins != null) m.flamePins.SetActive(pd != null && pd.equippedCharm_40);
                GateMarkers(_clone);
            }
            catch (Exception e) { Debug.LogWarning("[HkMap] quick map: " + e.Message); }
            _lastZone = z;
            _lastScene = gm != null ? gm.sceneName : "";
            _compassPending = true;      // the next settled frame always re-anchors
        }

        /// <summary>
        /// HK's per-pin gating, replayed on the frozen-FSM clone: pin_activation
        /// templates toggle SpriteRenderer.enabled; activate_if_pd_bool toggles
        /// the children's activeSelf (grubs self-hide once rescued).
        /// </summary>
        static void GateMarkers(GameObject clone)
        {
            var pd = PlayerData.instance; if (pd == null || clone == null) return;
            var fsms = clone.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int f = 0; f < fsms.Length; f++)
            {
                var fsm = fsms[f];
                try
                {
                    var v = fsm.FsmVariables;
                    var catV = v.FindFsmString("Pin Type Bool");
                    if (catV != null && !string.IsNullOrEmpty(catV.Value))
                    {
                        bool show = pd.GetBool(catV.Value);
                        var specV = v.FindFsmString("Specific Bool"); string sp = specV != null ? specV.Value : "";
                        if (show && !string.IsNullOrEmpty(sp)) show = pd.GetBool(sp);
                        var compV = v.FindFsmString("Completed Bool");
                        if (show && compV != null && !string.IsNullOrEmpty(compV.Value) && pd.GetBool(compV.Value)) show = false;
                        if (!fsm.gameObject.activeSelf) fsm.gameObject.SetActive(true);
                        var srs = fsm.GetComponentsInChildren<SpriteRenderer>(true);
                        for (int i = 0; i < srs.Length; i++) srs[i].enabled = show;
                        continue;
                    }
                    var pdn = v.FindFsmString("PD Bool Name");
                    if (pdn != null && !string.IsNullOrEmpty(pdn.Value))
                    {
                        var aif = v.FindFsmBool("Active If Bool True");
                        bool want = pd.GetBool(pdn.Value) == ((aif == null) || aif.Value);
                        bool grub = pdn.Value == "hasPinGrub";
                        for (int i = 0; i < fsm.transform.childCount; i++)
                        {
                            var ch = fsm.transform.GetChild(i).gameObject;
                            bool on = want && !(grub && GrubRescued(pd, ch.name));
                            ch.SetActive(on);
                            if (on)
                            {
                                var srs = ch.GetComponentsInChildren<SpriteRenderer>(true);
                                for (int k = 0; k < srs.Length; k++) srs[k].enabled = true;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        static bool GrubRescued(PlayerData pd, string scene)
        {
            try { return pd.scenesGrubRescued != null && pd.scenesGrubRescued.Contains(scene); } catch { return false; }
        }

        static bool NameHas(string s, string sub) => s != null && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;

        static readonly string[] CompassColorProps = { "_Color", "_FaceColor", "_TintColor" };

        void SetCompassAlpha(GameMap m, float a)
        {
            try
            {
                if (m == null || m.compassIcon == null) return;
                if (a == _compassAlphaApplied && ReferenceEquals(_compassRsFor, m.compassIcon)) return;
                _compassAlphaApplied = a;
                _forceFrames = Mathf.Max(_forceFrames, 1);
                if (!ReferenceEquals(_compassRsFor, m.compassIcon))
                {
                    _compassRsFor = m.compassIcon;
                    _compassRs = m.compassIcon.GetComponentsInChildren<Renderer>(true);
                    // The compass is a MeshRenderer: tint its own instanced material, once.
                    for (int i = 0; i < _compassRs.Length; i++)
                    {
                        var r = _compassRs[i];
                        if (r == null || r is SpriteRenderer) continue;
                        var mat = r.material;
                        if (mat != null) _ownedMaterials.Add(mat);
                    }
                }
                for (int i = 0; i < _compassRs.Length; i++)
                {
                    var r = _compassRs[i];
                    if (r == null) continue;
                    var sr = r as SpriteRenderer;
                    if (sr != null) { var c = sr.color; if (c.a != a) { c.a = a; sr.color = c; } continue; }
                    var mat = r.sharedMaterial;      // already the instance made above
                    if (mat == null) continue;
                    for (int p = 0; p < CompassColorProps.Length; p++)
                    {
                        string pn = CompassColorProps[p];
                        if (!mat.HasProperty(pn)) continue;
                        var c = mat.GetColor(pn);
                        if (c.a != a) { c.a = a; mat.SetColor(pn, c); }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Copy the REAL map's resolved room state before a compass re-anchor.
        /// Interiors (stag stations, shops, Ancestral Mound) resolve through the
        /// door the player came in by, which only the real map knows.
        /// </summary>
        void MirrorRoomState(GameMap m)
        {
            if (m == null) return;
            try
            {
                if (_realMap == null)
                {
                    var gm = GameManager.UnsafeInstance;
                    var go = gm != null ? gm.gameMap : null;
                    if (go != null) _realMap = go.GetComponent<GameMap>();
                }
                var r = _realMap;
                if (r == null)
                {
                    var pd = PlayerData.instance;
                    if (pd != null && !string.IsNullOrEmpty(pd.gMap_doorScene))
                    { m.doorScene = pd.gMap_doorScene; m.doorMapZone = pd.gMap_doorMapZone; }
                    return;
                }
                if (r.currentScene != null) { m.currentScene = r.currentScene; m.currentScenePos = r.currentScenePos; }
                m.inRoom = r.inRoom; m.doorScene = r.doorScene; m.doorMapZone = r.doorMapZone;
                m.doorX = r.doorX; m.doorY = r.doorY; m.doorOriginOffsetX = r.doorOriginOffsetX;
                m.doorSceneWidth = r.doorSceneWidth; m.doorSceneHeight = r.doorSceneHeight;
                if (_fiOX != null) _fiOX.SetValue(m, _fiOX.GetValue(r));
                if (_fiOY != null) _fiOY.SetValue(m, _fiOY.GetValue(r));
                if (_fiSW != null) _fiSW.SetValue(m, _fiSW.GetValue(r));
                if (_fiSH != null) _fiSH.SetValue(m, _fiSH.GetValue(r));
            }
            catch { }
        }

        static readonly string[] PinBools = { "hasPinBench", "hasPinStag", "hasPinTram", "hasPinBlackEgg", "hasPinGrub",
                                              "hasPinDreamPlant", "hasPinSpa", "hasPinCocoon", "hasPinShop",
                                              "hasPinGhost", "hasPinGuardian" };

        static int PinStamp()
        {
            int ps = 0;
            try
            {
                var pp = PlayerData.instance;
                if (pp == null) return 0;
                for (int i = 0; i < PinBools.Length; i++) { try { if (pp.GetBool(PinBools[i])) ps |= (1 << i); } catch { } }
                try { if (pp.scenesGrubRescued != null) ps = ps * 31 + pp.scenesGrubRescued.Count; } catch { }
                try { if (pp.equippedCharm_40) ps ^= 0x40000000; } catch { }
                try
                {
                    ps = ps * 31 + (pp.placedMarkers_r != null ? pp.placedMarkers_r.Count : 0);
                    ps = ps * 31 + (pp.placedMarkers_b != null ? pp.placedMarkers_b.Count : 0);
                    ps = ps * 31 + (pp.placedMarkers_y != null ? pp.placedMarkers_y.Count : 0);
                    ps = ps * 31 + (pp.placedMarkers_w != null ? pp.placedMarkers_w.Count : 0);
                }
                catch { }
                try
                {
                    if (pp.lurienDefeated) ps ^= 0x100000;
                    if (pp.monomonDefeated) ps ^= 0x200000;
                    if (pp.hegemolDefeated) ps ^= 0x400000;
                    ps = ps * 31 + (pp.scenesEncounteredCocoon != null ? pp.scenesEncounteredCocoon.Count : 0);
                    ps = ps * 31 + (pp.scenesEncounteredDreamPlant != null ? pp.scenesEncounteredDreamPlant.Count : 0);
                    ps = ps * 31 + (pp.scenesEncounteredDreamPlantC != null ? pp.scenesEncounteredDreamPlantC.Count : 0);
                }
                catch { }
            }
            catch { }
            return ps;
        }

        int ContentStamp()
        {
            try
            {
                var pd = PlayerData.instance; var gm = GameManager.UnsafeInstance;
                if (pd == null || gm == null) return -1;
                int h = 17;
                h = h * 31 + ResolveMapArea(gm.GetCurrentMapZone()).GetHashCode();
                h = h * 31 + (pd.scenesMapped != null ? pd.scenesMapped.Count : 0) + (pd.mapAllRooms ? 1000000 : 0);
                h = h * 31 + (pd.hasQuill ? 1 : 0) + (pd.equippedCharm_2 ? 2 : 0) + (pd.equippedCharm_40 ? 4 : 0) + (HasMapForCurrentZone() ? 8 : 0);
                h = h * 31 + PinStamp();
                h = h * 31 + (_worldMode ? 1 : 0);
                return h;
            }
            catch { return -1; }
        }

        // ── markers (the buyable blue/red/yellow/white pins) ────────────────
        //
        // Written exactly as the game's own MapMarkerMenu.PlaceMarker/RemoveMarker
        // do: a clone-local position into placedMarkers_*, spareMarkers_* moved
        // by one, then GameMap.SetupMapMarkers re-places the native objects.

        public const int MarkerTypes = 4;      // 0 blue, 1 red, 2 yellow, 3 white
        static readonly string[] MarkerSpriteNames = { "map_mark_0000_scarab", "map_mark_0001_pill", "map_mark_0002_chit", "map_mark_0003_shell" };
        static readonly Sprite[] MarkerSprites = new Sprite[MarkerTypes];
        static float _nextMarkerSpriteScan;

        public static bool MarkerUnlocked(int type)
        {
            var pd = PlayerData.instance;
            if (pd == null) return false;
            switch (type)
            {
                case 0: return pd.hasMarker_b;
                case 1: return pd.hasMarker_r;
                case 2: return pd.hasMarker_y;
                case 3: return pd.hasMarker_w;
                default: return false;
            }
        }

        public static bool AnyMarkerUnlocked()
        {
            for (int i = 0; i < MarkerTypes; i++) if (MarkerUnlocked(i)) return true;
            return false;
        }

        public static int MarkerSpare(int type)
        {
            var pd = PlayerData.instance;
            if (pd == null) return 0;
            switch (type)
            {
                case 0: return pd.spareMarkers_b;
                case 1: return pd.spareMarkers_r;
                case 2: return pd.spareMarkers_y;
                case 3: return pd.spareMarkers_w;
                default: return 0;
            }
        }

        static void SetMarkerSpare(PlayerData pd, int type, int value)
        {
            value = Mathf.Max(0, value);
            switch (type)
            {
                case 0: pd.spareMarkers_b = value; break;
                case 1: pd.spareMarkers_r = value; break;
                case 2: pd.spareMarkers_y = value; break;
                case 3: pd.spareMarkers_w = value; break;
            }
        }

        static List<Vector3> MarkerList(PlayerData pd, int type)
        {
            if (pd == null) return null;
            switch (type)
            {
                case 0: return pd.placedMarkers_b;
                case 1: return pd.placedMarkers_r;
                case 2: return pd.placedMarkers_y;
                case 3: return pd.placedMarkers_w;
                default: return null;
            }
        }

        GameObject[] MarkerObjects(int type)
        {
            if (_map == null) return null;
            switch (type)
            {
                case 0: return _map.mapMarkersBlue;
                case 1: return _map.mapMarkersRed;
                case 2: return _map.mapMarkersYellow;
                case 3: return _map.mapMarkersWhite;
                default: return null;
            }
        }

        /// <summary>The game's own pin art for a marker type; null until resident.</summary>
        public Sprite MarkerSprite(int type)
        {
            if (type < 0 || type >= MarkerTypes) return null;
            if (MarkerSprites[type] != null) return MarkerSprites[type];
            // The clone's own native marker objects carry the art.
            var slots = MarkerObjects(type);
            if (slots != null && slots.Length > 0 && slots[0] != null)
            {
                var sr = slots[0].GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null && sr.sprite != null) return MarkerSprites[type] = sr.sprite;
            }
            if (Time.unscaledTime < _nextMarkerSpriteScan) return null;
            _nextMarkerSpriteScan = Time.unscaledTime + 5f;
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int i = 0; i < all.Length; i++)
            {
                var sp = all[i];
                if (sp == null) continue;
                for (int t = 0; t < MarkerTypes; t++)
                    if (MarkerSprites[t] == null && sp.name == MarkerSpriteNames[t]) MarkerSprites[t] = sp;
            }
            return MarkerSprites[type];
        }

        /// <summary>
        /// Remove the placed pin nearest <paramref name="world"/>, or place one of
        /// <paramref name="type"/> there (unless erasing). True if a tap was handled.
        /// </summary>
        public bool PlaceOrRemoveMarker(Vector3 world, int type, bool erase)
        {
            var pd = PlayerData.instance;
            if (pd == null || _clone == null || _map == null || _cam == null) return false;
            float radius = Mathf.Max(0.3f, _cam.orthographicSize * 0.055f);
            float best = radius * radius;
            int removeType = -1, removeIndex = -1;
            for (int t = 0; t < MarkerTypes; t++)
            {
                var list = MarkerList(pd, t);
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    Vector3 mw = _clone.transform.TransformPoint(list[i]);
                    float d = (new Vector2(mw.x, mw.y) - new Vector2(world.x, world.y)).sqrMagnitude;
                    if (d < best) { best = d; removeType = t; removeIndex = i; }
                }
            }
            if (removeType >= 0)
            {
                MarkerList(pd, removeType).RemoveAt(removeIndex);
                SetMarkerSpare(pd, removeType, MarkerSpare(removeType) + 1);
                RefreshMarkers();
                return true;
            }
            if (erase || type < 0 || !MarkerUnlocked(type)) return true;
            var selected = MarkerList(pd, type);
            var slots = MarkerObjects(type);
            if (selected == null || slots == null || MarkerSpare(type) <= 0 || selected.Count >= slots.Length) return true;
            Vector3 local = _clone.transform.InverseTransformPoint(world);
            local.z = 0f;
            selected.Add(local);
            SetMarkerSpare(pd, type, MarkerSpare(type) - 1);
            RefreshMarkers();
            return true;
        }

        /// <summary>The scene of a visible bench pin under <paramref name="world"/>, or null.</summary>
        public string BenchPinAt(Vector3 world)
        {
            if (_clone == null || !ContentReady) return null;
            var fsms = _clone.GetComponentsInChildren<PlayMakerFSM>(false);
            for (int i = 0; i < fsms.Length; i++)
            {
                var fsm = fsms[i];
                var v = fsm.FsmVariables; if (v == null) continue;
                var cat = v.FindFsmString("Pin Type Bool");
                if (cat == null || cat.Value != "hasPinBench") continue;
                var sr = fsm.GetComponentInChildren<SpriteRenderer>();
                if (sr == null || !sr.enabled) continue;
                var b = sr.bounds; b.Expand(new Vector3(0.45f, 0.45f, 10f));
                if (world.x < b.min.x || world.x > b.max.x || world.y < b.min.y || world.y > b.max.y) continue;
                // The pin's parent room object is named after the scene.
                return fsm.transform.parent != null ? fsm.transform.parent.name : null;
            }
            return null;
        }

        // ── bounds ──────────────────────────────────────────────────────────

        static GameObject AreaObjFor(GameMap m, string z)
        {
            switch (z)
            {
                case "ABYSS": return m.areaAncientBasin;
                case "CITY": case "KINGS_STATION": case "SOUL_SOCIETY": case "LURIENS_TOWER": return m.areaCity;
                case "CLIFFS": return m.areaCliffs;
                case "CROSSROADS": case "SHAMAN_TEMPLE": return m.areaCrossroads;
                case "MINES": return m.areaCrystalPeak;
                case "DEEPNEST": case "BEASTS_DEN": return m.areaDeepnest;
                case "FOG_CANYON": case "MONOMON_ARCHIVE": return m.areaFogCanyon;
                case "WASTES": case "QUEENS_STATION": return m.areaFungalWastes;
                case "GREEN_PATH": return m.areaGreenpath;
                case "OUTSKIRTS": case "HIVE": case "COLOSSEUM": return m.areaKingdomsEdge;
                case "ROYAL_GARDENS": return m.areaQueensGardens;
                case "RESTING_GROUNDS": return m.areaRestingGrounds;
                case "TOWN": case "KINGS_PASS": return m.areaDirtmouth;
                case "WATERWAYS": case "GODSEEKER_WASTE": return m.areaWaterways;
                default: return null;
            }
        }

        static bool FitSkip(Transform t, Transform area)
        {
            for (var p = t; p != null && p != area; p = p.parent)
            {
                string n = p.name;
                if (NameHas(n, "pin") || NameHas(n, "marker") || NameHas(n, "shade") || NameHas(n, "compass") ||
                    NameHas(n, "flame") || NameHas(n, "glow")) return true;
            }
            return false;
        }

        static readonly Vector3[] Corners = new Vector3[4];

        static void EncapRect(ref Bounds b, ref bool have, Matrix4x4 mtx, Vector3 mn, Vector3 mx)
        {
            Corners[0] = mtx.MultiplyPoint3x4(new Vector3(mn.x, mn.y, 0f));
            Corners[1] = mtx.MultiplyPoint3x4(new Vector3(mx.x, mn.y, 0f));
            Corners[2] = mtx.MultiplyPoint3x4(new Vector3(mn.x, mx.y, 0f));
            Corners[3] = mtx.MultiplyPoint3x4(new Vector3(mx.x, mx.y, 0f));
            for (int k = 0; k < 4; k++)
            {
                var q = Corners[k]; q.z = 0f;
                if (float.IsNaN(q.x) || float.IsInfinity(q.x) || float.IsNaN(q.y) || float.IsInfinity(q.y)) continue;
                if (!have) { b = new Bounds(q, Vector3.zero); have = true; } else b.Encapsulate(q);
            }
        }

        bool TryWorldBounds(GameMap m, out Bounds b)
        {
            b = default(Bounds); bool have = false;
            GameObject[] areas = {
                m.areaAncientBasin, m.areaCity, m.areaCliffs, m.areaCrossroads,
                m.areaCrystalPeak, m.areaDeepnest, m.areaFogCanyon, m.areaFungalWastes,
                m.areaGreenpath, m.areaKingdomsEdge, m.areaQueensGardens,
                m.areaRestingGrounds, m.areaDirtmouth, m.areaWaterways,
            };
            for (int i = 0; i < areas.Length; i++)
            {
                var area = areas[i];
                if (area == null || !area.activeSelf) continue;
                Bounds ab;
                if (!TryAreaObjectBounds(area, out ab)) continue;
                if (!have) { b = ab; have = true; } else b.Encapsulate(ab);
            }
            return have;
        }

        /// <summary>
        /// The area's whole authored extent in clone-local space -- every room,
        /// revealed or not, plus its active in-map text -- so the frame never
        /// moves while the player explores.
        /// </summary>
        bool TryAreaObjectBounds(GameObject area, out Bounds b)
        {
            b = default(Bounds); bool have = false;
            if (area == null || _clone == null) return false;
            var w2l = _clone.transform.worldToLocalMatrix;
            var areaT = area.transform;
            var srs = area.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < srs.Length; i++)
            {
                var sr = srs[i]; if (sr == null || sr.sprite == null) continue;
                if (FitSkip(sr.transform, areaT)) continue;
                var lb = sr.sprite.bounds; Vector3 mn = lb.min, mx = lb.max;
                if (sr.flipX) { float t = mn.x; mn.x = -mx.x; mx.x = -t; }
                if (sr.flipY) { float t = mn.y; mn.y = -mx.y; mx.y = -t; }
                EncapRect(ref b, ref have, w2l * sr.transform.localToWorldMatrix, mn, mx);
            }
            var texts = area.GetComponentsInChildren<TMProOld.TextMeshPro>(false);
            for (int i = 0; i < texts.Length; i++)
            {
                var tmp = texts[i];
                if (tmp == null || FitSkip(tmp.transform, areaT)) continue;
                var r = tmp.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                try { tmp.ForceMeshUpdate(); } catch { }
                var rb = r.bounds;
                if (float.IsNaN(rb.center.x) || float.IsInfinity(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue;
                EncapRect(ref b, ref have, w2l, rb.min, rb.max);
            }
            return have;
        }

        /// <summary>Fallback for a zone with no single area object: what is drawing now.</summary>
        bool TryRendererBounds(out Vector3 center, out float fitOrtho)
        {
            center = Vector3.zero; fitOrtho = 8f;
            if (_clone == null) return false;
            var rends = _clone.GetComponentsInChildren<Renderer>();
            bool have = false; var b = new Bounds();
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (!r.enabled) continue;
                string nm = r.gameObject.name;
                if (NameHas(nm, "marker") || NameHas(nm, "pin") || NameHas(nm, "shade") || NameHas(nm, "compass") ||
                    NameHas(nm, "flame") || NameHas(nm, "glow")) continue;
                if (r.GetComponent<TMProOld.TextMeshPro>() != null) continue;
                var rb = r.bounds;
                if (float.IsNaN(rb.center.x) || float.IsInfinity(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue;
                if (!have) { b = rb; have = true; } else b.Encapsulate(rb);
            }
            if (!have) return false;
            center = b.center;
            fitOrtho = Mathf.Max(b.extents.y, b.extents.x / Mathf.Max(0.1f, _aspect)) * 1.05f;
            return true;
        }
    }

    /// <summary>Ties the rig, clone and RenderTexture to the page host's lifetime.</summary>
    public sealed class HkMapLifetime : MonoBehaviour
    {
        internal HkMapView View;

        void OnDestroy()
        {
            if (View == null) return;
            try { View.Dispose(); } catch { }
            View = null;
        }
    }
}
#endif
