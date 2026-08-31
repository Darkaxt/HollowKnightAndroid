# Unified launcher emulator checkpoint — 2026-08-31

## Environment

- AVD: `DualSoulsLabApi35`
- Serial: `emulator-5580`
- API/ABI: Android 35, x86-64
- Runtime evidence: `EMULATOR-FAKE`
- Lab APK SHA-256: `21d16ef3a623b0c0969c37d7dbc60426ccc68f55b8696e3807e93e49cdf459ca`
- ARM64 JNI in lab APK: none

The lab runner resolves the exact AVD name and explicitly rejects the AYN Thor
serial before installation or instrumentation.

## Result

The first run exposed a stale UI assertion: after the approved launcher
redesign, the plain-text game titles no longer exist because the supplied game
logos are the visible titles. The rendered hierarchy contained both stable
game-card IDs and accessible names in `contentDescription`. The instrumentation
now verifies that replacement contract.

The rerun passed all three instrumentation tests:

1. both profiles render and selection survives a launcher process exit;
2. profile generations publish, recover, and reset independently;
3. the active game process exits before the other profile starts.

The final rendered hierarchy also proves the two supplied game banners, pin
controls, profile status pills, and `Import saves` / `Export saves` labels.
The independent Silksong generation remained present after Hollow Knight was
reset.

## Boundary

This AVD cannot execute the production ARM64 Unity player or emulate the
Thor's physical display 1. It therefore cannot prove the in-game Mods modal,
second-screen touch containment, gameplay effects, or process-relaunch effect
restoration. Those remain physical-device gates.
