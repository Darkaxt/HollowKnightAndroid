# Dual Souls Bottom-Screen UI Port Implementation Plan

> **Execution status (2026-09-01): PARKED SUCCESSOR PHASE.** This plan retains
> the completed Silksong transport/frame evidence and the unfinished HUD/page
> tasks, but it no longer controls execution order. The authoritative
> Hollow Knight-first sequence is
> `docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md`.
> Stages 3–9 below may resume only after that plan's Hollow Knight reference
> gate (H5) and shared-contract extraction (H6) pass. Existing blockers and
> evidence in this file remain valid and must not be erased.

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:executing-plans` task by task. Every production change follows
> red-green-refactor, and every stage reconciles the authoritative
> specification before advancing.

**Goal:** Replace the rejected Silksong authored shell with a module-by-module
port of the Hollow Knight Dual Souls companion UI, using Silksong resident UI
objects and assets on SilksongAndroid's direct display-1 renderer. Hollow
Knight owns visible behavior, state transitions, and their consequences;
SilksongAndroid owns only the lower-level display/render/input technology.

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

- `DsPortRuntime.cs`: composition-state boundary below the `DualScreenV2`
  host/transport lifecycle; later modules attach orchestration here.
- `DsPortLayers.cs`: direct-display equivalent of `Bottom.Layering`.
- `DsPortFrame.cs`: frame, ornaments, tab row, page clone cache, page fit, and
  slide orchestration equivalent of `Bottom.Frame`.
- `DsPortHud.cs`: native Silksong HUD elements composed in the Hollow Knight
  layout, area/status, currencies,
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

- [x] **Step 1: Add failing contracts for the production entry point**

Require `DualScreenV2` to instantiate `DsPortRuntime` only after presentation
readiness, prohibit `DsShell`, and require exactly the proven content and
overlay layer/camera roles with named frame/pages/HUD/overlays/fade roots.
Require both port cameras to target display 1 and both port layers to be swept
from primary-display cameras.

- [x] **Step 2: Verify the contracts fail because the port runtime is absent**

Run the targeted unittest and confirm the failure identifies
`DsPortRuntime`, not an unrelated repository issue.

- [x] **Step 3: Implement the minimum transport/composition boundary**

`DualScreenV2` remains the host/transport lifecycle owner for display bring-up,
hot-plug, pause, input fencing, and teardown ordering. It creates
`DsPortRuntime` only after `DsPresentation` is ready and behind the existing
`testcard` diagnostic branch. The runtime owns composition-root visibility,
idle state, a monotonic active-scene revision for later invalidation, and
idempotent disposal; it does not queue gestures. `DsPortLayers` exposes empty
named roots beneath the exact two presentation roles, never widgets or extra
layers/cameras.

- [x] **Step 4: Compile exact Silksong and Hollow Knight patches**

Run:

```powershell
pwsh -NoProfile -File tools/silksong-patches/check.ps1 `
  -Depot 'D:\Temp\hkandroid-task11-silksong-managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-61-toolchain\android\Variations\il2cpp\Managed'
```

Expected: both exact-version compiles succeed with no production reference to
`DsShell` from `DualScreenV2`.

- [x] **Step 5: Reconcile and commit**

Update the matrix. A missing frame/HUD/page is a tracked deferral to its named
stage; a transport leak, duplicate camera, or primary-input regression is a
blocker. Commit `refactor: separate companion transport from UI composition`.

## Stage 2: Port private layering, frame, and tabs

**Requirements:** DSUI-01, DSUI-02, DSUI-03, DSUI-06, DSUI-10.

**Files:**

- Create: `DsPortUtil.cs`
- Create: `DsResidentUi.cs`
- Create: `DsPortFrame.cs`
- Create: `DsPortFrameState.cs`
- Modify: `DsPortRuntime.cs`
- Modify: `DsPortLayers.cs`
- Test: `tools/ci/tests/test_dual_souls_ui_port.py`

- [x] **Step 1: Add failing frame-structure contracts**

Require resident top/bottom ornaments, the Dual Souls bottom-centred label
row, selection fleurs, context masks, separate status/mod positions, cached
page hosts, and slide state. Reject flat filled tab rectangles and any call to
`DsWidgets.Fleur` in production port files.

- [x] **Step 2: Verify red**

