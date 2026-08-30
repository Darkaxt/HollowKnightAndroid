package dev.silksong.launcher.profiles

import com.google.gson.Gson
import java.io.File
import java.nio.charset.StandardCharsets

enum class SourceValidationStatus {
    SUPPORTED,
    WINDOWS_SOURCE,
    INCOMPLETE,
    MIXED_BUILD,
    MISSING_VULKAN,
    UNKNOWN_VERSION,
}

data class SourceValidationResult(
    val status: SourceValidationStatus,
    val offendingPath: String? = null,
    val offendingProperty: String? = null,
    val actualValue: String? = null,
)

data class SourceReportFile(
    val path: String,
    val inputSize: Long,
    val inputSha256: String,
    val missingVulkanShaders: Int?,
)

data class SourceReportDiagnostic(
    val code: String,
    val message: String,
    val relativePath: String? = null,
)

data class SourceIdentity(
    val companyName: String?,
    val productName: String?,
    val gameVersion: String?,
    val unityVersion: String,
    val platform: String,
    val buildBranch: String?,
    val buildRevision: Int?,
)

data class HollowKnightSourceReport(
    val schemaVersion: Int,
    val status: String,
    val sourceTreeSha256: String,
    val identity: SourceIdentity,
    val files: List<SourceReportFile>,
    val diagnostics: List<SourceReportDiagnostic>,
)

fun interface HollowKnightSourceReporter {
    fun inspect(sourceRoot: File): HollowKnightSourceReport
}

object HollowKnightSourceReportLoader {
    fun load(reportFile: File): HollowKnightSourceReport =
        reportFile.reader(StandardCharsets.UTF_8).use { reader ->
            Gson().fromJson(reader, HollowKnightSourceReport::class.java)
        } ?: throw IllegalArgumentException(
            "Hollow Knight source report is empty: ${reportFile.path}",
        )
}

