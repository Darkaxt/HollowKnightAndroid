using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DualSouls.Skins.Runtime;

namespace DualSouls.Skins.Silksong.Runtime
{
    /// <summary>Scope family, the same split as Hollow Knight's (#63): EVERYTHING adds World and Ui.</summary>
    public enum SilksongSkinFamily { Character, Hud, World, Ui }

    public sealed class SilksongSkinTarget
    {
        public string CanonicalPath { get; }
        public string CollectionName { get; }
        /// <summary>Exact name of the original atlas texture this target replaces (e.g. "atlas2").</summary>
        public string TextureName { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaterialCount { get; }
        public SilksongSkinFamily Family { get; }
        public bool IsHud => Family == SilksongSkinFamily.Hud;
        /// <summary>The first sixteen targets are found through the persistent hero/HUD owners; all others
        /// through a scan of loaded collections, silently skipped while their collection is not loaded.</summary>
        public bool OwnerBound { get; }

        internal SilksongSkinTarget(string collectionName, string textureName, int width, int height,
            int materialCount, SilksongSkinFamily family, bool ownerBound)
        {
            if (materialCount < 1) throw new ArgumentOutOfRangeException(nameof(materialCount));
            CanonicalPath = "Assets/Collections/" + collectionName + " Data/" + textureName + ".png";
            CollectionName = collectionName;
            TextureName = textureName;
            Width = width;
            Height = height;
            MaterialCount = materialCount;
            Family = family;
            OwnerBound = ownerBound;
        }
    }

    public static partial class SilksongSkinTargets
    {
        /// <summary>Owner-bound legacy targets: the catalog's first rows (silksong-1.0.29980-character-hud-16).</summary>
        public const int OwnerBoundCount = 16;

        static readonly IReadOnlyList<SilksongSkinTarget> targets = new ReadOnlyCollection<SilksongSkinTarget>(
            CatalogRows().Select((x, i) => new SilksongSkinTarget(x.Collection, x.Texture, x.Width, x.Height, x.Materials,
                x.Family, i < OwnerBoundCount)).ToArray());
        static readonly IReadOnlyDictionary<string, SilksongSkinTarget> byPath =
            new ReadOnlyDictionary<string, SilksongSkinTarget>(targets.ToDictionary(x => x.CanonicalPath, StringComparer.Ordinal));
        static readonly IReadOnlyDictionary<string, SilksongSkinTarget[]> byCollection =
            new ReadOnlyDictionary<string, SilksongSkinTarget[]>(targets.GroupBy(x => x.CollectionName, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal));

        public static IReadOnlyList<SilksongSkinTarget> All => targets;
        static readonly IReadOnlyDictionary<string, int> order =
            new ReadOnlyDictionary<string, int>(targets.Select((x, i) => (x.CanonicalPath, i)).ToDictionary(x => x.CanonicalPath, x => x.i, StringComparer.Ordinal));
        // Memory-budget priority: the player first, then the HUD, then world and menu art, each in catalog order.
        public static readonly SkinRuntimeRules RuntimeRules = new SkinRuntimeRules("silksong", targets.Count,
            IsSupported, Allows, path => TryGetByPath(path, out var target)
                ? (int)target.Family * targets.Count + order[path] : int.MaxValue);

        /// <summary>Same scope semantics as HollowKnightSkinPolicy.Allows.</summary>
        public static bool Allows(string spriteScope, string path)
        {
            if (!TryGetByPath(path, out var target)) return false;
            switch (spriteScope)
            {
                // CHARACTER_WORLD is a retired fourth mode; stored documents keep it and read as EVERYTHING (#63).
                case SkinSpriteScopes.All:
                case SkinSpriteScopes.CharacterWorld: return true;
                case SkinSpriteScopes.Character: return target.Family == SilksongSkinFamily.Character;
                case SkinSpriteScopes.CharacterHud:
                    return target.Family == SilksongSkinFamily.Character || target.Family == SilksongSkinFamily.Hud;
                default: return false;
            }
        }

        public static bool IsSupported(string path) => path != null && byPath.ContainsKey(path);
        public static bool TryGetByPath(string path, out SilksongSkinTarget target) =>
            byPath.TryGetValue(path ?? "", out target);
        public static IReadOnlyList<SilksongSkinTarget> ForCollection(string collectionName) =>
            collectionName != null && byCollection.TryGetValue(collectionName, out var found) ? found : Array.Empty<SilksongSkinTarget>();
    }
}
