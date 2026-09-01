# Hollow Knight-First Dual Souls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement this plan task by task. Steps use
> checkbox (`- [ ]`) syntax for tracking. Every stage must be reconciled
> against the authoritative specification before the next stage begins.

**Goal:** Run Hollow Knight's existing Dual Souls HUD, companion pages, Mods,
and multi-pack skin behavior through the proven direct-display technology,
then extract shared contracts and adapt Silksong to that working reference.

**Architecture:** SilksongAndroid contributes only the Unity display-1,
camera/layer, input, lifecycle, diagnostics, and fallback technology. Hollow
Knight is the first production consumer and retains the existing Dual Souls
composition and behavior. Shared UI, Mods, and skin contracts are extracted
only after the complete Hollow Knight slice passes on the AYN Thor; Silksong
then implements those contracts with resident game objects and semantic
extensions.

**Tech Stack:** Unity 6000.0.61f1/6000.0.50f1 IL2CPP, C# patch injection,
Unity multi-display and Input System, Kotlin launcher/profile storage, Python
source contracts, .NET tests, PowerShell exact-source compile checks, Android
Gradle/Robolectric, ADB on `bfa98654`, and GitHub Actions signing.

**Authority:**
`docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md` and
`docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md`.

**Authorization state:** Runtime implementation, host verification, signed
emulator/device verification, commit, push, and the final release workflow are
authorized. Do not interact with a locked physical device; otherwise continue
until completion, a genuine blocker, or an explicit pause request.

---

## Non-negotiable execution order

1. Hollow Knight direct-display transport.
2. Hollow Knight Dual Souls HUD and all existing companion modules.
3. Hollow Knight Mods and tweak persistence.
4. Hollow Knight multi-pack skins and death rotation.
5. Complete Hollow Knight device acceptance.
6. Shared-contract extraction with unchanged Hollow Knight regression.
7. Silksong resident-object adaptation.
8. Cross-game acceptance and release.

No Silksong UI implementation may pass a stage before item 5 is complete.
The uncommitted `DsProbe.cs` and its tests are preserved as parked successor
work. They must be validated before later use, but they are not part of the
Hollow Knight critical path.

## Batch-first verification cadence

Port each complete Hollow Knight stage from the pinned Dual Souls source before
starting signed runtime debugging. During the source pass, use fast contracts,
reference diffs, and exact `1.5.12620` compilation; do not create a signed APK
for individual visual or lifecycle corrections. After the complete stage is
present and host-green, run one signed integration matrix, record every defect,
fix that defect set as one batch, and repeat only the affected acceptance rows.

Specification reconciliation still occurs after every H stage. The stage is
the review boundary: a one-line placement correction is not a separate stage
and must not trigger its own documentation, signing, installation, and IL2CPP
cycle. A device cycle before the source-complete gate is permitted only when a
host-invisible platform question genuinely blocks further migration.

Every device cycle from H2 through release is feature-targeted and uses no
gameplay testing. An in-game room or save is not a permitted validation host,
even if the character would remain unmoving. Validation must use a title/menu
fixture, host contracts, controlled debug injection, or synthetic state instead
of navigation, combat, collection, progression, or save mutation through play.
Close the game after each affected-row capture.

This rule applies to every pass, including first checks, defect rechecks,
lifecycle checks, and release-candidate checks. Starting a new game, selecting a
save, skipping an intro, entering a room, or reusing an already-loaded gameplay
state is never part of a validation pass. If a production object only exists
after scene bootstrap, the fixture must create the minimum required state
through an explicit input-disabled, save-neutral debug seam; it must not obtain
that state through played input or a user save.

## File structure and ownership

### Direct-display technology

- Create `tools/shared-patches/src/DualScreen/DirectDisplayHost.cs`: display
  activation, readiness, presence changes, pause/resume, and ordered teardown.
