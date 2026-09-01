using System;
using System.Collections.Generic;
using UnityEngine;

// [B4] BOTTOM SCREEN — Inventory tab. Clone init, the open-kick / settle / freeze window (PaneSettleTick), the
// equipment-section rebuild from PlayerData (HK's exact order + gates), counters, Focus-cluster lift, and the tapped
// item -> localization key -> name/description resolution.
public partial class HKDualScreen
{
    // Pane clone run-state: kicks = frames left to send a per-instance "INVENTORY OPENED" to the clone's populate FSMs;
    // settle = frames left before we FREEZE the pane's FSMs (once content has populated) — stops their per-frame Update
    // from NRE-ing on the detached clone and leaking FsmExecutionStack. (finalized / focusDY: reserved for the per-clone
    // finalize + live Focus-lift tracking.)
    class PaneRun { public int kicks, settle; public bool finalized; }
    PaneRun invRun = new PaneRun(), charmRun = new PaneRun();   // ONE per cached clone: a tab switch mid-settle no longer cross-wires them (the stowed clone's window pauses and resumes)
    PaneRun RunFor(bool charms) => charms ? charmRun : invRun;

    // PERF: per-pane reference cache. The settle window used to run FULL-TREE scans EVERY frame for ~45 frames per
    // Inventory open (GetComponentsInChildren<PlayMakerFSM> ~100, <Renderer> ~700, <Component> ~3000 with
    // GetType().Name string compares + reflection, FindDeep("Equipment") + 34 Transform.Find in ReassertEquipment) —
    // the "opening Inventory/Charms stutters" the user felt. Everything the settle passes touch is resolved ONCE per
    // clone here (and refreshed once at settle-end in case populate created anything), then iterated as flat arrays.
    class PaneRefs
    {
        public GameObject pane;
        public PlayMakerFSM[] fsms; public PlayMakerFSM bel;                     // all FSMs; "Build Equipment List"
        public Renderer[] renderers;                                             // every renderer under the pane
        public Component[] tk2d; public System.Reflection.PropertyInfo tk2dColor; // tk2dSprite components + their `color` accessor
        public Transform eq; public Transform[] eqItems; public Transform t1, t2, t4;   // Equipment root + EQUIP_ORDER-aligned items + trinket anchors
        public MonoBehaviour[] amountMbs; public Component[][] amountTmps; public Renderer[][] amountRs;   // DisplayItemAmount + their TMPs/renderers
        public string[] amountKeys;                                              // each DisplayItemAmount's PlayerData int name (its `playerDataInt`), for live counter refresh
    }
    PaneRefs refsInv, refsCharm;   // one slot per cached clone, so a tab switch doesn't re-scan (Unity's == is false for a destroyed pane -> rebuilt)
    readonly List<Transform> _eqOrder = new List<Transform>(24);   // reused scratch for ReassertEquipment (no per-call alloc)
    PaneRefs Refs(GameObject pane, bool rebuild = false)
    {
        bool inv = pane == invCloneCache;
        var cur = inv ? refsInv : refsCharm;
        if (!rebuild && cur != null && cur.pane == pane) return cur;
        var r = new PaneRefs { pane = pane };
        r.fsms = pane.GetComponentsInChildren<PlayMakerFSM>(true);
        foreach (var f in r.fsms) if (f != null && f.FsmName == "Build Equipment List") { r.bel = f; break; }
        r.renderers = pane.GetComponentsInChildren<Renderer>(true);
        var tk = new List<Component>(); var am = new List<MonoBehaviour>();
        foreach (var c in pane.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            var tn = c.GetType().Name;
            if (tn == "tk2dSprite") { tk.Add(c); if (r.tk2dColor == null) r.tk2dColor = c.GetType().GetProperty("color"); }
            else if (tn == "DisplayItemAmount" && c is MonoBehaviour mb) am.Add(mb);
        }
        r.tk2d = tk.ToArray();
        r.amountMbs = am.ToArray(); r.amountTmps = new Component[am.Count][]; r.amountRs = new Renderer[am.Count][]; r.amountKeys = new string[am.Count];
        for (int i = 0; i < am.Count; i++)
        {
            try { var kf = am[i].GetType().GetField("playerDataInt"); r.amountKeys[i] = kf != null ? kf.GetValue(am[i]) as string : null; } catch { }
            var tl = new List<Component>(); var rl = new List<Renderer>();
            foreach (var t in am[i].GetComponentsInChildren<Component>(true))
                if (t != null && t.GetType().Name.Contains("TextMeshPro")) { tl.Add(t); rl.Add(t.GetComponent<Renderer>()); }
            r.amountTmps[i] = tl.ToArray(); r.amountRs[i] = rl.ToArray();
        }
        r.eq = FindDeep(pane.transform, "Equipment");
        r.eqItems = new Transform[EQUIP_ORDER.Length];
        if (r.eq != null)
        {
            for (int i = 0; i < EQUIP_ORDER.Length; i++) r.eqItems[i] = r.eq.Find(EQUIP_ORDER[i]);
            r.t1 = r.eq.Find("Trinket1"); r.t2 = r.eq.Find("Trinket2"); r.t4 = r.eq.Find("Trinket4");
        }
        if (inv) refsInv = r; else refsCharm = r;
        return r;
    }


