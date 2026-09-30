# Hollow Knight lower-layout core checkpoint

This is a bounded host checkpoint, **not complete five-pane synchronization or live visual parity**. The retained full audit remains the implementation authority.

## Implemented

- Removed lower FPS/battery objects, builders, polling, texture allocation, cached strings, config fields and frame-hash dependencies. Equipped/notch rows now use a named 40px right margin instead of telemetry coordinates.
- Removed gameplay capture setup from the active path and disable any existing capture camera; the lower ground clears solid black. `bgShow` retains its gameplay/title meaning, so this does not make the logo appear during gameplay.
- Removed the unconditional `compInvOffX` and `compCharmOffY` camera consumers. This alone does not prove full Inventory/Charms centering.
- `LogoTick()` and `PollInventoryToggle()` remain unchanged from base `5357994`; pause state/masks, native-inventory policy, transport and Skins production code are preserved.

## Verified

Executable production-extracted RED: **6 failed / 6**; strengthened capture-construction RED: **1 failed, 5 passed**. Focused layout/pause GREEN: **18 passed**.

Fresh coordinator final verification on ten normalized frozen inputs:

| Gate | Result |
| --- | --- |
| Complete Python CI | 370 passed |
| Complete shared C# | 1,824 passed, 0 failed/skipped |
| Exact HK 1.5.12620 + Android Unity | 0 warnings/errors; 846,336-byte DLL; 3 entrypoints |
| Exact SS 1.0.29980 + Android Unity | 105 sources; 15 entrypoints |
| Bounded independent source/contract review | No confirmed regressions; read-only, no runtime validation |

The existing exact-zero idle assertions were not relaxed. Fixtures are engine stand-ins, not Unity/device render or Android performance proof. Three CRLF source files were normalized to LF before final verification and publication; the original worker freeze/logs are retained separately.

## Explicit remaining scope

All targets and acceptance conditions are retained in the worker handoff, mapped to the existing action ledger:

- **A16/A17:** five native icon routes, stable persistent IDs/distinct inverse mappings, full-width touch cells, real Journal and conditional/localized Guide/Map Key, donor retry and all five cache lifecycles.
- **A18:** measured unselected/selected Inventory invariants and Charms target-region fit, including removal of active `compCharmsCenterY` redesign offset.
- **A19:** full-width shell/hit geometry replacing the remaining text-glyph band; telemetry display/polling itself is removed.
- **A20/A21:** scaled canonical HUD/body/tab hierarchy, centered titles, column density/scrolling, rules/gutters, native cursor/glow, typography/palette/opacity, unboxed body, map edge treatment, control placements/fade and native marker strip.
- **A16–A22:** executable geometry/routing/data coverage for the complete audit, all five real routes and lifecycle boundaries. Later Silksong pause audit remains separate.

No placeholder panes were added. Do not mark the full-layout, five-icon, Journal/Guide or centering requirements complete from this checkpoint.

## Evidence

`D:/Temp/dualsouls-2.0.0-c1c653f-gate/evidence/host-checkpoints/2026-09-30-lower-layout-core/`

Contains original worker RED/GREEN/full-suite/compile logs and handoff, original worker freeze, fresh coordinator verification/review and normalized final freeze.

Final ten-input freeze SHA-256: `2c1b2722facc021f2cabcc0b9042b5d479629969646de6327555a88a84f0561c`.
