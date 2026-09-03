package dev.silksong.launcher.skins.contracts

import java.io.File

data class QuarantinedArchive(
    val file: File,
    val archiveSha256: String,
    val byteCount: Long,
    val archiveName: String,
)

data class StagedPayload(
    val relativePath: String,
    val sha256: String,
    val length: Long,
    val file: File,
)

data class SkinAlias(
    val sourceRawPathHex: String,
    val target: String,
    val rule: String,
)

data class SkinWarning(
    val code: String,
    val sourceRawPathHex: String,
)

data class ZipArchive(
    val file: File,
    val entries: List<RawZipEntry>,
    val ignoredExtraMetadata: Boolean = false,
)

data class RawZipEntry(
    val centralIndex: Int,
    val rawName: ByteArray,
    val flags: Int,
    val method: Int,
    val crc32: Long,
    val compressedSize: Long,
    val uncompressedSize: Long,
    val localOffset: Long,
    val dataOffset: Long,
    val dataEnd: Long,
    val directory: Boolean,
    val descriptorLength: Int = 0,
    val ignoredExtraMetadata: Boolean = false,
)

data class SkinCandidate(
    val rawPrefix: ByteArray,
    val layoutCode: Int,
    val entries: List<RawZipEntry>,
)

data class CandidateSet(
    val candidates: List<SkinCandidate>,
    val warnings: List<SkinWarning>,
)

data class CatalogMapping(
    val textures: Map<String, RawZipEntry>,
    val aliases: List<SkinAlias>,
    val warnings: List<SkinWarning>,
)

data class PngInfo(
    val width: Int,
    val height: Int,
    val byteCount: Long,
)

data class DecodeResult(
    val width: Int,
    val height: Int,
    val pixelCount: Long,
)

data class SkinNodeIdentity(
    val fileKey: String,
    val size: Long,
    val regularFile: Boolean,
)

data class PreparedSkinCandidate(
    val candidateKey: String,
    val rawPrefix: ByteArray,
    val layoutCode: Int,
    val name: String,
    val contentSha256: String,
    val importReceiptBytes: ByteArray,
    val importReceiptSha256: String,
    val payloads: List<StagedPayload>,
    val mappings: Map<String, String>,
    val stagingRoot: File,
)

sealed interface CandidatePreparationResult {
    data class Ready(val candidate: PreparedSkinCandidate) : CandidatePreparationResult
    data class Rejected(
        val rawPrefix: ByteArray,
        val code: SkinImportCode,
        val detail: String,
    ) : CandidatePreparationResult
}

data class BuiltSkin(
    val id: String,
    val candidateKey: String,
    val name: String,
    val contentSha256: String,
    val treeSha256: String,
    val manifestSha256: String,
    val importReceiptSha256: String,
    val manifestBytes: ByteArray,
    val objectBytes: ByteArray,
    val importReceiptBytes: ByteArray,
    val ephemeralRoot: File,
)

data class PublishedSkin(
    val id: String,
    val candidateKey: String,
    val name: String,
    val contentSha256: String,
    val treeSha256: String,
    val manifestSha256: String,
    val importReceiptSha256: String,
    val objectRoot: File,
    val newlyCreatedRoots: List<File>,
)
