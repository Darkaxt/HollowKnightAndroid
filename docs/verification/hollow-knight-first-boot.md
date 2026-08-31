# Hollow Knight 1.5.12620 build checkpoint

Date: 2026-08-31

This began as the safe pre-install checkpoint for Task 10 and now records three
production-path attempts on the Thor. The host and publication proofs remain
valid, but this document still does **not** claim a first boot: attempt 3
published and loaded the real ARM64 generation, then proved Unity's Android APK
reader cannot consume the 5.18 GB ZIP64 player-data container.

## On-device attempt 1

- Device: serial `bfa98654`, AYN Thor, Android 13/API 33, ARM64.
- Isolated package: `io.github.darkaxt.dualsouls.hkpoc`, version
  `1.0.3`/`10003`, built from implementation commit `e336e28`.
- APK: 71,659,015 bytes, SHA-256
  `3aa95e7b4e50b7d320e8d02c9033125b7939f10dcb77b83e93d7b6a86de2cea5`;
  APK Signature Schemes v2 and v3 verify.
- The exact Linux 1.5.12620 source was copied to
  `/storage/emulated/0/Download/HollowKnight-1.5.12620`. Its retained transfer
  manifest verified 1,753 expected and actual files with zero missing, extra,
  or hash-mismatched files. The launcher persisted that path in the
  `hollow-knight` profile's `source.pointer`.
- The Hollow Knight folder under the device's `Games` directory is Windows
  only and is excluded. That directory will be consulted only for the
  separately identified Linux Silksong source during its regression pass.
- The production coordinator compiled `HollowKnightPatches.dll`, Harmony, and
  BepInEx, then converted 169 assemblies into 803 C++ and 180 C translation
  units. The IL2CPP managed conversion completed in 141 seconds.
- Native compilation failed after 485 seconds when Android's `mksh` treated
  `^` as a literal inside the Bash-style glob replacement used to sanitize
  relative paths. Slashes therefore survived in targets such as
  `obj/r__/_________.o`, whose unintended parent directory did not exist.
- `tools/ondevice-il2cpp/build-il2cpp.sh` now uses the pure-shell `!` bracket
  negation supported by both Bash and Android `mksh`, without adding a process
  per translation unit. CI contracts reject the former `^` syntax and any
  per-file `sed`; `bash -n` passes, and Thor itself maps `vm/Runtime.cpp` to
  `vm_Runtime.cpp`. Attempt 2 below verifies the replacement on the production
  device pipeline and closes this native object-name defect.
- The installed shell also exposed `SetupActivity` as `MAIN`/`LAUNCHER`, making
  the two-game selector unreachable, and displayed Silksong-specific setup
  copy for the selected Hollow Knight profile. Host regressions now require
  exported `LauncherActivity` to be the sole launcher entry point and require
  profile-specific Hollow Knight setup text. Attempt 2 below verifies both
  fixes in the installed shell.

## On-device attempt 2

- The package was updated in place without clearing its profile data, using an
  isolated APK built from commit `4c7d3d0`: 71,659,015 bytes, SHA-256
  `2a86391921e2e1691f1ff0fe598637a4bc81dc91449438c82c849f6bf9088c3a`.
  APK Signature Schemes v2 and v3 verify.
- Android resolves the app icon to `LauncherActivity`. The real rendered
  launcher exposes both game profiles and the `IMPORT SAVES` / `EXPORT SAVES`
  actions. Selecting Hollow Knight opens profile-specific setup copy with no
  Silksong text.
- The retained conversion reused its 803 generated C++ and 180 generated C
  inputs. On Thor, native phase A rebuilt 803 units in 482 seconds, phase B
  rebuilt 180 in 11 seconds, phase C rebuilt 363 Unity runtime units in 64
  seconds, then bdwgc, zlib, and Brotli completed in 2, 1, and 5 seconds.
- The fixed build pruned 83 stale objects produced by the broken sanitizer and
  linked 1,390 objects in 13 seconds. Total native duration was 587 seconds.
  The resulting `libil2cpp.so` is 267,628,408 bytes and was staged into the
  owned generation job.
- The next stage failed before classic conversion with
  `Addressables content root exceeds the catalog field`. `SetupActivity` was
  evaluating the Silksong catalog-root constraint before `PlayerImage` could
  branch to Hollow Knight's `CLASSIC_PLAYER` strategy.
- The source fix makes content-root allocation profile-layout-aware: classic
  player builds pass no Addressables root, while Addressables builds retain the
  exact 56-byte fail-closed guard. A focused regression covers the distinction.
  A replacement APK and resumed device run are required to publish and launch
  the first Hollow Knight generation.

## On-device attempt 3

- The package was updated in place from commit `7791431`, preserving the exact
  source, generated code, native object cache, and prior package state. Its APK
  is 71,659,015 bytes, SHA-256
  `3e04742ee70bfa8335abc7976974801fa7d6cc4b758c91c945c1e05575c6cde9`;
  APK Signature Schemes v2 and v3 verify.
- The retained native cache reported 803/803 generated C++, 180/180 generated
  C, and 363/363 Unity runtime units unchanged. It relinked 1,390 objects in 15
  seconds and completed the native stage in 47 seconds, producing the same
  267,628,408-byte `libil2cpp.so`.
- The layout-aware classic path passed the former Addressables blocker. The
  converter assembled a 4,939 MiB classic player image and packed 1,638 files
  into a 5,179,183,984-byte `data.apk`.
