# Unity Toolchain Registry Verification — 2026-08-31

## Scope

This is the host checkpoint for parent-plan Task 8. It proves exact descriptor
selection, immutable content-addressed toolchain roots, staged-download
isolation, and `classes.jar`-content-addressed dex output. It does not claim an
ARM64 on-device IL2CPP build or Hollow Knight gameplay.

## Primary metadata

The exact release records were read from Unity's public release API:

- `https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.0.50f1`
- `https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.0.61f1`

The corresponding Unity release pages identify changesets `f1ef1dca8bff` and
`74a0adb02c31`. The API supplied the URLs, byte counts, and MD5 integrity values
below. SHA-256 was independently derived by streaming each exact official CDN
object through .NET `IncrementalHash`; no archive was retained.

| Unity | Component | Bytes | Unity MD5 | Derived SHA-256 |
|---|---|---:|---|---|
| 6000.0.50f1 | Linux editor tar.xz | 4,501,932,484 | `a285e4378dc4c2c78cc3cfbaac388f4a` | `076a2f975c9b807f5b9d9c560ac3cbb8202653c71a721ae5ad6e61d9b46e0d9b` |
| 6000.0.50f1 | macOS Android module pkg | 673,712,656 | `8dfad5f83024fa533ac02b58a83d0898` | `839de4ae756852b9f2ae9e193082b6d5d790bece180d616a6f40df3acf841e7d` |
| 6000.0.61f1 | Linux editor tar.xz | 4,456,301,920 | `2f7a0ad3bb329d5be266c8a164b136cf` | `cf6182370a5c8911bc750122ee033d01d43e8bcf9348a930ab97f40831eef171` |
| 6000.0.61f1 | macOS Android module pkg | 675,183,137 | `0bf2ccdd763b2099183c20d6ee3f6023` | `af590a00ab049870c90b164ca86c1f245c01f2c22511f2a51148c566fa22afd5` |

The on-device editor fetch deliberately stops after the required archive trees
have passed, so it cannot validate a full-archive digest without downloading
the unused multi-gigabyte suffix. It instead verifies every executable/input
file it retains. For 6000.0.61f1, those independently extracted hashes are:

| Retained editor file | SHA-256 |
|---|---|
| `Editor/Data/il2cpp/build/deploy/il2cpp.dll` | `1dce82179954a6edbeb9c71cc20f05f2de3e22690c31078ee7acaa85aad0a1fe` |
| `Editor/Data/il2cpp/libil2cpp/il2cpp-config.h` | `50c73a112814ed24a9c36c067f5f0bc4fc906657bdb4d185335a15be28e2cd6d` |
| `Editor/Data/MonoBleedingEdge/lib/mono/unityaot-linux/mscorlib.dll` | `ac34797a4113d642776394e192cededc7de5b781761de8fac3725a75aa783e9b` |

## Implemented contract

- Both registered profiles resolve an exact immutable descriptor; unknown
  versions and altered profile copies fail closed.
- Toolchains live under `files/toolchains/<descriptor-content-hash>`.
- A bad cached Android module is deleted only from its owning `staging`
  directory; another toolchain root is not inspected or modified.
- Android modules are verified with SHA-256 before extraction.
- Editor output is verified by the hashes of the exact retained files before
  publication.
- Unity dex output lives under `files/unity-dex/<classes.jar-sha256>`; timestamps
  no longer determine cache identity.
- Process-start injection resolves only the selected profile's registered
  descriptor and matching dex output.

## Verification

- Focused registry suite: 6 passed, 0 failed, 0 skipped.
- Full Android host suite: 75 tests, 0 failures, 0 errors, 1 intentional skip.
- Converter suite: 36 passed, 0 failed.
- CI contract suite: 9 passed, 0 failed.
- Release AAR plus runtime-dependency collection: successful; 45 dependency
  jars before the Mono copy and four dependency-JNI ABI directories.
- Release AAR: 12,738,829 bytes,
  SHA-256 `dc0f7f1b503a27b4fb6fd62f13ca5e12a1b20a0b3573f677c18f059030b7e09e`.

The existing Android Gradle Plugin warning remains unchanged: AGP 8.7.3 is
tested through compileSdk 35 while this project compiles against 36.

## Cross-check

- `COMPLETE`: exact profile-to-toolchain selection, official URLs/sizes,
  independent SHA-256 pins, content-addressed roots, cache isolation, and
  content-addressed Unity dex.
- `BLOCKER`: neither descriptor has yet completed the real ARM64 on-device
  IL2CPP/player build and gameplay gate.
- `DEFERRED`: adoption of a legacy global external `unity/` cache into the new
  internal content-addressed root must be exercised as part of the controlled
  device preservation/adoption procedure; no device data was moved here.
