using System;
using System.Collections.Generic;
using UnityEngine;

// [B5] BOTTOM SCREEN — tap-to-select + control prompt (cross-cutting for Inventory + Charms). Touch bridge poll,
// tab-band vs item hit-test, the selection tuple, HK's own corner-bracket cursor (or the selBox fallback), the
// right-detail fonts, and the [verb][glyph] PRESS/HOLD/TAP control-prompt line drawn as our own overlay.
public partial class HKDualScreen
{
    int lastTapSeq;                   // bottom-panel touch: last seen tap sequence (the tapped tab lives in tab.tap)

    // tap-to-select-item state (Inventory/Charms): the picked item's identity + its transform (for the highlight box)
    // The tapped-item selection (Inventory/Charms): the item transform (for the cursor), its kind (0 inv / 1 charm /
    // 2 spell / 4 equip / 5 geo -> drives the detail populate + control-prompt line), the charm number, the INV key.
    struct Selection { public Transform item; public int kind, charmN; public string invKey; public void Clear() { item = null; kind = -1; charmN = 0; invKey = null; } }
    Selection sel = new Selection { kind = -1 };
    LineRenderer selBox;


    // Control-prompt is drawn with OUR OWN overlay (glyph SpriteRenderer + cloned verb TMP) — we only READ HK's
    // resolved icon sprite; we never let HK's ActionButtonIcon lay out anything visible (it fights us / breaks).
    SpriteRenderer ctrlMyGlyph; Transform ctrlMyVerbT, ctrlSrcIconT; Component ctrlMyVerbTmp;
    bool ctrlGlyphPending;   // ActionButtonIcon hadn't resolved a sprite at populate -> keep mirroring until it does, then freeze
    int ctrlLayoutPending;   // >0: measure + lay out on the NEXT tick (TMP bounds are one frame stale right after ForceMeshUpdate; measuring then caused the visible settle-in). Renderers stay hidden until placed.
    int ctrlLayoutWait;      // frames spent waiting for the glyph sprite to resolve (cap -> lay out verb-only)
    int ctrlVerbKind;        // 0=PRESS 1=HOLD 2=TAP -> its own DY knob (the skin's glyph sprites carry different padding per button, so each verb pair needs its own trim; a GPU ink-measure proved unreliable on this GLES path)
    Renderer ctrlTnR, ctrlTdR; Transform ctrlTdT;   // the pane's Text Name / Text Desc (measured at layout time, not populate)
    Vector3 ctrlGlyphFinal, ctrlVerbFinal; bool ctrlPlaced;   // final absolute positions — steady-state re-pins use these, NO live bounds

    float ctrlGlyphScale = 1f; bool ctrlActive;

    Renderer[] ctrlHkRs; Transform ctrlHkRsFor;   // HK's Item-Control renderers we keep hidden (cached per node)
    Transform paneCursor; GameObject paneCursorFor;   // HK's own selection cursor in the current pane clone

    Transform selCurTL, selCurTR, selCurBL, selCurBR, selCurFor;   // cached cursor corners + the item they were wired for (event-gated setup; per-frame only repositions)

    // [B5] Selected-item highlight box (5-pt rectangle loop, placed around the tapped item in PositionSelection) — the
    // fallback when the pane clone has no HK "Cursor" object. Parented under frameRoot so TeardownFrame frees it.
    void BuildSelBox()
    {
        Shader ush = Shader.Find("Sprites/Default") ?? Shader.Find("Sprites/Default-ColorFlash") ?? Shader.Find("Unlit/Color") ?? Shader.Find("UI/Default");
        if (ush != null)
        {
            var sgo = new GameObject("F_SelBox"); sgo.transform.SetParent(frameRoot.transform, false); sgo.layer = ATTR_LAYER;
            selBox = sgo.AddComponent<LineRenderer>();
            selBox.useWorldSpace = true; selBox.material = Own(new Material(ush) { color = Color.white });
            selBox.startColor = selBox.endColor = new Color(1f, 0.98f, 0.7f, 0.9f);
            selBox.positionCount = 5; selBox.numCapVertices = 1; selBox.loop = true; selBox.alignment = LineAlignment.View; selBox.sortingOrder = 61; selBox.enabled = false;
        }
    }

