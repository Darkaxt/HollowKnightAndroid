# Atomic Generation Verification — 2026-08-31

## Contract implemented

- Each production run owns `profiles/<profile>/staging/<job-id>`.
- Engine libraries, `libil2cpp.so`, `data.apk`, and the build marker are written
  below that private staging root.
- `generation.json` is written last and records profile/source/toolchain/patch
  identities plus SHA-256 for every payload file.
- Publication reopens and hashes every file, reads every APK/JAR/ZIP entry,
  atomically renames the staging tree into `generations`, revalidates the moved
  tree, and atomically replaces `current`.
- Cancellation is checked between sealed stages and while hashing file batches.
  The final atomic publication is allowed to finish; failure removes only the
  caller's job and never changes the previous pointer.
- The Unity shell resolves the same profile-scoped pointer. Its legacy staging
  installer writes only to the legacy package directory and cannot mutate an
  immutable generation.

## Verification

- `:app:testDebugUnitTest`: 84 tests, zero failures/errors, one intentional skip.
- `GenerationPublisherTest`: 11 cases.
- `ProfileBuildCoordinatorTest`: 3 cases.
- `:app:assembleRelease :app:collectRuntimeDeps`: green; 45 runtime JARs and
  dependency JNI directories for arm64-v8a, armeabi-v7a, x86, and x86_64.
- Release AAR: 12,781,276 bytes; SHA-256
  `54628a33329aad624af7d67158975617b7328673bce348828f7801748346f937`.
- `GameActivity.java` and `PlayerActivity.java` compile together against Android
  API 36 and the pinned Unity 6000.0.50f1 player classes.
- Guarded emulator lab: six fake-runtime unit tests and three API 35 x86-64
  instrumentation tests pass; rendered UI and profile-scoped storage checks
  pass on `emulator-5554`. The PowerShell guard now uses ASCII-only contract
  literals so Windows PowerShell 5.1 cannot corrupt the assertions.

## Cross-check classification

- `COMPLETE`: host implementation of atomic generation publication, payload
  verification, structured build stages, exact-job discard, profile-scoped
  reset integration, and cooperative cancellation.
- `BLOCKER`: an ARM64 device has not yet executed a full production generation
  or proved that a previous playable generation survives an injected real-build
  failure. This remains a device acceptance gate, not missing host logic.
- `DEFERRED`: Silksong records the stable registered source-contract hash because
  its older downloader does not persist the exact Steam manifest identity.
  Hollow Knight records the exact semantic source-manifest SHA-256. Persisting
  the Steam manifest identity belongs with the later downloader provenance work.
