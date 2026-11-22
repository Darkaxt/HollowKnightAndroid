package dev.silksong.launcher.skins.contracts

data class SkinLimits(
    val quarantineBytes: Long,
    val entries: Int,
    val directories: Int,
    val sourcePathBytes: Int,
    val sourceDepth: Int,
    val uncompressedBytes: Long,
    val expansionRatio: Int,
    val decoderMemoryBytes: Long,
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
            536870912,
            4096,
            512,
            512,
            16,
            536870912,
            100,
            67108864,
            128,
            64,
            512,
            514,
            512,
            64,
            1024,
            67108864,
            4194304,
            536870912,
            8192,
            33554432,
        )
    }
}