    // [B5] Selected-item highlight — HK's OWN corner-bracket cursor (Inv_0014_selection_cursor + Glow) moved and
    // scaled onto the tapped item, with the LineRenderer selBox as the fallback when the clone has no Cursor.
    void PositionSelection(float s)
    {
        // selected-item highlight — HK's OWN corner-bracket cursor (Inv_0014_selection_cursor + Glow), moved and
        // scaled onto the tapped item (just like the real menu). A LineRenderer box is the fallback if the clone
        // has no Cursor object.
        bool showSel = cfg.compSelHighlight == 1 && sel.item != null && (tab.cur == COMP_INV || tab.cur == COMP_CHARM);
        Renderer selR = null; Bounds selBB = new Bounds();
        if (showSel)
        {
            try
            {
                selR = sel.item.GetComponent<Renderer>();
                // charms: the backboard's OWN sprite is blank (bounds ~0), so frame the ICON (charmObject) instead
                if (sel.charmN > 0) { var bbc = sel.item.GetComponent("InvCharmBackboard"); if (bbc != null) { var fi = bbc.GetType().GetField("charmObject"); var ico = fi != null ? fi.GetValue(bbc) as GameObject : null; var ir = ico != null ? ico.GetComponentInChildren<Renderer>(true) : null; if (ir != null && ir.bounds.size.x > 0.05f) selR = ir; } }
                if (selR != null) selBB = selR.bounds;
                // Items whose graphic lives on CHILD objects have NO direct renderer -> selBB stays empty -> no fleur.
                // Encapsulate all child renderers so the cursor frames them. Covers the soul vessel (InvVesselFragments
                // self/piece/full), the mask shards (Heart Pieces), and Geo (coin icon + amount number).
                if (selBB.size.x < 0.05f || sel.invKey == "GEO")
                { bool any = false; foreach (var rr in sel.item.GetComponentsInChildren<Renderer>(true)) { if (rr == null || !rr.enabled || rr.bounds.size.x < 0.02f) continue; if (!any) { selBB = rr.bounds; any = true; } else selBB.Encapsulate(rr.bounds); } }
            }
            catch { }
        }
        bool selOk = showSel && selBB.size.x > 0.05f;
        if (paneClone != null)
        {
            if (paneCursorFor != paneClone) { paneCursor = FindDeep(paneClone.transform, "Cursor"); paneCursorFor = paneClone; selCurFor = null; }
            if (paneCursor != null)
            {
                if (selOk)
                {
                    // EVENT: wire the cursor (activate the FSM-disabled corner subtree, re-layer, find TL/TR/BL/BR,
                    // enable renderers) only when the SELECTION changed — not every frame. Repositioning to the
                    // item's bbox stays per-frame (cheap; the fit may still move the item).
                    if (selCurFor != sel.item)
                    {
                        foreach (var t in paneCursor.GetComponentsInChildren<Transform>(true)) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                        SetLayerRecursive(paneCursor, ATTR_LAYER);
                        selCurTL = FindDeep(paneCursor, "TL"); selCurTR = FindDeep(paneCursor, "TR"); selCurBL = FindDeep(paneCursor, "BL"); selCurBR = FindDeep(paneCursor, "BR");
                        foreach (var r in paneCursor.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                        selCurFor = sel.item;
                    }
                    var bb = selBB; var pl = paneCursor.parent != null ? paneCursor.parent.lossyScale : Vector3.one;
                    float z = bb.center.z - 0.2f;
                    // Keep each corner ornament at a UNIFORM native size (no stretch): frame the item by MOVING the
                    // four corners to its bbox corners — how HK's own menu cursor works.
                    float k = Mathf.Max(0.01f, cfg.compSelCursorScale) / Mathf.Max(1e-3f, pl.x);
                    paneCursor.localRotation = Quaternion.identity;
                    paneCursor.position = new Vector3(bb.center.x, bb.center.y, z);
                    paneCursor.localScale = new Vector3(k, k, 1f);
                    float grow = Mathf.Max(0.15f, 1f + cfg.compSelInset);   // negative inset pulls corners INSIDE the item bounds (closer)
                    float hx = Mathf.Max(0.05f, bb.size.x * 0.5f * grow);
                    float hy = Mathf.Max(0.05f, bb.size.y * 0.5f * grow);
                    if (selCurTL != null && selCurTR != null && selCurBL != null && selCurBR != null)
                    {
                        selCurTL.position = new Vector3(bb.center.x - hx, bb.center.y + hy, z);
                        selCurTR.position = new Vector3(bb.center.x + hx, bb.center.y + hy, z);
                        selCurBL.position = new Vector3(bb.center.x - hx, bb.center.y - hy, z);
                        selCurBR.position = new Vector3(bb.center.x + hx, bb.center.y - hy, z);
                    }
                    else   // fallback: no named corners -> at least keep it UNIFORM (still no stretch)
                    {
                        float side = Mathf.Max(bb.size.x, bb.size.y) * (1f + Mathf.Max(0f, cfg.compSelInset));
                        paneCursor.localScale = new Vector3(side / Mathf.Max(1e-3f, pl.x), side / Mathf.Max(1e-3f, pl.y), 1f);
                    }
                }
                else if (paneCursor.gameObject.activeSelf) { paneCursor.gameObject.SetActive(false); selCurFor = null; }
            }
        }
        if (selBox != null)   // fallback box only when no HK cursor exists in the clone
        {
            if (selOk && paneCursor == null)
            {
                var bb = selBB; float ins = cfg.compSelInset;
                float x0 = bb.min.x + ins, x1 = bb.max.x - ins, y0 = bb.min.y + ins, y1 = bb.max.y - ins, z = bb.center.z - 0.15f;
                selBox.enabled = true; selBox.widthMultiplier = 0.03f * s;
                selBox.SetPosition(0, new Vector3(x0, y0, z)); selBox.SetPosition(1, new Vector3(x1, y0, z));
                selBox.SetPosition(2, new Vector3(x1, y1, z)); selBox.SetPosition(3, new Vector3(x0, y1, z)); selBox.SetPosition(4, new Vector3(x0, y0, z));
            }
            else selBox.enabled = false;
        }
    }

    // Poll the bottom-panel touch bridge (HKAux). On a new tap in the tab-row band, select that tab.
    // NOTE: whether Android routes the secondary display's touch to the Presentation is the on-device
    // unknown — the "HKDS tap" log firing on a touch is the proof it works.
    int lastSimTapN;

    // ---- Map pinch-zoom state (dusklight-style; consumed by ApplyCompanionCamera's map branch) ----
    float mapUserZoom = 1f; Vector2 mapUserPan;    // runtime zoom (>=1) + pan offset, world units
    float pinchLastDist = -1f; Vector2 dragLastN; bool dragLastValid; int lastCleanTapSeq;

    float resetAnimT = 1f; float resetStartZoom; Vector2 resetStartPan;   // RESET-button glide (dusklight-style), cancelled by any touch

    void ResetMapView() { mapUserZoom = 1f; mapUserPan = Vector2.zero; pinchLastDist = -1f; dragLastValid = false; resetAnimT = 1f; }   // instant (area change / teardown)

    void ResetMapViewAnimated() { resetStartZoom = mapUserZoom; resetStartPan = mapUserPan; resetAnimT = 0f; pinchLastDist = -1f; dragLastValid = false; }   // the RESET button glides back instead of blipping

    // Convert a bottom-panel normalized touch (top-left origin) to an attrCam world point.
    Vector3 TouchToWorld(float nx, float ny)
    {
        Rect r = attrCam.rect;
        float vx = (nx - r.x) / Mathf.Max(1e-4f, r.width);
        float vy = ((1f - ny) - r.y) / Mathf.Max(1e-4f, r.height);
        return attrCam.ViewportToWorldPoint(new Vector3(Mathf.Clamp01(vx), Mathf.Clamp01(vy), 10f));
    }

    // Per-frame (Map tab only): PINCH = zoom anchored at the pinch midpoint; one-finger DRAG = pan when zoomed;
    // a clean tap on the map area resets the view. Runs off HKAux's live multi-pointer bridge.
    void MapPinchTick()
    {
        if (cfg.compMapPinch != 1 || transport == null || attrCam == null) return;
        try
        {
            int tc = transport.TouchCount;
            if (tc >= 2)
            {
                float ax = transport.T0X, ay = transport.T0Y;
                float bx = transport.T1X, by = transport.T1Y;
                float pasp = (float)BOTTOM_W / BOTTOM_H;
                float dist = Mathf.Sqrt(((bx - ax) * pasp) * ((bx - ax) * pasp) + (by - ay) * (by - ay));
                if (pinchLastDist > 1e-3f && dist > 1e-3f)
                {
                    float oldZoom = mapUserZoom;
                    mapUserZoom = Mathf.Clamp(mapUserZoom * (dist / pinchLastDist), 1f, Mathf.Max(1.5f, cfg.compMapZoomMax));
                    float f = mapUserZoom / oldZoom;
                    if (Mathf.Abs(f - 1f) > 1e-4f)
                    {
                        // keep the world point under the pinch MIDPOINT stationary while zooming
                        Vector3 mid = TouchToWorld((ax + bx) * 0.5f, (ay + by) * 0.5f);
                        Vector3 cp = attrCam.transform.position;
                        mapUserPan += new Vector2((mid.x - cp.x) * (1f - 1f / f), (mid.y - cp.y) * (1f - 1f / f));
                    }
                }
                pinchLastDist = dist; dragLastValid = false;
            }
            else if (tc == 1 && mapUserZoom > 1.01f)
            {
                float nx = transport.T0X, ny = transport.T0Y;
                if (ny < cfg.compTabBandY)   // never pan from the tab band
                {
                    // Pan from SCREEN-space finger deltas converted by the current world-per-viewport factor. The
                    // earlier world-point delta went through the camera THAT THE PAN ITSELF MOVES each frame — a
                    // feedback loop that oscillated ("shaky panning"). Screen deltas are camera-independent.
                    if (dragLastValid)
                    {
                        float o = attrCam.orthographicSize; Rect rr = attrCam.rect;
                        mapUserPan.x -= (nx - dragLastN.x) / Mathf.Max(1e-4f, rr.width) * 2f * o * attrCam.aspect;
                        mapUserPan.y += (ny - dragLastN.y) / Mathf.Max(1e-4f, rr.height) * 2f * o;   // panel Y grows downward
                    }
                    dragLastN = new Vector2(nx, ny); dragLastValid = true;
                }
                else dragLastValid = false;
                pinchLastDist = -1f;
            }
            else { pinchLastDist = -1f; dragLastValid = false; }
            // RESET glide: ease zoom+pan back to default; any new touch takes over immediately
            if (resetAnimT < 1f)
            {
                if (tc >= 1) resetAnimT = 1f;
                else
                {
                    resetAnimT = Mathf.Min(1f, resetAnimT + Time.unscaledDeltaTime / 0.35f);
                    float re = 1f - Mathf.Pow(1f - resetAnimT, 3f);   // easeOutCubic
                    mapUserZoom = Mathf.Lerp(resetStartZoom, 1f, re);
                    mapUserPan = Vector2.Lerp(resetStartPan, Vector2.zero, re);
                    if (resetAnimT >= 1f) { mapUserZoom = 1f; mapUserPan = Vector2.zero; }
                }
            }
            // clean tap ON THE RESET BUTTON only (tap-anywhere reset made drag-panning jumpy — user)
            int cts = transport.CleanTapSequence;
            if (cts != lastCleanTapSeq)
            {
                lastCleanTapSeq = cts;
                bool consumed = false;
                if (mapResetR != null && mapResetR.enabled)
                {
                    float tx = transport.CleanTapX, ty = transport.CleanTapY;
                    Vector3 w = TouchToWorld(tx, ty);
                    var b = mapResetR.bounds; b.Expand(new Vector3(0.6f, 0.6f, 10f));   // generous thumb pad
                    if (w.x >= b.min.x && w.x <= b.max.x && w.y >= b.min.y && w.y <= b.max.y) { ResetMapViewAnimated(); Dbg("HKDS map view reset (button, animated)"); consumed = true; }
                }
                // BENCH TELEPORT [user]: with the tweak on, a clean tap on a bench PIN travels there
                // (recorded benches only, tap-again confirm). Never while the reset pill took the tap.
                if (!consumed && HkStageHooks.TweaksAvailable && HkStageHooks.TweaksMenuVisible)
                {
                    float tx2 = transport.CleanTapX, ty2 = transport.CleanTapY;
                    BenchPinTap(TouchToWorld(tx2, ty2));
                }
            }
        }
        catch (Exception e) { WarnOnce("map pinch", e); }
    }

    void PollTouch()
    {
        // DEBUG sim-tap: fire a synthetic item tap at (compSimTapX, compSimTapY) when compSimTapN changes.
        if (cfg.debug == 1 && cfg.compSimTapN != lastSimTapN)
        {
            lastSimTapN = cfg.compSimTapN;
            if (cfg.compSimTapX >= 0f && (tab.cur == COMP_INV || tab.cur == COMP_CHARM)) { Dbg($"HKDS SIMTAP {cfg.compSimTapX},{cfg.compSimTapY}"); PollItemTap(cfg.compSimTapX, cfg.compSimTapY); }
        }
        if (cfg.compTouch != 1 || transport == null) return;
        try
        {
            int seq = transport.TapSequence;
            if (seq == lastTapSeq) return;
            lastTapSeq = seq;
            float nx = transport.TouchX;
            float ny = transport.TouchY;
            Dbg($"HKDS tap nx={nx:F2} ny={ny:F2}");
            if (nx < 0f) return;
            // B8: the gear (stacked above the fps readout) + the readout itself toggle the pane.
            // Panel-space test — valid in both touch bands and regardless of where attrCam is parked.
            if (GearTapN(nx, ny)) { ToggleTweaksPane(); return; }
            if (ny < cfg.compTabBandY)   // ABOVE the tab row -> an item tap on the Inventory/Charms pane (tab band is only the bottom strip)
            {
                if (tweaksOpen) return;   // B8: pane rows are handled by the clean-tap stream (TweaksTouchTick)
                if (cfg.compTapSelect == 1 && (tab.cur == COMP_INV || tab.cur == COMP_CHARM)) PollItemTap(nx, ny);
                return;
            }
            if (frameTabs.Count == 0 || attrCam == null) return;   // bottom tab-row band -> switch tab
            // B6: hit-test the ACTUAL tab-text bounds instead of splitting the whole width into 3 (nx*3). The FPS
            // (far left) and battery (far right) readouts share this band but sit OUTSIDE the tabs — the old split
            // mapped their taps onto Inventory/Charms. Now only a tap landing on a tab's own X extent switches.
            Rect rc = attrCam.rect;
            float vx = (nx - rc.x) / Mathf.Max(1e-4f, rc.width);
            float vy = ((1f - ny) - rc.y) / Mathf.Max(1e-4f, rc.height);
            Vector3 wp = attrCam.ViewportToWorldPoint(new Vector3(Mathf.Clamp01(vx), Mathf.Clamp01(vy), 10f));
            int hitTab = -1; float bestDx = float.MaxValue;
            foreach (var (tmp2, t2, col2) in frameTabs)
            {
                if (tmp2 == null) continue;
                var rr = (tmp2 as Component).GetComponent<Renderer>(); if (rr == null || !rr.enabled) continue;
                var b = rr.bounds; float padX = b.extents.x * 0.5f + 0.15f;   // small tap tolerance around each tab glyph
                if (wp.x >= b.min.x - padX && wp.x <= b.max.x + padX)
                {
                    float dx = Mathf.Abs(wp.x - b.center.x);
                    if (dx < bestDx) { bestDx = dx; hitTab = TAB_TO_COL[Mathf.Clamp(col2, 0, 2)]; }
                }
            }
            if (hitTab >= 0) { CloseTweaksPane(); tab.tap = hitTab; Dbg($"HKDS tab tapped -> tab={tab.tap}"); }   // a tab tap always leaves the tweaks pane
            else Dbg("HKDS tab-band tap outside tabs (fps/battery) -> ignored");
        }
        catch (Exception e) { Dbg($"HKDS touch err {e.Message}"); }
    }

    // Convert a bottom-screen touch (nx,ny normalized 0..1, top-left) to a world point in attrCam space, hit-test
    // the current pane clone's item renderers, and select the tapped item (fills the right detail + highlight box).
    void PollItemTap(float nx, float ny)
    {
        var pane = paneClone; if (pane == null || attrCam == null) return;
        try
        {
            Rect r = attrCam.rect;
            float vpx = (nx - r.x) / Mathf.Max(1e-4f, r.width);
            float vpy = ((1f - ny) - r.y) / Mathf.Max(1e-4f, r.height);   // flip Y (bottom-left viewport)
            if (vpx < -0.02f || vpx > 1.02f || vpy < -0.02f || vpy > 1.02f) return;   // outside the box
            Vector3 w = attrCam.ViewportToWorldPoint(new Vector3(Mathf.Clamp01(vpx), Mathf.Clamp01(vpy), 10f));
            // Match HK's OWN main-screen selectable items: Nail (InvNailSprite), the spells ('Spell *', which carry no
            // Inv* component), charms, and real collectibles (InvItemDisplay/InvRelicBackboard), PLUS Geo (a counter,
            // tappable by user request). Deliberately NOT the soul-vessel gauge (InvVesselFragments) nor the relic
            // counters (other DisplayItemAmount).
            Transform best = null; float bestArea = float.MaxValue; int bestKind = 0;   // 0=item 1=charm 2=spell
            foreach (var t in pane.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject; if (!go.activeInHierarchy) continue;
                int kind = -1;
                if (go.name.StartsWith("Spell ")) kind = 2;
                else if (go.name == "Geo") kind = 5;   // Geo counter: tappable for its detail (user request) though it carries only DisplayItemAmount
                else if (go.name == "Heart Pieces") kind = 0;   // mask shards: no Inv* component (just SpriteRenderer+BoxCollider) -> match by name so it's tappable (InvItemKey -> HEARTPIECE_<n>)
                else if (go.name == "Godfinder") kind = 6;      // God Tuner (Godhome saves): FSM-only GO, match by name (detail keys live in sheet CP3)
                else
                {
                    foreach (var mb in go.GetComponents<MonoBehaviour>())
                    {
                        if (mb == null) continue; var tn = mb.GetType().Name;
                        if (tn == "InvCharmBackboard")
                        {
                            // only OBTAINED charms are selectable (user: "only selectable is only that obtained")
                            int cn = CharmNumOf(t); var pdc = PlayerData.instance;
                            if (pdc != null && cn > 0 && !CharmGot(pdc, cn)) break;   // un-obtained -> not tappable
                            kind = 1; break;
                        }
                        if (tn == "InvItemDisplay" || tn == "InvNailSprite" || tn == "InvRelicBackboard" || tn == "InvVesselFragments") { kind = 0; break; }   // vessel/fragment now tappable (InvItemKey -> SOULORBS detail)
                    }
                    // Equipment-section items/abilities (Map & Quill, Mantis Claw, keys, Pale Ore…) carry only
                    // PlayMakerFSMs, no Inv* component — match them by their 'Equipment' parent.
                    if (kind < 0 && go.transform.parent != null && go.transform.parent.name == "Equipment") kind = 4;
                }
                if (kind < 0) continue;
                Bounds b;
                if (kind == 1)
                {
                    // charm backboard's OWN sprite is BLANK for obtained charms (the icon is a SIBLING), so ItemBounds
                    // reads ~0 and the tap was skipped. Use the charmObject icon's bounds (padded) so it's tappable.
                    var bbc = go.GetComponent("InvCharmBackboard"); GameObject ico = null;
                    if (bbc != null) { var fi = bbc.GetType().GetField("charmObject"); ico = fi != null ? fi.GetValue(bbc) as GameObject : null; }
                    var ir = ico != null ? ico.GetComponentInChildren<Renderer>(true) : null;
                    b = ir != null ? ir.bounds : new Bounds(t.position, new Vector3(0.7f, 0.7f, 0.2f));
                    b.Expand(new Vector3(0.35f, 0.35f, 0f));   // generous hit region across the slot
                }
                else
                {
                    b = ItemBounds(t);   // encapsulate child sprites (many items render their sprite on a child)
                    if (b.size.x < 0.30f || b.size.y < 0.30f) continue;   // skip tiny placeholders / hidden items
                    if (b.size.x > b.size.y * 4f) continue;   // skip decorative wide RAILS/lines (e.g. the relic-row connector) — not a real item
                }
                if (w.x < b.min.x || w.x > b.max.x || w.y < b.min.y || w.y > b.max.y) continue;   // 2D contains
                float area = b.size.x * b.size.y; if (area < bestArea) { bestArea = area; best = t; bestKind = kind; }   // smallest wins
            }
            if (best == null) return;
            sel.item = best; sel.kind = bestKind;
            if (bestKind == 1) { sel.charmN = CharmNumOf(best); sel.invKey = null; PopulateCharmDetail(pane); }
            else if (bestKind == 2) { sel.charmN = 0; sel.invKey = "SPELL"; PopulateSpellDetail(pane, best.name); }
            else if (bestKind == 4) { sel.charmN = 0; sel.invKey = "EQUIP"; PopulateEquipDetail(pane, best.name); }
            else if (bestKind == 5) { sel.charmN = 0; sel.invKey = "GEO"; PopulateGeoDetail(pane); }
            else if (bestKind == 6) { sel.charmN = 0; sel.invKey = "GODFINDER"; PopulateGodfinderDetail(pane); }
            else { sel.invKey = InvItemKey(best); sel.charmN = 0; PopulateInvDetail(pane); }
            PopulateControlPrompt(pane, bestKind, best.name);   // glyph + PRESS/HOLD/TAP line between name+desc (hidden for items w/o a control)
            Dbg($"HKDS item tap '{best.name}' kind={bestKind} charmN={sel.charmN} invKey='{sel.invKey}'");
        }
        catch (Exception e) { Dbg($"HKDS item-tap err {e.Message}"); }
    }

    // Right-detail text is HK's authored TMP, baked at a tiny fontSize then shown scaled -> soft/small. Re-set an
    // absolute fontSize (compDetailFont>0) and ForceMeshUpdate so TMP regenerates the SDF at that size = crisp+bigger.
    void SetDetailFont(GameObject pane)
    {
        if (cfg.compDetailFont <= 0f) return;
        // "Text Name" (the item name) gets its own larger size; "Text Desc" (the body) uses compDetailFont.
        foreach (var nm in new[] { "Text Name", "Text Desc" })
        {
            float want = (nm == "Text Name" && cfg.compNameFont > 0f) ? cfg.compNameFont : cfg.compDetailFont;
            var t = FindDeep(pane.transform, nm); if (t == null) continue;
            foreach (var c in t.GetComponentsInChildren<Component>(true))
            {
                if (!IsTextMeshProGraphic(c)) continue;
                try
                {
                    if (cfg.debug == 1) { var fs = c.GetType().GetProperty("fontSize")?.GetValue(c); Dbg($"HKDS detailfont '{nm}' orig={fs} -> {want}"); }
                    c.GetType().GetProperty("fontSize")?.SetValue(c, want, null);
                    c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null);
                }
                catch { }
                break;
            }
        }
    }

