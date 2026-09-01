using System;
using System.Collections.Generic;
using UnityEngine;

// [B7] BOTTOM SCREEN — Charms tab. The redesign layout (detail band on top, HK's native honeycomb grid below,
// equipped darkening, self-drawn notch-cost pips), the default/tapped charm detail, and the equip watch.
public partial class HKDualScreen
{
    // Per-clone charm cache (built once in EnsureCharmRefs; replaces two Transform dictionaries + a 40x
    // GetComponents/reflection scan that ran on EVERY layout call = every tap and every equip change).
    class CharmBoard { public Transform t; public int n; public GameObject icon; public Renderer br; public Vector3 natPos, s0, iconS0; }
    readonly List<CharmBoard> charmBoards = new List<CharmBoard>();
    GameObject charmBoardsFor;    // pane the cache belongs to (a rebuilt clone has new transforms)
    Transform charmNameT, charmDescT; Renderer charmNameR, charmDescR;   // detail text (cached; renderers must be FORCE-ENABLED — see LayoutCharmsRedesign)
    Renderer[] hkNotchRs;         // HK's own (invisibly-rendering) Notches cluster, kept off

    Transform costPipRoot;        // redesign: OUR OWN notch-cost pip row (HK's Notches renders invisibly even when layered)

    Sprite notchSprite;           // redesign: cached filled-notch sprite grabbed from HK's "Sprite Full"

    int lastDetailCharmN;         // redesign: the charm number PopulateCharmDetail last DISPLAYED (so cost pips match the name)

    int lastCharmEquipHash = int.MinValue;   // redesign: watch the equipped-charm set to re-darken the grid in place (no clone rebuild)

    // World-space bounds of a TMP's DRAWN GLYPHS (reflection textBounds -> TransformPoint; renderer bounds
    // over-span the authored rect). False if the component/property is missing or the mesh is empty.
    static System.Reflection.PropertyInfo _tmpTextBoundsPI;
    static bool TryTmpGlyphBoundsWorld(Transform tmpT, out Vector3 wMin, out Vector3 wMax)
    {
        wMin = wMax = Vector3.zero;
        try
        {
            if (tmpT == null) return false;
            Component tmp = null;
            foreach (var ccc in tmpT.GetComponents<Component>()) { if (IsTextMeshProGraphic(ccc)) { tmp = ccc; break; } }
            if (tmp == null) return false;
            if (_tmpTextBoundsPI == null || _tmpTextBoundsPI.DeclaringType != tmp.GetType()) _tmpTextBoundsPI = tmp.GetType().GetProperty("textBounds");
            if (_tmpTextBoundsPI == null) return false;
            Bounds tb = (Bounds)_tmpTextBoundsPI.GetValue(tmp, null);
            if (tb.size.sqrMagnitude < 1e-8f) return false;
            wMin = tmpT.TransformPoint(tb.min); wMax = tmpT.TransformPoint(tb.max);
            if (wMax.x < wMin.x) { var t2 = wMin.x; wMin.x = wMax.x; wMax.x = t2; }
            if (wMax.y < wMin.y) { var t2 = wMin.y; wMin.y = wMax.y; wMax.y = t2; }
            return true;
        }
        catch { return false; }
    }

    static int CharmNumOf(Transform t)
    {
        try
        {
            foreach (var mb in t.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name == "InvCharmBackboard")
                {
                    var m = mb.GetType().GetMethod("GetCharmNum"); if (m != null) return (int)m.Invoke(mb, null);
                    var f = mb.GetType().GetField("charmNum"); if (f != null) return (int)f.GetValue(mb);
                }
        }
        catch { }
        return 0;
    }

