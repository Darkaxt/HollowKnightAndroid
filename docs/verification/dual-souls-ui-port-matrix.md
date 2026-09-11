# Dual Souls UI source-to-source port matrix

> **Current execution authority:** The 2026-09-10 visible-HUD corrective
> authority in the specification and recovery override in the plan supersede
> the 2026-09-08 host-first override and historical H4/H5/H6 prerequisites.
> Earlier evidence remains preserved, but no host-only result accepts a visible
> Silksong feature.

Date: 2026-08-31

Authority:
`docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md`

Reference: `igawa6/dualsouls`
`5c22451435b772acde0c7e6456f9019bc1baef73`.

This matrix prevents the production work from substituting a separately
authored surface for the approved port. `RETAIN` below means reusable low-level
evidence or infrastructure. It never means that the current authored
composition satisfies the target.

## Task99 signed live result — 2026-09-10 corrective reset

Candidate `f56defa5f3c7701cdd0adbb565eec2493e1a46ee`, signed by dry-run
`34525989598`, installed update-compatibly and rebuilt exact Linux Silksong
`1.0.29980`. APK SHA-256 is
`43332fa22bf209bba0397e6072cde91a39daec503ae381cca0704e21ca76dd79`;
the pinned signer matched and no release was published.

The first ordinary-gameplay capture invalidates Task99 Batch A acceptance:

| Required visible row | Result | Evidence | Consequence |
| --- | --- | --- | --- |
| Primary gameplay display has no health/Silk/currency/loadout HUD | **FAIL** | `tools/shared-patches-tests/obj/task99-device-checkpoint-20260910/32-new-game-158s.png` | Native health and Silk remain at the primary top-left |
| Lower display carries live health | **FAIL** | `tools/shared-patches-tests/obj/task99-device-checkpoint-20260910/33-first-gameplay-lower.png` | No health visualization is present |
| Lower display carries live Silk and currency | **FAIL** | Same lower capture | Neither semantic group is present |
| Lower display carries current Crest/Tool status | **FAIL** | Same lower capture | Only text tabs and ornaments are visible |
| Silksong composition matches Hollow Knight hierarchy/density | **FAIL** | Primary/lower pair above against the Hollow Knight HUD definition | The result is a sparse authored shell, not the populated Dual Souls companion |
| Exact game launches through current transport | **PASS, infrastructure only** | `23-current-game-launch-60s.png`; version `1.0.29980` | Does not offset any HUD failure |

The former Task99 SPEC PASS, QUALITY PASS and fresh-main PASS are retained only
as source/host diagnostics. They no longer mean that HUD or page composition is
accepted. `DsPortHud.cs` and `DsPortHudState.cs` remain `REWRITE_PORT`, not
accepted HUD implementations. Any matrix text below describing accepted HUD
state is historical and superseded by this section.

### Task99 V1/V2 focused HUD candidate

The exact Linux Silksong `1.0.29980` prefab path is
`_GameCameras/HudCamera/In-game/Anchor TL/Hud Canvas Offset/Hud Canvas`.
The candidate discovers that canvas from the typed current `GameCameras` /
`HUDCamera.GameplayChild` rig and routes all nine direct children. It does not
route the whole canvas or derive independent roots from serialized visual
fields.

| Exact direct child | Classification | Concrete ownership retained |
| --- | --- | --- |
| `Health` | **routed persistent** | exactly two `health_display` `PlayMakerFSM` drivers in the complete health/effect subtree |
| `Extras` | **routed contextual** | exact children `Reserve Bind`, `Lava Bell HUD`, `Maggot Charm`; respectively 1/1/1 `PlayMakerFSM`, 1/1/1 `PositionRelativeTo`, 4/8/3 `EventRegister`, and 0/1/0 `Animator` owners |
| `Thread` | **routed persistent** | sole `Spool` child with exact children `Bind Orb`, `Thread Spool`, `Spool Appear`, `Bind Cancel Effects`, `Curse Silk Cancel Effects`; one `SilkSpool` and nested `Bind Orb`/`BindOrbHudFrame` travel as one root |
| `Tool Icons` | **routed persistent** | exact direct `Tool Icon U/N/D` children, each with one `ToolHudIcon` |
| `Crest Get Effects` | **routed contextual** | exact children `Crest Change Flash`, `white_light`, `Pt Dots`, `black_solid`; flash owns one `DeactivateAfter2dtkAnimation` and one `tk2dSpriteAnimator`, dots own one `ParticleSystem`; no invented persistent Crest sprite |
| `Delivery Icon` | **routed contextual** | one root `DeliveryHudIcon` with exact children `Parent` and `burst_appear_generic` |
| `Counters` | **routed persistent** | exact `Geo Counter`, `Shard Counter`, `Item Counter Template`, `Liquid Counter Template` children; one root `CurrencyCounterStack`, one Money and one Shard `CurrencyCounter`, one `ItemCurrencyCounter`, and one `LiquidReserveCounter` |
| `Blue_Health_Overblue_HUD_burst` | **routed contextual** | exact `haze2`/`particles` children and one each root `CameraControlAnimationEvents`, `Animator`, and `DisableAfterTime`; no unsupported `BlueHealth` assumption |
| `Blue_Health_Overblue_HUD_drips` | **routed contextual** | one same-named direct child and exactly one `ParticleSystem` on both root and child |

The early-game device state cannot currently reach Reserve Bind, Lava Bell,
Maggot Charm, delivery, Crest-acquisition, overblue, or item/liquid-template
presentations; these are **currently unreachable early-game**, not omitted.
Their owning direct roots still route so later native activation and spawned
children remain on display 1. The Unity-independent production admission core
`DsHud29980Topology.TryAdmit` is called by `DsResidentUi`; any missing,
duplicate, reordered, or wrongly owned exact inventory fact fails closed and the
normal 0.5-second probe remains reachable. Focused host tests prove nine-root
mutation, empty original parent, private target layer, unchanged
driver/active/child state, spawned-child adoption, exact restoration, and
rejection of genuinely nested route roots. They do not claim Unity rendering or
fake host display visibility. The exact cached compile is host/source evidence
only; visible scale, clipping, ordering, and native activation remain device
gates.

Tracked evidence is
`docs/verification/task99-hud-29980-evidence.json`. It records both exact source
bundle hashes, the concrete census, the 68-source compile-manifest algorithm and
SHA-256 `c45edc286bc2a19f78c2e77e00f44832797974621ac148b1f72af7b0ed701fd4`,
and the honest host/device boundary. The forced compile command was:
`dotnet build tools/shared-patches-tests/obj/task99-journal-continuation-20260908/ss-project/PatchCheck.csproj --no-restore --no-incremental -c Release -p:UseSharedCompilation=false -nodeReuse:false --output tools/shared-patches-tests/obj/task99-hud-correction-20260911/master-776c8fa-rebuild/bin --nologo`.
Tracked receipt, source manifest and compiler log are under
`docs/verification/evidence/task99-hud-master-776c8fa/`. The receipt records UTC
start/end, exit code 0, `0 errors / 7 existing warnings`, output SHA-256
`b01415179b20821010d5ef9a51d53c3fa149820a4dafb4b1632af38f68ed0b98`, and exact
compiled source HEAD `776c8fa30a6ca161e8ee2522f485cf8d339b9025`. This evidence-only follow-up
changes no compiled source. The failed stale project remains preserved at
`tools/shared-patches-tests/obj/task99-hud-correction-20260911/stale-cached-project/compiler.log`
(exit 1, 0 warnings, 3 missing-source errors) and receives no compile credit.

The active gate is plan V0–V5: pin one bounded Hollow Knight reference, route the
four minimum essential native Silksong groups directly, inventory every
additional source-proven or runtime-observed Silksong HUD mechanic, integrate
those groups into the same Hollow Knight-derived status/loadout/context regions,
run focused regressions and the exact compile, then immediately repeat the
signed two-display gameplay capture. The four named groups are a baseline, not
an exhaustive Silksong feature list; actual additional mechanics may not be
omitted or left in a second primary-screen UI, and speculative mechanics may not
be invented. No H4/H5/H6 closure, page work, Mods work, skin work, generalized
framework, or large golden expansion may precede that gate. A disposable new
game was created in profile slot 3 for validation and must be the only save
removed before final APK delivery.

## Task99 residual6 font-owner handoff — 2026-09-09 19:41 UTC

SAME SPEC reviewer now reports **1/2/3/4/5 RESOLVED** and6's committed-effect
lifetime corrected. The sole new correction below addresses6's conditional
text-bearing-extra font-owner mismatch; it awaits that reviewer's6-only recheck.
**Task99/parent97 OPEN;7/8 remain unapproved blockers; no QUALITY/full acceptance,
Mods or Task100.** The reviewed344/105/DLL`70c8a59f…` checkpoint is preserved.

- **Exactly one production file changed:** `DsPortProgress.Inventory.cs`. An
  admitted extra was prepared with `visual.Text`, then reparented under `page.Root`,
  where `Force` validates with `page.Text`. The strict font-identity gate correctly
  rejected that other owner's font. `ShowConsumeExtra` now uses
  `page.Text.Prepare(extra, page.Token.Content)`, matching existing custom-icon
  ownership. The unused ConsumeVisual.Text field and its Clear call are removed.
  No traversal exclusion, weakened font gate, helper framework or lifetime change.
- Root registration/budget and inactive preparation still precede activation.
  `Force` retains full page validation (only the existing separately validated detail
  exclusion). A failed Prepare poisons the page text owner before any future
  traversal, including no-text roots. `DestroyOwned` must finish retiring EVERY
  committed-extra root and page staging/root before page.Text.Clear can release
  fonts. A failed exact-root/font retirement retains obligations for retry.
  `DsPortProgress.cs` and `DsPortSelectState.cs` are unchanged.
- Source-native ordering remains the retained
  `native-InventoryItemCollectable/output.log:658–681`: commit signal wait precedes
  extra creation;535–584 ordinary release ends hold work,275–283 pane retirement
  ends retained effects. No native callback, Take/response or timing was changed.
- Seven new linked controls cover text/plain extras, foreign-font rejection,
  root retry, font retry, failed preparation preventing Ready publication, and an
  explicit negative separate-owner mismatch. They execute existing production
  DsPortOwnedGraph, DsPortTextPreflight.Resolve, commit/lifetime and page-state
  mechanics with managed font/root/signal fixtures; the TMP poison wrapper is a
  source-checked fixture. The new production-wiring contract verifies the exact
  preparation/traversal/retirement owners and unchanged strict gates. This is
  **NOT execution of Unity PaneText, native signals, meshes or rendering**, nor
  evidence every required serialized extra graph is admitted.

Receipts under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:

| Receipt | Actual result | Owned PID; UTC start–end; elapsed |
| --- | --- | --- |
| `spec-residual6-font-wiring-red` |1 expected source-wiring failure before production edit |13404;19:38:00.438–00.581;.143s |
| `spec-residual6-font-policy-control` |7/7 pre-fix linked controls, NOT production behavioral GREEN |11776;19:38:13.499–18.009;4.510s |
| `spec-residual6-font-focused-green` |11/13 FAILED: fixture incorrectly expected Tick to swallow errors; preserved despite receipt name |6476;19:39:20.716–25.019;4.302s |
| `spec-residual6-font-focused-final` |13/13:7 new controls +6 prior lifetime cases |27248;19:39:55.079–59.476;4.397s |
| `spec-residual6-font-contracts` |106 allowed tests, OK |42392;19:40:08.667–08.924;.257s |
| `spec-residual6-font-combined` |351/351, realCoreCompile=true |70556;19:40:15.405–19.735;4.330s |
| `spec-residual6-font-ss` |realCoreCompile=true;0 errors,7 existing unrelated warnings |15216;19:40:26.748–28.746;1.998s |
| `spec-residual6-font-identities-source` |64 source hashes; all314/334/344/275 identities retained; only Inventory changed |35664;19:41:26.067–26.164;.097s |

