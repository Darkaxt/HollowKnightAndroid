# Dual Souls Bottom-Screen UI Port Design

Date: 2026-08-31

## Status and authority

This document is the authoritative specification for the unified game's
bottom-screen user interface. It replaces the earlier interpretation that a
new game-neutral shell could merely imitate selected Dual Souls features.

The implementation priority is Hollow Knight first. The existing Dual Souls
HUD, pages, Mods presentation, tweak behavior, and CustomKnight-compatible
skin engine must run through the direct-display technology as one executable
reference before the shared composition is generalized or further Silksong UI
work resumes. Silksong is the second game adapter, not the place where the
reference design is invented.

The user-approved target is a port of the Hollow Knight Dual Souls companion
interface. Hollow Knight defines the design pattern. Silksong supplies its own
resident sprites, fonts, labels, data, and additional feature content inside
that pattern.

## 2026-09-10 corrective authority — visible HUD first

This section supersedes any stage ordering, addendum, host-complete label, review
result, or deferral that would postpone visible Silksong HUD parity behind H4,
H5, H6, page completeness, Mods, skins, generalized lifecycle work, or larger
host matrices. Those activities are paused as prerequisites. Existing work and
evidence are retained, but none may block the shortest correction of the live
HUD.

Signed candidate `f56defa5f3c7701cdd0adbb565eec2493e1a46ee` failed the first
production requirement on the AYN Thor. In ordinary new-game gameplay,
`32-new-game-158s.png` shows Silksong's health and Silk HUD still at the
primary display's top-left, while `33-first-gameplay-lower.png` shows only an
otherwise empty ornamental tab shell on the lower display. This invalidates
all prior claims that Task99 Batch A, its SPEC review, its QUALITY review, or
fresh-main host verification accepted the functional HUD. Their ownership and
compilation evidence remains useful only as nonvisual diagnostic evidence.

The immediate milestone is deliberately narrow:

1. keep SilksongAndroid's proven display-1 transport;
2. remove Silksong's live health, Silk, currency, and equipped Crest/Tool HUD
   from the primary gameplay composition;
3. route those same native objects and their existing drivers into the
   corresponding Hollow Knight lower-HUD regions;
4. present the Hollow Knight region hierarchy, scale, density, and spacing with
   Silksong-native art and values; and
5. restore the native primary relationships on full disable, display loss,
   native pause/inventory ownership, scene replacement, and teardown.

Health, Silk, currency, and equipped Crest/Tool state are the minimum known live
HUD groups, not an assertion that Hollow Knight exhausts Silksong's mechanics.
During this correction, inventory the actual Silksong HUD hierarchy and cached
managed APIs for every additional persistent or contextual mechanic that owns a
native HUD presentation. Integrate each source-proven or runtime-observed group
into the nearest Hollow Knight-derived status, loadout, prompt, or contextual
region using the same framing, spacing, typography, modal ordering, and
primary-cleanup rules. Do not leave a second Silksong HUD on display 0, omit a
real mechanic because Hollow Knight lacks it, or invent speculative mechanics
that the running game and source do not establish.

Use the most direct implementation supported by the two games' shared Unity UI
model. Do not introduce a new shared framework, synthetic HUD, generalized
resource protocol, speculative prefab admission system, exhaustive golden
matrix, or unrelated H4/H5/H6 closure to accomplish this milestone. Reuse the
existing concrete Hollow Knight layout constants and composition order where
the direct-display transport permits it; adapt only the concrete Silksong root
names, component types, and semantic values that actually differ.

This milestone passes only with one evidence bundle. All Silksong captures,
state-change observations, and lifecycle evidence must come from the same signed
correction commit and ordinary gameplay state. The Hollow Knight visual oracle
may reuse an earlier exact-build capture only when its game version, app commit,
and capture provenance are recorded. The bundle must contain:

- a Hollow Knight lower-HUD reference capture identifying its live HUD region,
  frame/content region, tab row, and clean primary display;
- a Silksong primary capture with no health, Silk, currency, or loadout HUD at
  the top-left or elsewhere on display 0;
