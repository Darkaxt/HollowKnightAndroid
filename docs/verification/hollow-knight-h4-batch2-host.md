# H4 Batch 2 integrated host verification — 2026-09-05

## Result and boundary

**588 passed, 2 explicitly assumption-skipped, 10 excluded; not a full-suite-green claim.** Source inventory contains 63 test classes / 600 methods. Retained XML reconciles by exact `(classname, method)` to 590 unique cases across 62 classes, with zero final failures/errors. There are no additional pending applicable classes, parameterized expansions, or ignored cases. The 107 repeated cases were prerequisite-failure retries, not additional coverage.

This supports the **conditional host-UI checkpoint**, not complete plan Task 2, production skins, Android runtime integration, corpus acceptance, or device acceptance. Production import/mutation/mode-advance bindings remain unavailable; default session observation is **UNKNOWN**, not a manufactured CLEAR. `H4-STORAGE-RETENTION-001` and `H4-STORAGE-GC-002` remain deferred in `hollow-knight-skins-deferrals.md`; admission tests do not establish a completed retention graph or collector. Production acquisition/launch wiring, cores/runtime and corpus gates remain outstanding. The ledger was not edited.

The verification worker added only this QA document to repository-visible work; it made no production/test source edits, commits, pushes, fetches, network requests, device/ADB/emulator runs, signing, releases, or packaging invocations. The coordinating thread subsequently archived the evidence and completed the bounded generated-output cleanup below before the coherent Batch 2 commit/push.

## Provenance and environment

