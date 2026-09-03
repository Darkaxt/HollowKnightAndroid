package dev.silksong.launcher.skins.importing

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.CandidatePreparationResult
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.CanonicalJson
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.storage.AndroidSkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinFileSystemSecurity
import dev.silksong.launcher.skins.storage.SkinPaths
import dev.silksong.launcher.skins.storage.openOutput
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.io.IOException
import java.io.OutputStream
import java.nio.file.Files
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinNormalizerTest {
    private lateinit var root: File
    private lateinit var paths: SkinPaths
    private lateinit var catalog: CatalogPathSet
    private var next = 0
    private val fs = AndroidSkinFileSystem()
    private val decoder = PngDecoder { file, expected ->
        when (val info = PngStructureValidator().inspect(file)) {
            is SkinResult.Ok -> {
                assertEquals(expected, info.value)
                SkinResult.Ok(DecodeResult(info.value.width, info.value.height, info.value.width.toLong() * info.value.height))
            }
            is SkinResult.Error -> info
        }
    }

    @Before fun setUp() {
        root = File("build/test-skin-normalizer").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        paths = SkinPaths(root)
        paths.quarantine.mkdirs()
        catalog = PinnedCatalogFixture.load()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun preparesBeforeIdWithoutManifestObjectOrTree() {
        val prepared = prepare(
            listOf(
                RawZipFixture.Entry("Pack/Knight.png".toByteArray(), TinyPngFixture.rgba()),
                RawZipFixture.Entry("Pack/HUD.png".toByteArray(), TinyPngFixture.rgba()),
                RawZipFixture.Entry("Pack/readme.txt".toByteArray(), "hello".toByteArray()),
            ),
        ).single() as CandidatePreparationResult.Ready

        val value = prepared.candidate
        assertTrue(value.candidateKey.matches(Regex("[0-9a-f]{64}")))
        assertEquals("Pack", value.name)
        assertEquals(1, value.payloads.size)
        assertTrue(value.payloads.single().relativePath.matches(Regex("assets/[a-z2-7]{52}")))
        assertEquals(setOf("Knight.png", "Hud.png"), value.mappings.keys)
        assertFalse(File(value.stagingRoot, "skin.json").exists())
        assertFalse(File(value.stagingRoot, "object.json").exists())
        assertFalse(File(value.stagingRoot, ".complete").exists())
        assertEquals(value.candidateKey, CanonicalJson.parseImportReceipt(value.importReceiptBytes, catalog).candidateKey)
    }

    @Test
    fun `preserves supplementary Unicode candidate names`() {
        val name = "Knight 🦾"
        val prepared = prepare(
            listOf(RawZipFixture.Entry("$name/Knight.png".toByteArray(), TinyPngFixture.rgba())),
        ).single() as CandidatePreparationResult.Ready
        assertEquals(name, prepared.candidate.name)
    }

    @Test
    fun preservesReadySiblingsWhenCandidateIsRejected() {
        val results = prepare(
            listOf(
                RawZipFixture.Entry("Outer/A/Knight.png".toByteArray(), TinyPngFixture.rgba()),
                RawZipFixture.Entry("Outer/B/Knight.png".toByteArray(), "not png".toByteArray()),
            ),
        )
        assertEquals(2, results.size)
        assertTrue(results[0] is CandidatePreparationResult.Ready)
        assertTrue(results[1] is CandidatePreparationResult.Rejected)
        assertEquals(SkinImportCode.PNG_INVALID, (results[1] as CandidatePreparationResult.Rejected).code)
    }

    @Test
    fun `archive identity change during preparation is an outer error and removes normalization staging`() {
        val built = RawZipFixture.build(
            listOf(RawZipFixture.Entry("Knight.png".toByteArray(), TinyPngFixture.rgba())),
        )
        val file = ownedArchive(built.bytes)
        val quarantine = QuarantinedArchive(file, SkinIdentity.sha256(file), file.length(), "mutable.zip")
        var changed = false
        val mutatingDecoder = PngDecoder { staged, expected ->
            val decoded = decoder.decodeAndRelease(staged, expected)
            if (!changed) {
                file.appendBytes(byteArrayOf(0))
                changed = true
            }
            decoded
        }

        val result = SkinNormalizer(catalog, mutatingDecoder, fs).prepare(quarantine)

        assertEquals(SkinImportCode.ZIP_CORRUPT, (result as SkinResult.Error).code)
        assertFalse(file.parentFile?.listFiles().orEmpty().any { it.name.startsWith("normalized-") })
    }

    @Test
    fun `archive-wide decoder errors and exceptions invalidate siblings and remove normalization staging`() {
        val entries = listOf(
            RawZipFixture.Entry("Outer/A/Knight.png".toByteArray(), TinyPngFixture.rgba()),
            RawZipFixture.Entry("Outer/B/Knight.png".toByteArray(), TinyPngFixture.rgba()),
        )
        val cases = listOf<PngDecoder>(
            PngDecoder { _, _ ->
                SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, "decoder authority unavailable")
            },
            PngDecoder { _, _ -> throw IOException("decoder filesystem failed") },
        )

        cases.forEach { failingDecoder ->
            val result = prepareResult(entries, failingDecoder, fs)

            assertTrue("Expected outer error, got $result", result is SkinResult.Error)
            assertFalse(hasNormalizationResidue())
        }
    }

    @Test
    fun `containment filesystem and identity failures are outer errors without staging residue`() {
        val entries = listOf(RawZipFixture.Entry("Knight.png".toByteArray(), TinyPngFixture.rgba()))
        val containmentFailure = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            override fun requireContained(path: File, owner: File, allowMissingLeaf: Boolean) {
                if (path.name.startsWith(".source-")) throw SecurityException("containment evidence failed")
                fs.requireContained(path, owner, allowMissingLeaf)
            }
        }
        val writeFailure = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            override fun openOutput(file: File, createNew: Boolean): OutputStream {
                if (file.name.startsWith(".source-")) throw IOException("filesystem write failed")
                return fs.openOutput(file, createNew)
            }
        }
        val identityFailure = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            private var sourceReads = 0
            override fun identity(path: File) = fs.identity(path).let { identity ->
                if (path.name.startsWith(".source-") && ++sourceReads > 1) {
                    identity.copy(fileKey = "replacement")
                } else {
                    identity
                }
            }
        }

        listOf(containmentFailure, writeFailure, identityFailure).forEach { failingFileSystem ->
            val result = prepareResult(entries, decoder, failingFileSystem)

            assertTrue("Expected outer error, got $result", result is SkinResult.Error)
            assertFalse(hasNormalizationResidue())
        }
    }

    @Test
    fun `ZIP parsing and extraction use the injected seekable capability`() {
        val entries = listOf(
            RawZipFixture.Entry("Knight.png".toByteArray(), TinyPngFixture.rgba()),
        )
        var seekableOpens = 0
        val tracking = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            override fun openSeekableNoFollow(file: File) =
                fs.openSeekableNoFollow(file).also { seekableOpens++ }
        }

        val result = prepareResult(entries, decoder, tracking)

        assertTrue("Expected prepared candidate, got $result", result is SkinResult.Ok)
        assertEquals(2, seekableOpens)
    }

    @Test
    fun `rejects an ancestor alias before archive reads`() {
        val outside = File(root, "outside-normalizer").apply { mkdirs() }
        val alias = File(paths.staging, "aliased-owner")
        if (runCatching { Files.createSymbolicLink(alias.toPath(), outside.toPath()) }.isSuccess) {
            val archive = File(alias, "archive").apply { writeBytes(RawZipFixture.one().bytes) }
            val quarantined = QuarantinedArchive(
                archive,
                SkinIdentity.sha256(archive),
                archive.length(),
                "aliased.zip",
            )

            val result = SkinNormalizer(catalog, decoder, fs).prepare(quarantined)

            assertTrue(result is SkinResult.Error)
            assertFalse(hasNormalizationResidue())
        }
    }

    @Test
    fun `archive mutation during extraction is an outer error without normalization residue`() {
        val built = RawZipFixture.one()
        val archive = ownedArchive(built.bytes)
        val quarantined = QuarantinedArchive(archive, SkinIdentity.sha256(archive), archive.length(), "racing.zip")
        var changed = false
        val changing = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            override fun openOutput(file: File, createNew: Boolean): OutputStream {
                if (file.name.startsWith(".source-") && !changed) {
                    archive.appendBytes(byteArrayOf(0))
                    changed = true
                }
                return fs.openOutput(file, createNew)
            }
        }

        val result = SkinNormalizer(catalog, decoder, changing).prepare(quarantined)

        assertEquals(SkinImportCode.ZIP_CORRUPT, (result as SkinResult.Error).code)
        assertFalse(hasNormalizationResidue())
    }

    @Test
    fun `structural ZIP failure is an outer error without staging residue`() {
        val result = SkinNormalizer(catalog, decoder, fs).prepare(quarantined("not a zip".toByteArray()))

        assertEquals(SkinImportCode.ZIP_CORRUPT, (result as SkinResult.Error).code)
        assertFalse(hasNormalizationResidue())
    }

    @Test
    fun `rejects Android decode dimensions that differ from validated structure`() {
        val mismatchingDecoder = PngDecoder { _, _ -> SkinResult.Ok(DecodeResult(2, 2, 4)) }
        val result = prepare(
            listOf(RawZipFixture.Entry("Knight.png".toByteArray(), TinyPngFixture.rgba())),
            decode = mismatchingDecoder,
        ).single()
        assertTrue(result is CandidatePreparationResult.Rejected)
        assertEquals(SkinImportCode.PNG_INVALID, (result as CandidatePreparationResult.Rejected).code)
    }

    @Test
    fun `candidate key uses only archive digest raw prefix and layout across directory and flag domains`() {
        val variants = listOf(
            listOf(RawZipFixture.Entry("Pack/Knight.png".toByteArray(), TinyPngFixture.rgba(), utf8 = true)),
            listOf(
                RawZipFixture.Entry("Pack/".toByteArray(), ByteArray(0), utf8 = true),
                RawZipFixture.Entry("Pack/Knight.png".toByteArray(), TinyPngFixture.rgba(), utf8 = true),
            ),
            listOf(RawZipFixture.Entry("Pack/Knight.png".toByteArray(), TinyPngFixture.rgba(), utf8 = false)),
        )
        for (entries in variants) {
            val ready = prepare(entries).single() as CandidatePreparationResult.Ready
            val receipt = CanonicalJson.parseImportReceipt(ready.candidate.importReceiptBytes, catalog)
            assertEquals("Pack", ready.candidate.rawPrefix.toString(Charsets.US_ASCII))
            assertEquals(1, ready.candidate.layoutCode)
            assertEquals(
                SkinIdentity.candidateKey(receipt.archiveSha256, "Pack".toByteArray(), 1),
                ready.candidate.candidateKey,
            )
        }
    }

    private fun prepare(
        entries: List<RawZipFixture.Entry>,
        archiveName: String = "test.zip",
        decode: PngDecoder = decoder,
    ): List<CandidatePreparationResult> {
        val built = RawZipFixture.build(entries)
        val file = ownedArchive(built.bytes)
        val quarantined = QuarantinedArchive(
            file = file,
            archiveSha256 = SkinIdentity.sha256(file),
            byteCount = file.length(),
            archiveName = archiveName,
        )
        val result = SkinNormalizer(catalog, decode, fs).prepare(quarantined)
        assertTrue("Expected prepared candidates, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun prepareResult(
        entries: List<RawZipFixture.Entry>,
        decode: PngDecoder,
        fileSystem: SkinFileSystem,
    ): SkinResult<List<CandidatePreparationResult>> {
        val built = RawZipFixture.build(entries)
        return SkinNormalizer(catalog, decode, fileSystem).prepare(quarantined(built.bytes))
    }

    private fun quarantined(bytes: ByteArray, archiveName: String = "test.zip"): QuarantinedArchive {
        val file = ownedArchive(bytes)
        return QuarantinedArchive(
            file = file,
            archiveSha256 = SkinIdentity.sha256(file),
            byteCount = file.length(),
            archiveName = archiveName,
        )
    }

    private fun hasNormalizationResidue(): Boolean =
        paths.quarantine.listFiles().orEmpty().flatMap { it.listFiles().orEmpty().asIterable() }
            .any { it.name.startsWith("normalized-") }

    private fun ownedArchive(bytes: ByteArray): File {
        val stage = File(paths.quarantine, "quarantine-${next++}").apply { mkdirs() }
        return File(stage, "archive").apply { writeBytes(bytes) }
    }
}