The two failed receipts exit1; all others exit0; all timedOut=false. The fixture-only
assertion was corrected to expect the existing page-state retire-and-rethrow path.
No active owned child. Inventory SHA-256:
`6a1900f033874d870bff8562b31aa73390adae6228cbc9be49ec7a8c24773d2e`.
Current SS DLL SHA-256:
`7a5305039ddb62994bb533a3d4ca0687862aacc23b72889cd8db8e8da5c7e810`.
Compiled-source manifest SHA-256:
`0484ad134f51a2e7e9674950f9dead21dac28710a0941397895dd4e0fd31a484`.
No production/test edit follows final verification/hash capture. All operational
boundaries and historical evidence preserved; unsafe restoring/deleting/unbounded
contract remains excluded without pass credit.

## Task99 residual3/6 handoff — 2026-09-09 19:20 UTC

Independent SAME-SPEC recheck reported **1/2/4/5 RESOLVED,3/6 PARTIAL,7/8 OPEN**.
This correction changes only3/6; their candidate fixes below require that reviewer's
recheck. **Task99/parent97 OPEN; no QUALITY/full acceptance/Mods/Task100.** The
reviewed334/103/DLL`5fca0c0c…` checkpoint and every historical failure remain intact.

- **3 — tool-entry animation candidate correction.** Exact retained native
  `native-InventoryItemTool/output.log:150–178` binds the separate entry slotAnimator
  and its refresh callback;288–342 plays Full/Empty/Equip/Unequip and switches
  attack/skill variants. `DsPortProgress.Loadout.cs:528–630` now admits the exact
  InventoryItemTool slotAnimator as well as the already-admitted crest slot drivers.
  All three authored controller arrays are shape-checked and frozen by identity;
  controller clips/state callbacks are checked on the born-inactive owned copy,
  restoring its original controller in finally. Live controller selection stays
  native-owned. Generated targets must map to the exact owned template path and
  retained variants, with local entry/manager and visual-only target authority.
  `AssertInputBlocked:631`, `RefreshActionPage:932` and `SubmitCore:1620` recheck
  enabled target/controller identity and keep primary input disabled. Four linked
  fixture cases cover equip/unequip, borrowed Animator and changed variant; source
  contracts verify actual preparation, refresh and submit wiring.
- **6 — committed-effect lifetime candidate correction.** Native
  `native-InventoryItemCollectable/output.log:535–584` stops the routine/owned
  consumption work on release but does NOT hide signal/recycle extra;275–283 owns
  pane-end cleanup.658–681 retriggers the signal, waits, then spawns a distinct extra.
  `DsPortProgress.Inventory.cs:614–684` separates exact per-entry page-owned visual
  records from cancellable hold/audio state. `CancelConsume:859` detaches the wait
  callback without invoking it, stops coroutine/audio/rumble and retires only
  never-committed preparation. A cancelled next hold cannot destroy prior committed
  effects. Successive commits allocate distinct owned extras only AFTER the signal
  wait; prior extras survive. Known total extra-node budget is checked BEFORE commit
  and bounded4096 per page; failure after commit never authorizes response/Take
  replay. Signal/extra visuals may finish naturally but remain bounded page-owned
  until retryable `DestroyOwned:1295` retirement. Native independent fade scheduling
  and Awake-captured original pose are retained rather than reset on release.
  `DsPortSelectState.cs:519` owns this small lifetime policy. Six linked fixture cases
  cover precommit cancellation, release during signal (no unreached extra), release
  during extra, cancelled next hold/distinct subsequent extra, owner loss and
  retirement retry; actual production order/cleanup/budget is source-contract checked.

Current receipts under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:

| Receipt | Executed result | Owned PID; UTC start–end; elapsed |
| --- | --- | --- |
| `spec-residual36-wiring-red` |2 source failures: missing tool preparation; actual unconditional signal retirement |60980;19:06:56.630–56.780;.149s |
| `spec-residual36-policy-red` |0/9: missing-type Assert.NotNull failures, NOT native behavioral REDs |20484;19:07:04.517–09.038;4.521s |
| `spec-residual36-focused-green` |10/10 linked cases |74132;19:18:40.483–44.920;4.437s |
| `spec-residual36-contracts` |105 allowed tests, OK |32332;19:18:53.826–54.071;.245s |
| `spec-residual36-combined` |344/344, realCoreCompile=true |69080;19:19:01.297–05.555;4.258s |
| `spec-residual36-ss` |realCoreCompile=true;0 errors,7 existing warnings outside changed files |13772;19:19:13.719–15.502;1.784s |
| `spec-residual36-identities-source` |all314 baseline/334 reviewed/275 overlay identities preserved;10 additions |60024;19:20:03.835–03.937;.101s |

RED receipts exit1; all listed GREEN/current receipts exit0; all timedOut=false.
Earlier wiring/policy check and first-SS receipts also remain preserved. No active
owned child. DLL SHA-256
`70c8a59f6fdfa799b26198a9c042bae52745f574a7762d2dba50604ceaefc756`;
compiled-source manifest SHA-256
`71d9f1b0a6bba6d4388b47422b29a3abc10520320836e8d82ff245a095b2256e`.
Manifest comparison confirms only Inventory,Loadout,SelectState changed since the
reviewed checkpoint; resolved overlay/frame/map/page-state sources are unchanged.
No production-source edits follow this compile/hash capture.

**Evidence limit unchanged:** linked managed fixtures + actual wiring + exact native
source + cached compilation, NOT native Unity controller/coroutine/effect execution
or required serialized graph inclusion.7/8 remain unapproved blockers, without new
research or scope waiver. No nested agent, restore/download, config/git mutation,
cleanup, device, packaging/source-image activation, native-memory/save edit or
publication operation occurred. The unsafe restoring/deleting/unbounded contract
remains excluded without pass credit.

## Task99 same-SPEC correction handoff — 2026-09-09 18:38 UTC

**Task99/parent97 OPEN. Candidate corrections for the SAME independent SPEC
reviewer; no SPEC acceptance, QUALITY, fresh-main acceptance or Mods advancement.**
This section supersedes only the current-source/verification status below, not
historical failures or evidence. No Unity/native graph, coroutine, animation,
audio, GPU or device execution is claimed.

| Finding | Current production correction / exact residual |
| --- | --- |
| 1 — native fade bridges | `DsPortOverlays.cs:746` validates exact native sprite/TMP bridge, co-located renderer/text and native fade-parent references; actual carrier/credits admission calls it at641/1490. Five executable bridge cases are **reference-policy fixtures**, not actual bridge-populated Unity admission. Required concrete bridge graphs remain unexecuted. |
| 2 — ordinary submit boundary | `DsPortProgress.Loadout.cs:1540–1597` retains ordinary crest/place/remove submits inside `_actions.Run`, exact slot/item targets through callback unwind, rechecks after native UnequipTool before SetEquipped, then refreshes the same owned page. Three new owner/retirement/presentation-failure cases check retained target lifetime and no repeated commit with linked boundary/page policies; not Unity SetEquipped execution. |
| 3 — successful slot animation | `DsPortProgress.Loadout.cs:525–558` enables only exact slotAnimator/slotFilledAnimator references in validated owned visual islands, rejects root motion/state callbacks/clip events/input-cache writers, and revalidates required drivers. Ordinary success no longer immediately invalidates. **Required serialized slot animator topology is still unproven**; this conservative gate can reject a required authored graph and is not inclusion evidence. |
| 4 — partial parent/sibling restore | Dialogue `DsPortOverlays.cs:527–538` and OpeningCredits692–696 use existing attempted-parent/sibling-completion restoration, retaining sibling work after a successful SetParent followed by failure. Three linked restoration fixtures and actual Dialogue/credits wiring contracts pass; native Unity failure injection is unexecuted. |
| 5 — visible outgoing contents | `DsPortFrame.cs:75`, `DsPortProgress.cs:163–203`, page SelectionChanged/Tick paths and `DsPortMap.cs:1197` retain only the exact outgoing owned presentation/image until completion/interruption/owner loss/hide. `DsPortJournalState.cs:181` immediately revokes Ready/token without clearing visible rows/detail/cursor. Actions cancel; Map retries pending restoration before retaining its last image. Four linked lifecycle cases retain **visible-content fixture values**, not merely host positions; Unity visible content remains unexecuted. |
| 6 — explicit Inventory consumption | `DsPortProgress.Inventory.cs:537–827` now supplies explicit selected-item Down/hold, raw release/drag/loss cancellation, source-native .5/1.5s hold, response/Take, exact owned animation-event wait, .5/.3s pauses/chaining, retained native closing-use obligation and native empty-item fallback on rebuilt owned content. `DsPortSelectState.cs:463` prevents replay across response/take/close callbacks. Native cache-entering ConsumeRoutine/UpdateItemDisplay, pooling and creating HeroChargeEffects access are not invoked. Denied feedback uses owned failed animation/audio and source-adapted memory fade/message state; native manager Show/HideMemoryUseMsg writes primary paneList.InSubMenu and is deliberately not called. Cleanup/owner-loss/blocked-feedback wiring is contract-checked. Five commit-policy cases plus existing hold/boundary controls pass, **not native consumption execution**. Required effect/audio/template graphs remain unproven; unsupported visual/event/audio graphs fail closed. A pending closing use whose exact gameplay owner disappears retains one bounded obligation for existing retirement retry, never calls a replacement owner. |
| 7 — T99B-OVERLAY-TUTORIAL-INPUT | **Concrete endpoint blocker unchanged.** Retained `native-UIMsgBase-overlay/output.log:79–99` waits only on native WasSkipButtonPressed before hide/completion. No retained-instance legal companion dismissal endpoint is established. No synthetic skip, Spawn/Setup replay or early completion; close only with a legal endpoint or explicit user scope approval. |
| 8 — T99B-MAP-PRESENT-MIXED-QUEUE | **Required-graph/pass evidence blocker unchanged.** `DsPortMap.cs:1171` still rejects mixed material queues within a SortingGroup. Presence/exclusion in required serialized graphs and native interleave are unproven. This is not a general Map failure or accepted support-by-rejection. |

Final receipts under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:

- `spec-corrections-final-contracts`: explicitly read **103 tests, OK**;
  PID66100, 18:37:21.671–21.898Z, .227s. The restoring/deleting/unbounded
  pure-frame subprocess contract remains excluded without pass credit.
- `spec-corrections-final-combined`: **334/334**; PID57628,
  18:37:28.486–32.846Z, 4.360s, realCoreCompile=true.
- `spec-corrections-final-ss`: actual current SS CoreCompile=true;
  PID42168, 18:37:39.069–41.035Z, 1.966s. DLL SHA-256
  `5fca0c0c395164730a17b9b518b71a7a2b674bccfbc4a102468cee88339bf8ed`.
- `spec-corrections-final-identities-source`: PID70252,
  18:38:28.880–28.969Z, .089s; all **314 baseline and275 overlay identities**
  retained,20 additions, zero missing/nonpassing. Full compiled-source manifest
  SHA-256 `7cf3f9a8886a010815a691d39701b8d46bbae7d2ee6f654e7dd72b5e73469ebd`.
- All four final receipts exit0/timedOut=false; no active owned child. Earlier
 334/103 and SS receipts are historical, not substituted for final-source checks.

Failures remain preserved and correctly classified: original overlay contracts
**97/99** despite receipt name `spec-overlay-contracts-green`; stale wrapper/local
assertion failures in `spec-overlay-ordinary-contracts-check`; missing-helper
bridge/retention/consume REDs (not executed native behavioral REDs); actual wiring
REDs; `spec-inventory-consume-first-compile` audio type compilation failure;
`spec-inventory-consume-policy-green` dynamic test-delegate binding failures before
`spec-inventory-consume-policy-verified`18/18. No failure/timeout/old core/golden was
removed. No source integration activation, packaging, device, native-memory/save
edit, config/git mutation, publication or cleanup occurred.

Required-graph closure remains under **T99B-CREST-GRAPH / T99B-PAGE /
T99B-VISUAL**: validate actual native bridge/slot/effect/audio graphs and successful,
denied, cancellation and owner-loss behavior without loosening exact ownership.
The source contracts and linked fixtures do not close those evidence gates or7/8.

## Task99 bounded implementation handoff — 2026-09-09 17:10 UTC

