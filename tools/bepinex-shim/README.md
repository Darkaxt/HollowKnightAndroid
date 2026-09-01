# bepinex-shim

`BepInEx.dll` and `0Harmony.dll`, as sources. Compiled on the device against
the depot's own assemblies, alongside `SilksongPatches`.

They are a **vocabulary and a runtime, not a loader**. A published BepInEx 5
plugin references these two assemblies by name, so they have to exist with the
right public surface or the plugin cannot be converted at all. What each part
does here:

| | |
|---|---|
| `HarmonyLib` attributes | read by `mod-weaver` at build time; inert at runtime |
| `Harmony.PatchAll` | no-op — the patches were already woven into the game's IL |
| `AccessTools`, `Traverse` | real reflection, works exactly as on a PC |
| `CodeInstruction`, `Transpilers` | present so transpiler-declaring plugins resolve; never called |
| `BepInEx.Logging` | real, into `UnityEngine.Debug` → logcat |
| `BepInEx.Configuration` | real, reads and writes BepInEx-shaped `.cfg` in `mods/config` |
| `UnityInput`, `IInputSystem` | real, over the legacy input manager |
| `BaseUnityPlugin` | real `MonoBehaviour` |
| `Chainloader.Start` | constructs each plugin at startup, in dependency order |

`Chainloader` is registered as a Unity runtime-initialize entry point by the
launcher, the same way `SilksongPatches` is.

Shapes matter as much as names. A published plugin was compiled against the
real BepInEx, so it calls `Chainloader.get_PluginInfos` and expects
`PluginInfo.Instance` to be a `BaseUnityPlugin` and `BepInPlugin.Version` to be
a `System.Version`. A field where BepInEx has a property, or `object` where it
has a type, is a member the plugin cannot resolve — and nothing notices until
il2cpp fails, seventeen minutes into a build.

## check.ps1

Which is what this is for:

```
make mod-check PLUGIN=path/to/Plugin.dll
```

It compiles both shims the way the device compiles them — against your depot
and Unity's Android player — stages them beside the game's assemblies, and runs
the real `mod-weaver` over the plugin. What comes back is the report the
launcher would show. About five seconds.

## Configuration Manager

[BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
is the settings UI most mods assume, and it works here: drop
`ConfigurationManager.dll` in the mods folder like any other plugin. It patches
nothing, so it needs no weaving — it reads every plugin's `ConfigFile` and
draws it.

It is opened with F1 on a PC. Here the chainloader binds a chord instead —
**click both sticks (L3+R3)** — and only when a plugin that can be opened is
installed; see `ModMenu.cs`. The chord is a setting in `mods/config/BepInEx.cfg`
like any other, read at startup, so it can be changed to any key or button
without a rebuild, and the menu lists it under Advanced settings. Beside it is
`Settings scale`, which is what makes a window written for a monitor readable
at arm's length: it grows the window, its columns and the skin's fonts while
the window is open, and puts the skin back when it closes.

The sticks are read twice: by number through the legacy input manager
(`JoystickButton8`/`9`, which is what a `KeyboardShortcut` can express), and by
name through the input system package, because Android numbers a pad's buttons
in the order it happens to report them and the two do not always agree.

We do not ship it: it is LGPL-3.0, which is redistributable, but a build that
compiles it into the game statically is a combined work with obligations
attached, and there is no reason to take them on when the user's own device
does that build from a file the user downloaded.
