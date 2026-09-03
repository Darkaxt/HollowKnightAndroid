package dev.silksong.launcher.skins.contracts

data class SkinLimits(
    val quarantineBytes: Long,
    val entries: Int,
    val directories: Int,
    val sourcePathBytes: Int,
    val sourceDepth: Int,
    val uncompressedBytes: Long,
    val expansionRatio: Int,
    val candidates: Int,
    val installedPacks: Int,
    val mappings: Int,
    val regularFiles: Int,
    val observedNodes: Int,
    val candidateDirectories: Int,
    val providerRows: Int,
    val textureBytes: Long,
    val previewBytes: Long,
    val payloadBytes: Long,
    val dimension: Int,
    val decodedPixels: Long,
) {
    companion object {
        val V1 = SkinLimits(
            268435456,
            4096,
            512,
            512,
            16,
            536870912,
            100,
            128,
            64,
            205,
            207,
            512,
            64,
            1024,
            16777216,
            4194304,
            268435456,
            8192,
            33554432,
        )
    }
}