    // Resolve a tapped inventory item -> its INV_*_<suffix> key suffix. Uses the item's component + PlayerData,
    // then validates the candidate against the loaded UI/CP3 sheets so only real keys are used.
    string InvItemKey(Transform item)
    {
        try
        {
            var pd = PlayerData.instance;
            if (item.name == "Heart Pieces")   // mask shards -> INV_NAME_/DESC_HEARTPIECE_<heartPieces 0..3> (verified keys: HEARTPIECE_0..3/ALL/NONE)
            { int hp = pd != null ? pd.GetInt("heartPieces") : 0; return ValidSuffix("HEARTPIECE_" + Mathf.Clamp(hp, 0, 3), "HEARTPIECE_ALL", "HEARTPIECE_NONE"); }
            foreach (var mb in item.GetComponents<MonoBehaviour>())
            {
                var tn = mb.GetType().Name;
                if (tn == "InvItemDisplay")
                {
                    string pdb = mb.GetType().GetField("playerDataBool")?.GetValue(mb) as string;
                    return ResolveInvSuffix(pdb, item.name, pd);
                }
                if (tn == "InvNailSprite") { int up = pd != null ? pd.GetInt("nailSmithUpgrades") : 0; return ValidSuffix("NAIL" + Mathf.Clamp(up + 1, 1, 5)); }
                if (tn == "InvVesselFragments")
                {
                    // FULL (all 3 reserve vessels, MPReserveMax 99) -> SOULORBS_ALL ("Soul Vessels"); otherwise the
                    // fragment count toward the NEXT vessel (vesselFragments 0..2) -> SOULORBS_0/1/2 ("Vessel Fragments").
                    int max = pd != null ? pd.GetInt("MPReserveMax") : 0;
                    if (max >= 99) return ValidSuffix("SOULORBS_ALL", "SOULORBS_2");
                    int frag = pd != null ? pd.GetInt("vesselFragments") : 0;
                    return ValidSuffix("SOULORBS_" + Mathf.Clamp(frag, 0, 2), "SOULORBS_NONE");
                }
                if (tn == "InvRelicBackboard") return ValidSuffix(NameToSuffix(item.name), "TRINKET1");
            }
        }
        catch { }
        return ValidSuffix(NameToSuffix(item.name));
    }

    // playerDataBool (+ item name) -> validated suffix. Explicit overrides for the non-mechanical ones; else strip
    // a "has"/"got" prefix and uppercase, then fall back to the GameObject name — validating each candidate.
    string ResolveInvSuffix(string pdb, string goName, PlayerData pd)
    {
        pdb = pdb ?? "";
        switch (pdb)
        {
            case "hasDashSlash": return ValidSuffix("ART_DASH");
            case "hasUpwardSlash": return ValidSuffix("ART_UPPER");
            case "hasCyclone": return ValidSuffix("ART_CYCLONE");
            case "hasDreamGate": return ValidSuffix("DREAMGATE");
            case "hasDreamNail": case "dreamNail": return ValidSuffix(pd != null && pd.GetBool("dreamNailUpgraded") ? "DREAMNAIL_B" : "DREAMNAIL_A", "DREAMNAIL_A");
            case "dreamNailUpgraded": return ValidSuffix("DREAMNAIL_B", "DREAMNAIL_A");
            case "xunFlowerBroken": return ValidSuffix(pd != null && pd.GetBool("xunFlowerBroken") ? "FLOWER_BROKEN" : "FLOWER", "FLOWER");
            case "hasKingsBrand": return ValidSuffix("KINGSBRAND");
            case "hasCityKey": return ValidSuffix("CITYKEY");
            case "hasWhiteKey": return ValidSuffix("WHITEKEY");
            case "hasLoveKey": return ValidSuffix("LOVEKEY");
            case "hasSlykey": case "gaveSlykey": return ValidSuffix("STOREKEY");
            case "hasTramPass": return ValidSuffix("TRAM_PASS");
            case "hasQuill": return ValidSuffix("QUILL", "MAPQUILL");
            case "hasSuperDash": return ValidSuffix("SUPERDASH");
            case "hasShadowDash": return ValidSuffix("SHADOWDASH");
            case "hasAcidArmour": return ValidSuffix("ACIDARMOUR");
        }
        // mechanical: hasDash->DASH, hasWalljump->WALLJUMP, hasDoubleJump->DOUBLEJUMP, hasLantern->LANTERN...
        string mech = pdb.StartsWith("has") ? pdb.Substring(3) : (pdb.StartsWith("got") ? pdb.Substring(3) : pdb);
        return ValidSuffix(mech.ToUpperInvariant(), NameToSuffix(goName));
    }

    static string NameToSuffix(string n) => (n ?? "").ToUpperInvariant().Replace(" ", "").Replace("_", "");

    // Return the first candidate whose INV_NAME_<cand> exists in a loaded sheet; "" if none.
    string ValidSuffix(params string[] cands)
    {
        foreach (var c in cands)
        {
            if (string.IsNullOrEmpty(c)) continue;
            try { foreach (var sh in TeamCherry.Localization.Language.GetSheets()) if (TeamCherry.Localization.Language.Has("INV_NAME_" + c, sh)) return c; } catch { }
        }
        return "";
    }

    // Fill the Inventory right column (same "Text Name"/"Text Desc" TMPs as charms) from the selected item's convos.
    void PopulateInvDetail(GameObject pane)
    {
        try
        {
            string suf = sel.invKey;
            string name = !string.IsNullOrEmpty(suf) ? CharmString("INV_NAME_" + suf) : "";
            string desc = !string.IsNullOrEmpty(suf) ? CharmString("INV_DESC_" + suf) : "";
            if (string.IsNullOrEmpty(name) && sel.item != null) name = sel.item.name;   // fallback to the GO name
            SetTmpTextByName(pane.transform, "Text Name", name);
            SetTmpTextByName(pane.transform, "Text Desc", desc);
            SetTmpTextByName(pane.transform, "Text Desc Low", "");
            SetDetailFont(pane);
        }
        catch (Exception e) { Dbg($"HKDS inv detail err {e.Message}"); }
    }

