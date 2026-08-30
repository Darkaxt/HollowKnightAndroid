# Dual Souls Emulator Test Instance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a dedicated API 35 x86-64 Dual Souls lab that runs the real launcher/profile/storage logic with deterministic substitutes only for the ARM64 Unity/Mono/IL2CPP boundary.

**Architecture:** Keep `:app` as the production Android library and add a separate, non-publishable `:emulator-test-app` application that depends on it. A fail-closed manifest-selected `LauncherRuntime` seam supplies either the unchanged production operations or a lab runtime; shared atomic generations and profile state live in the production library, while the synthetic activity and fixtures live only in the lab module.

**Tech Stack:** Kotlin 2.1, Android Gradle Plugin 8.7.3, Android API 35/36, JUnit 4, Robolectric 4.14.1, AndroidX Test, Espresso, PowerShell, Android Emulator/WHPX, ADB.

**Authorization:** `already_authorized` by the user's 2026-08-30 instruction to continue until completion or a genuine blocker.

**Authoritative specification:** `docs/superpowers/specs/2026-08-30-dual-souls-emulator-test-instance-design.md`

---

## File map

- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/InstalledGeneration.kt`: immutable description of a published generation.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/GenerationPublisher.kt`: exact staging ownership, validation, atomic directory publication, current-pointer replacement, recovery, and profile-scoped clearing.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/LauncherRuntime.kt`: shared runtime request/state/progress contract.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/LauncherRuntimeProvider.kt`: fail-closed production/lab selection from exact package and manifest metadata.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/ProductionLauncherRuntime.kt`: current device behavior behind the runtime contract.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/ProductionBuildSignature.kt`: production build-input signature shared with the authoritative setup path.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfileSettingsStore.kt`: profile-scoped launcher settings with one-time Silksong legacy adoption.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/LauncherActivity.kt`: common game selector, runtime banner, readiness, and launch routing.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SetupActivity.kt`: common setup UI using the runtime contract.
- `src/SilksongLauncher.Launcher/emulator-test-app/`: non-release lab application, fake runtime, synthetic game process, resources, and instrumentation.
- `tools/emulator/New-DualSoulsLabAvd.ps1`: exact AVD create/validate operation.
- `tools/emulator/Start-DualSoulsLabAvd.ps1`: exact-name start and observable readiness.
- `tools/emulator/Get-DualSoulsLabDevice.ps1`: exact AVD-name-to-ADB-serial resolution.
- `tools/emulator/Test-DualSoulsLab.ps1`: build/install/instrument/package contract entry point.
- `docs/verification/design-traceability.md`: cumulative `COMPLETE`, `BLOCKER`, `DEFERRED`, and `NOT-STARTED` reconciliation.

## Stage 1: Atomic profile generations

**Specification coverage:** synthetic provisioning and generations; recovery integration; profile-scoped reset safety.

### Task 1: Publish and recover exact generations

**Files:**
- Modify: `src/SilksongLauncher.Launcher/.gitignore`
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/build/GenerationPublisherTest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/InstalledGeneration.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/GenerationPublisher.kt`

- [x] **Step 1: Write failing generation tests**

Cover these concrete behaviors with real temporary directories: a verified staged directory becomes `generations/<id>` and atomically replaces `current`; a missing `generation.json` is rejected; an existing different generation is never overwritten; a failed publish retains the previous pointer; exact-job discard does not touch a sibling; clearing Hollow Knight generations preserves Silksong.

```kotlin
@Test fun `publish replaces current only after verification`() {
    val publisher = GenerationPublisher(paths)
    val staged = publisher.begin("job-1", "gen-1")
    File(staged, "generation.json").writeText("{\"profileId\":\"hollow-knight\",\"generationId\":\"gen-1\"}")
    val installed = publisher.publish("job-1", "gen-1")
    assertEquals("gen-1", installed.id)
    assertEquals("gen-1", paths.currentPointer.readText())
}
```

- [x] **Step 2: Run the focused test and verify RED**

Run from `src/SilksongLauncher.Launcher`:

```powershell
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.build.GenerationPublisherTest
```

Expected: compilation fails because `GenerationPublisher` and `InstalledGeneration` do not exist.

- [x] **Step 3: Implement the generation API**

Use registered-profile paths only, identifiers matching `[a-z0-9][a-z0-9._-]{0,63}`, a required UTF-8 `generation.json`, same-filesystem `Files.move(..., ATOMIC_MOVE)`, and a temporary `current.next` pointer moved atomically over `current`. If atomic move is unsupported, fail without changing `current`; do not silently downgrade. Recursive removal must reject symbolic links as directories and operate only below the exact profile staging/generation roots.

