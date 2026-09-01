using System;
using System.Collections.Generic;
using UnityEngine;

// [B6] BOTTOM SCREEN — Map tab. The Game Map clone, deferred SetupMap + zone quick-map, the 'you are here' compass
// (fade + re-anchor across room/zone changes), Wayward-Compass / Quill-rest / pin-purchase watchers, pin gating,
// the FULL-AREA fit (every room of the area, revealed or not, framed once per area with one equal margin against the
// frame's inner rect), the map-content stamp that lets a tab return skip the re-setup, and the zone tables.
public partial class HKDualScreen
{
    GameObject mapClone;           // Instantiate() of GameManager.instance.gameMap, on ATTR_LAYER (the cached Map clone)
    GameMap mapGm;                 // the clone's GameMap component, cached (was a GetComponent x5 per frame)

    UnityEngine.Object mapSrcRef;  // identity of gm.gameMap we cloned (rebuild the cache if the source is recreated)

    bool mapNeedsSetup;            // SetupMap is deferred until the clone's Start() sets its gm/pd/hero refs

    string lastMapZone = "", lastMapScene = "";   // live-refresh: re-run the quick-map on zone/room change

    bool mapAvailable;             // gate: current zone's quick-map (LB) is obtained — else render nothing (no map/markers/name)

    bool mapContentVisible;        // the clone is currently SHOWING a quick-map (false after the no-map hide / before the first setup)

    int mapStowStamp = -1;         // MapContentStamp() when the Map tab was last stowed; unchanged on return -> no re-setup, compass only

    // ---- full-area fit (replaces the per-reveal renderer-bounds refit burst) ----
    Bounds mapAreaB;               // the current area's WHOLE extent (all rooms incl. unrevealed + its in-map title), mapClone-LOCAL space
    bool mapAreaBValid;            // mapAreaB holds a measured area (false -> legacy TryMapBounds fallback for an unknown zone)
    string mapAreaBFor;            // resolved area the cached bounds belong to (null = never measured)
    int mapAreaBTries;             // re-measure for this many frames after a (re)setup — the title's TMP mesh can build a frame late
    bool mapFitIsArea;             // this frame's `fit` came from mapAreaB (ApplyCompanionCamera then centres it in the inner rect)
    float mapInnerYc;              // inner rect vertical centre (ortho fracs, cam-relative) the area is centred on
    float frameInnerBotFrac = float.NaN;   // measured each PositionFrame: top edge of the tab row's TOP FLEUR (ortho fracs, cam-relative) = inner bottom
    float frameInnerTopFrac = float.NaN;   // measured each PositionFrame: bottom edge of the top separator's FLEUR ornament = inner top (NaN -> compSepTopY line)

    bool lastCompassEquipped;      // fix3: watch pd.equippedCharm_2 to re-render the compass the moment it's equipped

    bool compassPending;           // set while a scene transition is in flight; re-anchor the compass once it ends

    System.Reflection.FieldInfo displayingCompassFI;   // GameMap.displayingCompass (private) — freeze icon during transitions
    System.Reflection.FieldInfo gmapPdFI, gmapGmFI;    // GameMap.pd/gm (private) — re-pointed at the live singletons each setup (savestate loads swap them)

    float compassFade = 1f;        // 0..1 alpha for the 'you are here' icon — fade OUT during a transition, back IN after re-anchor (hides the reposition, like the main-screen fade)

    int lastScenesMapped = -1;     // watch pd.scenesMapped.Count -> re-reveal the clone map when resting fills in new rooms (Quill)

    int lastPinStamp = -1;         // fix5: watch the hasPin* purchase bools -> re-gate the cached map's pins when a pin is bought
    bool lastMapAvail;             // fix(1.0.1): watch map-availability flips -> FULL re-setup (rooms)
    int lastSgStamp = int.MinValue; // audit(168): shade/dream-gate change watch -> re-setup

    // HK MapZone (GameManager.GetCurrentMapZone) -> the display title shown when you hold LB. LOCALIZED via
    // HK's own "Map Zones" language sheet (keys = the MapZone enum names, e.g. GODS_GLORY -> "Godhome"/"Hogar
    // de Dioses"); sub-zones fall back to their parent area's key. The English switch below is a last resort.
    static string ZoneName(string z)
    {
        try
        {
            string v = TeamCherry.Localization.Language.Get(z, "Map Zones");
            if (!string.IsNullOrEmpty(v) && v != z && !v.Contains("#!#")) return v.Replace("<br>", " ");
            string rz = ResolveMapArea(z);
            if (rz != z)
            {
                v = TeamCherry.Localization.Language.Get(rz, "Map Zones");
                if (!string.IsNullOrEmpty(v) && v != rz && !v.Contains("#!#")) return v.Replace("<br>", " ");
            }
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
            case "MINES": return "Crystal Peak";
            case "DEEPNEST": case "BEASTS_DEN": return "Deepnest";
            case "CITY": case "KINGS_STATION": case "SOUL_SOCIETY": case "LURIENS_TOWER": return "City of Tears";
            case "WATERWAYS": case "GODSEEKER_WASTE": return "Royal Waterways";
            case "ABYSS": return "Ancient Basin";
            case "ROYAL_GARDENS": return "Queen's Gardens";
            case "RESTING_GROUNDS": return "Resting Grounds";
            case "OUTSKIRTS": case "HIVE": case "COLOSSEUM": return "Kingdom's Edge";
            case "PEAK": return "Crystal Peak";
            case "WHITE_PALACE": return "White Palace";
            case "DREAM_WORLD": return "Dream";
            default: return "";
        }
    }

    // Collapse a raw MapZone to the AREA whose single quick-map it shares (matches SetupQuickMap's case
    // groupings). Used so sub-zone crossings that don't change the displayed map don't trigger a re-setup.
    static string ResolveMapArea(string z)
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

    // Is the current zone's quick-map (LB view) actually obtained? Mirrors HK's own per-zone map paper
    // (PlayerData.map*). If not, holding LB shows nothing in-game -> the bottom companion must NOT render a
    // map/markers/name either (e.g. a fresh save in King's Pass has no map at all -> hasMap=false).
    bool HasMapForCurrentZone()
    {
        try
        {
            var pd = PlayerData.instance;
            if (pd == null || !pd.hasMap) return false;   // no fold-out map at all yet
            string z = ""; var g = GameManager.instance; if (g != null) z = g.GetCurrentMapZone();
            switch (z)
            {
                case "TOWN": return pd.mapDirtmouth;
                case "KINGS_PASS": return pd.mapDirtmouth;   // fix#1(163-fb): with the Dirtmouth map (auto-map grants it) the intro area shows the Town quick-map
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
                default: return false;   // dream world / unknown -> no quick-map
            }
        }
        catch { return false; }
    }

