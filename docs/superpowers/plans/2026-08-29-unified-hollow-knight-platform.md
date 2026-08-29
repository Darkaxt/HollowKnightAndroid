# Unified Hollow Knight and Silksong Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one signed Android launcher that independently provisions and launches Hollow Knight or Hollow Knight: Silksong through the existing ARM64/Vulkan on-device pipeline, with shared mod, skin, rotation, and dual-screen foundations.

**Architecture:** Preserve SilksongAndroid's single installed package and dedicated launcher process. Introduce versioned game profiles, profile-scoped generations, and two content converters: the existing Addressables path for Silksong and a classic serialized-player path for Hollow Knight. Shared C# features are source modules compiled separately against each game's assemblies, while Unity processes are cold-started with exactly one profile's engine, native libraries, and data package.

**Tech Stack:** Kotlin, Android SDK/NDK, Unity 6 Android player modules, C#/.NET 8, AssetsTools.NET, Mono.Cecil, IL2CPP, Bash, Gradle 8.11, JUnit 4, Robolectric, GitHub Actions, `apksigner`, ARM64 Android, Vulkan.

---

**Execution state:** Implementation resumed on 2026-08-30. Begin with a
host-runnable POC; retain the milestone gates and device acceptance criteria.

**Design reference:** `docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md`

## File map

The plan introduces these bounded units:

- `.../profiles/GameProfile.kt`: immutable contract for one supported game family.
- `.../profiles/GameProfiles.kt`: registry and exact profile lookup.
- `.../profiles/ProfilePaths.kt`: all profile-scoped filesystem paths.
- `.../profiles/ProfileManifest.kt`: source and installed-generation manifests.
- `.../profiles/SilksongProfile.kt`: current Silksong rules expressed through the contract.
- `.../profiles/HollowKnightProfile.kt`: Hollow Knight Linux rules.
- `.../profiles/SelectedGameStore.kt`: selected profile persistence.
- `.../build/ProfileBuildCoordinator.kt`: shared staged build state machine.
- `.../build/GenerationPublisher.kt`: atomic generation publication and rollback.
- `.../skins/*`: manifest, scanner, registry, activation, and rotation.
- `.../mods/*`: manifest, compatibility resolver, source staging, and safe mode.
- `tools/bundle-surgery/SerializedFileTransformer.cs`: serialized-file parsing and Android/Vulkan transformation.
- `tools/bundle-surgery/ClassicPlayerTree.cs`: Hollow Knight classic layout discovery and conversion.
- `tools/bundle-surgery-tests/*`: legal synthetic fixtures and converter tests.
- `tools/shared-patches/*`: game-neutral C# runtime sources.
- `tools/hollow-knight-patches/*`: Hollow Knight adapter and entry points.
- `tools/silksong-patches/src/dualscreen/core/*`: game-neutral multi-display units.
- `docs/verification/design-traceability.md`: requirement-by-requirement state,
  evidence, blocker, and deferral ledger updated after every milestone.
- `.github/workflows/check.yml`: non-proprietary verification on every change.
- `.github/workflows/release.yml`: signed, manually dispatched release pipeline.

Existing large Kotlin classes remain in place initially. Each is reduced only
where a new boundary requires it; the plan does not perform an unrelated
repository-wide rewrite.

## Mandatory stage-close procedure

Run this procedure after every implementation milestone and before beginning a
dependent milestone:

1. Re-read
   `docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md`.
2. Re-read the completed milestone in this plan and inspect its complete diff.
3. Update `docs/verification/design-traceability.md` so every design
   requirement has one state: `COMPLETE`, `BLOCKER`, `DEFERRED`, or
   `NOT-STARTED`.
4. Link every `COMPLETE` row to a test, command output, artifact hash, or
   device-verification document.
5. Give every `BLOCKER` an exact failing contract, evidence, affected
   milestones, and unblock condition.
6. Give every `DEFERRED` item a reason, dependency, target milestone, and exact
   acceptance test. A deferral without all four fields is reclassified as a
   blocker.
7. Run `git diff --check`, `make test`, the milestone-specific checks, and a
   tracked-file scan proving that no game or Unity binaries entered the tree.
8. Commit the traceability update separately with the message
   `docs: cross-check milestone N against design`, replacing `N` with the
   completed milestone number.

Use this table shape:

```markdown
| Requirement | Planned milestone | State | Evidence or gap | Dependency | Acceptance test |
|---|---:|---|---|---|---|
| Goal 1: one launcher package | 4 | NOT-STARTED | Milestone 4 has not begun | Profile generations | Both profiles launch from one installed package |
```

The stage does not close while any requirement planned for that stage remains
`NOT-STARTED`. A `BLOCKER` prevents dependent work and any release. Independent
work may proceed only when the ledger explains why it cannot conceal, worsen,
or bypass the blocker.

### Local-first POC exception

The initial implementation slice may complete Tasks 1–3 and then work on
Tasks 5–7 as an independent host-only converter POC before Task 4's device
regression. Before Task 5 begins, the traceability ledger must record Task 4
as a `BLOCKER`, including its missing Silksong regression evidence and exact
unblock command/device test. This exception closes neither Milestone 1 nor
Milestone 2 and does not permit Task 8 or any other device/toolchain-dependent
work to begin. Tasks 5–7 operate only on synthetic fixtures and read-only
copies of user-supplied Hollow Knight data, so they cannot alter or conceal
the existing Silksong runtime path.

## Milestone 1: Profile foundation without Silksong regression

### Task 1: Add repeatable host-side tests

