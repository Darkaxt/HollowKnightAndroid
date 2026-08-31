# Hollow Knight 1.5.12620 build checkpoint

Date: 2026-08-31

This is the safe pre-install checkpoint for Task 10. It proves the current
Hollow Knight Linux build can be converted, compiled for Android ARM64, and
packed through the shared player-image path. It deliberately does **not**
claim a first boot: the user requested a pause before the isolated proof APK
was installed.

## Inputs and source proof

- Profile: `hollow-knight`
- Game version: `1.5.12620`
- Unity version: `6000.0.61f1`
- Source platform: Linux x86-64
- Source tree SHA-256: `1319a0eaf10cc6f64418a495a7ee481cc3f6e60b1be38180426c35742daa42f8`
- Committed semantic manifest SHA-256: `8035cad22a9cfb209e1e15d3ff27d0db901faf7b85d0cb3225a873d073f5074f`
- Conversion resume key: `88047359fa3f3cc8b45550db66b75e452512afaee5bd2530eb4308fa5b1ac85a`
- Source report: 1,748 files; 1,005 transformed, 737 copied, four
  desktop native plugins excluded, and two platform-owned resources replaced
  during player-image assembly.
- Shader census: 125 shaders, 125 Vulkan-capable, zero missing Vulkan, zero
  diagnostics.
- The environment-driven real-source validator test passed against the
  committed manifest after a fresh `manifest-classic-tree` inventory.

## Android resource proof

- Exact Unity Android `unity default resources`: 3,743,124 bytes, SHA-256
  `32b37bf1dfe4c3c0b32b1f6afff96a41d988fb058ed6cd1d077c56cb6b56f991`.
- Project `unity_builtin_extra` was transformed from the supplied game rather
  than replaced with a generic file: 442,452 bytes, SHA-256
  `edaead751c5a195db98370a506f94b17a476e57120f0c3ddad4f5ced6574e7f1`.
- Independent shader reopening reports platform 18/Vulkan for all 44 shaders
  in the transformed project built-in, with no non-Vulkan shader.

## IL2CPP and native compilation proof

- The exact assembly set, including the separately compiled
  `HollowKnightPatches.dll` injection probe, produced 799 C++ and 178 C
  generated translation units and a 30,439,916-byte `global-metadata.dat`.
- Native compilation passed all phases: 799 generated C++ units in 583 s, 178
  generated C units in 127 s, 363 Unity runtime units in 199 s, then bdwgc,
  zlib, and Brotli in 7 s, 9 s, and 32 s respectively.
- Windows rejected the first link invocation because expanding 1,385 object
  paths exceeded its command-line limit. The build now uses a Clang response
  file; its regression test passes and the same compiled objects linked
  successfully.
- `libil2cpp.so`: 293,057,880 bytes, SHA-256
  `d620cf5c8ace02a2aa5d6cac7c3f50a0c10184f1d240e8e273e49c6c699951ea`.
- Independent `llvm-readelf` verification: ELF64, AArch64, shared object,
  `DT_SONAME=libil2cpp.so`; dependencies are only `liblog.so`, `libm.so`,
  `libdl.so`, and `libc.so`; every load segment has `0x4000` alignment.

## Player image and proof APK

- The production `PlayerImage.install` ZIP64 path packed the full staged image
  in an environment-gated Robolectric integration test.
- `data.apk`: 5,179,143,894 bytes, SHA-256
  `e3c84486450ee8f2292a63b0a543edfe5ad4d83620e39254ef7af1eac0906d4d`.
- The archive contains 1,638 stored entries, reopens through `ZipFile`, carries
  both ZIP64 end records, and includes `globalgamemanagers`, Android engine
  resources, transformed project built-ins, IL2CPP metadata/resources,
  `scripting-backend=il2cpp`, and the Hollow Knight injection registration.
- A second real-image pack exposed Windows refusing to rename over the existing
  archive. The publisher now atomically replaces it; a focused regression and
  the repeated 5.18 GB pack both pass.
- The shell now accepts the `hollow-knight` launch profile and maps its compact
  runtime key to `hk`; a regression test prevents the former Silksong-only
  rejection from returning.
- Pre-fix isolated debug proof package: `io.github.darkaxt.dualsouls.hkpoc`, version
  `1.0.3`/`10003`, 70,985,716 bytes, SHA-256
  `dd26c71789af9f2eb1f40754834c37c40260adc8f5e6505a0313d3f5a92a542a`.
  APK Signature Schemes v2 and v3 verify. It was not installed and must be
  rebuilt from the post-sync tree before device use.

## Verification gates

- Android host suite: 92 tests, 90 passed, zero failures/errors, two
  environment-gated skips. Each skipped real-source/player-image test was
  then run explicitly against the current inputs and passed.
- Bundle-surgery suite: 37/37 passed.
- Python CI contracts: 14/14 passed.
- Hollow Knight patch compilation against the exact game and Android player
  assemblies passed with one registered entry point.

## Cross-check and safe pause

- `DEFERRED`: inspect and apply the nine pending commits from parent remote
  `origin` before resuming. Resolve them against this checkpoint and rerun all
  affected host, converter, packaging, and device gates.
- `COMPLETE`: exact 1.5.12620 source acceptance and full real-tree conversion.
- `COMPLETE`: exact Android built-ins and project shader transformation.
- `COMPLETE`: Hollow Knight patch assembly, IL2CPP generation, ARM64 native
  compilation/link, ELF contract, classic player-image assembly, and ZIP64
  reopening.
- `COMPLETE`: a signed, isolated launcher APK containing the Hollow Knight
  profile route builds without proprietary game content.
- `BLOCKER`: Task 10 is not complete until serial `bfa98654` (AYN Thor,
  Android 13/API 33, ARM64) runs the isolated package, logs
  `[DualSouls][HK] injection probe loaded`, reaches the main menu, and enters a
  playable room with rendering, audio, and input verified.
- `DEFERRED`: launcher visual redesign using the already preserved supplied
  grids, heroes, logos, and icons. No image generation is permitted.
- `DEFERRED`: pinned per-game shortcuts with direct profile launch and the
  supplied individual Hollow Knight/Silksong icons. This follows the Task 10
  boot gate so shortcuts cannot advertise an unproven launch path.

Local proof artifacts remain under their exact `D:\Temp\dualsouls-hk-*`
roots for the next device run. They contain user-owned game-derived data and
must never be committed or published.
