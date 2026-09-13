using DualSouls.Mods.HollowKnight;
using Xunit;

namespace SharedPatches.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HollowKnightGameApiCollection
{
    public const string Name = "Hollow Knight game API";
}

[Collection(HollowKnightGameApiCollection.Name)]
public sealed class HollowKnightGameplayBehaviorTests : System.IDisposable
{
    public HollowKnightGameplayBehaviorTests()
    {
        ResetGame();
    }

    [Fact]
    public void SharedBaselineRestoreRevertsEveryOwnedGameplayMutation()
    {
        var player = new PlayerData
        {
            health = 5,
            isInvincible = false,
            nailSmithUpgrades = 1,
            nailDamage = 9,
            MPCharge = 99,
        };
        var hero = new HeroController { RUN_SPEED = 8f, WALK_SPEED = 4f };
        PlayerData.instance = player;
        HeroController.instance = hero;
        GameManager.UnsafeInstance = new GameManager { playerData = player, hero_ctrl = hero };
        CheatManager.IsInstaKillEnabled = false;
        var api = new HollowKnightGameTweakApi();
        api.CaptureBaseline();

        api.SetDamageMode(HollowKnightDamageMode.Invincible);
        api.SetNailDamageMultiplier(3);
        api.SetOneHitKills(true);
        api.SetRunSpeedMultiplier(1.5f);
        api.SetUnlimitedSoul(true);
        Assert.True(player.isInvincible);
        Assert.Equal(27, player.nailDamage);
        Assert.True(CheatManager.IsInstaKillEnabled);
        Assert.Equal(12f, hero.RUN_SPEED);

        api.RestoreBaseline();
        Assert.False(player.isInvincible);
        Assert.Equal(9, player.nailDamage);
        Assert.False(CheatManager.IsInstaKillEnabled);
        Assert.Equal(8f, hero.RUN_SPEED);
        Assert.Equal(4f, hero.WALK_SPEED);

        player.MPCharge = 20;
        api.TickGameplay();
        Assert.Equal(20, player.MPCharge);
    }

    [Fact]
    public void DamageReceivedRefundsNormalMaskLossButNotDeathOrScriptedDrain()
    {
        var player = new PlayerData { health = 5 };
        var hero = new HeroController();
        PlayerData.instance = player;
        HeroController.instance = hero;
        GameManager.UnsafeInstance = new GameManager { playerData = player, hero_ctrl = hero };
        var api = new HollowKnightGameTweakApi();

        api.SetDamageMode(HollowKnightDamageMode.NoMaskLoss);
        player.isInvincible = true;
        api.TickGameplay();
        Assert.True(player.isInvincible);

        player.isInvincible = false;
        player.health = 3;
        api.TickGameplay();
        Assert.Equal(5, player.health);
        Assert.Equal(2, hero.HealthAdded);

        player.health = 0;
        api.TickGameplay();
        Assert.Equal(0, player.health);

        player.health = 8;
        api.TickGameplay();
        player.health = 3;
        api.TickGameplay();
        Assert.Equal(3, player.health);
    }

    [Fact]
    public void DamageReceivedRestoresExactInvincibilityBaselineAcrossPlayerReplacement()
    {
        var first = new PlayerData { health = 5, isInvincible = false };
        var firstHero = new HeroController();
        PlayerData.instance = first;
        HeroController.instance = firstHero;
        GameManager.UnsafeInstance = new GameManager { playerData = first, hero_ctrl = firstHero };
        var api = new HollowKnightGameTweakApi();

        api.SetDamageMode(HollowKnightDamageMode.Invincible);
        Assert.True(first.isInvincible);

        var replacement = new PlayerData { health = 7, isInvincible = true };
        PlayerData.instance = replacement;
        GameManager.UnsafeInstance.playerData = replacement;
        api.TickGameplay();
        Assert.False(first.isInvincible);
        Assert.True(replacement.isInvincible);

        api.RestoreDamageMode();
        Assert.True(replacement.isInvincible);
    }

