# Dual Souls Bottom-Screen UI Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:executing-plans` task by task. Every production change follows
> red-green-refactor, and every stage reconciles the authoritative
> specification before advancing.

**Goal:** Replace the rejected Silksong authored shell with a module-by-module
port of the Hollow Knight Dual Souls companion UI, using Silksong resident UI
objects and assets on SilksongAndroid's direct display-1 renderer.

**Architecture:** Preserve the proven direct-display transport, touch fence,
lifecycle, diagnostics, and fallback. Replace `DsShell` and its independently
drawn pages with a Dual Souls composition runtime divided by the original
`Bottom.*` responsibilities. A Silksong resident-object adapter locates,
clones, re-parents, re-layers, freezes, and drives native UI objects; synthetic
controls are permitted only when the port matrix proves that no native object
can fulfill the role.

**Tech stack:** Unity 6000.0.61f1 IL2CPP, C# patch injection, Unity display 1,
uGUI and world-space Unity renderers, Unity Input System display attribution,
PowerShell exact-source compile checks, Python source contracts, NUnit shared
tests, Android Gradle/Robolectric, GitHub Actions signing.

**Authority:**
`docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md`.

---

## File structure and disposition

### Retain as infrastructure

- `DsPresentation.cs`: direct display activation, companion cameras/canvas,
  camera-mask isolation, visibility, and teardown. It will be expanded to own
  multiple private composition layers/cameras but will contain no page design.
- `DsTouch.cs`: display-1 input fence.
- `DsInput.cs`: lower-panel gesture acquisition.
- `DsProbe.cs`: runtime object discovery diagnostics.
- `DsConfig.cs`: diagnostic/runtime switches only.
- `DsGameData.cs`: typed read-only game state used when no resident component
  already exposes the value.
- `DsGameArt.cs`: resident asset discovery helper only; it may not compose a
  substitute screen.
- `DsTestCard.cs`: opt-in transport diagnostic.
- `tools/shared-patches/src/Mods/*` and
  `tools/silksong-patches/src/mods/*`: keep typed tweak behavior and isolated
  persistence; reconnect its UI only after the composition port exists.

### Create as the production port

- `DsPortRuntime.cs`: lifecycle/orchestration equivalent of the companion
  portion of `HKDualScreen.cs`.
- `DsPortLayers.cs`: direct-display equivalent of `Bottom.Layering`.
- `DsPortFrame.cs`: frame, ornaments, tab row, page clone cache, page fit, and
  slide orchestration equivalent of `Bottom.Frame`.
- `DsPortHud.cs`: native Silksong HUD clone/re-layer, area/status, currencies,
  and equipped-loadout row equivalent of `Bottom.Hud`.
- `DsPortInventory.cs`: native inventory clone, settle/freeze, availability,
  counters, content/detail arrangement, and refresh fingerprint equivalent of
  `Bottom.Inventory`.
- `DsPortLoadout.cs`: Crests/Tools semantic port of `Bottom.Charms`.
- `DsPortMap.cs`: native Silksong map clone, availability, framing, watch
  invalidation, pan/zoom/reset, and compass equivalent of `Bottom.Map`.
- `DsPortSelect.cs`: resident selection cursor, item hit-test, action glyph,
  prompt text, tap/drag/pinch routing equivalent of `Bottom.Select`.
- `DsPortOverlays.cs`: tutorial, focus-equivalent, dialogue, speaker, item
  popup, title/attribution, fade/death/cutscene, and companion-visibility gate.
- `DsPortProgress.cs`: Tasks and Journal extensions expressed through the
  ported Inventory/content-detail pattern.
- `DsPortMods.cs`: Mods overlay expressed through the same frame, cursor,
  prompt, and modal rules.
- `DsResidentUi.cs`: exact Silksong object/component discovery and capability
  reporting.
- `DsPortUtil.cs`: clone, ownership, bounds, layer, renderer, localization,
  font, sprite, and deterministic teardown helpers.

### Remove from the production path

