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

## Selected boundary

The unified application keeps Silksong's direct Unity display-1 architecture.
Hollow Knight has already passed the Vulkan gameplay gate, so its native
`Presentation`/EGL transport is neither required nor desirable as the common
renderer. Direct-display operation, touch attribution, lifecycle, and
single-display fallback still require physical Thor proof for Hollow Knight.

The outer shell becomes game-neutral. It owns geometry, semantic tab order,
navigation and gestures, touch metrics, selection feedback, content/detail
layout, status placement, idle state, modal priority, diagnostics, and error
containment. Each game adapter supplies its localized data, supported pages,
resident fonts/art, and theme tokens.

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

Dual Souls remains the Hollow Knight functional and visual baseline. Its HUD and area
status, map, inventory, charms, story/dialogue/tutorial overlays, item popups,
fade synchronization, skin coverage, and background/resume behavior must be
implemented through the shared shell or recorded as explicit blockers or
tracked deferrals. Its native blitter, Java touch polling, and bespoke outer
transport are not retained; its outer-layout language is retained.

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