    // ---- Control-prompt line -----------------------------------------------------------------------------------
    // HK's authored 'Item Control' node holds one icon per hero action (IconSlash/IconDreamnail/IconFocus/IconQuickmap
    // ...), each an ActionButtonIcon (renders the active-device button glyph) + a Press/Hold/Tap verb TMP. We reuse
    // it: enable the one icon that matches the tapped item and park it between Text Name and Text Desc. Items with no
    // in-game control (charms/relics/keys/geo) hide the node — matching HK's own behaviour.

    // Selected item -> Item-Control icon child name. false = no control prompt for this item.
    static bool ItemControlIconName(int kind, string goName, out string icon)
    {
        icon = null; goName = goName ?? "";
        if (kind == 2)   // spell
        {
            string d = goName.StartsWith("Spell ") ? goName.Substring(6) : goName;
            switch (d)
            {
                case "Fireball": icon = "IconFireball"; return true;
                case "Focus":    icon = "IconFocus";    return true;
                case "Quake":    icon = "IconQuake";     return true;
                case "Scream":   icon = "IconScream";    return true;
            }
            return false;
        }
        if (kind == 4)   // equipment section
        {
            switch (goName)
            {
                case "Map and Quill": icon = "IconQuickmap"; return true;
                case "Dash Cloak":    icon = "IconDash";     return true;
                case "Super Dash":    icon = "IconSuperdash";return true;
                case "Double Jump":   icon = "IconJump";     return true;
            }
            return false;
        }
        if (kind == 0)   // inventory item (nail / dream nail / nail arts)
        {
            switch (goName)
            {
                case "Nail":         icon = "IconSlash";       return true;
                case "Dream Nail":   icon = "IconDreamnail";   return true;
                case "Art Dash":     icon = "IconDashSlash";   return true;
                case "Art Uppercut": icon = "IconGreatSlash";  return true;
                case "Art Cyclone":  icon = "IconCycloneSlash";return true;
            }
            return false;
        }
        return false;   // charms + unknown
    }

