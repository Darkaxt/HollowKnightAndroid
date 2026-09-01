# Hollow Knight Mods Capability Catalog Design

**Date:** 2026-09-01  
**Status:** Approved for specification  
**Target:** Hollow Knight `1.5.12620` in the unified Android fork

## Purpose

Add Hollow Knight's built-in Mods experience to the accepted direct-display companion without copying additional upstream modules. The implementation uses fork-owned catalog, controller, persistence, UI, and typed game adapters. It preserves the existing Map/Inventory/Charms layout and keeps every unavailable catalog capability visible and traceable until the final parity-remediation stage.

This design implements the authorized sequence after the host-complete H2 companion. It does not authorize Android-device checks, signed candidates, release work, or interaction with `igawa6/dualsouls` during H3 development.

## Goals

1. Provide the Mods gear and modal through the existing Hollow Knight companion context box.
2. Represent the complete Hollow Knight tweak catalog truthfully.
3. Implement every capability that can be proven against stock typed `1.5.12620` managed APIs with exact baseline restoration.
4. Show unsafe or unproved rows as disabled with a reason and tracked ledger ID.
5. Persist master and selections outside saves under the Hollow Knight profile namespace.
6. Keep enabled tweaks active independently of display-1 availability.
7. Fail closed on mutation, persistence, or maintenance errors.
8. Preserve a reachable final-stage target and acceptance condition for every deferred row.

## Non-goals

- No copied `HKTweaks`, `HKModsMenu`, or `Bottom.Tweaks` implementation.
- No upstream issue, pull request, comment, or other interaction.
- No native addresses, IL2CPP offsets, process scanning, or memory tables.
- No injected `PlayerData` fields in the initial H3 batch.
- No save editing, state slots, forced scene transitions, bench warps, unlock mutations, or progression writes without their later safety gates.
- No fourth companion tab, launcher-owned Mods screen, or visual redesign.
- No skin implementation; skins and death/respawn rotation remain H4.
- No H3-only device candidate. H3 and H4 share one later integrated Hollow Knight candidate.

## Existing authority

The shared behavior remains governed by TWEAK-01 through TWEAK-07 in `2026-08-29-unified-hollow-knight-platform-design.md`:

- shared menu interaction;
- typed adapters;
- first-run master off and exact reversion;
- profile-local persistence;
- truthful capability availability;
- semantic cross-game parity;
- save and lifecycle safety.

This design narrows those requirements into an independently authored Hollow Knight implementation.

## Architecture

### `HollowKnightModsRuntime`

A process-resident runtime is created by the existing Hollow Knight bootstrap, not by display readiness. It owns:

- profile-qualified store creation;
- game-readiness observation;
- one `TweakController` instance;
- lazy baseline capture before first apply;
- persisted-master restoration;
- periodic typed maintenance;
- the latest visible error;
- process-lifetime disposal.

Display loss or companion disable detaches the presenter but does not dispose the runtime or restore active tweaks. Master off, reset, a real apply failure, a persistence failure, or process teardown owns restoration.

### `HollowKnightTweakAdapter`

The adapter implements `ITweakAdapter` with `GameId = "hollow-knight"`. It owns the catalog and maps available descriptor values to `IHollowKnightTweakApi` operations. It contains no Unity UI, persistence code, source discovery, or fallback memory mutation.

The adapter captures a baseline once for every available capability before applying any selected value. Restoration is complete and deterministic; it never assumes hard-coded vanilla values.

### `IHollowKnightTweakApi`

This interface is the testable game boundary. It exposes only capability-shaped operations required by descriptors. Fake implementations drive host tests.

### `HollowKnightGameTweakApi`

This is the only production class allowed to call exact Hollow Knight managed APIs. Every member must compile against the selected `1.5.12620` depot. A capability cannot become available merely because a similarly named field or method exists; the implementation must also prove legal-state guards, baseline capture, and reversion.

### `PlayerPrefsTweakStore`

The existing Unity `PlayerPrefs` store pattern moves into shared code so both games use the same implementation. `TweakController` remains the namespace authority and qualifies all keys as:

```text
dualsouls.mods.hollow-knight.master
dualsouls.mods.hollow-knight.value.<capability-id>
```

Every accepted change flushes before returning success. Saves and save-slot identifiers never participate in tweak persistence.

### `TweakMenuModel`

A pure shared menu model owns TWEAK-01 interaction state independently of Unity rendering:

- stable group order;
- selected group and row;
- a bounded visible-row window with selection-following scroll;
- master, cycle, reset, and close actions;
- disabled-row action rejection;
- description/error selection;
- prior-page preservation.

The model consumes `TweakController` and descriptors but never calls a game API directly. It is host-tested and is the later Silksong menu authority as well.

### `HollowKnightModsPresenter`

The presenter uses the existing gear/context-box seam. It owns only rendering and touch-to-model translation:

- gear visibility and hit testing;
- master row;
- grouped capability rows;
- value cycling for available rows;
- disabled presentation for deferred rows;
- description, error, and deferral text;
- reset action;
- modal object lifecycle and teardown.