- `DsShell.cs`, `DsHudStrip.cs`, `DsIconGrid.cs`, `DsWidgets.cs`,
  `DsScreens.cs`, `DsInventoryScreen` in `DsScreens.cs`,
  `DsLoadoutScreen.cs`, `DsTasksScreen.cs`, `DsJournalScreen.cs`,
  `DsMapScreen.cs`, `DsHornetPanel.cs`, `DsTitleCard.cs`, and `IDsScreen.cs`.
- `DsMapView.cs` remains available only until its resident map discovery and
  render-hierarchy knowledge has been moved into `DsPortMap.cs`; it is then
  removed from production staging.
- `DsTheme.cs` remains only for resident font/asset lookup used by documented
  fallbacks; authored-shell geometry and procedural ornamentation are removed.

The files are deleted only after their replacement stage is green, so every
commit remains compilable and reviewable.

## Stage 0: Lock the correction and build the port matrix

**Requirements:** DSUI-01, DSUI-02, DSUI-03, DSUI-06, DSUI-08, DSUI-10.

**Files:**

- Existing authority/status: specification, matrix, `README.md`, and
  `docs/verification/design-traceability.md`
- Evidence ledgers: this plan and
  `docs/verification/dual-souls-ui-port-matrix.md`
- Test: `tools/ci/tests/test_dual_souls_ui_port.py`

- [x] **Step 1: Write the source-contract test**

Add tests that require the authoritative port spec and matrix, require matrix
rows for all eight `HKDualScreen.Bottom.*` modules plus the main lifecycle,
require a `DELETE`, `RETAIN`, or `REWRITE` disposition for every current
dualscreen source file. It also rejects acceptance language that claims the
current authored shell completes or visually proves the port. The live
`new DsShell(` production entry remains a Stage 1 contract.

- [x] **Step 2: Run the test against the already-authored contract**

Run:

```powershell
python -m unittest tools.ci.tests.test_dual_souls_ui_port -v
```

The design phase had already authored the specification, plan, matrix, README,
and traceability correction. Stage 0 therefore added enforcement without
manufacturing a fake missing-matrix failure.

- [x] **Step 3: Write the source-to-source matrix**

For every reference module, record its concrete object sources, clone/re-layer
behavior, frame/HUD/page/overlay responsibilities, Silksong resident
equivalent, production destination file, current evidence, and any exact
evidence gap. For every current `dualscreen/*.cs` file record one disposition:
`RETAIN_INFRASTRUCTURE`, `REWRITE_PORT`, `TEMPORARY_REFERENCE`, or
`DELETE_AFTER_STAGE_N`. No generic “similar implementation” row is allowed.

- [x] **Step 4: Correct traceability and README status**

Mark the authored-shell visual result `REJECTED_PROTOTYPE`, reset the UI port
to `IN-PROGRESS`, link the corrective spec/plan/matrix, and state that display
transport is proven while Dual Souls UI parity is not.

- [x] **Step 5: Run the contract test and self-review the documents**

Run the unittest again and search for placeholders or weakened language:

```powershell
python -m unittest tools.ci.tests.test_dual_souls_ui_port -v
rg -n "TBD|TODO|similar-looking|inspired by|approximation" docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md docs/superpowers/plans/2026-08-31-dual-souls-ui-port.md docs/verification/dual-souls-ui-port-matrix.md
```

Expected: tests pass; only explicit prohibitions or historical failure notes
may match the search.

- [x] **Step 6: Reconcile and commit**

Re-read DSUI-01 through DSUI-10, update the matrix ledger with stage evidence,
classify every gap, and commit:

```powershell
git add docs/superpowers/plans/2026-08-31-dual-souls-ui-port.md docs/verification/dual-souls-ui-port-matrix.md tools/ci/tests/test_dual_souls_ui_port.py
git commit -m "test: enforce the Dual Souls UI port contract"
```

## Stage 1: Separate direct-display transport from UI composition

**Requirements:** DSUI-02, DSUI-08, DSUI-10.

