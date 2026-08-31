# Hollow Knight Android

Hollow Knight Android is an experimental, unified Android build system and
launcher for **Hollow Knight** and **Hollow Knight: Silksong**. It takes
user-supplied Linux game files, converts and compiles them for Android ARM64,
and keeps both games behind one launcher, package identity, toolchain library,
mod library, and—eventually—skin library.

This repository is an implementation checkpoint, **not a finished public release**.
The exact Hollow Knight `1.5.12620` Linux build now completes on-device
conversion, ARM64 compilation, packaging, installation, first boot, and the
playable-room gate on the AYN Thor. The fork-signed Silksong `1.0.29980`
generation also builds and reaches live dual-display gameplay. Those runs
prove the direct Unity display path. The separately authored companion UI is
now rejected prototype work: production is being corrected to a module-by-
module port of Hollow Knight Dual Souls' bottom-screen composition using
Silksong's resident UI objects and assets.

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
planned launcher redesign and per-game shortcuts. Generative image tools are
not used for project artwork.

## Current status

| Area | Current evidence | Remaining gate |
|---|---|---|
| Hollow Knight `1.5.12620` | Exact Linux manifest accepted; 1,005 serialized files converted with complete Vulkan coverage; 267,628,408-byte AArch64 `libil2cpp.so` built; a ZIP32 base APK plus main OBB generation was independently hash-verified, mounted, and played through the first room on the Thor | Save reload, representative-scene coverage, shared input adapter, and later mod/skin/dual-screen gates remain |
| Silksong `1.0.29980` | Fork-signed `1.0.3` built the exact current source on the Thor, atomically published a verified ARM64 generation, reached gameplay, and rendered the redesigned companion directly on both physical displays | Prove lower-panel touch, effects, restart persistence, and two-game switching |
| One launcher | Both profiles, independent storage, atomic generations, and cold-process selection exist and have host/emulator coverage | Prove both switch directions with the production ARM64 Unity processes |
| Mods | The parent's build-time BepInEx 5/Harmony weaver is merged and routed through each selected profile's patch, IL2CPP, generation, and status paths | Real plugin compatibility for each game, compatibility manifests, dependencies, load order, and per-profile enablement remain unverified |
| Built-in tweaks | A shared, game-qualified controller hosts a persistent Mods overlay from the standalone HUD gear. Silksong `1.0.29980` exposes typed damage mode, one-hit kills, unlimited Silk, and equip-anywhere adapters; the standalone gear and first-run master OFF are proved in the signed production process | Prove physical gear/overlay touch, effects, process-exit persistence for each game, the Hollow Knight adapter, and later parity rows |
| Skins | Shared death/respawn rotation state machine has host coverage | Pack scanner/import, persistence, real game lifecycle hooks, texture adapters, rollback, and both-game runtime proof |
| Dual screen | Silksong's direct Vulkan display-1 transport is proved in signed live gameplay. Stage 1 host contracts verify the source boundary: `DualScreenV2` still owns transport/hot-plug/pause/input lifecycle and creates `DsPortRuntime` only after presentation readiness; that runtime owns empty-root visibility, idle state, scene revision, and disposal on layers 6/3. No device or UI parity is claimed | Port Dual Souls' frame, resident HUD, Map, Inventory, Loadout, selection, overlays, fades and composition behavior onto the empty roots; then close the side-by-side Hollow Knight/Silksong matrix |
| Releases | Fork identity and GitHub signing pipeline have a signed dry-run proof | No release until source reproducibility, device, gameplay, migration, tag/version, and fresh-download gates pass |

The detailed requirement ledger is
[`docs/verification/design-traceability.md`](docs/verification/design-traceability.md).
The current Hollow Knight device evidence is
[`docs/verification/hollow-knight-first-boot.md`](docs/verification/hollow-knight-first-boot.md).
The corrected bottom-screen port contract, plan, and source matrix are recorded
in
[`docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md`](docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md),
[`docs/superpowers/plans/2026-08-31-dual-souls-ui-port.md`](docs/superpowers/plans/2026-08-31-dual-souls-ui-port.md), and
[`docs/verification/dual-souls-ui-port-matrix.md`](docs/verification/dual-souls-ui-port-matrix.md).
The typed Silksong tweak seams and current host-only proof are recorded in
[`docs/verification/silksong-mod-menu-source-audit.md`](docs/verification/silksong-mod-menu-source-audit.md).
The signed Silksong production run and blocking companion-layout finding are
recorded in
[`docs/verification/silksong-device-2026-08-31.md`](docs/verification/silksong-device-2026-08-31.md).
The isolated API 35 launcher run is recorded in
[`docs/verification/emulator-lab-2026-08-31.md`](docs/verification/emulator-lab-2026-08-31.md).