    // Re-assert the currently-selected item's detail + control prompt (used after the settle-freeze so our text
    // survives the FSMs idling). Dispatches by the stored sel.kind so a spell/equip/geo detail isn't clobbered.
    void RefreshSelectedDetail(GameObject pane)
    {
        if (sel.item == null) return;
        string gn = sel.item.name;
        if (sel.kind == 2) PopulateSpellDetail(pane, gn);
        else if (sel.kind == 4) PopulateEquipDetail(pane, gn);
        else if (sel.kind == 5) PopulateGeoDetail(pane);
        else if (sel.kind == 6) PopulateGodfinderDetail(pane);
        else if (sel.kind == 1) PopulateCharmDetail(pane);
        else PopulateInvDetail(pane);
        PopulateControlPrompt(pane, sel.kind, gn);
    }

    // Verb word for each action icon (its authored Press/Hold/Tap).
    static string ItemControlVerb(string icon)
    {
        switch (icon)
        {
            case "IconSlash": case "IconDash": case "IconJump": return "PRESS";
            case "IconFireball": case "IconScream": return "TAP";
            default: return "HOLD";   // Dreamnail / Focus / Quickmap / Superdash / Quake / nail-arts
        }
    }

    // Build our overlay once: a glyph SpriteRenderer + a verb TMP cloned from "Text Name" (so it has HK's font).
    void EnsureCtrlOverlay(GameObject pane)
    {
        try
        {
            if (ctrlMyGlyph == null && compRoot != null)
            {
                var g = new GameObject("HKCtrlGlyph"); g.transform.SetParent(compRoot, false); SetLayerRecursive(g.transform, ATTR_LAYER);
                ctrlMyGlyph = g.AddComponent<SpriteRenderer>(); ctrlMyGlyph.sortingOrder = 60; ctrlMyGlyph.enabled = false;
            }
            if (ctrlMyVerbT == null && compRoot != null)
            {
                var src = FindDeep(pane.transform, "Text Name"); if (src == null) return;
                var v = Instantiate(src.gameObject, compRoot); v.name = "HKCtrlVerb";
                SanitizeDetachedTmpClone(v);
                SetLayerRecursive(v.transform, ATTR_LAYER); v.SetActive(true);
                foreach (var r in v.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = true; try { r.sortingOrder = 60; } catch { } }
                foreach (var c in v.GetComponentsInChildren<Component>(true)) { if (!IsTextMeshProGraphic(c)) continue; ctrlMyVerbTmp = c; break; }
                ctrlMyVerbT = v.transform;
            }
        }
        catch (Exception e) { Dbg($"HKDS ctrl overlay err {e.Message}"); }
    }

