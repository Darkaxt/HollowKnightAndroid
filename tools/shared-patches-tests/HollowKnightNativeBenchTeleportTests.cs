using DualSouls.Mods;
using Xunit;
using H = NativeMenuFixture.Hk.Engine;

namespace SharedPatches.Tests;

[Collection("Native menu production bodies")]
public sealed class HollowKnightNativeBenchTeleportTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UpperPausedBenchChooserWorksWithLowerCompanionAbsentOrSuppressed(bool absent)
    {
        var f = new NativeMenuFixture(false); f.Record("Room_B", "real-marker", 3, false); f.Bind(); f.Open();
        H.World.OpenLowerAvailable = !absent; H.World.LowerSuppressed = !absent;
        int writes = f.Store.Writes; f.SelectTweak("bench_teleport"); f.Call("Submit", f.Row());
        H.MenuScreen screen = (H.MenuScreen)f.Field("_modsScreen");
        Assert.Equal("BENCH TELEPORT", screen.title.GetComponent<H.UI.Text>().text);
        Assert.True((bool)f.Field("_benchOpen"));
        Assert.Equal(0, H.World.LowerOpened);
        Assert.Equal(writes, f.Store.Writes);
        Assert.Equal(0, H.World.BenchWrites);
        f.Call("Submit", f.Row(0));
        Assert.Equal(1, H.GameManager.UnsafeInstance.playerData.RespawnCalls);
        Assert.Equal(new object[] { "real-marker", "Room_B", 3, false }, H.GameManager.UnsafeInstance.playerData.LastRespawn);
        Assert.Equal(1, H.GameManager.UnsafeInstance.ReadyCalls); Assert.False(H.GameManager.UnsafeInstance.LastReady.Value);
        f.Tick();
        Assert.True(f.Controller.TryGetLatestOperationEvidence(out var evidence));
        Assert.Equal("Room_B", evidence.Readback.Value);
    }
    [Fact]
    public void ReentrantDuplicateUpperWarpCannotCallTypedAuthorityTwice()
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open();
        f.SelectTweak("bench_teleport"); f.Call("Submit", f.Row());
        long token = (long)f.Field("_benchOperationToken"); int generation = (int)f.Field("_benchGeneration");
        var player = H.GameManager.UnsafeInstance.playerData;
        player.BeforeRespawn = () => { player.BeforeRespawn = null; H.HkStageHooks.BenchWarp("Room_A", token, generation); };
        f.Call("Submit", f.Row(0));
        Assert.Equal(1, player.RespawnCalls); Assert.Equal(1, H.GameManager.UnsafeInstance.ReadyCalls);
        Assert.Single(f.HkApi.DrainCompletedOperations());
    }
    static (long Token, int Generation) Begin(NativeMenuFixture f)
    {
        f.SelectTweak("bench_teleport"); f.Controller.ClearOperationEvidence(); f.Call("Submit", f.Row());
        Assert.True((bool)f.Field("_benchOpen"));
        return ((long)f.Field("_benchOperationToken"), (int)f.Field("_benchGeneration"));
    }
    static void AssertTerminalFailure(NativeMenuFixture f)
    {
        f.Tick();
        var evidence = f.Controller.OperationEvidence.Where(e => e.RowId == "bench_teleport").ToArray();
        Assert.Single(evidence); Assert.False(evidence[0].Success); Assert.Equal("hollow-knight", evidence[0].GameId);
        Assert.Null(NativeMenuFixture.GetStatic(typeof(H.HkStageHooks), "pendingBenchCompletion"));
        Assert.False((bool)f.Field("_benchOpen"));
        Assert.Equal(0, H.World.BenchWrites);
    }
    [Fact]
    public void RecordedSnapshotIsOrdinalBoundedReadonlyAndDoesNotWriteRecords()
    {
        var f = new NativeMenuFixture(false);
        for (int i = 299; i >= 0; i--) f.Record("Room_" + i.ToString("D3"));
        f.Record("bad-record", marker: null);
        var scenes = H.HkStageHooks.RecordedBenchScenes();
        Assert.Equal(256, scenes.Count); Assert.Equal("Room_000", scenes[0]); Assert.Equal("Room_255", scenes[255]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)scenes).Add("fabricated"));
        Assert.Equal(0, H.World.BenchWrites); Assert.Equal(0, H.GameManager.UnsafeInstance.playerData.RespawnCalls);
    }
    [Fact]
    public void EmptyRecordedListHasReadableTruthfulMessageBackAndNoPendingAuthority()
    {
        var f = new NativeMenuFixture(false); f.Bind(); f.Open(); Begin(f);
        Assert.Null(NativeMenuFixture.GetStatic(typeof(H.HkStageHooks), "pendingBenchCompletion"));
        var screen = (H.MenuScreen)f.Field("_modsScreen");
        Assert.Equal("BENCH TELEPORT", screen.title.GetComponent<H.UI.Text>().text);
        H.World.Cancel(((H.HollowKnightNativeModsButton)f.Button(TweakMenuPresenterLayout.BackButtonIndex)).gameObject);
        AssertTerminalFailure(f); Assert.True(f.Menu.IsOpen);
        Assert.True(((H.HollowKnightNativeModsButton)f.Button(TweakMenuPresenterLayout.GroupButtonIndex)).gameObject.activeInHierarchy);
    }
    [Theory]
    [InlineData("removed")] [InlineData("missing-game")] [InlineData("missing-player")]
    [InlineData("respawn-exception")] [InlineData("ready-exception")]
    public void InvalidDestinationAndTypedDependenciesRetireOnceWithoutRecordWrites(string failure)
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open(); Begin(f);
        int writes = f.Store.Writes;
        var game = H.GameManager.UnsafeInstance; var player = game.playerData;
        if (failure == "removed") ((System.Collections.IDictionary)NativeMenuFixture.GetStatic(typeof(H.HkStageHooks), "benches")).Clear();
        if (failure == "missing-game") H.GameManager.UnsafeInstance = null;
        if (failure == "missing-player") game.playerData = null;
        if (failure == "respawn-exception") player.ThrowRespawn = true;
        if (failure == "ready-exception") game.ThrowReady = true;
        f.Call("Submit", f.Row(0));
        Assert.Equal(failure.EndsWith("exception") ? 1 : 0, player.RespawnCalls);
        Assert.Equal(failure == "ready-exception" ? 1 : 0, game.ReadyCalls);
        AssertTerminalFailure(f); Assert.Equal(writes, f.Store.Writes); Assert.True(f.Menu.IsOpen);
        Assert.NotNull(H.EventSystems.EventSystem.current.currentSelectedGameObject);
    }
    [Theory]
    [InlineData("game-b")] [InlineData("back")] [InlineData("outer-close")]
    [InlineData("pause-loss")] [InlineData("readiness-loss")] [InlineData("rebind")] [InlineData("destroy")]
    public void EveryChooserClosureRetiresAuthorityAndStaleEventsCannotWarp(string close)
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open(); var request = Begin(f);
        var stale = (H.HollowKnightNativeModsButton)f.Row(0);
        int writes = f.Store.Writes;
        if (close == "game-b") H.World.Cancel(stale.gameObject);
        if (close == "back") f.Call("Submit", f.Button(TweakMenuPresenterLayout.BackButtonIndex));
        if (close == "outer-close") { f.Call("Close"); H.World.Pump(); }
        if (close == "pause-loss") { ((H.UIManager)f.Manager).uiState = H.GlobalEnums.UIState.PLAYING; f.Call("Update"); }
        if (close == "readiness-loss") ((DualSouls.Mods.HollowKnight.HollowKnightModsSession)f.Session).Dispose();
        if (close == "rebind") f.Call("CancelAndClearBinding");
        if (close == "destroy") f.Call("OnDestroy");
        if (close == "game-b" || close == "back") {
            // The row control is reused by the ordinary menu. A new input on that
            // current control has no old request tuple and is not a stale callback.
            // First verify nested cancel restored usable ordinary controls without
            // writes, then retire its generation before replaying the old handler.
            Assert.False((bool)f.Field("_benchOpen")); Assert.True(f.Menu.IsOpen);
            Assert.Equal(writes, f.Store.Writes);
            Assert.True(((H.HollowKnightNativeModsButton)f.Button(TweakMenuPresenterLayout.GroupButtonIndex)).gameObject.activeInHierarchy);
            f.Call("CancelAndClearBinding");
        }
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", request.Token, request.Generation));
        H.HkStageHooks.CancelBenchChoice(request.Token, request.Generation, "stale cancel");
        ((H.EventSystems.ISubmitHandler)stale).OnSubmit(new H.EventSystems.BaseEventData());
        f.Tick();
        Assert.Null(NativeMenuFixture.GetStatic(typeof(H.HkStageHooks), "pendingBenchCompletion"));
        Assert.Equal(0, H.GameManager.UnsafeInstance.playerData.RespawnCalls);
        Assert.Equal(0, H.World.BenchWrites); Assert.Equal(writes, f.Store.Writes);
        Assert.False((bool)f.Field("_benchOpen"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TimeoutAndResetRetireExactRequestAndCannotCancelRetry(bool reset)
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open(); var old = Begin(f);
        if (reset) f.Menu.Reset(); else { H.Time.unscaledTime = 121f; f.Tick(); }
        Assert.False((bool)f.Field("_benchOpen"));
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", old.Token, old.Generation));
        var current = Begin(f);
        Assert.NotEqual(old.Token, current.Token);
        H.HkStageHooks.CancelBenchChoice(old.Token, old.Generation, "late close");
        H.HkStageHooks.CancelBenchTeleport(old.Token, old.Generation);
        H.HollowKnightNativeModsMenu.RetireBenchTeleportRoute(old.Token, old.Generation);
        Assert.True(H.HkStageHooks.BenchOperationMatches(current.Token, current.Generation));
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", old.Token, old.Generation));
        f.Call("Submit", f.Row(0)); f.Tick();
        Assert.Single(f.Controller.OperationEvidence.Where(e => e.RowId == "bench_teleport"));
        Assert.True(f.Controller.OperationEvidence.Single(e => e.RowId == "bench_teleport").Success);
        Assert.Equal(1, H.GameManager.UnsafeInstance.playerData.RespawnCalls);
    }
    [Fact]
    public void DeadlineRejectsSelectionEvenBeforeApiPollingAndDoubleSelectionCannotWarp()
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open(); var request = Begin(f);
        H.Time.unscaledTime = 121f;
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", request.Token, request.Generation));
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", request.Token, request.Generation));
        AssertTerminalFailure(f); Assert.Equal(0, H.GameManager.UnsafeInstance.playerData.RespawnCalls);
    }
    [Fact]
    public void OpenExceptionDoesNotLeaveDeferredOrChooserAuthority()
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open();
        f.SelectTweak("bench_teleport"); f.Controller.ClearOperationEvidence();
        H.World.FailOnPoint = "List.AddRange.before:String";
        f.Call("Submit", f.Row());
        Assert.Equal(new[] { "List.AddRange.before:String" }, H.World.FiredFailures);
        AssertTerminalFailure(f); Assert.Equal(0, H.GameManager.UnsafeInstance.playerData.RespawnCalls);
        Assert.True(f.Menu.IsOpen);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RetiredTypedWarpCannotCloseOrSettleAReentrantSuccessor(bool nativeFailure)
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open(); var old = Begin(f);
        var player = H.GameManager.UnsafeInstance.playerData;
        (long Token, int Generation) successor = default;
        player.BeforeRespawn = () => {
            player.BeforeRespawn = null;
            f.Menu.Reset(); f.Tick();
            f.Receipt("reentrant-reset-" + nativeFailure, new {
                token = old.Token, generation = old.Generation,
                chooserOpenAfterReset = (bool)f.Field("_benchOpen"),
                stagePendingAfterReset = H.HkStageHooks.BenchOperationMatches(old.Token, old.Generation)
            });
            successor = Begin(f);
            player.ThrowRespawn = nativeFailure;
        };
        f.Call("Submit", f.Row(0));
        Assert.NotEqual(old.Token, successor.Token);
        Assert.True((bool)f.Field("_benchOpen"));
        Assert.True(H.HkStageHooks.BenchOperationMatches(successor.Token, successor.Generation));
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", old.Token, old.Generation));
        H.HkStageHooks.CancelBenchChoice(old.Token, old.Generation, "late predecessor");
        Assert.Empty(f.HkApi.DrainCompletedOperations());
        player.ThrowRespawn = false;
        f.Call("Submit", f.Row(0));
        var queued = (Queue<TweakAdapterCompletion>)f.HkApi.GetType().GetField("_completedOperations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f.HkApi);
        var completion = Assert.Single(queued);
        Assert.Equal(successor.Token, completion.OperationToken);
        f.Tick();
        Assert.Equal(2, player.RespawnCalls);
        Assert.Equal(nativeFailure ? 1 : 2, H.GameManager.UnsafeInstance.ReadyCalls);
        var evidence = Assert.Single(f.Controller.OperationEvidence, e => e.RowId == "bench_teleport");
        Assert.True(evidence.Success);
        Assert.Equal("hollow-knight", evidence.GameId); Assert.Equal("Room_A", evidence.Readback.Value);
    }
    [Fact]
    public void IndependentLowerMapWarpCannotCompleteAnUpperChooserOperation()
    {
        var f = new NativeMenuFixture(false); f.Record("Room_A"); f.Bind(); f.Open();
        f.SelectTweak("bench_teleport"); f.Call("Submit", f.Row());
        H.HkStageHooks.BenchWarp("Room_A");
        Assert.Empty(f.HkApi.DrainCompletedOperations());
    }
}
