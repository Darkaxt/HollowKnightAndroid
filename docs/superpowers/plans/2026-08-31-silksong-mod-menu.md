# Silksong Built-in Mods Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent, opt-in Silksong Mods menu that reproduces the safe Dual Souls tweak workflow through typed game APIs and a reusable shared controller.

**Architecture:** A game-neutral controller owns descriptors, persistence, master/reset semantics, and transactional rollback. `SilksongTweakAdapter` maps those descriptors to a narrow API, while `SilksongGameTweakApi` is the only unit allowed to reference Silksong classes. The current Silksong shell hosts the modal; Task 15 moves the already-neutral controller and interaction contract into the extracted shared shell and adds the Hollow Knight adapter.

**Tech Stack:** C# 9/netstandard 2.1, xUnit/net8.0 host tests, Unity uGUI, Silksong managed APIs, Gradle asset staging, PowerShell exact-depot compile check.

---

## Specification mapping and authorization

| Stage | Requirements | Objective | Evidence |
| --- | --- | --- | --- |
| 1 | TWEAK-01, TWEAK-03, TWEAK-04 | Shared controller, descriptors, persistence, rollback | Red/green xUnit controller tests |
| 2 | TWEAK-02, TWEAK-05, TWEAK-06 | Typed Silksong adapter and exact baseline restoration | Red/green adapter tests and exact `1.0.29980` compile |
| 3 | TWEAK-01, TWEAK-05 | Persistent shell trigger and touch-driven modal | Source contract tests, patch compile, emulator rendering |
| 4 | TWEAK-03, TWEAK-04, TWEAK-07 | Thor effect, restart, lifecycle, and vanilla-path proof | Device log, visual review, save hash/behavior matrix |
| 5 | TWEAK-06, TWEAK-07 | Remaining semantic parity features | One separately proven hook/save contract per capability |

Implementation authorization is `already_authorized` by the user's
2026-08-31 instruction to update the documentation and start working. No
additional permission prompt is required within this scope.

## File structure

- `tools/shared-patches/src/Mods/TweakContracts.cs`: game-neutral descriptors,
  adapter/store contracts, and action result.
- `tools/shared-patches/src/Mods/TweakController.cs`: persisted selections,
  master/reset behavior, validation, cycling, rollback, and tick dispatch.
- `tools/shared-patches/SharedPatches.Core.csproj`: host-buildable shared core.
- `tools/shared-patches-tests/TweakControllerTests.cs`: controller behavior.
- `tools/silksong-patches/src/mods/SilksongTweakAdapter.cs`: pure semantic
  mapping to a narrow Silksong API.
- `tools/silksong-patches/src/mods/SilksongGameTweakApi.cs`: exact game-type
  boundary and startup baseline.
- `tools/silksong-patches/src/dualscreen/DsModsScreen.cs`: Dual Souls-style
  grouped list/detail surface.
- `tools/silksong-patches/src/dualscreen/DsShell.cs`: modal registration,
  persistent trigger, containment, and active-page restoration.
- `src/SilksongLauncher.Launcher/app/build.gradle.kts`: stage shared sources
  into both profile patch sets.
- `tools/silksong-patches/check.ps1`: compile shared plus Silksong sources.
- `tools/hollow-knight-patches/HollowKnightPatches.csproj`: compile the neutral
  shared core with the Hollow Knight adapter assembly.
- `tools/ci/tests/test_profile_mod_pipeline.py`: packaged-source and menu
  registration contracts.

### Task 1: Build the shared controller with TDD

- [x] **Step 1: Write failing controller tests**

Create tests covering: master defaults OFF; corrupt values fall back to the
descriptor default; enabling applies only selected non-defaults; disabling
restores the captured baseline; a failed apply restores the baseline and
leaves master OFF; cycling persists only a successful value; reset keeps the
master selection but restores every descriptor default; controller recreation
restores both master and individual selections after a simulated process exit;
and two game prefixes do not share either state.

