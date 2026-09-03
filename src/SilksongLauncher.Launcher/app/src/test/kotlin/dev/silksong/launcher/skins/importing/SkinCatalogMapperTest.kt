package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import java.io.File
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinCatalogMapperTest {
    private lateinit var root: File
    private var next = 0
    private val catalog by lazy { PinnedCatalogFixture.load() }

    @Before fun setUp() {
        root = File("build/test-skin-mapper").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun mapsOnlyExactCaseFoldAndFiniteAliases() {
        val (authorized, candidate) = candidate(
            listOf(
                "Pack/Knight.png",
                "Pack/HUD.png",
                "Pack/Charm_1.png",
                "Pack/Inventory/ElegantKey.png",
                "Pack/orbicon.png",
                "Pack/Deeper/Knight.png",
            ),
        )
        val result = SkinCatalogMapper(catalog).map(candidate, authorized)
        assertTrue(result is SkinResult.Ok)
        val mapping = (result as SkinResult.Ok).value
        assertEquals(
            setOf("Knight.png", "Hud.png", "Charms/Charm_1.png", "Inventory/ElegentKey.png"),
            mapping.textures.keys,
        )
        assertEquals(listOf("ASCII_CASE_FOLD", "ROOT_CHARM", "ELEGENT_KEY"), mapping.aliases.map { it.rule })
        assertFalse(mapping.textures.containsKey("SaveHud/soulOrbIcon.png"))
        assertEquals(2, mapping.warnings.size)
    }

    @Test
    fun `mapped entries retain one ignored metadata warning`() {
        val harmless = byteArrayOf(0xfe.toByte(), 0xca.toByte(), 0x01, 0x00, 0x2a)
        val built = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry(
                    "Pack/Knight.png".toByteArray(),
                    byteArrayOf(1),
                    centralExtra = harmless,
                ),
            ),
        )
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        val candidate = (SkinCandidateDiscovery(catalog).discover(authorized) as SkinResult.Ok).value.candidates.single()
        val mapping = (SkinCatalogMapper(catalog).map(candidate, authorized) as SkinResult.Ok).value

        assertTrue(mapping.textures.containsKey("Knight.png"))
        assertEquals(listOf("IGNORED_EXTRA_METADATA"), mapping.warnings.map { it.code })
    }

    @Test
    fun `directory extra metadata is warned without becoming payload`() {
        val harmless = byteArrayOf(0xfe.toByte(), 0xca.toByte(), 0x01, 0x00, 0x2a)
        val built = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry("Pack/".toByteArray(), ByteArray(0), centralExtra = harmless),
                RawZipFixture.Entry("Pack/Knight.png".toByteArray(), byteArrayOf(1)),
            ),
        )
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        val candidate = (SkinCandidateDiscovery(catalog).discover(authorized) as SkinResult.Ok).value.candidates.single()
        val mapping = (SkinCatalogMapper(catalog).map(candidate, authorized) as SkinResult.Ok).value
        assertEquals(listOf("IGNORED_EXTRA_METADATA"), mapping.warnings.map { it.code })
    }

    @Test
    fun `maps each of the seven finite alias families and rejects near misses`() {
        val (authorized, candidate) = candidate(
            listOf(
                "Pack/Charm_1.png",
                "Pack/HUD.png",
                "Pack/DreamNail.png",
                "Pack/Voidspells.png",
                "Pack/DeathPt.png",
                "Pack/Inventory/Godfinder_0.png",
                "Pack/Inventory/ElegantKey.png",
                "Pack/orbicon.png",
            ),
        )
        val mapping = (SkinCatalogMapper(catalog).map(candidate, authorized) as SkinResult.Ok).value

        assertEquals(
            listOf(
                "ROOT_CHARM", "ASCII_CASE_FOLD", "ASCII_CASE_FOLD", "ASCII_CASE_FOLD",
                "ASCII_CASE_FOLD", "ASCII_CASE_FOLD", "ELEGENT_KEY",
            ),
            mapping.aliases.map { it.rule },
        )
        assertEquals(7, mapping.textures.size)
        assertEquals(1, mapping.warnings.size)
        assertFalse(mapping.textures.containsKey("SaveHud/soulOrbIcon.png"))
    }

    @Test
    fun `finite alias near misses remain warnings`() {
        for (nearMiss in listOf("charm_1.png", "Hud.pngx", "Inventory/Godfinder.png")) {
            val (authorized, candidate) = candidate(listOf("Pack/Knight.png", "Pack/$nearMiss"))
            val mapping = (SkinCatalogMapper(catalog).map(candidate, authorized) as SkinResult.Ok).value
            assertEquals(setOf("Knight.png"), mapping.textures.keys)
            assertEquals(listOf("Pack/$nearMiss".toByteArray().toHex()), mapping.warnings.map { it.sourceRawPathHex })
        }
    }

    @Test
    fun `folds the entire path including extension before finite aliases`() {
        val (authorized, candidate) = candidate(
            listOf("Pack/Knight.png", "Pack/HUD.PNG", "Pack/DREAMNAIL.PNG", "Pack/INVENTORY/GODFINDER_0.PNG"),
        )

        val mapping = (SkinCatalogMapper(catalog).map(candidate, authorized) as SkinResult.Ok).value

        assertEquals(setOf("Knight.png", "Hud.png", "Dreamnail.png", "Inventory/GodFinder_0.png"), mapping.textures.keys)
        assertEquals(List(3) { "ASCII_CASE_FOLD" }, mapping.aliases.map { it.rule })
    }

    @Test
    fun `exact and fold sources collide in path authority while fold always precedes finite alias`() {
        val built = RawZipFixture.build(
            listOf("Pack/Hud.png", "Pack/HUD.PNG").map { RawZipFixture.Entry(it.toByteArray(), byteArrayOf(1)) },
        )
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        assertEquals(SkinImportCode.PATH_COLLISION, (ZipPathAuthority().validate(zip) as SkinResult.Error).code)

        val (foldArchive, foldCandidate) = candidate(listOf("Pack/HUD.png"))
        val folded = (SkinCatalogMapper(catalog).map(foldCandidate, foldArchive) as SkinResult.Ok).value
        assertEquals(listOf("ASCII_CASE_FOLD"), folded.aliases.map { it.rule })
    }

    @Test
    fun rejectsTwoSourcesForOneTarget() {
        val (authorized, candidate) = candidate(
            listOf("Pack/Inventory/ElegentKey.png", "Pack/Inventory/ElegantKey.png"),
        )
        val result = SkinCatalogMapper(catalog).map(candidate, authorized)
        assertEquals(SkinImportCode.TARGET_COLLISION, (result as SkinResult.Error).code)
    }

    @Test
    fun `warning priority is deterministic and one per ignored entry`() {
        val (authorized, candidate) = candidate(
            listOf("Pack/Knight.png", "Pack/Swap/archive.zip", "Pack/Cinematics/readme.txt", "outside.json"),
        )
        val mapping = (SkinCatalogMapper(catalog).map(candidate, authorized) as SkinResult.Ok).value
        assertEquals(
            listOf("IGNORED_NESTED_ARCHIVE", "IGNORED_CINEMATICS", "IGNORED_CONFIG_OR_TEXT"),
            mapping.warnings.map { it.code },
        )
        assertEquals(3, mapping.warnings.map { it.sourceRawPathHex }.toSet().size)
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun candidate(names: List<String>): Pair<AuthorizedZip, dev.silksong.launcher.skins.contracts.SkinCandidate> {
        val built = RawZipFixture.build(names.map { RawZipFixture.Entry(it.toByteArray(), byteArrayOf(1)) })
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        val candidates = (SkinCandidateDiscovery(catalog).discover(authorized) as SkinResult.Ok).value
        return authorized to candidates.candidates.single()
    }
}