```kotlin
data class InstalledGeneration(val id: String, val root: File)

class GenerationPublisher(private val paths: ProfilePaths) {
    fun begin(jobId: String, generationId: String): File
    fun publish(jobId: String, generationId: String): InstalledGeneration
    fun current(): InstalledGeneration?
    fun discard(jobId: String): Boolean
    fun clearPublished(): Long
}
```

- [x] **Step 4: Run focused and full host tests**

```powershell
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.build.GenerationPublisherTest
.\gradlew.bat :app:testDebugUnitTest
```

Expected: all tests pass.

- [x] **Step 5: Reconcile Stage 1 and commit**

Re-read the specification sections “Synthetic provisioning and generations,” “Recovery integration,” and “Failure and safety behavior.” Update the emulator row and atomic-generation rows in `docs/verification/design-traceability.md`; unresolved production adoption remains `BLOCKER` and production publisher integration is a `DEFERRED` dependency of the parent Task 9.

```powershell
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/build docs/verification/design-traceability.md
git commit -m "feat: publish atomic profile generations"
```

## Stage 2: Fail-closed runtime seam

**Specification coverage:** runtime seam; production behavior unchanged; package/metadata safety.

### Task 2: Select the runtime only from exact lab identity

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/runtime/LauncherRuntimeProviderTest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/LauncherRuntime.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/LauncherRuntimeProvider.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/ProductionLauncherRuntime.kt`

- [x] **Step 1: Write failing provider tests**

Assert that no metadata yields `ProductionLauncherRuntime`; the exact lab package plus an implementation name loads it; the production package plus lab metadata throws; the lab package with missing/malformed/unassignable class throws; and no ABI, model, or debuggable flag participates in selection.

```kotlin
@Test fun `production package cannot request lab runtime`() {
    assertFailsWith<IllegalStateException> {
        LauncherRuntimeProvider.resolve(
            packageName = "io.github.darkaxt.dualsouls",
            className = FakeRuntime::class.java.name,
            classLoader = javaClass.classLoader!!,
        )
    }
}
```

- [x] **Step 2: Run the focused test and verify RED**

```powershell
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.runtime.LauncherRuntimeProviderTest
```

Expected: compilation fails because the runtime contract/provider do not exist.

- [x] **Step 3: Implement the contract and provider**

```kotlin
enum class EvidenceKind { ARM64_DEVICE, EMULATOR_FAKE }
data class RuntimeState(val ready: Boolean, val generationId: String?, val detail: String)
data class RuntimeProgress(val stage: String, val fraction: Float, val detail: String)
data class RuntimeRequest(
    val context: Context,
    val profile: GameProfile,
    val paths: ProfileBuildPaths,
)
sealed interface ProvisionSource {
    data class Local(val depot: File) : ProvisionSource
    data class Steam(
        val credentials: TokenStore.Credentials,
        val depot: File,
        val staging: File,
    ) : ProvisionSource
    data object Synthetic : ProvisionSource
}
data class ProvisionRequest(val runtime: RuntimeRequest, val source: ProvisionSource)
interface LauncherRuntime {
    val evidenceKind: EvidenceKind
    fun inspect(request: RuntimeRequest): RuntimeState
    suspend fun provision(request: ProvisionRequest, emit: suspend (RuntimeProgress) -> Unit): RuntimeState
    fun reset(request: RuntimeRequest): Long
    fun gameIntent(request: RuntimeRequest): Intent
}
```

Use manifest key `io.github.darkaxt.dualsouls.LAUNCHER_RUNTIME`. Alternate loading is legal only for package `io.github.darkaxt.dualsouls.emutest`; otherwise throw. Default directly to a production singleton.

- [x] **Step 4: Run focused and full host tests**

```powershell
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.runtime.LauncherRuntimeProviderTest
.\gradlew.bat :app:testDebugUnitTest
```

- [x] **Step 5: Reconcile Stage 2 and commit**

Verify that the fake implementation is not present in `:app` and production cannot fall back to it. Record any production pipeline extraction still required by Stage 3 as a `BLOCKER`, not a completed seam.

```powershell
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/runtime docs/verification/design-traceability.md
git commit -m "feat: add fail-closed launcher runtime seam"
```

## Stage 3: Common launcher and setup behavior

**Specification coverage:** real shared activities; profile selection; readiness/provision/reset/launch routing; profile settings isolation.

### Task 3: Route production operations through the runtime contract

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/ProfileSettingsStoreTest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/runtime/ProductionLauncherRuntimeTest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfileSettingsStore.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/ProductionBuildSignature.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SetupActivity.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SettingsStore.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SettingsActivity.kt`

