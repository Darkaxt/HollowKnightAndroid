# Hollow Knight lower-display pause gate

## Scope

Host implementation of the five ordered pause requirements in the authoritative Dual Souls 2.0.0 plan. This checkpoint does not implement the later lower-layout synchronization or Silksong pause audit, and is not live Android acceptance.

- One existing-path gate parks the native gameplay HUD on the suppressed companion layer, blanks companion/prompt masks, and suppresses captured gameplay backdrop while paused.
- The independently parented `logoGo` remains on the HUD layer. `LogoTick()` and `PollInventoryToggle()` are unchanged from `fc1162e`.
- Paused gameplay updates, touch and prewarm are skipped; resident panes, frame and selection are retained. Existing enabled/disabled camera policy and HUD-before-tutorial routing resume on the first unpaused frame.
- A replaced live HUD root is returned to the existing native `UI_LAYER` before the cached root changes, preventing stale old HUD art on unpause without destroying the native root.
- Native inventory alone retains its existing policy. No Skins production code changed; three obsolete Skins source-shape assertions were refreshed without weakening their ownership, restored-visual-authority or quiet-sampling checks.

## Verification

The generated C# fixture compiles the production orchestration, layering, role-mask, backdrop and logo method bodies against bounded engine stand-ins. It is not a Unity/device graphics test.

The independent review identified stale old HUD rendering after root replacement during pause. The added assertion reproduced it: **0 passed / 1 failed**, with `lower.Drawn(lower.Health)` unexpectedly true. After the three-line ownership correction, focused pause contracts passed **12/12**. The strengthened replacement case verifies the old HUD remains alive on the native UI layer and invisible below.

Fresh coordinator verification on the final eight-file freeze:

| Check | Result |
| --- | --- |
| Complete Python CI suite | 369 passed |
| Complete shared C# suite | 1,818 passed, 0 failed/skipped |
| Exact HK 1.5.12620 + Android Unity compile | 0 warnings/errors; 852,992-byte DLL; 3 entrypoints |
| Exact SS 1.0.29980 + Android Unity compile | 105 sources; 15 entrypoints |
| Source hashes and diff check | All eight unchanged; clean |

Pause contracts also cover repeated pause-owned submenu cycles, next-frame mask/page restoration, popup/companion/HUD-fade policy, native inventory, routing order, root replacement, product/transport disabled state and exactly zero managed allocation over 3,600 warmed unchanged paused ticks. This host allocation measurement is not Android performance proof.

An earlier worker full-suite run reported a 2,664-byte Skins idle allocation failure. The cause remains unattributed; subsequent worker and final coordinator full suites passed without tolerance or assertion changes. Do not erase the failed attempt or infer its cause.

## Retained evidence

Protected external root:
`D:/Temp/dualsouls-2.0.0-c1c653f-gate/evidence/host-checkpoints/2026-09-30-hk-pause/`

- `pre-stale-root-review/`: prior full verification, independent review and prior freeze.
- `stale-root-red-green/`: confirmed RED, focused GREEN, worker full C# GREEN and corrected freeze.
- `final-main/`: fresh coordinator full-suite/compile output and final freeze.

Final freeze SHA-256: `b68c3c32ce6b19b431cfb7ea438f637ba01ad661ed5d756d73640511db3f5ddb`.

No signed candidate, live pass or cross-profile visual parity pass is implied.
