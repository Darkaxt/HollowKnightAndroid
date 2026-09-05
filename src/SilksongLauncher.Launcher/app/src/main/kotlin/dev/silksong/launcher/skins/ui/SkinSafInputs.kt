package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.SkinImportInput
import java.io.InputStream
import java.util.concurrent.atomic.AtomicBoolean

internal enum class SkinDocumentKind { FILE, DIRECTORY, VIRTUAL, UNKNOWN }
internal data class SkinDocument(val id: String, val displayName: String?, val kind: SkinDocumentKind)
internal interface SkinDocumentCursor : AutoCloseable { fun next(): SkinDocument? }
internal interface SkinDocumentProvider {
    fun file(document: String): SkinDocument
    fun children(tree: String): SkinDocumentCursor
    fun open(document: String): InputStream
}

/** Run on a worker. Only the coordinator may consume the one-shot openers; this adapter copies nothing. */
internal class SkinSafInputs(
    private val provider: SkinDocumentProvider,
    private val rowLimit: Int = SkinLimits.V1.providerRows,
) {
    init { require(rowLimit in 1..SkinLimits.V1.providerRows) }

    fun file(document: String): SkinResult<SkinImportInput> = guarded {
        val row = provider.file(document)
        require(row.kind == SkinDocumentKind.FILE) { "Selected document is not a regular file" }
        SkinImportInput.SelectedFile(row.displayName, opener(row.id))
    }

    fun folder(tree: String): SkinResult<List<SkinImportInput>> = try {
        val files = mutableListOf<SkinDocument>()
        val identities = mutableSetOf<String>()
        provider.children(tree).use { cursor ->
            var rows = 0
            while (true) {
                val row = cursor.next() ?: break
                if (++rows > rowLimit) return SkinResult.Error(
                    SkinImportCode.LIMIT_EXCEEDED, "Selected folder exceeds $rowLimit immediate provider rows",
                )
                require(identities.add(row.id)) { "Duplicate immediate document identity" }
                when (row.kind) {
                    SkinDocumentKind.FILE -> files += row
                    SkinDocumentKind.DIRECTORY -> Unit // Never recurse.
                    SkinDocumentKind.VIRTUAL, SkinDocumentKind.UNKNOWN -> error("Unsupported immediate document kind")
                }
            }
        }
        SkinResult.Ok(files.sortedBy { it.id }.map { row ->
            SkinImportInput.ImmediateFolderFile(row.displayName, row.id, opener(row.id))
        })
    } catch (error: Exception) {
        invalid(error)
    }

    private fun opener(document: String): () -> InputStream {
        val opened = AtomicBoolean(false)
        return {
            check(opened.compareAndSet(false, true)) { "Provider input was already opened" }
            provider.open(document)
        }
    }

    private fun <T> guarded(action: () -> T): SkinResult<T> = try { SkinResult.Ok(action()) }
    catch (error: Exception) { invalid(error) }
    private fun invalid(error: Exception) = SkinResult.Error(
        SkinImportCode.INVALID_INPUT, "SAF input unavailable: ${error.message}",
    )
}
