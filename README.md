# Hollow Knight Android

Hollow Knight Android is an experimental Android build system and launcher for
**Hollow Knight** and **Hollow Knight: Silksong**. It converts user-supplied
Linux game files for Android ARM64 and keeps both games behind one launcher and
package identity.

This repository does not contain Team Cherry game files and is not a finished
public release.

<p align="center">
  <img src="docs/icon.png" alt="Hollow Knight Android combined Hollow Knight and Silksong app icon" width="220" />
</p>

<table>
  <tr>
    <td><img src="docs/ui-source/steamgriddb/hollow-knight-grid-287022.webp" alt="Hollow Knight artwork" /></td>
    <td><img src="docs/ui-source/steamgriddb/silksong-grid-325379.webp" alt="Silksong artwork" /></td>
  </tr>
</table>

The supplied grids, heroes, logos, and individual game icons are preserved in
[`docs/ui-source/steamgriddb`](docs/ui-source/steamgriddb/ATTRIBUTION.md) for the
launcher and per-game shortcuts. Generative image tools are not used for
project artwork.

## Features

- One launcher and installed package for both supported games.
- On-device conversion and ARM64 compilation from user-supplied Linux files.
- Independent game profiles, saves, generations, mod settings, and launch
  state.
- Dual-screen companion support on compatible Android handhelds.
- Hollow Knight-style lower-screen HUD, pages, Mods controls, and skin support.
- Build-time BepInEx 5 and Harmony compatibility for a supported subset of
  managed mods.
- Atomic generation publishing so an interrupted build cannot replace the last
  working game generation.

## Supported inputs

Only complete **Linux** builds are accepted. Windows and macOS builds are not
interchangeable, and files from different versions or platforms must not be
mixed.

### Hollow Knight

- Supported version: `1.5.12620`
- Unity version: `6000.0.61f1`

The selected source remains necessary for repair, rebuilding, and updates.

### Hollow Knight: Silksong

- Supported version: `1.0.29980`
- Unity version: `6000.0.50f1`

Silksong reads its large Addressables content from the selected source folder at
runtime. Moving or deleting that folder will break the installed game profile.

Steam-assisted acquisition is optional. Users may instead select a complete,
legitimately acquired Linux installation.

## Mod support (beta)

The launcher can import BepInEx 5 mod folders and compile compatible Harmony
prefixes and postfixes into each game's Android generation. Adding, removing,
or replacing a plugin DLL requires rebuilding that game profile. Simple
configuration and enablement changes take effect after relaunching the game.

Open **Mods** for the selected game in the launcher, choose **Install a mod from
a folder**, and select the mod's complete folder. Supporting libraries and
packaged data are copied with the plugin.

Shared imported mods are stored under:

```text
Android/data/io.github.darkaxt.dualsouls/files/mods
```

Game-specific configuration and enablement are stored under:

```text
Android/data/io.github.darkaxt.dualsouls/files/profiles/<profile-id>/mods
```

Transpilers, runtime-computed patch targets, `Reflection.Emit`, and runtime DLL
discovery are not supported. A mod working with one game does not imply that it
works with the other.

### Configuration Manager

Mods that expose settings through
[BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases)
can use its BepInEx 5 release. Install its complete folder like any other mod;
it is not bundled with this project.

On Android, press **L3+R3** to open or close the Configuration Manager. Its
binding and interface scale can be changed in the selected profile's
`BepInEx.cfg` file.

### Mods tested with Silksong

The following mods have been tested with the supported Silksong profile:

- [BepInEx 5 + Configuration Manager](https://www.nexusmods.com/hollowknightsilksong/mods/26)
- [AutoMap](https://www.nexusmods.com/hollowknightsilksong/mods/31)
- [SaveScopedConfig](https://www.nexusmods.com/hollowknightsilksong/mods/1123)
- [Bonfire Teleport](https://www.nexusmods.com/hollowknightsilksong/mods/156)
- [Stakes of Marika – Rebirth Anywhere](https://www.nexusmods.com/hollowknightsilksong/mods/46) — partial; custom spawn points are not supported
- [Healthbar & Damage Show](https://www.nexusmods.com/hollowknightsilksong/mods/28)

Other mods are not guaranteed to work.

## Building from source

Prerequisites are an Android SDK, JDK 17 or newer, .NET 8, Git Bash on Windows,
and the Unity Android/editor components required by the supported game
versions.

The main host checks are:

```sh
make surgery
make weaver
make test
make check
```

`make dev` also packages and installs a development APK, so run it only when a
connected Android device is intended to be modified.

## Project history

This fork is based on
[`jakobkhansen/SilksongAndroid`](https://github.com/jakobkhansen/SilksongAndroid).
Its on-device toolchain, Vulkan player, dual-screen implementation, and
build-time BepInEx work form the foundation of this unified project.

## Legal

Hollow Knight, Hollow Knight: Silksong, their assets, and associated marks are
properties of Team Cherry. This unofficial project is not endorsed by Team
Cherry.

The repository contains build and adaptation tooling only. Users must supply
their own legitimately acquired game files. The tooling is MIT-licensed; see
[`LICENSE`](LICENSE). Shipped third-party open-source components and exclusions
are recorded in [`NOTICE.md`](NOTICE.md).
