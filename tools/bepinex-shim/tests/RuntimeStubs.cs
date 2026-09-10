using System;
using System.Collections.Generic;

// Config persistence runs unchanged; only the Unity runtime dependencies are replaced.
namespace UnityEngine
{
    public enum KeyCode { None }

    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(object message) { Warnings.Add(message.ToString()); }
    }
}

namespace BepInEx
{
    public sealed class BepInPlugin
    {
        public string Name { get; set; }
        public string GUID { get; set; }
        public Version Version { get; set; }
    }

    public static class Paths
    {
        public static string ConfigPath { get { throw new NotSupportedException(); } }
    }

    public static class UnityInput
    {
        public static InputStub Current { get { throw new NotSupportedException(); } }
    }

    public sealed class InputStub
    {
        public bool GetKey(UnityEngine.KeyCode key) { throw new NotSupportedException(); }
        public bool GetKeyDown(UnityEngine.KeyCode key) { throw new NotSupportedException(); }
    }
}
