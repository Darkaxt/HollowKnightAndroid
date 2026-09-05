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
import java.io.File
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SkinObjectBuilderTest {
    private lateinit var root: File
    private lateinit var paths: SkinPaths
    private lateinit var catalog: CatalogPathSet
    private val fs = AndroidSkinFileSystem()

    @Before fun setUp() {
        root = File("build/test-skin-object-builder").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        paths = SkinPaths(root)
        paths.quarantine.mkdirs()
        catalog = PinnedCatalogFixture.load()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun buildsExplicitDeterministicTestIdWithoutPublishing() {
        val result = SkinObjectBuilder(fs).build(prepared(), "local-explicit-test")
        assertTrue(result is SkinResult.Ok)
        val built = (result as SkinResult.Ok).value
        assertEquals("local-explicit-test", built.id)
        assertTrue(File(built.ephemeralRoot, "pack/skin.json").isFile)
        assertTrue(File(built.ephemeralRoot, "object.json").isFile)
        assertFalse(File(built.ephemeralRoot, ".complete").exists())
        assertFalse(paths.objects.exists())

        val objectDocument = CanonicalJson.parseObject(built.objectBytes)
        assertEquals(built.treeSha256, objectDocument.treeSha256)
        assertEquals(2, objectDocument.fileCount)
    }

    @Test
    fun `builds the same prepared candidate twice with byte-identical deterministic outputs`() {
        val prepared = prepared()
        val first = (SkinObjectBuilder(fs).build(prepared, "local-deterministic") as SkinResult.Ok).value
        val second = (SkinObjectBuilder(fs).build(prepared, "local-deterministic") as SkinResult.Ok).value

        assertEquals(first.treeSha256, second.treeSha256)
        assertEquals(first.manifestSha256, second.manifestSha256)
        assertEquals(first.contentSha256, second.contentSha256)
        assertTrue(first.manifestBytes.contentEquals(second.manifestBytes))
        assertTrue(first.objectBytes.contentEquals(second.objectBytes))
        assertTrue(first.importReceiptBytes.contentEquals(second.importReceiptBytes))
        val firstFiles = first.ephemeralRoot.walkTopDown().filter(File::isFile).associate {
            first.ephemeralRoot.toPath().relativize(it.toPath()).toString() to it.readBytes()
        }
        val secondFiles = second.ephemeralRoot.walkTopDown().filter(File::isFile).associate {
            second.ephemeralRoot.toPath().relativize(it.toPath()).toString() to it.readBytes()
        }
        assertEquals(firstFiles.keys, secondFiles.keys)
        firstFiles.forEach { (path, bytes) -> assertTrue(bytes.contentEquals(secondFiles.getValue(path))) }
    }

    @Test
    fun sameLengthCorruptionRejectsBeforeAnyBuildMutation() {
        val candidate = prepared()
        val payload = candidate.payloads.single().file
        val bytes = payload.readBytes()
        bytes[bytes.lastIndex] = (bytes.last().toInt() xor 1).toByte()
        payload.writeBytes(bytes)
        var mutations = 0
        val observed = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs,
            dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing by fs {
            override fun createDirectory(path: File) { mutations++; fs.createDirectory(path) }
        }
        val builder = SkinObjectBuilder(observed)
        assertTrue(builder.verifyPrepared(candidate) is SkinResult.Error)
        assertTrue(builder.build(candidate, "local-corrupt") is SkinResult.Error)
        assertEquals(0, mutations)
    }

    @Test
    fun candidateTraversalNeverUsesUnboundedListing() {
        val candidate = prepared()
        val boundedOnly = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs,
            dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing by fs {
            override fun list(path: File): List<File> = error("Unbounded candidate listing")
        }
        assertTrue(SkinObjectBuilder(boundedOnly).verifyPrepared(candidate) is SkinResult.Ok)
    }

    @Test
    fun standalonePrebuildVerifierRevalidatesIdentityWithoutBuilding() {
        val candidate = prepared()
        val builder = SkinObjectBuilder(fs)

        assertTrue(builder.verifyPrepared(candidate) is SkinResult.Ok)
        assertNoObjectStaging(candidate)

        val changed = candidate.copy(candidateKey = "f".repeat(64))
        assertEquals(SkinImportCode.DOCUMENT_INVALID, (builder.verifyPrepared(changed) as SkinResult.Error).code)
        assertTrue(builder.build(changed, "local-rechecked") is SkinResult.Error)
        assertNoObjectStaging(changed)
    }

    @Test
    fun `surfaces authoritative ephemeral cleanup failures`() {
        val prepared = prepared()
        val failing = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs,
            dev.silksong.launcher.skins.storage.SkinFileSystemBoundedListing by fs {
            override fun writeNew(path: File, bytes: ByteArray) {
                if (path.name == "object.json") throw IllegalStateException("object write failed")
                fs.writeNew(path, bytes)
            }

            override fun deleteContained(path: File, owner: File) {
                if (path.name.startsWith("object-")) throw IllegalStateException("ephemeral cleanup failed")
                fs.deleteContained(path, owner)
            }
        }

        val result = SkinObjectBuilder(failing).build(prepared, "local-cleanup-fault")

        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
    }

    @Test
    fun alwaysWritesExactUnknownAuthorAndAttribution() {
        val built = (SkinObjectBuilder(fs).build(prepared(), "local-metadata-test") as SkinResult.Ok).value
        val manifest = CanonicalJson.parseManifest(built.manifestBytes, catalog)
        assertEquals("Unknown", manifest.author)
        assertEquals("Unknown", manifest.attribution)
        assertEquals(null, manifest.license)
        assertEquals(null, manifest.source)
        assertEquals(null, manifest.homepage)
        assertEquals(null, manifest.preview)
    }

    @Test
    fun `rejects fake catalog mappings and prepared identity drift`() {
        val base = prepared()
        val invalid = listOf(
            base.copy(contentSha256 = "0".repeat(64)),
            base.copy(rawPrefix = "Other".toByteArray()),
            base.copy(mappings = mapOf("Texture_0.png" to base.mappings.values.single())),
            base.copy(mappings = mapOf("Knight.png" to "b".repeat(52))),
        )
        invalid.forEachIndexed { index, candidate ->
            assertTrue(SkinObjectBuilder(fs).build(candidate, "local-invalid-$index") is SkinResult.Error)
            assertNoObjectStaging(candidate)
        }
    }

    @Test
    fun `rejects digest filename drift and undeclared prepared nodes`() {
        val wrongNameBase = prepared()
        val wrongPayload = wrongNameBase.payloads.single().copy(relativePath = "assets/${"b".repeat(52)}")
        val wrongName = wrongNameBase.copy(
            contentSha256 = SkinIdentity.contentSha256(listOf(wrongPayload)),
            payloads = listOf(wrongPayload),
            mappings = mapOf("Knight.png" to "b".repeat(52)),
        )
        assertTrue(SkinObjectBuilder(fs).build(wrongName, "local-wrong-name") is SkinResult.Error)
        assertNoObjectStaging(wrongName)

        val undeclared = prepared()
        File(undeclared.stagingRoot, "undeclared.bin").writeText("extra")
        assertTrue(SkinObjectBuilder(fs).build(undeclared, "local-undeclared") is SkinResult.Error)
        assertNoObjectStaging(undeclared)
    }

    @Test
    fun `cleans ephemeral object staging when payload changes during build`() {
        val prepared = prepared()
        prepared.payloads.single().file.appendBytes(byteArrayOf(1))
        assertTrue(SkinObjectBuilder(fs).build(prepared, "local-payload-change") is SkinResult.Error)
        assertNoObjectStaging(prepared)
    }

    @Test
    fun `discard removes only owned ephemeral build`() {
        val built = (SkinObjectBuilder(fs).build(prepared(), "local-discard-test") as SkinResult.Ok).value
        val unrelated = File(paths.staging, "unrelated").apply { mkdirs() }
        assertTrue(SkinObjectBuilder(fs).discard(built) is SkinResult.Ok)
        assertFalse(built.ephemeralRoot.exists())
        assertTrue(unrelated.exists())
    }

    private fun assertNoObjectStaging(prepared: dev.silksong.launcher.skins.contracts.PreparedSkinCandidate) {
        assertFalse(
            prepared.stagingRoot.parentFile?.listFiles().orEmpty().any {
                it.name.startsWith("object-") && it.exists()
            },
        )
    }

    private fun prepared(): dev.silksong.launcher.skins.contracts.PreparedSkinCandidate {
        val built = RawZipFixture.one(name = "Pack/Knight.png", data = TinyPngFixture.rgba())
        val stage = File(paths.quarantine, "quarantine-${System.nanoTime()}").apply { mkdirs() }
        val archive = File(stage, "archive").apply { writeBytes(built.bytes) }
        val quarantined = QuarantinedArchive(archive, SkinIdentity.sha256(archive), archive.length(), "pack.zip")
        val decoder = PngDecoder { file, info ->
            SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong() * info.height))
        }
        val result = SkinNormalizer(catalog, decoder, fs).prepare(quarantined)
        assertTrue("Expected prepared candidate, got $result", result is SkinResult.Ok)
        val results = (result as SkinResult.Ok).value
        return (results.single() as CandidatePreparationResult.Ready).candidate
    }
}
