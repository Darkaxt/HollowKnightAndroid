using System;
using System.IO;
using BepInEx.Configuration;

static class Program
{
    static int assertions;

    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "silksong-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ReloadPreservesOrphans(Path.Combine(root, "auto.cfg"), true);
            ReloadPreservesOrphans(Path.Combine(root, "manual.cfg"), false);
            SaveAndBindDuringReload(Path.Combine(root, "callbacks.cfg"));
            ReadFailurePreservesMemory(Path.Combine(root, "missing.cfg"));
            CallbackFailureDoesNotSavePartialState(Path.Combine(root, "throw.cfg"));
            NestedReload(Path.Combine(root, "nested.cfg"));
            Console.WriteLine("Config persistence: " + assertions + " assertions passed");
        }
        finally { Directory.Delete(root, true); }
    }

    static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    static ConfigFile Open(string path, string text)
    {
        File.WriteAllText(path, text);
        return new ConfigFile(path, false);
    }

    static string ReadValue(string path, string section, string key)
    {
        var config = new ConfigFile(path, false) { SaveOnConfigSet = false };
        return config.Bind(section, key, "missing").Value;
    }

    static void ReloadPreservesOrphans(string path, bool autoSave)
    {
        var config = Open(path, "[Known]\nValue = 1\n[Slots]\nOther = original\n");
        config.SaveOnConfigSet = autoSave;
        var value = config.Bind("Known", "Value", 0);
        const string updated = "# Keep this comment\n[Known]\nValue = 2\n[Slots]\nOther = kept\nLater = also kept\n";
        File.WriteAllText(path, updated);
        int entryEvents = 0, fileEvents = 0, reloadEvents = 0;
        bool reloading = true;
        value.SettingChanged += (_, _) => {
            entryEvents++;
            if (reloading) Assert(File.ReadAllText(path) == updated, "Entry notification rewrote the file");
        };
        config.SettingChanged += (_, args) => {
            fileEvents++;
            Assert(args.ChangedSetting == value, "Wrong changed entry");
        };
        config.ConfigReloaded += (_, _) => {
            reloadEvents++;
            Assert(value.Value == 2, "Reload notification arrived before values were applied");
            Assert(config.OrphanedEntries[new ConfigDefinition("Slots", "Other")] == "kept",
                "Reload notification lost an unbound value");
        };

        config.Reload();
        reloading = false;
        Assert(value.Value == 2, "Reload did not apply the bound value");
        Assert(File.ReadAllText(path) == updated, "Reload autosaved a partial file");
        Assert(config.OrphanedEntries.Count == 2, "Reload lost trailing unbound entries");
        Assert(config.SaveOnConfigSet == autoSave, "Reload changed the autosave setting");
        Assert(entryEvents == 1 && fileEvents == 1 && reloadEvents == 1, "Reload changed notification behavior");
        Assert(ReadValue(path, "Slots", "Other") == "kept", "Unbound value did not survive reopening");
        Assert(ReadValue(path, "Slots", "Later") == "also kept", "Later unbound value did not survive reopening");

        value.Value = 3;
        Assert(ReadValue(path, "Known", "Value") == (autoSave ? "3" : "2"),
            "Autosave was not restored after reload");
        config.Save();
        Assert(ReadValue(path, "Known", "Value") == "3", "Explicit save stopped working");
        Assert(ReadValue(path, "Slots", "Other") == "kept", "Normal save lost an unbound value");
    }

    static void SaveAndBindDuringReload(string path)
    {
        var config = Open(path, "[Known]\nFirst = 1\nSecond = 10\n");
        var first = config.Bind("Known", "First", 0);
        var second = config.Bind("Known", "Second", 0);
        const string updated =
            "[Known]\nFirst = 2\nSecond = 20\n[Slots]\nClaimed = from disk\nOther = kept\n";
        File.WriteAllText(path, updated);
        ConfigEntry<string> claimed = null;
        first.SettingChanged += (_, _) => {
            config.Save();
            claimed = config.Bind("Slots", "Claimed", "default");
            Assert(claimed.Value == "from disk", "A reload callback could not bind a later entry");
            Assert(File.ReadAllText(path) == updated, "A callback saved partially applied bound values");
            config.SaveOnConfigSet = false;
        };

        config.Reload();
        Assert(first.Value == 2 && second.Value == 20, "Reload skipped a bound entry");
        Assert(claimed != null && claimed.Value == "from disk", "Callback binding was lost");
        Assert(!config.SaveOnConfigSet, "Reload overwrote a callback's autosave preference");
        Assert(!config.OrphanedEntries.ContainsKey(claimed.Definition), "Claimed setting stayed orphaned");
        Assert(ReadValue(path, "Known", "Second") == "20", "Deferred save wrote an old bound value");
        Assert(ReadValue(path, "Slots", "Other") == "kept", "Deferred save lost an unbound value");
    }

    static void ReadFailurePreservesMemory(string path)
    {
        var config = Open(path, "[Slots]\nOther = kept\n");
        File.Delete(path);
        UnityEngine.Debug.Warnings.Clear();
        int reloaded = 0;
        config.ConfigReloaded += (_, _) => reloaded++;
        config.Reload();
        Assert(reloaded == 0, "A failed read reported successful reload");
        Assert(UnityEngine.Debug.Warnings.Count == 1, "A failed read was not reported");
        Assert(config.OrphanedEntries[new ConfigDefinition("Slots", "Other")] == "kept",
            "A failed read cleared the previous unbound settings");
        config.Save();
        Assert(ReadValue(path, "Slots", "Other") == "kept", "Saving after a failed read erased settings");
    }

    static void CallbackFailureDoesNotSavePartialState(string path)
    {
        var config = Open(path, "[Known]\nValue = 1\n");
        var value = config.Bind("Known", "Value", 0);
        const string updated = "[Known]\nValue = 2\n[Slots]\nOther = kept\n";
        File.WriteAllText(path, updated);
        EventHandler fail = (_, _) => {
            config.Save();
            throw new InvalidOperationException("callback failure");
        };
        value.SettingChanged += fail;
        UnityEngine.Debug.Warnings.Clear();
        config.Reload();
        Assert(UnityEngine.Debug.Warnings.Count == 1, "Callback failure was not reported");
        Assert(File.ReadAllText(path) == updated, "A failed reload flushed a partial save");
        value.SettingChanged -= fail;
        value.Value = 3;
        Assert(ReadValue(path, "Known", "Value") == "3", "Failure left persistence suspended");
        Assert(ReadValue(path, "Slots", "Other") == "kept", "Failure lost the parsed unbound entries");
    }

    static void NestedReload(string path)
    {
        var config = Open(path, "[Known]\nValue = 1\n");
        var value = config.Bind("Known", "Value", 0);
        const string updated = "[Known]\nValue = 2\n[Slots]\nOther = kept\n";
        File.WriteAllText(path, updated);
        bool nested = false;
        value.SettingChanged += (_, _) => {
            if (nested) return;
            nested = true;
            config.Reload();
            config.Save();
            Assert(File.ReadAllText(path) == updated, "Nested reload resumed saving too early");
        };
        config.Reload();
        Assert(nested && value.Value == 2, "Nested reload did not finish");
        Assert(ReadValue(path, "Slots", "Other") == "kept", "Nested reload lost unbound entries");
        value.Value = 3;
        Assert(ReadValue(path, "Known", "Value") == "3", "Nested reload left autosave disabled");
    }
}