    // Once our glyph holds the resolved sprite, stop HK's ActionButtonIcon from ever re-resolving it (device-change
    // watching caused the per-frame tug-of-war). Idempotent.
    static void FreezeCtrlSource(Transform chosen)
    {
        try { foreach (var mb in chosen.GetComponents<MonoBehaviour>()) if (mb != null && mb.GetType().Name == "ActionButtonIcon") mb.enabled = false; } catch { }
    }

    void HideControlPrompt(GameObject pane)
    {
        ctrlActive = false; ctrlSrcIconT = null; ctrlGlyphPending = false; ctrlLayoutPending = 0; ctrlPlaced = false;
        try { if (ctrlMyGlyph != null) ctrlMyGlyph.enabled = false; } catch { }
        try { if (ctrlMyVerbTmp != null) { var r = (ctrlMyVerbTmp as Component).GetComponent<Renderer>(); if (r != null) r.enabled = false; } } catch { }
        try { var ic = FindDeep(pane.transform, "Item Control"); if (ic != null && ic.gameObject.activeSelf) ic.gameObject.SetActive(false); } catch { }
    }

    // item2: for a detail with NO control prompt (Geo/charm/relic), pull "Text Desc" back up to sit right below
    // the name. The shared Text Desc is pushed DOWN by PopulateControlPrompt for prompt items and never restored,
    // so selecting a prompt-less item afterwards inherited a phantom control-band gap under its name.
    void PullDescUnderName(GameObject pane)
    {
        try
        {
            var tn = FindDeep(pane.transform, "Text Name"); var td = FindDeep(pane.transform, "Text Desc");
            var tnR = tn != null ? tn.GetComponent<Renderer>() : null;
            var tdR = td != null ? td.GetComponent<Renderer>() : null;
            if (tnR == null || !tnR.enabled || td == null || tdR == null || !tdR.enabled) return;
            float topOff = tdR.bounds.max.y - td.position.y;                                  // transform -> rendered-text top
            float wantY = tnR.bounds.min.y - Mathf.Max(0.2f, cfg.compCtrlBand * 0.5f) - topOff; // desc top a small gap below the name (no prompt band)
            td.position = new Vector3(td.position.x, wantY, td.position.z);                    // set directly so it can move UP (restore), not only down
        }
        catch { }
    }