- Worktree: `D:/Temp/HollowKnightAndroid-h1`, branch `feature/h4-skins-host`, HEAD `02a5f0982d8e3891d3fea8bc721c673df53368d0`, with the existing uncommitted integrated Batch 2 changes. Evidence is for this worktree, not the HEAD tree alone.
- Cached baseline supplied by the coordinating thread: `fork/master` = `00627e3a21e953565402a103ef5a4cb7f775e575`, `origin/master` = `4e21f48dd8dde3fd1290cf629b28bf2b9dc0dc37`; cached divergence 75 ahead / 5 behind. No fetch/integration; **not fresh remote truth**.
- Windows 11 host; Zulu OpenJDK `21.0.12.1+1-LTS`; `JAVA_HOME=C:\Program Files\Zulu\zulu-21\`; Unity-provided Gradle 8.11; Robolectric 4.14.1; compileSdk 36.
- Offline SDK 28 and 35 jars were already staged. Broad legacy tests additionally required SDK 26, copied from local `C:/Users/darka/.m2/repository/org/robolectric/android-all-instrumented/8.0.0_r4-robolectric-r1-i7/android-all-instrumented-8.0.0_r4-robolectric-r1-i7.jar` (94,512,384 bytes) into the same flat offline directory. No download or configuration edit.
- Runs started `2026-09-05T00:55:53Z`; last run finished approximately `01:10:14Z`. Every invocation had a Python subprocess timeout of 180 seconds. `--no-daemon` isolated the invocation's single-use daemon; timeout handling was limited to its exact process descendants. **No timeout or process termination was needed.**
- Fresh filtered test execution, without `--rerun-tasks`: compilation tasks were `UP-TO-DATE`. This does not claim a forced clean compilation. Existing compileSdk/AGP compatibility warning remained nonfatal.

## Results by bounded invocation

| Retained group | Cases | Passed | Failed | Skipped | Wall seconds |
| --- | ---: | ---: | ---: | ---: | ---: |
| 01-launcher-regression | 160 | 53 | 107 prerequisite failures | 0 | 29.21 |
| 02-launcher-sdk26-retry | 107 | 105 | 0 | 2 | 71.39 |
| 03-session-recovery-sequence | 37 | 37 | 0 | 0 | 47.08 |
| 04-session-contracts | 86 | 86 | 0 | 0 | 41.24 |
| 05-registry-quota | 110 | 110 | 0 | 0 | 37.72 |
| 06-host-ui | 60 | 60 | 0 | 0 | 36.14 |
| 07-import-format-contracts | 71 | 71 | 0 | 0 | 50.82 |
| 08-object-builder-quarantine | 27 | 27 | 0 | 0 | 47.83 |
| 09-storage-containment | 22 | 22 | 0 | 0 | 69.74 |
| 10-publisher-recovery | 7 | 7 | 0 | 0 | 105.23 |
| 11-publisher-publication | 7 | 7 | 0 | 0 | 87.33 |
| 12-publisher-regressions | 3 | 3 | 0 | 0 | 68.05 |

Group 01's 107 failures all occurred before test bodies: `IllegalArgumentException: Path is not a file: .../robolectric-offline/android-all-instrumented-8.0.0_r4-robolectric-r1-i7.jar` at `LocalDependencyResolver.java:50`. After staging that exact cached artifact, only the affected classes were retried in group 02. Their failures were superseded by 105 passes and two documented assumption skips. The initial failed XML/logs remain available; these are harness prerequisite failures, not concealed code failures.

Broad launcher union: **158 passed + 2 skipped** across build publication/coordinator/toolchains, profiles/paths/settings/selection/source validation, launcher profile selection, Mods, display/mutation coordination, shortcuts, runtime eligibility/provider/production/startup, and test environment contracts.

Preserved session gate: **123/123** — recovery 27, sequence 10, launch coordinator 27, session store 19, bridge protocol 14, process identity 11, acquisition intent 6, descriptor codec 9. This includes durable monotonic allocation, restart/crash-cut recovery, one-use bridge authorization, raw-token nonpersistence, and `hasNoProductionLeaseEstablishmentOrAcquisitionWiring`. Descriptor **codec** coverage does not replace the excluded descriptor **builder** class.

Registry/quota: import coordinator 32, library reader 6, mutations 14, registry recovery 8, registry store 18, quota/admission 32. UI: 60 across all ten current classes. Publisher coverage is the disjoint **7 + 7 + 3 = 17** exact-method union, with no per-class cap or omitted slow method.

## Explicit non-passing inventory

1. **Excluded, not executed:** `dev.silksong.launcher.skins.session.SkinDescriptorBuilderTest`, all 10 methods. Sole explicit class exclusion authorized for the previously localized Windows `Files.getFileStore` stall; not reproduced in this slice. Requires a separate bounded compatible-host verification gate.
2. **Executed and assumption-skipped:** `HollowKnightRealSourceIntegrationTest.current_source_report_matches_the_committed_exact_manifest`. Requires both `HOLLOW_KNIGHT_SOURCE_ROOT` (prepared exact Hollow Knight 1.5.12620 source tree) and `HOLLOW_KNIGHT_INVENTORY_REPORT` (matching existing inventory JSON); both were unset.
3. **Executed and assumption-skipped:** `HollowKnightRealSourceIntegrationTest.current_classic_player_image_splits_into_zip32_apk_and_unity_obb`. Requires `HOLLOW_KNIGHT_PLAYER_IMAGE_ROOT`, with prepared `image/globalgamemanagers` and `image/Managed/Metadata/global-metadata.dat`; unset. Its body calls `PlayerImage.install` and creates package output, so activating it is outside this no-packaging gate.

The authorized read-only `G:/Modding/Downloads/Hollow Knight` top-level inventory contained skin archives/listing, not deterministically matched prepared source/report/player-image inputs. No archive extraction or asset processing was attempted. These skips are external-prerequisite/scope limitations, not a claim that public game/project inputs are forbidden or unavailable in principle.

## Exact command environment and filters

Existing instructions were checked in `docs/verification/host-poc-2026-08-30.md`, `docs/superpowers/plans/2026-09-03-hollow-knight-skins.md`, and the launcher app Gradle file. Every run used this command prefix, adding one `--tests` per argument below. `run_group.py` enforces `Popen.wait(timeout=180)` and snapshots results before another invocation. No shell-wide Java/Gradle kill is used.

```bash
export JAVA_TOOL_OPTIONS='-Drobolectric.offline=true -Drobolectric.dependency.dir=D:/Temp/HollowKnightAndroid-h1/src/SilksongLauncher.Launcher/app/build/robolectric-offline'
# Exact prefix used by the retained bounded runner:
java -classpath 'D:/Temp/dualsouls-unity-player/android/Tools/gradle/lib/gradle-launcher-8.11.jar' \
  org.gradle.launcher.GradleMain \
  -p 'D:/Temp/HollowKnightAndroid-h1/src/SilksongLauncher.Launcher' \
  --offline --no-daemon :app:testDebugUnitTest --tests '<filter>'
