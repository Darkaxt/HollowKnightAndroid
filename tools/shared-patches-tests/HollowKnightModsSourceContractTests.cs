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
        Assert.Contains("int currentProfileId = game.profileID;", features, StringComparison.Ordinal);
        Assert.Contains("(savedProfileId != currentProfileId && savedProfileId != 0)", features, StringComparison.Ordinal);
        Assert.DoesNotContain("game.playerData.profileID", features, StringComparison.Ordinal);
        Assert.Contains("new object[] { envelope.save, currentProfileId }", features, StringComparison.Ordinal);
        Assert.Contains("stateTransferPreparation?.Invoke()", features, StringComparison.Ordinal);
        Assert.Contains("SetStateTransferPreparation(PrepareForStateTransfer)", api, StringComparison.Ordinal);
        Assert.Contains("void EnsureStateTransferPreparation()", api, StringComparison.Ordinal);
        Assert.Contains("EnsureStateTransferPreparation();\n            HollowKnightGameplayFeatures.SaveState", api, StringComparison.Ordinal);
        Assert.Contains("_stateLoadOperation.Begin(operationToken", api, StringComparison.Ordinal);
        Assert.Contains("HollowKnightGameplayFeatures.LoadState(", api, StringComparison.Ordinal);
        Assert.Contains("_stateLoadOperation.Complete(", api, StringComparison.Ordinal);
        Assert.Contains("_stateLoadOperation.Poll(", api, StringComparison.Ordinal);
        Assert.Contains("_stateLoadOperation.Cancel(", api, StringComparison.Ordinal);
        Assert.Contains("\"load_from_slot\"", api, StringComparison.Ordinal);
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
        string api = Source("HollowKnightGameTweakApi.cs");
        string companion = Source("HKDualScreen.cs");
        string lowerMap = Source("HkMapScreen.cs");
        string nativeMenu = NativeSource("HollowKnightNativeModsMenu.cs");

        Assert.Contains("dev.silksong.launcher.skins.ui.SkinsActivity", hooks, StringComparison.Ordinal);
        Assert.Contains("player.SetBenchRespawn", hooks, StringComparison.Ordinal);
        Assert.Contains("game.ReadyForRespawn(false)", hooks, StringComparison.Ordinal);
        Assert.Contains("pendingBenchCompletion", hooks, StringComparison.Ordinal);
        Assert.Contains("_benchOperation.Begin(operationToken", api, StringComparison.Ordinal);
        Assert.Contains("_benchOperation.Poll(", api, StringComparison.Ordinal);
        Assert.Contains("_benchOperation.Complete(", api, StringComparison.Ordinal);
        Assert.Contains("int generation = HollowKnightNativeModsMenu.BenchBindingGeneration", hooks, StringComparison.Ordinal);
        Assert.Contains("pendingBenchToken = operationToken", hooks, StringComparison.Ordinal);
        Assert.Contains("pendingBenchGeneration = generation", hooks, StringComparison.Ordinal);
        Assert.Contains("HollowKnightNativeModsMenu.OpenBenchTeleportRoute(operationToken, generation)", hooks, StringComparison.Ordinal);
        Assert.Contains("pendingBenchToken == operationToken && pendingBenchGeneration == generation", hooks, StringComparison.Ordinal);
        Assert.Contains("HollowKnightNativeModsMenu.BenchRouteIsCurrent(operationToken, generation)", hooks, StringComparison.Ordinal);
        Assert.Contains("internal static void OpenBenchTeleportRoute(long token, int generation)", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("current._benchOperationToken = token", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("current._benchGeneration = generation", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("current.OpenBenchRoute()", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("current._benchOperationToken == token && current._benchGeneration == generation", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("current._nativeOpen && current._openRoute == NativeMenuRoute.Mods", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("global::HkStageHooks.RecordedBenchScenes()", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("if (_benchOpen) { SubmitBench(button); return; }", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("global::HkStageHooks.BenchWarp(\n                    _benchRows[_benchSelected], token, generation)", nativeMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("HKDualScreen.OpenBenchTeleportRoute", hooks, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenBenchTeleportRoute", companion, StringComparison.Ordinal);
        Assert.DoesNotContain("HKDualScreen", nativeMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("COMP_MAP", nativeMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("tab.tap", nativeMenu, StringComparison.Ordinal);
        Assert.Contains("!global::HkStageHooks.IsBenchRecorded(scene)", lowerMap, StringComparison.Ordinal);
        Assert.Contains("global::HkStageHooks.BenchWarp(scene)", lowerMap, StringComparison.Ordinal);
        Assert.Contains("_menu.ActivateSelected()", nativeMenu, StringComparison.Ordinal);
        Assert.DoesNotContain("IsBenchRecorded(string scene) => false", hooks, StringComparison.Ordinal);
        Assert.DoesNotContain("BenchWarp(string scene) { }", hooks, StringComparison.Ordinal);
    }

    static string NativeSource(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-skin-contracts", name));

    [Fact]
    public void LowerCharmsPaneHonoursEquipAnywhereAwayFromBenches()
    {
        // #17: the mod forces atBench only while the game's own inventory is open, and the
        // pane refuses to equip while that inventory is open, so the pane must read the mod itself.
        string features = Source("HollowKnightGameplayFeatures.cs");
        string pane = Source("HkCharmsScreen.cs");
        Assert.Contains("EquipAnywhereActive = enabled;", features, StringComparison.Ordinal);
        Assert.Contains("if (gm.inventoryFSM != null && gm.inventoryFSM.ActiveStateName != \"Closed\") return \"Close the inventory\";", pane, StringComparison.Ordinal);
        Assert.Contains("if (!pd.atBench && !DualSouls.Mods.HollowKnight.HollowKnightGameplayFeatures.EquipAnywhereActive) return \"Rest at a bench\";", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!pd.atBench) return", pane, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeathCounterBindsToTheChosenSlotBeforeTheFirstSave()
    {
        // Thor, 2.0.49: the first death of a new Hollow Knight game read DEATHS 0,
        // because PlayerData.profileID is only written by the save that death makes.
        foreach (string name in new[] { "HollowKnightGameplayFeatures.cs", "SilksongGameplayFeatures.cs" })
        {
            string features = Source(name);
            Assert.Contains("int slot = game.IsGameplayScene() ? game.profileID : 0;", features, StringComparison.Ordinal);
            Assert.Contains("deathTally.Tick(player, slot,", features, StringComparison.Ordinal);
            Assert.DoesNotContain("deathTally.Tick(player, player.profileID", features, StringComparison.Ordinal);
        }
    }

    static string Source(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "source-contracts", name));
}
