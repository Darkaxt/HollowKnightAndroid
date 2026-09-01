using DualSouls.Mods;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void DeferredRequiresNonblankTrackingId(string trackingId)
    {
        Assert.Throws<ArgumentException>(() => TweakDescriptor.Deferred(
            "bench_teleport", "MOVEMENT", "BENCH TELEPORT", "Teleport to benches.", trackingId, "The game adapter does not support bench teleport yet."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void DeferredRequiresNonblankUnavailableReason(string unavailableReason)
    {
        Assert.Throws<ArgumentException>(() => TweakDescriptor.Deferred(
            "bench_teleport", "MOVEMENT", "BENCH TELEPORT", "Teleport to benches.", "H3-BENCH-TELEPORT", unavailableReason));
    }

    [Fact]
    public void DeferredIsUnavailableAndFixedOff()
    {
        var descriptor = TweakDescriptor.Deferred(
            "bench_teleport", "MOVEMENT", "BENCH TELEPORT", "Teleport to benches.", "H3-BENCH-TELEPORT", "The game adapter does not support bench teleport yet.");

        Assert.False(descriptor.IsAvailable);
        Assert.Equal("off", descriptor.DefaultValue);
        Assert.Equal(new[] { "off" }, descriptor.Values);
        Assert.Equal("H3-BENCH-TELEPORT", descriptor.TrackingId);
        Assert.Equal("The game adapter does not support bench teleport yet.", descriptor.UnavailableReason);
    }

    [Fact]
    public void DeferredRowsCorrectStaleValuesSkipApplyAndRejectCycling()
    {
        var adapter = new DeferredRecordingAdapter();
        var store = new MemoryStore
        {
            ["dualsouls.mods.hollow-knight.master"] = "1",
            ["dualsouls.mods.hollow-knight.value.bench_teleport"] = "on"
        };
        var controller = new TweakController(adapter, store);

        var initialized = controller.Initialize();
        var cycled = controller.Cycle("bench_teleport");

        Assert.True(initialized.Success);
        Assert.True(controller.MasterEnabled);
        Assert.Equal("off", controller.Value("bench_teleport"));
        Assert.Equal("off", store["dualsouls.mods.hollow-knight.value.bench_teleport"]);
        Assert.Equal(2, store.FlushCount);
        Assert.Empty(adapter.Applied);
        Assert.False(cycled.Success);
        Assert.Contains("BENCH TELEPORT", cycled.Error);
        Assert.Contains("H3-BENCH-TELEPORT", cycled.Error);
        Assert.Contains("The game adapter does not support bench teleport yet.", cycled.Error);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void InitializeDefaultsMasterOffWithoutApplyingTweaks()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);

        var result = controller.Initialize();

        Assert.True(result.Success);
        Assert.False(controller.MasterEnabled);
        Assert.Equal("vanilla", controller.Value("damage_received"));
        Assert.Equal("off", controller.Value("unlimited_silk"));
        Assert.Equal(1, adapter.CaptureCount);
        Assert.Empty(adapter.Applied);
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
    public void EnablingMasterAppliesOnlyPersistedNonDefaults()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.master"] = "1",
            ["dualsouls.mods.silksong.value.damage_received"] = "prevent_death",
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "off"
        };

        var controller = new TweakController(adapter, store);
        var result = controller.Initialize();

        Assert.True(result.Success);
        Assert.True(controller.MasterEnabled);
        Assert.Equal(new[] { ("damage_received", "prevent_death") }, adapter.Applied);
    }

    [Fact]
    public void DisablingMasterRestoresCapturedBaseline()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.master"] = "1",
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        var result = controller.SetMaster(false);

        Assert.True(result.Success);
        Assert.False(controller.MasterEnabled);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.Equal("0", store["dualsouls.mods.silksong.master"]);
    }

    [Fact]
    public void FailedEnableRestoresBaselineAndLeavesMasterOff()
    {
        var adapter = new RecordingAdapter("silksong") { FailId = "unlimited_silk" };
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.master"] = "1",
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };

        var controller = new TweakController(adapter, store);
        var result = controller.Initialize();

        Assert.False(result.Success);
        Assert.Contains("unlimited_silk", result.Error);
        Assert.False(controller.MasterEnabled);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.Equal("0", store["dualsouls.mods.silksong.master"]);
    }

    [Fact]
    public void CyclePersistsOnlyAfterSuccessfulApply()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore { ["dualsouls.mods.silksong.master"] = "1" };
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
        Assert.False(controller.MasterEnabled);
        Assert.Equal(1, adapter.RestoreCount);
    }

    [Fact]
    public void ResetRestoresDefaultsWithoutChangingMasterSelection()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.master"] = "1",
            ["dualsouls.mods.silksong.value.damage_received"] = "invincible",
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        var result = controller.Reset();

        Assert.True(result.Success);
        Assert.True(controller.MasterEnabled);
        Assert.Equal("vanilla", controller.Value("damage_received"));
        Assert.Equal("off", controller.Value("unlimited_silk"));
        Assert.Equal("1", store["dualsouls.mods.silksong.master"]);
        Assert.Equal(1, adapter.RestoreCount);
    }

    [Fact]
    public void StoresAreIsolatedByGameId()
    {
        var store = new MemoryStore
        {
            ["dualsouls.mods.silksong.master"] = "1",
            ["dualsouls.mods.silksong.value.unlimited_silk"] = "on"
        };
        var silksong = new TweakController(new RecordingAdapter("silksong"), store);
        var hollowKnight = new TweakController(new RecordingAdapter("hollow-knight"), store);

        Assert.True(silksong.Initialize().Success);
        Assert.True(hollowKnight.Initialize().Success);

        Assert.True(silksong.MasterEnabled);
        Assert.Equal("on", silksong.Value("unlimited_silk"));
        Assert.False(hollowKnight.MasterEnabled);
        Assert.Equal("off", hollowKnight.Value("unlimited_silk"));
    }

    [Fact]
    public void MasterAndSelectionsSurviveControllerRecreation()
    {
        var store = new MemoryStore();
        var firstRun = new TweakController(new RecordingAdapter("silksong"), store);
        Assert.True(firstRun.Initialize().Success);
        Assert.True(firstRun.SetMaster(true).Success);
        Assert.True(firstRun.Cycle("unlimited_silk").Success);

        var relaunched = new TweakController(new RecordingAdapter("silksong"), store);
        Assert.True(relaunched.Initialize().Success);
        Assert.True(relaunched.MasterEnabled);
        Assert.Equal("on", relaunched.Value("unlimited_silk"));

        Assert.True(relaunched.SetMaster(false).Success);
        var relaunchedMasterOff = new TweakController(new RecordingAdapter("silksong"), store);
        Assert.True(relaunchedMasterOff.Initialize().Success);
        Assert.False(relaunchedMasterOff.MasterEnabled);
        Assert.Equal("on", relaunchedMasterOff.Value("unlimited_silk"));
    }

    [Fact]
    public void IndependentlyChangedGameStatesSurviveRecreation()
    {
        var store = new MemoryStore();
        var silksong = new TweakController(new RecordingAdapter("silksong"), store);
        var hollowKnight = new TweakController(new RecordingAdapter("hollow-knight"), store);
        Assert.True(silksong.Initialize().Success);
        Assert.True(hollowKnight.Initialize().Success);
        Assert.True(silksong.SetMaster(true).Success);
        Assert.True(silksong.Cycle("unlimited_silk").Success);
        Assert.True(hollowKnight.SetMaster(true).Success);
        Assert.True(hollowKnight.Cycle("damage_received").Success);

        var silksongRelaunched = new TweakController(new RecordingAdapter("silksong"), store);
        var hollowKnightRelaunched = new TweakController(new RecordingAdapter("hollow-knight"), store);
        Assert.True(silksongRelaunched.Initialize().Success);
        Assert.True(hollowKnightRelaunched.Initialize().Success);

        Assert.True(silksongRelaunched.MasterEnabled);
        Assert.Equal("on", silksongRelaunched.Value("unlimited_silk"));
        Assert.Equal("vanilla", silksongRelaunched.Value("damage_received"));
        Assert.True(hollowKnightRelaunched.MasterEnabled);
        Assert.Equal("off", hollowKnightRelaunched.Value("unlimited_silk"));
        Assert.Equal("prevent_death", hollowKnightRelaunched.Value("damage_received"));
    }

    [Fact]
    public void TickRunsOnlyWhileMasterIsEnabled()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);

        controller.Tick();
        Assert.Equal(0, adapter.TickCount);

        Assert.True(controller.SetMaster(true).Success);
        controller.Tick();
        Assert.Equal(1, adapter.TickCount);
    }

    [Fact]
    public void EnablingMasterFailsClosedWhenPersistenceCannotBeFlushed()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore();
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);
        store.ThrowOnFlush = true;

        var result = controller.SetMaster(true);

        Assert.False(result.Success);
        Assert.Contains("persist", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(controller.MasterEnabled);
        Assert.Equal(1, adapter.RestoreCount);
        Assert.Equal("0", store["dualsouls.mods.silksong.master"]);
    }

    [Fact]
    public void CycleRollsBackSelectionAndFailsClosedWhenPersistenceCannotBeFlushed()
    {
        var adapter = new RecordingAdapter("silksong");
        var store = new MemoryStore { ["dualsouls.mods.silksong.master"] = "1" };
        var controller = new TweakController(adapter, store);
        Assert.True(controller.Initialize().Success);
        store.ThrowOnFlush = true;

        var result = controller.Cycle("unlimited_silk");

        Assert.False(result.Success);
        Assert.Contains("persist", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(controller.MasterEnabled);
        Assert.Equal("off", controller.Value("unlimited_silk"));
        Assert.Equal("off", store["dualsouls.mods.silksong.value.unlimited_silk"]);
        Assert.Equal("0", store["dualsouls.mods.silksong.master"]);
        Assert.Equal(1, adapter.RestoreCount);
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

        public void RestoreBaseline() => RestoreCount++;
        public void Tick() => TickCount++;
    }

    private sealed class DeferredRecordingAdapter : ITweakAdapter
    {
        public string GameId => "hollow-knight";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            TweakDescriptor.Deferred(
                "bench_teleport", "MOVEMENT", "BENCH TELEPORT", "Teleport to benches.", "H3-BENCH-TELEPORT", "The game adapter does not support bench teleport yet.")
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