```

The following reproduces the exact group filters through the retained bounded runner. `q` expands each class/method argument with `dev.silksong.launcher.`; the JSON record in each group also stores the full actual command array. Use a new artifact directory/group name for a rerun: existing snapshot names deliberately refuse overwriting.

```bash
q() {
  local group="$1"; shift
  local filters=()
  for filter in "$@"; do filters+=("dev.silksong.launcher.$filter"); done
  python 'D:/Temp/HollowKnightAndroid-h1/src/SilksongLauncher.Launcher/app/build/h4batch2-qa-20260905/run_group.py' "$group" "${filters[@]}"
}
q 01-launcher-regression 'build.*Test' 'profiles.*Test' 'runtime.*Test' 'shortcuts.*Test' TestEnvironmentTest LauncherProfileSelectionTest ModsDisplayCoordinatorTest ModStateMutationTest ModsTest
q 02-launcher-sdk26-retry build.ProfileBuildCoordinatorTest LauncherProfileSelectionTest ModStateMutationTest ModsTest profiles.HollowKnightBuildPlanTest profiles.HollowKnightRealSourceIntegrationTest profiles.ProfileManifestAssetTest profiles.ProfileSettingsStoreTest profiles.SelectedGameStoreTest profiles.SilksongRegressionTest runtime.GameProcessStartupTest runtime.ProductionLauncherRuntimeTest shortcuts.GameShortcutControllerTest TestEnvironmentTest
q 03-session-recovery-sequence skins.session.SkinSessionRecoveryTest skins.session.SkinSessionSequenceTest
q 04-session-contracts skins.session.SkinLaunchCoordinatorTest skins.session.SkinSessionStoreTest skins.session.SkinSessionBridgeProtocolTest skins.session.ProcessIdentityTest skins.session.SkinAcquisitionIntentTest skins.session.SkinLaunchDescriptorTest
q 05-registry-quota 'skins.registry.*Test' 'skins.quota.*Test'
q 06-host-ui 'skins.ui.*Test'
q 07-import-format-contracts 'skins.catalog.*Test' 'skins.documents.*Test' skins.SkinRotationStateMachineTest skins.SkinTaskOneApiContractTest skins.importing.BoundedZipReaderTest skins.importing.PngValidationTest skins.importing.SkinCandidateDiscoveryTest skins.importing.SkinCatalogMapperTest skins.importing.SkinNormalizerTest skins.importing.ZipPathAuthorityTest
q 08-object-builder-quarantine skins.importing.SkinObjectBuilderTest skins.importing.SkinQuarantineTest
q 09-storage-containment skins.storage.DurableDirectoryPublisherTest skins.storage.SkinFileSystemContainmentTest skins.storage.SkinTreeVerifierTest
q 10-publisher-recovery \
  'skins.storage.SkinObjectPublisherTest.post-rename cleanup faults retain durable ownership for bounded recovery' \
  'skins.storage.SkinObjectPublisherTest.recovery retries a failed deletion parent barrier before dropping ownership' \
  'skins.storage.SkinObjectPublisherTest.recovery removes incomplete ownership staging after record creation crashes' \
  'skins.storage.SkinObjectPublisherTest.recovery closes a committed ownership record before shard parents exist' \
  'skins.storage.SkinObjectPublisherTest.recovery retries a failed ownership record deletion barrier' \
  'skins.storage.SkinObjectPublisherTest.recovery preserves referenced and reused roots and removes unreferenced owned roots' \
  'skins.storage.SkinObjectPublisherTest.recovery rejects ownership record overflow before reading any record'
q 11-publisher-publication \
  skins.storage.SkinObjectPublisherTest.publishesReceiptThenObjectOnlyForAcceptedBuild \
  'skins.storage.SkinObjectPublisherTest.idempotent reuse reports no created roots and can never delete existing immutable roots' \
  'skins.storage.SkinObjectPublisherTest.discard never deletes a root reused by a later publication' \
  'skins.storage.SkinObjectPublisherTest.failed CAS cleanup deletes only newly-created unreferenced digests' \
  'skins.storage.SkinObjectPublisherTest.receipt object and barrier failures always remove ephemeral roots and owned immutable orphans' \
  'skins.storage.SkinObjectPublisherTest.object failure after receipt reuse retains the pre-existing receipt and removes ephemeral root' \
  'skins.storage.SkinObjectPublisherTest.filesystem inspection failure is returned and still removes ephemeral root'
q 12-publisher-regressions skins.storage.SkinObjectPublisherTest.failedBCleanupPreservesOlderAOwnershipAndRoots skins.storage.SkinObjectPublisherTest.cleanupEvidenceNeverUsesUnboundedListing skins.storage.SkinObjectPublisherTest.corruptLaterRecordPreservesPendingEvidenceAndEarlierRoots
```

## Retained artifacts / handoff

- Retained local archive: `D:/Temp/h4-batch2-qa-20260905-02a5f09.zip` — 104 files, 147,369 bytes, SHA-256 `e907d8e08ae681be7cde59f71b08d84fb00c8872899f21942a6ddf0a203fd7ff`. ZIP integrity was verified before cleanup. This is local evidence, not a published repository asset.
- The archive preserves the original `app/build/h4batch2-qa-20260905/` snapshots and runner. Each numbered group contains `gradle.log`, `result.json` (command, environment, timestamp, elapsed time, exit status, per-class XML totals), and `xml/TEST-*.xml`.
- `coverage-union.json` retains all 600 discovered source methods, 590 unique XML identities with latest outcomes, and the exact 10 excluded methods. The coordinating thread independently reconciled the XML union; the only overlap is the 107 prerequisite retries.
- Transactional cleanup `3b1b55189b2e6877d2fc0fe4f2f5024a` reached `applied`: 2,437 generated entries deleted, 443,952,429 bytes reclaimed, no reported errors or residual helper files. Coverage was restricted to launcher `app/build`, `.kotlin`, and `.gradle`; it was not a whole-host scan. The external archive and original cached SDK jars were outside those roots.
- Original build snapshots, final Gradle XML, staged offline jars, and worktree build caches no longer exist. Restore the archived runner to a new evidence location and restage cached SDK jars before reproducing the commands above. Final Gradle XML represented only the last three-method group, never the aggregate.
- No further applicable class execution is pending in this slice; the descriptor-builder and two prerequisite gates remain explicitly unclosed.