- Create `tools/shared-patches/src/DualScreen/DirectDisplayPresentation.cs`:
  display-1 cameras, private layers, measured panel geometry, mask isolation,
  and single-display fallback.
- Create `tools/shared-patches/src/DualScreen/DirectDisplayTouch.cs`:
  display-attributed touch collection and primary-display fencing.
- Create `tools/shared-patches/src/DualScreen/IDirectDisplayContent.cs`: minimal
  lifecycle contract consumed by a game adapter.
- Modify `tools/silksong-patches/src/dualscreen/DualScreenV2.cs`,
  `DsPresentation.cs`, and `DsTouch.cs` only to delegate proven transport
  responsibilities; do not move Silksong composition into shared code.

### Hollow Knight reference implementation

- Add the MIT-licensed source modules from `igawa6/dualsouls` commit
  `5c22451435b772acde0c7e6456f9019bc1baef73` beneath
  `tools/hollow-knight-patches/src/dualsouls/`.
- Preserve the original module split:
  `HKDualScreen.cs`, `HKDualScreen.Util.cs`,
  `HKDualScreen.Bottom.Layering.cs`, `Bottom.Frame.cs`, `Bottom.Hud.cs`,
  `Bottom.Inventory.cs`, `Bottom.Charms.cs`, `Bottom.Map.cs`,
  `Bottom.Select.cs`, and `Bottom.Tweaks.cs`.
- Add `HkDirectDisplayAdapter.cs` solely for the new transport seam.
- Add `HKTweaks.cs`, `HKModsMenu.cs`, and only their proven dependencies for
  Hollow Knight Mods behavior.
- Add `HKMods.cs` and only its proven dependencies for the
  CustomKnight-compatible skin engine.
- Do not copy game assets, generated assemblies, Unity binaries, user packs,
  saves, credentials, or any file excluded by `REDISTRIBUTION-AUDIT.md`.

### Shared features after Hollow Knight acceptance

- Create `tools/shared-patches/src/Companion/ICompanionGameAdapter.cs` only in
  Stage H6, after the concrete Hollow Knight behavior is known.
- Retain `tools/shared-patches/src/Mods/TweakContracts.cs` and
  `TweakController.cs`; adapt Hollow Knight to them without weakening its
  existing behavior.
- Create the launcher skin registry/scanner under
  `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/`
  from the accepted Hollow Knight library behavior.
- Keep selections, Mods master/switches, skin enablement, rotation mode, and
  last page namespaced by profile.

### Silksong successor phase

- Retain the completed transport/frame source evidence and parked probe.
- Resume the `DsPort*` work only after Stage H5.
- Delete rejected `DsShell`/generic screen files only after their native
  replacements pass the Silksong device gate.

## Stage H0: Rebaseline authority and preserve the parked work

**Requirements:** DSUI-00, DSUI-08, DSUI-10.

**Files:**

- Modify the two authoritative specifications.
- Create this plan.
- Modify `docs/superpowers/plans/2026-08-29-unified-hollow-knight-platform.md`.
- Modify `docs/superpowers/plans/2026-08-31-dual-souls-ui-port.md`.
- Modify `docs/verification/design-traceability.md`.
- Modify `docs/verification/dual-souls-ui-port-matrix.md`.
- Modify `README.md`.

- [x] **Step 1: Record the Hollow Knight-first gate**

Require the Hollow Knight vertical slice to complete before any Silksong UI
stage can advance. Mark the existing Silksong Stage 2 device gap and Stage 3
probe as `PARKED`, retaining every blocker and evidence link.

- [x] **Step 2: Verify that no prior evidence was erased**

Run:

```powershell
rg -n "HOST-VERIFIED-SOURCE|AUDIT-COMPLETE|PARKED|ab77f5a|1feebf5" `
  docs README.md
