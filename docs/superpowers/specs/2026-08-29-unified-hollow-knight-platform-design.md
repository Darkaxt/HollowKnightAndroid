# Unified Hollow Knight and Silksong Android Platform

**Status:** Approved; implementation resumed on 2026-08-30 with a local-first
POC before the ARM64 device gates.

**Date:** 2026-08-29

**Base revision:** SilksongAndroid `d504275847d5477155dfe5bdc2edf7db84339eb7`

## Purpose

Turn SilksongAndroid into one installed Android application that can provision
and launch either Hollow Knight or Hollow Knight: Silksong from user-supplied
Linux game files. The launcher, on-device build system, Vulkan player,
dual-screen foundation, mod library, and skin library are shared. Differences
between the games are isolated behind versioned game profiles and adapters.

The application remains a build system and launcher. It must not ship Team
Cherry game code, art, audio, Unity binaries, Steam credentials, or converted
game data.

## Evidence and constraints

The design is based on repository inspection and direct inspection of the
available game distributions:

- Silksong Linux `1.0.29980` uses Unity
  `6000.0.50f1-uum-100966-branch1`, records OpenGLCore and Vulkan, and stores
  its content in 2,068 Addressables bundles.
- Hollow Knight Linux `1.5.12612` uses Unity `6000.0.61f1`, records OpenGLCore
  and Vulkan, and stores its content in 501 level files and 501 corresponding
  shared-asset files rather than Addressables bundles.
- All 503 Hollow Knight serialized `.assets` files were parsed without error.
  The 125 shaders found across them all contained Vulkan slices. No shader
  lacked Vulkan data.
- Representative Hollow Knight `level0` and `level1` files parsed and
  retargeted to Android successfully when treated as serialized files.
- The Hollow Knight Windows build records Direct3D 11 and contains no Vulkan
  shader slices. Windows game files are therefore rejected for the Vulkan
  conversion path.
- SilksongAndroid currently targets `arm64-v8a` throughout: the APK, fetched
  .NET runtime, compiler, Unity static libraries, and produced IL2CPP library
  are all ARM64-specific.
- The locally running Android emulator is x86-64, while the connected AYN Thor
  is ARM64. The emulator cannot execute the existing native pipeline or game
  unchanged.

Hollow Knight Linux `1.5.12620` is the current implementation target. Its
source remains fail-closed until the completed archive passes the exact
manifest and Vulkan checks. Version `1.5.12612` is retained only as a
backward-compatibility reference; it is not the preferred production target.
Builds are never assembled by mixing files or assemblies from different game
versions or operating systems.

## Goals

1. Ship one launcher APK with one app icon and one package identity.
2. Let the user choose Hollow Knight or Silksong before entering Unity.
3. Provision, validate, build, repair, reset, and launch each game
   independently.
4. Reuse SilksongAndroid's on-device toolchain and Vulkan player architecture.
5. Add a classic-Unity-content converter for Hollow Knight without requiring
   an AssetRipper reconstruction.
6. Share mod discovery, skin discovery, storage, validation, selection, and
   configuration while keeping game-specific integration behind adapters.
7. Support multiple valid skin packs and optional rotation to a different
   skin after death and stable respawn.
8. Preserve the current dual-screen Silksong behavior and provide a shared
   rendering foundation on which Hollow Knight's screens can be rebuilt.
9. Keep all proprietary inputs and generated game data out of source control
   and release artifacts.
10. Provide reproducible, signed launcher releases after the implementation
    and hardware gates pass.

## Non-goals

- Binary compatibility between arbitrary Hollow Knight and Silksong mods.
- Reusing one compiled patch DLL across both games. Shared source is compiled
  separately against each game's own assemblies.
- Combining different Hollow Knight builds to obtain newer code and older
  Vulkan assets.
- Shipping or automatically publishing any user-owned game content.
- Retaining DualSouls' native GLES3/EGL compositor as the primary renderer.
- Treating emulator success as proof of Thor dual-display behavior, Adreno
  driver behavior, touch display attribution, thermals, or performance.
