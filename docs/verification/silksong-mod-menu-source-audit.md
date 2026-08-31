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

The current port already compiles `tools/silksong-patches/src` against the
selected depot's `Assembly-CSharp.dll` before IL2CPP conversion. A renamed or
removed member therefore fails the patch compile instead of becoming a stale
runtime address.

## Boundary

The first implementation stage exposes only the four seams above. It captures
their startup values and restores those exact values when master is disabled
or an apply fails. This avoids clobbering another built-in or external patch
that established a non-default value before the menu initialized.

Needle multipliers, currency multipliers, movement speed, health bars, damage
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
