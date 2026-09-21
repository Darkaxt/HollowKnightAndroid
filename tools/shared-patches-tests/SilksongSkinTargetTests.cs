using System.Linq;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SilksongSkinTargetTests
{
    [Fact]
    public void Catalog_is_exactly_the_nine_character_and_two_persistent_hud_targets()
    {
        var targets = SilksongSkinTargets.All;
        Assert.Equal(11, targets.Count);
        Assert.Equal(9, targets.Count(x => !x.IsHud));
        Assert.Equal(2, targets.Count(x => x.IsHud));
        Assert.Equal(new[] {
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
            "Assets/Collections/HUD Extras Cln Data/atlas0.png"
        }, targets.Select(x => x.CanonicalPath));
        Assert.Equal(2, targets.Single(x => x.CollectionName == "Hornet CrestWeapon Scythe").MaterialCount);
        Assert.Equal(2, targets.Single(x => x.CollectionName == "Hornet CrestWeapon Shaman Cln").MaterialCount);
        Assert.Equal(2, targets.Single(x => x.CollectionName == "HUD Cln").MaterialCount);
        Assert.All(targets.Where(x => x.MaterialCount == 2), x => Assert.True(x.RequiresSharedOriginal));
    }

    [Fact]
    public void Runtime_rules_reuse_exact_IsHud_metadata_for_each_explicit_scope()
    {
        var rules = SilksongSkinTargets.RuntimeRules;
        var character = SilksongSkinTargets.All.First(x => !x.IsHud).CanonicalPath;
        var hud = SilksongSkinTargets.All.First(x => x.IsHud).CanonicalPath;
        Assert.Equal("silksong", rules.ProfileId);
        Assert.Equal(11, rules.MappingLimit);
        Assert.True(rules.Allows("ALL", character));
        Assert.True(rules.Allows("ALL", hud));
        Assert.True(rules.Allows("CHARACTER_HUD", character));
        Assert.True(rules.Allows("CHARACTER_HUD", hud));
        Assert.True(rules.Allows("CHARACTER", character));
        Assert.False(rules.Allows("CHARACTER", hud));
        Assert.False(rules.Allows("BOGUS", character));
        Assert.False(rules.IsSupported("Knight.png"));
        Assert.False(rules.IsSupported("Assets/Collections/Tools Cln Data/atlas0.png"));
    }
}
