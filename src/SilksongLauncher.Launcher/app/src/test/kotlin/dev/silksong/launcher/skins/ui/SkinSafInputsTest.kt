package dev.silksong.launcher.skins.ui

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream

class SkinSafInputsTest {
    @Test fun `folder charges every row before classification and closes at cap plus one`() {
        val provider = Provider(List(5) { SkinDocument("$it", "ignored", SkinDocumentKind.DIRECTORY) })
        val result = SkinSafInputs(provider, 4).folder("tree")
        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (result as SkinResult.Error).code)
        assertEquals(5, provider.rows)
        assertEquals(1, provider.closes)
        assertEquals(0, provider.opens)
    }

    @Test fun `immediate files are ordered by opaque ID not filtered by extension or MIME`() {
        val provider = Provider(listOf(
            SkinDocument("b", "not-a-zip.txt", SkinDocumentKind.FILE),
            SkinDocument("directory", "nested", SkinDocumentKind.DIRECTORY),
            SkinDocument("a", "archive.rar", SkinDocumentKind.FILE),
        ))
        val inputs = (SkinSafInputs(provider).folder("tree") as SkinResult.Ok).value
        assertEquals(listOf("archive.rar", "not-a-zip.txt"), inputs.map { it.displayName })
        assertEquals(1, provider.listings)
        assertEquals(0, provider.opens)
        inputs.forEach { it.openOnce().close() }
        assertEquals(2, provider.opens)
        assertThrows(IllegalStateException::class.java) { inputs.first().openOnce() }
        assertEquals(2, provider.opens)
    }

    @Test fun `duplicate document IDs fail closed before opening any input`() {
        val provider = Provider(List(2) { SkinDocument("same", "x", SkinDocumentKind.FILE) })
        assertTrue(SkinSafInputs(provider).folder("tree") is SkinResult.Error)
        assertEquals(0, provider.opens)
        assertEquals(1, provider.closes)
    }

    @Test fun `virtual or unknown rows fail closed and folder is never recursively listed`() {
        for (kind in listOf(SkinDocumentKind.VIRTUAL, SkinDocumentKind.UNKNOWN)) {
            val provider = Provider(listOf(SkinDocument("id", "x", kind)))
            assertTrue(SkinSafInputs(provider).folder("tree") is SkinResult.Error)
            assertEquals(1, provider.listings)
            assertEquals(0, provider.opens)
        }
    }

    @Test fun `selected file is not copied or opened while obtaining an input`() {
        val provider = Provider(listOf(SkinDocument("id", "arbitrary.bin", SkinDocumentKind.FILE)))
        val input = (SkinSafInputs(provider).file("id") as SkinResult.Ok).value
        assertEquals(0, provider.opens)
        input.openOnce().close()
        assertThrows(IllegalStateException::class.java) { input.openOnce() }
        assertEquals(1, provider.opens)
    }

    @Test fun `provider failure is an honest result and cursor is closed`() {
        val provider = Provider(emptyList()).apply { failRead = true }
        val result = SkinSafInputs(provider).folder("tree")
        assertTrue(result is SkinResult.Error)
        assertEquals(1, provider.closes)
    }

    private class Provider(val documents: List<SkinDocument>) : SkinDocumentProvider {
        var rows = 0; var opens = 0; var closes = 0; var listings = 0; var failRead = false
        override fun file(document: String) = documents.single()
        override fun children(tree: String): SkinDocumentCursor {
            listings++
            var index = 0
            return object : SkinDocumentCursor {
                override fun next(): SkinDocument? {
                    if (failRead) error("provider disconnected")
                    if (index == documents.size) return null
                    rows++
                    return documents[index++]
                }
                override fun close() { closes++ }
            }
        }
        override fun open(document: String) = ByteArrayInputStream(byteArrayOf(1)).also { opens++ }
    }
}
