#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DualSouls.Skins.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongSkinRuntime : IDisposable
    {
        sealed class HeroOwners
        {
            public HeroController Hero;
            public MeshRenderer Renderer;
            public tk2dSpriteAnimator Animator;
            public tk2dSprite Sprite;
            public HeroAnimationController Animation;
        }
        sealed class HudOwners
        {
            public GameCameras Cameras;
            public Camera Camera;
            public HUDCamera HudCamera;
            public GameObject GameplayChild;
            public PlayMakerFSM SlideOut;
            public SilkSpool Spool;
            public HudCanvas Canvas;
        }

        readonly UnityDecoder decoder = new UnityDecoder();
        readonly SkinRuntimeSession session;
        readonly List<string> omissions = new List<string>();
        HeroOwners heroOwners;
        HudOwners hudOwners;
        SkinPack requested;
        float nextRefresh;
        bool disposed;

        public static SilksongSkinRuntime Current { get; private set; }
        public SkinApplyResult LastResult { get; private set; } = new SkinApplyResult(SkinApplyStatus.Unchanged);
        public Func<bool> RefreshAllowed { get; set; }
        internal object HudOwnerIdentity => hudOwners;
        internal bool DeathTargetsAvailable(HeroController hero, GameManager manager) => !disposed &&
            hero != null && manager != null && heroOwners != null && hudOwners != null &&
            ReferenceEquals(heroOwners, CaptureHeroOwners(heroOwners)) && ReferenceEquals(heroOwners.Hero, hero) &&
            ReferenceEquals(hudOwners, CaptureHudOwners(hudOwners));

        public SilksongSkinRuntime()
        {
            if (Current != null) throw new InvalidOperationException("Only one Silksong skin runtime may own game visuals.");
            session = new SkinRuntimeSession(decoder, Discover, 512L * 1024 * 1024,
                SilksongSkinTargets.RuntimeRules);
            SceneManager.sceneLoaded += SceneChanged;
            SceneManager.sceneUnloaded += SceneUnloaded;
            Current = this;
        }

        public SkinApplyResult TryApply(SkinPack pack, CancellationToken cancellation = default)
        {
            requested = pack;
            return Publish(session.TryApply(pack, cancellation));
        }
        public SkinApplyResult TryRestore() => Publish(session.TryRestore());

        public void Tick()
        {
            if (disposed || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f;
            if (RefreshAllowed != null && !RefreshAllowed()) return;
            Publish(session.Refresh());
        }

        void SceneChanged(Scene scene, LoadSceneMode mode) { nextRefresh = 0f; }
        void SceneUnloaded(Scene scene) { nextRefresh = 0f; }

        SkinApplyResult Publish(SkinApplyResult result)
        {
            string omission = omissions.Count == 0 ? "" : " Omitted targets: " + string.Join("; ", omissions);
            LastResult = omission.Length == 0 ? result : new SkinApplyResult(result.Status,
                (result.Detail ?? "") + omission, result.UnsupportedTargets);
            if (LastResult.Status == SkinApplyStatus.Failed || LastResult.Status == SkinApplyStatus.RestoreFailed ||
                LastResult.Status == SkinApplyStatus.Blocked)
                Debug.LogWarning("[Silksong skins] " + LastResult.Status + ": " + LastResult.Detail);
            return LastResult;
        }

        IReadOnlyList<SkinSlot> Discover()
        {
            omissions.Clear();
            heroOwners = CaptureHeroOwners(heroOwners);
            hudOwners = CaptureHudOwners(hudOwners);
            var collections = Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>()
                .Where(x => x != null && !string.IsNullOrEmpty(x.spriteCollectionName))
                .Select(x => x.inst)
                .Where(x => x != null)
                .Distinct()
                .GroupBy(x => x.spriteCollectionName, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
            var slots = new List<SkinSlot>();
            IEnumerable<SilksongSkinTarget> wanted = requested == null
                ? SilksongSkinTargets.All
                : SilksongSkinTargets.All.Where(x => requested.Textures.ContainsKey(x.CanonicalPath));
            foreach (var target in wanted)
            {
                if (target.IsHud ? hudOwners == null : heroOwners == null)
                {
                    omissions.Add(target.CanonicalPath + " (persistent owner unavailable)");
                    continue;
                }
                if (!collections.TryGetValue(target.CollectionName, out var candidates) || candidates.Count == 0)
                {
                    omissions.Add(target.CanonicalPath + " (collection missing)");
                    continue;
                }
                var admitted = new List<List<SkinSlot>>();
                var failures = new List<string>();
                foreach (var collection in candidates)
                {
                    if (TryAdmit(target, collection, out var bindings, out var failure)) admitted.Add(bindings);
                    else failures.Add(failure);
                }
                if (admitted.Count != 1)
                {
                    omissions.Add(target.CanonicalPath + (admitted.Count > 1
                        ? " (ambiguous admitted collections)"
                        : " (" + string.Join(", ", failures.Distinct()) + ")"));
                    continue;
                }
                slots.AddRange(admitted[0]);
            }
            return slots;
        }

        bool TryAdmit(SilksongSkinTarget target, tk2dSpriteCollectionData collection,
            out List<SkinSlot> bindings, out string failure)
        {
            bindings = new List<SkinSlot>();
            var materials = collection.materials;
            var pages = materials == null ? null : materials.Select(material => material == null || material.mainTexture == null
                ? new SilksongMaterialPage(null, 0, 0)
                : new SilksongMaterialPage(material.mainTexture, material.mainTexture.width, material.mainTexture.height)).ToList();
            failure = SilksongCollectionAdmission.Validate(target, collection.spriteCollectionName, pages);
            if (failure != null) return false;
            if (collection.material == null || !ReferenceEquals(collection.material, materials[0]))
            {
                failure = "primary material does not match material page zero";
                return false;
            }
            if (collection.materialInsts == null || collection.materialInsts.Length != target.MaterialCount)
            {
                failure = "material instance count does not match material/page count";
                return false;
            }
            var all = materials.Concat(collection.materialInsts).Concat(new[] { collection.material }).Distinct().ToList();
            var original = pages[0].OriginalTexture;
            if (all.Any(material => material == null || !ReferenceEquals(material.mainTexture, original)))
            {
                failure = "material and materialInsts do not share the admitted original texture";
                return false;
            }
            foreach (var material in all)
            {
                var captured = material;
                bindings.Add(new SkinSlot(captured, "mainTexture", target.CanonicalPath,
                    () => captured != null && collection != null && OwnersStillMatch(target),
                    () => captured.mainTexture,
                    value => captured.mainTexture = RequireLive<Texture>(value),
                    (skin, _) => {
                        if (skin.Width != target.Width || skin.Height != target.Height)
                            throw new InvalidOperationException("Skin atlas dimensions do not match " + target.CanonicalPath);
                        return RequireLive<Texture2D>(skin.Value);
                    }));
            }
            return true;
        }

        bool OwnersStillMatch(SilksongSkinTarget target) => target.IsHud
            ? ReferenceEquals(hudOwners, CaptureHudOwners(hudOwners))
            : ReferenceEquals(heroOwners, CaptureHeroOwners(heroOwners));

        static HeroOwners CaptureHeroOwners(HeroOwners existing)
        {
            var hero = HeroController.SilentInstance;
            if (hero == null) return null;
            var renderer = hero.GetComponent<MeshRenderer>();
            var animator = hero.GetComponent<tk2dSpriteAnimator>();
            var sprite = hero.GetComponent<tk2dSprite>();
            var animation = hero.GetComponent<HeroAnimationController>();
            if (renderer == null || animator == null || sprite == null || animation == null) return null;
            if (existing != null && ReferenceEquals(existing.Hero, hero) && ReferenceEquals(existing.Renderer, renderer) &&
                ReferenceEquals(existing.Animator, animator) && ReferenceEquals(existing.Sprite, sprite) &&
                ReferenceEquals(existing.Animation, animation)) return existing;
            return new HeroOwners { Hero = hero, Renderer = renderer, Animator = animator, Sprite = sprite, Animation = animation };
        }

        static HudOwners CaptureHudOwners(HudOwners existing)
        {
            var cameras = GameCameras.SilentInstance;
            var camera = cameras != null ? cameras.hudCamera : null;
            var hudCamera = camera != null ? camera.GetComponent<HUDCamera>() : null;
            var gameplay = hudCamera != null ? hudCamera.GameplayChild : null;
            var slide = cameras != null ? cameras.hudCanvasSlideOut : null;
            var spool = cameras != null ? cameras.silkSpool : null;
            var canvases = gameplay != null ? gameplay.GetComponentsInChildren<HudCanvas>(true) : Array.Empty<HudCanvas>();
            if (cameras == null || camera == null || hudCamera == null || gameplay == null || slide == null || spool == null || canvases.Length != 1)
                return null;
            var canvas = canvases[0];
            if (existing != null && ReferenceEquals(existing.Cameras, cameras) && ReferenceEquals(existing.Camera, camera) &&
                ReferenceEquals(existing.HudCamera, hudCamera) && ReferenceEquals(existing.GameplayChild, gameplay) &&
                ReferenceEquals(existing.SlideOut, slide) && ReferenceEquals(existing.Spool, spool) &&
                ReferenceEquals(existing.Canvas, canvas)) return existing;
            return new HudOwners { Cameras = cameras, Camera = camera, HudCamera = hudCamera, GameplayChild = gameplay,
                SlideOut = slide, Spool = spool, Canvas = canvas };
        }

        static T RequireLive<T>(object value) where T : UObject
        {
            var result = value as T;
            if (result == null) throw new InvalidOperationException("Owned Unity skin resource retired before commit.");
            return result;
        }

        public void Dispose()
        {
            if (disposed) return;
            session.Dispose();
            SceneManager.sceneLoaded -= SceneChanged;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            disposed = true;
            if (ReferenceEquals(Current, this)) Current = null;
        }

        sealed class UnityDecoder : ISkinTextureDecoder
        {
            readonly Dictionary<Texture2D, bool> released = new Dictionary<Texture2D, bool>();
            public SkinTexture Decode(byte[] bytes, int width, int height)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                    { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SSSKIN_atlas" };
                bool decoded;
                try { decoded = ImageConversion.LoadImage(texture, bytes, false); }
                catch (Exception) { decoded = false; }
                released.Add(texture, false);
                return new SkinTexture(texture, texture.width, texture.height, () => {
                    if (texture != null) UObject.Destroy(texture);
                    released[texture] = true;
                }, () => texture == null || released[texture], decoded);
            }
        }
    }
}
#endif
