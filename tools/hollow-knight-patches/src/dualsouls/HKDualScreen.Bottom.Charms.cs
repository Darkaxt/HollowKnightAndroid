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
            Vector3 p0 = tmpT.TransformPoint(tb.min);
            Vector3 p1 = tmpT.TransformPoint(new Vector3(tb.min.x, tb.max.y, tb.min.z));
            Vector3 p2 = tmpT.TransformPoint(new Vector3(tb.max.x, tb.min.y, tb.min.z));
            Vector3 p3 = tmpT.TransformPoint(new Vector3(tb.max.x, tb.max.y, tb.min.z));
            wMin = Vector3.Min(Vector3.Min(p0, p1), Vector3.Min(p2, p3));
            wMax = Vector3.Max(Vector3.Max(p0, p1), Vector3.Max(p2, p3));
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
        if (tab.cur == COMP_CHARM)
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
            if(nativeCharmDesc!=null) nativeCharmDesc.ScrollOffset=0;
            { var fr=LayoutCharmsRedesign(pane);if(pane==paneClone) { ApplyFit(fr);paneNeedsFit=true; } }
        }
        catch (Exception e) { Dbg($"HKDS charm detail err {e.Message}"); }
    }

    PaneGraphics nativeCharmGraphics; NativePaneLabel nativeCharmName,nativeCharmDesc;
    Component nativeCharmNameSource,nativeCharmDescSource;
    Transform nativeCharmGrid,equippedCharmNative; Renderer[] nativeCharmGridRenderers;
    SpriteRenderer nativeCharmPortrait; Renderer[] nativeCharmRetired;

    // Canonical Loadout hierarchy with HK's own honeycomb content. The occupied
    // grid alone fits the chooser; selection/prose/legacy JSON never move its target.
    FitResult LayoutCharmsRedesign(GameObject pane)
    {
        if(pane == null || compRoot == null) return default;
        try
        {
            EnsureCharmRefs(pane);
            if(charmBoards.Count == 0) return default;
            var pd=PlayerData.instance;var cs=Charms();
            if(nativeCharmGrid == null || nativeCharmGrid.parent != pane.transform)
            {
                nativeCharmRetired=pane.GetComponentsInChildren<Renderer>(true);
                var root=new GameObject("CanonicalCharmChooser");root.transform.SetParent(pane.transform,false);nativeCharmGrid=root.transform;
                var renderers=new List<Renderer>();
                foreach(var b in charmBoards)
                {
                    b.t.SetParent(nativeCharmGrid,true);
                    renderers.AddRange(b.t.GetComponentsInChildren<Renderer>(true));
                    if(b.icon != null)
                    {
                        b.icon.transform.position=b.t.position-new Vector3(0,0,.02f);
                        b.icon.transform.SetParent(nativeCharmGrid,true);renderers.AddRange(b.icon.GetComponentsInChildren<Renderer>(true));
                    }
                }
                nativeCharmGridRenderers=renderers.ToArray();
                nativeCharmNameSource=TmpOn(charmNameT);nativeCharmDescSource=TmpOn(charmDescT);
                nativeCharmName=CopyPaneLabel(charmNameT,pane.transform,"CanonicalCharmName",37.44f);
                nativeCharmDesc=CopyPaneLabel(charmDescT,pane.transform,"CanonicalCharmDescription",28.08f);
                nativeCharmGraphics=BuildNativePaneGraphics(pane);
                var portrait=FindDeep(pane.transform,"Detail Sprite");nativeCharmPortrait=portrait != null ? portrait.GetComponent<SpriteRenderer>() : null;
                equippedCharmNative=FindDeep(pane.transform,"Equipped Charms");
                notchSprite=null;
                foreach(var sr in pane.GetComponentsInChildren<SpriteRenderer>(true))
                    if(sr != null && sr.gameObject.name=="Sprite Full" && sr.sprite != null) { notchSprite=sr.sprite;break; }
            }
            if(nativeCharmName == null) nativeCharmName=CopyPaneLabel(charmNameT,pane.transform,"CanonicalCharmName",37.44f);
            if(nativeCharmDesc == null) nativeCharmDesc=CopyPaneLabel(charmDescT,pane.transform,"CanonicalCharmDescription",28.08f);
            if(nativeCharmGraphics == null) nativeCharmGraphics=BuildNativePaneGraphics(pane);
            if(nativeCharmName == null || nativeCharmDesc == null || nativeCharmGraphics == null) return default;
            foreach(var b in charmBoards)
            {
                b.t.gameObject.SetActive(true);if(b.br != null) b.br.enabled=true;
                if(b.icon == null) continue;
                bool got=pd == null || b.n<=0 || CharmGot(pd,b.n);b.icon.SetActive(got);
                if(got)
                {
                    float dim=cs.Has(b.n) ? Mathf.Clamp01(cfg.compCharmsDimEquip) : 1;
                    // Cached renderer array is shared by the whole grid; only equip-change/populate reaches here.
                    foreach(var sr in nativeCharmGridRenderers)
                        if(sr is SpriteRenderer sprite && (sprite.transform==b.icon.transform || sprite.transform.IsChildOf(b.icon.transform)))
                        { sprite.enabled=true;sprite.color=new Color(dim,dim,dim,1); }
                }
            }
            var g=LowerGeometry();var p=HKLowerLayout.NativeColumns(g,true);
            // The chooser shares the exact padded column bounds with its masks.
            FitOccupiedNative(nativeCharmGrid,nativeCharmGridRenderers,new Rect(p.ChooserX,p.Top,p.ChooserWidth,p.Height));
            if(nativeCharmRetired!=null) foreach(var r in nativeCharmRetired)
                if(r!=null && r!=nativeCharmPortrait && !r.transform.IsChildOf(nativeCharmGrid) && !IsUnderNamed(r.transform,"Cursor")) r.enabled=false;
            if(charmNameR != null) charmNameR.enabled=false;if(charmDescR != null) charmDescR.enabled=false;
            if(hkNotchRs != null) foreach(var r in hkNotchRs) if(r != null) r.enabled=false;
            if(equippedCharmNative != null) equippedCharmNative.gameObject.SetActive(false);
            PositionNativePaneGraphics(nativeCharmGraphics,true);
            LayoutNativeDetail(nativeCharmName,nativeCharmDesc,nativeCharmNameSource,nativeCharmDescSource,true);
            if(nativeCharmPortrait != null)
            {
                for(var ancestor=nativeCharmPortrait.transform.parent;ancestor!=null && ancestor!=pane.transform;ancestor=ancestor.parent)
                    if(!ancestor.gameObject.activeSelf) ancestor.gameObject.SetActive(true);
                nativeCharmPortrait.enabled=lastDetailCharmN>0 && nativeCharmPortrait.sprite != null;
                FitSprite(nativeCharmPortrait,PanePixel(p.SubjectX+p.SubjectWidth/2,g.BodyCenterY),p.SubjectWidth*.88f,p.Height*.88f);
            }
            int cost=lastDetailCharmN>0 ? Mathf.Max(0,pd != null ? pd.GetInt("charmCost_"+lastDetailCharmN) : 1) : 0;
            if(notchSprite != null && cost>0 && costPipRoot == null)
            {
                var root=new GameObject("HKDS CostPips");root.transform.SetParent(pane.transform,false);costPipRoot=root.transform;
                for(int i=0;i<6;i++) ShellSprite("pip",costPipRoot,notchSprite,100);
            }
            if(costPipRoot != null)
                for(int i=0;i<costPipRoot.childCount;i++)
                {
                    var t=costPipRoot.GetChild(i);t.gameObject.SetActive(i<cost);
                    if(i<cost) FitSprite(t.GetComponent<SpriteRenderer>(),PanePixel(p.DetailX+12+i*26,p.Top+76),20,20);
                }
            return new FitResult { center=compRoot.position,ortho=BOTTOM_H/2f,valid=true };
        }
        catch(Exception e) { Dbg("HKDS canonical charms: "+e.Message);return default; }
    }
}
