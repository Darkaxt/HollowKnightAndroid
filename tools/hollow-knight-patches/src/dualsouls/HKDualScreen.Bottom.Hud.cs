using System;
using System.Collections.Generic;
using UnityEngine;

// [B3] BOTTOM SCREEN — persistent HUD. HK's own Hud Canvas (masks / soul / geo) is re-layered onto hudLayer and
// mirrored by hudCam2 (FrameHudCams). This file adds the always-on widgets: map-zone name, FPS + battery readout,
// the equipped-charm icon row, and the "Map not acquired yet" label (built once under frameRoot; positioned per frame).
public partial class HKDualScreen
{
    Component areaNameTmp;            // TMP label showing the current map-zone name (top-right, Map tab only)

    Transform areaNameT;

    Component noMapTmp;              // fix4: grey "Map not acquired yet" label (Map tab, no-map zones)

    Transform noMapT;

    bool noMapTextSet; string noMapShownText;   // bench-toast text + one-shot text/color init for the no-map label
    SpriteRenderer benchPillSR; Transform benchPillT;   // fix#2(164-fb): white pill behind the bench toast
    readonly List<SpriteRenderer> notchSRs = new List<SpriteRenderer>();   // fix#3(164-fb): Charms-tab notch row
    Texture2D notchTexLit, notchTexEmpty; int lastNotchTotal = -1, lastNotchUsed = -1;
    Sprite notchSprLitFb, notchSprEmptyFb;   // fix(1.0.0/B6): cached procedural fallback sprites

    Component statsTmp;              // TMP label: "<fps> fps  <batt>%" (top-right corner, all tabs)

    Transform statsT;

    string lastStats = "x";          // last text pushed to the stats label (non-empty sentinel forces first update)

    float fpsYNudge, lvlYNudge; bool fpsYNudgeOk, lvlYNudgeOk;   // cached Y-align corrections as FRACTIONS of the ortho size (fps / battery-%); re-measured only on frames whose text did NOT just change (a fresh ForceMeshUpdate can report one-frame-stale bounds -> the old visible fps dip). Stored /s because a room/zone crossing changes the camera scale on the SAME frame a load hitch changes the fps text — a world-space cache applied at the old scale made the label jump on every room move.

    float fpsAccum; int fpsFrames; int fpsShown = 60;   // rolling FPS, refreshed ~4x/sec

    float battPollT; int battShown = -1;                // battery %, polled ~every 5s (SystemInfo.batteryLevel)

    Transform battIconT; SpriteRenderer battIconSR; Texture2D battIconTex; int battIconLvl = -2;   // procedural battery glyph

    Component battLevelTmp; Transform battLevelT; string lastBattLevel = "z";   // 2nd TMP: battery % (order = fps | icon | level)

    string lastAreaName = "";  // last text pushed to the label (sentinel forces the first update)

    // PERF caches for the per-frame PositionHudStrip: TMP renderers fetched once per frame-build (they were
    // 5-6 GetComponent calls per frame), the raw zone key (ZoneName's language-sheet lookup now runs only when
    // the zone actually changes), and the fps/battery strings (interpolation only when the number changes).
    Renderer areaNameR, statsR, battLevelR, noMapR, tabMidR;
    string lastAreaZoneRaw = "\u0001";
    int fpsStrFor = int.MinValue; string fpsStr = "";
    int battStrFor = int.MinValue; string battStr = "";

    Transform equipRowRoot; readonly List<SpriteRenderer> equipCharmSRs = new List<SpriteRenderer>(); int lastEquipStamp = int.MinValue;   // equipped-charm icon row (top of the box)

    // Map-zone title (e.g. "Forgotten Crossroads") — the name HK flashes at the top when you hold LB.
    // We clone the same "Pane Name" TMP, park it TOP-RIGHT (inline with the HUD), and only show it while
    // the Map tab is active (set text + visibility each frame in PositionFrame).
    void BuildAreaName(Transform root)
    {
        try
        {
            var src = FindDeep(root, "Pane Name");
            if (src == null) return;
            var go = Instantiate(src.gameObject, frameRoot.transform);
            go.name = "F_AreaName";
            SanitizeDetachedTmpClone(go);
            SetLayerRecursive(go.transform, ATTR_LAYER);
            go.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = true; }
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (!IsTextMeshProGraphic(c)) continue;
                areaNameTmp = c;
                try { c.GetType().GetProperty("text")?.SetValue(c, "", null); } catch { }
                break;
            }
            areaNameT = go.transform;
            float s = attrCam.orthographicSize;
            var rr = go.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(); bool hv = false;
            foreach (var r in rr) { var rb = r.bounds; if (float.IsNaN(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue; if (!hv) { b = rb; hv = true; } else b.Encapsulate(rb); }
            float nd = hv ? Mathf.Max(0.001f, b.size.y) : 1f;
            go.transform.localScale *= (0.055f * 2f * s) / nd;   // base size; compAreaNameScale applied LIVE in PositionFrame
            frameBase[go.transform] = go.transform.localScale;
            frameEdge[go.transform] = new Vector3(cfg.compAreaNameX, cfg.compAreaNameY, 4f);
        }
        catch (Exception e) { Dbg($"HKDS area name err {e.Message}"); }
    }

    // FPS + battery readout (top-right of the bottom screen, on every tab). Same TMP-clone approach as
    // BuildAreaName; the text + position are updated LIVE in PositionFrame. Stays always-active (we hide it by
    // pushing empty text, same as the area name — toggling a TMP inactive leaves its mesh un-regenerated).
    void BuildStats(Transform root)
    {
        try
        {
            var src = FindDeep(root, "Pane Name");
            if (src == null) return;
            var go = Instantiate(src.gameObject, frameRoot.transform);
            go.name = "F_Stats";
            SanitizeDetachedTmpClone(go);
            SetLayerRecursive(go.transform, ATTR_LAYER);
            go.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = true; }
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (!IsTextMeshProGraphic(c)) continue;
                statsTmp = c;
                try { c.GetType().GetProperty("text")?.SetValue(c, "", null); } catch { }
                try { var rr2 = c.GetComponent<Renderer>(); if (rr2 != null) rr2.sortingOrder = 52; } catch { }   // fps in FRONT of battery icon (50)
                break;
            }
            statsT = go.transform;
            float s = attrCam.orthographicSize;
            var rr = go.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(); bool hv = false;
            foreach (var r in rr) { var rb = r.bounds; if (float.IsNaN(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue; if (!hv) { b = rb; hv = true; } else b.Encapsulate(rb); }
            float nd = hv ? Mathf.Max(0.001f, b.size.y) : 1f;
            go.transform.localScale *= (0.055f * 2f * s) / nd;   // base size; compStatsScale applied LIVE in PositionFrame
            frameBase[go.transform] = go.transform.localScale;
            try { var fsp = statsTmp.GetType().GetProperty("fontStyle"); if (fsp != null) fsp.SetValue(statsTmp, Enum.ToObject(fsp.PropertyType, 1), null); } catch { }   // Bold
            // Procedural battery glyph (SpriteRenderer on ATTR_LAYER), sits just left of the readout; fill = level.
            battIconTex = Own(new Texture2D(30, 15, TextureFormat.RGBA32, false)); battIconTex.filterMode = FilterMode.Bilinear;
            var bgo = new GameObject("F_BattIcon"); bgo.transform.SetParent(frameRoot.transform, false);
            battIconSR = bgo.AddComponent<SpriteRenderer>(); battIconSR.sortingOrder = 50;
            battIconT = bgo.transform; SetLayerRecursive(battIconT, ATTR_LAYER);
            // second TMP: the battery % (so the readout reads  fps | icon | level  left-to-right).
            var lgo = Instantiate(src.gameObject, frameRoot.transform); lgo.name = "F_BattLevel";
            SanitizeDetachedTmpClone(lgo);
            SetLayerRecursive(lgo.transform, ATTR_LAYER); lgo.SetActive(true);
            foreach (var r in lgo.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = true; }
            foreach (var c in lgo.GetComponentsInChildren<Component>(true)) { if (!IsTextMeshProGraphic(c)) continue; battLevelTmp = c; try { c.GetType().GetProperty("text")?.SetValue(c, "", null); } catch { } try { var rr3 = c.GetComponent<Renderer>(); if (rr3 != null) rr3.sortingOrder = 52; } catch { } break; }   // level % in FRONT of battery icon (50)
            battLevelT = lgo.transform; battLevelT.localScale = go.transform.localScale; frameBase[battLevelT] = battLevelT.localScale;
            try { var fsp2 = battLevelTmp.GetType().GetProperty("fontStyle"); if (fsp2 != null) fsp2.SetValue(battLevelTmp, Enum.ToObject(fsp2.PropertyType, 1), null); } catch { }
        }
        catch (Exception e) { Dbg($"HKDS stats build err {e.Message}"); }
    }

    // ---- Equipped-charms icon row (top of the box, every tab) --------------------------------------------------
    // A pool of SpriteRenderers we assign HK's charm icons to (CharmIconList.GetSprite by charm number). Built once;
    // sprites/layout refreshed live in UpdateEquipCharmRow from PlayerData.equippedCharm_*.
    void BuildEquipCharmRow()
    {
        if (equipRowRoot != null || frameRoot == null) return;
        var go = new GameObject("F_EquipRow"); go.transform.SetParent(frameRoot.transform, false); go.layer = ATTR_LAYER;
        equipRowRoot = go.transform;
        for (int i = 0; i < 11; i++)   // 11 = comfortably above the real max equipped count
        {
            var c = new GameObject("F_EquipCharm" + i); c.transform.SetParent(equipRowRoot, false); c.layer = ATTR_LAYER;
            var sr = c.AddComponent<SpriteRenderer>(); sr.sortingOrder = 55; sr.enabled = false;
            equipCharmSRs.Add(sr);
        }
    }

