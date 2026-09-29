using DualSouls.Mods;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void UnavailableRequiresNonblankReason(string unavailableReason)
    {
        Assert.Throws<ArgumentException>(() => TweakDescriptor.Unavailable(
            "bench_teleport", "bench_teleport", TweakControlKind.Route,
            "WORLD", "BENCH TELEPORT", "Open recorded benches.",
            "open", new[] { "open" }, unavailableReason));
    }

    [Fact]
    public void DescriptorCopiesConstructorValuesBeforeExposingThem()
    {
        string[] inputValues = { "off", "on" };
        var descriptor = new TweakDescriptor(
            "example", "TEST", "EXAMPLE", "Example descriptor.", "off", inputValues);

        inputValues[0] = "corrupt";
        inputValues[1] = "also_corrupt";

        Assert.Equal(new[] { "off", "on" }, descriptor.Values);
        Assert.Equal("off", descriptor.DefaultValue);
        Assert.True(descriptor.Allows("off"));
        Assert.False(descriptor.Allows("corrupt"));
    }

    [Fact]
    public void DescriptorValuesRejectListMutationAndPreserveDefaultInvariant()
    {
        var descriptor = new TweakDescriptor(
            "example", "TEST", "EXAMPLE", "Example descriptor.",
            "off", new[] { "off", "on" });
        var values = Assert.IsAssignableFrom<IList<string>>(descriptor.Values);

        Exception mutationError = Record.Exception(() => values[0] = "corrupt");

        Assert.IsType<NotSupportedException>(mutationError);
        Assert.Equal(new[] { "off", "on" }, descriptor.Values);
        Assert.Equal("off", descriptor.DefaultValue);
        Assert.True(descriptor.Allows(descriptor.DefaultValue));
        Assert.False(descriptor.Allows("corrupt"));
    }

    [Fact]
    public void UnavailableRetainsItsHonestControlMetadata()
    {
        var descriptor = TweakDescriptor.Unavailable(
            "bench_teleport", "bench_teleport", TweakControlKind.Route,
            "WORLD", "BENCH TELEPORT", "Open recorded benches.",
            "open", new[] { "open" }, "No adapter route exists yet.");

        Assert.False(descriptor.IsAvailable);
        Assert.Equal(TweakControlKind.Route, descriptor.ControlKind);
        Assert.Equal("open", descriptor.DefaultValue);
        Assert.Equal(new[] { "open" }, descriptor.Values);
        Assert.Equal("No adapter route exists yet.", descriptor.UnavailableReason);
    }

    [Fact]
    public void UnavailableChoicesPreserveCompatibleStateSkipApplyAndRejectMutation()
    {
        var adapter = new UnavailableRecordingAdapter();
        var store = new MemoryStore
        {
            ["dualsouls.mods.hollow-knight.value.secret_radar"] = "on"
        };
        var controller = new TweakController(adapter, store);

        var initialized = controller.Initialize();
        var changed = controller.Set("secret_radar", "off");

        Assert.True(initialized.Success);
        Assert.True(controller.MutationsAvailable);
        Assert.Equal("on", controller.Value("secret_radar"));
        Assert.Equal("on", store["dualsouls.mods.hollow-knight.value.secret_radar"]);
        Assert.Empty(adapter.Applied);
        Assert.False(changed.Success);
        Assert.Contains("currently unavailable", changed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No adapter operation exists yet.", changed.Error);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void InitializeCapturesBaselineAndMakesDirectMutationsAvailable()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);

        var result = controller.Initialize();

        Assert.True(result.Success);
        Assert.True(controller.MutationsAvailable);
        Assert.Equal("vanilla", controller.Value("damage_received"));
        Assert.Equal("off", controller.Value("unlimited_silk"));
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Empty(adapter.Applied);
        Assert.Empty(store);
    }

    [Fact]
    public void InvalidPersistedValueFallsBackToDescriptorDefault()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.value.damage_received"] = "corrupt"
        };

        var controller = new TweakController(adapter, store);
        var result = controller.Initialize();

        Assert.True(result.Success);
        Assert.Equal("vanilla", controller.Value("damage_received"));
        Assert.Equal("vanilla", store["dualsouls.mods.silksong.value.damage_received"]);
        Assert.Equal(1, store.FlushCount);
    }

    [Fact]
    public void InitializeAppliesOnlyPersistedNonDefaults()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.value.damage_received"] = "prevent_death",
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "off"
        };

        var controller = new TweakController(adapter, store);
        var result = controller.Initialize();

        Assert.True(result.Success);
        Assert.True(controller.MutationsAvailable);
        Assert.Equal(new[] { ("damage_received", "prevent_death") }, adapter.Applied);
    }

    [Fact]
    public void ResetRestoresCapturedBaselineAndPersistsDescriptorDefaults()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        var result = controller.Reset();

        Assert.True(result.Success);
        Assert.True(controller.MutationsAvailable);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.Equal("vanilla", store["dualsouls.mods.silksong.value.damage_received"]);
        Assert.Equal("off", store["dualsouls.mods.silksong.value.unlimited_silk"]);
    }

    [Fact]
    public void FailedResetRestorationPublishesFreshAggregateRetryEvidenceUntilRecovery()
    {
        var adapter = new RecordingAdapter("silksong");
        var controller = new TweakController(adapter, new MemoryStore());
        Assert.True(controller.Initialize().Success);
        adapter.RestoreFailuresRemaining = 2;

        var failed = controller.Reset();

        Assert.False(failed.Success);
        Assert.True(controller.RestorationPending);
        Assert.False(controller.MutationsAvailable);
        controller.ClearOperationEvidence();

        var failedRetry = controller.RetryRestoration();

        Assert.False(failedRetry.Success);
        TweakOperationEvidence failedEvidence = Assert.Single(controller.OperationEvidence);
        Assert.Equal("MOD-TRANSACTION-SAFETY", failedEvidence.FeatureId);
        Assert.Equal(TweakOperationKind.BaselineRestore, failedEvidence.Operation);
        Assert.False(failedEvidence.Success);
        Assert.Contains("restore", failedEvidence.Error, StringComparison.OrdinalIgnoreCase);

        var retried = controller.RetryRestoration();

        Assert.True(retried.Success);
        Assert.False(controller.RestorationPending);
        Assert.False(controller.MutationsAvailable);
        Assert.Equal(3, adapter.RestoreCount);
        Assert.Collection(
            controller.OperationEvidence,
            evidence => Assert.False(evidence.Success),
            evidence =>
            {
                Assert.Equal(TweakOperationKind.BaselineRestore, evidence.Operation);
                Assert.True(evidence.Success);
                Assert.True(evidence.Sequence > failedEvidence.Sequence);
            });
    }

    [Fact]
    public void PersistedApplyFailureRestoresBaselineAndBlocksMutations()
    {
        var adapter = new RecordingAdapter("silksong") { FailId = "unlimited_silk" };
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };

        var controller = new TweakController(adapter, store);
        var result = controller.Initialize();

        Assert.False(result.Success);
        Assert.Contains("unlimited_silk", result.Error);
        Assert.False(controller.MutationsAvailable);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.Equal("on", store["dualsouls.mods.silksong.value.unlimited_silk"]);
    }

    [Fact]
    public void CyclePersistsOnlyAfterSuccessfulApply()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        var result = controller.Cycle("unlimited_silk");

        Assert.True(result.Success);
        Assert.Equal("on", controller.Value("unlimited_silk"));
        Assert.Equal("on", store["dualsouls.mods.silksong.value.unlimited_silk"]);

        adapter.FailId = "damage_received";
        result = controller.Cycle("damage_received");

        Assert.False(result.Success);
        Assert.Equal("vanilla", controller.Value("damage_received"));
        Assert.False(store.ContainsKey("dualsouls.mods.silksong.value.damage_received"));
        Assert.False(controller.MutationsAvailable);
        Assert.Equal(1, adapter.RestoreCount);
    }

    [Fact]
    public void ResetRestoresEveryValueAndLeavesMutationHealthAvailable()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.value.damage_received"] = "invincible",
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        var result = controller.Reset();

        Assert.True(result.Success);
        Assert.True(controller.MutationsAvailable);
        Assert.Equal("vanilla", controller.Value("damage_received"));
        Assert.Equal("off", controller.Value("unlimited_silk"));
        Assert.Equal("vanilla", store["dualsouls.mods.silksong.value.damage_received"]);
        Assert.Equal("off", store["dualsouls.mods.silksong.value.unlimited_silk"]);
        Assert.Equal(1, adapter.RestoreCount);
    }

    [Fact]
    public void StoresAreIsolatedByGameId()
    {
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };
        var silksong = new TweakController(new RecordingAdapter("silksong"), store);
        var hollowKnight = new TweakController(new RecordingAdapter("hollow-knight"), store);

        Assert.True(silksong.Initialize().Success);
        Assert.True(hollowKnight.Initialize().Success);

        Assert.True(silksong.MutationsAvailable);
        Assert.Equal("on", silksong.Value("unlimited_silk"));
        Assert.True(hollowKnight.MutationsAvailable);
        Assert.Equal("off", hollowKnight.Value("unlimited_silk"));
    }

    [Fact]
    public void SelectionsSurviveControllerRecreation()
    {
        var store = new MemoryStore();
        var firstRun = new TweakController(new RecordingAdapter("silksong"), store);
        Assert.True(firstRun.Initialize().Success);
        Assert.True(firstRun.Cycle("unlimited_silk").Success);

        var relaunched = new TweakController(new RecordingAdapter("silksong"), store);
        Assert.True(relaunched.Initialize().Success);
        Assert.True(relaunched.MutationsAvailable);
        Assert.Equal("on", relaunched.Value("unlimited_silk"));
    }

    [Fact]
    public void IndependentlyChangedGameStatesSurviveRecreation()
    {
        var store = new MemoryStore();
        var silksong = new TweakController(new RecordingAdapter("silksong"), store);
        var hollowKnight = new TweakController(new RecordingAdapter("hollow-knight"), store);
        Assert.True(silksong.Initialize().Success);
        Assert.True(hollowKnight.Initialize().Success);
        Assert.True(silksong.Cycle("unlimited_silk").Success);
        Assert.True(hollowKnight.Cycle("damage_received").Success);

        var silksongRelaunched = new TweakController(new RecordingAdapter("silksong"), store);
        var hollowKnightRelaunched = new TweakController(new RecordingAdapter("hollow-knight"), store);
        Assert.True(silksongRelaunched.Initialize().Success);
        Assert.True(hollowKnightRelaunched.Initialize().Success);

        Assert.True(silksongRelaunched.MutationsAvailable);
        Assert.Equal("on", silksongRelaunched.Value("unlimited_silk"));
        Assert.Equal("vanilla", silksongRelaunched.Value("damage_received"));
        Assert.True(hollowKnightRelaunched.MutationsAvailable);
        Assert.Equal("off", hollowKnightRelaunched.Value("unlimited_silk"));
        Assert.Equal("prevent_death", hollowKnightRelaunched.Value("damage_received"));
    }

    [Fact]
    public void TickRunsOnlyWhileMutationsAreAvailable()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        controller.Tick();
        Assert.Equal(1, adapter.TickCount);

        adapter.FailId = "damage_received";
        Assert.False(controller.Cycle("damage_received").Success);
        controller.Tick();
        Assert.Equal(1, adapter.TickCount);
    }

    [Fact]
    public void CycleRollsBackSelectionAndFailsClosedWhenPersistenceCannotBeFlushed()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);
        store.ThrowOnFlush = true;

        var result = controller.Cycle("unlimited_silk");

        Assert.False(result.Success);
        Assert.Contains("persist", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(controller.MutationsAvailable);
        Assert.Equal("off", controller.Value("unlimited_silk"));
        Assert.Equal("off", store["dualsouls.mods.silksong.value.unlimited_silk"]);
        Assert.Equal(1, adapter.RestoreCount);
    }

    [Fact]
    public void DescriptorExposesCanonicalIdentityAndControlKind()
    {
        var descriptor = new TweakDescriptor(
            "damage_received", "damage_taken", TweakControlKind.Choice,
            "COMBAT", "DAMAGE TAKEN", "How damage is handled.",
            "vanilla", new[] { "vanilla", "invincible" });

        Assert.Equal("damage_received", descriptor.Id);
        Assert.Equal("damage_taken", descriptor.ContractId);
        Assert.Equal(TweakControlKind.Choice, descriptor.ControlKind);
    }

    [Fact]
    public void SetValidatesAndPersistsAnExactChoiceValue()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        var result = controller.Set("damage_received", "invincible");

        Assert.True(result.Success);
        Assert.Equal("invincible", controller.Value("damage_received"));
        Assert.Equal("invincible", store["dualsouls.mods.silksong.value.damage_received"]);
        Assert.Equal(new[] { ("damage_received", "invincible") }, adapter.Applied);
        Assert.False(controller.Set("damage_received", "unsupported").Success);
        Assert.Equal("invincible", controller.Value("damage_received"));
    }

    [Fact]
    public void CommandAndRouteOperationsAreNeverLoadedOrPersistedAsChoices()
    {
        var adapter = new OperationAdapter();
        var store = new MemoryStore
        {
            ["dualsouls.mods.operations.value.save_to_slot"] = "run",
            ["dualsouls.mods.operations.value.skins"] = "open",
            ["dualsouls.mods.operations.value.state_slots"] = "legacy",
        };
        var controller = new TweakController(adapter, store);

        Assert.True(controller.Initialize().Success);
        Assert.Empty(adapter.Applied);
        Assert.True(controller.Set("save_to_slot", "run").Success);
        Assert.True(controller.Set("skins", "open").Success);
        Assert.Equal(new[] { ("save_to_slot", "run"), ("skins", "open") }, adapter.Applied);
        Assert.Equal("run", store["dualsouls.mods.operations.value.save_to_slot"]);
        Assert.Equal("open", store["dualsouls.mods.operations.value.skins"]);
        Assert.Equal("legacy", store["dualsouls.mods.operations.value.state_slots"]);
        Assert.False(controller.Cycle("save_to_slot").Success);
    }

    [Fact]
    public void FailedResetNeverPersistsCommandOrRouteRows()
    {
        var adapter = new OperationAdapter();
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);
        store.ThrowOnFlush = true;

        var result = controller.Reset();

        Assert.False(result.Success);
        Assert.DoesNotContain("dualsouls.mods.operations.value.save_to_slot", store.Keys);
        Assert.DoesNotContain("dualsouls.mods.operations.value.skins", store.Keys);
    }

    [Fact]
    public void UnavailableOperationFailsBeforeAdapterMutation()
    {
        var adapter = new UnavailableOperationAdapter();
        var controller = new TweakController(adapter, new MemoryStore());
        Assert.True(controller.Initialize().Success);

        var result = controller.Set("skins", "open");

        Assert.False(result.Success);
        Assert.Contains("currently unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(adapter.Applied);
    }

    private sealed class RecordingAdapter : ITweakAdapter
    {
        public RecordingAdapter(string gameId) => GameId = gameId;

        public string GameId { get; }
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor(
                "damage_received", "COMBAT", "DAMAGE RECEIVED", "How damage is handled.",
                "vanilla", new[] { "vanilla", "prevent_death", "invincible" }),
            new TweakDescriptor(
                "unlimited_silk", "COMBAT", "UNLIMITED SILK", "Keep Silk available.",
                "off", new[] { "off", "on" }),
        };

        public int RestoreFailuresRemaining { get; set; }
        public int CaptureCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int TickCount { get; private set; }
        public string FailId { get; set; }
        public List<(string Id, string Value)> Applied { get; } = new();

        public void CaptureBaseline() => CaptureCount++;

        public TweakActionResult Apply(string id, string value)
        {
            if (id == FailId) return TweakActionResult.Fail("failed " + id);
            Applied.Add((id, value));
            return TweakActionResult.Ok();
        }

        public void RestoreBaseline()
        {
            RestoreCount++;
            if (RestoreFailuresRemaining > 0)
            {
                RestoreFailuresRemaining--;
                throw new InvalidOperationException("restore failed");
            }
        }
        public void Tick() => TickCount++;
    }

    private sealed class UnavailableRecordingAdapter : ITweakAdapter
    {
        public string GameId => "hollow-knight";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            TweakDescriptor.Unavailable(
                "secret_radar", "secret_radar", TweakControlKind.Choice,
                "WORLD", "SECRET RADAR", "Signal nearby secrets.",
                "off", new[] { "off", "on" }, "No adapter operation exists yet.")
        };
        public List<(string Id, string Value)> Applied { get; } = new();

        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value)
        {
            Applied.Add((id, value));
            return TweakActionResult.Ok();
        }

        public void RestoreBaseline() { }
        public void Tick() { }
    }

    private sealed class OperationAdapter : ITweakAdapter
    {
        public string GameId => "operations";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor(
                "save_to_slot", "save_to_slot", TweakControlKind.Command,
                "SAVE STATES", "SAVE TO SLOT", "Save now.", "run", new[] { "run" }),
            new TweakDescriptor(
                "skins", "skins", TweakControlKind.Route,
                "GENERAL", "SKINS", "Open skins.", "open", new[] { "open" }),
        };
        public List<(string Id, string Value)> Applied { get; } = new();
        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value)
        {
            Applied.Add((id, value));
            return TweakActionResult.Ok();
        }
        public void RestoreBaseline() { }
        public void Tick() { }
    }

    private sealed class UnavailableOperationAdapter : ITweakAdapter
    {
        public string GameId => "unavailable";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            TweakDescriptor.Unavailable(
                "skins", "skins", TweakControlKind.Route,
                "GENERAL", "SKINS", "Open skins.", "open", new[] { "open" },
                "No adapter route is connected yet."),
        };
        public List<(string Id, string Value)> Applied { get; } = new();
        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value)
        {
            Applied.Add((id, value));
            return TweakActionResult.Ok();
        }
        public void RestoreBaseline() { }
        public void Tick() { }
    }

    private sealed class MemoryStore : Dictionary<string, string>, ITweakStore
    {
        public int FlushCount { get; private set; }
        public bool ThrowOnFlush { get; set; }

        public string Read(string key) => TryGetValue(key, out var value) ? value : null;
        public void Write(string key, string value) => this[key] = value;
        public void Flush()
        {
            FlushCount++;
            if (ThrowOnFlush) throw new InvalidOperationException("persistence unavailable");
        }
    }
}