- [x] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter FullyQualifiedName~TweakControllerTests
```

Expected: compilation failure because `TweakController`, its contracts, and
the project do not exist.

- [x] **Step 3: Implement the minimum shared contracts and controller**

Use stable string IDs and values. `Initialize()` calls
`adapter.CaptureBaseline()` once, validates stored values, then applies the
stored set only if master was persisted. `SetMaster(false)` and every failed
transaction call `adapter.RestoreBaseline()`. No shared file references Unity
or either game.

- [x] **Step 4: Run focused and full shared tests and verify GREEN**

Run the focused command above, then:

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
```

Expected: all tests pass with zero warnings or skipped tests.

- [x] **Step 5: Reconcile Stage 1**

Re-read TWEAK-01, TWEAK-03, and TWEAK-04. Update the ledger below with test
counts and classify every gap before Stage 2.

### Task 2: Add the typed Silksong adapter with TDD

- [x] **Step 1: Write failing adapter mapping tests**

Use a recording fake `ISilksongTweakApi`. Assert the exact mappings:
`damage_received` cycles `vanilla`, `prevent_death`, `invincible`;
`one_hit_kills`, `unlimited_silk`, and `equip_anywhere` cycle `off`, `on`;
defaults restore their individual captured baseline; full restore restores all
four; and Tick refills only while unlimited Silk is effectively enabled.

