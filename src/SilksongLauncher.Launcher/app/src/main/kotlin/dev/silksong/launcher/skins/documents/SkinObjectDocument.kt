package dev.silksong.launcher.skins.documents

data class SkinFileDocument(
    val path: String,
    val length: Long,
    val sha256: String,
)

data class SkinObjectDocument(
    val schemaVersion: Int = 1,
    val treeSha256: String,
    val contentSha256: String,
    val manifestSha256: String,
    val fileCount: Int,
    val payloadBytes: Long,
    val files: List<SkinFileDocument>,
)
