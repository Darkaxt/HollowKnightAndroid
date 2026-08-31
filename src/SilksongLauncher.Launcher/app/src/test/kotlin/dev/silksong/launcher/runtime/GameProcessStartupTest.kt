package dev.silksong.launcher.runtime

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.UnityDex
import dev.silksong.launcher.build.GenerationMetadata
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.build.UnityToolchainRegistry
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfileBuildPaths
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class GameProcessStartupTest {
    private lateinit var context: Context
    private lateinit var root: File

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        root = File("build/test-game-process-startup").absoluteFile
        root.deleteRecursively()
        GameProcessStartup.resetForTests()
    }

    @After
    fun tearDown() {
        GameProcessStartup.resetForTests()
        root.deleteRecursively()
    }

    @Test
    fun `only the unsuffixed application process initializes Unity`() {
        assertTrue(ProcessRole.isGameProcess("io.github.darkaxt.dualsouls", "io.github.darkaxt.dualsouls"))
        assertTrue(!ProcessRole.isGameProcess("io.github.darkaxt.dualsouls", "io.github.darkaxt.dualsouls:launcher"))
        assertTrue(!ProcessRole.isGameProcess("io.github.darkaxt.dualsouls", "io.github.darkaxt.dualsouls:builder"))
    }

    @Test
    fun `startup requires a verified current generation`() {
        val profile = GameProfiles.require("silksong")
        val paths = paths(profile.id)

        assertThrows(IllegalStateException::class.java) {
            GameProcessStartup.resolve(context, profile, paths)
        }
    }

    @Test
    fun `startup snapshot binds exact profile generation toolchain and package`() {
        val profile = GameProfiles.require("silksong")
        val paths = paths(profile.id)
        publish(paths, "gen-ss", UnityToolchainRegistry.resolve(profile).contentHash)

        val snapshot = GameProcessStartup.resolve(context, profile, paths)

        assertEquals("silksong", snapshot.profileId)
        assertEquals("gen-ss", snapshot.generationId)
        assertEquals(UnityToolchainRegistry.resolve(profile).contentHash, snapshot.toolchainId)
        assertEquals(
            File(paths.profilePaths.generations, "gen-ss/pkg").canonicalPath,
            snapshot.packageDir,
        )
        assertEquals(File(snapshot.packageDir, "lib/arm64").canonicalPath, snapshot.nativeLibraryDir)
        assertEquals(File(snapshot.packageDir, "data.apk").canonicalPath, snapshot.dataArchive)
        assertEquals(
            UnityToolchainRegistry.rootFor(paths.filesDir, UnityToolchainRegistry.resolve(profile)).canonicalPath,
            snapshot.unityRoot,
        )
        assertTrue(File(snapshot.playerDexJar).isFile)
    }

    @Test
    fun `startup rejects a generation whose exact Unity player dex is absent`() {
        val profile = GameProfiles.require("silksong")
        val paths = paths(profile.id)
        publish(
            paths,
            "gen-no-dex",
            UnityToolchainRegistry.resolve(profile).contentHash,
            stagePlayerDex = false,
        )

        val error = assertThrows(IllegalStateException::class.java) {
            GameProcessStartup.resolve(context, profile, paths)
        }

        assertTrue(error.message.orEmpty().contains("player dex"))
    }

    @Test
    fun `generation from another Unity toolchain fails closed`() {
        val profile = GameProfiles.require("silksong")
        val paths = paths(profile.id)
        publish(paths, "gen-wrong", "wrong-toolchain")

        assertThrows(IllegalStateException::class.java) {
            GameProcessStartup.resolve(context, profile, paths)
        }
    }

    @Test
    fun `generation from a previous patch manifest fails closed`() {
        val profile = GameProfiles.require("silksong")
        val paths = paths(profile.id)
        publish(
            paths,
            "gen-old-patches",
            UnityToolchainRegistry.resolve(profile).contentHash,
            patchManifestSha256 = "0".repeat(64),
        )

        val error = assertThrows(IllegalStateException::class.java) {
            GameProcessStartup.resolve(context, profile, paths)
        }

        assertTrue(error.message.orEmpty().contains("patch manifest"))
    }

    @Test
    fun `intent mismatch is rejected without replacing immutable startup snapshot`() {
        val profile = GameProfiles.require("hollow-knight")
        val paths = paths(profile.id)
        publish(paths, "gen-hk", UnityToolchainRegistry.resolve(profile).contentHash)
        val snapshot = GameProcessStartup.resolve(context, profile, paths)
        GameProcessStartup.installForTests(snapshot)

        GameProcessStartup.requireProfile("hollow-knight")
        assertThrows(IllegalStateException::class.java) {
            GameProcessStartup.requireProfile("silksong")
        }
        assertEquals("hollow-knight", GameProcessStartup.requireSnapshot().profileId)
    }

    private fun paths(profileId: String): ProfileBuildPaths {
        val profile = GameProfiles.require(profileId)
        return ProfileBuildPaths(
            File(root, "files").apply { mkdirs() },
            File(root, "external").apply { mkdirs() },
            profile,
        )
    }

    private fun publish(
        paths: ProfileBuildPaths,
        generationId: String,
        toolchainId: String,
        stagePlayerDex: Boolean = true,
        patchManifestSha256: String = ProductionBuildSignature.computeSha256(context),
    ) {
        val publisher = GenerationPublisher(paths.profilePaths)
        val staging = publisher.begin("job-$generationId", generationId)
        val pkg = File(staging, "pkg")
        File(pkg, ".built").apply { parentFile.mkdirs(); writeText("ready") }
        File(pkg, "lib/arm64/libil2cpp.so").apply {
            parentFile.mkdirs()
            writeText("native")
        }
        writeZip(File(pkg, "data.apk"))
        if (paths.profile.id == "hollow-knight") {
            writeZip(File(pkg, PlayerImage.mainObbName(context)))
        }
        publisher.finalizeGeneration(
            "job-$generationId",
            generationId,
            GenerationMetadata(
                sourceManifestSha256 = "a".repeat(64),
                toolchainId = toolchainId,
                patchManifestSha256 = patchManifestSha256,
            ),
        )
        publisher.publish("job-$generationId", generationId)
        if (stagePlayerDex && toolchainId == UnityToolchainRegistry.resolve(paths.profile).contentHash) {
            val descriptor = UnityToolchainRegistry.resolve(paths.profile)
            val unityRoot = UnityToolchainRegistry.rootFor(paths.filesDir, descriptor)
            val source = File(
                unityRoot,
                "android/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Release/Classes/classes.jar",
            ).apply {
                parentFile.mkdirs()
                writeText("player-${paths.profile.id}")
            }
            File(UnityDex.outputDir(paths.filesDir, source), "classes.jar").apply {
                parentFile.mkdirs()
                writeText("dex-${paths.profile.id}")
            }
        }
    }

    private fun writeZip(file: File) {
        ZipOutputStream(file.outputStream()).use { output ->
            output.putNextEntry(ZipEntry("assets/bin/Data/settings.xml"))
            output.write("image".toByteArray())
            output.closeEntry()
        }
    }
}