**Files:**
- Modify: `src/SilksongLauncher.Launcher/app/build.gradle.kts`
- Modify: `Makefile`
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/TestEnvironmentTest.kt`
- Create: `docs/verification/design-traceability.md`
- Modify: `tools/silksong-patches/check.ps1`

- [x] **Step 1: Add JUnit and Robolectric dependencies**

Add the following to the existing `dependencies` block:

```kotlin
testImplementation("junit:junit:4.13.2")
testImplementation("org.robolectric:robolectric:4.14.1")
testImplementation("androidx.test:core:1.6.1")
```

Enable Android resources for local tests:

```kotlin
android {
    testOptions.unitTests.isIncludeAndroidResources = true
}
```

- [x] **Step 2: Write the first host test**

Use `RobolectricTestRunner` and `ApplicationProvider` to load
`R.string.launcher_app_name`, proving JUnit, Robolectric, and Android resource
loading all work. Before invoking Gradle, the test target must fail with an
actionable message if the pinned launcher JAR is absent under `AP`.

- [x] **Step 3: Add a deterministic test target**

Add a `test` target to `Makefile` that invokes the Gradle installation carried
by the fetched Unity Android player:

```make
test: ## Run host-side launcher and converter tests
	java -classpath "$(AP)/Tools/gradle/lib/gradle-launcher-8.11.jar" \
		org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher \
		:app:testDebugUnitTest
```

Add `test` to `.PHONY`.

- [x] **Step 4: Remove cancellation timeouts from patch checking**

Replace the `WaitForExit(180000)` branch in
`tools/silksong-patches/check.ps1` with an unbounded wait and normal exit-code
inspection:

```powershell
$proc.WaitForExit()
if ($proc.ExitCode -ne 0) {
    $errors.Add("dotnet build exited with $($proc.ExitCode)")
}
```

Replace its fixed `$env:TEMP` work directory with a task-scoped root that
prefers `D:\Temp` without repurposing a system home variable:

```powershell
$taskTempRoot = if ($env:DUALSOULS_TEMP_ROOT) {
    $env:DUALSOULS_TEMP_ROOT
} elseif (Test-Path -LiteralPath 'D:\Temp' -PathType Container) {
    'D:\Temp'
} else {
    [System.IO.Path]::GetTempPath()
}
$work = Join-Path $taskTempRoot ("dualsouls-patch-check-{0}" -f [Guid]::NewGuid().ToString('N'))
```

Clean only that unique, resolved child in a `finally` block after verifying it
is beneath the selected task temp root.

- [x] **Step 5: Run the test and existing compile check**

Run:

```bash
make test
make check
```

Expected: the JUnit test passes; the patch source check ends with `[check] OK`.

- [x] **Step 6: Commit**

```bash
git add Makefile tools/silksong-patches/check.ps1 src/SilksongLauncher.Launcher/app
git commit -m "test: add repeatable host verification"
```

- [x] **Step 7: Create the initial design traceability ledger**

Populate one row for every numbered design goal, non-goal boundary,
architecture subsection, recovery rule, test tier, and release gate. Mark only
documentation and already verified binary-feasibility claims `COMPLETE`; mark
future implementation requirements `NOT-STARTED` with their planned
milestones.

```bash
git add docs/verification/design-traceability.md
git commit -m "docs: initialize design traceability"
```

### Task 2: Define exact game profiles

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/GameProfile.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/GameProfiles.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/SilksongProfile.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/HollowKnightProfile.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/GameProfilesTest.kt`

- [x] **Step 1: Write failing registry tests**

```kotlin
package dev.silksong.launcher.profiles

import org.junit.Assert.*
import org.junit.Test

class GameProfilesTest {
    @Test fun registry_contains_both_games() {
        assertEquals(setOf("hollow-knight", "silksong"), GameProfiles.all.map { it.id }.toSet())
    }

    @Test fun windows_sources_are_not_accepted() {
        assertFalse(GameProfiles.require("hollow-knight").acceptedPlatforms.contains("WindowsPlayer"))
    }

    @Test fun profiles_pin_their_own_unity_versions() {
        assertEquals("6000.0.61f1", GameProfiles.require("hollow-knight").unityVersion)
        assertEquals("6000.0.50f1", GameProfiles.require("silksong").unityVersion)
    }
}
```

- [x] **Step 2: Run the targeted test and observe the missing symbols**

Run:

```bash
java -classpath "$AP/Tools/gradle/lib/gradle-launcher-8.11.jar" \
  org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher \
  :app:testDebugUnitTest --tests dev.silksong.launcher.profiles.GameProfilesTest
```

Expected: compilation fails because `GameProfiles` is absent.

- [x] **Step 3: Add the profile contract and registry**

```kotlin
package dev.silksong.launcher.profiles

enum class ContentLayout { ADDRESSABLES, CLASSIC_PLAYER }

data class GameProfile(
    val id: String,
    val displayName: String,
    val steamAppId: Int,
    val steamDepotId: Int,
    val unityVersion: String,
    val acceptedPlatforms: Set<String>,
    val dataDirectoryName: String,
    val executableNames: Set<String>,
    val contentLayout: ContentLayout,
    val patchSet: String,
)
```

`GameProfiles` exposes `all`, `find(id)`, and `require(id)`. Define Silksong
with its current app/depot constants and `ADDRESSABLES`. Define Hollow Knight
with its verified Linux executable/data paths, Unity `6000.0.61f1`, and
`CLASSIC_PLAYER`. Keep accepted source platform to `LinuxPlayer` for both.

- [x] **Step 4: Run all host tests**

Run `make test`.

Expected: all JUnit tests pass and the converter test project is either green
or contains no tests until Task 6.

- [x] **Step 5: Commit**

```bash
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles \
        src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles
git commit -m "feat: define versioned game profiles"
```