- a Silksong lower capture visibly containing live health, Silk, currency, and
  current Crest/Tool information in the corresponding Hollow Knight regions;
- an inventory showing every additional source-proven or runtime-observed
  Silksong HUD mechanic and a capture of each currently reachable presentation
  integrated into the same design rather than left on display 0;
- a side-by-side comparison showing equivalent hierarchy, information density,
  scale, and spacing rather than merely shared ornament styling;
- one bounded state-change observation proving that native HUD drivers still
  update the routed objects; and
- pause/resume plus teardown restoration evidence.

A compile, source contract, fake-node test, renderer existence check, empty
frame, tab shell, or statement that Unity/GPU behavior is unproved cannot pass
or defer any item above. Before the signed device gate, host tests are limited
to regressions directly needed for root discovery, routing, native-driver
preservation, restoration, and the exact failure being corrected. Test count
is not a progress metric.

Creating one disposable new-game save for this device validation is authorized.
Record its exact profile slot and remove only that test save before delivering
the functional APK; never clear package data or remove unrelated user saves.
No assistant-driven movement, jump, attack, collection, or progression is
required for the HUD gate.

Only after this visible HUD milestone passes may work continue, in order, to
Inventory, Crest/Tools, Tasks, Journal, Map and overlays. Each is then accepted
as a small visible vertical slice before Mods or skins resume. A page's host
ownership machinery does not count as page completion without a populated,
usable lower-display capture.

## Reference and technology boundary

The visual and behavioral reference is `igawa6/dualsouls` commit
`5c22451435b772acde0c7e6456f9019bc1baef73`, principally:

- `HKDualScreen.cs` and `HKDualScreen.Util.cs`;
- `HKDualScreen.Bottom.Layering.cs`;
- `HKDualScreen.Bottom.Frame.cs`;
- `HKDualScreen.Bottom.Hud.cs`;
- `HKDualScreen.Bottom.Inventory.cs`;
- `HKDualScreen.Bottom.Charms.cs`;
- `HKDualScreen.Bottom.Map.cs`;
- `HKDualScreen.Bottom.Select.cs`; and
- `HKDualScreen.Bottom.Tweaks.cs`.

SilksongAndroid remains authoritative only for the lower-level Android/Unity
technology that makes the companion surface possible: Unity display 1
activation, direct Vulkan rendering, camera isolation, display-attributed
touch, pause/resume handling, diagnostics, and single-display fallback. The
Dual Souls Android `Presentation`, EGL blitter, and Java touch bridge are not
ported where the direct-display equivalents already satisfy the same role.

This boundary is technological, not presentational and not a safety wrapper
around the game UI. Hollow Knight Dual Souls remains authoritative for the
bottom-screen hierarchy, slots, geometry, transitions, lifecycle, and every
resulting gameplay and main-screen consequence. Silksong's native UI objects
and assets must be re-parented, re-layered, adapted, or extended into that
design. The port may translate the mechanism needed to make that behavior work
on SilksongAndroid's direct renderer, but it may not replace or suppress an
oracle consequence merely to leave the original Silksong UI untouched.

## Requirements

### DSUI-00 — Establish the executable Hollow Knight reference first

The first production vertical slice is Hollow Knight `1.5.12620` running the
existing Dual Souls companion, Mods, and skin behavior through the proven
Unity display-1 transport. Porting may replace the old Presentation/EGL/Java
transport mechanisms, but it must not redesign, approximate, or independently
recreate the working Hollow Knight composition.

No further Silksong composition implementation may advance beyond its parked
diagnostic/source-evidence state until Hollow Knight has passed its HUD,
pages, Mods persistence, skin scanning/application/rotation, lower-display
touch, lifecycle, and single-display fallback gates on the reference device.
Shared UI contracts are extracted from that accepted implementation. The
paused Silksong probe and Stage 2 source work remain useful successor-phase
evidence; they are neither discarded nor allowed to gate the reference slice.

### DSUI-01 — Dual Souls is the design oracle