```

Expected: completed Stage 1/2 source evidence and the Stage 3 audit remain
present, while the successor work is explicitly non-critical-path.

- [x] **Step 3: Commit the rebaseline**

```powershell
git add README.md docs
git commit -m "docs: prioritize the Hollow Knight reference port"
```

**Stage reconciliation:** Re-read DSUI-00 through DSUI-10. This stage passes
only when the plan, specifications, README, traceability, and matrix all name
the same Hollow Knight-first order and the parked Silksong blockers remain.

## Stage H1: Isolate the minimum direct-display transport seam

**Requirements:** DSUI-00, DSUI-02, DSUI-10.

**Files:**

- Create the four `tools/shared-patches/src/DualScreen/DirectDisplay*` files
  listed above.
- Create `tools/shared-patches-tests/DirectDisplayStateTests.cs`.
- Modify `tools/shared-patches/SharedPatches.Core.csproj`.
- Modify `tools/silksong-patches/src/dualscreen/DualScreenV2.cs`.
- Modify `tools/silksong-patches/src/dualscreen/DsPresentation.cs`.
- Modify `tools/silksong-patches/src/dualscreen/DsTouch.cs`.
- Modify `tools/hollow-knight-patches/HollowKnightPatches.csproj`.

- [x] **Step 1: Write failing shared-state tests**

Create tests for presence, readiness, pause, display loss, reactivation,
single-display fallback, and ordered teardown. The pure contract begins as:

```csharp
public interface IDirectDisplayContent : System.IDisposable
{
    void SetTransportActive(bool active);
    void OnPanelGeometry(float width, float height);
}
```

Run:

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
```

Expected: RED because the transport state types do not yet exist.

- [x] **Step 2: Extract only proven technology**

Move display activation, presence/readiness state, camera/layer ownership,
panel measurement, touch attribution, pause/resume, diagnostics, and fallback.
Do not move `DsPortFrame`, `DsShell`, any page, game data, or game type into
the shared project.

- [x] **Step 3: Verify both consumers compile**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
pwsh -NoProfile -File tools/silksong-patches/check.ps1 `
  -Depot 'D:\Temp\hkandroid-task11-silksong-managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

Expected: shared tests green and both exact patch targets compile.

- [ ] **Step 4: Prove a transport-only Hollow Knight panel**

Build through the signing workflow, install as an update preserving data, and
display only an opt-in diagnostic card on display 1. Verify display loss,
resume, and single-screen fallback; this proves transport only, not the HUD.
Close the game after capture.

**2026-09-01 device result:** The signed candidate updated the production
package in place, rebuilt exact Hollow Knight `1.5.12620`, and rendered the
opt-in diagnostic on display 1 while the game remained on display 0. The same
PID survived pause/resume and the transport reactivated without a crash. The
Thor's lower DRM connector can be forced `disconnected`, but its composer
keeps logical display 4 registered and Unity emits no removal event. Therefore
physical display-loss and true single-display startup remain one tracked
deferral, not a pass. See
`docs/verification/hollow-knight-direct-display.md`.

- [x] **Step 5: Reconcile and commit**

Record exact hashes/logs and commit `refactor: share direct display transport`.
Any visual redesign or Silksong-only dependency in shared code is a blocker.

**Stage reconciliation:** DSUI-00 and the H1 transport portion of DSUI-02 are
satisfied. DSUI-10 has RED/GREEN tests, exact Hollow Knight and Silksong
compiles, a signed artifact, update-preserving Thor evidence, and synchronized
documentation. H1 has `blockers = 0` and `tracked_deferrals = 1`; the physical
detach/single-display device row must reach zero by H5. This does not permit
the diagnostic card to stand in for any H2 HUD or companion responsibility.

## Stage H2: Port the complete Hollow Knight Dual Souls companion

**Requirements:** DSUI-00, DSUI-01, DSUI-02, DSUI-07, DSUI-10.

**Files:**

- Add the Hollow Knight reference modules listed under file ownership.
- Create `tools/hollow-knight-patches/src/dualsouls/HkDirectDisplayAdapter.cs`.
- Modify `tools/hollow-knight-patches/entrypoints.json`.
- Create `tools/ci/tests/test_hollow_knight_reference_port.py`.
- Create `docs/verification/hollow-knight-direct-display.md`.

- [x] **Step 1: Write failing module and behavior contracts**

The test must require every reference module, the exact pinned reference
commit, a direct-display adapter, and absence of the old EGL/Java bridge in
the compiled source list. It must assert that `RelayerHud` routes the one live
HUD, adopts spawned children, and restores it under the oracle conditions.

Run:

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port
```

