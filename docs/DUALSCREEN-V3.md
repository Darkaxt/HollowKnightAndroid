# Dual Souls product contract

This is the sole product contract for the dual-screen UI, built-in Mods, and
Custom Knight skin experience. Both games share Silksong's direct-display
transport and v2 lower-screen frame; each keeps its own mechanics.

A plan, stage label, host build, or deferred ledger cannot narrow this contract.
A required feature that is absent is **missing**, not complete. Historical test
receipts record only what they directly prove.

## Visible dual-screen experience

- The upper display remains the game view. HUD elements assigned to the lower
  display must not remain duplicated on the upper display.
- Both games host their lower display on one shared, game-neutral frame
  (`tools/shared-patches/src/DsFrame/`: DsShell tab strip, header HUD band,
  title card, page slides, cursor, action column, zoom slider). Silksong v2's
  layout is canonical; each game supplies only an `IDsFrameHost` (in-game
  state, layout size, content layer, cursor art, header HUD, title logo) and
  its own pages.
- The lower display uses its full renderable surface for the resident HUD band
  and the pages rather than a small panel inside unused space.
- Silksong pages: Inventory, Crest/Tools, Tasks, Journal, Map.
  Hollow Knight pages: Inventory, Charms, Guide, Journal, Map, using HK's own
  data, art, text and rules. Mechanics of one game are never invented for the
  other.
- Page actions remain functional where the game permits them: item details,
  charm equip/unequip, journal entries, map pan/zoom/reset, markers and bench
  travel (bench travel only while the Mods tweak menu is open).
- The Hollow Knight lower screen shows no FPS counter or battery readout.
- Pause hides the gameplay HUD and pages; the title card shows the game's own
  logo. Resume, scene changes, quit to menu, display loss and app restore must
  not leave duplicated, missing, frozen or stale UI.
- With the companion display turned off the pages are hidden while the HUD
  stays in the header band.

Visual acceptance requires device comparison of both games in ordinary
gameplay. Source similarity and synthetic host tests do not substitute for it.

### Hollow Knight lower screen (2026-11 reset)

The pre-reset world-space HK lower screen (companion pane clones, native layout
engine) was retired by reset step 4 (#19) and replaced by the shared frame above.

- Five HK pages on DsShell via `HKDualScreen.Shell`, the HUD framed into the
  header band, and the pause/title-logo behaviour above: verified on device on
  signed 2.0.22 (#19, #23).
- The lower screen is always the opaque black ground. The old scenery-wash
  backdrop and its Black background Mods row were removed (#16): the wash
  needed a second scene camera every frame, and nothing drew it after the reset.
- Mods are reachable from the title-screen and pause Options in both games, with
  readback from live game state (#5, #20).

## Built-in Mods

The complete reference catalog is required. The names below describe required
user-visible behavior; they are not a claim that the current build implements
all rows.

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
   Mods configuration interface and must expose the complete required catalog for
   the active game, including every group, row, unavailable-state explanation,
   Reset Mods command, and Back action. Every supported Choice row mutates
   directly through Left/Right or confirm; no global switch or prerequisite may
   gate it. The screen must provide practical controller navigation, stable focus
   after confirm or value changes, command and route execution, reset, and native
   Back/Cancel behavior. Grouping and scrolling may organize the catalog, but
   must not omit rows that exist in the shared Mods model. The category selector
   is shown at the upper left as `< CATEGORY >`, with its `n/N` category position
   separately at the upper right. Settings use aligned name and value columns,
   plus a readable multiline description for the focused setting; they must not
   appear as centered sentence-like rows. `STATUS READY` and similar
   implementation diagnostics are not product UI. The separate Options → Skins
   screen is the only in-game route to skin configuration, so Mods must not
   contain a duplicate `SKINS OPEN` row.
2. The **launcher mod-package screen** for installing and managing profile-scoped
   plugin packages. It must not configure built-in Mods choices or write their
   runtime settings. Skin-package management remains a separate launcher screen.

The lower display is reserved for its resident HUD, game pages, and their
existing controls. It must not contain a Mods wheel, gear, shortcut, pane,
modal, catalog, or duplicate Mods presenter. No hidden or fallback lower-display
Mods input owner may remain.

The launcher mod-package and skin-package screens remain outside gameplay.
They do not create a second in-game Mods configuration menu.

Mods must be profile-isolated. Each Choice starts at its descriptor default
(normally off or vanilla), applies live where the reference applies live,
persists across relaunch, and restores normal game behavior when reset. Labels
must describe the actual effect. Silent no-ops, hidden required rows, and
presentation-only substitutes do not count as parity.

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
2. Hollow Knight ordinary gameplay shows the header HUD and its five pages on
   the shared lower-screen frame with no duplicate upper HUD and no lower-display Mods wheel, gear,
   pane, or presenter. Its upper Pause → Options → Mods screen uses the full
   available upper menu window, is fully controller-operable, exposes the
   complete catalog, and proves persistence/reset. Its upper Pause → Options →
   Skins screen is likewise controller-operable and authoritative, with no
   lower-display Skins UI. Skin application and one normal-death rotation are
   also demonstrated.
3. Silksong demonstrates the equivalent upper-display Mods and Skins ownership
   and lower-display HUD/page experience, with its additional mechanics
   on the same frame, and neither game regresses the other.
4. Repeated launch, pause/resume, scene transition, display interruption, and
   process restore preserve correct ownership and profile isolation.
5. Production artifacts use the persistent release signer. Publishing a public
   release requires separate authorization.