**Task99/parent97 OPEN; independent SPEC, separate QUALITY and fresh main
verification remain required.** This reconciles the current implementation;
all historical checkpoints and receipts below remain preserved.

- The concrete equipped-crest-only ACTION restriction is corrected, not deferred
  as unknown prefab evidence. Native supplied-slot `Submit`/`DoPress`, manager
  `PlaceTool`/`EndSelection`, and crest `SaveEquips` establish the displayed crest
  name as save target. `Legal` now requires exact owned selected crest, retained
  source/native registered identity, unlocked/visible/not-hidden state, slot
  descriptor/index and native callback peers. Global equipped ID remains a stale
  owner guard, not the target. Existing bench/cursed/type/tool-equipped gates remain.
- Selected-crest policy absence RED **0/6** and wiring RED precede **6/6** GREEN.
  Combined **314/314** retains all **308** prior identities plus exactly six;
  original overlay275 remains intact. Allowed contracts **97/97**. Receipts:
  `action-selected-crest-authority-red`, `action-selected-crest-wiring-red`,
  `action-selected-crest-authority-green`, `action-selected-crest-contracts-green`,
  `task99-bounded-implementation-combined-green`, `task99-bounded-implementation-identities`.
- The four existing `ActionHoldRechecksDeferredLegalStateBeforeNativeStep` loss
  identities now mutate distinct owner, resource-legality, selected-item and
  visibility fixture inputs consumed by linked `DsPortSelectState.ActionAvailable`
  and `DsPortActionHold.Step`. Each asserts only its named input changed while
  hold generation remains current; no repeated shared false flag or removed row.
  This test-only correction is **4/4** focused, then **314/314** combined, with
  exact prior314/overlay275/focused identities retained and no xUnit1026 warning.
  It models native inputs; it does not execute Unity `ExtraLegal`/`ActionBase`.
- Latest receipts under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
  `action-distinct-loss-green` (PID57700, 17:09:05.490–14.112Z, 8.621s),
  `task99-handoff-combined-green` (PID50812, 17:09:49.066–57.777Z, 8.709s),
  `task99-handoff-identities` (PID21100, 17:10:14.099–14.213Z, .114s),
  `task99-handoff-contracts-green` (**97/97** after ledger reconciliation,
  PID32040, 17:11:43.143–43.476Z, .332s).
  All exit0/timedOut=false. No active child remains.
- `action-selected-crest-current-compile-green` records actual SS CoreCompile,
  DLL SHA-256 `ef39b4b7edb0933ef94f90fc01241ab4918c7307ba1621318cede394565556e6`.
  A later narrow display-discrepancy check captured valid `// Native MoveNext...`
  text and Loadout SHA-256 `0668918615a0b40242fff7a87b107f664ee797253764c9135147728046958588`.
  No syntax failure reproduced and no syntax edit was made. Despite its name,
  `action-current-source-drift-compile-red` is **GREEN**, not RED credit:
  PID60840, 17:08:27.369–30.687Z, 3.317s, exit0/no timeout, realCoreCompile=true;
  current SS DLL SHA-256 `c3148b81ff20d221abee7f59c70bb802ff7d32e430b3071f5e3b9100a9ce83ca`.
  Source capture is retained in `action-current-source-drift-capture`.

Map freshness/command-only rendering/sorting below is implemented at host scope,
not still an untouched host task. The two concrete limitations remain separately
OPEN: **T99B-OVERLAY-TUTORIAL-INPUT** and **T99B-MAP-PRESENT-MIXED-QUEUE**, with exact
behavior/closure conditions in the table below. Ordinary graph admission and
Unity/native/GPU execution are separate unknown evidence, not those limitations'
justification or acceptance. No fresh HK compile, native execution, full parity,
Task99 acceptance or Task100 advancement is claimed.

## Task99 Map presentation host checkpoint — 2026-09-09 16:53 UTC

**Bounded implementation handoff, not Task99 acceptance.** All prior receipts
and unknown runtime gates remain preserved.

- Same-observation freshness now snapshots explicit native recipe membership and
  values: serialized zone/room/condition/layout/template fields, source transforms,
  renderer/material ordering, compass/hero/maze/override/tilemap/corpse inputs and
  exact native camera projection/mask/sort authority. It does not traverse or
  serialize PlayerData. Same-owner/same-count changes reject stale output; a
  fresh observation retries. Publication rechecks after owned-resource retirement.
- Only the two owned draw command buffers execute into the owned output. Native
  cameras retain their exact masks/depth/projection authority without invoking
  `Camera.Render`, attaching/detaching native buffers, or touching callback lists.
  Commands own target/viewport/depth/color clears and GPU projection. Marker and
  corpse-arrow pose transactions now affect owned renderer donors, not primary
  transforms; retained rollback/resource retirement still precedes replacement.
- Draw records include material render queue, native sorting layer/order,
  camera transparency mode/axis, SpriteSortPoint, distance and SortingGroup
  hierarchy/`sortAtRoot`. Nested groups use their outer group's distance rather
  than independently sorting against the camera. Equal-key ties are stable,
  not a claim to have executed Unity's internal tie-breaker.
- Executed **308/308** combined, exact **299** prior identities retained plus
  exactly **9** new Map controls; all original overlay275 retained. Zero missing,
  unexpected or nonpassing. Allowed contracts **96/96**. Actual cached final SS
  `CoreCompile=true`, DLL SHA-256
  `a1902bf19ea84a3f4d232b7f9f81f7c16e760d06c43951ff3d5c31ab955ba120`.
