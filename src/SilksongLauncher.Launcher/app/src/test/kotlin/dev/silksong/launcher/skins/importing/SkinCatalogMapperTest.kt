package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.catalog.SkinCatalogPaths
import dev.silksong.launcher.skins.catalog.SkinCatalogProfiles
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import java.io.ByteArrayInputStream
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
    fun `safe catalog suffix matching accepts case but leaves actual near misses as warnings`() {
        val (caseArchive, caseCandidate) = candidate(listOf("Pack/Knight.png", "Pack/charm_1.png"))
        val caseMapping = (SkinCatalogMapper(catalog).map(caseCandidate, caseArchive) as SkinResult.Ok).value
        assertEquals(setOf("Knight.png", "Charms/Charm_1.png"), caseMapping.textures.keys)
        assertEquals(listOf("CATALOG_SUFFIX"), caseMapping.aliases.map { it.rule })

        for (nearMiss in listOf("Hud.pngx", "Inventory/Godfinder.png")) {
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
    fun `matches Hollow Knight through arbitrary wrappers using the shortest unique catalog suffix`() {
        val (authorized, candidate) = candidate(
            listOf(
                "Download/Nexus/Pack/Knight.png",
                "Download/Nexus/Pack/Inventory/Geo.png",
                "Download/Nexus/Pack/Geo.png",
            ),
        )
        val result = SkinCatalogMapper(catalog).map(candidate, authorized)

        assertTrue(result is SkinResult.Ok)
        val mapping = (result as SkinResult.Ok).value
        assertEquals(setOf("Knight.png", "Inventory/Geo.png", "Geo.png"), mapping.textures.keys)
        assertTrue(mapping.aliases.isEmpty())
    }

    @Test
    fun `matches Silksong collection and filename with case and optional Data suffix normalization`() {
        val silksong = silksongCatalog()
        val hornet = "Assets/Collections/Hornet Cln Data/atlas0.png"
        val hud = "Assets/Collections/HUD Cln Data/atlas0.png"
        val (authorized, candidate) = candidate(
            listOf(
                "HornetKnight/Hornet Cln/ATLAS0.PNG",
                "HornetKnight/HUD Cln Data/atlas0.png",
            ),
            silksong,
        )
        val result = SkinCatalogMapper(silksong).map(candidate, authorized)

        assertTrue(result is SkinResult.Ok)
        val mapping = (result as SkinResult.Ok).value
        assertEquals(setOf(hornet, hud), mapping.textures.keys)
        assertEquals(listOf("CATALOG_SUFFIX", "CATALOG_SUFFIX"), mapping.aliases.map { it.rule })
    }

    @Test
    fun `bare repeated filename cannot identify a Silksong target`() {
        val result = candidateResult(listOf("Pack/atlas0.png"), silksongCatalog())
        assertEquals(SkinImportCode.NO_CANDIDATE, (result as SkinResult.Error).code)
    }

    @Test
    fun `catalog suffix matching remains profile isolated`() {
        val silksongInHollowKnight = candidateResult(listOf("Pack/Hornet Cln/atlas0.png"), catalog)
        val hollowKnightInSilksong = candidateResult(listOf("Pack/Knight.png"), silksongCatalog())

        assertEquals(SkinImportCode.NO_CANDIDATE, (silksongInHollowKnight as SkinResult.Error).code)
        assertEquals(SkinImportCode.NO_CANDIDATE, (hollowKnightInSilksong as SkinResult.Error).code)
    }

    @Test
    fun `normalized Silksong aliases that resolve to one target collide instead of guessing`() {
        val silksong = silksongCatalog()
        val (authorized, candidate) = candidate(
            listOf(
                "Pack/Hornet Cln/atlas0.png",
                "Pack/Hornet Cln Data/atlas0.png",
            ),
            silksong,
        )

        val result = SkinCatalogMapper(silksong).map(candidate, authorized)
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

    private fun candidateResult(names: List<String>, authority: CatalogPathSet): SkinResult<dev.silksong.launcher.skins.contracts.CandidateSet> {
        val built = RawZipFixture.build(names.map { RawZipFixture.Entry(it.toByteArray(), byteArrayOf(1)) })
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        return SkinCandidateDiscovery(authority).discover(authorized)
    }

    private fun candidate(
        names: List<String>,
        authority: CatalogPathSet = catalog,
    ): Pair<AuthorizedZip, dev.silksong.launcher.skins.contracts.SkinCandidate> {
        val built = RawZipFixture.build(names.map { RawZipFixture.Entry(it.toByteArray(), byteArrayOf(1)) })
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        val candidates = (SkinCandidateDiscovery(authority).discover(authorized) as SkinResult.Ok).value
        return authorized to candidates.candidates.single()
    }

    private fun silksongCatalog(): CatalogPathSet {
        val profile = SkinCatalogProfiles.Silksong
        val bytes = (profile.paths.joinToString("\n") + "\n").toByteArray(Charsets.UTF_8)
        return when (val result = SkinCatalogPaths.load(profile, ByteArrayInputStream(bytes))) {
            is SkinResult.Ok -> result.value
            is SkinResult.Error -> error(result.detail)
        }
    }
}
