package dev.silksong.launcher.build

import java.nio.charset.StandardCharsets
import java.security.MessageDigest

data class UnityToolchainDescriptor(
    val unityVersion: String,
    val editorUrl: String,
    val editorSha256: String,
    val androidModuleUrl: String,
    val androidModuleSha256: String,
    val editorArchiveBytes: Long,
    val androidModuleBytes: Long,
    val editorRequiredSha256: Map<String, String>,
) {
    init {
        require(unityVersion.matches(Regex("[0-9]+\\.[0-9]+\\.[0-9]+[abfp][0-9]+"))) {
            "Invalid Unity version: $unityVersion"
        }
        require(editorUrl.startsWith("https://download.unity3d.com/download_unity/")) {
            "Unity editor URL must use Unity's download service"
        }
        require(androidModuleUrl.startsWith("https://download.unity3d.com/download_unity/")) {
            "Unity Android module URL must use Unity's download service"
        }
        requireSha256("editor", editorSha256)
        requireSha256("Android module", androidModuleSha256)
        require(editorArchiveBytes > 0L)
        require(androidModuleBytes > 0L)
        require(editorRequiredSha256.isNotEmpty())
        editorRequiredSha256.forEach { (path, hash) ->
            require(path.startsWith("Editor/") && !path.contains("..")) {
                "Invalid required editor path: $path"
            }
            requireSha256(path, hash)
        }
    }

    /** Stable identity for the complete verified descriptor, not just its version label. */
    val contentHash: String by lazy {
        val canonical = buildString {
            appendLine(unityVersion)
            appendLine(editorUrl)
            appendLine(editorSha256)
            appendLine(androidModuleUrl)
            appendLine(androidModuleSha256)
            appendLine(editorArchiveBytes)
            appendLine(androidModuleBytes)
            editorRequiredSha256.toSortedMap().forEach { (path, hash) ->
                append(path).append('=').append(hash).append('\n')
            }
        }
        sha256(canonical.toByteArray(StandardCharsets.UTF_8))
    }

    private fun requireSha256(label: String, value: String) {
        require(value.matches(Regex("[0-9a-f]{64}"))) {
            "$label SHA-256 must be 64 lowercase hexadecimal characters"
        }
    }

    private fun sha256(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256")
            .digest(bytes)
            .joinToString("") { "%02x".format(it) }
}
