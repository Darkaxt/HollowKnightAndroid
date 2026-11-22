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
        assertEquals(2, provider.listings)
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

    @Test fun `loose PNGs become one ZIP named after their folder, readme files are skipped`() {
        val provider = Provider(listOf(
            SkinDocument("k", "Knight.png", SkinDocumentKind.FILE),
            SkinDocument("r", "readme.txt", SkinDocumentKind.FILE),
            SkinDocument("s", "Sprint.PNG", SkinDocumentKind.FILE),
        )).apply { treeName = "Crystal Knight" }
        val inputs = (SkinSafInputs(provider).folder("tree") as SkinResult.Ok).value
        assertEquals(listOf("Crystal Knight.zip"), inputs.map { it.displayName })
        assertEquals(0, provider.opens)
        val zip = java.util.zip.ZipInputStream(inputs.single().openOnce())
        val names = generateSequence { zip.nextEntry }.map { it.name to zip.readBytes().toList() }.toList()
        assertEquals(listOf("Crystal Knight/Knight.png" to listOf<Byte>(1), "Crystal Knight/Sprint.PNG" to listOf<Byte>(1)), names)
        assertEquals(2, provider.opens)
        assertThrows(IllegalStateException::class.java) { inputs.single().openOnce() }
    }

    @Test fun `a folder of skin folders is packed whole with relative paths and its archives stay separate`() {
        val provider = object : Provider(emptyList()) {
            override fun rowsFor(tree: String) = when (tree) {
                "tree" -> listOf(
                    SkinDocument("d1", "Angel Knight", SkinDocumentKind.DIRECTORY),
                    SkinDocument("d2", "Golden Knight", SkinDocumentKind.DIRECTORY),
                )
                "d1" -> listOf(
                    SkinDocument("d1/a", "Angel.zip", SkinDocumentKind.FILE),
                    SkinDocument("d1/n", "notes.txt", SkinDocumentKind.FILE),
                )
                "d2" -> listOf(
                    SkinDocument("d2/k", "Knight.png", SkinDocumentKind.FILE),
                    SkinDocument("d2/c", "Charms", SkinDocumentKind.DIRECTORY),
                )
                "d2/c" -> listOf(SkinDocument("d2/c/1", "Charm_1.png", SkinDocumentKind.FILE))
                else -> emptyList()
            }
        }.apply { treeName = "Lore picks" }
        val inputs = (SkinSafInputs(provider).folder("tree") as SkinResult.Ok).value
        assertEquals(listOf("Angel.zip", "Lore picks.zip"), inputs.map { it.displayName })
        val zip = java.util.zip.ZipInputStream(inputs.last().openOnce())
        val names = generateSequence { zip.nextEntry }.map { it.name.also { zip.readBytes() } }.toList()
        assertEquals(listOf("Lore picks/Golden Knight/Knight.png", "Lore picks/Golden Knight/Charms/Charm_1.png"), names)
    }

    @Test fun `folder walk stops at its depth bound`() {
        val provider = object : Provider(emptyList()) {
            override fun rowsFor(tree: String) =
                listOf(SkinDocument("$tree/d", "d", SkinDocumentKind.DIRECTORY), SkinDocument("$tree/p", "Knight.png", SkinDocumentKind.FILE))
        }
        val inputs = (SkinSafInputs(provider).folder("tree") as SkinResult.Ok).value
        assertEquals(7, provider.listings)
        val zip = java.util.zip.ZipInputStream(inputs.single().openOnce())
        val names = generateSequence { zip.nextEntry }.map { it.name.also { zip.readBytes() } }.toList()
        assertEquals("folder/d/d/d/d/d/d/Knight.png", names.last())
        assertEquals(7, names.size)
    }

    @Test fun `unsafe folder names fail closed`() {
        val provider = object : Provider(emptyList()) {
            override fun rowsFor(tree: String) =
                if (tree == "tree") listOf(SkinDocument("d", "..", SkinDocumentKind.DIRECTORY)) else emptyList()
        }
        assertTrue(SkinSafInputs(provider).folder("tree") is SkinResult.Error)
    }

    @Test fun `subfolder rows share the folder row budget`() {
        val provider = object : Provider(emptyList()) {
            override fun rowsFor(tree: String) =
                if (tree == "tree") listOf(SkinDocument("d", "sub", SkinDocumentKind.DIRECTORY))
                else List(4) { SkinDocument("$tree/$it", "$it.zip", SkinDocumentKind.FILE) }
        }
        assertEquals(SkinImportCode.LIMIT_EXCEEDED, (SkinSafInputs(provider, 4).folder("tree") as SkinResult.Error).code)
    }

    @Test fun `unsafe loose PNG names fail closed`() {
        val provider = Provider(listOf(SkinDocument("k", "a\u0000b.png", SkinDocumentKind.FILE)))
        assertTrue(SkinSafInputs(provider).folder("tree") is SkinResult.Error)
        assertEquals(0, provider.opens)
    }

    @Test fun `virtual or unknown rows fail closed`() {
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

    private open class Provider(val documents: List<SkinDocument>) : SkinDocumentProvider {
        var rows = 0; var opens = 0; var closes = 0; var listings = 0; var failRead = false
        var treeName: String? = "folder"
        open fun rowsFor(tree: String) = if (tree == "tree") documents else emptyList()
        override fun file(document: String) = documents.single()
        override fun name(tree: String) = treeName
        override fun children(tree: String): SkinDocumentCursor {
            listings++
            val documents = rowsFor(tree)
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
