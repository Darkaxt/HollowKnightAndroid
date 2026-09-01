using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Dual-screen for the AYN Thor (native GPU blit, GLES3). Three private/empty Unity layers are
// taken over and rendered by dedicated cameras into one RenderTexture, which libhkgpu blits onto a
// second EGL window surface on the bottom panel:
//   hudLayer(6) -> persistent HUD (Hud Canvas) + menu logo, drawn by hudCam2 (bottom, 75% top-left)
//   tutLayer(7) -> tutorials / focus / attribution / focus-heal particles, drawn by promptCam (100%)
//   ATTR_LAYER(3) -> the opening attribution + the bottom-screen COMPANION, drawn by attrCam
// The three private layers are stripped from every on-screen camera EVERY frame so nothing leaks to
// the main display (also keeps the menu logo off the main screen during the intro). Live-tunable
// values come from hkds_layout.json in persistentDataPath (hot-reloads; see HKLayout.cs).
//
// The class is a `partial` split by concern (all parts share one instance's private fields; ownership is by
// placement — a field lives in the file that owns its lifecycle):
//   HKDualScreen.cs                  [M]  MAIN GAME: MonoBehaviour lifecycle, Boot, the per-frame Tick() ORCHESTRATOR,
//                                         config load/hot-reload, aux/input bridge pushes, inventory-button toggle,
//                                         L3+R3 combo, HK HUD re-layer, tutorial/flash/heal/lore routing to the
//                                         bottom, backdrop capture, hero halo, bottom title logo, attribution.
//   HKDualScreen.Bottom.Layering.cs  [B1] BOTTOM SCREEN 1: RT + the 4 bottom cameras, private layers, native blit
//                                         (libhkgpu), dual-screen toggle, the companion SHOW/HIDE gate, RT dump.
//   HKDualScreen.Bottom.Frame.cs     [B2] BOTTOM SCREEN 2: area/context-box management — frame chrome, tab row,
//                                         tab state, clone-cache lifecycle, pane fit, UpdateCompanion/PositionFrame
//                                         orchestration, companion camera.
//   HKDualScreen.Bottom.Hud.cs       [B3] BOTTOM SCREEN 3: persistent HUD — hudCam2 mirror of HK's masks/soul/geo
//                                         + our widgets: area name, fps, battery, equipped-charm row, no-map label.
//   HKDualScreen.Bottom.Inventory.cs [B4] BOTTOM SCREEN 4: Inventory tab — clone init/settle/finalize, equipment
//                                         grid, counters, Focus lift, item->detail resolution.
//   HKDualScreen.Bottom.Select.cs    [B5] tap/select (touch bridge, hit-test, HK cursor) + the control-prompt line.
//   HKDualScreen.Bottom.Map.cs       [B6] BOTTOM SCREEN 6: Map tab — clone, quick-map, compass, watchers, framing.
//   HKDualScreen.Bottom.Charms.cs    [B7] BOTTOM SCREEN 7: Charms tab — redesign layout, detail, notch pips.
//   HKDualScreen.Util.cs             [U]  shared helpers (find/bounds/layer/TMP/localization/texture bake).
public partial class HKDualScreen : MonoBehaviour
{
    GameObject logoGo;

    bool logoNeedsBake;            // title art wasn't loaded yet at boot -> retry the bake periodically

    HKLayout cfg = new HKLayout();

    bool wired;

    int scanTick;

    Transform creditT;                                          // opening attribution ("Credits")

    Camera bgCaptureCam; HollowKnightPatches.HkBackdropDimmer bgDimmer;
    bool bgSetup; int bgCullMask; bool bgShow;

    Transform tk2dCamT;                                         // cached tk2dCamera (holds "Screen Flash(Clone)")

    Transform dreamLoreT;                                       // cached tk2dCamera/Audio/Dream Dialogue (focus-tablet lore)

    Transform dlgBoxT; PlayMakerFSM dlgBoxFsm; bool loreDialogueOpen;   // B4/B5: DialogueManager/DialogueBox ("Box Open" FSM) -> route to bottom + hide companion while reading a lore tablet
    Transform dlgNameT; int dlgNameOrigLayer = -1; Vector3 dlgNameOrigLocal; bool dlgNameRouted; float dlgNameHoldT = -1f; float dlgBoxDownT = -1f;   // fix#4(157-fb): the SPEAKER NAME plate follows the box to the bottom
    public static bool GameInventoryOpen;               // B8: true while HK's own (top-screen) inventory is open
    int lastSkinStamp;                                  // fix#6(161-fb): rebuild companion clones when skins change
    bool creditNow;                                     // fix#3(157-fb): attribution overlays the live companion

    readonly List<Transform> tutRoots = new List<Transform>();          // tutorial/focus-prompt subtrees moved to the bottom (tutLayer) -> hide companion while any is actually drawing

    readonly Dictionary<Transform, Vector3> healBase = new Dictionary<Transform, Vector3>();  // heal-fx clone -> base pos

    readonly List<Transform> healDead = new List<Transform>(); // scratch for pruning destroyed clones

    int cfgTick; long cfgMtime, cfgBadMtime;                    // config hot-reload throttle; cfgBadMtime = last mtime that failed to parse (log once per bad version)

    int compOn = -1;               // runtime companion override (-1 = follow config, 0/1 = gamepad toggle)

    bool compComboWas;             // L3+R3 toggle-combo edge latch (survives config hot-reloads)

    readonly WaitForEndOfFrame _eof = new WaitForEndOfFrame();   // cached yield object (per-frame `new` is a GC alloc)

    // GameCameras.instance / GameManager.instance log an error every time their
    // singleton has not been created yet. The launcher/title startup path can
    // legitimately run HKDualScreen before those scene owners exist, so cache
    // them through Unity's silent scene lookup and do no HUD work until both
    // are ready. Unity's destroyed-object null semantics automatically re-arm
    // discovery if either owner is ever replaced.
    GameCameras resolvedGameCameras;
    GameManager resolvedGameManager;

    bool TryResolveSceneManagers(out GameCameras cameras, out GameManager manager)
    {
        if (resolvedGameCameras == null)
            resolvedGameCameras = UnityEngine.Object.FindFirstObjectByType<GameCameras>();
        if (resolvedGameManager == null)
            resolvedGameManager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        cameras = resolvedGameCameras;
        manager = resolvedGameManager;
        return cameras != null && manager != null;
    }