- Adding x86-64 game support solely to make the existing desktop emulator run
  the complete pipeline. That would create a second native toolchain and is
  outside the first implementation.

## Considered approaches

### 1. One package with versioned game profiles — selected

Keep SilksongAndroid's single-package model. The launcher runs in its existing
dedicated process. Each game owns a separate source pointer, build generation,
Unity engine directory, native library directory, data package, patches,
settings, saves, logs, and validation manifest. The launcher selects one
profile before starting the Unity game process.

This preserves the superior provisioning design, allows different Unity
versions and `libunity.so` files to coexist on disk, and avoids loading both
runtimes into one process. Switching games requires the current Unity process
to end before the other profile starts.

### 2. One launcher managing two installed game packages

This gives Android stronger isolation and reduces native-library switching
complexity, but creates three package identities, duplicated UI and update
coordination, separate permissions and storage, and a worse user experience.
It does not satisfy the desired single-application product.

### 3. Shared UI with DualSouls retained as a separate GLES3 runtime

This is the lowest-risk compatibility fallback, but it does not share the
Silksong Vulkan renderer or provisioning architecture. Direct inspection of
the Hollow Knight Linux files removed the shader blocker, so this fallback is
not the recommended foundation.

## Architecture

### Launcher and process model

`LauncherActivity` remains the only launcher entry point and continues to run
in the dedicated `:launcher` process. It gains a game picker and a selected
profile identifier. Setup, settings, logs, source selection, Steam operations,
and build status are all scoped to that profile.

The Unity activity remains in the main application process. A launch request
contains only the selected profile identifier. Before any Unity class or
native library is loaded, startup code resolves the profile's installed
generation and injects its Unity Java classes, native-library path, and data
package path. No game switch is attempted inside a live Unity process.

Quitting returns to the surviving launcher process. Selecting another game
starts a fresh Unity process with the other profile. Failure to terminate a
game process is reported as a state error; the design does not use a timeout
to force a switch.

### Game profile contract

Each supported game/build family implements a declarative `GameProfile` plus
small behavior adapters. The contract owns:

- stable profile identifier and display metadata;
- accepted product name, platform, depot, layout, and Unity-version rules;
- required and forbidden paths;
- source-manifest calculation and validation;
- Unity editor/player module descriptor and verified download hashes;
- content conversion strategy;
- assembly and native-plugin discovery;
- per-game patch source set and entry-point manifest;
- save location and optional Steam Cloud mapping;
- shared settings supported by the game;
- skin target mapping and lifecycle adapter;
- dual-screen data adapter and feature flags;
- installed-generation validation and repair rules.

Profiles may accept a range only when compatibility is demonstrated by
fixtures and runtime tests. Otherwise, a new game build receives an explicit
manifest entry. Unknown versions fail closed with a precise diagnostic rather
than being treated as the nearest supported build.

### Storage model

Shared immutable toolchains are stored once by content hash and Unity version.
Everything game-specific is namespaced:

```text
files/
  toolchains/<toolchain-id>/
  profiles/<game-id>/
    source.pointer
    current
    generations/<generation-id>/
      manifest.json
      engine/
      lib/arm64-v8a/
      data.apk
      patches/
    staging/<job-id>/
    logs/
  library/
    mods/
    skins/
```

The source depot remains in the user-selected location, matching the current
SilksongAndroid contract. Generated files live in private app storage.
Publication is atomic: a completed, verified staging generation is renamed
into `generations`, then `current` is updated. The previous valid generation
is retained until the replacement is proven launchable or the user explicitly
resets it.

### Provisioning pipeline

Both games use the same state machine:

1. Resolve the selected profile and source location.
2. Inventory files and calculate the source manifest.
3. Reject an unsupported platform, version, incomplete source, or mixed build.
4. Resolve and verify the exact Unity/toolchain components.
5. Compile shared and game-specific patch sources against the selected
   depot's assemblies.