- [x] **Step 1: Write failing isolation and production-delegation tests**

Test independent Hollow Knight/Silksong launcher settings, one-time adoption of legacy `launcher_settings` into Silksong only, runtime reset preserving the other profile, and a recording runtime receiving the exact selected `RuntimeRequest` from setup orchestration.

- [x] **Step 2: Run the focused tests and verify RED**

```powershell
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.profiles.ProfileSettingsStoreTest
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.runtime.ProductionLauncherRuntimeTest
```

- [x] **Step 3: Resolve production build ownership**

The extraction was evaluated through Stage 5 but was not the authoritative
production path. Stage 6 chose the plan's safe alternative: retain the proven
`SetupActivity` operation order/catch/finally/screen-on/source/`.built`
contract, remove the unused duplicate, keep `ProductionBuildSignature` as a
separate shared utility, and make production runtime provisioning explicitly
reject the lab-only synthetic request.

```kotlin
override suspend fun provision(...): RuntimeState =
    throw UnsupportedOperationException("Production setup remains owned by SetupActivity")
```

`ProductionLauncherRuntime` owns production inspect, reset, and launch routing.
Only the exact lab runtime implements the synthetic provisioning operation;
production source acquisition remains in its established activity path.

- [x] **Step 4: Implement profile-scoped settings**

Use preference name `launcher_settings.<profile-id>`. On first Silksong access, copy each known key from legacy `launcher_settings`, commit the new file, and write a migration marker; Hollow Knight never reads the legacy file. Update `SettingsActivity` and `LauncherActivity` to construct settings for the selected registered profile.

- [x] **Step 5: Run focused, full host, and production AAR tests**

```powershell
.\gradlew.bat :app:testDebugUnitTest
.\gradlew.bat :app:assembleRelease :app:collectRuntimeDeps
```

- [x] **Step 6: Reconcile Stage 3 and commit**

Cross-check production behavior, credentials, source immutability, settings isolation, and reset. Any changed production output or missing setup path is a stage blocker.

Stage 6 resolution: the unused extraction was removed, the authoritative
production setup path remains unchanged, and the fake-runtime seam remains
available only through the exact lab package/metadata contract.

```powershell
git add src/SilksongLauncher.Launcher/app docs/verification/design-traceability.md
git commit -m "refactor: route setup through launcher runtime"
```

### Task 4: Add the shared game selector and runtime banner

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/LauncherProfileSelectionTest.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_launcher.xml`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/LauncherActivity.kt`

- [x] **Step 1: Write failing Robolectric selector tests**

Assert that both registered games render before launch, the persisted selection is checked, choosing the other game persists then recreates/rebinds the activity, a non-production runtime shows `TEST RUNTIME · EMULATOR-FAKE`, the launch button opens setup when not ready, and a ready runtime receives the exact selected profile in `gameIntent`.

- [x] **Step 2: Run the focused tests and verify RED**

```powershell
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.LauncherProfileSelectionTest
```

- [x] **Step 3: Implement the shared selector**

Add a hidden `txt_runtime_banner`, a `RadioGroup` with `radio_hollow_knight` and `radio_silksong`, and a `txt_selected_game_status` above the existing actions. Bind exclusively from `GameProfiles.all`/`SelectedGameStore`; do not hard-code a third profile registry. Disable cloud actions for profiles whose save adapter is not implemented. Route readiness, setup, reset, and launch through `LauncherRuntime`.

- [x] **Step 4: Run focused and full host tests**

```powershell
.\gradlew.bat :app:testDebugUnitTest --tests dev.silksong.launcher.LauncherProfileSelectionTest
.\gradlew.bat :app:testDebugUnitTest
```

- [x] **Step 5: Reconcile Stage 3 UI and commit**

Verify both-game selection, one launcher, evidence banner, settings isolation, and the unchanged supplied icon resources. Keep actual Hollow Knight boot and game adapters `NOT-STARTED`/`BLOCKER` in the parent ledger.

```powershell
git add src/SilksongLauncher.Launcher/app docs/verification/design-traceability.md
git commit -m "feat: select both games in the shared launcher"
```

