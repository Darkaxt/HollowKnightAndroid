using System;
using DualSouls.Skins.Runtime;

namespace DualSouls.Skins.HollowKnight.Runtime
{
    // Existing canonical targets only: no new preload aliases or live-binding claims.
    public static class HollowKnightSkinPolicy
    {
        public static readonly SkinRuntimeRules RuntimeRules = new SkinRuntimeRules(
            "hollow-knight", 205, HollowKnightSkinTargets.IsSupported, Allows);

        public static bool Allows(string mode, string target)
        {
            if (!HollowKnightSkinTargets.IsSupported(target)) return false;
            if (mode == "ON") return true;
            if (mode != "ROTATE") return false;
            // World currency is not Inventory/Geo UI. Birthplace combines character frames
            // with grave-shell scenery: its character frames are the narrow vanilla exception.
            // NPC sheets (including Quirrel) otherwise remain character-eligible.
            return !string.Equals(target, "Geo.png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(target, "Birthplace.png", StringComparison.OrdinalIgnoreCase);
        }
    }
}
