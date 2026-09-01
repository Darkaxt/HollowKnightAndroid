using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

// [U] Shared helpers used across the partial files: hierarchy find / bounds / layers, TMP reflection setters,
// localization lookups, and the texture bake/cache used for the frame art and the title logo.
public partial class HKDualScreen
{
    // Assets WE create for the frame (Materials, Meshes, Texture2Ds, Sprites): Destroy(frameRoot) frees the GameObjects
    // but never these -> they leaked on every frame rebuild (companion toggle, compFrame toggle, config-driven rebuild).
    // Register each one; TeardownFrame destroys them.
    readonly List<UnityEngine.Object> frameAssets = new List<UnityEngine.Object>();
    T Own<T>(T o) where T : UnityEngine.Object { if (o != null) frameAssets.Add(o); return o; }
    void DestroyOwnedAssets() { foreach (var o in frameAssets) if (o != null) Destroy(o); frameAssets.Clear(); }

    // Once-per-site warning for swallowed exceptions on PER-FRAME paths. A bare `catch { }` there hides a PERMANENT
    // failure (a renamed HK object, a missing FSM) forever; this logs the first hit per site (always, not just debug=1)
    // and stays silent after — costs nothing steady-state, but the failure is visible in logcat.
    static readonly HashSet<string> _warned = new HashSet<string>();
    static void WarnOnce(string site, Exception e)
    {
        if (!_warned.Add(site)) return;
        Debug.Log($"HKDS {site} err (first occurrence, further ones suppressed): {e.GetType().Name}: {e.Message}");
    }

    // CHARM STATE, frame-cached. Three separate 40-iteration `pd.GetBool("equippedCharm_" + i)` loops used to run
    // EVERY frame (equipped-charm row on every tab, the Charms equip watch, the first-equipped scan) = ~120 string
    // allocations + ~120 reflection lookups per frame. Now: PlayerData.equippedCharms (HK's own List<int> of equipped
    // ids, kept in sync by EquipCharm/UnequipCharm) is folded into a bitmask ONCE per frame; got/broken use pre-built
    // key strings (read only on tab show / layout).
    struct CharmState { public ulong equipped; public int hash, count; public bool Has(int id) => id > 0 && id < 64 && ((equipped >> id) & 1UL) != 0; }
    CharmState _charms; int _charmsFrame = -1;
    static readonly string[] K_GOT = new string[41], K_BROKEN = new string[41], K_EQ = new string[41];
    static bool _charmKeysBuilt;
    static void BuildCharmKeys() { if (_charmKeysBuilt) return; for (int i = 1; i <= 40; i++) { K_GOT[i] = "gotCharm_" + i; K_BROKEN[i] = "brokenCharm_" + i; K_EQ[i] = "equippedCharm_" + i; } _charmKeysBuilt = true; }
    CharmState Charms()
    {
        if (_charmsFrame == Time.frameCount) return _charms;
        _charmsFrame = Time.frameCount;
        var cs = new CharmState { hash = 17 };
        try
        {
            var pd = PlayerData.instance;
            var list = pd != null ? pd.equippedCharms : null;
            if (list != null)
            {
                for (int k = 0; k < list.Count; k++) { int id = list[k]; if (id > 0 && id < 64) cs.equipped |= 1UL << id; }
                for (int i = 1; i <= 40; i++) if (((cs.equipped >> i) & 1UL) != 0) { cs.hash = cs.hash * 31 + i; cs.count++; }   // same ordered hash the old loops produced
            }
            else if (pd != null)   // fallback: the bools (allocation-free thanks to K_EQ)
            {
                BuildCharmKeys();
                for (int i = 1; i <= 40; i++) if (pd.GetBool(K_EQ[i])) { cs.equipped |= 1UL << i; cs.hash = cs.hash * 31 + i; cs.count++; }
            }
        }
        catch { }
        _charms = cs;
        return cs;
    }
    bool CharmGot(PlayerData pd, int id)    { BuildCharmKeys(); return id > 0 && id <= 40 && pd != null && pd.GetBool(K_GOT[id]); }
    bool CharmBroken(PlayerData pd, int id) { BuildCharmKeys(); return id > 0 && id <= 40 && pd != null && pd.GetBool(K_BROKEN[id]); }

    // TMP reflection, CACHED. HK's TextMeshPro isn't referenced at compile time, so we drive TMPs by reflection —
    // but `c.GetType().GetProperty("color")` was being re-resolved on ~7 labels EVERY frame. Resolve once per (Type, name).
    static readonly Dictionary<Type, Dictionary<string, System.Reflection.PropertyInfo>> _tmpProps = new Dictionary<Type, Dictionary<string, System.Reflection.PropertyInfo>>();
    static System.Reflection.PropertyInfo TmpProp(Component c, string name)
    {
        var ty = c.GetType();
        if (!_tmpProps.TryGetValue(ty, out var d)) { d = new Dictionary<string, System.Reflection.PropertyInfo>(); _tmpProps[ty] = d; }
        if (!d.TryGetValue(name, out var pi)) { pi = ty.GetProperty(name); d[name] = pi; }
        return pi;
    }
    static void SetTmpColor(Component c, Color col) { if (c == null) return; try { TmpProp(c, "color")?.SetValue(c, col, null); } catch { } }

