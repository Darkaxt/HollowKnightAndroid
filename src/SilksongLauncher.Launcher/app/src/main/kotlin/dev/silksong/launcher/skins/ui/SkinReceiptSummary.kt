package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinImportReceiptDocument

internal data class SkinReceiptSummary(
    val archiveName: String? = null,
    val sourceStatus: String? = null,
    val warnings: List<String> = emptyList(),
    val omittedWarnings: Int = 0,
    val error: SkinResult.Error? = null,
)

/** Worker-only, digest-verified receipt observation. No links are opened and no source is downloaded. */
internal class SkinReceiptSummaryReader(
    private val verify: (receiptSha256: String) -> SkinResult<SkinImportReceiptDocument>,
) {
    fun read(candidateKey: String, receiptSha256: String): SkinReceiptSummary = try {
        when (val result = verify(receiptSha256)) {
            is SkinResult.Error -> SkinReceiptSummary(error = result)
            is SkinResult.Ok -> {
                val receipt = result.value
                if (receipt.candidateKey != candidateKey) SkinReceiptSummary(error = SkinResult.Error(
                    SkinImportCode.IMPORT_RECEIPT_CORRUPT, "Receipt candidate identity does not match this pack",
                )) else SkinReceiptSummary(
                    receipt.archiveName, receipt.signatureStatus,
                    receipt.warnings.take(32).map { "${it.code}: ${it.sourceRawPathHex}" },
                    (receipt.warnings.size - 32).coerceAtLeast(0),
                )
            }
        }
    } catch (error: Exception) {
        SkinReceiptSummary(error = SkinResult.Error(SkinImportCode.IMPORT_RECEIPT_CORRUPT,
            "Receipt details unavailable: ${error.message}"))
    }

    companion object {
        val unavailable = SkinReceiptSummaryReader {
            SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "Receipt warnings have not been loaded")
        }
    }
}