    // Show the CURRENT-ZONE quick-map (the LB view) on the clone: one area active, centred, with a live
    // compass (GameMap.Update tracks the hero every frame). Dispatch by GameManager.GetCurrentMapZone().
    void SetupQuickMap(GameMap m)
    {
        var pd = PlayerData.instance;
        string z = "";
        try { z = GameManager.instance.GetCurrentMapZone(); } catch { }
        try
        {
            // P0: the availability-lost hide disabled renderers that QuickMapX/SetActive gating never re-enables, so the
            // map stayed blank after any no-map zone (dream boss / White Palace / unbought-map area) until a tab rebuild.
            // Every reveal path calls SetupQuickMap, so re-enable here — but ONLY the renderers WE hid (mapHiddenRs), not
            // a blanket enable that would also reveal renderers HK itself keeps disabled on active objects.
            foreach (var r in mapHiddenRs) if (r != null) r.enabled = true;
            mapHiddenRs.Clear();
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
                default: m.WorldMap(); break;   // dream world / unknown -> full map fallback
            }
            m.StopPan();
            // QuickMapX force-activates these auto-markers regardless of progress. Gate them to match HK:
            //  - dreamerPins: world-map-only group -> always hidden on the quick-map (correct).
            //  - flamePins (P3): Grimmkin-flame locations, shown while Grimmchild (equippedCharm_40) is
            //    equipped. Static map positions, so gating the container is faithful + safe.
            //  - shadeMarker / dreamGateMarker: DEFERRED. HK positions these via PositionCompass's posShade/
            //    posGate branches off pd.shadeMapPos/dreamgateMapPos; driving that on the clone risks
            //    disturbing the (working) compass state, and both are niche. Kept hidden for now.
            // fix(167): REAL shade/dream-gate logic [user] — shown exactly when they exist, placed by
            // HK's own methods. audit(168): dreamer pins now REAL too — container shown iff the
            // purchasable Dreamer pin (hasPinGuardian) is owned; each defeated dreamer's child hides.
            if (m.dreamerPins != null)
            {
                bool guard = false; try { guard = pd != null && pd.hasPinGuardian; } catch { }
                m.dreamerPins.SetActive(guard);
                if (guard)
                    try
                    {
                        foreach (Transform dch in m.dreamerPins.transform)
                        {
                            string dn = dch.name.ToLowerInvariant();
                            bool dead = (dn.Contains("lurien") && pd.lurienDefeated)
                                     || (dn.Contains("monomon") && pd.monomonDefeated)
                                     || (dn.Contains("herrah") || dn.Contains("hegemol")) && pd.hegemolDefeated;
                            dch.gameObject.SetActive(!dead);
                        }
                    }
                    catch { }
            }
            bool hasShade = false, hasGate = false;
            try { hasShade = pd != null && !string.IsNullOrEmpty(pd.shadeScene) && pd.shadeScene != "None"; } catch { }
            // fix(1.0.1g) [user: "dreamgate icon render even though i still dont have it"]: a recorded scene is
            // NOT proof of a gate — the savestate warp writes pd.dreamGateScene to teleport (see HKTweaks),
            // and older saves may already carry that pollution. hasDreamGate is the ability itself, so require
            // it: you cannot have placed a gate without the spell. This also repairs affected saves.
            try { hasGate = pd != null && pd.hasDreamGate && !string.IsNullOrEmpty(pd.dreamGateScene); } catch { }
            if (m.shadeMarker != null) m.shadeMarker.SetActive(hasShade);
            if (m.dreamGateMarker != null) m.dreamGateMarker.SetActive(hasGate);
            if (cfg.debug == 1)
                try
                {
                    var sm = m.shadeMarker;
                    string chain = "";
                    if (sm != null) for (var q = sm.transform; q != null && chain.Length < 120; q = q.parent) chain += q.name + (q.gameObject.activeSelf ? "+" : "-") + "/";
                    string cpos = "?"; try { if (m.compassIcon != null) cpos = m.compassIcon.transform.position.ToString("F1"); } catch { }
                    string spos = "?"; try { if (sm != null) spos = sm.transform.position.ToString("F1"); } catch { }
                    Dbg($"HKDS map markers scene='{(GameManager.instance != null ? GameManager.instance.sceneName : "?")}' compassW={cpos} shadeW={spos} " +
                        $"shade={hasShade} '{pd?.shadeScene}' pos={(pd != null ? pd.shadeMapPos.ToString("F1") : "?")} " +
                        $"smActive={(sm != null ? sm.activeInHierarchy.ToString() : "null")} chain={chain} | gate={hasGate} has={(pd != null && pd.hasDreamGate)} '{pd?.dreamGateScene}'");
                }
                catch { }
            // fix(1.0.1h) [user: "when load save, the shade icon is wrong place, it sit on same place as current
            // char load ... i think its save source of bug" — correct, and worse than it looked]:
            // PositionCompass(posShade:true) is not a "draw the shade" call. It is HK's "STAMP the shade HERE,
            // NOW" call — it sets shadeMarker to the CURRENT room and writes pd.shadeMapPos into the save (and
            // in its failure branch writes (-10000,-10000), destroying it). HK calls it once, at the moment of
            // death. We were calling it on every map setup, so the recorded shade position followed the player
            // around — visible in the logs as shadeMapPos drifting (-1.5,5.1) -> (0.9,4.9) -> (-2.2,5.8).
            // PositionDreamGateMarker() does the same to pd.dreamgateMapPos. Both are now READ-ONLY: place the
            // markers from the positions HK already recorded, and never write the save.
            if (hasShade && m.shadeMarker != null)
            {
                try { m.shadeMarker.transform.localPosition = pd.shadeMapPos; } catch { }
                // Map_shade_head / Map_shade_fader are plain SpriteRenderers under an object HK ships inactive,
                // and the Animator that would reveal them is frozen on the clone: make the head visible.
                try
                {
                    foreach (var sr in m.shadeMarker.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr == null) continue;
                        sr.gameObject.SetActive(true); sr.enabled = true;
                        if (NameHas(sr.gameObject.name, "fader")) continue;   // a soft glow authored at low alpha
                        var c = sr.color; if (c.a < 0.99f) { c.a = 1f; sr.color = c; }
                    }
                }
                catch { }
            }
            if (hasGate && m.dreamGateMarker != null)
                try { m.dreamGateMarker.transform.localPosition = pd.dreamgateMapPos; } catch { }
            MirrorRoomState(m);
            try { m.PositionCompass(false); } catch { }   // re-anchor the 'you are here' after the marker passes
            if (m.flamePins != null) m.flamePins.SetActive(pd != null && pd.equippedCharm_40);
            // Fine-gate every remaining pin by its OWN FSM's PlayerData tests (the clone's FSMs are frozen,
            // so pins force-show regardless of progress). This matches what HK actually shows at the player's
            // current progress: benches/stags only with the pin item, shop/NPC/ghost pins only once met, etc.
            if (mapClone != null) GateMarkers(mapClone);
        }
        catch (Exception e) { Dbg($"HKDS quickmap err {e.Message}"); }
        lastMapZone = z;
        lastMapScene = GameManager.instance != null ? GameManager.instance.sceneName : "";
        // fix(1.0.1h) [user: "compass still at death spot ... moving to another room resolves it"]: exactly the
        // symptom of a MISSED re-anchor. Dying fires the shade watch, which re-runs this setup DURING the load —
        // and latching lastMapScene here to the (already updated) respawn scene meant the settled-frame check
        // `sc != lastMapScene` never fired afterwards, so the compass kept the position resolved mid-load: the
        // room you died in. Walking to another room changed the scene again and fixed it, which is why it
        // looked intermittent. Arm the pending flag so the next SETTLED frame always re-anchors.
        compassPending = true;
    }

    // Replicate HK's per-pin gating on the frozen-FSM clone (the real pin FSMs are disabled, so they can't run their
    // OnEnable gate). Verified against the ripped FSM templates + GameMap.cs. Two visibility templates:
    //   * pin_activation / pin_activation_ghost  — vars "Pin Type Bool" (+ "Specific Bool", +ghost "Completed Bool").
    //       show = pd[PinTypeBool] && (Specific=="" || pd[Specific]) && !(ghost && pd[CompletedBool]).  HK toggles the
    //       pin's SpriteRenderer.enabled (NOT activeSelf), so we do the same — a SetActive alone wouldn't re-show a pin
    //       whose SR was cloned already-disabled.
    //   * activate_if_pd_bool  — vars "PD Bool Name" (+ bool "Active If Bool True", always 1 in the prefab). HK runs
    //       ActivateAllChildren(Self, pd[bool]==ActiveIfBoolTrue) -> toggles the CHILDREN's activeSelf, leaving Self (the
    //       room / group container) alone. Covers grubs (hasPinGrub), Black Egg (hasPinBlackEgg, child Pin_Black_Egg),
    //       the Flame Pins group (equippedCharm_40), the inert Watcher room (0 children). Grub children ALSO self-hide
    //       once rescued (GrubPin: pd.scenesGrubRescued.Contains(childName)), so gate that in too.
    // Left to inherited state / SetupQuickMap (not touched here): toll-bench sprite swap ("PlayerData Bool"), room
    // sprite variants ("PD Bool"), the hasKingsBrand sub-area label + shade/dream-gate inline FSMs (both use the
    // "playerData bool" var — catching it here would wrongly drive the shade marker), and the dreamer pins.
    void GateMarkers(GameObject clone)
    {
        var pd = PlayerData.instance; if (pd == null || clone == null) return;
        foreach (var fsm in clone.GetComponentsInChildren<PlayMakerFSM>(true))
        {
            try
            {
                var v = fsm.FsmVariables;
                var catV = v.FindFsmString("Pin Type Bool");
                if (catV != null && !string.IsNullOrEmpty(catV.Value))
                {
                    bool show = pd.GetBool(catV.Value);                                  // category owned (hasPinBench/Stag/Tram/Spa/Shop/Ghost)
                    var specV = v.FindFsmString("Specific Bool"); string sp = specV != null ? specV.Value : "";
                    if (show && !string.IsNullOrEmpty(sp)) show = pd.GetBool(sp);         // per-thing discovery (metSly, xeroPinned, ...)
                    var compV = v.FindFsmString("Completed Bool");                        // ghost graves: gone once the ghost is beaten
                    if (show && compV != null && !string.IsNullOrEmpty(compV.Value) && pd.GetBool(compV.Value)) show = false;
                    SetPinRenderers(fsm.gameObject, show);
                    continue;
                }
                var pdn = v.FindFsmString("PD Bool Name");
                if (pdn != null && !string.IsNullOrEmpty(pdn.Value))
                {
                    var aif = v.FindFsmBool("Active If Bool True");
                    bool want = pd.GetBool(pdn.Value) == ((aif == null) || aif.Value);    // children active iff the PD bool matches
                    bool grub = pdn.Value == "hasPinGrub";
                    for (int i = 0; i < fsm.transform.childCount; i++)
                    {
                        var ch = fsm.transform.GetChild(i).gameObject;
                        bool on = want && !(grub && want && ScenesGrubRescued(pd, ch.name));   // a rescued grub's pin disappears
                        ch.SetActive(on);
                        if (on) foreach (var sr in ch.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = true;   // undo any cloned-disabled SR
                    }
                }
                // else: not a visibility-gating pin FSM (toll sprite swap / room variant / sub-area / shade / dream gate)
            }
            catch { }
        }
    }

    // ---- bench teleport (tweak) ------------------------------------------------------------------
    // Bench pins are pin_activation FSMs with "Pin Type Bool" == hasPinBench; the pin's parent room
    // object is NAMED the scene. Tap once to arm (toast), tap the same pin again to travel — only
    // benches the player has actually sat on are warpable (HKTweaks records their respawn data).
    string benchSelScene; float benchSelUntil;
    string benchToastText; float benchToastUntil;   // shown via the map's message TMP (Hud) — PRIVATE: public fields would change the MonoBehaviour's serialized layout and break scripts-only builds
    void BenchToast(string msg, float secs) { benchToastText = msg; benchToastUntil = Time.unscaledTime + secs; }

    void BenchPinTap(Vector3 wp)
    {
        if (mapClone == null || tab.cur != COMP_MAP || !mapContentVisible) return;
        try
        {
            foreach (var fsm in mapClone.GetComponentsInChildren<PlayMakerFSM>(false))
            {
                var v = fsm.FsmVariables; if (v == null) continue;
                var catV = v.FindFsmString("Pin Type Bool");
                if (catV == null || catV.Value != "hasPinBench") continue;
                var sr = fsm.GetComponentInChildren<SpriteRenderer>();
                if (sr == null || !sr.enabled) continue;
                var b = sr.bounds; b.Expand(new Vector3(0.45f, 0.45f, 10f));
                if (wp.x < b.min.x || wp.x > b.max.x || wp.y < b.min.y || wp.y > b.max.y) continue;
                string scene = fsm.transform.parent != null ? fsm.transform.parent.name : null;
                if (string.IsNullOrEmpty(scene)) return;
                if (!HkStageHooks.IsBenchRecorded(scene)) { BenchToast("SIT ON THAT BENCH ONCE TO ENABLE TRAVEL", 3.5f); Dbg($"HKDS bench pin {scene}: not recorded"); return; }
                if (benchSelScene == scene && Time.unscaledTime < benchSelUntil)
                {
                    benchSelScene = null;
                    BenchToast("TRAVELING...", 2f);
                    HkStageHooks.BenchWarp(scene);
                }
                else
                {
                    benchSelScene = scene; benchSelUntil = Time.unscaledTime + 4f;
                    BenchToast("TRAVEL TO " + scene.ToUpperInvariant() + "?  TAP THE PIN AGAIN", 4f);
                    Dbg($"HKDS bench pin armed {scene}");
                }
                return;
            }
        }
        catch (Exception e) { WarnOnce("bench pin tap", e); }
    }

    // pin_activation / pin_activation_ghost render via SpriteRenderer.enabled with the object left active (matches HK).
    static void SetPinRenderers(GameObject go, bool show)
    {
        if (!go.activeSelf) go.SetActive(true);
        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = show;
    }

    static bool ScenesGrubRescued(PlayerData pd, string scene)
    {
        try { return pd.scenesGrubRescued != null && pd.scenesGrubRescued.Contains(scene); } catch { return false; }
    }

    static readonly string[] _compassColorProps = { "_Color", "_FaceColor", "_TintColor" };
    readonly List<Renderer> mapHiddenRs = new List<Renderer>();
    int mapSetupFails;   // consecutive deferred-SetupMap failures (transient while the clone's Start() hasn't run; a permanent one is warned once)   // renderers the availability-lost hide disabled (re-enabled by SetupQuickMap, and only those)

    // Set the alpha of the map's 'you are here' compass icon — used to fade it out during a transition and
    // back in after re-anchoring, so the reposition isn't visible. The icon is a MeshRenderer (NOT a
    // SpriteRenderer, so the earlier sprite-only version faded nothing), so tint its instanced material's
    // colour; also handle any child SpriteRenderers.
    GameObject _compassRsFor; Renderer[] _compassRs;   // PERF: cached per compassIcon — this runs per frame on the Map tab
    float _compassAlphaApplied = -1f;                  // last applied alpha (skip the whole walk when unchanged — steady state)
    void SetCompassAlpha(GameMap m, float a)
    {
        try
        {
            if (m == null || m.compassIcon == null) return;
            if (a == _compassAlphaApplied && ReferenceEquals(_compassRsFor, m.compassIcon)) return;
            _compassAlphaApplied = a;
            if (!ReferenceEquals(_compassRsFor, m.compassIcon)) { _compassRsFor = m.compassIcon; _compassRs = m.compassIcon.GetComponentsInChildren<Renderer>(true); }
            foreach (var r in _compassRs)
            {
                if (r == null) continue;
                if (r is SpriteRenderer sr) { var c = sr.color; if (c.a != a) { c.a = a; sr.color = c; } continue; }
                var mat = r.material;   // instanced on first access, cached after -> no per-frame alloc
                if (mat == null) continue;
                foreach (var pn in _compassColorProps)
                    if (mat.HasProperty(pn)) { var c = mat.GetColor(pn); if (c.a != a) { c.a = a; mat.SetColor(pn, c); } }
            }
        }
        catch { }
    }

    // ---- UpdateCompanion() tenants, split by owning module (bodies moved verbatim) ----

    // [B6] Per-frame map maintenance. Runs on EVERY tab (it clears mapAvailable off the Map tab, which the no-map
    // label and the framing branch read). Order: availability gate -> deferred SetupMap -> zone/room/transition +
    // compass fade -> Wayward-Compass equip watch -> scenesMapped (Quill/rest) watch -> hasPin* purchase watch.
    void MapTick()
    {
        // Gate: render the map only if the current zone's quick-map is actually obtained (LB works in-game).
        // Otherwise (e.g. a fresh King's Pass save with no map) hide the whole clone -> no map, no markers.
        // Tracked against what the clone is actually SHOWING (mapContentVisible), not against last frame's flag — the
        // flag is false on every other tab, so a tab return used to look like "availability regained" and re-ran setup.
        mapAvailable = (tab.cur == COMP_MAP) && HasMapForCurrentZone();
        if (tab.cur != COMP_MAP || mapClone == null) return;   // nothing below applies off the Map tab / before the clone exists

        if (!mapAvailable)
        {
            if (mapContentVisible)   // just entered a no-map zone -> hide the whole clone once
            {
                mapHiddenRs.Clear();
                foreach (var r in mapClone.GetComponentsInChildren<Renderer>(true)) if (r != null && r.enabled) { r.enabled = false; mapHiddenRs.Add(r); }   // remember exactly what WE hid
                mapContentVisible = false; fit.valid = false;
            }
            return;   // no quick-map here -> nothing to maintain
        }
        if (!mapContentVisible) mapNeedsSetup = true;   // just obtained/entered a mapped zone -> full deferred setup below

        var m = mapGm != null ? mapGm : (mapGm = mapClone.GetComponent<GameMap>());   // one GetComponent, cached
        var gm = GameManager.instance; var pd = PlayerData.instance;
        if (m == null || gm == null || pd == null) return;

        // Deferred SetupMap: retry until the clone's Start() has run (set gm/pd/hero) so it stops NREing. One heavy op
        // per frame — return after it so the live watchers below run on the next (fully set-up) frame.
        if (mapNeedsSetup)
        {
            try
            {
                // fix#1(160-fb): the clone's PRIVATE pd/gm refs are latched in its Start() — after a
                // savestate load PlayerData.instance is a NEW object and the clone kept reading the old
                // one (auto-map rooms + compass went stale until an area change). Re-point them here so
                // EVERY setup works off the live singletons.
                try
                {
                    if (gmapPdFI == null)
                    {
                        gmapPdFI = typeof(GameMap).GetField("pd", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        gmapGmFI = typeof(GameMap).GetField("gm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    }
                    gmapPdFI?.SetValue(m, pd); gmapGmFI?.SetValue(m, gm);
                }
                catch { }
                m.SetupMap();                                      // reveal pins/rooms data (needs refs)
                SetupQuickMap(m);                                  // show the current-zone quick-map (LB view)
                SetLayerRecursive(mapClone.transform, ATTR_LAYER); // re-apply: QuickMapX activates area sprites
                mapNeedsSetup = false; mapContentVisible = true; mapSetupFails = 0;
                mapAreaBTries = 3;   // (re)measure the area's full extent over the next frames (title TMP mesh may build late)
                ResetMapView();      // new area -> discard any pinch zoom/pan from the previous one
                lastCompassEquipped = pd.equippedCharm_2;   // SetupQuickMap's PositionCompass just latched it
                lastPinStamp = PinStamp();                  // SetupQuickMap + GateMarkers just gated every pin
            }
            catch (Exception e) { if (++mapSetupFails == 300) WarnOnce("map SetupMap (still failing after 300 retries)", e); /* clone refs not ready yet — retry next frame */ }
            return;
        }

        // ---- live maintenance (map is set up and available; m/gm/pd cached above) ----

        // Room/zone change + compass fade. The 'you are here' icon tracks live every frame via the clone's GameMap.Update,
        // but its per-room scale/offset must be refreshed on a ROOM change (checked every frame so a boundary is caught
        // immediately), and a zone change re-runs the full deferred setup.
        {
            string z = gm.GetCurrentMapZone(), sc = gm.sceneName;
            bool inTrans = false; try { inTrans = gm.IsInSceneTransition; } catch { }
            bool playing = false; try { playing = gm.gameState == GlobalEnums.GameState.PLAYING; } catch { }
            bool settling = inTrans || !playing;   // transition in flight OR hero not yet back in control

            // AREA change: re-run the full deferred setup the moment the NEW zone is reported — which happens while
            // the screen is still held BLACK during the load (the bottom is dimmed in sync), so the old area is
            // swapped for the new one BEHIND the fade instead of visibly re-rendering after the fade-in (user).
            // P8: compare the RESOLVED area so sub-zone crossings sharing one quick-map don't re-setup.
            if (ResolveMapArea(z) != ResolveMapArea(lastMapZone))
            {
                mapNeedsSetup = true;   // robust deferred setup (SetupMap + SetupQuickMap), runs during the dim
                lastMapZone = z;
            }

            if (settling)
            {
                // fix3 (compass blip -> fade): hold the icon FADED OUT + tracking frozen for the WHOLE unsettled window —
                // the load transition AND the hero walking in from the door (gameState != PLAYING) — not just while
                // IsInSceneTransition is set. Clearing displayingCompass stops Update() from streaking it; the fade hides
                // the reposition until it's truly settled.
                try
                {
                    if (displayingCompassFI == null)
                        displayingCompassFI = typeof(GameMap).GetField("displayingCompass",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    displayingCompassFI?.SetValue(m, false);
                }
                catch { }
                compassFade = Mathf.MoveTowards(compassFade, 0f, Time.unscaledDeltaTime * 14f);   // fade out fast
                // HARD-hide the icon for the whole unsettled window: the alpha fade alone proved insufficient (the
                // icon's shader can miss the faded colour properties), and the during-dim area setup re-activates it
                // via PositionCompass at the STALE hero position — it showed misplaced through the fade-in, then
                // snapped. Deactivating is bulletproof; the settled re-anchor's PositionCompass re-activates it.
                try { if (m.compassIcon != null && m.compassIcon.activeSelf) m.compassIcon.SetActive(false); } catch { }
                compassPending = true;   // re-anchor once the hero has settled
            }
            else   // PLAYING — hero settled in the room (area change handled above, during the dim)
            {
                if (sc != lastMapScene || compassPending)
                {
                    // GUARD: the clone's LevelReady -> GetTilemapDimensions calls gm.RefreshTilemapInfo(null) on the
                    // REAL GameManager whenever gm.tilemap is momentarily fake-null (dreamgate arrivals) — the
                    // null-scene scan can latch the wrong scene's dimensions into gm.sceneWidth/Height and misplace
                    // the compass. Re-anchor only once the tilemap exists; until then stay pending (the compass is
                    // faded out during this window anyway).
                    bool tilemapReady = false; try { tilemapReady = gm.tilemap != null; } catch { }
                    if (tilemapReady)
                    {
                        MirrorRoomState(m);
                        try { m.LevelReady(); m.PositionCompass(false); } catch (Exception e) { WarnOnce("map compass re-anchor", e); }   // same-zone room, settled: re-anchor once
                        lastMapScene = sc;
                        compassPending = false;
                    }
                }
                // fix(1.0.1h) [user: "it refresh corectly placed when change room, why not refresh like it when
                // dead"]: exactly so — a room change runs the re-anchor above, and that is also what re-enables
                // tracking. The settling path CLEARS displayingCompass, and on death/respawn no re-anchor
                // necessarily follows, so nothing switched it back on: the icon stayed hidden and the only
                // marker left on screen was the SHADE, sitting where you died. Re-assert tracking here, on
                // exactly HK's own condition (Wayward Compass equipped, and not parked inside an interior).
                try
                {
                    if (displayingCompassFI == null)
                        displayingCompassFI = typeof(GameMap).GetField("displayingCompass",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (pd != null && pd.equippedCharm_2 && !m.inRoom)
                    {
                        displayingCompassFI?.SetValue(m, true);
                        if (m.compassIcon != null && !m.compassIcon.activeSelf) m.compassIcon.SetActive(true);
                    }
                }
                catch { }
                if (cfg.debug == 1 && (Time.frameCount % 60) == 0)
                {
                    string cs = "?", rcs = "?"; bool disp = false;
                    try { cs = m.currentScene != null ? m.currentScene.name : "null"; } catch { }
                    try { rcs = realMapGm != null && realMapGm.currentScene != null ? realMapGm.currentScene.name : "null"; } catch { }
                    try { disp = displayingCompassFI != null && (bool)displayingCompassFI.GetValue(m); } catch { }
                    Dbg($"HKDS compass sc='{sc}' last='{lastMapScene}' pend={compassPending} inRoom={m.inRoom} door='{m.doorScene}' " +
                        $"cloneScene='{cs}' realScene='{rcs}' pos={m.currentScenePos.ToString("F1")} disp={disp}");
                }
                compassFade = Mathf.MoveTowards(compassFade, 1f, Time.unscaledDeltaTime * 6f);   // fade back in
            }
            // NOTE: do NOT mirror the real map's currentScene here. It only re-resolves when the LB map is
            // OPENED, so between openings it holds a stale room — and running it after our own re-anchor
            // clobbered the correct value with that stale one. MirrorRoomState now runs only BEFORE a
            // PositionCompass call, purely to hand it inRoom/doorScene for interior rooms.
            SetCompassAlpha(m, compassFade);   // apply the fade to the 'you are here' icon every frame
        }

        // Wayward Compass equipped/unequipped mid-zone -> re-latch displayingCompass (only PositionCompass sets it, gated
        // on equippedCharm_2). PositionCompass alone is enough — no full re-setup for a marker toggle. Fires on change.
        if (pd.equippedCharm_2 != lastCompassEquipped)
        {
            lastCompassEquipped = pd.equippedCharm_2;
            MirrorRoomState(m);
            try { m.PositionCompass(false); } catch (Exception e) { WarnOnce("map compass re-latch", e); }
        }

        // fix(1.0.1) [user: dreamgate icon appears on a new save with no dream nail]: EVERY
        // QuickMap<Area>() and WorldMap() in the game force-enables shadeMarker/dreamGateMarker
        // unconditionally (11 call sites). Gating them once per setup loses the race whenever any of
        // those re-runs afterwards. Re-assert it here every map tick — two SetActive calls, and it
        // closes the hole regardless of which path re-enabled them.
        if (mapClone != null)
        {
            try
            {
                var mm = mapClone.GetComponent<GameMap>();
                if (mm != null)
                {
                    bool hs = !string.IsNullOrEmpty(pd.shadeScene) && pd.shadeScene != "None";
                    bool hg = pd.hasDreamGate && !string.IsNullOrEmpty(pd.dreamGateScene);   // ability gates it, see SetupMap
                    if (mm.shadeMarker != null && mm.shadeMarker.activeSelf != hs) mm.shadeMarker.SetActive(hs);
                    if (mm.dreamGateMarker != null && mm.dreamGateMarker.activeSelf != hg) mm.dreamGateMarker.SetActive(hg);
                }
            }
            catch { }
        }

        // audit(168): the SHADE appears on death and the DREAMGATE moves when set — both are placed
        // during SetupQuickMap, so a change forces the full deferred re-setup.
        {
            int sg = 17;
            try { sg = (pd.shadeScene ?? "").GetHashCode() * 31 + (pd.dreamGateScene ?? "").GetHashCode() + (int)pd.dreamGateX; } catch { }
            if (lastSgStamp != int.MinValue && sg != lastSgStamp) { mapNeedsSetup = true; Dbg("HKDS map shade/gate watch -> re-setup"); }
            lastSgStamp = sg;
        }

        // Quill / rest-update: resting at a bench fills in newly-explored rooms -> pd.scenesMapped grows. Watch the count
        // (+ the mapAllRooms cheat, which reveals rooms without changing it) and re-reveal when it changes.
        int scm = pd.scenesMapped != null ? pd.scenesMapped.Count + (pd.mapAllRooms ? 1000000 : 0) : -1;
        if (scm >= 0 && scm != lastScenesMapped)
        {
            lastScenesMapped = scm;
            mapNeedsSetup = true;   // re-run the robust deferred setup to reveal the newly-mapped rooms
            Dbg($"HKDS map rooms watch -> re-setup (scenesMapped={scm})");   // fix#5(157-fb): diagnosable on-device
        }

        // Pins update on purchase/event (bench/stag/…, placed markers): gated once at setup, so re-stamp the driving
        // PlayerData (a handful of GetBool/Count calls, every 20 frames) and re-gate the cached clone when it flips —
        // without a full SetupMap (which would re-hide unexplored rooms).
        else if ((Time.frameCount % 20) == 0)
        {
            int ps = PinStamp();
            if (ps != lastPinStamp)
            {
                lastPinStamp = ps;
                // fix(1.0.1) [user: "map sometimes does not fill in the rough map with the full map"]:
                // buying an area map flips pd.map<Area>, which IS in this stamp — but the pins-only pass
                // below can never reveal ROOMS. HK gates that on `pd.hasQuill && !pinsOnly`, so with
                // pinsOnly the pins refreshed while the rough map stayed rough until some later event
                // forced a full setup. When map availability changes, take the FULL path instead.
                bool mapNow = HasMapForCurrentZone();
                if (mapNow != lastMapAvail)
                {
                    lastMapAvail = mapNow;
                    lastPinStamp = ps;
                    mapNeedsSetup = true;          // full SetupMap(false) + SetupQuickMap -> rooms appear
                    Dbg($"HKDS map availability -> {mapNow}, full re-setup");
                    return;
                }
                // fix#6(157-fb): PLACE markers before gating — GateMarkers only hides/permits existing
                // objects; a freshly-placed bought marker needs SetupMapMarkers to activate+position it.
                // audit(168): native pinsOnly pass first — it places NEW cocoon/root pins.
                try { m.SetupMap(true); } catch (Exception e) { WarnOnce("map SetupMap pins", e); }
                try { m.SetupMapMarkers(); } catch (Exception e) { WarnOnce("map SetupMapMarkers", e); }
                try { GateMarkers(mapClone); } catch (Exception e) { WarnOnce("map GateMarkers", e); }   // light re-gate: no QuickMapX re-activate (that would re-hide unexplored rooms)
                Dbg("HKDS map pin/marker watch -> markers re-placed + re-gated");
            }
        }
    }

    // [B6] Map-tab framing. Pin the clone at compRoot every frame, then fit the WHOLE current area — every room of the
    // area, revealed or not, plus its in-map title — into the frame's inner rect (top separator .. tab-row fleur, view
    // sides) with ONE equal margin (compMapMargin) on the binding sides and the area centred. The extent is a property of
    // the area's prefab data, not of what has been revealed, so it is measured once per area (mapAreaB) and never moves
    // while you explore: rooms fill in, the compass walks, the frame stays put. No settle burst, no per-frame renderer
    // scan. Unknown zones (no area object) fall back to the old revealed-renderer bounds.
    void MapFrameTick(ref Vector3 frameCenter)
    {
        mapClone.transform.localPosition = Vector3.zero;   // pin at compRoot EVERY frame
        compFrameTick++;
        string area = ResolveMapArea(lastMapZone);   // the zone the quick-map was set up for
        if (!mapNeedsSetup && (mapAreaBFor != area || mapAreaBTries > 0))
        {
            if (mapAreaBTries > 0) mapAreaBTries--;
            var m = mapGm != null ? mapGm : (mapGm = mapClone.GetComponent<GameMap>());
            Bounds b;
            if (m != null && TryAreaBounds(m, lastMapZone, out b))
            {
                bool changed = !mapAreaBValid || mapAreaBFor != area || (b.center - mapAreaB.center).sqrMagnitude > 1e-4f || (b.size - mapAreaB.size).sqrMagnitude > 1e-4f;
                mapAreaB = b; mapAreaBValid = true; mapAreaBFor = area;
                if (changed) Dbg($"HKDS mapfit area={area} ext=({b.extents.x:F2},{b.extents.y:F2}) c=({b.center.x:F2},{b.center.y:F2})");
            }
            else
            {
                Vector3 c; float sz;
                if (TryMapBounds(mapClone, out c, out sz)) { fit = new FitResult { center = c, ortho = sz, valid = true }; mapAreaBValid = false; mapAreaBFor = area; Dbg($"HKDS mapfit FALLBACK zone={lastMapZone} ortho={sz:F2}"); }
            }
        }
        mapFitIsArea = mapAreaBValid && mapAreaBFor == area;
        if (mapFitIsArea)
        {
            // Inner rect in ortho fracs (cam-relative): x = the view sides; y = bottom edge of the top context-box fleur
            // (the separator ornament) .. top edge of the tab row's top fleur — both measured by PositionFrame each frame
            // (line position / a rough estimate on the very first frame). Frame off -> whole view.
            float asp = attrCam != null ? attrCam.aspect : (float)BOTTOM_W / BOTTOM_H;
            float yt = 1f, yb = -1f;
            if (cfg.compFrame == 1)
            {
                yt = float.IsNaN(frameInnerTopFrac) ? cfg.compSepTopY : frameInnerTopFrac;
                yb = float.IsNaN(frameInnerBotFrac) ? cfg.compTabY + 0.4f : frameInnerBotFrac;
            }
            float mgn = Mathf.Max(0f, cfg.compMapMargin);
            float hw = Mathf.Max(0.05f, asp - mgn), hh = Mathf.Max(0.05f, (yt - yb) * 0.5f - mgn);
            mapInnerYc = (yt + yb) * 0.5f;
            var tr = mapClone.transform;
            Vector3 c = tr.TransformPoint(mapAreaB.center);
            Vector3 e = Vector3.Scale(mapAreaB.extents, tr.lossyScale);
            fit = new FitResult { center = c, ortho = Mathf.Max(0.5f, Mathf.Max(e.y / hh, e.x / hw)), valid = true };   // ortho: the binding axis touches the margin exactly, the other has >= margin
        }
        if (fit.valid) frameCenter = fit.center;
    }

    // The area GameObject QuickMapX activates for a MapZone (same groupings as SetupQuickMap). null = no single area
    // (dream world / White Palace / unknown) -> the caller falls back to renderer bounds.
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

    // Anything under the area that is NOT map art: pins/markers (Grub Pins, pin_bench, Pin_Black_Egg…), shade/dream-gate/
    // compass/flame/glow. The "Next Area" hints (adjacent-zone name + Map_Arrow at the boundary rooms, e.g. "Dirtmouth" above
    // the Crossroads well) ARE part of the drawn map -> counted, so the margin keeps them clear of the top fleur.
    // Walks up to (not incl.) the area root.
    static bool MapFitSkip(Transform t, Transform area)
    {
        for (var p = t; p != null && p != area; p = p.parent)
        {
            string n = p.name;
            if (NameHas(n, "pin") || NameHas(n, "marker") || NameHas(n, "shade") || NameHas(n, "compass") || NameHas(n, "flame") || NameHas(n, "glow")) return true;
        }
        return false;
    }

    static readonly Vector3[] _cornerBuf = new Vector3[4];

    // Encapsulate a LOCAL rect (min..max, z ignored) transformed by mtx into b (clone-local space).
    static void EncapRect(ref Bounds b, ref bool have, Matrix4x4 mtx, Vector3 mn, Vector3 mx)
    {
        _cornerBuf[0] = mtx.MultiplyPoint3x4(new Vector3(mn.x, mn.y, 0f));
        _cornerBuf[1] = mtx.MultiplyPoint3x4(new Vector3(mx.x, mn.y, 0f));
        _cornerBuf[2] = mtx.MultiplyPoint3x4(new Vector3(mn.x, mx.y, 0f));
        _cornerBuf[3] = mtx.MultiplyPoint3x4(new Vector3(mx.x, mx.y, 0f));
        for (int k = 0; k < 4; k++)
        {
            var q = _cornerBuf[k]; q.z = 0f;
            if (float.IsNaN(q.x) || float.IsInfinity(q.x) || float.IsNaN(q.y) || float.IsInfinity(q.y)) continue;
            if (!have) { b = new Bounds(q, Vector3.zero); have = true; } else b.Encapsulate(q);
        }
    }

    // The area's FULL extent in mapClone-LOCAL space: every room SpriteRenderer under the area — active OR NOT, so the
    // unrevealed rooms count — from sprite.bounds x localToWorld (Renderer.bounds is not valid for inactive objects;
    // transforms are), plus the area's ACTIVE TMP title(s) (mesh forced so the bounds are real on the setup frame).
    bool TryAreaBounds(GameMap m, string z, out Bounds b)
    {
        b = default; bool have = false;
        var area = AreaObjFor(m, z); if (area == null || mapClone == null) return false;
        var w2l = mapClone.transform.worldToLocalMatrix;
        var areaT = area.transform;
        var srs = area.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < srs.Length; i++)
        {
            var sr = srs[i]; if (sr == null || sr.sprite == null) continue;
            if (MapFitSkip(sr.transform, areaT)) continue;
            var lb = sr.sprite.bounds; Vector3 mn = lb.min, mx = lb.max;
            if (sr.flipX) { float t = mn.x; mn.x = -mx.x; mx.x = -t; }
            if (sr.flipY) { float t = mn.y; mn.y = -mx.y; mx.y = -t; }
            EncapRect(ref b, ref have, w2l * sr.transform.localToWorldMatrix, mn, mx);
        }
        // in-map text: the area title + the "Next Area" adjacent-zone names — TMP MeshRenderers under the area (only the
        // ACTIVE ones render; MapNextAreaDisplay shows a hint once that neighbour is visited). ForceMeshUpdate first so a
        // label enabled this very frame already has glyph bounds.
        var rends = area.GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i]; if (r == null || r is SpriteRenderer || !r.enabled) continue;
            var tmp = r.GetComponent("TextMeshPro"); if (tmp == null) continue;
            if (MapFitSkip(r.transform, areaT)) continue;
            try { tmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(tmp, null); } catch { }
            var rb = r.bounds;
            if (float.IsNaN(rb.center.x) || float.IsInfinity(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue;
            EncapRect(ref b, ref have, w2l, rb.min, rb.max);
        }
        return have;
    }

    // Legacy fallback (unknown zone only): combined bounds of the clone's currently DRAWING room renderers + the near
    // TMP title, turned into a centre + fit ortho for the box.
    bool TryMapBounds(GameObject go, out Vector3 center, out float fitOrtho)
    {
        center = Vector3.zero; fitOrtho = 8f;
        var rends = go.GetComponentsInChildren<Renderer>();
        bool have = false; Bounds b = new Bounds();
        for (int i = 0; i < rends.Length; i++)
        {
            if (!rends[i].enabled) continue;
            string nm = rends[i].gameObject.name;
            if (NameHas(nm, "marker") || NameHas(nm, "pin") || NameHas(nm, "shade") || NameHas(nm, "compass") || NameHas(nm, "flame") || NameHas(nm, "glow")) continue;
            if (rends[i].GetComponent("TextMeshPro") != null || rends[i].GetComponent("TextMeshProUGUI") != null) continue;
            var rb = rends[i].bounds;
            if (float.IsNaN(rb.center.x) || float.IsInfinity(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue;
            if (!have) { b = rb; have = true; } else b.Encapsulate(rb);
        }
        if (have)
        {
            float near = Mathf.Max(b.extents.x, b.extents.y) * 1.7f + 0.5f;
            for (int i = 0; i < rends.Length; i++)
            {
                if (!rends[i].enabled) continue;
                if (rends[i].GetComponent("TextMeshPro") == null && rends[i].GetComponent("TextMeshProUGUI") == null) continue;
                var rb2 = rends[i].bounds;
                if (float.IsNaN(rb2.center.x) || float.IsInfinity(rb2.center.x) || rb2.size.sqrMagnitude < 1e-8f) continue;
                if ((rb2.center - b.center).sqrMagnitude > near * near) continue;
                b.Encapsulate(rb2);
            }
            center = b.center;
            float aspect = attrCam != null ? attrCam.aspect : (float)BOTTOM_W / BOTTOM_H;
            fitOrtho = Mathf.Max(b.extents.y, b.extents.x / aspect) * 1.05f;
        }
        return have;
    }

    // NOTE: hasPinRoot / hasPinShadeMarker do NOT exist in HK 1.5 — PlayerData.GetBool logs a
    // "Could not find bool named ..." line for every miss, and PinStamp runs ~3x/second on the Map
    // tab, so including them spammed logcat continuously. Only real 1.5 pin bools are listed.
    static readonly string[] PIN_BOOLS = { "hasPinBench", "hasPinStag", "hasPinTram", "hasPinBlackEgg", "hasPinGrub",
                                           "hasPinDreamPlant", "hasPinSpa", "hasPinCocoon", "hasPinShop",
                                           "hasPinGhost", "hasPinGuardian" };   // audit(168): Tot/DreamNail never existed in 1.5 (GetBool spam); Guardian (the Dreamer pin!) was missing

    // Bitmask of the hasPin* purchase bools (a handful of GetBool calls).
    static int PinStamp()
    {
        int ps = 0;
        try
        {
            var pp = PlayerData.instance;
            if (pp != null)
            {
                for (int i = 0; i < PIN_BOOLS.Length; i++) { try { if (pp.GetBool(PIN_BOOLS[i])) ps |= (1 << i); } catch { } }
                // grub rescues (a grub's pin vanishes when saved) + Grimmchild equip (flame pins) so live GateMarkers re-fires
                try { if (pp.scenesGrubRescued != null) ps = ps * 31 + pp.scenesGrubRescued.Count; } catch { }
                try { if (pp.equippedCharm_40) ps ^= 0x40000000; } catch { }
                // fix#6(157-fb): PLACED map markers (the buyable r/b/y/w flags) were absent from the stamp —
                // placing one on the LB map never refreshed the bottom map until a tab bounce.
                try
                {
                    ps = ps * 31 + (pp.placedMarkers_r != null ? pp.placedMarkers_r.Count : 0);
                    ps = ps * 31 + (pp.placedMarkers_b != null ? pp.placedMarkers_b.Count : 0);
                    ps = ps * 31 + (pp.placedMarkers_y != null ? pp.placedMarkers_y.Count : 0);
                    ps = ps * 31 + (pp.placedMarkers_w != null ? pp.placedMarkers_w.Count : 0);
                }
                catch { }
                // audit(168): dreamer kills re-gate their pins; lifeblood/root encounters place
                // NEW native pins (SetupMap children), so they must re-stamp too.
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
        }
        catch { }
        return ps;
    }

    // Everything the bottom map's CONTENT depends on: the resolved area, mapped-room count (+mapAllRooms), quill, per-zone
    // map paper, Wayward Compass, Grimmchild (flame pins), pin purchases, placed markers. Taken when the tab is stowed and
    // again on return: equal -> the cached clone is still exact, only the compass needs a re-anchor (no SetupMap /
    // SetupQuickMap / re-fit). A plain room move never changes it.
    int MapContentStamp()
    {
        try
        {
            var pd = PlayerData.instance; var gm = GameManager.instance;
            if (pd == null || gm == null) return -1;
            int h = 17;
            h = h * 31 + ResolveMapArea(gm.GetCurrentMapZone()).GetHashCode();
            h = h * 31 + (pd.scenesMapped != null ? pd.scenesMapped.Count : 0) + (pd.mapAllRooms ? 1000000 : 0);
            h = h * 31 + (pd.hasQuill ? 1 : 0) + (pd.equippedCharm_2 ? 2 : 0) + (pd.equippedCharm_40 ? 4 : 0) + (HasMapForCurrentZone() ? 8 : 0);
            h = h * 31 + PinStamp();
            int mk = 0;
            if (pd.placedMarkers_b != null) mk += pd.placedMarkers_b.Count;
            if (pd.placedMarkers_r != null) mk += pd.placedMarkers_r.Count;
            if (pd.placedMarkers_y != null) mk += pd.placedMarkers_y.Count;
            if (pd.placedMarkers_w != null) mk += pd.placedMarkers_w.Count;
            h = h * 31 + mk;
            return h;
        }
        catch { return -1; }
    }

    // Build (Instantiate) the Map clone once. Freezes its FSMs and defers WorldMap()/SetupMap() to the poll.

    // fix(1.0.1g) [user: "sometime compass char icon shown in wrong place ... shown where its dead instead of
    // that bench"]: PositionCompass resolves the player's map position by finding a child of the zone's area
    // object NAMED after the current scene — but for INTERIOR rooms (Ancestral Mound, stag stations, shops…)
    // there is no such child, so HK instead resolves via inRoom/doorScene/doorMapZone: the door you came in by.
    // Those live on the REAL GameMap and our clone never receives them, so interiors failed to resolve and the
    // clone silently kept the PREVIOUS room's currentScene — leaving the compass parked at the old location
    // (the death room) after a respawn. Mirror the real map's room state before every re-anchor.
    GameMap realMapGm;
    static System.Reflection.FieldInfo fiOX, fiOY, fiSW, fiSH;

    // fix(1.0.1h) [user: "lb map is correct, bottom map still shown the place it dead"]: the MAIN screen gets
    // this right, so rather than re-deriving the player's map position on the clone (which fails for interior
    // rooms — Ancestral Mound, stag stations, shops — because PositionCompass looks for a map child NAMED after
    // the scene and there isn't one), copy the REAL map's already-resolved answer. GameMap.Update() recomputes
    // the icon every frame from currentScene + currentScenePos + the tilemap dimensions, so mirroring those
    // makes the bottom compass agree with the main screen by construction. currentScene is only read for its
    // sprite size, so pointing at the real map's object is safe.
    void MirrorRoomState(GameMap m)
    {
        if (m == null) return;
        try
        {
            if (realMapGm == null)
            {
                var go = GameManager.instance != null ? GameManager.instance.gameMap : null;
                if (go != null) realMapGm = go.GetComponent<GameMap>();
            }
            var r = realMapGm;
            if (r == null)
            {   // fallback: HK persists the door in the save, so it survives without the live map
                var pd = PlayerData.instance;
                if (pd != null && !string.IsNullOrEmpty(pd.gMap_doorScene))
                { m.doorScene = pd.gMap_doorScene; m.doorMapZone = pd.gMap_doorMapZone; }
                return;
            }
            if (r.currentScene != null) { m.currentScene = r.currentScene; m.currentScenePos = r.currentScenePos; }
            m.inRoom = r.inRoom; m.doorScene = r.doorScene; m.doorMapZone = r.doorMapZone;
            m.doorX = r.doorX; m.doorY = r.doorY; m.doorOriginOffsetX = r.doorOriginOffsetX;
            m.doorSceneWidth = r.doorSceneWidth; m.doorSceneHeight = r.doorSceneHeight;
            // the tilemap dimensions the icon is scaled against are private; the clone's own LevelReady can
            // latch the wrong scene's values (see the RefreshTilemapInfo guard), so take the real map's.
            if (fiOX == null)
            {
                var t = typeof(GameMap);
                var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                fiOX = t.GetField("originOffsetX", F); fiOY = t.GetField("originOffsetY", F);
                fiSW = t.GetField("sceneWidth", F);    fiSH = t.GetField("sceneHeight", F);
            }
            if (fiOX != null) fiOX.SetValue(m, fiOX.GetValue(r));
            if (fiOY != null) fiOY.SetValue(m, fiOY.GetValue(r));
            if (fiSW != null) fiSW.SetValue(m, fiSW.GetValue(r));
            if (fiSH != null) fiSH.SetValue(m, fiSH.GetValue(r));
        }
        catch { }
    }

    void BuildMapClone(GameObject srcMap)
    {
        mapClone = Instantiate(srcMap, compRoot);
        mapClone.name = "HKMapClone";
        mapGm = mapClone.GetComponent<GameMap>();
        // The clone's GameMap.Awake subscribed its OnActiveSceneChanged to SceneManager.activeSceneChanged — on
        // every scene change that handler calls GetTilemapDimensions -> gm.RefreshTilemapInfo(null) on the REAL
        // GameManager, mid-transition, when BOTH scenes are loaded — the null-scene scan can latch the wrong
        // scene's tilemap dimensions into gm.sceneWidth/Height. The real GameMap + GameManager own that
        // bookkeeping; the clone must never touch it. (NOTE: this is NOT the dreamgate see-through-ground bug —
        // that was tk2d chunk meshes destroyed by the unloading scene copy; see FixWipedTilemap in Main.)
        try
        {
            var mi = typeof(GameMap).GetMethod("OnActiveSceneChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi != null && mapGm != null)
                UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.Scene>)
                    System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.Scene>), mapGm, mi);
            else if (mi == null) Debug.Log("HKDS map clone scene-unsub: OnActiveSceneChanged not found (stripped?) — clone handler stays subscribed");   // the WarnOnce below only covers throws; a silent null here would hide the exact mid-transition corruption this unsubscribe exists to prevent
        }
        catch (Exception e) { WarnOnce("map clone scene-unsub", e); }
        mapClone.transform.localPosition = Vector3.zero;   // pin at compRoot for reliable framing
        SetLayerRecursive(mapClone.transform, ATTR_LAYER);
        // Freeze the clone's PlayMaker FSMs so they can't fight HK's real map/inventory. GameMap's own
        // Start()/Update() still run (compass, display).
        foreach (var fsm in mapClone.GetComponentsInChildren<PlayMakerFSM>(true)) fsm.enabled = false;
        mapClone.SetActive(true);
        mapNeedsSetup = true;   // WorldMap()/SetupMap() deferred until the clone's Start() sets gm/pd/hero
        mapContentVisible = false; mapAreaBValid = false; mapAreaBFor = null; mapFitIsArea = false;   // fresh clone: nothing shown/measured yet
        realMapGm = null;
        lastMapZone = ""; lastMapScene = ""; lastScenesMapped = -1; lastPinStamp = -1; compassPending = false;
        Dbg("HKDS companion: map clone built");
    }
}
