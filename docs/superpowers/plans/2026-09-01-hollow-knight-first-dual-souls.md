# Hollow Knight-First Dual Souls Implementation Plan

> **Current execution authority:** The 2026-09-10 Task99 recovery override and
> matching specification authority supersede the 2026-09-08 host-continuation
> order. V0–V5 is the active queue; H4/H5/H6 are not prerequisites, and
> host-only evidence cannot accept the HUD.

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement this plan task by task. Steps use
> checkbox (`- [ ]`) syntax for tracking. Every stage must be reconciled
> against the authoritative specification before the next stage begins.

**Goal:** Run Hollow Knight's existing Dual Souls HUD, companion pages, Mods,
and multi-pack skin behavior through the proven direct-display technology,
then extract shared contracts and adapt Silksong to that working reference.

**Architecture:** SilksongAndroid contributes only the Unity display-1,
camera/layer, input, lifecycle, diagnostics, and fallback technology. Hollow
Knight is the first production consumer and retains the existing Dual Souls
composition and behavior. Shared UI, Mods, and skin contracts are extracted
only after the complete Hollow Knight slice passes on the AYN Thor; Silksong
then implements those contracts with resident game objects and semantic
extensions.

**Tech Stack:** Unity 6000.0.61f1/6000.0.50f1 IL2CPP, C# patch injection,
Unity multi-display and Input System, Kotlin launcher/profile storage, Python
source contracts, .NET tests, PowerShell exact-source compile checks, Android
Gradle/Robolectric, ADB on `bfa98654`, and GitHub Actions signing.

**Authority:**
`docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md` and
`docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md`.

**Authorization state:** Runtime implementation, host verification, signed
emulator/device verification, commit, push, and the final release workflow are
authorized. Do not interact with a locked physical device; otherwise continue
until completion, a genuine blocker, or an explicit pause request.

---

## Task99 residual6 font-owner handoff — 2026-09-09 19:41 UTC

**SAME SPEC reviewer:1/2/3/4/5 RESOLVED;6's committed-effect lifetime corrected.**
One conditional text-bearing-extra ownership mismatch was corrected and now awaits
that reviewer's6-only recheck. **Task99/parent97 OPEN,7/8 unchanged unapproved
blockers; no QUALITY/full acceptance/Mods/Task100.** No repeated broader review or
new discovery. Reviewed344/105/DLL`70c8a59f…` and all failures remain preserved.

- Only production change is `DsPortProgress.Inventory.cs`: prepare committed extras
  with the same page.Text owner that later traverses them, like existing custom
  icons. Remove unused ConsumeVisual.Text/its Clear. No exclusions or font-check
  relaxation. Common text ownership, poison-before-validation and exact page-state
  retirement mechanics remain unchanged. All extra/page roots must retire before
  page.Text.Clear; failure retains exact obligations. No native response/Take,
  signal/lifetime/reparenting/budget changes.
- **13/13 focused,351/351 combined,106 allowed source contracts**, actual SS
  CoreCompile=true,0 errors/7 existing unrelated warnings. Seven new linked font
  controls cover text/plain, foreign font, exact root/font retirement retry,
  failed preparation and separate-owner negative mismatch. Actual source wiring
  checks the shared owner and strict validation/cleanup boundaries. Managed linked
  mechanics + source/native proof only, NOT Unity text/signal/render execution or
  required serialized graph inclusion.
- Receipts under the existing Task99 evidence root are
  `spec-residual6-font-focused-final`, `spec-residual6-font-contracts`,
  `spec-residual6-font-combined`, `spec-residual6-font-ss`,
  `spec-residual6-font-identities-source`; all exit0/timedOut=false. Matrix addendum
  records PID/start/end/elapsed and preserves source-wiring RED plus the historical
  misleadingly named focused-green11/13 failure (fixture omitted expected Tick
  rethrow, corrected only in tests). No active owned child.
- All **314/334/344/275 identities** preserved;64 compiled-source hashes compared,
  exactly Inventory changed. Inventory SHA-256:
  `6a1900f033874d870bff8562b31aa73390adae6228cbc9be49ec7a8c24773d2e`.
  Current SS DLL SHA-256:
  `7a5305039ddb62994bb533a3d4ca0687862aacc23b72889cd8db8e8da5c7e810`.
  Compiled-source manifest SHA-256:
  `0484ad134f51a2e7e9674950f9dead21dac28710a0941397895dd4e0fd31a484`.
  No production/test edits after final verification/hash capture. Only existing
  status addenda changed afterward. All prior operation/scope boundaries unchanged.

## Task99 residual3/6 handoff — 2026-09-09 19:20 UTC

**SAME independent SPEC recheck:1/2/4/5 RESOLVED,3/6 PARTIAL,7/8 OPEN.**
Only concrete residual3/6 were corrected; candidates now await that same reviewer's
recheck, not self-acceptance. **Task99/parent97 OPEN; no QUALITY/full acceptance,
Mods or Task100 advancement.** Matching matrix addendum contains source/native line
references and PID/start/end/elapsed/exit/timedOut receipts. Preserve the reviewed
334/103 checkpoint, historical addenda, failures, old cores, goldens and evidence.

- **3:** retain exact owned InventoryItemTool slotAnimator alongside crest slot
  Animators. Validate all three native controller arrays on the born-inactive copy
  with finally restoration; native SetData/refresh retains live controller choice.
  Pin/recheck exact entry, template mapping, manager, Animator and ordered variants
  after page refresh and before submit. Primary input remains disabled. Four linked
  fixture cases plus actual production-wiring contracts cover the candidate.
- **6:** separate hold/audio cancellation from exact per-entry page-owned committed
  signal/extra lifetime. Ordinary release detaches callbacks/stops the routine but
  preserves committed visuals; a cancelled next hold cannot retire prior effects.
  Distinct extras spawn only after the reached native signal wait, so early release
  cannot spawn an unreached extra and later commits do not restart previous extras.
  Known extra-node budget admission precedes native commit; postcommit presentation
  failure cannot replay response/Take. Native independent fade timing is preserved.
  Bounded exact-owner visual cleanup remains retryable at page retirement. Six linked
  fixture cases plus actual production-wiring contracts cover cancellation, signal,
  extra, successive holds/commits, retirement retry and owner loss.
- Current-source evidence: **10/10 focused,344/344 combined,105 allowed contracts**;
  actual SS CoreCompile=true,0 errors and7 existing warnings outside changed files.
  Receipts `spec-residual36-focused-green`, `spec-residual36-contracts`,
  `spec-residual36-combined`, `spec-residual36-ss`,
  `spec-residual36-identities-source` are under
  `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`.
  All exit0/timedOut=false; no active owned child. RED source failures and nine
  missing-policy-type failures are preserved, not misrepresented as Unity REDs.
- All **314 baseline/334 reviewed/275 overlay identities** retained. Compiled-source
  comparison changes exactly Inventory,Loadout,SelectState; resolved source files
  remain unchanged. No production edit follows the final compile/hash capture.
  SS DLL SHA-256:
  `70c8a59f6fdfa799b26198a9c042bae52745f574a7762d2dba50604ceaefc756`.
  Compiled-source manifest SHA-256:
  `71d9f1b0a6bba6d4388b47422b29a3abc10520320836e8d82ff245a095b2256e`.
- Evidence is linked managed policy/fixture + reachable source wiring + exact native
  source + cached compilation, **not Unity animation/coroutine/effect execution or
  required serialized graph inclusion**.7/8 remain unchanged OPEN/unapproved; no
  additional searches or scope waiver. Existing offline/no-restore, command/child
  bounds, no config/git mutation/cleanup/device/packaging/source-image activation,
  native-memory/save edits or publication boundaries remain unchanged.

## Task99 same-SPEC correction handoff — 2026-09-09 18:38 UTC

**Task99/parent97 remain OPEN; SAME independent SPEC recheck next. No QUALITY,
acceptance, Mods or Task100 advancement.** Per-finding corrections, residuals and
receipts are recorded in the matching addendum in
`docs/verification/dual-souls-ui-port-matrix.md`; all prior sections remain historical.

- Candidate corrections1–6 are wired in production: exact native fade bridges;
  retained ordinary Loadout submit targets/callback boundary; exact owned slot
  visual Animators and same-page success refresh; retryable Dialogue/credits
  parent-plus-sibling restoration; outgoing page CONTENT retained separately from
  revoked interaction; guarded native Inventory hold/consume/closing consequences,
  cancellation and denied feedback with owned memory-message state. Native primary
  `paneList.InSubMenu` is not written by the Inventory adapter.
- Final current-source verification: **334/334 linked tests,103 allowed source
  contracts**, actual SS CoreCompile=true; all **314 baseline/275 overlay case
  identities** preserved. Receipts are `spec-corrections-final-contracts`,
  `spec-corrections-final-combined`, `spec-corrections-final-ss` and
  `spec-corrections-final-identities-source` under the existing Task99 evidence
  root. All exit0/timedOut=false, each child <=175s; no active owned child.
- Current SS DLL SHA-256:
  `5fca0c0c395164730a17b9b518b71a7a2b674bccfbc4a102468cee88339bf8ed`.
  Compiled-source manifest SHA-256:
  `7cf3f9a8886a010815a691d39701b8d46bbae7d2ee6f654e7dd72b5e73469ebd`.
- This is **managed policy/source-wiring/real compilation evidence**, not native
  Unity graph, coroutine, animation/audio, GPU or live execution. Bridge-populated
  graphs, required serialized slot Animator topology and Inventory effect/audio/
  custom-display/failed-animation graph inclusion remain unproven under existing
  T99B-CREST-GRAPH / T99B-PAGE / T99B-VISUAL gates. Outgoing visible-content tests use
  managed fixture content. No completeness claim follows from conservative rejection.
- **T99B-OVERLAY-TUTORIAL-INPUT remains OPEN:** inspected native DoMsg exposes only
  native skip polling before its completion sequence, no proven legal retained-instance
  companion dismissal endpoint. Legal endpoint or explicit user scope approval is
  required; no synthetic skip, Spawn/Setup replay or early completion was added.
- **T99B-MAP-PRESENT-MIXED-QUEUE remains OPEN:** required serialized graph/pass
  inclusion/exclusion and interleave are not established. Mixed queues in one
  SortingGroup still fail closed; this is not evidence that all Maps fail or that
  required support is complete.
- All REDs, compilation/test-harness failures, old cores/goldens and evidence are
  retained and classified in the matrix. No restore/download/config/git mutation,
  cleanup, packaging/source-image activation, native-memory/save edit, device or
  publication work occurred. No fresh HK compile is claimed for this correction run.

