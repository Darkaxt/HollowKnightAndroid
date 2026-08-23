// Startup.
//
// On a PC the chainloader is the whole product: it loads DLLs off disk, builds
// a dependency graph and hands each plugin to Harmony. Here the loading and
// the patching both already happened, at build time, so what is left is the
// last step -- construct each plugin and let its Awake run.
//
// The launcher registers this method as a Unity runtime-initialize entry
// point, the same mechanism the port's own patches use. It is called once,
// before the first scene loads.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        /// <summary>Everything that started, by GUID.</summary>
        public static Dictionary<string, PluginInfo> PluginInfos = new Dictionary<string, PluginInfo>();

        /// <summary>The object plugins live on. Survives scene loads.</summary>
        public static UnityEngine.GameObject ManagerObject { get; private set; }

        /// <summary>Public, and oddly named, because BepInEx 5's own is:
        /// plugins do read it.</summary>
        public static bool _loaded;

        static readonly ManualLogSource _log = Logger.CreateLogSource("BepInEx");

        public static void Start()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                ManagerObject = new UnityEngine.GameObject("BepInEx_Manager");
                UnityEngine.Object.DontDestroyOnLoad(ManagerObject);
                ManagerObject.SetActive(false);

                var plugins = Order(Discover());
                _log.LogInfo("Loading " + plugins.Count + " plugin(s)");

                foreach (var type in plugins)
                {
                    var metadata = (BepInPlugin)Attribute.GetCustomAttribute(type, typeof(BepInPlugin));
                    try
                    {
                        var instance = ManagerObject.AddComponent(type) as BaseUnityPlugin;
                        if (instance == null)
                        {
                            _log.LogError("Could not construct " + type.FullName);
                            continue;
                        }
                        PluginInfos[metadata.GUID] = instance.Info;
                        _log.LogInfo("Loaded " + metadata.Name + " v" + metadata.Version);
                    }
                    catch (Exception e)
                    {
                        _log.LogError("Plugin " + metadata.GUID + " failed to load: " + e);
                    }
                }

                // Activating last means every plugin's Awake runs after all of
                // them have been added, so one plugin can find another's
                // component in its own Awake.
                ManagerObject.SetActive(true);
            }
            catch (Exception e)
            {
                _log.LogError("Chainloader failed: " + e);
            }
        }

        static List<Type> Discover()
        {
            var found = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!References(assembly)) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch (Exception) { continue; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract) continue;
                    if (!typeof(BaseUnityPlugin).IsAssignableFrom(type)) continue;
                    if (Attribute.GetCustomAttribute(type, typeof(BepInPlugin)) == null) continue;
                    found.Add(type);
                }
            }

            return found;
        }

        /// <summary>
        /// Whether an assembly could contain a plugin. Only plugins reference
        /// BepInEx, so this skips enumerating the game's own several thousand
        /// types. A resolution failure falls back to scanning it anyway.
        /// </summary>
        static bool References(Assembly assembly)
        {
            try
            {
                var name = assembly.GetName().Name;
                if (name == "BepInEx") return false;
                return assembly.GetReferencedAssemblies().Any(a => a.Name == "BepInEx");
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// Dependency order, by [BepInDependency]. A cycle, or a dependency on
        /// something that is not installed, is not fatal: the plugin still
        /// starts, just in whatever position it landed.
        /// </summary>
        static List<Type> Order(List<Type> plugins)
        {
            var byGuid = new Dictionary<string, Type>();
            foreach (var type in plugins)
            {
                var metadata = (BepInPlugin)Attribute.GetCustomAttribute(type, typeof(BepInPlugin));
                if (metadata != null && !byGuid.ContainsKey(metadata.GUID)) byGuid[metadata.GUID] = type;
            }

            var ordered = new List<Type>();
            var state = new Dictionary<Type, int>();

            Action<Type> visit = null;
            visit = type =>
            {
                int mark;
                if (state.TryGetValue(type, out mark) && mark != 0) return;
                state[type] = 1;

                foreach (BepInDependency dependency in
                    Attribute.GetCustomAttributes(type, typeof(BepInDependency)))
                {
                    Type other;
                    if (!byGuid.TryGetValue(dependency.DependencyGUID, out other)) continue;
                    if (other == type) continue;
                    int otherMark;
                    // A partially visited node is a cycle; leaving it alone
                    // breaks the loop and keeps the rest of the order valid.
                    if (state.TryGetValue(other, out otherMark) && otherMark == 1) continue;
                    visit(other);
                }

                state[type] = 2;
                ordered.Add(type);
            };

            foreach (var type in plugins) visit(type);
            return ordered;
        }
    }
}