    // ---- narrative overlays: move onto the bottom-screen layers by name ------------------------
    // tutorials/focus popups AND the opening attribution ("Credits") (UI layer) -> tutLayer, drawn by
    // promptCam (full-frame bottom; FrameHudCams zooms it by creditScale while the credit draws — the old
    // ATTR_LAYER/attrCam home is owned by the companion whenever it is on, which is the shipped default,
    // so credits parked there never rendered). World signposts (layer 0) + focus-tablet lore untouched.
    void ScanTutorials(int layer)
    {
        int n = UnityEngine.SceneManagement.SceneManager.sceneCount;
        for (int i = 0; i < n; i++)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!sc.isLoaded) continue;
            var roots = sc.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++) ScanNode(roots[r].transform, layer);
        }
        // GameCameras/HUD can live in Unity's hidden DontDestroyOnLoad
        // scene, which SceneManager.sceneCount does not enumerate. Scan its
        // persistent root explicitly so newly spawned attack/focus prompts
        // cannot remain on the primary display.
        var cameras = resolvedGameCameras;
        Transform persistentRoot = cameras != null ? cameras.transform.root : null;
        if (persistentRoot != null) ScanNode(persistentRoot, layer);
    }

    void ScanNode(Transform t, int layer)
    {
        var go = t.gameObject;
        string nm = go.name;   // PERF: match with NameHas (no ToLowerInvariant alloc per node — this recurses whole scenes)
        bool onUI = go.layer == UI_LAYER || go.layer == hudLayer || go.layer == layer || go.layer == ATTR_LAYER;
        bool isCredit = onUI && NameHas(nm, "credit");
        bool isTut = (go.layer == UI_LAYER || go.layer == hudLayer || go.layer == layer) &&
                     (NameHas(nm, "tutorial") || NameHas(nm, "focus_prompt"));
        if (isCredit || isTut)
        {
            RouteToLayer(t, layer);
            // target the actual credit TEXT (not "tutorial_credits_blank") for centering
            if (isCredit && !NameHas(nm, "blank")) creditT = t;
            if (isTut && !tutRoots.Contains(t)) tutRoots.Add(t);   // track -> hide companion while this tutorial actually draws on the bottom
            return;   // whole subtree moved; don't recurse further
        }
        for (int i = 0; i < t.childCount; i++) ScanNode(t.GetChild(i), layer);
    }

    // ---- lifeblood flash: soften "Screen Flash(Clone)" -----------------------------------------
    // The fullscreen lifeblood flash is "Screen Flash(Clone)", a layer-0 SpriteRenderer spawned as a
    // direct child of tk2dCamera (DontDestroyOnLoad). It renders as an opaque cyan wall; clamping the
    // SpriteRenderer's alpha scales the whole flash (peak + fade) down to a translucent pulse.
    void SoftenLifebloodFlash()
    {
        if (cfg.killBlueFlash != 1) return;
        if (tk2dCamT == null)
        {
            var c = GameObject.Find("_GameCameras/CameraParent/tk2dCamera");
            if (c != null) tk2dCamT = c.transform;
        }
        if (tk2dCamT == null) return;
        for (int i = 0; i < tk2dCamT.childCount; i++)
        {
            var ch = tk2dCamT.GetChild(i);
            if (!ch.name.StartsWith("Screen Flash")) continue;
            var sr = ch.GetComponent<SpriteRenderer>();
            if (sr == null) continue;
            if (cfg.flashAlpha <= 0f) { if (sr.enabled) sr.enabled = false; }
            else { var c = sr.color; if (c.a > cfg.flashAlpha) { c.a = cfg.flashAlpha; sr.color = c; } }
        }
    }

    // ---- focus-heal splash: route "HP Up Particles(Clone)" to the bottom -----------------------
    // The mask-fill splash is "HP Up Particles(Clone)" (ParticleSystems "Max Up 1/2"), instantiated
    // on layer 5 OUTSIDE the HUD each heal, so it gets stuck at the top-left of the main screen. Its
    // particles default to World simulation space (ignore the transform) and HK re-drives its
    // position every frame. Fix: switch the systems to Local space, then PIN base+offset every frame
    // (overriding HK) so cfg.healOffX/Y/Scale place it on the bottom, on the healed mask.
    int healFind;

    void RouteHealParticles()
    {
        if (cfg.routeHudFx != 1) return;
        // Pin tracked clones EVERY frame (HK re-drives their position) + prune destroyed ones. Cheap:
        // the dict holds ~1 entry, and is empty except during the ~1s heal effect.
        if (healBase.Count > 0)
        {
            healDead.Clear();
            foreach (var k in healBase.Keys) if (k == null) healDead.Add(k);
            for (int i = 0; i < healDead.Count; i++) healBase.Remove(healDead[i]);
            foreach (var kv in healBase)
            {
                var root = kv.Key;
                if (root == null) continue;
                root.position = kv.Value + new Vector3(cfg.healOffX, cfg.healOffY, 0f);
                if (root.gameObject.layer != tutLayer) RouteToLayer(root, tutLayer);
            }
        }
        // Catch a NEW clone only every 6th frame: GameObject.Find walks all active scene objects and
        // (on the common no-heal frame) returns null only after a full traversal — doing that every
        // frame is the lag. Every ~6 frames (~100ms) still catches the ~1s effect before it's visible.
        if ((healFind++ % 6) != 0) return;
        var found = GameObject.Find("HP Up Particles(Clone)");
        if (found == null || healBase.ContainsKey(found.transform)) return;
        var t = found.transform;
        // fix(171): HK spawns a NEW clone per heal and the previous one can still be alive, so
        // GameObject.Find (first match only) kept returning the already-tracked clone and every
        // heal after the first was never routed — it played on the MAIN screen [user]. Renaming a
        // tracked clone makes the next Find return the NEXT untracked one. Safe: no HK script
        // looks this object up by name (decomp-checked; it's referenced by the spawner directly).
        found.name = "HP Up Particles(HKDS)";
        var systems = t.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            if (main.simulationSpace != ParticleSystemSimulationSpace.Local)
            {
                main.simulationSpace = ParticleSystemSimulationSpace.Local;  // follow the transform
                systems[i].Clear(true); systems[i].Play(true);               // re-emit in local space
            }
        }
        healBase[t] = t.position;
        if (cfg.healScale != 1f) t.localScale *= cfg.healScale;
        RouteToLayer(t, tutLayer);
        Dbg($"HKDS heal-fx base=({t.position.x:F1},{t.position.y:F1})");
    }

    // ---- focus-tablet lore -> bottom -----------------------------------------------------------
    // The dream lore ("Higher beings…") is "Dream Dialogue" under tk2dCamera/Audio, on layer 5 (main).
    // Route it to the full-frame bottom camera (promptCam) so it reads on the bottom like tutorials.
    // Rendering-only move — the reader input (press to advance/close) is untouched.
    void RouteDreamLore()
    {
        if (cfg.routeHudFx != 1) return;
        if (tk2dCamT == null)
        {
            var c = GameObject.Find("_GameCameras/CameraParent/tk2dCamera");
            if (c != null) tk2dCamT = c.transform;
        }
        if (tk2dCamT == null) return;
        if (dreamLoreT == null) dreamLoreT = tk2dCamT.Find("Audio/Dream Dialogue");   // finds it even while inactive
        if (dreamLoreT != null && dreamLoreT.gameObject.layer != tutLayer)
            RouteToLayer(dreamLoreT, tutLayer);
    }

    // ---- stone lore tablets / NPC dialogue -> bottom + hide companion (B4/B5) -------------------
    // Stone lore tablets show their text through HK's shared DialogueManager/DialogueBox, whose "Box Open"
    // FSM enters state "Box Up" while a line is displayed. That path does NOT drop the Hud Canvas alpha, so
    // the HUD-alpha sync (compHudSync) never caught it -> the bottom companion stayed up over the lore, and
    // the lore never moved to the bottom. Here: find that box once, and while it is UP route it to the
    // bottom (promptCam) + set loreDialogueOpen so the companion hides. The Yes/No prompt uses a SEPARATE
    // "Box Open YN" FSM, so bench/shop confirmations are untouched.
    void RouteLoreDialogue()
    {
        loreDialogueOpen = false;
        if (cfg.compHudSync != 1) return;
        if (dlgBoxFsm == null)
        {
            if ((Time.frameCount % 30) != 0) return;   // PERF: GameObject.Find walks the whole hierarchy — retry ~2x/s, not per frame (it stays null the entire main menu)
            var m = GameObject.Find("_GameCameras/HudCamera/DialogueManager");
            if (m != null)
                foreach (var f in m.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f != null && f.FsmName == "Box Open") { dlgBoxFsm = f; dlgBoxT = f.transform; break; }
        }
        if (dlgBoxFsm == null || dlgBoxT == null) return;
        bool up = false;
        try { var st = dlgBoxFsm.ActiveStateName; up = !string.IsNullOrEmpty(st) && st.StartsWith("Box Up"); } catch { }
        // Keep the cached speaker name ACROSS the segments of one conversation (HK closes and reopens the box
        // between lines), and only drop it once the box has stayed down long enough to be a real end.
        if (!up)
        {
            // fix(1.0.1e) [user: "the name is not rendered correctly"]: HIDING the label and FORGETTING the
            // name are two different things. Holding the label up for the 1.5 s grace left it on screen after
            // the box closed — by which point the bottom camera has already returned to companion framing, so
            // it drew across the top of the map at the wrong scale. Hide it the moment the box goes down; keep
            // the cached name a little longer so the next segment of the same conversation still has it.
            SetNameClone(false);
            if (dlgBoxDownT < 0f) dlgBoxDownT = Time.unscaledTime;
            if (Time.unscaledTime - dlgBoxDownT > 1.5f) dlgNameStr = "";
        }
        else dlgBoxDownT = -1f;
        if (up)
        {
            if (dlgBoxT.gameObject.layer != tutLayer) RouteToLayer(dlgBoxT, tutLayer);   // render on the bottom
            loreDialogueOpen = true;
            // fix#2(160-fb): the SPEAKER NAME is HK's "Title Small" card (Main/Sub/Super TMPs) under
            // HudCamera — NOT a DialogueManager child, which is why the earlier sibling search missed
            // it. Route the whole card to the bottom while the box is up; CenterDialogue parks it at
            // the bottom screen's bottom-left [user].
            if (dlgNameT == null && dlgBoxT.parent != null && dlgBoxT.parent.parent != null)
                dlgNameT = FindDeep(dlgBoxT.parent.parent, "Title Small");
                // a fresh card after a scene load invalidates everything bound to the old one
                if (dlgNameT != null) { dlgNameSrcTmps = null; dlgTitleFsm = null; dlgTitleFsmSearched = false; }
            if (dlgNameT != null && !dlgNameRouted)
            {
                // fix(1.0.2) [user: "the dialog char name is not top left now slightly right"]: this used to
                // save and restore the WORLD position, but the card lives under HudCamera and HK places it in
                // LOCAL space (its FSM's SetPosition actions are all space=Self). A world position captured at
                // one moment is wrong as soon as the camera moves, so the card came back slightly off. Local.
                dlgNameOrigLayer = dlgNameT.gameObject.layer; dlgNameOrigLocal = dlgNameT.localPosition; dlgNameHoldT = -1f;
                dlgNameSeen = NameCardText(); dlgRouteT = Time.unscaledTime;   // what the card held BEFORE this conversation
                ZeroNameCard();   // never let the previous speaker's name flash through
                SetLayerRecursive(dlgNameT, tutLayer); dlgNameRouted = true;
                if (cfg.debug == 1) Dbg($"HKDS dlg name '{dlgNameT.name}' -> bottom");
            }
        }
        else if (dlgNameRouted && dlgNameT == null)
        {   // fix(1.0.1d): the card is destroyed on LEVEL LOADED. Unity's null-overload then made the restore
            // branch below unreachable and left dlgNameRouted stuck TRUE for the rest of the session.
            dlgNameRouted = false; dlgNameHoldT = -1f; dlgNameOrigLayer = -1;
        }
        else if (dlgNameRouted && dlgNameT != null)
        {   // box closed: hand the name plate back to the main screen exactly as it was.
            // fix(1.0.1c) [user: "the dialog char name after closing in bottom screen is blipping on off in
            // main screen"]: HK keeps FADING the card out for a few frames after the box goes down, so
            // restoring it the instant the box closed put those last visible frames back on the TOP screen
            // as a flash. Hold it on the bottom until it has actually faded to nothing (or 3 s have passed,
            // so a card that never clears its alpha can't strand the plate on our layer).
            if (dlgNameHoldT < 0f) dlgNameHoldT = Time.unscaledTime;
            if (NameCardAlpha() <= 0.02f || Time.unscaledTime - dlgNameHoldT > 3f)
            {
                SetLayerRecursive(dlgNameT, dlgNameOrigLayer >= 0 ? dlgNameOrigLayer : 5);
                dlgNameT.localPosition = dlgNameOrigLocal; dlgNameRouted = false; dlgNameHoldT = -1f;
            }
        }
        if (cfg.debug == 1 && up && (Time.frameCount % 45) == 0) Dbg("HKDS lore/dialogue BOX UP -> bottom + hide companion");
    }

    // ---- dimmed environment-only backdrop ------------------------------------------------------
    // A dedicated camera mirrors the main game camera but culls the character + dynamic actors, so
    // only scenery renders into bgRT. The native plugin draws bgRT dimmed + centre-cropped behind
    // the HUD. bgBlur renders it at 1/N res -> the plugin's bilinear upscale smears it to a soft wash.
    void SetupBgCapture(GameCameras gc)
    {
        if (bgSetup) return;
        Camera main = gc.mainCamera != null ? gc.mainCamera : Camera.main;
        if (main == null) return;
        // exclude: Player(9) Enemies(11) Projectiles(12) HeroDetector(13) EnemyDetector(15)
        // Tinker(16) Attack(17) HeroBox(20) EnemyAttack(22) + our private layers
        int excl = (1 << 9) | (1 << 11) | (1 << 12) | (1 << 13) | (1 << 15) | (1 << 16) | (1 << 17)
                 | (1 << 20) | (1 << 22) | (1 << HUD_LAYER) | (1 << TUT_LAYER);
        bgCullMask = ~excl;
        if (transport == null) return;
        bgCaptureCam = transport.CreateRoleCamera(
            "HKBgCaptureCam", 0, 97, CameraClearFlags.SolidColor);
        bgDimmer = bgCaptureCam.gameObject.AddComponent<HollowKnightPatches.HkBackdropDimmer>();
        bgSetup = true;
        Dbg($"HKDS bg cam on '{main.name}' mask=0x{bgCullMask:X}");
    }

    void SyncBgCapture(GameCameras gc)
    {
        if (bgCaptureCam == null || gc.mainCamera == null) return;
        // PERF: the backdrop cam renders the WHOLE scene a second time into bgRT. Only the gameplay backdrop
        // (bgShow) ever uses it — menus/pause/inventory/dual-screen-off show black+logo, and a bottom popup
        // (popupBlack) wants clean black behind it — so disable the cam (and skip the CopyFrom) when unused.
        bgCaptureCam.enabled = directDisplayActive && bgShow && !popupBlack;
        if (clearCam != null)
            clearCam.clearFlags = bgCaptureCam.enabled
                ? CameraClearFlags.Depth : CameraClearFlags.SolidColor;
        if (!bgCaptureCam.enabled) return;
        var m = gc.mainCamera;
        bgCaptureCam.CopyFrom(m);                       // match projection/zoom/clip exactly
        bgCaptureCam.aspect = (float)BOTTOM_W / BOTTOM_H;
        int wantMask = cfg.bgMask != 0 ? cfg.bgMask : bgCullMask;
        bgCaptureCam.cullingMask = wantMask & ~((1 << HUD_LAYER) | (1 << TUT_LAYER));   // never our private layers
        bgCaptureCam.targetTexture = null;
        bgCaptureCam.targetDisplay = transport.TargetDisplayIndex;
        bgCaptureCam.clearFlags = CameraClearFlags.SolidColor;
        bgCaptureCam.backgroundColor = Color.black;
        bgCaptureCam.depth = 97;
        bgCaptureCam.transform.position = m.transform.position;
        bgCaptureCam.transform.rotation = m.transform.rotation;
        if (bgDimmer != null)
        {
            bgDimmer.Brightness = HkStageHooks.BlackBackground ? 0f : cfg.dim;
            bgDimmer.BlurFactor = cfg.bgBlur;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // On Android the hub screen (HKBoot) owns the display until the user presses "Start game",
        // and there is nothing to companion until then — no bottom cameras, no native blit, no
        // per-frame work. HKBoot calls EnsureStarted() at that moment.
        //
        // Deliberately NOT gated on HKBoot.SetupActive: both Boot() methods are
        // RuntimeInitializeOnLoad, so reading a static the other one sets is a bet on their
        // relative order. Checking the platform instead makes the decision order-independent.
        if (Application.platform == RuntimePlatform.Android) return;
        EnsureStarted();
    }

    static bool started;

    // Idempotent: the companion is created either at process start (data already good) or the
    // moment the hub hands over to the game. Both paths land here so neither can double-create it.
    public static HKDualScreen EnsureStarted()
    {
        if (started && activeInstance != null) return activeInstance;
        started = true;
        var go = new GameObject("HKDualScreen");
        DontDestroyOnLoad(go);
        activeInstance = go.AddComponent<HKDualScreen>();
        return activeInstance;
    }
    static HKDualScreen activeInstance;
    bool lowerHudFixtureActive;

    // Config hot-reload: check the file's mtime a few times a second and only re-parse on change
    // (avoids per-frame disk I/O + GC while keeping the adb-push live-tuning workflow).
    void LoadConfig(bool force)
    {
        if (!force && (cfgTick++ % 20) != 0) return;
        try
        {
            string p = Path.Combine(Application.persistentDataPath, "hkds_layout.json");
            if (!File.Exists(p)) return;
            long mt = File.GetLastWriteTimeUtc(p).ToFileTimeUtc();
            if (mt == cfgMtime && !force) return;
            // Parse into a TEMP and only then commit cfg + cfgMtime. Previously cfgMtime was advanced BEFORE the parse,
            // so a half-written / truncated / empty file (adb push mid-write, bad edit) threw, was swallowed, and the
            // mtime gate then rejected that same file forever -> hot-reload silently WEDGED until the file was touched
            // again. (JsonUtility.FromJson("") THROWS rather than returning null, so the old `?? new HKLayout()` guard
            // never helped.) Now a bad file keeps the previous config and is retried on the next tick.
            string txt = File.ReadAllText(p);
            if (string.IsNullOrWhiteSpace(txt)) return;   // mid-write / empty -> retry next tick, keep cfgMtime
            // Overlay the file onto a fully initialized layout. FromJson<T>
            // zeroes fields omitted by a partial tuning file on this IL2CPP
            // runtime, despite HKLayout's field initializers; that can turn a
            // harmless {"debug":1} edit into a collapsed/disabled companion.
            HKLayout parsed = new HKLayout();
            try { JsonUtility.FromJsonOverwrite(txt, parsed); }
            catch (Exception pe)
            {
                if (mt != cfgBadMtime) { cfgBadMtime = mt; Debug.Log($"HKDS cfg parse error (keeping previous config, will retry): {pe.Message}"); }   // once per bad file version, not 3x/s
                return;
            }
            cfg = parsed; cfgMtime = mt;
            OnConfigReloaded();    // module re-apply hooks (B2 frame rebuild for build-time knobs, B4 grid, fit)
            LogUnknownConfigKeys(txt);
        }
        catch (Exception e) { Debug.Log($"HKDS cfg error {e.Message}"); }
    }

    // Unknown-key audit: JsonUtility silently IGNORES keys that don't match an HKLayout field, so a stale/renamed key
    // in hkds_layout.json is an invisible no-op (the deployed file already carried 7 such keys). Log them once per
    // reload so a live-tuning edit that "does nothing" is diagnosable, and so pruned keys can be cleaned out.
    static HashSet<string> _cfgFieldNames;

    static void LogUnknownConfigKeys(string json)
    {
        try
        {
            if (_cfgFieldNames == null)
            {
                _cfgFieldNames = new HashSet<string>();
                foreach (var f in typeof(HKLayout).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)) _cfgFieldNames.Add(f.Name);
            }
            var unknown = new List<string>();
            // top-level keys only: `"name"` followed by optional whitespace and ':' (values are flat scalars/strings)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"([A-Za-z_][A-Za-z0-9_]*)\"\\s*:"))
            { var k = m.Groups[1].Value; if (!_cfgFieldNames.Contains(k) && !unknown.Contains(k)) unknown.Add(k); }
            if (unknown.Count > 0) Debug.Log($"HKDS cfg unknown keys ({unknown.Count}, ignored): {string.Join(", ", unknown)}");
        }
        catch { }
    }

    IEnumerator Start()
    {
        while (transport == null && !directDisplayShuttingDown) yield return null;
        if (directDisplayShuttingDown) yield break;
        LoadConfig(true);
        SetupBottomCameras();
        SetupLogo();
        // One-shot GPU capability report (asset-pipeline planning: can this device use the PC's DXT/BC textures
        // directly, or must the OBB stay ASTC?). One log line at boot, debug-independent.
        try
        {
            Debug.Log("HKDS texcaps DXT1=" + SystemInfo.SupportsTextureFormat(TextureFormat.DXT1)
                    + " DXT5=" + SystemInfo.SupportsTextureFormat(TextureFormat.DXT5)
                    + " BC4=" + SystemInfo.SupportsTextureFormat(TextureFormat.BC4)
                    + " BC7=" + SystemInfo.SupportsTextureFormat(TextureFormat.BC7)
                    + " ASTC6x6=" + SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_6x6)
                    + " gpu='" + SystemInfo.graphicsDeviceName + "' api=" + SystemInfo.graphicsDeviceType);
        }
        catch { }

        // ONE per-frame orchestrator. The body lives in Tick() so it can be try/caught: C# forbids `yield` inside a
        // try that has a catch, and before this a single uncaught exception (destroyed attrCam on scene reload, a
        // dead TMP in frameTabs, a clone destroyed mid-frame...) terminated the coroutine PERMANENTLY -> HUD mirror,
        // backdrop, companion AND the native blit all stopped for the rest of the session with no recovery.
        while (!directDisplayShuttingDown)
        {
            try
            {
                if (directDisplayRestorePending)
                    RetryPendingDirectDisplayRestore();
                else
                    Tick();
            }
            catch (Exception e) { TickError(e); }
            yield return _eof;   // PERF: cached; `new WaitForEndOfFrame()` every frame is a GC alloc
        }
    }

    // Rate-limited error reporter for Tick(): first occurrence of a distinct message immediately, then at most one
    // line per 5 s, so a permanently-broken frame doesn't flood logcat but is never silent either.
    string _lastTickErr; float _nextTickErrLog; int _tickErrN;

    void TickError(Exception e)
    {
        _tickErrN++;
        string msg = e.GetType().Name + ": " + e.Message;
        if (msg != _lastTickErr || Time.unscaledTime >= _nextTickErrLog)
        {
            _lastTickErr = msg; _nextTickErrLog = Time.unscaledTime + 5f;
            Debug.Log($"HKDS tick err #{_tickErrN} {msg}\n{e.StackTrace}");
        }
    }

    // The per-frame body (runs at end-of-frame from the Start() coroutine). This is the ORCHESTRATOR: it owns the
    // frame's control flow and calls into the modules in a fixed order. ORDER MATTERS:
    //   bgShow before SyncBgCapture; RouteLoreDialogue (sets loreDialogueOpen) before the companion gate; dsOn before
    //   the camera enables; PollTouch AFTER UpdateCompanion (a tap takes effect next frame); PushToBottom LAST.
    void Tick()
    {
        LoadConfig(false);
        GameCameras gc;
        GameManager gm;
        if (!TryResolveSceneManagers(out gc, out gm)) return;
        if (TryRunLowerHudFixture(gc, gm)) return;
        HkStageHooks.Tick(cfg, cfg.debug == 1);
        SyncDumpHook();   // B1: RT->PNG dump hook only while compDumpRT=1
        // A touch tab-select (tab.tap) overrides cfg.compTab indefinitely; if the config tab actually
        // changes (hot-reload), clear the override so config regains control instead of being stuck.
        if (cfg.compTab != tab.lastCfg) { tab.lastCfg = cfg.compTab; tab.tap = -1; }
        PushInputSettings();      // M : config -> HKControllerService / HKInputDebug

        bool paused = false;
        try { paused = gm.IsGamePaused(); } catch { }

        bool invOpen = PollInventoryToggle(gc, paused);   // M : invBtn toggle + is-inventory-open
        GameInventoryOpen = invOpen;                      // B8: HKTweaks charms-anywhere gates its atBench force on this
        PollCompanionCombo();                             // M : L3+R3 runtime companion toggle
        bool dsOn = ApplyDualScreenToggle();              // B1: dual-screen master + camera enables (BEFORE companionOn)
        // Display loss, pause teardown, or a product toggle has already run
        // SetTransportActive(false), which restores the live HUD and every
        // routed overlay. Stop here so none of the reference bottom-screen
        // hooks can route those objects again while transport is inactive.
        if (!dsOn) return;
        StripPrivateLayers();     // B1: keep the 3 private layers off every on-screen camera
        bool companionOn = dsOn && (compOn >= 0 ? compOn : cfg.companion) == 1;

        bool overlay = paused || invOpen || !dsOn;   // pause/inventory OR dual-screen-off -> HUD to top, bottom blank
        // Backdrop + HUD only during ACTIVE gameplay; menus/intro/cutscenes AND pause/inventory -> black+logo.
        // Computed here (before SyncBgCapture) so the backdrop-cam gate uses this frame's value.
        bgShow = gc != null && gc.hudCanvas != null && gc.hudCanvas.transform.gameObject.activeInHierarchy && !overlay;
        bool hudFadedInGameplay = HudFadedInGameplay(gc);   // B1: HK faded its HUD (lore tablet / cutscene) -> hide companion

        // Session boundary: QUIT-TO-MENU tears the companion down and re-arms the startup tab, so the next game
        // load starts fresh on cfg.compTab (Map) with re-cloned panes — the new save's data and any language
        // changed in the menu included. (Pause/inventory only HIDE the clones — deliberately, to avoid rebuild
        // stutter; the menu is the one real reset point. Without this the old tab + stale-language clones survived.)
        bool atMenu = false; try { atMenu = gm.gameState == GlobalEnums.GameState.MAIN_MENU; } catch { }
        if (atMenu && !wasAtMenu && (mapClone != null || invCloneCache != null || charmCloneCache != null || tab.built != -1))
        {
            TeardownCompanion();
            tab.tap = -1;   // back to the configured startup tab (Map)
            Dbg("HKDS session reset: quit-to-menu -> companion torn down, tab -> cfg.compTab");
        }
        wasAtMenu = atMenu;

        if (gc != null && gc.hudCamera != null)
        {
            var src = gc.hudCamera;
            // fix(1.0.2): with dual-screen OFF, hand everything back to the top screen instead of leaving it
            // stranded on a private layer no enabled camera renders. The non-routing hooks still run — the
            // lifeblood-flash softener and the dreamgate tilemap repair are main-screen fixes, not bottom ones.
            bool dsOff = cfg.dualScreen == 0;
            RelayerHud(gc, overlay || dsOff);   // M : HK's HUD subtree -> bottom (hudLayer) or back to the top
            if (dsOff)
            {
                if (routedLayers.Count > 0) { RestoreRoutedLayers(); RestoreNameCard(); Dbg("HKDS dual-screen OFF -> routed objects returned to the main screen"); }
                SoftenLifebloodFlash();
                FixWipedTilemap();
            }
            else MainGameHooks(gc);   // M : tutorial/flash/heal/lore routing (RouteLoreDialogue sets loreDialogueOpen)
            // Popup gate, computed ONCE per frame after the routing hooks: a dialogue box, a tutorial prompt or
            // the attribution card drawing on the bottom hides the companion AND (compPopupBlack) blanks the HUD
            // mirror + backdrop, so the popup reads on clean black.
            bool creditShowing = CreditShowing();
            creditNow = creditShowing;
            // fix#3(157-fb): the attribution no longer blanks the whole bottom — the companion stays up
            // and only the map content + "Map not acquired yet" hide (UpdateCompanion / PositionHudStrip),
            // with the credit drawing over it on promptCam (depth 101 > companion 99/100).
            bool popupAny = loreDialogueOpen || BottomOverlayActive();
            popupBlack = cfg.compPopupBlack == 1 && popupAny;
            SetupBgCapture(gc);        // M : backdrop capture (after popupBlack so its gate is same-frame)
            SyncBgCapture(gc);
            ApplyMainFocusLift();      // M : lift the REAL inventory's Focus+3-spells cluster (edits the Check Active FSMs' Up Y/Down Y once)
            if (invOpen) FixEmptyCounterDetail();   // M : mask-shard / vessel counters with 0 found: swap HK's empty *_NONE convo keys for the informative *_0 ones (match the bottom screen)
            // fix#6(161-fb): skin textures changed -> the clones hold material copies frozen at clone
            // time; rebuild them so the bottom screen wears the skin too.
            if (HkStageHooks.SkinStamp != lastSkinStamp) { lastSkinStamp = HkStageHooks.SkinStamp; InvalidateCompanionClones(); }
            if (cfg.debug == 1) { InvStateDiag(); DebugPopupTick(); }   // M : debug — inventory FSM trace + popup glyph repro/diag
            FrameHudCams(src, popupBlack, creditShowing);   // B3: hudCam2 (HUD mirror; blanked behind popups) + promptCam framing (credit zoom + dialogue centring)
            SyncBottomFade();          // B1: mirror the main screen's CameraFade (room/death/cutscene dims) onto the bottom

            if (companionOn) PrewarmTick();   // B2: build/settle the Inventory+Charms clones in the background (during the black load fade-in if possible)

            // attrCam belongs to the companion. (The opening attribution used to time-share it, but now
            // lives on tutLayer/promptCam — see ScanNode/CreditShowing.)
            if (CompanionVisible(companionOn, overlay, hudFadedInGameplay, popupAny))
            {
                attrCam.cullingMask = 1 << ATTR_LAYER;   // show companion
                UpdateCompanion(src);
                PollTouch();
            }
            else if (companionOn)
            {
                // Overlay/popup up (pause / real select-map / dialogue / tutorial / credit). KEEP the clone
                // alive (no teardown -> no rebuild/re-render when it closes); just hide it.
                attrCam.cullingMask = 0;
            }
            else
            {
                // Companion OFF -> tear it down; nothing renders on ATTR_LAYER.
                if (mapClone != null || tab.built != -1) TeardownCompanion();
                attrCam.cullingMask = 0;
            }
            if (creditShowing) CenterAttribution();   // M : keep the credit card on promptCam's view axis
            if (!wired) { Dbg($"HKDS wired (direct display) hud={hudLayer} tut={tutLayer}"); wired = true; }
            ApplyHalo();
        }
        else
        {
            hudCam2.cullingMask = 1 << hudLayer;   // menu/intro: just the logo on the bottom
            popupBlack = false;
        }

        LogoTick();      // M : bottom title logo bake/retry + tab-fleur bake + logo visibility
        PushToBottom();  // B1: native blit — LAST
    }

    // [M] Push virtual-controller / glyph settings to the input bridge (it owns the on-screen touch overlay).
    void PushInputSettings()
    {
        // P4: HKControllerService owns TouchEnabled (auto by hasGamepad).
        HkStageHooks.PushInputSettings(cfg);
        // P3/P6: HKControllerService owns the glyph family via direct activeGamepadType writes (the old
        // reflection forcing — fixControls/controllerStyle/gamepadAlias — is deleted). forceType=AUTO detects
        // the pad's brand from its VID/PID (P6); NINTENDO/XBOX/PS4/PS5/NONE force a family for testing.
    }

    // [M] Inventory: (a) toggle it from a chosen button — HK's openInventory (InControl Select/View/Back)
    // never reaches the Thor pad and HK exposes no rebind; (b) track whether it's open so the
    // bottom screen goes to black+logo (like pause) while it's up. Returns invOpen.
    bool PollInventoryToggle(GameCameras gc, bool paused)
    {
        bool invOpen = false;
        try
        {
            var g = GameManager.instance;
            var invFsm = g != null ? g.inventoryFSM : null;
            if (invFsm != null)
            {
                string invState = invFsm.ActiveStateName;
                invOpen = invState != "Closed";
                if (gc != null && !paused && cfg.invBtn >= 0 && cfg.invBtn <= 19 &&
                    Input.GetKeyDown(HkStageHooks.JoyBtn(cfg.invBtn)))
                {
                    // Native toggle. Open: "BUTTON PRESSED" (what Closed's own listener raises).
                    // Close: "CLOSE" — the event both openInventory AND B/menuCancel raise; it routes
                    // through the FSM's "Regain Control" state, which calls HeroController.RegainControl()
                    // so hero input is restored. (The old INVENTORY CANCEL path hit "R Lock Close" /
                    // "Regain Control 2", which restore timeScale but NOT input → dead pad.) CLOSE is only
                    // handled in "Opened" and BUTTON PRESSED in "Closed"; mid-transition presses are
                    // ignored, which avoids dropped or duplicated toggles. (InjectCancelFrames B-press is
                    // kept in HKInputDebug as a labeled fallback but no longer the primary path.)
                    if (invState == "Closed") invFsm.SendEvent("BUTTON PRESSED");
                    else if (invState == "Opened") invFsm.SendEvent("CLOSE");
                }
            }
        }
        catch (Exception e) { Debug.Log($"HKDS inv err {e.Message}"); }
        return invOpen;
    }

    // [M] Gamepad toggle for the companion (Android 13 blocks editing the on-device config without
    // adb): hold L3+R3 to flip it on/off. This runtime override survives config hot-reloads and
    // doubles as an escape hatch if the companion render misbehaves.
    void PollCompanionCombo()
    {
        bool combo = Input.GetKey(HkStageHooks.JoyBtn(8)) && Input.GetKey(HkStageHooks.JoyBtn(9));
        if (combo && !compComboWas)
        {
            int cur = compOn >= 0 ? compOn : cfg.companion;
            compOn = (cur == 1) ? 0 : 1;
            Debug.Log($"HKDS companion {(compOn == 1 ? "ON" : "OFF")} (L3+R3)");
        }
        compComboWas = combo;
    }

    // [M] Move the whole top-left HUD anchor (Anchor TL, two levels above Hud Canvas) onto the
    // HUD layer — so effects that spawn near the masks but outside Hud Canvas also go bottom.
    void RelayerHud(GameCameras gc, bool overlay)
    {
        if (gc.hudCanvas != null)
        {
            var hudRoot = gc.hudCanvas.transform;
            if (hudRoot.parent != null && hudRoot.parent.parent != null)
                hudRoot = hudRoot.parent.parent;   // Anchor TL (fallback: Hud Canvas)
            // While an overlay is up (PAUSE or INVENTORY), put the HUD back on its original UI
            // layer so it leaves the bottom -> the bottom shows just black + logo like the intro.
            // In normal play, route it to the private HUD layer (bottom).
            // PERF: the recursive walk over the whole HUD subtree ran EVERY frame; now on change / root change /
            // every 10 frames (catches HUD children HK spawns later, e.g. new mask/soul pieces) — invisible delay.
            int wantLayer = overlay ? UI_LAYER : hudLayer;
            if (wantLayer != hudLayerApplied || hudRoot != hudRootApplied || (Time.frameCount % 10) == 0)
            { SetLayerRecursive(hudRoot, wantLayer); hudLayerApplied = wantLayer; hudRootApplied = hudRoot; }
        }
    }
    int hudLayerApplied = -1; Transform hudRootApplied;   // RelayerHud change-detect

    bool popupBlack;   // this frame: a popup draws on the bottom AND compPopupBlack wants the HUD/backdrop blanked behind it

    // [M] Per-frame main-game hooks, in this order: tutorial scan, lifeblood-flash soften, heal-particle /
    // dream-lore / lore-dialogue routing to the bottom (RouteLoreDialogue sets loreDialogueOpen). The backdrop
    // capture runs from Tick AFTER these, so its popup gate sees this frame's routing result.
    // The speaker-name card is not just re-layered, it is repositioned too, and our stand-in label lives on
    // the dialogue box — so it needs handing back explicitly, not just via the layer map.
    void RestoreNameCard()
    {
        try { RestoreNameCardOrThrow(); }
        catch (Exception e) { WarnOnce("name card restore", e); }
    }

    // Transport shutdown must know whether the original ownership was really
    // restored. Keep state intact on failure so DirectDisplay can retry.
    void RestoreNameCardOrThrow()
    {
        if (dlgNameRouted && dlgNameT != null)
        {
            SetLayerRecursive(dlgNameT, dlgNameOrigLayer >= 0 ? dlgNameOrigLayer : UI_LAYER);
            dlgNameT.localPosition = dlgNameOrigLocal;
        }
        SetNameClone(false); dlgNameStr = "";
        dlgNameRouted = false; dlgNameHoldT = -1f; dlgNameOrigLayer = -1; loreDialogueOpen = false;
    }

    void MainGameHooks(GameCameras gc)
    {
        if ((scanTick++ % 30) == 0) ScanTutorials(tutLayer);
        SoftenLifebloodFlash();
        RouteHealParticles();
        RouteDreamLore();
        RouteLoreDialogue();   // B4/B5: stone-tablet / NPC dialogue box -> bottom + set loreDialogueOpen
        FixWipedTilemap();     // dreamgate same-scene reload wipes the scene tilemap's render meshes -> detect + ForceBuild
    }

    bool tilemapCheckPending;   // armed by any scene transition; one post-arrival check per transition (edge-based, NOT
    int tilemapFixDelay;        // scene-name-based: a dreamgate reloads the SAME scene, so names never change)

    // Dreamgate teleports reload the CURRENT scene: for a moment two copies of it are loaded, and when the old copy
    // unloads, tk2dTileMap.OnDestroy's mesh cleanup (SpriteChunk.DestroyGameData -> DestroyMesh) takes the surviving
    // scene's chunk RENDER meshes with it — zero-vertex "Scenemap" chunks, so the flat ground fill vanishes and the
    // backdrop shows through below the floor. Collider meshes are separate objects and survive (floor stays solid).
    // Proven by renderer-dump diff 2026-08-17: clean Chunk 0 1/0 2 bounds y 0..32 -> bugged y 0..0 / missing.
    // No game code calls Build() at runtime and the DLL can't be patched, so: detect empty chunks after every
    // transition settles and rebuild in place via the public tk2dTileMap.Build(ForceBuild) (regenerates render +
    // collider data from the tilemap's serialized `data` + `spriteCollection`; heals identically to a room change).
    void FixWipedTilemap()
    {
        try
        {
            var g = GameManager.instance; if (g == null) return;
            bool settled = false;
            try { settled = !g.IsInSceneTransition && g.gameState == GlobalEnums.GameState.PLAYING; } catch { }
            if (!settled) { tilemapCheckPending = true; tilemapFixDelay = 20; return; }
            if (!tilemapCheckPending) return;
            if (tilemapFixDelay-- > 0) return;                     // ~1/3 s after PLAYING so the load fully settles
            tilemapCheckPending = false;
            tk2dTileMap tm = null; try { tm = g.tilemap; } catch { }
            if (tm == null || tm.renderData == null) return;
            var mfs = tm.renderData.GetComponentsInChildren<MeshFilter>(false);
            int alive = 0; bool wiped = false;
            for (int i = 0; i < mfs.Length; i++)
            {
                var m = mfs[i] != null ? mfs[i].sharedMesh : null;
                if (m == null) continue;                           // destroyed mesh reads fake-null
                alive++;
                if (m.vertexCount == 0) { wiped = true; break; }   // alive but cleared
            }
            if (!wiped && mfs.Length > 0 && alive == 0) wiped = true;   // filters exist, every mesh destroyed
            if (!wiped) return;
            try { tm.Build(tk2dTileMap.BuildFlags.ForceBuild); }
            catch (Exception be) { Debug.Log("HKDS tilemap rebuild err " + be.Message); return; }
            // verify: did the rebuild actually restore vertices? (Build no-ops if data/spriteCollection are gone)
            int verts = 0;
            var mfs2 = tm.renderData != null ? tm.renderData.GetComponentsInChildren<MeshFilter>(false) : null;
            if (mfs2 != null) for (int i = 0; i < mfs2.Length; i++) { var m = mfs2[i] != null ? mfs2[i].sharedMesh : null; if (m != null) verts += m.vertexCount; }
            Debug.Log("HKDS tilemap chunks were wiped (dreamgate reload) -> ForceBuild, verts now " + verts);
        }
        catch (Exception e) { WarnOnce("tilemap fix", e); }
    }

    // [M] Show the menu logo whenever the real HUD isn't up (menus, intro, in-engine cutscenes), hide it once the
    // HUD canvas is active (bgShow). The title art (vheart_title) only loads once the menu scene is up, so a
    // boot-time bake can miss it: retry ~once a second until it succeeds (SetupLogo caches the bake). Also grabs
    // the start-screen fleur while its atlas is resident (intro/menu) and caches it for the in-game frame.
    void LogoTick()
    {
        if (logoGo == null && logoNeedsBake && (Time.frameCount % 60) == 0) { logoNeedsBake = false; SetupLogo(); }
        if (!fleurBaked && (Time.frameCount % 30) == 0) { TryBakeTabFleurs(); if (fleurBaked && frameRoot != null) TeardownFrame(); }   // fleurs baked AFTER the frame was built (first-ever run, empty cache) -> rebuild once so the real art replaces the tk2d fallback
        if (logoGo != null && logoGo.activeSelf == bgShow)
            logoGo.SetActive(!bgShow);
    }

    // Opening attribution: recentre the (freely animating) "Credits" onto promptCam's view axis via its
    // combined renderer bounds (promptCam draws tutLayer, where ScanNode now parks it; FrameHudCams zooms
    // the camera by creditScale while it draws). Do NOT force localScale — HK fades it in from scale 0, so
    // forcing it locked it at 0 (degenerate/NaN). NaN-guard the bounds (some child renderers report NaN).
    void CenterAttribution()
    {
        if (creditT == null || !creditT.gameObject.activeInHierarchy) return;
        var rends = creditT.GetComponentsInChildren<Renderer>();
        bool have = false; Bounds b = new Bounds();
        for (int i = 0; i < rends.Length; i++)
        {
            var rb = rends[i].bounds;
            if (float.IsNaN(rb.center.x) || float.IsInfinity(rb.center.x)) continue;
            if (rb.size.sqrMagnitude < 1e-8f) continue;
            if (!have) { b = rb; have = true; } else b.Encapsulate(rb);
        }
        if (!have) return;   // no valid bounds this frame -> leave it where it is
        var cam = promptCam.transform;
        float along = Vector3.Dot(b.center - cam.position, cam.forward);
        Vector3 axisPoint = cam.position + cam.forward * along;
        Vector3 delta = axisPoint - b.center;
        if (float.IsNaN(delta.x) || float.IsInfinity(delta.x)) return;
        creditT.position += delta;
    }

    // ---- hero light / halo (all config-gated; default config leaves HK's light untouched) -------
    Transform heroLight;

    Vector3 haloBaseScale = Vector3.one;

    bool haloCaptured;

    Sprite haloGlowSprite;

    int idleScan, darkTries; Camera darknessCam; bool darkSearched;

    void ApplyHalo()
    {
        try
        {
            bool tick = (idleScan++ % 20) == 0;
            if (cfg.killIdleGlow == 1 && tick)
            {
                var ig = GameObject.Find("Idle Glow");
                if (ig != null && ig.activeSelf) ig.SetActive(false);
            }
            // THE ring: the Knight's own "white_light_donut" ring-shaped light sprites.
            if (cfg.killDonut == 1 && tick)
            {
                var d1 = GameObject.Find("white_light_donut");
                if (d1 != null && d1.activeSelf) d1.SetActive(false);
                var d2 = GameObject.Find("white_light_donut2");
                if (d2 != null && d2.activeSelf) d2.SetActive(false);
            }
            // Dark-room "lit bubble" = HK's Darkness Camera (black overlay masked by the hero light).
            if (darknessCam == null && !darkSearched && tick)
            {
                var dc = GameObject.Find("Darkness Camera");
                if (dc != null) darknessCam = dc.GetComponent<Camera>();
                else if (++darkTries >= 30) darkSearched = true;   // ~10 min of 20-frame ticks without it -> stop the global Find (was: forever; darkSearched was never set)
            }
            if (darknessCam != null)
            {
                bool want = cfg.killDarkness != 1;
                if (darknessCam.enabled != want) darknessCam.enabled = want;
            }

            if (heroLight == null)
            {
                var go = GameObject.FindGameObjectWithTag("HeroLightMain");
                if (go != null) { heroLight = go.transform; haloBaseScale = heroLight.localScale; haloCaptured = true; }
            }
            // haloAlpha < 0 => leave HK's DEFAULT hero light untouched.
            if (heroLight != null && haloCaptured)
            {
                var sr = heroLight.GetComponent<SpriteRenderer>();
                if (cfg.haloSolid == 1 && sr != null)
                {
                    if (haloGlowSprite == null) haloGlowSprite = MakeGlowSprite(256);
                    if (sr.sprite != haloGlowSprite) sr.sprite = haloGlowSprite;
                    var c = sr.color; c.a = Mathf.Clamp01(cfg.haloAlpha >= 0f ? cfg.haloAlpha : 0.5f); sr.color = c;
                    heroLight.localScale = Vector3.one * Mathf.Max(0.01f, cfg.haloScale);
                }
                else if (cfg.haloAlpha >= 0f && sr != null)
                {
                    var c = sr.color; c.a = Mathf.Clamp01(cfg.haloAlpha); sr.color = c;
                    heroLight.localScale = haloBaseScale * Mathf.Max(0.01f, cfg.haloScale);
                }
            }
        }
        catch { }
    }

    Sprite MakeGlowSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) / 2f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d); a = a * a * a;   // gentle cubic falloff -> soft glow, no hard ring
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    void SetupLogo()
    {
        try
        {
            // cache -> else bake from HK's own title art (vheart_title), which is loaded at the title screen
            var tex = LoadCompanionTex("hklogo");
            if (tex == null)
            {
                var src = FindLoadedTexture("vheart_title", "vheart_title");
                if (src != null) { tex = BakeRegion(src, new Rect(0f, 0f, 1f, 1f)); if (tex != null) CacheCompanionTex("hklogo", tex); }
                Dbg($"HKDS logo bake src={(src != null ? src.name : "none")} ok={(tex != null)}");
            }
            if (tex == null) { logoNeedsBake = true; return; }   // title art not loaded yet — retry later
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            logoGo = new GameObject("HKLogo");
            DontDestroyOnLoad(logoGo);
            logoGo.layer = hudLayer;   // private layer -> only hudCam2 (bottom), never the main screen
            logoGo.transform.SetParent(hudCam2.transform, false);
            logoGo.transform.localPosition = new Vector3(0f, 0f, 10f);
            float worldW = hudCam2.orthographicSize * hudCam2.aspect * 2f * 0.7f * cfg.logoScale;
            logoGo.transform.localScale = Vector3.one * (worldW / (tex.width / 100f));
            var sr = logoGo.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 32000;
            Dbg($"HKDS logo {tex.width}x{tex.height} loaded");
        }
        catch (Exception e) { Debug.Log($"HKDS logo error {e.Message}"); }
    }

    // ==== Main-screen Focus-cluster lift ==================================================================
    // HK positions Spell Fireball/Focus/Quake/Scream each pane-open via their own "Check Active" PlayMakerFSM,
    // which writes Y from the FSM float vars "Up Y"/"Down Y" (hasNailArt branch). A transform nudge would be
    // overwritten every open — so lift the VARS once (base + cfg.compFocusDY): HK itself then places the whole
    // cluster lifted on every open, exactly like the bottom screen. The clone inherits the lifted vars via
    // Instantiate, so FinalizeInvPane skips its own NudgeFocusCluster while this is active (mainFocusBase != null).
    static readonly string[] FOCUS_SPELL_GOS = { "Spell Fireball", "Spell Focus", "Spell Quake", "Spell Scream" };
    bool wasAtMenu;                    // edge detector for the quit-to-menu session reset (Tick)
    UnityEngine.Object mainFocusSrc;   // inventoryFSM identity the lift was applied to (re-apply if HK rebuilds it)
    float[] mainFocusBase;             // captured original Up Y/Down Y per spell (4 x 2) — null until applied
    float mainFocusApplied;            // the compFocusDY currently baked into the vars (hot-reload re-applies)

    // The Focus-cluster lift, god-aware: with the God Tuner owned, HK's SetPosIfPlayerdataBool shuffle slides the
    // soul gauge into the column directly ABOVE the spells, so the full compFocusDY lift clipped it — Godhome saves
    // use the smaller compFocusDYGod instead. Applied to the FSM vars (main) AND the clone nudge fallback.
    float EffectiveFocusDY()
    {
        try { var pd = PlayerData.instance; if (pd != null && pd.hasGodfinder) return cfg.compFocusDYGod; } catch { }
        return cfg.compFocusDY;
    }

    void ApplyMainFocusLift()
    {
        try
        {
            var gm = GameManager.instance; var invFsm = gm != null ? gm.inventoryFSM : null;
            if (invFsm == null) return;
            if (!ReferenceEquals(mainFocusSrc, (UnityEngine.Object)invFsm)) { mainFocusSrc = invFsm; mainFocusBase = null; mainFocusApplied = 0f; }
            float wantDY = EffectiveFocusDY();
            if (mainFocusBase != null && mainFocusApplied == wantDY) return;   // steady state: cheap compares per frame (re-applies when cfg OR hasGodfinder changes)
            var inv = FindDeep(invFsm.transform, "Inv"); if (inv == null) return;
            var items = FindDeep(inv, "Inv_Items"); if (items == null) return;
            // collect ALL FOUR pairs first (all-or-nothing — no partial lifts if a var name ever differs)
            var ups = new HutongGames.PlayMaker.FsmFloat[4]; var dns = new HutongGames.PlayMaker.FsmFloat[4];
            for (int i = 0; i < 4; i++)
            {
                var t = FindDeep(items, FOCUS_SPELL_GOS[i]); if (t == null) return;
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                {
                    if (fsm == null || fsm.FsmName != "Check Active") continue;
                    ups[i] = fsm.FsmVariables.FindFsmFloat("Up Y"); dns[i] = fsm.FsmVariables.FindFsmFloat("Down Y");
                    break;
                }
                if (ups[i] == null || dns[i] == null) return;   // vars not found -> leave vanilla (bottom falls back to NudgeFocusCluster)
            }
            bool fresh = mainFocusBase == null;
            if (fresh) { mainFocusBase = new float[8]; for (int i = 0; i < 4; i++) { mainFocusBase[i * 2] = ups[i].Value; mainFocusBase[i * 2 + 1] = dns[i].Value; } }
            for (int i = 0; i < 4; i++) { ups[i].Value = mainFocusBase[i * 2] + wantDY; dns[i].Value = mainFocusBase[i * 2 + 1] + wantDY; }
            mainFocusApplied = wantDY;
            Dbg($"HKDS mainFocusLift applied dy={wantDY:F2} (Check Active Up/Down Y vars, 4 spells)");
        }
        catch (Exception e) { WarnOnce("main focus lift", e); }
    }

    // ==== Main-screen mask-shard / vessel-fragment detail =================================================
    // Vanilla pushes INV_NAME/DESC_HEARTPIECE_NONE (and SOULORBS_NONE) when 0 shards/fragments are held — and
    // those strings are EMPTY in every language sheet (verified EN/ES), so the cursor shows no name/desc. The
    // bottom companion uses the *_0 keys ("Mask Shards / collect four…"), so make the main screen match: when
    // the UI Inventory FSM's convo vars hold a *_NONE key, rewrite them to *_0 — and if that counter is the
    // CURRENTLY displayed item (the pane even OPENS on Heart Pieces), re-push through the Update Text FSM
    // ("Convo Name"/"Convo Desc" + the UPDATE TEXT event: Idle -> State 1 -> Change Text redraws the TMPs).
    PlayMakerFSM uiInvMain, updTextMain; UnityEngine.Object uiInvMainSrc;
    void FixEmptyCounterDetail()
    {
        try
        {
            var gm = GameManager.instance; var root = gm != null ? gm.inventoryFSM : null;
            if (root == null) return;
            if (!ReferenceEquals(uiInvMainSrc, (UnityEngine.Object)root) || uiInvMain == null)
            {
                uiInvMainSrc = root; uiInvMain = updTextMain = null;
                var inv = FindDeep(root.transform, "Inv"); if (inv == null) return;
                foreach (var f in inv.GetComponents<PlayMakerFSM>())
                {
                    if (f == null) continue;
                    if (f.FsmName == "UI Inventory") uiInvMain = f;
                    else if (f.FsmName == "Update Text") updTextMain = f;
                }
            }
            if (uiInvMain == null) return;
            bool hpFixed = SwapNoneConvo("H Piece Title Convo", "INV_NAME_HEARTPIECE_NONE", "INV_NAME_HEARTPIECE_0")
                         | SwapNoneConvo("H Piece Desc Convo",  "INV_DESC_HEARTPIECE_NONE", "INV_DESC_HEARTPIECE_0");
            bool soFixed = SwapNoneConvo("Vessel Title Convo",  "INV_NAME_SOULORBS_NONE",   "INV_NAME_SOULORBS_0")
                         | SwapNoneConvo("Vessel Desc Convo",   "INV_DESC_SOULORBS_NONE",   "INV_DESC_SOULORBS_0");
            if (!hpFixed && !soFixed) return;   // vars already informative (or some shards held) — nothing displayed empty
            // If the fixed counter is what the cursor is showing RIGHT NOW (initial state IS Heart Pieces), redraw it.
            string st = uiInvMain.ActiveStateName;
            if (updTextMain != null && ((hpFixed && st == "Heart Pieces") || (soFixed && st == "Soul Orb")))
            {
                var cn = updTextMain.FsmVariables.FindFsmString("Convo Name"); var cd = updTextMain.FsmVariables.FindFsmString("Convo Desc");
                if (cn != null) cn.Value = hpFixed && st == "Heart Pieces" ? "INV_NAME_HEARTPIECE_0" : "INV_NAME_SOULORBS_0";
                if (cd != null) cd.Value = hpFixed && st == "Heart Pieces" ? "INV_DESC_HEARTPIECE_0" : "INV_DESC_SOULORBS_0";
                updTextMain.SendEvent("UPDATE TEXT");
            }
            Dbg($"HKDS counterDetail swapped NONE->0 (hp={hpFixed} vessel={soFixed} at '{st}')");
        }
        catch (Exception e) { WarnOnce("counter detail", e); }
    }

    // Replace an exact *_NONE convo key with its *_0 twin on the UI Inventory FSM; true if it rewrote.
    bool SwapNoneConvo(string varName, string noneKey, string zeroKey)
    {
        var v = uiInvMain.FsmVariables.FindFsmString(varName);
        if (v == null || v.Value != noneKey) return false;
        v.Value = zeroKey; return true;
    }

    // ==== Debug: real-inventory state trace (mask-shard detail investigation) =============================
    // Vanilla HK fills the Heart-Pieces name/desc UNCONDITIONALLY in the pane-activation chain (Inactive
    // -ACTIVATE-> Completion Rate -> ... -> Set Heart Piece Convo -> ... -> Activate -> Determine Initial ->
    // Heart Pieces). The main screen shows empty text + no cursor + un-positioned equipment on this save, i.e.
    // the chain halts. Log the ROOT Inventory FSM + all five Inv-pane FSMs + Build Equipment List + the convo
    // vars whenever any state changes, so the halt point is visible in logcat. debug==1 only.
    PlayMakerFSM[] invDiagFsms; PlayMakerFSM invDiagRoot; string lastInvDiag;
    void InvStateDiag()
    {
        try
        {
            var gm = GameManager.instance; var root = gm != null ? gm.inventoryFSM : null;
            if (root == null) return;
            if (invDiagRoot != root || invDiagFsms == null)
            {
                invDiagRoot = root; invDiagFsms = null; lastInvDiag = null;
                var inv = FindDeep(root.transform, "Inv"); if (inv == null) return;
                var list = new List<PlayMakerFSM>();
                foreach (var f in inv.GetComponents<PlayMakerFSM>()) if (f != null) list.Add(f);                       // UI Inventory / ui_list_getinput / Update Cursor / Update Text / Button Control
                var eq = FindDeep(inv, "Equipment");
                if (eq != null) foreach (var f in eq.GetComponents<PlayMakerFSM>()) if (f != null && f.FsmName == "Build Equipment List") list.Add(f);
                invDiagFsms = list.ToArray();
            }
            if (invDiagFsms == null || invDiagFsms.Length == 0) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("root='").Append(invDiagRoot.ActiveStateName).Append('\'');
            PlayMakerFSM uiInv = null;
            foreach (var f in invDiagFsms)
            {
                if (f == null) continue;
                if (f.FsmName == "UI Inventory") uiInv = f;
                sb.Append("  ").Append(f.FsmName).Append('=').Append(f.enabled ? "" : "OFF:").Append('\'').Append(f.ActiveStateName).Append('\'');
            }
            if (uiInv != null)
            {
                var t1 = uiInv.FsmVariables.FindFsmString("H Piece Title Convo"); var t2 = uiInv.FsmVariables.FindFsmString("H Piece Desc Convo");
                sb.Append("  hp='").Append(t1 != null ? t1.Value : "?").Append("'/'").Append(t2 != null ? t2.Value : "?").Append('\'');
            }
            string line = sb.ToString();
            if (line == lastInvDiag) return;
            lastInvDiag = line;
            Debug.Log("HKDS invstate " + line);
        }
        catch (Exception e) { WarnOnce("invstate diag", e); }
    }

    // ==== Debug: popup glyph repro + diagnostic (debug==1 only) ===========================================
    // The charm-tutorial / journal-update popups carry an animated tk2d BUTTON GLYPH ("Animation" child)
    // that reportedly renders empty on device. Bump compDbgMsgN in the config to spawn the journal popup on
    // demand (HK pre-spawns it; ShowJournalUpdateMsg is its real display path), and while any such popup is
    // active the renderer dump below shows exactly which part isn't drawing and why.
    int lastDbgMsgN;
    void DebugPopupTick()
    {
        if (cfg.compDbgMsgN != lastDbgMsgN)
        {
            lastDbgMsgN = cfg.compDbgMsgN;
            try { EnemyDeathEffects.ShowJournalUpdateMsg(cfg.compDbgMsgKind == 1); Debug.Log($"HKDS dbgmsg journal popup fired full={cfg.compDbgMsgKind == 1}"); }
            catch (Exception e) { Debug.Log($"HKDS dbgmsg err {e.Message}"); }
        }
        if ((Time.frameCount % 45) != 0) return;
        var go = GameObject.Find("Journal Update Msg(Clone)") ?? GameObject.Find("Journal Update Msg")
              ?? GameObject.Find("Charm Tutorial Msg(Clone)") ?? GameObject.Find("Charm Tutorial Msg");
        if (go != null) DumpPopupDiag(go);
    }

    void DumpPopupDiag(GameObject root)
    {
        try
        {
            var sb = new System.Text.StringBuilder($"HKDS popupdiag '{root.name}' L{root.layer} pos={root.transform.position}\n");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string extra = "";
                var mf2 = r.GetComponent<MeshFilter>();
                if (mf2 != null && mf2.sharedMesh != null) extra += $" verts={mf2.sharedMesh.vertexCount}";
                foreach (var mb in r.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue; var tn = mb.GetType().Name;
                    try
                    {
                        if (tn == "tk2dSprite") extra += $" tk2dId={mb.GetType().GetProperty("spriteId")?.GetValue(mb, null)}";
                        else if (tn == "tk2dSpriteAnimator") extra += $" animPlaying={mb.GetType().GetProperty("Playing")?.GetValue(mb, null)}";
                        else if (tn.Contains("TextMeshPro")) { var tx = mb.GetType().GetProperty("text")?.GetValue(mb, null) as string; extra += $" tmp='{(tx != null && tx.Length > 28 ? tx.Substring(0, 28) : tx)}'"; }
                    }
                    catch { }
                }
                var m = r.sharedMaterial;
                sb.Append($"  {r.name} L{r.gameObject.layer} act={r.gameObject.activeInHierarchy} en={r.enabled} vis={r.isVisible}")
                  .Append($" b={r.bounds.size.x:F2}x{r.bounds.size.y:F2} mat={(m != null ? m.name : "-")} sh={(m != null && m.shader != null ? m.shader.name : "-")} tex={(m != null && m.mainTexture != null ? m.mainTexture.name : "-")}{extra}\n");
            }
            Debug.Log(sb.ToString());
        }
        catch (Exception e) { Debug.Log($"HKDS popupdiag err {e.Message}"); }
    }
}