## Task99 current bounded handoff — 2026-09-09 17:10 UTC

Historical unchecked implementation rows below describe their named checkpoint;
this addendum supersedes their ACTION/Map-pending status, not their evidence.
**Task99/parent97 remain OPEN.** No independent acceptance or Mods advance.

- [x] Implement Map same-observation recipe-value/membership freshness, owned
  command-only rendering without native camera callbacks/buffer mutation, and
  native material/layer/order/camera/SortingGroup-informed sorting. Map checkpoint
  **308/308**, **96/96** contracts retained all299 plus9 and original overlay275.
- [x] Correct the concrete equipped-crest-only action gate using native supplied
  slot and exact selected crest/name/descriptor/SaveEquips callback authority.
  Global equipped ID remains freshness authority; ordinary legality gates remain.
  Focused **0/6 RED → 6/6 GREEN**, wiring RED/GREEN, combined **314/314** (308+6),
  **97/97** allowed contracts. No unknown-prefab deferral for that code restriction.
- [x] Correct all four existing named hold-loss cases without changing identities:
  distinct owner/resource/selection/visibility fixtures feed actual linked
  selection/hold policy, each asserts only its named input changes. Focused4/4,
  final combined314/314, exact314/overlay275 retained, no xUnit1026 warning.
  This is modeled native input, not execution of Unity action legality.
- [x] Cached actual current SS CoreCompile, exit0/no timeout. Latest DLL SHA-256
  `c3148b81ff20d221abee7f59c70bb802ff7d32e430b3071f5e3b9100a9ce83ca`.
  Receipt `action-current-source-drift-compile-red` is GREEN by actual result,
  despite its directory name: syntax failure did not reproduce; captured source
  comment was valid, no syntax edit/RED credit. Earlier selected-crest DLL
  `ef39b4b7edb0933ef94f90fc01241ab4918c7307ba1621318cede394565556e6`
  and earlier Map DLL remain separately retained historical evidence.
- [ ] **T99B-OVERLAY-TUTORIAL-INPUT:** native tutorial/PowerUp coroutine owns
  dismissal; close with proven legal staged companion endpoint or explicit
  acceptance of precise native-only input. No injected confirm/progression replay.
- [ ] **T99B-MAP-PRESENT-MIXED-QUEUE:** mixed queues within a SortingGroup are
  rejected pending exact pass/group interleave authority. Close with required
  concrete graph/pass evidence plus matching ordering, or evidence required graphs
  do not use this case. Rejection is not support.
- [ ] Required ordinary graph coverage and Unity/native callback/coroutine/action/
  particle/animation/GPU clipping/pass/visual execution remain separate unpassed
  evidence. Independent SPEC, separate QUALITY and fresh main verification are next;
  no full Task99 acceptance, fresh HK compile or native execution is claimed here.

Receipts: `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`,
including `action-selected-crest-*`, `task99-bounded-implementation-*`,
`action-distinct-loss-green`, `task99-handoff-combined-green`,
`task99-handoff-identities` and `action-current-source-drift-capture`.
Exact command metadata/hashes and residual closure conditions are in the existing
matrix. All historical receipts, offline/no-restore and operational restrictions
remain intact; no active owned child, accepted HUD/shared edit, nested agent,
device/publication/source activation, git/config/save edit or cleanup.

## Task99 ACTION continuation checkpoint — 2026-09-09 16:37 UTC

- [x] Wire native locked-slot hold/removal, source reload/toggle dispatch, exact
  pre-precedence touch cancellation, owned socket icons and action effects;
  preserve native commit authority, waits, and cancellation-before-retirement.
- [x] Preserve same owned page after native action refresh without replaying
  commits; fence reentrant retirement until native callback unwind. Native
  particle callbacks are page-owned rather than creating a global singleton.
- [x] Combined **299/299** linked cases retain all **275** overlay checkpoint
  identities plus exactly **24** action controls. Focused **124/124**, allowed
  source contracts **93/93**, actual cached current SS CoreCompile. DLL SHA-256
  `7688e4e064873d32db6cfcbcdf3cfab39490b687aee34ca9c18bdc26f5d5b83f`.
  Receipt directory `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
  `action-unwind-selection-green`, `action-unwind-contracts-green`,
  `action-unwind-current-compile-green`, `action-factories-combined-green`,
  `action-factories-case-identities`; all exit0/no timeout. Detailed command
  metadata, preserved REDs and implementation limits are in the existing matrix.
- Native `CustomButtonCombo` is informational gameplay instruction, not a missing
  inventory `CustomAction` dispatch. This corrects earlier broad action wording.
- [ ] **T99B-MAP-PRESENT** dynamic freshness/sorting/native callback-buffer authority.
- [ ] Full99 implementation, independent SPEC, separate QUALITY, fresh main QA.

Task99/parent97 remain **OPEN**, Tasks100/101/102 separate and incomplete. No
Unity/native coroutine, concrete prefab, native action, animation/particle or
rendering execution is claimed. Unknown concrete graphs and non-equipped-crest
variants remain unaccepted; strict ownership guards are not proof of support.
The native-only tutorial dismissal gap remains separately tracked. All prior
history/receipts and current offline/no-restore/no-device/no-publication/no-git-
mutation/no-cleanup boundaries are unchanged.

## Non-negotiable execution order

1. Hollow Knight direct-display transport.
2. Hollow Knight Dual Souls HUD and all existing companion modules.
3. Hollow Knight Mods and tweak persistence.
4. Hollow Knight multi-pack skins and death rotation.
5. Complete Hollow Knight device acceptance.
6. Shared-contract extraction with unchanged Hollow Knight regression.
7. Silksong resident-object adaptation.
8. Cross-game acceptance and release.

No Silksong UI implementation may pass a stage before item 5 is complete.
The uncommitted `DsProbe.cs` and its tests are preserved as parked successor
work. They must be validated before later use, but they are not part of the
Hollow Knight critical path.

## Batch-first verification cadence

Port each complete Hollow Knight stage from the pinned Dual Souls source before
starting signed runtime debugging. During the source pass, use fast contracts,
reference diffs, and exact `1.5.12620` compilation; do not create a signed APK
for individual visual or lifecycle corrections. After the complete stage is
present and host-green, run one signed integration matrix, record every defect,
fix that defect set as one batch, and repeat only the affected acceptance rows.
Host-complete H2/H3 checkpoints do not create signed micro-candidates: their
remaining physical observations are batched into the complete Hollow Knight
candidate. Work remains confined to this fork; do not contact or interact with
`igawa6/dualsouls` upstream.

Specification reconciliation still occurs after every H stage. The stage is
the review boundary: a one-line placement correction is not a separate stage
and must not trigger its own documentation, signing, installation, and IL2CPP
cycle. A device cycle before the source-complete gate is permitted only when a
host-invisible platform question genuinely blocks further migration.

Every device cycle from H2 through release is feature-targeted and uses no
gameplay testing. An in-game room or save is not a permitted validation host,
even if the character would remain unmoving. Validation must use a title/menu
fixture, host contracts, controlled debug injection, or synthetic state instead
of navigation, combat, collection, progression, or save mutation through play.
Close the game after each affected-row capture.

This rule applies to every pass, including first checks, defect rechecks,
lifecycle checks, and release-candidate checks. Starting a new game, selecting a
save, skipping an intro, entering a room, or reusing an already-loaded gameplay
state is never part of a validation pass. If a production object only exists
after scene bootstrap, the fixture must create the minimum required state
through an explicit input-disabled, save-neutral debug seam; it must not obtain
that state through played input or a user save.

## File structure and ownership

### Direct-display technology

- Create `tools/shared-patches/src/DualScreen/DirectDisplayHost.cs`: display
  activation, readiness, presence changes, pause/resume, and ordered teardown.
- Create `tools/shared-patches/src/DualScreen/DirectDisplayPresentation.cs`:
  display-1 cameras, private layers, measured panel geometry, mask isolation,
  and single-display fallback.
- Create `tools/shared-patches/src/DualScreen/DirectDisplayTouch.cs`:
  display-attributed touch collection and primary-display fencing.
- Create `tools/shared-patches/src/DualScreen/IDirectDisplayContent.cs`: minimal
  lifecycle contract consumed by a game adapter.
- Modify `tools/silksong-patches/src/dualscreen/DualScreenV2.cs`,
  `DsPresentation.cs`, and `DsTouch.cs` only to delegate proven transport
  responsibilities; do not move Silksong composition into shared code.

### Hollow Knight reference implementation

- Add the MIT-licensed source modules from `igawa6/dualsouls` commit
  `5c22451435b772acde0c7e6456f9019bc1baef73` beneath
  `tools/hollow-knight-patches/src/dualsouls/`.
- Preserve the original module split:
  `HKDualScreen.cs`, `HKDualScreen.Util.cs`,
  `HKDualScreen.Bottom.Layering.cs`, `Bottom.Frame.cs`, `Bottom.Hud.cs`,
  `Bottom.Inventory.cs`, `Bottom.Charms.cs`, `Bottom.Map.cs`,
  `Bottom.Select.cs`, and `Bottom.Tweaks.cs`.
- Add `HkDirectDisplayAdapter.cs` solely for the new transport seam.
- Add `HKTweaks.cs`, `HKModsMenu.cs`, and only their proven dependencies for
  Hollow Knight Mods behavior.
- Add `HKMods.cs` and only its proven dependencies for the
  CustomKnight-compatible skin engine.
- Do not copy game assets, generated assemblies, Unity binaries, user packs,
  saves, credentials, or any file excluded by `REDISTRIBUTION-AUDIT.md`.

### Shared features after Hollow Knight acceptance

- Create `tools/shared-patches/src/Companion/ICompanionGameAdapter.cs` only in
  Stage H6, after the concrete Hollow Knight behavior is known.
- Retain `tools/shared-patches/src/Mods/TweakContracts.cs` and
  `TweakController.cs`; adapt Hollow Knight to them without weakening its
  existing behavior.
- Create the launcher skin registry/scanner under
  `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/`
  from the accepted Hollow Knight library behavior.
- Keep selections, Mods master/switches, skin enablement, rotation mode, and
  last page namespaced by profile.

### Silksong successor phase

- Retain the completed transport/frame source evidence and parked probe.
- Resume the `DsPort*` work only after Stage H5.
- Delete rejected `DsShell`/generic screen files only after their native
  replacements pass the Silksong device gate.

## Stage H0: Rebaseline authority and preserve the parked work

**Requirements:** DSUI-00, DSUI-08, DSUI-10.

**Files:**

- Modify the two authoritative specifications.
- Create this plan.
- Modify `docs/superpowers/plans/2026-08-29-unified-hollow-knight-platform.md`.
- Modify `docs/superpowers/plans/2026-08-31-dual-souls-ui-port.md`.
- Modify `docs/verification/design-traceability.md`.
- Modify `docs/verification/dual-souls-ui-port-matrix.md`.
- Modify `README.md`.

- [x] **Step 1: Record the Hollow Knight-first gate**

Require the Hollow Knight vertical slice to complete before any Silksong UI
stage can advance. Mark the existing Silksong Stage 2 device gap and Stage 3
probe as `PARKED`, retaining every blocker and evidence link.

- [x] **Step 2: Verify that no prior evidence was erased**

Run:

```powershell
rg -n "HOST-VERIFIED-SOURCE|AUDIT-COMPLETE|PARKED|ab77f5a|1feebf5" `
  docs README.md
```