Expected: RED because the modules and adapter are absent.

- [x] **Step 2: Import the MIT-licensed source modules without assets**

Preserve the original module boundaries and behavior. Record source SHA-256
values and the reference commit in the verification document. Reject any
binary/game-asset path before staging it.

- [x] **Step 3: Replace only the presentation technology**

`HkDirectDisplayAdapter` must translate the shared transport's panel geometry,
layers, cameras, touch events, visibility, display loss, and teardown into the
existing `HKDualScreen` lifecycle. Do not reimplement the HUD or pages with
new widgets.

- [x] **Step 4: Complete one source-level companion parity pass**

Audit all imported H2 modules against the pinned Dual Souls source in one
pass. Require every HUD, frame, page, selection, prompt, dialogue/tutorial,
item/lore overlay, fade/death, pause/inventory, resume, touch, and restoration
responsibility to exist behind the direct-display adapter. Resolve source-list,
API, lifecycle, geometry, and exact-build failures together. Run the focused
Python contracts and exact Hollow Knight compile, but do not sign or deploy
until this whole source-level inventory is green.

- [x] **Step 5: Run one signed lower-HUD companion gate**

Build and sign once, install as an update, launch exact Hollow Knight
`1.5.12620`, and inspect only the companion surface: live HUD and clean top
screen; Inventory, Charms, Map; selection/action prompts; pause presentation;
the blurred/dimmed scenery wash; and lower-display touch. Do not navigate,
fight, collect, progress, alter saves, or run general gameplay. Dialogue,
tutorial, item-popup, fade/death, restoration, and other state transitions use
focused host contracts or controlled debug injection/current static state.
Resident tab labels must remain visible after the source inventory closes;
glyphs and fleurs must fit their measured cells. Record the entire lower-HUD
defect set before changing source, and close the game after capture.

- [ ] **Step 6: Fix the captured H2 defect set as one batch**

Convert every observed defect into a focused regression contract, implement
the complete correction batch, rerun host verification and exact compilation,
then repeat only the affected lower-HUD signed rows through the controlled
title/menu fixture plus process pause/resume and teardown smoke. Do not select a
save, start a game, skip an intro, enter a room, or issue gameplay input during
the recheck.
A missing module or visual/behavioral difference is a blocker, not a Silksong
deferral.

