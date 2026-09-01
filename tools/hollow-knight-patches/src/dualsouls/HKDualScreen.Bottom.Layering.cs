using System;
using System.Collections.Generic;
using UnityEngine;

// [B1] BOTTOM SCREEN — layering + show/hide. The RenderTexture, the four bottom cameras (depth clear98 < attr99 <
// hud100 < prompt101), the three private layers, the native libhkgpu blit, the dual-screen master toggle, the companion
// SHOW/HIDE gate (overlay / HUD-faded / lore-dialogue / bottom-prompt-drawing), and the debug RT->PNG dump.
public partial class HKDualScreen
{
    const int DEFAULT_BOTTOM_W = 1240, DEFAULT_BOTTOM_H = 1080;
    int bottomWidth = DEFAULT_BOTTOM_W, bottomHeight = DEFAULT_BOTTOM_H;
    int BOTTOM_W => bottomWidth;
    int BOTTOM_H => bottomHeight;

    const int UI_LAYER = 5;

    // HK names layers 8-31; the only genuinely EMPTY Unity layers are 3,6,7 (TagManager). Layer 30 is
    // "Map Pin" — must NOT be used (the per-frame private-layer strip would blank real map pins).
    const int HUD_LAYER = 6, TUT_LAYER = 7, ATTR_LAYER = 3;

    int hudLayer = HUD_LAYER, tutLayer = TUT_LAYER;

    Camera hudCam2, promptCam, attrCam, clearCam;

    // ---- bottom fade sync (dusklight-style): mirror the MAIN screen's fade onto the bottom ----
    MeshRenderer fadeQuadMR; Transform fadeQuadT;                            // our black overlay quad (child of promptCam, TUT layer, drawn on top of the whole bottom RT)
    SpriteRenderer fadeSrc1, fadeSrc2; Material fadeSrc1Mat, fadeSrc2Mat;    // HK's fade quads + their INSTANCED materials — the CameraFade FSM animates the MATERIAL tint (_Color.a), NOT sprite.color (constant 1, probe-verified)
    UnityEngine.Object fadeSrcFor;
    float fadeEnv;                                                           // our smoothed fade envelope (0..1) driven by sampled alpha + the transition window
    float fadeGrace;                                                         // hold-black until this time after a deep sampled fade (bridges the action-exit -> load-flag gap)
    bool fadePostIn;                                                         // fade-IN under way/finished -> the hold window must not re-darken (cleared by the next fade-out)
    float fadeQuadOrtho = -1f;                                               // quad sized for this ortho (re-laid only on change)
    float fadeQuadAspect = -1f;                                              // panel aspect can change without an ortho change

    bool dsWas = true;

    Camera[] _allCams;                // reused buffer for Camera.GetAllCameras (avoids per-frame Camera.allCameras alloc)

    // True while a lore/tutorial overlay is ACTUALLY DRAWING on the bottom (tutLayer): the focus/soul tutorials,
    // dream-nail lore, etc. The companion pane is hidden while this is true so the two never overlap (user chose
    // "hide companion while it shows"). Uses renderer.isVisible — NOT mere activeInHierarchy — because HK leaves
    // tutorial objects active-but-not-drawing near the focus tablet; the old active-only gate false-fired and
    // repeatedly vanished the HUD. Heal particles and credits live elsewhere, so they don't count here.
    bool BottomOverlayActive()
    {
        if (dreamLoreT != null && dreamLoreT.gameObject.activeInHierarchy && AnyRendererDrawing(dreamLoreT)) return true;
        for (int i = tutRoots.Count - 1; i >= 0; i--)
        {
            var t = tutRoots[i];
            if (t == null) { tutRoots.RemoveAt(i); continue; }   // destroyed -> prune
            if (t.gameObject.activeInHierarchy && AnyRendererDrawing(t)) return true;
        }
        return false;
    }