### Task 3: Isolate profile paths and selected-game state

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfilePaths.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/SelectedGameStore.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/ProfilePathsTest.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/SelectedGameStoreTest.kt`

- [ ] **Step 1: Write failing path-isolation tests**

```kotlin
@Test fun profiles_never_share_generated_paths() {
    val root = File("build/test-profile-paths")
    val hk = ProfilePaths(root, "hollow-knight")
    val ss = ProfilePaths(root, "silksong")
    assertNotEquals(hk.generations.canonicalPath, ss.generations.canonicalPath)
    assertTrue(hk.staging.canonicalPath.startsWith(hk.root.canonicalPath))
}
```

Add store tests that verify the default is `silksong`, a valid selection
survives recreation, and an unknown stored ID falls back to `silksong`.

- [ ] **Step 2: Run the tests and observe missing classes**

Run `make test`.

Expected: compilation fails for `ProfilePaths` and `SelectedGameStore`.

- [ ] **Step 3: Implement focused path and preference classes**

```kotlin
data class ProfilePaths(val filesDir: File, val profile: GameProfile) {
    val root = File(filesDir, "profiles/${profile.id}")
    val generations = File(root, "generations")
    val staging = File(root, "staging")
    val logs = File(root, "logs")
    val currentPointer = File(root, "current")
    val sourcePointer = File(root, "source.pointer")
}
```

Resolve paths with normalized `Path` containment rather than string-prefix
checks. Add rejection tests for `..`, both directory separators, absolute
identifiers, and sibling-prefix collisions; only registered profile IDs are
accepted.

`SelectedGameStore` uses a dedicated `SharedPreferences` file named
`selected-game`, accepts IDs only through `GameProfiles.find`, and exposes
`get()` and `set(GameProfile)`.

- [ ] **Step 4: Run tests and commit**

Run `make test`; expect all tests to pass.

```bash
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles \
        src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles
git commit -m "feat: isolate profile storage and selection"
```

### Task 4: Express the existing Silksong build through the profile

**Files:**
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SetupActivity.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/DepotFetcher.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/DepotLocation.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/UnityFetcher.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/PlayerImage.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/SilksongRegressionTest.kt`

- [ ] **Step 1: Write regression tests for current constants**

Assert that `SilksongProfile` carries the existing Steam depot ID, Unity
version, data directory, executable, and Addressables root expected by
`PlayerImage`.

- [ ] **Step 2: Replace hard-coded game constants with a profile parameter**

Change entry points to accept `GameProfile` and `ProfilePaths`, for example:

```kotlin
suspend fun fetch(
    profile: GameProfile,
    paths: ProfilePaths,
    session: SteamSession,
    onProgress: (DepotProgress) -> Unit,
): File
```

`SetupActivity` resolves the selected profile once and passes it through the
existing pipeline. Do not change conversion behavior in this task.

- [ ] **Step 3: Prove Silksong output paths remain valid**

Run `make test`, `make check`, and `make dev` with the existing Silksong depot.

Expected: host tests and patch check pass; the launcher APK builds and installs;
Silksong's existing installed generation is either adopted into the new
Silksong profile or rebuilt without touching the source depot.

- [ ] **Step 4: Device smoke test on `bfa98654`**

Run:

```bash
adb -s bfa98654 shell am force-stop com.jakobkhansen.silksong
adb -s bfa98654 shell monkey -p com.jakobkhansen.silksong 1
```

Expected: launcher opens, the Silksong profile is selected, and Play starts
the same game path as the base revision.

- [ ] **Step 5: Commit**

```bash
git add src/SilksongLauncher.Launcher/app/src/main/kotlin \
        src/SilksongLauncher.Launcher/app/src/test
git commit -m "refactor: route Silksong through game profiles"
```

## Milestone 2: Hollow Knight classic-content conversion

### Task 5: Split serialized-file transformation from bundle traversal

**Files:**
- Create: `tools/bundle-surgery/SerializedFileTransformer.cs`
- Modify: `tools/bundle-surgery/Program.cs`
- Create: `tools/bundle-surgery/Properties/AssemblyInfo.cs`
- Create: `tools/bundle-surgery-tests/BundleSurgery.Tests.csproj`
- Create: `tools/bundle-surgery-tests/SerializedFileTransformerTests.cs`
- Create: `tools/bundle-surgery-tests/Fixtures/SyntheticAssetFactory.cs`
- Modify: `Makefile`

- [ ] **Step 1: Create the xUnit project and a failing transform test**

The test project references `BundleSurgery.csproj`, xUnit `2.9.2`, and
`Microsoft.NET.Test.Sdk` `17.12.0`. Add:

```csharp
[Fact]
public void Transform_requires_a_vulkan_slice_when_a_shader_is_present()
{
    using var fixture = SyntheticAssetFactory.WithShaderPlatforms(15);
    var output = Path.Combine(fixture.OutputRoot, "converted.assets");
    var error = Assert.Throws<InvalidDataException>(() =>
        SerializedFileTransformer.Transform(fixture.InputPath, output, requireVulkan: true));
    Assert.Contains("Vulkan", error.Message);
    Assert.False(File.Exists(output));
}
```

- [ ] **Step 2: Expose internals to the test assembly**

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("BundleSurgery.Tests")]
```

Set `<AssemblyName>BundleSurgery.Tests</AssemblyName>` explicitly, then add the
converter test project to the `Makefile` test target:

```make
	dotnet test tools/bundle-surgery-tests/BundleSurgery.Tests.csproj -c Release
```

- [ ] **Step 3: Extract the reusable transformer**

Move serialized-file loading, shader-platform selection, target-platform
rewrite, and output verification from `Program.cs` into:

```csharp
internal sealed record TransformReport(
    int ShadersSeen,
    int VulkanShaders,
    int MissingVulkanShaders,
    int OriginalTarget,
    int NewTarget);

