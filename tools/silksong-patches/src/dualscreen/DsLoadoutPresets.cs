// DsLoadoutPresets -- #56 loadout presets for the Loadout pane (behaviour
// reference: Nexus mod HK130 CharmPresets; no mod code). A preset is the crest,
// the tool in each of its slots and the tools in the extra slots, stored per
// save profile. Loading equips through the game's own ToolItemManager paths.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class DsLoadoutPresets
{
    public const int Count = 3;

    [Serializable]
    sealed class Preset
    {
        public string crest;
        public List<string> slots = new List<string>();
        public List<string> extraIds = new List<string>();
        public List<string> extraTools = new List<string>();
    }

    static string PathFor(int profile, int preset) =>
        Path.Combine(Application.persistentDataPath, "dualsouls-loadout-presets", "silksong",
                     "profile-" + profile, "preset-" + preset + ".json");

    /// <summary>
    /// Why presets cannot be loaded right now, or null when they can: at a bench,
    /// or anywhere with EQUIP ANYWHERE, which is the game's own CanChangeEquips rule.
    /// </summary>
    public static string BlockReason()
    {
        if (!DsGameData.InGame) return "Unavailable";
        try
        {
            if (!PlayerData.instance.atBench && !CheatManager.CanChangeEquipsAnywhere) return "Rest at a bench";
        }
        catch { return "Unavailable"; }
        return null;
    }

    public static bool Exists(int preset)
    {
        if (!DsGameData.InGame || preset < 1 || preset > Count) return false;
        try { return File.Exists(PathFor(GameManager.instance.profileID, preset)); } catch { return false; }
    }

    public static string Save(int preset)
    {
        if (!DsGameData.InGame) return "Load a save first";
        if (preset < 1 || preset > Count) return "No such preset";
        var pd = PlayerData.instance;
        var data = new Preset { crest = pd.CurrentCrestID };
        var equipped = ToolItemManager.GetEquippedToolsForCrest(data.crest);
        var crest = ToolItemManager.GetCrestByName(data.crest);
        int length = crest != null && crest.Slots != null ? crest.Slots.Length : 0;
        int tools = 0;
        for (int i = 0; i < length; i++)
        {
            var held = equipped != null && i < equipped.Count ? equipped[i] : null;
            data.slots.Add(held != null ? held.name : string.Empty);
            if (held != null) tools++;
        }
        foreach (var extra in DsGameArt.ExtraToolSlots())
        {
            string held = pd.ExtraToolEquips.GetData(extra.Id).EquippedTool ?? string.Empty;
            data.extraIds.Add(extra.Id);
            data.extraTools.Add(held);
            if (held.Length > 0) tools++;
        }
        string path = PathFor(GameManager.instance.profileID, preset);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(data));
        return "Preset " + preset + " saved (" + tools + " tools)";
    }

    public static string Load(int preset)
    {
        string reason = BlockReason();
        if (reason != null) return reason;
        if (preset < 1 || preset > Count) return "No such preset";
        var pd = PlayerData.instance;
        string path = PathFor(GameManager.instance.profileID, preset);
        if (!File.Exists(path)) return "Preset " + preset + " is empty";
        Preset data = JsonUtility.FromJson<Preset>(File.ReadAllText(path));
        if (data == null || string.IsNullOrEmpty(data.crest)) return "Preset " + preset + " is unreadable";

        var crest = ToolItemManager.GetCrestByName(data.crest);
        if (crest == null || !crest.IsUnlocked || crest.Slots == null) return "That crest is not unlocked";

        int skipped = 0;
        try
        {
            if (pd.CurrentCrestID != data.crest) ToolItemManager.SetEquippedCrest(data.crest);

            List<ToolCrestsData.SlotData> saved = null;
            try { saved = crest.SaveData.Slots; } catch { }
            var used = new HashSet<string>(StringComparer.Ordinal);
            var slots = new List<string>(crest.Slots.Length);
            for (int i = 0; i < crest.Slots.Length; i++)
            {
                string name = i < data.slots.Count ? data.slots[i] : string.Empty;
                bool open = !crest.Slots[i].IsLocked || (saved != null && i < saved.Count && saved[i].IsUnlocked);
                var tool = string.IsNullOrEmpty(name) ? null : ToolItemManager.GetToolByName(name);
                bool ok = tool != null && open && tool.IsUnlocked && tool.Type == crest.Slots[i].Type && used.Add(name);
                if (!string.IsNullOrEmpty(name) && !ok) skipped++;
                slots.Add(ok ? name : string.Empty);
            }
            ToolItemManager.SetEquippedTools(data.crest, slots);

            foreach (var extra in DsGameArt.ExtraToolSlots())
            {
                int index = data.extraIds.IndexOf(extra.Id);
                if (index < 0 || index >= data.extraTools.Count) continue;
                string name = data.extraTools[index] ?? string.Empty;
                var tool = name.Length == 0 ? null : ToolItemManager.GetToolByName(name);
                bool ok = tool != null && tool.IsUnlocked && tool.Type == extra.Type && used.Add(name);
                if (name.Length > 0 && !ok) skipped++;
                ToolItemManager.SetExtraEquippedTool(extra.Id, ok ? name : string.Empty);
            }
            ToolItemManager.SendEquippedChangedEvent(true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DualScreen] preset load failed: " + e.Message);
            return "Preset " + preset + " could not be loaded";
        }
        return "Preset " + preset + " loaded" + (skipped > 0 ? ", " + skipped + " skipped" : "");
    }
}
#endif