    // A tapped spell ('Spell Fireball/Focus/Quake/Scream') has no Inv* component — resolve its name/desc from HK's
    // inventory sheet by candidate key (base + upgrade), falling back to the readable name.
    void PopulateSpellDetail(GameObject pane, string goName)
    {
        try
        {
            string disp = goName.StartsWith("Spell ") ? goName.Substring(6) : goName;
            var pds = PlayerData.instance;
            string[] cand;
            // REAL localization keys (verified via HKUIKEY dump): SPELL_FIREBALL1/2, SPELL_QUAKE1/2, SPELL_SCREAM1/2,
            // SPELL_FOCUS. Level 2 = the EVOLVED form (Shade Soul / Descending Dark / Abyss Shriek) -> its key is the
            // "...2" variant; base (Vengeful Spirit / Desolate Dive / Howling Wraiths) is "...1".
            switch (disp)
            {
                case "Fireball": { int lv = pds != null ? pds.GetInt("fireballLevel") : 1; cand = lv >= 2 ? new[] { "SPELL_FIREBALL2", "SPELL_FIREBALL1" } : new[] { "SPELL_FIREBALL1" }; break; }
                case "Focus":    cand = new[] { "SPELL_FOCUS" }; break;
                case "Quake":    { int lv = pds != null ? pds.GetInt("quakeLevel") : 1;    cand = lv >= 2 ? new[] { "SPELL_QUAKE2", "SPELL_QUAKE1" } : new[] { "SPELL_QUAKE1" }; break; }
                case "Scream":   { int lv = pds != null ? pds.GetInt("screamLevel") : 1;   cand = lv >= 2 ? new[] { "SPELL_SCREAM2", "SPELL_SCREAM1" } : new[] { "SPELL_SCREAM1" }; break; }
                default:         cand = new string[0]; break;
            }
            string name = disp, desc = "";
            foreach (var c in cand) { var n = CharmString("INV_NAME_" + c); if (!string.IsNullOrEmpty(n) && n != "INV_NAME_" + c) { name = n; desc = CharmString("INV_DESC_" + c); break; } }
            SetTmpTextByName(pane.transform, "Text Name", name);
            SetTmpTextByName(pane.transform, "Text Desc", desc);
            SetTmpTextByName(pane.transform, "Text Desc Low", "");
            SetDetailFont(pane);
            Dbg($"HKDS spell detail '{disp}' -> '{name}'");
        }
        catch (Exception e) { Dbg($"HKDS spell detail err {e.Message}"); }
    }

    // An Equipment-section item ('Map and Quill', abilities, keys) — resolve HK's name/desc by candidate key
    // (explicit for the common ones, then generic from the GO name), falling back to the readable name.
    void PopulateEquipDetail(GameObject pane, string goName)
    {
        try
        {
            // REAL localization keys (verified via HKUIKEY dump) — single tokens, no underscores except TRAM_PASS.
            var pdq = PlayerData.instance;
            var cand = new System.Collections.Generic.List<string>();
            switch (goName)
            {
                case "Map and Quill": cand.AddRange(new[] { "MAPQUILL", "MAP", "QUILL" }); break;
                case "Dash Cloak":    cand.AddRange(pdq != null && pdq.GetBool("hasShadowDash") ? new[] { "SHADOWDASH", "DASH" } : new[] { "DASH" }); break;   // mothwing (DASH) -> shade cloak (SHADOWDASH)
                case "Mantis Claw":   cand.Add("WALLJUMP"); break;
                case "Super Dash":    cand.Add("SUPERDASH"); break;      // Crystal Heart
                case "Double Jump":   cand.Add("DOUBLEJUMP"); break;     // Monarch Wings
                case "Acid Armour":   cand.Add("ACIDARMOUR"); break;     // Isma's Tear
                case "Lantern":       cand.Add("LANTERN"); break;
                case "Ore":           cand.Add("ORE"); break;            // Pale Ore
                case "Rancid Egg":    cand.Add("RANCIDEGG"); break;
                case "Simple Key":    cand.Add("SIMPLEKEY"); break;
                case "Store Key":     cand.Add("STOREKEY"); break;       // Sly's / Shopkeeper's Key
                case "White Key":     cand.Add("WHITEKEY"); break;       // Elegant Key
                case "City Key":      cand.Add("CITYKEY"); break;
                case "Love Key":      cand.Add("LOVEKEY"); break;
                case "Waterway Key":  cand.AddRange(new[] { "SIMPLEKEY" }); break;   // no dedicated key; falls back below
                case "Tram Pass":     cand.Add("TRAM_PASS"); break;
                case "Kings Brand":   cand.Add("KINGSBRAND"); break;
                case "Xun Flower":    cand.Add(pdq != null && pdq.GetBool("xunFlowerBroken") ? "FLOWER_BROKEN" : "FLOWER"); break;
            }
            string up = goName.ToUpperInvariant();
            cand.Add(up.Replace(" AND ", "_").Replace(" ", "_"));
            cand.Add(up.Replace(" ", ""));
            string name = goName, desc = "";
            foreach (var c in cand) { var n = CharmString("INV_NAME_" + c); if (!string.IsNullOrEmpty(n) && n != "INV_NAME_" + c) { name = n; desc = CharmString("INV_DESC_" + c); break; } }
            SetTmpTextByName(pane.transform, "Text Name", name);
            SetTmpTextByName(pane.transform, "Text Desc", desc);
            SetTmpTextByName(pane.transform, "Text Desc Low", "");
            SetDetailFont(pane);
            Dbg($"HKDS equip detail '{goName}' -> '{name}'");
        }
        catch (Exception e) { Dbg($"HKDS equip detail err {e.Message}"); }
    }

    // Geo is a counter (no Inv* item), but the user wants it tappable. Show its name + the live amount.
    // God Tuner ("Godfinder" under Inv_Items, present on Godhome-progressed saves): name/desc from the CP3 sheet
    // (CharmString searches every sheet, so the INV_NAME/DESC_GODFINDER keys resolve).
    void PopulateGodfinderDetail(GameObject pane)
    {
        try
        {
            string name = CharmString("INV_NAME_GODFINDER");
            if (string.IsNullOrEmpty(name) || name == "INV_NAME_GODFINDER") name = "God Tuner";
            string desc = CharmString("INV_DESC_GODFINDER");
            if (string.IsNullOrEmpty(desc) || desc == "INV_DESC_GODFINDER") desc = "A tool for tuning in to the echoes of powerful beings, used by the Godseeker.";
            SetTmpTextByName(pane.transform, "Text Name", name);
            SetTmpTextByName(pane.transform, "Text Desc", desc);
            SetTmpTextByName(pane.transform, "Text Desc Low", "");
            SetDetailFont(pane);
        }
        catch (Exception e) { Dbg($"HKDS godfinder detail err {e.Message}"); }
    }