- Receipt base `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
  `map-presentation-combined-green` (PID74312, 16:52:14.505–20.096Z, 5.590s),
  `map-presentation-case-identities` (PID44140, 16:52:35.403–35.495Z, .091s),
  `map-presentation-contracts-green` (PID62148, 16:52:46.111–46.401Z, .290s),
  `map-presentation-current-compile-green` (PID3860, 16:52:52.972–55.380Z, 2.407s).
  All exit0/timedOut=false. Missing-policy/wiring REDs, post-retirement publication
  RED and nested-group-distance RED are retained. Earlier compile is not credited
  for either later fix. No owned child remains active.

### Remaining Task99 seams for independent SPEC

| ID / target | Exact residual and closure condition |
| --- | --- |
| T99B-OVERLAY-TUTORIAL-INPUT / Task99 message interaction | Native tutorial/PowerUp dismissal remains in its native coroutine; no source-proven callable staged companion endpoint exists in retained source. Companion input fences underlying actions without injecting confirm or writing/replaying progression. Close only with a proven legal endpoint or explicit acceptance of this precise native-only interaction gap. Item/lore presentation is not an invented submit endpoint. |
| T99B-MAP-PRESENT-MIXED-QUEUE / Task99 Map admission | Mixed-material-queue SortingGroups are explicitly rejected because their exact native pass/group interleave has no retained finite authority. This is an implementation limit, not supported-by-rejection or a Unity pass. Close with concrete required graph/pass evidence and matching owned command ordering, or evidence that required graphs do not use this case. |
| T99B-ACTION / Task99 native action scope | Equipped-crest-only placement/removal restriction superseded by the 17:10 selected-crest correction above: exact displayed crest/source/slot/native SaveEquips callback authority is implemented and host-tested. Native action execution and ordinary graph equivalence remain unexecuted; these are not a remaining globally-equipped-only code gate. Independent SPEC must validate the supplied-slot legal path without broad bypass. |
| T99B-PAGE / T99B-CREST-GRAPH / T99B-TASKS / T99B-VISUAL | Ordinary concrete serialized graphs, native callbacks/reflection/coroutines/animation/particles, GPU clipping/material/pass equivalence, and message/page layout remain unexecuted. Known owned factories are implemented, but unknown graph admission is not normal-content acceptance. Validate exact required graphs without loosening ownership guards. |
| Task99 / parent97 acceptance | Independent SPEC, separate QUALITY and fresh main verification remain required. No self-acceptance, no Task100 Mods advance, no Tasks101/102 completion. |

No additional concrete required host omission is asserted closed by the test
counts. The remaining message endpoint and mixed-queue pass authority cannot be
invented from unavailable evidence. Independent review must evaluate these exact
limits, not treat this handoff as full native/visual parity. All offline/no-restore,
no-device/no-publication/no-config/no-git-mutation/no-save-edit/no-cleanup bounds
remain unchanged; no fresh HK compile or Unity execution is claimed here.

## Task99 ACTION host checkpoint — 2026-09-09 16:37 UTC

Task99/parent97 remain **OPEN**. This supersedes historical ACTION omissions,
not the preserved overlay275 checkpoint or any unpassed runtime gate.

- Native locked empty-slot hold/cancellation and locked equipped-slot explicit
  removal are wired; unlock payment remains inside the native coroutine.
  Touch generation/liveness is observed before overlay precedence. Native waits
  pass through unchanged; `ExtraReleased` is never cleanup. Reload uses native
  `Extra`/`Update`; toggle uses native `ToolItem.DoToggle`, preserving native
  refresh/animation/audio ordering with only its pooled visual replaced.
- Socket custom icons use an inactive, registered owned native factory without
  entering the global `InventoryItemCollectable.Item` cache. Toggle effects and
  native unlock-burst donors have exact source/clone graph admission and owned
  lifetime. Repeated effect activation no longer reconstructs runtime listeners
  or activates the entry's parent. Native particle play/stop/fade remains; the
  exact private `OnUpdate` is driven once/frame for active owned instances, with
  the native creating-singleton subscription disabled before activation.
- Successful native actions refresh the same owned page after dispatch returns,
  preserving effects without replaying commits. Reentrant retirement is retained
  until native callback unwind; failures do not auto-repeat toggle/payment.
- **Classification correction:** `CustomButtonCombo` is native informational
  gameplay instruction, displayed through the native combo prompt. `ToolItem`
  has no inventory `CustomAction` endpoint; inventing one is not remaining work.
- Executed **299/299** combined cases: all **275** previous `(testId,testName)`
  identities retained, exactly **24** new action cases, zero missing/unexpected/
  nonpassing/focused-missing. Focused **124/124**; allowed contracts **93/93**.
  Cached current SS `CoreCompile=true`, DLL SHA-256
  `7688e4e064873d32db6cfcbcdf3cfab39490b687aee34ca9c18bdc26f5d5b83f`.
- Receipts under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
  `action-unwind-selection-green` (PID32324, 16:36:06.478–11.927Z, 5.448s),
  `action-unwind-contracts-green` (PID36044, 16:36:25.736–26.012Z, .276s),
  `action-unwind-current-compile-green` (PID27272, 16:36:33.236–35.487Z, 2.250s),
  `action-factories-combined-green` (PID60472, 16:37:15.940–21.267Z, 5.326s),
  `action-factories-case-identities` (PID43556, 16:37:36.717–36.796Z, .079s).
  All exit0/timedOut=false. Earlier RED/failure receipts remain preserved,
  including missing unwind boundary and repeated-effect/callback source defects.
- This is linked-policy/source/compile evidence, **not Unity/native coroutine,
  concrete prefab, particle scheduling/rendering, or gameplay execution**.
  Strict external-physics/deparenting/unknown animation-event graph restrictions
  remain; ordinary concrete graphs remain unknown, not accepted by rejection.
  Non-equipped-crest action variants are not broadly accepted. Next host work is
  **T99B-MAP-PRESENT** freshness/sorting/callback authority, then full99 SPEC,
  separate QUALITY and fresh main verification. Tutorial native-only dismissal
  gap `T99B-OVERLAY-TUTORIAL-INPUT` remains separate. Tasks100/101/102 are not done.
  No devices, packaging, publication, git mutation, restore or cleanup occurred.

## Execution-priority gate

The authoritative execution plan is now
`docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md`.
Hollow Knight must first run the existing Dual Souls companion, Mods, and
multi-pack skin/death-rotation behavior through the direct-display transport
and pass the complete device reference matrix. Shared contracts are extracted
from that accepted result; only then may the Silksong module rows below resume.

The completed Silksong transport/frame evidence remains valid. Stage 3's
uncommitted probe and all later Silksong composition work are `PARKED`, not
cancelled or accepted. Their recorded blockers remain intact.

## Reference module matrix

| Reference module | Hollow Knight implementation responsibilities and resident sources | Silksong evidence already present | Correct production decision | Current gap/state |
| --- | --- | --- | --- | --- |
| `HKDualScreen.cs` | MonoBehaviour lifecycle and tick ordering; HUD re-layer; tutorial/focus/credit scan and routing; dialogue box and speaker routing; item/lore overlays; backdrop/title behavior; scene, death, pause, and resume gates | `DualScreenV2` retains display bring-up, hot-plug, pause/resume, input fencing, idle detection, gesture acquisition, and ordered teardown. Only after presentation readiness does it create `DsPortRuntime`, which tracks composition-root visibility, idle state, and active-scene revision without drawing or retaining gestures. The rejected `DsShell` path is no longer reachable from production | **REWRITE_PORT** as `DsPortRuntime.cs` plus `DsPortOverlays.cs`; retain the proven host/transport lifecycle in `DualScreenV2` until a later stage has evidence to move it | Frame/HUD/pages remain tracked to Stages 2–6; overlay discovery and game fade/death/cutscene mapping remain tracked to Stage 7 |
| `Bottom.Layering.cs` | ATTR/HUD/TUT private layers; dedicated composition cameras; main-camera exclusion; companion visibility gate; native blit; HUD routing; bottom fade reproduction | `DsPresentation` directly targets display 1 with a black-clearing content camera/canvas/root on proven blank layer 6 and a depth-only overlay camera/canvas/root on proven blank layer 3. Both use the same measured/scaled panel geometry; the combined owned mask is swept from every non-companion camera. `DsPortLayers` owns empty content/frame/pages/HUD and overlays/fade roots plus their visibility only | **RETAIN_INFRASTRUCTURE** for direct display and touch; **REWRITE_PORT** the composition roles, visibility gate, and fade synchronization in `DsPortLayers.cs`; do not port EGL/blit | Stage 1 boundary is host-verified; frame/page/HUD population is tracked to Stages 2–6 and overlay/fade behavior to Stage 7 |
| `Bottom.Frame.cs` | Companion root; resident `Inventory/Border/Inv_Border_Top` ornaments; native `Pane Name`; bottom tabs; selected fleurs; content masks; cached `Inv`, `Charms`, and `GameMap` clones; controlled FSM settle; fit; slide transitions; teardown | `DsPortFrame` owns dynamic content/status/Mods regions, the approved Inventory/Loadout/Tasks/Journal/Map order, native `InventoryPaneList.GetPane(...)` labels cloned from serialized `currentPaneText`, glyph-bound bottom-centred layout and hit slots, selected/inactive native-label alpha 1.0/0.6 with RGB preserved, cached empty page hosts, interruption-safe horizontal slides, renderer-compatible black cover masks, explicit page/frame/HUD sorting, and deterministic teardown. Discovery is attempted once per in-game/source transition and uses one revision-local loaded-Image index with unique exact root-to-leaf matches. Exact current Silksong UGUI mappings are frame top `_UIManager/UICanvas/OptionsMenuScreen/TopFleur` + `Warning_Fleur0008` (Sprite/source rect 959x106), frame bottom `_UIManager/UICanvas/KeepResPrompt/BottomFleur` + `bottom_fleur0008` (Sprite rect 355x134; source RectTransform 303x66), selected top `_UIManager/UICanvas/PauseMenuScreen/TopFleur` + `pause_top_fleur0000` (Sprite/source rect 426x123), and selected bottom `_UIManager/UICanvas/PauseMenuScreen/BottomFleur` + `bottom_fleur0000` (Sprite/source rect 355x134) | **REWRITE_PORT** as `DsPortFrame.cs`, `DsPortFrameState.cs`, `DsResidentUi.cs`, and `DsPortUtil.cs`; static frame chrome alone is cloned inactive, sanitized before first activation, and accepted only when exactly one loaded native `Image` matches its complete path, Sprite, Sprite rect, and source `RectTransform.sizeDelta`; otherwise log a capability gap and draw no replacement | Asset identities and paths are source-proven from the exact `1.0.29980` UIManager prefab. Static sanitation must not spread to Stages 3–7: native HUD/pages/overlays must retain the oracle's activate/open, settle, and selective-freeze/driver lifecycle and consequences. **BLOCKER:** live residency, clone rendering, glyph/fleur geometry, functional cover compositing, and side-by-side Dual Souls parity have no device evidence. Native Map/Inventory/Loadout page clones and settle/fit logic remain tracked to Stages 4–6 |
| `Bottom.Hud.cs` | Moves/re-layers the one live `Hud Canvas` hierarchy to the private HUD layer during active play, naturally clearing the top screen; routes it back for pause, inventory, or full dual-screen off; the separate companion-page toggle leaves it routed; reasserts for spawned children; defines HUD camera geometry and separate cloned/pool-based area/status/equipped chrome | Typed Silksong anchors are `HUDCamera.GameplayChild`, `hudCanvasSlideOut`, and `silkSpool`, plus separate typed currency/loadout anchors; semantic live sources are `health_display` tk2d children, `SilkSpool`/`SilkChunk`, Money/Shard `CurrencyCounter`, `BindOrbHudFrame`, and `ToolHudIcon`. Static bundles prove current art identities but not runtime transform paths. `DsHudStrip` remains a rejected synthetic summary | **REWRITE_PORT** as `DsPortHud.cs`; move the same live Silksong semantic objects/subtrees into Hollow Knight slots with their original drivers and instance IDs, recursively apply the private layer, reassert after native updates, and restore only adapter-mutated parent/sibling/moved-root-transform/layer properties on pause, inventory, or full dual-screen-off/display loss. Proactively restore still-valid objects before scene/routing-rig teardown as transport safety. Clone resident donors or create renderer pools only for the oracle's separate static/status chrome. Append Silksong-only status in the same layout grammar; delete the authored HUD/panel after green | A cloned/mirrored gameplay HUD, intact Silksong layout, duplicate stacked HUD, or Hollow Knight art/widgets overlaid on Silksong is rejected. **BLOCKER:** exact runtime paths per semantic anchor, safe same-instance slot routing, spawned-child adoption, driver-reparent resistance, routing-only restoration, and side-by-side evidence remain unproved. Dialogue/tutorial/prompt paths remain Stage 7 gaps |
| `Bottom.Inventory.cs` | Clones native `Inv`; runs then freezes its own layout/FSM state; reasserts equipment/items; reads native counters; preserves availability; fits the pane; fills native name/description details | `DsGameArt` locates `InventoryPaneList`, invokes native widget state/display paths, evaluates game visibility, and mirrors resident `SpriteRenderer` composition. `DsInventoryScreen` currently uses those results inside a generic grid | **RETAIN_INFRASTRUCTURE** for discovery and typed data; **REWRITE_PORT** composition as `DsPortInventory.cs`; delete generic grid production path | Need exact stable clone/open/settle/freeze sequence for Silksong inventory managers without committing actions; blocker for Stage 5 |
| `Bottom.Charms.cs` | Post-processes cloned Charms pane; reads native charm backboards/icons; lays out grid/detail; displays notch cost; keeps equipped row; uses native name/description/detail sprites | Current `DsLoadoutScreen` reads `CurrentCrestID`, `ToolCrest.Slots`, equipped tools, and `ToolItemManager`, but draws an independent page and intentionally omits browsing/equip mutations | **REWRITE_PORT** as `DsPortLoadout.cs`; reuse typed discovery; map Charms semantics to resident Crest/Tool objects; delete current authored page | Native Crest/Tool browsing hierarchy and legal equip actions are blockers for Stage 6 |
| `Bottom.Map.cs` | Clones `GameManager.gameMap`; disables unsafe clone FSMs; drives native map setup, quick map, compass, markers, zone/room watchers, framing, availability, and optional bench-pin action | `DsMapView` already binds live `gameMap`, discovers Silksong `CameraRenderToMesh` map cameras, invokes native quick-map/world-map/compass paths, and restores main-screen state. `DsMapScreen` wraps it in an authored page | **TEMPORARY_REFERENCE** for `DsMapView` discovery; **REWRITE_PORT** composition as `DsPortMap.cs`; delete wrapper/render-texture page after native hierarchy port | Must prove whether the live map hierarchy can be cloned/re-layered directly on display 1. Bench teleport is a separate tweak gap, not a reason to retain the authored page |
| `Bottom.Select.cs` | Bottom touch polling; tab/item hit-tests; map pinch/pan; native pane `Cursor` corners; native component identification; live `Item Control` action glyph and localized verb prompt | `DsInput` and `DsTouch` provide display-aware tap/drag/pinch. `DsGameArt.SelectionCursor` locates resident `InventoryCursor` art. Current `DsIconGrid` draws its own selection/detail footer. No native control-prompt mapping is proven | **RETAIN_INFRASTRUCTURE** for input/touch and cursor discovery; **REWRITE_PORT** as `DsPortSelect.cs`; delete generic grid selection | Exact native Silksong action-glyph/verb source and legality boundary are blockers for Stage 5 |
| `Bottom.Tweaks.cs` | Cloned native frame/text; gear; grouped settings/detail; master/reset; state slots; bench/map actions; selection/prompt interaction through the same companion language | Task100 shares the guarded process session, exposes exactly damage, unlimited Silk, one-hit kills/nail damage and equip-anywhere, and presents them through production `DsPortMods` using `ModsAnchor` plus native resident labels/fleurs; the old `DsModsScreen` remains confined to rejected `DsShell` | **RETAIN_INFRASTRUCTURE** for typed behavior and persistence; **REWRITE_PORT** UI is host-complete in `DsPortMods.cs`; delete dormant authored modal only at its recorded cleanup stage | Additional tweak APIs remain explicit feature gaps; Unity/device visual, touch, gameplay-effect and relaunch acceptance remain deferred |

## Current Silksong file disposition

| Current file | Disposition | Replacement or retained responsibility |
| --- | --- | --- |
| `DualScreenV2.cs` | `REWRITE_PORT` | Minimal bootstrap for `DsPortRuntime`; retain proven bring-up callbacks until transferred |
| `DsPresentation.cs` | `RETAIN_INFRASTRUCTURE` + modify | Direct display, isolated composition cameras/layers, visibility, teardown |
| `DsPortRuntime.cs` | `REWRITE_PORT` | Composition state plus Stage 2 frame ownership, tab gesture forwarding, scene invalidation, and ordered disposal |
| `DsPortHud.cs` | `REWRITE_PORT` | Task99 Batch A live Unity routing adapter: typed native eligibility/owners, semantic slots below the retained transport, pre-destruction route-back; Unity binding and visual acceptance remain unproved |
| `DsPortHudState.cs` | `REWRITE_PORT` | Actual Unity-independent routing-only snapshots, late reassertion, descendant layers and survivor/failure-safe restoration, executed through a node-access seam in shared host tests |
| `DsPortSelectState.cs` | `REWRITE_PORT` | Task99 Batch B selection-identity/action-revalidation core and reachable short-circuit gesture decision; current native adapter coverage is recorded in the continuation below |
| `DsPortProgress.cs` | `REWRITE_PORT` | Owned native Journal graph, read-only display, native cursor, delayed layout/fit and renderer containment; also owns Inventory/Loadout/Tasks adapters |
| `DsPortJournalState.cs` | `REWRITE_PORT` | Production-linked admission, owner/selection epochs, inactive initialization and owned lifecycle orchestration; no native Journal seen/selection path |
| `DsPortProgress.Inventory.cs` | `REWRITE_PORT` | Owned native Inventory, custom icons and admitted direct-parent extra details; normal-content graph/detail gaps remain explicit |
| `DsPortCollectableManager.cs` | `REWRITE_PORT` | Exact production typed GetItems/GetGridSections overrides bypass native seen-reporting and shared custom-display cache; linked verbatim into host tests |
| `DsPortProgress.Loadout.cs` | `REWRITE_PORT` | Native Crest/Tool browse and explicit guarded manual equip dispatch; ordinary graph/details and full action equivalence remain host blockers |
| `DsPortProgress.Tasks.cs` | `REWRITE_PORT` | Native Tasks list/detail/cursor with primary input suppressed; unsupported custom counter detail retains the native list |
| `DsPortMap.cs` | `REWRITE_PORT` | Native camera transaction, finite owned cold room/condition/layout and renderer-donor recipes, read-only marker/pin/compass/corpse projection and retained restoration queue; private ungenerated text/font support and Unity presentation remain open |
| `DsPortMods.cs` | `REWRITE_PORT` | Task100 process-session presentation: HK-style dedicated gear at `ModsAnchor`, native resident text/fleur modal, shared model actions, production gesture lease, and detach without session disposal; Unity/device visual acceptance remains open |
| `DsPortOverlays.cs` | `REWRITE_PORT` | Exact-owner same-instance DialogueBox via original-parent carrier with native pose preserved, mapped clip coordinates and guarded native advance; retained fade/scenery consumers. Other overlay families and Unity/GPU acceptance remain open |
| `DsPortLayers.cs` | `REWRITE_PORT` | Content/frame/pages/HUD and overlays/fade roots on proven layers 6 and 3 with explicit page-below-frame-below-HUD sibling order |
| `DsPortFrame.cs` | `REWRITE_PORT` | Native UGUI frame/fleur composition, glyph-bound tabs, dynamic mask regions, page-host cache, interruption-safe horizontal slide, explicit sorting, teardown; fail-closed if the exact resident object is absent |
| `DsPortFrameState.cs` | `REWRITE_PORT` | Pure production alpha, hit-boundary, repeated-selection direction, and interrupted-slide host-visibility decisions; executable without Unity in the host contract |
| `DsPortUtil.cs` | `REWRITE_PORT` | Inactive staging, exactly one concrete retained static visual type, immediate removal and exhaustive post-verification of every other owned-clone `MonoBehaviour`, retained visual/renderer activation while hierarchy-inactive, recursive re-layering, and cleanup for Stage 2 static frame chrome only; no authored art and no blanket policy for later resident clones |
| `DsResidentUi.cs` | `REWRITE_PORT` | Typed `InventoryPaneList`/`InventoryPane` discovery, private native Pane Name access, revision-local exact loaded-Image index, unique identity validation, provenance, cached failures, and capability-gap reporting |
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
| H0 Hollow Knight-first rebaseline | `COMPLETE` | None | Silksong Stage 3 and later work are assigned to S1–S2 after H5/H6 | Both specifications, both parent plans, this matrix, traceability, README, and the new ordering contract agree on the reference-first dependency. Existing Stage 1/2 evidence and Stage 3 blockers remain recorded |
| H1 Hollow Knight direct-display transport | `IMPLEMENTED / DEVICE-PARTIAL` | None | Physical display loss and true single-display startup must be device-proved by H5; the Thor composer retained logical display 4 after DSI-2 was forced disconnected | Shared seam and opt-in Hollow Knight consumer committed at `9fb2db2`; 49/49 .NET and 78/78 Python tests; exact Hollow Knight and Silksong compiles; signed run `33494317664`; in-place Thor update; physical display-1 diagnostic and same-PID pause/resume captures; cleanup/settings preservation verified. The diagnostic card is not H2 evidence |
| Stage 0 contract enforcement | `COMPLETE` | None | All production implementation requirements remain assigned to Stages 1–9 | Current contract run: 18/18 tests passed; DSUI-01–10, distinct first-column rows for all nine reference modules, exactly one valid disposition row for each of the 26 current dualscreen C# filenames, explicit prototype/port status, README/traceability acceptance language, repository-wide production `DsShell` construction exclusion, exact two-camera/layer isolation, serialized single-rig reactivation, combined pause/presence/readiness activation, full-stretch empty roots, and the concrete Stage 1 composition-state boundary are enforced |
| Stage 1 transport/composition separation | `HOST-VERIFIED-BOUNDARY` | None | Frame/tabs to Stage 2; resident HUD to Stage 3; Map/Inventory/Loadout/progress pages to Stages 4–6; overlays/fade to Stage 7 | Second review RED: 18 tests ran with 13 green and 5 failures covering four defects: stale reattach readiness, presentation retention/serialization, combined active-state derivation, and root stretch. GREEN: 18/18 tests; Silksong 42 sources/10 entry points; Hollow Knight 4 sources, 0 warnings/errors, 16,896-byte DLL/1 entry point. Source contracts verify reactivation through `Display.Activate`, settle, presence recheck, remeasure, and force-sweep on one retained rig without a cancellation timeout. Device and UI parity remain unclaimed |
| Stage 2 frame/tab composition | `HOST-VERIFIED-SOURCE / DEVICE-BLOCKED` | Exact UGUI path/Sprite identities are source-proven, but live residency, clone rendering, glyph/fleur geometry, renderer-cover clipping, and side-by-side parity remain unproved | Native page contents/clone settle/fit remain assigned to Stages 4–6; HUD/status data to Stage 3; Mods control content to Stage 8 | Fix-pass RED: 27 tests with nine failures covering obsolete art discovery, absent glyph/aspect geometry, RectMask-only clipping, missing explicit sorting, and interrupted-slide leakage. Re-review RED: 28 tests with six assertions failing across two source defects: conflated Sprite/source-rect dimensions and absent 1.0/0.6 tab alpha. Quality-review RED: 32 tests with five failures covering unsafe static-clone activation, suffix/unloaded/duplicate discovery, repeated scans, and missing executable decision proof. Final spec-review RED: 32 tests with one intended failure because the old five-family sanitizer left arbitrary cloned `MonoBehaviour` drivers eligible to run. Quality re-review RED: 33 tests with two intended failures because disabled drivers could still receive `Awake` and the executable harness was Windows-path-bound. GREEN: 33/33 focused and 59/59 full Python tests; the focused suite compiles and executes the exact pure production state across boundary and repeated interrupted-selection cases, selects portable automatic-cleanup temp storage, and inspects exact-type removal before activation. Exact Silksong `1.0.29980` compile: 46 sources/10 entry points. Exact Hollow Knight `1.5.12620` compile: 4 sources, 0 warnings/errors, 16,896-byte DLL/1 entry point. Proven source APIs include revision-cached unique loaded `Image` identity, exactly-one retained visual plus exhaustive removal verification beneath inactive staging, independent Sprite/source-rect validation, RGB-preserving alpha, hit boundaries, direction, and host visibility; missing live objects fail closed. No device/UI parity is claimed |
| Stage 3 persistent HUD | `DEFERRED / AUDIT-COMPLETE` | Exact runtime paths, same-instance slot routing, spawned-child adoption, driver-reparent resistance, exact restoration, and side-by-side device proof remain unproved | Entire implementation is assigned to successor Stage S1 after H5/H6 | Source comparison proves `RelayerHud` moves the one live Hollow Knight HUD root to `hudLayer`, returns it for pause/inventory/full dual-screen off, and reasserts for spawned children. Silksong source/static-asset audit identifies the typed anchors, semantic objects, driver owners, and art identities. The mirror/suppress proposal failed the specification cross-check and was removed before implementation. Probe RED contracts and an unverified implementation remain preserved in the working tree; they cannot advance before Hollow Knight H5/H6 |
| Direct-display transport | `PROVEN_BASELINE` | None for the Stage 1 boundary | Port population and visual proof remain in Stages 2–9 | Signed Silksong display-1 gameplay proof plus the exact-compiling two-role `DsPresentation` source |
| Authored shell | `REJECTED_PROTOTYPE` | Old source files remain until their scheduled deletion stages; `DsShell` stays dormant until Stage 2 visual proof closes | None | `DualScreenV2` contains no `DsShell` field, construction, registration, or rebuild path |
| Hollow Knight direct-display reference | `SOURCE/HOST-COMPLETE / PHYSICAL-BATCHED` | No H2 source/host implementation blocker remains at `00627e3`. At signed run `33544518172`, the physical clip-safe label/teardown observation remained open: separators, bounded fleurs, and the battery icon rendered, but detached TMP labels did not. The later exact-type/clip-block correction is host-proved only | The unpassed H2 label/teardown and controlled-injection observations, plus H1 physical detach/true single-display, are batched into the complete Hollow Knight candidate. None is converted to a pass | Cumulative historical signed evidence proves persistent credit, live masks/Soul, primary cleanup, Inventory, lower touch, backdrop, pages, selection/prompt, pause, and teardown. The final correction passes 32 focused contracts, 122 Android tests, 58 shared tests, 38 bundle-surgery tests, 112 Python tests, and a 226,816-byte exact `1.5.12620` patch. No separate physical rerun or signed H2 micro-candidate follows; later validation remains explicitly authorized, fixture/injection-only, and never uses a save, room, or gameplay |
| Shared contract extraction | `SOURCE/HOST-COMPLETE FOR MODS` | Task100's concrete Silksong consumer now shares guarded `TweakSession`; broader companion/skin abstraction remains separately scoped | None for Task100 | The extraction is intentionally bounded to controller/store/adapter lifetime, retryable restoration and presentation separation |
| Silksong Dual Souls composition port | `DEFERRED` | Hollow Knight H5/H6 plus Stage 2 live visual proof and successor Stages S1–S2 | All remaining Silksong composition work is assigned to S1–S2 after H5/H6 | Stage 2 source contracts cover exact native art/text identity, geometry, masks, sorting, cache, slide interruption, and teardown. Physical rendering/parity is still unclaimed |
| Dual Souls composition port | `IN-PROGRESS` | Hollow Knight H1–H6, then Silksong S1–S2 | Silksong-specific implementation is deferred behind the reference gate | The global port remains active under the Hollow Knight-first plan; this status does not advance the deferred Silksong stages |
| Side-by-side device acceptance | `NOT_STARTED` | Depends on the completed port | None | Stage 9 matrix not yet run |

Required work may move between named stages only by updating this matrix and
the plan together. It may not be silently removed or called complete because
the lower display renders.

## Historical 2026-09-08 execution override — superseded 2026-09-10

Historical PARKED/DEFERRED and H5/H6 dependency entries above describe the
former execution order. The 2026-09-08 override resumed Silksong host work under
Tasks99–102, as specified by the matching historical execution addendum in
`docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md`. The
2026-09-10 corrective authority now supersedes this sequence.

| Current work | Owner | Host acceptance condition |
| --- | --- | --- |
| Stage3/HUD | Task99 Batch A | Same-instance semantic routing into HK-style slots; native drivers preserved; spawned children/reparenting/replacement handled; routing-only restoration before teardown; executed production routing tests and exact compiles |
| Stages4–7/native pages, selection, overlays | Task99 Batch B | Native open/settle/selective-freeze and map composition; source-backed legal actions/native prompts; overlay/fade ownership; behavioral host tests, not an empty frame or generic grid |
| Stage8/Mods | Task100 — `HOST-COMPLETE / DEVICE-DEFERRED` at `41bb3cd` | Process-owned shared controller/store/session and reachable native-resident gear/modal; default-off, reset, restoration/apply retry, owner replacement and profile isolation pass host tests; exactly four source-proven capabilities, with additional capabilities unclaimed |
| Shared Silksong skins/death rotation | Task101 — `HOST-COMPLETE / DEVICE-DEFERRED` at `0b92f4d` | Closed 11-target Silksong catalog/profile; exact tk2d collection/material admission; character-only ROTATE; persistent HUD ON; exact pinned `HeroController.<Die>d__1101.MoveNext` bridge; restore-before-frozen-successor and two-frame stable-respawn tests; cross-game isolation. The host checkpoint compiles 79 Silksong sources against exact 1.0.29980 and preserves the exact Hollow Knight compile. The death rewrite accepts only Assembly-CSharp SHA-256 `1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d`, rewrites once, is byte-idempotent, and rejects managed drift. Android bundle identity/parity, sampled material appearance, Scythe/Shaman shader behavior, crest/fallback visuals, HUD effects, resource pressure, death/respawn visuals/lifecycle, and persistence interaction remain device-only. Evidence: `docs/verification/evidence/task101-host-0b92f4d/` |
| Combined host/isolation acceptance | Task102 | Reachable production integration, independent SPEC then QUALITY and fresh main verification; unsupported and device-unverified results remain separate |

The required production design and source-object authority are unchanged:
Silksong's engine is retained, not its suboptimal default HUD composition.
Historical device, renderer, hierarchy, action-binding and visual-parity gaps
remain unpassed until their actual evidence is recorded. A host test of node
access or state mechanics does not prove Unity driver behavior or visual fit.

Hollow Knight's simplified skin host gate is accepted by Task96:
`tools/shared-patches-tests/obj/task96-main-20260908T125700Z/final-main-host-verification.json`,
SHA-256 `cd72d5757ba7cca8a6833e8dc0e11640b60aa33804f56e6a499944335818ff5d`.
Its 121 C#/84 Kotlin cases cover the recorded 566-source/27-input freeze, not
later source changes. H3 remains a bounded Mods capability slice, not full
gameplay Mods parity. Shared extraction occurs with the concrete second consumer
and targeted regression, not as a blocking framework milestone.

The old boundary excluded a live-device gate before those host batches and also
excluded packaging/source-image activation, devices/ADB/emulators,
signing/release/publication, commits/pushes, cleanup and upstream-visible
actions. That exclusion does not apply to the V3–V4 integrated Android gate, its
dry-run signing candidate, or the exact-commit push to the user's fork. A new
thread/session-specific device lease is still required before ADB or physical-
device work; no public release or upstream interaction is authorized.
Historical DELETE_AFTER rows are future dispositions, not current deletion
authorization. Old cores, goldens and evidence remain preserved.

### Task99 Batch B partial implementation checkpoint

This is **PARTIAL / NOT BATCH-B ACCEPTANCE**. Task99 and parent97 remain open;
independent SPEC, separate QUALITY and fresh main verification have not occurred
for this delta. No native page is populated by this checkpoint.

Reachable work is `DsPortRuntime` → `DsPortOverlays` native fade synchronization
and scenery consumption, plus overlay → production `DsPortMods` → cached tab
consumption. `SetModsGestureConsumer` now carries the Task100 presentation lease;
detach clears it without disposing the process session. The selected-page consumer
remains absent. `DsPortSelectState` retains executable identity, read-only-display/
action separation, action-time legality and reentrant selection-revision checks,
but no native selection/equip adapter.

Native fade authority is the exact `ScreenFaderState.instance` and serialized
`spriteRenderer`; the companion reproduces its current sprite/shared material/tint
and visibility without changing the primary owner or native material. Missing
owner, sprite, material or visible opacity clears the companion fade. Material
rendering on the companion Canvas remains Unity-unproved. Native scenery
authority is the current `GameCameras` object's
active `LightBlurredBackground.backgroundCamera`/`renderTexture` pair.
`LightBlur.OnRenderImage` writes the final native blur-material pass to the
camera's destination texture. `BlurManager.Update` enables that producer by
native shader quality, not by an inventory/menu-only condition. The consumer
requires a current producer, matching texture, supported active blur/material,
positive pass count, and the background slice behind the native blur plane and
hero plane with UI/private layers excluded. It waits a later frame after
owner/texture replacement, downsamples to one-sixth panel size with a 256-pixel
maximum edge, uses bilinear filtering and 0.08 brightness, and restores
`RenderTexture.active` in `finally`. It never activates a native producer or
captures the gameplay camera. Only companion-created resources are released.
Native availability, blur pixels, fill geometry and visual parity remain unproved.

| Open ID | Owner/target | Required closure / exact remaining seam |
| --- | --- | --- |
| T99B-PAGE | Task99 Batch B / `DsPortProgress.cs`, then `DsPortInventory.cs` | Implement the first reachable native Journal page, then Inventory/Tasks. `JournalItemManager.UpdateList` → native template `Setup` is read-only; `GetStartSelectable` clears `EnemyJournalManager.UpdatedRecord`, and `Select` marks records seen. Admit actual resident components, cursor/template dependencies and serialized local targets before any activation; reject unknown callback ownership without blanket sanitation. Native `ScrollView` uses unscaled world-space bounds, so simply scaling the parent breaks scroll/fit. Preserve open/settle/visual drivers and adapt/freeze only the proven scroll/input paths. Next RED: external template/callback target rejects before activation, inactive/interrupted hosts cannot settle another page, and display/clear never runs native seen/new setters. |
| T99B-ACTION | Task99 Batch B / `DsPortSelect.cs`, `DsPortLoadout.cs` | Populate native Crests/Tools and native details/cursor/glyph/localized verbs. Revalidate current owner/item/data/crest/slot and bench/cursed/unlocked/type conditions on explicit dispatch. `InventoryToolCrestSlot.SetEquipped(isManual:true)` reaches native `SaveEquips` but does not itself establish legality. No native equip action exists in this checkpoint. |
| T99B-MAP | Task99 Batch B / `DsPortMap.cs` | Native availability/compass/markers/room/zone/pan/pinch, primary-map yield and complete `finally` restoration with failed-setup tests. `GameMap.OnAwake` registers shared pins and `OnDestroy` clears that registry; an ordinary clone/teardown cannot be treated as save-neutral. No map implementation or invented teleport is supplied. |
| T99B-OVERLAY | Task99 Batch B / `DsPortOverlays.cs` | Same-instance dialogue/speaker/tutorial/focus/title/credits/item/lore routing, visibility and pre-destructive restoration. `DialogueBox._instance` is the exact dialogue owner; `NpcDialogueTitle` is an NPC event/data owner and routes the visual through `AreaTitle`, not its own NPC transform. Native fade coverage does not close these families or every death/cutscene-specific overlay. |
| T99B-VISUAL | Task99 Batch B host integration, then explicitly authorized runtime gate | Finish page fit/sorting/clipping below the HUD band and actual selected-page gesture reachability. The native-background availability/blur, fade rendering and all Unity/native action/driver behavior remain unproved by host tests. The backdrop has no gameplay-camera fallback when its native source is unavailable. |

Task-owned evidence is
`tools/shared-patches-tests/obj/task99-batchb-20260908T190000Z/verification.json`;
its baseline SHA-256 is
`325c71f0e1de8346d3eb5bdd747decfb6b488fec39823710497c28cbc6b905fb`.
Focused RED/GREEN includes initial selection 13 failures → 13 passes, native
fade 10 failures → 10 passes, reentrant selection 1 failure → fixed, and scenery
9 failures/4 passes → 13 passes. The final exact regression filter passes
224/224 (187 retained + 14 selection + 23 overlay/scenery). These prove actual
production-linked decisions, not populated Unity pages. Both cached two-game
compiles succeed; Silksong includes all 57 sources/29 references (7 existing
warnings), and Hollow Knight has 1 existing warning. The material-corrected
regression preserves all 224 exact case identities. Source/docs contracts pass
50 with 1 explicitly excluded legacy restore/delete harness. The initial blanket
Image-ban failure and subsequent material-binding RED are retained. Its narrow
replacement admits only one source-backed native fade Image allocation, and
six mutated-source plus frame/HUD/page negative controls still reject authored
Images; the rule is not disabled for the whole overlay file. Exact ten registered
entrypoint signatures/phases are checked from the compiled IL, without executing
native code. Failed native
lookups, the interrupted six-child decompiler group and unsuccessful prefab
metadata attempts are retained; unbounded/incomplete launches earn no credit.

### Task99 Batch B native-page/Map continuation — provisional host checkpoint

The preceding 224-case checkpoint is historical and unchanged. This continuation
populates native-page adapters but **does not close Task99 or parent97**. No
independent SPEC, separate QUALITY or fresh main acceptance has occurred. The
accepted HUD state/tests and shared direct-display sources are not changed.

| Current surface | Implemented source boundary | Remaining host blocker / acceptance target |
| --- | --- | --- |
| Journal | Owned born-inactive native pane/template/cursor graph, exact serialized admission, local display callbacks, read-only SetDisplay, delayed native layout, world-space cursor and whole-row renderer containment | T99B-PAGE: native lifecycle/layout/driver execution is not established by host mechanics; owner-stable content refresh and normal resident graph coverage still require evidence. Never SetSelected, Select, PaneStart/End, InstantScroll, GetStartSelectable or seen setters for browse/clear |
| Inventory | Actual typed GetItems/GetGridSections bypass reported-collection writes and shared custom-display cache; owned icons are validated before and after clone; admitted extras are direct children of exact native descSectionParent. DsPortDetailAttempt confines optional validation/fade/clone exceptions to owned detail/selection, retaining the list and next-selection retry | T99B-INVENTORY: required normal fixed/custom-icon graphs and material ownership remain unproved; unknown custom icons can still reject the pane. T99B-DETAIL: SetupExtraDescription overrides remain rejected by DeclaringType != SavedItem. Failure locality is not override support. Owner loss or failed cleanup still propagates to outer teardown. Missing serialized component evidence does not prove every ordinary pane fails |
| Tasks | Native list/templates/headings/cursor/details, disabled primary input, clone-local completed-list toggle; known unsupported custom counter clears only selection and retains native list for immediate supported retry | T99B-TASKS: custom counter detail and owner-stable native content refresh remain host work. Read-only browse must not set seen/new or progression |
| Loadout | Optional unselected ToolItem.ExtraDescriptionSection and ToolCrest.DisplayPrefab no longer reject the whole pane. All source crests get native owned slot setup through fresh blank metadata and an independently cloned value-slot array, then exact source rebinding and every slot/delegate/callback check before activation. Selected required visuals are owned and admitted separately; unsupported visuals hide only that crest and block its actions, retaining next-crest browsing. Floating actions validate exact source/owned LAST-fulfilled config, descriptor ID/type/slot correspondence, null-Crest/-1 ABI and native SaveEquips callback; all callback peers refresh from live saved values before native manual dispatch | T99B-ACTION: native Unity action/config/lifecycle execution, required locked-slot semantics and full legal prompt/action equivalence remain unproved. T99B-DETAIL: selected tool overrides and exact selected-slot custom-detail condition/destination binding remain absent. T99B-CREST-GRAPH: required selected custom component/reference/lifecycle graphs still face unchanged auxiliary admission; no placeholder art substitutes for a rejected visual |
| Map | Finite owned cold recipes read serialized mapZoneInfo parents/rooms, native art originals, conditions and layout; renderer-only donors avoid GameMap activation/cache/registry writes. Exact mapped/visited set membership replaces count-only freshness. Marker/template local hierarchy, room-based compass/corpse positions and viewport arrow are retained; shared/private retained TMP meshes can supply existing text geometry. Exact native camera transaction, read-only marker copy and companion-only gestures remain | T99B-MAP-TEXT: truly ungenerated text still reports NEEDS_CONTEXT; finite private text/font/material closure is absent. T99B-MAP-PRESENT: camera callbacks/pre-existing buffers, dynamic geometry/material/condition freshness and normal serialized graph require admission/evidence. Explicit inactive-renderer DrawRenderer, sorting, clipping and visual parity are Unity-unexecuted; no unconditional cold-Map support claim |
| Dialogue/fade/scenery | Exact DialogueBox._instance and same native root/drivers route through an empty carrier under the original parent; root local pose and fade ancestry remain native-owned. Native clip relativeTo/min/max are mapped and individually restored, then exact native LateUpdate refreshes clips. Guarded native AdvanceConversation follows native printing/waiting conditions. Pending restoration remains a gesture barrier. Prior native fade and post-LightBlur dimmed scenery consumers remain | T99B-OVERLAY: speaker/AreaTitle, tutorial, focus, title, credits, item/lore and other required families remain absent. Dialogue native fade/clip/tap/lifecycle and all scenery/GPU evidence are still unexecuted; no gameplay-camera fallback |

Map's `DsPortMapRestoreQueue` registers each rollback before its presentation
write, retries only failed fields, and retires resources only after every live
write is restored. Capture abandoned before setup (including partial capture
failure) releases only owned buffers; it never restores untouched primary fields.
Command-local view/projection framing avoids overriding native aspect, camera
size, projection authority or transform. Corpse root/arrow position and rotation
are retained exact-instance presentation values; no MapMarkerArrow initialPos,
activity, primary viewport collider or global view/layout event is changed.

Outer retention is concrete: `DsPortRuntime.RestoreHud` attempts HUD restoration,
then Map.Invalidate in `finally`, then native overlay restoration in nested
`finally` even if Map restoration throws. Scene changes and pre-transition call
this boundary; hiding restores overlays before hiding layers. Existing V2 inactive LateUpdate/manager
callbacks and the `DsHudReleaseState` restore callback reach it. PortContent
retains `_port` when disposal throws; the independent `DsHudReleasePump` retries
at .25 seconds after normal page Tick stops, and blocks replacement until restore,
content disposal and enclosing release succeed. Destroyed retained Unity targets
are skipped; a live failed write remains owned. No replacement native object is
resolved for restoration. This is source/host evidence, not Unity teardown proof.

**T99B-MAP-COLD / NEEDS_CONTEXT — owner Task99 Map initializer.** Native evidence
is under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
`native-GameMap/output.log`, `native-GameMapScene/output.log`,
`native-MapPin/output.log`, `native-CameraRenderToMesh/output.log`, and
`native-MapMarkerArrow/output.log`. `GameMap.OnAwake` activates compass donors,
instantiates marker rows and registers shared pins. `OnStart` calls OnAwake,
area deactivation, InitZoneMaps/cache construction, LevelReady (which can refresh
tilemap state) and bounds work. SetupMap changes native scene art/activation,
conditional positions, pin/layout/bounds caches; MapPin Awake changes registration
and the global new-pin bit. GameMap destruction clears the global registry.
WorldMap/TryOpenQuickMap reach SetupMapMarkers; that method and
CalculateMapScrollBounds resize live PlayerData.placedMarkers and insert missing
WrappedVector2List values. UpdateGameMap writes mapped progression. None is an
admitted browsing initializer, including temporary mutation plus rollback.

**Historical pre-recipe cold packet (not the current implementation state).**
The following missing-state/advisor-wait description is preserved as the earlier
checkpoint. The current table and consolidation below supersede that wait:
finite cold recipes and renderer donors now exist; private ungenerated text does not.

Minimum missing owned cold state: source-backed initial/mapped/visited and
conditional room art/color/enabled values; zone unlock/hidden/lost and position
conditions; initialized native room/text geometry; privately derived pin layout
and bounds; initialized marker/arrow donors; and content freshness authority
stronger than saved-set count. Existing GameManager.gameMap/mapZoneInfo and its
serialized parent/scene graph are the residency/asset authority, not an invented
map. Native GetMapPosition depends on cached BoundsSprite/initialSprite and
GetCorpsePosition on corpseSceneMapZone; their initialized-source use must not
be extended to cold state by removing the gate. Native TMP cannot be cold-started
by ForceMeshUpdate(true) without considering Awake/font/material cache mutation.
Closure requires an owned initializer with no native activation/registry/save
side effects plus executable normal cold/refresh cases. Research is paused for
the coordinator's focused advisor. `.5s` retry only observes readiness following
a real native primary-map open/setup or refresh; polling is not cold-map support.

Earlier focused evidence (same continuation root, preserved per-command outputs):
`map-restore-queue-red` 0/4 → `map-restore-queue-green` 30/30 combined;
`map-outer-retention-red` → `map-outer-retention-source-green`, with linked
queue + existing release-state behavior in `map-outer-retention-green`;
`map-projection-policy-red` 0/12 → `map-projection-policy-green` 43/43 combined;
`map-corpse-viewport-red` 0/5 → `map-corpse-viewport-green` 48/48 combined.
`map-corpse-arrow-compile` is the successful full current SS compile before the
small subsequent Inventory clone-admission correction. The projection namespace
and ambiguous-Camera compile failures remain preserved. `inventory-clone-admission-red`
→ `inventory-clone-admission-green` covers three source contracts only, not Unity
clone lifecycle. Prior marker-copy and Inventory direct-parent RED/GREEN remain
retained. Full regression/new manifests, fresh HK compile and compiled entrypoint
verification must be recaptured before review; these focused results do not
replace the historical freeze or establish normal-content/visual acceptance.

### Task99 Batch B bounded partial consolidation

**PARTIAL / NOT BATCH-B ACCEPTANCE. Task99 and parent97 remain OPEN.** The
current surface table above supersedes earlier missing-page/cold-advisor-wait
statements; all historical evidence remains retained. The four concrete defects
(optional tool details rejecting the whole Loadout pane, all-assets crest prefab
rejection, absent floating equip route, and Inventory detail exceptions tearing
down its list) are **implementer-corrected only**, not independently accepted.
The crest factory never Instantiates a source ToolCrest: its native OnEnable /
OnValidate can mutate previousVersion.upgradedVersion. Only fresh blank owned
metadata receives finite Setup-read fields; every final action resolves exact
live source assets, not metadata. No adapter save setter was introduced.

Fresh evidence is beneath
`tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:

