package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import java.io.File
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class ZipPathAuthorityTest {
    private lateinit var root: File
    private var next = 0

    @Before fun setUp() {
        root = File("build/test-zip-path-authority").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun usesRawCentralPathAuthorityAcrossFlagDomains() {
        val nonUtf8 = byteArrayOf(0x80.toByte()) + "/Knight.png".toByteArray()
        val built = RawZipFixture.build(
            listOf(RawZipFixture.Entry(nonUtf8, byteArrayOf(1), utf8 = false)),
        )
        val authorized = authorize(built)
        val components = authorized.canonicalPaths.getValue(0)
        assertArrayEquals(nonUtf8, components.joinRaw())
        assertArrayEquals(byteArrayOf(0x80.toByte()), components.first())

        val collision = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry("Pack/Knight.png".toByteArray(), byteArrayOf(1), utf8 = true),
                RawZipFixture.Entry("pack/knight.png".toByteArray(), byteArrayOf(2), utf8 = false),
            ),
        )
        assertError(collision, SkinImportCode.PATH_COLLISION)
    }

    @Test
    fun derivesImplicitDirectoriesWithoutChangingCandidateKey() {
        val withoutDirectory = authorize(
            RawZipFixture.build(
                listOf(RawZipFixture.Entry("Pack/Charms/Charm_1.png".toByteArray(), byteArrayOf(1))),
            ),
        )
        val withDirectory = authorize(
            RawZipFixture.build(
                listOf(
                    RawZipFixture.Entry("Pack/".toByteArray(), ByteArray(0)),
                    RawZipFixture.Entry("Pack/Charms/".toByteArray(), ByteArray(0)),
                    RawZipFixture.Entry("Pack/Charms/Charm_1.png".toByteArray(), byteArrayOf(1)),
                ),
            ),
        )
        val rawDomain = authorize(
            RawZipFixture.build(
                listOf(RawZipFixture.Entry("Pack/Charms/Charm_1.png".toByteArray(), byteArrayOf(1), utf8 = false)),
            ),
        )
        val catalog = PinnedCatalogFixture.load()
        val keys = listOf(withoutDirectory, withDirectory, rawDomain).map { archive ->
            val candidate = (SkinCandidateDiscovery(catalog).discover(archive) as SkinResult.Ok).value.candidates.single()
            SkinIdentity.candidateKey("a".repeat(64), candidate.rawPrefix, candidate.layoutCode)
        }
        assertEquals(1, keys.toSet().size)
        assertEquals(setOf("Pack", "Pack/Charms"), implicitDirectories(withoutDirectory))
        assertEquals(setOf("Pack", "Pack/Charms"), implicitDirectories(withDirectory))
    }

    @Test
    fun `rejects traversal aliases invalid separators and normalized collisions`() {
        val badNames = listOf(
            "/Knight.png",
            "../Knight.png",
            "Pack//Knight.png",
            "Pack\\Knight.png",
            "C:/Knight.png",
            "Pack/NUL.png",
            "Pack./Knight.png",
        )
        for (name in badNames) {
            assertError(RawZipFixture.one(name = name, data = byteArrayOf(1)), SkinImportCode.PATH_REJECTED)
        }
        val normalizedCollision = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry("é/Knight.png".toByteArray(), byteArrayOf(1)),
                RawZipFixture.Entry("e\u0301/Knight.png".toByteArray(), byteArrayOf(2)),
            ),
        )
        assertError(normalizedCollision, SkinImportCode.PATH_COLLISION)
    }

    @Test
    fun `rejects collisions between implicit directories`() {
        val implicitCollision = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry("Pack/A.png".toByteArray(), byteArrayOf(1), utf8 = true),
                RawZipFixture.Entry("pack/B.png".toByteArray(), byteArrayOf(2), utf8 = false),
            ),
        )
        assertError(implicitCollision, SkinImportCode.PATH_COLLISION)

        val normalizedDirectoryCollision = RawZipFixture.build(
            listOf(
                RawZipFixture.Entry("é/A.png".toByteArray(), byteArrayOf(1)),
                RawZipFixture.Entry("é/B.png".toByteArray(), byteArrayOf(2)),
            ),
        )
        assertError(normalizedDirectoryCollision, SkinImportCode.PATH_COLLISION)
    }

    @Test
    fun `rejects Unicode path extras but preserves bounded metadata warning`() {
        val unicodePathExtra = byteArrayOf(0x75, 0x70, 0x01, 0x00, 0x00)
        assertError(
            RawZipFixture.build(
                listOf(RawZipFixture.Entry("Knight.png".toByteArray(), byteArrayOf(1), centralExtra = unicodePathExtra)),
            ),
            SkinImportCode.PATH_REJECTED,
        )

        val harmless = byteArrayOf(0xfe.toByte(), 0xca.toByte(), 0x01, 0x00, 0x2a)
        val authorized = authorize(
            RawZipFixture.build(
                listOf(RawZipFixture.Entry("Knight.png".toByteArray(), byteArrayOf(1), centralExtra = harmless)),
            ),
        )
        assertTrue(authorized.archive.entries.single().ignoredExtraMetadata)
    }

    private fun implicitDirectories(archive: AuthorizedZip): Set<String> = buildSet {
        archive.canonicalPaths.values.forEach { components ->
            for (count in 1 until components.size) add(components.take(count).joinRaw().toString(Charsets.UTF_8))
        }
        archive.archive.entries.filter { it.directory }.forEach { entry ->
            add(archive.canonicalPaths.getValue(entry.centralIndex).joinRaw().toString(Charsets.UTF_8))
        }
    }

    private fun List<ByteArray>.joinRaw(): ByteArray =
        foldIndexed(ByteArray(0)) { index, result, component ->
            result + if (index == 0) component else byteArrayOf('/'.code.toByte()) + component
        }

    private fun authorize(built: RawZipFixture.Built): AuthorizedZip {
        val file = write(built.bytes)
        val zip = (BoundedZipReader().read(file) as SkinResult.Ok).value
        return (ZipPathAuthority().validate(zip) as SkinResult.Ok).value
    }

    private fun assertError(built: RawZipFixture.Built, code: SkinImportCode) {
        val file = write(built.bytes)
        val zipResult = BoundedZipReader().read(file)
        val result = if (zipResult is SkinResult.Ok) ZipPathAuthority().validate(zipResult.value) else zipResult
        assertEquals(code, (result as SkinResult.Error).code)
    }

    private fun write(bytes: ByteArray): File = File(root, "archive-${next++}.zip").apply { writeBytes(bytes) }
}