    void PopulateGeoDetail(GameObject pane)
    {
        try
        {
            string name = CharmString("INV_NAME_GEO");
            if (string.IsNullOrEmpty(name) || name == "INV_NAME_GEO") name = "Geo";
            string desc = CharmString("INV_DESC_GEO");
            if (string.IsNullOrEmpty(desc) || desc == "INV_DESC_GEO")
            {
                var pd = PlayerData.instance; int geo = pd != null ? pd.GetInt("geo") : 0;
                desc = "The currency of Hallownest, gathered from fallen foes and hidden caches. You hold " + geo + " Geo.";
            }
            SetTmpTextByName(pane.transform, "Text Name", name);
            SetTmpTextByName(pane.transform, "Text Desc", desc);
            SetTmpTextByName(pane.transform, "Text Desc Low", "");
            SetDetailFont(pane);
            Dbg($"HKDS geo detail -> '{name}'");
        }
        catch (Exception e) { Dbg($"HKDS geo detail err {e.Message}"); }
    }

    // No item tapped yet (e.g. just switched Map -> Inventory): the right detail must be EMPTY, not HK's tiny
    // native default nor a stale detail carried over from another tab. Blank the TMPs + hide the control prompt.
    void ClearInvDetail(GameObject pane)
    {
        sel.item = null; sel.kind = -1; sel.charmN = 0; sel.invKey = null;
        ClearInvDetailLocal(pane);
    }

    // Pane-local blank only — does NOT touch the global selection or the control prompt overlay. Used by the
    // BACKGROUND (pre-warm) finalize: clearing `sel` from there wiped a live selection made on ANOTHER tab.
    void ClearInvDetailLocal(GameObject pane)
    {
        try
        {
            SetTmpTextByName(pane.transform, "Text Name", "");
            SetTmpTextByName(pane.transform, "Text Desc", "");
            SetTmpTextByName(pane.transform, "Text Desc Low", "");
            HideControlPrompt(pane);
        }
        catch (Exception e) { Dbg($"HKDS clear-detail err {e.Message}"); }
    }

    // [B4] Pane clone open-kicks + settle/freeze window (both INV and Charms clones flow through here; charms only
    // use the settle-end detail re-assert). Order inside the settle window MUST stay: DrainFsmStack -> BEL Init kick
    // -> [INV] ReassertEquipment -> renderers OFF (anti-blip); at settle end: freeze all FSMs -> [charms]
    // PopulateCharmDetail | [INV] ReassertEquipment -> ForcePaneOpaque -> ForceAmountCounters -> reset+NudgeFocusCluster
    // -> ClearInvDetail|RefreshSelectedDetail -> paneNeedsFit.
    // Pane clone open-kicks + settle/freeze window. Generalized to ANY pane clone (current tab OR a pre-warming one):
    //   pane = the clone, run = ITS run-state, inv = Inventory (else Charms), isCurrent = it's the shown tab (drives
    //   the selection/detail refresh + refit; a warming clone is finalized then STOWED instead).
    // Order inside the settle window MUST stay: DrainFsmStack -> BEL Init kick -> [INV] ReassertEquipment ->
    // renderers OFF (anti-blip); at settle end: freeze all FSMs -> [charms] PopulateCharmDetail | [INV] FinalizeInvPane
    // -> paneNeedsFit (current only).
    void PaneSettleTick(GameObject pane, PaneRun run, bool inv, bool isCurrent)
    {
        if (pane == null || run == null || !pane.activeSelf) return;   // a stowed (inactive) clone can't tick its FSMs -> its window pauses until shown
        // Kick the populate FSMs with a per-instance "INVENTORY OPENED" for a few frames after build
        // (their Start() lands the frame after SetActive). Delivered per-fsm so it never reaches the real
        // inventory. Skip the nav drivers ("UI Charms"/"UI Inventory") so they stay idle, not navigating.
        if (run.kicks > 0)
        {
            run.kicks--;
            var R = Refs(pane);
            foreach (var fsm in R.fsms)
            {
                if (fsm == null || !fsm.enabled) continue;
                if (fsm.FsmName == "UI Charms" || fsm.FsmName == "UI Inventory") continue;
                try { fsm.SendEvent("INVENTORY OPENED"); } catch { }
                // "Build Equipment List" waits at its Init state for FINISHED (NOT "INVENTORY OPENED") to cascade
                // through Dash->Walljump->...->Ore->Rancid Egg->Simple Key, activating each owned item. Without
                // this it stays at Init and only the trinket row builds -> abilities/keys/CONSUMABLE COUNTERS
                // never appear. Kick it explicitly out of Init (its verified event set: FINISHED/CHECK/...).
                if (fsm.FsmName == "Build Equipment List")
                { try { if (fsm.Fsm != null && fsm.Fsm.ActiveStateName == "Init") fsm.SendEvent("FINISHED"); } catch { } }
            }
            if (inv) ForcePaneOpaque(pane);   // items populate faded-out (source is "closed") -> force opaque each kick frame so the grid is visible during gameplay, not only when HK's real inventory is paused-open
            if (isCurrent) paneNeedsFit = true;   // content shifts as icons (de)activate -> refit while it settles
        }
        // Once the content has populated, FREEZE every pane FSM so its per-frame Update stops NRE-ing on
        // the detached clone (the leak SOURCE + per-switch CPU). Freezing halts an FSM's Update; it does
        // NOT revert the icons it already activated, so the populated content stays put. Until then, drain
        // each frame so the populate frames' stranded Fsm graphs can't accumulate.
        if (run.settle > 0)
        {
            DrainFsmStack();
            // Keep kicking "Build Equipment List" out of Init for the WHOLE settle window, not just the 8 open-kick
            // frames: its Start() lands a variable number of frames after SetActive, so on a cached one-time build
            // the open-kicks can all fire BEFORE it reaches Init (ActiveStateName still null) and the equipment
            // row never builds ("all middle items missing"). Firing every settle frame catches Init whenever it lands.
            var RS = Refs(pane);
            if (inv && RS.bel != null && RS.bel.enabled && RS.bel.Fsm != null)
            {
                string asn = null; try { asn = RS.bel.Fsm.ActiveStateName; } catch { }
                if (asn == "Init") { try { RS.bel.SendEvent("FINISHED"); } catch { } }
            }
            // ANTI-BLIP (INV): keep the pane's content INVISIBLE for the whole settle window (renderers off each
            // frame) while the FSMs populate + we lay out the equipment grid, then the settle-end pass below reveals
            // everything at once, fully laid out (Focus lifted, grid placed, counters on). No intermediate frames
            // (stacked blob / un-lifted Focus / late counters) are ever visible. Layout passes still run per frame
            // so positions are settled by reveal time. (Focus nudge is deliberately AFTER the freeze: the 3 orbiting
            // spells' own FSMs re-seat them around Focus every frame while running.)
            if (inv)
            {
                ReassertEquipment(pane);
                foreach (var r in RS.renderers) if (r != null && r.enabled) r.enabled = false;
            }
            if (--run.settle == 0)
            {
                RS = Refs(pane, rebuild: true);   // one refresh at settle-end: pick up anything the populate FSMs created
                foreach (var fsm in RS.fsms)
                    if (fsm != null) fsm.enabled = false;
                // Re-assert the detail AFTER the FSMs freeze — idling FSMs during settle can blank Text Name/Desc;
                // now that they're frozen our text sticks.
                if (!inv) { if (isCurrent) PopulateCharmDetail(pane); }   // (a warming charms clone is parked: its layout would place boards at compRoot -> re-populated on first show instead)
                else FinalizeInvPane(pane, isCurrent);
                run.finalized = true;
                if (isCurrent) paneNeedsFit = true;   // fix2: one clean FINAL fit now that content is fully populated + detail cleared, then lock
                else pane.SetActive(false);           // pre-warmed clone: fully built + laid out -> stow until its tab is shown (instant show, no settle)
            }
        }
    }