The Hollow Knight companion defines the production UI's region hierarchy,
geometry, framing, page composition, information density, navigation,
selection treatment, typography roles, animation vocabulary, modal ordering,
state ownership, lifecycle behavior, and observable consequences. A surface
that is merely inspired by it or that
recreates its result with unrelated widgets does not satisfy this requirement.

### DSUI-02 — Port the composition pipeline

The implementation must port the responsibilities of every referenced
`HKDualScreen.Bottom.*` module onto SilksongAndroid's direct-display renderer.
It must retain the same separation between private layers, dedicated cameras,
persistent HUD/frame content, page clones, contextual overlays, and touch
selection. The exact number of layers or cameras may change only when the
direct renderer makes one redundant and the resulting composition remains
structurally and behaviorally equivalent, including the same state transitions
and consequences outside the bottom display.

### DSUI-03 — Reuse resident game UI

Where Silksong provides a native HUD, inventory, crest/tool, map, dialogue,
tutorial, item, cursor, font, ornament, or other suitable UI object, the
companion must clone, re-parent, re-layer, or otherwise reuse that object and
its resident assets. Reading game state and drawing an independent substitute
is prohibited when a usable resident equivalent exists.

The result is one composed HUD. Hollow Knight UI assets or widgets must not be
dumped over an intact Silksong HUD, and Silksong's original HUD layout must not
be copied wholesale merely because it is resident. Each semantic Silksong
element—health, Silk, currencies, Crests/Tools, and later Silksong-only
features—must occupy or extend the corresponding Hollow Knight design slot
using Silksong sprites, fonts, labels, and data. Where live Silksong objects are
re-parented or re-layered for the gameplay HUD, every existing driver instance
must remain attached and running on that same live object. Layout conflicts
must be handled by slot routing and post-update reassertion, never by removing
or freezing gameplay-HUD drivers. The separate static/status chrome may clone
resident visual donors and sanitize or manually drive those clones only where
`Bottom.Hud` itself follows that lifecycle. `Bottom.Hud` remains authoritative
for layout, ownership, routing, transitions, lifecycle, and consequences. Original
relationships are routed back under the oracle's actual conditions: pause,
inventory, or full dual-screen off/display unavailable. The separate companion
page toggle does not return the live gameplay HUD to the top. Scene replacement
or port teardown must proactively restore any still-valid moved objects before
destroying the routing rig; that is direct-transport safety, not behavior
attributed to `RelayerHud`. Restoration covers only properties changed by the
adapter and must not roll back driver-owned health, Silk, currency, active, or
visual state.

Static frame ornaments/fleurs must be instantiated under an inactive staging
parent and reduced to their one exact retained visual before first activation.
Pane Name tab labels are the explicit legacy-TMP exception: the pinned Hollow
Knight oracle clones them directly under the live frame, preserves every
component dependency, disables each non-TMP `MonoBehaviour`, explicitly
activates the clone/renderers, and never applies the ornament or pane-clone
staging policy to resident labels. Exact Android `1.5.12620` adds one required
settle boundary: each direct live clone remains blank while `BuildFrame`
constructs and sorts all native TMP siblings; `PositionFrame` then assigns the
localized text, forces the mesh, enables its renderer, and normalizes the live
glyph bounds. Failed text/mesh/bounds finalization retains retry ownership, and
a partial construction tears down the frame for a clean next-tick rebuild.
This preserves the oracle's live-clone dependency and activation sequence while
matching the only resident-label timing that produces pixels on this target.
Staged label clones retained valid geometry but produced no glyph pixels and
remain forbidden. HUD, page, and overlay work in Stages 3–7 likewise
must reproduce the oracle's own activate/open, settle, selective-freeze or
retained-driver sequence and observable consequences; no blanket resident-clone
sanitizer is acceptable.

The retained tab row is laid out from the generated glyph bounds on the actual
lower panel. All labels and their selected-tab fleurs must remain fully inside
their tab cells at the measured display size, with the labels sorted above the
ornament quads. Source pivots, localization width, or a source-tuned scale may
not place the glyphs off-screen, make adjacent titles overlap, or allow either
fleur to span multiple cells. This is a transport adaptation of the existing
Dual Souls chrome, not a replacement design.

