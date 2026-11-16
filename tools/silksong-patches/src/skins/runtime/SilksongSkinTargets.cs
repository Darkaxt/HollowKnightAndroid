using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DualSouls.Skins.Runtime;

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongSkinTarget
    {
        public string CanonicalPath { get; }
        public string CollectionName { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaterialCount { get; }
        /// <summary>Material page whose original texture this target replaces (Knight has four distinct pages).</summary>
        public int Page { get; }
        public bool RequiresSharedOriginal { get; }
        public bool IsHud { get; }

        internal SilksongSkinTarget(string canonicalPath, string collectionName, int width, int height,
            int materialCount, bool requiresSharedOriginal, bool isHud, int page = 0)
        {
            if (page < 0 || page >= materialCount) throw new ArgumentOutOfRangeException(nameof(page));
            Page = page;
            CanonicalPath = canonicalPath;
            CollectionName = collectionName;
            Width = width;
            Height = height;
            MaterialCount = materialCount;
            RequiresSharedOriginal = requiresSharedOriginal;
            IsHud = isHud;
        }
    }

    public static class SilksongSkinTargets
    {
        static readonly IReadOnlyList<SilksongSkinTarget> targets = new ReadOnlyCollection<SilksongSkinTarget>(new[]
        {
            Target("Assets/Collections/Hornet Cln Data/atlas0.png", "Hornet Cln", 2048, 2048, 1),
            Target("Assets/Collections/Hornet Cloakless Cln Data/atlas0.png", "Hornet Cloakless Cln", 2048, 4096, 1),
            Target("Assets/Collections/Hornet CrestWeapon Dagger Cln Data/atlas0.png", "Hornet CrestWeapon Dagger Cln", 2048, 2048, 1),
            Target("Assets/Collections/Hornet CrestWeapon Drill Lance Cln Data/atlas0.png", "Hornet CrestWeapon Drill Lance Cln", 2048, 2048, 1),
            Target("Assets/Collections/Hornet CrestWeapon Scythe Data/atlas0.png", "Hornet CrestWeapon Scythe", 2048, 4096, 2, true),
            Target("Assets/Collections/Hornet CrestWeapon Shaman Cln Data/atlas0.png", "Hornet CrestWeapon Shaman Cln", 2048, 2048, 2, true),
            Target("Assets/Collections/Hornet CrestWeapon Warrior Cln Data/atlas0.png", "Hornet CrestWeapon Warrior Cln", 2048, 4096, 1),
            Target("Assets/Collections/Hornet CrestWeapon Whip Cln Data/atlas0.png", "Hornet CrestWeapon Whip Cln", 1024, 2048, 1),
            Target("Assets/Collections/Hornet_Needolin_Windy Data/atlas0.png", "Hornet_Needolin_Windy", 1024, 2048, 1),
            Target("Assets/Collections/HUD Cln Data/atlas0.png", "HUD Cln", 4096, 4096, 2, true, true),
            Target("Assets/Collections/HUD Extras Cln Data/atlas0.png", "HUD Extras Cln", 512, 1024, 1, false, true),
            // Hornet's own moveset lives in the inherited "Knight" collection: four distinct 4096 pages (#34).
            Target("Assets/Collections/Knight Data/atlas0.png", "Knight", 4096, 4096, 4, page: 0),
            Target("Assets/Collections/Knight Data/atlas1.png", "Knight", 4096, 4096, 4, page: 1),
            Target("Assets/Collections/Knight Data/atlas2.png", "Knight", 4096, 4096, 4, page: 2),
            Target("Assets/Collections/Knight Data/atlas3.png", "Knight", 4096, 4096, 4, page: 3),
            Target("Assets/Collections/Bone Beast Child Call Cln Data/atlas0.png", "Bone Beast Child Call Cln", 1024, 2048, 1)
        });
        static readonly IReadOnlyDictionary<string, SilksongSkinTarget> byPath =
            new ReadOnlyDictionary<string, SilksongSkinTarget>(targets.ToDictionary(x => x.CanonicalPath, StringComparer.Ordinal));

        public static IReadOnlyList<SilksongSkinTarget> All => targets;
        public static readonly SkinRuntimeRules RuntimeRules = new SkinRuntimeRules("silksong", 16,
            IsSupported, (spriteScope, path) => TryGetByPath(path, out var target) &&
                (spriteScope == SkinSpriteScopes.All ||
                 spriteScope == SkinSpriteScopes.CharacterHud ||
                 // Silksong has no separate world targets, so PLAYER + WORLD is player-only here.
                 (spriteScope == SkinSpriteScopes.Character || spriteScope == SkinSpriteScopes.CharacterWorld) && !target.IsHud));

        public static bool IsSupported(string path) => path != null && byPath.ContainsKey(path);
        public static bool TryGetByPath(string path, out SilksongSkinTarget target) =>
            byPath.TryGetValue(path ?? "", out target);

        static SilksongSkinTarget Target(string path, string collection, int width, int height,
            int materialCount, bool shared = false, bool hud = false, int page = 0) =>
            new SilksongSkinTarget(path, collection, width, height, materialCount, shared, hud, page);
    }
}
