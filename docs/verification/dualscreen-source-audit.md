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

Silksong adapters must reuse resident game UI through the operation prescribed
by the corresponding Dual Souls module. The persistent gameplay health, Silk,
and currency HUD uses the same live instances and drivers and is moved, never
cloned. The separate area/equipped/FPS/battery/status chrome may clone resident
Silksong visual donors or create renderer pools where `Bottom.Hud` does so.
Independently authored widgets are allowed only as documented fallbacks for
genuinely missing native objects.
Silksong asset identity replaces Hollow Knight asset identity, but structural,
compositional, and behavioral fidelity to Dual Souls is required. The authored
`DsShell` path is rejected prototype work, and render success does not satisfy
the corrected acceptance gate.

## Stage 3 combat-HUD source finding

The Hollow Knight reference behavior is source-proven, not inferred:

- `HKDualScreen.cs:500-526` defines pause, inventory, or dual-screen-off as
  the route-back condition and calls `RelayerHud` every active frame;
- `HKDualScreen.cs:655-674` selects the one live `Hud Canvas` grandparent
  (`Anchor TL` when available), recursively assigns `hudLayer` for normal play
  or `UI_LAYER` for the route-back condition, and reasserts every ten frames to
  adopt later-spawned masks/Soul children; and
- `HKDualScreen.Bottom.Hud.cs:620-633` points `hudCam2` only at `hudLayer` and
  derives its bottom-panel geometry from the live source HUD camera.

Therefore the main gameplay HUD contract is **move the one live HUD down and
clean the top screen**, then return that same hierarchy at the oracle's
boundaries. Clone-and-mirror is not an equivalent implementation.

The exact `1.0.29980` static bundles do not contain the live combat-HUD
hierarchy. `hud_assets_all.bundle` contains the `HUD Cln`, `HUD Extras Cln`,
and `Area Title Cln` tk2d collections plus `HUD Anim.prefab`; the UIManager
bundle contains menu HUD settings and frame fleurs, not the runtime health,
Silk, currency, or Crest hierarchy. Literal runtime transform paths therefore
remain a probe gate and must not be guessed from prefab names.

The managed assemblies do expose the typed runtime anchors:

- `GameCameras.SilentInstance.hudCamera.GetComponent<HUDCamera>().GameplayChild`;
- `GameCameras.SilentInstance.hudCanvasSlideOut`, including FSM bool
  `Is Visible`;
- `GameCameras.SilentInstance.silkSpool`; and
- `HudCanvas.IsVisible`.

The single Hollow Knight-layout HUD maps those sources as follows:

| Hollow Knight role | Resident Silksong source | Port boundary |
| --- | --- | --- |
| Masks/health | `health_display` PlayMaker FSM descendants under `HUDCamera.GameplayChild`, with `tk2dSprite`/`tk2dSpriteAnimator`; special health uses `BlueHealth` and `HealthSpecialHealIndicator` | Move the one live health subtree into the Hollow Knight mask row with its existing drivers attached; never clone the FSM, poison/static-count, or event drivers |
| Soul | `SilkSpool` and its `SilkChunk` visual children | Move the one live spool/active visual hierarchy into the Soul slot; retain the original singleton, pooling, audio, and event ownership and never clone it |
| Geo | unique Money and Shard `CurrencyCounter` instances with `CurrencyCounterIcon`, `TextBridge`, and `CurrencyCounterStack` | Move both live currencies inside the one Hollow Knight currency region; retain and accommodate their original counter/stack ownership rather than cloning it |
| Area | `GameManager.GetFormattedMapZoneString(GetCurrentMapZoneEnum())` using the `Map Zones` language sheet | Drive a resident text donor in the Hollow Knight area slot; do not run a cloned `AreaTitleController` |
| Equipped charms | current `ToolCrest.CrestSprite` plus `ToolItemManager.GetEquippedToolsForCrest(CurrentCrestID)`, `ExtraToolEquips`, and each Tool's native HUD/inventory sprite | Put the Crest and Tool sprites in the Hollow Knight equipped row; no Hollow Knight charm art |
| Soul/loadout context | live `BindOrbHudFrame` and `ToolHudIcon : RadialHudIcon` visual subtrees | Move only the required live subtree into its Hollow Knight slot; retain its original subscriptions/coroutines and never clone global drivers |

The gameplay HUD must be moved, not cloned or mirrored. Each routed object must
keep the same instance ID and native driver instances. The port must record its
original parent, sibling index, moved-root local position/rotation/scale, and
every descendant layer it changes before placing it in the Hollow Knight slot
and private HUD layer. Like Dual Souls, routing must be reasserted after native
updates so spawned or driver-reparented children remain on the bottom. Direct
route-back occurs on pause, inventory, or full dual-screen-off/display loss;
the separate companion-page toggle leaves the live gameplay HUD on the bottom.
Before scene replacement or routing-rig teardown, the port must proactively
restore any still-valid moved object as a direct-transport safety operation.
Restoration must cover only adapter-mutated routing properties and must not
overwrite current driver-owned active, health, Silk, currency, or visual state.
Moving the live elements naturally clears them from the primary display; there
is no second copy to suppress. Runtime proof of same-instance routing,
reassertion, and exact restoration remains pending. Missing, duplicated, or
unproven sources fail Stage 3 closed and do not produce a generic fallback.

The static art evidence is sufficient to validate discovered runtime output:
regular health idle source frames are 74x99 with a 126x167 backboard; normal
Silk chunks are 67x168, with a 34x46 cap, 80x15 rod, and 24x61 bind notch;
Money `HUD_coin_v020004` and Shard `shell_shard_icon0004` are 62x62 source
frames; the default Crest frame `HUD_frame_v020005 1` is 423x148, while
Crest-specific frame sequences use 767x247 sources. These identities validate
resident output but do not authorize selecting a static frame instead of the
live object's current state.

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
