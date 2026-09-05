package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.contracts.SkinWarning
import dev.silksong.launcher.skins.documents.SkinImportReceiptDocument
import org.junit.Assert.*
import org.junit.Test

class SkinReceiptSummaryTest {
    @Test fun `verified receipt preserves source status and warning order without payloads`() {
        val reader = SkinReceiptSummaryReader { digest ->
            assertEquals("d".repeat(64), digest)
            SkinResult.Ok(receipt(listOf(SkinWarning("IGNORED_UNKNOWN", "61"), SkinWarning("IGNORED_SWAP", "62"))))
        }
        val summary = reader.read("a".repeat(64), "d".repeat(64))
        assertEquals("source.zip", summary.archiveName)
        assertEquals("UNVERIFIED_SOURCE", summary.sourceStatus)
        assertEquals(listOf("IGNORED_UNKNOWN: 61", "IGNORED_SWAP: 62"), summary.warnings)
        assertNull(summary.error)
    }

    @Test fun `foreign candidate receipt is not shown as this packs provenance`() {
        val result = SkinReceiptSummaryReader { SkinResult.Ok(receipt(emptyList())) }.read("b".repeat(64), "d".repeat(64))
        assertEquals(SkinImportCode.IMPORT_RECEIPT_CORRUPT, result.error!!.code)
        assertNull(result.archiveName)
    }

    @Test fun `corrupt receipt is explicit not no warnings`() {
        val error = SkinResult.Error(SkinImportCode.IMPORT_RECEIPT_CORRUPT, "digest mismatch")
        val summary = SkinReceiptSummaryReader { error }.read("a".repeat(64), "d".repeat(64))
        assertSame(error, summary.error)
        assertNull(summary.sourceStatus)
    }

    @Test fun `large ordered warning list has a bounded honest display count`() {
        val summary = SkinReceiptSummaryReader {
            SkinResult.Ok(receipt(List(40) { SkinWarning("IGNORED_UNKNOWN", "%02x".format(it)) }))
        }.read("a".repeat(64), "d".repeat(64))
        assertEquals(32, summary.warnings.size)
        assertEquals(8, summary.omittedWarnings)
        assertEquals("IGNORED_UNKNOWN: 00", summary.warnings.first())
    }

    @Test fun `unavailable verifier cannot abort library status rendering`() {
        val summary = SkinReceiptSummaryReader { error("receipt reader unavailable") }
            .read("a".repeat(64), "d".repeat(64))
        assertEquals(SkinImportCode.IMPORT_RECEIPT_CORRUPT, summary.error!!.code)
    }

    private fun receipt(warnings: List<SkinWarning>) = SkinImportReceiptDocument(
        candidateKey = "a".repeat(64), archiveSha256 = "c".repeat(64), archiveName = "source.zip",
        candidateRawPathHex = "61", layoutCode = 0, warnings = warnings,
    )
}
