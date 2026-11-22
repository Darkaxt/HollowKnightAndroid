using System;

namespace DualSouls.Skins.Runtime
{
    /// <summary>
    /// QA-only memory of the last skin failure and the last reported status, read by the
    /// `skin detail` command in both games. Nothing here is logged or written to disk.
    /// </summary>
    public static class SkinRuntimeDiagnostics
    {
        const int Limit = 1500;
        public static string LastError { get; private set; } = "none";
        public static string LastReport { get; private set; } = "none";

        public static void Error(string where, Exception error)
        {
            if (error == null) return;
            string stack = error.StackTrace ?? "";
            string[] frames = stack.Split('\n');
            string top = string.Join(" | ", frames, 0, Math.Min(6, frames.Length)).Replace("\r", "").Trim();
            LastError = Clip(where + ": " + error.GetType().Name + ": " + error.Message + " @ " + top);
        }

        public static void Report(string status, string detail) =>
            LastReport = Clip((status ?? "?") + ": " + (detail ?? ""));

        static string Clip(string text) => text.Length > Limit ? text.Substring(0, Limit) : text;
    }
}