`HkStageHooks` delegates to the runtime and presenter. It does not become a second controller or game adapter. Map, Inventory, and Charms remain the only tabs. Opening Mods does not change the remembered page; a normal tab tap closes the modal.

## Capability model

`TweakDescriptor` gains backward-compatible availability metadata:

- `IsAvailable`;
- `UnavailableReason`;
- `TrackingId`.

Existing available descriptors continue to use the current constructor. A second constructor or factory creates unavailable descriptors and assigns one immutable storage default, `OFF`; the presenter ignores that storage value and renders `DEFERRED`. An unavailable descriptor must have a non-empty reason and tracking ID. Promotion replaces the descriptor with its exact active value set only after the capability gate passes.

The shared controller enforces the boundary:

- initialization may read an unavailable row only to replace invalid/stale stored values with its default;
- unavailable rows cannot cycle or apply;
- unavailable rows are skipped during persisted-master application;
- reset writes only defaults;
- corrupt preferences cannot promote a row;
- the menu can query the reason and tracking ID directly.

At runtime, every row is exactly `AVAILABLE` or `DEFERRED`. There is no experimental or best-effort state.

## Fork-owned catalog

The following IDs define semantic parity without copying another implementation. The two presentation rows have an existing reversible H2 seam. Every gameplay/progression row begins deferred and may be promoted during H3 only after meeting the promotion gate below.

| ID | Group | Capability | Initial state | Tracking ID |
| --- | --- | --- | --- | --- |
| `companion_backdrop` | Presentation | Dimmed or black lower-screen scenery backdrop | `AVAILABLE` | — |
| `lifeblood_flash` | Presentation | Vanilla, softened, or disabled lower-screen lifeblood flash | `AVAILABLE` | — |
| `damage_received` | Combat | Vanilla, prevent-death, or invincible damage behavior | `DEFERRED` | `HKMOD-001` |
| `nail_damage` | Combat | Reversible nail-damage multiplier | `DEFERRED` | `HKMOD-002` |
| `one_hit_kills` | Combat | One-hit enemy damage behavior | `DEFERRED` | `HKMOD-003` |
| `run_speed` | Player | Reversible movement-speed selection | `DEFERRED` | `HKMOD-004` |
| `unlimited_soul` | Player | Maintain Soul through the game's own managed path | `DEFERRED` | `HKMOD-005` |
| `charm_costs` | Charms | Reversible charm-cost selection | `DEFERRED` | `HKMOD-006` |
| `unlimited_notches` | Charms | Remove notch exhaustion without corrupting equipped state | `DEFERRED` | `HKMOD-007` |
| `equip_anywhere` | Charms | Permit legal charm changes outside benches | `DEFERRED` | `HKMOD-008` |
| `geo_multiplier` | Economy | Multiply newly awarded Geo without rewriting existing currency | `DEFERRED` | `HKMOD-009` |
| `keep_geo_on_death` | Economy | Preserve Geo across the normal death/respawn path | `DEFERRED` | `HKMOD-010` |
| `journal_one_kill` | Journal | Reduce remaining journal kill requirements safely | `DEFERRED` | `HKMOD-011` |
| `auto_map` | World | Reveal only map state proven safe by the current game API | `DEFERRED` | `HKMOD-012` |
| `health_bars` | World | Present enemy health through a managed runtime seam | `DEFERRED` | `HKMOD-013` |
| `damage_numbers` | World | Present dealt damage through a managed runtime seam | `DEFERRED` | `HKMOD-014` |
| `boss_retry` | World | Retry a boss without damaging scene/save state | `DEFERRED` | `HKMOD-015` |
| `secret_radar` | World | Present nearby-secret assistance without progression mutation | `DEFERRED` | `HKMOD-016` |
| `bench_teleport` | World | Travel only to recorded benches through a safe scene transition | `DEFERRED` | `HKMOD-017` |
| `state_slots` | State | Four explicit snapshot/restore slots | `DEFERRED` | `HKMOD-018` |

The catalog is complete for H3 even when rows are deferred because every missing capability remains visible, named, and owned. H3 may advance to H4 with tracked deferrals; final release may not claim full Mods parity while any `HKMOD-*` row remains unresolved or has not been explicitly removed from product scope by the user.

## Capability promotion gate

A deferred row becomes available only when all applicable conditions pass:

1. A stock typed managed seam or a separately documented managed rewrite is named.
2. The exact `1.5.12620` compile proves every referenced symbol.
3. Fake-API tests prove value mapping, unknown-value rejection, and legal guards.
4. Baseline tests prove master-off, reset, apply-failure, and maintenance-failure restoration.
5. Persistence tests prove profile-qualified recreation and corrupt-value fallback.
6. Progression-sensitive rows additionally pass synthetic ordinary-play, death/respawn, scene-transition, save/reload, and rollback tests without reading or writing a live user save.
7. The ledger records the evidence and changes the runtime state to available.

