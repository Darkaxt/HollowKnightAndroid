# Dual Souls Emulator Test Instance

**Status:** Recommended approach approved on 2026-08-30; written design review
pending before implementation planning.

**Date:** 2026-08-30

**Parent design:**
`docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md`

## Purpose

Create a repeatable local Android integration environment for Dual Souls so
launcher, profile, storage, recovery, mod, skin, and death-rotation work can be
implemented and exercised without waiting for every ARM64 device gate.

The environment consists of:

1. a dedicated Android 15/API 35 x86-64 AVD named `DualSoulsLabApi35`; and
2. a clearly non-releasable `Dual Souls Lab` APK that packages the real
   launcher code and replaces only the ARM64 Unity/Mono/IL2CPP execution
   boundary with deterministic test implementations.

This design changes the breadth of the earlier development gate, not the
production architecture. The unresolved device preservation/adoption
regression still blocks claims about migration, production updates, real game
execution, release readiness, and publication. It does not block independent
host or fake-runtime integration work whose evidence is explicitly scoped.

## Evidence and constraints

- The host is Windows x64 and Windows Hypervisor Platform acceleration is
  available.
- Android SDK API 35 `google_apis/x86_64` is already installed.
- Existing local AVDs are x86-64. A new project-owned instance is required so
  tests do not depend on or mutate another project's emulator state.
- The production launcher AAR, fetched .NET runtime, on-device compiler,
  Unity player libraries, and IL2CPP output are ARM64-specific.
- The production launcher is an Android library whose AAR is merged into a
  hand-assembled Unity APK. The lab APK therefore needs a separate Android
  application shell rather than converting the production artifact into an
  x86 build.
- `LauncherActivity` and `SetupActivity` currently call concrete production
  services and reject the Hollow Knight classic-content profile. A narrow
  runtime seam is required before the same activities can drive deterministic
  lab behavior.
- The lab must not contain, download, or derive Team Cherry game content,
  Unity binaries, Steam credentials, or user saves.
- No image generation is permitted. The existing supplied project artwork is
  reused unchanged except for normal Android adaptive-icon masking already
  specified by the parent design.

## Approaches considered

### 1. Dedicated x86-64 AVD with a fake-native lab APK — selected

Use the installed accelerated x86-64 image. Package the same launcher
activities, resources, profile registry, stores, path rules, scanners, and
state machines as production. Select a lab implementation of a small runtime
interface through explicit lab-manifest metadata. The lab implementation
creates synthetic generations and launches a synthetic game activity; it does
not load or emulate ARM64 native libraries.

This gives fast, repeatable lifecycle and UI integration coverage while
keeping every native or hardware claim behind the real ARM64 device gates.

### 2. ARM64 system image under software emulation — rejected for the lab

This would be slower, is not backed by a matching installed modern Google API
image, and still would not reproduce the Thor's Adreno Vulkan driver,
dual-display topology, touch attribution, thermals, or performance. It may be
revisited only as an additional native smoke environment; it is not required
for the first implementation.

### 3. Add a complete x86-64 production toolchain — rejected

Porting the fetched Mono runtime, compiler, Unity player, native plugins, and
IL2CPP output to x86-64 would create and maintain a second game runtime. That
is outside the parent design and would spend substantial effort on an ABI the
target hardware does not use.

### 4. Reuse an existing AVD — rejected

The existing AVDs belong to other projects. Reusing one would couple evidence
to unknown snapshots, packages, permissions, and storage. The dedicated AVD
must be reproducible from a checked-in definition.

## Architecture

### Dedicated AVD

The checked-in PowerShell tooling will create or validate:

- AVD name: `DualSoulsLabApi35`
- system image: `system-images;android-35;google_apis;x86_64`
- device profile: Pixel 6 class
- persistent AVD home: `D:\Android\avd`
- clean project snapshot/state owned only by Dual Souls

The scripts set `ANDROID_AVD_HOME` explicitly and do not create symlinks. They
must discover the SDK from the normal Android environment, verify the exact
system-image package, and fail closed if an existing AVD with the chosen name
does not match the checked-in definition.