Progress checkpoint: signed dry-run `33520627292` installed commit `0f9f398`
in place and rebuilt exact generation
`gen-b947d3af-d366-4107-93dc-9bb9a494a724`. A minimum static King's Pass room
hosted the lower HUD without movement or gameplay testing. The persistent
opening credit routed to display 1 and lower touch still opened Inventory, but
the three resident tab labels remained absent. Comparison with the pinned Dual
Souls source found the remaining divergence: the reference preserves every
cloned label dependency and disables each non-TMP `MonoBehaviour`, while the
Android port destroyed all but `TextContainer`. Signed dry-run `33525049660`
then installed commit `672724c`, rebuilt exact generation
`gen-8b38e8dc-3117-4236-9c7c-1fc3b91ebc01`, and ran an affected-row-only pass:
the static King's Pass fixture remained unmoving, live masks/Soul and the lower
Inventory panel rendered, the primary HUD stayed clean, and lower touch worked,
but the three labels still produced no pixels. Two isolated `GameCameras`
startup lookups also remained. That pass proved that preserving dependencies was
necessary but that the added inactive staging lifecycle itself diverged from the
pinned reference. The third correction now restores the oracle's direct
live-frame clone/disable/activate/text/mesh sequence and makes the persistent-HUD
scan use the already resolved quiet camera owner. It passes 25 focused contracts,
58 shared tests, 103 Python tests, both exact compiles, and the exact 222,720-byte
Hollow Knight patch. Signed dry-run `33528905347` installed commit `0dd27b7`,
preserved the package identity and app UID, and rebuilt exact generation
`gen-77e25023-d93d-412d-ae4f-e614b2b02bd8`. The attempted pass was stopped
before acceptance when its setup path reached new-game/intro state; no save file
was created and no result from that path is counted. Step 6 remains open until a
title/menu-bound, controlled lower-HUD fixture proves the labels and startup log
behavior without entering gameplay. Signed dry-run `33532750369` then installed
commit `55dceea`, preserved package identity/UID/first-install time, and rebuilt
exact generation `gen-b38920f7-580d-47bb-96d6-f5abcecdb2e5`. Its default-off
`MAIN_MENU` fixture locked native menu input, invoked only the production
frame/tab path, and closed without selecting a save, starting a game, entering a
room, issuing gameplay input, or creating a save. The capture exposed a real
prior Android-only scale divergence: `b866732` changed the pinned
`compTabScale` from `2.7` to `0.6`, reducing the associated tab chrome to 22%
of the reference size. Complete log stacks also show both isolated
`GameCameras` messages came from Hollow Knight's own
`Platform:SetSceneLoadState`, not the patch. The next correction restores the
pinned `2.7` scale and eliminates all remaining patch-owned logging singleton
getters. Signed run `33536682728` proved the scale correction and exact
generation `gen-0815d295-5ed7-4011-b3d9-41ef0cd79983` through the same locked
menu fixture, but the native glyphs remained absent on both Map and Inventory.
A controlled `compNoMapY` probe kept the known-good native label visible inside
the tab band, ruling out camera/layer, Map-mask, and spatial-stencil causes.
The working native lifecycle instead finalizes text after all frame siblings
exist; the tab clones now follow that path from `PositionFrame`. It passes 30
focused contracts, 58 shared tests, 109 Python tests, both exact compiles, and
produces a 226,816-byte Hollow Knight patch. Step 6 remains open for one
lower-HUD-only label/teardown recheck.

- [ ] **Step 7: Reconcile and commit**

Update the verification document, traceability, matrix, and README. Commit
`feat: run Dual Souls through direct display` only after source tests, exact
compile, and the device matrix are green.

## Stage H3: Port Hollow Knight Mods and tweaks intact

**Requirements:** DSUI-00, DSUI-05, DSUI-09, DSUI-10.

**Files:**

- Add `HKTweaks.cs`, `HKModsMenu.cs`, `HKDualScreen.Bottom.Tweaks.cs`, and
  their audited source-only dependencies.
- Create `tools/hollow-knight-patches/src/mods/HollowKnightTweakAdapter.cs`.
- Modify `tools/hollow-knight-patches/entrypoints.json`.
- Extend `tools/shared-patches-tests/TweakControllerTests.cs`.
- Extend `tools/ci/tests/test_hollow_knight_reference_port.py`.

- [ ] **Step 1: Write failing persistence/isolation tests**

Require `dualsouls.mods.hollow-knight.*` and
`dualsouls.mods.silksong.*` to remain disjoint, every master to default off,
and each switch to survive leaving and relaunching its own game.

- [ ] **Step 2: Port existing Hollow Knight behavior**

Retain the Dual Souls Mods screen, categories, descriptions, legal-action
guards, reset behavior, state-slot confirmations, and live revert behavior.
Use typed Hollow Knight APIs; native memory-address tables are prohibited.

