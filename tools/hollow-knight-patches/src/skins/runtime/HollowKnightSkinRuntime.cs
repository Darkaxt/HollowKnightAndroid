using System;
using DualSouls.Skins.Runtime;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace DualSouls.Skins.HollowKnight.Runtime
{
    // Rendering traversal/mappings adapted from igawa6/dualsouls Assets/HKMods.cs,
    // 5c22451435b772acde0c7e6456f9019bc1baef73, source-only MIT label; see NOTICE.md.
    // No folder picker, tweaks, registry, self-test activation, or automatic pack selection.
    public sealed class HollowKnightSkinRuntime : IDisposable
    {
        readonly UnityDecoder decoder = new UnityDecoder();
        readonly SkinRuntimeSession session;
        readonly Dictionary<Texture2D, SkinAtlasSlot> atlases = new Dictionary<Texture2D, SkinAtlasSlot>();
        readonly Dictionary<(SkinTexture Skin, tk2dSpriteDefinition[] Definitions, Texture2D Vanilla), SkinTexture> hudCopies =
            new Dictionary<(SkinTexture, tk2dSpriteDefinition[], Texture2D), SkinTexture>();
        readonly SkinRuntimeRefreshSchedule refreshSchedule;
        HeroController hero;
        GameObject hud;
        Renderer deathHeroRenderer;
        SpriteRenderer deathHudRenderer;
        internal Func<bool> RefreshAllowed;
        int reportedStamp;
        bool disposed;
        public static HollowKnightSkinRuntime Current { get; private set; }
        public static int SkinStamp { get; private set; }
        public SkinApplyResult LastResult => refreshSchedule.LastResult;

        public HollowKnightSkinRuntime()
        {
            if (Current != null) throw new InvalidOperationException("Only one skin runtime may own game visuals.");
            session = new SkinRuntimeSession(decoder, Discover, 512L * 1024 * 1024,
                HollowKnightSkinPolicy.RuntimeRules);
            refreshSchedule = new SkinRuntimeRefreshSchedule(CacheDeathTargets,
                () => RefreshAllowed == null || RefreshAllowed(), () => Publish(session.Refresh()));
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += SceneUnloaded;
            Current = this;
        }
        public SkinApplyResult TryApply(SkinPack pack, CancellationToken cancellation = default) => Publish(session.TryApply(pack, cancellation));
        public SkinApplyResult TryRestore() => Publish(session.TryRestore());
        SkinApplyResult Publish(SkinApplyResult result)
        {
            if (reportedStamp != session.SkinStamp)
            {
                unchecked { SkinStamp += session.SkinStamp - reportedStamp; }
                reportedStamp = session.SkinStamp;
            }
            return refreshSchedule.Publish(result);
        }
        public void Tick()
        {
            if (disposed) return;
            var nextHero = HeroController.instance;
            var cameras = GameCameras.instance;
            var nextHud = cameras != null ? cameras.hudCanvas : null;
            hero = nextHero; hud = nextHud;
            var before = LastResult;
            refreshSchedule.Tick(Time.unscaledTime, nextHero, nextHud);
            var result = LastResult;
            if (result != null && (result.Status == SkinApplyStatus.Failed || result.Status == SkinApplyStatus.RestoreFailed ||
                 result.Detail.Contains("Resource retirement pending")) &&
                (before == null || before.Status != result.Status || before.Detail != result.Detail))
                Debug.LogWarning("[HK skins] " + result.Status + ": " + result.Detail);
        }
        void CacheDeathTargets()
        {
            deathHeroRenderer = hero != null ? hero.GetComponent<Renderer>() : null;
            var orb = hud != null ? FindChildDeep(hud.transform, "Orb Full") : null;
            deathHudRenderer = orb != null ? orb.GetComponent<SpriteRenderer>() : null;
        }
        internal bool DeathTargetsAvailable(HeroController currentHero, GameObject currentHud)
        {
            // Cached by normal scan/replacement work, never Resources traversal per death frame.
            // Availability is not visibility: the soul mask can legitimately hide Orb Full.
            return !disposed && currentHero != null && currentHud != null && ReferenceEquals(hero, currentHero) &&
                ReferenceEquals(hud, currentHud) && deathHeroRenderer != null && deathHeroRenderer.sharedMaterial != null &&
                deathHeroRenderer.sharedMaterial.mainTexture != null && deathHudRenderer != null &&
                deathHudRenderer.sprite != null && deathHudRenderer.sprite.texture != null;
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode) { refreshSchedule.Invalidate(); }
        void SceneUnloaded(Scene scene) { refreshSchedule.Invalidate(); }
        public void Dispose()
        {
            if (disposed) return;
            try { session.Dispose(); }
            catch (Exception error)
            {
                Publish(new SkinApplyResult(SkinApplyStatus.RestoreFailed, error.Message));
                throw; // failed/pending teardown leaves ownership and retry reachable
            }
            Publish(new SkinApplyResult(SkinApplyStatus.Unchanged));
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= SceneUnloaded;
            disposed = true;
            if (ReferenceEquals(Current, this)) Current = null;
        }

        IReadOnlyList<SkinSlot> Discover()
        {
            var slots = new List<SkinSlot>();
            foreach (var key in hudCopies.Keys.Where(x => x.Skin.ReleaseRequested || x.Vanilla == null || hudCopies[x].ReleaseRequested).ToArray()) hudCopies.Remove(key);
            // Same collection discovery and material pages as the reference. Never edit UVs/definitions.
            foreach (var collection in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
            {
                if (collection == null || string.IsNullOrEmpty(collection.spriteCollectionName) ||
                    !HollowKnightSkinTargets.IsAuthoritativeHierarchy(HierarchyNames(collection.transform))) continue;
                AddPages(slots, collection, collection.materials);
                AddPages(slots, collection, collection.materialInsts);
                var first = HollowKnightSkinTargets.CollectionTarget(collection.spriteCollectionName, 0);
                if (collection.material != null && first != null) AddMaterial(slots, collection.material, first, collection);
            }
            var cameras = GameCameras.instance;
            var canvas = cameras != null ? cameras.hudCanvas : null;
            if (canvas != null)
            {
                var orb = FindChildDeep(canvas.transform, "Orb Full");
                var renderer = orb != null ? orb.GetComponent<SpriteRenderer>() : null;
                if (renderer != null && renderer.sprite != null)
                {
                    slots.Add(new SkinSlot(renderer, "sprite", "OrbFull.png", () => renderer != null,
                        () => renderer.sprite, value => renderer.sprite = Live<Sprite>(value),
                        (texture, original) => decoder.SpriteFor(texture, original as Sprite)));
                    var pulse = FindChildDeep(canvas.transform, "Pulse Sprite");
                    if (pulse != null)
                    {
                        var go = pulse.gameObject;
                        slots.Add(new SkinSlot(go, "activeSelf", "OrbFull.png", () => go != null,
                            () => go.activeSelf, value => go.SetActive((bool)value), (texture, original) => false));
                    }
                }
                var liquid = FindChildDeep(canvas.transform, "Liquid");
                if (liquid != null) AddRenderer(slots, liquid.GetComponent<Renderer>(), "Liquid.png");
            }
            var hc = HeroController.instance;
            if (hc != null)
                foreach (var mapping in HollowKnightSkinTargets.HeroObjects)
                    foreach (var name in mapping.Value)
                    {
                        var target = FindChildDeep(hc.transform, name);
                        if (target != null) AddRenderer(slots, target.GetComponent<Renderer>(), mapping.Key);
                    }
            AddCharmAndInventorySlots(slots, cameras);
            foreach (var dead in atlases.Keys.Where(x => x == null).ToArray()) atlases.Remove(dead);
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (texture == null || texture.width < 64 || texture.width > 4096 || texture.height > 4096) continue;
                var target = HollowKnightSkinTargets.AtlasTarget(texture.name);
                if (target == null) continue;
                if (!atlases.TryGetValue(texture, out var atlas))
                {
                    atlas = new SkinAtlasSlot(new UnityAtlas(texture, decoder), target, session.AllocateAuxiliary);
                    atlases.Add(texture, atlas);
                }
                slots.Add(atlas.Binding());
            }
            return slots;
        }
        void AddCharmAndInventorySlots(List<SkinSlot> slots, GameCameras cameras)
        {
            var charms = CharmIconList.Instance;
            if (charms != null)
            {
                var array = charms.spriteList;
                if (array != null)
                    for (int i = 0; i < array.Length; i++)
                    {
                        int index = i; var target = HollowKnightSkinTargets.CharmListTarget(i);
                        if (target == null) continue;
                        slots.Add(new SkinSlot(array, "sprite[" + i + "]", target,
                            () => charms != null && ReferenceEquals(charms.spriteList, array),
                            () => array[index], value => array[index] = Live<Sprite>(value),
                            (texture, original) => decoder.SpriteFor(texture, original as Sprite)));
                    }
                foreach (var mapping in HollowKnightSkinTargets.CharmFields)
                    AddSpriteField(slots, charms, mapping.Value, mapping.Key, () => charms != null);
            }
            if (cameras == null) return;
            var inv = FindChildDeep(cameras.transform.root, "Inv");
            if (inv != null)
            {
                foreach (var mapping in HollowKnightSkinTargets.InventoryObjects)
                {
                    var parts = mapping.Value.Split('|'); var target = FindChildDeep(inv, parts[0]);
                    if (target == null) continue;
                    if (parts.Length == 1)
                    {
                        var renderer = target.GetComponent<SpriteRenderer>();
                        if (renderer != null) slots.Add(new SkinSlot(renderer, "sprite", mapping.Key, () => renderer != null,
                            () => renderer.sprite, value => renderer.sprite = Live<Sprite>(value),
                            (texture, original) => decoder.SpriteFor(texture, original as Sprite)));
                    }
                    else foreach (var component in target.GetComponents<Component>())
                    {
                        if (component != null && AddSpriteField(slots, component, parts[1], mapping.Key, () => component != null)) break;
                    }
                }
                foreach (var mapping in HollowKnightSkinTargets.InventoryFsms)
                {
                    var p = mapping.Value.Split('>');
                    AddFsmSprite(slots, FindFsm(FindChildDeep(inv, p[0]), p[1]), p[2], int.Parse(p[3]), mapping.Key);
                }
            }
            if (cameras.hudCamera == null) return;
            var root = cameras.hudCamera.transform;
            var detail = FindFsm(FindChildDeep(root, "Detail Sprite"), "Update Sprite");
            var displays = cameras.transform.root.GetComponentsInChildren<CharmDisplay>(true)
                .Where(x => x != null && HollowKnightSkinTargets.IsAuthoritativeHierarchy(HierarchyNames(x.transform))).ToArray();
            foreach (var mapping in HollowKnightSkinTargets.CharmFsms)
            {
                var p = mapping.Value.Split('>'); int index = int.Parse(p[2]);
                AddFsmSprite(slots, FindFsm(FindChildDeep(root, p[0]), "charm_show_if_collected"), p[1], index, mapping.Key);
                AddFsmSprite(slots, detail, p[1], index, mapping.Key);
                if (p[3].Length > 0)
                    foreach (var display in displays)
                        if (display != null) AddSpriteField(slots, display, p[3], mapping.Key, () => display != null);
            }
        }
        bool AddSpriteField(List<SkinSlot> slots, object owner, string member, string target, Func<bool> alive)
        {
            var field = owner.GetType().GetField(member);
            if (field == null || field.FieldType != typeof(Sprite)) return false;
            slots.Add(new SkinSlot(owner, member, target, alive, () => field.GetValue(owner),
                value => field.SetValue(owner, Live<Sprite>(value)),
                (texture, original) => decoder.SpriteFor(texture, original as Sprite), InventoryConsumer(owner, member, target)));
            return true;
        }
        static SkinSlot InventoryConsumer(object owner, string member, string target)
        {
            // Actual managed Awake/OnEnable: these components cache GetComponent<SpriteRenderer>()
            // and copy the selected source field into sprite. Do not invoke them or inspect PlayerData.
            Type type = owner is InvNailSprite && (member == "level1" || member == "level2" || member == "level3" || member == "level4" || member == "level5")
                ? typeof(InvNailSprite) : owner is InvItemDisplay && (member == "activeSprite" || member == "inactiveSprite") ? typeof(InvItemDisplay) : null;
            if (type == null) return null;
            var component = (Component)owner;
            if (!HollowKnightSkinTargets.IsAuthoritativeHierarchy(HierarchyNames(component.transform))) return null;
            var cached = type.GetField("spriteRenderer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var renderer = cached?.GetValue(owner) as SpriteRenderer;
            if (renderer == null) renderer = component.GetComponent<SpriteRenderer>(); // before Awake
            if (renderer == null) return null;
            return new SkinSlot(renderer, "sprite", target, () => renderer != null,
                () => renderer.sprite, value => renderer.sprite = Live<Sprite>(value),
                (texture, original) => throw new InvalidOperationException("Inventory consumer must use its source field's vanilla identity."));
        }
        static PlayMakerFSM FindFsm(Transform target, string name) => target == null ? null :
            target.GetComponents<PlayMakerFSM>().FirstOrDefault(x => x != null && x.FsmName == name);
        void AddFsmSprite(List<SkinSlot> slots, PlayMakerFSM fsm, string stateName, int index, string target)
        {
            var owner = fsm != null ? fsm.Fsm : null;
            var states = owner != null ? SkinRuntimeFsmReadiness.ReadInitialized(owner.Initialized, () => owner.States) : null;
            if (states == null) return;
            var state = states.FirstOrDefault(x => x != null && x.Name == stateName);
            if (state == null) return;
            var actions = SkinRuntimeFsmReadiness.ReadActions(owner.Initialized, state.ActionsLoaded, () => state.Actions);
            if (actions == null || index < 0 || index >= actions.Length || actions[index] == null) return;
            var action = actions[index];
            Func<bool> alive = () =>
            {
                if (fsm == null || !ReferenceEquals(fsm.Fsm, owner)) return false;
                var currentStates = SkinRuntimeFsmReadiness.ReadInitialized(owner.Initialized, () => owner.States);
                if (currentStates == null || !currentStates.Contains(state)) return false;
                var current = SkinRuntimeFsmReadiness.ReadActions(owner.Initialized, state.ActionsLoaded, () => state.Actions);
                return current != null && index < current.Length && ReferenceEquals(current[index], action);
            };
            foreach (var field in action.GetType().GetFields())
            {
                if (field.FieldType == typeof(Sprite))
                { AddSpriteField(slots, action, field.Name, target, alive); return; }
                if (!typeof(HutongGames.PlayMaker.FsmObject).IsAssignableFrom(field.FieldType)) continue;
                var wrapper = field.GetValue(action) as HutongGames.PlayMaker.FsmObject;
                if (wrapper == null || !(wrapper.Value is Sprite)) continue; // null object slots have no sprite type authority
                slots.Add(new SkinSlot(wrapper, "Value", target,
                    () => alive() && ReferenceEquals(field.GetValue(action), wrapper),
                    () => wrapper.Value, value => wrapper.Value = Live<Sprite>(value),
                    (texture, original) => decoder.SpriteFor(texture, original as Sprite)));
                return;
            }
            slots.Add(new SkinSlot(action, "unsupported-sprite-field", "unsupported:" + target + "/" + stateName + "/" + index,
                alive, () => null, value => { }, (texture, original) => null));
        }
        void AddPages(List<SkinSlot> slots, tk2dSpriteCollectionData owner, Material[] materials)
        {
            string collection = owner.spriteCollectionName;
            if (materials == null) return;
            for (int page = 0; page < materials.Length; page++)
            {
                if (materials[page] == null) continue;
                var target = HollowKnightSkinTargets.CollectionTarget(collection, page);
                if (target == null && page > 1 && string.Equals(collection, "Knight", StringComparison.OrdinalIgnoreCase))
                    target = "unsupported:Knight/material-page-" + page;
                if (target != null) AddMaterial(slots, materials[page], target, owner);
            }
        }
        void AddMaterial(List<SkinSlot> slots, Material material, string target, tk2dSpriteCollectionData collection)
        {
            slots.Add(new SkinSlot(material, "mainTexture", target, () => material != null,
                () => material.mainTexture, value => material.mainTexture = Live<Texture>(value),
                (texture, original) => string.Equals(target, "Hud.png", StringComparison.OrdinalIgnoreCase)
                    ? PrepareHud(texture, original as Texture2D, collection.spriteDefinitions) : texture.Value));
        }
        object PrepareHud(SkinTexture skin, Texture2D vanilla, tk2dSpriteDefinition[] definitions)
        {
            if (vanilla == null || definitions == null || definitions.Length == 0) return skin.Value;
            var key = (skin, definitions, vanilla);
            if (hudCopies.TryGetValue(key, out var existing) && !existing.ReleaseRequested) return existing.Value;
            SkinTexture repaired = null;
            session.WithScratch(HollowKnightHudRepair.ScratchBytes, () =>
            {
                if (definitions.Length > 4096 || hudCopies.Count >= 256) throw new System.IO.InvalidDataException("HUD definition/copy count exceeds bound.");
                var baseline = atlases.TryGetValue(vanilla, out var atlas) ? atlas.OriginalPixels : null;
                if (baseline == null || baseline.ReleaseRequested)
                    baseline = session.AllocateAuxiliary(checked((long)vanilla.width * vanilla.height * 12),
                        () => decoder.ReadableCopy(vanilla, vanilla.width, vanilla.height));
                if (!baseline.DecodeSucceeded) throw new System.IO.IOException("HUD vanilla readback failed.");
                repaired = session.AllocateAuxiliary(checked((long)skin.Width * skin.Height * 12),
                    () => decoder.ReadableCopy(Live<Texture2D>(skin.Value), skin.Width, skin.Height));
                if (!repaired.DecodeSucceeded) throw new System.IO.IOException("HUD candidate readback failed.");
                var rects = new List<SkinHudRect>();
                foreach (var definition in definitions)
                {
                    if (definition == null || string.IsNullOrEmpty(definition.name) || definition.uvs == null || definition.uvs.Length < 3) continue;
                    float u0 = 1, v0 = 1, u1 = 0, v1 = 0;
                    foreach (var uv in definition.uvs) { u0 = Math.Min(u0, uv.x); v0 = Math.Min(v0, uv.y); u1 = Math.Max(u1, uv.x); v1 = Math.Max(v1, uv.y); }
                    rects.Add(new SkinHudRect(definition.name, u0, v0, u1, v1));
                }
                HollowKnightHudRepair.Repair(new UnityPixels(Live<Texture2D>(skin.Value)), new UnityPixels(Live<Texture2D>(baseline.Value)),
                    new UnityPixels(Live<Texture2D>(repaired.Value)), rects);
                Live<Texture2D>(repaired.Value).Apply(false);
            });
            hudCopies[key] = repaired;
            return repaired.Value;
        }
        void AddRenderer(List<SkinSlot> slots, Renderer renderer, string target)
        {
            if (renderer == null || renderer.sharedMaterial == null) return;
            // Unlike Renderer.material, discovery must not allocate an unowned material instance.
            // Prepare a tracked clone before commit, and restore the exact original sharedMaterial.
            slots.Add(new SkinSlot(renderer, "sharedMaterial", target, () => renderer != null,
                () => renderer.sharedMaterial, value => renderer.sharedMaterial = Live<Material>(value),
                (texture, original) => decoder.MaterialFor(texture, (Material)original)));
        }
        static IEnumerable<string> HierarchyNames(Transform target)
        {
            for (var ancestor = target; ancestor != null; ancestor = ancestor.parent) yield return ancestor.name;
        }
        static T Live<T>(object value) where T : UObject
        {
            var result = (T)value;
            if (!ReferenceEquals(result, null) && result == null)
                throw new InvalidOperationException("Cannot apply/restore a destroyed Unity resource.");
            return result;
        }
        static Transform FindChildDeep(Transform root, string name, int depth = 0)
        {
            if (root == null || depth > 64) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = FindChildDeep(root.GetChild(i), name, depth + 1);
                if (child != null) return child;
            }
            return null;
        }

        sealed class UnityPixels : ISkinHudPixels
        {
            readonly Texture2D texture;
            public UnityPixels(Texture2D texture) { this.texture = texture; }
            public int Width => texture.width;
            public int Height => texture.height;
            static SkinHudPixel Convert(Color p) => new SkinHudPixel(p.r, p.g, p.b, p.a);
            public SkinHudPixel Sample(float u, float v) => Convert(texture.GetPixelBilinear(u, v));
            public SkinHudPixel Pixel(int x, int y) => Convert(texture.GetPixel(x, y));
            public void WriteRow(int x, int y, SkinHudPixel[] pixels, int count)
            {
                for (int i = 0; i < count; i++) { var p = pixels[i]; texture.SetPixel(x + i, y, new Color(p.R, p.G, p.B, p.A)); }
            }
        }

        sealed class UnityAtlas : ISkinAtlasSurface
        {
            readonly Texture2D destination;
            readonly UnityDecoder decoder;
            public UnityAtlas(Texture2D destination, UnityDecoder decoder) { this.destination = destination; this.decoder = decoder; }
            public object Identity => destination;
            public bool IsAlive => destination != null;
            public int Width => destination.width;
            public int Height => destination.height;
            public SkinTexture Capture() => decoder.ReadableCopy(destination, Width, Height);
            public SkinTexture Fit(SkinTexture source) => decoder.ReadableCopy(Live<Texture2D>(source.Value), Width, Height);
            public bool CopyFrom(SkinTexture source) => destination != null &&
                Graphics.ConvertTexture(Live<Texture2D>(source.Value), destination);
        }

        sealed class UnityDecoder : ISkinTextureDecoder
        {
            sealed class Owned
            {
                public readonly List<UObject> Objects = new List<UObject>();
                public readonly Dictionary<float, Sprite> Sprites = new Dictionary<float, Sprite>();
                public readonly Dictionary<Material, Material> Materials = new Dictionary<Material, Material>();
                public void AdmitObject()
                {
                    if (Objects.Count >= 128) throw new InvalidOperationException("Generated sprite/material count bound exceeded.");
                }
            }
            readonly Dictionary<Texture2D, Owned> owned = new Dictionary<Texture2D, Owned>();
            public SkinTexture ReadableCopy(Texture source, int width, int height)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                    { name = "HKSKIN_gpu_copy", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                bool copied = false;
                var previous = RenderTexture.active;
                RenderTexture temporary = null;
                try
                {
                    try
                    {
                        temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                        Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(false);
                        copied = true;
                    }
                    finally
                    {
                        try { RenderTexture.active = previous; }
                        finally { if (temporary != null) RenderTexture.ReleaseTemporary(temporary); }
                    }
                }
                catch (Exception) { copied = false; } // allocated output is returned as failed, never orphaned
                return Track(texture, copied);
            }
            public SkinTexture Decode(byte[] bytes, int width, int height)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HKSKIN_sheet" };
                bool decoded;
                try { decoded = ImageConversion.LoadImage(texture, bytes, false); }
                catch (Exception) { decoded = false; } // result still carries the allocated texture for bounded retirement
                return Track(texture, decoded);
            }
            SkinTexture Track(Texture2D texture, bool succeeded)
            {
                var resources = new Owned(); resources.Objects.Add(texture); owned.Add(texture, resources);
                return new SkinTexture(texture, texture.width, texture.height, () =>
                {
                    for (int i = resources.Objects.Count - 1; i >= 0; i--)
                        if (resources.Objects[i] != null) UObject.Destroy(resources.Objects[i]);
                    owned.Remove(texture);
                }, () => resources.Objects.All(x => x == null), succeeded);
            }
            public Sprite SpriteFor(SkinTexture skin, Sprite original)
            {
                var texture = (Texture2D)skin.Value; var resources = owned[texture];
                float ppu = original != null ? original.pixelsPerUnit : 100f;
                if (resources.Sprites.TryGetValue(ppu, out var existing)) return existing;
                resources.AdmitObject();
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
                resources.Objects.Add(sprite); skin.Own(sprite); resources.Sprites.Add(ppu, sprite); return sprite;
            }
            public Material MaterialFor(SkinTexture skin, Material original)
            {
                var texture = (Texture2D)skin.Value; var resources = owned[texture];
                if (resources.Materials.TryGetValue(original, out var existing)) return existing;
                resources.AdmitObject();
                var material = new Material(original) { mainTexture = texture, name = "HKSKIN_material" };
                resources.Objects.Add(material); skin.Own(material); resources.Materials.Add(original, material); return material;
            }
        }
    }
}
#endif

