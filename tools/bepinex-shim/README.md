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
| `BaseUnityPlugin` | real `MonoBehaviour` |
| `Chainloader.Start` | constructs each plugin at startup, in dependency order |

`Chainloader` is registered as a Unity runtime-initialize entry point by the
launcher, the same way `SilksongPatches` is.