- [ ] **Step 3: Verify host, compile, and device effects**

Test every exposed benign tweak in a reversible scenario, master off/on,
individual persistence, process exit/relaunch, and isolation from Silksong.
Close the game after capture.

- [ ] **Step 4: Reconcile and commit**

Commit `feat: port Hollow Knight Mods to direct display` only with zero stage
blockers.

## Stage H4: Port multi-pack skins and death rotation

**Requirements:** DSUI-00, DSUI-05, DSUI-09, DSUI-10 and unified-platform
Goals 6–7.

**Files:**

- Add audited `HKMods.cs` skin behavior and its source-only dependencies.
- Create launcher `SkinManifest.kt`, `SkinPackScanner.kt`, `SkinRegistry.kt`,
  and `SkinActivationStore.kt`.
- Create `tools/hollow-knight-patches/src/skins/HollowKnightSkinAdapter.cs`.
- Create `docs/skin-pack-format.md`.
- Extend `SkinRotationStateMachineTest.kt` and add scanner/registry tests.

- [ ] **Step 1: Write failing scanner and state tests**

Cover multiple valid immediate-child packs, one invalid sibling, duplicate IDs
with equal/different hashes, traversal attempts, per-game mappings, independent
activation, ordered/shuffled rotation, repeated death before respawn, one-pack
behavior, disabled rotation, and apply-failure rollback.

- [ ] **Step 2: Implement the fail-closed pack model**

Use this manifest boundary:

```kotlin
data class SkinManifest(
    val schemaVersion: Int,
    val id: String,
    val name: String,
    val author: String,
    val games: Map<String, SkinGameMapping>,
)

data class SkinGameMapping(
    val assetRoot: String,
    val textures: Map<String, String>,
)
```

Reject absolute paths, `..`, unknown schema versions, empty mappings, and any
resolved file outside its pack root. Preserve pack directories; never flatten
texture files.

- [ ] **Step 3: Connect the proven Hollow Knight texture engine**

Map CustomKnight names to the existing `HKMods` targets. On death, select but
do not apply the next pack; apply only after stable respawn. Preserve the last
working skin and record an error if application fails.

- [ ] **Step 4: Verify host, exact compile, and device rotation**

Import at least two valid packs and one invalid sibling, apply each pack,
rotate across controlled death/respawn-state injection, relaunch, disable skins,
and verify live rollback. Never obtain those states through gameplay or a user
save. Confirm no Silksong selection or files changed. Close the game after
capture.

- [ ] **Step 5: Reconcile and commit**

Update documentation and README. Commit `feat: add Hollow Knight skin library
and death rotation` only with zero stage blockers.

## Stage H5: Close the Hollow Knight reference gate

**Requirements:** DSUI-00 through DSUI-10 as applicable to Hollow Knight.

**Files:**

- Modify `docs/verification/hollow-knight-direct-display.md`.
- Modify both ledgers and `README.md`.

- [ ] **Step 1: Run fresh host verification**

Run the full Python, .NET, Gradle/Robolectric, exact Hollow Knight compile,
repository-content audit, and signing-contract suites from a clean checkout.

- [ ] **Step 2: Run the complete Thor matrix**

On `bfa98654`, verify HUD, every companion page, all lower-display gestures,
Mods effects and persistence, skin scan/apply/rotation/rollback, pause/resume,
display loss, single-display fallback, and process exit through title/menu or
controlled injected state. Verify save-preservation and rollback semantics on
host synthetic save fixtures; device passes neither read nor write game saves.

- [ ] **Step 3: Cross-check the specification**

Record `blockers = 0` and `tracked_deferrals = 0` for the Hollow Knight
reference slice. Rendering only a HUD or diagnostic card cannot close H5.

- [ ] **Step 4: Commit the accepted reference**