- Generation `gen-4414776c-58f0-41a0-88c8-8237279e9af3` published atomically.
  Its manifest records `data.apk` SHA-256
  `585bc8eb3197682761e41b253eeedd9df9844b61c98e5703c85e5e34e18c0f4c`
  and `libil2cpp.so` SHA-256
  `60ef6fdb9605e9a9014320349817309f302f726d2578d869554bab42adcb028d`.
  Independent on-device hashing matched every manifest entry, and `current`
  names that exact generation. Staging was empty after publication.
- `GameActivity` started and loaded `libmain.so`, `libunity.so`, and the
  generation's `libil2cpp.so`. Unity identified itself as 6000.0.61f1 on
  ARM64/API 33, then failed to read `assets/bin/Data/unity_app_guid` and showed
  the misleading storage-capacity dialog.
- This is not a capacity failure: 736 GB remained free. Toybox `unzip` opened
  the same archive and extracted the 36-byte GUID, proving that the entry and
  bytes exist. The failure boundary is Unity's `FileSystemAndroidAPK` reading a
  ZIP64 package larger than 4 GiB.
- The replacement host implementation follows Unity 6000.0.61f1's own split:
  first-scene resources remain in a ZIP32 `data.apk`, later data moves to a
  standard main OBB, and both contain the same `unity_obb_guid`. The exact live
  image census projects 3,575,609,029 bytes in the base container and
  1,603,332,315 bytes in the OBB, below ZIP32 and Android's 2 GiB OBB limit.
  The actual 5.18 GB split and Unity mount remain the next device gate.

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
- The older pre-sync isolated debug proof package and the attempt-1 package are
  superseded. Attempt 2 verifies the portable-shell and launcher-entry fixes;
  its APK must now be superseded by one containing the layout-aware classic
  content-root fix.
- Attempt 3 supersedes that content-root candidate and proves the monolithic
  ZIP64 layout is not readable by Unity even though Java and toybox can reopen
  it. The current packer disables ZIP64 for classic data, preserves Unity's
  exact first-scene rule, maps StreamingAssets to the archive `assets/` root,
  emits a generation-local `main.<version>.<package>.obb`, and validates OBB
  contents before atomic publication.
- The replacement isolated proof APK is 71,675,399 bytes, SHA-256
  `a9391355210ff5decc2a357be1ad32f0a0e2afbae2cf45165ede4db8c0ab5ba8`.
  Its v2/v3 signature and debug certificate
  `06197430e1a4ba85dca54f7a2ecf8a2db5cc2c2e76eb8f7516e8acf3d1e6a934`
  verify. It has not yet been installed.

## Verification gates

- Android host suite: 101 tests, 99 passed, zero failures/errors, two
  environment-gated skips. Each skipped real-source/player-image test was
  then run explicitly against the current inputs and passed.
- Bundle-surgery suite: 37/37 passed.
- Python CI contracts: 21/21 passed.
- Mod-weaver build: zero warnings and zero errors.
- Hollow Knight patch compilation against the exact game and Android player
  assemblies passed with one registered entry point.

## Cross-check and safe pause

- `COMPLETE`: the nine pending commits from parent remote `origin` were merged
  and reconciled against the dual-profile pipeline. Their BepInEx/Harmony mod
  support now uses the selected profile's build, conversion, and registration
  paths. The affected host, real-source, patch-compilation, and release-AAR
  gates were rerun successfully, and attempt 2 verifies the merged launcher
  and native-build route on Thor.
- `COMPLETE`: exact 1.5.12620 source acceptance and full real-tree conversion.
- `COMPLETE`: exact Android built-ins and project shader transformation.
- `COMPLETE`: Hollow Knight patch assembly, IL2CPP generation, ARM64 native
  compilation/link, ELF contract, classic player-image assembly, atomic
  generation publication, and independent installed-payload hashes.
- `COMPLETE`: a signed, isolated launcher APK containing the Hollow Knight
  profile route builds without proprietary game content.
- `COMPLETE`: the second Thor run verifies the portable object sanitizer and
  the real on-device native path through a 1,390-object ARM64 link and a
  267,628,408-byte `libil2cpp.so`.
- `COMPLETE`: attempt 3 closes the Addressables-root blocker, publishes the
  exact generation, and proves the Unity 6000.0.61f1 ARM64 engine and all three
  generation-owned native libraries load.
- `BLOCKER`: Unity cannot read the published 5.18 GB ZIP64 `data.apk`. The
  ZIP32 plus main-OBB replacement is host-covered and packaged; it must repack
  the retained image, publish a new generation, and prove Unity mounts both
  archives before any menu/gameplay claim.
- `BLOCKER`: Task 10 is not complete until serial `bfa98654` (AYN Thor,
  Android 13/API 33, ARM64) runs the isolated package, logs
  `[DualSouls][HK] injection probe loaded`, reaches the main menu, and enters a
  playable room with rendering, audio, and input verified.
- `DEFERRED`: launcher visual redesign using the already preserved supplied
  grids, heroes, logos, and icons. No image generation is permitted.
- `DEFERRED`: pinned per-game shortcuts with direct profile launch and the
  supplied individual Hollow Knight/Silksong icons. This follows the Task 10
  boot gate so shortcuts cannot advertise an unproven launch path.
- `DEFERRED`: reduce the private runtime base container from the exact Unity
  split's projected 3.58 GB toward roughly 1 GB by pairing later-scene `.resS`
  sidecars with their owners across main and patch OBBs. The installed launcher
  APK remains about 72 MB. This custom split must not replace the exact-layout
  POC until device evidence proves Unity resolves those sidecars across mounts.

Local proof artifacts remain under their exact `D:\Temp\dualsouls-hk-*`
roots for the next device run. They contain user-owned game-derived data and
must never be committed or published.