Expected: completed Stage 1/2 source evidence and the Stage 3 audit remain
present, while the successor work is explicitly non-critical-path.

- [x] **Step 3: Commit the rebaseline**

```powershell
git add README.md docs
git commit -m "docs: prioritize the Hollow Knight reference port"
```

**Stage reconciliation:** Re-read DSUI-00 through DSUI-10. This stage passes
only when the plan, specifications, README, traceability, and matrix all name
the same Hollow Knight-first order and the parked Silksong blockers remain.

## Stage H1: Isolate the minimum direct-display transport seam

**Requirements:** DSUI-00, DSUI-02, DSUI-10.

**Files:**

- Create the four `tools/shared-patches/src/DualScreen/DirectDisplay*` files
  listed above.
- Create `tools/shared-patches-tests/DirectDisplayStateTests.cs`.
- Modify `tools/shared-patches/SharedPatches.Core.csproj`.
- Modify `tools/silksong-patches/src/dualscreen/DualScreenV2.cs`.
- Modify `tools/silksong-patches/src/dualscreen/DsPresentation.cs`.
- Modify `tools/silksong-patches/src/dualscreen/DsTouch.cs`.
- Modify `tools/hollow-knight-patches/HollowKnightPatches.csproj`.

- [x] **Step 1: Write failing shared-state tests**

Create tests for presence, readiness, pause, display loss, reactivation,
single-display fallback, and ordered teardown. The pure contract begins as:

```csharp
public interface IDirectDisplayContent : System.IDisposable
{
    void SetTransportActive(bool active);
    void OnPanelGeometry(float width, float height);
}
```

Run:

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
```

Expected: RED because the transport state types do not yet exist.

- [x] **Step 2: Extract only proven technology**

Move display activation, presence/readiness state, camera/layer ownership,
panel measurement, touch attribution, pause/resume, diagnostics, and fallback.
Do not move `DsPortFrame`, `DsShell`, any page, game data, or game type into
the shared project.

- [x] **Step 3: Verify both consumers compile**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
pwsh -NoProfile -File tools/silksong-patches/check.ps1 `
  -Depot 'D:\Temp\hkandroid-task11-silksong-managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

Expected: shared tests green and both exact patch targets compile.

- [ ] **Step 4: Prove a transport-only Hollow Knight panel**

Build through the signing workflow, install as an update preserving data, and
display only an opt-in diagnostic card on display 1. Verify display loss,
resume, and single-screen fallback; this proves transport only, not the HUD.
Close the game after capture.

**2026-09-01 device result:** The signed candidate updated the production
package in place, rebuilt exact Hollow Knight `1.5.12620`, and rendered the
opt-in diagnostic on display 1 while the game remained on display 0. The same
PID survived pause/resume and the transport reactivated without a crash. The
Thor's lower DRM connector can be forced `disconnected`, but its composer
keeps logical display 4 registered and Unity emits no removal event. Therefore
physical display-loss and true single-display startup remain one tracked
deferral, not a pass. See
`docs/verification/hollow-knight-direct-display.md`.

- [x] **Step 5: Reconcile and commit**

Record exact hashes/logs and commit `refactor: share direct display transport`.
Any visual redesign or Silksong-only dependency in shared code is a blocker.

**Stage reconciliation:** DSUI-00 and the H1 transport portion of DSUI-02 are
satisfied. DSUI-10 has RED/GREEN tests, exact Hollow Knight and Silksong
compiles, a signed artifact, update-preserving Thor evidence, and synchronized
documentation. H1 has `blockers = 0` and `tracked_deferrals = 1`; the physical
detach/single-display device row must reach zero by H5. This does not permit
the diagnostic card to stand in for any H2 HUD or companion responsibility.

## Stage H2: Port the complete Hollow Knight Dual Souls companion

**Requirements:** DSUI-00, DSUI-01, DSUI-02, DSUI-07, DSUI-10.

**Files:**

- Add the Hollow Knight reference modules listed under file ownership.
- Create `tools/hollow-knight-patches/src/dualsouls/HkDirectDisplayAdapter.cs`.
- Modify `tools/hollow-knight-patches/entrypoints.json`.
- Create `tools/ci/tests/test_hollow_knight_reference_port.py`.
- Create `docs/verification/hollow-knight-direct-display.md`.

- [x] **Step 1: Write failing module and behavior contracts**

The test must require every reference module, the exact pinned reference
commit, a direct-display adapter, and absence of the old EGL/Java bridge in
the compiled source list. It must assert that `RelayerHud` routes the one live
HUD, adopts spawned children, and restores it under the oracle conditions.