- `partial-combined-regression`: **179/179**, actual linked production code;
  exact TRX identities cover Selection/Map 68, Overlay 27, Journal 42, accepted
  HUD/frame/release 37, and typed Collectable hooks 5. This focused combination
  retains the relevant prior cases; it is not a rerun of unrelated historical
  suites. The 12 new correction cases exercise detail-local exception/retry,
  all-slot freshness and crest setup/rebind/release ordering through simulated
  native delegates, not Unity factory/action execution.
- `partial-source-contracts-first`: 65 passed, one stale Tick `_hud.Restore`
  expectation errored. The error/log is preserved; the corrected contract now
  follows RestoreHud and requires nested HUD/Map/overlay finally restoration.
  `partial-source-contracts-corrected`: **66/66**. Exactly
  `test_pure_frame_state_executes_repeated_selection_boundaries_and_interruptions`
  is excluded, not passed: it restores implicitly, waits unbounded and deletes
  temporary output. The linked frame cases execute in the combined run instead.
- `partial-ss-compile` and `partial-hk-compile`: cached `--no-restore` builds,
  real CoreCompile/csc invocations, not incremental-only successes. DLL SHA-256:
  SS `d9e94e0ffbd4d83b67b3f43d27b8874a0758d87e0ff41c09489c1c064433554a`;
  HK `d84dee2585ed648b69479c683940b4a3b00eeeb1501d663302f109cc9d0b3a1e`.
  Compiled IL inspection is retained separately in `partial-compiled-il`.
