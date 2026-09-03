package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.CanonicalJson
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.documents.SkinImportReceiptDocument
import java.io.File

class SkinImportReceiptRepository(
    internal val paths: SkinPaths,
    private val fileSystem: SkinFileSystem = AndroidSkinFileSystem(),
    private val catalog: CatalogPathSet = CatalogPathSet.requirePinned(),
) {
    fun verify(importReceiptSha256: String): SkinResult<SkinImportReceiptDocument> =
        verify(paths.importReceiptRoot(importReceiptSha256), importReceiptSha256)

    fun verify(root: File, importReceiptSha256: String): SkinResult<SkinImportReceiptDocument> = try {
        fileSystem.requireContained(root, paths.profileRoot)
        val shard = root.parentFile?.name
        if (!safeDirectory(root) || root.name != importReceiptSha256 || shard != importReceiptSha256.take(2)) {
            corrupt("Import receipt path is invalid")
        }
        val children = fileSystem.list(root)
        children.forEach { fileSystem.requireContained(it, paths.profileRoot) }
        if (children.map { it.name }.toSet() != setOf("import-receipt.json", ".complete")) {
            corrupt("Import receipt directory contains undeclared nodes")
        }
        val marker = File(root, ".complete")
        val documentFile = File(root, "import-receipt.json")
        fileSystem.requireContained(marker, paths.profileRoot)
        fileSystem.requireContained(documentFile, paths.profileRoot)
        if (!safeFile(marker) || fileSystem.identity(marker).size != 0L || !safeFile(documentFile)) {
            corrupt("Import receipt files are invalid")
        }
        val markerIdentity = fileSystem.identity(marker)
        val documentIdentity = fileSystem.identity(documentFile)
        if (!markerIdentity.regularFile || !documentIdentity.regularFile) corrupt("Receipt identities are not regular files")
        if (documentIdentity.size > 8L * 1024 * 1024) corrupt("Import receipt exceeds 8 MiB")
        if (fileSystem.sameFile(marker, documentFile)) corrupt("Receipt files are hard-linked")
        val bytes = fileSystem.openNoFollow(documentFile).use { it.readBytes() }
        val documentIdentityAfter = fileSystem.identity(documentFile)
        if (documentIdentity != documentIdentityAfter ||
            bytes.size.toLong() != documentIdentity.size ||
            SkinIdentity.sha256(bytes) != importReceiptSha256
        ) {
            corrupt("Import receipt changed, or its digest or size mismatches")
        }
        when (val parsed = CanonicalJson.tryParseImportReceipt(bytes, catalog)) {
            is SkinResult.Ok -> parsed
            is SkinResult.Error -> corrupt(parsed.detail)
        }
    } catch (error: CorruptReceipt) {
        SkinResult.Error(SkinImportCode.IMPORT_RECEIPT_CORRUPT, error.message ?: "Import receipt is corrupt")
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.IMPORT_RECEIPT_CORRUPT, "Import receipt verification failed: ${error.message}")
    }

    private fun safeDirectory(file: File) = fileSystem.exists(file) && fileSystem.isDirectory(file) && !fileSystem.isSymbolicLink(file)
    private fun safeFile(file: File) = fileSystem.exists(file) && fileSystem.isRegularFile(file) && !fileSystem.isSymbolicLink(file)
    private class CorruptReceipt(detail: String) : RuntimeException(detail)
    private fun corrupt(detail: String): Nothing = throw CorruptReceipt(detail)
}
