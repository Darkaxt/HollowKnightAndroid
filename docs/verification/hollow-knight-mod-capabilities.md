# Hollow Knight Mods capability ledger

This ledger is authoritative for the gameplay, progression, world, economy, and state capabilities visible in the initial Hollow Knight Mods catalog. Every row remains visible but unavailable until its named acceptance condition is proven.

The paired [managed API audit](hollow-knight-mod-managed-api-audit.md) preserves the exact Hollow Knight `1.5.12620` `Assembly-CSharp.dll` hash, size, inspection-tool version, bounded type inventory, observed signatures, semantic findings, and promotion decisions. That assembly/API provenance remains the source audit for this ledger; host scaffolding does not supersede it.

## H3 host checkpoint

H3 remains **HOST-COMPLETE / TRACKED-DEFERRALS** at the bounded host gate. The earlier process-owned policy boundary was `ba32812`; the current authority/state-core checkpoint is `fix: unify Hollow Knight flash authority`. Its immutable decision keeps master `soft`/`vanilla`/`off` and the process Soft alpha independent of live display config. Executable host tests now own the per-renderer transition and final reconciliation behavior.

Fresh host evidence at that boundary:

- Focused Mods/state-core tests: 89 passed, 0 failed, 0 skipped.
- Complete shared suite: 143 passed, 0 failed, 0 skipped.
- Hollow Knight reference/source contracts: 47 passed; combined source/docs contracts: 53 passed.
- Exact Hollow Knight `1.5.12620` compile: 0 errors, 1 pre-existing `CS0649` warning, 2 entry points, 271,360-byte DLL.
- Exact Silksong compile: 53 sources, 10 entry points.
- Production scan for `DllImport`, `ReadProcessMemory`, `WriteProcessMemory`, `HKTW_`, `HKTweaks`, `HKModsMenu`, and `Bottom.Tweaks`: no matches.
- Final Git diff/status clean and branch synchronized with the fork at checkpoint publication.

The host-complete scope is the Hollow Knight capability catalog, controller, process runtime, and existing-layout modal. The reversible **Companion backdrop** (`dimmed`/`black`) and **Lifeblood flash** (`soft`/`vanilla`/`off`) capabilities are available presentation catalog rows outside `HKMOD-001` through `HKMOD-018`. Lifeblood renderer discovery, enforcement, and restoration are process-policy-owned. That policy restores tracked state on no-owner release (including master-off only when no legacy owner remains), transition to Vanilla as needed, camera replacement, or runtime disposal. Master-off with a live legacy publisher transfers authority to the legacy policy and continues enforcement. `HKDualScreen` only publishes and clears its nullable live H2 fallback; direct-display or presenter teardown cannot restore/dispose the process policy. These capabilities do not promote any row below.

Every `HKMOD-001` through `HKMOD-018` row remains **DEFERRED**, visible but disabled, with target **Final managed-rewrite remediation** and its existing acceptance condition unchanged. **HOST-COMPLETE / TRACKED-DEFERRALS** therefore does not mean full Mods parity or completed gameplay Mods effects, and `Pending host evidence` is not replaced merely because the scaffolding is green.

No Android, device, emulator, ADB, signing, live, or UI acceptance testing occurred, and H3 triggers no signed build. H4 follows on the same branch. The complete H3/H4 Hollow Knight slice receives one future device gate only when explicitly authorized; no micro-candidate is built or signed.