- `partial-consolidated/verification.json` binds the exact source delta against
  the pinned 573-source baseline, exact prior/new TRX comparisons, actual compiler
  inputs/artifacts and ten registered compiled entrypoint signatures/phases.
  Baseline SHA-256 remains
  `d02b80ad57397b18a9df60697618ac07cc62d6a837236c037b79792395cbc1d7`.
  It reuses six pinned historical preservation receipts, not another 35,023-file
  scan. Per-command launch/completion/logs retain PID, UTC start/end, elapsed,
  exit and timeout; no failures are deleted or late success credited.

Required continuation remains with **Task99 Batch B**, not Tasks100/101:

| Open ID | Named target | Closure condition |
| --- | --- | --- |
| T99B-OVERLAY | DsPortOverlays native family adapters | Exact visual AreaTitle speaker authority, tutorial, focus, title, credits, item/lore and other required families; source-proven driver/coordinate ownership, restoration under partial failure and linked mechanics |
| T99B-MAP-TEXT | DsPortMap private native text donor | Finite born-inactive native text with independently owned mutable font/fallback/kerning/material graph; preserve exact characters/styles, no lazy global initialization or borrowed mutations; cold generation and owned release tests |
| T99B-DETAIL | Inventory/Loadout selected native details | Implement exact required SetupExtraDescription overrides and native slot-specific condition/destination/template binding; next selection retries without whole-list rejection |
| T99B-CREST-GRAPH | Loadout selected native DisplayPrefab | Admit actual required custom components/references/lifecycle and material ownership without weakening read-only/source authority or substituting placeholder art |
| T99B-PAGE / T99B-TASKS | Native page adapters | Owner-stable content refresh, required normal serialized graph coverage, Tasks custom counters; browse/clear preserves seen/new/progression/equipment/save state |
| T99B-ACTION / T99B-MAP-PRESENT / T99B-VISUAL | Native integration | Required locked-slot semantics and full prompts/action equivalence; Map callbacks/dynamic freshness/sorting; actual Unity lifecycle/rendering/native actions and parity remain unproved |

