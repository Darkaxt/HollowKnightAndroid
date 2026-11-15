// DsCommandFile — an app-owned command channel for device QA.
//
// adb (or the app itself) drops dualsouls-cmd.txt into the app's external
// files directory; each line is handed to the game's command handler and the
// replies are appended to dualsouls-cmd.out next to it. Only the app and adb
// can write there, and an absent file costs one stat every half second.
//
// Write the file atomically (echo ... > x.tmp && mv x.tmp dualsouls-cmd.txt)
// so a half-written batch is never read.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class DsCommandFile
{
    const float PollSeconds = 0.5f;
    const float BackoffSeconds = 5f;
    const int MaxLines = 64;

    static float _next;
    static string _in, _out;

    public static void Poll(Func<string, string> handle)
    {
        if (handle == null || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + PollSeconds;
        try
        {
            if (_in == null)
            {
                string dir = Application.persistentDataPath;
                _in = Path.Combine(dir, "dualsouls-cmd.txt");
                _out = Path.Combine(dir, "dualsouls-cmd.out");
            }
            if (!File.Exists(_in)) return;
            string[] lines = File.ReadAllLines(_in);
            File.Delete(_in);

            var reply = new StringBuilder();
            int run = 0;
            for (int i = 0; i < lines.Length && run < MaxLines; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                run++;
                string result;
                try { result = handle(line) ?? ""; }
                catch (Exception e) { result = "error " + e.GetType().Name + ": " + e.Message; }
                reply.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(" > ").Append(line).Append('\n')
                     .Append(result).Append('\n');
                int cut = result.IndexOf('\n');
                Debug.Log("[DsCmd] " + line + " -> " + (cut < 0 ? result : result.Substring(0, cut)));
            }
            File.AppendAllText(_out, reply.ToString());
        }
        catch (Exception e)
        {
            _next = Time.unscaledTime + BackoffSeconds;
            Debug.LogWarning("[DsCmd] command file failed: " + e.Message);
        }
    }
}
#endif
