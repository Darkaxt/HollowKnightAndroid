using System;
using System.Collections.Generic;
using Xunit;

// Only the native platform boundary is fake. The tested override below is linked
// verbatim from production. Native template/layout ownership stays in the adapter.
public class InventoryItemCollectableManager
{
    protected virtual List<CollectableItem> GetItems() => CollectableItemManager.GetCollectedItems();
    public List<CollectableItem> NativeAwakeForTest() => GetItems();
    protected virtual List<InventoryItemGrid.GridSection> GetGridSections(List<InventoryItemCollectable> entries, List<CollectableItem> items) =>
        throw new InvalidOperationException("unsafe native Item setter reached shared custom-display cache");
    public List<InventoryItemGrid.GridSection> NativeSectionsForTest(List<InventoryItemCollectable> entries, List<CollectableItem> items) => GetGridSections(entries, items);
}
public sealed class InventoryItemCollectable { }
public sealed class InventoryItemGrid { public sealed class GridSection { } }
public sealed class CollectableItem { }
public static class CollectableItemManager
{
    public static List<CollectableItem> GetCollectedItems() =>
        throw new InvalidOperationException("unsafe native enumeration reached ReportPreviouslyCollected");
}
public sealed class SilksongPortCollectableTests
{
    [Fact]
    public void NativeInitializationUsesTypedReadOnlyHookNotReportingEnumeration()
    {
        var a = new CollectableItem(); var b = new CollectableItem();
        var manager = new DsPortCollectableManager();
        manager.BindReadOnlyItems(new List<CollectableItem> { a, b });
        Assert.Equal(new[] { a, b }, manager.NativeAwakeForTest());
    }
    [Fact]
    public void InitializationWithoutAnOwnedSnapshotFailsBeforeNativeEnumeration()
    {
        var manager = new DsPortCollectableManager();
        var error = Assert.Throws<InvalidOperationException>(() => manager.NativeAwakeForTest());
        Assert.Equal("Inventory read-only item snapshot not bound", error.Message);
    }
    [Fact]
    public void NativeSectionsUseOwnedBinderInsteadOfCustomCacheSetter()
    {
        var manager = new DsPortCollectableManager();
        var item = new CollectableItem(); var entry = new InventoryItemCollectable();
        manager.BindReadOnlyItems(new[] { item });
        var section = new InventoryItemGrid.GridSection(); int calls = 0;
        Func<List<InventoryItemCollectable>, List<CollectableItem>, List<InventoryItemGrid.GridSection>> binder = (entries, items) =>
        {
            Assert.Same(entry, Assert.Single(entries)); Assert.Same(item, Assert.Single(items)); calls++;
            return new List<InventoryItemGrid.GridSection> { section };
        };
        var bind = typeof(DsPortCollectableManager).GetMethod("BindReadOnlySections");
        Assert.NotNull(bind); bind.Invoke(manager, new object[] { binder });
        Assert.Same(section, Assert.Single(manager.NativeSectionsForTest(new List<InventoryItemCollectable> { entry }, new List<CollectableItem> { item })));
        Assert.Equal(1, calls);
    }
    [Fact]
    public void UnboundSectionsFailBeforeUnsafeNativeSetter()
    {
        var manager = new DsPortCollectableManager();
        var error = Assert.Throws<InvalidOperationException>(() => manager.NativeSectionsForTest(new List<InventoryItemCollectable>(), new List<CollectableItem>()));
        Assert.Equal("Inventory read-only section binder not bound", error.Message);
    }
    [Fact]
    public void NativeLayoutCannotMutateTheBoundSnapshot()
    {
        var a = new CollectableItem(); var items = new List<CollectableItem> { a };
        var manager = new DsPortCollectableManager();
        manager.BindReadOnlyItems(items); items.Clear();
        manager.NativeAwakeForTest().Clear();
        Assert.Same(a, Assert.Single(manager.NativeAwakeForTest()));
    }
}