6. Convert managed assemblies through IL2CPP and compile ARM64 native output.
7. Convert the game data using the profile's content strategy.
8. Assemble the profile's engine, native libraries, and `data.apk` generation.
9. Verify hashes, required entries, ABI, Unity stamp, graphics API, shader
   coverage, patch manifest, and package readability.
10. Publish the generation atomically and mark it ready.

Stages expose structured progress and heartbeat events. Cancellation requests
stop scheduling new work, allow the current atomic write to finish, remove
only the incomplete staging generation, and preserve source files and the
previous installed generation. No stage uses elapsed time as a cancellation
mechanism.

### Content conversion

Silksong retains its existing Addressables conversion: normalize the internal
Unity stamp, retain the Vulkan shader slice, retarget serialized bundle files
to Android, patch the catalog path, and keep the multi-gigabyte content tree
outside the APK.

Hollow Knight receives a classic-player converter that:

- discovers `globalgamemanagers`, `globalgamemanagers.assets`, `resources.*`,
  all `levelN` files, all `sharedassetsN.assets` files, and associated `.resS`
  and `.resource` sidecars;
- parses serialized files by content rather than filename extension;
- normalizes the Unity stamp only when the exact profile requires it;
- sets `BuildSettings.m_GraphicsAPIs` to Vulkan;
- sets every serialized file's target platform to Android;
- retains only the Vulkan shader slice while failing if any required shader
  lacks Vulkan data;
- installs Android-player built-in resources instead of desktop-player
  built-ins;
- preserves resource sidecars byte-for-byte and preserves path relationships;
- inventories native plugins and rejects unsupported desktop-only dependencies;
- reports video/audio formats and introduces conversion only where Android
  runtime testing demonstrates a need;
- packages the result using a ZIP64-capable, readable data layout rather than
  assuming the existing Silksong Addressables arrangement.

Classic conversion has three fail-closed phases. Discovery produces a
read-only inventory of normalized relative paths, content hashes, serialized
metadata, sidecars, media, native plugins, and diagnostics; it never claims
that an unknown source is supported. Validation compares that inventory with
the exact current profile manifest and is the only operation that can create a
validated layout. Conversion accepts only that validated layout. Manifest
entries explicitly say `TRANSFORM`, `COPY`, `EXCLUDE`, or
`REPLACE_AT_ASSEMBLY`, and sidecars name an owner or use the documented exact
basename rule. Raw roots plus inferred filename lists are never accepted by
the conversion seam.

Source, output, and report locations are canonicalized and pairwise disjoint.
Rooted/traversal paths, case collisions, numeric aliases such as `level1` and
`level01`, and reparse points fail validation. Desktop built-ins are reported
as `REPLACE_AT_ASSEMBLY`; their Android replacement and final ZIP64 packaging
remain the later packaging milestone rather than being copied by the classic
converter.

The classic converter is resumable by file content hash. It validates every
input and output serialized file, rather than treating a successful rewrite
of two representative scenes as proof of all 501 scenes.

### Patch and mod architecture

Shared runtime features live as source-level modules with narrow interfaces:

- launcher settings bridge;
- lifecycle event bus;
- skin registry and rotation policy;
- dual-screen presentation primitives;
- diagnostic logging;
- mod manifest and dependency resolution.

Each profile supplies an adapter assembly compiled together with those shared
sources against its own game assemblies. Private or internal game members are
accessed through an explicit adapter, reflection, or a documented Cecil
rewrite. Shared code must not directly reference Hollow Knight- or
Silksong-specific types.

External mods are discovered from the shared library but declare compatible
game identifiers, game-version ranges, dependencies, load order, and patch
entry points. A mod is compiled and enabled per profile. A failed mod build or
load cannot invalidate the base game generation; the launcher offers a safe
launch with external mods disabled.

### Skin packs and death rotation

