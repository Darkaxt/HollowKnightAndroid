// DsConfig — runtime knobs for the second screen, read from a file.
//
// The dev loop for this code is a ~3 minute APK build plus a ~4 minute
// on-device IL2CPP compile, so anything that can only be changed by rebuilding
// costs about seven minutes per question. During the M0 spike, moving the
// experiment's variables into a file instead turned four hypotheses into four
// app restarts and no rebuilds at all. That is worth keeping.
//
// The file is optional and absent by default:
//
//     adb shell 'echo "canvasmode=overlay testcard=1" > \
//         /sdcard/Android/data/com.jakobkhansen.silksong/files/dualscreen_v2'
//
// Nothing here is a shipping setting. Real settings come from the launcher
// through SilksongPatches.Settings; this is for turning a knob on a device that
// is behaving oddly, without a build.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class DsConfig
{
    const string FILE = "dualscreen_v2";

    static Dictionary<string, string> _values;

    static Dictionary<string, string> Values
    {
        get
        {
            if (_values != null) return _values;
            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(Application.persistentDataPath, FILE);
                if (File.Exists(path))
                {
                    foreach (string tok in File.ReadAllText(path)
                                 .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = tok.IndexOf('=');
                        if (eq > 0) _values[tok.Substring(0, eq)] = tok.Substring(eq + 1);
                    }
                    Debug.Log("[DualScreen] runtime overrides: " + Describe());
                }
            }
            catch (Exception e)
            {
                // A knob file that cannot be read means default behaviour, never
                // a failure to start.
                Debug.LogWarning("[DualScreen] could not read " + FILE + ": " + e.Message);
            }
            return _values;
        }
    }

    public static bool Bool(string key, bool fallback)
    {
        string v;
        if (!Values.TryGetValue(key, out v)) return fallback;
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static int Int(string key, int fallback)
    {
        string v; int parsed;
        if (!Values.TryGetValue(key, out v)) return fallback;
        return int.TryParse(v, out parsed) ? parsed : fallback;
    }

    public static string Str(string key, string fallback)
    {
        string v;
        return Values.TryGetValue(key, out v) ? v : fallback;
    }

    public static string Describe()
    {
        if (Values.Count == 0) return "(none)";
        var parts = new List<string>();
        foreach (var kv in Values) parts.Add(kv.Key + "=" + kv.Value);
        parts.Sort(StringComparer.Ordinal);
        return string.Join(" ", parts.ToArray());
    }
}
#endif
