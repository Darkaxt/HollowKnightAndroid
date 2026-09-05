package dev.silksong.launcher.skins.ui

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.provider.DocumentsContract
import androidx.test.core.app.ApplicationProvider
import android.content.Context
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.shadows.ShadowContentResolver
import dev.silksong.launcher.skins.contracts.SkinResult

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28])
class AndroidSkinDocumentProviderTest {
    @Test fun `tree query is immediate bounded and closes cursor including discarded rows`() {
        val source = Provider(1025)
        ShadowContentResolver.registerProviderInternal("skins.test", source)
        val resolver = ApplicationProvider.getApplicationContext<Context>().contentResolver
        val result = SkinSafInputs(AndroidSkinDocumentProvider(resolver)).folder("content://skins.test/tree/root")
        assertTrue(result is SkinResult.Error)
        assertEquals(1, source.queries)
        assertEquals("/tree/root/document/root/children", source.uri!!.path)
        assertTrue(source.cursor!!.isClosed)
    }

    @Test fun `selected file cursor is closed and accepts arbitrary filename hint`() {
        val source = Provider(1, "application/octet-stream")
        ShadowContentResolver.registerProviderInternal("skins.test", source)
        val resolver = ApplicationProvider.getApplicationContext<Context>().contentResolver
        val result = SkinSafInputs(AndroidSkinDocumentProvider(resolver)).file("content://skins.test/document/id0")
        assertTrue(result is SkinResult.Ok)
        assertEquals("anything.txt", (result as SkinResult.Ok).value.displayName)
        assertTrue(source.cursor!!.isClosed)
    }

    @Test fun `non content URI never reaches provider`() {
        val resolver = ApplicationProvider.getApplicationContext<Context>().contentResolver
        assertTrue(SkinSafInputs(AndroidSkinDocumentProvider(resolver)).file("https://example.invalid/skin") is SkinResult.Error)
    }

    private class Provider(val count: Int, val mime: String = DocumentsContract.Document.MIME_TYPE_DIR) : ContentProvider() {
        var queries = 0; var cursor: MatrixCursor? = null; var uri: Uri? = null
        override fun onCreate() = true
        override fun query(uri: Uri, projection: Array<out String>?, selection: String?, selectionArgs: Array<out String>?, sortOrder: String?): Cursor {
            queries++; this.uri = uri
            return MatrixCursor(projection).also { c ->
                repeat(count) { index -> c.addRow(arrayOf("id$index", "anything.txt", mime, 0)) }
                cursor = c
            }
        }
        override fun getType(uri: Uri) = mime
        override fun insert(uri: Uri, values: ContentValues?): Uri? = error("unused")
        override fun update(uri: Uri, values: ContentValues?, selection: String?, selectionArgs: Array<out String>?) = error("unused")
        override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?) = error("unused")
    }
}