internal static class SerializedFileTransformer
{
    internal static TransformReport Transform(
        string input,
        string output,
        bool requireVulkan);
}
```

The transformer writes to `output.part`, reopens and verifies the produced
serialized file, then renames it to `output`. A shader without Vulkan throws
when `requireVulkan` is true.

- [ ] **Step 4: Keep existing CLI behavior as a delegation**

`extract-vulkan-android` recognizes serialized files by header, not by
`.assets` suffix. Bundle traversal calls the same transformer for each inner
serialized file.

- [ ] **Step 5: Run tests and regression commands**

Run:

```bash
dotnet test tools/bundle-surgery-tests/BundleSurgery.Tests.csproj -c Release
dotnet build tools/bundle-surgery/BundleSurgery.csproj -c Release
```

Expected: all xUnit tests pass and BundleSurgery builds without warnings.

- [ ] **Step 6: Commit**

```bash
git add tools/bundle-surgery tools/bundle-surgery-tests
git commit -m "refactor: share serialized file transformation"
```

### Task 6: Discover and convert a classic Unity player tree

**Files:**
- Create: `tools/bundle-surgery/ClassicPlayerTree.cs`
- Modify: `tools/bundle-surgery/Program.cs`
- Create: `tools/bundle-surgery-tests/ClassicPlayerTreeTests.cs`

- [ ] **Step 1: Write failing layout tests**

Create a fixture containing `globalgamemanagers`, `resources.assets`,
`level0`, `level1`, `sharedassets0.assets`, `sharedassets1.assets`, and paired
`.resS` files. Assert deterministic ordering and exact sidecar pairing.

```csharp
var tree = ClassicPlayerTree.Discover(fixture.Root);
Assert.Equal(new[] { "level0", "level1" }, tree.Levels.Select(Path.GetFileName));
Assert.Empty(tree.Errors);
```

Add rejection tests for a missing `sharedassets1.assets`, an orphaned sidecar,
duplicate numeric indices, and a non-Linux BuildSettings platform.

- [ ] **Step 2: Implement discovery and validation**

```csharp
internal sealed record ClassicPlayerLayout(
    string GlobalGameManagers,
    IReadOnlyList<string> SerializedFiles,
    IReadOnlyList<string> Sidecars,
    IReadOnlyList<string> Errors);
```

Sort numeric filenames numerically, not lexically. Required serialized-file
membership comes from the exact profile manifest; do not infer a universal
`levelN` to `sharedassetsN.assets` pairing rule. Treat `.resS` and `.resource`
as opaque sidecars, associate them through serialized references or a
documented basename rule, and validate normalized containment under the
selected data root. Reject source/output overlap and reparse points.

- [ ] **Step 3: Add classic-tree commands**

Add these exact commands:

```text
retarget-classic-tree <source-root> <output-root> <profile-manifest.json> <report.json>
manifest-classic-tree <source-root> <report.json>
```

The first transforms every manifest-selected serialized file, copies sidecars
without rewriting them, and writes a versioned JSON report. Resume identity
includes input SHA-256, transformer version, transformation options, profile
ID, and game version; reuse also verifies the existing output hash. It never
writes inside the source root. `manifest-classic-tree` performs discovery and
the shader/media/plugin census without producing converted game data.

Run:

```bash
dotnet test tools/bundle-surgery-tests/BundleSurgery.Tests.csproj -c Release \
  --filter ClassicPlayerTreeTests
```

Expected: exit 0; the synthetic output has Android target 13 in every produced
serialized file and an error-free conversion report.

- [ ] **Step 4: Run all converter tests and commit**

```bash
dotnet test tools/bundle-surgery-tests/BundleSurgery.Tests.csproj -c Release
git add tools/bundle-surgery tools/bundle-surgery-tests
git commit -m "feat: convert classic Unity player trees"
```

### Task 7: Validate Hollow Knight sources by exact manifest

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfileManifest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/HollowKnightSourceValidator.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/HollowKnightSourceValidatorTest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/assets/profiles/hollow-knight-1.5.12620.json`

- [ ] **Step 1: Write source-validation tests**

Use a temporary synthetic file tree. Assert these results: `SUPPORTED`,
`WINDOWS_SOURCE`, `INCOMPLETE`, `MIXED_BUILD`, `MISSING_VULKAN`, and
`UNKNOWN_VERSION`. Each result includes the exact offending path or property.

- [ ] **Step 2: Define the manifest schema**

```kotlin
data class ProfileManifest(
    val profileId: String,
    val gameVersion: String,
    val unityVersion: String,
    val platform: String,
    val requiredFiles: List<ManifestFile>,
    val converterReportSchema: Int,
    val manifestSha256: String,
)

data class ManifestFile(
    val relativePath: String,
    val size: Long,
    val sha256: String,
)
```

The committed JSON contains only normalized relative paths, sizes, version
identifiers, counts, and hashes. It contains no game bytes or extracted
strings. Exact per-file hashes make mixed-build classification deterministic.

- [ ] **Step 3: Implement read-only validation**

The C# tool produces a typed, versioned report and has its own converter tests.
Pure Kotlin validation consumes that report through an injected reporter
interface so Robolectric tests use a fake. A later Android adapter invokes the
on-device BundleSurgery executable. Validation reads `app.info`, serialized
metadata, and required-file hashes. It must not change timestamps, add markers,
or write into the depot.

- [ ] **Step 4: Generate and review the current Linux manifest locally**

Linux `1.5.12620` is the current profile target but remains unaccepted as a
source until its exact manifest exists. Extract it into a unique directory
under `D:\Temp`, run the full layout, manifest, Vulkan, media, and plugin
checks, and compare it with `1.5.12612` strictly as backward-compatibility
evidence. Only after the current build passes may the validator accept
`hollow-knight-1.5.12620.json` and the design record claim current-build
support. Review the JSON for absence of paths outside the game root and
proprietary contents, then remove only that exact temporary extraction using a
validated absolute path.