    // Build the per-clone caches: sorted board list (charm id, backboard transform/renderer, icon object,
    // NATIVE world position + scales — captured HERE, before any layout moves them), the detail text refs,
    // and HK's Notches renderers. One reflection pass per CLONE instead of per layout call.
    void EnsureCharmRefs(GameObject pane)
    {
        if (ReferenceEquals(charmBoardsFor, pane) && charmBoards.Count > 0) return;
        charmBoardsFor = pane; charmBoards.Clear(); costPipRoot = null;   // pips belonged to the old clone
        foreach (var comp in pane.GetComponentsInChildren<Component>(true))
        {
            if (comp == null || comp.GetType().Name != "InvCharmBackboard") continue;
            var b = new CharmBoard { t = comp.transform, n = CharmNumOf(comp.transform) };
            b.br = b.t.GetComponent<Renderer>();
            b.natPos = b.t.position; b.s0 = b.t.localScale;
            try { var f = comp.GetType().GetField("charmObject"); b.icon = f != null ? f.GetValue(comp) as GameObject : null; } catch { }
            if (b.icon != null) b.iconS0 = b.icon.transform.localScale;
            charmBoards.Add(b);
        }
        charmBoards.Sort((a, b) => a.n.CompareTo(b.n));   // fixed charm-ID order = main screen
        charmNameT = FindDeep(pane.transform, "Text Name"); charmNameR = charmNameT != null ? charmNameT.GetComponent<Renderer>() : null;
        charmDescT = FindDeep(pane.transform, "Text Desc"); charmDescR = charmDescT != null ? charmDescT.GetComponent<Renderer>() : null;
        var hkn = FindDeep(pane.transform, "Notches");
        hkNotchRs = hkn != null ? hkn.GetComponentsInChildren<Renderer>(true) : null;
    }

    // [B7] Equip/unequip watch: equipping changes only darkening (not the charm set), so re-lay-out IN PLACE
    // instead of rebuilding the clone (the rebuild path broke the render). Fires only on an equip change.
    void CharmsTick()
    {
        if (tab.cur == COMP_CHARM && cfg.compCharmsRedesign == 1)
        {
            int eh = Charms().hash;   // frame-cached (was a 40x string-concat + GetBool loop EVERY frame on this tab)
            if (eh != lastCharmEquipHash) { lastCharmEquipHash = eh; try { ApplyFit(LayoutCharmsRedesign(paneClone)); } catch { } }   // re-darken IMMEDIATELY on equip/unequip (direct call, not via a flag another block may consume)
        }
    }

    // [B7] Charms pane fit: (a) the charm-grid backboards (the clean square grid), then fold in the POPULATED right
    // detail (Text Name/Desc, X-range only) and the equipped-charm row so the whole grid+detail frames like the real menu.
    void CharmsPaneBounds(GameObject go, ref Bounds b, ref bool have, ref int kept)
    {
        // (a) charm-grid backboards — the clean square grid.
        foreach (var comp in go.GetComponentsInChildren<Component>())
        {
            if (comp == null || comp.GetType().Name != "InvCharmBackboard") continue;
            var r = comp.GetComponent<Renderer>() ?? comp.GetComponentInChildren<Renderer>();
            if (r == null || !r.enabled) continue;
            var rb = r.bounds;
            if (rb.size.sqrMagnitude < 1e-8f || rb.size.x > 200f || rb.size.y > 200f) continue;
            if (!have) { b = rb; have = true; } else b.Encapsulate(rb); kept++;
        }
        if (have)
        {
            // CHARM: fold in the POPULATED right detail by its RENDERED extent (Text Name/Desc draw real
            // glyphs now), X-range only, so the whole grid+detail frames and the description's right edge
            // is never cropped. (Anchoring to the transform position under-measured it -> right crop.)
            foreach (var nm in new[] { "Text Name", "Text Desc" })
            {
                var tt = FindDeep(go.transform, nm); var rr = tt != null ? tt.GetComponent<Renderer>() : null;
                if (rr != null && rr.enabled && rr.bounds.size.x > 1e-3f && rr.bounds.size.x < 60f && rr.bounds.size.y < 60f)
                { b.Encapsulate(new Vector3(rr.bounds.min.x, b.center.y, b.center.z)); b.Encapsulate(new Vector3(rr.bounds.max.x, b.center.y, b.center.z)); }
            }
            // Equipped-charm icon row + notch pips (Equipped Charms subtree) so they frame WITH the grid like
            // the real menu. Filter any far stray (a pip sub-sprite HK's charm_cost_indicator FSM parked at y+50).
            var eqC = FindDeep(go.transform, "Equipped Charms");
            if (eqC != null)
                foreach (var r in eqC.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    var rb = r.bounds;
                    if (rb.size.sqrMagnitude < 1e-8f || rb.size.x > 30f || rb.size.y > 30f) continue;
                    if ((rb.center - b.center).sqrMagnitude > 900f) continue;
                    b.Encapsulate(rb);
                }
        }
    }