ADB commands must identify the emulator whose reported AVD name is exactly
`DualSoulsLabApi35`. They must never fall back to the first connected device,
so the connected AYN Thor cannot be selected accidentally. Readiness and
process transitions use observable state and heartbeat reporting rather than
cancellation timeouts or fixed sleeps.

### Lab application shell

Add an Android application module named `:emulator-test-app` beside the
existing `:app` Android library.

The lab application:

- uses application ID `io.github.darkaxt.dualsouls.emutest`;
- uses label `Dual Souls Lab` and an always-visible in-app `TEST RUNTIME`
  banner;
- depends on the existing launcher library, so Gradle merges the same
  activities, classes, resources, and manifest declarations into the APK;
- excludes the production AAR's ARM64-only JNI payload from the lab package;
- replaces the production `Application`, removes the production builder
  services, and supplies a synthetic game activity in the lab module;
- is debuggable, has no release signing configuration, and has no publishable
  release variant;
- is excluded explicitly from the signed release workflow and release asset
  discovery; and
- never shares a package, sandbox, or signer continuity claim with the
  production application.

The existing `:app` module remains the production AAR. Its `assembleRelease`
and `collectRuntimeDeps` contracts remain the source for the hand-assembled
ARM64 APK.

### Runtime seam

Introduce a narrow `LauncherRuntime` contract in the production launcher
library. It owns only operations whose current implementations cross the
native or production-process boundary:

- inspect the selected profile's provisioned state;
- provision or repair one profile while reporting structured progress;
- reset only generated state for one profile;
- produce the intent used to start that profile's game process;
- report stable game lifecycle events needed by launcher integration; and
- expose a runtime capability label for diagnostics.

`LauncherRuntimeProvider` defaults directly to the production implementation.
It loads an alternate class only when both conditions hold:

1. the application manifest contains the explicit lab runtime class metadata;
2. the installed package name is exactly
   `io.github.darkaxt.dualsouls.emutest`.

Missing, malformed, or unexpected metadata fails closed. Production does not
auto-detect emulators and does not select behavior from CPU ABI, device model,
or debuggability.

The lab implementation resides only in `:emulator-test-app`. Production code
must not contain a user-accessible fake-runtime switch.

### Synthetic provisioning and generations

The lab runtime uses the same `GameProfile`, `SelectedGameStore`,
`ProfileBuildPaths`, generation publisher, settings, mod/skin catalog, and
rotation state machines as production as those components are implemented.
Only file conversion, Unity acquisition, IL2CPP compilation, native library
loading, and actual game execution are synthetic.

Synthetic inputs are small generated files with deterministic hashes. A lab
provisioning run writes into the selected profile's real app-sandbox path and
publishes through the production atomic-generation API. Controlled test
commands can stop at each publication boundary or return a structured error,
allowing recovery to be tested without corrupting another profile or the last
valid generation.

No lab operation reads `G:\Game Installers`, Steam, a document-tree URI, or
production app data.

### Synthetic game process

`LabGameActivity` runs in the same dedicated game-process shape intended by
production and displays:

- selected profile ID and display name;
- active synthetic generation ID;
- enabled mod and skin IDs;
- current lifecycle and rotation state; and
- controls/test hooks for death, stable respawn, clean exit, and injected
  failure.

The death and stable-respawn signals pass through the same lifecycle adapter
and skin-rotation state machine that production adapters will use. The lab
therefore proves orchestration and persistence, but not that a game's real
managed patches detect every gameplay death boundary correctly.

Switching profiles is permitted only after the previous synthetic game
process is observably gone. Tests record the old and new PIDs and fail if the
launcher starts the second profile while the first process remains alive.

## Test matrix

### AVD and packaging contract

- Recreate or validate the exact AVD definition.
- Build the lab APK from a clean checkout.
- Prove the APK supports x86-64 installation and contains no project-owned
  ARM64 Mono, monohost, Unity, or IL2CPP libraries.
- Prove package ID, label, debuggability, and explicit lab-runtime metadata.
- Prove the production release task still produces the ARM64 AAR and that the
  signed-release workflow cannot select the lab APK.

### Launcher and profile integration

- Fresh launch shows a choice between Hollow Knight and Silksong before a
  game process starts.
- Selection survives activity recreation and process restart.
- Each profile receives distinct generation, settings, reset, save-test,
  log, mod, and skin roots.
