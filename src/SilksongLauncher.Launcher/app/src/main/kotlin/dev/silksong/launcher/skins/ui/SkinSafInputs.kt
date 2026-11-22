package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.importing.SkinImportInput
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.util.concurrent.atomic.AtomicBoolean
import java.util.zip.Deflater
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

internal enum class SkinDocumentKind { FILE, DIRECTORY, VIRTUAL, UNKNOWN }
internal data class SkinDocument(val id: String, val displayName: String?, val kind: SkinDocumentKind)
internal interface SkinDocumentCursor : AutoCloseable { fun next(): SkinDocument? }
internal interface SkinDocumentProvider {
    fun file(document: String): SkinDocument
    fun children(tree: String): SkinDocumentCursor
    fun name(tree: String): String? = null
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

    /**
     * A chosen folder imports like an archive of itself (#66). Its PNGs, at any depth up to
     * [MAX_FOLDER_DEPTH], are streamed as one uncompressed ZIP named after the folder, keeping their
     * relative paths, so the normal quarantine, candidate discovery and PNG validation decide which
     * skins it holds: an unpacked skin, a nested Silksong pack, or a folder of several skins. Archives
     * found in the same walk are imported separately as before. All listings share one row budget.
     */
    fun folder(tree: String): SkinResult<List<SkinImportInput>> = try {
        val identities = mutableSetOf<String>()
        var rows = 0
        val archives = mutableListOf<SkinDocument>()
        val pngs = mutableListOf<Pair<String, SkinDocument>>()
        val rootOthers = mutableListOf<SkinDocument>()
        var overflow = false
        // Breadth-first so the root's own rows are read first; paths are relative to the chosen folder.
        val pending = ArrayDeque(listOf(Triple(tree, "", 0)))
        while (pending.isNotEmpty()) {
            val (document, path, depth) = pending.removeFirst()
            val files = mutableListOf<SkinDocument>()
            val directories = mutableListOf<SkinDocument>()
            provider.children(document).use { cursor ->
                while (true) {
                    val row = cursor.next() ?: break
                    if (++rows > rowLimit) { overflow = true; break }
                    require(identities.add(row.id)) { "Duplicate document identity" }
                    when (row.kind) {
                        SkinDocumentKind.FILE -> files += row
                        SkinDocumentKind.DIRECTORY -> directories += row
                        SkinDocumentKind.VIRTUAL, SkinDocumentKind.UNKNOWN -> error("Unsupported document kind")
                    }
                }
            }
            if (overflow) break
            for (file in files.sortedBy { it.id }) when {
                isArchive(file) -> archives += file
                isPng(file) -> pngs += "$path${component(file.displayName)}" to file
                depth == 0 -> rootOthers += file
            }
            if (depth < MAX_FOLDER_DEPTH) {
                directories.sortedBy { it.id }.forEach {
                    pending += Triple(it.id, "$path${component(it.displayName)}/", depth + 1)
                }
            }
        }
        if (overflow) {
            SkinResult.Error(SkinImportCode.LIMIT_EXCEEDED, "Selected folder exceeds $rowLimit provider rows")
        } else {
            val inputs = mutableListOf<SkinImportInput>()
            // A skin folder's readme or preview files are not imports; a folder without PNGs keeps its own files.
            (archives + rootOthers.takeIf { pngs.isEmpty() }.orEmpty()).forEach {
                inputs += SkinImportInput.ImmediateFolderFile(it.displayName, it.id, opener(it.id))
            }
            if (pngs.isNotEmpty()) inputs += packed(provider.name(tree), tree, pngs)
            SkinResult.Ok(inputs)
        }
    } catch (error: Exception) {
        invalid(error)
    }

    private fun component(name: String?): String {
        val value = requireNotNull(name) { "Document name missing" }
        require(value.isNotEmpty() && value != "." && value != ".." && value.none { it < ' ' || it == '/' || it == '\\' }) {
            "Unsafe document name"
        }
        return value
    }

    private fun packed(folderName: String?, folderId: String, pngs: List<Pair<String, SkinDocument>>): SkinImportInput {
        val name = folderName.orEmpty().filter { it >= ' ' && it != '/' && it != '\\' }.trim()
            .takeIf { it.isNotEmpty() && it != "." && it != ".." } ?: "Skin"
        val entries = pngs.map { (path, row) -> "$name/$path" to opener(row.id) }
        val opened = AtomicBoolean(false)
        return SkinImportInput.ImmediateFolderFile("$name.zip", folderId) {
            check(opened.compareAndSet(false, true)) { "Provider input was already opened" }
            PackedFolderStream(entries)
        }
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

    private fun isPng(row: SkinDocument) = row.displayName?.endsWith(".png", ignoreCase = true) == true
    private fun isArchive(row: SkinDocument) =
        row.displayName?.substringAfterLast('.', "")?.lowercase() in setOf("zip", "rar", "7z")

    private companion object {
        /** Deep enough for a parent folder of unpacked Silksong loader packs (plugins/Patchwork/Spritesheets). */
        const val MAX_FOLDER_DEPTH = 6
    }
}

/**
 * A ZIP of the given entries produced on demand as it is read: no thread, no temporary file, and
 * stored-level deflate so its size stays at the PNG bytes plus headers. Each source opens only when
 * its entry is reached and closes as soon as it ends.
 */
internal class PackedFolderStream(private val entries: List<Pair<String, () -> InputStream>>) : InputStream() {
    private val buffer = ByteArrayOutputStream()
    private val zip = ZipOutputStream(buffer).apply { setLevel(Deflater.NO_COMPRESSION) }
    private val chunk = ByteArray(64 * 1024)
    private var index = 0
    private var current: InputStream? = null
    private var finished = false
    private var pending = ByteArray(0)
    private var position = 0

    override fun read(): Int {
        val one = ByteArray(1)
        return if (read(one, 0, 1) < 0) -1 else one[0].toInt() and 0xff
    }

    override fun read(target: ByteArray, offset: Int, length: Int): Int {
        if (length == 0) return 0
        while (position == pending.size) if (!pump()) return -1
        val count = minOf(length, pending.size - position)
        System.arraycopy(pending, position, target, offset, count)
        position += count
        return count
    }

    private fun pump(): Boolean {
        if (finished) return false
        val input = current
        when {
            input != null -> {
                val read = input.read(chunk)
                if (read < 0) { input.close(); current = null; zip.closeEntry() } else zip.write(chunk, 0, read)
            }
            index == entries.size -> { zip.finish(); finished = true }
            else -> {
                val (name, open) = entries[index++]
                zip.putNextEntry(ZipEntry(name))
                current = open()
            }
        }
        pending = buffer.toByteArray()
        buffer.reset()
        position = 0
        return true
    }

    override fun close() {
        current?.close()
        current = null
        finished = true
    }
}
