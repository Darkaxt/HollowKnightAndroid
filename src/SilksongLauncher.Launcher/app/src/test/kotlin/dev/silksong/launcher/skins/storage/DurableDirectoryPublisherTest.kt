package dev.silksong.launcher.skins.storage

import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.fixtures.FaultingSkinFileSystem
import java.io.File
import java.nio.file.Files
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class DurableDirectoryPublisherTest {
    private lateinit var root: File

    @Before fun setUp() {
        root = File("build/test-durable-directory-publisher").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
    }

    @After fun tearDown() { root.deleteRecursively() }

    @Test
    fun publishesOnlyAfterAncestorBarrier() {
        val fs = FaultingSkinFileSystem()
        val staging = stage("stage", "payload")
        val destination = destination("a")
        val result = DurableDirectoryPublisher(fs).publishDetailed(staging, destination, root, ::verifyPayload)

        assertTrue("$result", result is SkinResult.Ok)
        assertTrue((result as SkinResult.Ok).value.newlyCreated)
        assertTrue(File(destination, ".complete").isFile)
        val rename = fs.events.indexOfFirst { it.startsWith("rename:") }
        assertTrue(fs.events.indexOf("sync-file:payload.bin") in 0 until rename)
        assertTrue(fs.events.indexOf("sync-dir:nested") in 0 until rename)
        assertTrue(fs.events.indexOf("write-new:.complete") in 0 until rename)
        assertTrue(fs.events.indexOf("sync-file:.complete") in 0 until rename)
        assertTrue(fs.events.indexOfLast { it == "sync-dir:aa" } > rename)
        assertTrue(fs.events.indexOfLast { it == "sync-dir:${root.name}" } > rename)
    }

    @Test
    fun `fault matrix never succeeds and safely removes only a moved destination`() {
        val faults = listOf(
            "sync-dir:skins" to 1,
            "sync-dir:${root.name}" to 1,
            "sync-dir:objects" to 1,
            "sync-dir:skins" to 2,
            "sync-dir:sha256" to 1,
            "sync-dir:objects" to 2,
            "sync-dir:aa" to 1,
            "sync-dir:sha256" to 2,
            "sync-file:payload.bin" to 1,
            "sync-dir:nested" to 1,
            "sync-dir:stage" to 1,
            "write-new:.complete" to 1,
            "sync-file:.complete" to 1,
            "sync-dir:stage" to 2,
            "rename:stage->${"a".repeat(64)}" to 1,
            "sync-dir:aa" to 2,
            "sync-dir:sha256" to 3,
            "sync-dir:objects" to 3,
            "sync-dir:skins" to 3,
            "sync-dir:${root.name}" to 2,
        )
        for ((event, occurrence) in faults) {
            root.deleteRecursively()
            root.mkdirs()
            val staging = stage("stage", "payload")
            val destination = destination("a")
            val fs = FaultingSkinFileSystem().apply {
                failOnEvent = event
                failOnOccurrence = occurrence
            }

            val result = DurableDirectoryPublisher(fs).publishDetailed(staging, destination, root, ::verifyPayload)

            assertTrue("$event unexpectedly succeeded: $result", result is SkinResult.Error)
            assertFalse("$event left a moved immutable root", destination.exists())
        }
    }

    @Test
    fun `preserves moved root identity when post-rename cleanup fails`() {
        val destination = destination("9")
        val destinationParent = requireNotNull(destination.parentFile).absoluteFile.normalize()
        val delegate = AndroidSkinFileSystem()
        var moved = false
        val failing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun atomicMove(source: File, target: File) {
                delegate.atomicMove(source, target)
                moved = true
            }

            override fun syncDirectory(path: File) {
                if (moved && path.absoluteFile.normalize() == destinationParent) {
                    throw IllegalStateException("post-rename barrier failed")
                }
                delegate.syncDirectory(path)
            }

            override fun deleteContained(path: File, owner: File) {
                if (path.absoluteFile.normalize() == destination.absoluteFile.normalize()) {
                    throw IllegalStateException("moved cleanup failed")
                }
                delegate.deleteContained(path, owner)
            }
        }

        val result = DurableDirectoryPublisher(failing).publishTracked(
            stage("stage", "payload"),
            destination,
            root,
            ::verifyPayload,
        )

        assertTrue(result is DirectoryPublicationResult.Failure)
        val failed = result as DirectoryPublicationResult.Failure
        assertEquals(destination.absoluteFile.normalize(), failed.movedRoot?.root)
        assertEquals(delegate.identity(destination), failed.movedRoot?.identity)
        assertTrue(destination.exists())
    }

    @Test
    fun `verification failure after rename reports corruption and cleans moved root`() {
        val staging = stage("stage", "payload")
        val destination = destination("b")
        val result = DurableDirectoryPublisher().publishDetailed(staging, destination, root) {
            SkinResult.Error(SkinImportCode.OBJECT_CORRUPT, "rejected")
        }
        assertEquals(SkinImportCode.OBJECT_CORRUPT, (result as SkinResult.Error).code)
        assertFalse(destination.exists())
    }

    @Test
    fun revalidatesExistingImmutableDestination() {
        val first = DurableDirectoryPublisher().publishDetailed(stage("first", "payload"), destination("c"), root, ::verifyPayload)
        assertTrue(first is SkinResult.Ok)
        val destination = destination("c")
        val fs = FaultingSkinFileSystem().apply {
            failOnEvent = "sync-dir:aa"
            failOnOccurrence = 1
        }
        val secondStage = stage("second", "payload")

        val result = DurableDirectoryPublisher(fs).publishDetailed(secondStage, destination, root, ::verifyPayload)

        assertTrue(result is SkinResult.Error)
        assertTrue(destination.exists())
        assertTrue(secondStage.exists())
    }

    @Test
    fun `atomic collision reuses verified destination without claiming creation`() {
        val destination = destination("d")
        val stage = stage("stage", "payload")
        val delegate = AndroidSkinFileSystem()
        val racing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun atomicMove(source: File, target: File) {
                source.copyRecursively(target)
                throw IllegalStateException("lost compare-and-swap")
            }
        }

        val result = DurableDirectoryPublisher(racing).publishDetailed(stage, destination, root, ::verifyPayload)

        assertTrue("$result", result is SkinResult.Ok)
        assertFalse((result as SkinResult.Ok).value.newlyCreated)
        assertTrue(stage.exists())
        assertTrue(destination.exists())
    }

    @Test
    fun `destination identity change after verifier fails without deleting replacement`() {
        val destination = destination("e")
        val delegate = AndroidSkinFileSystem()
        var identityChanged = false
        val replacing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun identity(path: File): SkinNodeIdentity {
                val identity = delegate.identity(path)
                return if (identityChanged && path.absoluteFile.normalize() == destination.absoluteFile.normalize()) {
                    identity.copy(fileKey = "replacement-directory")
                } else {
                    identity
                }
            }
        }

        val result = DurableDirectoryPublisher(replacing).publishDetailed(stage("stage", "payload"), destination, root) {
            verifyPayload(it).also { identityChanged = true }
        }

        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        assertTrue(destination.exists())
    }

    @Test
    fun `reused destination identity change after final barrier fails without deleting replacement`() {
        val destination = destination("f")
        val first = DurableDirectoryPublisher().publishDetailed(stage("first", "payload"), destination, root, ::verifyPayload)
        assertTrue(first is SkinResult.Ok)
        val delegate = AndroidSkinFileSystem()
        var identityChanged = false
        val replacing = object : SkinFileSystem by delegate, SkinFileSystemSecurity by delegate {
            override fun syncDirectory(path: File) {
                delegate.syncDirectory(path)
                if (path.absoluteFile.normalize() == root.absoluteFile.normalize()) identityChanged = true
            }

            override fun identity(path: File): SkinNodeIdentity {
                val identity = delegate.identity(path)
                return if (identityChanged && path.absoluteFile.normalize() == destination.absoluteFile.normalize()) {
                    identity.copy(fileKey = "replacement-after-barrier")
                } else {
                    identity
                }
            }
        }
        val secondStage = stage("second", "payload")

        val result = DurableDirectoryPublisher(replacing).publishDetailed(secondStage, destination, root, ::verifyPayload)

        assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        assertTrue(destination.exists())
        assertTrue(secondStage.exists())
    }

    @Test
    fun `rejects staging outside profile and output never follows links`() {
        val outsideStage = File(root.parentFile, "outside-stage-${System.nanoTime()}").apply {
            mkdirs()
            File(this, "payload.bin").writeText("payload")
        }
        try {
            val result = DurableDirectoryPublisher().publishDetailed(outsideStage, destination("e"), root, ::verifyPayload)
            assertEquals(SkinImportCode.DURABILITY_UNAVAILABLE, (result as SkinResult.Error).code)
        } finally {
            outsideStage.deleteRecursively()
        }

        val outside = File(root, "outside.txt").apply { writeText("outside") }
        val link = File(root, "linked.txt")
        if (runCatching { Files.createSymbolicLink(link.toPath(), outside.toPath()) }.isSuccess) {
            assertThrows(Exception::class.java) {
                AndroidSkinFileSystem().openOutput(link, createNew = false).use { it.write(1) }
            }
            assertEquals("outside", outside.readText())
        }
    }

    private fun stage(name: String, contents: String) = File(root, name).apply {
        mkdirs()
        File(this, "nested").mkdirs()
        File(this, "nested/payload.bin").writeText(contents)
    }

    private fun destination(digestCharacter: String) =
        File(root, "skins/objects/sha256/aa/${digestCharacter.repeat(64)}")

    private fun verifyPayload(directory: File): SkinResult<Unit> =
        if (File(directory, "nested/payload.bin").readText() == "payload") SkinResult.Ok(Unit)
        else SkinResult.Error(SkinImportCode.OBJECT_CORRUPT, "payload mismatch")
}