**Files:**

- Create: `tools/silksong-patches/src/dualscreen/DsPortRuntime.cs`
- Create: `tools/silksong-patches/src/dualscreen/DsPortLayers.cs`
- Modify: `DualScreenV2.cs`
- Modify: `DsPresentation.cs`
- Test: `tools/ci/tests/test_dual_souls_ui_port.py`

- [ ] **Step 1: Add failing contracts for the production entry point**

Require `DualScreenV2` to instantiate `DsPortRuntime`, prohibit `DsShell`, and
require private layer/camera roles for frame/pages, HUD, overlays, and fade.
Require every port camera to target display 1 and every port layer to be swept
from primary-display cameras.

- [ ] **Step 2: Verify the contracts fail because the port runtime is absent**

Run the targeted unittest and confirm the failure identifies
`DsPortRuntime`, not an unrelated repository issue.

- [ ] **Step 3: Implement the minimum transport/composition boundary**

`DsPortRuntime` owns bring-up callbacks, visibility, update ordering, scene
invalidation, and teardown. `DsPortLayers` requests the dedicated frame/page,
HUD, overlay, and fade camera/layer roles from `DsPresentation` and exposes
only roots/cameras, never widget construction. Switch `DualScreenV2` to this
runtime behind the existing `testcard` diagnostic branch.

- [ ] **Step 4: Compile exact Silksong and Hollow Knight patches**

Run:

```powershell
pwsh -NoProfile -File tools/silksong-patches/check.ps1
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1
```

Expected: both exact-version compiles succeed with no production reference to
`DsShell` from `DualScreenV2`.

- [ ] **Step 5: Reconcile and commit**

Update the matrix. A missing frame/HUD/page is a tracked deferral to its named
stage; a transport leak, duplicate camera, or primary-input regression is a
blocker. Commit `refactor: separate companion transport from UI composition`.

## Stage 2: Port private layering, frame, and tabs

**Requirements:** DSUI-01, DSUI-02, DSUI-03, DSUI-06, DSUI-10.

**Files:**

- Create: `DsPortUtil.cs`
- Create: `DsResidentUi.cs`
- Create: `DsPortFrame.cs`
- Modify: `DsPortRuntime.cs`
- Modify: `DsPortLayers.cs`
- Test: `tools/ci/tests/test_dual_souls_ui_port.py`

- [ ] **Step 1: Add failing frame-structure contracts**

Require resident top/bottom ornaments, the Dual Souls bottom-centred label
row, selection fleurs, context masks, separate status/mod positions, cached
page hosts, and slide state. Reject flat filled tab rectangles and any call to
`DsWidgets.Fleur` in production port files.

- [ ] **Step 2: Verify red**

Run the targeted unittest and confirm the required frame roles are absent.

- [ ] **Step 3: Port `Bottom.Frame` and shared utilities**

Translate clone ownership, recursive layer assignment, resident ornament
lookup, frame bounds, page-host cache, tab mapping, selected fleurs, masks,
slide orchestration, and teardown. Use Silksong resident ornament/font objects
located by `DsResidentUi`; when an object is not resident, keep the page hidden
and report the capability gap rather than drawing a generic substitute.

- [ ] **Step 4: Compile and run structural contracts**

Run the targeted Python test followed by both exact patch compiles.

- [ ] **Step 5: Reconcile, update README, and commit**

Compare the file against `Bottom.Layering` and `Bottom.Frame` responsibility by
responsibility. Frame or tab geometry gaps are blockers. Commit
`feat: port Dual Souls frame composition`.

## Stage 3: Port the persistent HUD from resident Silksong objects

**Requirements:** DSUI-01, DSUI-03, DSUI-04, DSUI-07, DSUI-10.

**Files:**

- Create: `DsPortHud.cs`
- Modify: `DsResidentUi.cs`
- Modify: `DsPortRuntime.cs`
- Modify: `DsPortFrame.cs`
- Delete after green: `DsHudStrip.cs`, `DsHornetPanel.cs`
- Test: `tools/ci/tests/test_dual_souls_ui_port.py`

