# Dual Souls

Dual Souls is an Android launcher and on-device porting toolchain for
**Hollow Knight** and **Hollow Knight: Silksong**. It converts your own Linux
copy of each game into an Android ARM64 build, right on the device, and keeps
both games behind one app. It is built for dual-screen Android handhelds such as
the Ayn Thor: the game plays on the upper screen while the lower screen carries
the HUD, inventory, map and other pages.

This repository does not contain Team Cherry game files. Signed APKs are
published on the [Releases](https://github.com/Darkaxt/HollowKnightAndroid/releases)
page; the current release is **v2.0.78**.

<p align="center">
  <img src="docs/icon.png" alt="Dual Souls app icon, combining Hollow Knight and Silksong" width="200" />
</p>

<p align="center">
  <img src="docs/screenshots/launcher.webp" alt="The Dual Souls launcher with Hollow Knight and Silksong ready to play" width="900" />
</p>

## Features

### Two games, one launcher

- Hollow Knight and Silksong share one launcher and one installed app, with
  separate profiles, saves, settings, Mods, skins and generated game files.
- The port runs on the device from the game's Linux files. Choose the folder you
  copied them into, or sign in to Steam and let the launcher download the
  supported Linux depot.
- An interrupted port never replaces the last working build of a game.
- Optional Steam Cloud save sync, manual or automatic.

### The lower screen

Both games use the same lower-screen frame: the live HUD sits in a header band
and the pages slide underneath it, driven by touch or the controller. Each game
keeps its own art, text and rules.

- **Hollow Knight:** Inventory, Charms (equip and unequip on the lower screen),
  Guide, Journal and Map.
- **Silksong:** Inventory, Crest and Tools, Tasks, Journal and Map.
- The page tabs along the bottom are white glyphs in both games. Silksong
  uses its own inventory tab icons; Hollow Knight has none, so its tabs use
  white versions of its item art (nail, charm, map pin, hunter's mark, quill).
- Maps support pan, pinch and slider zoom, markers, and switching between the
  current area and the whole world.
- On the title screen and while paused, the lower screen hides the HUD and
  pages and shows the game's title logo. Both games use matching SteamGridDB
  logos in the same style.

<table>
  <tr>
    <td><img src="docs/screenshots/hk-play.webp" alt="Hollow Knight in Dirtmouth on the upper screen" /></td>
    <td width="40%"><img src="docs/screenshots/hk-lower.webp" alt="Hollow Knight lower screen: HUD header above the Charms page" /></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/ss-play.webp" alt="Silksong in Bone Bottom on the upper screen" /></td>
    <td width="40%"><img src="docs/screenshots/ss-lower.webp" alt="Silksong lower screen: HUD header above the Bone Bottom map" /></td>
  </tr>
</table>

#### Every lower-screen page

Hollow Knight, from a new save with the Mods unlocks applied:

<table>
  <tr>
    <th>Inventory</th>
    <th>Charms</th>
    <th>Guide</th>
    <th>Journal</th>
    <th>Map</th>
  </tr>
  <tr>
    <td><img src="docs/screenshots/hk-page-inventory.webp" alt="Hollow Knight lower screen: Inventory page" /></td>
    <td><img src="docs/screenshots/hk-page-charms.webp" alt="Hollow Knight lower screen: Charms page" /></td>
    <td><img src="docs/screenshots/hk-page-guide.webp" alt="Hollow Knight lower screen: Guide page" /></td>
    <td><img src="docs/screenshots/hk-page-journal.webp" alt="Hollow Knight lower screen: Journal page" /></td>
    <td><img src="docs/screenshots/hk-page-map.webp" alt="Hollow Knight lower screen: Map page" /></td>
  </tr>
</table>

Silksong:

<table>
  <tr>
    <th>Inventory</th>
    <th>Crest and Tools</th>
    <th>Tasks</th>
    <th>Journal</th>
    <th>Map</th>
  </tr>
  <tr>
    <td><img src="docs/screenshots/ss-page-inventory.webp" alt="Silksong lower screen: Inventory page" /></td>
    <td><img src="docs/screenshots/ss-page-crest.webp" alt="Silksong lower screen: Crest and Tools page" /></td>
    <td><img src="docs/screenshots/ss-page-tasks.webp" alt="Silksong lower screen: Tasks page" /></td>
    <td><img src="docs/screenshots/ss-page-journal.webp" alt="Silksong lower screen: Journal page" /></td>
    <td><img src="docs/screenshots/ss-page-map.webp" alt="Silksong lower screen: Map page" /></td>
  </tr>
</table>

#### Paused

The pause menu stays on the upper screen; the lower screen drops the HUD and pages and shows the game's title logo.

<table>
  <tr>
    <td><img src="docs/screenshots/hk-pause.webp" alt="Hollow Knight pause menu on the upper screen" /></td>
    <td width="40%"><img src="docs/screenshots/hk-pause-lower.webp" alt="Hollow Knight lower screen while paused: logo only" /></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/ss-pause.webp" alt="Silksong pause menu on the upper screen" /></td>
    <td width="40%"><img src="docs/screenshots/ss-pause-lower.webp" alt="Silksong lower screen while paused: logo only" /></td>
  </tr>
</table>

### Built-in Mods

Every game has a full Mods menu at **Pause → Options → Mods** (also on the
title screen). Settings are per game profile, apply live, survive relaunches and
can all be reset at once. The groups are:

- **World:** run speed, fast transitions, auto map, innate compass, bench
  teleport, secret radar, game speed.
- **Combat and encounters:** nail/needle damage, damage taken, damage cap,
  one-hit kills, unlimited Soul/Silk, enemy and boss health bars, damage
  numbers, boss retry.
- **Charms** (Hollow Knight) and **Crests & Tools** (Silksong): equip anywhere,
  free costs, unlimited notches or tool slots, unlocks.
- **Abilities, unlocks and economy:** grant abilities, unlock maps, stags or
  fast travel, Geo/Rosary magnet, multiplier and keep-on-death.
- **Assists:** noclip flight, no dash cooldown, no hazard damage, dash
  invincibility, infinite air jump, health regeneration, and (Silksong)
  Needolin Silk regeneration.
- **Save states and recall points:** save and load the whole game state to
  slots, or mark up to five places per save and return to them later.
- **Presentation and player:** disable the vignette, hero and scene colour,
  achievement pop-ups, an item picker, and an extra-mode unlock (Steel Soul,
  plus Godseeker in Hollow Knight).

Loadout presets (three per save) live in each game's own Start-menu
inventory: a selector beside the notches in the Hollow Knight Charms pane,
drawn with the game's own art, and a Preset button beside Change Crest (or in
its place, with one crest, pressed with Change Crest's own key) in the Silksong
Tools pane.
Switching keeps the current loadout in the preset you leave, and follows the
game's own bench rule (or EQUIP ANYWHERE).

Collectible pins (grubs, mask shards, vessel fragments, charms, roots, and the
Silksong equivalents) appear on the game's own map and on the lower-screen map,
only in rooms the player has mapped.

<table>
  <tr>
    <td><img src="docs/screenshots/hk-presets.webp" alt="Hollow Knight Charms pane with the preset selector beside the notches" /></td>
    <td><img src="docs/screenshots/ss-presets.webp" alt="Silksong Tools pane with the Preset button in Change Crest's place" /></td>
  </tr>
  <tr>
    <td colspan="2"><img src="docs/screenshots/hk-pins.webp" alt="Hollow Knight quick map of Forgotten Crossroads with collectible pins" /></td>
  </tr>
</table>

<p align="center">
  <img src="docs/screenshots/mods.webp" alt="The in-game Mods menu, World group, paused over Hollow Knight" width="900" />
</p>

### Skins

Dual Souls reads CustomKnight-style skin packs. Import them from a folder or an
archive in the launcher's **Skins** screen, then choose them in the game at
**Pause → Options → Skins**:

- **Mode:** off, one fixed skin, or rotate to a random skin on every death.
- **Sprites:** apply the skin to everything, to the character and HUD, or to the
  character only.
- Each save slot remembers its own skin. Hero colour can also change randomly on
  death.

<table>
  <tr>
    <td><img src="docs/screenshots/launcher-skins.webp" alt="The launcher Skins screen: in-game settings summary and installed skins" /></td>
    <td><img src="docs/screenshots/hk-skins-menu.webp" alt="Hollow Knight in-game Skins menu with five imported packs, Angel Knight enabled" /></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/hk-skinned.webp" alt="Hollow Knight with the Angel Knight skin applied" /></td>
    <td><img src="docs/screenshots/ss-skins-menu.webp" alt="Silksong in-game Skins menu with three imported packs, Hollow Hornet Knight enabled" /></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/ss-skinned.webp" alt="Silksong with the Hollow Hornet Knight skin applied" /></td>
    <td></td>
  </tr>
</table>

### Plugins (beta)

The launcher's **Plugins** screen can install BepInEx 5 mods and compile their
Harmony patches into the game build. See [Plugin support](#plugin-support-beta).

## Setup

1. Install the signed APK from the latest
   [release](https://github.com/Darkaxt/HollowKnightAndroid/releases).
2. Select Hollow Knight or Silksong in the launcher and press **Prepare**.
3. Choose the folder that holds the game's complete Linux files, or sign in to
   Steam to download them.
4. Press **Start porting**. The first port also downloads supporting tools, so
   it needs an internet connection and takes roughly 20 minutes on a
   Snapdragon 8 Gen 2. Later updates reuse the cached work and take a few
   minutes.
5. Keep the game's files where they are. They are needed for repairs and
   updates, and Silksong reads its large content from that folder while running.

<table>
  <tr>
    <td><img src="docs/screenshots/porting.webp" alt="The launcher porting Silksong on the upper screen" /></td>
    <td width="40%"><img src="docs/screenshots/porting-log.webp" alt="The porting activity log on the lower screen" /></td>
  </tr>
</table>

## Supported inputs

Only complete **Linux** builds are accepted. Windows and macOS builds are not
interchangeable, and files from different versions or platforms must not be
mixed.

| Game | Version | Unity |
| --- | --- | --- |
| Hollow Knight | `1.5.12620` | `6000.0.61f1` |
| Hollow Knight: Silksong | `1.0.29980` | `6000.0.50f1` |

Steam-assisted download is optional. You may instead select a complete,
legitimately acquired Linux installation.

## Plugin support (beta)

The launcher can import BepInEx 5 plugin folders and compile compatible Harmony
prefixes and postfixes into each game's Android build. Adding, removing, or
replacing a plugin DLL requires rebuilding that game; simple configuration and
enablement changes take effect after relaunching the game.

Open **Plugins** for the selected game, choose **Install a plugin from a
folder**, and select the plugin's complete folder. Supporting libraries and
packaged data are copied with it. The screen reports which plugins were built,
how many patches were applied, and which could not be compiled. Rebuilds are
incremental.

Shared imported plugins are stored under:

```text
Android/data/io.github.darkaxt.dualsouls/files/mods
```

Game-specific configuration and enablement are stored under:

```text
Android/data/io.github.darkaxt.dualsouls/files/profiles/<profile-id>/mods
```

Transpilers, runtime-computed patch targets, `Reflection.Emit`, and runtime DLL
discovery are not supported. A plugin working with one game does not imply that
it works with the other.

### Configuration Manager

Plugins that expose settings through
[BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases)
can use its BepInEx 5 release. Install its complete folder like any other
plugin; it is not bundled with this project. On Android, press **L3+R3** to open
or close it. Its binding and interface scale can be changed in the selected
profile's `BepInEx.cfg` file.

### Plugins tested with Silksong

- [BepInEx 5 + Configuration Manager](https://www.nexusmods.com/hollowknightsilksong/mods/26)
- [AutoMap](https://www.nexusmods.com/hollowknightsilksong/mods/31)
- [SaveScopedConfig](https://www.nexusmods.com/hollowknightsilksong/mods/1123)
- [Bonfire Teleport](https://www.nexusmods.com/hollowknightsilksong/mods/156)
- [Stakes of Marika – Rebirth Anywhere](https://www.nexusmods.com/hollowknightsilksong/mods/46) — partial; custom spawn points are not supported
- [Healthbar & Damage Show](https://www.nexusmods.com/hollowknightsilksong/mods/28)

Other plugins are not guaranteed to work. Many popular mods are already covered
by the built-in Mods menu, which needs no plugin at all.

## Steam Cloud saves

Steam sign-in can pull and push the selected profile's Steam Cloud saves, either
manually or automatically (see Settings). Save conflicts are reported rather
than silently choosing a side; keeping an independent backup is still
recommended.

QR-code and password sign-in use
[JavaSteam](https://github.com/Longi94/JavaSteam), an open-source Steam client.
The launcher does not store the Steam password. It keeps only Steam's issued
login token, encrypted in Android storage, and does not send it elsewhere.

## Project status

Dual Souls is feature-complete for its current plan: the dual-screen pages,
the built-in Mods catalog, skins with death rotation, and the mods accepted from
a review of the 100 most-downloaded Nexus mods for each game (released in
v2.0.77). Every release is checked on an Ayn Thor before it is published, and
the open questions for each feature are kept in the
[issue tracker](https://github.com/Darkaxt/HollowKnightAndroid/issues?q=is%3Aissue).

The product contract for the dual-screen UI, Mods and skins is
[`docs/DUALSCREEN-V3.md`](docs/DUALSCREEN-V3.md).

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
connected Android device is intended to be modified. Release APKs are built and
signed by GitHub Actions.

## Project history

This fork is based on
[`jakobkhansen/SilksongAndroid`](https://github.com/jakobkhansen/SilksongAndroid).
Its on-device toolchain, Vulkan player, dual-screen implementation, and
build-time BepInEx work form the foundation of this unified project. Nexus mods
were used as behaviour references only; the built-in Mods are this project's
own code.

The launcher artwork (grids, heroes, logos and game icons) and the lower-screen
title logos come from SteamGridDB and are credited in
[`docs/ui-source/steamgriddb`](docs/ui-source/steamgriddb/ATTRIBUTION.md).
Generative image tools are not used for project artwork.

## Legal

Hollow Knight, Hollow Knight: Silksong, their assets, and associated marks are
properties of Team Cherry. This unofficial project is not endorsed by Team
Cherry.

The repository contains build and adaptation tooling only. Users must supply
their own legitimately acquired game files. The tooling is MIT-licensed; see
[`LICENSE`](LICENSE). Shipped third-party open-source components and exclusions
are recorded in [`NOTICE.md`](NOTICE.md).
