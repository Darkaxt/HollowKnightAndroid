# bepinex-shim

`BepInEx.dll` and `0Harmony.dll`, as sources. Compiled on the device against
the selected profile's depot assemblies, alongside `HollowKnightPatches` or
`SilksongPatches`.

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
| `BepInEx.Configuration` | real, reads and writes BepInEx-shaped `.cfg` in the immutable launched profile's `mods/config` |
| `UnityInput`, `IInputSystem` | real, over the legacy input manager |
| `BaseUnityPlugin` | real `MonoBehaviour` |
| `Chainloader.Start` | constructs each plugin at startup, in dependency order |

`Chainloader` is registered as a Unity runtime-initialize entry point by each
profile build, alongside that profile's patch assembly.

Shapes matter as much as names. A published plugin was compiled against the
real BepInEx, so it calls `Chainloader.get_PluginInfos` and expects
`PluginInfo.Instance` to be a `BaseUnityPlugin` and `BepInPlugin.Version` to be
a `System.Version`. A field where BepInEx has a property, or `object` where it
has a type, is a member the plugin cannot resolve — and nothing notices until
il2cpp fails, seventeen minutes into a build.

## check.ps1

Which is what this is for:

```
make mod-check PROFILE=hollow-knight \
  PLUGIN=path/to/Plugin.dll \
  DEPOT=path/to/the/profile/Managed \
  PACKAGES=path/to/that/profile/packages
```

The check compiles both shims against the production first-wins reference
sequence: Unity's `unityaot` class library, the Android player, then the exact
profile depot. It stages that same complete sequence for `mod-weaver`, then
applies the supplied production-built package directory as the override tier.
A plugin check fails closed when `PACKAGES` is absent rather than silently
resolving against the depot's desktop assemblies. It also verifies that the
Unity class library belongs to the selected profile (`6000.0.50f1` for
Silksong, `6000.0.61f1` for Hollow Knight), so pass that profile's fetched root
with `UNITY_ROOT` when it is not in the default cache. `PROFILE=silksong`
remains the default.

The reusable staged universe is isolated by profile and invalidated by the
.NET toolchain plus content digests of every input assembly and shim source;
a depot updated in place therefore cannot reuse stale resolution evidence.
Invoking `check.ps1` without `-Plugin` compiles and stages the two shims for a
profile without requiring package outputs.

## Configuration Manager

[BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
is the settings UI most mods assume, and it works here: drop
`ConfigurationManager.dll` in the mods folder like any other plugin. It patches
nothing, so it needs no weaving — it reads every plugin's `ConfigFile` and
draws it.

It is opened with F1 on a PC. Here the chainloader binds a chord instead —
**click both sticks (L3+R3)** — and only when a plugin that can be opened is
installed; see `ModMenu.cs`. The chord is a setting in
`profiles/<profile-id>/mods/config/BepInEx.cfg` like any other. `Paths.cs` gets
that root from the immutable `GameProcessStartup` snapshot, while plugin DLL
discovery remains shared. The setting is read at startup, can be changed without
a rebuild, and the menu lists it under Advanced settings. Beside it is
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
