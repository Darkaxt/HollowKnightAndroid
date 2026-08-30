// Where the mods live, from inside the game.
//
// The launcher owns this folder: it is where a user drops a plugin and where
// the weaver reads them from. The game process needs the same answer, and gets
// it by looking rather than by being told -- the two run at different times
// and a path baked into the build at conversion time would be stale the moment
// Android moved the app's storage.
//
// Both candidates are the same directory by different names, and which one is
// spelled correctly depends on how Unity resolved persistentDataPath for this
// player. Trying both costs one File.Exists.

using System;
using System.IO;

namespace BepInEx
{
    public static class Paths
    {
        static string _root;

        /// <summary>Where plugin DLLs came from. Read at runtime for configs and data.</summary>
        public static string PluginPath { get { return Root; } }

        /// <summary>Config files, as .cfg, in the layout BepInEx uses on a PC.</summary>
        public static string ConfigPath { get { return Path.Combine(Root, "config"); } }

        public static string BepInExRootPath { get { return Root; } }
        public static string BepInExAssemblyDirectory { get { return Root; } }
        public static string CachePath { get { return Path.Combine(Root, "cache"); } }
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
