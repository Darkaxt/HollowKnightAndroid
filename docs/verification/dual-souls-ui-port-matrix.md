# Dual Souls UI source-to-source port matrix

Date: 2026-08-31

Authority:
`docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md`

Reference: `igawa6/dualsouls`
`5c22451435b772acde0c7e6456f9019bc1baef73`.

This matrix prevents the production work from substituting a separately
authored surface for the approved port. `RETAIN` below means reusable low-level
evidence or infrastructure. It never means that the current authored
composition satisfies the target.

## Reference module matrix

| Reference module | Hollow Knight implementation responsibilities and resident sources | Silksong evidence already present | Correct production decision | Current gap/state |
| --- | --- | --- | --- | --- |
| `HKDualScreen.cs` | MonoBehaviour lifecycle and tick ordering; HUD re-layer; tutorial/focus/credit scan and routing; dialogue box and speaker routing; item/lore overlays; backdrop/title behavior; scene, death, pause, and resume gates | `DualScreenV2` has display bring-up, hot-plug, pause/resume, input fencing, idle detection, and teardown. It does not port the native overlay or composition routing | **REWRITE_PORT** as `DsPortRuntime.cs` plus `DsPortOverlays.cs`; retain only the proven lower-level lifecycle path | Overlay object discovery and game fade/death/cutscene mapping are blockers for Stage 7 |
| `Bottom.Layering.cs` | ATTR/HUD/TUT private layers; dedicated composition cameras; main-camera exclusion; companion visibility gate; native blit; HUD routing; bottom fade reproduction | `DsPresentation` activates display 1 directly, creates an isolated camera/canvas/layer, and strips that layer from other cameras. `DualScreenV2` restores input/visibility on hot-plug and pause | **RETAIN_INFRASTRUCTURE** for direct display and touch; **REWRITE_PORT** the multiple composition roles, visibility gate, and fade synchronization in `DsPortLayers.cs`; do not port EGL/blit | Multiple port layer/camera roles and bottom fade synchronization are blockers for Stages 1 and 7 |
| `Bottom.Frame.cs` | Companion root; resident `Inventory/Border/Inv_Border_Top` ornaments; native `Pane Name`; bottom tabs; selected fleurs; content masks; cached `Inv`, `Charms`, and `GameMap` clones; controlled FSM settle; fit; slide transitions; teardown | `DsShell` independently draws chrome and hosts generic pages. It does not clone or compose the resident interface and has no proven equivalent of the clone/settle/slide pipeline | **REWRITE_PORT** as `DsPortFrame.cs` and `DsPortUtil.cs`; use resident Silksong ornaments/text/cursors; delete `DsShell` after replacement | Exact Silksong resident ornament and pane-host paths must be captured; current authored frame is `REJECTED_PROTOTYPE` |
| `Bottom.Hud.cs` | Re-layers the real `Hud Canvas`; mirrors masks, Soul, and Geo; uses native notch sprites; adds area, FPS, battery, and equipped charms; routes/reframes real dialogue, tutorials, and prompts | `DsHudStrip` draws a synthetic state summary. `DsGameArt` and `DsHornetPanel` contain useful resident sprite/widget discovery, but the combat HUD, dialogue, and prompt hierarchies are not cloned | **REWRITE_PORT** as `DsPortHud.cs`; retain resident discovery knowledge from `DsGameArt`/`DsHornetPanel`; delete the authored HUD/panel after green | Exact live Silksong combat-HUD, dialogue speaker-card, tutorial, and prompt paths are the largest current evidence gap and block Stage 3/7 |
| `Bottom.Inventory.cs` | Clones native `Inv`; runs then freezes its own layout/FSM state; reasserts equipment/items; reads native counters; preserves availability; fits the pane; fills native name/description details | `DsGameArt` locates `InventoryPaneList`, invokes native widget state/display paths, evaluates game visibility, and mirrors resident `SpriteRenderer` composition. `DsInventoryScreen` currently uses those results inside a generic grid | **RETAIN_INFRASTRUCTURE** for discovery and typed data; **REWRITE_PORT** composition as `DsPortInventory.cs`; delete generic grid production path | Need exact stable clone/open/settle/freeze sequence for Silksong inventory managers without committing actions; blocker for Stage 5 |
| `Bottom.Charms.cs` | Post-processes cloned Charms pane; reads native charm backboards/icons; lays out grid/detail; displays notch cost; keeps equipped row; uses native name/description/detail sprites | Current `DsLoadoutScreen` reads `CurrentCrestID`, `ToolCrest.Slots`, equipped tools, and `ToolItemManager`, but draws an independent page and intentionally omits browsing/equip mutations | **REWRITE_PORT** as `DsPortLoadout.cs`; reuse typed discovery; map Charms semantics to resident Crest/Tool objects; delete current authored page | Native Crest/Tool browsing hierarchy and legal equip actions are blockers for Stage 6 |
| `Bottom.Map.cs` | Clones `GameManager.gameMap`; disables unsafe clone FSMs; drives native map setup, quick map, compass, markers, zone/room watchers, framing, availability, and optional bench-pin action | `DsMapView` already binds live `gameMap`, discovers Silksong `CameraRenderToMesh` map cameras, invokes native quick-map/world-map/compass paths, and restores main-screen state. `DsMapScreen` wraps it in an authored page | **TEMPORARY_REFERENCE** for `DsMapView` discovery; **REWRITE_PORT** composition as `DsPortMap.cs`; delete wrapper/render-texture page after native hierarchy port | Must prove whether the live map hierarchy can be cloned/re-layered directly on display 1. Bench teleport is a separate tweak gap, not a reason to retain the authored page |
| `Bottom.Select.cs` | Bottom touch polling; tab/item hit-tests; map pinch/pan; native pane `Cursor` corners; native component identification; live `Item Control` action glyph and localized verb prompt | `DsInput` and `DsTouch` provide display-aware tap/drag/pinch. `DsGameArt.SelectionCursor` locates resident `InventoryCursor` art. Current `DsIconGrid` draws its own selection/detail footer. No native control-prompt mapping is proven | **RETAIN_INFRASTRUCTURE** for input/touch and cursor discovery; **REWRITE_PORT** as `DsPortSelect.cs`; delete generic grid selection | Exact native Silksong action-glyph/verb source and legality boundary are blockers for Stage 5 |
| `Bottom.Tweaks.cs` | Cloned native frame/text; gear; grouped settings/detail; master/reset; state slots; bench/map actions; selection/prompt interaction through the same companion language | Typed `TweakController`, isolated store, and `SilksongGameTweakApi` exist. `DsModsScreen` independently draws the modal; the Silksong adapter currently exposes damage, unlimited Silk, one-hit kills, and equip-anywhere | **RETAIN_INFRASTRUCTURE** for typed behavior and persistence; **REWRITE_PORT** UI as `DsPortMods.cs`; delete authored modal after replacement | Additional tweak APIs remain feature gaps; UI parity and independent relaunch persistence block Stage 8 |