Run the targeted unittest and confirm the required frame roles are absent.

- [x] **Step 3: Port `Bottom.Frame` and shared utilities**

Translate clone ownership, recursive layer assignment, resident ornament
lookup, frame bounds, page-host cache, tab mapping, selected fleurs, masks,
slide orchestration, and teardown. Use Silksong resident ornament/font objects
located by `DsResidentUi`; when an object is not resident, keep the page hidden
and report the capability gap rather than drawing a generic substitute.

Host implementation uses exact current Silksong UGUI sources: Options TopFleur
`Warning_Fleur0008`, Keep Resolution BottomFleur `bottom_fleur0008`, and the
Pause Menu's `pause_top_fleur0000` / `bottom_fleur0000` selected pair. Full
source path, Sprite identity, Sprite rect, and source `RectTransform.sizeDelta`
must all independently match a live resident `Image`; otherwise the role stays
absent with a capability-gap log. In particular, Keep Resolution BottomFleur
uses a 355x134 Sprite rect inside a 303x66 source RectTransform.
Native Pane Name glyph bounds drive baseline alignment, responsive hit slots,
selected-fleur placement, aspect-preserving scale, and dynamic inner edges.
The selected label retains its native RGB at alpha 1.0 while every inactive
label retains native RGB at alpha 0.6, both after build and after selection.
Four black renderer covers provide functional clipping below frame/tab/HUD
sorting. Interrupted slides normalize every cached host before starting anew.
The static ornaments and native tab labels are born under an inactive staging
parent and made inactive. Before reparenting, every `MonoBehaviour` except the
one exact retained static visual is removed immediately from the owned clone:
`UnityEngine.UI.Image` is retained for ornaments/fleurs and
`TMProOld.TextMeshPro` for Pane Name labels. The clone fails closed unless a
post-removal scan finds exactly that one concrete visual type; subclasses,
zero matches, and multiple matches are rejected. The retained visual and
renderer objects are explicitly active/enabled while the staging parent still
keeps the hierarchy inactive; non-`MonoBehaviour` animation, audio, and
collider drivers remain disabled. This sanitizer is Stage 2 static-chrome
policy only: Stages 3–7 must instead port each oracle object's activate/open,
settle, and selective-freeze or retained-driver lifecycle and consequences. Exact resident
Image discovery runs once per in-game/source revision, accepts only loaded
scenes and one exact root-to-leaf path/Sprite/dimension match, caches misses,
and resets only on gameplay/source invalidation. Pure selection, hit, slide,
and host-visibility decisions live in `DsPortFrameState` and execute in a host
contract using the production source.

- [x] **Step 4: Compile and run structural contracts**

Run the targeted Python test followed by both exact patch compiles.

- [x] **Step 5: Reconcile, update README, and commit**

Compare the file against `Bottom.Layering` and `Bottom.Frame` responsibility by
responsibility. Frame or tab geometry gaps are blockers. Commit
`feat: port Dual Souls frame composition`.

Reconciliation status is `HOST-VERIFIED-SOURCE / DEVICE-BLOCKED`: exact asset
identity and the geometry, sorting, masking, and slide source contracts compile,
but live residency, rendered geometry, cover clipping, and side-by-side parity
still require device evidence. The dormant shell is not deleted and Stage 3
must not treat Stage 2 as visually complete until that blocker closes.

## Stage 3: Port the persistent HUD from resident Silksong objects (PARKED)

**Requirements:** DSUI-01, DSUI-03, DSUI-04, DSUI-07, DSUI-10.

**Files:**

- Create: `DsPortHud.cs`
- Modify: `DsResidentUi.cs`
- Modify: `DsPortRuntime.cs`
- Modify: `DsPortFrame.cs`
- Delete after green: `DsHudStrip.cs`, `DsHornetPanel.cs`
- Test: `tools/ci/tests/test_dual_souls_ui_port.py`

- [x] **Step 0: Audit the two native HUD implementations**

The Hollow Knight oracle moves its already-correct live `Hud Canvas`, retains
native health/Soul/Geo drivers, and adds area, equipped, FPS, and battery chrome
using its own HUD camera geometry. Silksong does not expose one equivalent safe
pane: its static bundles contain tk2d collections and animations, while its
runtime health, Silk, Money/Shard, Crest, and Tool visuals are owned by separate
singleton, event, pooling, audio, stack, or PlayMaker drivers.

