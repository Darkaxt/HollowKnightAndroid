using DualSouls.Mods;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakMenuModelTests
{
    [Fact]
    public void ConstructorValidatesControllerAndVisibleRows()
    {
        var controller = new TweakController(new RecordingAdapter(), new MemoryStore());

        Assert.Throws<ArgumentNullException>(() => new TweakMenuModel(null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TweakMenuModel(controller, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TweakMenuModel(controller, -1));
    }

    [Fact]
    public void CatalogUsesFirstSeenGroupOrderAndStartsAtFirstSelection()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);

        Assert.False(fixture.Model.IsOpen);
        Assert.Equal(0, fixture.Model.SelectedGroupIndex);
        Assert.Equal(0, fixture.Model.SelectedRowIndex);
        Assert.Equal(0, fixture.Model.WindowStart);
        Assert.Equal(2, fixture.Model.VisibleRows);
        Assert.Equal("", fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);
        Assert.Equal(new[] { "COMBAT", "MOVEMENT" }, fixture.Model.Groups);
        Assert.Equal(
            new[] { "damage_received", "unlimited_soul", "bench_teleport" },
            fixture.Model.CurrentRows.Select(row => row.Id));
        Assert.Same(fixture.Adapter.Descriptors[0], fixture.Model.Selected);
    }

    [Fact]
    public void OpenAndCloseOnlyChangeVisibility()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        fixture.Model.MoveRow(1);

        fixture.Model.Open();
        Assert.True(fixture.Model.IsOpen);
        fixture.Model.Close();

        Assert.False(fixture.Model.IsOpen);
        Assert.Equal(1, fixture.Model.SelectedRowIndex);
        Assert.Equal("unlimited_soul", fixture.Model.Selected.Id);

        fixture.Model.Open();
        Assert.True(fixture.Model.IsOpen);
        Assert.Equal(1, fixture.Model.SelectedRowIndex);
    }

    [Fact]
    public void RowMovementWrapsForLargePositiveAndNegativeDeltas()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);

        fixture.Model.MoveRow(-1);
        Assert.Equal(2, fixture.Model.SelectedRowIndex);
        Assert.Equal(1, fixture.Model.WindowStart);

        fixture.Model.MoveRow(4);
        Assert.Equal(0, fixture.Model.SelectedRowIndex);
        Assert.Equal(0, fixture.Model.WindowStart);

        fixture.Model.MoveRow(-4);
        Assert.Equal(2, fixture.Model.SelectedRowIndex);
        Assert.Equal(1, fixture.Model.WindowStart);
    }

    [Fact]
    public void ViewportMovementKeepsSelectedRowVisible()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);

        fixture.Model.MoveRow(1);
        AssertSelectionIsVisible(fixture.Model);
        Assert.Equal(0, fixture.Model.WindowStart);

        fixture.Model.MoveRow(1);
        AssertSelectionIsVisible(fixture.Model);
        Assert.Equal(1, fixture.Model.WindowStart);

        fixture.Model.MoveRow(-1);
        AssertSelectionIsVisible(fixture.Model);
        Assert.Equal(1, fixture.Model.WindowStart);

        fixture.Model.MoveRow(-1);
        AssertSelectionIsVisible(fixture.Model);
        Assert.Equal(0, fixture.Model.WindowStart);
    }

    [Fact]
    public void GroupMovementWrapsForLargePositiveAndNegativeDeltas()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);

        fixture.Model.MoveGroup(-3);
        Assert.Equal(1, fixture.Model.SelectedGroupIndex);
        Assert.Equal(new[] { "run_speed" }, fixture.Model.CurrentRows.Select(row => row.Id));
        Assert.Equal("run_speed", fixture.Model.Selected.Id);

        fixture.Model.MoveGroup(5);
        Assert.Equal(0, fixture.Model.SelectedGroupIndex);
        Assert.Equal("damage_received", fixture.Model.Selected.Id);
    }

    [Fact]
    public void ChangingGroupsResetsRowAndWindowWhilePreservingOpenState()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        fixture.Model.Open();
        fixture.Model.MoveRow(-1);
        Assert.Equal(2, fixture.Model.SelectedRowIndex);
        Assert.Equal(1, fixture.Model.WindowStart);

        fixture.Model.MoveGroup(1);

        Assert.True(fixture.Model.IsOpen);
        Assert.Equal(1, fixture.Model.SelectedGroupIndex);
        Assert.Equal(0, fixture.Model.SelectedRowIndex);
        Assert.Equal(0, fixture.Model.WindowStart);
        Assert.Equal("run_speed", fixture.Model.Selected.Id);
    }

    [Fact]
    public void DeferredSelectionRejectsCyclingAndPreservesControllerError()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        fixture.Model.MoveRow(2);

        var result = fixture.Model.CycleSelected();

        Assert.False(result.Success);
        Assert.Equal(result.Error, fixture.Model.Message);
        Assert.Contains("HKMOD-017", fixture.Model.Message);
        Assert.Contains("No safe scene-transition seam is enabled.", fixture.Model.Message);
        Assert.True(fixture.Model.MessageIsError);
        Assert.Equal("bench_teleport", fixture.Model.Selected.Id);
        Assert.Empty(fixture.Adapter.Applied);
    }

    [Fact]
    public void RowNavigationClearsErrorOnlyWhenSelectionChanges()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        fixture.Model.MoveRow(2);
        Assert.False(fixture.Model.CycleSelected().Success);
        string error = fixture.Model.Message;

        fixture.Model.MoveRow(3);
        Assert.Equal(2, fixture.Model.SelectedRowIndex);
        Assert.Equal(error, fixture.Model.Message);
        Assert.True(fixture.Model.MessageIsError);

        fixture.Model.MoveRow(1);
        Assert.Equal(0, fixture.Model.SelectedRowIndex);
        Assert.Equal("", fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);
    }

    [Fact]
    public void GroupNavigationClearsSuccessOnlyWhenSelectionChanges()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        Assert.True(fixture.Model.ToggleMaster().Success);
        string message = fixture.Model.Message;

        fixture.Model.MoveGroup(2);
        Assert.Equal(0, fixture.Model.SelectedGroupIndex);
        Assert.Equal(message, fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);

        fixture.Model.MoveGroup(1);
        Assert.Equal(1, fixture.Model.SelectedGroupIndex);
        Assert.Equal("", fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);
    }

    [Fact]
    public void CycleSelectedForwardsAvailableSelectionAndReportsSuccess()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        Assert.True(fixture.Model.ToggleMaster().Success);
        fixture.Model.MoveRow(1);

        var result = fixture.Model.CycleSelected();

        Assert.True(result.Success);
        Assert.Equal("Value saved.", fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);
        Assert.Equal("on", fixture.Controller.Value("unlimited_soul"));
        Assert.Equal(new[] { ("unlimited_soul", "on") }, fixture.Adapter.Applied);
    }

    [Fact]
    public void ToggleMasterForwardsBothStatesAndReportsSuccess()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);

        var enabled = fixture.Model.ToggleMaster();
        Assert.True(enabled.Success);
        Assert.True(fixture.Controller.MasterEnabled);
        Assert.Equal("Mods enabled.", fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);

        var disabled = fixture.Model.ToggleMaster();
        Assert.True(disabled.Success);
        Assert.False(fixture.Controller.MasterEnabled);
        Assert.Equal("Mods disabled; game baseline restored.", fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);
        Assert.Equal(1, fixture.Adapter.RestoreCount);
    }

    [Fact]
    public void ToggleMasterPreservesControllerError()
    {
        var fixture = MenuFixture.Create(
            visibleRows: 2,
            configureStore: store =>
                store["dualsouls.mods.menu-test.value.damage_received"] = "invincible");
        fixture.Adapter.FailId = "damage_received";

        var result = fixture.Model.ToggleMaster();

        Assert.False(result.Success);
        Assert.Equal(result.Error, fixture.Model.Message);
        Assert.Contains("adapter rejected damage_received", fixture.Model.Message);
        Assert.True(fixture.Model.MessageIsError);
        Assert.False(fixture.Controller.MasterEnabled);
    }

    [Fact]
    public void ResetForwardsAndReportsSuccess()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        Assert.True(fixture.Model.ToggleMaster().Success);
        fixture.Model.MoveRow(1);
        Assert.True(fixture.Model.CycleSelected().Success);

        var result = fixture.Model.Reset();

        Assert.True(result.Success);
        Assert.Equal("All values reset.", fixture.Model.Message);
        Assert.False(fixture.Model.MessageIsError);
        Assert.Equal("off", fixture.Controller.Value("unlimited_soul"));
        Assert.Equal(1, fixture.Adapter.RestoreCount);
    }

    [Fact]
    public void ResetPreservesControllerError()
    {
        var fixture = MenuFixture.Create(visibleRows: 2);
        fixture.Adapter.ThrowOnRestore = true;

        var result = fixture.Model.Reset();

        Assert.False(result.Success);
        Assert.Equal(result.Error, fixture.Model.Message);
        Assert.Contains("Could not restore the game baseline", fixture.Model.Message);
        Assert.True(fixture.Model.MessageIsError);
    }

    static void AssertSelectionIsVisible(TweakMenuModel model)
    {
        Assert.InRange(
            model.SelectedRowIndex,
            model.WindowStart,
            model.WindowStart + model.VisibleRows - 1);
    }

    sealed class MenuFixture
    {
        MenuFixture(RecordingAdapter adapter, TweakController controller, TweakMenuModel model)
        {
            Adapter = adapter;
            Controller = controller;
            Model = model;
        }

        public RecordingAdapter Adapter { get; }
        public TweakController Controller { get; }
        public TweakMenuModel Model { get; }

        public static MenuFixture Create(int visibleRows, Action<MemoryStore> configureStore = null)
        {
            var adapter = new RecordingAdapter();
            var store = new MemoryStore();
            configureStore?.Invoke(store);
            var controller = new TweakController(adapter, store);
            Assert.True(controller.Initialize().Success);
            return new MenuFixture(adapter, controller, new TweakMenuModel(controller, visibleRows));
        }
    }

    sealed class RecordingAdapter : ITweakAdapter
    {
        public string GameId => "menu-test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor(
                "damage_received", "COMBAT", "DAMAGE RECEIVED", "How damage is handled.",
                "vanilla", new[] { "vanilla", "invincible" }),
            new TweakDescriptor(
                "run_speed", "MOVEMENT", "RUN SPEED", "Choose movement speed.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "unlimited_soul", "COMBAT", "UNLIMITED SOUL", "Keep Soul available.",
                "off", new[] { "off", "on" }),
            TweakDescriptor.Deferred(
                "bench_teleport", "COMBAT", "BENCH TELEPORT", "Travel to a recorded bench.",
                "HKMOD-017", "No safe scene-transition seam is enabled.")
        };

        public string FailId { get; set; }
        public bool ThrowOnRestore { get; set; }
        public int RestoreCount { get; private set; }
        public List<(string Id, string Value)> Applied { get; } = new();

        public void CaptureBaseline() { }

        public TweakActionResult Apply(string id, string value)
        {
            if (id == FailId) return TweakActionResult.Fail("adapter rejected " + id);
            Applied.Add((id, value));
            return TweakActionResult.Ok();
        }

        public void RestoreBaseline()
        {
            RestoreCount++;
            if (ThrowOnRestore) throw new InvalidOperationException("restore unavailable");
        }

        public void Tick() { }
    }

    sealed class MemoryStore : Dictionary<string, string>, ITweakStore
    {
        public string Read(string key) => TryGetValue(key, out var value) ? value : null;
        public void Write(string key, string value) => this[key] = value;
        public void Flush() { }
    }
}