### DSUI-04 — Semantic substitutions preserve the pattern

Hollow Knight concepts map to Silksong by meaning without changing the design
system: masks become Silksong health; Soul becomes Silk; Geo becomes the
appropriate Silksong currencies; Charms become Crests and Tools; Hollow Knight
inventory items become Silksong inventory items; and the map remains the map.
Labels and data come from the running game wherever its APIs expose them.
Silksong concepts with no Hollow Knight equivalent are appended to the nearest
semantic region using the same Hollow Knight hierarchy, spacing, typography,
selection, transition, and modal rules; they do not preserve or introduce a
second Silksong-specific layout system.

### DSUI-05 — New pages extend, never replace, the pattern

Silksong-only Tasks and Journal pages, the shared Mods overlay, skin controls,
and later supported features must use the same frame, content/detail split,
selection cursor, prompt treatment, tab treatment, spacing, transitions, and
modal rules as the ported Hollow Knight interface. They may add semantic tabs
or controls, but may not introduce a second visual system.

### DSUI-06 — Synthetic UI is a documented fallback

A newly drawn control is allowed only when the source game has no usable
resident equivalent. Each such control must be recorded in the port matrix
with the missing native source, the Dual Souls pattern it follows, and its
verification evidence. Generic replacement HUDs, generic icon grids, flat
tabs, and procedural ornament approximations are not acceptable production
fallbacks.

### DSUI-07 — Preserve the full companion behavior

The port covers the persistent HUD and area/status row; frame and bottom tab
row; Inventory; Loadout; Map; selection cursor and action prompts;
story/dialogue/tutorial/focus equivalents; item popups; fade/death/cutscene
synchronization; background/resume; display loss; and single-display
fallback. Unsupported Silksong equivalents are blockers or tracked deferrals,
not silent omissions.

During active gameplay, the lower display must preserve Dual Souls' backdrop
treatment: scenery-only capture, the configured low-resolution bilinear blur,
and the configured brightness multiplication behind the HUD. Rendering a
sharp or normally exposed second copy of the gameplay camera is a visual and
behavioral blocker. The direct-display implementation may replace the native
blitter, but its shader/material path must demonstrably support the brightness
multiplier and its output must be checked on the target panel.

### DSUI-08 — The authored shell is rejected

The `DsShell`, `DsHudStrip`, `DsIconGrid`, and separately authored page path
introduced by commits `a310332`, `ff22860`, and `a82cb08` is a diagnostic
prototype. It must not remain reachable in the production companion once the
corresponding ported modules land. Parts may survive only if the port matrix
proves they are low-level transport, input, diagnostics, data, or a necessary
synthetic fallback under DSUI-06.

### DSUI-09 — Profile state remains isolated

Hollow Knight and Silksong remember their page, optional HUD behavior, Mods
master state, and individual switch values independently. Every Mods master
switch defaults to off and persists after leaving and relaunching its game.

### DSUI-10 — Stage gates use the reference, not render success

Every implementation stage must be reconciled against this specification and
the corresponding Dual Souls source module. A stage passes only with:

1. source-to-source responsibility coverage;
2. automated structural or behavioral tests that first failed;
3. exact compilation for every patch target touched by the stage, with both
   Hollow Knight and Silksong required after a boundary becomes shared;
4. a port-matrix update identifying satisfied requirements, blockers, and
   tracked deferrals; and
5. when visually relevant, side-by-side capture evidence against the running
   Hollow Knight Dual Souls reference.

Rendering something on display 1 is not acceptance evidence by itself.

Every remaining reference-device pass for this work—from H2 through final
cross-game acceptance, including first checks, rechecks, lifecycle checks, and
release-candidate checks—is a targeted feature validation pass only. It may use
only a title/menu-bound fixture, host contracts, controlled debug injection, or
synthetic state to inspect rendering, layout, primary-HUD cleanup, backdrop,
page/modal controls, display-specific touch, persistence,
pause/resume/restoration, and teardown. It must not select a save, start a new
game, skip an intro, enter or reuse an in-game room, navigate, fight, collect,
progress, mutate saves through play, or run general gameplay. State-dependent
HUD, Mods, skin, death/respawn, and rotation behavior must be exercised through
focused host contracts, controlled debug injection, or synthetic state. Each
device pass closes the game immediately after its affected rows. This
restriction changes the verification method, not the required production
behavior.

