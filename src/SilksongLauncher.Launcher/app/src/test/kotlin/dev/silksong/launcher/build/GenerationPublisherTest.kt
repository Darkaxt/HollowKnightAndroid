package dev.silksong.launcher.build

import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfilePaths
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class GenerationPublisherTest {
    private lateinit var root: File
    private lateinit var hollowKnightPaths: ProfilePaths
    private lateinit var silksongPaths: ProfilePaths

    @Before
    fun setUp() {
        root = File("build/test-generation-publisher").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        hollowKnightPaths = ProfilePaths(root, GameProfiles.require("hollow-knight"))
        silksongPaths = ProfilePaths(root, GameProfiles.require("silksong"))
    }

    @After
    fun tearDown() {
        root.deleteRecursively()
    }

    @Test
    fun `publish replaces current only after verification`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val staged = publisher.begin("job-1", "gen-1")
        File(staged, "payload.bin").writeText("verified")
        publisher.finalizeGeneration("job-1", "gen-1", metadata())

        val installed = publisher.publish("job-1", "gen-1")

        assertEquals("gen-1", installed.id)
        assertEquals("hollow-knight", installed.profileId)
        assertEquals(SOURCE_SHA, installed.sourceManifestSha256)
        assertEquals(TOOLCHAIN_ID, installed.toolchainId)
        assertEquals(PATCH_SHA, installed.patchManifestSha256)
        assertTrue(installed.files.containsKey("payload.bin"))
        assertEquals(File(hollowKnightPaths.generations, "gen-1"), installed.root)
        assertEquals("gen-1", hollowKnightPaths.currentPointer.readText())
        assertFalse(File(hollowKnightPaths.staging, "job-1").exists())
    }

    @Test
    fun `missing manifest retains previous current generation`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val first = publisher.begin("job-1", "gen-1")
        File(first, "payload.bin").writeText("first")
        publisher.finalizeGeneration("job-1", "gen-1", metadata())
        publisher.publish("job-1", "gen-1")
        publisher.begin("job-2", "gen-2")

        assertThrows(IllegalStateException::class.java) {
            publisher.publish("job-2", "gen-2")
        }

        assertEquals("gen-1", hollowKnightPaths.currentPointer.readText())
        assertTrue(File(hollowKnightPaths.staging, "job-2").isDirectory)
        assertFalse(File(hollowKnightPaths.generations, "gen-2").exists())
    }

    @Test
    fun `existing generation is never overwritten`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val first = publisher.begin("job-1", "gen-1")
        File(first, "payload.bin").writeText("first")
        publisher.finalizeGeneration("job-1", "gen-1", metadata())
        publisher.publish("job-1", "gen-1")

        val second = publisher.begin("job-2", "gen-1")
        File(second, "payload.bin").writeText("second")
        publisher.finalizeGeneration("job-2", "gen-1", metadata())

        assertThrows(IllegalStateException::class.java) {
            publisher.publish("job-2", "gen-1")
        }
        assertEquals("first", File(hollowKnightPaths.generations, "gen-1/payload.bin").readText())
        assertEquals("gen-1", hollowKnightPaths.currentPointer.readText())
    }

    @Test
    fun `discard removes only the exact staging job`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        publisher.begin("job-1", "gen-1")
        publisher.begin("job-2", "gen-2")

        assertTrue(publisher.discard("job-1"))

        assertFalse(File(hollowKnightPaths.staging, "job-1").exists())
        assertTrue(File(hollowKnightPaths.staging, "job-2").isDirectory)
    }

    @Test
    fun `clearing one profile preserves the other profile`() {
        val hollowKnight = GenerationPublisher(hollowKnightPaths)
        val silksong = GenerationPublisher(silksongPaths)
        File(hollowKnight.begin("hk-job", "hk-gen"), "payload.bin").writeText("hk")
        File(silksong.begin("ss-job", "ss-gen"), "payload.bin").writeText("ss")
        hollowKnight.finalizeGeneration("hk-job", "hk-gen", metadata())
        silksong.finalizeGeneration("ss-job", "ss-gen", metadata())
        hollowKnight.publish("hk-job", "hk-gen")
        silksong.publish("ss-job", "ss-gen")

        hollowKnight.clearPublished()

        assertFalse(hollowKnightPaths.generations.exists())
        assertFalse(hollowKnightPaths.currentPointer.exists())
        assertEquals("ss-gen", silksongPaths.currentPointer.readText())
        assertTrue(File(silksongPaths.generations, "ss-gen/generation.json").isFile)
    }

    @Test
    fun `clearing staged jobs preserves the other profile staging`() {
        val hollowKnight = GenerationPublisher(hollowKnightPaths)
        val silksong = GenerationPublisher(silksongPaths)
        hollowKnight.begin("hk-job", "hk-gen")
        silksong.begin("ss-job", "ss-gen")

        hollowKnight.clearStaged()

        assertFalse(hollowKnightPaths.staging.exists())
        assertTrue(File(silksongPaths.staging, "ss-job/.generation-id").isFile)
    }

    @Test
    fun `identifiers cannot escape their owned roots`() {
        val publisher = GenerationPublisher(hollowKnightPaths)

        assertThrows(IllegalArgumentException::class.java) {
            publisher.begin("../job", "gen-1")
        }
        assertThrows(IllegalArgumentException::class.java) {
            publisher.begin("job-1", "../generation")
        }
    }

    @Test
    fun `payload mutation after manifest finalization retains previous generation`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val first = publisher.begin("job-1", "gen-1")
        File(first, "payload.bin").writeText("first")
        publisher.finalizeGeneration("job-1", "gen-1", metadata())
        publisher.publish("job-1", "gen-1")

        val second = publisher.begin("job-2", "gen-2")
        File(second, "payload.bin").writeText("before")
        publisher.finalizeGeneration("job-2", "gen-2", metadata())
        File(second, "payload.bin").writeText("after")

        assertThrows(IllegalStateException::class.java) {
            publisher.publish("job-2", "gen-2")
        }
        assertEquals("gen-1", hollowKnightPaths.currentPointer.readText())
        assertFalse(File(hollowKnightPaths.generations, "gen-2").exists())
    }

    @Test
    fun `corrupt zip payload is rejected before publication`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val staged = publisher.begin("job-1", "gen-1")
        File(staged, "pkg/data.apk").apply {
            parentFile.mkdirs()
            writeText("not a zip")
        }
        publisher.finalizeGeneration("job-1", "gen-1", metadata())

        assertThrows(IllegalStateException::class.java) {
            publisher.publish("job-1", "gen-1")
        }
        assertFalse(hollowKnightPaths.currentPointer.exists())
    }

    @Test
    fun `corrupt obb payload is rejected before publication`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val staged = publisher.begin("job-1", "gen-1")
        File(staged, "pkg/main.10003.io.github.darkaxt.dualsouls.obb").apply {
            parentFile.mkdirs()
            writeText("not a zip")
        }
        publisher.finalizeGeneration("job-1", "gen-1", metadata())

        assertThrows(IllegalStateException::class.java) {
            publisher.publish("job-1", "gen-1")
        }
        assertFalse(hollowKnightPaths.currentPointer.exists())
    }

    @Test
    fun `payload added after manifest finalization is rejected`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val staged = publisher.begin("job-1", "gen-1")
        File(staged, "payload.bin").writeText("listed")
        publisher.finalizeGeneration("job-1", "gen-1", metadata())
        File(staged, "extra.bin").writeText("unlisted")

        assertThrows(IllegalStateException::class.java) {
            publisher.publish("job-1", "gen-1")
        }
        assertFalse(hollowKnightPaths.currentPointer.exists())
    }

    @Test
    fun `valid zip is reopened and published`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        val staged = publisher.begin("job-1", "gen-1")
        val zip = File(staged, "pkg/data.apk").apply { parentFile.mkdirs() }
        ZipOutputStream(zip.outputStream()).use { output ->
            output.putNextEntry(ZipEntry("assets/bin/Data/settings.xml"))
            output.write("ok".toByteArray())
            output.closeEntry()
        }
        publisher.finalizeGeneration("job-1", "gen-1", metadata())

        val installed = publisher.publish("job-1", "gen-1")

        assertTrue(File(installed.root, "pkg/data.apk").isFile)
    }

    private fun metadata() = GenerationMetadata(
        sourceManifestSha256 = SOURCE_SHA,
        toolchainId = TOOLCHAIN_ID,
        patchManifestSha256 = PATCH_SHA,
    )

    private companion object {
        const val SOURCE_SHA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        const val TOOLCHAIN_ID = "unity-6000.0.61f1-test"
        const val PATCH_SHA = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
    }
}