    // PERF: the per-root Renderer[] used to be re-fetched EVERY frame for every tracked tutorial root (+ the dream-lore
    // node). Cache per root, refresh every 30 frames (tutorial subtrees don't grow children mid-display).
    readonly Dictionary<Transform, Renderer[]> _rootRs = new Dictionary<Transform, Renderer[]>(); int _rootRsFrame = -999;
    bool AnyRendererDrawing(Transform root)
    {
        if (Time.frameCount - _rootRsFrame > 30) { _rootRs.Clear(); _rootRsFrame = Time.frameCount; }
        if (!_rootRs.TryGetValue(root, out var rs)) { rs = root.GetComponentsInChildren<Renderer>(false); _rootRs[root] = rs; }
        for (int i = 0; i < rs.Length; i++)
        {
            var r = rs[i];
            if (r == null || !r.enabled || !r.isVisible || r.bounds.size.sqrMagnitude <= 1e-4f) continue;
            if (EffectiveAlpha(r) < 0.08f) continue;   // faded-but-active: HK parks tutorials/lore at alpha ~0 after dismiss; without this they read as "drawing" and the companion never comes back (stuck-black bug)
            return true;
        }
        return false;
    }

    Camera MakeBottomCam(string name, int layer, int depth, CameraClearFlags clear)
    {
        if (transport == null) return null;
        return transport.CreateRoleCamera(name, layer, depth, clear);
    }

    // ---- Tick() helpers, split by owning module (bodies moved verbatim from the former inline loop) ----

    // [B1] RT + the four bottom cameras. Depth band (low draws first): clear(98, whole RT) < companion(99, a sub-rect
    // BOX) < HUD(100) < tutorials/popups(101). A dedicated full-screen clear lets the companion camera be confined to
    // a box below the HUD while the rest of the RT still clears each frame (companion clears Nothing).
    void SetupBottomCameras()
    {
        if (transport == null) throw new InvalidOperationException("direct display transport is not bound");
        transport.ConfigureSharedRoleCameras();
        clearCam = MakeBottomCam("HKClearCam", 0, 98, CameraClearFlags.SolidColor);
        clearCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        clearCam.cullingMask = 0;
        attrCam = transport.AttributeCamera;
        hudCam2 = transport.HudCamera;
        hudCam2.orthographicSize = 8f;                          // placeholder; reframed every frame below
        promptCam = MakeBottomCam("HKPromptCam", tutLayer, 101, CameraClearFlags.Nothing);    // tutorials/popups on top
        SetRoleCamerasEnabled(directDisplayActive);
    }

    // [B1] Keep the three private layers OFF every on-screen camera, ALWAYS (incl. the intro/menu,
    // so the bottom-screen logo can't leak onto the main display via an "Everything"-mask cam).
    void StripPrivateLayers()
    {
        int privMask = (1 << hudLayer) | (1 << tutLayer) | (1 << ATTR_LAYER);
        int camN = Camera.allCamerasCount;                                  // PERF: Camera.allCameras allocs a
        if (_allCams == null || _allCams.Length < camN) _allCams = new Camera[camN];   // fresh array every frame;
        Camera.GetAllCameras(_allCams);                                     // fill a reused buffer instead.
        for (int ci = 0; ci < camN; ci++)
        {
            var cam = _allCams[ci];
            if (cam == null || cam == hudCam2 || cam == promptCam || cam == attrCam) continue;
            int m = cam.cullingMask;
            if ((m & privMask) != 0) cam.cullingMask = m & ~privMask;   // write only when a private bit is actually set (native setter isn't free)
        }
    }

    // [B1] Dual-screen master toggle (Options): off -> HUD stays on the TOP screen, companion off, and the
    // bottom Presentation is dismissed (blank). Flip the aux visibility only on change. Dual-screen off:
    // disable the bottom cameras so nothing (not even a stale frame) renders to the bottom/overlay. Returns dsOn.
    bool ApplyDualScreenToggle()
    {
        bool requested = cfg.dualScreen != 0;
        if (requested != dsWas)
        {
            if (transport != null) transport.SetProductEnabled(requested);
            dsWas = requested;
        }
        bool dsOn = requested && directDisplayActive;
        // Dual-screen off: disable the bottom cameras so nothing (not even a stale frame) renders to the
        // bottom/overlay; the companion flow below is skipped too (companionOn=false).
        if (clearCam != null) clearCam.enabled = dsOn;
        if (attrCam != null) attrCam.enabled = dsOn;
        if (promptCam != null) promptCam.enabled = dsOn;
        if (hudCam2 != null) hudCam2.enabled = dsOn;
        return dsOn;
    }

