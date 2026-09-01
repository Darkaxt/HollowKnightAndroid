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

Static frame ornaments and tab labels must be instantiated under an inactive
staging parent. Every cloned `MonoBehaviour` except the explicitly retained
static visual type (`UnityEngine.UI.Image` for ornaments/fleurs or
`TMProOld.TextMeshPro` for Pane Name labels) must be removed immediately from
the owned clone while it remains inactive in hierarchy. Exactly one component
whose runtime type equals the requested visual type must remain; zero, multiple,
subclass, or incomplete-removal results fail closed. The retained visual and
renderer objects are made active/enabled before reparenting and first
activation. This reproduces
the Dual Souls treatment of these objects as static chrome. It is not a
universal resident-clone policy. HUD, page, and overlay work in Stages 3–7
must reproduce the Hollow Knight oracle's own activate/open, settle,
selective-freeze or retained-driver sequence and its observable consequences;
it must not reuse a blanket static-clone sanitizer.

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
