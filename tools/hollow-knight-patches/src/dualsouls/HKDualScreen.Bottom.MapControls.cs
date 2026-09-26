using System;
using System.Collections.Generic;
using UnityEngine;

// [B6] MAP CONTROLS — visible zoom plus native marker placement/removal. This uses Hollow Knight's
// own PlayerData marker lists and spare counts, then asks the cloned GameMap to redraw them. Touch
// remains the lower display's only input authority; controller input continues to belong to gameplay.
public partial class HKDualScreen
{
    sealed class MapActionButton
    {
        public Transform Root;
        public Component Label;
        public Renderer LabelRenderer;
        public SpriteRenderer Plate;
        public Vector3 BaseScale;
        public Bounds Hit;
        public string Text;
        public bool Normalized;
    }

    MapActionButton mapMarkerAction, mapMarkerTypeAction;
    LineRenderer mapZoomTrack;
    SpriteRenderer mapZoomThumb;
    Sprite mapControlPill;

    bool mapMarkerMode;
    int mapMarkerType = -1;
    bool mapZoomHeld;
    float mapZoomGrabY, mapZoomGrabPosition;
    float mapZoomX, mapZoomTopY, mapZoomBottomY, mapZoomHitHalfWidth;
    float mapControlLeftX, mapControlRightX, mapControlTopY, mapControlBottomY;

    static readonly string[] MAP_MARKER_NAMES = { "BLUE", "RED", "YELLOW", "WHITE" };
    static readonly Color[] MAP_MARKER_COLORS = {
        new Color(0.20f, 0.55f, 1f, 1f),
        new Color(0.88f, 0.18f, 0.15f, 1f),
        new Color(1f, 0.78f, 0.12f, 1f),
        Color.white,
    };

    void BuildMapControls(Transform nativeRoot)
    {
        if (frameRoot == null || nativeRoot == null) return;
        if (mapControlPill == null) mapControlPill = MakePillSprite();
        if (mapMarkerAction == null)
            mapMarkerAction = BuildMapActionButton(nativeRoot, "F_MapMarkers", "MARKERS");
        if (mapMarkerTypeAction == null)
            mapMarkerTypeAction = BuildMapActionButton(nativeRoot, "F_MapMarkerType", "BLUE 0");
        if (mapZoomTrack == null)
        {
            var track = new GameObject("F_MapZoomTrack");
            track.transform.SetParent(frameRoot.transform, false);
            track.layer = ATTR_LAYER;
            mapZoomTrack = track.AddComponent<LineRenderer>();
            mapZoomTrack.useWorldSpace = true;
            mapZoomTrack.positionCount = 2;
            mapZoomTrack.numCapVertices = 4;
            mapZoomTrack.startColor = mapZoomTrack.endColor = new Color(1f, 1f, 1f, 0.82f);
            mapZoomTrack.sortingLayerName = "Inventory";
            mapZoomTrack.sortingOrder = 30040;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Sprites/Default-ColorFlash") ??
                         Shader.Find("Unlit/Color") ?? Shader.Find("UI/Default");
            if (shader != null) mapZoomTrack.material = Own(new Material(shader) { color = Color.white });
        }
        if (mapZoomThumb == null)
        {
            var thumb = new GameObject("F_MapZoomThumb");
            thumb.transform.SetParent(frameRoot.transform, false);
            thumb.layer = ATTR_LAYER;
            mapZoomThumb = thumb.AddComponent<SpriteRenderer>();
            mapZoomThumb.sprite = mapControlPill;
            mapZoomThumb.color = Color.white;
            mapZoomThumb.sortingLayerName = "Inventory";
            mapZoomThumb.sortingOrder = 30050;
        }
    }