    // [B1] Tie the whole companion to the HUD's visibility: when HK FADES the HUD in-gameplay (reading a lore
    // tablet, cutscenes, dream-dialogue) it drops the Hud Canvas CanvasGroup alpha (GameObject stays active).
    // Detect that and hide our companion (pane + frame + fps/battery/area-name/equip-row) to match.
    // Use ONLY the CanvasGroup on the Hud Canvas object itself. (A GetComponentInChildren(true) search
    // grabbed an INACTIVE HUD sub-element stuck at alpha 0, so hudFaded was always true and the companion
    // vanished during normal play.) If that CanvasGroup isn't the HUD-fade one, this is a safe no-op.
    bool HudFadedInGameplay(GameCameras gc)
    {
        bool hudFadedInGameplay = false; float hudAlpha = 1f;
        try
        {
            if (cfg.compHudSync == 1 && gc != null && gc.hudCanvas != null)
            {
                var hgo = gc.hudCanvas.transform.gameObject;
                var hcg = hgo.GetComponent<CanvasGroup>();
                if (hgo.activeInHierarchy && hcg != null) { hudAlpha = hcg.alpha; hudFadedInGameplay = hudAlpha <= 0.5f; }
            }
        }
        catch { }
        if (cfg.debug == 1 && (Time.frameCount % 90) == 0) Dbg($"HKDS hudFaded={hudFadedInGameplay} alpha={hudAlpha:F2} sync={cfg.compHudSync}");
        return hudFadedInGameplay;
    }

    // [B1] The companion SHOW gate: on, no pause/inventory/dual-screen-off overlay, HK's HUD not faded, and no
    // popup (dialogue box / tutorial prompt / attribution card) drawing on the bottom (popupAny, computed once
    // per frame in Tick).
    bool CompanionVisible(bool companionOn, bool overlay, bool hudFaded, bool popupAny)
    {
        return companionOn && !overlay && !hudFaded && !popupAny;
    }

    // [B1] True while the opening attribution ("Team Cherry presents" / the title card) actually draws on the
    // bottom (tutLayer). It shares the popup gate — companion, HUD mirror and backdrop hide so the card reads on
    // clean black — and FrameHudCams zooms promptCam by cfg.creditScale while it is up. (The old design parked
    // credits on ATTR_LAYER with a dedicated attrCam framing, but the companion now owns that camera whenever it
    // is enabled — which is the shipped default — so the attribution never rendered at all.)
    bool CreditShowing()
    {
        return creditT != null && creditT.gameObject.activeInHierarchy && AnyRendererDrawing(creditT);
    }

