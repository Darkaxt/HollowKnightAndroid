using System.IO;
using System.Linq;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SilksongSkinTargetTests
{
    static readonly string[] Legacy = {
        "Assets/Collections/Hornet Cln Data/atlas0.png",
        "Assets/Collections/Hornet Cloakless Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Dagger Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Drill Lance Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Scythe Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Shaman Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Warrior Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Whip Cln Data/atlas0.png",
        "Assets/Collections/Hornet_Needolin_Windy Data/atlas0.png",
        "Assets/Collections/HUD Cln Data/atlas0.png",
        "Assets/Collections/HUD Extras Cln Data/atlas0.png",
        "Assets/Collections/Knight Data/atlas0.png",
        "Assets/Collections/Knight Data/atlas1.png",
        "Assets/Collections/Knight Data/atlas2.png",
        "Assets/Collections/Knight Data/atlas3.png",
        "Assets/Collections/Bone Beast Child Call Cln Data/atlas0.png"
    };

    [Fact]
    public void Catalog_keeps_the_sixteen_owner_bound_targets_first_and_covers_every_collection_texture()
    {
        var targets = SilksongSkinTargets.All;
        Assert.Equal(466, targets.Count);
        Assert.Equal("silksong-1.0.29980-collections-" + targets.Count, SilksongSkinTargets.CatalogId);
        Assert.Equal(Legacy, targets.Take(16).Select(x => x.CanonicalPath));
        Assert.All(targets.Take(16), x => Assert.True(x.OwnerBound));
        Assert.All(targets.Skip(16), x => Assert.False(x.OwnerBound));
        Assert.Equal(new[] { "HUD Cln", "HUD Extras Cln" }, targets.Take(16).Where(x => x.IsHud).Select(x => x.CollectionName));
        Assert.Equal(targets.Count, targets.Select(x => x.CanonicalPath).Distinct().Count());
        Assert.Equal(2, targets.Single(x => x.CollectionName == "Hornet CrestWeapon Scythe").MaterialCount);
        Assert.Equal(2, targets.Single(x => x.CollectionName == "HUD Cln").MaterialCount);
        // Hornet's moveset: one collection, four distinct pages, one target per page (#34).
        var knight = targets.Where(x => x.CollectionName == "Knight").ToList();
        Assert.Equal(new[] { "atlas0", "atlas1", "atlas2", "atlas3" }, knight.Select(x => x.TextureName));
        Assert.All(knight, x => { Assert.Equal(4, x.MaterialCount); Assert.Equal(SilksongSkinFamily.Character, x.Family); });
        // Hero collections the first hand-made catalog missed (#63).
        Assert.Equal(SilksongSkinFamily.Character, targets.Single(x => x.CollectionName == "Tools Cln").Family);
        Assert.Equal(SilksongSkinFamily.Character, targets.Single(x => x.CollectionName == "Knight Hatchling Cln").Family);
        Assert.Equal(SilksongSkinFamily.Hud, targets.Single(x => x.CollectionName == "HUD Frame Steel Soul Cln").Family);
        Assert.Equal(SilksongSkinFamily.Ui, targets.First(x => x.CollectionName == "UI Journal Cln").Family);
        Assert.Equal(SilksongSkinFamily.World, targets.First(x => x.CollectionName == "Bone Beast Cln").Family);
    }

    [Fact]
    public void Catalog_file_matches_the_generated_runtime_table()
    {
        string root = Path.GetDirectoryName(typeof(SilksongSkinTargetTests).Assembly.Location);
        while (root != null && !File.Exists(Path.Combine(root, "docs/superpowers/specs/data/silksong-skin-catalog-v1.txt")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var lines = File.ReadAllText(Path.Combine(root, "docs/superpowers/specs/data/silksong-skin-catalog-v1.txt")).Split('\n');
        Assert.Equal("", lines[lines.Length - 1]);
        Assert.Equal(SilksongSkinTargets.All.Select(x => x.CanonicalPath), lines.Take(lines.Length - 1));
    }

    [Fact]
    public void Runtime_rules_use_the_same_three_scope_families_as_hollow_knight()
    {
        var rules = SilksongSkinTargets.RuntimeRules;
        string Of(SilksongSkinFamily family) => SilksongSkinTargets.All.First(x => x.Family == family).CanonicalPath;
        string character = Of(SilksongSkinFamily.Character), hud = Of(SilksongSkinFamily.Hud),
            world = Of(SilksongSkinFamily.World), ui = Of(SilksongSkinFamily.Ui);
        Assert.Equal("silksong", rules.ProfileId);
        Assert.Equal(SilksongSkinTargets.All.Count, rules.MappingLimit);
        foreach (var scope in new[] { "ALL", "CHARACTER_WORLD" })
            Assert.All(new[] { character, hud, world, ui }, x => Assert.True(rules.Allows(scope, x)));
        Assert.True(rules.Allows("CHARACTER_HUD", character));
        Assert.True(rules.Allows("CHARACTER_HUD", hud));
        Assert.False(rules.Allows("CHARACTER_HUD", world));
        Assert.False(rules.Allows("CHARACTER_HUD", ui));
        Assert.True(rules.Allows("CHARACTER", character));
        Assert.False(rules.Allows("CHARACTER", hud));
        Assert.False(rules.Allows("CHARACTER", world));
        Assert.False(rules.Allows("BOGUS", character));
        Assert.False(rules.IsSupported("Knight.png"));
        Assert.True(rules.IsSupported("Assets/Collections/Tools Cln Data/atlas0.png"));
    }
}