| ID | Capability | State | Missing seam/proof | Target | Acceptance condition | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| HKMOD-001 | Damage received | DEFERRED | Stock damage-mode symbols exist, but PreventDeath semantics and scene-safe baseline ownership are unproved | Final managed-rewrite remediation | Exact compile; fake mapping; death/respawn/scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-002 | Nail damage | DEFERRED | No progression-safe authority owns smith-upgrade recomputation while active | Final managed-rewrite remediation | Exact compile; upgrade-aware recomputation; master-off/reset/save rollback; integrated candidate | Pending host evidence |
| HKMOD-003 | One-hit kills | DEFERRED | No enemy-only typed hit interception proves boss/script controls or rollback | Final managed-rewrite remediation | Exact compile; enemy-only typed hit contract; boss/script controls; rollback; integrated candidate | Pending host evidence |
| HKMOD-004 | Run speed | DEFERRED | Public movement fields exist, but hero replacement and scene maintenance are unproved | Final managed-rewrite remediation | Exact compile; hero reacquisition; transition/death baseline restore; integrated candidate | Pending host evidence |
| HKMOD-005 | Unlimited Soul | DEFERRED | Managed Soul methods exist, but live resource ownership and stale-baseline restoration are unproved | Final managed-rewrite remediation | Exact compile; focus/spell/death/scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-006 | Charm costs | DEFERRED | Public cost fields exist, but an all-cost snapshot and equipped/save lifecycle are unproved | Final managed-rewrite remediation | Exact compile; all-cost snapshot; equip/reload/reset rollback; integrated candidate | Pending host evidence |
| HKMOD-007 | Unlimited notches | DEFERRED | Overcharm/notch invariants and legal unequip behavior are unproved | Final managed-rewrite remediation | Exact compile; overcharm/equip/save lifecycle; exact restore; integrated candidate | Pending host evidence |
| HKMOD-008 | Equip anywhere | DEFERRED | No safe managed inventory/bench legality seam is proven | Final managed-rewrite remediation | Exact compile; legal inventory FSM actions; bench/scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-009 | Geo multiplier | DEFERRED | No award-source interception distinguishes new Geo from an existing balance | Final managed-rewrite remediation | Exact compile; pickup/reward-source tests; save rollback; integrated candidate | Pending host evidence |
| HKMOD-010 | Keep Geo on death | DEFERRED | Shade/death-pool ownership and duplicate-award prevention are unproved | Final managed-rewrite remediation | Exact compile; death/shade/respawn/save rollback; integrated candidate | Pending host evidence |
| HKMOD-011 | Journal one kill | DEFERRED | Journal progression writes and completion-event idempotence are unproved | Final managed-rewrite remediation | Exact compile; synthetic journal progression; reload rollback; integrated candidate | Pending host evidence |
| HKMOD-012 | Auto map | DEFERRED | No bounded area-only reveal seam proves progression safety | Final managed-rewrite remediation | Exact compile; area-only reveal contract; scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-013 | Health bars | DEFERRED | No managed renderer/event lifecycle is proven for spawned and pooled enemies | Final managed-rewrite remediation | Exact compile; spawned/pooled enemy renderer/event lifecycle; boss controls; integrated candidate | Pending host evidence |
| HKMOD-014 | Damage numbers | DEFERRED | No typed authoritative dealt-damage event covers all attack sources | Final managed-rewrite remediation | Exact compile; attack-source matrix; pooled UI teardown; integrated candidate | Pending host evidence |
| HKMOD-015 | Boss retry | DEFERRED | Boss-scene reset and save-checkpoint semantics are unproved | Final managed-rewrite remediation | Exact compile; synthetic boss transition/death/save rollback; integrated candidate | Pending host evidence |
| HKMOD-016 | Secret radar | DEFERRED | Secret identity, range, and non-progression presentation authority are unproved | Final managed-rewrite remediation | Exact compile; multi-scene discovery contract; no save delta; integrated candidate | Pending host evidence |
| HKMOD-017 | Bench teleport | DEFERRED | Recorded-bench validation and safe transition rollback are unproved | Final managed-rewrite remediation | Exact compile; recorded-only destination; transition failure/save rollback; integrated candidate | Pending host evidence |
| HKMOD-018 | State slots | DEFERRED | A versioned checksummed transactional snapshot format and rollback authority are absent | Final managed-rewrite remediation | Exact compile; checksummed disposable fixtures; atomic restore/failure rollback; integrated candidate | Pending host evidence |