- [ ] **Step 5: Test and commit**

```bash
make test
git add src/SilksongLauncher.Launcher/app/src/main/assets/profiles \
        src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles \
        src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles
git commit -m "feat: validate Hollow Knight Linux sources"
```

## Milestone 3: Profile-scoped on-device builds and first Hollow Knight boot

### Task 8: Resolve Unity toolchains per profile

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/UnityToolchainDescriptor.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/UnityToolchainRegistry.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/UnityFetcher.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/UnityDex.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/build/UnityToolchainRegistryTest.kt`

- [ ] **Step 1: Test exact version and hash selection**

Assert that `6000.0.50f1` and `6000.0.61f1` resolve to different descriptors,
unknown versions are rejected, and a cached component with a wrong hash is
deleted from staging but not from another toolchain directory.

- [ ] **Step 2: Define the descriptor**

```kotlin
data class UnityToolchainDescriptor(
    val unityVersion: String,
    val editorUrl: String,
    val editorSha256: String,
    val androidModuleUrl: String,
    val androidModuleSha256: String,
)
```

Populate both verified Unity versions using primary Unity download metadata.

- [ ] **Step 3: Namespace fetched and dexed output**

Store toolchains under `files/toolchains/<descriptor-content-hash>` and key
Unity dex output by `classes.jar` hash. Update `UnityFetcher` and `UnityDex` to
accept a descriptor rather than global constants.

- [ ] **Step 4: Test, build, and commit**

```bash
make test
make dev
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher
git commit -m "feat: resolve Unity toolchains by profile"
```

### Task 9: Publish atomic profile generations

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/InstalledGeneration.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/GenerationPublisher.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build/ProfileBuildCoordinator.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SetupActivity.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/NativeBuild.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/PlayerImage.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/build/GenerationPublisherTest.kt`

- [ ] **Step 1: Write publication and interruption tests**

Assert that an incomplete staging directory never becomes current, publishing
generation B preserves current generation A until B verifies, a cancellation
removes only its job directory, and pointer replacement is atomic.

- [ ] **Step 2: Implement the generation model**

```kotlin
data class InstalledGeneration(
    val id: String,
    val profileId: String,
    val sourceManifestSha256: String,
    val toolchainId: String,
    val patchManifestSha256: String,
    val files: Map<String, String>,
)
```

Write the manifest last inside staging. Reopen every published file, check
hashes and ZIP readability, rename staging to generations, then atomically
replace the `current` pointer.

- [ ] **Step 3: Move current build orchestration behind the coordinator**

Represent stages as a sealed class and emit structured progress:

```kotlin
sealed interface BuildStage {
    data object ValidateSource : BuildStage
    data object ResolveToolchain : BuildStage
    data object CompilePatches : BuildStage
    data object ConvertIl2Cpp : BuildStage
    data object CompileNative : BuildStage
    data object ConvertContent : BuildStage
    data object Verify : BuildStage
    data object Publish : BuildStage
}
```

Cancellation is cooperative between stages and while processing file batches;
atomic file publication is allowed to finish. Do not introduce elapsed-time
cancellation.

- [ ] **Step 4: Run injected-failure tests and commit**

```bash
make test
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/build \
        src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SetupActivity.kt \
        src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/NativeBuild.kt \
        src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/PlayerImage.kt
git commit -m "feat: publish atomic game generations"
```

### Task 10: Build and boot Hollow Knight through the shared player

**Files:**
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/PackageCompiler.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/Il2cppConverter.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/PlayerImage.kt`
- Create: `tools/hollow-knight-patches/HollowKnightPatches.csproj`
- Create: `tools/hollow-knight-patches/entrypoints.json`
- Create: `tools/hollow-knight-patches/src/InjectionProbe.cs`
- Create: `tools/hollow-knight-patches/check.ps1`

- [ ] **Step 1: Add a separately compiled Hollow Knight injection probe**

```csharp
using UnityEngine;

internal static class InjectionProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void Start() => Debug.Log("[DualSouls][HK] injection probe loaded");
}
```

The project references only assemblies from the selected Hollow Knight depot
plus shared patch sources. The entry-point manifest names this method
explicitly.

- [ ] **Step 2: Parameterize assembly and patch inputs**

`PackageCompiler`, `Il2cppConverter`, and `PlayerImage` accept `GameProfile`,
`ProfilePaths`, and the selected generation. Remove assumptions that the data
directory, patch DLL, or entrypoint file contains `Silksong`.

- [ ] **Step 3: Add the classic content strategy**

For `ContentLayout.CLASSIC_PLAYER`, invoke `retarget-classic-tree`, install the
matching `6000.0.61f1` Android built-in resources, and create the profile's
`data.apk` with ZIP64 support. Preserve sidecar path relationships.

- [ ] **Step 4: Run the end-to-end ARM64 spike on the Thor**

Use `adb -s bfa98654` for every device command. Supply the untouched Hollow
Knight Linux source through the launcher, build it on device, and capture the
structured log. Success requires:

```text
[DualSouls][HK] injection probe loaded
```

followed by a visible main menu, successful new-game or save load, and one
playable room with correct rendering, audio, and input.

- [ ] **Step 5: Record the spike evidence**

Create `docs/verification/hollow-knight-first-boot.md` containing source
manifest hash, APK commit, device serial/model, Android version, Unity version,
build duration, relevant log excerpts, tested scene, observed defects, and
artifact hashes. Do not include game files or credentials.

- [ ] **Step 6: Commit the working spike**

```bash
git add tools/hollow-knight-patches src/SilksongLauncher.Launcher \
        docs/verification/hollow-knight-first-boot.md