    int equipRowN;   // icons currently shown in the equipped-charm row (set on change; the layout below uses it every frame)
    void UpdateEquipCharmRow(float s, float asp)
    {
        if (equipRowRoot == null) return;
        if (cfg.compEquipRow != 1 || benchToastUntil > Time.unscaledTime) { foreach (var sr in equipCharmSRs) if (sr != null) sr.enabled = false; if (benchToastUntil > Time.unscaledTime) lastEquipStamp = int.MinValue; return; }   // fix#5(163-fb): clear the top strip while the toast shows
        var cs = Charms();   // frame-cached bitmask (no per-frame string allocs / GetBool)
        int stamp = cs.hash;
        if (stamp != lastEquipStamp)
        {
            lastEquipStamp = stamp;
            var eq = new List<int>(cs.count); for (int i = 1; i <= 40; i++) if (cs.Has(i)) eq.Add(i);   // only on change
            equipRowN = eq.Count;
            CharmIconList cil = null; try { cil = CharmIconList.Instance; } catch { }
            if (cfg.debug == 1) Dbg($"HKDS equiprow n={eq.Count} charms=[{string.Join(",", eq)}] cil={(cil != null ? "ok" : "NULL")}");
            for (int i = 0; i < equipCharmSRs.Count; i++)
            {
                var sr = equipCharmSRs[i]; if (sr == null) continue;
                if (i < eq.Count && cil != null)
                {
                    Sprite sp = null; try { sp = cil.GetSprite(eq[i]); } catch { }
                    sr.sprite = sp; sr.enabled = sp != null; sr.color = Color.white;
                }
                else { sr.sprite = null; sr.enabled = false; }
            }
        }
        int n = equipRowN; if (n == 0) return;
        float targetH = 0.5f * s * Mathf.Max(0.05f, cfg.compEquipRowScale);       // icon world height (∝ ortho => constant apparent size)
        float pitch = targetH * (1f + Mathf.Max(0f, cfg.compEquipRowGap));
        // RIGHT-ANCHOR the row so its RIGHT edge sits at the SAME X as the battery readout (compStatsX), matching
        // the HUD's right margin (user: "right spacing equal battery side"). compEquipRowX is now a fine-nudge on
        // that right edge; compEquipRowY sets the top gap. Icons grow LEFTWARD from the rightmost.
        float cy = cfg.compEquipRowY * s;
        float rightEdge = (cfg.compStatsX + cfg.compEquipRowX) * s * asp;
        var srLast = equipCharmSRs[Mathf.Min(n, equipCharmSRs.Count) - 1];
        float lastAsp = (srLast != null && srLast.sprite != null) ? srLast.sprite.bounds.size.x / Mathf.Max(0.01f, srLast.sprite.bounds.size.y) : 1f;
        float rightIconCx = rightEdge - targetH * lastAsp * 0.5f;                  // rightmost icon centre
        for (int i = 0; i < n && i < equipCharmSRs.Count; i++)
        {
            var sr = equipCharmSRs[i]; if (sr == null || sr.sprite == null) continue;
            float sh = Mathf.Max(0.01f, sr.sprite.bounds.size.y);
            float sc = targetH / sh; sr.transform.localScale = new Vector3(sc, sc, 1f);
            sr.transform.localPosition = new Vector3(rightIconCx - pitch * (n - 1 - i), cy, 4f);
        }
    }