    // The INV settle-end pass, in the ONLY order that is correct: re-activate owned abilities/keys/consumables from
    // PlayerData (the FSM race deactivates them) -> enable + un-fade every renderer -> counters on -> Focus lift ->
    // detail. Kept as one method so every path (settle end, pre-warm, cache re-show) applies the same sequence.
    void FinalizeInvPane(GameObject pane, bool isCurrent)
    {
        ReassertEquipment(pane);                             // FIRST: must precede the opaque/counter passes below
        ForcePaneOpaque(pane);                               // FINAL opaque pass after FSMs freeze -> grid alpha can no longer be re-zeroed by a stray fade FSM
        ForceAmountCounters(pane);                           // relic/ore/egg/key COUNT numbers on (TMP mesh rebuilt + DisplayItemAmount frozen) now that equipment has built + text is set
        RefreshInvCounters(pane);                            // ...and current values (DisplayItemAmount is frozen from here on)
        nudgedFocusFor = null; nudgedFocusAmt = 0f;          // the spell FSMs re-seated the 3 orbiters during settle -> re-apply the FULL lift now that FSMs are frozen (positions stick)
        if (mainFocusBase == null) NudgeFocusCluster(pane);  // lift Focus + its 3 spells — ONLY when the main-screen vars-lift is off: the clone inherits the lifted Up Y/Down Y via Instantiate (its Check Active already placed it lifted), so nudging again would double-lift
        // Shown tab: clear only when nothing is selected; re-assert an in-settle tap. BACKGROUND (pre-warm)
        // finalize must NOT go through ClearInvDetail — that clears the GLOBAL selection + control prompt,
        // which wiped a live Charms-tab selection whenever a stale INV clone rebuilt during a room transition.
        if (!isCurrent) ClearInvDetailLocal(pane);
        else if (sel.item == null) ClearInvDetail(pane);     // no tap yet -> empty detail (not HK's native default / stale)
        else RefreshSelectedDetail(pane);                    // a tap landed during settle -> re-assert by kind
    }

    // [B4] Inventory pane fit: percentile fit over the real renderers (drops the sparse far tail), then fold in both
    // inner dividers + the two authored content roots (Inv_Items, Equipment) so the full 3-section inventory frames.
    // Returns false when there is nothing to fit (caller returns false).
    bool InvPaneBounds(GameObject go, ref Bounds b, ref bool have, ref int kept, ref int total)
    {
        // INV: percentile fit over the real renderers (drops the sparse far tail: cursor/tweener/strays),
        // but keep 92% (was 80) so the right detail cluster survives. INCLUDE TMP this time.
        var rends = go.GetComponentsInChildren<Renderer>();
        var rbs = new List<Bounds>(); Vector3 cen = Vector3.zero;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null || !rends[i].enabled) continue;
            var rn = rends[i].gameObject.name;   // exclude the right-detail TMPs so a tap-populate can't move the fit
            if (rn == "Text Name" || rn == "Text Desc" || rn == "Text Desc Low" || rn == "Text Completion" || rn == "Percentage") continue;
            if (IsUnderNamed(rends[i].transform, "Item Control")) continue;   // control-prompt glyph/verb is an overlay — never let it stretch the content fit
            var rb = rends[i].bounds;
            if (float.IsNaN(rb.center.x) || float.IsInfinity(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue;
            if (rb.size.x > 200f || rb.size.y > 200f) continue;
            rbs.Add(rb); cen += rb.center;
        }
        total = rbs.Count;
        if (rbs.Count == 0) return false;
        cen /= rbs.Count;
        var d = new List<float>(); foreach (var rb in rbs) d.Add((rb.center - cen).magnitude);
        var ds = new List<float>(d); ds.Sort();
        float thresh = Mathf.Max(1f, ds[Mathf.Clamp((int)(ds.Count * 0.92f), 0, ds.Count - 1)]);
        for (int i = 0; i < rbs.Count; i++)
        {
            if (d[i] > thresh) continue;
            kept++;
            if (!have) { b = rbs[i]; have = true; } else b.Encapsulate(rbs[i]);
        }
        if (have)
        {
            // INV: fold in BOTH inner dividers so all 3 sections' separators show, but DON'T extend into the
            // EMPTY description column (no cursor selection => no glyphs): including it dragged the fit centre
            // right and left the two icon grids off-centre. Centre on the grids + dividers instead.
            foreach (var nm in new[] { "Divider L", "Divider R" })
            {
                var dt = FindDeep(go.transform, nm); var dr = dt != null ? dt.GetComponent<Renderer>() : null;
                if (dr != null && dr.enabled && dr.bounds.size.sqrMagnitude > 1e-8f && dr.bounds.size.x < 50f && dr.bounds.size.y < 50f)
                    b.Encapsulate(dr.bounds);
            }
            // Once the Equipment section populates, the percentile centre shifts RIGHT and drops the far-LEFT
            // self/soul portrait as an outlier -> "zoomed in, left cut off". Explicitly fold in the two authored
            // content roots' ACTIVE renderer bounds so the full 3-section inventory always frames (no re-zoom).
            foreach (var nm in new[] { "Inv_Items", "Equipment" })
            {
                var ct = FindDeep(go.transform, nm); if (ct == null) continue;
                foreach (var r in ct.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    var rb = r.bounds;
                    if (rb.size.sqrMagnitude < 1e-8f || rb.size.x > 30f || rb.size.y > 30f) continue;
                    b.Encapsulate(rb);
                }
            }
        }
        return true;
    }

