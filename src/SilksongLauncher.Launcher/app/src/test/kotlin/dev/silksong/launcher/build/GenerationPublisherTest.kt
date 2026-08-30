package dev.silksong.launcher.build

import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfilePaths
import java.io.File
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
        writeManifest(staged, "hollow-knight", "gen-1")

        val installed = publisher.publish("job-1", "gen-1")

        assertEquals("gen-1", installed.id)
        assertEquals(File(hollowKnightPaths.generations, "gen-1"), installed.root)
        assertEquals("gen-1", hollowKnightPaths.currentPointer.readText())
        assertFalse(File(hollowKnightPaths.staging, "job-1").exists())
    }

    @Test
    fun `missing manifest retains previous current generation`() {
        val publisher = GenerationPublisher(hollowKnightPaths)
        writeManifest(publisher.begin("job-1", "gen-1"), "hollow-knight", "gen-1")
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
        writeManifest(first, "hollow-knight", "gen-1")
        File(first, "payload.bin").writeText("first")
        publisher.publish("job-1", "gen-1")

        val second = publisher.begin("job-2", "gen-1")
        writeManifest(second, "hollow-knight", "gen-1")
        File(second, "payload.bin").writeText("second")

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
        writeManifest(hollowKnight.begin("hk-job", "hk-gen"), "hollow-knight", "hk-gen")
        writeManifest(silksong.begin("ss-job", "ss-gen"), "silksong", "ss-gen")
        hollowKnight.publish("hk-job", "hk-gen")
        silksong.publish("ss-job", "ss-gen")

        hollowKnight.clearPublished()

        assertFalse(hollowKnightPaths.generations.exists())
        assertFalse(hollowKnightPaths.currentPointer.exists())
        assertEquals("ss-gen", silksongPaths.currentPointer.readText())
        assertTrue(File(silksongPaths.generations, "ss-gen/generation.json").isFile)
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

    private fun writeManifest(root: File, profileId: String, generationId: String) {
        File(root, "generation.json").writeText(
            """{"profileId":"$profileId","generationId":"$generationId"}""",
        )
    }
}