- Switching in both directions records the prior synthetic PID as gone before
  the next process starts.
- Reset of one profile preserves the other profile and shared libraries.

### Recovery integration

- Successful synthetic provisioning atomically publishes one generation.
- Failure before publication leaves the previous current generation intact.
- Failure during staged publication is resumable or discardable by exact job
  identity.
- App restart reconstructs state from disk rather than relying on process
  memory.

### Feature integration as milestones land

- Mod discovery, compatibility, dependency, conflict, and safe-mode behavior.
- Skin scanning of immediate child pack roots, including one invalid sibling
  and duplicate IDs without tree flattening.
- Independent active-skin selection per profile.
- Death selects a different eligible skin and stable respawn applies it.
- Repeated death before stable respawn, disabled rotation, one-pack catalogs,
  corrupt packs, and apply failure retain or restore the last valid skin.

Android instrumentation uses idling/state synchronization rather than fixed
sleeps. Host tests remain the faster first layer; the AVD exercises merged
manifest, package sandbox, activity/process lifecycle, storage, and rendered
UI behavior that Robolectric cannot prove by itself.

## Evidence boundaries

Lab evidence may close only rows explicitly labeled `HOST` or
`EMULATOR-FAKE`. It may prove:

- launcher rendering and navigation;
- profile selection and persistence;
- Android package/process lifecycle;
- profile-scoped path and reset isolation;
- atomic publication and recovery orchestration;
- mod/skin scanning and state-machine integration; and
- switching order for the synthetic game process.

Lab evidence cannot close or weaken these `ARM64-DEVICE` gates:

- exact production Unity component execution;
- on-device ARM64 IL2CPP conversion and linking;
- Hollow Knight or Silksong menu/gameplay boot;
- real patch/mod/skin adapters against the games;
- Vulkan rendering, Adreno driver behavior, audio, video, or performance;
- Thor single/dual-display output and touch attribution;
- production saves, Steam credential continuity, adoption, update, rollback,
  or signer migration; and
- release publication.

Task 8 is therefore split by evidence type. Descriptor parsing, exact version
and hash selection, cache namespacing, and wrong-hash isolation are host work
and may proceed. Fetching or executing production components and asserting an
actual toolchain works remain ARM64-device work.

## Failure and safety behavior

- A mismatched AVD definition is reported; it is never silently reused.
- Lab reset targets only the exact lab package or exact AVD name.
- No cleanup command targets an unresolved environment variable, wildcard
  device, shared SDK, existing project AVD, or the Thor.
- Synthetic failure injection is explicit and disabled by default.
- A lab/runtime mismatch is fatal and diagnostic; it never falls back from a
  failed production runtime to the fake runtime.
- Logs identify all lab evidence as `EMULATOR-FAKE`.

## Implementation stages

1. Add the runtime seam with production behavior unchanged and host contract
   tests.
2. Add the lab application module and packaging guard tests.
3. Add reproducible AVD create/start/identify scripts on `D:`.
4. Add synthetic provisioning, generation, and game-process behavior.
5. Add instrumentation for the currently implemented launcher/profile/storage
   contracts.
6. After every later feature stage, extend the emulator matrix, cross-check
   the parent design, and classify missing requirements as `BLOCKER`,
   `DEFERRED`, or `NOT-STARTED` in the traceability ledger.

Each stage must keep production host/build tests green and must not claim a
device gate. Implementation planning begins only after this written design is
reviewed.

## Acceptance criteria

The emulator-test-instance stage is complete only when:

1. `DualSoulsLabApi35` can be created or validated repeatably from checked-in
   tooling and its persistent data resides under `D:\Android\avd`;
2. the exact named emulator is selected without addressing the Thor;
3. the lab APK installs and launches on API 35 x86-64;
4. both registered profiles can be selected and persist independently;
5. synthetic provisioning, failure recovery, reset isolation, and cold
   process switching pass instrumentation tests;
6. the APK and UI identify themselves as a fake test runtime;
7. the production AAR/APK build contract and signed pipeline remain unchanged;
8. release automation cannot sign or publish the lab APK; and
9. the traceability ledger records every remaining native, device, gameplay,
   feature, adoption, signing, and release requirement with its proper state.