git commit -m "feat: boot Hollow Knight through the Vulkan player"
```

**Milestone gate:** Stop Hollow Knight feature expansion if this task cannot
reach a playable room. Preserve all passing Silksong and generic profile work.

## Milestone 4: One launcher and reliable profile switching

### Task 11: Add game selection and cold-process switching

**Files:**
- Modify: `src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_launcher.xml`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/LauncherActivity.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SilksongApp.kt`
- Modify: `tools/depot-to-apk/shell/GameActivity.java`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/LauncherProfileTest.kt`

- [ ] **Step 1: Test launch eligibility and switch behavior**

Assert that Play is enabled only when the selected profile has a verified
current generation; switching updates settings/log links; and a switch request
while the Unity process is alive returns `GAME_PROCESS_ACTIVE` without
changing native paths.

- [ ] **Step 2: Add an accessible two-game selector**

Use two labeled radio cards with status text (`Not configured`, `Building`,
`Ready`, `Needs repair`). Selection writes through `SelectedGameStore` and
refreshes launcher actions. Do not begin Unity from the selection callback.

- [ ] **Step 3: Resolve native and data paths before Unity class loading**

`SilksongApp.attachBaseContext` reads the selected profile and current
generation, verifies both, injects that profile's `classes.jar` dex, and
records immutable process startup paths. `GameActivity` refuses an Intent
whose profile differs from the process startup profile.

- [ ] **Step 4: Test both switch directions on the Thor**

Prove Silksong -> launcher -> Hollow Knight and Hollow Knight -> launcher ->
Silksong. Confirm the old Unity PID is gone before starting the other profile;
do not kill it on an elapsed-time threshold.

- [ ] **Step 5: Commit**

```bash
git add src/SilksongLauncher.Launcher tools/depot-to-apk/shell/GameActivity.java
git commit -m "feat: select and switch game profiles"
```

## Milestone 5: Shared skins, rotation, and mods

### Task 12: Implement the shared skin-pack library

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/SkinManifest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/SkinPackScanner.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/SkinRegistry.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/SkinActivationStore.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/SkinPackScannerTest.kt`
- Create: `docs/skin-pack-format.md`

- [ ] **Step 1: Write scanner and collision tests**

Cover multiple valid sibling packs, one invalid sibling that does not block
others, duplicate IDs with identical content, duplicate IDs with different
content, duplicate basenames in different packs, path traversal, missing
per-game mapping, and one pack supporting both games.

- [ ] **Step 2: Define the manifest model**

```kotlin
data class SkinManifest(
    val schemaVersion: Int,
    val id: String,
    val name: String,
    val author: String,
    val games: Map<String, SkinGameMapping>,
)

data class SkinGameMapping(
    val assetRoot: String,
    val textures: Map<String, String>,
)
```

Reject absolute paths, `..`, unknown schema versions, empty mappings, and
files resolving outside the pack root.

- [ ] **Step 3: Implement import by validated pack root**

Scan only immediate children as candidate roots. Calculate a deterministic
SHA-256 tree hash, copy to `files/library/skins/<id>/<hash>`, verify the copy,
and update the registry atomically. Never flatten texture files.

- [ ] **Step 4: Document the format and run tests**

Document a complete two-game example manifest, folder layout, replacement
rules, and invalid-pack diagnostics. Run `make test`; expect all skin tests to
pass.

- [ ] **Step 5: Commit**

```bash
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins \
        src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins \
        docs/skin-pack-format.md
git commit -m "feat: add shared skin pack library"
```

### Task 13: Apply skins and rotate after death

**Files:**
- Create: `tools/shared-patches/src/Lifecycle/GameLifecycle.cs`
- Create: `tools/shared-patches/src/Skins/SkinRotationPolicy.cs`
- Create: `tools/shared-patches/src/Skins/SkinRuntime.cs`
- Create: `tools/hollow-knight-patches/src/HollowKnightLifecycleAdapter.cs`
- Create: `tools/hollow-knight-patches/src/HollowKnightSkinAdapter.cs`
- Create: `tools/silksong-patches/src/SilksongLifecycleAdapter.cs`
- Create: `tools/silksong-patches/src/SilksongSkinAdapter.cs`
- Create: `tools/shared-patches-tests/SkinRotationPolicyTests.cs`
- Create: `tools/shared-patches-tests/SharedPatches.Tests.csproj`
- Modify: `Makefile`

- [ ] **Step 1: Write rotation state-machine tests**

```csharp
[Fact]
public void death_selects_but_stable_respawn_applies_the_next_skin()
{
    var policy = new SkinRotationPolicy(new[] { "a", "b" }, "a");
    policy.OnPlayerDied();
    Assert.Equal("a", policy.AppliedSkin);
    policy.OnStableRespawn();
    Assert.Equal("b", policy.AppliedSkin);
}
```

Also test a one-pack library, disabled rotation, consecutive deaths before
respawn, invalid selected pack, deterministic ordered mode, seeded shuffle,
and rollback when application fails.

- [ ] **Step 2: Implement a game-neutral lifecycle contract**

```csharp
public interface IGameLifecycle
{
    event Action PlayerDied;
    event Action StableRespawn;
}

public interface ISkinAdapter
{
    bool TryApply(string packRoot, out string error);
}
```

Add the shared-patch test project to the `Makefile` test target:

```make
	dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
```

- [ ] **Step 3: Implement per-game adapters**

The Hollow Knight adapter uses the proven death/respawn state boundary from
DualSouls/HKTweaks and maps pack texture keys to Hollow Knight targets. The
Silksong adapter uses Silksong-specific player lifecycle and atlas mappings.
Adapters emit one stable-respawn event per death and do no folder I/O during
the critical death callback.

- [ ] **Step 4: Compile against both depots and test on device**

Run both patch checks, then verify manual skin switching and one death rotation
in each game. Confirm the current save is unchanged and a bad pack rolls back
to the prior working skin.

