# Dual Souls product contract

This is the sole product contract for the dual-screen UI, built-in Mods, and
Custom Knight skin experience. The audited Hollow Knight implementation is the
behavioral reference. Silksong must receive the same experience while retaining
its direct-display transport and accommodating its additional mechanics.

A plan, stage label, host build, or deferred ledger cannot narrow this contract.
A required feature that is absent is **missing**, not complete. Historical test
receipts record only what they directly prove.

## Visible dual-screen experience

- The upper display remains the game view. HUD elements assigned to the lower
  display must not remain duplicated on the upper display.
- The lower display uses its full renderable surface rather than a small modal
  floating inside unused space.
- Hollow Knight is the implementation and validation oracle. Hollow Knight must
  work first; shared behavior is then adapted to Silksong without regressing it.
- Silksong-specific health, Silk, currency, Crests, Tools, Tasks, and other
  mechanics occupy or extend the corresponding Hollow Knight layout instead of
  restoring Silksong's original upper-screen HUD or stacking a second HUD.
- The lower display provides the complete resident HUD and the original page
  set: Inventory, Map, Charms for Hollow Knight, Crest/Tools for Silksong,
  Tasks, Journal, and the original overlays and transitions.
- Page actions remain functional, including item use, equip/unequip operations,
  map pan/zoom and marker handling where the game permits them.
- Pause, resume, scene changes, display loss, and app restore must not leave
  duplicated, missing, frozen, or stale UI.

Visual acceptance requires side-by-side comparison with the Hollow Knight
reference in ordinary gameplay. Source similarity and synthetic host tests do
not substitute for that comparison.

## Built-in Mods

The complete reference catalog is required. The names below describe required
user-visible behavior; they are not a claim that the current build implements
all rows.

### General

- Skins
- Black background

### World

- Run speed
- Fast transitions
- Auto map
- Innate compass
- Bench teleport
- Secret radar

### Combat

- Nail damage
- Damage taken
- Damage cap
- One-hit kills
- Unlimited soul

### Encounters

- Enemy health bars
- Damage numbers
- Boss retry

### Charms

- Equip anywhere
- Charm costs
- Unlimited notches

### Save states

- Slot
- Save to slot
- Load from slot
- Delete slot

### Economy

- Geo magnet
- Keep geo on death
- Journal in one kill
- Geo multiplier

The interface also includes the group selector, Reset Mods, and Back. A row may
be adapted to Silksong terminology or mechanics, but it cannot disappear merely
because an implementation seam has not yet been built.

### Required Mods surfaces

The reference exposes three distinct surfaces, all of which remain part of the
product:

1. The in-game Options → Mods screen, with practical controller navigation,
   confirm, value changes, reset, and native Back/Cancel behavior.
2. The lower-display gear pane, using the whole lower render target with the
   reference list/details proportions. It may remain touch-driven so gameplay
   controller input continues to belong to the game, but it cannot be the only
   way to reach the catalog.
3. The launcher skin-package screen for installing and managing Custom Knight
   packs.

The launcher Mods screen must also provide visible controller focus, stable
focus after changes, deterministic directional navigation, confirm, left/right
value changes, and controller Back. Its content must use the full available
window, including a readable details pane; a non-fullscreen activity or
zero-width details view does not satisfy this requirement.

Mods must be profile-isolated, default off unless the user enabled them, apply
live where the reference applies live, persist across relaunch, and restore
normal game behavior when disabled or reset. Labels must describe the actual
effect. Silent no-ops, hidden required rows, and presentation-only substitutes
do not count as parity.

## Skins

- Import the user's local Custom Knight ZIP packs and enumerate every valid
  candidate without requiring archives to be normalized by hand.
- Candidate preparation must complete in practical seconds for ordinary local
  packs, not minutes or hours.
- Show installed packs, selection, and rotation membership clearly.
- Apply the selected pack in Hollow Knight before relying on the shared path for
  Silksong.
- Preserve the selected skin per save slot.
- In rotation mode, a normal in-game death advances to the next included pack
  exactly once. Menus, relaunches, synthetic callbacks, and non-death state
  changes must not advance it.
- Missing or invalid assets fall back safely without breaking gameplay or the
  other game profile.

## Constraints

- Keep Hollow Knight and Silksong profiles, saves, Mods state, skin state, and
  generated game files isolated.
- Preserve Silksong's direct-display transport.
- Do not use native addresses, IL2CPP offsets, process scanning, injected
  `PlayerData` fields, or save edits.
- Do not remove or rewrite user saves. Disposable validation saves must be
  recorded and removed after validation.
- Do not trade ordinary gameplay performance for import-time or per-frame proof
  machinery.
- Work only in this fork. No upstream-visible interaction is part of this work.

## Acceptance

The deliverable remains incomplete until all of the following are demonstrated:

1. Exact host compiles and focused tests pass for both supported game builds.
2. Hollow Knight ordinary gameplay shows the reference HUD/pages on the lower
   display, no duplicate upper HUD, full-window Mods layout, controller-operable
   Mods, the complete catalog, persistence/reset, skin application, and one
   normal-death rotation.
3. Silksong demonstrates the equivalent experience with its additional
   mechanics integrated into the same design and no Hollow Knight regression.
4. Repeated launch, pause/resume, scene transition, display interruption, and
   process restore preserve correct ownership and profile isolation.
5. Production artifacts use the persistent release signer. Publishing a public
   release requires separate authorization.
