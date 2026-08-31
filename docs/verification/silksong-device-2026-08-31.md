# Signed Silksong device checkpoint — 2026-08-31

Target: AYN Thor `bfa98654`

This checkpoint records the first fork-signed production Silksong run. It is
both positive renderer evidence and a visual-design failure report.

## Artifact and source identity

- GitHub Actions dry run: `33420591659`
- APK: `DualSouls-1.0.3.apk`
- APK SHA-256: `E06749DB621B6EF59A574C416E17BD0912951B8A52C215DEBD3D05917EEDA0B7`
- package/version: `io.github.darkaxt.dualsouls`, `1.0.3` (`10003`)
- signer SHA-256: `324B3A3E854B69D567D1527AE52E96A1051ADF13550B485E320F8CE8CF678C38`
- selected source: Hollow Knight: Silksong `1.0.29980`
- source `globalgamemanagers` SHA-256:
  `7C2AF8EBCF5CD360CB8017D6C0EDDE56967F2BE834894352E36CF05F5D14E033`

## On-device build and launch

The signed launcher compiled 955 IL2CPP units and 360 engine-runtime units,
linked the ARM64 player, and published generation
`gen-9539acc2-8bb9-4bf3-b93a-eb63b21f5525`. Its `data.apk` is 63,742,318 bytes
with SHA-256
`EC8BED5B1FA08E6AB18E719060FCA6F337A065802C991631BD86A4240F7D955A`.
The game started in a process separate from the surviving launcher, displayed
version `1.0.29980`, completed first-run setup, created slot 1, and reached
playable gameplay.

Unity activated the Thor's lower physical display and rendered the companion
surface there while gameplay remained on the primary display. This closes the
Silksong production build, launch, gameplay, and display-1 rendering questions
for this exact artifact/source combination.

## Tweak-state finding

The live profile preferences contained
`dualsouls.mods.silksong.master=0` after first launch. No gameplay tweak was
enabled. This proves the required first-run master-OFF state for Silksong; it
does not yet prove row interaction, effects, process-exit persistence, or
Hollow Knight key isolation.

## Blocking visual finding

The lower display rendered the Map/Inventory/Crest/Tasks/Journal shell, but the
surface did not match the agreed Hollow Knight / Dual Souls design. It used a
flat top tab strip, presented Mods as a sixth peer tab, and omitted the
persistent HUD/status strip, ornamental context frame, bottom-centred tabs,
and selected-tab fleurs. The renderer is accepted; the shell is rejected.

The active remediation gate is a signed Thor capture showing:

1. a persistent top HUD/status strip;
2. a framed true-black content region with ornamental separators;
3. semantic tabs centred along the bottom with ornamental selection feedback;
4. Mods opened from a separate gear control, never a peer tab; and
5. unchanged primary-display gameplay and fail-contained page behavior.

The game was force-stopped after evidence collection. Existing Hollow Knight
PoC package/data and both user-supplied source trees were left untouched.

## Prepared remediation

The rejected shell has since been replaced in source by a game-neutral
`CompanionShellLayout` and a Silksong adapter that implements the required HUD,
framed context region, bottom-centred semantic tabs, paired selected-tab
ornaments, left/right status gutters, and standalone gear entry. The Mods
surface is constrained to the context box and no longer replaces the shell or
appears as a peer tab.

Host evidence for this remediation is 29/29 shared tests (including five new
layout/hit-test contracts), 9/9 profile contracts, 120 Android host tests with
zero failures/errors and two environment-gated skips, 38/38 bundle-surgery
tests, exact Silksong and Hollow Knight patch compiles, and a successful debug
AAR assembly. These checks do not close the blocking signed Thor visual/touch
gate.

## Replacement-shell signed run

Commit `a310332` was built by fork dry run `33428813976`. The downloaded
`DualSouls-1.0.3.apk` was 72,227,340 bytes with SHA-256
`1A75565AB98D17CE651A686CE66A0298B06E108C9979CD9DC603598B477FC095`,
package/version `io.github.darkaxt.dualsouls` `1.0.3` (`10003`), and the same
pinned signer SHA-256
`324B3A3E854B69D567D1527AE52E96A1051ADF13550B485E320F8CE8CF678C38`.
Installing it in place retained the original install time, selected source,
preferences, old generation, and `hkpoc` package.

The launcher correctly marked the old generation stale rather than launching
it. During repair, `current` remained
`gen-9539acc2-8bb9-4bf3-b93a-eb63b21f5525`. The device compiled the new
40-source patch assembly, converted 955 C++ and 200 C units in 184 seconds,
rebuilt the changed native objects, and linked 1,559 objects in a 107-second
native pass. Only after packaging and verification did `current` atomically
change to `gen-92b911fd-0063-4a83-b8f1-a34ec3e893b6`.

Published payload evidence:

- `generation.json`: 901 bytes, SHA-256
  `D05588EF4D6EFD07692575CDD73B4A3BC0EA3A1EAAC5B7383A1E3E206AF7765B`;
- `data.apk`: 63,749,846 bytes, SHA-256
  `DC2DB4CF0FAB3F3D73B5084A3291B025C843415733AC22AB250AD40BD0A27B9F`;
- `libil2cpp.so`: 316,375,952 bytes, SHA-256
  `F18701F4247BB30344B1FB95588406CFB1CD0A2A7C70D94715CBEB4357169CE6`;
- source `globalgamemanagers` remained
  `7C2AF8EBCF5CD360CB8017D6C0EDDE56967F2BE834894352E36CF05F5D14E033`;
  and
- `dualsouls.mods.silksong.master` remained `0`.

The exact generation reached `Moss Grotto` gameplay on the primary display.
The lower physical display rendered the required persistent health/Silk/crest/
area strip, true-black framed context, bottom-only semantic tabs
`INVENTORY / CREST / TASKS / JOURNAL / MAP`, paired selection ornaments,
standalone gear, FPS, and battery status. `MODS` was not a peer tab. This closes
the blocking replacement-shell **visual** gate for Silksong.

Android's synthetic `input -d 4` touch does not appear in the Unity Input
System as a physical display-1 touch, and writing event records to the
lower-panel evdev node is not input injection. Therefore the physical gear,
tab, and modal interaction gate remains open; host hit testing is not relabeled
as device touch proof.

The game was force-stopped after capture. The production game and launcher
PIDs were absent, the separate `hkpoc` launcher remained alive, master stayed
OFF, and the isolated empty-slot run produced no save file.

## Post-run design reconciliation

The visual pass exposed no new geometry blocker. A read-only specification
audit did find four host defects, which were repaired immediately after the
signed run: failed pages now retain the prior working page, font discovery
continues until both display/body faces exist, missing resident sprites retry
instead of caching a permanent miss, and the Mods header controls use the
shared 72-pixel touch minimum. The revised sources pass 29/29 shared tests,
9/9 profile contracts, 38/38 bundle-surgery tests, both exact-profile patch
compiles, and 120 Android tests with zero failures/errors and two environment
skips. A signed rebuild remains required before those four post-run repairs are
device evidence.