Run:

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port
```

Expected: RED because the modules and adapter are absent.

- [x] **Step 2: Import the MIT-licensed source modules without assets**

Preserve the original module boundaries and behavior. Record source SHA-256
values and the reference commit in the verification document. Reject any
binary/game-asset path before staging it.

- [x] **Step 3: Replace only the presentation technology**

`HkDirectDisplayAdapter` must translate the shared transport's panel geometry,
layers, cameras, touch events, visibility, display loss, and teardown into the
existing `HKDualScreen` lifecycle. Do not reimplement the HUD or pages with
new widgets.

- [x] **Step 4: Complete one source-level companion parity pass**

Audit all imported H2 modules against the pinned Dual Souls source in one
pass. Require every HUD, frame, page, selection, prompt, dialogue/tutorial,
item/lore overlay, fade/death, pause/inventory, resume, touch, and restoration
responsibility to exist behind the direct-display adapter. Resolve source-list,
API, lifecycle, geometry, and exact-build failures together. Run the focused
Python contracts and exact Hollow Knight compile, but do not sign or deploy
until this whole source-level inventory is green.

- [x] **Step 5: Run one signed lower-HUD companion gate**

Build and sign once, install as an update, launch exact Hollow Knight
`1.5.12620`, and inspect only the companion surface: live HUD and clean top
screen; Inventory, Charms, Map; selection/action prompts; pause presentation;
the blurred/dimmed scenery wash; and lower-display touch. Do not navigate,
fight, collect, progress, alter saves, or run general gameplay. Dialogue,
tutorial, item-popup, fade/death, restoration, and other state transitions use
focused host contracts or controlled debug injection/current static state.
Resident tab labels must remain visible after the source inventory closes;
glyphs and fleurs must fit their measured cells. Record the entire lower-HUD
defect set before changing source, and close the game after capture.

- [x] **Step 6: Fix the captured H2 defect set as one host batch**

Convert every observed defect into a focused regression contract, implement
the complete correction batch, and rerun host verification and exact
compilation. H2 source/host implementation is complete at `00627e3`; the
remaining physical label, teardown, and topology observations are not passed.
They are batched into the complete Hollow Knight candidate rather than a signed
H2 micro-candidate. That later authorized gate must use the controlled
title/menu fixture plus process pause/resume and teardown smoke, and must not
select a save, start a game, skip an intro, enter a room, or issue gameplay input.
A missing module or visual/behavioral difference found there remains a blocker,
not a Silksong deferral.

Progress checkpoint: signed dry-run `33520627292` installed commit `0f9f398`
in place and rebuilt exact generation
`gen-b947d3af-d366-4107-93dc-9bb9a494a724`. A minimum static King's Pass room
hosted the lower HUD without movement or gameplay testing. The persistent
opening credit routed to display 1 and lower touch still opened Inventory, but
the three resident tab labels remained absent. Comparison with the pinned Dual
Souls source found the remaining divergence: the reference preserves every
cloned label dependency and disables each non-TMP `MonoBehaviour`, while the
Android port destroyed all but `TextContainer`. Signed dry-run `33525049660`
then installed commit `672724c`, rebuilt exact generation
`gen-8b38e8dc-3117-4236-9c7c-1fc3b91ebc01`, and ran an affected-row-only pass:
the static King's Pass fixture remained unmoving, live masks/Soul and the lower
Inventory panel rendered, the primary HUD stayed clean, and lower touch worked,
but the three labels still produced no pixels. Two isolated `GameCameras`
startup lookups also remained. That pass proved that preserving dependencies was
necessary but that the added inactive staging lifecycle itself diverged from the
pinned reference. The third correction now restores the oracle's direct
live-frame clone/disable/activate/text/mesh sequence and makes the persistent-HUD
scan use the already resolved quiet camera owner. It passes 25 focused contracts,
58 shared tests, 103 Python tests, both exact compiles, and the exact 222,720-byte
Hollow Knight patch. Signed dry-run `33528905347` installed commit `0dd27b7`,
preserved the package identity and app UID, and rebuilt exact generation
`gen-77e25023-d93d-412d-ae4f-e614b2b02bd8`. The attempted pass was stopped
before acceptance when its setup path reached new-game/intro state; no save file
was created and no result from that path is counted. At that checkpoint, the
physical recheck remained open for a title/menu-bound controlled lower-HUD
fixture to observe labels and startup logs without entering gameplay. Signed
dry-run `33532750369` then installed
commit `55dceea`, preserved package identity/UID/first-install time, and rebuilt
exact generation `gen-b38920f7-580d-47bb-96d6-f5abcecdb2e5`. Its default-off
`MAIN_MENU` fixture locked native menu input, invoked only the production
frame/tab path, and closed without selecting a save, starting a game, entering a
room, issuing gameplay input, or creating a save. The capture exposed a real
prior Android-only scale divergence: `b866732` changed the pinned
`compTabScale` from `2.7` to `0.6`, reducing the associated tab chrome to 22%
of the reference size. Complete log stacks also show both isolated
`GameCameras` messages came from Hollow Knight's own
`Platform:SetSceneLoadState`, not the patch. The next correction restores the
pinned `2.7` scale and eliminates all remaining patch-owned logging singleton
getters. Signed run `33536682728` proved the scale correction and exact
generation `gen-0815d295-5ed7-4011-b3d9-41ef0cd79983` through the same locked
menu fixture, but the native glyphs remained absent on both Map and Inventory.
A controlled `compNoMapY` probe kept the known-good native label visible inside
the tab band, ruling out camera/layer, Map-mask, and spatial-stencil causes.
The working native lifecycle instead finalizes text after all frame siblings
exist; the tab clones now follow that path from `PositionFrame`. It passes 30
focused contracts, 58 shared tests, 109 Python tests, both exact compiles, and
produces a 226,816-byte Hollow Knight patch. At that checkpoint, the physical
recheck remained open for the lower-HUD label/teardown observation.

Signed dry-run `33544518172` then installed converter-safeguard commit
`c0e3ff3` in place, rejected the interrupted markerless tree, completed a clean
conversion, linked a 268,705,088-byte ARM64 library, and published exact
generation `gen-49b4e6f5-be4b-4221-9904-b753c9734157`. The durable completion
marker and a direct linker check both passed, closing the interrupted-output
reuse defect. The subsequent input-locked `MAIN_MENU` fixture selected no save,
started no session, entered no room, issued no gameplay input, and was closed
after the affected capture. Separators, fleurs, and the battery icon rendered,
but every detached native text clone remained invisible despite valid bounds.
Exact-source comparison and `1.5.12620` assembly inspection found the pinned
reference's `Name.Contains("TextMeshPro")` filter also retained
`TextMeshProClipRect`; its `LateUpdate` kept writing the original inventory
`_ClipRect` to the detached renderer and submeshes. The current correction keeps
only exact `TMProOld`/`TMPro` graphics, disables the stale driver before
activation, and neutralizes every main/submesh property block after mesh
generation. It passes 32 focused contracts, 122 Android tests, 58 shared tests,
38 bundle-surgery tests, 112 Python tests, and the exact 226,816-byte Hollow
Knight compile. This was the last device result; it did not prove the clip-safe
correction.

Host checkpoint `00627e3` completes the H2 source/host implementation. No later
Android, device, emulator, ADB, signing, or live validation occurred during the
H2/H3 host work, so the label/teardown and tracked H1 physical-topology
observations remain unpassed. Both are batched into the complete Hollow Knight
candidate under the no-micro-candidate cadence.

- [x] **Step 7: Reconcile the source/host checkpoint**

Record H2 source/host completion separately from physical acceptance. The
reconciliation commit follows `00627e3` and does not turn either remaining
physical observation green.

## Stage H3: Establish the Hollow Knight Mods host slice and tracked deferrals

**Requirements:** DSUI-00, DSUI-05, DSUI-09, DSUI-10.

**Files:**

- Add `HollowKnightTweakAdapter.cs`, `HollowKnightGameTweakApi.cs`,
  `HollowKnightModsSession.cs`, and `HollowKnightModsRuntime.cs` under
  `tools/hollow-knight-patches/src/mods/`.
- Add `HollowKnightModsPresenter.cs` within the existing H2 companion layout and
  connect it through the bounded H2 stage hooks/direct-display lifecycle.
- Extend the shared Mods contracts, controller, menu model, persistence store,
  presenter helpers, and their focused tests.
- Extend `tools/ci/tests/test_hollow_knight_reference_port.py` and the profile
  pipeline contracts.

- [x] **Step 1: Prove persistence and isolation on the host**

The shared contracts keep `dualsouls.mods.hollow-knight.*` and
`dualsouls.mods.silksong.*` disjoint, keep each master default-off, and cover
process-owned persistence without borrowing the companion-view lifecycle.

- [x] **Step 2: Implement the bounded Hollow Knight Mods host slice**

The fork now has a Hollow Knight capability catalog, controller, process
runtime, and existing-layout modal. The process runtime also owns the sole
lifeblood renderer policy; `HKDualScreen` only publishes its live H2 config as
a nullable fallback and display teardown clears that reference without
restoring the process policy. Only the reversible companion-backdrop
`dimmed`/`black` and lifeblood-flash `soft`/`vanilla`/`off` presentation
capabilities are available. Every gameplay, progression, world, economy, and
state capability `HKMOD-001` through `HKMOD-018` remains visible but disabled
and tracked for Final managed-rewrite remediation. This is not full Mods parity.
Typed Hollow Knight APIs remain mandatory; native memory-address tables are
prohibited.

- [x] **Step 3: Run the bounded host gate**

The earlier process-owned policy checkpoint is `ba32812`; the current H3
authority/state-core checkpoint is `fix: unify Hollow Knight flash authority`.
Its immutable decision keeps master `soft`/`vanilla`/`off` and the process Soft
alpha independent of live display config. Only the process-owned policy may
restore tracked renderer state: on no-owner release (including master-off only
when no legacy owner remains), transition to Vanilla as needed, camera
replacement, or runtime disposal. Master-off with a live legacy publisher
transfers authority to the legacy policy and continues enforcement.
Direct-display teardown clears only legacy publisher state; presenter teardown
never touches the process policy.
Fresh host evidence at the corrected boundary is:

- focused Mods/state-core tests: 89 passed, 0 failed, 0 skipped;
- complete shared suite: 143 passed, 0 failed, 0 skipped;
- Hollow Knight reference/source contracts: 47 passed;
- exact Hollow Knight `1.5.12620` compile: 0 errors, 1 pre-existing `CS0649`
  warning, 2 entry points, and a 271,360-byte DLL;
- exact Silksong compile: 53 sources and 10 entry points;
- production scan for `DllImport`, `ReadProcessMemory`, `WriteProcessMemory`,
  `HKTW_`, `HKTweaks`, `HKModsMenu`, and `Bottom.Tweaks`: no matches; and
- final Git diff/status clean and branch synchronized to the fork at checkpoint
  publication.

No Android, device, emulator, ADB, signing, live, or UI acceptance testing
occurred. H3 therefore triggers no signed build and passes no physical result.

- [x] **Step 4: Reconcile the host checkpoint**

H3 is `HOST-COMPLETE / TRACKED-DEFERRALS` after the bounded host gate, not
device-accepted and not gameplay-tweak parity. H4 follows on the same branch in
the unchanged product sequence. Once the complete H3/H4 Hollow Knight slice is
ready, it receives one future device gate only when that gate is explicitly
authorized; no H3 or H4 micro-candidate is built or signed.

## Stage H4: Port multi-pack skins and death rotation

**Requirements:** DSUI-00, DSUI-05, DSUI-09, DSUI-10 and unified-platform
Goals 6–7.

**Files:**

- Add audited `HKMods.cs` skin behavior and its source-only dependencies.
- Create launcher `SkinManifest.kt`, `SkinPackScanner.kt`, `SkinRegistry.kt`,
  and `SkinActivationStore.kt`.
- Create `tools/hollow-knight-patches/src/skins/HollowKnightSkinAdapter.cs`.
- Create `docs/skin-pack-format.md`.
- Extend `SkinRotationStateMachineTest.kt` and add scanner/registry tests.

- [ ] **Step 1: Write failing scanner and state tests**

Cover multiple valid immediate-child packs, one invalid sibling, duplicate IDs
with equal/different hashes, traversal attempts, per-game mappings, independent
activation, ordered/shuffled rotation, repeated death before respawn, one-pack
behavior, disabled rotation, and apply-failure rollback.

- [ ] **Step 2: Implement the fail-closed pack model**

Use this manifest boundary:

```kotlin
data class SkinManifest(
    val schemaVersion: Int,
    val id: String,
    val name: String,
    val author: String,
    val games: Map<String, SkinGameMapping>,
)