    MapActionButton BuildMapActionButton(Transform nativeRoot, string name, string text)
    {
        try
        {
            var donor = FindDeep(nativeRoot, "Pane Name");
            if (donor == null) return null;
            var go = Instantiate(donor.gameObject, frameRoot.transform);
            go.name = name;
            SanitizeDetachedTmpClone(go);
            SetLayerRecursive(go.transform, ATTR_LAYER);
            go.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.SetActive(true);
                r.enabled = true;
                r.sortingLayerName = "Inventory";
                r.sortingOrder = 30050;
            }
            Component tmp = null;
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (IsTextMeshProGraphic(c)) { tmp = c; break; }
            if (tmp == null) { Destroy(go); return null; }
            try { tmp.GetType().GetProperty("text")?.SetValue(tmp, text, null); } catch { }
            try { tmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(tmp, null); } catch { }
            NeutralizeDetachedTmpClip(go);

            var plateGo = new GameObject("plate");
            plateGo.transform.SetParent(go.transform, false);
            plateGo.layer = ATTR_LAYER;
            var plate = plateGo.AddComponent<SpriteRenderer>();
            plate.sprite = mapControlPill;
            plate.color = Color.white;
            plate.sortingLayerName = "Inventory";
            plate.sortingOrder = 30045;

            return new MapActionButton {
                Root = go.transform,
                Label = tmp,
                LabelRenderer = (tmp as Component).GetComponent<Renderer>(),
                Plate = plate,
                Text = text,
            };
        }
        catch (Exception e) { WarnOnce("map action build", e); return null; }
    }

    static bool ValidButton(MapActionButton button)
    {
        return button != null && button.Root != null && button.Label != null &&
               button.LabelRenderer != null && button.Plate != null;
    }

    void SetMapAction(MapActionButton button, bool show, string text, Vector3 center,
                      float zf, Color plateColor, Color textColor)
    {
        if (!ValidButton(button)) return;
        if (button.Root.gameObject.activeSelf != show) button.Root.gameObject.SetActive(show);
        if (!show) return;

        if (button.Text != text)
        {
            button.Text = text;
            try { button.Label.GetType().GetProperty("text")?.SetValue(button.Label, text, null); } catch { }
            try { button.Label.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(button.Label, null); } catch { }
            NeutralizeDetachedTmpClip(button.Root.gameObject);
        }
        button.LabelRenderer.enabled = true;
        if (!button.Normalized)
        {
            Bounds first = button.LabelRenderer.bounds;
            if (first.size.y > 0.001f)
            {
                button.Root.localScale *= (0.045f * 2f * frameRefOrtho) / first.size.y;
                button.BaseScale = button.Root.localScale;
                button.Normalized = true;
            }
        }
        if (button.Normalized) button.Root.localScale = button.BaseScale * zf;
        button.Root.position = center;
        Bounds glyph = button.LabelRenderer.bounds;
        if (glyph.size.y > 0.001f)
        {
            button.Root.position += center - glyph.center;
            glyph = button.LabelRenderer.bounds;
        }
        SetTmpColor(button.Label, textColor);
        button.Plate.color = plateColor;
        button.Plate.enabled = true;
        PositionPill(button.Plate, glyph, Mathf.Max(0.12f, glyph.size.y * 0.58f));
        button.Hit = button.Plate.bounds;
        button.Hit.Expand(new Vector3(glyph.size.y * 0.45f, glyph.size.y * 0.45f, 10f));
    }

    static void PositionPill(SpriteRenderer plate, Bounds glyph, float pad)
    {
        if (plate == null || plate.sprite == null) return;
        var t = plate.transform;
        t.position = new Vector3(glyph.center.x, glyph.center.y, glyph.center.z + 0.05f);
        t.rotation = Quaternion.identity;
        Vector3 size = plate.sprite.bounds.size;
        float worldW = Mathf.Max(0.1f, glyph.size.x + pad * 2f);
        float worldH = Mathf.Max(0.1f, glyph.size.y + pad * 1.45f);
        Vector3 lossy = t.lossyScale;
        Vector3 local = t.localScale;
        t.localScale = new Vector3(
            worldW / Mathf.Max(0.001f, size.x) * local.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
            worldH / Mathf.Max(0.001f, size.y) * local.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
            1f);
    }

    void PositionMapControls(float s, float asp, float innerTop, float innerBottom, bool onMap)
    {
        bool showMap = onMap && mapAvailable && mapClone != null && mapGm != null &&
                       mapContentVisible && !mapNeedsSetup;
        if (!showMap) SetMapMarkerMode(false);
        bool haveMarkers = showMap && AnyMarkerUnlocked();
        float zf = (s / Mathf.Max(0.01f, frameRefOrtho)) * cfg.compFrameScale;
        var cam = attrCam != null ? attrCam.transform : null;
        if (cam == null) return;

        float topY = cam.position.y + innerTop * s;
        float bottomY = cam.position.y + innerBottom * s;
        float actionX = cam.position.x + 0.68f * s * asp;
        SetMapAction(mapMarkerAction, haveMarkers,
            mapMarkerMode ? "DONE" : "MARKERS",
            new Vector3(actionX, topY - 0.10f * s, cam.position.z + 3.4f), zf,
            Color.white, new Color(0.08f, 0.08f, 0.1f, 1f));

        EnsureSelectedMarkerType();
        int spare = mapMarkerType >= 0 ? MarkerSpare(mapMarkerType) : 0;
        Color markerColor = mapMarkerType >= 0 ? MAP_MARKER_COLORS[mapMarkerType] : Color.white;
        Color markerText = mapMarkerType == 2 || mapMarkerType == 3
            ? new Color(0.08f, 0.08f, 0.1f, 1f) : Color.white;
        string markerLabel = mapMarkerType >= 0
            ? MAP_MARKER_NAMES[mapMarkerType] + "  " + spare : "NO MARKERS";
        SetMapAction(mapMarkerTypeAction, haveMarkers && mapMarkerMode,
            markerLabel,
            new Vector3(actionX, topY - 0.23f * s, cam.position.z + 3.4f), zf,
            markerColor, markerText);

        bool showSlider = showMap;
        if (mapZoomTrack != null) mapZoomTrack.enabled = showSlider;
        if (mapZoomThumb != null) mapZoomThumb.enabled = showSlider;
        if (!showSlider) { mapZoomHeld = false; return; }

        mapZoomX = cam.position.x + 0.90f * s * asp;
        mapZoomTopY = topY - (mapMarkerMode ? 0.38f : 0.24f) * s;
        mapZoomBottomY = bottomY + 0.12f * s;
        if (mapZoomTopY <= mapZoomBottomY + 0.15f * s)
            mapZoomTopY = mapZoomBottomY + 0.15f * s;
        mapZoomHitHalfWidth = 0.075f * s;
        mapControlLeftX = cam.position.x - s * asp;
        mapControlRightX = cam.position.x + s * asp;
        mapControlTopY = topY;
        mapControlBottomY = bottomY;

        mapZoomTrack.startWidth = mapZoomTrack.endWidth = 0.012f * s;
        mapZoomTrack.SetPosition(0, new Vector3(mapZoomX, mapZoomBottomY, cam.position.z + 3.5f));
        mapZoomTrack.SetPosition(1, new Vector3(mapZoomX, mapZoomTopY, cam.position.z + 3.5f));

        float position = MapZoomPosition(mapUserZoom, Mathf.Max(1.5f, cfg.compMapZoomMax));
        float thumbY = Mathf.Lerp(mapZoomBottomY, mapZoomTopY, position);
        mapZoomThumb.transform.position = new Vector3(mapZoomX, thumbY, cam.position.z + 3.35f);
        Vector3 spriteSize = mapZoomThumb.sprite != null ? mapZoomThumb.sprite.bounds.size : Vector3.one;
        mapZoomThumb.transform.localScale = new Vector3(
            0.12f * s / Mathf.Max(0.001f, spriteSize.x),
            0.035f * s / Mathf.Max(0.001f, spriteSize.y), 1f);
    }

    static float MapZoomPosition(float zoom, float maxZoom)
    {
        if (zoom <= 1f || maxZoom <= 1f) return 0f;
        return Mathf.Clamp01(Mathf.Log(zoom) / Mathf.Log(maxZoom));
    }

    static float MapZoomForPosition(float position, float maxZoom)
    {
        return Mathf.Exp(Mathf.Log(maxZoom) * position);
    }

    bool MapControlTouchTick(int touchCount)
    {
        if (transport == null || attrCam == null || tab.cur != COMP_MAP || !mapAvailable ||
            !mapContentVisible || mapNeedsSetup || mapGm == null)
        {
            mapZoomHeld = false;
            return false;
        }
        if (touchCount != 1)
        {
            bool released = mapZoomHeld && touchCount == 0;
            mapZoomHeld = false;
            if (released)
            {
                lastCleanTapSeq = transport.CleanTapSequence;
                return true;
            }
            return false;
        }

        Vector3 world = TouchToWorld(transport.T0X, transport.T0Y);
        bool over = Mathf.Abs(world.x - mapZoomX) <= mapZoomHitHalfWidth &&
                    world.y >= mapZoomBottomY && world.y <= mapZoomTopY;
        if (!mapZoomHeld && !over) return false;
        if (!mapZoomHeld)
        {
            mapZoomHeld = true;
            mapZoomGrabY = world.y;
            mapZoomGrabPosition = MapZoomPosition(mapUserZoom,
                Mathf.Max(1.5f, cfg.compMapZoomMax));
        }
        float travel = Mathf.Max(0.001f, mapZoomTopY - mapZoomBottomY);
        float position = Mathf.Clamp01(mapZoomGrabPosition + (world.y - mapZoomGrabY) / travel);
        mapUserZoom = MapZoomForPosition(position, Mathf.Max(1.5f, cfg.compMapZoomMax));
        resetAnimT = 1f;
        pinchLastDist = -1f;
        dragLastValid = false;
        return true;
    }

    bool HandleMapControlTap(Vector3 world)
    {
        if (tab.cur != COMP_MAP || !mapAvailable || !mapContentVisible ||
            mapNeedsSetup || mapGm == null) return false;
        if (ValidButton(mapMarkerAction) && mapMarkerAction.Root.gameObject.activeSelf &&
            mapMarkerAction.Hit.Contains(world))
        {
            SetMapMarkerMode(!mapMarkerMode);
            return true;
        }
        if (mapMarkerMode && ValidButton(mapMarkerTypeAction) &&
            mapMarkerTypeAction.Root.gameObject.activeSelf && mapMarkerTypeAction.Hit.Contains(world))
        {
            CycleMarkerType();
            return true;
        }
        if (!mapMarkerMode) return false;
        if (mapResetR != null && mapResetR.enabled)
        {
            Bounds reset = mapResetR.bounds;
            reset.Expand(new Vector3(0.6f, 0.6f, 10f));
            if (reset.Contains(world)) return false;   // the normal RESET action keeps priority
        }
        if (world.x < mapControlLeftX || world.x > mapControlRightX ||
            world.y < mapControlBottomY || world.y > mapControlTopY) return false;
        return PlaceOrRemoveMarker(world);
    }

    void SetMapMarkerMode(bool active)
    {
        mapMarkerMode = active && AnyMarkerUnlocked();
        if (mapMarkerMode) EnsureSelectedMarkerType();
    }

    bool AnyMarkerUnlocked()
    {
        for (int i = 0; i < 4; i++) if (MarkerUnlocked(i)) return true;
        return false;
    }

    void EnsureSelectedMarkerType()
    {
        if (mapMarkerType >= 0 && MarkerUnlocked(mapMarkerType)) return;
        mapMarkerType = -1;
        for (int i = 0; i < 4; i++)
            if (MarkerUnlocked(i)) { mapMarkerType = i; break; }
    }

    void CycleMarkerType()
    {
        EnsureSelectedMarkerType();
        if (mapMarkerType < 0) return;
        for (int step = 1; step <= 4; step++)
        {
            int candidate = (mapMarkerType + step) % 4;
            if (MarkerUnlocked(candidate)) { mapMarkerType = candidate; return; }
        }
    }

    static bool MarkerUnlocked(int type)
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

    static int MarkerSpare(int type)
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

    GameObject[] MarkerObjects(int type)
    {
        if (mapGm == null) return null;
        switch (type)
        {
            case 0: return mapGm.mapMarkersBlue;
            case 1: return mapGm.mapMarkersRed;
            case 2: return mapGm.mapMarkersYellow;
            case 3: return mapGm.mapMarkersWhite;
            default: return null;
        }
    }

    bool PlaceOrRemoveMarker(Vector3 world)
    {
        var pd = PlayerData.instance;
        if (pd == null || mapClone == null || mapGm == null) return false;
        float radius = Mathf.Max(0.3f, attrCam.orthographicSize * 0.055f);
        float best = radius * radius;
        int removeType = -1, removeIndex = -1;
        for (int type = 0; type < 4; type++)
        {
            List<Vector3> list = MarkerList(pd, type);
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 markerWorld = mapClone.transform.TransformPoint(list[i]);
                float distance = (new Vector2(markerWorld.x, markerWorld.y) -
                                  new Vector2(world.x, world.y)).sqrMagnitude;
                if (distance < best) { best = distance; removeType = type; removeIndex = i; }
            }
        }
        if (removeType >= 0)
        {
            List<Vector3> list = MarkerList(pd, removeType);
            list.RemoveAt(removeIndex);
            SetMarkerSpare(pd, removeType, MarkerSpare(removeType) + 1);
            RefreshNativeMarkers();
            return true;
        }

        EnsureSelectedMarkerType();
        if (mapMarkerType < 0 || !MarkerUnlocked(mapMarkerType)) return true;
        List<Vector3> selected = MarkerList(pd, mapMarkerType);
        GameObject[] slots = MarkerObjects(mapMarkerType);
        if (selected == null || slots == null || MarkerSpare(mapMarkerType) <= 0 ||
            selected.Count >= slots.Length) return true;
        Vector3 local = mapClone.transform.InverseTransformPoint(world);
        local.z = 0f;
        selected.Add(local);
        SetMarkerSpare(pd, mapMarkerType, MarkerSpare(mapMarkerType) - 1);
        RefreshNativeMarkers();
        return true;
    }

    void RefreshNativeMarkers()
    {
        try
        {
            mapGm.SetupMapMarkers();
            lastPinStamp = PinStamp();
        }
        catch (Exception e) { WarnOnce("map marker refresh", e); }
    }

    void TeardownMapControls()
    {
        mapMarkerAction = null;
        mapMarkerTypeAction = null;
        mapZoomTrack = null;
        mapZoomThumb = null;
        mapControlPill = null;
        mapZoomHeld = false;
        mapMarkerMode = false;
    }
}