## Stage 4: Non-publishable lab application

**Specification coverage:** lab application shell; synthetic provisioning; synthetic game process; package and release separation.

### Task 5: Build the lab APK and synthetic runtime

**Files:**
- Modify: `src/SilksongLauncher.Launcher/settings.gradle.kts`
- Modify: `src/SilksongLauncher.Launcher/build.gradle.kts`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/build.gradle.kts`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/main/AndroidManifest.xml`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/main/kotlin/io/github/darkaxt/dualsouls/lab/LabApplication.kt`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/main/kotlin/io/github/darkaxt/dualsouls/lab/LabLauncherRuntime.kt`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/main/kotlin/io/github/darkaxt/dualsouls/lab/LabGameActivity.kt`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/main/res/layout/activity_lab_game.xml`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/main/res/values/strings.xml`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/test/kotlin/io/github/darkaxt/dualsouls/lab/LabLauncherRuntimeTest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/SkinRotationStateMachine.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/SkinRotationStateMachineTest.kt`

- [x] **Step 1: Write failing lab runtime tests**

Test deterministic generation IDs, successful publication, injected failure before publish retaining the previous generation, exact-profile reset, launch intent profile/generation extras, and clean exit state. In the shared library, separately test that death selects a different eligible skin, repeated death does not reselect, and only stable respawn commits the pending skin; disabled rotation, zero/one-pack catalogs, and apply failure retain the last valid skin.

- [x] **Step 2: Run the lab unit test and verify RED**

```powershell
.\gradlew.bat :emulator-test-app:testDebugUnitTest --tests io.github.darkaxt.dualsouls.lab.LabLauncherRuntimeTest
```

Expected: the module/task does not exist.

- [x] **Step 3: Add the application module and packaging guards**

Apply `com.android.application`; use namespace/application ID `io.github.darkaxt.dualsouls.emutest`, minSdk 26, targetSdk 35, debug only, and `implementation(project(":app"))`. Disable every non-debug variant with `androidComponents.beforeVariants`. Exclude `**/arm64-v8a/**` from lab JNI packaging and reuse the existing checked-in icon resource directory at `tools/depot-to-apk/shell/res` without copying, editing, or generating artwork.

The manifest replaces the production `Application`, removes both Mono builder services, declares runtime metadata pointing at `LabLauncherRuntime`, and declares `LabGameActivity` in `:game`. The visible label is `Dual Souls Lab`.

- [x] **Step 4: Implement deterministic lab behavior**

Use `GenerationPublisher` and synthetic UTF-8 manifests only. Generation ID is `lab-<profile-id>-<monotonic-sequence>` persisted per profile. Failure injection is an explicit lab-only intent extra/state flag and is disabled by default. Implement `SkinRotationStateMachine` as a pure shared state machine with `onDeath(eligibleIds)`, `onStableRespawn(apply)`, and `reset`; it never touches Android or game types. `LabGameActivity` displays profile, generation, enabled mod/skin IDs, and lifecycle; its death/stable-respawn/exit buttons use that shared state machine, never game-specific code.

- [x] **Step 5: Run lab tests and inspect the APK contract**

```powershell
.\gradlew.bat :emulator-test-app:testDebugUnitTest :emulator-test-app:assembleDebug
```

Inspect with SDK `aapt2`, `apkanalyzer`, and `zipinfo`; require the exact package/label/debuggable/metadata, no `lib/arm64-v8a`, and no `libmonohost`, `libunity`, `libil2cpp`, or Mono BCL asset.

- [x] **Step 6: Reconcile Stage 4 and commit**

Update traceability with lab packaging and synthetic-runtime evidence. Actual game execution, real adapter signals, mods, skins, and rotation remain classified accurately unless their shared implementations exist and their tests ran.

```powershell
git add src/SilksongLauncher.Launcher docs/verification/design-traceability.md
git commit -m "feat: add non-publishable Dual Souls lab app"
```

## Stage 5: Dedicated AVD and Android integration

**Specification coverage:** exact AVD, D-drive persistence, exact device targeting, rendered UI/process/storage evidence.

### Task 6: Create and address only `DualSoulsLabApi35`

**Files:**
- Create: `tools/emulator/DualSoulsLab.psm1`
- Create: `tools/emulator/New-DualSoulsLabAvd.ps1`
- Create: `tools/emulator/Start-DualSoulsLabAvd.ps1`
- Create: `tools/emulator/Get-DualSoulsLabDevice.ps1`
- Create: `tools/emulator/Test-DualSoulsLabScripts.ps1`

