package dev.silksong.launcher.skins.documents

data class SkinGameDocument(
    val gameVersion: String,
    val catalogId: String,
    val assetRoot: String,
    val textures: Map<String, String>,
)

data class SkinManifestDocument(
    val schemaVersion: Int = 1,
    val id: String,
    val name: String,
    val author: String,
    val contentSha256: String,
    val games: Map<String, SkinGameDocument>,
    val license: String? = null,
    val source: String? = null,
    val homepage: String? = null,
    val attribution: String? = null,
    val preview: String? = null,
)
