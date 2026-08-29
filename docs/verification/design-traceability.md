# Unified Platform Design Traceability

Last cross-check: 2026-08-30, implementation baseline / Task 1.

States follow the design contract: `COMPLETE`, `BLOCKER`, `DEFERRED`, and
`NOT-STARTED`. `NOT-STARTED` means the planned milestone has not begun. The
Task 4 blocker prevents either Milestone 1 or Milestone 2 from closing, but the
approved local-first POC exception permits independent Tasks 5–7.

| Requirement | Planned milestone | State | Evidence or gap | Dependency | Acceptance test |
|---|---:|---|---|---|---|
| Host test harness: JUnit, Robolectric, Android resources, and patch compile check | 1 | COMPLETE | `docs/verification/host-poc-2026-08-30.md` records green Gradle and 32-source patch checks | Pinned Unity Android module and user-owned Silksong assemblies | Repeat the recorded Gradle command and `check.ps1` successfully |
| Evidence: Silksong Linux layout and Vulkan slices | Baseline | COMPLETE | Design evidence records Unity 6000.0.50f1 and 2,068 Addressables bundles | User-owned Linux source | Repeat census when registering a new source build |
| Evidence: Hollow Knight 1.5.12612 classic layout | Baseline | COMPLETE | Design evidence records 501 levels, 501 shared assets, and 503 parsed serialized files | User-owned Linux source | Converter POC repeats a full read-only census |
| Evidence: Hollow Knight Vulkan coverage | Baseline | COMPLETE | Design evidence records 125 of 125 shaders with Vulkan slices | User-owned Linux source | `manifest-classic-tree` reports zero missing Vulkan shaders |
| Evidence: Windows Hollow Knight is unsuitable | Baseline | COMPLETE | Design evidence records Direct3D 11 and no Vulkan slices | None | Validator returns `WINDOWS_SOURCE` for a synthetic Windows fixture |
| Evidence: production ABI is ARM64 only | Baseline | COMPLETE | Design and repository build configuration both specify `arm64-v8a` | None | APK/native artifact ABI inspection at release |
| Evidence: x86-64 emulator cannot prove the native pipeline | Baseline | COMPLETE | Design records the ABI mismatch and limits emulator evidence to fakes | None | No release row cites x86-64 as native/device proof |
| Candidate Hollow Knight 1.5.12620 support | 2 | NOT-STARTED | Candidate archive has not passed the new exact-manifest converter | Tasks 5–7 | Full read-only census, exact manifest, zero missing Vulkan, media/plugin report |
| Goal 1: one launcher APK and package identity | 4 | NOT-STARTED | Launcher selection milestone has not begun | Profiles and generations | Both profiles launch from one installed package |
| Goal 2: choose game before Unity | 4 | NOT-STARTED | Launcher selection milestone has not begun | Selected profile store | Robolectric selector test plus both switch directions on Thor |
| Goal 3: independent provision/validate/build/repair/reset/launch | 3–4 | NOT-STARTED | Build coordinator and switching have not begun | Profiles, manifests, generations | Isolation and reset matrix passes for both profiles |
| Goal 4: reuse on-device Vulkan architecture | 3 | NOT-STARTED | Hollow Knight build integration has not begun | Converter and toolchain registry | Hollow Knight reaches a playable room through the shared player |
| Goal 5: classic Hollow Knight converter | 2 | NOT-STARTED | Converter POC has not begun | Serialized transformer | All manifest-selected files convert and revalidate |
| Goal 6: shared mod and skin libraries with adapters | 5 | NOT-STARTED | Shared libraries have not begun | Game adapters | Shared discovery and per-game compatibility tests pass |
| Goal 7: multiple skins and death rotation | 5 | NOT-STARTED | Skin runtime has not begun | Lifecycle and skin adapters | Death selects and stable respawn applies with rollback |
| Goal 8: shared dual-screen Vulkan foundation | 6 | NOT-STARTED | Renderer extraction has not begun | Both game adapters | Silksong regression and Hollow Knight proof screen pass on Thor |
| Goal 9: exclude proprietary inputs/artifacts | Every stage | COMPLETE | Current tracked tree contains documentation/source only; rescan is mandatory at every gate | Ongoing discipline | Tracked-file and APK content scans find no game/Unity binaries |
| Goal 10: reproducible signed releases | 7 | NOT-STARTED | Release adaptation has not begun | All device gates | Fresh-download hash/signature/install verification passes |
| Non-goal: arbitrary cross-game mod binary compatibility | Boundary | COMPLETE | Design explicitly requires separate per-game compilation | None | No shared compiled game-dependent patch DLL exists |
| Non-goal: one compiled patch DLL for both games | Boundary | COMPLETE | Source-sharing boundary is explicit | None | Build manifests show separate profile patch outputs |
| Non-goal: mixing builds or operating systems | 2 | NOT-STARTED | Exact source validator has not begun | Exact manifest | Mixed synthetic tree returns `MIXED_BUILD` |
| Non-goal: ship or publish user game content | Every stage | COMPLETE | No game content is tracked at baseline | Ongoing scans | Repository/APK scans remain clean |
| Non-goal: retain native GLES3/EGL as primary renderer | 6 | NOT-STARTED | Shared Vulkan renderer work has not begun | Renderer extraction | Runtime evidence identifies Unity Vulkan/multi-display path |
| Non-goal: emulator as Thor proof | Boundary | COMPLETE | Evidence policy is explicit | None | Device-only rows cite serial `bfa98654` evidence |
| Non-goal: add x86-64 production pipeline | Boundary | COMPLETE | First implementation remains ARM64-only | None | No production x86-64 native artifacts are introduced |
| Architecture: dedicated launcher and one cold Unity process | 4 | NOT-STARTED | Process switching has not begun | Profile generations | Live-process mismatch is rejected without timeout |
| Architecture: launch request contains only profile ID | 4 | NOT-STARTED | Launcher integration has not begun | Selected profile store | Intent and startup-profile tests pass |
| Architecture: exact declarative game profiles | 1–3 | NOT-STARTED | Profile implementation has not begun | Task 2 | Registry and fail-closed lookup tests pass |
| Architecture: profile owns validation/toolchain/conversion/patch/save/features | 2–6 | NOT-STARTED | Contracts are introduced incrementally | Profile skeleton | Each field/adapter is linked to its owning milestone test |
| Architecture: unknown versions fail closed | 2 | NOT-STARTED | Exact validator has not begun | Profile manifests | Unknown fixture returns `UNKNOWN_VERSION` with exact property |
| Architecture: toolchains shared by content hash/version | 3 | NOT-STARTED | Toolchain registry has not begun | Exact descriptors | Wrong hash cannot affect another toolchain directory |
| Architecture: all game-specific storage is namespaced | 1 | NOT-STARTED | Profile path implementation has not begun | Task 3 | Containment and cross-profile path tests pass |
| Architecture: source depot remains untouched | 2–3 | NOT-STARTED | Validator/converter have not begun | Read-only adapters | Before/after source tree manifest and timestamps agree |
| Architecture: atomic generation publication and rollback | 3 | NOT-STARTED | Generation publisher has not begun | Profile paths | Injected-failure tests retain previous current generation |
| Architecture: shared provisioning state machine | 3 | NOT-STARTED | Build coordinator has not begun | Profiles, validator, converters | Structured stages execute and report profile-scoped evidence |
| Architecture: cooperative cancellation, no elapsed-time cancellation | 3 | NOT-STARTED | Coordinator has not begun | Atomic staging jobs | Cancellation test removes only its owned staging job |
| Architecture: Silksong Addressables conversion preserved | 1 | BLOCKER | Task 4 regression has not run; independent converter POC cannot close this gate | Existing Silksong Linux depot and Thor | `make test`, `make check`, launcher build/install, then force-stop/monkey and Play on `bfa98654` reproduce base path |
| Architecture: Hollow Knight classic discovery by content | 2 | NOT-STARTED | Classic converter has not begun | Serialized transformer and exact manifest | Extensionless levels parse and deterministic discovery passes |
| Architecture: every serialized file becomes Android/Vulkan | 2 | NOT-STARTED | Classic converter has not begun | Vulkan-bearing source | Full report shows target 13 and zero missing Vulkan |
| Architecture: built-ins replaced and sidecars preserved | 2–3 | NOT-STARTED | Conversion/package integration has not begun | Exact Unity player and converter | Sidecar hashes match; Android built-ins are installed |
| Architecture: plugins/media are inventoried before conversion policy | 2–3 | NOT-STARTED | Census reporting has not begun | Classic report schema | Report lists every plugin/media format and rejects unsupported plugins |
| Architecture: classic output is resumable and ZIP64-readable | 2–3 | NOT-STARTED | Resume/package work has not begun | Converter report and packager | Hash-keyed resume plus ZIP64 reopen tests pass |
| Architecture: shared patches contain no game-specific types | 5–6 | NOT-STARTED | Shared source extraction has not begun | Per-game adapters | Shared projects compile without either game assembly |
| Architecture: external mods resolve per profile and fail safely | 5 | NOT-STARTED | Mod resolver has not begun | Generation publisher | Compatibility/dependency/safe-mode tests pass |
| Architecture: skin scanner treats immediate children as packs | 5 | NOT-STARTED | Skin scanner has not begun | Manifest schema | Multiple valid siblings survive one invalid sibling |
| Architecture: duplicate skin IDs require safe resolution | 5 | NOT-STARTED | Skin registry has not begun | Tree hashes | Identical/different duplicate tests pass without flattening |
| Architecture: one active skin per game | 5 | NOT-STARTED | Activation store has not begun | Profile storage | Independent selection persists for both profiles |
| Architecture: death selects; stable respawn applies | 5 | NOT-STARTED | Rotation state machine has not begun | Lifecycle adapters | Ordered/shuffled/rollback state tests and both device checks pass |
| Architecture: shared Unity Vulkan multi-display renderer | 6 | NOT-STARTED | Renderer extraction has not begun | Shared patch core | Display 1, touch attribution, diagnostics tests pass |
| Architecture: game adapters supply screens/state only | 6 | NOT-STARTED | Adapters have not begun | Renderer boundary | Shared runtime references no game-specific types |
| Architecture: single-display fallback remains usable | 6 | NOT-STARTED | Fallback has not begun | Renderer abstraction | Host decision test and device fallback smoke pass |
| Branding: diagonal combined identity and adaptive safe zone | 7 | NOT-STARTED | Visual task has not begun | Licensed/original artwork | Circle/squircle/rounded/OEM plus monochrome review passes |
| Recovery: validation never writes to source | 2 | NOT-STARTED | Validator has not begun | Read-only implementation | Before/after source metadata comparison passes |
| Recovery: generated artifacts stage and verify before publish | 3 | NOT-STARTED | Generation publisher has not begun | Atomic writer | Injected failures never update current pointer |
| Recovery: resume/discard exact staging job only | 2–3 | NOT-STARTED | Resume/coordinator work has not begun | Content hashes and job IDs | Interruption test preserves source and other jobs |
| Recovery: unsupported/corrupt input fails before replacement | 2–3 | NOT-STARTED | Validators have not begun | Exact manifests | Error matrix retains previous generation |
| Recovery: logs identify profile/stage/artifact without credentials | 3 | NOT-STARTED | Structured logging has not begun | Coordinator | Redaction and structured-event tests pass |
| Recovery: reset is profile-scoped | 3–4 | NOT-STARTED | Reset refactor has not begun | Profile paths | Reset matrix preserves other game/library/saves/credentials |
| Recovery: encrypted Steam credential boundary is retained | 1–4 | NOT-STARTED | Profile refactor has not been regression-tested | Existing encrypted store | Login survives refactor without plaintext storage/logging |
| Host test: profile selection and manifest validation | 1–2 | NOT-STARTED | Behavior tests have not begun | Tasks 2 and 7 | Registry, selection, and exact-manifest suites pass |
| Host test: platform/version rejection | 1–2 | NOT-STARTED | Behavior tests have not begun | Tasks 2 and 7 | Windows and unknown-version cases pass |
| Host test: classic discovery/sidecars | 2 | NOT-STARTED | Converter tests have not begun | Task 6 | Legal synthetic fixture suite passes |
| Host test: Vulkan coverage/target rewrite | 2 | NOT-STARTED | Transformer tests have not begun | Task 5 | Synthetic missing-Vulkan and output-reopen tests pass |
| Host test: atomic generation/recovery | 3 | NOT-STARTED | Publisher tests have not begun | Task 9 | Injected-failure suite passes |
| Host test: mod/skin manifests and safe mode | 5 | NOT-STARTED | Library tests have not begun | Tasks 12–14 | Compatibility/collision/safe-mode suites pass |
| Host test: death-to-respawn rotation | 5 | NOT-STARTED | Rotation tests have not begun | Task 13 | State-transition suite passes |
| Host test: launcher/settings/reset isolation | 1–4 | NOT-STARTED | Storage and launcher tests have not begun | Tasks 3, 9, 11 | Robolectric isolation suite passes |
| Emulator: Robolectric covers fakeable UI/state behavior | 1–5 | NOT-STARTED | Runner bootstrap is in progress | Host test harness | Named Robolectric suites pass on the host |
| Emulator: x86-64 is optional and not native evidence | Boundary | COMPLETE | Design and plan explicitly exclude it as release proof | None | Traceability cites no x86 native/device claims |
| Device gate 1: clean provisioning for both Linux sources | Release | NOT-STARTED | Device matrix has not begun | Both profiles | Clean source-to-ready run passes per profile |
| Device gate 2: exact toolchain and on-device IL2CPP | Release | NOT-STARTED | Device matrix has not begun | Toolchain registry | Logs and artifact hashes prove exact components |
| Device gate 3: menu and representative gameplay | Release | NOT-STARTED | Device matrix has not begun | Playable generations | Both profiles enter gameplay |
| Device gate 4: rendering/audio/video/input/save/resume | Release | NOT-STARTED | Device matrix has not begun | Playable generations | Recorded functional matrix passes |
| Device gate 5: representative scene coverage | Release | NOT-STARTED | Device matrix has not begun | Stable gameplay | Menu/map/boss/effects/dependency-heavy checks pass |
| Device gate 6: single and Thor dual display | Release | NOT-STARTED | Renderer milestone has not begun | Shared renderer | Both modes pass on `bfa98654` |
| Device gate 7: display-specific touch | Release | NOT-STARTED | Renderer milestone has not begun | Input adapter | Touch attribution evidence passes |
| Device gate 8: base/safe/mod/skin/rotation matrix | Release | NOT-STARTED | Feature milestones have not begun | Mods and skins | Full feature matrix passes for both profiles |
| Device gate 9: switch only after old Unity exits | Release | NOT-STARTED | Switching has not begun | Cold-process model | Both directions prove old PID gone |
| Device gate 10: update/rollback without loss | Release | NOT-STARTED | Generation/update work has not begun | Atomic publisher | Update and rollback preservation matrix passes |
| Release gate: repository/APK contain no proprietary content | 7 | NOT-STARTED | Baseline repository is clean; APK gate has not begun | Final artifact | Tracked-file and APK scans pass |
| Release gate: clean CI builds and tests | 7 | NOT-STARTED | Unified CI has not begun | Non-proprietary harness | Clean-checkout workflow passes |
| Release gate: signing secrets stay out of source/logs | 7 | NOT-STARTED | Signing adaptation has not begun | Repository secrets | Published workflow logs/artifacts pass secret scan |
| Release gate: numeric identifiers all agree | 7 | NOT-STARTED | Unified versioning has not begun | Release workflow | APK/tag/title/filename assertions pass |
| Release gate: downloaded APK is reverified and exercised | 7 | NOT-STARTED | Publication has not begun | Signed release | Fresh-download hash/signature/update/both-profile smoke passes |
