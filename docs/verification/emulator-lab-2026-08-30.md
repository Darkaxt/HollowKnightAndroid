# Dual Souls Android emulator lab verification — 2026-08-31

## Scope

This run verifies only the `EMULATOR-FAKE` integration boundary defined by
`docs/superpowers/specs/2026-08-30-dual-souls-emulator-test-instance-design.md`.
It exercises the real launcher, game-profile registry, Android sandbox paths,
atomic generation publisher, selection store, and process topology while the
ARM64 Unity/Mono/IL2CPP boundary is replaced by deterministic synthetic
implementations.

It is not evidence for either real game, ARM64 native execution, Vulkan,
gameplay, the AYN Thor, data adoption, production signing, or release
readiness.

## Exact environment

```text
AVD:          DualSoulsLabApi35
AVD home:     D:\Android\avd
system image: system-images;android-35;google_apis;x86_64
device:       pixel_6
serial:       emulator-5554
API:          35
ABI:          x86_64
excluded:     bfa98654 (connected AYN Thor)
```

The checked-in script-contract suite proves exact create arguments, matching
definition and pointer validation, mismatched-definition rejection, exact AVD
name lookup, no first-device fallback, and unconditional refusal to return the
Thor serial. The real persistent AVD then validated and booted with the values
above.

## Command and result

```powershell
pwsh -NoProfile -File tools\emulator\Test-DualSoulsLab.ps1
```

Result: PASS.

- shared launcher host tests: 69 tests, zero failures/errors, one intentional
  skip;
- lab-runtime host tests: 6 tests, zero failures/errors/skips;
- Android instrumentation: 3 tests, zero failures;
- script contracts: PASS; and
- cold post-instrumentation UI/package/storage gate: PASS.

Gradle emitted the already-tracked warning that Android Gradle Plugin 8.7.3
was tested only through compileSdk 35 while the existing project compiles with
36. It did not change the result and is not hidden as a clean-warning claim.

## APK and package evidence

```text
file:        emulator-test-app-debug.apk
SHA-256:     2bbd3304d3a973cc343ce3a13c1e41fcbaa77aef5643a0f64f3abcd93a59cbf0
package:     io.github.darkaxt.dualsouls.emutest
label:       Dual Souls Lab
version:     0.1-lab / 1
min/target:  26 / 35
debuggable:  true
runtime:     io.github.darkaxt.dualsouls.lab.LabLauncherRuntime
ARM64 JNI:   none
```

The APK retains dependency JNI only for `armeabi-v7a`, `x86`, and `x86_64`;
it contains no `lib/arm64-v8a` entry. The explicit lab runtime metadata and
exact lab package identity are both present. Production remains the default
runtime and cannot request the alternate implementation through a debug,
emulator, model, or ABI heuristic.

The lab module has only a debuggable variant, declares no release signing
configuration, removes the production builder services, and uses the existing
checked-in user artwork without editing, reconstructing, or generating it.

## Integration proof

The three on-device tests prove:

1. both registered profiles render in one launcher; selection changes to
   Hollow Knight, survives activity recreation, and survives a clean
   `:launcher` process restart;
2. Hollow Knight and Silksong publish independent synthetic generations;
   injected Silksong failure leaves `lab-ss-1` current, the exact failed job is
   discarded, and resetting Hollow Knight preserves Silksong; and
3. the synthetic Hollow Knight `:game` process exits before the Silksong
   process starts, the new PID differs, and the clean-exit generation marker is
   reconstructed from disk.

After instrumentation ended, the wrapper cold-started the launcher and dumped
the rendered Android hierarchy. It independently found `Hollow Knight`,
`Hollow Knight: Silksong`, `TEST RUNTIME · EMULATOR-FAKE`, and the arrow-free
`Import saves` / `Export saves` actions. The package sandbox then contained the
surviving Silksong generation and current pointer but no Hollow Knight current
pointer.

No fixed sleep or elapsed-time cancellation is used for runtime assertions.
The AVD readiness loop reports heartbeats until observable boot/API/ABI state
matches.

## Icon observation

The Pixel Launcher screenshot initially appeared to put a pink circle around
Dual Souls. An uninstall/reinstall control moved the identical circle to Gmail
in the same predicted hotseat position, while Dual Souls rendered without it
in the app drawer. The circle is launcher UI for the suggested slot, not part
of the supplied icon. No icon artwork change was retained.

## Stage cross-check

Emulator Stage 5 is complete with no stage-local blocker or deferral. The
following parent-platform gaps remain classified rather than hidden:

- `BLOCKER`: production Hollow Knight conversion/generation/launch;
- `BLOCKER`: real Hollow Knight and Silksong mod/skin/lifecycle adapters;
- `BLOCKER`: ARM64 gameplay, Vulkan, audio/input/save, Thor dual-display, and
  update/adoption evidence;
- `BLOCKER`: final aligned tag, release candidate, reproducibility, install,
  update, and publication gates; and
- `NOT-STARTED`: shared mod catalog, skin scanner/registry/activation, and
  shared renderer stages.

## Stage 6 production and release isolation

The final regression used the pinned Gradle 8.11 launcher because this checkout
does not contain a Gradle wrapper:

```text
:app:testDebugUnitTest
:app:assembleRelease
:app:collectRuntimeDeps
:emulator-test-app:testDebugUnitTest
:emulator-test-app:assembleDebug
```

All tasks passed. The production release AAR is 12,726,684 bytes with SHA-256
`be02c43c1f0ba7f0f150f7890dbcf1db344b9ae76fc7674e844738cb18d6fd6b`.
Its 251 entries include 10 JNI libraries, all under `jni/arm64-v8a`, and 169
Mono BCL DLLs. The lab APK is 18,821,586 bytes with the SHA-256 above; its 290
entries contain zero ARM64 JNI and zero `assets/mono-bcl` files.

The unused duplicate `ProductionProvisioner` was removed. At this historical
Stage 6 checkpoint the production build still remained directly owned by
`SetupActivity`; parent Task 9 subsequently moved that orchestration behind the
shared atomic coordinator without enabling synthetic production provisioning.

Nine CI contract tests pass. Release selection now fails unless `build/`
contains exactly `DualSouls-<VERSION>.apk`, rejects every extra APK, requires
package `io.github.darkaxt.dualsouls`, and rejects `.emutest`. The signing
workflow contains no `:emulator-test-app` invocation. The lab module still has
no release variant or release signing configuration.

The Stage 6 boundary scan found zero forbidden APK/archive/private-keystore
paths among 213 tracked files and zero game-content/private-key signature
matches among either the 251 production AAR entries or 290 lab APK entries.

The emulator specification is closed with `blockers = 0` and
`tracked_deferrals = 0`. Parent-platform blockers listed above remain open and
are not relabeled as emulator evidence.

## Task 9 rerun — 2026-08-31

The guarded wrapper passed again on `emulator-5554` (API 35, x86-64): 84 app
host tests, six fake-runtime host tests, and all three instrumentation tests.
The fake runtime now writes the same complete, manifest-last generation schema
as production. The rendered launcher still proves both profiles, the explicit
`TEST RUNTIME` / `EMULATOR-FAKE` identity, and arrow-free `Import saves` /
`Export saves`; profile-scoped storage still retains Silksong after resetting
Hollow Knight. This remains emulator-fake evidence only.
