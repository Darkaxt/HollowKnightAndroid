package dev.silksong.launcher.skins.documents

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinAlias
import dev.silksong.launcher.skins.contracts.SkinWarning
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class CanonicalSkinDocumentsTest {
    private lateinit var catalog: CatalogPathSet

    @Before fun loadCatalog() {
        catalog = PinnedCatalogFixture.load()
    }

    @Test
    fun emitsByteIdenticalCanonicalDocuments() {
        val manifest = manifest()
        val bytes = CanonicalJson.manifest(manifest)
        assertEquals(
            "{\"attribution\":\"Unknown\",\"author\":\"Unknown\",\"contentSha256\":\"${"a".repeat(64)}\",\"games\":{\"hollow-knight\":{\"assetRoot\":\"assets\",\"catalogId\":\"hk-custom-knight-v3.5.0-205\",\"gameVersion\":\"1.5.12620\",\"textures\":{\"Knight.png\":\"${"a".repeat(52)}\"}}},\"id\":\"local-test\",\"name\":\"Test Knight\",\"schemaVersion\":1}",
            bytes.toString(Charsets.UTF_8),
        )
        assertArrayEquals(bytes, CanonicalJson.manifest(CanonicalJson.parseManifest(bytes)))

        val objectDocument = SkinObjectDocument(
            treeSha256 = "b".repeat(64),
            contentSha256 = "a".repeat(64),
            manifestSha256 = "c".repeat(64),
            fileCount = 1,
            payloadBytes = 0,
            files = listOf(SkinFileDocument("skin.json", bytes.size.toLong(), "c".repeat(64))),
        )
        val objectBytes = CanonicalJson.objectDocument(objectDocument)
        assertArrayEquals(objectBytes, CanonicalJson.objectDocument(CanonicalJson.parseObject(objectBytes)))

        val receiptBytes = CanonicalJson.importReceipt(receipt())
        assertArrayEquals(receiptBytes, CanonicalJson.importReceipt(CanonicalJson.parseImportReceipt(receiptBytes)))
    }

    @Test
    fun `strict parsers reject duplicate unknown null and wrong typed fields`() {
        val malformed = listOf(
            "{\"schemaVersion\":1,\"schemaVersion\":1}",
            "{\"schemaVersion\":1,\"unknown\":1}",
            "{\"schemaVersion\":null}",
            "{\"schemaVersion\":\"1\"}",
        )
        malformed.forEach { json ->
            assertThrows(IllegalArgumentException::class.java) { CanonicalJson.parseManifest(json.toByteArray()) }
        }
    }

    @Test
    fun `catalog authority rejects fake texture and alias targets`() {
        assertThrows(IllegalArgumentException::class.java) {
            CanonicalJson.manifest(
                manifest().copy(
                    games = mapOf(
                        "hollow-knight" to manifest().games.getValue("hollow-knight").copy(
                            textures = mapOf("Texture_0.png" to "a".repeat(52)),
                        ),
                    ),
                ),
                catalog,
            )
        }
        assertThrows(IllegalArgumentException::class.java) {
            CanonicalJson.importReceipt(
                receipt().copy(
                    aliases = listOf(
                        SkinAlias(raw("Pack/texture_0.png"), "Texture_0.png", "ASCII_CASE_FOLD"),
                    ),
                ),
                catalog,
            )
        }
    }

    @Test
    fun `receipt rejects nonfinite aliases and warning ownership drift`() {
        val invalidReceipts = listOf(
            receipt().copy(
                aliases = listOf(SkinAlias(raw("Pack/Knight.png"), "Knight.png", "ASCII_CASE_FOLD")),
            ),
            receipt().copy(
                warnings = listOf(
                    SkinWarning("IGNORED_UNKNOWN", ""),
                    SkinWarning("IGNORED_UNKNOWN", raw("Pack/readme.bin")),
                ),
            ),
            receipt().copy(
                warnings = listOf(
                    SkinWarning("IGNORED_UNKNOWN", raw("Pack/readme.bin")),
                    SkinWarning("IGNORED_CONFIG_OR_TEXT", raw("Pack/readme.bin")),
                ),
            ),
        )
        invalidReceipts.forEach { invalid ->
            assertThrows(IllegalArgumentException::class.java) { CanonicalJson.importReceipt(invalid, catalog) }
        }
    }

    @Test
    fun `receipt separately bounds entry and archive warnings`() {
        val entryWarnings = (0 until 4097).map { index ->
            SkinWarning(WARNING_CODES[index % WARNING_CODES.size], index.toString(16).padStart(1024, '0'))
        }
        val archiveWarnings = (WARNING_CODES + WARNING_CODES.first()).map { SkinWarning(it, "") }
        assertThrows(IllegalArgumentException::class.java) {
            CanonicalJson.importReceipt(receipt().copy(warnings = entryWarnings), catalog)
        }
        assertThrows(IllegalArgumentException::class.java) {
            CanonicalJson.importReceipt(receipt().copy(warnings = archiveWarnings), catalog)
        }
    }

    @Test
    fun fitsMaximumReceiptWithoutTruncation() {
        val aliases = catalog.paths.map { target ->
            val source = caseVariant(target)
            SkinAlias(raw("Pack/$source"), target, "ASCII_CASE_FOLD")
        }
        val entryWarnings = (0 until 4096).map { index ->
            SkinWarning(WARNING_CODES[index % WARNING_CODES.size], index.toString(16).padStart(1024, '0'))
        }
        val archiveWarnings = WARNING_CODES.map { SkinWarning(it, "") }
        val maximum = receipt().copy(aliases = aliases, warnings = entryWarnings + archiveWarnings)
        val bytes = CanonicalJson.importReceipt(maximum, catalog)

        assertTrue(bytes.size < 8 * 1024 * 1024)
        val parsed = CanonicalJson.parseImportReceipt(bytes, catalog)
        assertEquals(catalog.paths, parsed.aliases.map { it.target })
        assertEquals(4096, parsed.warnings.count { it.sourceRawPathHex.isNotEmpty() })
        assertEquals(10, parsed.warnings.count { it.sourceRawPathHex.isEmpty() })
    }

    @Test
    fun `validates preview collision and optional display metadata`() {
        val base = manifest().copy(license = "CC BY/NC 4.0", attribution = null)
        assertNotNull(CanonicalJson.parseManifest(CanonicalJson.manifest(base, catalog), catalog))
        for (preview in listOf("ASSETS/${"a".repeat(52)}", "preview.bin")) {
            assertThrows(IllegalArgumentException::class.java) {
                CanonicalJson.manifest(base.copy(preview = preview), catalog)
            }
        }
    }

    @Test
    fun `rejects case folded document path collisions`() {
        val files = listOf(
            SkinFileDocument("assets/A", 1, "a".repeat(64)),
            SkinFileDocument("assets/a", 1, "b".repeat(64)),
            SkinFileDocument("skin.json", 1, "c".repeat(64)),
        )
        assertThrows(IllegalArgumentException::class.java) {
            CanonicalJson.objectDocument(
                SkinObjectDocument(
                    treeSha256 = "d".repeat(64),
                    contentSha256 = "e".repeat(64),
                    manifestSha256 = "c".repeat(64),
                    fileCount = files.size,
                    payloadBytes = 2,
                    files = files,
                ),
            )
        }
    }

    @Test
    fun `rejects duplicate import receipt alias ownership and framing drift`() {
        val alias = receipt().aliases.single()
        assertThrows(IllegalArgumentException::class.java) {
            CanonicalJson.importReceipt(receipt().copy(aliases = listOf(alias, alias)), catalog)
        }
        assertThrows(IllegalArgumentException::class.java) {
            CanonicalJson.importReceipt(receipt().copy(candidateKey = "0".repeat(64)), catalog)
        }
    }

    @Test
    fun `accepts supplementary Unicode and rejects unpaired surrogates`() {
        val supplementary = receipt().copy(archiveName = "Knight 🦾.zip")
        assertNotNull(CanonicalJson.parseImportReceipt(CanonicalJson.importReceipt(supplementary, catalog), catalog))
        for (archiveName in listOf("broken-\uD83E.zip", "broken-\uDDBE.zip")) {
            assertThrows(IllegalArgumentException::class.java) {
                CanonicalJson.importReceipt(receipt().copy(archiveName = archiveName), catalog)
            }
        }
    }

    @Test
    fun `archive name changes only receipt provenance`() {
        val a = CanonicalJson.importReceipt(receipt().copy(archiveName = "one.zip"), catalog)
        val b = CanonicalJson.importReceipt(receipt().copy(archiveName = "two.zip"), catalog)
        assertTrue(!a.contentEquals(b))
    }

    private fun manifest() = SkinManifestDocument(
        id = "local-test",
        name = "Test Knight",
        author = "Unknown",
        attribution = "Unknown",
        contentSha256 = "a".repeat(64),
        games = mapOf(
            "hollow-knight" to SkinGameDocument(
                gameVersion = "1.5.12620",
                catalogId = "hk-custom-knight-v3.5.0-205",
                assetRoot = "assets",
                textures = mapOf("Knight.png" to "a".repeat(52)),
            ),
        ),
    )

    private fun receipt(): SkinImportReceiptDocument {
        val archiveSha256 = "2".repeat(64)
        val prefix = "Pack".toByteArray()
        return SkinImportReceiptDocument(
            candidateKey = SkinIdentity.candidateKey(archiveSha256, prefix, 1),
            archiveSha256 = archiveSha256,
            archiveName = "archive.zip",
            candidateRawPathHex = raw("Pack"),
            layoutCode = 1,
            aliases = listOf(SkinAlias(raw("Pack/KNIGHT.png"), "Knight.png", "ASCII_CASE_FOLD")),
            warnings = listOf(SkinWarning("IGNORED_UNKNOWN", raw("Pack/readme.bin"))),
        )
    }

    private fun caseVariant(path: String): String {
        val index = path.indexOfFirst { it in 'A'..'Z' || it in 'a'..'z' }
        require(index >= 0)
        val replacement = if (path[index] in 'A'..'Z') path[index].lowercaseChar() else path[index].uppercaseChar()
        return path.replaceRange(index, index + 1, replacement.toString())
    }

    private fun raw(value: String): String = value.toByteArray(Charsets.US_ASCII)
        .joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private companion object {
        val WARNING_CODES = listOf(
            "IGNORED_NESTED_ARCHIVE",
            "IGNORED_SWAP",
            "IGNORED_CINEMATICS",
            "IGNORED_REPLACE_AUDIO",
            "IGNORED_HP_BAR",
            "IGNORED_CONFIG_OR_TEXT",
            "IGNORED_ALTERNATE",
            "IGNORED_PATH_ENCODING",
            "IGNORED_EXTRA_METADATA",
            "IGNORED_UNKNOWN",
        )
    }
}