class HollowKnightSourceValidator(
    private val profile: GameProfile,
    private val reporter: HollowKnightSourceReporter,
) {
    fun validate(
        sourceRoot: File,
        manifest: ProfileManifest,
    ): SourceValidationResult {
        require(profile.id == "hollow-knight") {
            "HollowKnightSourceValidator requires the hollow-knight profile"
        }
        require(manifest.profileId == profile.id) {
            "Manifest profile ${manifest.profileId} does not match ${profile.id}"
        }
        require(manifest.gameVersion == profile.currentGameVersion) {
            "Manifest version ${manifest.gameVersion} is not the current profile target"
        }
        require(manifest.unityVersion == profile.unityVersion) {
            "Manifest Unity version ${manifest.unityVersion} does not match the profile"
        }
        require(manifest.platform.equals("linux", ignoreCase = true)) {
            "Hollow Knight manifests must describe a Linux source"
        }

        if (!manifest.manifestSha256.equals(manifest.computeSha256(), ignoreCase = true)) {
            return SourceValidationResult(
                SourceValidationStatus.INCOMPLETE,
                offendingProperty = "manifestSha256",
                actualValue = manifest.manifestSha256,
            )
        }

        val report = reporter.inspect(sourceRoot)
        if (report.schemaVersion != manifest.converterReportSchema ||
            report.status != "INVENTORIED"
        ) {
            return SourceValidationResult(
                SourceValidationStatus.INCOMPLETE,
                offendingProperty = "report",
                actualValue = "${report.schemaVersion}:${report.status}",
            )
        }
        val identity = report.identity
        if (identity.platform !in profile.acceptedPlatforms) {
            return SourceValidationResult(
                SourceValidationStatus.WINDOWS_SOURCE,
                offendingProperty = "platform",
                actualValue = identity.platform,
            )
        }
        if (identity.companyName != "Team Cherry") {
            return SourceValidationResult(
                SourceValidationStatus.UNKNOWN_VERSION,
                offendingProperty = "companyName",
                actualValue = identity.companyName,
            )
        }
        if (identity.productName != "Hollow Knight") {
            return SourceValidationResult(
                SourceValidationStatus.UNKNOWN_VERSION,
                offendingProperty = "productName",
                actualValue = identity.productName,
            )
        }
        identity.gameVersion?.let { gameVersion ->
            if (gameVersion != manifest.gameVersion) {
                return SourceValidationResult(
                    SourceValidationStatus.UNKNOWN_VERSION,
                    offendingProperty = "gameVersion",
                    actualValue = gameVersion,
                )
            }
        }
        val expectedRevision = manifest.gameVersion.substringAfterLast('.').toIntOrNull()
        if (expectedRevision == null || identity.buildRevision != expectedRevision) {
            return SourceValidationResult(
                SourceValidationStatus.UNKNOWN_VERSION,
                offendingProperty = "buildRevision",
                actualValue = identity.buildRevision?.toString(),
            )
        }
        if (identity.unityVersion != manifest.unityVersion) {
            return SourceValidationResult(
                SourceValidationStatus.UNKNOWN_VERSION,
                offendingProperty = "unityVersion",
                actualValue = identity.unityVersion,
            )
        }
        report.diagnostics.firstOrNull()?.let { diagnostic ->
            return SourceValidationResult(
                SourceValidationStatus.INCOMPLETE,
                offendingPath = diagnostic.relativePath,
                offendingProperty = diagnostic.code,
                actualValue = diagnostic.message,
            )
        }

        val reportFiles = linkedMapOf<String, SourceReportFile>()
        for (file in report.files) {
            val normalized = normalizeRelativePath(file.path)
                ?: return SourceValidationResult(
                    SourceValidationStatus.INCOMPLETE,
                    offendingPath = file.path,
                    offendingProperty = "path",
                )
            if (reportFiles.put(normalized.lowercase(), file.copy(path = normalized)) != null) {
                return SourceValidationResult(
                    SourceValidationStatus.INCOMPLETE,
                    offendingPath = normalized,
                    offendingProperty = "duplicatePath",
                )
            }
        }

        val manifestPaths = HashSet<String>()
        for (required in manifest.requiredFiles) {
            val normalized = normalizeRelativePath(required.relativePath)
                ?: return SourceValidationResult(
                    SourceValidationStatus.INCOMPLETE,
                    offendingPath = required.relativePath,
                    offendingProperty = "manifestPath",
                )
            val lookupPath = normalized.lowercase()
            if (!manifestPaths.add(lookupPath)) {
                return SourceValidationResult(
                    SourceValidationStatus.INCOMPLETE,
                    offendingPath = normalized,
                    offendingProperty = "duplicateManifestPath",
                )
            }
            val actual = reportFiles[lookupPath]
                ?: return SourceValidationResult(
                    SourceValidationStatus.INCOMPLETE,
                    offendingPath = normalized,
                )
            if (actual.inputSize != required.size) {
                return SourceValidationResult(
                    SourceValidationStatus.MIXED_BUILD,
                    offendingPath = normalized,
                    offendingProperty = "size",
                    actualValue = actual.inputSize.toString(),
                )
            }
            if (!actual.inputSha256.equals(required.sha256, ignoreCase = true)) {
                return SourceValidationResult(
                    SourceValidationStatus.MIXED_BUILD,
                    offendingPath = normalized,
                    offendingProperty = "sha256",
                    actualValue = actual.inputSha256,
                )
            }
        }

        reportFiles.entries.firstOrNull { it.key !in manifestPaths }?.let { entry ->
            return SourceValidationResult(
                SourceValidationStatus.MIXED_BUILD,
                offendingPath = entry.value.path,
                offendingProperty = "unmanifestedFile",
            )
        }

        report.files.firstOrNull { (it.missingVulkanShaders ?: 0) > 0 }?.let { file ->
            return SourceValidationResult(
                SourceValidationStatus.MISSING_VULKAN,
                offendingPath = file.path,
                offendingProperty = "missingVulkanShaders",
                actualValue = file.missingVulkanShaders.toString(),
            )
        }

        return SourceValidationResult(SourceValidationStatus.SUPPORTED)
    }

    private fun normalizeRelativePath(path: String): String? {
        if (path.isBlank() || path.startsWith('/') || path.contains('\\')) return null
        if (path.length >= 2 && path[1] == ':') return null
        val segments = path.split('/')
        if (segments.any {
                it.isEmpty() || it == "." || it == ".." ||
                    it.endsWith('.') || it.endsWith(' ')
            }
        ) {
            return null
        }
        return segments.joinToString("/")
    }
}