Typed runtime anchors are `HUDCamera.GameplayChild`,
`GameCameras.hudCanvasSlideOut`, `GameCameras.silkSpool`, and
`HudCanvas.IsVisible`. The semantic sources are `health_display` tk2d
descendants, `SilkSpool`/`SilkChunk`, Money and Shard `CurrencyCounter`
instances, `ToolCrest.CrestSprite`, `BindOrbHudFrame`, and
`ToolHudIcon : RadialHudIcon`. Exact transform paths remain a runtime probe
gate because the live combat hierarchy is absent from the supplied prefabs.

- [ ] **Step 1: Add failing HUD contracts**

Require exact Hollow Knight HUD region/slot geometry populated by resident
Silksong health, Silk, currency, area, and equipped Crest/Tool elements plus
FPS/battery gutters, provenance, and display-loss restoration. Silksong-only
status is appended inside the same layout grammar. Reject the authored
`MaxHealthGlyphs`/procedural Silk bar, an intact Silksong HUD layout, duplicate
stacked HUDs, or Hollow Knight widgets/art dumped over Silksong elements.
Require the same active-play bottom routing and top-screen cleanup as
`Bottom.Hud`. Direct route-back occurs on pause, inventory, or full
dual-screen-off/display loss; the separate companion-page toggle leaves the
live gameplay HUD on the bottom. Scene replacement and routing-rig teardown
must restore still-valid moved objects as a direct-transport safety operation,
not as behavior attributed to `RelayerHud`.

Require unique typed discovery beneath each proven semantic anchor, full
path/type/driver provenance, same-instance routing into Hollow Knight slots,
recursive private-layer assignment, and exact original-state restoration.
Reject cloned or mirrored gameplay HUDs and reject cloned
`PlayMakerFSM`, `SilkSpool`, `SilkChunk`, `CurrencyCounter`,
`CurrencyCounterStack`, `BindOrbHudFrame`, `ToolHudIcon`, `AreaTitle`,
`HUDCamera`, or `HudCanvas` drivers. Require the live object to leave the top
screen only by being routed to the bottom and require exact restoration at
every oracle boundary.

- [ ] **Step 2: Verify red, then port `Bottom.Hud` semantics**

Locate the live Silksong HUD elements after residency, map each semantic object
to its Hollow Knight HUD slot, and move the one live object or required live
subtree into that composition. Keep the same object and driver instances;
record original parent, sibling index, moved-root local transform (position,
rotation, and scale), and every descendant layer changed by the adapter before routing. Do
not change driver-owned active or visual state. Apply the private HUD layer
recursively and reassert
slot placement after native updates so spawned or driver-reparented health,
Silk, and currency children remain on the bottom. Route every object back to
its exact adapter-mutated routing state on pause, inventory, or full
dual-screen-off/display loss; do not route it back for the separate companion
page toggle. Before scene replacement or routing-rig teardown, proactively
restore any still-valid moved object as direct-transport safety. Never restore
an old active/visual value that the live driver changed while routed. Add
area/status/loadout and Silksong-only widgets from compatible resident
text/icon sources using the oracle's geometry and lifecycle rules. This static
chrome follows the oracle's separate clone/renderer-pool behavior and is not a
gameplay-HUD clone. Do not create a second gameplay HUD, retain an intact
Silksong layout under the port, mirror the live gameplay HUD, or overlay Hollow
Knight art.

- [ ] **Step 3: Verify compile and runtime provenance diagnostics**

Both exact patch compiles must pass. Runtime diagnostics must log every cloned
or moved source path and must fail closed if required native HUD objects cannot
be located. Runtime evidence must record the live HUD hierarchy's parent,
layer, driver inventory, active state, destination, and exact restoration
state.