data class SkinGameMapping(
    val assetRoot: String,
    val textures: Map<String, String>,
)
```

Reject absolute paths, `..`, unknown schema versions, empty mappings, and any
resolved file outside its pack root. Preserve pack directories; never flatten
texture files.

- [ ] **Step 3: Connect the proven Hollow Knight texture engine**

Map CustomKnight names to the existing `HKMods` targets. On death, select but
do not apply the next pack; apply only after stable respawn. Preserve the last
working skin and record an error if application fails.

- [ ] **Step 4: Verify host, exact compile, and device rotation**

Import at least two valid packs and one invalid sibling, apply each pack,
rotate across controlled death/respawn-state injection, relaunch, disable skins,
and verify live rollback. Never obtain those states through gameplay or a user
save. Confirm no Silksong selection or files changed. Close the game after
capture.

- [ ] **Step 5: Reconcile and commit**

Update documentation and README. Commit `feat: add Hollow Knight skin library
and death rotation` only with zero stage blockers.

## Stage H5: Close the Hollow Knight reference gate

**Requirements:** DSUI-00 through DSUI-10 as applicable to Hollow Knight.

**Files:**

- Modify `docs/verification/hollow-knight-direct-display.md`.
- Modify both ledgers and `README.md`.

- [ ] **Step 1: Run fresh host verification**

Run the full Python, .NET, Gradle/Robolectric, exact Hollow Knight compile,
repository-content audit, and signing-contract suites from a clean checkout.

- [ ] **Step 2: Run the complete Thor matrix**

On `bfa98654`, verify HUD, every companion page, all lower-display gestures,
Mods effects and persistence, skin scan/apply/rotation/rollback, pause/resume,
display loss, single-display fallback, and process exit through title/menu or
controlled injected state. Verify save-preservation and rollback semantics on
host synthetic save fixtures; device passes neither read nor write game saves.

- [ ] **Step 3: Cross-check the specification**

Record `blockers = 0` and `tracked_deferrals = 0` for the Hollow Knight
reference slice. Rendering only a HUD or diagnostic card cannot close H5.

- [ ] **Step 4: Commit the accepted reference**

Commit `test: accept the Hollow Knight Dual Souls reference` and push. Do not
start Silksong adaptation until this commit exists.

## Stage H6: Extract shared contracts from the accepted reference

**Requirements:** DSUI-02, DSUI-09, DSUI-10.

**Files:**

- Create `tools/shared-patches/src/Companion/ICompanionGameAdapter.cs`.
- Refine shared direct-display, Mods, and skin contracts.
- Modify the Hollow Knight adapter without changing observable behavior.
- Create shared contract tests.

- [ ] **Step 1: Write failing boundary tests**

Require shared sources to reference no Hollow Knight or Silksong game type.
Define only capabilities proven by H5:

```csharp
public interface ICompanionGameAdapter : System.IDisposable
{
    string ProfileId { get; }
    bool IsGameplayReady { get; }
    void Attach(IDirectDisplayContent host);
    void Tick();
    void RestorePrimaryDisplay();
}
```

- [ ] **Step 2: Extract one responsibility at a time**

After each transport, lifecycle, Mods-store, and skin-library extraction, rerun
the full H5 host and device matrix. A Hollow Knight behavior change is a
blocker and the abstraction must be corrected before continuing.

- [ ] **Step 3: Reconcile and commit**

Commit `refactor: extract shared companion contracts` only after the accepted
Hollow Knight evidence remains unchanged.

## Stage S1: Resume the Silksong resident-object port

**Requirements:** DSUI-01 through DSUI-10.

**Files:**

- Resume `DsProbe.cs` and `test_dual_souls_ui_port.py` from their parked state.
- Continue `DsPortHud`, `DsPortMap`, `DsPortInventory`, `DsPortLoadout`,
  `DsPortSelect`, `DsPortOverlays`, and `DsPortProgress`.
- Update the source-to-source matrix after every module.

- [ ] **Step 1: Validate the parked probe before use**

Run its four RED contracts, complete the implementation, focused tests, exact
Silksong compile, and two independent source reviews. Do not commit a probe
that can false-complete on unrelated loaded objects.

- [ ] **Step 2: Port modules in oracle order**

Use the accepted Hollow Knight implementation—not screenshots or the rejected
shell—as the executable reference. Move the same live Silksong HUD objects,
preserve drivers, and implement pages/overlays through resident objects and
the shared contracts.

- [ ] **Step 3: Reconcile each module**

For HUD, Map, Inventory, Loadout, selection, progress, and overlays: write RED
contracts, implement, run both exact compiles, capture side-by-side device
evidence, update the matrix/README, and resolve all stage blockers before the
next module.

## Stage S2: Adapt Mods and skins to Silksong

**Requirements:** DSUI-04, DSUI-05, DSUI-09, DSUI-10.

- [ ] **Step 1: Implement the Silksong typed Mods adapter**

Map only source-proven APIs. Persist every master and switch under the
Silksong profile, default master off, and keep Hollow Knight values unchanged.

- [ ] **Step 2: Implement the Silksong skin/lifecycle adapter**

Use Silksong-specific texture targets and death/stable-respawn events while
consuming the same library/rotation contracts. A pack may contain separate
per-game mappings; texture compatibility is never inferred across games.

- [ ] **Step 3: Run the full Silksong device matrix**

Verify Mods effects/persistence and two-pack death rotation/rollback in
addition to the complete companion matrix. Close the game after capture.

## Stage F1: Cross-game acceptance, cleanup, and release

**Requirements:** All unified-platform goals and DSUI-00 through DSUI-10.

- [ ] **Step 1: Remove rejected and transitional code**

Delete the authored Silksong shell and any temporary duplicate transport only
after both adapters pass. Confirm no production entry point references them.

- [ ] **Step 2: Prove two-game isolation and switching**

Run both switch directions at the title/menu boundary, independent
Mods/skins/page state through controlled injection, reset isolation, display
fallback, update, and rollback. Verify save preservation only with host
synthetic fixtures; device switching does not open or mutate a user save.

- [ ] **Step 3: Final specification reconciliation**

Require `blockers = 0` and `tracked_deferrals = 0` across both ledgers. Refresh
README claims from fresh evidence.

- [ ] **Step 4: Build, sign, publish, and reverify**

Use GitHub Actions. Rebuild the same commit reproducibly, publish the aligned
tag/release, download the APK again, verify hash/package/version/signer, update
the installed app, and smoke-test both profiles. Close the running game.

## Cumulative reconciliation ledger

| Stage | Requirements | State | Blocking gate | Required evidence |
| --- | --- | --- | --- | --- |
| H0 | DSUI-00/08/10 | `COMPLETE` | None | Both specifications, both parent plans, matrix, traceability, README, and 5/5 ordering contracts agree; the existing 38-contract Silksong suite remains green after the status correction |
| H1 | DSUI-00/02/10 | `IMPLEMENTED / DEVICE-PARTIAL` | No implementation blocker; one tracked physical detach/true single-display deferral must close by H5 | 49/49 shared tests, 78/78 Python tests, both exact compiles, signed run `33494317664`, update-preserving Thor transport and pause/resume captures |
| H2 | DSUI-00/01/02/07/10 | `SOURCE/HOST-COMPLETE / PHYSICAL-BATCHED` | Source/host implementation is complete at `00627e3`. The locked clip-safe label/teardown recheck and H1 topology observation remain unpassed and are batched into the complete Hollow Knight candidate, not a signed micro-candidate | Historical signed evidence remains recorded above. The final H2 correction passes 32 focused contracts, 122 Android tests, 58 shared tests, 38 bundle-surgery tests, 112 Python tests, and an exact 226,816-byte patch; no later Android/device/emulator/ADB/signing/live validation occurred |
| H3 | DSUI-00/05/09/10 | `HOST-COMPLETE / TRACKED-DEFERRALS` | No H3 host blocker through authority/state-core checkpoint `fix: unify Hollow Knight flash authority`; `HKMOD-001`–`HKMOD-018` remain visible, disabled tracked deferrals for Final managed-rewrite remediation. This is not full Mods parity or device acceptance | 89/89 focused Mods/state-core tests, 143/143 complete shared tests, 47/47 Hollow Knight reference/source contracts, and 53/53 combined source/docs contracts; exact Hollow Knight compile with 0 errors, 1 pre-existing `CS0649` warning, 2 entry points, 271,360-byte DLL; exact Silksong compile with 53 sources and 10 entry points; prohibited production scan clean. Executable state tests cover display loss, single-display master authority, per-field native reconciliation, release/Vanilla restoration, idempotence, and fresh replacement bindings. Only the process policy restores tracked state; direct-display teardown clears only legacy publisher state and presenter teardown does not touch process policy. H3 creates no signed build; H4 follows on the same branch, then the complete H3/H4 slice receives one future explicitly authorized device gate |
| H4 | DSUI-00/05/09/10 | `PAUSED / NOT A PREREQUISITE` | None for Task99 HUD recovery | Preserve existing scanner/application/rotation work; do not expand it before visible Silksong HUD/pages |
| H5 | DSUI-00–10 | `PARTIAL REFERENCE / NOT A PREREQUISITE` | None for Task99 HUD recovery | Use one bounded Hollow Knight lower-HUD reference capture; complete Mods/skins/topology closure is deferred |
| H6 | DSUI-02/09/10 | `SUPERSEDED AS A PROJECT` | None | Extract only a concrete seam when a working second consumer requires it; no generalized extraction gate |
| S1 | DSUI-01–10 | `IN-PROGRESS / LIVE HUD FAILED` | Task99 V0–V5 visible HUD recovery | Signed commit `f56defa5` leaves the native HUD on display 0 and an empty shell on display 1; host acceptance is withdrawn |
| S2 | DSUI-04/05/09/10 | `BLOCKED BY VISIBLE HUD/PAGES` | Pass Task99 HUD and page slices first | Existing host work is preserved; no further Mods/skin implementation before visible companion parity |
| F1 | All | `PENDING` | Zero blockers/deferrals across both games | Switching, update/rollback, signed fresh-download proof |

No stage passes merely because code compiles or something renders on display
1. After every stage, re-read the specifications, update this ledger and the
two verification ledgers, classify every gap, update the README for a major
achievement, and stop advancement while a stage blocker remains.

## 2026-09-08 execution addendum — Silksong host continuation

The user has resumed Silksong host implementation. This addendum supersedes
historical H5/H6 and per-module device prerequisites for this continuation;
historical states and unpassed evidence remain preserved, not converted to
passes. The approved product scope includes both games, not only Hollow Knight.

Retain Silksong's direct-display engine and adapt Hollow Knight's bottom-screen
design using Silksong's same live semantic HUD objects/native drivers and native
page, selection, prompt and overlay composition. DsShell, cloned/mirrored gameplay
HUD, the intact default Silksong HUD layout and synthetic replacement widgets
remain rejected. Static/status chrome may reuse native donors separately.

Task96 closes the simplified Hollow Knight skin host gate: 121 accepted C# and
84 accepted Kotlin cases over the recorded 566-source/27-input freeze. It does
not establish device parity, complete texture-family support or full gameplay
Mods parity. H3 still has two implemented presentation capabilities and eighteen
disabled gameplay rows. The retained final acceptance artifact is
`tools/shared-patches-tests/obj/task96-main-20260908T125700Z/final-main-host-verification.json`,
SHA-256 `cd72d5757ba7cca8a6833e8dc0e11640b60aa33804f56e6a499944335818ff5d`.
These documentation and subsequent implementation changes establish a new
source baseline; they do not extend that historical freeze or its test credit.

### 2026-09-10 Task99 recovery override — stop building around the missing HUD

This override supersedes the Task99 batch order below and every H4/H5/H6
prerequisite for the current correction. It implements the corrective authority
in the specification. Historical implementation notes remain evidence, not the
active work queue.

The signed/current-source Thor result is **FAIL**:

- primary `32-new-game-158s.png`: Silksong's health and Silk HUD remains at the
  top-left of the gameplay display;
- lower `33-first-gameplay-lower.png`: only a sparse ornamental shell and text
  tabs render; health, Silk, currency and Crest/Tool status are absent.

Therefore Task99 Batch A is not functionally complete. Its prior SPEC PASS,
QUALITY PASS, fresh-main PASS and accepted-HUD wording are withdrawn for product
acceptance. They prove only that the current routing/state code compiles and
passes its own host model.

#### Active work order

- [ ] **V0 — Pin one visual reference, not all of H5.** Capture or reuse one
  exact Hollow Knight gameplay pair showing the clean primary display and the
  populated lower companion. Record its exact game version, app commit and
  capture provenance, then mark the lower live-HUD rectangle, central content
  rectangle and tab/status rows. This is a bounded visual reference; complete
  Hollow Knight Mods, skins, topology and H5 closure are not prerequisites.
- [ ] **V1 — Route the four essential live groups directly.** Using the existing
  Silksong direct-display host and the concrete Hollow Knight frame geometry,
  route the same live Silksong health, Silk, currency and equipped Crest/Tool
  objects into those lower-display regions. Preserve their current native
  components and drivers. Remove them from display 0 by ownership-preserving
  reparenting/re-layering, not by drawing copies or hiding a separate clone.
  These four groups are the minimum visible baseline, not a claim that Silksong
  has no additional HUD mechanics. In the same pass, inventory the real
  Silksong HUD hierarchy and managed APIs for additional persistent/contextual
  mechanic presentations; map each proven group into the nearest Hollow
  Knight-derived region instead of leaving a separate display-0 HUD. Do not
  speculate beyond actual source/runtime evidence. Rewrite or delete
  `DsPortHud` logic that merely records slot state without producing this
  result. Do not work on pages, Map, overlays, Mods, skins or a new abstraction
  during V1.
- [ ] **V2 — Run only correction-focused host checks.** Add the smallest RED
  regressions that prove exact runtime root discovery, successful mutation of
  all four groups, display-0 removal, display-1 layer/slot assignment, late
  native-driver reassertion and restoration. Use exact cached Silksong managed
  metadata rather than permissive invented nodes. Bind the HUD-mechanic
  inventory to concrete owner/component identities and classify every actual
  group as routed, contextual, or unavailable in the current early-game state;
  no discovered group may silently disappear. Run the focused suite and exact
  Silksong compile; run Hollow Knight compile only if shared/HK source was
  changed. Do not expand test/golden counts or close unrelated matrices.
- [ ] **V3 — Run the signed visible gate immediately.** Commit and push the
  focused correction to the user's fork, build that exact commit with the
  persistent signing identity, update-install it without clearing app data,
  rebuild only the invalidated Silksong generation, and open the recorded
  disposable test slot. Capture both physical displays in ordinary gameplay.
  The gate fails if any essential primary HUD remains, any essential lower HUD
  group is absent, or any currently reachable source/runtime-proven additional
  HUD presentation is absent from its mapped lower-display region, fails to
  update through its native driver, or remains anywhere in a separate display-0
  layout. It also fails if the lower hierarchy/density/scale is not recognizably
  the Hollow Knight companion. Compilation or log evidence cannot override the
  images.
- [ ] **V4 — Observe one native update and lifecycle cycle.** Without assistant
  movement/jump/attack, use an available passive/current-state update or ask the
  user for one bounded direct action. Prove the routed native HUD changes, then
  prove pause/resume and teardown restore ownership without duplication.
- [ ] **V5 — Review and publish the corrected checkpoint.** Only after V3 and V4
  pass, run independent SPEC then QUALITY review over the correction and its
  images. Any review-driven implementation change invalidates the signed gate:
  commit and push the change, rebuild that exact commit, and repeat V3/V4 before
  acceptance. When review and signed evidence agree, retain the accepted commit
  on the user's fork and create the next signed candidate. Keep the disposable
  test save until the complete device pass no longer needs it; before delivering
  the functional APK, delete only the recorded test slot.

#### Hard stop conditions

Do not advance to native pages while the live HUD gate is red. Do not advance to
Task100 Mods or Task101 skins until the HUD and then each required page has a
populated lower-display device capture. Tasks39–41, 56–57, 79, 81, 84 and 90–91
are preserved but paused; none is a prerequisite for this route. An unsupported
hypothetical prefab, exhaustive lifecycle matrix, full H4 catalog protocol or
full H5 reference closure cannot interrupt V0–V5.

After V5, resume Task99 as visible vertical slices in this order: Inventory,
Crest/Tools, Tasks, Journal, Map, then overlays. For each slice: direct resident
object reuse, focused host regression, exact compile, one lower-display capture,
and correction before moving to the next. Existing host-heavy implementations
may be simplified or replaced when they do not produce the required visible
result.

### Task99: functional HUD and native companion pages

Execute two coherent internal batches, with focused behavioral RED/GREEN,
exact cached two-game compiles, independent SPEC then QUALITY review, and fresh
main verification. These are host boundaries, not per-module device gates.

- [ ] **Batch A — live HUD and frame ownership.** Create
  `tools/silksong-patches/src/dualscreen/DsPortHud.cs` and the Unity-independent
  production routing implementation `DsPortHudState.cs`; link that actual code
  into `tools/shared-patches-tests/SharedPatches.Tests.csproj` and test through
  `SilksongHudRoutingTests.cs`, faking only node access. Extend `DsResidentUi.cs`
  with unique typed semantic-root discovery from `GameCameras.SilentInstance`,
  `HUDCamera.GameplayChild`, `hudCanvasSlideOut`, `silkSpool`, health, Money/Shard
  `CurrencyCounter`, `BindOrbHudFrame` and `ToolHudIcon` owners. Verify the exact
  minimal roots and native transition ordering from cached managed source.
  Missing/ambiguous essential roots must move nothing and retry on valid
  residency, including replacement within the same scene.
- [ ] Route the same native objects into health/Silk/currency/loadout slots;
  capture original parent, sibling, moved-root local transform and changed node
  layers before mutation. Reassert after native updates and adopt new children
  without recapturing companion state as vanilla. Restore only these routing
  properties, never child animation, color, active flags or native drivers.
  Native HUD belongs to `DsPortLayers.HUD`, not disposable static frame chrome.
- [ ] Expose slot geometry/readiness/revision from `DsPortFrame.cs` and notify
  before every `DestroyComposition()` path. Integrate ownership into
  `DsPortRuntime.cs`; restore before frame invalidation, hiding roots and
  disposal. Forward `LateUpdate()` from `DualScreenV2.cs`. Establish a typed
  pre-transition restore boundary, not merely `sceneUnloaded`. Pause, native
  inventory, full-off and display loss restore primary HUD; companion-page
  switching/toggling does not. Never use static `NormalizeRenderers()` on live HUD.
- [ ] Execute routing tests for identity/native-state preservation, each restore
  gate, page-only toggles, spawned-child layers, driver reparenting, same-scene
  replacement, missing/duplicate roots and retry, restoration before frame
  destruction, idempotence and surviving objects after child destruction.
- [ ] **Batch B — native pages, selection and overlays.** Add
  `DsPortInventory.cs`, `DsPortLoadout.cs`, `DsPortProgress.cs`, `DsPortMap.cs`,
  `DsPortSelect.cs` and `DsPortOverlays.cs` in the same dualscreen directory.
  Reuse `DsGameArt` discovery and native manager open/settle/selective-freeze
  knowledge, not Widget/Piece reconstruction or a generic grid. Do not invoke
  `PlayerDataTestResponse.Evaluate` on live sources without proving UnityEvent
  effects. Reuse Crest/Tool reads but establish legal native action dispatch
  and native glyph/verb producers; browsing must not equip items. Reuse
  `DsMapView` source/map/compass knowledge, not its render-texture wrapper;
  restore primary-map state in `finally` and yield to a native primary map.
  Map direct-clone side effects and dialogue/tutorial/title/item/fade owners
  require bounded game-specific source checks. Do not apply static-frame
  sanitization to functional pages/overlays.
- [ ] Test interrupted host switching, stale selection after source replacement,
  unavailable actions, no equip from browsing, action-time legality, failed map
  setup restoration and overlay visibility/ownership. Consume each gesture once
  in overlay, Mods, tabs, selected-page precedence. Preserve dormant prototypes;
  historical deletion directions are not current cleanup permission.

### Task99 Batch B partial continuation checkpoint

Batch B and parent97 remain **OPEN**. This checkpoint does not populate any
native page and is not SPEC/QUALITY/main acceptance. Its source/evidence boundary
is `tools/shared-patches-tests/obj/task99-batchb-20260908T190000Z/verification.json`.

- [x] Implement the reachable `DsPortOverlays` native fade consumer and its
  overlay-first gesture path, with the concrete future Task100 Mods callback
  followed by cached frame tabs. The primary `ScreenFaderState` instance and
  renderer remain untouched; the companion follows the current native sprite,
  shared material, tint and visibility, and clears when unavailable. Native
  material/Canvas rendering is Unity-unproved. The selected-page consumer is
  still absent.
- [x] Implement the bounded scenery consumer behind the HUD using only the
  current native `LightBlurredBackground` post-`LightBlur.OnRenderImage` output.
  Require active/supported native blur/material and the background-only plane
  slice; clear stale owner/texture/visibility output, wait a later frame, use
  one-sixth/bounded bilinear output and 0.08 brightness, and restore the active
  render target in `finally`. Never activate native producers or substitute a
  gameplay-camera mirror. Actual ordinary-gameplay availability and blur pixels
  remain Unity/GPU-unproved; native `BlurManager` source provides the gameplay
  shader-quality enablement, not a runtime observation.
- [x] Execute meaningful selection/fade/scenery RED/GREEN and the exact retained
  regression filter: 224/224 (187 retained, 14 selection, 23 overlay/scenery).
  `DsPortSelectState` is selection-core evidence only until the native adapter is
  connected. Both cached compiles pass, with 57 Silksong sources/29 references;
  accepted HUD state/tests and the shared direct-display engine are preserved.
  The material-corrected rerun preserves all 224 case identities. Source/docs
  contracts pass 50 with the one explicit legacy restore/delete-harness exclusion;
  the initial Image-ban failure and material-binding RED remain recorded. Only
  one native fade Image allocation is admitted, with six mutated-source and
  frame/HUD/page negative controls retaining the authored-replacement ban.
  Silksong's 7 and Hollow Knight's 1 existing compiler warnings remain recorded;
  all ten registered entrypoint signatures/phases are checked in compiled IL.
- [ ] **T99B-PAGE — next concrete implementation:** create the smallest complete
  reachable native Journal page in `DsPortProgress.cs`, followed by Inventory
  and Tasks in the approved modules. Already-read native authority is
  `JournalItemManager.UpdateList`/`GetGridSections`/`SetDisplay`,
  `JournalEntryItem.Setup`, `InventoryItemListManager.SetupGrid`,
  `InventoryItemGrid.Setup`, `InventoryCursor`, `InventoryPaneInput`, and
  `ScrollView`. Do not call `PaneStart`, `GetStartSelectable`, or item `Select`
  merely to browse. Preflight actual resident component types, the external
  cursor/template prefab dependencies, and serialized callback targets before
  activation; preserve native visual/open/settle behavior and selectively adapt
  input/scroll. `ScrollView`'s unscaled world-space bounds invalidate naive
  parent-scale fitting. Start RED with rejection-before-activation of an
  external/unknown callback or template, interrupted/inactive-host settle, and
  no seen/new writes from display/clear. A rejected shape must name its exact
  component/reference and retry on a real residency/source event, not become a
  generic replacement page or a falsely completed capability.
- [ ] **T99B-ACTION:** native Loadout/selection/details/cursor/glyph/verb integration
  and explicit legal native equip dispatch with current owner/item/data/crest/
  slot and native legal conditions rechecked synchronously. Native manual
  `InventoryToolCrestSlot.SetEquipped` → `SaveEquips` is a dispatch relation,
  not permission to bypass its caller's legality checks.
- [ ] **T99B-MAP:** native map hierarchy/setup/compass/markers/room/zone updates,
  secondary gestures, primary-map yield and complete failed-setup `finally`
  restoration. Resolve shared pin registration/clear in native `GameMap` before
  any ordinary clone activation/destruction; do not invent bench teleport.
- [ ] **T99B-OVERLAY:** same-instance dialogue/speaker/tutorial/focus/title/credits/
  item/lore ownership and pre-destructive restoration. `DialogueBox._instance`
  and the `NpcDialogueTitle` → `AreaTitle` relation are source-established, not
  implemented routing. Native fade reproduction does not close every overlay
  family or native death/cutscene state.
- [ ] **T99B-VISUAL:** finish actual page fit/sorting/clipping below the HUD band,
  selected-page input reachability, independent SPEC then QUALITY and fresh main
  verification. Preserve all unpassed Unity/native/GPU and device observations.

The matching open-ID acceptance conditions are in
`docs/verification/dual-souls-ui-port-matrix.md`. Failed lookups, interrupted
uncredited decompiler launches and unsuccessful cached prefab metadata reads
remain evidence, not passes. No native page/action gap moves to Task100 or
Task101; those remain separate Mods/skins assignments. No device, packaging,
publication, cleanup or save/native-memory action occurred in this checkpoint.

### Task99 Batch B bounded consolidation and required continuation

**PARTIAL / NOT BATCH-B ACCEPTANCE. Task99 and parent97 remain OPEN.** The
preceding 224-case partial checkpoint is historical, not the current page state.
Native Journal, Inventory, Tasks, Loadout and Map adapters now occupy cached
DsPortFrame PageHosts, with selected-page input after overlays → Mods seam →
tabs, consumed once. Native browse/clear remains separate from actions and may
not change seen/new flags, progression, equipment or saves. Journal never calls
SetSelected/Select/PaneStart/PaneEnd/InstantScroll/GetStartSelectable/seen setters.
Accepted HUD/shared direct-display sources remain preserved.

Four reported defects are implementer-corrected only: selected-owned optional
ToolItem details no longer reject all Loadout items; native all-source crest
setup uses fresh blank presentation metadata rather than unsafe source asset
cloning; exact native floating-config/slot/callback authority enables manual
floating equip dispatch; Inventory optional detail exceptions now clear only
owned detail/selection while retaining list/retry. Exact source CrestData and
every slot/delegate/SaveEquips callback are verified before activation. Source
ToolCrest OnEnable/OnValidate version links are never copied or changed. Native
manual dispatch remains source-proven but Unity-unexecuted; no direct save setter
is introduced. These corrections do not implement still-rejected detail overrides
or establish required selected prefab graph coverage.

Map now has finite owned cold room/condition/layout/renderer-donor recipes and
mapped/visited membership freshness, native marker-template local hierarchy and
retained text-mesh access without native GameMap initialization, registry/cache
writes or placed-marker insertion. Truly ungenerated text still needs private
text/font/material closure. DialogueBox now routes the same exact native root
through an empty original-parent carrier, preserving native root pose/fade
ancestry, mapping/restoring exact clip coordinates and guarding native advance.
Nested finally restoration reaches HUD, Map and overlays before enclosing
retirement, with the existing release pump retaining failed restoration.

- [x] Run one bounded partial consolidation: 179/179 combined production-linked
  cases (Selection/Map 68, Overlay 27, Journal 42, HUD/frame/release 37,
  Collectable hooks 5); 66/66 allowed source contracts, with the exact legacy
  implicit-restore/unbounded/auto-delete frame harness excluded, not passed.
  Preserve the first 65-pass/one-error stale RestoreHud contract run and its
  narrow expectation correction. Both cached/no-restore builds execute real
  CoreCompile. Exact source delta, prior test identities, compiler input/output
  hashes, registered compiled entrypoints and PID timing records are bound by
  `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/partial-consolidated/verification.json`.
  Reuse pinned historical preservation; do not rescan old evidence or expand
  unrelated suites. This is an implementation checkpoint, not an acceptance gate.
- [x] Implement the finite existing-started `OpeningGameplayCredits` host slice:
  retain exact native owner/animator/camera and native pose/animation/progression;
  original-parent carrier maps world aspect, with parent/sibling/layer recovery
  registered before mutation and independent family restoration attempts. Provisional
  evidence: 37/37 linked overlay cases, 68/68 allowed source contracts and actual
  cached SS compile in `opening-credits-*-green` continuation outputs. Preserve
  REDs and the stale Dialogue literal failure; do not alter the frozen partial.
  Root motion/StateMachineBehaviours, unknown component/world-clip graphs and
  unsupported XY bases remain rejected. Ordinary serialized credits graph coverage
  is unavailable evidence, not demonstrated support or universal failure. No Unity
  lifecycle/animation/rendering acceptance; T99B-OVERLAY remains OPEN.
- [ ] **T99B-OVERLAY — remaining implementation:** exact native visual
  AreaTitle/speaker, tutorial, focus, title, other credits, item/lore and required
  overlay families. Prove each family's coordinate/lifecycle authority rather
  than applying the Dialogue carrier indiscriminately. Preserve same-instance
  native pose/drivers and partial-failure restoration. AreaTitle, not the NPC
  event-owner transform, owns speaker visuals. Tutorial has no proven callable
  staged dismissal yet; do not invent one or inject broad native confirm input.
- [ ] **T99B-MAP-TEXT:** finite born-inactive private native text donor; independently
  own mutable font/glyph/kerning/fallback/weight/material state, preserve exact
  characters/styles, avoid lazy global initialization and borrowed graph writes,
  and retire text/submeshes before owned fonts/materials. Use retained advisor
  source evidence; do not repeat discovery or claim cold support by polling.
- [ ] **T99B-DETAIL:** implement required exact Inventory/Loadout
  SetupExtraDescription overrides and selected-slot native condition/destination/
  template binding. Current DeclaringType != SavedItem rejection is still an
  implementation gap even though optional failure is local.
- [ ] **T99B-CREST-GRAPH / T99B-INVENTORY:** admit required selected native custom
  component/reference/lifecycle graphs and privately owned materials. Unknown
  selected crest visuals hide that subtree and block its actions, but retain
  native tool-list/next-crest browsing; no substitute art. Missing actual serialized
  evidence does not prove every ordinary pane fails and cannot justify blind
  allowlist growth.
- [x] Implement the finite Tasks selected custom-counter path using exact native
  regular/main/subquest template condition, prefab and mapped destination; retain
  native base details, privately own selected counter materials, never touch its
  global prefab cache or clone quest assets. Preserve local unsupported-detail
  retry. This does not establish required ordinary custom graph coverage.
- [x] Implement owner-stable native content snapshots and real page rebuild wiring
  for Journal/Tasks/Inventory/Loadout. Explicit identity/value reads detect
  same-count membership and value changes; stable/empty pages poll at 125 ms,
  each snapshot rejects rather than truncates over 65,536 input values, and
  gestures/actions recapture without cadence caching. Getter/bound failures clear
  noninteractively and remain retryable. Actual rebuilds reenter born-inactive,
  hidden unit-world native layout and later settling; reentrant refresh cancels
  stale selection/generation and waits for native callback unwind. All four
  adapters retain failed cleanup with separate Released/Destroyed latches.
  Current provisional evidence: 218/218 linked cases (all prior 203 retained),
  74/74 allowed source contracts, actual cached SS CoreCompile in
  `page-snapshot-final-*-green`; REDs and frozen partial evidence remain unchanged.
  See the matrix continuation for exact source scope, DLL hash and lifecycle limits.
- [ ] **T99B-PAGE / T99B-TASKS / T99B-ACTION:** required ordinary native/custom
  graph coverage, required locked-slot action semantics and full native
  prompts/action equivalence. Finite named snapshot inputs do not establish
  arbitrary unknown custom-script freshness or Unity lifecycle acceptance.
  Test actual linked boundaries, not copied recipes.
- [ ] **T99B-MAP-PRESENT / T99B-VISUAL:** native camera callbacks/pre-existing buffers,
  dynamic geometry/material/condition freshness, normal graph/sorting/clipping;
  Unity lifecycle, native action execution and visual parity remain unproved by
  host tests/compiles.

Continue this remaining coherent Batch B scope without awaiting user approval.
No new independent acceptance stage starts at this partial checkpoint; main may
audit handoff integrity only. Eventual independent SPEC, separate QUALITY and
fresh main verification remain required. Task100 Mods and Task101 skins stay
separate. The matching current table/open-ID closure conditions are in
`docs/verification/dual-souls-ui-port-matrix.md`; historical evidence and all
operational restrictions below remain unchanged.

### Task99 current owned-text checkpoint — remaining Batch B order

Historical sections/receipts above remain intact. **Task99/parent97 are OPEN**;
this is an implementation checkpoint, not full Batch B or independent acceptance.
- [x] Implement exact selected-slot/extra-description binding and owned native
  text/font/material closure across Journal, Inventory, Loadout, Tasks and their
  auxiliaries/details, plus genuinely cold Map text. Preserve native per-text
  decoding/tag/weight and ordered fallback authority, shared graph identities,
  resource guards and text/root-before-font retryable retirement.
- [x] Verify **243/243** combined linked cases, **81/81** allowed source contracts
  and current actual cached SS CoreCompile. Receipts under
  `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/` are
  `owned-native-text-combined-final/completion.json`,
  `owned-native-text-contracts-final/completion.json`, and
  `owned-native-text-current-compile-final/completion.json`;
  DLL SHA-256 `5651eb35f70a70c0eed0d4943a0c75a882ccce134ac5a39313cc7a6664c44255`.
  All exited 0 without timeout. Preserve failures (including the removed
  experimental HarmonyLib hook), frozen partial receipts and excluded-harness
  no-credit status. No automatic native parser interception, Unity lifecycle,
  native actions, rendering or ordinary/custom prefab-graph acceptance is proved.
- [ ] **Next T99B-OVERLAY:** exact AreaTitle speaker/title visual authority, then
  tutorial/focus/item/lore, using existing native instances/drivers and ownership/
  restore machinery with source-backed lifecycle, coordinates and clipping.
  Never route `NpcDialogueTitle.transform` as the speaker visual. Preserve native
  animation/fade/progression and restore only adapter-owned routing before teardown.
  Tutorial coroutine owns dismissal; use only a source-proven legal callable
  endpoint if one exists, otherwise record its exact interaction gap separately.
  Each concrete family requires reachable wiring, focused RED/GREEN and bounded
  cached compile; preserve Dialogue/OpeningCredits/HUD regression.
- [ ] Then reconcile **T99B-ACTION** locked slots/full native prompts/actions and
  **T99B-MAP-PRESENT** dynamic freshness/sorting; unknown ordinary prefab evidence
  does not excuse missing host-fixable bindings. Do not advance to Task100 yet.
- [ ] Complete full Batch B before independent SPEC, separate QUALITY and fresh
  main verification. Continue automatically unless a real blocker is reported.

The matrix records the detailed current outcomes and limitations. Accepted
HUD/shared sources remain untouched. No nested agents, restores, config/git
mutation, device/publication/source activation, save edits or cleanup; commands
<=180s, every child wait <=175s with retained PID/timing/exit/timeout records.

### Task99 AreaTitle/tutorial continuation checkpoint

- [x] Exact AreaTitle visual/FSM/local-coordinate routing and same-instance
  ToolTutorialMsg running-registration routing now reach overlay tick, gestures
  and independent restoration. Native pose/animation/fade/progression remain native.
- [x] Preserved receipts under the preceding checkpoint directory:
  `area-title-focused-green` **47/47**, `area-title-contracts-green` **82/82**,
  `area-title-current-compile-green` actual SS CoreCompile;
  `tutorial-focused-green` **52/52**, `tutorial-contracts-green` **83/83**,
  `tutorial-current-compile-green` actual SS CoreCompile. All exited 0/no timeout;
  hashes and precise boundaries are in the existing matrix. Parent-after-write
  behavioral RED was fixed; helper-absence REDs retain their narrower meaning.
- [ ] **T99B-OVERLAY-TUTORIAL-INPUT:** native coroutine has no source-proven
  callable staged companion dismissal. Preserve native skip and page fencing;
  closure requires a legal endpoint with tests or explicit gap acceptance, not
  invented confirm/progression writes. Visual routing does not close interaction.
- [ ] Continue PowerUpGetMsg/EvaHeal, item/lore, then Action/Map host gaps above.
  Full combined regression and native graph/runtime acceptance are not claimed.
  Task99/parent97 remain OPEN; full implementation precedes SPEC/QUALITY/main.

### Task99 PowerUp/item/lore current host checkpoint

- [x] Exact registered PowerUpGetMsg/EvaHeal native visual sequence; preserve
  native selected prompts and the existing native-only dismissal gap.
- [x] CollectableUIMsg child visual islands preserve native root stack/spawn-limit
  and coroutine/sorting authority. Actual root-layout targets remain fixed;
  parent rect/pivot and moving-anchor mapping are checked on the owned carrier.
- [x] Exact backing MemoryMsgBox/NeedolinMsgBox singleton routes retain native
  text selection, proximity, fade/animation and complete hide continuation.
- [x] Preserved focused/contracts/current actual SS compile checkpoints under
  `tools/shared-patches-tests/obj/task99-journal-continuation-20260908/`:
  `powerup-*-green` **57/84**, `item-*-green` **62/85**, `lore-*-green` **69/86**;
  the existing matrix records exact directories/hashes and evidence limits.
- [x] Combined overlay checkpoint **275/275** with all original **243** exact
  `(testId, testName)` identities retained and exactly **32** new cases;
  **86/86** allowed contracts and current actual SS compile. Receipts are
  `overlay-families-combined-green`, `overlay-families-case-identities`,
  `overlay-families-contracts-green`, `overlay-families-current-compile-green`;
  current DLL SHA-256 `c0bedefac32443bb458f77b549705c26a7b272680776fe6f6ab0e62c7ca2a6d3`.
- [ ] Remaining locked-slot/native prompt/action and Map freshness/sorting work.
  Fixed-target/cross-island graph restrictions remain explicit; no ordinary Unity
  graph/runtime equivalence, full Task99 handoff or independent acceptance claimed.

### Tasks100–102: reachable Mods, shared skins and combined host acceptance

- [x] **Task100.** Reuse shared `tools/shared-patches/src/Mods/` controller,
  persistence and presenter logic. Extract `TweakSession.cs` from
  `HollowKnightModsSession.cs` only with its actual second consumer; retain
  guarded initialization/restoration and the existing 60-ready-tick retry.
  Add process-owned `SilksongModsRuntime.cs` under the Silksong `src/mods/`
  directory and reachable `DsPortMods.cs` under `src/dualscreen/`, using the
  frame's ModsAnchor and HK native-style gear/modal design. Start the runtime
  before the display-enabled early return; presenter detach must not reset
  settings. Existing typed SS bindings cover invincibility, unlimited Silk,
  nail damage and equip-anywhere. Additional unbound capabilities remain
  explicitly unavailable, not claimed as full Mods parity. Test default-off,
  baseline restoration/reset, initialization retry, rebuild/hotplug ownership,
  unavailable actions and HK/SS persistence isolation.
  **Host-complete/device-deferred at `c3f61f5b3ac0b212a31a424e9ea556f8683cde3d`.**
  Shared lifecycle, exact four typed bindings, startup reachability, native-resident
  `DsPortFrame` presentation, consumer detach, restoration retry/owner replacement,
  and no production `DsShell` route are source/host proven. Executable regressions
  prove that initial capture performs no invalid restore, failed HK teardown retains
  its exact process owner and blocks replacement, permanent apply rejection commits
  a recoverable disabled state after restoration, sustained restore failures retain
  bounded diagnostics, and unchanged Mods model/layout stamps skip TMP repaint while
  all relevant changes invalidate it. Exact evidence and the device boundary are in
  `docs/verification/evidence/task100-quality-c3f61f5/completion.json`; this
  supersedes `task100-spec-fix-0c422bc`.
- [x] **Task101.** Share existing bounded Kotlin import/library/UI algorithms
  through a closed immutable HK/SS catalog/profile definition. Thread it through
  catalog, library codec/store, object builder, canonical manifest, tree verifier,
  quota, UI services and runtime bridge; retain immutable GameProcessStartup
  authority. SS never invokes HK legacy migration. Extract managed session,
  library-controller/polling and occurrence/ack/stability mechanics only when
  SS consumes them, retaining original identity, rollback, accounting and
  retirement algorithms. Add SS target/policy/runtime/death adapters under
  `tools/silksong-patches/src/skins/runtime/`. Establish actual SS writable
  texture bindings/catalog and normal-death/stable-respawn event ordering;
  neither HK filenames nor its death exclusions are SS evidence. Preserve
  ON across all eleven supported targets, ROTATE across only the nine character
  collections, the two persistent HUD collections ON-only and excluded from
  ROTATE, and OFF original restoration.
  Test foreign profiles/catalogs, identical pack IDs across games, startup-selection
  changes, selected/pending protection, mode changes, duplicate/hazard events,
  prompt matching acknowledgments, next death before polling and owner replacement.
  **Host-complete/device-deferred at `a701d33`, superseding `bcf3508`.** Exact SS
  1.0.29980 managed input SHA-256
  `1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d`
  is pinned by an exact post-guard/post-normalization classifier and a canonical,
  byte-idempotent rewrite with SHA-256
  `86e8ffd402bb5e58c57d89ef2e0c3fa8dd3e89a9c1c049d4bf663484434b6a8e`.
  BundleSurgery verifies that exact SHA and canonical bridge IL before publishing
  every fresh output. The launcher independently checks the pinned rewritten SHA
  and invokes canonical verification before replacing its staged assembly.
  Original drift and unrelated drift after the bridge are rejected without output.
  Closed eleven-target profile/storage/import/JNI authority, exact multi-instance
  collection/material admission, all sparse omissions, atomic apply/restore,
  retry-owned teardown, a 32-slot ordered hero/manager/token occurrence ring,
  stale-occurrence-only cancellation, restore-before-frozen-successor rotation,
  two consecutive stable PLAYING frames, and profile-aware launcher output are
  executable-host tested. Counter jumps preserve distinct owners, H1 replacement
  cancels only H1 while H2 remains queued, and bounded overflow fails closed.
  Evidence is retained in
  `docs/verification/evidence/task101-spec-fix-a701d33/`. Android bundle
  identity/parity, sampled material appearance, Scythe/Shaman shaders,
  crest/fallback visuals, HUD effects, resource pressure, death/respawn visuals
  and lifecycle, and persistence interaction remain genuine device-only deferrals.
- [ ] **Task102.** Verify production reachability, focused behavioral suites,
  exact two-game compiles and cross-game isolation, with independent SPEC,
  separate QUALITY and fresh main verification. Distinguish executed host
  behavior, static wiring/compile evidence, unsupported bindings and unproved
  Unity/GPU/JNI/native lifecycle/rendering/artist-pack/device acceptance. The
  parent Silksong milestone remains open until this host coverage is accepted.

Extract proven game-neutral logic when the second consumer needs it, with
focused Hollow Knight regression. No generalized receipt/lease/transaction
framework or separate H6 abstraction project is a prerequisite. Missing native
bindings remain assigned to Tasks99–101 with explicit acceptance conditions;
they neither block unrelated host work nor count as implemented behavior.

### Current operational boundary

The V3 exact-commit fork push, dry-run persistent-identity signing candidate and
V3–V4 integrated Android gate are required by the 2026-09-10 recovery override;
they are not prohibited by the older host-only boundary. Before any new ADB or
physical-device action, obtain a thread/session-specific device lease and wait
for the user to prepare the device. No unrelated device work, public release,
merge, cleanup or upstream-visible action is authorized. Commits and pushes go
only to the user's `fork`; never push, open a PR, create an issue, comment or
otherwise interact with `igawa6/dualsouls`.

No native addresses, IL2CPP offsets, process scanning, injected `PlayerData`
fields or save edits. Local source reading/adaptation is allowed with
provenance/notices preserved. Preserve old cores, goldens and evidence. Earlier
broader authorization text is historical, not permission.

Use offline cached inputs, no downloads or restores, commands at most 180 seconds
and each build/test subprocess wait at most 175 seconds. Use task-owned fresh
output/cache/temp locations, preserve timeout records and never credit late
success. Do not kill global Java/.NET processes or inspect game processes. Do not
run the existing Silksong check script's implicit restore; use its exact source
and entrypoint contract with cached restore assets and `--no-restore`. Android
host-test resources/fixtures remain the sole packaging-task exception; no APK
assemble/sign/stage/install/deploy. Exclude preBuild, stageBuildScript,
stageBundleSurgery, stagePatches, stageHollowKnightPatches, stageIo,
stageModWeaver, stageBepInExShim and fetchMonoRuntime. No project/global
configuration changes beyond scoped source/test wiring. Delegates spawn no
subagents; use completion events plus session-only 17-minute checkpoints.
