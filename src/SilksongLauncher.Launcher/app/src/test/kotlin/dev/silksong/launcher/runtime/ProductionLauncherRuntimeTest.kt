package dev.silksong.launcher.runtime

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.PlayerImage
import dev.silksong.launcher.UnityDex
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.build.GenerationMetadata
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.build.UnityToolchainRegistry
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
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class ProductionLauncherRuntimeTest {
    private lateinit var root: File
    private lateinit var request: RuntimeRequest
    private val runtime = ProductionLauncherRuntime()

    @Before
    fun setUp() {
        root = File("build/test-production-runtime").absoluteFile
        root.deleteRecursively()
        val files = File(root, "files").apply { mkdirs() }
        val external = File(root, "external").apply { mkdirs() }
        val context = ApplicationProvider.getApplicationContext<Context>()
        val profile = GameProfiles.require("silksong")
        request = RuntimeRequest(context, profile, ProfileBuildPaths(files, external, profile))
    }

    @After
    fun tearDown() {
        root.deleteRecursively()
    }

    @Test
    fun `legacy package is never launch eligible without a verified current generation`() {
        request.paths.packageDir.mkdirs()
        File(request.paths.packageDir, ".built").writeText("signature")
        assertFalse(runtime.inspect(request).ready)

        val native = File(request.paths.packageDir, "lib/arm64/libil2cpp.so")
        requireNotNull(native.parentFile).mkdirs()
        native.writeText("native")
        assertFalse(runtime.inspect(request).ready)

        File(request.paths.packageDir, "data.apk").writeText("image")

        val state = runtime.inspect(request)
        assertFalse(state.ready)
        assertEquals(RuntimeCondition.NEEDS_REPAIR, state.condition)
    }

    @Test
    fun `legacy classic runtime remains ineligible even with a main OBB`() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val profile = GameProfiles.require("hollow-knight")
        val paths = ProfileBuildPaths(
            File(root, "hk-files").apply { mkdirs() },
            File(root, "hk-external").apply { mkdirs() },
            profile,
        )
        val classic = RuntimeRequest(context, profile, paths)
        val packageDir = paths.packageDir.apply { mkdirs() }
        File(packageDir, ".built").writeText("signature")
        File(packageDir, "lib/arm64/libil2cpp.so").apply {
            parentFile.mkdirs()
            writeText("native")
        }
        File(packageDir, "data.apk").writeText("base")

        assertFalse(runtime.inspect(classic).ready)

        File(packageDir, PlayerImage.mainObbName(context)).writeText("expansion")

        assertFalse(runtime.inspect(classic).ready)
    }

    @Test
    fun `game intent carries only registered profile identity`() {
        val intent = runtime.gameIntent(request)

        assertEquals(request.context.packageName, intent.component?.packageName)
        assertEquals(ProductionLauncherRuntime.GAME_ACTIVITY_CLASS, intent.component?.className)
        assertEquals(
            "silksong",
            intent.getStringExtra(ProductionLauncherRuntime.PROFILE_ID_EXTRA),
        )
        assertEquals(1, intent.extras?.keySet()?.size)
    }

    @Test
    fun `inspect resolves the atomically selected generation`() {
        val publisher = GenerationPublisher(request.paths.profilePaths)
        val staged = publisher.begin("job-1", "gen-1")
        val packageDir = File(staged, "pkg")
        File(packageDir, ".built").apply { parentFile.mkdirs(); writeText("signature") }
        File(packageDir, "lib/arm64/libil2cpp.so").apply {
            parentFile.mkdirs()
            writeText("native")
        }
        val image = File(packageDir, "data.apk")
        ZipOutputStream(image.outputStream()).use { output ->
            output.putNextEntry(ZipEntry("assets/bin/Data/settings.xml"))
            output.write("image".toByteArray())
            output.closeEntry()
        }
        publisher.finalizeGeneration(
            "job-1",
            "gen-1",
            GenerationMetadata(
                sourceManifestSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                toolchainId = UnityToolchainRegistry.resolve(request.profile).contentHash,
                patchManifestSha256 = ProductionBuildSignature.computeSha256(request.context),
            ),
        )
        publisher.publish("job-1", "gen-1")
        stagePlayerDex()

        val state = runtime.inspect(request)

        assertTrue(state.ready)
        assertEquals("gen-1", state.generationId)
    }

    @Test
    fun `published generation with missing player dex needs repair`() {
        val publisher = GenerationPublisher(request.paths.profilePaths)
        val staged = publisher.begin("job-no-dex", "gen-no-dex")
        val packageDir = File(staged, "pkg")
        File(packageDir, ".built").apply { parentFile.mkdirs(); writeText("signature") }
        File(packageDir, "lib/arm64/libil2cpp.so").apply {
            parentFile.mkdirs()
            writeText("native")
        }
        ZipOutputStream(File(packageDir, "data.apk").outputStream()).use { output ->
            output.putNextEntry(ZipEntry("assets/bin/Data/settings.xml"))
            output.write("image".toByteArray())
            output.closeEntry()
        }
        publisher.finalizeGeneration(
            "job-no-dex",
            "gen-no-dex",
            GenerationMetadata(
                sourceManifestSha256 = "a".repeat(64),
                toolchainId = UnityToolchainRegistry.resolve(request.profile).contentHash,
                patchManifestSha256 = ProductionBuildSignature.computeSha256(request.context),
            ),
        )
        publisher.publish("job-no-dex", "gen-no-dex")

        val state = runtime.inspect(request)

        assertFalse(state.ready)
        assertEquals(RuntimeCondition.NEEDS_REPAIR, state.condition)
        assertTrue(state.detail.contains("player dex"))
    }

    @Test
    fun `invalid current pointer never falls back to a legacy package`() {
        File(request.paths.packageDir, ".built").apply { parentFile.mkdirs(); writeText("legacy") }
        File(request.paths.packageDir, "lib/arm64/libil2cpp.so").apply {
            parentFile.mkdirs()
            writeText("native")
        }
        File(request.paths.packageDir, "data.apk").writeText("legacy image")
        request.paths.profilePaths.currentPointer.apply {
            parentFile.mkdirs()
            writeText("missing-generation")
        }

        val state = runtime.inspect(request)

        assertFalse(state.ready)
        assertTrue(state.detail.contains("invalid"))
    }

    @Test
    fun `production runtime has no synthetic provisioning path`() {
        assertThrows(UnsupportedOperationException::class.java) {
            kotlinx.coroutines.runBlocking {
                runtime.provision(ProvisionRequest(request, ProvisionSource.Synthetic)) { }
            }
        }
    }

    private fun stagePlayerDex() {
        val descriptor = UnityToolchainRegistry.resolve(request.profile)
        val unityRoot = UnityToolchainRegistry.rootFor(request.paths.filesDir, descriptor)
        val source = File(
            unityRoot,
            "android/PlaybackEngines/AndroidPlayer/Variations/il2cpp/Release/Classes/classes.jar",
        ).apply {
            parentFile.mkdirs()
            writeText("player")
        }
        File(UnityDex.outputDir(request.paths.filesDir, source), "classes.jar").apply {
            parentFile.mkdirs()
            writeText("dex")
        }
    }
}
