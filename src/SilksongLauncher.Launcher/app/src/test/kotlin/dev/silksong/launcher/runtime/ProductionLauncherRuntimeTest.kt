package dev.silksong.launcher.runtime

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfileBuildPaths
import java.io.File
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
    fun `inspect requires both built marker and native output`() {
        request.paths.packageDir.mkdirs()
        File(request.paths.packageDir, ".built").writeText("signature")
        assertFalse(runtime.inspect(request).ready)

        val native = File(request.paths.packageDir, "lib/arm64/libil2cpp.so")
        requireNotNull(native.parentFile).mkdirs()
        native.writeText("native")

        assertTrue(runtime.inspect(request).ready)
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
    fun `production runtime rejects synthetic provisioning`() {
        assertThrows(IllegalArgumentException::class.java) {
            kotlinx.coroutines.runBlocking {
                runtime.provision(ProvisionRequest(request, ProvisionSource.Synthetic)) { }
            }
        }
    }
}
