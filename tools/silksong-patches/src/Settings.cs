// What the launcher decided, as the game sees it.
//
// The settings live in the launcher's SharedPreferences, and the launcher runs
// in its own process (":launcher") so that the game calling System.exit on
// quit does not take it with it. That separation is deliberate and it means
// the game process cannot simply read those preferences: cross-process
// SharedPreferences needs MODE_MULTI_PROCESS, which is deprecated precisely
// because it does not reliably work.
//
// So the launcher writes a file immediately before starting the game, and this
// reads it. A file is not a workaround here, it is the right shape: the game
// reads its settings exactly once at startup, the launcher writes them exactly
// once at launch, and there is no shared state to keep coherent.
//
// The path is Application.persistentDataPath, which on this platform is the
// app's external files directory -- confirmed on device, where a patch logged
// "/storage/emulated/0/Android/data/com.jakobkhansen.silksong/files". The launcher's
// getExternalFilesDir(null) is the same directory, so neither side has to know
// the package name or guess at a layout.
//
// Nothing here throws. A setting that cannot be read is a setting at its
// default, which is always the behaviour the game already had.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SilksongPatches
{
    public static class Settings
    {
        const string FileName = "game-settings.txt";

        static Dictionary<string, string> _values;

        /// <summary>Everything the launcher wrote, read once and kept.</summary>
        static Dictionary<string, string> Values
        {
            get
            {
                if (_values != null) return _values;
                _values = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    string path = Path.Combine(Application.persistentDataPath, FileName);
                    if (File.Exists(path))
                    {
                        // Deliberately not JSON. The payload is a dozen scalars
                        // and this runs before the game has done anything, so
                        // the fewer types and allocations involved the better.
                        foreach (string line in File.ReadAllLines(path))
                        {
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            _values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                        }
                    }
                    else
                    {
                        Debug.Log("[SilksongPatches] no settings at " + path + "; using defaults");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[SilksongPatches] could not read settings: " + e);
                }
                return _values;
            }
        }

        public static bool GetBool(string key, bool fallback = false)
        {
            string v;
            if (!Values.TryGetValue(key, out v)) return fallback;
            return v == "true" || v == "1";
        }

        public static string GetString(string key, string fallback = null)
        {
            string v;
            return Values.TryGetValue(key, out v) ? v : fallback;
        }

        public static int GetInt(string key, int fallback = 0)
        {
            string v;
            int parsed;
            if (Values.TryGetValue(key, out v) && int.TryParse(v, out parsed)) return parsed;
            return fallback;
        }

        /// <summary>One line naming everything that was read, for the log.</summary>
        public static string Describe()
        {
            if (Values.Count == 0) return "(none)";
            var parts = new List<string>();
            foreach (var kv in Values) parts.Add(kv.Key + "=" + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(" ", parts.ToArray());
        }
    }
}
