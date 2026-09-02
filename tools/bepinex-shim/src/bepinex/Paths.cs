// Plugin DLLs are imported from one shared library, but every mutable runtime
// path belongs to the exact profile captured in GameProcessStartup. The game
// process never re-reads the launcher's selected-profile preference.

using System;
using System.IO;

namespace BepInEx
{
    public static class Paths
    {
        static string _root;
        static string _stateRoot;

        /// <summary>The shared library from which plugin DLLs were compiled.</summary>
        public static string PluginPath { get { return Root; } }

        /// <summary>Mutable state for the immutable launched profile.</summary>
        public static string ModStatePath { get { return StateRoot; } }

        /// <summary>Config files, isolated in the launched profile.</summary>
        public static string ConfigPath { get { return Path.Combine(StateRoot, "config"); } }

        public static string BepInExRootPath { get { return StateRoot; } }
        public static string BepInExAssemblyDirectory { get { return Root; } }
        public static string CachePath { get { return Path.Combine(StateRoot, "cache"); } }
        public static string PatcherPluginPath { get { return Path.Combine(Root, "patchers"); } }

        /// <summary>Where the game's own data is. Read-only here.</summary>
        public static string GameRootPath { get { return UnityEngine.Application.dataPath; } }

        public static string ProcessName { get { return UnityEngine.Application.productName; } }

        public static string ExecutablePath { get { return UnityEngine.Application.dataPath; } }

        public static string ManagedPath { get { return Path.Combine(UnityEngine.Application.dataPath, "Managed"); } }

        static string Root
        {
            get
            {
                if (_root != null) return _root;

                foreach (var candidate in Candidates())
                {
                    if (candidate == null) continue;
                    if (!Directory.Exists(candidate)) continue;
                    _root = candidate;
                    return _root;
                }

                // Nothing found: hand back the most likely one anyway, so that
                // a plugin writing a default config creates the folder rather
                // than throwing.
                foreach (var candidate in Candidates())
                {
                    if (candidate == null) continue;
                    _root = candidate;
                    return _root;
                }

                _root = "";
                return _root;
            }
        }

        static string StateRoot
        {
            get
            {
                if (_stateRoot != null) return _stateRoot;
                try
                {
                    using (var startup = new UnityEngine.AndroidJavaClass(
                        "dev.silksong.launcher.runtime.GameProcessStartup"))
                    {
                        var path = startup.CallStatic<string>("requireModStatePath");
                        if (string.IsNullOrEmpty(path))
                            throw new InvalidOperationException("launched profile mod state path is empty");
                        Directory.CreateDirectory(path);
                        _stateRoot = path;
                        return _stateRoot;
                    }
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        "could not resolve immutable launched-profile mod state", e);
                }
            }
        }

        static string[] Candidates()
        {
            string persistent = null;
            string external = null;
            try
            {
                persistent = Path.Combine(UnityEngine.Application.persistentDataPath, "mods");
                external = "/storage/emulated/0/Android/data/" +
                    UnityEngine.Application.identifier + "/files/mods";
            }
            catch (Exception)
            {
                // Called before the engine is up. The next caller gets a real
                // answer; nothing here is cached until one is found.
            }
            return new string[] { persistent, external };
        }
    }
}
