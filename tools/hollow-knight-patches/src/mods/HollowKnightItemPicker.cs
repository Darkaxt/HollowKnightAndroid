#if UNITY_ANDROID && !UNITY_EDITOR
using System;

namespace DualSouls.Mods.HollowKnight
{
    /// <summary>
    /// #25 Hollow Knight backport of mod 19's collectable picker. PREVIOUS/NEXT ITEM
    /// step through the countable inventory items; ADD/TAKE/SET apply PICKER AMOUNT
    /// to the selection as a saved edit, within each item's own limit.
    /// </summary>
    internal sealed class HollowKnightItemPicker
    {
        public const string AmountId = "picker_amount";
        public static readonly string[] Amounts = { "1", "5", "10", "50", "99" };

        // Display name, count field, "found" flag that shows it in the inventory, limit.
        static readonly object[][] Items =
        {
            new object[] { "Wanderer's Journal", "trinket1", "foundTrinket1", 99 },
            new object[] { "Hallownest Seal", "trinket2", "foundTrinket2", 99 },
            new object[] { "King's Idol", "trinket3", "foundTrinket3", 99 },
            new object[] { "Arcane Egg", "trinket4", "foundTrinket4", 99 },
            new object[] { "Simple Key", "simpleKeys", null, 99 },
            new object[] { "Rancid Egg", "rancidEggs", null, 99 },
            new object[] { "Pale Ore", "ore", null, 99 },
            new object[] { "Essence", "dreamOrbs", null, 9999 },
            new object[] { "Charm Notches", "charmSlots", null, 11 },
        };

        int _item = -1, _amount = 1;

        public static bool IsCommand(string id) =>
            id == "item_prev" || id == "item_next" || id == "item_add" || id == "item_take" || id == "item_set";

        public void SetAmount(string value)
        {
            int amount;
            if (Array.IndexOf(Amounts, value) < 0 || !int.TryParse(value, out amount))
                throw new ArgumentException("Unknown picker amount: " + value);
            _amount = amount;
        }

        public void RestoreAll() => _amount = 1;

        public TweakActionResult Run(string id, global::PlayerData data)
        {
            if (id == "item_prev" || id == "item_next")
            {
                int step = id == "item_next" ? 1 : -1;
                _item = _item < 0 ? (step > 0 ? 0 : Items.Length - 1) : ((_item + step) % Items.Length + Items.Length) % Items.Length;
                object[] entry = Items[_item];
                return Text((_item + 1) + "/" + Items.Length + " " + entry[0] + " · " + data.GetInt((string)entry[1]) + " owned");
            }
            if (_item < 0) return TweakActionResult.Fail("Pick an item first with NEXT ITEM.");
            object[] item = Items[_item];
            string field = (string)item[1];
            int limit = (int)item[3];
            int before = data.GetInt(field);
            int target = id == "item_add" ? before + _amount : id == "item_take" ? before - _amount : _amount;
            target = Math.Max(0, Math.Min(limit, target));
            data.SetInt(field, target);
            if (target > 0 && item[2] != null) data.SetBool((string)item[2], true);
            return Text(item[0] + " · " + before + " to " + target);
        }

        static TweakActionResult Text(string value) => TweakActionResult.Ok(TweakReadback.Text(value));
    }
}
#endif
