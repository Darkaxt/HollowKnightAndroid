package dev.silksong.launcher.skins.catalog

import androidx.test.core.app.ApplicationProvider
import android.content.Context
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import java.io.ByteArrayInputStream
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class HollowKnightCatalogPathsTest {
    @Test
    fun loadsPinnedCatalogFromPackagedAsset() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val result = HollowKnightCatalogPaths.load(context.assets)

        assertTrue(result is SkinResult.Ok)
        val catalog = (result as SkinResult.Ok).value
        assertEquals(205, catalog.paths.size)
        assertEquals("Knight.png", catalog.paths.first())
        assertEquals("AreaBackgrounds/defeatedBackground.png", catalog.paths.last())
        assertEquals("hk-custom-knight-v3.5.0-205", catalog.catalogId)
        assertTrue(catalog.exactBytes.toString(Charsets.UTF_8).lines().first() == "Knight.png")
        assertEquals("258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a", catalog.sha256)
    }

    @Test
    fun `rejects BOM CR missing LF duplicate and non png rows`() {
        val invalid = listOf(
            byteArrayOf(0xef.toByte(), 0xbb.toByte(), 0xbf.toByte()) + "Knight.png\n".toByteArray(),
            "Knight.png\r\n".toByteArray(),
            "Knight.png".toByteArray(),
            "Knight.png\nKnight.png\n".toByteArray(),
            "Knight.jpg\n".toByteArray(),
        )
        for (bytes in invalid) {
            val result = HollowKnightCatalogPaths.load(ByteArrayInputStream(bytes), enforcePinnedIdentity = false)
            assertEquals(SkinImportCode.DOCUMENT_INVALID, (result as SkinResult.Error).code)
        }
    }
}