## Current Silksong file disposition

| Current file | Disposition | Replacement or retained responsibility |
| --- | --- | --- |
| `DualScreenV2.cs` | `REWRITE_PORT` | Minimal bootstrap for `DsPortRuntime`; retain proven bring-up callbacks until transferred |
| `DsPresentation.cs` | `RETAIN_INFRASTRUCTURE` + modify | Direct display, isolated composition cameras/layers, visibility, teardown |
| `DsTouch.cs` | `RETAIN_INFRASTRUCTURE` | Display-1 input fence |
| `DsInput.cs` | `RETAIN_INFRASTRUCTURE` | Display-1 gestures consumed by `DsPortSelect` |
| `DsProbe.cs` | `RETAIN_INFRASTRUCTURE` | Object-path and provenance diagnostics |
| `DsConfig.cs` | `RETAIN_INFRASTRUCTURE` | Diagnostic switches; no design geometry authority |
| `DsTestCard.cs` | `RETAIN_INFRASTRUCTURE` | Opt-in transport diagnostic only |
| `DsGameData.cs` | `RETAIN_INFRASTRUCTURE` | Typed read-only data fallback |
| `DsGameArt.cs` | `RETAIN_INFRASTRUCTURE` + split | Resident object/sprite/cursor discovery moves to `DsResidentUi` |
| `DsMapView.cs` | `TEMPORARY_REFERENCE` | Native map discovery moves to `DsPortMap`; then delete |
| `DsTheme.cs` | `TEMPORARY_REFERENCE` | Resident font/asset lookup only; authored geometry/fallback art removed |
| `DsShell.cs` | `DELETE_AFTER_STAGE_2` | Replaced by `DsPortRuntime`, `DsPortLayers`, and `DsPortFrame` |
| `DsHudStrip.cs` | `DELETE_AFTER_STAGE_3` | Replaced by `DsPortHud` |
| `DsHornetPanel.cs` | `DELETE_AFTER_STAGE_3` | Discovery knowledge moves to `DsResidentUi`/`DsPortHud` |
| `DsMapScreen.cs` | `DELETE_AFTER_STAGE_4` | Replaced by `DsPortMap` |
| `DsScreens.cs` | `DELETE_AFTER_STAGE_5` | Inventory composition replaced by `DsPortInventory` |
| `DsIconGrid.cs` | `DELETE_AFTER_STAGE_6` | Replaced by native pane composition and `DsPortSelect` |
| `DsLoadoutScreen.cs` | `DELETE_AFTER_STAGE_6` | Replaced by `DsPortLoadout` |
| `DsTasksScreen.cs` | `DELETE_AFTER_STAGE_6` | Replaced by `DsPortProgress` |
| `DsJournalScreen.cs` | `DELETE_AFTER_STAGE_6` | Replaced by `DsPortProgress` |
| `DsTitleCard.cs` | `DELETE_AFTER_STAGE_7` | Replaced by native title/attribution routing in `DsPortOverlays` |
| `DsModsScreen.cs` | `DELETE_AFTER_STAGE_8` | Typed behavior retained; presentation replaced by `DsPortMods` |
| `DsWidgets.cs` | `DELETE_AFTER_STAGE_8` | No generic widget composition in production port |
| `IDsScreen.cs` | `DELETE_AFTER_STAGE_8` | Native clone/capability runtime replaces generic screen interface |

## Reconciliation ledger

| Stage | State | Blockers | Tracked deferrals | Evidence |
| --- | --- | --- | --- | --- |
| Corrective specification and plan | `IN-PROGRESS` | None in the document update itself | All implementation requirements are assigned to Stages 1–9 | Corrective spec, plan, and this matrix |
| Direct-display transport | `PROVEN_BASELINE` | Multi-role composition isolation and fade remain unimplemented | None | Signed Silksong display-1 gameplay proof and current `DsPresentation` source |
| Authored shell | `REJECTED_PROTOTYPE` | Must be removed from production after replacements land | None | Device review plus source audit proves independent composition |
| Dual Souls composition port | `NOT_STARTED` | Stages 1–8 | None | No production `DsPort*` implementation exists yet |
| Side-by-side device acceptance | `NOT_STARTED` | Depends on the completed port | None | Stage 9 matrix not yet run |

Required work may move between named stages only by updating this matrix and
the plan together. It may not be silently removed or called complete because
the lower display renders.
