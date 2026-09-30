# Hollow Knight five-route host checkpoint

Bounded restoration of five native icon routes, Journal and Guide/Map Key. **Not complete lower-layout synchronization, a signed candidate, or live visual parity.**

## Implemented

- Visual order: Inventory / Charms / Guide / Journal / Map. Persisted IDs retain `0 Map`, `1 Inventory`, `2 Charms`; append `3 Journal`, `4 Guide`. Distinct inverse mappings, invalid-ID Map recovery, partial configuration overlay and touch override behavior are covered.
- Measured five-cell shell/hit geometry, fitted verified native icons, bounded cold-donor retries, native selection cursor/glow and existing horizontal cubic page travel. Cross-route slides retain outgoing projected scale/anchor and restore cached transforms at retirement.
- Detached read-only Journal template data, acquired/discovered/fill state, remaining-kills note requirements, chooser/portrait/detail, disjoint prose clips and region-directed bounded detail scrolling. No native Journal entry/list update driver or seen-state mutation runs.
- Guide rows use native root `Control` FSM bindings, `hasPin` and all eleven acquired-pin conditions, localized native row text and fail-closed discovery. No invented tasks or row-local condition guesses.
- Correlated save/native-source/map/skin ownership retires all five caches, including during pause; failed partial builds retire only the affected pane without resetting its retry deadline. Healthy-frame allocation contracts remain exact-zero.
- Retained map RESET action, equipped-row chrome ordering, finalized Inventory dirty-grid reload, one cold no-map label, fixed-size copied text and measured header container were restored/corrected during review.

`LogoTick()` and `PollInventoryToggle()` are byte-identical to published base `8bc9836`. Closed pause/native-inventory policies, Skins and production identity are unchanged.

## Verification

Fresh MAIN execution on the 15-input freeze:

| Gate | Result |
| --- | --- |
| Original-production review-defect playback | 15 failed / 15 executed, 0 skipped |
| Restored focused routing/pause/layout contracts | 118 passed, 0 failed/skipped |
| Complete Python CI | 378 passed |
| Complete shared C# | 1,924 passed, 0 failed/skipped |
| Exact HK 1.5.12620 + Android Unity | 862,208-byte DLL; 3 entrypoints; 4 unused-field warnings, 0 errors |
| Exact SS 1.0.29980 + Android Unity | 105 sources compile; 15 entrypoints |

Earlier partial-build exception playback also reproduced 2 failures, then passed the 103-case pre-review focused set. Review findings and every failed harness attempt are retained; the final tests qualify the fixture `PlayerData`, share its existing owner-serialization collection and exercise actual quit-to-menu retirement rather than pretending display loss destroys resident panes. No assertion tolerances were relaxed. One xUnit collection-count style warning remains.

Production bodies are extracted against bounded engine stand-ins. Native donor residency, actual TMP/fallback-font GPU output, Android performance and live visual/touch parity are not established by these results.

## Remaining agreed scope

- **A18:** Inventory/Charms complete hierarchy and occupied-bounds centering, including active `compCharmsCenterY` removal.
- **A20/A21:** remaining audited pane typography/density/palette/style and Map controls/edges/marker-strip/no-map treatment.
- **A22:** separate Silksong pause/production-owner contract audit.
- Complete cross-game visual parity and impact-aware live evidence remain unaccepted. Do not close the full synchronization checklist from this checkpoint.

## Evidence

Final logs, original-source binaries, freeze and publication binding:
`D:/Temp/dualsouls-2.0.0-c1c653f-gate/evidence/host-checkpoints/2026-09-30-five-routes/`.

Earlier work/failures remain in the linked `2026-09-30-five-routes-*-wip` and `2026-09-30-five-routes-post-api-wip` directories; these historical snapshots are not final candidate acceptance.

Final 15-input freeze SHA-256: `6b50be9566e8d0645dd033a7509871c1dfa5f8e9e78e909850b2a527bd2f0af4`.
