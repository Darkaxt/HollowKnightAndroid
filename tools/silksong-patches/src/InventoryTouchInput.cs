// InventoryTouchInput — touch control for the in-game inventory / "select" menu.
//
// Unlike the title/options/pause menus (Unity Selectables, made tappable
// elsewhere), the inventory is a custom cursor/directional system with no
// pointer support even on desktop. This overlay bridges touch into it using the
// game's own public APIs, so behaviour stays consistent with the controller:
//
//   * Item panes (Tools / Quests / Journal): TWO-TAP. First tap on an item
//     highlights it (InventoryItemManager.SetSelected -> shows its description),
//     a second tap on the SAME item activates it
//     (InventoryItemManager.SubmitButtonSelected) - mirroring "navigate then
//     submit" on a controller.
//   * Tabs (InventoryPaneListItem): TAP switches to that pane, the same way the
//     game does on a bumper press ("Inventory Control" FSM "MOVE PANE TO").
//   * Map pane (InventoryMapManager): in the wide overview, TAP an area
//     (InventoryItemWideMapZone) to zoom into it; once zoomed, DRAG to pan the
//     map (GameMap.UpdateMapPosition, which the game also uses for stick panning).
//
// Items have a Collider2D (tested via OverlapPoint); tabs and map areas have only
// a SpriteRenderer (tested via renderer bounds). Everything is converted
// screen->world with the inventory's render camera. It only does anything while
// PlayerData.instance.isInventoryOpen, so it never affects normal play.
//
// This is the MAIN screen's touch support and has nothing to do with the second
// panel, beyond having to ignore its touches (see DsTouch).

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public class InventoryTouchInput : MonoBehaviour
{
    const float TAP_MOVE_TOLERANCE = 28f;   // px; beyond this a touch is a drag, not a tap
    const float MAP_DRAG_THRESHOLD = 12f;    // px before a map drag starts

    int _finger = -1;
    Vector2 _downScreen;
    bool _moved;

    // item-pane gesture state
    InventoryItemManager _mgr;
    InventoryItemSelectable _downItem;

    // map-pane gesture state
    bool _isMap;
    GameMap _map;
    Camera _mapCam;
    bool _dragging;
    Vector3 _mapStartLocal;
    Vector3 _mapDownWorld;

    // tab + map-area tap state. These objects have a SpriteRenderer but no Collider2D, so they're
    // hit-tested by renderer bounds rather than HitTestItem's collider overlap.
    InventoryPaneListItem _downTab;
    InventoryItemWideMapZone _downZone;

    bool _wasOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("__InventoryTouchInput__");
        DontDestroyOnLoad(go);
        go.AddComponent<InventoryTouchInput>();
        Debug.Log("[InvTouch] registered");
    }

    void Update()
    {
        bool open = PlayerData.HasInstance && PlayerData.instance.isInventoryOpen;
        if (open != _wasOpen)
        {
            _wasOpen = open;
            Debug.Log("[InvTouch] inventory " + (open ? "OPEN" : "closed"));
            _finger = -1;
        }
        if (!open) return;

        if (Input.touchCount == 0) { _finger = -1; return; }

        if (GameCameras.instance == null) return;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);

            // A touch on the second panel belongs to the second screen's own
            // UI, not to the game's inventory. Without this the bottom screen
            // drives the top one. No-op unless the second screen is live.
            if (DsTouch.IsSecondScreen(t)) continue;

            if (t.phase == TouchPhase.Began)
            {
                if (_finger != -1) continue;
                _finger = t.fingerId;
                _downScreen = t.position;
                _moved = false;
                BeginGesture(t.position);
            }
            else if (t.fingerId != _finger) continue;
            else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
            {
                GestureMove(t.position);
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                GestureEnd(t.position, t.phase == TouchPhase.Ended);
            }
        }
    }

    void BeginGesture(Vector2 screen)
    {
        _mgr = null;
        _isMap = false;
        _downItem = null;
        _map = null;
        _mapCam = null;
        _dragging = false;
        _downTab = null;
        _downZone = null;

        // Tabs sit in a strip above the panes and switch panes from anywhere — check them first.
        _downTab = HitTestTab(screen);
        if (_downTab != null) return;

        _mgr = FindActiveManager();
        _isMap = _mgr is InventoryMapManager;

        if (_isMap)
        {
            _map = FindActiveGameMap();
            if (_map != null)
            {
                _mapCam = PickMapCamera();
                _mapStartLocal = _map.transform.localPosition;
                if (_mapCam != null)
                    _mapDownWorld = _mapCam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
                // Wide overview (not pannable): tap an area to zoom into the detailed map. Once
                // zoomed (CanStartPan), the gesture is a drag-to-pan instead (handled in GestureMove).
                if (!_map.CanStartPan())
                    _downZone = HitTestZone(screen);
            }
        }
        else
        {
            _downItem = HitTestItem(screen, _mgr, log: false);
        }
    }

    void MapDrag(Vector2 screen)
    {
        if ((screen - _downScreen).sqrMagnitude > MAP_DRAG_THRESHOLD * MAP_DRAG_THRESHOLD)
            _dragging = true;
        if (!_dragging || _mapCam == null) return;

        Vector3 nowWorld = _mapCam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        Vector3 delta = nowWorld - _mapDownWorld;          // world units the finger moved
        Vector3 target = _mapStartLocal + delta;           // map content follows the finger
        _map.UpdateMapPosition(new Vector2(target.x, target.y));
    }

    void EndItemTap(Vector2 screen)
    {
        if (_mgr == null) return;
        InventoryItemSelectable up = HitTestItem(screen, _mgr, log: false);
        if (up == null || up != _downItem) return;

        if (_mgr.CurrentSelected != up)
        {
            _mgr.SetSelected(up, null);                    // first tap: highlight + description
            Debug.Log("[InvTouch] highlight -> " + up.name);
        }
        else
        {
            _mgr.SubmitButtonSelected();                   // second tap: activate / equip
            Debug.Log("[InvTouch] submit -> " + up.name);
        }
    }

    // Shared move/end handlers, driven by both the on-screen touch loop and the forwarded
    // second-screen pointer.
    void GestureMove(Vector2 screen)
    {
        if ((screen - _downScreen).sqrMagnitude > TAP_MOVE_TOLERANCE * TAP_MOVE_TOLERANCE)
            _moved = true;
        if (_isMap && _map != null) MapDrag(screen);   // pans only once zoomed (CanStartPan); a no-op otherwise
    }

    void GestureEnd(Vector2 screen, bool ended)
    {
        if (ended && !_moved)                           // a tap (no drag) triggers the button action
        {
            if (_downTab != null) TrySwitchTab(screen);
            else if (_downZone != null) TryZoomZone(screen);
            else if (!_isMap) EndItemTap(screen);
        }
        EndGesture();
    }

    void EndGesture()
    {
        _finger = -1;
        _mgr = null;
        _downItem = null;
        _map = null;
        _mapCam = null;
        _isMap = false;
        _dragging = false;
        _downTab = null;
        _downZone = null;
    }

    // ── Tab buttons (pane switching) ─────────────────────────────────────────
    // Tabs are InventoryPaneListItem (a SpriteRenderer icon, no Collider2D). A tap switches to the
    // tapped tab's pane the same way the game does on a bumper press: set the "Inventory Control"
    // FSM's Target Pane Index and send "MOVE PANE TO" (see InventoryPaneInput.Update).
    static System.Reflection.FieldInfo _fiTabIcon, _fiTabPane;

    InventoryPaneListItem HitTestTab(Vector2 screen)
    {
        var tabs = Object.FindObjectsOfType<InventoryPaneListItem>();
        return tabs.Length == 0 ? null : HitTestSprite(screen, tabs, GetTabIcon);
    }

    void TrySwitchTab(Vector2 screen)
    {
        var up = HitTestTab(screen);
        if (up == null || up != _downTab) return;
        var pane = GetTabPane(up);
        var paneList = Object.FindObjectOfType<InventoryPaneList>();
        if (pane == null || paneList == null) return;
        int idx = paneList.GetPaneIndex(pane);
        if (idx < 0) return;
        var fsm = PlayMakerFSM.FindFsmOnGameObject(paneList.gameObject, "Inventory Control");
        var v = (fsm != null) ? fsm.FsmVariables.FindFsmInt("Target Pane Index") : null;
        if (v == null) return;
        v.Value = idx;
        fsm.SendEvent("MOVE PANE TO");
        Debug.Log("[InvTouch] tab -> pane " + idx);
    }

    static SpriteRenderer GetTabIcon(InventoryPaneListItem it)
    {
        if (_fiTabIcon == null)
            _fiTabIcon = typeof(InventoryPaneListItem).GetField("icon",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var sr = _fiTabIcon != null ? _fiTabIcon.GetValue(it) as SpriteRenderer : null;
        return sr != null ? sr : it.GetComponentInChildren<SpriteRenderer>();
    }

    static InventoryPane GetTabPane(InventoryPaneListItem it)
    {
        if (_fiTabPane == null)
            _fiTabPane = typeof(InventoryPaneListItem).GetField("currentPane",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return _fiTabPane != null ? _fiTabPane.GetValue(it) as InventoryPane : null;
    }

    // ── Map-tab area buttons (zoom into the pannable map) ────────────────────
    // Areas are InventoryItemWideMapZone (a SpriteRenderer, no Collider2D). A tap selects + submits
    // it, which calls InventoryMapManager.ZoomIn into the detailed pannable map.
    InventoryItemWideMapZone HitTestZone(Vector2 screen)
    {
        var zones = Object.FindObjectsOfType<InventoryItemWideMapZone>();
        return zones.Length == 0 ? null : HitTestSprite(screen, zones, z => z.GetComponent<SpriteRenderer>());
    }

    void TryZoomZone(Vector2 screen)
    {
        var up = HitTestZone(screen);
        if (up == null || up != _downZone) return;
        if (_mgr != null) _mgr.SetSelected(up, null);   // keep the game's selection in sync (for zoom-out)
        up.Submit();                                     // -> InventoryMapManager.ZoomIn
        Debug.Log("[InvTouch] map area -> zoom " + up.ZoomToZone);
    }

    // Hit-test sprite-bounds objects (tabs/zones have no Collider2D): convert the touch to world
    // with each candidate camera and test the renderer AABB. Returns the closest hit, or null.
    T HitTestSprite<T>(Vector2 screen, T[] candidates, System.Func<T, SpriteRenderer> getSr) where T : Component
    {
        var cams = Candidates();
        foreach (var cam in cams)
        {
            Vector3 w3 = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            Vector2 wp = new Vector2(w3.x, w3.y);
            T best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                var c = candidates[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var sr = getSr(c);
                if (sr == null || !sr.enabled) continue;
                Bounds b = sr.bounds;
                if (wp.x < b.min.x || wp.x > b.max.x || wp.y < b.min.y || wp.y > b.max.y) continue;
                float d = ((Vector2)b.center - wp).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            if (best != null) return best;
        }
        return null;
    }

    // The manager whose pane is currently active (manager + InventoryPaneBase
    // live on the same GameObject).
    static InventoryItemManager FindActiveManager()
    {
        var all = Object.FindObjectsOfType<InventoryItemManager>();
        InventoryItemManager fallback = null;
        foreach (var m in all)
        {
            if (!m.isActiveAndEnabled) continue;
            var pane = m.GetComponent<InventoryPaneBase>();
            if (pane != null && pane.IsPaneActive) return m;
            fallback = m;
        }
        return fallback;
    }

    static GameMap FindActiveGameMap()
    {
        var maps = Object.FindObjectsOfType<GameMap>();
        foreach (var m in maps)
            if (m.isActiveAndEnabled) return m;
        return maps.Length > 0 ? maps[0] : null;
    }

    // Candidate cameras for screen->world, most-likely first. The inventory
    // UICanvas worldCamera toggles between hudCamera and mainCamera
    // (GameCameras.MoveMenuToHUDCamera / MoveMenuToMainCamera), and items are
    // world objects, so we don't assume which one — we try them in order plus
    // any other enabled camera as a fallback.
    static List<Camera> Candidates()
    {
        var list = new List<Camera>(4);
        var gc = GameCameras.instance;
        if (gc != null)
        {
            if (gc.hudCamera != null && gc.hudCamera.isActiveAndEnabled) list.Add(gc.hudCamera);
            if (gc.mainCamera != null && gc.mainCamera.isActiveAndEnabled && !list.Contains(gc.mainCamera))
                list.Add(gc.mainCamera);
        }
        foreach (var c in Camera.allCameras)
            if (c != null && c.isActiveAndEnabled && !list.Contains(c)) list.Add(c);
        return list;
    }

    static Camera PickMapCamera()
    {
        var gc = GameCameras.instance;
        if (gc != null && gc.hudCamera != null && gc.hudCamera.isActiveAndEnabled) return gc.hudCamera;
        if (gc != null && gc.mainCamera != null && gc.mainCamera.isActiveAndEnabled) return gc.mainCamera;
        var all = Camera.allCameras;
        return all.Length > 0 ? all[0] : null;
    }

    // Topmost item under the finger. Items are world objects with a Collider2D;
    // we convert the touch to a world point with each candidate camera and test
    // item colliders, so the result is independent of which camera renders the
    // inventory. Broadens discovery to ALL active InventoryItemSelectables (not
    // just children of the manager) so hierarchy layout can't hide them.
    InventoryItemSelectable HitTestItem(Vector2 screen, InventoryItemManager mgr, bool log)
    {
        var items = Object.FindObjectsOfType<InventoryItemSelectable>();
        var cams = Candidates();

        InventoryItemSelectable best = null;
        float bestDist = float.MaxValue;
        Camera hitCam = null;

        foreach (var cam in cams)
        {
            Vector3 w3 = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            Vector2 wp = new Vector2(w3.x, w3.y);
            foreach (var it in items)
            {
                if (it == null || !it.isActiveAndEnabled) continue;
                var cols = it.GetComponentsInChildren<Collider2D>(false);
                for (int c = 0; c < cols.Length; c++)
                {
                    if (!cols[c].enabled || !cols[c].OverlapPoint(wp)) continue;
                    float d = ((Vector2)cols[c].bounds.center - wp).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = it; hitCam = cam; }
                    break;
                }
            }
            if (best != null) break;   // first camera that hits wins
        }

        if (log)
        {
            DumpDiag(screen, items, cams, mgr);
            Debug.Log("[InvTouch] hit-test: " + items.Length + " selectable(s), " + cams.Count +
                      " cam(s), result=" + (best != null ? best.name + " via " + hitCam.name : "none"));
        }
        return best;
    }

    // One-shot diagnostics so a miss is debuggable from logcat alone.
    static void DumpDiag(Vector2 screen, InventoryItemSelectable[] items, List<Camera> cams, InventoryItemManager mgr)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("[InvTouch] DIAG screen=").Append(screen).Append(" cams=[");
        foreach (var cam in cams)
        {
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            sb.Append(cam.name).Append("@(").Append(w.x.ToString("F1")).Append(",").Append(w.y.ToString("F1")).Append(") ");
        }
        sb.Append("] ");
        int shown = 0;
        foreach (var it in items)
        {
            if (it == null || !it.isActiveAndEnabled) continue;
            var col = it.GetComponentInChildren<Collider2D>(false);
            if (col == null) continue;
            sb.Append(it.name).Append("@").Append(col.bounds.center.ToString("F1"))
              .Append("/sz").Append(col.bounds.size.ToString("F1")).Append(" ");
            if (++shown >= 4) break;
        }
        Debug.Log(sb.ToString());
    }
}
#endif
