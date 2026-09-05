package dev.silksong.launcher.skins.ui

import android.content.ContentResolver
import android.database.Cursor
import android.net.Uri
import android.provider.DocumentsContract
import java.io.InputStream

/** Worker-only SAF transport. No persistent grant, recursive query, MIME filtering, or copying. */
internal class AndroidSkinDocumentProvider(private val resolver: ContentResolver) : SkinDocumentProvider {
    override fun file(document: String): SkinDocument {
        val uri = contentUri(document)
        return query(uri).use { cursor ->
            require(cursor.moveToNext()) { "Selected document is absent" }
            val row = row(cursor, uri, false)
            require(DocumentsContract.getDocumentId(uri) == DocumentsContract.getDocumentId(Uri.parse(row.id))) {
                "Selected document identity changed"
            }
            require(!cursor.moveToNext()) { "Selected document query returned multiple rows" }
            row
        }
    }

    override fun children(tree: String): SkinDocumentCursor {
        val uri = contentUri(tree)
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(uri, DocumentsContract.getTreeDocumentId(uri))
        val cursor = query(children)
        return object : SkinDocumentCursor {
            override fun next(): SkinDocument? = if (cursor.moveToNext()) row(cursor, uri, true) else null
            override fun close() = cursor.close()
        }
    }

    override fun open(document: String): InputStream = requireNotNull(resolver.openInputStream(contentUri(document))) {
        "Provider could not open selected document"
    }

    private fun query(uri: Uri): Cursor = requireNotNull(resolver.query(uri, arrayOf(
        DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME,
        DocumentsContract.Document.COLUMN_MIME_TYPE, DocumentsContract.Document.COLUMN_FLAGS,
    ), null, null, null)) { "Provider query returned no cursor" }

    private fun row(cursor: Cursor, owner: Uri, tree: Boolean): SkinDocument {
        val id = requireNotNull(cursor.getString(0)) { "Document identity missing" }
        val mime = cursor.getString(2)
        val flags = if (cursor.isNull(3)) null else cursor.getInt(3)
        val kind = when {
            flags == null || mime.isNullOrEmpty() -> SkinDocumentKind.UNKNOWN
            flags and DocumentsContract.Document.FLAG_VIRTUAL_DOCUMENT != 0 -> SkinDocumentKind.VIRTUAL
            mime == DocumentsContract.Document.MIME_TYPE_DIR -> SkinDocumentKind.DIRECTORY
            else -> SkinDocumentKind.FILE
        }
        val uri = if (tree) DocumentsContract.buildDocumentUriUsingTree(owner, id)
            else DocumentsContract.buildDocumentUri(requireNotNull(owner.authority), id)
        return SkinDocument(uri.toString(), cursor.getString(1), kind)
    }

    private fun contentUri(value: String): Uri = Uri.parse(value).also {
        require(it.scheme == ContentResolver.SCHEME_CONTENT && !it.authority.isNullOrEmpty()) { "Only SAF content documents are accepted" }
    }
}