- [ ] **Step 1: Add failing HUD contracts**

Require cloning/re-layering of Silksong health and Silk HUD renderers, native
currency sprites, area localization, equipped Crest/Tool summary, FPS/battery
gutters, resident asset provenance, and display-loss restoration. Reject the
authored `MaxHealthGlyphs`/procedural Silk bar implementation.

- [ ] **Step 2: Verify red, then port `Bottom.Hud` semantics**

Clone the live Silksong HUD subtree after it becomes resident; isolate the
clone on the HUD layer; freeze only drivers that would mutate primary-game
state; mirror live visual state; add area/status/loadout widgets by cloning
compatible resident text/icon objects; and preserve main-screen HUD behavior
unless the user explicitly enables the already specified safe move option.

- [ ] **Step 3: Verify compile and runtime provenance diagnostics**

Both exact patch compiles must pass. Runtime diagnostics must log every cloned
source path and must fail closed if required native HUD objects cannot be
located.

- [ ] **Step 4: Reconcile and commit**

Any independently drawn health/Silk replacement is a blocker. Delete the old
HUD files only after no production reference remains. Commit
`feat: port the resident Silksong HUD to display 1`.

## Stage 4: Port the native Map page

**Requirements:** DSUI-01 through DSUI-07, DSUI-10.

**Files:**

- Create: `DsPortMap.cs`
- Create/modify: `DsPortSelect.cs`
- Modify: `DsResidentUi.cs`, `DsPortFrame.cs`, `DsPortRuntime.cs`
- Delete after green: `DsMapScreen.cs`, `DsMapView.cs`

- [ ] **Step 1: Add failing map and gesture contracts**

Require native quick-map/map-root discovery, cloned render hierarchy, map
availability/no-map state, bounds framing, area invalidation, resident compass,
pinch zoom anchored to midpoint, drag only while zoomed, reset animation, and
tab-band exclusion.

- [ ] **Step 2: Verify red, then port `Bottom.Map`**

Move the proven discovery knowledge from `DsMapView` into the port module, but
clone/re-layer the actual map hierarchy instead of presenting a separately
authored page around a render texture. Reproduce the Dual Souls watchers,
framing, masks, pan/zoom/reset, compass, and no-map treatment with Silksong
resident equivalents.

- [ ] **Step 3: Verify and reconcile**

Run source contracts, exact compiles, and existing map-touch host checks. A
generic no-map card or missing resident map interaction is a blocker. Commit
`feat: port the Silksong map into the Dual Souls frame`.

## Stage 5: Port Inventory, selection, and action prompts

**Requirements:** DSUI-01 through DSUI-07, DSUI-10.

**Files:**

- Create: `DsPortInventory.cs`
- Complete: `DsPortSelect.cs`
- Modify: `DsResidentUi.cs`, `DsPortFrame.cs`, `DsPortRuntime.cs`
- Delete after green: inventory parts of `DsScreens.cs`, `DsIconGrid.cs`

- [ ] **Step 1: Add failing inventory contracts**

Require a native inventory manager/root clone; controlled open-kick,
settle/freeze, and reveal; live availability fingerprint; resident counters;
Dual Souls content/detail fit; resident cursor corners; native action glyph;
localized prompt; and two-step selection/action safety.

- [ ] **Step 2: Verify red, then port `Bottom.Inventory` and `Bottom.Select`**

Clone Silksong's real inventory hierarchy, let its own layout/FSM reach stable
state without committing gameplay actions, freeze only the clone drivers,
re-layer it, fit it inside the ported frame, and route touches through the
resident selection cursor and prompt pattern. Use typed Silksong legality APIs
before any actionable second tap.

- [ ] **Step 3: Verify and reconcile**

Run contracts and exact compiles. A separately drawn generic icon grid is a
blocker. Commit `feat: port Silksong inventory and selection`.

## Stage 6: Port Loadout and extend progress pages

