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
- The lower display uses its full renderable surface for the resident HUD and
  game pages rather than placing them in a small panel inside unused space.
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

### Required Mods surfaces and ownership

The product has exactly two Mods-related surfaces:

1. The **upper-display** in-game Options → Mods screen. It is the sole in-game
   Mods interface and must expose the complete required catalog for the active
   game, including every group, row, unavailable-state explanation, master
   switch, Reset Mods command, and Back action. It must provide practical
   controller navigation, stable focus after confirm or value changes,
   left/right value changes, command and route execution, reset, and native
   Back/Cancel behavior. Grouping and scrolling may organize the catalog, but
   must not omit rows that exist in the shared Mods model. The category selector
   is shown at the upper left as `< CATEGORY >`, with its `n/N` category position
   separately at the upper right. Settings use aligned name and value columns,
   plus a description for the focused setting; they must not appear as centered
   sentence-like rows. `STATUS READY` and similar implementation diagnostics are
   not product UI. The separate Options → Skins screen is the only in-game route
   to skin configuration, so Mods must not contain a duplicate `SKINS OPEN` row.
2. The launcher skin-package screen for installing and managing Custom Knight
   packs. This remains separate because package import and management happen
   outside gameplay.

The lower display is reserved for its resident HUD, game pages, and their
existing controls. It must not contain a Mods wheel, gear, shortcut, pane,
modal, catalog, or duplicate Mods presenter. The existing lower-display Mods
UI may be removed only after automated catalog parity and live controller
validation prove that the upper Options → Mods screen exposes and operates the
complete catalog in both games. Once that gate passes, the lower Mods UI and
its touch/input ownership must be removed rather than retained as a hidden or
fallback path.

The launcher skin-package screen must provide visible controller focus, stable
focus after changes, deterministic directional navigation, confirm, controller
Back, and a readable full-window layout. These launcher requirements do not
create a second in-game Mods menu.

Mods must be profile-isolated, default off unless the user enabled them, apply
live where the reference applies live, persist across relaunch, and restore
normal game behavior when disabled or reset. Labels must describe the actual
effect. Silent no-ops, hidden required rows, and presentation-only substitutes
do not count as parity.

## Skins

The upper Pause → Options → Skins screen is the sole in-game Skins
configuration menu in both games. Its rows, in order, are MODE, SPRITES,
installed skins, and Back. MODE selects OFF, ON, or ROTATE. The exact SPRITES
scope choices are ALL / CHARACTER + HUD / CHARACTER. The menu must be fully
controller-operable, keep stable focus after changes, and persist its
configuration through the shared profile-isolated skin authority.

The launcher Skins screen is package management only. It imports and replaces
local packages, enumerates every valid candidate, reports compatibility,
receipt, error, runtime, and recovery status, and deletes packages subject to
in-use safeguards. It may report mode, selected/active pack, rotation
membership, and sprite scope as read-only status, but it must not mutate those
settings. Candidate preparation must complete in practical seconds for ordinary
local packs, not minutes or hours.

The lower display is reserved for the resident HUD and game pages and must not
contain a Skins UI, shortcut, pane, modal, selector, or duplicate presenter.

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
   display with no duplicate upper HUD and no lower-display Mods wheel, gear,
   pane, or presenter. Its upper Pause → Options → Mods screen uses the full
   available upper menu window, is fully controller-operable, exposes the
   complete catalog, and proves persistence/reset. Its upper Pause → Options →
   Skins screen is likewise controller-operable and authoritative, with no
   lower-display Skins UI. Skin application and one normal-death rotation are
   also demonstrated.
3. Silksong demonstrates the equivalent upper-display Mods and Skins ownership
   and lower-display HUD/page experience, with its additional mechanics
   integrated into the same design and no Hollow Knight regression.
4. Repeated launch, pause/resume, scene transition, display interruption, and
   process restore preserve correct ownership and profile isolation.
5. Production artifacts use the persistent release signer. Publishing a public
   release requires separate authorization.
