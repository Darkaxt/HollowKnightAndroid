package dev.silksong.launcher.profiles

import android.content.res.AssetManager
import com.google.gson.Gson
import com.google.gson.annotations.SerializedName
import java.io.InputStreamReader
import java.nio.charset.StandardCharsets
import java.security.MessageDigest

data class ProfileManifest(
    val schemaVersion: Int,
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
    val action: ManifestAction,
    val ownerRelativePath: String? = null,
)

enum class ManifestAction {
    @SerializedName("transform")
    TRANSFORM,

    @SerializedName("copy")
    COPY,

    @SerializedName("exclude")
    EXCLUDE,

    @SerializedName("replaceAtAssembly")
    REPLACE_AT_ASSEMBLY,
}

object ProfileManifestLoader {
    private val sha256 = Regex("^[0-9a-fA-F]{64}$")

    fun load(
        assets: AssetManager,
        assetPath: String,
    ): ProfileManifest = assets.open(assetPath).use { input ->
        val manifest = InputStreamReader(input, StandardCharsets.UTF_8).use { reader ->
            Gson().fromJson(reader, ProfileManifest::class.java)
        } ?: throw IllegalArgumentException("Profile manifest is empty: $assetPath")
        validate(manifest, assetPath)
        manifest
    }

    private fun validate(
        manifest: ProfileManifest,
        assetPath: String,
    ) {
        require(manifest.schemaVersion == 1) {
            "Unsupported profile manifest schema in $assetPath: ${manifest.schemaVersion}"
        }
        require(manifest.converterReportSchema == 1) {
            "Unsupported converter report schema in $assetPath: ${manifest.converterReportSchema}"
        }
        require(manifest.profileId.isNotBlank() && manifest.gameVersion.isNotBlank()) {
            "Profile manifest identity is incomplete: $assetPath"
        }
        require(manifest.platform.equals("linux", ignoreCase = true)) {
            "Profile manifest is not Linux: $assetPath"
        }

        val paths = HashSet<String>()
        for (file in manifest.requiredFiles) {
            require(normalizeRelativePath(file.relativePath) == file.relativePath) {
                "Profile manifest path is not normalized: ${file.relativePath}"
            }
            require(paths.add(file.relativePath.lowercase())) {
                "Profile manifest contains a duplicate path: ${file.relativePath}"
            }
            require(file.size >= 0 && sha256.matches(file.sha256)) {
                "Profile manifest file identity is invalid: ${file.relativePath}"
            }
        }
        require(sha256.matches(manifest.manifestSha256) &&
            manifest.manifestSha256.equals(manifest.computeSha256(), ignoreCase = true)
        ) {
            "Profile manifest semantic hash is invalid: $assetPath"
        }
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

fun ProfileManifest.computeSha256(): String {
    val canonical = buildString {
        append(schemaVersion).append('\n')
        append(profileId).append('\n')
        append(gameVersion).append('\n')
        append(unityVersion).append('\n')
        append(platform.lowercase()).append('\n')
        append(converterReportSchema).append('\n')
        requiredFiles
            .sortedWith(
                compareBy<ManifestFile> { it.relativePath.lowercase() }
                    .thenBy { it.relativePath },
            )
            .forEach { file ->
                append(file.relativePath).append('\u0000')
                append(file.size).append('\u0000')
                append(file.sha256.lowercase()).append('\u0000')
                append(file.action.name).append('\u0000')
                append(file.ownerRelativePath ?: "").append('\n')
            }
    }
    return MessageDigest.getInstance("SHA-256")
        .digest(canonical.toByteArray(StandardCharsets.UTF_8))
        .joinToString("") { byte -> "%02x".format(byte) }
}

fun ProfileManifest.withComputedSha256(): ProfileManifest =
    copy(manifestSha256 = computeSha256())