    // DEBUG: save the composited bottom-screen RT to a PNG so we can SEE the bottom screen without the
    // native blit (absent on the x86_64 emulator build) or a working secondary-display screencap.
    // MUST be called AFTER WaitForEndOfFrame — the cams render into rt during the frame, and on SwiftShader
    // the prior frame's rt contents are not guaranteed to persist, so a pre-render read comes back empty.
    // Read the composited bottom-screen RT to a PNG so we can SEE the bottom screen without the native blit
    // (absent on the x86_64 emulator build) or a working secondary-display screencap. Hooked to
    // Camera.onPostRender and fired ONLY for promptCam (the last, depth-101 bottom cam): at that instant rt
    // holds the full composite AND we're inside the render pipeline, so ReadPixels returns real pixels
    // (post-frame/manual reads came back empty on the emulator's software GL; an in-frame read works).
    // [B1] Mirror the main screen's fade level onto the bottom. HK dims the TOP via the tk2dCamera's CameraFade
    // FSM (room/stag/tram/elevator transitions FADE OUT, death RESPAWN FADE, hazard HAZARD FADE, cutscene JUST
    // FADE, game-start START FADE, failsafe FADE SCENE IN), which animates the fade quads' MATERIAL colour —
    // effective alpha = sprite.color.a * material _Color.a (probe-verified: sprite.color.a is constant 1). Sampling
    // that product each frame catches every trigger without hooking any of them, and drives an identical black
    // mesh quad over the whole bottom RT (child of promptCam = the last, depth-101 bottom camera; mesh+material
    // recipe borrowed from the separators — a runtime SpriteRenderer proved not to render on the bottom cams).
    void SyncBottomFade()
    {
        if (promptCam == null || !promptCam.enabled || cfg.compDimSync == 0) { if (fadeQuadMR != null && fadeQuadMR.enabled) fadeQuadMR.enabled = false; return; }
        try
        {
            var gcams = GameCameras.instance; var fadeFsm = gcams != null ? gcams.cameraFadeFSM : null;
            if (fadeFsm == null)
            {
                if (fadeQuadMR != null && fadeQuadMR.enabled) fadeQuadMR.enabled = false;
                return;
            }
            if (!ReferenceEquals(fadeSrcFor, (UnityEngine.Object)fadeFsm) || (fadeSrc1 == null && fadeSrc2 == null))
            {
                fadeSrcFor = fadeFsm; fadeSrc1Mat = fadeSrc2Mat = null;
                var a = FindDeep(fadeFsm.transform, "Start Blanker");  fadeSrc1 = a != null ? a.GetComponent<SpriteRenderer>() : null;
                var b = FindDeep(fadeFsm.transform, "Masker Blackout"); fadeSrc2 = b != null ? b.GetComponent<SpriteRenderer>() : null;
                try { if (fadeSrc1 != null) fadeSrc1Mat = fadeSrc1.material; } catch { }   // instanced once, cached
                try { if (fadeSrc2 != null) fadeSrc2Mat = fadeSrc2.material; } catch { }
            }
            // EXACT 1:1 SYNC (dusklight-grade). HK's fades are drawn by PlayMaker's CameraFadeOut/In actions via
            // IMGUI on the MAIN display (no renderer to sample; their private colorLerp has no IL2CPP reflection
            // metadata) -> FsmStateFadeAlpha reproduces the identical lerp from the FSM's state + entry clock +
            // the actions' public time/delay fields. The renderer/material sample covers legacy/white fades that
            // animate the Start Blanker material. Only the load-time BLACK HOLD has no signal (the main is black
            // because nothing renders) — there the transition window holds our envelope, with the live-tunable
            // ramps bridging its edges. compDimSync: 0=off, 1=sync, 2=force 55% (render test).
            float sampled = Mathf.Max(FadeSrcAlpha(fadeSrc1, fadeSrc1Mat), FadeSrcAlpha(fadeSrc2, fadeSrc2Mat));
            sampled = Mathf.Max(sampled, FsmStateFadeAlpha(fadeFsm));
            bool hold = false;
            try
            {
                var g = GameManager.instance;
                if (g != null)
                {
                    var st = g.gameState;
                    hold = g.IsInSceneTransition || st == GlobalEnums.GameState.ENTERING_LEVEL || st == GlobalEnums.GameState.EXITING_LEVEL || st == GlobalEnums.GameState.LOADING;
                }
            }
            catch { }
            // fadePostIn latch: once a fade-IN is under way (or done), the hold window must NOT re-darken —
            // the whole fade-in runs during ENTERING_LEVEL (= the hold window), and on slow loads the fade-in
            // finishes BEFORE gm reaches PLAYING, so the hold branch used to yank the just-cleared bottom back
            // to black ("fade in happening again after the real fade in"). A new fade-out re-arms the hold.
            if (fadeStKind == 3) fadePostIn = true;
            else if (fadeStKind == 1 || fadeStKind == 2) fadePostIn = false;
            float alpha;
            if (cfg.compDimSync == 2) alpha = fadeEnv = 0.55f;                 // debug force: verify the overlay renders
            else if (sampled > 0.01f)
            {
                // TRUE signal — copy it, with monotonicity guards killing single-frame seams at state changes:
                // a fade-OUT can only rise (the hold ramp may have started a frame early), a fade-IN can only fall.
                if (fadeStKind == 1) alpha = fadeEnv = Mathf.Max(sampled, fadeEnv);
                else if (fadeStKind == 3) alpha = fadeEnv = Mathf.Min(sampled, fadeEnv <= 0.011f ? 1f : fadeEnv);
                else alpha = fadeEnv = sampled;
                if (sampled > 0.5f && fadeStKind != 3) fadeGrace = Time.unscaledTime + 0.3f;   // deep OUT/HOLD fades arm a short grace (never during fade-IN)
            }
            else if ((hold || Time.unscaledTime < fadeGrace) && !fadePostIn) alpha = fadeEnv = Mathf.MoveTowards(fadeEnv, 1f, Time.unscaledDeltaTime * Mathf.Max(0.5f, cfg.compDimOutRate));   // signal-less black hold (bridged by the grace across the action-exit -> load-flag gap)
            else alpha = fadeEnv = Mathf.MoveTowards(fadeEnv, 0f, Time.unscaledDeltaTime * Mathf.Max(0.5f, cfg.compDimInRate));             // window edges only
            FadeSyncDiag(alpha);
            if (alpha < 0.01f) { if (fadeQuadMR != null && fadeQuadMR.enabled) fadeQuadMR.enabled = false; return; }
            if (fadeQuadMR == null)
            {
                var go = new GameObject("HKDS FadeSync");
                go.layer = tutLayer;
                go.transform.SetParent(promptCam.transform, false);
                fadeQuadT = go.transform;
                var mf = go.AddComponent<MeshFilter>();
                fadeQuadMR = go.AddComponent<MeshRenderer>();
                var mesh = new Mesh { name = "HKDS FadeQuad" };
                mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds(); mf.mesh = mesh;
                Shader fsh = Shader.Find("Sprites/Default-ColorFlash") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                fadeQuadMR.sharedMaterial = new Material(fsh) { color = new Color(0f, 0f, 0f, 0f) };
                fadeQuadMR.sortingOrder = 32000;   // over everything promptCam draws (and promptCam is the last bottom cam)
            }
            float o = promptCam.orthographicSize, asp = promptCam.aspect;
            if (o != fadeQuadOrtho || asp != fadeQuadAspect)
            {
                fadeQuadOrtho = o;
                fadeQuadAspect = asp;
                fadeQuadT.localPosition = new Vector3(0f, 0f, promptCam.nearClipPlane + 0.5f);
                fadeQuadT.localScale = new Vector3(o * 2.2f * asp, o * 2.2f, 1f);   // 10% overscan
            }
            fadeQuadMR.sharedMaterial.color = new Color(0f, 0f, 0f, alpha);
            if (!fadeQuadMR.enabled) fadeQuadMR.enabled = true;
        }
        catch (System.Exception e) { WarnOnce("bottom fade sync", e); }
    }