    // Redraw the battery glyph texture for a given level (0..100; -1 = unknown). White outline + terminal nub,
    // inner fill proportional to level and coloured green/amber/red. Cheap: only called when the level changes.
    void DrawBatteryTex(int lvl)
    {
        if (battIconTex == null) return;
        int W = battIconTex.width, H = battIconTex.height;
        var px = new Color32[W * H];
        Color32 clear = new Color32(0, 0, 0, 0), white = new Color32(255, 255, 255, 235);
        for (int i = 0; i < px.Length; i++) px[i] = clear;
        int bx0 = 0, bx1 = W - 5, by0 = 1, by1 = H - 2;                 // body rect (leaves room for the terminal)
        void Set(int x, int y, Color32 col) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = col; }
        for (int x = bx0; x <= bx1; x++) { Set(x, by0, white); Set(x, by1, white); }   // top/bottom
        for (int y = by0; y <= by1; y++) { Set(bx0, y, white); Set(bx1, y, white); }   // left/right
        for (int y = by0 + 2; y <= by1 - 2; y++) { Set(bx1 + 1, y, white); Set(bx1 + 2, y, white); Set(bx1 + 3, y, white); }   // terminal nub
        float f = lvl < 0 ? 0f : Mathf.Clamp01(lvl / 100f);
        int fillMax = bx1 - 2, fillTo = bx0 + 2 + Mathf.RoundToInt((fillMax - (bx0 + 2)) * f);
        Color32 fc = lvl < 0 ? new Color32(160, 160, 160, 235)
                   : lvl <= 15 ? new Color32(230, 70, 60, 235)
                   : lvl <= 35 ? new Color32(235, 190, 60, 235)
                                : new Color32(90, 210, 110, 235);
        for (int x = bx0 + 2; x <= fillTo; x++) for (int y = by0 + 2; y <= by1 - 2; y++) Set(x, y, fc);
        battIconTex.SetPixels32(px); battIconTex.Apply(false);
        if (battIconSR != null && (battIconSR.sprite == null || battIconSR.sprite.texture != battIconTex))
            battIconSR.sprite = Own(Sprite.Create(battIconTex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f));   // once; the texture updates in place (was: a new leaked Sprite per level change)
        battIconLvl = lvl;
    }

    // fix4: subtle grey "Map not acquired yet" label — shown centred in the box when the current zone has no
    // map. Same TMP-clone approach as BuildAreaName; positioned + toggled live in PositionFrame.
    void BuildNoMapLabel(Transform root)
    {
        try
        {
            var src = FindDeep(root, "Pane Name");
            if (src == null) return;
            var go = Instantiate(src.gameObject, frameRoot.transform);
            go.name = "F_NoMap";
            SanitizeDetachedTmpClone(go);
            SetLayerRecursive(go.transform, ATTR_LAYER);
            go.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = true; }
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (!IsTextMeshProGraphic(c)) continue;
                noMapTmp = c;
                try { c.GetType().GetProperty("text")?.SetValue(c, "", null); } catch { }
                break;
            }
            noMapT = go.transform;
            float s = attrCam.orthographicSize;
            var rr = go.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(); bool hv = false;
            foreach (var r in rr) { var rb = r.bounds; if (float.IsNaN(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue; if (!hv) { b = rb; hv = true; } else b.Encapsulate(rb); }
            float nd = hv ? Mathf.Max(0.001f, b.size.y) : 1f;
            go.transform.localScale *= (0.045f * 2f * s) / nd;   // slightly smaller than the area name
            frameBase[go.transform] = go.transform.localScale;
            noMapTextSet = false;
        }
        catch (Exception e) { Dbg($"HKDS nomap label err {e.Message}"); }
    }

    // [B3] Persistent HUD strip widgets (every tab): map-zone title (Map tab), FPS + battery readout, equipped-charm
    // icon row, "Map not acquired yet" label. Layout locals threaded from PositionFrame: s = attrCam ortho, asp =
    // aspect, zf = frame zoom factor, effectiveTab = the tab being shown.
    void PositionHudStrip(float s, float asp, float zf, int effectiveTab)
    {
        // Map-zone title (top-right): the label GameObject stays ALWAYS ACTIVE (like the tabs — toggling a
        // TMP inactive/active left its mesh un-regenerated => invisible). We hide it by pushing EMPTY text
        // off the Map tab, and only re-set text (+ ForceMeshUpdate) when the string actually changes.
        if (areaNameT != null && areaNameTmp != null)
        {
            // area-name position is LIVE here (compAreaNameX/Y hot-reload) — set it directly rather than via
            // the frameEdge dict, which BuildFrame doesn't refresh, so X/Y were stuck until a rebuild.
            // fix5: RIGHT-anchor the label — keep its right edge at compAreaNameX (matches the HUD's right
            // margin) so the gap is constant regardless of zone-name length; text grows leftward. Raise Y
            // (compAreaNameY) to sit slightly higher. Half-width read from the rendered bounds (frameRoot is
            // an identity-scale child of attrCam, so world offsets == local offsets here).
            if (areaNameR == null) { try { areaNameR = (areaNameTmp as Component).GetComponent<Renderer>(); } catch { } }   // PERF: cached (was 2x GetComponent per frame)
            float anHalfW = areaNameR != null ? areaNameR.bounds.extents.x : 0f;
            areaNameT.localPosition = new Vector3(cfg.compAreaNameX * s * asp - anHalfW, cfg.compAreaNameY * s, 4f);
            bool showName = cfg.compAreaName == 1 && effectiveTab != COMP_CHARM;   // fix#3(164-fb): the Charms tab shows the NOTCH row in this corner instead
            // PERF: ZoneName does a language-sheet lookup — resolve only when the RAW zone key changes, not per frame.
            string zRaw = showName ? "" : null;
            if (showName) { try { var g = GameManager.instance; if (g != null) zRaw = g.GetCurrentMapZone(); } catch { } }
            if (zRaw != lastAreaZoneRaw)
            {
                lastAreaZoneRaw = zRaw;
                string want = string.IsNullOrEmpty(zRaw) ? "" : ZoneName(zRaw);
                if (want != lastAreaName)
                {
                    lastAreaName = want;
                    try { areaNameTmp.GetType().GetProperty("text")?.SetValue(areaNameTmp, want, null); } catch { }
                    try { areaNameTmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(areaNameTmp, null); } catch { }
                    NeutralizeDetachedTmpClip(areaNameT.gameObject);
                    SetTmpColor(areaNameTmp, new Color(1f, 1f, 1f, 0.92f));   // constant tint — on text change, not per frame
                }
            }
            // TMP auto-DISABLES its mesh renderer while the text is empty and doesn't re-enable it after a
            // reflection text-set — force it every frame (this was the invisible label: real mesh, vis=False).
            if (areaNameR != null) areaNameR.enabled = lastAreaName.Length > 0 && benchToastUntil <= Time.unscaledTime;   // fix#5(163-fb): give the bench toast the corner
            if (lastAreaName.Length > 0 && frameBase.TryGetValue(areaNameT, out var anbs)) areaNameT.localScale = anbs * zf * Mathf.Max(0.1f, cfg.compAreaNameScale);
        }
        // FPS + battery readout (top-right corner, EVERY tab). Right-anchored like the area name so the right
        // edge stays put regardless of digit count. FPS = rolling average over ~15 frames; battery from
        // SystemInfo.batteryLevel polled ~every 5s (-1 -> omit the %). Empty text hides it (same TMP quirk).
        if (statsT != null && statsTmp != null)
        {
            fpsAccum += Time.unscaledDeltaTime; fpsFrames++;
            if (fpsFrames >= 15) { fpsShown = Mathf.Clamp(Mathf.RoundToInt(fpsFrames / Mathf.Max(1e-4f, fpsAccum)), 0, 999); fpsAccum = 0f; fpsFrames = 0; }
            battPollT -= Time.unscaledDeltaTime;
            if (battShown < 0 || battPollT <= 0f)
            {
                battPollT = 5f;
                float bl = -1f; try { bl = SystemInfo.batteryLevel; } catch { }
                battShown = bl >= 0f ? Mathf.Clamp(Mathf.RoundToInt(bl * 100f), 0, 100) : -1;
            }
            bool showStats = cfg.compStats == 1;
            float scl = zf * Mathf.Max(0.1f, cfg.compStatsScale);
            // FPS text + battery % are TWO TMPs so the readout reads  fps | icon | level  left-to-right.
            if (fpsStrFor != fpsShown) { fpsStrFor = fpsShown; fpsStr = fpsShown + " fps"; }   // PERF: interp only when the number changes
            string fwant = showStats ? fpsStr : "";
            bool fpsTextChanged = fwant != lastStats;
            if (fpsTextChanged)
            {
                lastStats = fwant;
                try { statsTmp.GetType().GetProperty("text")?.SetValue(statsTmp, fwant, null); } catch { }
                try { statsTmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(statsTmp, null); } catch { }
                NeutralizeDetachedTmpClip(statsT.gameObject);
            }
            if (battStrFor != battShown) { battStrFor = battShown; battStr = battShown >= 0 ? battShown + "%" : ""; }
            string lwant = showStats ? battStr : "";
            bool lvlTextChanged = battLevelTmp != null && lwant != lastBattLevel;
            if (lvlTextChanged)
            {
                lastBattLevel = lwant;
                try { battLevelTmp.GetType().GetProperty("text")?.SetValue(battLevelTmp, lwant, null); } catch { }
                try { battLevelTmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(battLevelTmp, null); } catch { }
                NeutralizeDetachedTmpClip(battLevelT.gameObject);
            }
            if (frameBase.TryGetValue(statsT, out var stbs)) statsT.localScale = stbs * scl;
            if (battLevelT != null && frameBase.TryGetValue(battLevelT, out var lbs)) battLevelT.localScale = lbs * scl;
            if (statsR == null) { try { statsR = (statsTmp as Component).GetComponent<Renderer>(); } catch { } }         // PERF: cached
            if (battLevelR == null && battLevelTmp != null) { try { battLevelR = (battLevelTmp as Component).GetComponent<Renderer>(); } catch { } }
            Renderer str = statsR, lr = battLevelR;
            if (str != null) { str.enabled = fwant.Length > 0; if (fpsTextChanged) SetTmpColor(statsTmp, new Color(1f, 1f, 1f, 0.96f)); }   // constant tint — re-set on text change only
            if (lr != null) { lr.enabled = lwant.Length > 0; if (lvlTextChanged) SetTmpColor(battLevelTmp, new Color(1f, 1f, 1f, 0.96f)); }
            // Bottom row, vertically CENTRED on the tab labels. The tab TMPs render offset from their transform and
            // at a bigger scale (compTabScale) than the stats, so matching transform-Y doesn't line up — instead we
            // read the active tab's RENDERED centre Y and align the FPS/battery rendered centres to it.
            Vector3 fr = frameRoot != null ? frameRoot.transform.position : Vector3.zero;
            float yRow = cfg.compTabY * s;
            // Align to a STABLE tab (the Map tab, col 1 — always present + centred), NOT the active one: the active
            // tab changes per switch and (with its fleurs) gave a shifting reference, so FPS/battery jumped around.
            if (tabMidR == null || !tabMidR.enabled)   // PERF: cached reference tab renderer (per-frame GetComponent otherwise)
            {
                tabMidR = null;
                foreach (var (tmp2, t2, col2) in frameTabs)
                {
                    if (tmp2 == null || col2 != 1) continue;
                    try { var tr2 = (tmp2 as Component).GetComponent<Renderer>(); if (tr2 != null && tr2.enabled) tabMidR = tr2; } catch { }
                    break;
                }
                if (tabMidR == null)   // fallback: first tab with a live renderer
                    foreach (var (tmp2, t2, col2) in frameTabs)
                    { if (tmp2 == null) continue; try { var tr2 = (tmp2 as Component).GetComponent<Renderer>(); if (tr2 != null && tr2.enabled) { tabMidR = tr2; break; } } catch { } }
            }
            float tabMidW = tabMidR != null && tabMidR.enabled ? tabMidR.bounds.center.y : float.NaN;
            // Battery LEVEL first (right-anchored; centred on the tab text row) — the FPS aligns to IT below.
            float grpRight = cfg.compStatsX * s * asp;
            bool hasLvl = lr != null && lwant.Length > 0;
            float lHalfW = hasLvl ? lr.bounds.extents.x : 0f;
            if (battLevelT != null)
            {
                battLevelT.localPosition = new Vector3(grpRight - lHalfW, yRow, 4f);
                if (!float.IsNaN(tabMidW) && lr != null && hasLvl)
                {
                    if (!lvlTextChanged || !lvlYNudgeOk) { lvlYNudge = (tabMidW - lr.bounds.center.y) / s; lvlYNudgeOk = !lvlTextChanged; }   // stable-frame rule: fresh ForceMeshUpdate bounds are one frame stale
                    battLevelT.localPosition += new Vector3(0f, lvlYNudge * s, 0f);   // fraction * current s -> scale-proof on zone crossings
                }
            }
            // FPS: left-anchored; TOP-aligned with the battery % — "fps" has a descender ('p'), so centring its
            // ink bounds sat it visibly LOWER than "87%"; matching the top edges puts both on the same digit line.
            float fHalfW = str != null && fwant.Length > 0 ? str.bounds.extents.x : 0f;
            statsT.localPosition = new Vector3(cfg.compFpsX * s * asp + fHalfW, yRow, 4f);
            if (str != null && fwant.Length > 0)
            {
                bool alignToLvl = hasLvl && battLevelT != null;
                if ((!fpsTextChanged && !lvlTextChanged) || !fpsYNudgeOk)
                {
                    fpsYNudge = alignToLvl ? (lr.bounds.max.y - str.bounds.max.y) / s
                                           : (float.IsNaN(tabMidW) ? 0f : (tabMidW - str.bounds.center.y) / s);
                    fpsYNudgeOk = !fpsTextChanged && !lvlTextChanged;
                }
                statsT.localPosition += new Vector3(0f, fpsYNudge * s, 0f);
            }
            Renderer refR = hasLvl ? lr : str;
            float cyW = !float.IsNaN(tabMidW) ? tabMidW : (refR != null ? refR.bounds.center.y : fr.y + yRow), czW = 4f;
            // B8: the Tweaks gear anchors to the fps text (leftmost readout element) — its live bounds
            // are the only reliable row alignment (raw compTabY*s sits below the visible edge).
            if (str != null && str.enabled)
            {
                var gearB = str.bounds;
                // fix(180) [user: "gear now not horizontally centered with fps"]: CENTRE it again, but
                // on a STABLE centre. The label is left-anchored at compFpsX (min.x fixed) while its
                // width breathes with the digit count ("119 fps" vs "60 fps"), so live centre.x made
                // the gear drift (the v176 report). Centre on min.x + the WIDEST half-width seen so
                // far: it converges within a second of gameplay and then never moves again.
                hudFpsHalfMax = Mathf.Max(hudFpsHalfMax, gearB.extents.x);
                hudGearAnchor = new Vector3(gearB.min.x + hudFpsHalfMax, cyW, 4f);
                hudFpsB = gearB;                                        // tapping the readout itself also opens the pane
                hudGearH = Mathf.Max(0.05f, gearB.size.y); hudGearOk = true;
            }
            float cH = refR != null ? Mathf.Max(0.05f, refR.bounds.size.y) : 0.5f, gapIL = cH * Mathf.Max(0f, cfg.compBattGap);
            float levelLeft = grpRight - 2f * lHalfW;
            if (battIconSR != null)
            {
                if (battIconLvl != battShown) DrawBatteryTex(battShown);
                battIconSR.enabled = showStats;
                if (showStats)
                {
                    // Rotate the battery glyph 90° (upright/vertical, terminal nub on top). The sprite is 0.30w×0.15h;
                    // rotated, its displayed HEIGHT = 0.30·sc and WIDTH = 0.15·sc, so scale off 0.30 to keep height≈cH·scale.
                    float sc = Mathf.Max(0.01f, (cH * Mathf.Max(0.1f, cfg.compBattScale)) / 0.30f); battIconT.localScale = new Vector3(sc, sc, 1f);
                    battIconT.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    float iconW = 0.15f * sc;   // rotated horizontal footprint = the sprite's original height
                    battIconT.position = new Vector3(fr.x + (levelLeft - gapIL - iconW * 0.5f), cyW + cfg.compBattOffY * cH, czW);   // Y = tab text centre + tunable nudge
                }
            }
        }
        UpdateNotchRow(s, asp, effectiveTab);      // fix#3(164-fb): Charms tab: notch icons where the area name sits
        PositionGear(s, asp, cfg.compTabY * s);   // B8: tweaks gear in the corner left of the fps readout
        UpdateEquipCharmRow(s, asp);   // equipped-charm icon row across the top (every tab)
        // fix4: grey "Map not acquired yet" label — centred in the box, shown only on the Map tab when the
        // zone has no map (mutually exclusive with the area name, which requires mapAvailable).
        if (noMapT != null && noMapTmp != null)
        {
            bool showNoMap = cfg.compNoMapMsg == 1 && effectiveTab == COMP_MAP && !mapAvailable && !creditNow
                             && !tweaksOpen;   // fix(172): the Mod pane covers the box — no stray label behind it [user]
            // Bench-teleport toast borrows this label (only shows on an AVAILABLE map, so no clash).
            bool toast = benchToastUntil > Time.unscaledTime && effectiveTab == COMP_MAP && !creditNow;
            // fix#2(164-fb): toast = BLACK text on a WHITE PILL (reset-button style) centred on the
            // TOP strip row (where the area name / charm row live) — was overlapping the top fleur.
            if (toast)
            {
                // fix(171): RIGHT-ANCHOR the toast to the same margin as the area name / charm row
                // [user: "should be in top right area of current equipped charms and area name"] —
                // it was centred on the frame. Same formula as the area name (compAreaNameX is the
                // right edge; subtract the rendered half-width so the text grows leftward), and
                // frameRoot is identity-scaled under attrCam so local == world offsets here.
                if (noMapR == null) { try { noMapR = (noMapTmp as Component).GetComponent<Renderer>(); } catch { } }
                float toastHalfW = noMapR != null ? noMapR.bounds.extents.x : 0f;
                float toastY = areaNameT != null ? areaNameT.localPosition.y : cfg.compAreaNameY * s;
                noMapT.localPosition = new Vector3(cfg.compAreaNameX * s * asp - toastHalfW, toastY, 4f);
            }
            else noMapT.localPosition = new Vector3(0f, cfg.compNoMapY * s, 4f);
            if (toast)
            {
                if (noMapShownText != benchToastText)
                {
                    noMapShownText = benchToastText;
                    try { noMapTmp.GetType().GetProperty("text")?.SetValue(noMapTmp, benchToastText, null); } catch { }
                    try { noMapTmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(noMapTmp, null); } catch { }
                    NeutralizeDetachedTmpClip(noMapT.gameObject);
                    try { noMapTmp.GetType().GetProperty("color")?.SetValue(noMapTmp, new Color(0.08f, 0.08f, 0.08f, 1f), null); } catch { }   // BLACK on the white pill
                    try { if (noMapR != null) noMapR.sortingOrder = 30052; } catch { }
                }
                // white pill sized to the text, just underneath it
                if (benchPillSR == null)
                {
                    var pgo = new GameObject("F_BenchToastPill");
                    pgo.transform.SetParent(frameRoot.transform, false);
                    pgo.layer = ATTR_LAYER;
                    benchPillSR = pgo.AddComponent<SpriteRenderer>();
                    benchPillSR.sprite = MakePillSprite();
                    benchPillSR.color = Color.white;
                    benchPillSR.sortingLayerName = "Inventory"; benchPillSR.sortingOrder = 30046;
                    benchPillT = pgo.transform;
                }
                try
                {
                    if (noMapR != null)
                    {
                        var tb = noMapR.bounds;
                        benchPillT.position = new Vector3(tb.center.x, tb.center.y, noMapT.position.z + 0.5f);
                        var pb = benchPillSR.bounds; var cs2 = benchPillT.localScale;
                        if (pb.size.x > 1e-4f && pb.size.y > 1e-4f)
                            benchPillT.localScale = new Vector3(cs2.x * (tb.size.x * 1.18f) / pb.size.x,
                                                                cs2.y * (tb.size.y * 1.85f) / pb.size.y, 1f);
                        benchPillSR.enabled = true;
                    }
                }
                catch { }
                noMapTextSet = false;   // restore the standard text after the toast
            }
            else if (!noMapTextSet)
            {
                noMapTextSet = true; noMapShownText = null;
                try { noMapTmp.GetType().GetProperty("text")?.SetValue(noMapTmp, "Map not acquired yet", null); } catch { }
                try { noMapTmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(noMapTmp, null); } catch { }
                NeutralizeDetachedTmpClip(noMapT.gameObject);
                try { noMapTmp.GetType().GetProperty("color")?.SetValue(noMapTmp, new Color(0.72f, 0.72f, 0.72f, 0.55f), null); } catch { }
            }
            if (noMapR == null) { try { noMapR = (noMapTmp as Component).GetComponent<Renderer>(); } catch { } }   // PERF: cached
            if (noMapR != null) noMapR.enabled = showNoMap || toast;
            if (!toast && benchPillSR != null && benchPillSR.enabled) benchPillSR.enabled = false;
            if (showNoMap && frameBase.TryGetValue(noMapT, out var nmbs)) noMapT.localScale = nmbs * zf * Mathf.Max(0.1f, cfg.compNoMapScale);
        }
    }

    // fix#3(164-fb): Charms tab corner widget — HK-style notch row, icon only: one dot per notch
    // (pd.charmSlots), LIT while consumed (pd.charmSlotsFilled), dim outline when free.
    Texture2D MakeNotchTex(int n, bool filled)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f, r = n * 0.42f, ring = n * 0.10f;
        Color32 white = new Color32(255, 255, 255, 255), clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                bool on = filled ? d <= r : Mathf.Abs(d - r) <= ring;
                px[y * n + x] = on ? white : clear;
            }
        t.SetPixels32(px); t.Apply(false);
        return t;
    }

    // The Charms inventory page carries the real notch art: "Notches/Charm Cost N/{Sprite Full,
    // Sprite Empty}". Grab those two sprites ONCE (inactive objects included — the page is closed
    // most of the time), then reuse them; skins repaint their atlas so they stay in style.
    Sprite notchSprFull, notchSprEmpty; int notchScanT;
    void FindNotchSprites()
    {
        if (notchSprFull != null && notchSprEmpty != null) return;
        if (notchScanT++ % 120 != 0) return;   // cheap retry: the page may not be loaded yet
        try
        {
            var all = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
            for (int i = 0; i < all.Length; i++)
            {
                var sr = all[i]; if (sr == null || sr.sprite == null) continue;
                var pt = sr.transform.parent; if (pt == null) continue;
                if (!pt.name.StartsWith("Charm Cost")) continue;
                if (sr.name == "Sprite Full" && notchSprFull == null) notchSprFull = sr.sprite;
                else if (sr.name == "Sprite Empty" && notchSprEmpty == null) notchSprEmpty = sr.sprite;
                if (notchSprFull != null && notchSprEmpty != null) break;
            }
            if (notchSprFull != null && notchSprEmpty != null) Dbg("HKDS notch art from Charm Cost sprites");
        }
        catch { }
    }

    float hudFpsHalfMax;   // fix(180): converging half-width so the gear centre stops drifting

    void UpdateNotchRow(float s, float asp, int effectiveTab)
    {
        bool show = effectiveTab == COMP_CHARM && frameRoot != null;
        if (!show)
        {
            foreach (var sr in notchSRs) if (sr != null && sr.enabled) sr.enabled = false;
            return;
        }
        var pdn = PlayerData.instance; if (pdn == null) return;
        int total = Mathf.Clamp(pdn.charmSlots, 0, 11), used = Mathf.Clamp(pdn.charmSlotsFilled, 0, total);
        if (notchTexLit == null) { notchTexLit = Own(MakeNotchTex(28, true)); notchTexEmpty = Own(MakeNotchTex(28, false)); }
        while (notchSRs.Count < total)
        {
            var go = new GameObject("F_Notch" + notchSRs.Count);
            go.transform.SetParent(frameRoot.transform, false);
            go.layer = ATTR_LAYER;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "Inventory"; sr.sortingOrder = 30055;
            notchSRs.Add(sr);
        }
        // fix(172) [user]: use the GAME's own notch art (Charms page "Charm Cost N/Sprite Full|Empty"),
        // which also picks up skins for free, and sit the row DIRECTLY UNDER the equipped-charm row in
        // the top strip (was parked on the area-name line, reading as inside the box).
        FindNotchSprites();
        float targetH = 0.5f * s * Mathf.Max(0.05f, cfg.compEquipRowScale);   // same metric as the charm row
        float h = targetH * 0.70f;                                            // fix(173): larger [user] (was 0.5x the charm icons)
        float rowY = cfg.compEquipRowY * s - targetH * 0.5f - h * 0.95f;      // below the charm icons, nudged further down [user]
        float rightEdge = (cfg.compStatsX + cfg.compEquipRowX) * s * asp;     // same right margin as that row
        bool retex = total != lastNotchTotal || used != lastNotchUsed;
        for (int i = 0; i < notchSRs.Count; i++)
        {
            var sr = notchSRs[i]; if (sr == null) continue;
            bool on = i < total;
            if (sr.enabled != on) sr.enabled = on;
            if (!on) continue;
            // fix(175) [user]: EMPTY notches on the LEFT — fill from the right, so index i is lit
            // only once past the unused ones (was: lit block first, empties trailing right).
            bool lit = i >= (total - used);
            var want = lit ? (notchSprFull != null ? notchSprFull : null) : (notchSprEmpty != null ? notchSprEmpty : null);
            if (retex || sr.sprite == null || (want != null && sr.sprite != want))
            {
                if (want != null)
                {
                    sr.sprite = want;
                    sr.color = Color.white;   // real art carries its own lit/dim look
                }
                else
                {
                    // fix(1.0.0/B6): build the two fallback sprites ONCE. This ran per notch per
                    // equip/unequip (up to 11 new Sprites each time), freed only at TeardownFrame.
                    if (notchSprLitFb == null)
                    {
                        if (notchTexLit == null) { notchTexLit = Own(MakeNotchTex(28, true)); notchTexEmpty = Own(MakeNotchTex(28, false)); }
                        var r28 = new Rect(0, 0, 28, 28); var piv = new Vector2(0.5f, 0.5f);
                        notchSprLitFb = Own(Sprite.Create(notchTexLit, r28, piv, 100f));
                        notchSprEmptyFb = Own(Sprite.Create(notchTexEmpty, r28, piv, 100f));
                    }
                    sr.sprite = lit ? notchSprLitFb : notchSprEmptyFb;
                    sr.color = lit ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.40f);
                }
            }
            float sh = (sr.sprite != null) ? Mathf.Max(0.01f, sr.sprite.bounds.size.y) : 0.28f;
            float sc2 = h / sh;
            sr.transform.localScale = new Vector3(sc2, sc2, 1f);
            float wIcon = (sr.sprite != null ? sr.sprite.bounds.size.x * sc2 : h);
            sr.transform.localPosition = new Vector3(rightEdge - wIcon * 0.5f - (total - 1 - i) * h * 1.15f, rowY, 4f);
        }
        if (retex) { lastNotchTotal = total; lastNotchUsed = used; }
    }

    // [B3] hudCam2 mirrors HK's OWN Hud Canvas (masks / soul / geo — the persistent HUD) at 75% top-left of the
    // bottom panel (zoom + pan tunable); promptCam is full-frame with the same view centre as HK's HUD camera.
    void FrameHudCams(Camera src, bool popupBlackNow, bool creditShowing)
    {
        // Blank the HUD mirror while a popup/dialogue/credit draws on the bottom (compPopupBlack), so the
        // overlay reads on clean black instead of over the live HUD. The gate uses the renderer-DRAWING
        // predicate (AnyRendererDrawing, alpha-aware) — NOT activeInHierarchy, which is what made the old
        // v0.37-era auto-hide stick near the focus tablet and got the gate removed entirely.
        hudCam2.cullingMask = popupBlackNow ? 0 : 1 << hudLayer;

        // hudCam2: HUD at 75% top-left (zoom + pan tunable)
        hudCam2.orthographicSize = src.orthographicSize * (src.aspect / ((float)BOTTOM_W / BOTTOM_H)) * cfg.zoomMul;
        hudCam2.transform.position = src.transform.position + new Vector3(cfg.panX, cfg.panY, 0f);
        hudCam2.transform.rotation = src.transform.rotation;

        // promptCam: full-frame (100%), same view centre as the main HUD camera. While the opening
        // attribution draws, zoom in by creditScale (its old dedicated-camera treatment, now on this cam).
        // The dialogue zoom is NOT applied here: enlarging blindly cropped the box left/right in
        // v1.0.1. CenterDialogue() does it, where the box's real bounds are known and the zoom can be
        // clamped to keep the whole box (and the speaker name) on screen.
        promptCam.orthographicSize = creditShowing ? src.orthographicSize / Mathf.Max(0.1f, cfg.creditScale)
                                                   : src.orthographicSize;
        promptCam.transform.position = src.transform.position;
        promptCam.transform.rotation = src.transform.rotation;
        CenterDialogue();   // fix5: vertically centre the routed NPC/lore dialogue box (moves promptCam, not the box)
        CenterTutorial();   // fix(1.0.1f) [user]: fit the tutorial/prompt overlay to the bottom panel instead of inheriting the main screen's 1:1 framing
    }

    // fix(1.0.1f) [user: "i need bottom screen more zoomed in and up"]: the tutorial/prompt overlays (focus,
    // soul, charm/journal tutors) are authored for the MAIN screen and promptCam mirrors that framing 1:1, so
    // on the shorter bottom panel they land small and low with a wide empty band above. Frame them the way the
    // dialogue box is framed: fit the drawing content to the panel, then centre it.
    Renderer[] tutRs; Transform tutRsFor; int tutRsFrame = -999;
    void CenterTutorial()
    {
        if (cfg.compTutFit != 1 || loreDialogueOpen || dlgNameRouted || CreditShowing()) return;
        try
        {
            Transform t = null;
            for (int i = 0; i < tutRoots.Count; i++)
            {
                var r = tutRoots[i];
                if (r != null && r.gameObject.activeInHierarchy && AnyRendererDrawing(r)) { t = r; break; }
            }
            if (t == null) return;
            if (tutRsFor != t || tutRs == null || Time.frameCount - tutRsFrame > 30)
            { tutRs = t.GetComponentsInChildren<Renderer>(false); tutRsFor = t; tutRsFrame = Time.frameCount; }

            float baseOrtho = promptCam.orthographicSize;         // = the main camera's size
            float aspect = (float)BOTTOM_W / BOTTOM_H;
            // A full-screen backdrop is not part of the artwork the reader looks at; measuring it would peg
            // the fit at 1:1 and defeat the whole point (the same trap the dialogue frame hit).
            float rejectH = baseOrtho * 1.6f, rejectW = baseOrtho * aspect * 1.9f;
            Bounds b = default; bool have = false; float bestArea = 0f, artCx = float.NaN;
            for (int i = 0; i < tutRs.Length; i++)
            {
                var r = tutRs[i];
                if (r == null || !r.enabled || !r.isVisible) continue;
                var rb = r.bounds;
                if (float.IsNaN(rb.center.y) || rb.size.sqrMagnitude < 1e-4f) continue;
                if (rb.size.y > rejectH || rb.size.x > rejectW) continue;
                if (EffectiveAlpha(r) < 0.08f) continue;          // faded-out leftovers must not drag the frame
                if (cfg.debug == 1 && (Time.frameCount % 180) == 0)
                    Dbg($"HKDS tut + '{r.transform.name}' {rb.size.x:F2}x{rb.size.y:F2} @x{rb.center.x:F2} a={EffectiveAlpha(r):F2}");
                // fix(1.0.1f) [user: "its not on center and slightly right"]: HK ships ALTERNATE prompt lines
                // (keyboard vs gamepad wording) that are not drawn but still report bounds — one of them sits
                // ~3 units to the left, which dragged the measured centre left and pushed everything visible
                // to the right. Take the horizontal centre from the ARTWORK panel (much the largest element,
                // and genuinely centred) and let the union decide size and vertical placement only.
                float area = rb.size.x * rb.size.y;
                if (area > bestArea) { bestArea = area; artCx = rb.center.x; }
                if (!have) { b = rb; have = true; } else b.Encapsulate(rb);
            }
            if (!have) return;

            const float PAD = 1.08f;
            float needHalfW = Mathf.Max(b.extents.x, 1e-4f) * PAD, needHalfH = Mathf.Max(b.extents.y, 1e-4f) * PAD;
            float zoom = Mathf.Max(1f, cfg.compTutScale);
            zoom = Mathf.Min(zoom, (baseOrtho * aspect) / needHalfW);
            zoom = Mathf.Min(zoom, baseOrtho / needHalfH);
            zoom = Mathf.Clamp(zoom, 1f, 4f);
            promptCam.orthographicSize = baseOrtho / zoom;

            var p = promptCam.transform.position;
            float wantY = b.center.y - cfg.compTutY * promptCam.orthographicSize;   // + = content sits higher
            if (float.IsNaN(b.center.x) || float.IsNaN(wantY)) return;
            float cx = float.IsNaN(artCx) ? b.center.x : artCx;
            promptCam.transform.position = new Vector3(cx + cfg.compTutX * promptCam.orthographicSize, wantY, p.z);
            if (cfg.debug == 1 && (Time.frameCount % 90) == 0)
                Dbg($"HKDS tut fit '{t.name}' box={b.size.x:F2}x{b.size.y:F2} -> zoom={zoom:F2}");
        }
        catch (Exception e) { WarnOnce("tut centre", e); }
    }

    // fix5: HK authors the dialogue box near the TOP of the screen; on the bottom panel the user wants it
    // vertically centred. Move promptCam's view centre to the box's rendered centre instead of moving the
    // box — HK's own open/close FSM keeps driving the box untouched, and the fade quad rides the camera.
    Renderer[] dlgRs; Transform dlgRsFor; int dlgRsFrame = -999;

    // fix(1.0.1c) [user: "increase dialog box height so i can increase font size more"]
    // The font could not simply be zoomed further: HK wraps its dialogue at a FIXED rect width, so at
    // compDlgScale 1.25 the text already spanned ~88% of the bottom panel and the fit-clamp refused to go
    // bigger. The lever that actually works is to narrow HK's wrap (compDlgWidth) — the box then occupies
    // less width, the fit-clamp allows a proportionally larger zoom, and the glyphs come out bigger. That
    // alone would cost extra page turns, so the rect is made taller (compDlgHeight) to hold the extra lines,
    // which is the taller box the user asked for. Applied ONCE when the box is first found (never mid-
    // conversation: TMP re-paginates on a rect change and HK's page index would then point at other text).
    Component dlgTmp, dlgTc; RectTransform dlgRt; Vector2 dlgSizeOrig; Vector3 dlgRtPosOrig; bool dlgShaped; float dlgShapedW, dlgShapedH;
    // Everything anchored to the BOTTOM of the box has to travel down with a taller box. That is not just
    // the ornament: HK's page-advance "Arrow" and end-of-conversation "Stop" glyphs sit centred on the same
    // baseline, and leaving them behind split the bottom ornament in half [user screenshot].
    static readonly string[] DLG_BOTTOM_BITS = { "Fleur Bot", "Arrow", "Stop" };
    Transform[] dlgBotBits; Vector3[] dlgBotBitsOrig; float dlgGrewWorld; Transform dlgBackboard; Vector3 dlgBackboardOrigScale, dlgBackboardOrigPos;

    static Vector2 TcSize(Component tc)
    {
        var pi = tc.GetType().GetProperty("size"); if (pi == null) return Vector2.zero;
        var v = pi.GetValue(tc, null); return v is Vector2 ? (Vector2)v : Vector2.zero;
    }
    static void TcSetSize(Component tc, Vector2 v) { tc.GetType().GetProperty("size")?.SetValue(tc, v, null); }

    // How visible the speaker-name card actually is. HK fades it with COLOUR alpha (FadeGroup lerps the TMP
    // color.a on Title Small Main/Sub/Super) and only disables the mesh renderers once it reaches zero, so
    // Renderer.enabled alone cannot answer it — which is why handing the card back the moment the box closed
    // flashed it on the main screen.
    // fix(1.0.1d) [user: "char name not showing"]: HK's own name card cannot be relied on to stay up for a
    // conversation. Its FSM parks in "NPC Wait for Event" but leaves on either NPC TITLE DOWN or NPC CONVO
    // START, so on multi-part conversations the card fades out and the reader loses track of who is talking.
    // Forcing HK's card back on is not safe — "Quick Disappear" clears its Area Event string first, so a
    // reactivated card re-reads an empty key. Instead: remember the name while HK is showing it, and draw OUR
    // OWN copy (cloned from HK's own Title Small Main, so the font and styling are identical) whenever HK's
    // has faded but the dialogue box is still up. Never both at once, so nothing double-draws.
    // fix(1.0.1f) [user: "some name like snail shaman is cropped and only show shaman"]: HK splits a speaker
    // title across THREE TMPs — Title Small Super / Main / Sub, from the <KEY>_SUPER/_MAIN/_SUB entries of the
    // Titles sheet — and renders them at different sizes ("Snail" above "Shaman"). Cloning Main alone dropped
    // the other lines. Clone the whole Title Small group and drive every part.
    static readonly string[] NAME_PARTS = { "Title Small Main", "Title Small Sub", "Title Small Super" };
    Transform dlgNameClone; Component[] dlgNameSrcTmps, dlgNameCloneTmps; Renderer[] dlgNameCloneRs;
    string[] dlgNameParts = new string[NAME_PARTS.Length];
    string dlgNameStr = "", dlgNameSeen = ""; float dlgRouteT; Vector3 dlgNameSrcLossy = Vector3.one;
    float dlgNameFit = 1f;   // <1 only when a caption is wider than the panel

    static string TmpText(Component c)
    {
        try { var v = TmpProp(c, "text")?.GetValue(c, null); return v as string ?? ""; } catch { return ""; }
    }

    static Component TmpOn(Transform t)
    {
        if (t == null) return null;
        foreach (var c in t.GetComponents<Component>())
            if (IsTextMeshProGraphic(c)) return c;
        return null;
    }

    // The TMPs on HK's card, in NAME_PARTS order (missing parts stay null).
    // fix(1.0.1f) [user: "when talk to snail shaman first time, it show ancestral mound"]: Title Small is the
    // SAME card HK uses for area titles, so on entering a room it is left holding the area name. HK tells the
    // two apart with a bool on the card's own FSM — "NPC Title", set true by the NPC's Conversation Control
    // before it publishes the speaker key — so read that instead of trusting whatever text is loaded.
    PlayMakerFSM dlgTitleFsm; bool dlgTitleFsmSearched;
    bool NpcTitleActive()
    {
        if (!dlgTitleFsmSearched && dlgNameT != null)
        {
            dlgTitleFsmSearched = true;
            var root = dlgNameT.parent != null ? dlgNameT.parent : dlgNameT;
            foreach (var f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f != null && f.FsmName == "Area Title Control") { dlgTitleFsm = f; break; }
        }
        if (dlgTitleFsm == null) return true;   // FSM not found: fall back to accepting the text
        try { var v = dlgTitleFsm.FsmVariables.GetFsmBool("NPC Title"); return v == null || v.Value; }
        catch { return true; }
    }

    void BindNameSources()
    {
        if (dlgNameSrcTmps != null || dlgNameT == null) return;
        dlgNameSrcTmps = new Component[NAME_PARTS.Length];
        for (int i = 0; i < NAME_PARTS.Length; i++) dlgNameSrcTmps[i] = TmpOn(FindDeep(dlgNameT, NAME_PARTS[i]));
    }

    // Everything the card is currently displaying, joined — used both to detect a new speaker and to log.
    string NameCardText()
    {
        BindNameSources();
        if (dlgNameSrcTmps == null) return "";
        string j = "";
        for (int i = 0; i < dlgNameSrcTmps.Length; i++)
            if (dlgNameSrcTmps[i] != null) { var t = TmpText(dlgNameSrcTmps[i]); if (!string.IsNullOrEmpty(t)) j += (j.Length > 0 ? " " : "") + t; }
        return j;
    }

    void EnsureNameClone()
    {
        if (dlgNameClone != null || dlgNameT == null || dlgBoxT == null) return;
        try
        {
            BindNameSources();
            dlgNameSrcLossy = dlgNameT.lossyScale;
            var go = Instantiate(dlgNameT.gameObject, dlgBoxT);   // the WHOLE group: Super + Main + Sub, laid out as HK authored it
            go.name = "F_DlgName";
            SanitizeDetachedTmpClone(go);   // no FadeGroup/FSM/legacy clip driver on our detached copy
            SetLayerRecursive(go.transform, tutLayer);
            go.SetActive(true);
            dlgNameClone = go.transform;
            dlgNameCloneTmps = new Component[NAME_PARTS.Length];
            dlgNameCloneRs = new Renderer[NAME_PARTS.Length];
            for (int i = 0; i < NAME_PARTS.Length; i++)
            {
                var t = FindDeep(dlgNameClone, NAME_PARTS[i]);
                if (t == null) continue;
                t.gameObject.SetActive(true);
                dlgNameCloneTmps[i] = TmpOn(t);
                dlgNameCloneRs[i] = t.GetComponent<Renderer>();
            }
            ApplyNameCloneScale();
            SetNameClone(false);
            if (cfg.debug == 1) Dbg("HKDS dlg name clone built (Super/Main/Sub)");
        }
        catch (Exception e) { WarnOnce("dlg name clone", e); }
    }

    // The clone hangs off the dialogue box, whose scale HK animates while it opens, so a one-shot localScale
    // computed at clone time can be badly wrong by the time the animation settles. Re-assert it every frame,
    // with compDlgNameScale on top so the label can be sized to taste [user: "name is too big"].
    void ApplyNameCloneScale()
    {
        if (dlgNameClone == null || dlgNameClone.parent == null) return;
        var ps = dlgNameClone.parent.lossyScale;
        if (Mathf.Abs(ps.x) < 1e-6f || Mathf.Abs(ps.y) < 1e-6f || Mathf.Abs(ps.z) < 1e-6f) return;
        float k = Mathf.Clamp(cfg.compDlgNameScale, 0.3f, 2f) * Mathf.Clamp(dlgNameFit, 0.3f, 1f);
        var want = new Vector3(dlgNameSrcLossy.x / ps.x, dlgNameSrcLossy.y / ps.y, dlgNameSrcLossy.z / ps.z) * k;
        if ((dlgNameClone.localScale - want).sqrMagnitude > 1e-8f) dlgNameClone.localScale = want;
    }

    // Show or hide our label. A blank TMP reports its whole container rect as bounds, which would poison both
    // the placement and the zoom reserve — so an empty part has its renderer switched off, not just its text.
    void SetNameClone(bool show)
    {
        if (dlgNameCloneTmps == null) return;
        for (int i = 0; i < dlgNameCloneTmps.Length; i++)
        {
            var c = dlgNameCloneTmps[i];
            if (c == null) continue;
            string t = show && dlgNameParts != null && i < dlgNameParts.Length ? (dlgNameParts[i] ?? "") : "";
            try
            {
                TmpProp(c, "text")?.SetValue(c, t, null);
                c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null);
                var pi = TmpProp(c, "color");
                if (pi != null) { var v = pi.GetValue(c, null); if (v is Color) { var col = (Color)v; if (col.a < 0.999f) { col.a = 1f; pi.SetValue(c, col, null); } } }
            }
            catch { }
            if (dlgNameCloneRs[i] != null) dlgNameCloneRs[i].enabled = t.Length > 0;
        }
        NeutralizeDetachedTmpClip(dlgNameClone.gameObject);
    }

    // The ornament and page glyphs sit under DialogueBox, whose scale HK ANIMATES as the box opens. Converting
    // the world-space growth into their local space once, at shaping time, therefore baked in a mid-animation
    // scale and left the ornament split [user screenshot]. Re-apply every frame against the CURRENT scale.
    void ApplyBottomBits()
    {
        if (dlgBotBits == null || dlgGrewWorld <= 0f) return;
        for (int i = 0; i < dlgBotBits.Length; i++)
        {
            var t = dlgBotBits[i];
            if (t == null || t.parent == null) continue;
            float ps = Mathf.Abs(t.parent.lossyScale.y);
            if (ps > 1e-5f) t.localPosition = dlgBotBitsOrig[i] + new Vector3(0f, -dlgGrewWorld / ps, 0f);
        }
    }

    void ZeroNameCard()
    {
        if (dlgNameT == null) return;
        try
        {
            foreach (var c in dlgNameT.GetComponentsInChildren<Component>(true))
            {
                if (!IsTextMeshProGraphic(c)) continue;
                var pi = TmpProp(c, "color");
                if (pi == null) continue;
                var v = pi.GetValue(c, null);
                if (v is Color) { var col = (Color)v; col.a = 0f; pi.SetValue(c, col, null); }
            }
        }
        catch { }
    }

    // The clone hangs off the dialogue box, whose scale HK animates while it opens, so a one-shot localScale
    // computed at clone time can be badly wrong by the time the animation settles — big enough to hide the
    // label and to inflate the name reserve that caps the text size. Re-assert it every frame.
    float NameCardAlpha()
    {
        if (dlgNameT == null || !dlgNameT.gameObject.activeInHierarchy) return 0f;
        float max = 0f;
        try
        {
            foreach (var r in dlgNameT.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var sr = r as SpriteRenderer;
                if (sr != null) { max = Mathf.Max(max, sr.color.a); if (max > 0.99f) break; continue; }
                float a = -1f;
                foreach (var c in r.GetComponents<Component>())
                {
                    if (!IsTextMeshProGraphic(c)) continue;
                    var pi = TmpProp(c, "color");
                    if (pi != null) { var v = pi.GetValue(c, null); if (v is Color) a = ((Color)v).a; }
                    break;
                }
                if (a < 0f) { var m = r.sharedMaterial; if (m != null && m.HasProperty("_Color")) a = m.color.a; }
                max = Mathf.Max(max, a < 0f ? 1f : a);
                if (max > 0.99f) break;
            }
        }
        catch { return 1f; }
        return max;
    }

    void ShapeDialogue()
    {
        if (dlgBoxT == null) return;
        if (cfg.compDlgCenter != 1) { if (dlgShaped) RestoreDialogueShape(); return; }
        float wS = Mathf.Clamp(cfg.compDlgWidth, 0.4f, 1f);
        float hS = Mathf.Clamp(cfg.compDlgHeight, 1f, 2.5f);
        // re-shape only when the dials actually move (hkds_layout.json hot-reloads, so these can be tuned live)
        if (dlgShaped && Mathf.Approximately(dlgShapedW, wS) && Mathf.Approximately(dlgShapedH, hS)) return;
        try
        {
            if (dlgRt == null)
            {
                // HK's dialogue text is the TMP that carries the DialogueBox script (its siblings are the
                // shop/YN widgets, which must not be touched).
                foreach (var mb in dlgBoxT.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb.GetType().Name != "DialogueBox") continue;
                    foreach (var c in mb.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        var n = c.GetType().Name;
                        if (IsTextMeshProGraphic(c)) dlgTmp = c;
                        else if (n == "TextContainer") dlgTc = c;
                    }
                    if (dlgTmp != null) { dlgRt = mb.transform as RectTransform; break; }
                }
                // fix(1.0.1d): HK ships TMProOld, where a TextContainer component — not the RectTransform —
                // is the authority on the text area (it keeps its own m_rect and re-applies it). Writing
                // sizeDelta directly therefore did NOT change the wrap, while the position compensation I
                // paired with it still moved the block, so the whole box left the view [user: "bottom screen
                // dialog and name hidden ... blipping then hidden"]. Drive the container instead, and if
                // there is no container, shape nothing at all rather than guess.
                if (dlgRt == null || dlgTc == null) { dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS; return; }
                dlgSizeOrig = TcSize(dlgTc); dlgRtPosOrig = dlgRt.localPosition;
                dlgBotBits = new Transform[DLG_BOTTOM_BITS.Length]; dlgBotBitsOrig = new Vector3[DLG_BOTTOM_BITS.Length];
                for (int i = 0; i < DLG_BOTTOM_BITS.Length; i++)
                {
                    dlgBotBits[i] = FindDeep(dlgBoxT, DLG_BOTTOM_BITS[i]);
                    if (dlgBotBits[i] != null) dlgBotBitsOrig[i] = dlgBotBits[i].localPosition;
                }
                dlgBackboard = FindDeep(dlgBoxT, "backboard");
                if (dlgBackboard != null) { dlgBackboardOrigScale = dlgBackboard.localScale; dlgBackboardOrigPos = dlgBackboard.localPosition; }
                if (cfg.debug == 1) Dbg($"HKDS dlg container size {dlgSizeOrig} bits={(dlgBotBits[0] != null)}/{(dlgBotBits[1] != null)}/{(dlgBotBits[2] != null)} backboard={(dlgBackboard != null)}");
                if (dlgSizeOrig.x < 1e-4f || dlgSizeOrig.y < 1e-4f) { dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS; return; }
            }

            var want = new Vector2(dlgSizeOrig.x * wS, dlgSizeOrig.y * hS);
            if ((TcSize(dlgTc) - want).sqrMagnitude > 1e-5f)
            {
                // Measure in the rect's OWN local space, never world space: HK's "Box Up" FSM is animating
                // this box's position/scale while it opens, so a world-space before/after would latch a delta
                // from mid-animation and keep it forever. Keep the top edge and the horizontal centre where
                // HK authored them, so the first line still sits under the top ornament.
                var r0 = dlgRt.rect;
                float pTop0 = dlgRtPosOrig.y + r0.yMax, pBot0 = dlgRtPosOrig.y + r0.yMin, pCx0 = dlgRtPosOrig.x + r0.center.x;

                TcSetSize(dlgTc, want);
                try { dlgTmp?.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(dlgTmp, null); } catch { }

                var r1 = dlgRt.rect;
                var np = new Vector3(pCx0 - r1.center.x, pTop0 - r1.yMax, dlgRtPosOrig.z);
                dlgRt.localPosition = np;

                float grewParent = Mathf.Max(0f, pBot0 - (np.y + r1.yMin));      // how much lower the block now reaches
                float grewWorld = grewParent * (dlgRt.parent != null ? Mathf.Abs(dlgRt.parent.lossyScale.y) : 1f);
                dlgGrewWorld = grewWorld;   // applied per frame by ApplyBottomBits (parents are scale-animated)
                if (dlgBackboard != null && dlgBackboard.parent != null && dlgSizeOrig.y > 1e-5f)
                {   // the dark panel behind the text (invisible under the black-background tweak) grows with it
                    var sc = dlgBackboardOrigScale; sc.y *= want.y / dlgSizeOrig.y; dlgBackboard.localScale = sc;
                    float ps = Mathf.Abs(dlgBackboard.parent.lossyScale.y);
                    if (ps > 1e-5f) dlgBackboard.localPosition = dlgBackboardOrigPos + new Vector3(0f, -grewWorld * 0.5f / ps, 0f);
                }
                if (cfg.debug == 1) Dbg($"HKDS dlg shaped -> {want} grewWorld={grewWorld:F3}");
            }
            dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS;
        }
        catch (Exception e) { dlgShaped = true; dlgShapedW = wS; dlgShapedH = hS; WarnOnce("dlg shape", e); }
    }

    // Hand HK's box back exactly as authored (used if the feature is switched off at runtime).
    void RestoreDialogueShape()
    {
        try { RestoreDialogueShapeOrThrow(); }
        catch (Exception e) { WarnOnce("dialogue shape restore", e); }
    }

    // Failure-propagating form used by transport shutdown. The shaped-state
    // ownership flag clears only after every mutable reference is restored.
    void RestoreDialogueShapeOrThrow()
    {
        if (dlgTc != null && dlgSizeOrig.sqrMagnitude > 1e-5f) { TcSetSize(dlgTc, dlgSizeOrig); if (dlgRt != null) dlgRt.localPosition = dlgRtPosOrig; }
        for (int i = 0; dlgBotBits != null && i < dlgBotBits.Length; i++)
            if (dlgBotBits[i] != null) dlgBotBits[i].localPosition = dlgBotBitsOrig[i];
        if (dlgBackboard != null) { dlgBackboard.localScale = dlgBackboardOrigScale; dlgBackboard.localPosition = dlgBackboardOrigPos; }
        dlgShaped = false;
    }

    // fix5: HK authors the dialogue box near the TOP of the screen; on the bottom panel the user wants it
    // vertically centred. Move promptCam's view centre to the box's rendered centre instead of moving the
    // box — HK's own open/close FSM keeps driving the box untouched, and the fade quad rides the camera.
    void CenterDialogue()
    {
        // fix(1.0.1d) [user: "bottom screen dialog and name hidden ... blipping then hidden"]: this gate was
        // widened to `|| dlgNameRouted` on the belief that NPC talk did not set loreDialogueOpen. It does —
        // RouteLoreDialogue sets it for ANY box that is up. The widened gate meant that while the name card
        // was being held on the bottom during its fade-out, this kept re-framing promptCam onto a box that
        // had already closed, so the whole bottom panel went black. Frame only while the box is genuinely up.
        if (!loreDialogueOpen || cfg.compDlgCenter != 1 || dlgBoxT == null) return;
        try
        {
            ShapeDialogue();
            ApplyBottomBits();
            ApplyNameCloneScale();
            if (dlgRsFor != dlgBoxT || dlgRs == null || Time.frameCount - dlgRsFrame > 30)
            { dlgRs = dlgBoxT.GetComponentsInChildren<Renderer>(true); dlgRsFor = dlgBoxT; dlgRsFrame = Time.frameCount; }   // refresh: TMP submeshes/arrow can appear after open
            Bounds b = default; bool have = false;
            // fix(1.0.1e) [user: "text need larger"]: something under DialogueManager reports PIXEL-scale
            // bounds (measured 3840x2915 world units against a box that is really ~13.5x5.3 — a full-screen
            // canvas element). Encapsulating it made the "box" enormous, so the fit-zoom computed 0.01 and was
            // clamped to its 1.0 floor: the text could never grow, whatever the dials said. Anchor the frame
            // on the TEXT RECT, whose size we set ourselves and therefore trust, and admit only renderers of a
            // sane size next to it (the ornaments and page glyphs) — everything else is not part of the box.
            float maxW = float.MaxValue, maxH = float.MaxValue, rectHalfW = 0f, rectMinX = float.NaN;
            if (dlgRt != null)
            {
                var wc = new Vector3[4]; dlgRt.GetWorldCorners(wc);
                var rb0 = new Bounds(wc[0], Vector3.zero); for (int i = 1; i < 4; i++) rb0.Encapsulate(wc[i]);
                if (!float.IsNaN(rb0.size.x) && rb0.size.x > 1e-4f)
                {
                    maxW = rb0.size.x * 4f; maxH = Mathf.Max(rb0.size.y, 1e-3f) * 4f;
                    rectHalfW = rb0.extents.x;   // stable width reference for the zoom (glyph width varies per page)
                    rectMinX = rb0.min.x;        // the TEXT's left edge — what the name should line up with
                }
            }
            for (int i = 0; i < dlgRs.Length; i++)
            {
                var r = dlgRs[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                // the dark backboard plate is bigger than the visible box and would throttle the zoom
                if (dlgBackboard != null && r.transform == dlgBackboard) continue;
                // fix(1.0.1e) [user: "it pushed down screen because char name is popping"]: our name label is
                // parented to the box, so it lands in the box's own renderer list. Measuring it made the frame
                // grow UPWARD the moment the name appeared — pushing the box down the panel and jumping the
                // zoom as the name popped in and out. The name is positioned against this frame, so it can
                // never be part of it.
                if (dlgNameClone != null && (r.transform == dlgNameClone || r.transform.IsChildOf(dlgNameClone))) continue;
                // fix(1.0.1e) [user: "instead of zooming based on text, how about based on fleur dialog that
                // consistent"]: the glyph mesh changes size with every page (a short line measured 11.3 wide,
                // a long one 13.3), which re-zoomed and re-centred the box as the reader turned pages. The
                // ornaments are fixed in size and position, so they define a frame that never moves; the text
                // lives inside them, and its wrap width is bounded by the rect used for the width reserve.
                if (dlgRt != null && r.transform == dlgRt) continue;
                // fix(1.0.1e) [user: "why its pulsing, isnt bottom center ornament only that pulsing"]: HK
                // ANIMATES the page-advance glyph, so measuring it made the frame breathe — and the zoom is
                // derived from the frame, so the entire box pulsed with it. The glyphs still travel down with
                // a taller box (ApplyBottomBits); they just must not define its size.
                if (dlgBotBits != null && (r.transform == dlgBotBits[1] || r.transform == dlgBotBits[2])) continue;
                var rb = r.bounds;
                if (float.IsNaN(rb.center.y) || float.IsInfinity(rb.center.y) || rb.size.sqrMagnitude < 1e-4f) continue;
                if (rb.size.x > maxW || rb.size.y > maxH)
                {
                    if (cfg.debug == 1 && (Time.frameCount % 180) == 0)
                        Dbg($"HKDS dlg frame: ignoring out-of-scale '{r.transform.name}' {rb.size.x:F0}x{rb.size.y:F0}");
                    continue;
                }
                if (cfg.debug == 1 && (Time.frameCount % 180) == 0)
                    Dbg($"HKDS dlg frame: + '{r.transform.name}' {rb.size.x:F2}x{rb.size.y:F2} @y{rb.center.y:F2}");
                if (!have) { b = rb; have = true; } else b.Encapsulate(rb);
            }
            if (!have) return;
            // fix(1.0.1b) [user: "dialog is cropped left and right"]: v1.0.1 divided the ortho size by
            // compDlgScale with no regard for how wide the box actually is, so the enlarged box ran off
            // both edges. Enlarge only as far as the box still FITS, and frame the camera on the box
            // itself (it is not centred on the main camera's x, which is why the left side clipped worst).
            float aspect = (float)BOTTOM_W / BOTTOM_H;
            float baseOrtho = promptCam.orthographicSize;          // = the main camera's size
            // reserve room above the box for the speaker-name card
            float nameH = 0f, nameHalfW = 0f;
            if (dlgNameRouted && dlgNameT != null)
            {
                try
                {
                    // fix(1.0.1e): measure only the label that is ACTUALLY on screen. An empty TMP falls back
                    // to reporting its whole container rect — our clone measured 1341x754 while blank — and
                    // that was feeding the width reserve, collapsing the fit-zoom to its floor. Both axes are
                    // now capped against the box: the speaker's name must never dictate the box's zoom.
                    var m = dlgNameClone;
                    if (m != null)
                    {
                        var nb = ItemBounds(m);
                        if (nb.size.y > 1e-4f) { nameH = nb.size.y; nameHalfW = nb.extents.x; }
                    }
                    nameH = Mathf.Min(nameH, b.size.y * 0.25f);
                    nameHalfW = Mathf.Min(nameHalfW, b.extents.x);
                }
                catch { nameH = 0f; nameHalfW = 0f; }
            }
            const float PAD = 1.03f;                               // 3% breathing room at the edges
            float needHalfW = Mathf.Max(Mathf.Max(b.extents.x, nameHalfW), rectHalfW) * PAD;
            float needHalfH = (b.extents.y + nameH * 1.1f) * PAD;  // box + name stacked above it
            float zoom = Mathf.Max(1f, cfg.compDlgScale);
            if (needHalfW > 1e-4f) zoom = Mathf.Min(zoom, (baseOrtho * aspect) / needHalfW);
            if (needHalfH > 1e-4f) zoom = Mathf.Min(zoom, baseOrtho / needHalfH);
            zoom = Mathf.Clamp(zoom, 1f, 4f);
            promptCam.orthographicSize = baseOrtho / zoom;
            if (cfg.debug == 1 && (Time.frameCount % 90) == 0)
            {
                float zW = needHalfW > 1e-4f ? (baseOrtho * aspect) / needHalfW : 99f;
                float zH = needHalfH > 1e-4f ? baseOrtho / needHalfH : 99f;
                Dbg($"HKDS dlg fit box={b.size.x:F2}x{b.size.y:F2} nameH={nameH:F2} zW={zW:F2} zH={zH:F2} ceil={cfg.compDlgScale:F2} -> zoom={zoom:F2} name='{dlgNameStr}' hk={NameCardAlpha():F2}");
            }

            // fix(1.0.1d) [user: "it blipping, want to show but second later seems hidden"]: 1.0.1c added a
            // `+ nameAllow * 0.5f` term here to reserve a band above the box. HK does not activate the name
            // card until ~1 s into the conversation, so that term switched on mid-conversation and jumped the
            // bottom camera far enough to carry the box out of view — the box showed, then vanished a second
            // later. The camera stays centred on the box; the name is fitted into the frame below instead.
            float wantY = b.center.y + cfg.compDlgCenterY * promptCam.orthographicSize;
            if (float.IsNaN(wantY) || float.IsInfinity(wantY)) return;
            var p = promptCam.transform.position;
            // centre horizontally on the BOX so it cannot clip on one side
            float wantX = float.IsNaN(b.center.x) ? p.x : b.center.x;
            promptCam.transform.position = new Vector3(wantX, wantY, p.z);
            p = promptCam.transform.position;
            // fix#2(160-fb): park the routed speaker-name card ("Title Small") above the dialogue box.
            // fix(1.0.1c): left-aligned with the box's TEXT and sitting in the reserved band, with a real
            // margin — previously it was clamped hard into the top-left corner of the panel.
            if (dlgNameRouted && dlgNameT != null)
            {
                EnsureNameClone();
                // fix(1.0.1e): ONE display path. HK's card is kept silent and our label always draws — its own
                // placement cannot be measured reliably (empty parts report a full-container fallback size).
                ZeroNameCard();
                var cur = NameCardText();
                // Accept the card's text once it differs from what it held when the box opened (a new speaker),
                // or after a short grace — by then HK's "Set Text Small" has certainly run, so the same speaker
                // talking twice still resolves correctly.
                if (!string.IsNullOrEmpty(cur) && NpcTitleActive() && (cur != dlgNameSeen || Time.unscaledTime - dlgRouteT > 0.35f))
                {
                    dlgNameStr = cur;
                    for (int i = 0; dlgNameSrcTmps != null && i < dlgNameSrcTmps.Length; i++)
                        dlgNameParts[i] = dlgNameSrcTmps[i] != null ? TmpText(dlgNameSrcTmps[i]) : "";
                }
                SetNameClone(dlgNameStr.Length > 0);
                Transform tgt = dlgNameClone;
                if (tgt != null)
                {
                    var nb = ItemBounds(tgt);
                    float nHalfW = nb.extents.x, nHalfH = nb.extents.y;
                    if (nHalfW < 1e-4f) { nHalfW = nameHalfW; nHalfH = nameH * 0.5f; }
                    float hw = promptCam.orthographicSize * aspect, hh = promptCam.orthographicSize;
                    // A name wider than the panel would be clipped whatever we do about position, so shrink
                    // that one caption to fit (only ever downward — normal names are untouched).
                    float budget = (hw * 2f) * 0.94f;
                    dlgNameFit = (nHalfW * 2f > budget && nHalfW > 1e-4f) ? budget / (nHalfW * 2f) : 1f;
                    if (dlgNameFit < 0.999f) { nHalfW *= dlgNameFit; nHalfH *= dlgNameFit; }
                    // fix(1.0.2) [user: "when on, it need more left, also check if its can be left dynamic to
                    // prevent long name cropped"]: the caption used to align to the dialogue TEXT's left edge,
                    // which sits inset from the panel. Anchor its LEFT EDGE to a fixed margin from the panel
                    // instead — further left, and inherently safe for long names: the start never moves, the
                    // name grows rightward, so it cannot be pushed off the left however long it gets.
                    // compDlgNameLeft is that margin (fraction of the half-view); compDlgNameX still nudges.
                    float leftEdge = p.x - hw + cfg.compDlgNameLeft * hw;
                    float nx = leftEdge + nHalfW + cfg.compDlgNameX * hw;
                    float ny = b.max.y + nHalfH + hh * 0.12f + cfg.compDlgNameY * hh;
                    float minX = p.x - hw + nHalfW + hw * 0.02f, maxX = p.x + hw - nHalfW - hw * 0.02f;
                    float minY = p.y - hh + nHalfH + hh * 0.04f, maxY = p.y + hh - nHalfH - hh * 0.04f;
                    if (maxX > minX) nx = Mathf.Clamp(nx, minX, maxX);
                    if (maxY > minY) ny = Mathf.Clamp(ny, minY, maxY);
                    tgt.position = new Vector3(nx, ny, tgt.position.z);
                }
            }
        }
        catch (Exception e) { WarnOnce("dlg center", e); }
    }
}
