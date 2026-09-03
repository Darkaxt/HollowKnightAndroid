package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import java.io.File
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinCandidateDiscoveryTest {
    private lateinit var root: File
    private var next = 0
    private val catalog by lazy { PinnedCatalogFixture.load() }

    @Before fun setUp() {
        root = File("build/test-skin-discovery").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun appliesFiniteLayoutPrecedenceAndRawOrdering() {
        val rootCandidate = discover(listOf("Knight.png"))
        assertEquals(listOf("" to 0), roots(rootCandidate))

        val wrapper = discover(listOf("Wrapper/Knight.png", "Wrapper/ignored.txt"))
        assertEquals(listOf("Wrapper" to 1), roots(wrapper))

        val multi = discover(listOf("Outer/b/Knight.png", "Outer/A/Knight.png"))
        assertEquals(listOf("Outer/A" to 2, "Outer/b" to 2), roots(multi))

        val full = discover(
            listOf(
                "hollow_knight_Data/Managed/Mods/CustomKnight/Z/Knight.png",
                "hollow_knight_Data/Managed/Mods/CustomKnight/A/Knight.png",
                "unrelated.txt",
            ),
        )
        assertEquals(
            listOf(
                "hollow_knight_Data/Managed/Mods/CustomKnight/A" to 3,
                "hollow_knight_Data/Managed/Mods/CustomKnight/Z" to 3,
            ),
            roots(full),
        )
    }

    @Test
    fun `rejects root wrapper and full install ambiguity`() {
        assertCode(listOf("Knight.png", "Wrapper/Knight.png"), SkinImportCode.AMBIGUOUS_LAYOUT)
        assertCode(listOf("One/Knight.png", "Two/Knight.png"), SkinImportCode.AMBIGUOUS_LAYOUT)
        assertCode(
            listOf(
                "hollow_knight_Data/Managed/Mods/CustomKnight/Pack/Knight.png",
                "Other/Knight.png",
            ),
            SkinImportCode.AMBIGUOUS_LAYOUT,
        )
    }

    @Test
    fun `does not recurse or infer candidates from unknown files`() {
        assertCode(listOf("Outer/Pack/Deeper/Knight.png"), SkinImportCode.NO_CANDIDATE)
        assertCode(listOf("knightish.png", "readme.txt"), SkinImportCode.NO_CANDIDATE)
    }

    @Test
    fun `emits one archive level warning for ignored EOCD metadata`() {
        val built = RawZipFixture.build(
            listOf(RawZipFixture.Entry("Knight.png".toByteArray(), byteArrayOf(1))),
            comment = "comment".toByteArray(),
        )
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        val candidates = (SkinCandidateDiscovery(catalog).discover(authorized) as SkinResult.Ok).value
        assertEquals(listOf("IGNORED_EXTRA_METADATA"), candidates.warnings.map { it.code })
        assertEquals(listOf(""), candidates.warnings.map { it.sourceRawPathHex })
    }

    @Test
    fun `assigns each ignored entry to one longest candidate owner`() {
        val candidates = discover(
            listOf("Outer/A/Knight.png", "Outer/A/extra.txt", "Outer/B/Knight.png", "outside.txt"),
        ).candidates
        assertEquals(
            listOf("Outer/A/Knight.png", "Outer/A/extra.txt", "outside.txt"),
            candidates[0].entries.filter { !it.directory }.map { it.rawName.toString(Charsets.UTF_8) },
        )
        assertEquals(
            listOf("Outer/B/Knight.png"),
            candidates[1].entries.filter { !it.directory }.map { it.rawName.toString(Charsets.UTF_8) },
        )
    }

    private fun discover(names: List<String>): dev.silksong.launcher.skins.contracts.CandidateSet {
        val built = RawZipFixture.build(names.map { RawZipFixture.Entry(it.toByteArray(), byteArrayOf(1)) })
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        val result = SkinCandidateDiscovery(catalog).discover(authorized)
        assertTrue("Expected candidates, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun roots(set: dev.silksong.launcher.skins.contracts.CandidateSet) =
        set.candidates.map { it.rawPrefix.toString(Charsets.UTF_8) to it.layoutCode }

    private fun assertCode(names: List<String>, expected: SkinImportCode) {
        val built = RawZipFixture.build(names.map { RawZipFixture.Entry(it.toByteArray(), byteArrayOf(1)) })
        val file = File(root, "archive-${next++}.zip").apply { writeBytes(built.bytes) }
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        val authorized = (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
        val result = SkinCandidateDiscovery(catalog).discover(authorized)
        assertEquals(expected, (result as SkinResult.Error).code)
    }
}