    // [B7] Charms clone init: the equipped row isn't self-built on a detached clone, and the right-side detail is
    // populated for a default charm (a tapped charm wins later).
    void CharmsPaneInit(GameObject pane)
    {
        var bec = pane.GetComponentInChildren<BuildEquippedCharms>(true);   // equipped row isn't self-built
        if (bec != null) try { bec.SendMessage("BuildCharmList", SendMessageOptions.DontRequireReceiver); } catch { }
        PopulateCharmDetail(pane);            // name+desc on the right (default charm)
    }

    // [B7] Charms clone fingerprint: which charms EXIST (got/broken).
    // NOTE: charmSlots/charmSlotsFilled are intentionally NOT in the stamp — they change on every equip/unequip but
    // don't change which charms EXIST, so they must not force a clone rebuild (that rebuild path left the detail
    // container inactive -> grid/name/detail vanished). Darkening updates via the reuse path's re-layout + the
    // per-frame equip watch instead. equippedCharm is likewise NOT in the stamp (equipping changes only darkening).
    int CharmsStampHash(PlayerData pd, int h)
    {
        for (int i = 1; i <= 40; i++)
        {
            if (CharmGot(pd, i)) h = h * 31 + i;
            if (CharmBroken(pd, i)) h = h * 31 + 293 + i;
        }
        return h;
    }

