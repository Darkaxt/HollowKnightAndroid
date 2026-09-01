// Installing a mod, from a folder the user points at.
//
// The alternative is the one this app started with: "put the DLL in
// Android/data/com.jakobkhansen.silksong/files/mods". That is a path a person
// has to reach with a file manager, on a device where the file manager may not
// show Android/data at all, having first extracted an archive somewhere. It
// works, and it is not something to ask of somebody who has just downloaded a
// mod on the device they are holding.
//
// So: pick the folder the mod came in, and it is copied in whole. Whole,
// rather than the DLLs out of it, because a mod is often a plugin plus the
// library it was split out of plus a config it ships with, and because a
// BepInEx download that still has its BepInEx/plugins layout is a shape people
// will point at -- Mods.all walks the tree, and mod-weaver skips the copies of
// BepInEx and Harmony such a download brings with it.

package dev.silksong.launcher

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.DocumentsContract
import java.io.File
import java.io.IOException

object ModImport {

    /**
     * A folder is not a place to put a game in, so the guards are generous but
     * present: somebody who picks their whole SD card should be told, not
     * silently sent to fill the app's storage.
     */
    private const val MAX_FILES = 4_000
    private const val MAX_BYTES = 512L * 1024 * 1024

    /** What a completed import copied. */
    data class Result(val name: String, val files: Int, val plugins: Int)

    fun intent(): Intent = DepotLocation.pickIntent()

    /**
     * Copies the picked folder into the mods folder, replacing any earlier
     * copy of the same name.
     *
     * Replacing rather than merging, because the case this is for is
     * installing a newer version of something already installed, and a merge
     * would leave the old plugin beside the new one -- two copies of the same
     * assembly, which the weaver would then refuse as a duplicate.
     */
    fun copy(context: Context, uri: Uri, mods: File): Result {
        val name = folderName(context, uri) ?: throw IOException("that folder has no name")
        val safe = name.replace(Regex("[^A-Za-z0-9._ -]"), "_").trim().ifEmpty { "mod" }
        val target = File(mods, safe)

        // Into a temporary name first, so an import that fails half way
        // through does not leave a mod that is partly the old one and partly
        // the new.
        val staging = File(mods, ".$safe.incoming")
        staging.deleteRecursively()
        staging.mkdirs()

        val tally = Tally()
        try {
            // The fast path, and the one that will normally be taken: the
            // picker hands back a document id that IS a path on this device's
            // own storage, and a plain recursive copy is both quicker and free
            // of the provider's per-file query cost.
            val direct = DepotLocation.pathFor(uri)
            if (direct != null && direct.isDirectory) copyTree(direct, staging, tally)
            else copyDocuments(context, uri, DocumentsContract.getTreeDocumentId(uri), staging, tally)

            if (tally.files == 0) throw IOException("there is nothing in that folder")

            target.deleteRecursively()
            if (!staging.renameTo(target)) {
                // Rename can fail across the same volume for reasons that are
                // not worth diagnosing here; copying is slower and always
                // works.
                copyTree(staging, target.apply { mkdirs() }, Tally())
                staging.deleteRecursively()
            }
        } catch (t: Throwable) {
            staging.deleteRecursively()
            throw t
        }

        val plugins = target.walkTopDown().count { it.isFile && it.extension.equals("dll", true) }
        LauncherLog.log("mods: imported $safe -- ${tally.files} file(s), $plugins assembly/assemblies")
        return Result(safe, tally.files, plugins)
    }

    private class Tally {
        var files = 0
        var bytes = 0L

        fun add(size: Long) {
            files++
            bytes += size
            if (files > MAX_FILES || bytes > MAX_BYTES) {
                throw IOException(
                    "that folder is too big to be a mod (over " +
                        "${MAX_BYTES / (1024 * 1024)} MB or $MAX_FILES files). " +
                        "Pick the mod's own folder rather than the one it is inside.",
                )
            }
        }
    }

    private fun copyTree(from: File, into: File, tally: Tally) {
        for (child in from.listFiles().orEmpty()) {
            val destination = File(into, child.name)
            if (child.isDirectory) {
                destination.mkdirs()
                copyTree(child, destination, tally)
            } else {
                tally.add(child.length())
                child.inputStream().use { input ->
                    destination.outputStream().use { output -> input.copyTo(output) }
                }
            }
        }
    }

    /**
     * The same copy, through the provider.
     *
     * Needed because not every folder the picker offers has a path behind it:
     * Downloads is served by a provider of its own, and so is anything on a
     * network or cloud volume that happens to be mounted.
     */
    private fun copyDocuments(
        context: Context,
        tree: Uri,
        documentId: String,
        into: File,
        tally: Tally,
    ) {
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, documentId)
        val projection = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
            DocumentsContract.Document.COLUMN_SIZE,
        )
        context.contentResolver.query(children, projection, null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val id = cursor.getString(0)
                val displayName = cursor.getString(1) ?: continue
                val mime = cursor.getString(2)
                val size = if (cursor.isNull(3)) 0L else cursor.getLong(3)
                val destination = File(into, displayName.replace('/', '_'))

                if (mime == DocumentsContract.Document.MIME_TYPE_DIR) {
                    destination.mkdirs()
                    copyDocuments(context, tree, id, destination, tally)
                } else {
                    tally.add(size)
                    val file = DocumentsContract.buildDocumentUriUsingTree(tree, id)
                    context.contentResolver.openInputStream(file)?.use { input ->
                        destination.outputStream().use { output -> input.copyTo(output) }
                    } ?: throw IOException("could not read $displayName")
                }
            }
        } ?: throw IOException("that folder could not be read")
    }

    /** The folder's own name, which is what the mod ends up called. */
    private fun folderName(context: Context, uri: Uri): String? {
        val id = runCatching { DocumentsContract.getTreeDocumentId(uri) }.getOrNull() ?: return null
        val document = runCatching {
            DocumentsContract.buildDocumentUriUsingTree(uri, id)
        }.getOrNull()

        if (document != null) {
            runCatching {
                context.contentResolver.query(
                    document,
                    arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME),
                    null, null, null,
                )?.use { if (it.moveToFirst()) it.getString(0) else null }
            }.getOrNull()?.let { if (it.isNotBlank()) return it }
        }

        // No provider answer: the tail of the document id is the folder name.
        return id.substringAfterLast(':').substringAfterLast('/').ifBlank { null }
    }
}
