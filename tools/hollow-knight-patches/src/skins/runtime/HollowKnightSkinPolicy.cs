using DualSouls.Skins.Runtime;

namespace DualSouls.Skins.HollowKnight.Runtime
{
    // Existing canonical targets only: no new preload aliases or live-binding claims.
    public static class HollowKnightSkinPolicy
    {
        public static readonly SkinRuntimeRules RuntimeRules = new SkinRuntimeRules(
            "hollow-knight", 205, HollowKnightSkinTargets.IsSupported, Allows);

        public static bool Allows(string spriteScope, string target)
        {
            if (!HollowKnightSkinTargets.TryGetFamily(target, out var family)) return false;
            if (spriteScope == SkinSpriteScopes.All) return true;
            if (spriteScope == SkinSpriteScopes.CharacterHud)
                return family == HollowKnightSkinFamily.Character || family == HollowKnightSkinFamily.Hud;
            return spriteScope == SkinSpriteScopes.Character && family == HollowKnightSkinFamily.Character;
        }
    }
}