**Requirements:** DSUI-01, DSUI-03 through DSUI-07, DSUI-10.

**Files:**

- Create: `DsPortLoadout.cs`, `DsPortProgress.cs`
- Modify: `DsResidentUi.cs`, `DsPortSelect.cs`, `DsPortFrame.cs`
- Delete after green: `DsLoadoutScreen.cs`, `DsTasksScreen.cs`,
  `DsJournalScreen.cs`, remaining `DsIconGrid.cs` references

- [ ] **Step 1: Add failing Loadout/Tasks/Journal contracts**

Require Crests/Tools to follow the ported `Bottom.Charms` grid/detail/notch
pattern with resident assets and legal equip rules. Require Tasks and Journal
to reuse the same frame, selection cursor, content/detail hierarchy, prompt,
transition, and modal rules rather than generic grids.

- [ ] **Step 2: Verify red, then implement the semantic port**

Clone/re-layer native Crest and Tool objects, map notch/cost semantics to the
resident Silksong equivalents, and preserve native availability. Build Tasks
and Journal by cloning their native rows/details and arranging them through
the established Inventory/Charms composition rules. Any truly missing frame
element must be recorded under DSUI-06 before a fallback is added.

- [ ] **Step 3: Verify and reconcile**

Run contracts and exact compiles. Commit
`feat: extend the Dual Souls pattern to Silksong progress`.

## Stage 7: Port overlays, fades, and lifecycle behavior

**Requirements:** DSUI-02, DSUI-03, DSUI-07, DSUI-10.

**Files:**

- Create: `DsPortOverlays.cs`
- Modify: `DsPortRuntime.cs`, `DsPortLayers.cs`, `DsResidentUi.cs`
- Delete after green: `DsTitleCard.cs`

- [ ] **Step 1: Add failing overlay/lifecycle contracts**

Require native tutorial/focus-equivalent, dialogue box, speaker name, item
popup, title/attribution sources; the Dual Souls companion visibility gate;
fade/death/cutscene synchronization; scene invalidation; pause/resume;
display-loss; hot-plug; and single-display restoration.

- [ ] **Step 2: Verify red, then port the main-class overlay behavior**

Locate and re-layer the actual Silksong overlay subtrees onto the overlay
camera, suppress the companion only under the same modal conditions, sample or
reproduce the game's real fade driver, and restore every object/layer/camera on
teardown or fallback.

- [ ] **Step 3: Verify and reconcile**

Run contracts, exact compiles, Gradle tests, and fallback tests. Missing
dialogue/tutorial/fade behavior is a blocker. Commit
`feat: port companion overlays and lifecycle`.

## Stage 8: Reattach Mods and remove the rejected shell

**Requirements:** DSUI-05, DSUI-06, DSUI-08, DSUI-09, DSUI-10.

**Files:**

- Create: `DsPortMods.cs`
- Modify: `DsPortFrame.cs`, `DsPortSelect.cs`, `DualScreenV2.cs`
- Delete: all remaining rejected authored-shell production files
- Modify: `tools/silksong-patches/check.ps1`
- Test: shared patch tests and `test_profile_mod_pipeline.py`

- [ ] **Step 1: Add failing production-pruning and persistence contracts**

Require zero production references to `DsShell`, `IDsScreen`, `DsWidgets`, or
the old screen classes. Require the Mods overlay to use the ported frame/cursor
and require profile-qualified persisted keys with master default off.

- [ ] **Step 2: Verify red, then port the Mods presentation**

Keep the existing typed tweak controller and game API. Replace only its
presentation with a port-pattern modal using resident frame/text/cursor/prompt
objects. Preserve active page while opening/closing and persist every switch
independently per game.

- [ ] **Step 3: Delete the rejected production path**

Remove each old file only after `rg` proves it has no production consumer.
Update staging/compile lists and keep only infrastructure and documented
fallback helpers.

- [ ] **Step 4: Run the complete host gate**

