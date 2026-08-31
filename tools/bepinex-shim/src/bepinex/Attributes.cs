// The attributes a BepInEx plugin declares itself with, and the metadata the
// loader reads back out of them.
//
// Same reasoning as the Harmony attributes: a plugin DLL references these by
// name, so they must exist with these shapes for the assembly to resolve. The
// difference is that these ones are also read at runtime -- Chainloader finds
// plugins by looking for [BepInPlugin], exactly as BepInEx does.

using System;

namespace BepInEx
{
    /// <summary>Marks a class as a plugin, and names it.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class BepInPlugin : Attribute
    {
        public string GUID { get; protected set; }
        public string Name { get; protected set; }
        public string Version { get; protected set; }

        public BepInPlugin(string GUID, string Name, string Version)
        {
            this.GUID = GUID;
            this.Name = Name;
            this.Version = Version;
        }
    }

    [Flags]
    public enum DependencyFlags
    {
        HardDependency = 1,
        SoftDependency = 2,
    }

    /// <summary>
    /// Another plugin this one needs, or would like.
    ///
    /// Honoured for load order. Not honoured as a hard failure: on a PC a
    /// missing hard dependency stops the plugin loading, but here the whole
    /// set was already fixed when the game was built, and refusing to start
    /// something at that point helps nobody.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInDependency : Attribute
    {
        public string DependencyGUID { get; protected set; }
        public DependencyFlags Flags { get; protected set; }
        public string MinimumVersion { get; protected set; }

        public BepInDependency(string DependencyGUID, DependencyFlags Flags = DependencyFlags.HardDependency)
        {
            this.DependencyGUID = DependencyGUID;
            this.Flags = Flags;
            MinimumVersion = "";
        }

        public BepInDependency(string DependencyGUID, string MinimumDependencyVersion)
            : this(DependencyGUID)
        {
            MinimumVersion = MinimumDependencyVersion;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInIncompatibility : Attribute
    {
        public string IncompatibilityGUID { get; protected set; }

        public BepInIncompatibility(string IncompatibilityGUID)
        {
            this.IncompatibilityGUID = IncompatibilityGUID;
        }
    }

    /// <summary>
    /// Restricts a plugin to a named executable. There is one process here and
    /// it is the game, so this is recorded and ignored.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInProcess : Attribute
    {
        public string ProcessName { get; protected set; }

        public BepInProcess(string ProcessName)
        {
            this.ProcessName = ProcessName;
        }
    }

    /// <summary>What Chainloader knows about one loaded plugin.</summary>
    public class PluginInfo
    {
        public BepInPlugin Metadata { get; internal set; }
        public string Location { get; internal set; }
        public object Instance { get; internal set; }
        public Version TargettedBepInExVersion { get; internal set; }

        internal PluginInfo(BepInPlugin metadata, string location)
        {
            Metadata = metadata;
            Location = location;
            TargettedBepInExVersion = new Version(5, 4, 21);
        }
    }
}