No new independent review stage is opened for this partial packet. Continue the
remaining native families, private cold text and required custom support first;
eventual acceptance still requires independent SPEC, separate QUALITY and fresh
main verification. Host tests/compiles are not Unity or visual acceptance. All
offline/bounded/no-device/no-publication/no-source-activation/no-git-mutation/
no-save-edits/no-cleanup restrictions remain in force.

### Task99 opening-credit host continuation (after frozen partial)

**PARTIAL / NOT BATCH-B ACCEPTANCE.** The partial packet above remains frozen;
current sources intentionally advance beyond it. `DsPortOverlays.NativeOpeningCredits`
now routes only an existing started exact `OpeningGameplayCredits` owner: private
native `pd` is bound, its played flag is read only, and the exact live animator,
HUD camera and original parent remain authoritative. No Start/clone/replay,
animator/active/fade/progression write is introduced. The empty carrier remains
under native ancestry and maps the original world aspect, including anisotropic
parent scales, without snapshotting or restoring the native root's current pose.
Parent/sibling/layer recovery is registered before mutation; failed recovery keeps
the exact owner and carrier. Every family restore is attempted independently,
including outer teardown. Successful attribution is not a gesture modal; a failed
still-owned route remains a barrier.

Finite admission rejects root motion, StateMachineBehaviours, unknown components,
world-coordinate drivers/clips and unsupported parent/camera bases. Actual normal
serialized credits controller/component/clip graphs are unavailable: the host
mechanics neither establish ordinary-instance coverage nor prove universal failure.
AreaTitle remains the speaker visual authority, not `NpcDialogueTitle.transform`.
Tutorial's coroutine owns dismissal/progression and only polls native skip input;
no callable staged dismissal has yet been established and no broad confirm input
or invented dismissal is added. T99B-OVERLAY remains OPEN for those and the other
required families. All other open ledger IDs above remain required.

Fresh bounded cached evidence under the same continuation root:
- `overlay-family-restore-red` 0/3 → `overlay-family-restore-green` 30/30;
  actual production combiner attempts every owner and retains only failed leases.
- `opening-credits-plane-red` 0/7 → `opening-credits-overlay-green` 37/37;
  production XY mapping checks native aspect, translated/anisotropic parents and
  invalid camera/scale values. These are managed mechanics, not Unity rendering.
- `opening-credits-wiring-red` and `opening-credits-partial-red` remain preserved.
  `opening-credits-contracts-first` ran 67 with one stale Dialogue restoration
  literal failure; its narrow correction requires the independent family combiner.
  `opening-credits-contracts-green` passes 68/68, excluding the same one forbidden
  implicit-restore/unbounded/auto-delete harness with no pass credit.
- `opening-credits-compile-green` executes actual CoreCompile against cached native
  references, exit 0; `PatchCheck.dll` SHA-256
  `d1238d06cf3e04e3a066fca3ad8fbdebfb43bd058c3b2e6d7e562f88a4ff8b1b`.
  Command PID/start/end/elapsed/exit/timeout records remain beside every run.

No Unity lifecycle, native animation/action execution or visual parity is claimed;
Task99/parent97 stay OPEN pending independent SPEC, separate QUALITY and fresh main.

### Task99 Tasks detail, retirement and owner-stable refresh continuation

**PARTIAL HOST IMPLEMENTATION / NOT BATCH-B ACCEPTANCE.** Historical rows and the
frozen partial packet above remain unchanged. This advances their implementation
state without extending their source or runtime acceptance credit.

- Tasks now retains native `QuestItemDescription`/manager display and uses the
  exact regular/main/subquest template condition and mapped `descSectionParent`
  for an independently owned selected custom counter prefab. No shared
  `InventoryItemExtraDescription` prefab cache or native quest-asset cloning is
  used. Owned counter text/submeshes retire before owned materials. Optional
  unsupported detail still fails locally and can retry on next selection;
  required ordinary custom component graphs remain unproved and OPEN.