    [Fact]
    public void NailDamageRecomputesSmithUpgradeAndRestoresExactLiveBaseline()
    {
        var first = new PlayerData { nailSmithUpgrades = 1, nailDamage = 9 };
        PlayerData.instance = first;
        GameManager.UnsafeInstance = new GameManager { playerData = first };
        var api = new HollowKnightGameTweakApi();

        api.SetNailDamageMultiplier(2);
        Assert.Equal(18, first.nailDamage);

        first.nailSmithUpgrades = 2;
        first.nailDamage = 13;
        api.TickGameplay();
        Assert.Equal(26, first.nailDamage);

        var replacement = new PlayerData { nailSmithUpgrades = 3, nailDamage = 17 };
        PlayerData.instance = replacement;
        GameManager.UnsafeInstance.playerData = replacement;
        api.TickGameplay();
        Assert.Equal(13, first.nailDamage);
        Assert.Equal(34, replacement.nailDamage);

        api.RestoreNailDamage();
        Assert.Equal(17, replacement.nailDamage);
        Assert.All(PlayMakerFSM.Broadcasts, item => Assert.Equal("UPDATE NAIL DAMAGE", item));
    }

    [Fact]
    public void OneHitKillsUsesManagedCheatGateAndRestoresExactBaseline()
    {
        CheatManager.IsInstaKillEnabled = false;
        var api = new HollowKnightGameTweakApi();

        api.SetOneHitKills(true);
        Assert.True(CheatManager.IsInstaKillEnabled);

        CheatManager.IsInstaKillEnabled = false;
        api.TickGameplay();
        Assert.True(CheatManager.IsInstaKillEnabled);

        api.RestoreOneHitKills();
        Assert.False(CheatManager.IsInstaKillEnabled);

        CheatManager.IsInstaKillEnabled = true;
        api.SetOneHitKills(true);
        api.RestoreOneHitKills();
        Assert.True(CheatManager.IsInstaKillEnabled);
    }

    [Fact]
    public void RunSpeedRestoresEachHeroExactBaselineAcrossReplacement()
    {
        var first = new HeroController { RUN_SPEED = 8f, WALK_SPEED = 4f };
        HeroController.instance = first;
        var api = new HollowKnightGameTweakApi();

        api.SetRunSpeedMultiplier(1.25f);
        Assert.Equal(10f, first.RUN_SPEED);
        Assert.Equal(5f, first.WALK_SPEED);

        var replacement = new HeroController { RUN_SPEED = 12f, WALK_SPEED = 6f };
        HeroController.instance = replacement;
        api.TickGameplay();
        Assert.Equal(8f, first.RUN_SPEED);
        Assert.Equal(4f, first.WALK_SPEED);
        Assert.Equal(15f, replacement.RUN_SPEED);
        Assert.Equal(7.5f, replacement.WALK_SPEED);

        api.RestoreRunSpeed();
        Assert.Equal(12f, replacement.RUN_SPEED);
        Assert.Equal(6f, replacement.WALK_SPEED);
    }

    [Fact]
    public void UnlimitedSoulUsesNormalRefillPathAndStopsExactlyWhenDisabled()
    {
        var player = new PlayerData { health = 5, MPCharge = 30 };
        var hero = new HeroController();
        PlayerData.instance = player;
        HeroController.instance = hero;
        GameManager.UnsafeInstance = new GameManager { playerData = player, hero_ctrl = hero };
        var api = new HollowKnightGameTweakApi();

        api.SetUnlimitedSoul(true);
        Assert.Equal(63, player.MPCharge);
        Assert.Equal(33, hero.SoulAdded);

        api.RestoreUnlimitedSoul();
        player.MPCharge = 20;
        api.TickGameplay();
        Assert.Equal(20, player.MPCharge);
        Assert.Equal(33, hero.SoulAdded);

        api.SetUnlimitedSoul(true);
        player.health = 0;
        player.MPCharge = 10;
        api.TickGameplay();
        Assert.Equal(10, player.MPCharge);
    }

    public void Dispose()
    {
        ResetGame();
    }

    static void ResetGame()
    {
        PlayerData.instance = null;
        HeroController.instance = null;
        GameManager.UnsafeInstance = null;
        CheatManager.IsInstaKillEnabled = false;
        PlayMakerFSM.Broadcasts.Clear();
    }
}