    // Every frame at pre-render: keep HK's own Item-Control renderers OFF and hold our glyph + verb at their
    // PLACED positions. All measuring happens ONCE in LayoutCtrlPrompt, one tick after populate, when the TMP
    // meshes are settled — measuring on the populate frame (right after ForceMeshUpdate) returned one-frame-stale
    // bounds, and chasing live bounds every frame after made the line visibly drift into place over seconds.
    void ReassertControlPrompt()
    {
        if (!ctrlActive || tab.cur != COMP_INV) return;
        try
        {
            if (ctrlSrcIconT != null)
            {
                if (ctrlGlyphPending)   // sprite wasn't resolved at populate -> mirror until it appears, then freeze
                {
                    var srcSR = ctrlSrcIconT.GetComponent<SpriteRenderer>();
                    if (srcSR != null && srcSR.sprite != null && ctrlMyGlyph != null)
                    { ctrlMyGlyph.sprite = srcSR.sprite; FreezeCtrlSource(ctrlSrcIconT); ctrlGlyphPending = false; }
                }
                var ic = ctrlSrcIconT.parent;   // keep HK's own Item-Control render hidden (we draw our own)
                if (ic != null)
                {
                    if (ctrlHkRs == null || ctrlHkRsFor != ic) { ctrlHkRs = ic.GetComponentsInChildren<Renderer>(true); ctrlHkRsFor = ic; }   // PERF: cached per Item-Control node (was a per-frame alloc)
                    foreach (var r in ctrlHkRs) if (r != null && r != ctrlMyGlyph && r.enabled) r.enabled = false;
                }
            }
            if (ctrlLayoutPending > 0)
            {
                // wait for the glyph sprite (usually already there), then lay out ONCE on this settled frame
                if (ctrlGlyphPending && ++ctrlLayoutWait < 60) return;   // renderers stay hidden — no half-placed flash
                ctrlLayoutPending = 0;
                LayoutCtrlPrompt();
                return;
            }
            if (!ctrlPlaced) return;
            // steady state: hold the placed positions (idempotent, no bounds reads)
            if (ctrlMyGlyph != null && ctrlMyGlyph.enabled) ctrlMyGlyph.transform.position = ctrlGlyphFinal;
            if (ctrlMyVerbT != null) ctrlMyVerbT.position = ctrlVerbFinal;
        }
        catch { }
    }