    // [B4] INV clone init. Soul VESSEL (InvVesselFragments): its self/piece1/piece2/full sprite renderers are left
    // DISABLED in the detached clone, so the reserve-vessel graphic vanishes (empty showed only the dark backboard).
    // OnEnable already chose the right sprites (full / pieces / backboard); just force those four renderers ON.
    void InvPaneInit(GameObject pane)
    {
        foreach (var mb in pane.GetComponentsInChildren<Component>(true))
        {
            if (mb == null || mb.GetType().Name != "InvVesselFragments") continue;
            foreach (var fn in new[] { "self", "piece1", "piece2", "full" })
            { try { var fi = mb.GetType().GetField(fn); var sr = fi != null ? fi.GetValue(mb) as SpriteRenderer : null; if (sr != null) sr.enabled = true; } catch { } }
        }
    }

    // The detached Inv pane inherits HK's inventory-CLOSED state: the equipment item renderers are BOTH DISABLED
    // (renderer.enabled=false — how HK hides the pane) AND faded (vertex-color alpha ~0). The item GameObjects stay
    // ACTIVE and correctly positioned (the diag shows distinct bounds), but nothing draws. HK only re-enables + fades
    // the pane in when the REAL inventory opens, which never happens for our clone (we freeze its FSMs). So we must
    // re-ENABLE every renderer on an ACTIVE GameObject and force it opaque — alpha alone is useless on a disabled
    // renderer (that was the bug: a fresh rebuild cloned the CLOSED source -> 75 renderers disabled -> only the
    // handful already-enabled, e.g. the soul vessel, showed). The activeInHierarchy guard is the safety: un-obtained
    // items are SetActive(false) (inactive) -> skipped -> they stay hidden. INV-ONLY: the charms pane has hidden
    // per-charm cost pips that must NOT be force-enabled (LayoutCharmsRedesign handles charms separately).
    void ForcePaneOpaque(GameObject pane)
    {
        if (pane == null) return;
        int fixedN = 0;
        // Enable EVERY renderer on an ACTIVE GameObject — not just SpriteRenderer/tk2dSprite icons but also the
        // MeshRenderers behind TMP COUNTERS (relic/egg/ore/key amounts) which HK's inventory-CLOSED state disables
        // too (that's why "all counted items go missing"). Force sprite alphas opaque. activeInHierarchy guard keeps
        // un-obtained (deactivated) items hidden. INV-only (charms handled by LayoutCharmsRedesign).
        var R = Refs(pane);
        foreach (var r in R.renderers)
        {
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            bool ch = false;
            if (!r.enabled) { r.enabled = true; ch = true; }
            if (r is SpriteRenderer sr) { var c = sr.color; if (c.a < 0.999f) { c.a = 1f; sr.color = c; ch = true; } }
            if (ch) fixedN++;
        }
        // tk2dSprite carries its opacity in the VERTEX color (separate from renderer.enabled, handled above).
        var pc = R.tk2dColor;
        if (pc != null)
            foreach (var mb in R.tk2d)
            {
                if (mb == null || !mb.gameObject.activeInHierarchy) continue;
                try { var c = (Color)pc.GetValue(mb, null); if (c.a < 0.999f) { c.a = 1f; pc.SetValue(mb, c, null); } } catch { }
            }
        if (cfg.debug == 1 && fixedN > 0) Dbg($"HKDS ForcePaneOpaque enabled/opaqued {fixedN} renderers");
    }

    // The relic/consumable AMOUNT numbers (Trinket/Ore/Egg/Simple-Key counts) are TMP MeshRenderers driven by
    // DisplayItemAmount, which sets the text but leaves the MeshRenderer DISABLED in the detached clone (it gates
    // display on the live inventory being open, which never happens for our clone) — so the counts never show.
    // Force each amount's TMP mesh to rebuild + its MeshRenderer on, then FREEZE the DisplayItemAmount so it can't
    // re-disable the renderer on its next Update.
    void ForceAmountCounters(GameObject pane)
    {
        if (pane == null) return;
        int n = 0;
        var R = Refs(pane);
        for (int i = 0; i < R.amountMbs.Length; i++)
        {
            var mb = R.amountMbs[i];
            if (mb == null || !mb.gameObject.activeInHierarchy) continue;
            try
            {
                var tmps = R.amountTmps[i]; var rs = R.amountRs[i];
                for (int k = 0; k < tmps.Length; k++)
                {
                    var tmp = tmps[k]; if (tmp == null) continue;
                    try { tmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(tmp, null); } catch { }
                    var mr = rs[k]; if (mr != null && !mr.enabled) { mr.enabled = true; n++; }
                }
                mb.enabled = false;   // freeze: stop it re-disabling the renderer
            }
            catch { }
        }
        if (cfg.debug == 1 && n > 0) Dbg($"HKDS ForceAmountCounters enabled {n} amount renderers");
    }