Commit `test: accept the Hollow Knight Dual Souls reference` and push. Do not
start Silksong adaptation until this commit exists.

## Stage H6: Extract shared contracts from the accepted reference

**Requirements:** DSUI-02, DSUI-09, DSUI-10.

**Files:**

- Create `tools/shared-patches/src/Companion/ICompanionGameAdapter.cs`.
- Refine shared direct-display, Mods, and skin contracts.
- Modify the Hollow Knight adapter without changing observable behavior.
- Create shared contract tests.

- [ ] **Step 1: Write failing boundary tests**

Require shared sources to reference no Hollow Knight or Silksong game type.
Define only capabilities proven by H5:

```csharp
public interface ICompanionGameAdapter : System.IDisposable
{
    string ProfileId { get; }
    bool IsGameplayReady { get; }
    void Attach(IDirectDisplayContent host);
    void Tick();
    void RestorePrimaryDisplay();
}
```

- [ ] **Step 2: Extract one responsibility at a time**

After each transport, lifecycle, Mods-store, and skin-library extraction, rerun
the full H5 host and device matrix. A Hollow Knight behavior change is a
blocker and the abstraction must be corrected before continuing.

- [ ] **Step 3: Reconcile and commit**

Commit `refactor: extract shared companion contracts` only after the accepted
Hollow Knight evidence remains unchanged.

## Stage S1: Resume the Silksong resident-object port

**Requirements:** DSUI-01 through DSUI-10.

**Files:**

- Resume `DsProbe.cs` and `test_dual_souls_ui_port.py` from their parked state.
- Continue `DsPortHud`, `DsPortMap`, `DsPortInventory`, `DsPortLoadout`,
  `DsPortSelect`, `DsPortOverlays`, and `DsPortProgress`.
- Update the source-to-source matrix after every module.

- [ ] **Step 1: Validate the parked probe before use**

Run its four RED contracts, complete the implementation, focused tests, exact
Silksong compile, and two independent source reviews. Do not commit a probe
that can false-complete on unrelated loaded objects.

- [ ] **Step 2: Port modules in oracle order**

Use the accepted Hollow Knight implementation—not screenshots or the rejected
shell—as the executable reference. Move the same live Silksong HUD objects,
preserve drivers, and implement pages/overlays through resident objects and
the shared contracts.

- [ ] **Step 3: Reconcile each module**

For HUD, Map, Inventory, Loadout, selection, progress, and overlays: write RED
contracts, implement, run both exact compiles, capture side-by-side device
evidence, update the matrix/README, and resolve all stage blockers before the
next module.

## Stage S2: Adapt Mods and skins to Silksong

**Requirements:** DSUI-04, DSUI-05, DSUI-09, DSUI-10.

- [ ] **Step 1: Implement the Silksong typed Mods adapter**

Map only source-proven APIs. Persist every master and switch under the
Silksong profile, default master off, and keep Hollow Knight values unchanged.

- [ ] **Step 2: Implement the Silksong skin/lifecycle adapter**

Use Silksong-specific texture targets and death/stable-respawn events while
consuming the same library/rotation contracts. A pack may contain separate
per-game mappings; texture compatibility is never inferred across games.

- [ ] **Step 3: Run the full Silksong device matrix**

Verify Mods effects/persistence and two-pack death rotation/rollback in
addition to the complete companion matrix. Close the game after capture.

## Stage F1: Cross-game acceptance, cleanup, and release

**Requirements:** All unified-platform goals and DSUI-00 through DSUI-10.

- [ ] **Step 1: Remove rejected and transitional code**

Delete the authored Silksong shell and any temporary duplicate transport only
after both adapters pass. Confirm no production entry point references them.

- [ ] **Step 2: Prove two-game isolation and switching**

Run both switch directions at the title/menu boundary, independent
Mods/skins/page state through controlled injection, reset isolation, display
fallback, update, and rollback. Verify save preservation only with host
synthetic fixtures; device switching does not open or mutate a user save.