```powershell
python -m unittest discover tools/ci/tests -v
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj
dotnet test tools/bundle-surgery-tests/BundleSurgery.Tests.csproj
pwsh -NoProfile -File tools/silksong-patches/check.ps1
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1
./gradlew.bat test assembleRelease
```

Expected: all tests and both exact compiles pass; no old production UI entry
remains; no proprietary content is introduced.

- [ ] **Step 5: Reconcile, update README, and commit**

The stage cannot pass with a tracked UI deferral. Commit
`refactor: retire the authored companion shell`.

## Stage 9: Side-by-side device acceptance and final remediation

**Requirements:** DSUI-01 through DSUI-10.

**Files:**

- Create: `docs/verification/dual-souls-ui-device-acceptance.md`
- Modify: port matrix, traceability, README

- [ ] **Step 1: Build and verify one signed candidate**

Use the existing GitHub signing workflow. Independently verify package,
version, checksum, v2/v3 signer, certificate pin, and absence of proprietary
game content before installation.

- [ ] **Step 2: Preserve state and install in place**

Record installed package/version/signer, profile generations, source hashes,
saves, preferences, and first-install timestamp. Update without clearing app
data. Never touch the separate hkpoc package.

- [ ] **Step 3: Capture Hollow Knight as the live oracle**

Run the exact current Hollow Knight profile and capture the persistent HUD,
Map, Inventory, Charms, item selection/prompt, dialogue/tutorial overlay,
transition fade, pause/resume, and display fallback states. Close the game.

- [ ] **Step 4: Run the same Silksong matrix**

Capture the equivalent Silksong HUD, Map, Inventory, Crests/Tools,
Tasks/Journal, selection/prompt, dialogue/tutorial, fade, pause/resume, and
fallback states. Verify display-1 touch does not affect primary gameplay.
Close the game.

- [ ] **Step 5: Verify independent persistence**

For each game separately, change page and Mods switches, leave the game,
launch the other game, return, and prove that each profile restored only its
own values and that a never-enabled master remains off.

- [ ] **Step 6: Final remediation and completion gate**

Re-read the full specification and matrix. Resolve every blocker and tracked
deferral, rerun affected host and device checks, and require
`blockers = 0` and `tracked_deferrals = 0`. A visual mismatch against the
Hollow Knight composition is a blocker even if the lower screen renders and
responds.

- [ ] **Step 7: Commit, push, and release only after zero-gap proof**

Update README with the verified achievement and exact evidence, commit
`docs: verify the Dual Souls UI port`, push `master`, and create the signed
release only if all repository release gates are also green.

## Reconciliation ledger

| Stage | Requirements | State | Blockers | Tracked deferrals | Evidence |
| --- | --- | --- | --- | --- | --- |
| 0 | DSUI-01/02/03/06/08/10 | COMPLETE | None | None | `python -m unittest tools.ci.tests.test_dual_souls_ui_port -v`: 7 tests passed; contract covers DSUI-01–10, distinct first-column rows for all nine reference modules, exactly one valid disposition row for each of the 24 current dualscreen C# filenames, prototype/port status, and README/traceability acceptance language |
| 1 | DSUI-02/08/10 | PENDING | Depends on Stage 0 | None | — |
| 2 | DSUI-01/02/03/06/10 | PENDING | Depends on Stage 1 | None | — |
| 3 | DSUI-01/03/04/07/10 | PENDING | Depends on Stage 2 | None | — |
| 4 | DSUI-01–07/10 | PENDING | Depends on Stage 3 | None | — |
| 5 | DSUI-01–07/10 | PENDING | Depends on Stage 4 | None | — |
| 6 | DSUI-01/03–07/10 | PENDING | Depends on Stage 5 | None | — |
| 7 | DSUI-02/03/07/10 | PENDING | Depends on Stage 6 | None | — |
| 8 | DSUI-05/06/08/09/10 | PENDING | Depends on Stage 7 | None | — |
| 9 | DSUI-01–10 | PENDING | Depends on Stage 8 | None | — |

No required work may remain a tracked deferral at completion.
