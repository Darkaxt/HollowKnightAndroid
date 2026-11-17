#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    /// <summary>
    /// #25 per-item pickers (mod 19's collectable and tool dropdowns). A controller
    /// menu has no dropdown, so PREVIOUS/NEXT commands step through the game's own
    /// lists and report the selection; the action rows then apply PICKER AMOUNT to it.
    /// Collectable and unlock edits are saved like mod 19's buttons. Tool storage is
    /// the tool asset's capacity, which the game never saves; it is restored with the
    /// other overlays.
    /// </summary>
    internal sealed class SilksongItemPickers
    {
        public const string AmountId = "picker_amount";
        public static readonly string[] Amounts = { "1", "5", "10", "50", "99" };

        static readonly FieldInfo BaseStorageField =
            typeof(ToolItem).GetField("baseStorageAmount", BindingFlags.Instance | BindingFlags.NonPublic);

        readonly Dictionary<ToolItem, int> _storageOriginals = new Dictionary<ToolItem, int>();
        int _item = -1, _tool = -1, _amount = 1;

        public static bool IsCommand(string id)
        {
            switch (id)
            {
                case "item_prev": case "item_next": case "item_add": case "item_take": case "item_set":
                case "tool_prev": case "tool_next": case "tool_unlock": case "tool_storage":
                    return true;
                default:
                    return false;
            }
        }

        public void SetAmount(string value)
        {
            int amount;
            if (Array.IndexOf(Amounts, value) < 0 || !int.TryParse(value, out amount))
                throw new ArgumentException("Unknown picker amount: " + value);
            _amount = amount;
        }

        public TweakActionResult Run(string id)
        {
            switch (id)
            {
                case "item_prev": return StepItem(-1);
                case "item_next": return StepItem(1);
                case "tool_prev": return StepTool(-1);
                case "tool_next": return StepTool(1);
            }
            if (id.StartsWith("item_", StringComparison.Ordinal))
            {
                CollectableItem item = SelectedItem();
                if (item == null) return TweakActionResult.Fail("Pick a collectable first with NEXT ITEM.");
                int before = item.CollectedAmount;
                int target = id == "item_add" ? before + _amount : id == "item_take" ? Math.Max(0, before - _amount) : _amount;
                if (target > before)
                {
                    if (item.IsAtMax()) return TweakActionResult.Fail(ItemName(item) + " is already at its maximum.");
                    item.Collect(target - before, false);
                }
                else if (target < before)
                    item.Take(before - target, false);
                return Text(ItemName(item) + " · " + before + " to " + item.CollectedAmount);
            }

            ToolItem tool = SelectedTool();
            if (tool == null) return TweakActionResult.Fail("Pick a tool first with NEXT TOOL.");
            if (id == "tool_unlock")
            {
                bool was = tool.IsUnlocked;
                tool.Unlock(null, ToolItem.PopupFlags.None);
                ToolItemManager.SendEquippedChangedEvent(true);
                return Text(ToolName(tool) + (was ? " refilled (already unlocked)" : " unlocked"));
            }
            // tool_storage
            if (BaseStorageField == null) return TweakActionResult.Fail("This game version has no tool storage field.");
            int original = (int)BaseStorageField.GetValue(tool);
            if (!_storageOriginals.ContainsKey(tool))
            {
                if (original <= 0) return TweakActionResult.Fail(ToolName(tool) + " has no storage to change.");
                _storageOriginals[tool] = original;
            }
            BaseStorageField.SetValue(tool, _amount);
            if (tool.IsUnlocked)
            {
                ToolItemsData.Data saved = tool.SavedData;
                saved.AmountLeft = ToolItemManager.GetToolStorageAmount(tool);
                tool.SavedData = saved;
                AttackToolBinding? binding = ToolItemManager.GetAttackToolBinding(tool);
                if (binding.HasValue) ToolItemManager.ReportBoundAttackToolUpdated(binding.Value);
            }
            return Text(ToolName(tool) + " storage " + _amount + " until restart (was " + _storageOriginals[tool] + ")");
        }

        public void RestoreAll()
        {
            if (BaseStorageField != null)
                foreach (KeyValuePair<ToolItem, int> entry in _storageOriginals)
                    if (entry.Key != null) BaseStorageField.SetValue(entry.Key, entry.Value);
            _storageOriginals.Clear();
            _amount = 1;
        }

        TweakActionResult StepItem(int step)
        {
            List<CollectableItem> items = Items();
            if (items.Count == 0) return TweakActionResult.Fail("Collectables are not loaded yet.");
            _item = Step(_item, step, items.Count);
            CollectableItem item = items[_item];
            return Text((_item + 1) + "/" + items.Count + " " + ItemName(item) + " · " + item.CollectedAmount + " owned");
        }

        TweakActionResult StepTool(int step)
        {
            List<ToolItem> tools = Tools();
            if (tools.Count == 0) return TweakActionResult.Fail("Tools are not loaded yet.");
            _tool = Step(_tool, step, tools.Count);
            ToolItem tool = tools[_tool];
            string state = !tool.IsUnlocked ? "locked"
                : tool.BaseStorageAmount > 0 ? tool.SavedData.AmountLeft + "/" + ToolItemManager.GetToolStorageAmount(tool) + " uses"
                : "unlocked";
            return Text((_tool + 1) + "/" + tools.Count + " " + ToolName(tool) + " · " + state);
        }

        static int Step(int current, int step, int count) =>
            current < 0 ? (step > 0 ? 0 : count - 1) : ((current + step) % count + count) % count;

        CollectableItem SelectedItem()
        {
            List<CollectableItem> items = Items();
            return _item >= 0 && _item < items.Count ? items[_item] : null;
        }

        ToolItem SelectedTool()
        {
            List<ToolItem> tools = Tools();
            return _tool >= 0 && _tool < tools.Count ? tools[_tool] : null;
        }

        static List<CollectableItem> Items()
        {
            var list = new List<CollectableItem>();
            CollectableItemManager manager = ManagerSingleton<CollectableItemManager>.Instance;
            if (manager == null) return list;
            foreach (CollectableItem item in manager.GetAllCollectables())
                if (item != null) list.Add(item);
            return list;
        }

        static List<ToolItem> Tools()
        {
            var list = new List<ToolItem>();
            foreach (ToolItem tool in ToolItemManager.GetAllTools())
                if (tool != null) list.Add(tool);
            return list;
        }

        static string ItemName(CollectableItem item)
        {
            string name = null;
            try { name = item.GetDisplayName(CollectableItem.ReadSource.Inventory); } catch { }
            return Clean(name, item.name);
        }

        static string ToolName(ToolItem tool)
        {
            string name = null;
            try { name = tool.DisplayName.ToString(); } catch { }
            return Clean(name, tool.name);
        }

        // Menu messages print in capitals with underscores as spaces; keep names plain.
        static string Clean(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) name = fallback;
            return name.Replace("\n", " ").Trim();
        }

        static TweakActionResult Text(string value) => TweakActionResult.Ok(TweakReadback.Text(value));
    }
}
#endif