If any condition is missing, the row remains disabled. The implementation never substitutes a weaker behavior under the same row name.

## Runtime data flow

1. Bootstrap creates `HollowKnightModsRuntime` for the immutable Hollow Knight profile.
2. The runtime loads and validates the store without mutating the game.
3. It waits for a stable game API readiness signal.
4. The adapter captures all available baselines once.
5. If persisted master is enabled, the controller applies available non-default selections in catalog order. Deferred rows are skipped.
6. The presenter may attach whenever direct-display content becomes ready.
7. User changes apply first, then persist and flush. Persistence failure restores the old value and fails closed.
8. Runtime maintenance ticks only while master is enabled and game state is legal.
9. Display loss detaches the presenter only.
10. Master off, reset, apply failure, or maintenance failure restores the full baseline. A failure also persists master off and remains visible when the presenter next attaches.

A temporary not-ready state delays initialization or maintenance. It is not treated as an apply failure. A user-triggered action attempted in an illegal state returns a visible failure and follows the controller's fail-closed policy.

## Menu behavior

The existing context box displays one selected group at a time:

- `MODS` title and master state;
- stable previous/next group controls;
- a vertical row viewport whose scroll follows keyboard/controller/touch selection;
- selected value for available rows;
- `DEFERRED` for unavailable rows;
- row description;
- unavailable reason plus `HKMOD-*` tracking ID;
- reset action;
- latest apply/persistence error.

The shared `TweakMenuModel` owns group, row, and viewport state. The Hollow Knight presenter computes hit regions from the measured context-box geometry and forwards semantic actions to the model; it does not duplicate navigation rules.

The master defaults off on first run. When off, available rows retain their configured selections but render disabled and do not mutate the game. Deferred rows remain visibly distinct from master-disabled available rows.

Reset restores the baseline and sets all selections to descriptor defaults while preserving the user's explicit master selection, matching the existing shared-controller contract.

## Error handling

- Invalid persisted values fall back to descriptor defaults and are corrected in storage.
- Baseline capture failure leaves master off and exposes an error.
- Apply failure restores every captured baseline, persists master off, and exposes the original error plus any restoration error.
- Persistence failure restores the previous value, then restores the baseline and disables master.
- Maintenance failure follows the same fail-closed path.
- Presenter errors cannot disable gameplay input or mutate game state; the modal tears down and the runtime retains its last safe controller state.
- An unavailable capability request is rejected before adapter invocation or persistence.

## Verification strategy

### Pure shared tests

- available and unavailable descriptor invariants;
- unavailable cycle/apply rejection;
- stale unavailable preference correction;
- master restoration skips deferred rows;
- game-qualified persistence and corrupt-value fallback;
- apply/persistence/maintenance failure rollback.

### Hollow Knight adapter tests

Using a fake `IHollowKnightTweakApi`:

- exact `hollow-knight` game ID and complete catalog order;
- baseline captured once before any apply;
- exact mapping for both initial presentation rows and every promoted gameplay row;
- legal-state gating;
- deterministic apply and restoration order;
- full restoration after disable/reset/failure;
- deferred rows never invoke the API;
- process-resident runtime survives presenter detach/reattach.

### Source and compile contracts

- no native-address tables, process scanning, injected `PlayerData` members, save writes, copied H3 modules, or extra companion tab;
- every disabled row has a unique `HKMOD-*` ID present in the verification ledger;
- every available row has adapter tests and a typed API implementation;
- exact Hollow Knight patch compile;
- exact Silksong patch compile after shared-contract changes.

### Device cadence

H3 does not produce a device candidate. After H3 and H4 are host-green, one signed Hollow Knight integration candidate exercises the Mods modal, available effects, persistence, master/reset/revert, skins, and death/respawn rotation in one owned-device session. Deferred rows are not presented as testable effects.

## Deferral closure

`docs/verification/hollow-knight-mod-capabilities.md` is the authoritative `HKMOD-*` ledger. Every row includes status, missing seam, target stage, implementation owner, acceptance condition, and evidence.

All unsafe rows target the final managed-rewrite/parity-remediation stage before final cross-game acceptance. That stage must resolve each ID by either:

- promoting it after the capability gate passes; or
- recording explicit user-approved removal from product scope.

Silence, omission, an inert toggle, or carrying an ID without a reachable acceptance condition is not closure.

## Completion conditions

The initial H3 implementation batch is ready to advance when:

- the fork-owned full catalog is rendered through the existing Mods modal;
- both presentation rows and every additional compile-proven safe typed row work through the shared controller;
- master, reset, persistence, baseline restoration, and visible errors pass host tests;
- every unavailable row is disabled and linked to a complete ledger entry;
- both exact game compiles pass;
- no upstream interaction, copied H3 source, device build, or signing action occurred.

H3 is recorded as `HOST-COMPLETE / TRACKED-DEFERRALS`, not full parity. Full Mods parity is reached only when the final remediation stage has zero unresolved `HKMOD-*` entries.
