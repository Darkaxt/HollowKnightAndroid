using DualSouls.Mods;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakNoMasterContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("malformed")]
    public void LegacyMasterStateCannotLockOrDiscardPersistedChoices(string legacyMaster)
    {
        var adapter = new RecordingAdapter();
        var store = new MemoryStore
        {
            ["dualsouls.mods.test.value.speed"] = "fast",
        };
        if (legacyMaster != null)
            store["dualsouls.mods.test.master"] = legacyMaster;

        var controller = new TweakController(adapter, store);

        Assert.True(controller.Initialize().Success);
        Assert.Equal("fast", controller.Value("speed"));
        Assert.Contains(("speed", "fast"), adapter.Applied);
        Assert.Equal(
            legacyMaster,
            store.TryGetValue("dualsouls.mods.test.master", out string retained) ? retained : null);

        Assert.True(controller.Cycle("speed").Success);
        Assert.Equal("normal", controller.Value("speed"));
        Assert.Equal("normal", store["dualsouls.mods.test.value.speed"]);
    }

    [Fact]
    public void ControllerExposesInternalMutationHealthInsteadOfMasterProductState()
    {
        var controller = new TweakController(new RecordingAdapter(), new MemoryStore());

        Assert.NotNull(typeof(TweakController).GetProperty("MutationAvailability"));
        Assert.NotNull(typeof(TweakController).GetProperty("MutationsAvailable"));
        Assert.True(controller.Initialize().Success);
        Assert.Equal("Available", controller.MutationAvailability.ToString());
        Assert.True(controller.MutationsAvailable);
    }

    [Fact]
    public void EveryAvailableChoiceCanBeSetDirectlyWithoutGlobalInteractionState()
    {
        var adapter = new RecordingAdapter();
        var controller = new TweakController(adapter, new MemoryStore());
        Assert.True(controller.Initialize().Success);

        foreach (TweakDescriptor descriptor in controller.Descriptors)
        {
            if (!descriptor.IsAvailable || descriptor.ControlKind != TweakControlKind.Choice)
                continue;

            foreach (string value in descriptor.Values)
            {
                TweakActionResult result = controller.Set(descriptor.Id, value);
                Assert.True(result.Success, $"{descriptor.Id}={value}: {result.Error}");
                Assert.Equal(value, controller.Value(descriptor.Id));
            }
        }
    }

    private sealed class RecordingAdapter : ITweakAdapter
    {
        public string GameId => "test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor(
                "speed", "run_speed", TweakControlKind.Choice,
                "WORLD", "SPEED", "Choose speed.", "normal", new[] { "normal", "fast" }),
            new TweakDescriptor(
                "damage", "damage_taken", TweakControlKind.Choice,
                "COMBAT", "DAMAGE", "Choose damage.", "normal", new[] { "normal", "safe", "none" }),
            new TweakDescriptor(
                "command", "command", TweakControlKind.Command,
                "TOOLS", "COMMAND", "Run command.", "run", new[] { "run" }),
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
        public string Read(string key) => TryGetValue(key, out string value) ? value : null;
        public void Write(string key, string value) => this[key] = value;
        public void Flush() { }
    }
}
