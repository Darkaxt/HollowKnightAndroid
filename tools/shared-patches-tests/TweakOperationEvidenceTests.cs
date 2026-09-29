using DualSouls.Mods;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakOperationEvidenceTests
{
    [Fact]
    public void ApplyEvidenceIsCorrelatedAndPublishedAfterTypedAdapterCompletion()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);

        TweakActionResult result = controller.Set("speed", "fast");

        Assert.True(result.Success);
        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.True(adapter.ApplyCompleted);
        Assert.Equal("test", evidence.GameId);
        Assert.Equal("MOD-RUN-SPEED", evidence.FeatureId);
        Assert.Equal("speed", evidence.RowId);
        Assert.Equal(TweakOperationKind.Apply, evidence.Operation);
        Assert.True(evidence.Success);
        Assert.Equal(TweakReadbackKind.Choice, evidence.Readback.Kind);
        Assert.Equal("fast", evidence.Readback.Value);
        Assert.Equal("", evidence.Error);
    }

    [Fact]
    public void SuccessfulApplyWithoutAdapterReadbackReportsReadbackUnavailableWithoutEchoingIntent()
    {
        var adapter = new EvidenceAdapter { SuppressReadback = true };
        var controller = NewController(adapter);
        var menu = new TweakMenuModel(controller, visibleRows: 2);

        Assert.True(menu.SetSelected("fast").Success);

        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal(TweakReadbackKind.None, evidence.Readback.Kind);
        Assert.False(evidence.Readback.HasValue);
        Assert.Contains("readback unavailable", menu.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAST", menu.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandAndRouteEvidenceUsesTheActualControlOperation()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);

        Assert.True(controller.Set("route", "open").Success);
        Assert.True(controller.Set("command", "run").Success);

        Assert.Collection(
            controller.OperationEvidence,
            route =>
            {
                Assert.Equal(TweakOperationKind.Route, route.Operation);
                Assert.Equal("MOD-ROUTE", route.FeatureId);
                Assert.Equal(TweakReadbackKind.Text, route.Readback.Kind);
                Assert.Equal("opened", route.Readback.Value);
            },
            command =>
            {
                Assert.Equal(TweakOperationKind.Command, command.Operation);
                Assert.Equal("MOD-COMMAND", command.FeatureId);
                Assert.Equal(TweakReadbackKind.Integer, command.Readback.Kind);
                Assert.Equal("3", command.Readback.Value);
            });
    }

    [Theory]
    [InlineData("hollow-knight", "lifeblood_flash", "MOD-HK-LIFEBLOOD-FLASH")]
    [InlineData("silksong", "instant_dialogue", "MOD-SS-INSTANT-DIALOGUE")]
    [InlineData("silksong", "disable_world_rumble", "MOD-SS-DISABLE-WORLD-RUMBLE")]
    [InlineData("silksong", "ignore_frost_slowdown", "MOD-SS-IGNORE-FROST-SLOWDOWN")]
    public void ProfileExclusiveFeaturesUseTheirAuthoritativeIds(
        string gameId,
        string contractId,
        string expectedFeatureId)
    {
        var controller = NewController(new FeatureIdAdapter(gameId, contractId));

        Assert.True(controller.Set("feature", "on").Success);

        Assert.Equal(expectedFeatureId, Assert.Single(controller.OperationEvidence).FeatureId);
    }

    [Fact]
    public void FailedApplyEvidenceWaitsForFailClosedBaselineRestoreAndBlocksFurtherMutations()
    {
        var adapter = new EvidenceAdapter { FailApplyId = "speed" };
        var controller = NewController(adapter);

        TweakActionResult result = controller.Set("speed", "fast");

        Assert.False(result.Success);
        Assert.True(adapter.RestoreCompleted);
        Assert.False(controller.MutationsAvailable);
        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.False(evidence.Success);
        Assert.Equal(TweakOperationKind.Apply, evidence.Operation);
        Assert.Contains("apply failed", evidence.Error);
        Assert.False(controller.Set("flag", "on").Success);
    }

    [Fact]
    public void ResetRestoresRuntimeBaselineDefaultsAndEmitsOneOutcomePerChoice()
    {
        var adapter = new EvidenceAdapter();
        var store = new MemoryStore();
        var controller = NewController(adapter, store);
        Assert.True(controller.Set("speed", "fast").Success);
        Assert.True(controller.Set("flag", "on").Success);
        controller.ClearOperationEvidence();

        TweakActionResult result = controller.Reset();

        Assert.True(result.Success);
        Assert.True(adapter.RestoreCompleted);
        Assert.True(controller.MutationsAvailable);
        Assert.Equal("normal", controller.Value("speed"));
        Assert.Equal("off", controller.Value("flag"));
        Assert.Equal("normal", store["dualsouls.mods.test.value.speed"]);
        Assert.Equal("off", store["dualsouls.mods.test.value.flag"]);
        Assert.Collection(
            controller.OperationEvidence,
            speed => AssertReset(speed, "speed", "normal"),
            flag => AssertReset(flag, "flag", "off"));
    }

    [Fact]
    public void ResetReadbackMismatchFailsBeforeDefaultsArePersisted()
    {
        var adapter = new EvidenceAdapter();
        var store = new MemoryStore();
        var controller = NewController(adapter, store);
        Assert.True(controller.Set("speed", "fast").Success);
        controller.ClearOperationEvidence();
        adapter.IncorrectResetReadbackId = "speed";

        TweakActionResult result = controller.Reset();

        Assert.False(result.Success);
        Assert.False(controller.MutationsAvailable);
        Assert.Equal("fast", controller.Value("speed"));
        Assert.Equal("fast", store["dualsouls.mods.test.value.speed"]);
        TweakOperationEvidence speed = Assert.Single(
            controller.OperationEvidence, item => item.RowId == "speed");
        Assert.False(speed.Success);
        Assert.Contains("default", speed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetRecoversMutationAvailabilityAfterSynchronousFailure()
    {
        var adapter = new EvidenceAdapter { FailApplyId = "speed" };
        var controller = NewController(adapter);
        Assert.False(controller.Set("speed", "fast").Success);
        adapter.FailApplyId = null;
        controller.ClearOperationEvidence();

        Assert.True(controller.Reset().Success);

        Assert.True(controller.MutationsAvailable);
        Assert.True(controller.Set("flag", "on").Success);
    }

    [Fact]
    public void DeferredChoicePersistsAndPublishesOnlyAfterItsTypedTickReadback()
    {
        var adapter = new DeferredEvidenceAdapter();
        var store = new MemoryStore();
        var controller = NewController(adapter, store);

        TweakActionResult pending = controller.Set("speed", "fast");

        Assert.True(pending.Success);
        Assert.True(pending.Pending);
        Assert.Equal("fast", controller.Value("speed"));
        Assert.False(store.ContainsKey("dualsouls.mods.test.value.speed"));
        Assert.Empty(controller.OperationEvidence);

        controller.Tick();

        Assert.Equal("fast", store["dualsouls.mods.test.value.speed"]);
        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal("MOD-RUN-SPEED", evidence.FeatureId);
        Assert.Equal("speed", evidence.RowId);
        Assert.Equal(TweakOperationKind.Deferred, evidence.Operation);
        Assert.True(evidence.Success);
        Assert.Equal(TweakReadbackKind.Choice, evidence.Readback.Kind);
        Assert.Equal("fast", evidence.Readback.Value);
    }

    [Fact]
    public void DeferredChoiceWithoutTypedReadbackFailsClosedAndNeverPersists()
    {
        var adapter = new DeferredEvidenceAdapter { SuppressCompletionReadback = true };
        var store = new MemoryStore();
        var controller = NewController(adapter, store);
        Assert.True(controller.Set("speed", "fast").Pending);

        controller.Tick();

        Assert.False(store.ContainsKey("dualsouls.mods.test.value.speed"));
        Assert.Equal("normal", controller.Value("speed"));
        Assert.False(controller.MutationsAvailable);
        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal("speed", evidence.RowId);
        Assert.Equal(TweakOperationKind.Deferred, evidence.Operation);
        Assert.False(evidence.Success);
        Assert.Contains("readback", evidence.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeferredFailureRetainsTheOriginatingFeatureInsteadOfGenericRuntimeEvidence()
    {
        var adapter = new DeferredEvidenceAdapter { FailTick = true };
        var controller = NewController(adapter);
        Assert.True(controller.Set("speed", "fast").Pending);

        controller.Tick();

        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal("MOD-RUN-SPEED", evidence.FeatureId);
        Assert.Equal("speed", evidence.RowId);
        Assert.Equal(TweakOperationKind.Deferred, evidence.Operation);
        Assert.False(evidence.Success);
        Assert.Contains("deferred failed", evidence.Error);
        Assert.False(controller.MutationsAvailable);
    }

    [Fact]
    public void OneDeferredChoiceFailureCancelsSiblingChoiceCompletionsAfterBaselineRestore()
    {
        var store = new MemoryStore();
        var controller = NewController(new MultiDeferredEvidenceAdapter(), store);
        Assert.True(controller.Set("speed", "fast").Pending);
        Assert.True(controller.Set("flag", "on").Pending);

        controller.Tick();

        Assert.False(store.ContainsKey("dualsouls.mods.test.value.speed"));
        Assert.False(store.ContainsKey("dualsouls.mods.test.value.flag"));
        Assert.Equal("normal", controller.Value("speed"));
        Assert.Equal("off", controller.Value("flag"));
        Assert.False(controller.MutationsAvailable);
        Assert.Collection(
            controller.OperationEvidence,
            speed =>
            {
                Assert.Equal("speed", speed.RowId);
                Assert.False(speed.Success);
            },
            flag =>
            {
                Assert.Equal("flag", flag.RowId);
                Assert.False(flag.Success);
                Assert.Contains("canceled", flag.Error, StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public void DeferredChoiceFailureCancelsAcceptedAsyncCommandsAfterBaselineRestore()
    {
        var controller = NewController(new MixedDeferredEvidenceAdapter());
        TweakActionResult load = controller.Set("load", "run");
        Assert.True(controller.Set("speed", "fast").Pending);

        controller.Tick();

        TweakOperationEvidence canceled = Assert.Single(
            controller.OperationEvidence,
            evidence => evidence.RowId == "load");
        Assert.Equal(TweakOperationKind.Command, canceled.Operation);
        Assert.False(canceled.Success);
        Assert.Contains("canceled", canceled.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(controller.CompletePending(
            "load",
            load.OperationToken,
            TweakActionResult.Ok(TweakReadback.Text("late"))).Success);
    }

    [Fact]
    public void RouteProducesNoSuccessUntilTheUnderlyingDestinationOperationCompletes()
    {
        var adapter = new DeferredEvidenceAdapter();
        var controller = NewController(adapter);

        TweakActionResult pending = controller.Set("route", "open");

        Assert.True(pending.Pending);
        Assert.Empty(controller.OperationEvidence);

        Assert.True(controller.CompletePending(
            "route",
            pending.OperationToken,
            TweakActionResult.Ok(TweakReadback.Text("bone-bottom"))).Success);

        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal(TweakOperationKind.Route, evidence.Operation);
        Assert.Equal("bone-bottom", evidence.Readback.Value);
        Assert.False(controller.CompletePending(
            "route",
            pending.OperationToken,
            TweakActionResult.Ok(TweakReadback.Text("stale"))).Success);
        Assert.Single(controller.OperationEvidence);
    }

    [Fact]
    public void StaleCompletionTokenCannotResolveANewerOperationForTheSameRow()
    {
        var adapter = new TokenDeferredEvidenceAdapter();
        var controller = NewController(adapter);

        TweakActionResult first = controller.Set("route", "open");
        Assert.False(controller.CompletePending(
            "route", first.OperationToken, TweakActionResult.Fail("first attempt failed")).Success);
        controller.ClearOperationEvidence();

        TweakActionResult second = controller.Set("route", "open");
        Assert.NotEqual(first.OperationToken, second.OperationToken);

        Assert.False(controller.CompletePending(
            "route", first.OperationToken, TweakActionResult.Ok(TweakReadback.Text("stale"))).Success);
        Assert.Empty(controller.OperationEvidence);
        Assert.True(controller.CompletePending(
            "route", second.OperationToken, TweakActionResult.Ok(TweakReadback.Text("current"))).Success);
        Assert.Equal("current", Assert.Single(controller.OperationEvidence).Readback.Value);
    }

    [Fact]
    public void BaselineResetCancelsAcceptedDeferredOperationWithCorrelatedEvidence()
    {
        var controller = NewController(new TokenDeferredEvidenceAdapter());
        TweakActionResult pending = controller.Set("route", "open");

        Assert.True(controller.Reset().Success);

        TweakOperationEvidence canceled = Assert.Single(
            controller.OperationEvidence,
            evidence => evidence.RowId == "route");
        Assert.Equal(TweakOperationKind.Route, canceled.Operation);
        Assert.False(canceled.Success);
        Assert.Contains("canceled", canceled.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(controller.CompletePending(
            "route",
            pending.OperationToken,
            TweakActionResult.Ok(TweakReadback.Text("late"))).Success);
    }

    [Fact]
    public void DeferredOperationWatchdogPublishesCorrelatedTimeoutAndCancellation()
    {
        var operation = new TweakDeferredOperation("load_from_slot", 10f);
        operation.Begin(41, 100f);

        Assert.Null(operation.Poll(109.99f, "load timed out"));
        TweakAdapterCompletion timeout = Assert.IsType<TweakAdapterCompletion>(
            operation.Poll(110f, "load timed out"));
        Assert.Equal(41, timeout.OperationToken);
        Assert.False(timeout.Result.Success);
        Assert.Contains("timed out", timeout.Result.Error);

        operation.Begin(42, 200f);
        TweakAdapterCompletion canceled = Assert.IsType<TweakAdapterCompletion>(
            operation.Cancel("load canceled"));
        Assert.Equal(42, canceled.OperationToken);
        Assert.False(canceled.Result.Success);
        Assert.Null(operation.Complete(42, TweakActionResult.Ok()));
    }

    [Fact]
    public void CanceledRouteIsRecordedAsItsOwnCorrelatedFailureNeverAsSuccess()
    {
        var controller = NewController(new DeferredEvidenceAdapter());
        TweakActionResult pending = controller.Set("route", "open");
        Assert.True(pending.Pending);

        Assert.False(controller.CompletePending(
            "route",
            pending.OperationToken,
            TweakActionResult.Fail("Bench Teleport was canceled.")).Success);

        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal("MOD-BENCH-TELEPORT", evidence.FeatureId);
        Assert.Equal(TweakOperationKind.Route, evidence.Operation);
        Assert.False(evidence.Success);
        Assert.Contains("canceled", evidence.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetEvidenceComesFromPerSettingAdapterReadbackNotDescriptorDefaults()
    {
        var adapter = new DeferredEvidenceAdapter();
        var controller = NewController(adapter);
        controller.ClearOperationEvidence();

        Assert.True(controller.Reset().Success);

        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal("speed", evidence.RowId);
        Assert.Equal(TweakOperationKind.Reset, evidence.Operation);
        Assert.Equal(TweakReadbackKind.Choice, evidence.Readback.Kind);
        Assert.Equal("normal", evidence.Readback.Value);
        Assert.Equal(new[] { "speed" }, adapter.ReadbackRequests);
    }

    [Fact]
    public void OpeningMenuRejectsInitializationEvidenceAsStale()
    {
        var store = new MemoryStore
        {
            ["dualsouls.mods.test.value.speed"] = "fast",
        };
        var controller = new TweakController(new EvidenceAdapter(), store);
        Assert.True(controller.Initialize().Success);
        Assert.Single(controller.OperationEvidence);
        var menu = new TweakMenuModel(controller, visibleRows: 2);

        menu.Open();

        Assert.False(menu.RefreshOperationMessage());
        Assert.Equal("", menu.Message);
    }

    [Fact]
    public void PendingRouteMessageCannotBeOverwrittenByEarlierChoiceEvidence()
    {
        var controller = NewController(new DeferredEvidenceAdapter());
        Assert.True(controller.Set("speed", "fast").Pending);
        controller.Tick();
        Assert.Single(controller.OperationEvidence);
        var menu = new TweakMenuModel(controller, visibleRows: 2);
        menu.MoveRow(1);

        TweakActionResult pending = menu.ActivateSelected();

        Assert.True(pending.Pending);
        Assert.Equal("Runtime operation pending.", menu.Message);
    }

    [Fact]
    public void MenuReportsPendingUntilTheDeferredReadbackCompletes()
    {
        var adapter = new DeferredEvidenceAdapter();
        var controller = NewController(adapter);
        var menu = new TweakMenuModel(controller, visibleRows: 2);

        TweakActionResult pending = menu.SetSelected("fast");

        Assert.True(pending.Pending);
        Assert.Contains("pending", menu.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("saved", menu.Message, StringComparison.OrdinalIgnoreCase);

        controller.Tick();
        Assert.True(menu.RefreshOperationMessage());
        Assert.Equal("SPEED: FAST.", menu.Message);
    }

    [Fact]
    public void DeferredFailureIsReportedOnlyWhenTickResolvesItAndFailsClosed()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);

        controller.Tick();
        Assert.Empty(controller.OperationEvidence);

        adapter.ThrowOnTick = true;
        controller.Tick();

        TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal("MOD-TRANSACTION-SAFETY", evidence.FeatureId);
        Assert.Equal("runtime", evidence.RowId);
        Assert.Equal(TweakOperationKind.Deferred, evidence.Operation);
        Assert.False(evidence.Success);
        Assert.Contains("deferred failed", evidence.Error);
        Assert.True(adapter.RestoreCompleted);
        Assert.False(controller.MutationsAvailable);
    }

    [Fact]
    public void MenuSurfacesDeferredFailureOnlyAfterItResolves()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);
        var menu = new TweakMenuModel(controller, visibleRows: 2);

        controller.Tick();
        Assert.False(menu.RefreshOperationMessage());
        Assert.Equal("", menu.Message);

        adapter.ThrowOnTick = true;
        controller.Tick();

        Assert.True(menu.RefreshOperationMessage());
        Assert.True(menu.MessageIsError);
        Assert.Contains("deferred failed", menu.Message);
    }

    [Fact]
    public void MenuConsumesOnlyTheLatestOperationAndDoesNotReplayItAfterNavigation()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);
        var menu = new TweakMenuModel(controller, visibleRows: 2);

        Assert.True(controller.Set("speed", "fast").Success);
        Assert.True(controller.Set("speed", "normal").Success);

        Assert.True(menu.RefreshOperationMessage());
        Assert.False(menu.MessageIsError);
        Assert.Equal("SPEED: NORMAL.", menu.Message);

        menu.MoveGroup(1);
        Assert.Equal("", menu.Message);
        Assert.False(menu.RefreshOperationMessage());
        Assert.Equal("", menu.Message);
    }

    [Fact]
    public void MenuUsesCompletedTypedEvidenceInsteadOfAGenericImmediateSuccess()
    {
        var controller = NewController(new EvidenceAdapter());
        var menu = new TweakMenuModel(controller, visibleRows: 2);

        Assert.True(menu.SetSelected("fast").Success);

        Assert.Equal("SPEED: FAST.", menu.Message);
        Assert.False(menu.MessageIsError);
    }

    [Fact]
    public void DismissingAnOperationMessageDoesNotReplayConsumedEvidence()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);
        var menu = new TweakMenuModel(controller, visibleRows: 2);
        Assert.True(menu.SetSelected("fast").Success);
        Assert.NotEqual("", menu.Message);

        menu.DismissMessage();

        Assert.Equal("", menu.Message);
        Assert.False(menu.RefreshOperationMessage());
    }

    [Fact]
    public void MenuDoesNotReplayConsumedOperationAfterCloseAndReopen()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);
        var menu = new TweakMenuModel(controller, visibleRows: 2);
        menu.Open();
        Assert.True(controller.Set("speed", "fast").Success);
        Assert.True(menu.RefreshOperationMessage());
        Assert.NotEqual("", menu.Message);

        menu.Close();
        menu.Open();

        Assert.Equal("", menu.Message);
        Assert.False(menu.RefreshOperationMessage());
    }

    [Theory]
    [InlineData("failed path=/home/person/private/state.txt", "person")]
    [InlineData(@"failed at \\server\person\private\state.txt", "person")]
    [InlineData(@"failed path=\Users\person\private\state.txt", "person")]
    [InlineData(@"failed at C:\Users\Jane Doe\state.txt", @"Doe\state.txt")]
    public void EvidenceRedactsAbsoluteAndNetworkPaths(string error, string privateSegment)
    {
        var adapter = new EvidenceAdapter
        {
            FailApplyId = "speed",
            FailureMessage = error,
        };
        var controller = NewController(adapter);

        Assert.False(controller.Set("speed", "fast").Success);

        TweakOperationEvidence failed = Assert.Single(controller.OperationEvidence);
        Assert.DoesNotContain(privateSegment, failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[path]", failed.Error);
    }

    [Fact]
    public void EvidenceTextNeverExceedsThePublishedBound()
    {
        var adapter = new EvidenceAdapter
        {
            FailApplyId = "speed",
            FailureMessage = new string('x', 237) + @" C:\Users\person\private.txt",
        };
        var controller = NewController(adapter);

        Assert.False(controller.Set("speed", "fast").Success);

        Assert.True(Assert.Single(controller.OperationEvidence).Error.Length <= 240);
    }

    [Fact]
    public void EvidenceIsBoundedSequencedAndRedactsPersonalPaths()
    {
        var adapter = new EvidenceAdapter();
        var controller = NewController(adapter);
        for (int i = 0; i < TweakController.MaximumOperationEvidence + 17; i++)
            Assert.True(controller.Set("flag", i % 2 == 0 ? "on" : "off").Success);

        Assert.Equal(TweakController.MaximumOperationEvidence, controller.OperationEvidence.Count);
        Assert.True(controller.OperationEvidence[0].Sequence > 1);
        Assert.Equal(
            controller.OperationEvidence[0].Sequence + controller.OperationEvidence.Count - 1,
            controller.OperationEvidence[^1].Sequence);

        adapter.FailApplyId = "speed";
        adapter.FailureMessage = @"failed at C:\Users\person\private\state.txt";
        controller.Reset();
        controller.ClearOperationEvidence();
        Assert.False(controller.Set("speed", "fast").Success);
        TweakOperationEvidence failed = Assert.Single(controller.OperationEvidence);
        Assert.DoesNotContain("person", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[path]", failed.Error);
    }

    static void AssertReset(TweakOperationEvidence evidence, string rowId, string value)
    {
        Assert.Equal(rowId, evidence.RowId);
        Assert.Equal(TweakOperationKind.Reset, evidence.Operation);
        Assert.True(evidence.Success);
        Assert.Equal(TweakReadbackKind.Choice, evidence.Readback.Kind);
        Assert.Equal(value, evidence.Readback.Value);
    }

    static TweakController NewController(ITweakAdapter adapter, MemoryStore store = null)
    {
        var controller = new TweakController(adapter, store ?? new MemoryStore());
        Assert.True(controller.Initialize().Success);
        controller.ClearOperationEvidence();
        return controller;
    }

    sealed class EvidenceAdapter : ITweakAdapter, ITweakOperationAdapter
    {
        public string GameId => "test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor("speed", "run_speed", TweakControlKind.Choice, "WORLD", "SPEED", "Choose speed.", "normal", new[] { "normal", "fast" }),
            new TweakDescriptor("flag", "flag", TweakControlKind.Choice, "WORLD", "FLAG", "Choose flag.", "off", new[] { "off", "on" }),
            new TweakDescriptor("route", "route", TweakControlKind.Route, "TOOLS", "ROUTE", "Open route.", "open", new[] { "open" }),
            new TweakDescriptor("command", "command", TweakControlKind.Command, "TOOLS", "COMMAND", "Run command.", "run", new[] { "run" }),
        };

        public string FailApplyId { get; set; }
        public string FailureMessage { get; set; } = "apply failed";
        public bool ThrowOnTick { get; set; }
        public bool SuppressReadback { get; set; }
        public string IncorrectResetReadbackId { get; set; }
        public bool ApplyCompleted { get; private set; }
        public bool RestoreCompleted { get; private set; }
        readonly Dictionary<string, string> _actual = new()
        {
            ["speed"] = "normal",
            ["flag"] = "off",
        };

        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value)
        {
            ApplyCompleted = true;
            if (id == FailApplyId) return TweakActionResult.Fail(FailureMessage);
            if (id == "route") return TweakActionResult.Ok(TweakReadback.Text("opened"));
            if (id == "command") return TweakActionResult.Ok(TweakReadback.Integer(3));
            _actual[id] = value;
            return SuppressReadback
                ? TweakActionResult.Ok()
                : TweakActionResult.Ok(TweakReadback.Choice(value));
        }
        public void RestoreBaseline()
        {
            RestoreCompleted = true;
            _actual["speed"] = "normal";
            _actual["flag"] = "off";
        }

        public TweakActionResult Readback(string id)
        {
            if (id == IncorrectResetReadbackId)
                return TweakActionResult.Ok(TweakReadback.Choice(
                    id == "speed" ? "fast" : "on"));
            return _actual.TryGetValue(id, out string value)
                ? TweakActionResult.Ok(TweakReadback.Choice(value))
                : TweakActionResult.Ok();
        }

        public IReadOnlyList<TweakAdapterCompletion> TickWithOutcomes()
        {
            Tick();
            return Array.Empty<TweakAdapterCompletion>();
        }

        public void Tick()
        {
            if (ThrowOnTick) throw new InvalidOperationException("deferred failed");
        }
    }

    sealed class MixedDeferredEvidenceAdapter : ITweakAdapter, ITweakOperationAdapter
    {
        long _nextToken;
        long _speedToken;
        bool _speedPending;

        public string GameId => "test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor("speed", "run_speed", TweakControlKind.Choice, "WORLD", "SPEED", "Choose speed.", "normal", new[] { "normal", "fast" }),
            new TweakDescriptor("load", "load_from_slot", TweakControlKind.Command, "SAVE STATES", "LOAD", "Load state.", "run", new[] { "run" }),
        };

        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value)
        {
            long token = ++_nextToken;
            if (id == "speed")
            {
                _speedToken = token;
                _speedPending = true;
            }
            return TweakActionResult.PendingResult(token);
        }
        public void RestoreBaseline() { _speedPending = false; }
        public TweakActionResult Readback(string id) =>
            TweakActionResult.Ok(TweakReadback.Choice("normal"));
        public void Tick() { }
        public IReadOnlyList<TweakAdapterCompletion> TickWithOutcomes()
        {
            if (!_speedPending) return Array.Empty<TweakAdapterCompletion>();
            _speedPending = false;
            return new[]
            {
                new TweakAdapterCompletion(
                    "speed",
                    _speedToken,
                    TweakActionResult.Fail("speed failed")),
            };
        }
    }

    sealed class MultiDeferredEvidenceAdapter : ITweakAdapter, ITweakOperationAdapter
    {
        bool _pending;
        long _nextToken;
        long _speedToken;
        long _flagToken;

        public string GameId => "test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor("speed", "run_speed", TweakControlKind.Choice, "WORLD", "SPEED", "Choose speed.", "normal", new[] { "normal", "fast" }),
            new TweakDescriptor("flag", "flag", TweakControlKind.Choice, "WORLD", "FLAG", "Choose flag.", "off", new[] { "off", "on" }),
        };

        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value)
        {
            _pending = true;
            long token = ++_nextToken;
            if (id == "speed") _speedToken = token;
            else _flagToken = token;
            return TweakActionResult.PendingResult(token);
        }
        public void RestoreBaseline() { _pending = false; }
        public TweakActionResult Readback(string id) => TweakActionResult.Ok(
            TweakReadback.Choice(id == "speed" ? "normal" : "off"));
        public void Tick() { }
        public IReadOnlyList<TweakAdapterCompletion> TickWithOutcomes()
        {
            if (!_pending) return Array.Empty<TweakAdapterCompletion>();
            _pending = false;
            return new[]
            {
                new TweakAdapterCompletion("speed", _speedToken, TweakActionResult.Fail("speed deferred failed")),
                new TweakAdapterCompletion("flag", _flagToken, TweakActionResult.Ok(TweakReadback.Choice("on"))),
            };
        }
    }

    sealed class DeferredEvidenceAdapter : ITweakAdapter, ITweakOperationAdapter
    {
        bool _speedPending;
        long _nextToken;
        long _speedToken;
        string _speed = "normal";

        public string GameId => "test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor("speed", "run_speed", TweakControlKind.Choice, "WORLD", "SPEED", "Choose speed.", "normal", new[] { "normal", "fast" }),
            new TweakDescriptor("route", "bench_teleport", TweakControlKind.Route, "WORLD", "BENCH TELEPORT", "Choose a bench.", "open", new[] { "open" }),
        };
        public bool FailTick { get; set; }
        public bool SuppressCompletionReadback { get; set; }
        public List<string> ReadbackRequests { get; } = new();

        public void CaptureBaseline() { }

        public TweakActionResult Apply(string id, string value)
        {
            long token = ++_nextToken;
            if (id == "speed")
            {
                _speed = value;
                _speedPending = true;
                _speedToken = token;
            }
            return TweakActionResult.PendingResult(token);
        }

        public void RestoreBaseline()
        {
            _speed = "normal";
            _speedPending = false;
        }

        public TweakActionResult Readback(string id)
        {
            ReadbackRequests.Add(id);
            return id == "speed"
                ? TweakActionResult.Ok(TweakReadback.Choice(_speed))
                : TweakActionResult.Fail("No safe readback is available for " + id + ".");
        }

        public void Tick() { }

        public IReadOnlyList<TweakAdapterCompletion> TickWithOutcomes()
        {
            if (FailTick) throw new InvalidOperationException("deferred failed");
            if (!_speedPending) return Array.Empty<TweakAdapterCompletion>();
            _speedPending = false;
            TweakActionResult completion = SuppressCompletionReadback
                ? TweakActionResult.Ok()
                : Readback("speed");
            return new[] { new TweakAdapterCompletion("speed", _speedToken, completion) };
        }
    }

    sealed class FeatureIdAdapter : ITweakAdapter
    {
        readonly string _gameId;
        readonly IReadOnlyList<TweakDescriptor> _descriptors;

        public FeatureIdAdapter(string gameId, string contractId)
        {
            _gameId = gameId;
            _descriptors = new[]
            {
                new TweakDescriptor("feature", contractId, TweakControlKind.Choice, "TEST", "FEATURE", "Test feature.", "off", new[] { "off", "on" }),
            };
        }

        public string GameId => _gameId;
        public IReadOnlyList<TweakDescriptor> Descriptors => _descriptors;
        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value) =>
            TweakActionResult.Ok(TweakReadback.Choice(value));
        public void RestoreBaseline() { }
        public void Tick() { }
    }

    sealed class TokenDeferredEvidenceAdapter : ITweakAdapter
    {
        long _nextToken;

        public string GameId => "test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor("route", "bench_teleport", TweakControlKind.Route, "WORLD", "BENCH TELEPORT", "Choose a bench.", "open", new[] { "open" }),
        };

        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value) =>
            TweakActionResult.PendingResult(++_nextToken);
        public void RestoreBaseline() { }
        public void Tick() { }
    }

    sealed class MemoryStore : Dictionary<string, string>, ITweakStore
    {
        public string Read(string key) => TryGetValue(key, out string value) ? value : null;
        public void Write(string key, string value) => this[key] = value;
        public void Flush() { }
    }
}