- Page retirement retains the exact noninteractive owner until cleanup returns.
  All four actual adapters separate immediate `Released` from terminal
  `Destroyed`; successful driver stops and individual Loadout metadata releases
  are not replayed. Failed resources remain available to the existing inactive
  runtime/full-off retry path. These are host dispatch/ownership guarantees,
  not execution of Unity's deferred destruction lifecycle.
- All four actual `Capture` paths now bind immutable, explicitly enumerated page
  snapshots into `DsJournalToken.Same`. Journal reads record identity, native
  kill/seen values, visibility and detail presentation; Tasks reads exact live
  quest manager/master identity, native version, accepted/main/current-subquest
  membership, completion, targets/counters and rewards; Inventory reads item
  membership, amounts, visibility and native presentation; Loadout reads tools,
  crests, native/saved slot values, extra-slot configuration and legal conditions.
  Native source display-test results are read without invoking their callbacks.
  No general PlayerData serialization, hash-only or count-only freshness is used.
- Stable/empty pages poll at 125 ms; construction/retained retirement still gets
  subsequent-frame retries. Every snapshot is limited to 65,536 emitted input
  values; exceeding the bound or a getter failure rejects the whole read, never
  truncates rows or publishes the previous snapshot as current. The page clears
  noninteractively, keeps failed cleanup ownership, and retries on a later poll
  without latching the old content as a permanent failure. Gesture/selection/
  action checks bypass the poll cadence and recapture current authority.
- Content changes use the actual retained clear → born-inactive clone → bind →
  hidden unit-world layout → later settle/present path. Old selection and owned
  coroutine work are cancelled. Reentrant refresh cancels the generation and
  defers retirement until the active native callback unwinds; no replacement is
  constructed within that callback. A snapshot read exception before layout is
  now inside the same fail-closed lifecycle boundary.

Retained evidence under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:

- `tasks-counter-combined-green`: 203/203 linked cases; its Tasks detail/retirement
  REDs and source failures remain preserved. `tasks-page-adapter-retirement-red`
  and `tasks-page-adapter-effects-red` catch actual all-four adapter wiring; the
  correction passes 60 focused linked cases, 72 allowed contracts and actual SS
  CoreCompile in `tasks-page-adapter-*-green`.
- `page-snapshot-lifecycle-red`: 11/11 intended failures (missing snapshots and
  replacement construction inside native callbacks). `page-snapshot-read-failure-red`
  preserves two further failures: wrong bound outcome and stale Ready surviving
  an initial current-read exception; its two stale-equip controls already passed.
  Both actual adapter wiring REDs remain retained.
- `page-snapshot-final-linked-green`: **218/218**, including 15 new cases; the
  bounded `page-snapshot-identity-check` confirms every prior 203 identity remains
  passing. `page-snapshot-final-contracts-green`: **74/74**, excluding the same
  one forbidden restore/unbounded/auto-delete harness without pass credit.
- `page-snapshot-final-compile-green`: actual cached/no-restore SS CoreCompile,
  exit 0, `PatchCheck.dll` SHA-256
  `f062f4f9b147e5806b72ceefe0102341eaf0d2c1cd5400fffbdd6a655814b784`.
  All commands retain PID/start/end/elapsed/exit/timeout records, with no timeout.
  These later sources have not received a new HK compile or compiled-IL freeze.

T99B-DETAIL, required Tasks/Inventory/Crest serialized graphs, locked-slot/full
native action equivalence, remaining overlays, Map private text/presentation and
Unity visual acceptance remain OPEN. The finite snapshots cover named native
inputs, not arbitrary unknown custom-script dependencies. Native quest accepted
cache updates remain owned by its source versioned gameplay paths; the adapter
never clears or mutates that global cache. No device/game execution, publication,
source activation, git mutation, save edit, cleanup or new independent review
stage occurred. Task99 and parent97 remain OPEN.

### Task99 AreaTitle and ToolTutorialMsg routing checkpoint

**T99B-OVERLAY remains OPEN.** Exact initialized `AreaTitle.UnsafeInstance` now
routes through an original-parent carrier, with actual FSM targets/local spaces
and iTween inputs/bridge identities admitted before routing. NPC event-owner
transforms are never used. Parent-return-after-write failure now retains pending
sibling restoration; independent clips/layers restore before carrier release.
`ToolTutorialMsg` routes only the exact active native rumble-preventer registration,
with native local visual references and already-resident 2D UI sound authority.
Native instances, animation, fade, progression and current local pose are retained.

Receipts under `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
- `area-title-focused-green`, `area-title-contracts-green`,
  `area-title-current-compile-green`: **47/47**, **82/82**, actual SS CoreCompile;
  DLL SHA-256 `99170101d14862988d8d4fcaccf62b408c309a439af7f5367bda5371f5af7e21`.
- `tutorial-focused-green`, `tutorial-contracts-green`,
  `tutorial-current-compile-green`: **52/52**, **83/83**, actual SS CoreCompile;
  DLL SHA-256 `73abd7c4c3b750a1805a54aa9bc01f6bf679a08ffd02661e4239a9435d257da7`.
  Each directory retains completion metadata; all GREEN commands exited 0 without
  timeout. Missing-helper/source REDs are not behavioral failures; the AreaTitle
  parent-after-write RED is behavioral. All failures/old receipts remain preserved.

**T99B-OVERLAY-TUTORIAL-INPUT — OPEN, target Task99 interaction acceptance.**
Native `UIMsgBase.DoMsg` polls primary skip in its own coroutine and exposes no
source-proven staged companion dismissal endpoint. Companion gestures fence the
underlying pages but do not inject input, replay sequence or write progression.
Close only with a source-proven legal endpoint and focused acceptance, or explicit
acceptance of this precise native-only interaction gap; visual routing is separate.

PowerUp/EvaHeal, item/lore, Action and Map presentation remain the next host work.
No combined regression has yet rerun after these overlays; no Unity lifecycle,
reflection, ordinary prefab/controller graphs or visual equivalence is accepted.
Task99/parent97 remain OPEN pending full implementation and independent acceptance.
All restrictions in the following owned-text checkpoint continue unchanged.

### Task99 PowerUp, popup islands and lore continuation

**Partial implementation checkpoints; Task99/parent97 remain OPEN.** Following
AreaTitle/tutorial, exact `PowerUpGetMsg` (including native EvaHeal sequence)
now shares the native running-registration route, with selected native art/text/
prompt references left intact. Registration selection is exact-type, live,
reference-unique and bounded; it no longer scans arbitrary scene instances.
The native-only dismissal restriction also applies to this UIMsgBase sequence.

`CollectableUIMsg` retains its native root: local stacking and
`LastActiveMsgShared.position.y` are native spawn-limit authority, while its
coroutine owns queued save/recycle. Only contained visual islands move. Actual
root-layout targets remain fixed; islands below those targets may route without
changing native direct-child layout authority. Independent island restoration
precedes any carrier release. Owned RectTransform carriers follow the original
parent rect/pivot; native anchor movement and resized-parent mapping are rechecked.
Exact renderer/fixed-target conflicts and unproven cross-island animation/clip
references remain explicit admission gaps, not blanket root-component rejection.
Missing ordinary graphs are not universal rejection evidence or full acceptance.

`MemoryMsgBox` and `NeedolinMsgBox` now route their exact backing singleton and
native appear/cycle/hide/fade state. Needolin event callbacks are read-only checked
against the exact native listener. Text selection, proximity, fade, animation and
hide continuation remain native. These are nonmodal; only failed pending routing
restoration fences gestures. No invented companion continuation is supplied.

Receipts beneath `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
- `powerup-focused-green`, `powerup-contracts-green`, `powerup-current-compile-green`:
  **57/57**, **84/84**, actual SS CoreCompile; DLL SHA-256
  `3f0b5412d8e193f9299cc8397a034631812f67497eedeb5bfa65039b2e48c2c5`.
- `item-focused-green`, `item-contracts-green`, `item-current-compile-green`:
  **62/62**, **85/85**, actual SS CoreCompile; DLL SHA-256
  `4c45c6dfb2443fe601c604350653c2aafa44963bb7429ba6fea7572552003288`.
- `lore-focused-green`, `lore-contracts-green`, `lore-current-compile-green`:
  **69/69**, **86/86**, actual SS CoreCompile; DLL SHA-256
  `b89d6fa55772318e65a954d03031de4d6d4e053c96b48942f4686ef11488051a`.
All GREEN commands exited 0 without timeout, with retained per-directory metadata.
Helper/source absence REDs retain their narrower meaning; prior failures remain.
These exercise actual linked policy/lease/queue/math and source wiring, not Unity
parent callbacks, reflection execution, rendering or concrete prefab graphs.

The combined overlay checkpoint now passes **275/275** in
`overlay-families-combined-green/completion.json`. Exact `(testId, testName)`
multiset comparison in `overlay-families-case-identities` retains every original
**243** case and exactly the **32** new family cases, with no missing, unexpected,
missing-focused or nonpassing identities. `overlay-families-contracts-green`
passes **86/86** allowed contracts; `overlay-families-current-compile-green` is
actual current SS CoreCompile, DLL SHA-256
`c0bedefac32443bb458f77b549705c26a7b272680776fe6f6ab0e62c7ca2a6d3`.
All four commands exited 0 without timeout; receipts share the directory above.
This is still host-only partial implementation, not full/native graph acceptance.

Next are remaining **T99B-ACTION** locked-slot/full native prompts/actions and
**T99B-MAP-PRESENT** dynamic freshness/sorting. Full implementation and independent SPEC/QUALITY/main
acceptance remain required; no Task100 advance or device/publication authorization.

### Task99 owned-native-text checkpoint and ordered overlay continuation

**PARTIAL HOST CHECKPOINT / Task99 and parent97 OPEN.** This supersedes the
implementation gaps above, not their historical evidence or runtime limits.
Exact Inventory/Loadout selected-slot binding and source-proven extra-description
overrides now exist. Journal/Inventory/Loadout/Tasks and auxiliary/details prepare
owned mutable native font/glyph/kerning/weight/fallback/material graphs before
activation. Actual destination text uses native decoding/tag/weight selection;
prospective snapshot strings receive resource checks, not cross-font glyph tests.
Shared fallback-list aliases are extended once in native local/settings order.
Cold Map text reconstructs owned native renderer/subtext state and generates only
owned text; text/submesh/root retirement precedes fonts and retains failed cleanup.
Warm native meshes/atlas textures remain read-only borrowed resources.

Current receipts beneath
`tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
- `owned-native-text-combined-final/completion.json`: **243/243** linked cases.
- `owned-native-text-contracts-final/completion.json`: **81/81** allowed contracts;
  the same implicit-restore/unbounded/auto-delete harness is excluded, not passed.
- `owned-native-text-current-compile-final/completion.json`: actual cached SS
  CoreCompile; DLL SHA-256
  `5651eb35f70a70c0eed0d4943a0c75a882ccce134ac5a39313cc7a6664c44255`.

All three exited 0 without timeout. Earlier REDs, the failed HarmonyLib compile
(the experimental hook was removed), failed native type lookup, old cores,
goldens and frozen partial receipts remain preserved. There is no automatic
native SetArraySizes interception: pre-activation/pre-layout and synchronous
post-callback checks do not establish every native callback timing route safe.
No Unity lifecycle/reflection/rendering/native-action or visual-parity acceptance
is claimed; ordinary/custom serialized graph coverage remains evidence-unknown.

Next required host work is T99B-OVERLAY: exact AreaTitle speaker/title visual
(not `NpcDialogueTitle.transform`), then tutorial/focus/item/lore, preserving
native instances, drivers, animation/fade/progression and independent exact-owner
routing restoration before teardown. Tutorial dismissal stays in its native
coroutine unless an exact legal callable endpoint is source-proven; any remaining
interaction gap is separate from visual routing. Follow with T99B-ACTION locked
slots/full native prompts/actions and T99B-MAP-PRESENT freshness/sorting. Each
family requires reachable wiring, focused RED/GREEN and cached compile; full
Batch B still needs independent SPEC, separate QUALITY and fresh main verification.
No Task100 advancement, accepted HUD/shared changes, nested agents, restores,
config/git mutation, devices/publication/source activation, save edits or cleanup.
Commands remain <=180s, every child wait <=175s with PID/timing/exit/timeout receipts.

