package dev.silksong.launcher.profiles

import java.io.File
import java.nio.file.Files
import java.nio.file.Path
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Before
import org.junit.Test

class HollowKnightSourceValidatorTest {
    private lateinit var sourceRoot: File
    private lateinit var manifest: ProfileManifest

    @Before
    fun setUp() {
        val preferredTemp = Path.of("D:/Temp").takeIf(Files::isDirectory)
        sourceRoot = Files.createTempDirectory(
            preferredTemp,
            "hollow-knight-source-validator-",
        ).toFile()
        manifest = ProfileManifest(
            schemaVersion = 1,
            profileId = "hollow-knight",
            gameVersion = "1.5.12620",
            unityVersion = "6000.0.61f1",
            platform = "linux",
            requiredFiles = listOf(
                ManifestFile(
                    relativePath = "globalgamemanagers",
                    size = 100,
                    sha256 = hash('a'),
                    action = ManifestAction.TRANSFORM,
                ),
                ManifestFile(
                    relativePath = "sharedassets0.assets.resS",
                    size = 200,
                    sha256 = hash('b'),
                    action = ManifestAction.COPY,
                    ownerRelativePath = "sharedassets0.assets",
                ),
            ),
            converterReportSchema = 1,
            manifestSha256 = "",
        ).withComputedSha256()
    }

    @After
    fun tearDown() {
        sourceRoot.deleteRecursively()
    }

    @Test
    fun exact_current_linux_source_is_supported() {
        val reporter = FakeReporter(report())

        val result = validator(reporter).validate(sourceRoot, manifest)

        assertSame(sourceRoot, reporter.inspectedRoot)
        assertEquals(SourceValidationStatus.SUPPORTED, result.status)
        assertEquals(null, result.offendingPath)
        assertEquals(null, result.offendingProperty)
    }

    @Test
    fun windows_source_reports_the_platform_property() {
        val result = validator(FakeReporter(report(platform = "WindowsPlayer")))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.WINDOWS_SOURCE, result.status)
        assertEquals("platform", result.offendingProperty)
        assertEquals("WindowsPlayer", result.actualValue)
    }

    @Test
    fun missing_required_file_reports_its_exact_path() {
        val result = validator(FakeReporter(report(files = report().files.dropLast(1))))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.INCOMPLETE, result.status)
        assertEquals("sharedassets0.assets.resS", result.offendingPath)
    }

    @Test
    fun mixed_build_reports_the_first_hash_mismatch() {
        val files = report().files.map { file ->
            if (file.path == "globalgamemanagers") {
                file.copy(inputSha256 = hash('c'))
            } else {
                file
            }
        }

        val result = validator(FakeReporter(report(files = files)))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.MIXED_BUILD, result.status)
        assertEquals("globalgamemanagers", result.offendingPath)
        assertEquals("sha256", result.offendingProperty)
    }

    @Test
    fun mixed_build_reports_an_unmanifested_extra_file() {
        val files = report().files + SourceReportFile(
            path = "unexpected.bin",
            inputSize = 1,
            inputSha256 = hash('d'),
            missingVulkanShaders = null,
        )

        val result = validator(FakeReporter(report(files = files)))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.MIXED_BUILD, result.status)
        assertEquals("unexpected.bin", result.offendingPath)
        assertEquals("unmanifestedFile", result.offendingProperty)
    }

    @Test
    fun missing_vulkan_reports_the_exact_serialized_file() {
        val files = report().files.map { file ->
            if (file.path == "globalgamemanagers") {
                file.copy(missingVulkanShaders = 1)
            } else {
                file
            }
        }

        val result = validator(FakeReporter(report(files = files)))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.MISSING_VULKAN, result.status)
        assertEquals("globalgamemanagers", result.offendingPath)
        assertEquals("missingVulkanShaders", result.offendingProperty)
    }

    @Test
    fun unknown_version_reports_the_app_info_version_property() {
        val result = validator(FakeReporter(report(gameVersion = "1.5.12612")))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.UNKNOWN_VERSION, result.status)
        assertEquals("gameVersion", result.offendingProperty)
        assertEquals("1.5.12612", result.actualValue)
    }

    @Test
    fun wrong_unity_version_is_an_unknown_version_not_a_mixed_build() {
        val result = validator(FakeReporter(report(unityVersion = "2020.2.2f1")))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.UNKNOWN_VERSION, result.status)
        assertEquals("unityVersion", result.offendingProperty)
        assertEquals("2020.2.2f1", result.actualValue)
    }

    @Test
    fun exact_build_revision_is_authoritative_when_player_settings_is_unreadable() {
        val result = validator(FakeReporter(report(gameVersion = null)))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.SUPPORTED, result.status)
    }

    @Test
    fun wrong_build_revision_is_an_unknown_version() {
        val result = validator(FakeReporter(report(buildRevision = 12612)))
            .validate(sourceRoot, manifest)

        assertEquals(SourceValidationStatus.UNKNOWN_VERSION, result.status)
        assertEquals("buildRevision", result.offendingProperty)
        assertEquals("12612", result.actualValue)
    }

    @Test
    fun changed_manifest_policy_without_a_new_hash_is_rejected() {
        val tampered = manifest.copy(
            requiredFiles = manifest.requiredFiles.map { file ->
                if (file.relativePath == "globalgamemanagers") {
                    file.copy(action = ManifestAction.EXCLUDE)
                } else {
                    file
                }
            },
        )

        val result = validator(FakeReporter(report())).validate(sourceRoot, tampered)

        assertEquals(SourceValidationStatus.INCOMPLETE, result.status)
        assertEquals("manifestSha256", result.offendingProperty)
    }

    private fun validator(reporter: HollowKnightSourceReporter) =
        HollowKnightSourceValidator(
            profile = GameProfiles.require("hollow-knight"),
            reporter = reporter,
        )

    private fun report(
        gameVersion: String? = manifest.gameVersion,
        unityVersion: String = manifest.unityVersion,
        platform: String = "LinuxPlayer",
        buildRevision: Int = 12620,
        files: List<SourceReportFile> = listOf(
            SourceReportFile(
                path = "globalgamemanagers",
                inputSize = 100,
                inputSha256 = hash('a'),
                missingVulkanShaders = 0,
            ),
            SourceReportFile(
                path = "sharedassets0.assets.resS",
                inputSize = 200,
                inputSha256 = hash('b'),
                missingVulkanShaders = 0,
            ),
        ),
    ) = HollowKnightSourceReport(
        schemaVersion = 1,
        status = "INVENTORIED",
        sourceTreeSha256 = hash('e'),
        identity = SourceIdentity(
            companyName = "Team Cherry",
            productName = "Hollow Knight",
            gameVersion = gameVersion,
            unityVersion = unityVersion,
            platform = platform,
            buildBranch = "release-1",
            buildRevision = buildRevision,
        ),
        files = files,
        diagnostics = emptyList(),
    )

    private fun hash(character: Char) = character.toString().repeat(64)

    private class FakeReporter(
        private val report: HollowKnightSourceReport,
    ) : HollowKnightSourceReporter {
        var inspectedRoot: File? = null

        override fun inspect(sourceRoot: File): HollowKnightSourceReport {
            inspectedRoot = sourceRoot
            return report
        }
    }
}