    // Populate the Charms pane's detail (name + description + cost pips) for the TAPPED charm — or EMPTY when
    // nothing is selected (user: match the Inventory tab, which shows no detail until a tap). compCharmDetailN
    // remains a debug override (>0 forces that id). A detached clone has no live cursor to drive "UI Charms",
    // so the TMP text is set directly through the localization sheet.
    void PopulateCharmDetail(GameObject pane)
    {
        try
        {
            int n = sel.charmN > 0 ? sel.charmN : cfg.compCharmDetailN;   // <=0 -> empty detail (no default charm)
            lastDetailCharmN = n;   // 0 -> LayoutCharmsRedesign draws NO cost pips; >0 -> pips match this charm
            SetTmpTextByName(pane.transform, "Text Name", n > 0 ? CharmString("CHARM_NAME_" + n) : "");
            SetTmpTextByName(pane.transform, "Text Desc", n > 0 ? CharmString("CHARM_DESC_" + n) : "");
            // Detail charm ICON (Details/Detail Sprite): its "Update Sprite" FSM defaults ID=0 -> "NO CHARM" ->
            // disables the renderer, so it's blank on the clone. Set the sprite directly + disable its FSMs.
            // (The redesign band hides this icon again — kept for the compCharmsRedesign=0 path.)
            var det = FindDeep(pane.transform, "Detail Sprite");
            if (det != null)
            {
                foreach (var f in det.GetComponents<PlayMakerFSM>()) if (f != null) f.enabled = false;
                var sr = det.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (n > 0) { try { var cil = CharmIconList.Instance; if (cil != null) sr.sprite = cil.GetSprite(n); } catch { } }
                    sr.enabled = n > 0; sr.color = Color.white;
                }
            }
            if (cfg.compCharmsRedesign == 1) { var fr = LayoutCharmsRedesign(pane); if (pane == paneClone) { ApplyFit(fr); paneNeedsFit = true; } }   // re-lay now + once more next tick (fresh TMP bounds can be a frame stale -> the second pass pins the inline pips to the SETTLED name width)
        }
        catch (Exception e) { Dbg($"HKDS charm detail err {e.Message}"); }
    }

    // Charms REDESIGN (user: "detail on top full width, below it full width charms list"). HK's authored pane is
    // grid-LEFT / detail-RIGHT; here we WORLD-position the objects into: a detail band across the TOP (name with the
    // cost pips INLINE after it, wrapped description below) and the charm backboards as a full-width grid BELOW,
    // replicating HK's exact honeycomb via each board's cached NATIVE position. Positions persist (the clone is
    // pinned static at compRoot); PopulateCharmDetail swaps text in place and re-calls this. All per-item lookups
    // come from the EnsureCharmRefs cache — no reflection here.
    FitResult LayoutCharmsRedesign(GameObject pane)
    {
        FitResult fitOut = default;   // invalid unless the layout completes
        if (pane == null || compRoot == null || cfg.compCharmsRedesign != 1) return fitOut;
        try
        {
            var pd = PlayerData.instance;
            EnsureCharmRefs(pane);
            int n = charmBoards.Count;
            if (n == 0) return fitOut;
            // GRID: rigid-translate the native cluster into the region below the detail band. Backboards forced
            // active + their renderer on (EQUIPPED charms: HK hides the slot as "worn"; we show it darkened + tappable).
            Vector3 c = compRoot.position;
            float cell = 0f;
            Vector3 oMin = new Vector3(1e9f, 1e9f, 0f), oMax = new Vector3(-1e9f, -1e9f, 0f);
            foreach (var b in charmBoards)
            {
                oMin = Vector3.Min(oMin, b.natPos); oMax = Vector3.Max(oMax, b.natPos);
                if (cell < 0.3f && b.br != null && b.br.bounds.size.x > 0.1f) cell = Mathf.Max(b.br.bounds.size.x, b.br.bounds.size.y);
            }
            if (cell < 0.3f) cell = 0.75f;
            float pitch = cell * 1.18f;                              // detail-band Y offsets are still expressed in pitch units
            Vector3 oC = (oMin + oMax) * 0.5f;
            float natW = Mathf.Max(0.1f, oMax.x - oMin.x);
            float scale = Mathf.Max(0.2f, cfg.compCharmsCellScale);  // uniform cluster scale (1 = HK native size)
            float gridW = natW * scale;                              // grid width = native span * scale
            float gridTopY = c.y + cfg.compCharmsGridTopY * pitch;   // native TOP row lands here; grid grows downward
            var cs = Charms();                                       // frame-cached equip set
            foreach (var b in charmBoards)
            {
                if (!b.t.gameObject.activeSelf) b.t.gameObject.SetActive(true);
                if (b.br != null) b.br.enabled = true;               // slot bg (DOT when un-obtained) + tap target
                var bpos = new Vector3(c.x + (b.natPos.x - oC.x) * scale, gridTopY + (b.natPos.y - oMax.y) * scale, c.z);
                b.t.position = bpos;
                b.t.localScale = b.s0 * scale;
                if (b.icon == null) continue;
                // the charm ICON is a sibling "charmObject": move it onto the slot, scale it, dim ONLY if equipped.
                bool got = pd == null || b.n <= 0 || CharmGot(pd, b.n);
                if (got)
                {
                    if (!b.icon.activeSelf) b.icon.SetActive(true);
                    var cr = b.icon.GetComponentInChildren<Renderer>(true); if (cr != null) cr.enabled = true;
                    b.icon.transform.position = new Vector3(bpos.x, bpos.y, bpos.z - 0.02f);
                    b.icon.transform.localScale = b.iconS0 * Mathf.Max(0.1f, cfg.compCharmsIconScale) * scale;
                    float dim = cs.Has(b.n) ? Mathf.Clamp01(cfg.compCharmsDimEquip) : 1f;   // EQUIPPED -> darkened
                    foreach (var sr in b.icon.GetComponentsInChildren<SpriteRenderer>(true)) if (sr != null) sr.color = new Color(dim, dim, dim, 1f);   // reset ALL so unequipped are NEVER left dark
                }
                else if (b.icon.activeSelf) b.icon.SetActive(false);   // UN-OBTAINED: icon hidden -> the backboard DOT shows
            }
            // DETAIL BAND on top: name (cost pips inline after it), wrapped description below.
            int detN = lastDetailCharmN;                             // 0 = nothing selected -> band stays empty
            float detailCy = c.y + cfg.compCharmsDetailBandY * pitch;
            var tn = charmNameT; var td = charmDescT;
            // A detached clone can leave HK's detail PANEL parent inactive (no cursor), hiding name/desc even though
            // their text is set — force the parent chains active...
            for (Transform p = tn; p != null && p != pane.transform; p = p.parent) if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            for (Transform p = td; p != null && p != pane.transform; p = p.parent) if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            // ...and FORCE THE TEXT RENDERERS ON: HK's idling FSMs can disable them during the clone's settle window,
            // and nothing else re-enables them — THE "charms tab only renders the icons, name/detail gone" bug.
            if (charmNameR != null) charmNameR.enabled = true;
            if (charmDescR != null) charmDescR.enabled = true;
            SetTmpFont(tn, cfg.compCharmsNameFont);
            SetTmpFont(td, cfg.compCharmsDescFont);
            WrapTmp(td, cfg.compCharmsDescWidth);
            SetTmpLineSpacing(td, cfg.compCharmsLineSpacing);   // tighten the description's line + paragraph gaps
            float nameLeftX = c.x + gridW * cfg.compCharmsNameX;             // shared LEFT edge for name + description
            float nameY = detailCy + pitch * cfg.compCharmsNameDY;          // name sits ABOVE the description
            if (tn != null) { var r = charmNameR; float leftOff = r != null ? tn.position.x - r.bounds.min.x : 0f; float topOff = r != null ? r.bounds.max.y - tn.position.y : 0f; tn.position = new Vector3(nameLeftX + leftOff, nameY - topOff, c.z - 0.1f); }
            if (td != null) { var r = charmDescR; float leftOff = r != null ? td.position.x - r.bounds.min.x : 0f; float topOff = r != null ? r.bounds.max.y - td.position.y : 0f; td.position = new Vector3(nameLeftX + leftOff, detailCy - topOff, c.z - 0.1f); }
            // COST pips drawn OURSELVES from HK's real notch sprite (HK's own "Notches" renders invisibly even when
            // layered — kept off). INLINE: the row starts right AFTER the name's rendered edge, vertically centred on
            // the name line (user: "cost notches right after charm name"). Zero pips when nothing is selected.
            {
                if (hkNotchRs != null) foreach (var rr in hkNotchRs) if (rr != null && rr.enabled) rr.enabled = false;
                int cost = 0;
                if (detN > 0) { cost = 1; try { int cc = pd != null ? pd.GetInt("charmCost_" + detN) : 1; if (cc > 0) cost = cc; } catch { } }
                if (notchSprite == null) foreach (var srr in pane.GetComponentsInChildren<SpriteRenderer>(true)) { if (srr != null && srr.gameObject.name == "Sprite Full" && srr.sprite != null) { notchSprite = srr.sprite; break; } }
                if (notchSprite != null && cost > 0 && charmNameR != null)
                {
                    if (costPipRoot == null) { var g = new GameObject("HKDS CostPips"); g.transform.SetParent(pane.transform, false); costPipRoot = g.transform; }
                    while (costPipRoot.childCount < 6) { var pg = new GameObject("pip"); pg.transform.SetParent(costPipRoot, false); pg.AddComponent<SpriteRenderer>(); }
                    float pw = 0.5f * Mathf.Max(0.1f, cfg.compCharmsNotchScale);   // pip world width
                    float gap = pw * 1.2f, sw = Mathf.Max(0.01f, notchSprite.bounds.size.x);
                    // Anchor to the GLYPHS, not the renderer bounds: HK's Text Name TMP mesh bounds span the whole
                    // authored text RECT (much taller than the visible line), so bounds.center.y sat the pips down in
                    // the description. TMP.textBounds is tight to the drawn glyphs -> its right edge + line centre.
                    float pipX0, pipY;
                    Vector3 gMin, gMax;
                    if (TryTmpGlyphBoundsWorld(tn, out gMin, out gMax))
                    { pipX0 = gMax.x + pw * 0.9f; pipY = (gMin.y + gMax.y) * 0.5f + cfg.compCharmsNotchDY * pitch; }
                    else
                    { var nb = charmNameR.bounds; pipX0 = nb.max.x + pw * 0.9f; pipY = nameY - pw * 0.6f + cfg.compCharmsNotchDY * pitch; }   // fallback: just under the name TOP (which we placed at nameY)
                    for (int i = 0; i < costPipRoot.childCount; i++)
                    {
                        var pt = costPipRoot.GetChild(i); var sr = pt.GetComponent<SpriteRenderer>();
                        bool show = i < cost; sr.enabled = show; pt.gameObject.SetActive(show); if (!show) continue;
                        sr.sprite = notchSprite; sr.color = Color.white; sr.sortingOrder = 60;
                        pt.gameObject.layer = ATTR_LAYER;
                        float ps = pw / sw; pt.localScale = new Vector3(ps, ps, 1f);
                        pt.position = new Vector3(pipX0 + i * gap + pw * 0.5f, pipY, c.z - 0.2f);   // left-aligned row, growing right
                        float dy = pipY - sr.bounds.center.y; pt.position += new Vector3(0f, dy, 0f);   // pip's VISUAL centre on the name line (notch sprite pivot is off-centre)
                    }
                    if (cfg.debug == 1) Dbg($"HKDS costpips inline cn={detN} cost={cost} pipX0={pipX0:F2} pipY={pipY:F2}");
                }
                else if (costPipRoot != null)   // no selection (or no sprite yet) -> hide every pip
                    for (int i = 0; i < costPipRoot.childCount; i++) costPipRoot.GetChild(i).gameObject.SetActive(false);
            }
            var eqc = FindDeep(pane.transform, "Equipped Charms");   // HK's in-pane equipped row -> hidden (top-right compEquipRow covers it)
            if (eqc != null && eqc.gameObject.activeSelf) eqc.gameObject.SetActive(false);
            // FIXED framing (decoupled): centre + zoom depend ONLY on compRoot, the grid WIDTH, and the config —
            // moving the name/desc/grid via config does not re-centre the whole view. Grid fills ~compCharmsGridPct.
            float aspect = attrCam != null ? attrCam.aspect : (float)BOTTOM_W / BOTTOM_H;
            fitOut = new FitResult
            {
                center = new Vector3(c.x, c.y + cfg.compCharmsCenterY * pitch, c.z),
                ortho = ((gridW * 0.5f) / aspect / Mathf.Clamp(cfg.compCharmsGridPct, 0.3f, 1f)) * 1.06f / Mathf.Max(0.3f, cfg.compCharmsZoom),
                valid = true,
            };
            if (cfg.debug == 1) Dbg($"HKDS charms redesign n={n} natW={natW:F2} scale={scale:F2} fit={fitOut.ortho:F2} detN={detN}");
        }
        catch (Exception e) { Dbg($"HKDS charms redesign err {e.Message}"); }
        return fitOut;
    }
}