    // fix(1.0.2) [user: "explore deep all that been moved to bottom screen and should be back to main
    // screen if the settings is off"]: routing a game object to our private layers makes it visible ONLY to
    // the bottom cameras, because StripPrivateLayers removes those layers from every other camera. So simply
    // disabling the bottom cameras when dual-screen is off does not hand anything back to the top — it makes
    // it disappear. Remember each object's ORIGINAL layer the first time we move it, so it can be put back.
    readonly Dictionary<Transform, int> routedLayers = new Dictionary<Transform, int>();

    void RouteToLayer(Transform t, int layer)
    {
        if (t == null) return;
        if (!routedLayers.ContainsKey(t)) routedLayers[t] = t.gameObject.layer;
        SetLayerRecursive(t, layer);
    }

    // Put every routed game object back where HK had it. Called when dual-screen is switched off.
    void RestoreRoutedLayers()
    {
        foreach (var kv in routedLayers)
            if (kv.Key != null) SetLayerRecursive(kv.Key, kv.Value);
        routedLayers.Clear();
    }

    // Recursive find-by-name (includes inactive children) for reaching inventory ornaments.
    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) { var r = FindDeep(root.GetChild(i), name); if (r != null) return r; }
        return null;
    }

    // Encapsulated world bounds of an item = all its active child renderers (many inv items render their sprite on a
    // CHILD, so the item component's own renderer is a tiny placeholder). Falls back to a zero-size bounds at pos.
    static Bounds ItemBounds(Transform t)
    {
        Bounds b = default; bool has = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) b = new Bounds(t.position, Vector3.zero);
        return b;
    }

    // Companion art (bottom-screen title logo + start-menu fleur) comes from the GAME'S OWN loaded assets at
    // runtime — never shipped inside our APK. That keeps the build free of Team Cherry art (so a code-only APK
    // stays distributable; the art only ever exists on a device that already has the user's own game data).
    //
    // Why we copy instead of referencing HK's sprite directly: the source art lives in tk2d atlases that are
    // only GPU-resident while their own menu is open, so a live atlas sprite draws NOTHING on our bottom screen
    // during gameplay. So we bake the region once into a Texture2D WE own (always resident), and cache that PNG
    // in persistentDataPath purely as a REGENERABLE cache — if it's ever wiped (uninstall/clear-data) we simply
    // re-bake it from the game assets on the next launch. No user action, nothing to lose.
    Texture2D LoadCompanionTex(string name)
    {
        try
        {
            string p = Path.Combine(Application.persistentDataPath, name + ".png");
            if (File.Exists(p))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (t.LoadImage(File.ReadAllBytes(p))) return t;   // regenerable cache hit
            }
        }
        catch { }
        return null;   // caller bakes from the game assets, then CacheCompanionTex()es the result
    }

    // Persist a baked texture as the regenerable cache (best-effort; failure just means we re-bake next boot).
    void CacheCompanionTex(string name, Texture2D tex)
    {
        try { if (tex != null) File.WriteAllBytes(Path.Combine(Application.persistentDataPath, name + ".png"), tex.EncodeToPNG()); }
        catch (Exception e) { Dbg($"HKDS cache '{name}' err {e.Message}"); }
    }

    // Find a Texture2D already loaded in memory by name (HK's own asset), e.g. "vheart_title" for the title logo.
    static Texture2D FindLoadedTexture(string nameEquals, string nameContains)
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].name == nameEquals) return all[i];
            if (!string.IsNullOrEmpty(nameContains))
                for (int i = 0; i < all.Length; i++) if (all[i] != null && NameHas(all[i].name, nameContains)) return all[i];
        }
        catch { }
        return null;
    }

    // Bake a sub-region of a source texture (given by UV rect) into a NEW Texture2D we own. Goes through a
    // RenderTexture blit so it works on compressed/non-readable atlas textures (ReadPixels off the GPU).
    static Texture2D BakeRegion(Texture src, Rect uv)
    {
        try
        {
            if (src == null) return null;
            int w = Mathf.Clamp(Mathf.RoundToInt(uv.width * src.width), 1, 4096);
            int h = Mathf.Clamp(Mathf.RoundToInt(uv.height * src.height), 1, 4096);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Graphics.Blit(src, rt, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(false);
            RenderTexture.active = prevActive; RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
        catch { return null; }
    }

    static bool IsUnderNamed(Transform t, string ancestorName)
    {
        for (var p = t; p != null; p = p.parent) if (p.name == ancestorName) return true;
        return false;
    }

    void Dbg(string m) { if (cfg.debug == 1) Debug.Log(m); }

    static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
    }

    // Case-insensitive substring test that does NOT allocate (unlike name.ToLowerInvariant().Contains()).
    static bool NameHas(string s, string sub) => s != null && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;

    // Best-effort visible alpha of a renderer: SpriteRenderer.color.a, else the co-located TMP's alpha/color.a, else 1.
    static float EffectiveAlpha(Renderer r)
    {
        var sr = r as SpriteRenderer; if (sr != null) return sr.color.a;
        var comps = r.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i]; if (c == null || !c.GetType().Name.Contains("TextMeshPro")) continue;
            try { var a = c.GetType().GetProperty("alpha")?.GetValue(c); if (a is float af) return af; } catch { }
            try { var col = c.GetType().GetProperty("color")?.GetValue(c); if (col is Color cc) return cc.a; } catch { }
        }
        return 1f;
    }

    // PlayMaker's global static FsmExecutionStack (HutongGames.PlayMaker) wraps FSM execution in a bare
    // PushFsm(this) ... PopFsm() with NO try/finally. Our detached Inventory/Charms clones run/kick their
    // populate FSMs while missing the real inventory's singleton wiring, so those actions throw NREs; the
    // throw unwinds straight past PopFsm() and STRANDS a live Fsm graph (its states/actions/variables) on
    // the static stack. Destroy(clone) frees the GameObjects but never that managed graph -> every pane
    // build (INV kick) and every frame a pane is shown (CHARM's enabled FSMs) grows the managed heap ->
    // GC pauses lengthen -> the progressive tab-switch stutter. Between top-level Update calls this stack
    // is ALWAYS empty except for those leaked entries (real FSMs push+pop within one synchronous call), so
    // draining it to zero here strands nothing real and restores a correct (empty) ExecutingFsm for events.
    static void DrainFsmStack()
    {
        try { int g = 0; while (HutongGames.PlayMaker.FsmExecutionStack.StackCount > 0 && g++ < 1024) HutongGames.PlayMaker.FsmExecutionStack.PopFsm(); }
        catch { }
    }

    void SetTmpFont(Transform t, float size)
    {
        if (t == null || size <= 0f) return;
        var comps = t.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i]; if (c == null || !c.GetType().Name.Contains("TextMeshPro")) continue;
            // HK's TMP labels ship with enableAutoSizing=true, which IGNORES fontSize and auto-fits to the rect —
            // so compCharmsNameFont/DescFont had no effect (apparent size was just the framing zoom). Disable it first.
            try { c.GetType().GetProperty("enableAutoSizing")?.SetValue(c, false, null); c.GetType().GetProperty("fontSize")?.SetValue(c, size, null); c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null); } catch { }
            break;
        }
    }

    void SetTmpLineSpacing(Transform t, float spacing)
    {
        if (t == null) return;
        var comps = t.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i]; if (c == null || !c.GetType().Name.Contains("TextMeshPro")) continue;
            try { var ty = c.GetType(); ty.GetProperty("lineSpacing")?.SetValue(c, spacing, null); ty.GetProperty("paragraphSpacing")?.SetValue(c, spacing, null); ty.GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null); } catch { }
            break;
        }
    }

    void WrapTmp(Transform t, float worldWidth)
    {
        if (t == null) return;
        var comps = t.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i]; if (c == null || !c.GetType().Name.Contains("TextMeshPro")) continue;
            try { c.GetType().GetProperty("enableWordWrapping")?.SetValue(c, true, null); } catch { }
            var rt = t.GetComponent<RectTransform>();
            if (rt != null) { float sx = Mathf.Abs(t.lossyScale.x) < 1e-4f ? 1f : Mathf.Abs(t.lossyScale.x); try { rt.sizeDelta = new Vector2(worldWidth / sx, Mathf.Max(rt.sizeDelta.y, 4f)); } catch { } }
            try { c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null); } catch { }
            break;
        }
    }

    // Localized UI label: first candidate key that resolves (in any sheet), else the English fallback. Lets the
    // context-box tab names show in the player's language instead of hardcoded English.
    string LocalizedLabel(string english, params string[] keys)
    {
        foreach (var k in keys) { var v = CharmString(k); if (!string.IsNullOrEmpty(v)) return v; }
        return english;
    }

    // Resolve a localization key across whichever sheet holds it (charm strings aren't in the default sheet).
    string CharmString(string key)
    {
        try
        {
            foreach (var sheet in TeamCherry.Localization.Language.GetSheets())
                if (TeamCherry.Localization.Language.Has(key, sheet))
                    return (TeamCherry.Localization.Language.Get(key, sheet) ?? "").Replace("<br>", "\n");   // HK uses <br> for newlines
        }
        catch { }
        return "";
    }

    // Find a TMP by GameObject name under root and set its text (+ ForceMeshUpdate) via the same reflection path
    // as the map/tab labels; enable its renderer only when non-empty (TMP disables its mesh while blank).
    void SetTmpTextByName(Transform root, string goName, string text)
    {
        var t = FindDeep(root, goName); if (t == null) return;
        foreach (var c in t.GetComponentsInChildren<Component>(true))
        {
            if (c == null || !c.GetType().Name.Contains("TextMeshPro")) continue;
            try { c.GetType().GetProperty("text")?.SetValue(c, text ?? "", null); } catch { }
            try { c.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes)?.Invoke(c, null); } catch { }
            try { var rr = c.GetComponent<Renderer>(); if (rr != null) rr.enabled = !string.IsNullOrEmpty(text); } catch { }
            break;
        }
    }
}
