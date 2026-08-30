package dev.silksong.launcher.build

import java.io.File

data class InstalledGeneration(
    val id: String,
    val profileId: String,
    val sourceManifestSha256: String,
    val toolchainId: String,
    val patchManifestSha256: String,
    val files: Map<String, String>,
    val root: File,
)

data class GenerationMetadata(
    val sourceManifestSha256: String,
    val toolchainId: String,
    val patchManifestSha256: String,
)
