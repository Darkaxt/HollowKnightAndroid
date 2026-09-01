using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// [B2] BOTTOM SCREEN — area management: the context-box FRAME (ornaments, tab fleurs, separators), the TAB ROW +
// tab state, the clone-cache lifecycle (build/stow/show/rebuild-on-PlayerData-change), pane fit-to-box, and the
// UpdateCompanion()/PositionFrame() orchestration + companion camera. Tenants: B4 (inventory), B6 (map), B7 (charms),
// B3 (HUD strip), B5 (selection) each expose a *Tick/Position* entry called from here.
public partial class HKDualScreen
{
    // ---- bottom-screen companion state ----
    Transform compRoot;            // our own parent (DontDestroyOnLoad); pan = move it, zoom = scale it

    GameObject paneClone;          // ALIAS -> the pane clone currently shown (invCloneCache or charmCloneCache)

    GameObject invCloneCache;      // cached Inventory pane clone (built once; SetActive-toggled on tab switch)

    GameObject charmCloneCache;    // cached Charms pane clone (built once; SetActive-toggled)

    UnityEngine.Object paneSrcRef; // identity of the inventoryFSM we cloned the panes from (same purpose)

    int invStamp = int.MinValue;   // PlayerData fingerprint at the inv clone's last (re)build (dirty-gate: rebuild on change)

    int charmStamp = int.MinValue; // PlayerData fingerprint at the charm clone's last (re)build


    // Tab state: cur = the EFFECTIVE tab this frame (a bottom-screen tap wins over cfg.compTab; drives build + fit);
    // built = which tab's clone is currently built/shown (-1 none); tap = the tapped tab (-1 = follow config; a config
    // change clears it); lastCfg = last cfg.compTab seen (edge detect for that clear).
    struct TabState { public int cur, built, tap, lastCfg; }
    TabState tab = new TabState { built = -1, tap = -1, lastCfg = int.MinValue };
    static readonly int[] TAB_TO_COL = { 1, 0, 2 };   // compTab {Map,Inv,Charm} -> display column {Inventory,Map,Charms}

    bool paneNeedsFit;             // recompute the Charms/Inv pane fit-to-box next frame

    int compFrameTick;             // throttle for the map auto-frame bounds scan

    // The companion's content FIT (what the box frames): centre + orthographic size + whether a fit exists yet. Owned
    // here (B2); WRITTEN by the map framing burst (B6, via TryMapBounds), the pane fit dispatch (B4/B7, via TryPaneBounds)
    // and LayoutCharmsRedesign (B7, RETURNS one); invalidated on tab build / map availability loss / teardown.
    struct FitResult { public Vector3 center; public float ortho; public bool valid; }
    FitResult fit;
    void ApplyFit(FitResult r) { if (r.valid) fit = r; }


    GameObject frameRoot;         // context-box frame: parent of the ornament clones, child of attrCam

    readonly Dictionary<Transform, Vector3> frameEdge = new Dictionary<Transform, Vector3>();  // clone -> (edge dir x,y in [-1..1], local z)

    readonly Dictionary<Transform, Vector3> frameBase = new Dictionary<Transform, Vector3>();   // clone -> normalized base scale

    readonly List<(Component tmp, Transform t, int col)> frameTabs = new List<(Component, Transform, int)>();  // tab TMP + transform + column


    Transform botFleurT, botFleur2T;   // botFleurT = fleur ABOVE selected tab (#3); botFleur2T = BELOW (#2)

    float botFleurAspWH = 6.9f, botFleur2AspWH = 7.3f;   // texture aspects (width/height) for aspect-locked resizing

    Transform sepTopT, sepBotT;        // full-width separators: #5 (HUD->box) and #6 (box->tabs)

    float sepTopAspWH = 31f, sepBotAspWH = 31f;

    float frameRefOrtho = 8f;     // attrCam ortho captured at frame-build (for zoom-proportional scaling)

