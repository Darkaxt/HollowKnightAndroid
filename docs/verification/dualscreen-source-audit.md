# Dual-screen source audit

Date: 2026-08-31

This audit compares the second-screen behavior that the unified application
must preserve. It is an implementation input, not device proof.

## Sources

- SilksongAndroid branch `design/unified-hollow-knight-platform` at `fcc4170`,
  principally `tools/silksong-patches/src/dualscreen` and
  `tools/silksong-patches/DUALSCREEN-V2.md`.
- [`igawa6/dualsouls`](https://github.com/igawa6/dualsouls) at
  `5c22451435b772acde0c7e6456f9019bc1baef73`, principally
  `Assets/HKDualScreen*.cs`, `Assets/Plugins/Android/HKAux.java`, and
  `native/hkgpu.c`.

## Observed differences

| Concern | SilksongAndroid | Dual Souls / Hollow Knight |
| --- | --- | --- |
| Transport | Unity activates display 1 and a dedicated camera/canvas targets it directly through the Vulkan player | Android `Presentation` owns a `SurfaceView`; a native GLES/EGL plugin blits a Unity `RenderTexture` into its surface |
| Isolation | One private Unity layer is rendered only by the display-1 camera and swept out of other cameras | Several private layers and cameras compose HUD, prompts, companion pages, and backdrop into the blitted texture |
| Touch | Unity Input System touch `displayIndex` identifies panel input; the shared code hit-tests its own UI | Android `MotionEvent` is consumed by the `Presentation`, normalized, and polled from Unity through a Java bridge |
| Pages | Inventory, Crests, Tasks, Journal, Map | Map, Inventory, Charms |
| Persistent context | Idle title card and tab shell | Live HUD, area, equipped charms, FPS/battery, optional dimmed environment backdrop |
| Context overlays | Pages and title/idle state | Tutorials, focus prompts, lore, NPC dialogue and speaker, item popups, attribution, room/death/cutscene fades |
| Theme construction | Shared uGUI metrics with fonts, strings, sprites, and colors resolved from the running Silksong build | Clones and relayers Hollow Knight panes, ornaments, maps, HUD, fonts, sprites, and dialogue objects |
| Lifecycle | Display rig is hidden or rebuilt for pause and display changes; no second display leaves it dormant | The Android presentation is hidden in the background and shown on resume; single-screen mode disables the companion |

## Superseded boundary

The earlier boundary below incorrectly allowed a separately authored
game-neutral shell to reproduce only the visible outcome. Device review proved
that interpretation unacceptable: it produced an approximation instead of a
port. It is retained here only to explain the failed prototype.

The unified application keeps Silksong's direct Unity display-1 architecture.
Hollow Knight has already passed the Vulkan gameplay gate, so its native
`Presentation`/EGL transport is neither required nor desirable as the common
renderer. Direct-display operation, touch attribution, lifecycle, and
single-display fallback still require physical Thor proof for Hollow Knight.

The rejected prototype made the outer shell game-neutral and independently
authored. It owned geometry, semantic tab order, navigation, gestures, touch
metrics, selection feedback, content/detail layout, status placement, idle
state, modal priority, diagnostics, and error containment while adapters
supplied data and theme tokens. That boundary is no longer a production
option.

The live Silksong device review corrected an ambiguity in the original audit:
Dual Souls is not merely a feature checklist. Its HUD and outer layout are the
visual template. The direct Unity display-1 renderer remains the selected tech
layer, but the flat top tab bar observed in the first signed Silksong run is a
failed design gate. The common shell must instead reproduce Dual Souls'
persistent top HUD, ornamental context frame, bottom-centred tab labels,
selected-tab fleurs, and separate gear/status control. Promotional launcher
art remains prohibited on this surface.

Canonical page order is `Inventory`, `Loadout`, optional game-specific
progress pages, then `Map`. Hollow Knight maps `Charms` to Loadout; Silksong
maps `Crests` to Loadout. Tasks and Journal remain available only where the
adapter can back them with real game state. Omitting a page does not reorder
the shared semantic groups.

Dual Souls remains the Hollow Knight functional and visual baseline. Its HUD
and area status, map, inventory, charms, story/dialogue/tutorial overlays,
item popups, fade synchronization, skin coverage, and background/resume
behavior must be ported module by module or recorded as explicit blockers or
tracked deferrals. Its native blitter, Java touch polling, and bespoke outer
transport are not retained.

## Corrected production boundary

`docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md` is now
authoritative. SilksongAndroid contributes only the direct-display transport,
touch, lifecycle, diagnostics, and fallback technology. The UI composition is
a port of Dual Souls' `HKDualScreen.Bottom.*` pipeline.

Silksong adapters must clone, re-parent, re-layer, and drive resident game UI
objects wherever equivalents exist. Independently authored widgets are
allowed only as documented fallbacks for genuinely missing native objects.
Silksong asset identity replaces Hollow Knight asset identity, but structural,
compositional, and behavioral fidelity to Dual Souls is required. The authored
`DsShell` path is rejected prototype work, and render success does not satisfy
the corrected acceptance gate.

## Signed Silksong device finding

The fork-signed `1.0.3` launcher built the exact Silksong `1.0.29980` source on
the Thor, started a separate ARM64 Unity process, activated the lower physical
display through Unity, created a save, and reached live gameplay. The second
screen rendered its Map/Inventory/Crest/Tasks/Journal content, proving the
selected renderer and display path. The same capture failed the visual gate:
it showed a flat top navigation bar with `MODS` as a peer tab and no persistent
Dual Souls-style HUD/frame. That failure is a blocker for the shared-shell
stage, not a cosmetic deferral.

## Required proof

Host tests may close shell geometry, ordering, state, error-isolation, and
single-display decision logic. The emulator may exercise launcher and
fallback integration. Only the Thor can close the display-1 rendering,
display-specific touch, pause/resume, hot-plug, primary-input isolation, and
side-by-side visual-coherence gates for both games.
