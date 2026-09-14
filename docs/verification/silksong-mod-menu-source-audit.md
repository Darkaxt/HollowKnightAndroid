# Silksong built-in tweak source audit

**Date:** 2026-08-31  
**Game source:** user-owned Linux Silksong `1.0.29980`  
**Assembly SHA-256:** `1AF095416B89F73993058F9CBAC3A93959D928314B735CC4ACBCA7BF1A952D2D`

## Finding

Silksong already contains a release-dormant `CheatManager`. Its original GUI
is unavailable, but gameplay code still reads several public static switches.
The port can expose a curated subset through its own bottom-screen UI without
using native addresses or guessing IL2CPP offsets.

Observed managed consumers:

| Capability | Typed seam | Consumed by |
| --- | --- | --- |
| Damage received | `CheatManager.Invincibility` with `Off`, `PreventDeath`, and `FullInvincible` | `HeroController` and `PlayerData` damage/death paths |
| One-hit kills | `CheatManager.NailDamage = NailDamageStates.InstaKill` | `HealthManager.Hit` |
| Unlimited Silk | `CheatManager.IsSilkDrainDisabled`; `HeroController.RefillSilkToMaxSilent()` | PlayMaker silk-drain checks and the hero's normal refill path |
| Equip anywhere | `CheatManager.CanChangeEquipsAnywhere` | `InventoryItemToolManager` |
| Instant dialogue | `CheatManager.IsTextPrintSkipEnabled` | `DialogueBox.<PrintText>d__49.MoveNext()` |
| Disable world rumble | `CheatManager.IsWorldRumbleDisabled` | `WorldRumbleManager.get_IsRumblesPrevented()` |
| Ignore frost slowdown | `CheatManager.IsFrostDisabled` | `HeroController.GetTotalFrostSpeed()` |

The current port already compiles `tools/silksong-patches/src` against the
selected depot's `Assembly-CSharp.dll` before IL2CPP conversion. A renamed or
removed member therefore fails the patch compile instead of becoming a stale
runtime address.

## Boundary

The implementation exposes only the seven seams above. It captures their
startup values and restores those exact values when master is disabled, an
individual row returns to `off`, or the controller rolls back a failed apply.
This avoids clobbering another built-in or external patch that established a
non-default value before the menu initialized.

Needle multipliers, currency multipliers, game-speed and movement-speed
multipliers, health bars, damage
numbers, map helpers, boss retry, teleport, and save states do not have an
equivalent proven switch in this audit. They remain required parity candidates
but need separate typed-hook or Cecil-rewrite evidence and, where progression
or scene state changes, save-safety proof.

## Host implementation proof — 2026-08-31

The first implementation slice compiles against the pulled managed set for
Silksong `1.0.29980`. `Assembly-CSharp.dll` remains SHA-256
`1AF095416B89F73993058F9CBAC3A93959D928314B735CC4ACBCA7BF1A952D2D`.
The exact-depot check compiled 38 sources successfully, including the shared
controller, typed adapter, concrete `CheatManager` boundary, durable
`PlayerPrefs` store, and second-screen modal.

The baseline in `SilksongGameTweakApi` is process-wide so rebuilding the HUD
after the game font becomes available cannot recapture an enabled tweak as the
value to restore. Host tests also force storage flush failure and prove the
controller reports failure, restores the captured baseline, rolls back the
selection, and leaves master OFF.

The API 35 x86-64 emulator proves the launcher lifecycle, profile persistence,
profile isolation, and rendered unified launcher, but it cannot execute the
ARM64 Unity player or reproduce the Thor's physical display 1. Production
modal rendering, touch containment, real effects, and process-exit/relaunch
persistence therefore remain device gates rather than inferred passes.

## Task100 production reachability checkpoint

Task100 replaces the dormant authored-shell route with a process-owned
`SilksongModsRuntime` and production `DsPortMods` presentation. Bootstrap starts
Mods before the dual-screen-enabled early return, so settings, initialization and
restoration retry do not depend on display 1. The presenter uses the accepted
`DsPortFrame.ModsAnchor`, native resident pane labels/fleurs and the shared
`TweakMenuModel`; detach removes its gesture consumer and owned visuals without
disposing the process session.

At that checkpoint, the source-backed capability boundary remained the original
four rows: damage received, one-hit kills, unlimited Silk, and equip anywhere.
Typed actions require the current `GameManager`, gameplay scene, `PlayerData` and
`HeroController`; unavailable state fails closed. Shared controller/session tests
cover default-off profile isolation, reset, apply retry, restoration retry and
owner replacement. Executable regressions additionally prove that failed initial
capture never attempts restoration, an exact failed Hollow Knight teardown owner
blocks replacement until retry succeeds, permanent enabled-apply rejection commits
a recoverable disabled state only after restoration, and sustained restoration
failure keeps a bounded current diagnostic. `DsPortMods` uses shared model and
layout/geometry paint stamps, retaining its live labels and touch hit boxes without
per-frame TMP generation when stamps are unchanged. Exact source manifests, compile
receipts and the unproved device boundary are recorded in
`docs/verification/evidence/task100-quality-c3f61f5/completion.json`, which
supersedes `task100-spec-fix-0c422bc`.

## Task 137 first safe built-in Mods batch

Task 137 adds three public static `bool` properties from the same exact
Silksong `1.0.29980` assembly to the typed boundary:
`IsTextPrintSkipEnabled`, `IsWorldRumbleDisabled`, and `IsFrostDisabled`.
Their managed consumers are listed in the table above. Each row defaults OFF,
captures and restores its process baseline, and remains scoped to the Silksong
profile through the shared state, catalog, controller, and runtime path.
Game speed and the other deferred candidates above remain excluded.