    // EXACT reproduction of the IMGUI fade PlayMaker draws on the MAIN screen. The CameraFade FSM's fade
    // states run CameraFadeOut / CameraFadeIn(WithDelay) actions whose visible alpha is literally
    // elapsed/time (Color.Lerp from OnEnter) — but their private `colorLerp` has no reflection metadata under
    // IL2CPP (read as null -> the earlier gui=0.00 trace). So: track the state-ENTER time ourselves and read the
    // actions' PUBLIC serialized fields (`time`, `delay` — metadata guaranteed) to recompute the same lerp,
    // frame-accurate. State families from the decoded camera_fade template; durations fall back to trace-measured
    // values (out 0.5s, in 0.05+0.55s) if the field read ever fails.
    string fadeStLast; float fadeStEnter, fadeStDur = 0.5f, fadeStDelay;
    int fadeStKind;   // 0 = none/clear, 1 = fading OUT (0->1), 2 = BLACK hold, 3 = fading IN (1->0)
    float FsmStateFadeAlpha(PlayMakerFSM fsm)
    {
        try
        {
            string st = fsm != null ? fsm.ActiveStateName : null;
            if (string.IsNullOrEmpty(st)) return 0f;
            if (st != fadeStLast)
            {
                fadeStLast = st; fadeStEnter = Time.unscaledTime; fadeStDelay = 0f;
                switch (st)
                {
                    case "FadingOut": case "RespawnFadingOut": case "HazardFadingOut":
                    case "Just Fading": case "Start Fade": case "Wait For Fade In":
                        fadeStKind = 1; fadeStDur = ActionFloat(fsm, "time", 0.5f); break;
                    case "Instant Fade": case "Insta Fade Out": case "Fade Out Instant":
                        fadeStKind = 2; break;                                        // instant black
                    case "Pre Fade": case "Pre Fade Menu": case "Pause":
                    case "WaitForRespawn": case "WaitForHazardRespawn":
                    case "Set Black": case "Set Black 2": case "Set White": case "Set White 2":
                        fadeStKind = 2; break;                                        // hold (between out and in)
                    case "FadeIn": case "Fade Back": case "Slow Fade In": case "Instant Fade In": case "Done":
                        fadeStKind = 3; fadeStDur = ActionFloat(fsm, "time", 0.55f); fadeStDelay = ActionFloat(fsm, "delay", 0.05f); break;
                    default:
                        fadeStKind = 0; break;                                        // Normal / event stubs -> clear
                }
            }
            float el = Time.unscaledTime - fadeStEnter;
            switch (fadeStKind)
            {
                case 1: return Mathf.Clamp01(fadeStDur > 0.01f ? el / fadeStDur : 1f);            // same lerp as CameraFadeOut
                case 2: return 1f;
                case 3: return el <= fadeStDelay ? 1f : Mathf.Clamp01(1f - (el - fadeStDelay) / Mathf.Max(0.01f, fadeStDur));   // same as CameraFadeInWithDelay
                default: return 0f;
            }
        }
        catch { return 0f; }
    }