When a runtime-only production object must be bootstrapped, the validation seam
must create the minimum required state explicitly with gameplay input disabled
and save reads/writes prevented; the pass still exercises only the named
lower-screen row and teardown. An already-loaded gameplay state is not an
allowed shortcut.

## Architecture

The production implementation has four ordered boundaries:

1. **Direct-display transport:** the proven SilksongAndroid technology that
   activates display 1, owns isolated cameras/layers, fences display-specific
   touch, handles lifecycle/display loss, emits diagnostics, and falls back to
   one screen. It contains no game-specific UI design.
2. **Hollow Knight reference adapter:** connects the existing Dual Souls
   modules to that transport while preserving their object ownership,
   hierarchy, geometry, pages, transitions, Mods behavior, skin behavior, and
   consequences. This must work before any UI abstraction is accepted.
3. **Shared companion contracts:** extracted only from the accepted Hollow
   Knight implementation. They describe transport hosting, composition
   lifecycle, profile-scoped feature state, skin-library access, and adapter
   capabilities without referencing either game's concrete types.
4. **Silksong adapter:** locates and safely clones/re-parents/re-layers
   Silksong resident objects, supplies typed state and localized terminology,
   and maps them into the proven shared composition. It extends the reference
   for Tasks/Journal and other Silksong-only semantics without inventing a
   replacement layout.

Temporary duplication is allowed only inside the Hollow Knight reference
adapter while proving the transport boundary. It must be removed during the
explicit extraction stage, with the accepted Hollow Knight device matrix kept
green before Silksong consumes the shared contracts.

## Implementation order

The corrective implementation proceeds in this dependency order:

1. Rebaseline the documents and park the unfinished Silksong composition
   branch state without deleting its evidence.
2. Isolate the minimum proven direct-display transport seam needed to host a
   game adapter; retain Silksong's currently working transport behavior.
3. Port the complete Hollow Knight Dual Souls companion onto that seam,
   beginning with the persistent HUD and then the existing frame, pages,
   selection, overlays, fades, and lifecycle behavior.
4. Port Hollow Knight's Mods menu and `HKTweaks` behavior with a first-run
   master default of off and independent persistent switch values.
5. Port Hollow Knight's CustomKnight-compatible multi-pack scanner,
   activation/rollback, and death-to-stable-respawn rotation.
6. Pass the complete Hollow Knight host and reference-device matrix.
7. Extract shared companion, Mods, and skin contracts from the proven Hollow
   Knight result, rerunning its full regression after every extraction step.
8. Resume the parked Silksong source probes and adapt its resident HUD, Map,
   Inventory, Crests/Tools, Tasks/Journal, overlays, Mods, and skins into the
   shared composition.
9. Remove the rejected authored Silksong shell and run the cross-game,
   fallback, persistence, switching, update, and release matrices.

The authoritative task sequence is
`docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md`. The
earlier Silksong-first plan remains only as a record of completed source work
and parked successor tasks.

## Acceptance criteria

The feature is complete only when blockers and tracked deferrals are both
zero and all of the following are true:

- Hollow Knight runs the existing Dual Souls companion, Mods, and skin system
  through the direct-display transport before Silksong-specific composition
  work is accepted;
- Silksong's bottom screen is immediately recognizable as the Hollow Knight
  Dual Souls interface with Silksong content, rather than as a separate UI;
- every Dual Souls bottom-screen responsibility has a ported implementation or
  an explicit user-approved specification revision;
- resident Silksong UI objects/assets are used wherever available;
- Hollow Knight behavior remains unchanged and provides the side-by-side
  reference;
- both profiles pass page, touch, overlay, pause/resume, display-loss,
  single-display, Mods-state, and relaunch persistence checks; and
- the repository and APK remain free of proprietary game content.
