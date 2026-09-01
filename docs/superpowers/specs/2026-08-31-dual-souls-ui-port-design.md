# Dual Souls Bottom-Screen UI Port Design

Date: 2026-08-31

## Status and authority

This document is the authoritative specification for the unified game's
bottom-screen user interface. It replaces the earlier interpretation that a
new game-neutral shell could merely imitate selected Dual Souls features.

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
re-parented or re-layered, retain only the native drivers required to provide
their game state and visual updates. `Bottom.Hud` remains authoritative for
layout, ownership, routing, transitions, lifecycle, and consequences; any
Silksong driver that would impose conflicting behavior must be adapted or
selectively frozen while its required visual output is preserved. Original
relationships are restored only at the oracle's corresponding pause, disable,
display-loss, scene, or teardown boundary.

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
3. exact Silksong and Hollow Knight patch compilation;
4. a port-matrix update identifying satisfied requirements, blockers, and
   tracked deferrals; and
5. when visually relevant, side-by-side capture evidence against the running
   Hollow Knight Dual Souls reference.

Rendering something on display 1 is not acceptance evidence by itself.

## Architecture

The production implementation has three boundaries:

1. **Direct-display transport:** activates and owns display 1, private render
   layers/cameras, display-specific touch, lifecycle, diagnostics, and
   fallback. This is game-neutral and contains no game UI design.
2. **Dual Souls composition port:** implements the frame, HUD, page clone,
   selection, overlay, fade, and transition behavior defined by the Hollow
   Knight modules. This is the shared design pattern.
3. **Game object adapter:** locates and safely clones/re-layers the active
   game's resident objects, supplies typed state and localized terminology,
   and reports capabilities. It does not invent a replacement layout.

Hollow Knight continues to use its proven Dual Souls companion behavior.
Silksong uses the same composition port with Silksong-specific object
discovery and semantic substitutions.

## Implementation order

The corrective implementation proceeds in dependency order:

1. Freeze the current authored shell as a non-production prototype and create
   the file-by-file port matrix.
2. Extract the direct-display transport from the authored shell.
3. Port layer/camera isolation and the frame/tab composition.
4. Port the persistent HUD using Silksong's real HUD objects and assets.
5. Port Map using Silksong's real map render hierarchy.
6. Port Inventory and selection/action prompts using native inventory objects.
7. Port Charms semantics to Crests/Tools and add Tasks/Journal in the same
   composition pattern.
8. Port contextual overlays and lifecycle/fade synchronization.
9. Reattach Mods and later skin controls as pattern-conforming overlays.
10. Remove the rejected production code path and run the full cross-game,
    fallback, persistence, and device acceptance matrix.

## Acceptance criteria

The feature is complete only when blockers and tracked deferrals are both
zero and all of the following are true:

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