The shared scanner treats each immediate child of an imported folder as a
candidate pack. Packs are never flattened into one namespace. A valid pack
contains a manifest with:

- schema version and stable pack identifier;
- display name and author metadata;
- compatible game identifiers and optional version constraints;
- per-game asset root and texture mapping;
- optional preview and attribution;
- content hash.

Import copies validated packs into the internal shared library. Duplicate
identifiers with different content require an explicit replacement decision;
duplicate texture basenames in different packs are harmless. Invalid packs
are listed with actionable reasons and do not block valid siblings.

Exactly one skin is active per game profile. The shared rotation policy owns
the ordered or shuffled candidate list, avoids immediately selecting the
current pack when another valid candidate exists, and persists its state. A
game adapter emits `playerDied` and `stableRespawn` lifecycle events. Rotation
selects the next skin after death but applies it only on stable respawn. An
application failure restores the previously working skin and records the
failed pack without breaking the save or game session.

A pack may support both games through separate `hollow-knight/` and
`silksong/` mappings. Sharing the library and schema does not imply that the
same textures fit both protagonists.

### Shared Vulkan dual-screen renderer

The shared renderer is extracted from Silksong's `DualScreenV2` implementation
into game-neutral presentation, display, input, theme, and diagnostic units.
It continues to use Unity multi-display with a second camera targeting display
1. Touch input uses the Input System's display attribution where available.

Game adapters provide state and screens, not rendering infrastructure.
Silksong initially preserves its existing map, inventory, crests, tasks, and
journal screens. Hollow Knight receives equivalent screens incrementally,
using DualSouls behavior as a functional reference without retaining its
native EGL compositor.

Single-display devices remain supported. The renderer must degrade to the
primary display without creating an unusable invisible UI path.

### Branding and launcher icon

The installed application must not reuse the Silksong-only icon. The final
adaptive icon combines Hollow Knight and Silksong visual identities in two
halves separated by a diagonal running from the bottom-left corner to the
top-right corner. Foreground artwork must remain legible within Android's
adaptive-icon safe zone under circle, squircle, rounded-square, and OEM masks.

Implementation must use original or appropriately licensed source artwork and
record attribution. The monochrome themed icon should communicate the same
two-part diagonal composition. Icon generation is a separately reviewed
visual task and is not part of this documentation change.

## Design traceability and stage gates

Every implementation milestone ends with a fresh cross-check against this
design document. The reviewer maintains
`docs/verification/design-traceability.md`, with one row for every numbered
goal, non-goal boundary, architecture contract, error-recovery rule, test gate,
and release gate.

Each row has exactly one state:

- `COMPLETE`: implemented and linked to current test, build, or device
  evidence;
- `BLOCKER`: required by the current or an earlier milestone but absent,
  incorrect, regressed, or unverified;
- `DEFERRED`: deliberately assigned to a later named milestone, with a reason,
  dependency, acceptance test, and target milestone;
- `NOT-STARTED`: permitted only for requirements whose planned milestone has
  not begun.

An unmet requirement cannot be described only in a progress summary. If its
planned milestone has ended, it must be either a blocker or a tracked
deferral. A deferral without a target milestone and acceptance test is a
blocker. A blocker prevents starting a dependent milestone and prevents a
release, although independent work may continue when the traceability record
shows that it cannot conceal or worsen the blocked contract.

The stage review also compares the working diff with the implementation plan,
records any intentional design change, checks that no proprietary content was
introduced, and confirms that the previous working Silksong and profile
generations remain recoverable. Design changes are made in this specification
before code begins to depend on them.

The initial host-only POC may implement the profile/storage primitives and the
classic converter/source validator before the Silksong device regression. It
is an independent workstream, not a completed milestone: the ledger records
the missing Silksong regression as a blocker, Tasks 5–7 may touch only new
profile/converter boundaries plus legal fixtures and read-only source copies,
and no device/toolchain-dependent milestone may begin until the normal gates
for Milestones 1 and 2 close.

## Error handling and recovery

