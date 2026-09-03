package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.contracts.CandidatePreparationResult
import dev.silksong.launcher.skins.contracts.DecodeResult
import dev.silksong.launcher.skins.contracts.PreparedSkinCandidate
import dev.silksong.launcher.skins.contracts.QuarantinedArchive
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.fixtures.PinnedCatalogFixture
import dev.silksong.launcher.skins.fixtures.RawZipFixture
import dev.silksong.launcher.skins.fixtures.TinyPngFixture
import dev.silksong.launcher.skins.importing.PngDecoder
import dev.silksong.launcher.skins.importing.SkinNormalizer
import dev.silksong.launcher.skins.importing.SkinObjectBuilder
import java.io.File
import java.nio.file.Files
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

class SkinTreeVerifierTest {
    private lateinit var root: File
    private lateinit var paths: SkinPaths
    private lateinit var catalog: CatalogPathSet
    private val fs = AndroidSkinFileSystem()

    @Before fun setUp() {
        root = File("build/test-skin-tree-verifier").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        paths = SkinPaths(root)
        paths.quarantine.mkdirs()
        catalog = PinnedCatalogFixture.load()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun `verifies exact published tree catalog and digest shard closure`() {
        val published = publish("local-valid")
        val result = verifier().verify(published.objectRoot, published.treeSha256)
        assertTrue(result is SkinResult.Ok)
        assertEquals("local-valid", (result as SkinResult.Ok).value.id)
    }

    @Test
    fun `rejects fake catalog target even when manifest remains png-like`() {
        val published = publish("local-fake-target")
        val manifest = File(published.objectRoot, "pack/skin.json")
        manifest.writeText(manifest.readText().replace("Knight.png", "Texture_0.png"))
        assertTrue(verifier().verify(published.objectRoot, published.treeSha256) is SkinResult.Error)
    }

    @Test
    fun `rejects undeclared directories and wrong digest shard`() {
        val published = publish("local-extra")
        File(published.objectRoot, "pack/empty").mkdirs()
        assertTrue(verifier().verify(published.objectRoot, published.treeSha256) is SkinResult.Error)

        root.deleteRecursively()
        root.mkdirs()
        paths = SkinPaths(root)
        paths.quarantine.mkdirs()
        val second = publish("local-shard")
        val wrongShard = File(paths.objects, "ff/${second.treeSha256}")
        wrongShard.parentFile?.mkdirs()
        Files.move(second.objectRoot.toPath(), wrongShard.toPath())
        assertTrue(verifier().verify(wrongShard, second.treeSha256) is SkinResult.Error)
    }

    @Test
    fun `rejects linked aliases without traversing them`() {
        val published = publish("local-link")
        val link = File(published.objectRoot, "pack/escape")
        val outside = File(root, "outside").apply { writeText("keep") }
        val symbolicCreated = runCatching { Files.createSymbolicLink(link.toPath(), outside.toPath()) }.isSuccess
        assumeTrue(symbolicCreated)

        assertTrue(verifier().verify(published.objectRoot, published.treeSha256) is SkinResult.Error)
        assertEquals("keep", outside.readText())
    }

    @Test
    fun `revalidates per texture bounds and stable identities`() {
        val published = publish("local-bound")
        val bounded = SkinTreeVerifier(fs, SkinLimits.V1.copy(textureBytes = 1), catalog, paths.profileRoot)
        assertTrue(bounded.verify(published.objectRoot, published.treeSha256) is SkinResult.Error)

        val changing = object : SkinFileSystem by fs, SkinFileSystemSecurity by fs {
            private var reads = 0
            override fun identity(path: File): dev.silksong.launcher.skins.contracts.SkinNodeIdentity {
                val identity = fs.identity(path)
                if (path.name == "object.json" && ++reads > 1) return identity.copy(fileKey = "changed")
                return identity
            }
        }
        assertTrue(
            SkinTreeVerifier(changing, catalog = catalog, profileAncestor = paths.profileRoot)
                .verify(published.objectRoot, published.treeSha256) is SkinResult.Error,
        )
    }

    @Test
    fun `repository fails closed outside its exact immutable path and fixed profile`() {
        val published = publish("local-outside")
        val wrongOwnedPath = File(paths.root, "wrong-object").apply { published.objectRoot.copyRecursively(this) }
        assertTrue(SkinObjectRepository(paths, fs, catalog).verify(wrongOwnedPath, published.treeSha256) is SkinResult.Error)

        val outside = File(root.parentFile, "outside-object-${System.nanoTime()}")
        published.objectRoot.copyRecursively(outside)
        try {
            assertTrue(SkinObjectRepository(paths, fs, catalog).verify(outside, published.treeSha256) is SkinResult.Error)
        } finally {
            outside.deleteRecursively()
        }
    }

    private fun verifier() = SkinTreeVerifier(fs, catalog = catalog, profileAncestor = paths.profileRoot)

    private fun publish(id: String): dev.silksong.launcher.skins.contracts.PublishedSkin {
        val built = (SkinObjectBuilder(fs).build(prepared(), id) as SkinResult.Ok).value
        val publisher = SkinObjectPublisher(
            SkinObjectRepository(paths, fs, catalog),
            SkinImportReceiptRepository(paths, fs, catalog),
            fs,
        )
        val result = publisher.publish(built)
        assertTrue("Expected publication, got $result", result is SkinResult.Ok)
        return (result as SkinResult.Ok).value
    }

    private fun prepared(): PreparedSkinCandidate {
        val bytes = RawZipFixture.one(name = "Pack/Knight.png", data = TinyPngFixture.rgba()).bytes
        val stage = File(paths.quarantine, "quarantine-${System.nanoTime()}").apply { mkdirs() }
        val archive = File(stage, "archive").apply { writeBytes(bytes) }
        val quarantined = QuarantinedArchive(archive, SkinIdentity.sha256(archive), archive.length(), "archive.zip")
        val decoder = PngDecoder { _, info ->
            SkinResult.Ok(DecodeResult(info.width, info.height, info.width.toLong() * info.height))
        }
        val results = (SkinNormalizer(catalog, decoder, fs).prepare(quarantined) as SkinResult.Ok).value
        return (results.single() as CandidatePreparationResult.Ready).candidate
    }
}