namespace DualSouls.Skins.HollowKnight.Runtime
{
    internal static class SkinRuntimeFsmReadiness
    {
        public static T ReadInitialized<T>(bool fsmInitialized, Func<T> read) where T : class =>
            fsmInitialized ? read() : null;
        public static T ReadActions<T>(bool fsmInitialized, bool actionsLoaded, Func<T> read) where T : class =>
            fsmInitialized && actionsLoaded ? read() : null;
    }

    // Local production scan cadence and observation cache; graphics/component boundaries stay in the runtime.
    internal sealed class SkinRuntimeRefreshSchedule
    {
        readonly Action cacheTargets, refresh;
        readonly Func<bool> canRefresh;
        object hero, hud;
        float nextScan;
        bool settled;
        public SkinApplyResult LastResult { get; private set; }
        public SkinRuntimeRefreshSchedule(Action cacheTargets, Func<bool> canRefresh, Action refresh)
        { this.cacheTargets = cacheTargets; this.canRefresh = canRefresh; this.refresh = refresh; }
        public SkinApplyResult Publish(SkinApplyResult result)
        {
            settled = false;
            return LastResult = result;
        }
        public void Invalidate()
        {
            nextScan = 0f;
            settled = false;
            // Success describes the old targets, not replacements. Keep current failures visible.
            if (LastResult == null || LastResult.Status == SkinApplyStatus.Applied ||
                LastResult.Status == SkinApplyStatus.Unchanged || LastResult.Status == SkinApplyStatus.Restored)
                LastResult = new SkinApplyResult(SkinApplyStatus.AwaitingTargets, "Scene targets changed; current visual refresh pending.");
        }
        public void Tick(float now, object nextHero, object nextHud)
        {
            bool replaced = !ReferenceEquals(hero, nextHero) || !ReferenceEquals(hud, nextHud);
            if (replaced) Invalidate(); // before the gate/throttle can retain a stale successful observation
            hero = nextHero; hud = nextHud;
            if (!replaced && settled) return;
            if (!replaced && now < nextScan) return;
            nextScan = now + 2f;
            cacheTargets();
            if (!canRefresh()) return;
            refresh();
            settled = LastResult != null && !LastResult.Detail.Contains("Resource retirement pending") &&
                (LastResult.Status == SkinApplyStatus.Applied || LastResult.Status == SkinApplyStatus.Unchanged ||
                 LastResult.Status == SkinApplyStatus.Restored);
        }
    }
}