    // One-shot measurement + placement, run one tick after PopulateControlPrompt (TMP meshes settled). Places the
    // [verb][gap][glyph] line centred under the item name, pushes the description below it, caches the final
    // positions, and only THEN shows the renderers — the line appears complete and never adjusts afterwards.
    void LayoutCtrlPrompt()
    {
        try
        {
            // verb metrics from the settled mesh
            float verbHalfW = 0.7f, verbH = 0.6f;
            Renderer vr = ctrlMyVerbT != null ? ctrlMyVerbT.GetComponent<Renderer>() : null;
            if (vr != null) { verbHalfW = Mathf.Max(0.1f, vr.bounds.extents.x); verbH = Mathf.Max(0.1f, vr.bounds.size.y); }
            ctrlGlyphScale = cfg.compCtrlGlyphSize > 0f ? cfg.compCtrlGlyphSize : (verbH * 1.7f);   // FIXED size = consistent per item
            bool haveGlyph = ctrlMyGlyph != null && !ctrlGlyphPending && ctrlMyGlyph.sprite != null;
            float glyphAspect = haveGlyph ? (ctrlMyGlyph.sprite.bounds.size.x / Mathf.Max(0.01f, ctrlMyGlyph.sprite.bounds.size.y)) : 1f;
            float glyphHalfW = haveGlyph ? ctrlGlyphScale * 0.5f * glyphAspect : 0f;

            // line band below the (settled) name; centre the sequence under the name centre
            float lineY, nameCx;
            if (ctrlTnR != null && ctrlTnR.enabled) { nameCx = ctrlTnR.bounds.center.x; lineY = ctrlTnR.bounds.min.y - cfg.compCtrlBand; }
            else if (ctrlSrcIconT != null) { nameCx = ctrlSrcIconT.position.x; lineY = ctrlSrcIconT.position.y; }
            else { ctrlPlaced = false; return; }
            // push the description DOWN to open room for the line (moves only down; PullDescUnderName restores)
            if (ctrlTdT != null && ctrlTdR != null && ctrlTdR.enabled)
            {
                float topOff = ctrlTdR.bounds.max.y - ctrlTdT.position.y;
                float wantY = (lineY - Mathf.Max(0.6f, ctrlGlyphScale * 0.5f) - cfg.compCtrlGap) - topOff;
                if (wantY < ctrlTdT.position.y - 0.01f) ctrlTdT.position = new Vector3(ctrlTdT.position.x, wantY, ctrlTdT.position.z);
            }
            lineY += cfg.compCtrlDY; nameCx += cfg.compCtrlDX;

            float gap = haveGlyph ? Mathf.Abs(cfg.compCtrlVerbGap) : 0f;
            float seqW = 2f * verbHalfW + gap + 2f * glyphHalfW;
            float seqLeft = nameCx - seqW * 0.5f;
            float verbCx = seqLeft + verbHalfW;
            float glyphCx = seqLeft + 2f * verbHalfW + gap + glyphHalfW;
            float verbY = lineY + cfg.compCtrlGlyphDY
                        + (ctrlVerbKind == 0 ? cfg.compCtrlPressDY : ctrlVerbKind == 1 ? cfg.compCtrlHoldDY : cfg.compCtrlTapDY);   // per-verb trim (each glyph sprite's padding differs)

            if (haveGlyph)
            {
                float sh = Mathf.Max(0.01f, ctrlMyGlyph.sprite.bounds.size.y); float sc = ctrlGlyphScale / sh;
                ctrlMyGlyph.transform.localScale = new Vector3(sc, sc, 1f);
                ctrlMyGlyph.transform.position = new Vector3(glyphCx, lineY, ctrlSrcIconT != null ? ctrlSrcIconT.position.z : 0f);
                Vector3 goff = ctrlMyGlyph.bounds.center - ctrlMyGlyph.transform.position;   // sprite pivot/trim correction (deterministic)
                ctrlGlyphFinal = new Vector3(glyphCx - goff.x, lineY - goff.y, ctrlMyGlyph.transform.position.z);
                ctrlMyGlyph.transform.position = ctrlGlyphFinal;
                ctrlMyGlyph.color = Color.white; ctrlMyGlyph.enabled = true;
            }
            else if (ctrlMyGlyph != null) ctrlMyGlyph.enabled = false;
            if (ctrlMyVerbT != null && vr != null)
            {
                Vector3 off = vr.bounds.center - ctrlMyVerbT.position;   // settled-mesh render offset, measured once
                ctrlVerbFinal = new Vector3(verbCx - off.x, verbY - off.y, ctrlMyVerbT.position.z);
                ctrlMyVerbT.position = ctrlVerbFinal;
                vr.enabled = true;
            }
            ctrlPlaced = true;
            if (cfg.debug == 1) Dbg($"HKDS ctrlprompt placed kind={ctrlVerbKind} glyph={haveGlyph} spr='{(haveGlyph ? ctrlMyGlyph.sprite.name : "")}' lineY={lineY:F2} verbY={verbY:F2} glyphY={ctrlGlyphFinal.y:F2}");
        }
        catch (Exception e) { Dbg($"HKDS ctrlprompt layout err {e.Message}"); }
    }

