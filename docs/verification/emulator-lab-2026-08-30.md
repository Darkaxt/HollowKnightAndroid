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
SHA-256:     927dade1e201d7d9a3a116f9c5ac02b1dc59d1d1595c26b09873690949799623
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
- `NOT-STARTED`: shared mod catalog, skin scanner/registry/activation, full
  production coordinator, and shared renderer stages.

Stage 6 still must prove the lab cannot enter the signed release pipeline and
repeat the production build/regression gates before the emulator specification
itself can be closed.