    Transform CloneOrnament(Transform root, string name, Vector2 edge, float targetFrac, bool useWidth, float zRot, int spriteId)
    {
        var src = FindDeep(root, name);
        if (src == null) { Dbg($"HKDS frame: no '{name}'"); return null; }
        var go = Instantiate(src.gameObject, frameRoot.transform);
        go.name = "F_" + name + (zRot != 0f ? "_" + (int)zRot : "");
        // Disable EVERY MonoBehaviour except the tk2dSprite renderer, so the clone can't animate/fade/
        // close itself (kills its PlayMakerFSM, tk2dSpriteAnimator, up/down driver, NestedFadeGroup).
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name != "tk2dSprite") mb.enabled = false;
        SetLayerRecursive(go.transform, ATTR_LAYER);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, zRot);
        go.SetActive(true);
        // The inventory-closed fade may have DISABLED the ornament's MeshRenderer and/or deactivated its
        // sprite child (that's how HK hides it) — force renderers on + their GameObjects active, else the
        // clone is invisible even though it "built".
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = true; }
        // Force the authored open frame + full opacity: the fade also drives the tk2dSprite VERTEX-COLOR
        // alpha to 0 while closed → the ornament would otherwise be transparent.
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "tk2dSprite") continue;
            var ty = mb.GetType();
            if (spriteId >= 0) { try { ty.GetProperty("spriteId")?.SetValue(mb, spriteId, null); } catch { } }
            try { ty.GetProperty("color")?.SetValue(mb, Color.white, null); } catch { }
        }
        // normalize: scale so the ornament's measured size == targetFrac of the (build-time) view dim
        float s = attrCam.orthographicSize, asp = attrCam.aspect;
        float viewDim = useWidth ? (2f * s * asp) : (2f * s);
        var rr = go.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(); bool hv = false;
        foreach (var r in rr) { var rb = r.bounds; if (float.IsNaN(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue; if (!hv) { b = rb; hv = true; } else b.Encapsulate(rb); }
        float nd = hv ? Mathf.Max(0.001f, useWidth ? b.size.x : b.size.y) : 1f;
        go.transform.localScale *= (targetFrac * viewDim) / nd;
        frameBase[go.transform] = go.transform.localScale;
        frameEdge[go.transform] = new Vector3(edge.x, edge.y, 5f);
        if (cfg.debug == 1)
        {
            int en = 0; foreach (var r in rr) if (r.enabled) en++;
            float a = 1f; foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) { if (mb == null || mb.GetType().Name != "tk2dSprite") continue; try { a = ((Color)mb.GetType().GetProperty("color").GetValue(mb, null)).a; } catch { } break; }
            Dbg($"HKDS orn '{name}' rends={rr.Length} en={en} boundsX={(hv ? b.size.x : 0):F2} boundsY={(hv ? b.size.y : 0):F2} lossy={go.transform.lossyScale.x:F3} a={a:F2}");
        }
        return go.transform;
    }

    // Bake the two tab fleurs ONCE from HK's own "Menu" sprite atlas (resident only at the title/menu, not during
    // gameplay) into persistentDataPath PNGs; thereafter BuildFleurFromFile just loads the cache. Atlas rects:
    //   TOP    = Yes_No_top_fleur0007     atlas rect (0,69,409,64)     -> hkds_fleur.png
    //   BOTTOM = Yes_No_bottom_fleur0007  atlas rect (349,408,345,47)  -> hkds_fleur_bot.png
    // Rects are bottom-left origin (Unity sprite space); divide by the atlas's real dims for UVs. No APK art —
    // the atlas belongs to the user's game; we own only the baked Texture2D copies (cached, regenerable).
    bool fleurBaked;

    void TryBakeTabFleurs()
    {
        if (fleurBaked) return;
        try
        {
            string pTop = Path.Combine(Application.persistentDataPath, "hkds_fleur.png");
            string pBot = Path.Combine(Application.persistentDataPath, "hkds_fleur_bot.png");
            bool haveTop = File.Exists(pTop), haveBot = File.Exists(pBot);
            if (haveTop && haveBot) { fleurBaked = true; return; }
            Texture2D atlas = null;
            var all = Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && NameHas(all[i].name, "-Menu-") && !NameHas(all[i].name, "premenu")) { atlas = all[i]; break; }
            if (atlas == null) return;   // Menu atlas not resident yet — retry (it's up at the title menu)
            float W = atlas.width, H = atlas.height;
            if (!haveTop) { var t = BakeRegion(atlas, new Rect(0f / W, 69f / H, 409f / W, 64f / H)); if (t != null) { botFleurAspWH = (float)t.width / Mathf.Max(1, t.height); CacheCompanionTex("hkds_fleur", t); } }
            if (!haveBot) { var b = BakeRegion(atlas, new Rect(349f / W, 408f / H, 345f / W, 47f / H)); if (b != null) { botFleur2AspWH = (float)b.width / Mathf.Max(1, b.height); CacheCompanionTex("hkds_fleur_bot", b); } }
            fleurBaked = File.Exists(pTop) && File.Exists(pBot);
            Dbg($"HKDS tab-fleurs bake atlas='{atlas.name}' {W}x{H} top={File.Exists(pTop)} bot={File.Exists(pBot)}");
        }
        catch (Exception e) { Dbg($"HKDS tab-fleur err {e.Message}"); }
    }

    // Bottom ornament from a standalone PNG (hkds_fleur.png in persistentDataPath): the pause/options-menu
    // divider flourish, pre-baked from the Pause_fleurs atlas with real alpha. We own the Texture2D (LoadImage)
    // so it's always GPU-resident — the live atlas sprite drew nothing during gameplay (atlas not resident).
    // Rendered as an explicit quad (full 0..1 UVs) with Sprites/Default-ColorFlash (guaranteed in the build).
    // Returns null if the PNG isn't present (caller falls back to a tk2d ornament).
    Transform BuildFleurFromFile(string cacheName, bool isTop, Vector2 edge, float targetFrac)
    {
        try
        {
            // The two tab fleurs are baked from the Menu atlas by TryBakeTabFleurs and loaded here from cache.
            var tex = Own(LoadCompanionTex(cacheName));
            if (tex == null) { Dbg($"HKDS fleur '{cacheName}': cache not ready yet"); return null; }
            var go = new GameObject("F_Fleur_" + cacheName);
            go.transform.SetParent(frameRoot.transform, false);
            go.layer = ATTR_LAYER;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = Own(new Mesh { name = "F_FleurQuad" });
            float aspectWH = Mathf.Max(0.01f, (float)tex.width / Mathf.Max(1, tex.height));   // quad: width=aspectWH, height=1
            if (isTop) botFleurAspWH = aspectWH; else botFleur2AspWH = aspectWH;   // for aspect-locked resize in PositionFrame
            float hw = aspectWH * 0.5f, hh = 0.5f;
            mesh.vertices = new[] { new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f), new Vector3(hw, hh, 0f), new Vector3(-hw, hh, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };   // ColorFlash multiplies COLOR; absent channel = (0,0,0,0) = invisible
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            mf.mesh = mesh;
            Shader fsh = Shader.Find("Sprites/Default-ColorFlash") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            var mat = Own(new Material(fsh) { mainTexture = tex, color = Color.white });
            mr.sharedMaterial = mat; mr.sortingOrder = 40;
            float s2 = attrCam.orthographicSize, asp = attrCam.aspect;
            float sc = (targetFrac * 2f * s2 * asp) / aspectWH;   // scale the unit-height quad so its WIDTH = targetFrac of the view
            go.transform.localScale = new Vector3(sc, sc, 1f);
            frameBase[go.transform] = go.transform.localScale;
            frameEdge[go.transform] = new Vector3(edge.x, edge.y, 5f);
            Dbg($"HKDS bottom fleur from file {tex.width}x{tex.height} aspWH={aspectWH:F2} sc={sc:F2} shader={(fsh != null ? fsh.name : "NULL")}");
            return go.transform;
        }
        catch (Exception e) { Dbg($"HKDS fleur file err {e.Message}"); return null; }
    }

    // Full-width horizontal separator from a cached PNG (a thin divider fleur). Unlike the tab fleurs, its
    // source (credits / SS_Death_Screen atlases) is NOT resident during gameplay, so it comes from a pushed
    // persistentDataPath cache (user data, not APK). Positioned + scaled every frame in PositionFrame.
    Transform BuildSeparator(string cacheName, bool isTop)
    {
        try
        {
            var tex = Own(LoadCompanionTex(cacheName));
            if (tex == null)
            {
                // Unlike the tab fleurs, a separator has no source in the game's live assets to bake
                // from — so fall back to the copy shipped in the build and write it out as the normal
                // cache. Without this the box silently loses an edge whenever the file goes missing.
                tex = Own(HKEmbedded.LoadTexture(cacheName));
                if (tex != null) { CacheCompanionTex(cacheName, tex); Dbg($"HKDS sep '{cacheName}': restored from the built-in copy"); }
            }
            if (tex == null) { Dbg($"HKDS sep '{cacheName}': no cache and nothing built in"); return null; }
            var go = new GameObject("F_Sep_" + cacheName);
            go.transform.SetParent(frameRoot.transform, false);
            go.layer = ATTR_LAYER;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = Own(new Mesh { name = "F_SepQuad" });
            float aspectWH = Mathf.Max(0.01f, (float)tex.width / Mathf.Max(1, tex.height));   // unit-height quad, width=aspectWH
            if (isTop) sepTopAspWH = aspectWH; else sepBotAspWH = aspectWH;
            float hw = aspectWH * 0.5f, hh = 0.5f;
            mesh.vertices = new[] { new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f), new Vector3(hw, hh, 0f), new Vector3(-hw, hh, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds(); mf.mesh = mesh;
            Shader fsh = Shader.Find("Sprites/Default-ColorFlash") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            mr.sharedMaterial = Own(new Material(fsh) { mainTexture = tex, color = Color.white });
            mr.sortingOrder = 40;
            Dbg($"HKDS separator '{cacheName}' {tex.width}x{tex.height} aspWH={aspectWH:F1}");
            return go.transform;
        }
        catch (Exception e) { Dbg($"HKDS sep err {e.Message}"); return null; }
    }

    // Procedural white capsule (pill) sprite for the RESET button background. Built once, Own()-tracked.
    Sprite MakePillSprite()
    {
        const int W = 64, H = 32, R = 15;
        var tex = Own(new Texture2D(W, H, TextureFormat.RGBA32, false));
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cx = Mathf.Clamp(x, R, W - 1 - R);   // capsule: distance to the centre SEGMENT
                float dx = x - cx, dy = y - (H - 1) * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)(d <= R - 1f ? 255 : d >= R ? 0 : (byte)((R - d) * 255f));
                px[y * W + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px); tex.Apply(false);
        return Own(Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f));
    }

    // A full-width black quad used to clip the zoomed map at the inner rect's edge (sortingOrder BELOW the
    // raised chrome, ABOVE HK's map sprites). Sized/positioned per frame in PositionFrame; MAP tab only.
    // The caller caches the returned quad's Renderer (mapMaskTopR/BotR) — PositionFrame runs per frame and
    // must not GetComponent there.
    Transform BuildMapMask(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(frameRoot.transform, false);
        go.layer = ATTR_LAYER;
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = Own(new Mesh { name = name });
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds(); mf.mesh = mesh;
        Shader fsh = Shader.Find("Sprites/Default-ColorFlash") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        mr.sharedMaterial = Own(new Material(fsh) { color = Color.black });
        mr.sortingLayerName = "Inventory";   // HK's map/pane sprites ALL live on the topmost "Inventory" sorting layer — a Default-layer mask loses regardless of order
        mr.sortingOrder = 10000;             // above map content (low orders), below the raised chrome
        mr.enabled = false;
        return go.transform;
    }

    // Build HK's context-box frame from the real inventory ornaments (skip the corner swirls) + a tab
    // title row. Parented to attrCam so it stays fixed at the panel edges while the map pans/zooms.
    void BuildFrame()
    {
        if (frameRoot != null || cfg.compFrame != 1) return;
        var invFsm = GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
        if (invFsm == null) return;
        // The real ornaments are Inventory/Border/Inv_Border_Top|Bottom — UNIQUE names, so a root search is
        // unambiguous (the old "Fleur Top"/"Divider L" hit the dialogue-box fleurs + item-slot dividers).
        var root = invFsm.transform.root;
        if (FindDeep(root, "Inv_Border_Top") == null) return;   // inventory chrome not ready — retry next frame
        frameRoot = new GameObject("HKCompFrame");
        frameRoot.transform.SetParent(attrCam.transform, false);
        frameRefOrtho = attrCam.orthographicSize;
        // Two fleurs FRAME the selected tab: TOP (#3, hkds_fleur.png) above its text, BOTTOM (#2,
        // hkds_fleur_bot.png) below it — both baked from the Menu atlas (TryBakeTabFleurs), loaded from cache.
        // Positioned + resized live in PositionFrame (BOT_FLEUR_W0 is only the build-time width; the follow-the-tab
        // layout recomputes both widths from the Charms tab text every frame). tk2d Inv_Border_Top = last-ditch fallback.
        botFleurT = BuildFleurFromFile("hkds_fleur", true, new Vector2(0f, cfg.compTabY + cfg.compBotFleurGap), BOT_FLEUR_W0);
        botFleur2T = BuildFleurFromFile("hkds_fleur_bot", false, new Vector2(0f, cfg.compTabY - cfg.compBotFleurGap), BOT_FLEUR_W0 * cfg.compBotFleur2WScale);
        if (botFleurT == null)
            botFleurT = CloneOrnament(root, "Inv_Border_Top", new Vector2(0f, cfg.compTabY + cfg.compBotFleurGap), BOT_FLEUR_W0, true, 0f, 41);
        // Full-width separators (positioned live in PositionFrame): #5 HUD->box, #6 box->tabs.
        if (cfg.compSepTop == 1) sepTopT = BuildSeparator("hkds_sep_top", true);
        if (cfg.compSepBot == 1) sepBotT = BuildSeparator("hkds_sep_bot", false);
        BuildTabRow(root);
        if (cfg.compAreaName == 1) BuildAreaName(root);
        if (cfg.compStats == 1) BuildStats(root);
        BuildEquipCharmRow();
        if (cfg.compNoMapMsg == 1) BuildNoMapLabel(root);
        // our own fleur/separator quads: renderers ON once here (nothing disables them later; the old per-frame
        // re-enable was 5 GetComponentsInChildren allocations every frame)
        foreach (var t in new[] { botFleurT, botFleur2T, sepTopT, sepBotT }) if (t != null) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        // CHROME ABOVE THE MAP MASKS: raise every frame renderer's sortingOrder well past the masks (10000) so the
        // clip quads cover only MAP CONTENT (which uses HK's low orders), never the box chrome / HUD strip labels.
        foreach (var r in frameRoot.GetComponentsInChildren<Renderer>(true)) { r.sortingLayerName = "Inventory"; r.sortingOrder += 20000; }
        // MAP CLIP MASKS (pinch-zoom): screen-anchored black quads over everything ABOVE the top-separator fleur and
        // BELOW the tab-row fleur line — a zoomed/panned map hard-clips at the context box's inner rect.
        mapMaskTopT = BuildMapMask("HKDS MapMaskTop");
        mapMaskBotT = BuildMapMask("HKDS MapMaskBot");
        mapMaskTopR = mapMaskTopT != null ? mapMaskTopT.GetComponent<Renderer>() : null;
        mapMaskBotR = mapMaskBotT != null ? mapMaskBotT.GetComponent<Renderer>() : null;
        // RESET button: a small label centred ON the top fleur; shown only when the map view isn't default.
        try
        {
            var src = FindDeep(root, "Pane Name");
            if (src != null)
            {
                var go = Instantiate(src.gameObject, frameRoot.transform);
                go.name = "F_MapReset";
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && !mb.GetType().Name.Contains("TextMeshPro")) mb.enabled = false;
                SetLayerRecursive(go.transform, ATTR_LAYER);
                go.SetActive(true);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = false; r.sortingLayerName = "Inventory"; r.sortingOrder = 30050; }
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || !c.GetType().Name.Contains("TextMeshPro")) continue;
                    mapResetTmp = c;
                    try { c.GetType().GetProperty("text")?.SetValue(c, "RESET", null); } catch { }
                    try { c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null); } catch { }
                    mapResetR = (c as Component).GetComponent<Renderer>();
                    break;
                }
                // white PILL behind the text (procedural capsule sprite); text renders black on top
                var pillGo = new GameObject("pill");
                pillGo.transform.SetParent(go.transform, false);
                pillGo.layer = ATTR_LAYER;
                mapResetPillSR = pillGo.AddComponent<SpriteRenderer>();
                mapResetPillSR.sprite = MakePillSprite();
                mapResetPillSR.color = Color.white;   // solid fill (user)
                mapResetPillSR.sortingLayerName = "Inventory";
                mapResetPillSR.sortingOrder = 30045;   // just under the RESET text (30050)
                mapResetPillSR.enabled = false;
                float s0 = attrCam.orthographicSize;
                var rr0 = go.GetComponentsInChildren<Renderer>(); Bounds b0 = new Bounds(); bool hv0 = false;
                foreach (var r in rr0) { if (r == mapResetPillSR) continue; var rb = r.bounds; if (float.IsNaN(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue; if (!hv0) { b0 = rb; hv0 = true; } else b0.Encapsulate(rb); }
                go.transform.localScale *= (0.06f * 2f * s0) / Mathf.Max(0.001f, hv0 ? b0.size.y : 1f);   // readable thumb-target size
                mapResetT = go.transform;
                frameBase[mapResetT] = mapResetT.localScale;
                frameEdge[mapResetT] = new Vector3(0f, cfg.compSepTopY, 3.5f);   // centred on the top separator's fleur
            }
        }
        catch (Exception e) { Dbg($"HKDS map reset btn err {e.Message}"); }
        Dbg($"HKDS frame built, parts={frameEdge.Count} tabs={frameTabs.Count}");
    }

    Transform mapMaskTopT, mapMaskBotT;   // MAP-tab clip masks: black quads over everything past the inner rect (above the top-sep fleur / below the tab fleur); chrome sorts ABOVE them, map content below
    Renderer mapMaskTopR, mapMaskBotR;    // their renderers, cached at build (PositionFrame toggles them per frame)
    int tabColorCol = -1;                 // active display column the tab colors were last set for (reflection-set only on change)
    Transform mapResetT; Component mapResetTmp; Renderer mapResetR;   // RESET button centred on the top fleur, shown only when the pinch view isn't default
    SpriteRenderer mapResetPillSR;   // white pill behind the RESET text (text drawn black on top)

    const float BOT_FLEUR_W0 = 0.55f;   // build-time width of the two tab fleurs (fraction of view); PositionFrame resizes them live

    // Tab-name row: Inventory | Map | Charms (3 tabs). HK has no static tab row — it uses a 3-name carousel
    // (Pane Name / L / R). We clone the "Pane Name" world-space TMP (Perpetua SDF) once per label and highlight
    // the active one by alpha (active=1, others=0.4) — HK's own indicator.
    void BuildTabRow(Transform root)
    {
        try
        {
            var src = FindDeep(root, "Pane Name");
            if (src == null) { Dbg("HKDS frame: no 'Pane Name'"); return; }
            // Localized tab names (fall back to English if the key isn't found). Candidate keys cover HK's known
            // inventory-pane title conventions; the discovery dump below reveals the exact ones per language.
            // Exact HK pane-title keys (UI sheet), verified by decrypting EN_UI.txt: PANE_INVENTORY/PANE_MAP/PANE_CHARMS.
            string[] labels = {
                LocalizedLabel("Inventory", "PANE_INVENTORY"),
                LocalizedLabel("Map",       "PANE_MAP"),
                LocalizedLabel("Charms",    "PANE_CHARMS"),
            };
            for (int i = 0; i < labels.Length; i++)
            {
                var staging = new GameObject("HKTabCloneStaging");
                staging.SetActive(false);
                GameObject go = null;
                try
                {
                    // Keep the clone inactive in hierarchy until every driver
                    // except the retained TMP visual has been removed. This
                    // prevents the inventory's close/fade behaviours from
                    // running Awake/OnEnable on our resident tab label.
                    go = Instantiate(src.gameObject, staging.transform);
                    go.SetActive(false);
                    go.name = "F_Tab" + i;
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                        if (mb != null && !mb.GetType().Name.Contains("TextMeshPro")) DestroyImmediate(mb);
                    SetLayerRecursive(go.transform, ATTR_LAYER);
                    // Force renderers and the retained TMP Behaviour on. The
                    // closed inventory can leave both disabled independently.
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.gameObject.SetActive(true); r.enabled = true; }
                    Component tmp = null;
                    foreach (var c in go.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || !c.GetType().Name.Contains("TextMeshPro")) continue;
                        tmp = c;
                        var tmpBehaviour = c as Behaviour;
                        if (tmpBehaviour != null) tmpBehaviour.enabled = true;
                        try { c.GetType().GetProperty("text")?.SetValue(c, labels[i], null); } catch { }
                        try { c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null); } catch { }
                        break;
                    }
                    go.transform.SetParent(frameRoot.transform, false);
                    go.SetActive(true);
                    frameTabs.Add((tmp, go.transform, i));
                    float s = attrCam.orthographicSize;
                    var rr = go.GetComponentsInChildren<Renderer>();
                    Bounds b = new Bounds(); bool hv = false;
                    foreach (var r in rr) { var rb = r.bounds; if (float.IsNaN(rb.center.x) || rb.size.sqrMagnitude < 1e-8f) continue; if (!hv) { b = rb; hv = true; } else b.Encapsulate(rb); }
                    float nd = hv ? Mathf.Max(0.001f, b.size.y) : 1f;
                    go.transform.localScale *= (0.055f * 2f * s) / nd;   // base size; compTabScale applied LIVE in PositionFrame
                    frameBase[go.transform] = go.transform.localScale;
                    frameEdge[go.transform] = new Vector3((i - 1f) * cfg.compTabSpacing, cfg.compTabY, 4f);   // 3 tabs centered on col 1
                    if (cfg.debug == 1) Dbg($"HKDS tab{i} '{labels[i]}' rends={rr.Length} bX={(hv ? b.size.x : 0):F2} bY={(hv ? b.size.y : 0):F2} lossy={go.transform.lossyScale.x:F3} tmpEnabled={(tmp as Behaviour)?.enabled}");
                }
                finally { Destroy(staging); }
            }
            BuildSelBox();   // B5: the selection-highlight fallback box lives with the frame (destroyed with it)
        }
        catch (Exception e) { Dbg($"HKDS frame tabs err {e.Message}"); }
    }

    // Each frame: keep the ornaments pinned at the panel edges + a constant apparent size as the map's
    // fit (attrCam.orthographicSize) changes per zone.
    void PositionFrame()
    {
        if (frameRoot == null || attrCam == null) return;
        float s = attrCam.orthographicSize, asp = attrCam.aspect;
        // The fleur ornaments frame the whole CONTEXT BOX, not just the map — show them on EVERY companion
        // tab (Map / Inventory / Charms), always, whenever the frame is up. (They used to be gated on
        // mapAvailable, which hid them on the non-map tabs; user wants the box decorated everywhere.)
        if (botFleurT != null && !botFleurT.gameObject.activeSelf) botFleurT.gameObject.SetActive(true);
        if (botFleur2T != null && !botFleur2T.gameObject.activeSelf) botFleur2T.gameObject.SetActive(true);
        if (sepTopT != null && !sepTopT.gameObject.activeSelf) sepTopT.gameObject.SetActive(true);
        if (sepBotT != null && !sepBotT.gameObject.activeSelf) sepBotT.gameObject.SetActive(true);
        float zf = (s / Mathf.Max(0.01f, frameRefOrtho)) * cfg.compFrameScale;   // keep constant apparent size as the map zooms
        // bottom fleur is positioned in WORLD space from the tab-row's real glyph bounds below (near the
        // tab text) — not from raw compTabY, since TMP draws the visible glyphs well above the transform.
        // LIVE tab positions (compTabY / compTabSpacing tune without a rebuild). 3 tabs centered on col 1.
        foreach (var (tmp, t, col) in frameTabs)
            if (t != null && frameEdge.ContainsKey(t)) frameEdge[t] = new Vector3((col - 1f) * cfg.compTabSpacing, cfg.compTabY, 4f);   // 3 tabs centered on col 1
        // area-name label lives TOP-RIGHT (live-tunable X/Y)
        if (areaNameT != null && frameEdge.ContainsKey(areaNameT))
            frameEdge[areaNameT] = new Vector3(cfg.compAreaNameX, cfg.compAreaNameY, 4f);
        foreach (var kv in frameEdge)
        {
            var t = kv.Key; if (t == null) continue;
            var e = kv.Value;
            t.localPosition = new Vector3(e.x * s * asp, e.y * s, e.z);
            if (frameBase.TryGetValue(t, out var bs)) t.localScale = bs * zf;
        }
        // active-tab highlight — the tapped tab wins, else cfg.compTab. compTab order {0=Map 1=Inv 2=Charm}
        // -> display column {Inventory|Map|Charms} = {1,0,2}.
        int effectiveTab = tab.cur;   // == the value UpdateCompanion resolved this frame (tap wins over cfg.compTab)
        int activeCol = TAB_TO_COL[Mathf.Clamp(effectiveTab, 0, 2)];
        bool colChanged = activeCol != tabColorCol;   // PERF: SetTmpColor is a reflection property set x3 — only on active-tab change
        foreach (var (tmp, t, col) in frameTabs)
        {
            if (tmp == null) continue;
            bool active = col == activeCol;
            if (colChanged) SetTmpColor(tmp, new Color(1f, 1f, 1f, active ? 1f : 0.6f));
            if (t != null && frameBase.TryGetValue(t, out var tbs)) t.localScale = tbs * zf * Mathf.Max(0.1f, cfg.compTabScale);   // LIVE font size
        }
        if (colChanged) tabColorCol = activeCol;
        PositionHudStrip(s, asp, zf, effectiveTab);   // B3: area name, fps/battery, equipped-charm row, no-map label
        var cam = attrCam.transform;
        // Anchor the selection line + bottom fleur to the tabs' ACTUAL rendered glyph bounds (world AABB),
        // read AFTER the scale loops above. The tab transform sits at compTabY (panel bottom) but TMP draws
        // the glyphs ~1 line-height higher, so placing decorations at raw compTabY left a big gap.
        Bounds rowB = default, actB = default, charmsB = default; bool rowHave = false, actHave = false, charmsHave = false;
        foreach (var (tmp2, t, col) in frameTabs)
        {
            if (tmp2 == null) continue;
            // Use the TMP component's OWN co-located mesh renderer (tight glyph AABB) — NOT
            // GetComponentsInChildren, which also grabbed a stray renderer reaching down to the tab's
            // transform origin (panel bottom), inflating the bounds and dropping the line to the bottom.
            var tr = (tmp2 as Component).GetComponent<Renderer>();
            if (tr == null) continue;
            var rb = tr.bounds;
            if (rb.size.x < 1e-5f || rb.size.y < 1e-5f) continue;
            if (!rowHave) { rowB = rb; rowHave = true; } else rowB.Encapsulate(rb);
            if (col == activeCol) { if (!actHave) { actB = rb; actHave = true; } else actB.Encapsulate(rb); }
            if (col == 2) { if (!charmsHave) { charmsB = rb; charmsHave = true; } else charmsB.Encapsulate(rb); }   // fix7: Charms tab = col 2 (was 3, never matched -> fleur fell back to the SELECTED tab's width, so it resized per tab). Now the fleur width is ALWAYS the Charms-tab text width; Map/Inv follow it.
        }
        PositionSelection(s);   // B5: HK's corner-bracket cursor / selBox fallback around the tapped item
        // TWO fleurs FRAME the selected tab: TOP (#3) above its text, BOTTOM (#2, narrower) below it, with the
        // SAME gap (compBotFleurGap) between the text and each fleur. Both sized aspect-locked to the Charms tab
        // width. Tracks the selected tab.
        if (actHave)
        {
            float charmsW = charmsHave ? charmsB.size.x : actB.size.x;
            float gap = cfg.compBotFleurGap * s;
            float textTop = actB.max.y;                              // cap tops (reliable)
            float textBot = actB.max.y - cfg.compBotFleurTextH * s;  // visible text bottom (same font for all tabs)
            if (botFleurT != null)
            {
                float w = charmsW * Mathf.Max(0.05f, cfg.compBotFleurWScale);
                float sc = Mathf.Max(0.0001f, w / botFleurAspWH), hh = sc * 0.5f;
                botFleurT.localScale = new Vector3(sc, sc * (cfg.compBotFleurFlip == 1 ? -1f : 1f), sc);
                botFleurT.position = new Vector3(actB.center.x, textTop + gap + hh, actB.center.z - 0.1f);   // above, edge = gap over text top
                frameInnerBotFrac = (textTop + gap + hh * 2f - cam.position.y) / s;   // B6: the box's inner BOTTOM = this fleur's top edge (ortho fracs) -> the map's full-area fit stops here
            }
            else frameInnerBotFrac = (textTop + gap - cam.position.y) / s;
            if (botFleur2T != null)
            {
                float w2 = charmsW * Mathf.Max(0.05f, cfg.compBotFleur2WScale);
                float sc2 = Mathf.Max(0.0001f, w2 / botFleur2AspWH), hh2 = sc2 * 0.5f;
                botFleur2T.localScale = new Vector3(sc2, sc2 * (cfg.compBotFleur2Flip == 1 ? -1f : 1f), sc2);
                botFleur2T.position = new Vector3(actB.center.x, textBot - gap - hh2, actB.center.z - 0.1f);  // below, edge = gap under text bottom
            }
        }
        // Full-width horizontal separators (#5 HUD->box, #6 box->tabs): centred in X, width = compSepW of the
        // full view width (aspect-locked so they stay thin), at the live-adjustable compSepTopY / compSepBotY.
        float sepFullW = cfg.compSepW * 2f * s * asp;
        if (sepTopT != null)
        {
            float sc = Mathf.Max(0.0001f, sepFullW / sepTopAspWH); sepTopT.localScale = new Vector3(sc, sc, sc);
            sepTopT.position = cam.position + cam.up * (cfg.compSepTopY * s) + cam.forward * 5f;
            frameInnerTopFrac = cfg.compSepTopY - sc * 0.5f / s;   // B6: the box's inner TOP = the bottom edge of this separator's fleur ornament (unit-height quad, tight bake) -> the map's full-area fit starts here
        }
        if (sepBotT != null)
        {
            float sc = Mathf.Max(0.0001f, sepFullW / sepBotAspWH); sepBotT.localScale = new Vector3(sc, sc, sc);
            sepBotT.position = cam.position + cam.up * (cfg.compSepBotY * s) + cam.forward * 5f;
        }
        // MAP clip masks + RESET button (MAP tab only). The masks pin to the inner rect's edges in SCREEN space
        // (children of attrCam -> they ride the camera), so a pinch-zoomed/panned map hard-clips at the top-sep
        // fleur and the tab-fleur line. RESET shows centred on the top fleur only when the view isn't default.
        bool onMap = effectiveTab == COMP_MAP;
        float yt = float.IsNaN(frameInnerTopFrac) ? cfg.compSepTopY : frameInnerTopFrac;
        float yb = float.IsNaN(frameInnerBotFrac) ? cfg.compTabY + 0.4f : frameInnerBotFrac;
        if (mapMaskTopT != null)
        {
            if (mapMaskTopR != null && mapMaskTopR.enabled != onMap) mapMaskTopR.enabled = onMap;
            if (onMap)
            {
                float hgt = Mathf.Max(0.01f, (1.35f - yt) * s);
                mapMaskTopT.position = cam.position + cam.up * ((yt * s) + hgt * 0.5f) + cam.forward * 4.5f;
                mapMaskTopT.localScale = new Vector3(2.3f * s * asp, hgt, 1f);
            }
        }
        if (mapMaskBotT != null)
        {
            if (mapMaskBotR != null && mapMaskBotR.enabled != onMap) mapMaskBotR.enabled = onMap;
            if (onMap)
            {
                float hgt = Mathf.Max(0.01f, (yb + 1.35f) * s);
                mapMaskBotT.position = cam.position + cam.up * ((yb * s) - hgt * 0.5f) + cam.forward * 4.5f;
                mapMaskBotT.localScale = new Vector3(2.3f * s * asp, hgt, 1f);
            }
        }
        if (mapResetR != null)
        {
            bool show = onMap && (mapUserZoom > 1.01f || mapUserPan.sqrMagnitude > 1e-4f);
            if (mapResetR.enabled != show) mapResetR.enabled = show;
            if (mapResetPillSR != null && mapResetPillSR.enabled != show) mapResetPillSR.enabled = show;
            if (show)
            {
                SetTmpColor(mapResetTmp, new Color(0.08f, 0.08f, 0.1f, 1f));   // black text on the white pill
                if (mapResetPillSR != null && mapResetPillSR.sprite != null)
                {
                    // GLYPH-tight bounds (TMP renderer bounds span the whole line box, which reaches below the caps —
                    // the pill sat low with extra space under the text). Equal padding on all four sides.
                    Vector3 gMin, gMax; Bounds tb;
                    if (TryTmpGlyphBoundsWorld((mapResetTmp as Component).transform, out gMin, out gMax))
                        tb = new Bounds((gMin + gMax) * 0.5f, gMax - gMin);
                    else tb = mapResetR.bounds;
                    float pad = tb.size.y * 0.6f;                                   // one pad, all four margins
                    var ps = mapResetPillSR.sprite.bounds.size;
                    var pt = mapResetPillSR.transform;
                    pt.position = new Vector3(tb.center.x, tb.center.y, tb.center.z + 0.05f);   // centred on the visible glyphs
                    pt.rotation = Quaternion.identity;
                    float sx = (tb.size.x + 2f * pad) / Mathf.Max(0.001f, ps.x);
                    float sy = (tb.size.y + 2f * pad) / Mathf.Max(0.001f, ps.y);
                    var ls = pt.lossyScale; var lp = pt.localScale;
                    pt.localScale = new Vector3(sx * lp.x / Mathf.Max(1e-5f, ls.x), sy * lp.y / Mathf.Max(1e-5f, ls.y), 1f);   // world-size the pill regardless of parent scale
                }
            }
        }
    }

    void TeardownFrame()
    {
        hudFpsHalfMax = 0f;   // fix(1.0.0/B10): stale gear anchor after a rebuild at another ortho size
        if (frameRoot != null) { Destroy(frameRoot); frameRoot = null; }
        DestroyOwnedAssets();   // Materials / Meshes / Textures / Sprites we created for the frame (see Own)
        frameEdge.Clear(); frameBase.Clear(); frameTabs.Clear();
        botFleurT = botFleur2T = sepTopT = sepBotT = null; mapMaskTopT = mapMaskBotT = null; mapMaskTopR = mapMaskBotR = null; mapResetT = null; mapResetTmp = null; mapResetR = null; mapResetPillSR = null; tabColorCol = -1; frameInnerBotFrac = frameInnerTopFrac = float.NaN; selBox = null; sel.item = null; sel.invKey = null; sel.charmN = 0; sel.kind = -1; paneCursor = null; paneCursorFor = null;
        costPipRoot = null; charmBoardsFor = null;   // redesign: re-create cost pips + rebuild the per-clone charm cache for the fresh pane
        areaNameT = null; areaNameTmp = null; areaNameR = null; lastAreaZoneRaw = "\u0001"; lastAreaName = "\u0001";   // sentinel: the rebuilt TMP starts blank — force a re-set even for the same zone
        statsT = null; statsTmp = null; lastStats = "x"; statsR = null; battLevelR = null; noMapR = null; tabMidR = null;
        // EQUIPPED-CHARM ROW — all three of these must die with the frame.
        //   * equipCharmSRs kept every destroyed renderer and appended 11 more per rebuild, so the update
        //     loop then wrote sprites into the DEAD leading entries and disabled the live ones.
        //   * lastEquipStamp is the "charms changed?" gate; leaving it set means the rebuilt (empty,
        //     disabled) renderers are never populated, because the charm set legitimately has not changed.
        //     Net effect: one build-time config edit blanked the row until the process restarted.
        equipRowRoot = null; equipCharmSRs.Clear(); lastEquipStamp = int.MinValue;
        if (battIconSR != null) { Destroy(battIconSR.gameObject); battIconSR = null; } battIconT = null; battIconTex = null; battIconLvl = -2;
        gearT = null; gearSR = null; gearTex = null;   // B8: gear is a frameRoot child; sprite/tex are Own()-tracked
        if (battLevelT != null) { Destroy(battLevelT.gameObject); battLevelT = null; } battLevelTmp = null; lastBattLevel = "z";
    }

    // ---- bottom-screen companion (Map / Inventory / Charms) ------------------------------------
    // Additive feature: render a CLONE of HK's real pane onto the reclaimed ATTR layer (attrCam),
    // composited between the backdrop and the untouched HUD. Config-gated (cfg.companion). Cloning
    // (vs reusing HK's panes) keeps us independent of the Inventory Control FSM and of the top-screen
    // menu. Clones are built once and cached (SetActive-toggled on tab switch; rebuilt only when the
    // PlayerData that drives their content changes — see PaneStamp).
    const int COMP_MAP = 0, COMP_INV = 1, COMP_CHARM = 2;   // the 3 companion tabs (Journal/Guide were removed)

    // Tab-slide animation, mirroring the MAIN screen's pane carousel ("Tween Panes" = two simultaneous
    // iTweenMoveTo: old pane out one side, new pane in from the other), with the camera framing eased between
    // the two tabs' fits. Only ready (finalized) clones slide; a first build shows instantly as before.
    GameObject slideOutClone; float slideT = 1f; int slideDir; Vector3 slideStartCamPos; bool slideCamValid;

    // Per-frame slide driver. Runs AFTER ApplyCompanionCamera set the new tab's target framing: the camera is
    // eased from the outgoing tab's captured framing toward it, the incoming clone starts one view-width away in
    // the travel direction, and the outgoing clone (kept alive just for the slide; FSMs frozen) leaves the other
    // side — the same easeOut feel as HK's iTweenMoveTo pane carousel.
    void TabSlideTick()
    {
        if (slideT >= 1f) { if (slideOutClone != null) StowSlideClone(); return; }
        slideT = Mathf.Min(1f, slideT + Time.unscaledDeltaTime / Mathf.Max(0.05f, cfg.compTabSlideTime));
        float e = 1f - Mathf.Pow(1f - slideT, 3f);   // easeOutCubic
        // PURE HORIZONTAL swipe (user: no diagonal). The camera SNAPS to the new tab's framing (no blend — easing
        // it added the two framings' vertical/zoom difference under the slide = the diagonal). The outgoing pane is
        // COMPENSATED by the camera jump so it stays screen-anchored, then leaves horizontally.
        float W = attrCam != null ? attrCam.orthographicSize * attrCam.aspect * 2.15f : 20f;
        var incoming = tab.cur == COMP_MAP ? mapClone : paneClone;
        if (incoming != null && incoming != slideOutClone)
            incoming.transform.position += new Vector3(slideDir * W * (1f - e), 0f, 0f);   // slides IN from the travel side
        if (slideOutClone != null)
        {
            if (!slideOutClone.activeSelf) slideOutClone.SetActive(true);   // BuildCompanionTab stowed it — keep it visible for the slide
            Vector3 camDelta = slideCamValid && attrCam != null ? attrCam.transform.position - slideStartCamPos : Vector3.zero;
            slideOutClone.transform.position = compRoot.position + new Vector3(camDelta.x - slideDir * W * e, camDelta.y, 0f);   // screen-anchored, exits horizontally
        }
        if (slideT >= 1f) StowSlideClone();
    }

    // Finish the slide: put the outgoing clone back into its normal stowed state and pinned position.
    void StowSlideClone()
    {
        if (slideOutClone == null) { slideCamValid = false; return; }
        var cur = tab.cur == COMP_MAP ? mapClone : paneClone;
        if (slideOutClone != cur) slideOutClone.SetActive(false);
        slideOutClone.transform.localPosition = Vector3.zero;
        slideOutClone = null; slideCamValid = false; slideT = 1f;
    }

    // Config hot-reload re-apply. Live per-frame knobs need nothing (they're read every frame). Two classes need help:
    //   * BUILD-TIME knobs (only read inside BuildFrame/Build*: which widgets exist, separators on/off, area name/stats/
    //     no-map on/off) — BuildFrame early-returns while frameRoot exists, so before this an edit was a SILENT no-op
    //     until the frame happened to be torn down. Hash them; on change tear the frame down (rebuilt next frame).
    //   * the Inventory equipment grid offset (compEquipGridDY) — re-lay a finalized INV clone directly (works stowed).
    int frameBuildHash;
    void OnConfigReloaded()
    {
        paneNeedsFit = true;   // re-fit the pane/charms layout instantly (live tuning), then lock again
        int h = 17;
        h = h * 31 + cfg.compSepTop; h = h * 31 + cfg.compSepBot; h = h * 31 + cfg.compAreaName; h = h * 31 + cfg.compStats;
        h = h * 31 + cfg.compNoMapMsg; h = h * 31 + cfg.compEquipRow;
        if (h != frameBuildHash) { if (frameBuildHash != 0 && frameRoot != null) { TeardownFrame(); Dbg("HKDS cfg: build-time frame knob changed -> frame rebuilt"); } frameBuildHash = h; }
        if (ctrlActive) { ctrlLayoutPending = 1; ctrlLayoutWait = 0; }   // B5: re-measure + re-place the control-prompt line with the new compCtrl* values (live tuning)
        equipGridDirty = true;   // ReassertEquipment re-lays the grid on its next pass...
        if (invCloneCache != null && invRun.finalized) ReassertEquipment(invCloneCache);   // ...and right now for a finished clone (stowed or shown), so compEquipGridDY tunes live
    }

    // our own parent, parked far from the live game world so the clones can't overlap real geometry
    void EnsureCompRoot()
    {
        if (compRoot != null) return;
        var go = new GameObject("HKCompanionRoot");
        DontDestroyOnLoad(go);
        go.transform.position = new Vector3(20000f, 20000f, 0f);   // known point far from live geometry
        compRoot = go.transform;
    }

    // PRE-WARM: deep-cloning HK's ~1000-object panes is the one unavoidable hitch of a first tab open. Do it while the
    // player is NOT looking: as soon as the game session exists (inventoryFSM present = the load fade-in), build the
    // Inventory then the Charms clone, ONE per frame, PARKED far below compRoot (off every camera), and run their settle
    // windows there (PaneSettleTick with isCurrent=false); at settle-end each finalizes and STOWS itself. The first real
    // open then costs nothing (no clone, no 45-frame settle). A clone that stops being current mid-settle is parked (not
    // stowed) so its window completes in the background too — no more blank pane after a quick tab switch.
    bool prewarmDone;
    static readonly Vector3 PARK = new Vector3(0f, -100000f, 0f);   // local offset from compRoot while warming
    void PrewarmTick()
    {
        var gm = GameManager.instance; var invFsm = gm != null ? gm.inventoryFSM : null;
        if (invFsm == null) return;
        // Only once a SESSION is loading/playing: at the main menu the inventory FSM exists but PlayerData is not the
        // save's yet, so a clone made there is stale (its fingerprint mismatches in-game -> rebuilt on first open anyway).
        GlobalEnums.GameState st; try { st = gm.gameState; } catch { return; }
        bool entering = st == GlobalEnums.GameState.ENTERING_LEVEL;   // black screen: load fade-in AND every room transition
        bool playing  = st == GlobalEnums.GameState.PLAYING;
        if (!(entering || playing)) return;
        // 1) tick any warming clone (active, unfinished, not the shown tab)
        if (invCloneCache != null && invCloneCache != paneClone && !invRun.finalized) PaneSettleTick(invCloneCache, invRun, true, false);
        if (charmCloneCache != null && charmCloneCache != paneClone && !charmRun.finalized) PaneSettleTick(charmCloneCache, charmRun, false, false);
        // 2) while the screen is black (level transition), refresh a STALE finished cache (fingerprint changed since it was
        //    built — spell upgrade, new charm, key used...) so the deep re-clone happens here, not on the next tab open.
        if (entering)
        {
            if (invCloneCache != null && invCloneCache != paneClone && invRun.finalized && PaneStamp(false, invStamp) != invStamp) { Destroy(invCloneCache); invCloneCache = null; prewarmDone = false; Dbg("HKDS prewarm: Inventory cache stale -> rebuilding during transition"); }
            if (charmCloneCache != null && charmCloneCache != paneClone && charmRun.finalized && PaneStamp(true, charmStamp) != charmStamp) { Destroy(charmCloneCache); charmCloneCache = null; prewarmDone = false; Dbg("HKDS prewarm: Charms cache stale -> rebuilding during transition"); }
        }
        if (prewarmDone) return;
        // 3) build the missing clones, one deep clone per frame
        EnsureCompRoot();
        if (!ReferenceEquals(paneSrcRef, (UnityEngine.Object)invFsm))
        {   // inventory subtree rebuilt (save-load / scene reload) -> BOTH cached panes are stale (same rule as BuildCompanionTab)
            if (invCloneCache != null) { Destroy(invCloneCache); invCloneCache = null; }
            if (charmCloneCache != null) { Destroy(charmCloneCache); charmCloneCache = null; }
            paneSrcRef = invFsm;
        }
        if (invCloneCache == null)
        {
            var src = FindDeep(invFsm.transform, "Inv"); if (src == null) return;
            invCloneCache = BuildPaneClone(COMP_INV, src); invStamp = PaneStamp(false, invStamp);
            invCloneCache.transform.localPosition = PARK;
            Dbg("HKDS prewarm: Inventory clone built (parked)");
            return;
        }
        if (charmCloneCache == null)
        {
            var src = FindDeep(invFsm.transform, "Charms"); if (src == null) return;
            charmCloneCache = BuildPaneClone(COMP_CHARM, src); charmStamp = PaneStamp(true, charmStamp);
            charmCloneCache.transform.localPosition = PARK;
            Dbg("HKDS prewarm: Charms clone built (parked)");
            return;
        }
        prewarmDone = true;
    }

    void UpdateCompanion(Camera src)
    {
        EnsureCompRoot();

        // B8: the Tweaks pane occupies the context box while open (gear icon toggles it). It stows the
        // normal content pane and owns the camera; tab taps close it (PollTouch). Gated on the hub checkbox.
        if (HkStageHooks.TweaksAvailable && tweaksOpen && HkStageHooks.TweaksMenuVisible)
        {
            TweaksPaneTick(src);   // (Tick's own PollTouch call after UpdateCompanion still runs — no double poll here)
            return;
        }
        if (tweaksRoot != null && tweaksRoot.activeSelf) tweaksRoot.SetActive(false);   // closed -> hidden

        // Effective tab: a bottom-panel tab-tap (touch) overrides the config tab. Rebuild on change.
        // HK instantiates gameMap lazily (only on first map-open), so keep retrying the Map build until
        // the clone actually exists — then it persists and stays visible thereafter.
        int prevTab = tab.cur;
        tab.cur = tab.tap >= 0 ? tab.tap : Mathf.Clamp(cfg.compTab, 0, 2);
        if (tab.cur != prevTab)
        {
            // (pinch zoom/pan PERSIST across tab switches — only the RESET button / an area change clears them)
            if (slideOutClone != null) StowSlideClone();   // a switch mid-slide finishes the previous slide instantly
            var fromClone = prevTab == COMP_MAP ? mapClone : prevTab == COMP_INV ? invCloneCache : prevTab == COMP_CHARM ? charmCloneCache : null;
            bool fromReady = fromClone != null && (prevTab == COMP_MAP || (prevTab == COMP_INV ? invRun.finalized : charmRun.finalized));
            if (cfg.compTabSlide == 1 && fromReady && prevTab >= 0 && attrCam != null)
            {
                slideOutClone = fromClone; slideT = 0f;
                slideDir = TAB_TO_COL[Mathf.Clamp(tab.cur, 0, 2)] > TAB_TO_COL[Mathf.Clamp(prevTab, 0, 2)] ? 1 : -1;   // travel direction from the display column order slideStartCamPos = attrCam.transform.position; slideCamValid = true;
            }
            sel.invKey = null; sel.charmN = 0; sel.item = null; sel.kind = -1;   // tab change clears the item selection
            if (invCloneCache != null) ClearInvDetail(invCloneCache);            // ...and blank the cached INV detail (cache reuse skips the settle-clear)
        }
        bool needBuild = tab.built != tab.cur || (tab.cur == COMP_MAP && mapClone == null);
        if (needBuild)
        {
            BuildCompanionTab(tab.cur);
            bool built = (tab.cur == COMP_MAP) ? mapClone != null
                       : (tab.cur == COMP_CHARM || tab.cur == COMP_INV) ? paneClone != null : true;
            if (built) tab.built = tab.cur;   // lock once built; retry next frame if the source wasn't ready
        }
        // Self-heal: guarantee the CURRENT tab's clone is actually shown every frame. A stow/show desync — or an
        // early-return before the SetActive on a transient GameManager/PlayerData null — could otherwise leave a
        // cached clone hidden (the intermittent "charms not rendering"). Cheap: one activeSelf check.
        // fix#3(157-fb): while the opening attribution draws over the live companion, the MAP CONTENT
        // stays stowed (frame/tabs/HUD keep rendering; the credit needs the centre clear) — the self-heal
        // below would otherwise flip it straight back on.
        if (creditNow)
        { if (mapClone != null && mapClone.activeSelf) { mapStowStamp = MapContentStamp(); mapClone.SetActive(false); } }
        else if (tab.cur == COMP_MAP) { if (mapClone != null && !mapClone.activeSelf) mapClone.SetActive(true); }
        else if ((tab.cur == COMP_CHARM || tab.cur == COMP_INV) && paneClone != null && !paneClone.activeSelf) paneClone.SetActive(true);

        MapTick();   // B6: availability gate + deferred SetupMap + zone/room/compass watchers + Quill/pin watches (EVERY frame — it also clears mapAvailable off the Map tab)

        // FORCED COINCIDENCE (the only framing that reliably rendered): pin the clone at compRoot every
        // frame (overriding SetupMap/GameMap re-positioning), and sit attrCam at compRoot. Auto-frame on
        // the clone's content bounds when available (centres it); else fall back to compRoot (still
        // renders, just off-centre). compZoom = ortho zoom; compOff shifts the view.
        Vector3 frameCenter = compRoot.position;
        if (tab.cur == COMP_MAP && mapClone != null && mapAvailable)
        {
            MapFrameTick(ref frameCenter);   // B6: pin the map clone + full-area fit (per area, no burst)
            MapPinchTick();                  // B5: pinch zoom / drag pan / tap reset on the map (dusklight-style)
        }
        else if ((tab.cur == COMP_CHARM || tab.cur == COMP_INV) && paneClone != null)
        {
            paneClone.transform.localPosition = Vector3.zero;   // pin at compRoot
            PaneSettleTick(paneClone, RunFor(tab.cur == COMP_CHARM), tab.cur == COMP_INV, true);   // B4: open-kicks + settle/freeze window (INV finalize; charms detail re-assert)
            CharmsTick();       // B7: equip/unequip watch -> re-darken the charm grid in place
            if (tab.cur == COMP_INV && invRun.finalized && (Time.frameCount % 30) == 0) RefreshInvCounters(paneClone);   // B4: geo/relic/ore/egg/key numbers stay live (~2x/s)
            compFrameTick++;
            if (paneNeedsFit)
            {
                paneNeedsFit = false;
                if (tab.cur == COMP_CHARM && cfg.compCharmsRedesign == 1)
                    ApplyFit(LayoutCharmsRedesign(paneClone));   // re-lays detail-top/grid-below AND returns the fit
                else { Vector3 c; float s; if (TryPaneBounds(paneClone, out c, out s, tab.cur == COMP_CHARM)) fit = new FitResult { center = c, ortho = s, valid = true }; }
            }
            if (fit.valid) frameCenter = fit.center;
        }
        ApplyCompanionCamera(frameCenter);   // B2: box rect/aspect -> per-tab ortho -> position (BEFORE BuildFrame/PositionFrame: they read attrCam.orthographicSize)
        TabSlideTick();                      // B2: HK-style pane slide (offsets BOTH clones + eases the camera between the two tabs' framings)
        if (cfg.compFrame == 1) { BuildFrame(); PositionFrame(); }
        else if (frameRoot != null) TeardownFrame();
        ReassertControlPrompt();   // re-pin the control-prompt line at pre-render (beats ActionButtonIcon's Update)
        if (cfg.debug == 1 && (Time.frameCount % 90) == 0)   // frame-count gate: compFrameTick only advances on the pane tabs, so on the Map tab the old %90 was true EVERY frame -> logcat flood at debug=1
            Debug.Log($"HKDS mapframe attrPos={attrCam.transform.position} ortho={attrCam.orthographicSize:F1} centered={fit.valid} c={fit.center}");
    }

    // [B2] Companion camera: confine to the BOX (viewport sub-rect, aspect matched so nothing stretches) or
    // full-screen; per-tab zoom (Map=compZoom, Inventory=compPaneZoom, Charms=compCharmZoom) with the frame-fit
    // margin; then position at frameCenter with the per-tab offsets. Must run BEFORE BuildFrame/PositionFrame.
    void ApplyCompanionCamera(Vector3 frameCenter)
    {
        attrCam.orthographic = true;
        // Confine the whole companion (map + frame) to its own BOX below the HUD (a viewport sub-rect), or
        // full-screen when compBox is off. aspect is matched to the box so nothing stretches.
        if (cfg.compBox == 1)
        {
            attrCam.rect = new Rect(cfg.compBoxX, cfg.compBoxY, cfg.compBoxW, cfg.compBoxH);
            attrCam.aspect = (cfg.compBoxW * BOTTOM_W) / Mathf.Max(1f, cfg.compBoxH * BOTTOM_H);
        }
        else { attrCam.rect = new Rect(0f, 0f, 1f, 1f); attrCam.aspect = (float)BOTTOM_W / BOTTOM_H; }
        // MAP tab (full-area fit): fit.ortho already has the equal margin against the inner rect baked in; centre the
        // area on the inner rect's vertical centre (mapInnerYc, cam-relative ortho fracs). compZoom>1 zooms in (the area
        // stays centred in the rect). compFrameFit / compMapCenterY do NOT apply here — the margin knob replaces them.
        if (tab.cur == COMP_MAP && mapFitIsArea && fit.valid && mapAvailable)
        {
            float baseOrtho = Mathf.Max(0.5f, fit.ortho / Mathf.Max(0.05f, cfg.compZoom));
            attrCam.orthographicSize = Mathf.Max(0.5f, baseOrtho / mapUserZoom);   // pinch zoom on top of the area fit
            float o = attrCam.orthographicSize;
            // clamp the pan so the view never scrolls past the fitted area's edges (available slack shrinks with zoom)
            float maxPanX = Mathf.Max(0f, baseOrtho * attrCam.aspect * (1f - 1f / mapUserZoom));
            float maxPanY = Mathf.Max(0f, baseOrtho * (1f - 1f / mapUserZoom));
            mapUserPan = new Vector2(Mathf.Clamp(mapUserPan.x, -maxPanX, maxPanX), Mathf.Clamp(mapUserPan.y, -maxPanY, maxPanY));
            attrCam.transform.position = new Vector3(frameCenter.x + cfg.compOffX + mapUserPan.x,
                                                     frameCenter.y - mapInnerYc * o + cfg.compOffY + mapUserPan.y, frameCenter.z - 10f);
            attrCam.transform.rotation = Quaternion.identity;
            return;
        }
        // Fit the pane to the box (fit.ortho sizes to the content bounds). When the context-box frame is on, zoom OUT
        // by compFrameFit so the content sits INSIDE the frame border rather than filling the box edge-to-edge under
        // the ornaments. compZoom>1 zooms in further, <1 out.
        float fitMargin = (cfg.compFrame == 1) ? Mathf.Max(1f, cfg.compFrameFit) : 1f;
        // Per-tab zoom: Map=compZoom, Inventory=compPaneZoom, Charms=compCharmZoom (they need different fills).
        float zoomDiv = tab.cur == COMP_CHARM ? cfg.compCharmZoom : tab.cur == COMP_INV ? cfg.compPaneZoom : cfg.compZoom;
        attrCam.orthographicSize = Mathf.Max(0.5f, (fit.valid ? fit.ortho * fitMargin : 8.71f) / Mathf.Max(0.05f, zoomDiv));
        float ortho = attrCam.orthographicSize;
        // when framed, shift the camera DOWN so the content sits in the UPPER part of the box (inside the frame).
        float mapShift = (cfg.compFrame == 1) ? cfg.compMapCenterY * ortho : 0f;
        float tabOffX = (tab.cur == COMP_INV ? cfg.compInvOffX : 0f) * ortho;     // +X => camera right => content LEFT (inventory)
        float tabOffY = (tab.cur == COMP_CHARM ? cfg.compCharmOffY : 0f) * ortho; // +Y => camera up => content DOWN (charms)
        attrCam.transform.position = new Vector3(frameCenter.x + cfg.compOffX + tabOffX,
                                                 frameCenter.y + cfg.compOffY - mapShift + tabOffY, frameCenter.z - 10f);
        attrCam.transform.rotation = Quaternion.identity;
    }

    // Fit the pane (Charms/Inventory) to the box. Frame the icon grid PLUS the right-side detail panel
    // (name/desc/cost) so the description is visible — the old fit excluded the detail TMP and culled the far
    // items, zooming into the left grid only. For CHARMS the equipped-charms + notch row is deliberately left
    // OUT (it's pulled to a top-right overlay). HK UI scripts aren't decompiled, so match by name/type.
    bool TryPaneBounds(GameObject go, out Vector3 center, out float fitOrtho, bool charmsGrid)
    {
        center = Vector3.zero; fitOrtho = 8f;
        Bounds b = new Bounds(); bool have = false; int kept = 0, total = 0;
        // Per-tab content measurement (B7 / B4 own their pane's fit rules); the shared tail below turns the bounds
        // into a centre + fit ortho for the box.
        if (charmsGrid) CharmsPaneBounds(go, ref b, ref have, ref kept);
        else if (!InvPaneBounds(go, ref b, ref have, ref kept, ref total)) return false;
        if (!have) return false;
        b.Expand(b.size * 0.06f);   // small margin
        center = b.center;
        float aspect = attrCam != null ? attrCam.aspect : (float)BOTTOM_W / BOTTOM_H;
        fitOrtho = Mathf.Max(b.extents.y, b.extents.x / aspect) * 1.05f;
        if (cfg.debug == 1) Dbg($"HKDS paneFit {(charmsGrid ? "CHARM" : "INV")} kept={kept}/{total} ext=({b.extents.x:F1},{b.extents.y:F1}) fitOrtho={fitOrtho:F1}");
        return have;
    }

    // fix#6(161-fb): skins changed — every cached clone holds materials frozen at clone time, so
    // destroy them all; the normal build/prewarm paths re-clone from the now-reskinned sources.
    void InvalidateCompanionClones()
    {
        try
        {
            paneSrcRef = null;   // BuildCompanionTab's rebuilt-source path destroys + rebuilds both panes
            if (mapClone != null) { Destroy(mapClone); mapClone = null; mapGm = null; }
            mapSrcRef = null;
            tab.built = -1;      // force BuildCompanionTab for the tab currently on screen
            lastEquipStamp = int.MinValue;   // equipped-charm icon row re-snapshots
            Dbg("HKDS companion clones invalidated (skin change)");
        }
        catch (Exception e) { WarnOnce("clone invalidate", e); }
    }

    void BuildCompanionTab(int tab)
    {
        if (compRoot == null) return;

        // Stow the clones we are NOT about to show. cache=1 -> SetActive(false): keep them for instant re-show,
        // so a tab tap never re-deep-clones an 849-object map / 100-FSM pane (the per-switch stutter).
        if (tab != COMP_MAP   && mapClone != null)        { if (mapClone.activeSelf) mapStowStamp = MapContentStamp(); mapClone.SetActive(false); }   // B6: remember what the map showed
        // A clone whose settle window hasn't finished stays ACTIVE (its FSMs must tick) but PARKED off-camera; PaneSettleTick
        // (via PrewarmTick, isCurrent=false) finalizes and stows it when done. Finished clones stow immediately.
        if (tab != COMP_INV   && invCloneCache != null)   { if (invRun.finalized)   invCloneCache.SetActive(false);   else invCloneCache.transform.localPosition = PARK; }
        if (tab != COMP_CHARM && charmCloneCache != null) { if (charmRun.finalized) charmCloneCache.SetActive(false); else charmCloneCache.transform.localPosition = PARK; }
        fit.valid = false;

        if (tab == COMP_MAP)
        {
            paneClone = null;
            var gm = GameManager.instance;
            var srcMap = gm != null ? gm.gameMap : null;   // runtime "Game Map" object (null until built)
            if (srcMap == null) { Dbg("HKDS companion: no gameMap yet"); return; }
            if (mapClone != null && !ReferenceEquals(mapSrcRef, (UnityEngine.Object)srcMap)) { Destroy(mapClone); mapClone = null; mapGm = null; }   // source rebuilt (save-load) -> invalidate cache
            if (mapClone == null) { BuildMapClone(srcMap); mapSrcRef = srcMap; }
            else
            {
                mapClone.SetActive(true);
                // Re-sync the cached map ONLY if the PlayerData its content depends on changed while it was hidden (rest,
                // area, map/quill/pin purchase, compass, markers) — that re-runs the deferred SetupMap + SetupQuickMap.
                // Otherwise the clone is still exact: a plain room move just needs the 'you are here' icon re-anchored
                // (GameMap.Update didn't run while inactive). The full-area fit is per area, so no re-fit either way.
                if (mapContentVisible && MapContentStamp() != mapStowStamp) mapNeedsSetup = true;
                compassPending = true;
                mapAreaBTries = 1;   // one re-measure on show (no-op in area mode; refreshes the legacy fallback fit)
            }
        }
        else if (tab == COMP_CHARM || tab == COMP_INV)
        {
            bool charms = tab == COMP_CHARM;
            var invFsm = GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
            if (invFsm == null) { Dbg("HKDS companion: no inventoryFSM yet"); return; }
            var srcT = FindDeep(invFsm.transform, charms ? "Charms" : "Inv");
            if (srcT == null) { Dbg("HKDS companion: inventory pane not found (retry)"); return; }
            // Inventory subtree rebuilt (save-load / scene reload) -> BOTH cached panes are stale.
            if (!ReferenceEquals(paneSrcRef, (UnityEngine.Object)invFsm))
            {
                if (invCloneCache != null) { Destroy(invCloneCache); invCloneCache = null; }
                if (charmCloneCache != null) { Destroy(charmCloneCache); charmCloneCache = null; }
                paneSrcRef = invFsm; prewarmDone = false;   // let PrewarmTick rebuild the other tab's clone in the background
            }
            GameObject cur = charms ? charmCloneCache : invCloneCache;
            int last = charms ? charmStamp : invStamp;
            int stamp = PaneStamp(charms, last);
            // Rebuild the clone only when it's missing or the PlayerData that drives its content changed
            // (equip/acquire/upgrade). Otherwise the cached clone's icons are still correct -> pure SetActive
            // toggle: no re-clone, no FSM re-run — the smooth path on rapid tab-switching.
            if (cur == null || stamp != last)
            {
                if (cur != null) Destroy(cur);
                cur = BuildPaneClone(tab, srcT);
                if (charms) { charmCloneCache = cur; charmStamp = stamp; }
                else        { invCloneCache = cur;   invStamp = stamp; }
            }
            else
            {
                cur.SetActive(true); paneNeedsFit = true;
                if (charms) PopulateCharmDetail(cur);   // a pre-warmed clone skipped its settle-end detail populate (would have drawn at compRoot while parked) -> set text now; the fit dispatch below re-lays + applies its fit
                else if (invRun.finalized) { ReassertEquipment(cur); RefreshInvCounters(cur); }   // cached INV re-show: equipment visibility (e.g. used Elegant Key) + counters current, no rebuild
            }
            paneClone = cur;
        }
    }

    // Build (Instantiate) a pane clone (Charms/Inv) once: enable the populate FSMs, arm the kick+settle, and for
    // charms populate the right-side detail. Returns the clone; the caller caches it.
    GameObject BuildPaneClone(int tab, Transform srcT)
    {
        bool charms = tab == COMP_CHARM;
        var staging = new GameObject("HKPaneCloneStaging");
        staging.SetActive(false);
        GameObject pane = null;
        try
        {
            // Instantiate below an inactive parent so no cloned behaviour gets
            // Awake/OnEnable before the resident pane has been sanitized. The
            // live inventory hierarchy is never deactivated or otherwise
            // mutated by this operation.
            pane = Instantiate(srcT.gameObject, staging.transform);
            pane.SetActive(false);
            pane.name = charms ? "HKCharmClone" : "HKInvClone";
            pane.transform.localPosition = Vector3.zero;
            SetLayerRecursive(pane.transform, ATTR_LAYER);
            // Runtime iTween instances carry launch arguments that are valid
            // only for the source menu. Their Awake dereferences those stale
            // arguments when a clone becomes active, so remove those visual
            // animation drivers before first activation.
            foreach (var tween in pane.GetComponentsInChildren<MonoBehaviour>(true))
                if (tween != null && tween.GetType().Name == "iTween") DestroyImmediate(tween);
            // POPULATE FSMs read PlayerData + lay out the dynamic lists — keep enabled (INV is kicked below). NAV/INPUT
            // FSMs fight the real menu -> stay disabled. "UI Charms" idles (enabled, not kicked). This is the
            // known-good enabled set (kicking/enabling more re-lays the charm grid to 67u).
            foreach (var fsm in pane.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                string n = fsm.FsmName;
                if (charms) fsm.enabled = (n == "UI Charms" || n == "charm_show_if_collected");
                else        fsm.enabled = (n == "Check Active" || n == "Build Equipment List" || n == "Set Pieces");
            }
            pane.transform.SetParent(compRoot, false);
            pane.SetActive(true);
            if (!charms) InvPaneInit(pane);      // B4: soul-vessel renderers on
            else         CharmsPaneInit(pane);   // B7: equipped row + default detail (needs an ACTIVE pane: it reads renderer bounds)
            paneNeedsFit = true;
            var run = RunFor(charms); run.finalized = false;
            run.kicks = charms ? 0 : 8;   // kick only INV's populate FSMs; charms need no kick (more kicks: let Build Equipment List finish activating the ability/key/consumable row, not just the trinkets)
            run.settle = charms ? 20 : 45;   // populate, THEN freeze all pane FSMs (stops the per-frame NRE leak source); INV needs longer for the equipment row to build
            if (cfg.debug == 1) Dbg($"HKDS pane clone built tab={tab} renderers={pane.GetComponentsInChildren<Renderer>(true).Length}");
            return pane;
        }
        finally { Destroy(staging); }
    }

    // Fingerprint the PlayerData that drives a pane's content, so the cached clone is rebuilt only when it changes.
    // Cheap: a handful of ints/bools read once, only on a tab SHOW. On failure returns an ever-changing value so
    // the caller rebuilds (correct, just not cached that time).
    int PaneStamp(bool charms, int fallback)
    {
        var pd = PlayerData.instance; if (pd == null) return fallback;   // transient PD-null (scene load) -> "unchanged", no rebuild
        int h = 17;
        try
        {
            h = charms ? CharmsStampHash(pd, h) : InvStampHash(pd, h);   // B7 / B4 own what drives their clone's content
        }
        catch { return fallback; }
        return h;
    }

    void TeardownCompanion()
    {
        if (mapClone != null) { Destroy(mapClone); mapClone = null; mapGm = null; mapContentVisible = false; mapAreaBValid = false; mapAreaBFor = null; mapFitIsArea = false; }
        if (invCloneCache != null) { Destroy(invCloneCache); invCloneCache = null; }
        if (charmCloneCache != null) { Destroy(charmCloneCache); charmCloneCache = null; }
        paneClone = null;
        slideOutClone = null; slideT = 1f; slideCamValid = false;   // any in-flight tab slide dies with the clones
        refsInv = refsCharm = null;   // B4 PaneRefs caches die with their clones
        nudgedFocusFor = null; nudgedFocusAmt = 0f;   // don't pin a destroyed clone
        // Control-prompt overlay: its glyph/verb live under compRoot (NOT frameRoot), so they survive the
        // teardown — without this reset a session quit with a prompt showing left a stray PRESS/HOLD glyph
        // rendering into the next session, re-pinned against a destroyed pane.
        ctrlActive = false; ctrlSrcIconT = null; ctrlHkRs = null; ctrlHkRsFor = null; ctrlPlaced = false; ctrlLayoutPending = 0; ctrlGlyphPending = false;
        ctrlTnR = ctrlTdR = null; ctrlTdT = null;
        try { if (ctrlMyGlyph != null) ctrlMyGlyph.enabled = false; } catch { }
        try { if (ctrlMyVerbTmp != null) { var vr = (ctrlMyVerbTmp as Component).GetComponent<Renderer>(); if (vr != null) vr.enabled = false; } } catch { }
        prewarmDone = false;
        mapSrcRef = null; paneSrcRef = null; invStamp = int.MinValue; charmStamp = int.MinValue;
        if (tweaksRoot != null) { Destroy(tweaksRoot); tweaksRoot = null; } tweakRows.Clear(); tweaksOpen = false;   // B8: pane clones die with the session
        TeardownFrame();
        tab.built = -1;
        fit.valid = false;
    }
}