- [ ] **Step 3: Final specification reconciliation**

Require `blockers = 0` and `tracked_deferrals = 0` across both ledgers. Refresh
README claims from fresh evidence.

- [ ] **Step 4: Build, sign, publish, and reverify**

Use GitHub Actions. Rebuild the same commit reproducibly, publish the aligned
tag/release, download the APK again, verify hash/package/version/signer, update
the installed app, and smoke-test both profiles. Close the running game.

## Cumulative reconciliation ledger

| Stage | Requirements | State | Blocking gate | Required evidence |
| --- | --- | --- | --- | --- |
| H0 | DSUI-00/08/10 | `COMPLETE` | None | Both specifications, both parent plans, matrix, traceability, README, and 5/5 ordering contracts agree; the existing 38-contract Silksong suite remains green after the status correction |
| H1 | DSUI-00/02/10 | `IMPLEMENTED / DEVICE-PARTIAL` | No implementation blocker; one tracked physical detach/true single-display deferral must close by H5 | 49/49 shared tests, 78/78 Python tests, both exact compiles, signed run `33494317664`, update-preserving Thor transport and pause/resume captures |
| H2 | DSUI-00/01/02/07/10 | `HOST-GREEN / DEVICE-BLOCKED` | Signed lower-HUD evidence proves routed live HUD, clean primary HUD, blurred/dimmed backdrop, real lower-controller page switching, selection/details/prompt, bounded ornaments, pause title presentation, persistent opening-credit routing, and live Inventory under the direct transport. Menu-only run `33536682728` proves the fixture/input-lock path and pinned `2.7` chrome scale, but the three native glyphs remain absent. A known-good native label stayed visible in the tab band, ruling out camera/layer, Map-mask, and spatial-stencil clipping; post-frame finalization is device-unproved. Run `33540377737` reached no Unity/HUD code after an LMK-interrupted conversion was reused; conversion completion is now atomically committed | Run `33515971125` established the broad matrix. Earlier room-hosted evidence is historical and is not an allowed future validation method. Run `33536682728` used only the input-locked `MAIN_MENU` production frame fixture, controlled label-position injection, and clean teardown. The correction and converter safeguard pass 30 focused contracts, 122 Android tests, 58 shared tests, 38 bundle-surgery tests, 110 Python tests, both exact compiles, and a 226,816-byte Hollow Knight patch. Base-game `Platform:SetSceneLoadState` singleton messages are not an H2 blocker; one clean lower-HUD-only label/teardown recheck remains |
| H3 | DSUI-00/05/09/10 | `PENDING` | Hollow Knight Mods behavior and persistence | Host isolation plus Thor effect/relaunch matrix |
| H4 | DSUI-00/05/09/10 | `PENDING` | Scanner, application, rotation, rollback | Host pack matrix plus controlled Thor death/respawn-state injection proof; no gameplay testing |
| H5 | DSUI-00–10 | `PENDING` | Hollow Knight blockers and deferrals must both reach zero | Clean host and complete Thor reference matrix |
| H6 | DSUI-02/09/10 | `PENDING` | Shared extraction must preserve accepted H5 behavior | Shared boundary tests plus unchanged H5 regression |
| S1 | DSUI-01–10 | `DEFERRED` | Depends on H5 and H6 | Validated probe, module RED/GREEN, both compiles, side-by-side captures |
| S2 | DSUI-04/05/09/10 | `DEFERRED` | Depends on S1 | Silksong Mods/skin effect and persistence matrix |
| F1 | All | `PENDING` | Zero blockers/deferrals across both games | Switching, update/rollback, signed fresh-download proof |

No stage passes merely because code compiles or something renders on display
1. After every stage, re-read the specifications, update this ledger and the
two verification ledgers, classify every gap, update the README for a major
achievement, and stop advancement while a stage blocker remains.