## Design

- One installed package: `io.github.darkaxt.dualsouls`.
- One dedicated launcher process; only one Unity game process is loaded at a
  time.
- Exact, fail-closed profiles for source validation, Unity version, conversion,
  patches, saves, and feature adapters.
- Independent profile generations under private app storage, published
  atomically after verification.
- Shared immutable toolchains by Unity version and content hash.
- One Dual Souls bottom-screen composition across both games: Hollow Knight's
  frame, hierarchy, navigation, selection, modal, and lifecycle patterns;
  Silksong's resident sprites, fonts, data, terminology, and additional pages;
  and SilksongAndroid's direct-display transport.
- No Team Cherry game files, Steam credentials, generated game data, Unity
  binaries, or private signing keys in this repository or its release APK.

The approved architecture is documented in
[`docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md`](docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md).

## Supported inputs

Only complete **Linux** builds are accepted. Windows and macOS builds are not
interchangeable inputs, and files from different versions or platforms must
never be mixed.

### Hollow Knight

- Production target: `1.5.12620`, Unity `6000.0.61f1`.
- `1.5.12612` is retained only as a backward-compatibility reference.
- The classic player layout is copied into the generated private player image;
  the original source remains necessary for repair, rebuild, and updates.

### Silksong

- Current profile: `1.0.29980`, Unity `6000.0.50f1` branch build.
- Its multi-gigabyte Addressables content remains in the selected source folder
  and is read there at runtime, so moving or deleting that folder breaks the
  installed generation.

Steam-assisted acquisition remains optional. Users may instead select a
complete, legitimately acquired Linux installation.

## Mods

The newly merged parent implementation supports a useful subset of BepInEx 5
plugins by weaving Harmony prefixes and postfixes into managed assemblies
**before** IL2CPP conversion. There is no runtime JIT, so adding, removing, or
replacing a DLL requires rebuilding the selected game profile. Configuration
and enable/disable gates are read at launch.

Development builds use the shared folder:

```text
Android/data/io.github.darkaxt.dualsouls/files/mods
```

Transpilers, runtime-computed patch targets, `Reflection.Emit`, and runtime DLL
discovery cannot work in this architecture. The weaver reports unsupported
patches before native compilation. Until the remaining Task 14 contracts are
implemented, do not treat a plugin working in one game as evidence that it is
compatible with the other.

## Building and verification

Prerequisites are an Android SDK, JDK 17 or newer, .NET 8, Git Bash on Windows,
and the exact Unity Android/editor components resolved by the game profiles.

The core host gates are:

```sh
make surgery
make weaver
make test
make check
```

`make dev` additionally packages and installs a development APK, so use it only
when device mutation is intended. The GitHub Actions signing workflow uses the
fork's pinned package identity and signing certificate; it does not publish a
release until explicitly dispatched and all release gates are satisfied.

## Project history and upstream

This fork is based on
[`jakobkhansen/SilksongAndroid`](https://github.com/jakobkhansen/SilksongAndroid).
Its on-device toolchain, Vulkan player, dual-screen implementation, and
build-time BepInEx work are the foundation being generalized into the unified
platform.

## Legal

Hollow Knight, Hollow Knight: Silksong, their assets, and associated marks are
properties of Team Cherry. This unofficial project is not endorsed by Team
Cherry.

The repository contains build and adaptation tooling only. Users must supply
their own legitimately acquired game files. The tooling is MIT-licensed; see
[`LICENSE`](LICENSE). Shipped third-party open-source components and exclusions
are recorded in [`NOTICE.md`](NOTICE.md).