    // Live counter refresh: DisplayItemAmount is FROZEN on the clone (ForceAmountCounters), so its numbers (geo, relics,
    // ore, eggs, simple keys) would go stale — the old answer was to put geo etc. in the rebuild fingerprint, which
    // re-DEEP-CLONED the whole pane on the next open after every geo pickup (a hitch each time). Instead: re-read each
    // amount's PlayerData int and set the TMP text directly (cheap; ~8 labels) at finalize, on re-show, and ~2x/s
    // while the tab is up.
    void RefreshInvCounters(GameObject pane)
    {
        if (pane == null) return;
        try
        {
            var pd = PlayerData.instance; if (pd == null) return;
            var R = Refs(pane);
            for (int i = 0; i < R.amountMbs.Length; i++)
            {
                var key = R.amountKeys[i]; if (string.IsNullOrEmpty(key)) continue;
                var mb = R.amountMbs[i]; if (mb == null || !mb.gameObject.activeInHierarchy) continue;
                string txt; try { txt = pd.GetInt(key).ToString(); } catch { continue; }
                foreach (var tmp in R.amountTmps[i])
                {
                    if (tmp == null) continue;
                    try
                    {
                        var tp = tmp.GetType().GetProperty("text"); if (tp == null) continue;
                        if ((tp.GetValue(tmp, null) as string) == txt) continue;   // unchanged -> no mesh rebuild
                        tp.SetValue(tmp, txt, null);
                        tmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(tmp, null);
                    }
                    catch { }
                }
            }
        }
        catch (Exception e) { Dbg($"HKDS counters err {e.Message}"); }
    }

    // "Build Equipment List" DOES run to All Done on the detached clone (verified: Init->Dash->...->All Done on the
    // first settle frame), activating + positioning each owned ability/key/consumable from PlayerData. But the
    // detached clone's other populate FSMs (Check Active etc.) then respond to our "INVENTORY OPENED" open-kicks by
    // DEACTIVATING those items again (HK's inventory-closed behaviour) — a per-build race the cache-off path happened
    // to win and the cached one-time build loses -> the whole middle Equipment section renders empty ("all middle
    // items in top-left"). Fix: after the FSMs FREEZE, deterministically re-assert each Equipment item's active
    // state straight from PlayerData (the same gates the FSM uses), so no FSM can undo it. Positions were already
    // laid out by the cascade (SetPosition per state) and persist across SetActive.
    // HK's EXACT equipment display order + per-item visibility, transcribed from the "Build Equipment List" FSM's
    // FINISHED-> chain and each state's PlayerData gate (verified via HKEQFSM dump). Order = the main screen's order.
    // Compound gates matter: the Elegant (White) Key is shown only while owned AND NOT YET USED (usedWhiteKey), which
    // is why a bare hasWhiteKey check showed it on the bottom screen when the top had already hidden it.
    static readonly string[] EQUIP_ORDER = {
        "Dash Cloak", "Mantis Claw", "Super Dash", "Double Jump", "Acid Armour", "Lantern", "Map and Quill",
        "Kings Brand", "Tram Pass", "City Key", "Store Key", "White Key", "Love Key", "Xun Flower",
        "Simple Key", "Ore", "Rancid Egg",
    };

    static bool EquipShown(PlayerData pd, string item)
    {
        bool B(string k) { try { return pd.GetBool(k); } catch { return false; } }
        int I(string k) { try { return pd.GetInt(k); } catch { return 0; } }
        switch (item)
        {
            case "Dash Cloak":    return B("hasDash") || B("hasShadowDash");
            case "Mantis Claw":   return B("hasWalljump");
            case "Super Dash":    return B("hasSuperDash");
            case "Double Jump":   return B("hasDoubleJump");
            case "Acid Armour":   return B("hasAcidArmour");
            case "Lantern":       return B("hasLantern");
            case "Map and Quill": return B("hasMap") || B("hasQuill");
            case "Kings Brand":   return B("hasKingsBrand");
            case "Tram Pass":     return B("hasTramPass");
            case "City Key":      return B("hasCityKey");
            case "Store Key":     return B("hasSlykey");
            case "White Key":     return !B("hasSlykey") && B("hasWhiteKey") && !B("usedWhiteKey");   // Elegant Key: HK's exact FSM branches — hasSlykey T->skip (given to Sly), hasWhiteKey F->skip, usedWhiteKey T->skip
            case "Love Key":      return B("hasLoveKey");
            case "Xun Flower":    return B("hasXunFlower");
            case "Simple Key":    return I("simpleKeys") > 0;
            case "Ore":           return I("ore") > 0;
            case "Rancid Egg":    return I("rancidEggs") > 0;
        }
        return false;
    }