Device diagnostics must also prove every mask and special-health renderer;
every `SilkSpool` serialized visual and spawned `SilkChunk`; both currency
counter/icon/text/stack ancestries; current Crest frame and Tool HUD objects;
and the live-object routing round trip across damage/heal, Silk gain/use/break,
currency changes, Crest/Tool changes, HUD hide, pause, scene replacement, and
display loss/restoration. Static bundle identity is supporting evidence, not a
replacement for these runtime paths.

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
| 1 | DSUI-02/08/10 | HOST-VERIFIED-BOUNDARY | None | Frame/tabs to Stage 2; resident HUD to Stage 3; Map/Inventory/Loadout/progress pages to Stages 4–6; overlays/fade to Stage 7 | Initial RED: 12 tests ran with 7 Stage 0 contracts green, 4 intended failures, and 2 absent-source skips. First review RED: 14 tests ran with 12 green and 2 intended failures for empty composition lifecycle and overbroad status. Second review RED: 18 tests ran with 13 green and 5 failures covering four defects: stale reattach readiness, presentation retention/serialization, pause/presence/readiness activation, and unstretched roots. GREEN: 18/18 tests; exact Silksong compile 42 sources/10 entry points; exact Hollow Knight compile 4 sources, 0 warnings/errors, 16,896-byte DLL/1 entry point. These host checks verify only the source boundary: `DualScreenV2` retains one presentation before yielding; `DsPresentation` serializes activation without a cancellation timeout, rechecks presence after settle, remeasures, and force-sweeps the reused rig; cameras, roots, and touch fencing activate only when the app is unpaused and display 1 is present and ready. Layers remain exactly 6/3. No device or UI parity is claimed. |
| 2 | DSUI-01/02/03/06/10 | HOST-VERIFIED-SOURCE / DEVICE-BLOCKED | Live UGUI residency, rendered clone/glyph/fleur geometry, functional cover clipping, and side-by-side parity are not device-proven | Native page content/clone settle/fit to Stages 4–6; HUD/status data to Stage 3; Mods control content to Stage 8 | Fix-pass RED: 27 tests with nine failures for obsolete art discovery, geometry, sorting/masks, and interrupted slides. Re-review RED: 28 tests with six assertions failing across the two defects: conflated Sprite/source-rect validation and missing selected-tab alpha. Quality-review RED: 32 tests with five intended failures for unsafe clone activation, non-unique/suffix/unloaded discovery, repeated scans, and missing executable production-state proof. Final spec-review RED: 32 tests with one intended failure because the five-family sanitizer did not disable arbitrary cloned `MonoBehaviour` drivers. Quality re-review RED: 33 tests with two intended failures because disabled drivers could still receive `Awake` and the executable harness hard-coded `D:/Temp`. GREEN: 33/33 focused and 59/59 full Python tests, including a compiled/executed pure production-source harness and structural exact-type/removal/lifecycle checks. Exact Silksong compile: 46 sources/10 entry points. Exact Hollow Knight compile: 4 sources, 0 warnings/errors, 16,896-byte DLL/1 entry point. Exact UGUI identity, revision-cached discovery, exact-type static pre-activation removal, portable automatic-cleanup harness storage, separate Sprite/source-rect dimensions, selected/inactive alpha, hit boundaries, slide decisions, and native Pane Name APIs are source-proven; no device/UI parity is claimed. |
| 3 | DSUI-01/03/04/07/10 | DEFERRED / AUDIT-COMPLETE | Exact runtime paths, same-instance slot routing, spawned-child adoption, driver-reparent resistance, exact restoration, and device parity are unproved | Entire implementation is assigned to successor Stage S1 after Hollow Knight H5/H6 | Source audit proves the Hollow Knight live-HUD move/return/reassert lifecycle, Silksong typed anchors and semantic driver owners, and static tk2d art identities. It rejects the earlier mirror proposal: Stage 3 must move the same live Silksong objects into Hollow Knight slots, preserving instance/driver ownership and naturally clearing the top screen. Probe RED contracts and unverified implementation remain preserved; both exact compiles, runtime provenance, and side-by-side proof remain pending. |
| 4 | DSUI-01–07/10 | PENDING | Depends on Stage 3 | None | — |
| 5 | DSUI-01–07/10 | PENDING | Depends on Stage 4 | None | — |
| 6 | DSUI-01/03–07/10 | PENDING | Depends on Stage 5 | None | — |
| 7 | DSUI-02/03/07/10 | PENDING | Depends on Stage 6 | None | — |
| 8 | DSUI-05/06/08/09/10 | PENDING | Depends on Stage 7 | None | — |
| 9 | DSUI-01–10 | PENDING | Depends on Stage 8 | None | — |

No required work may remain a tracked deferral at completion.
