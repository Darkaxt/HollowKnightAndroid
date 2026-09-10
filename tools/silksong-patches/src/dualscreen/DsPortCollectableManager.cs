// Native typed read-only enumeration hook. The adapter binds owned browse input
// before activation; the inherited native manager still owns templates/layout/details.
using System;
using System.Collections.Generic;

public sealed class DsPortCollectableManager : InventoryItemCollectableManager
{
    List<CollectableItem> _items;
    Func<List<InventoryItemCollectable>, List<CollectableItem>, List<InventoryItemGrid.GridSection>> _sections;
    public void BindReadOnlySections(Func<List<InventoryItemCollectable>, List<CollectableItem>, List<InventoryItemGrid.GridSection>> sections)
    { _sections = sections ?? throw new ArgumentNullException(nameof(sections)); }
    protected override List<InventoryItemGrid.GridSection> GetGridSections(List<InventoryItemCollectable> entries, List<CollectableItem> items)
    {
        if (_sections == null) throw new InvalidOperationException("Inventory read-only section binder not bound");
        return _sections(entries, items);
    }
    public void BindReadOnlyItems(IList<CollectableItem> items)
    { _items = new List<CollectableItem>(items ?? throw new ArgumentNullException(nameof(items))); }
    protected override List<CollectableItem> GetItems()
    {
        if (_items == null) throw new InvalidOperationException("Inventory read-only item snapshot not bound");
        return new List<CollectableItem>(_items);
    }
}