- [x] **Step 1: Write failing script contract tests**

The tests import the module with fake command adapters and prove: exact system-image/device/AVD arguments, `ANDROID_AVD_HOME=D:\Android\avd`, mismatched existing AVD rejection, exact AVD-name serial selection, no fallback to the first device, and refusal to return serial `bfa98654`.

```powershell
pwsh -NoProfile -File tools\emulator\Test-DualSoulsLabScripts.ps1
```

Expected: failure because `DualSoulsLab.psm1` does not exist.

- [x] **Step 2: Implement exact AVD tooling**

Use the installed SDK tools, `pixel_6`, and `system-images;android-35;google_apis;x86_64`. Create `D:\Android\avd` directly, with no symlink. Existing matching definitions validate without reset; mismatches fail. Start only the exact named AVD. Determine its serial by enumerating emulator serials and querying each with `adb -s <serial> emu avd name`; print heartbeats while boot properties are not ready and never cancel by elapsed time.

- [x] **Step 3: Run script tests and create the AVD**

```powershell
pwsh -NoProfile -File tools\emulator\Test-DualSoulsLabScripts.ps1
pwsh -NoProfile -File tools\emulator\New-DualSoulsLabAvd.ps1
```

- [x] **Step 4: Start and validate the exact AVD**

```powershell
pwsh -NoProfile -File tools\emulator\Start-DualSoulsLabAvd.ps1
pwsh -NoProfile -File tools\emulator\Get-DualSoulsLabDevice.ps1
```

Require Android API 35, ABI `x86_64`, AVD name `DualSoulsLabApi35`, and a serial different from `bfa98654`.

- [x] **Step 5: Reconcile Stage 5 tooling and commit**

```powershell
git add tools/emulator docs/verification/design-traceability.md
git commit -m "test: add dedicated Dual Souls Android lab"
```

### Task 7: Exercise launcher/profile/storage/process behavior on Android

**Files:**
- Modify: `src/SilksongLauncher.Launcher/emulator-test-app/build.gradle.kts`
- Create: `src/SilksongLauncher.Launcher/emulator-test-app/src/androidTest/kotlin/io/github/darkaxt/dualsouls/lab/LabLauncherIntegrationTest.kt`
- Create: `tools/emulator/Test-DualSoulsLab.ps1`
- Create: `docs/verification/emulator-lab-2026-08-30.md`

- [x] **Step 1: Write failing instrumentation tests**

Use ActivityScenario/Espresso and Android package APIs to prove both profiles render, selection persists after recreation, each profile provisions independently, failure retains the previous generation, one reset preserves the other, the banner is visible, and switch order records the old `:game` PID gone before the new profile launches.

- [x] **Step 2: Run instrumentation and verify RED**

```powershell
pwsh -NoProfile -File tools\emulator\Test-DualSoulsLab.ps1
```

Expected: instrumentation fails because the integration behavior/test hooks are incomplete.

- [x] **Step 3: Implement only the missing integration behavior**

Use Espresso idling resources backed by runtime state transitions; do not add fixed sleeps. The wrapper builds the debug APK/test APK, resolves the exact AVD serial, installs with `adb -s`, runs the exact instrumentation runner, captures package/process/storage evidence, and labels the report `EMULATOR-FAKE`.

- [x] **Step 4: Run the complete lab gate**

```powershell
pwsh -NoProfile -File tools\emulator\Test-DualSoulsLab.ps1
```

Expected: script/unit/host/instrumentation/package checks all pass with zero test failures.

- [x] **Step 5: Reconcile Stage 5 and commit**

Update `docs/verification/emulator-lab-2026-08-30.md` with commands, exact AVD/serial/API/ABI, APK SHA-256, test counts, and scoped claims. Update every applicable traceability row; leave all ARM64/gameplay/Thor/release rows open.

```powershell
git add src/SilksongLauncher.Launcher/emulator-test-app tools/emulator docs/verification
git commit -m "test: verify Dual Souls launcher on Android emulator"
```

## Stage 6: Production regression and final reconciliation

**Specification coverage:** production pipeline unchanged; release exclusion; zero emulator-spec blockers or deferrals.

### Task 8: Prove lab isolation and close the emulator specification

**Files:**
- Modify: `.github/workflows/release.yml`
- Modify: `tools/ci/test_release_pipeline.py`
- Modify: `docs/verification/design-traceability.md`
- Modify: `docs/superpowers/specs/2026-08-30-dual-souls-emulator-test-instance-design.md`