    // Read a PUBLIC FsmFloat field (e.g. "time"/"delay") off the first CameraFade* action in the FSM's active state.
    static float ActionFloat(PlayMakerFSM fsm, string field, float fallback)
    {
        try
        {
            var st = fsm.Fsm != null ? fsm.Fsm.ActiveState : null;
            if (st == null || st.Actions == null) return fallback;
            foreach (var act in st.Actions)
            {
                if (act == null) continue;
                string n = act.GetType().Name;
                if (n != "CameraFadeOut" && n != "CameraFadeIn" && n != "CameraFadeInWithDelay") continue;
                var f = act.GetType().GetField(field);
                var ff = f != null ? f.GetValue(act) as HutongGames.PlayMaker.FsmFloat : null;
                if (ff != null && ff.Value > 0.001f) return ff.Value;
            }
        }
        catch { }
        return fallback;
    }

    // Effective rendered alpha of a fade quad: sprite tint x material tint (whichever exists), zero when hidden.
    static float FadeSrcAlpha(SpriteRenderer sr, Material m)
    {
        if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy) return 0f;
        float a = sr.color.a;
        if (m != null) { try { if (m.HasProperty("_Color")) a *= m.GetColor("_Color").a; } catch { } }
        return Mathf.Clamp01(a);
    }

    // Debug probe (debug=1): EVENT-DRIVEN — logs whenever the alpha/gui/hold/FSM-state changes materially, so a
    // whole transition reads as a compact trace (which channel drove it, where any gap/lag came from).
    float diagLastA = -1f, diagLastGui = -1f; string diagLastSt; bool diagLastHold;
    void FadeSyncDiag(float alpha)
    {
        if (cfg.debug != 1) return;
        try
        {
            bool hold = false; string gs = "?";
            try { var g = GameManager.instance; if (g != null) { hold = g.IsInSceneTransition; gs = g.gameState.ToString(); } } catch { }
            var gcams2 = GameCameras.instance; var ff = gcams2 != null ? gcams2.cameraFadeFSM : null;
            float gui = FsmStateFadeAlpha(ff);
            string st = ff != null ? ff.ActiveStateName : "?";
            if (Mathf.Abs(alpha - diagLastA) > 0.03f || Mathf.Abs(gui - diagLastGui) > 0.03f || st != diagLastSt || hold != diagLastHold)
            {
                diagLastA = alpha; diagLastGui = gui; diagLastSt = st; diagLastHold = hold;
                Debug.Log($"HKDS fadesync a={alpha:F2} gui={gui:F2} fsm='{st}' inTrans={hold} gm={gs} t={Time.unscaledTime:F2}");
            }
        }
        catch { }
    }

    // The shared direct renderer does not expose an intermediate composited RT.
    // Keep the diagnostic hook callable so the oracle orchestrator remains intact.
    void SyncDumpHook() { }

    void PushToBottom()
    {
        // Cameras target display 1 directly. Rendering is submitted by Unity.
    }
}
