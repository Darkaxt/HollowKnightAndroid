#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DualSouls.Skins.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongSkinRuntime : IDisposable, ISkinTeardownSession
    {
        sealed class HeroOwners
        {
            public HeroController Hero;
            public MeshRenderer Renderer;
            public tk2dSpriteAnimator Animator;
            public tk2dSprite Sprite;
            public HeroAnimationController Animation;
            public tk2dSpriteAnimation DefaultLibrary;
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

        sealed class OwnedCollection
        {
            public object Owner;
            public tk2dSpriteCollectionData Collection;
        }

        static readonly FieldInfo WindyLibraryField = typeof(HeroAnimationController).GetField("windyAnimLib",
            BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo ConfigField = typeof(HeroAnimationController).GetField("config",
            BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo CrestLibraryField = typeof(HeroControllerConfig).GetField("heroAnimOverrideLib",
            BindingFlags.Instance | BindingFlags.NonPublic);

        readonly UnityDecoder decoder = new UnityDecoder();
        readonly SkinRuntimeSession session;
        readonly List<string> omissions = new List<string>();
        HeroOwners heroOwners;
        HudOwners hudOwners;
        SilksongCollectionOwnerStamp collectionOwnerStamp;
        SkinPack requested;
        float nextRefresh;
        bool disposed;

        public static SilksongSkinRuntime Current { get; private set; }
        public SkinApplyResult LastResult { get; private set; } = new SkinApplyResult(SkinApplyStatus.Unchanged);
        public bool TeardownComplete => disposed;
        public string LastError { get; private set; } = "";
        public Func<bool> RefreshAllowed { get; set; }
        internal object HudOwnerIdentity => hudOwners;
        internal bool DeathTargetsAvailable(HeroController hero, GameManager manager) => !disposed &&
            hero != null && manager != null && heroOwners != null && hudOwners != null &&
            ReferenceEquals(heroOwners, CaptureHeroOwners(heroOwners)) && ReferenceEquals(heroOwners.Hero, hero) &&
            ReferenceEquals(hudOwners, CaptureHudOwners(hudOwners)) && collectionOwnerStamp != null &&
            collectionOwnerStamp.Equals(CaptureOwnerStamp());

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
            var characterCollections = CaptureCharacterCollections();
            var hudCollections = CaptureHudCollections();
            var allCollections = characterCollections.Concat(hudCollections).ToList();
            collectionOwnerStamp = OwnerStamp(allCollections);
            var slots = new List<SkinSlot>();
            var requestedTargets = requested == null ? Array.Empty<string>() : requested.Textures.Keys;

            foreach (var target in SilksongSkinTargets.All)
            {
                if (target.IsHud ? hudOwners == null : heroOwners == null)
                {
                    omissions.Add(target.CanonicalPath + " (persistent owner unavailable)");
                    continue;
                }
                var owned = (target.IsHud ? hudCollections : characterCollections)
                    .Where(x => x.Collection != null && string.Equals(x.Collection.spriteCollectionName,
                        target.CollectionName, StringComparison.Ordinal)).ToList();
                var instances = new List<SilksongCollectionInstance>();
                var slotByCollection = new Dictionary<object, List<SkinSlot>>(ReferenceComparer.Instance);
                foreach (var item in owned)
                {
                    if (!slotByCollection.TryGetValue(item.Collection, out var bindings))
                    {
                        TryAdmit(target, item.Collection, out bindings, out var failure);
                        slotByCollection[item.Collection] = bindings;
                        instances.Add(new SilksongCollectionInstance(target.CollectionName,
                            item.Owner, item.Collection, failure));
                    }
                    else
                    {
                        instances.Add(new SilksongCollectionInstance(target.CollectionName,
                            item.Owner, item.Collection, instances.First(x =>
                                ReferenceEquals(x.CollectionIdentity, item.Collection)).Failure));
                    }
                }
                var plan = SilksongCollectionPlan.Build(new[] { target }, requestedTargets, instances);
                omissions.AddRange(plan.Omissions);
                if (!plan.Bindings.TryGetValue(target.CanonicalPath, out var admitted)) continue;
                foreach (var instance in admitted)
                    slots.AddRange(slotByCollection[instance.CollectionIdentity]);
            }
            return slots;
        }

        List<OwnedCollection> CaptureCharacterCollections()
        {
            var result = new List<OwnedCollection>();
            if (heroOwners == null) return result;
            AddLibrary(result, heroOwners.DefaultLibrary);
            AddLibrary(result, heroOwners.Animator.Library);
            AddLibrary(result, WindyLibraryField?.GetValue(heroOwners.Animation) as tk2dSpriteAnimation);
            var config = ConfigField?.GetValue(heroOwners.Animation) as HeroControllerConfig;
            AddLibrary(result, config == null ? null : CrestLibraryField?.GetValue(config) as tk2dSpriteAnimation);
            AddCollection(result, heroOwners.Sprite, heroOwners.Sprite.Collection);
            return result;
        }

        List<OwnedCollection> CaptureHudCollections()
        {
            var result = new List<OwnedCollection>();
            if (hudOwners == null) return result;
            foreach (var root in new[] { hudOwners.GameplayChild, hudOwners.Spool.gameObject })
            {
                foreach (var animator in root.GetComponentsInChildren<tk2dSpriteAnimator>(true))
                    if (animator != null) AddLibrary(result, animator.Library);
                foreach (var sprite in root.GetComponentsInChildren<tk2dSprite>(true))
                    if (sprite != null) AddCollection(result, sprite, sprite.Collection);
            }
            return result;
        }

        static void AddLibrary(List<OwnedCollection> result, tk2dSpriteAnimation library)
        {
            if (library == null || library.clips == null) return;
            foreach (var clip in library.clips)
            {
                if (clip == null || clip.frames == null) continue;
                foreach (var frame in clip.frames)
                    if (frame != null) AddCollection(result, library, frame.spriteCollection);
            }
        }

        static void AddCollection(List<OwnedCollection> result, object owner,
            tk2dSpriteCollectionData collection)
        {
            if (owner == null || collection == null) return;
            var instance = collection.inst;
            if (instance == null || result.Any(x => ReferenceEquals(x.Owner, owner) &&
                ReferenceEquals(x.Collection, instance))) return;
            result.Add(new OwnedCollection { Owner = owner, Collection = instance });
        }

        SilksongCollectionOwnerStamp CaptureOwnerStamp() =>
            OwnerStamp(CaptureCharacterCollections().Concat(CaptureHudCollections()));

        static SilksongCollectionOwnerStamp OwnerStamp(IEnumerable<OwnedCollection> collections) =>
            new SilksongCollectionOwnerStamp(collections.Select(x => new SilksongCollectionInstance(
                x.Collection.spriteCollectionName ?? "", x.Owner, x.Collection, null)));

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object left, object right) => ReferenceEquals(left, right);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
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
                    () => captured != null && collection != null,
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
            return new HeroOwners { Hero = hero, Renderer = renderer, Animator = animator, Sprite = sprite,
                Animation = animation, DefaultLibrary = animator.Library };
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

        public void TickTeardown()
        {
            if (disposed) return;
            session.TickTeardown();
            LastError = session.LastError;
            if (!session.TeardownComplete) return;
            SceneManager.sceneLoaded -= SceneChanged;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            disposed = true;
            LastError = "";
            if (ReferenceEquals(Current, this)) Current = null;
        }

        public void Dispose()
        {
            TickTeardown();
            if (!disposed) throw new InvalidOperationException(LastError);
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
