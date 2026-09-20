using System;
using System.IO;
using Xunit;

namespace SharedPatches.Tests;

public sealed class HollowKnightModsSourceContractTests
{
    [Fact]
    public void SaveStatesUseBoundedSidecarsAndGameOwnedManagedSaveLoad()
    {
        string source = Source("HollowKnightGameplayFeatures.cs");

        Assert.Contains("MaximumStateBytes = 4 * 1024 * 1024", source, StringComparison.Ordinal);
        Assert.Contains("game.SaveLevelState()", source, StringComparison.Ordinal);
        Assert.Contains("new SaveGameData(game.playerData, game.sceneData)", source, StringComparison.Ordinal);
        Assert.Contains("SetLoadedGameData", source, StringComparison.Ordinal);
        Assert.Contains("game.BeginSceneTransition", source, StringComparison.Ordinal);
        Assert.Contains("slot < 1 || slot > 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearSaveFile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("game.SaveGame(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Marshal.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IntPtr", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HKTW_", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveStatesAreProfileScopedAndRebaseLiveModOwnership()
    {
        string features = Source("HollowKnightGameplayFeatures.cs");
        string api = Source("HollowKnightGameTweakApi.cs");

        Assert.Contains("public int profileId;", features, StringComparison.Ordinal);
        Assert.Contains("ProfileStateDirectory", features, StringComparison.Ordinal);
        Assert.Contains("\"profile-\" + profileId", features, StringComparison.Ordinal);
        Assert.Contains("envelope.profileId != currentProfileId", features, StringComparison.Ordinal);
        Assert.Contains("envelope.save.playerData.profileID != currentProfileId", features, StringComparison.Ordinal);
        Assert.Contains("new object[] { envelope.save, currentProfileId }", features, StringComparison.Ordinal);
        Assert.Contains("stateTransferPreparation?.Invoke()", features, StringComparison.Ordinal);
        Assert.Contains("SetStateTransferPreparation(PrepareForStateTransfer)", api, StringComparison.Ordinal);
        Assert.Contains("void EnsureStateTransferPreparation()", api, StringComparison.Ordinal);
        Assert.Contains("EnsureStateTransferPreparation();\n            HollowKnightGameplayFeatures.SaveState", api, StringComparison.Ordinal);
        Assert.Contains("EnsureStateTransferPreparation();\n            HollowKnightGameplayFeatures.LoadState", api, StringComparison.Ordinal);
        Assert.Contains("if (enabled) EnsureStateTransferPreparation();", api, StringComparison.Ordinal);
        Assert.Contains("RestoreDamageOwner()", api, StringComparison.Ordinal);
        Assert.Contains("RestoreNailOwner()", api, StringComparison.Ordinal);
        Assert.Contains("RestoreRunSpeedOwner()", api, StringComparison.Ordinal);
        Assert.DoesNotContain("envelope.save.playerData.profileID });", features, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoMapRestoresEveryMutatedMapValue()
    {
        string source = Source("HollowKnightGameplayFeatures.cs");

        Assert.Contains("mapScenesBaseline", source, StringComparison.Ordinal);
        Assert.Contains("mapRegionBaseline", source, StringComparison.Ordinal);
        Assert.Contains("CaptureMapBaseline(player)", source, StringComparison.Ordinal);
        Assert.Contains("RestoreMapBaseline(player)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutesAndBenchMapSeamsHaveConcreteManagedIntegrations()
    {
        string hooks = Source("HkStageHooks.cs");
        string presenter = Source("HollowKnightModsPresenter.cs");

        Assert.Contains("dev.silksong.launcher.skins.ui.SkinsActivity", hooks, StringComparison.Ordinal);
        Assert.Contains("player.SetBenchRespawn", hooks, StringComparison.Ordinal);
        Assert.Contains("game.ReadyForRespawn(false)", hooks, StringComparison.Ordinal);
        Assert.Contains("HKDualScreen.OpenBenchTeleportRoute()", hooks, StringComparison.Ordinal);
        Assert.Contains("activeInstance.tab.tap = COMP_MAP", presenter, StringComparison.Ordinal);
        Assert.Contains("menu.ActivateSelected()", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("IsBenchRecorded(string scene) => false", hooks, StringComparison.Ordinal);
        Assert.DoesNotContain("BenchWarp(string scene) { }", hooks, StringComparison.Ordinal);
    }

    static string Source(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "source-contracts", name));
}