- [x] **Step 1: Write failing CI guard tests**

Assert release asset discovery accepts only `DualSouls-<numeric-version>.apk`, rejects package suffix `.emutest`, and never invokes `:emulator-test-app` from the signing workflow.

- [x] **Step 2: Run CI tests and verify RED**

```powershell
python -m unittest tools.ci.test_release_pipeline
```

- [x] **Step 3: Add explicit release isolation guards**

Keep lab build/test in a separate non-signing job or out of release workflow. Release signing continues to consume only the hand-assembled ARM64 production APK and the existing pinned production certificate contract.

- [x] **Step 4: Run fresh end-to-end verification**

```powershell
pwsh -NoProfile -File tools\emulator\Test-DualSoulsLab.ps1
Set-Location src\SilksongLauncher.Launcher
.\gradlew.bat :app:testDebugUnitTest :app:assembleRelease :app:collectRuntimeDeps :emulator-test-app:testDebugUnitTest :emulator-test-app:assembleDebug
Set-Location ..\..
python -m unittest tools.ci.test_release_pipeline
git diff --check
```

Inspect production AAR/APK inputs and lab APK again. Record exact hashes and counts.

- [x] **Step 5: Final specification reconciliation**

Re-read every section and all nine acceptance criteria in the emulator specification. The emulator-spec ledger must have `blockers = 0` and `tracked_deferrals = 0`. Parent-platform game/native/device/release requirements remain separately open and must not be relabeled as emulator completion.

- [x] **Step 6: Commit and push the completed lab stage**

```powershell
git add .github/workflows/release.yml tools/ci/test_release_pipeline.py docs/verification docs/superpowers/specs/2026-08-30-dual-souls-emulator-test-instance-design.md
git commit -m "ci: keep emulator lab outside signed releases"
git push fork design/unified-hollow-knight-platform
```

## Cumulative reconciliation ledger

| Requirement | Stage | Initial state | Completion evidence |
|---|---:|---|---|
| Dedicated API 35 x86-64 AVD on `D:` | 5 | COMPLETE | `DualSoulsLabApi35`, API 35/x86-64, persists under `D:\Android\avd` |
| Exact emulator targeting; Thor excluded | 5 | COMPLETE | Script contracts plus real `emulator-5554` resolution; `bfa98654` refused |
| Real launcher/profile/storage code in lab | 2–4 | COMPLETE | Merged real activity/profile/path/publisher code passes Android sandbox integration |
| Fake only native Unity/Mono/IL2CPP boundary | 2–4 | COMPLETE | Fail-closed provider plus APK inspection prove exact lab metadata and zero ARM64 JNI entries |
| Both profile selection and persistence | 3–5 | COMPLETE | Host tests plus rendered Android instrumentation and launcher-process restart pass |
| Atomic synthetic generations/recovery | 1, 4–5 | COMPLETE | Seven publisher, six lab-runtime, and Android failure-retention/exact-discard tests pass |
| Profile-scoped reset/settings isolation | 3–5 | COMPLETE | Host tests plus Android sandbox evidence prove sibling generation/current preservation |
| Synthetic cold game process switching | 4–5 | COMPLETE | Android instrumentation proves old PID gone and new PID differs |
| Visible `EMULATOR-FAKE` identity | 3–5 | COMPLETE | Runtime assertion plus clean post-instrumentation rendered UI dump pass |
| No proprietary inputs or production-data access | 4–6 | COMPLETE | 213 tracked paths plus 251 AAR and 290 lab-APK entries scan clean; lab accepts synthetic requests only |
| Existing user artwork only | 4 | COMPLETE | Lab reuses the checked-in approved resources; no image generation/editing is present |
| Production AAR/APK behavior unchanged | 3–6 | COMPLETE | Fresh host/release-AAR/runtime-dependency build passes; production setup remains authoritative |
| Production setup runtime delegation | 3, 6 | COMPLETE | Unused duplicate removed; production rejects synthetic setup while the existing direct path remains proven |
| Lab cannot be signed or published | 4, 6 | COMPLETE | No release variant/signing config; exact asset/package guards and no workflow lab invocation pass 4 tests |
| Stage-by-stage design reconciliation | 1–6 | COMPLETE | All six stages cross-checked; emulator blockers 0, deferrals 0; parent gaps remain separately classified |
| ARM64/device claims remain open | 1–6 | BLOCKER | Parent ledger retains device gates; lab cannot close them |