    // Show the control-prompt glyph+verb for the selected item on `pane`. Idempotent (absolute positions/scales),
    // so re-calling on the cached clone after repeated taps doesn't drift.
    void PopulateControlPrompt(GameObject pane, int kind, string goName)
    {
        try
        {
            var ic = FindDeep(pane.transform, "Item Control"); if (ic == null) return;
            string icon;
            if (cfg.compCtrlPrompt != 1 || !ItemControlIconName(kind, goName, out icon)) { HideControlPrompt(pane); PullDescUnderName(pane); return; }   // no control (Geo/charm/relic) -> hide overlay AND pull the description back up under the name (item2: a prior item's prompt push had left a phantom gap)
            // Force HK to treat the last input as a CONTROLLER *before* the icon activates, so ActionButtonIcon's
            // OnEnable paints the pad glyph (Switch/Xbox) immediately instead of the keyboard skin (which otherwise
            // shows until the next physical button press). Then repaint the shared button-skin mappings.
            try { var ih = InputHandler.Instance; if (ih != null) ih.lastActiveController = InControl.BindingSourceType.DeviceBindingSource; } catch { }
            try { var uim = UIManager.instance; if (uim != null && uim.uiButtonSkins != null) uim.uiButtonSkins.RefreshButtonMappings(); } catch { }
            ic.gameObject.SetActive(true);
            Transform chosen = null;
            foreach (Transform child in ic) { bool on = child.name == icon; if (child.gameObject.activeSelf != on) child.gameObject.SetActive(on); if (on) chosen = child; }
            if (chosen == null) { ctrlActive = false; ic.gameObject.SetActive(false); return; }

            // Let HK's ActionButtonIcon RESOLVE the correct device sprite onto the icon's SpriteRenderer (we only read it).
            foreach (var mb in chosen.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "ActionButtonIcon") continue;
                mb.enabled = true;
                foreach (var mn in new[] { "ShowActionButton", "SetControlButton", "RefreshButton", "Refresh" })
                    try { mb.GetType().GetMethod(mn, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(mb, null); } catch { }
            }

            EnsureCtrlOverlay(pane);
            var tn = FindDeep(pane.transform, "Text Name"); var td = FindDeep(pane.transform, "Text Desc");
            string verbTxt = ItemControlVerb(icon);
            ctrlVerbKind = verbTxt == "PRESS" ? 0 : verbTxt == "HOLD" ? 1 : 2;
            ctrlTnR = tn != null ? tn.GetComponent<Renderer>() : null;
            ctrlTdT = td; ctrlTdR = td != null ? td.GetComponent<Renderer>() : null;

            // OUR verb TMP (cloned from Text Name) — set text/font and rebuild the mesh, but measure NOTHING here:
            // TMP bounds are one frame stale right after ForceMeshUpdate (the old populate-time layout visibly
            // settled-in over the next frames). LayoutCtrlPrompt measures + places on the next tick.
            if (ctrlMyVerbTmp != null)
            {
                try { ctrlMyVerbTmp.GetType().GetProperty("text")?.SetValue(ctrlMyVerbTmp, verbTxt, null); } catch { }
                try { if (cfg.compCtrlFont > 0f) ctrlMyVerbTmp.GetType().GetProperty("fontSize")?.SetValue(ctrlMyVerbTmp, cfg.compCtrlFont, null); } catch { }
                try { ctrlMyVerbTmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(ctrlMyVerbTmp, null); } catch { }
                NeutralizeDetachedTmpClip(ctrlMyVerbT.gameObject);
                try { var r = (ctrlMyVerbTmp as Component).GetComponent<Renderer>(); if (r != null) r.enabled = false; } catch { }
            }

            // OUR glyph — capture HK's resolved icon sprite ONCE, then freeze the source (disable ActionButtonIcon)
            // so nothing re-resolves/blips it; HK's own Item-Control renderers are kept OFF. Hidden until placed.
            var chSR = chosen.GetComponent<SpriteRenderer>();
            if (chSR != null && ctrlMyGlyph != null) { ctrlMyGlyph.sprite = chSR.sprite; ctrlMyGlyph.enabled = false; }
            ctrlGlyphPending = chSR == null || chSR.sprite == null;   // not resolved yet -> ReassertControlPrompt mirrors until it is
            if (!ctrlGlyphPending) FreezeCtrlSource(chosen);
            foreach (var r in ic.GetComponentsInChildren<Renderer>(true)) r.enabled = false;   // hide HK's own glyph/verb/label

            ctrlSrcIconT = chosen; ctrlActive = true; ctrlPlaced = false;
            ctrlLayoutPending = 1; ctrlLayoutWait = 0;   // measure + place + show on the next tick (settled TMP bounds)
        }
        catch (Exception e) { Dbg($"HKDS ctrlprompt err {e.Message}"); }
    }
}