- [x] **Step 2: Run the focused test and verify RED**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter FullyQualifiedName~SilksongTweakAdapterTests
```

Expected: compilation failure because the adapter and API contract are absent.

- [x] **Step 3: Implement the mapping and concrete game boundary**

The pure adapter contains no Unity calls. The concrete boundary maps to
`CheatManager.Invincibility`, `CheatManager.NailDamage`,
`CheatManager.IsSilkDrainDisabled`,
`CheatManager.CanChangeEquipsAnywhere`, and
`HeroController.RefillSilkToMaxSilent()`. It captures current static values
before the controller applies a selection. No native pointer or offset exists.

- [x] **Step 4: Verify GREEN and compile against the exact depot**

Run the focused adapter tests followed by:

```powershell
pwsh -NoProfile -File tools/silksong-patches/check.ps1 -Depot "D:\Temp\dualsouls-silksong-modmenu-audit\Managed"
```

This directory is the read-only pulled Managed set from the user-owned
Silksong `1.0.29980` source. Expected: adapter tests pass and the patch check
reports every source compiled.

- [x] **Step 5: Reconcile Stage 2**

Re-read TWEAK-02, TWEAK-05, and TWEAK-06. Record the exact assembly hash and
compile output. A missing or renamed typed symbol is a blocker.

### Task 3: Integrate the second-screen modal

- [x] **Step 1: Write failing source/packaging contract tests**

Extend `test_profile_mod_pipeline.py` to require shared source staging for both
profiles, `DsModsScreen` registration, no numeric-address/unsafe-memory API in
the tweak sources, and master default OFF.

- [x] **Step 2: Run the test and verify RED**

```powershell
python -m unittest tools.ci.tests.test_profile_mod_pipeline -v
```

Expected: failures for missing shared staging and modal registration.

- [x] **Step 3: Implement the modal and staging**

Add a persistent gear control to the Dual Souls-style HUD/status strip. It
opens the Mods overlay without changing `ActiveId`; Mods is not a peer tab.
Selecting a row updates the detail pane and tapping the selected row again
cycles it. Master OFF dims gameplay rows. Reset and apply errors remain
visible. Idle state closes and hides the modal. Every modal call uses the
shell's existing exception containment.

Stage `tools/shared-patches/src` beneath both on-device patch assets, include
it in the exact-depot checks, and add the shared xUnit suite to `make test`.

- [x] **Step 4: Run contracts, shared tests, and patch checks**

Run the unittest command, the full shared test project, the existing Android
host suite, and both exact-depot patch checks. Expected: all available host
gates pass; a missing real depot is recorded as a blocker, never as a skip that
proves compatibility.

- [x] **Step 5: Reconcile Stage 3**

Re-read TWEAK-01 and TWEAK-05. Record visible/interaction items still requiring
the emulator or Thor as blockers, not host-complete claims.

### Task 4: Device proof and recovery

- [ ] **Step 1: Build and install the same signed fork generation**

Preserve the current profile generations and saves. Rebuild only the Silksong
patch/player generation needed by the source change; do not wipe app data.

- [ ] **Step 2: Exercise the matrix on Thor**

Verify the modal, touch isolation, all four capabilities, master OFF, reset,
pause/resume, scene transition, and a real process exit/relaunch. Repeat the
relaunch check after setting different values in Hollow Knight so cross-game
isolation is observed rather than inferred. Record the save identity before
and after. Close the game after the matrix.

- [ ] **Step 3: Reconcile Stage 4**

TWEAK-03, TWEAK-04, and TWEAK-07 pass only with fresh evidence. Any partial
effect, lost setting, stuck global, input leak, crash, or save delta is a
blocker.

### Task 5: Parity expansion and final remediation

- [ ] **Step 1: Implement one independently proved capability at a time**

Proceed in this order: Needle multiplier, movement speed, money/shell-shard
multipliers, map/navigation helpers, health bars, damage numbers, boss retry,
teleport, save states. Start every capability with a failing adapter test and
an exact typed seam or documented Cecil rewrite.

- [ ] **Step 2: Run progression-sensitive gates where required**

Boss retry, teleport, map mutation, unlocks, and save states require the
ordinary/death/transition/save/reload/rollback matrix before their rows become
available.

- [ ] **Step 3: Final reconciliation and publication**

Re-read TWEAK-01 through TWEAK-07 and resolve every blocker and tracked
deferral. Only at zero blockers and zero deferrals run the full project gates,
update README and verification evidence, commit, push, and publish through the
existing GitHub signing pipeline.

## Reconciliation ledger

| Stage | Status | Satisfied requirements | Blockers | Tracked deferrals | Evidence |
| --- | --- | --- | --- | --- | --- |
| 0 — agreement/source audit | COMPLETE | TWEAK-02 design boundary; TWEAK-05 initial seams | None for planning | Runtime implementation and device proof go to Stages 1–4; parity expansion goes to Stage 5 | `silksong-mod-menu-source-audit.md`; assembly SHA-256 above |
| 1 — shared controller | COMPLETE | Shared portions of TWEAK-01, TWEAK-03, and TWEAK-04: defaults, validation, master/reset, fail-closed rollback, successful-change flush, game-qualified keys, recreation persistence, and visible failure when the store cannot flush | None for Stage 2 | Real process exit/relaunch goes to Stage 4 | Focused RED exposed swallowed flush failures; full GREEN passes 24/24 shared controller and adapter tests |
| 2 — Silksong adapter | COMPLETE | TWEAK-02 and initial TWEAK-05: four truthful capabilities, typed game seam, process-wide exact baseline, and no native memory APIs | None | TWEAK-06 parity rows beyond the first four remain gated in Stage 5 | 24/24 shared tests; 38 sources compile against Silksong `1.0.29980` `Assembly-CSharp.dll` SHA-256 `1AF095416B89F73993058F9CBAC3A93959D928314B735CC4ACBCA7BF1A952D2D` |
| 3 — second-screen modal | BEHAVIOR-COMPLETE/UI-REJECTED | The typed modal behavior keeps Mods out of the tab list and preserves master/reset/error containment, successful-change flush, and prior-page state. The `DsModsScreen`/`DsShell` presentation is rejected prototype work and does not satisfy the UI target | Re-present the existing controller through `DsPortMods` in Stage 8 of the authoritative Dual Souls UI port plan; then prove physical interaction | TWEAK-06 parity rows remain in Stage 5. No presentation refinement is deferred: the authored modal must be replaced | 9/9 profile contracts; 29/29 shared tests; Android host 120 tests with zero failures/errors and two environment skips; both exact profile patch compiles |
| 4 — device proof | IN-PROGRESS | Fork-signed launcher, exact Silksong `1.0.29980` atomic on-device build, separate ARM64 Unity process, gameplay, display-1 transport, and first-run master OFF are proved. The captured authored-shell appearance is not accepted visual evidence | Ported gear/modal touch, all four effects, process-exit persistence, post-port signed rebuild, and Hollow Knight isolation remain | None | `docs/verification/silksong-device-2026-08-31.md` |
| 5 — parity/final remediation | NOT-STARTED | None | TWEAK-06/TWEAK-07 parity work absent | None | Pending |