- Source validation is read-only. The app never edits or deletes the user's
  depot.
- Every generated artifact is written to staging and verified before publish.
- Interrupted work resumes from verified content hashes or discards only its
  exact staging job.
- Unknown builds, missing Vulkan shaders, unsupported plugins, insufficient
  storage, and corrupt sidecars fail before replacing a working generation.
- Logs identify the profile, source manifest, stage, artifact, and recovery
  action without logging credentials.
- Reset actions are profile-scoped. Resetting Hollow Knight cannot remove
  Silksong, the shared skin library, saves, credentials, or another valid
  generation.
- Steam credentials and tokens retain the current encrypted-storage boundary.

## Testing and acceptance

### Host and unit tests

- Game-profile selection and manifest validation.
- Source-platform and Unity-version rejection.
- Classic serialized-file discovery and sidecar pairing.
- Vulkan shader coverage and target-platform rewrites using legal synthetic or
  generated fixtures.
- Atomic generation publication and recovery after injected failures.
- Mod and skin manifest parsing, compatibility, collision handling, and safe
  mode.
- Death-to-stable-respawn rotation state transitions.
- Launcher selection, settings isolation, and profile-scoped reset behavior.

### Emulator tests

The production APK cannot run on the existing x86-64 emulator because its
launcher runtime, fetched tools, Unity player, and IL2CPP output are ARM64.
Robolectric host tests cover launcher navigation, profile state, storage,
manifest parsing, import flows, error reporting, and lifecycle behavior through
fakes. An x86-64 emulator shell would require a deliberately separate test
artifact with the native pipeline replaced by fakes; it is optional and is not
accepted as evidence for the production pipeline.

An ARM64 Android environment may run broader integration tests if it supports
the required executable-files policy and Vulkan features. Emulator-only
success is never accepted as proof of multi-display or Adreno behavior.

### ARM64 device gates

The connected AYN Thor is the reference end-to-end device. Before a release,
each supported profile must prove:

1. clean provisioning from its supported Linux source;
2. exact Unity/toolchain resolution and successful on-device IL2CPP build;
3. boot to menu and entry into representative gameplay;
4. rendering, audio, video, input, save, load, suspend, and resume;
5. representative coverage across menus, map, boss, effects-heavy, and
   dependency-heavy scenes;
6. single-screen fallback and Thor dual-screen output;
7. touch attribution on the intended display;
8. base launch, safe launch, one shared mod, one game-specific mod, one skin,
   multiple-pack scanning, and death rotation;
9. switching games only after the prior Unity process exits;
10. update and rollback without loss of sources, saves, mods, skins, or the
    other profile.

### Release gates

- The repository and APK contain no proprietary game or Unity content.
- CI builds the launcher from a clean checkout and runs all non-proprietary
  tests.
- Release signing uses repository secrets and never writes private key
  material into source control, logs, or artifacts.
- Numeric APK `versionName`, `versionCode`, Git tag, release title, and APK
  filename agree.
- The published APK is downloaded again, signature-verified, hashed, installed
  over the previous release, and exercised on the launcher and both profiles.

## Delivery sequence

The implementation is divided into independently reviewable milestones:

1. Profile and storage foundations with Silksong behavior preserved.
2. Hollow Knight source validation and classic serialized-content converter.
3. Hollow Knight on-device Unity/IL2CPP build and first playable scene.
4. Game selection and reliable profile switching in one package.
5. Shared patch core and Hollow Knight adapter.
6. Shared skin library, scanner, activation, and death rotation.
7. Shared Vulkan dual-screen extraction and Hollow Knight screens.
8. Mod library and per-profile compilation/loading.
9. Launcher branding, accessibility, recovery UX, and final icon.
10. Signing CI, device regression, and release verification.

Each milestone must preserve the last working Silksong path. Failure of the
Hollow Knight spike stops Hollow Knight-specific expansion but does not require
discarding the profile, storage, testing, or launcher improvements.