- [ ] **Step 5: Commit**

```bash
git add tools/shared-patches tools/shared-patches-tests \
        tools/hollow-knight-patches tools/silksong-patches
git commit -m "feat: share skin activation and death rotation"
```

### Task 14: Add the shared mod library and safe mode

**Files:**
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/mods/ModManifest.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/mods/ModResolver.kt`
- Create: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/mods/ModBuildSet.kt`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/PackageCompiler.kt`
- Test: `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/mods/ModResolverTest.kt`
- Create: `docs/mod-format.md`

- [ ] **Step 1: Test compatibility and dependency resolution**

Cover game ID, game-version range, dependency order, cycle rejection, duplicate
mod IDs, disabled mods, compilation failure, and safe mode with external mods
excluded.

- [ ] **Step 2: Define the manifest and resolver**

```kotlin
data class ModManifest(
    val schemaVersion: Int,
    val id: String,
    val version: String,
    val games: Map<String, String>,
    val dependencies: List<String>,
    val sources: List<String>,
    val entryPoints: List<String>,
)
```

Use a deterministic topological sort. Cycles and missing dependencies prevent
only the affected build set from compiling.

- [ ] **Step 3: Compile a per-profile patch assembly**

Stage shared sources, the selected game adapter, and resolved external mod
sources into an isolated build directory. Compile against only the selected
depot's assemblies. Publish the patch output as part of a new generation so a
failed compile cannot replace the working base game.

- [ ] **Step 4: Add safe launch and documentation**

The launcher exposes `Play without external mods`; built-in shared patches and
the game adapter remain enabled. Document the schema, load order, compatibility
rules, and recovery behavior.

- [ ] **Step 5: Test and commit**

```bash
make test
make check
git add src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/mods \
        src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/mods \
        src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/PackageCompiler.kt \
        docs/mod-format.md
git commit -m "feat: add per-profile mod builds and safe mode"
```

## Milestone 6: Shared dual-screen renderer

### Task 15: Extract game-neutral dual-screen primitives

**Files:**
- Create: `tools/shared-patches/src/DualScreen/IDualScreenData.cs`
- Create: `tools/shared-patches/src/DualScreen/DualScreenRuntime.cs`
- Move: `tools/silksong-patches/src/dualscreen/DsPresentation.cs` to `tools/shared-patches/src/DualScreen/DsPresentation.cs`
- Move: `tools/silksong-patches/src/dualscreen/DsInput.cs` to `tools/shared-patches/src/DualScreen/DsInput.cs`
- Move: `tools/silksong-patches/src/dualscreen/DsTheme.cs` to `tools/shared-patches/src/DualScreen/DsTheme.cs`
- Create: `tools/hollow-knight-patches/src/dualscreen/HollowKnightScreenData.cs`
- Test: `tools/shared-patches-tests/DualScreenRuntimeTests.cs`

- [ ] **Step 1: Test display and fallback decisions**

Using a fake display provider, assert that display 1 is activated and targeted
when available, a single display produces an explicit primary-display
fallback, and touches with the wrong display index are ignored.

- [ ] **Step 2: Define the neutral data boundary**

```csharp
public interface IDualScreenData
{
    string GameId { get; }
    bool IsReady { get; }
    IReadOnlyList<ScreenDescriptor> Screens { get; }
}
```

The shared runtime owns display activation, target camera, presentation,
themes, touch routing, diagnostics, and single-display fallback. It references
no Silksong types.

- [ ] **Step 3: Preserve Silksong behavior through an adapter**

Compile and launch Silksong on Thor. Compare map, inventory, crests, tasks,
journal, touch, title cards, and single-display behavior with the base
revision. Record screenshots and functional observations in
`docs/verification/silksong-dualscreen-regression.md`.

- [ ] **Step 4: Add the first Hollow Knight screen**

Implement a map/status proof using Hollow Knight's adapter. Success is a
stable second-display camera, correct scene transitions, correct touch display
attribution, and no regression to primary-display input.

- [ ] **Step 5: Commit**

```bash
git add tools/shared-patches tools/shared-patches-tests tools/silksong-patches \
        tools/hollow-knight-patches docs/verification
git commit -m "feat: share the Vulkan dual-screen runtime"
```

## Milestone 7: Branding, CI, signing, and release proof

### Task 16: Establish the unified product identity and icon

**Files:**
- Modify: `tools/depot-to-apk/build.sh`
- Modify: `src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml`
- Replace: `docs/icon.png`
- Replace: `tools/depot-to-apk/shell/res/mipmap-*/ic_launcher.png`
- Replace: `tools/depot-to-apk/shell/res/mipmap-*/ic_launcher_bg.png`
- Create: `tools/depot-to-apk/shell/res/mipmap-anydpi-v33/ic_launcher_monochrome.xml`
- Create: `docs/icon-source/ATTRIBUTION.md`

- [ ] **Step 1: Set a non-colliding package identity**

Change build defaults to:

```bash
PKG="${PKG:-io.github.darkaxt.dualsouls}"
APP_LABEL="${APP_LABEL:-Dual Souls}"
APK_NAME="${APK_NAME:-DualSouls-$VERSION_NAME.apk}"
```

Keep Java/Kotlin namespaces unchanged in this task; Android permits the
application ID to differ from code namespaces, avoiding a risky mechanical
rename.

- [ ] **Step 2: Produce the approved adaptive icon**

Create original or licensed foreground artwork divided diagonally from the
bottom-left corner to the top-right corner: Hollow Knight identity on one side
and Silksong identity on the other. Verify the composition under circle,
squircle, rounded-square, and OEM adaptive masks. Create a matching monochrome
themed icon and record source licenses and attribution.

- [ ] **Step 3: Verify packaged identity and icon resources**

Run `make dev`, then:

```bash
aapt dump badging build/DualSouls-$(tr -d ' \r\n' < VERSION).apk
apksigner verify --verbose --print-certs build/DualSouls-$(tr -d ' \r\n' < VERSION).apk
```

Expected: package `io.github.darkaxt.dualsouls`, label `Dual Souls`, matching
version, non-debuggable release output when `DEBUGGABLE=0`, and a verified
signature.

- [ ] **Step 4: Commit**

```bash
git add tools/depot-to-apk src/SilksongLauncher.Launcher/app/src/main/res \
        docs/icon.png docs/icon-source