    void ReassertEquipment(GameObject pane)
    {
        if (pane == null) return;
        try
        {
            var pd = PlayerData.instance; if (pd == null) return;
            var R = Refs(pane);
            var eq = R.eq; if (eq == null) return;
            int on = 0, off = 0;
            // AUTHORITATIVE per-item visibility from PlayerData (show owned, HIDE what HK hides — e.g. used Elegant Key).
            for (int i = 0; i < EQUIP_ORDER.Length; i++)
            {
                var t = R.eqItems[i]; if (t == null) continue;
                bool want = EquipShown(pd, EQUIP_ORDER[i]);
                if (want && !t.gameObject.activeSelf) { t.gameObject.SetActive(true); on++; }
                else if (!want && t.gameObject.activeSelf) { t.gameObject.SetActive(false); off++; }
            }
            // COMPACT 4-COLUMN GRID AT HK'S NATIVE ANCHOR (user, round 2). The items are all authored STACKED at
            // local (1.1,-3.8) — the top-centre spot where the main screen's row STARTS — and HK's Build Equipment
            // List (which never completes on the detached clone) walks the owned ones right from there. Mirroring the
            // real pane's positions (v0.1.100) reproduced that WIDE row, which overflowed the bottom pane's divider
            // on a full save ("8x2") — so instead: anchor at the SAME native spot (single item = exactly the main
            // screen's position) but wrap at 4 COLUMNS, growing DOWN with the trinket-row pitch and compressing the
            // row pitch when needed so the block always ends ABOVE the trinket row. compEquipGridDY trims (+down).
            var order = _eqOrder; order.Clear();
            for (int i = 0; i < R.eqItems.Length; i++) { var t = R.eqItems[i]; if (t != null && t.gameObject.activeSelf) order.Add(t); }
            bool stacked = false;
            if (order.Count >= 2) { var p0 = order[0].position; int same = 0; foreach (var t in order) if ((t.position - p0).sqrMagnitude < 0.05f) same++; stacked = same >= order.Count - 1; }
            if (order.Count > 0 && (stacked || on > 0 || off > 0 || equipGridDirty))
            {
                const float anchorX = 1.1f, anchorY = -3.8f;          // authored stack = HK's row start (pane-local)
                float pitchX = 2.08f, trinketY = -10.74f;             // defaults = authored trinket row
                var tr1 = R.t1; var tr2 = R.t2;
                if (tr1 != null && tr2 != null) { pitchX = Mathf.Abs(tr2.localPosition.x - tr1.localPosition.x); trinketY = tr1.localPosition.y; }
                int cols = 4, rows = Mathf.CeilToInt(order.Count / (float)cols);
                float availY = anchorY - (trinketY + 1.3f);           // vertical span above the trinket row (icon-height margin)
                float pitchY = rows > 1 ? Mathf.Min(pitchX * 0.95f, Mathf.Max(0.8f, availY / (rows - 1))) : 0f;
                for (int i = 0; i < order.Count; i++)
                {
                    int c = i % cols, r = i / cols;
                    order[i].localPosition = new Vector3(anchorX + c * pitchX, anchorY - r * pitchY - cfg.compEquipGridDY, order[i].localPosition.z);
                }
                equipGridDirty = false;
                if (cfg.debug == 1) Dbg($"HKDS ReassertEquipment {order.Count} items in {rows}x{cols} grid at native anchor (pitchX {pitchX:F2}, pitchY {pitchY:F2}, dy {cfg.compEquipGridDY:F2})");
            }
            // (quiet when nothing changed: this runs every settle frame)
            if (cfg.debug == 1 && (on > 0 || off > 0)) Dbg($"HKDS ReassertEquipment shown={on} hidden={off}");
        }
        catch (Exception e) { Dbg($"HKDS equip reassert err {e.Message}"); }
    }

    // Focus + its 3 orbiting spells (Fireball/Quake/Scream) sit so low that the bottom spell (Scream) all but touches
    // the nail-arts row beneath (Art Dash/Uppercut/Cyclone) — ~0.5u gap, visually clipped. Lift the whole cluster by
    // compFocusDY (world units) once per clone (idempotent via nudgedFocusFor) so a refit can't re-shift it.
    bool equipGridDirty;   // set on config hot-reload -> ReassertEquipment re-lays the grid once (live tuning of compEquipGridDY)

    GameObject nudgedFocusFor; float nudgedFocusAmt;   // clone we lifted + how much (so a config change applies the DELTA live)

    void NudgeFocusCluster(GameObject pane)
    {
        if (pane == null) return;
        float want = EffectiveFocusDY(), applied = nudgedFocusFor == pane ? nudgedFocusAmt : 0f;
        float delta = want - applied; if (Mathf.Abs(delta) < 1e-4f) return;   // already at the wanted lift for this clone
        nudgedFocusFor = pane; nudgedFocusAmt = want;
        try
        {
            int n = 0;
            foreach (var t in pane.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith("Spell ")) continue;   // Spell Focus / Spell Fireball / Spell Quake / Spell Scream
                t.position += new Vector3(0f, delta, 0f); n++;
            }
            if (cfg.debug == 1) Dbg($"HKDS NudgeFocusCluster lifted {n} spells by {delta:F2} (total {want:F2})");
        }
        catch (Exception e) { Dbg($"HKDS focus nudge err {e.Message}"); }
    }

    // [B4] Inventory clone fingerprint: the ints/bools that change which items/upgrades the pane shows.
    // NOTE: pure COUNTERS (geo, ore, eggs, simple keys, relics) are deliberately NOT here — they're refreshed live
    // (RefreshInvCounters); stamping geo re-deep-cloned the pane after every pickup. Positional bits (`? 1 : 0`, not
    // `i+1`) so two different progressions can't collide.
    static readonly string[] INV_STAMP_INTS = { "nailDamage","maxHealth","MPReserveMax","fireballLevel","quakeLevel","screamLevel",
                                                "vesselFragments","heartPieces","nailSmithUpgrades" };
    static readonly string[] INV_STAMP_BOOLS = { "hasDash","hasWalljump","hasSuperDash","hasAcidArmour","hasDoubleJump","hasLantern",
                                                 "hasDreamNail","dreamNailUpgraded","hasCyclone","hasDashSlash","hasUpwardSlash",
                                                 "hasKingsBrand","hasWhiteKey","hasCityKey","hasSlykey","hasLoveKey","hasTramPass","hasQuill",
                                                 "usedWhiteKey","hasShadowDash","hasMap","hasXunFlower","xunFlowerBroken","hasDreamGate" };
    int InvStampHash(PlayerData pd, int h)
    {
        for (int i = 0; i < INV_STAMP_INTS.Length; i++) h = h * 31 + pd.GetInt(INV_STAMP_INTS[i]);
        for (int i = 0; i < INV_STAMP_BOOLS.Length; i++) h = h * 31 + (pd.GetBool(INV_STAMP_BOOLS[i]) ? 1 : 0);
        return h;
    }
}
