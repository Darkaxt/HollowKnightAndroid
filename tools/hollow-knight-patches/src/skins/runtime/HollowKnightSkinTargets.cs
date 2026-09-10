using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DualSouls.Skins.HollowKnight.Runtime
{
    // Adapted from igawa6/dualsouls Assets/HKMods.cs at
    // 5c22451435b772acde0c7e6456f9019bc1baef73 (source-only MIT label). See NOTICE.md.
    // These are rendering targets, not an alternate pack-name/registry authority.
    public static class HollowKnightSkinTargets
    {
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "hud cln", "Hud.png" },
            { "knight hatchling cln", "Hatchling.png" },
            { "grimmchild cln", "Grimm.png" },
            { "weaverling cln", "Weaver.png" },
            { "fluke spell cln", "Fluke.png" },
            { "knight slug cln", "Unn.png" },
            { "spell effects neutral", "VoidSpells.png" },
            { "spell effects 2", "Wraiths.png" },
            { "knight dream cutscene cln", "DreamArrival.png" },
            { "charm blocker cln", "Baldur.png" },
        };
        static readonly HashSet<string> RootSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Knight.png", "Sprint.png", "Unn.png", "Shade.png", "ShadeOrb.png", "Wraiths.png", "VoidSpells.png", "VS.png",
            "Geo.png", "Hud.png", "OrbFull.png", "Liquid.png", "QOrbs.png", "QOrbs2.png", "ScrOrbs.png", "ScrOrbs2.png",
            "DungRecharge.png", "SDCrystalBurst.png", "DoubleJFeather.png", "Leak.png", "HitPt.png", "ShadowDashBlobs.png",
            "Deathpt.png", "DDeathpt.png", "Baldur.png", "Fluke.png", "Grimm.png", "Shield.png", "Weaver.png", "Hatchling.png",
            "Compass.png", "Beam.png", "Cloak.png", "Shriek.png", "Wings.png", "Quirrel.png", "Webbed.png", "DreamArrival.png",
            "Dreamnail.png", "Hornet.png", "Birthplace.png", "DeathNail.png", "DeathAsh.png", "BrummWave.png", "BrummShield.png",
            "FlowerBreak.png", "Salubra.png",
        };
        public static readonly IReadOnlyDictionary<string, string[]> HeroObjects =
            new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>
            {
                { "Wraiths.png", new[] { "Scr Heads", "Scr Base" } },
                { "VoidSpells.png", new[] { "Scr Heads 2", "Scr Base 2" } },
                { "VS.png", new[] { "Heal Anim" } },
                { "QOrbs.png", new[] { "Q Orbs" } },
                { "QOrbs2.png", new[] { "Q Orbs 2" } },
                { "ScrOrbs.png", new[] { "Scr Orbs" } },
                { "ScrOrbs2.png", new[] { "Scr Orbs 2" } },
                { "HitPt.png", new[] { "Hit Pt 1", "Hit Pt 2" } },
                { "Leak.png", new[] { "Leak", "Low Health Leak" } },
                { "SDCrystalBurst.png", new[] { "SD Crystal Burst GL", "SD Crystal Burst GR", "SD Crystal Burst W" } },
                { "DoubleJFeather.png", new[] { "Double J Feather" } },
                { "ShadowDashBlobs.png", new[] { "Shadow Dash Blobs" } },
                { "Baldur.png", new[] { "Shell Anim" } },
                { "DeathAsh.png", new[] { "Ash L", "Ash R" } },
                { "Deathpt.png", new[] { "Particle Wave" } },
                { "DDeathpt.png", new[] { "Dream Burst Pt" } },
                { "BrummShield.png", new[] { "grimm_flame_particle" } },
                { "FlowerBreak.png", new[] { "white_petal_break_particle" } },
            });
        public static readonly IReadOnlyDictionary<string, string> InventoryObjects = Map(
            "Inventory/Claw.png|Mantis Claw", "Inventory/CRHeart.png|Super Dash", "Inventory/DJump.png|Double Jump",
            "Inventory/Lantern.png|Lantern", "Inventory/Tear.png|Acid Armour", "Inventory/Ore.png|Ore",
            "Inventory/SlyKey.png|Store Key", "Inventory/ElegentKey.png|White Key", "Inventory/CityKey.png|City Key",
            "Inventory/LoveKey.png|Love Key", "Inventory/SimpleKey.png|Simple Key", "Inventory/TramPass.png|Tram Pass",
            "Inventory/Brand.png|Kings Brand", "Inventory/RancidEgg.png|Rancid Egg", "Inventory/WJournal.png|Trinket1",
            "Inventory/HSeal.png|Trinket2", "Inventory/Idol.png|Trinket3", "Inventory/BlackEgg.png|Trinket4",
            "Inventory/Cloak_1.png|Dash Cloak", "Inventory/DSlash.png|Art Uppercut", "Inventory/CSlash.png|Art Cyclone",
            "Inventory/GSlash.png|Art Dash", "Inventory/Geo.png|Geo", "Inventory/Focus.png|Spell Focus",
            "Inventory/ArtBG.png|Art Backboard", "Inventory/RelicBG.png|trinket_backboard",
            "Inventory/Nail_1.png|Nail|level1", "Inventory/Nail_2.png|Nail|level2", "Inventory/Nail_3.png|Nail|level3",
            "Inventory/Nail_4.png|Nail|level4", "Inventory/Nail_5.png|Nail|level5",
            "Inventory/Vessel_0.png|Soul Orb|backboardSprite", "Inventory/Vessel_1.png|Soul Orb|singlePieceSprite",
            "Inventory/Vessel_2.png|Soul Orb|doublePieceSprite", "Inventory/Vessel_3.png|Soul Orb|fullSprite",
            "Inventory/DreamNail_0.png|Dream Nail|inactiveSprite", "Inventory/DreamNail_1.png|Dream Nail|activeSprite",
            "Inventory/DreamGate.png|Dream Gate|activeSprite", "Inventory/Flower_0.png|Xun Flower|inactiveSprite",
            "Inventory/Flower_1.png|Xun Flower|activeSprite", "Inventory/GodFinder_0.png|Godfinder|normalSprite",
            "Inventory/GodFinder_1.png|Godfinder|newBossSprite", "Inventory/GodFinder_2.png|Godfinder|allBossesSprite",
            "Inventory/Heart_0.png|Heart Pieces", "Inventory/Heart_1.png|Pieces 1", "Inventory/Heart_2.png|Pieces 2",
            "Inventory/Heart_3.png|Pieces 3", "Inventory/Heart_4.png|Pieces 4");
        public static readonly IReadOnlyDictionary<string, string> InventoryFsms = Map(
            "Inventory/Fireball_1.png|Spell Fireball>Check Active>Lv 1>0", "Inventory/Fireball_2.png|Spell Fireball>Check Active>Lv 2>0",
            "Inventory/Quake_1.png|Spell Quake>Check Active>Lv 1>0", "Inventory/Quake_2.png|Spell Quake>Check Active>Lv 2>0",
            "Inventory/Scream_1.png|Spell Scream>Check Active>Lv 1>0", "Inventory/Scream_2.png|Spell Scream>Check Active>Lv 2>0",
            "Inventory/Cloak_2.png|Equipment>Build Equipment List>Dash>16", "Inventory/Map.png|Equipment>Build Equipment List>Map>1",
            "Inventory/Quill.png|Equipment>Build Equipment List>Quill>1", "Inventory/MapQuill.png|Equipment>Build Equipment List>Map and Quill>1");
        public static readonly IReadOnlyDictionary<string, string> CharmFields = Map(
            "Charms/Charm_23_Unbreakable.png|unbreakableHeart", "Charms/Charm_24_Unbreakable.png|unbreakableGreed",
            "Charms/Charm_25_Unbreakable.png|unbreakableStrength", "Charms/Charm_40_1.png|grimmchildLevel1",
            "Charms/Charm_40_2.png|grimmchildLevel2", "Charms/Charm_40_3.png|grimmchildLevel3",
            "Charms/Charm_40_4.png|grimmchildLevel4", "Charms/Charm_40_5.png|nymmCharm");
        public static readonly IReadOnlyDictionary<string, string> CharmFsms = Map(
            "Charms/Charm_23_Broken.png|23>Glass HP>2>brokenGlassHP", "Charms/Charm_24_Broken.png|24>Glass Geo>2>brokenGlassGeo",
            "Charms/Charm_25_Broken.png|25>Glass Attack>2>brokenGlassAttack", "Charms/Charm_36_Left.png|36>R Queen>0>",
            "Charms/Charm_36_Right.png|36>R King>0>", "Charms/Charm_36_Full.png|36>R Final>0>whiteCharm",
            "Charms/Charm_36_Black.png|36>R Shade>0>blackCharm", "Charms/Charm_0.png|36>Check>7>");
        static IReadOnlyDictionary<string, string> Map(params string[] entries)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries) { int split = entry.IndexOf('|'); result.Add(entry.Substring(0, split), entry.Substring(split + 1)); }
            return new ReadOnlyDictionary<string, string>(result);
        }
        public static string CharmListTarget(int index)
        {
            if (index < 0 || index > 39 || index == 36) return null;
            return "Charms/Charm_" + index + (index >= 23 && index <= 25 ? "_Fragile.png" : ".png");
        }
        public static IEnumerable<string> SupportedTargets()
        {
            foreach (var target in RootSheets) yield return target;
            foreach (var map in new[] { InventoryObjects, InventoryFsms, CharmFields, CharmFsms })
                foreach (var target in map.Keys) yield return target;
            for (int i = 0; i < 40; i++) { var target = CharmListTarget(i); if (target != null) yield return target; }
        }
        public static string AtlasTarget(string name)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("HKSKIN", StringComparison.Ordinal) || name.Equals("icon", StringComparison.OrdinalIgnoreCase)) return null;
            var key = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : name + ".png";
            string found = null;
            foreach (var target in SupportedTargets())
            {
                string comparison = name.Contains("/") ? target : target.Substring(target.LastIndexOf('/') + 1);
                if (!string.Equals(comparison, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null && !string.Equals(found, target, StringComparison.OrdinalIgnoreCase)) return null;
                found = target;
            }
            return found;
        }
        // These owned roots are created by HKDualScreen.Bottom.Frame.cs. Companion clones consume
        // SkinStamp; they must never become a second authority for capturing game originals.
        public static bool IsAuthoritativeHierarchy(IEnumerable<string> ancestors)
        {
            foreach (var name in ancestors)
                if (name == "HKCompanionRoot" || name == "HKPaneCloneStaging" || name == "HKCompFrame") return false;
            return true;
        }
        static readonly HashSet<string> Supported = new HashSet<string>(SupportedTargets(), StringComparer.OrdinalIgnoreCase);
        public static bool IsSupported(string canonicalTarget) => Supported.Contains(canonicalTarget);
        public static string CollectionTarget(string name, int page)
        {
            if (string.IsNullOrEmpty(name) || page < 0) return null;
            if (string.Equals(name, "Knight", StringComparison.OrdinalIgnoreCase))
                return page == 0 ? "Knight.png" : page == 1 ? "Sprint.png" : null;
            // OrbFull is a SpriteRenderer; Liquid is its own material. Never smear either on these atlases.
            if (string.Equals(name, "HUD_SoulOrb_Fills", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "HUD_SoulOrb_Prefab", StringComparison.OrdinalIgnoreCase)) return null;
            if (Aliases.TryGetValue(name, out var target)) return target;
            target = name + ".png";
            return RootSheets.Contains(target) ? target : null;
        }
    }
}