git commit -m "feat: establish Dual Souls product identity"
```

### Task 17: Add continuous checks and adapt the signed release workflow

**Files:**
- Create: `.github/workflows/check.yml`
- Modify: `.github/workflows/release.yml`
- Modify: `tools/docker/apk.Dockerfile`
- Modify: `README.md`
- Modify: `NOTICE.md`

- [ ] **Step 1: Add non-proprietary CI**

`check.yml` runs on pull requests and pushes. It builds the Docker environment,
runs `make test`, builds the launcher APK with an ephemeral debug key, verifies
that no forbidden game filenames or extensions are tracked, and uploads test
reports. It does not download a game depot.

- [ ] **Step 2: Remove elapsed-time cancellation from workflows**

Remove `timeout-minutes` from `release.yml` and do not add it to `check.yml`.
Retain `concurrency.cancel-in-progress: false` for releases. Workflow progress
is visible through step logs and job heartbeats rather than cancellation.

- [ ] **Step 3: Adapt release identity and signing documentation**

Keep the existing stable-keystore contract with these secrets:

```text
ANDROID_KEYSTORE_BASE64
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_PASSWORD
ANDROID_KEY_ALIAS
```

Change the generated dry-run key alias and distinguished name to Dual Souls,
rename the release artifact to `DualSouls-${VERSION_NAME}.apk`, and fail a publishing
run when any signing secret is absent. A dry run may use an ephemeral key and
must remain unpublished.

- [ ] **Step 4: Verify version and artifact alignment**

The release job reads `VERSION`, derives the numeric version code, checks
manifest version name/code and package ID, verifies the certificate, calculates
SHA-256, uploads the APK, downloads it into a fresh directory, repeats hash and
signature checks, and only then creates the GitHub release.

- [ ] **Step 5: Update legal and build documentation**

Document both supported Linux sources, one-package profile behavior, ARM64-only
requirements, emulator limits, on-device build flow, mod/skin formats,
dual-screen device gate, signing backup requirement, and the prohibition on
committing game/Unity content.

- [ ] **Step 6: Run a dry release and commit**

Dispatch `Release APK` with `dry_run=true`. Expected: checks pass, a signed
artifact is downloadable from the workflow, no tag or GitHub release is
created, and the APK contains no proprietary content.

```bash
git add .github tools/docker README.md NOTICE.md
git commit -m "ci: verify and sign Dual Souls releases"
```

### Task 18: Complete the real-device regression and first release

**Files:**
- Create: `docs/verification/release-checklist.md`
- Create: `docs/verification/releases.md`
- Modify: `VERSION`

- [ ] **Step 1: Run the complete Thor matrix**

On `bfa98654`, prove clean and update installs, both profile builds, menu and
representative gameplay, saves, suspend/resume, audio/video/input, single and
dual display, display-specific touch, game switching, base and safe launches,
one shared mod, one game-specific mod per game, multiple skin discovery, skin
activation, death rotation, invalid-pack isolation, build interruption,
rollback, and profile-scoped reset.

- [ ] **Step 2: Record exact evidence**

Record commit, version, APK SHA-256, signing certificate SHA-256, source
manifest hashes, Android/device versions, tested scenes, test results, known
limitations, and recovery results. Exclude credentials and proprietary files.

- [ ] **Step 3: Align release identifiers**

Set `VERSION` to the chosen numeric semantic version and commit it. Confirm
that `versionName`, derived `versionCode`, tag `v${VERSION_NAME}`, release title,
and `DualSouls-${VERSION_NAME}.apk` agree.

- [ ] **Step 4: Publish and verify the downloaded artifact**

Dispatch the release workflow with `dry_run=false`. Download the published APK
into a fresh directory, verify its SHA-256 and signing certificate against the
workflow evidence, install it over the preceding signed build, and repeat
launcher plus one launch of each game.

- [ ] **Step 5: Commit verification evidence**

```bash
git add VERSION docs/verification
git commit -m "docs: record Dual Souls release verification"
```

## Final completion criteria

Implementation is complete only when all checkboxes pass and the following are
simultaneously true:

- every design-traceability row is `COMPLETE`; no `BLOCKER`, `DEFERRED`, or
  `NOT-STARTED` state remains at release time;

- one installed package selects and launches both games;
- each game uses its own exact Linux source, Unity toolchain, assemblies,
  native output, data package, settings, saves, logs, and recoverable
  generation;
- both games render through Vulkan and pass representative gameplay tests;
- Silksong's dual-screen features regress cleanly and Hollow Knight has a
  working shared-renderer screen;
- mods and skins are discovered from shared libraries but resolved and applied
  through per-game compatibility adapters;
- death rotation changes skin only after stable respawn and rolls back safely;
- Robolectric covers launcher and state logic while the ARM64 Thor proves the
  production APK, native build, Vulkan, multi-display, input, and performance
  path; the x86-64 emulator is not claimed as production-pipeline evidence;
- the combined diagonal Hollow Knight/Silksong icon is packaged in adaptive,
  legacy, round, and monochrome forms;
- the release APK contains no proprietary game or Unity content, is signed by
  the durable release key, carries aligned version identifiers, and passes
  fresh-download verification.
