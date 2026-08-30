package io.github.darkaxt.dualsouls.lab

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.runtime.ProvisionRequest
import dev.silksong.launcher.runtime.ProvisionSource
import dev.silksong.launcher.runtime.RuntimeRequest
import java.io.File
import kotlinx.coroutines.runBlocking
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
class LabLauncherRuntimeTest {
    private lateinit var root: File
    private lateinit var context: Context
    private val runtime = LabLauncherRuntime()

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        root = File("build/test-lab-runtime").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        context.getSharedPreferences(LabLauncherRuntime.CONTROL_PREFS, Context.MODE_PRIVATE)
            .edit().clear().commit()
        context.getSharedPreferences(LabLauncherRuntime.STATE_PREFS, Context.MODE_PRIVATE)
            .edit().clear().commit()
    }

    @After
    fun tearDown() {
        root.deleteRecursively()
    }

    @Test
    fun `synthetic provisioning publishes deterministic monotonic generations`() = runBlocking {
        val request = request("hollow-knight")

        val first = runtime.provision(ProvisionRequest(request, ProvisionSource.Synthetic)) { }
        val second = runtime.provision(ProvisionRequest(request, ProvisionSource.Synthetic)) { }

        assertEquals("lab-hk-1", first.generationId)
        assertEquals("lab-hk-2", second.generationId)
        assertEquals("lab-hk-2", runtime.inspect(request).generationId)
    }

    @Test
    fun `injected prepublish failure retains previous generation`() = runBlocking {
        val request = request("silksong")
        runtime.provision(ProvisionRequest(request, ProvisionSource.Synthetic)) { }
        context.getSharedPreferences(LabLauncherRuntime.CONTROL_PREFS, Context.MODE_PRIVATE)
            .edit().putBoolean(LabLauncherRuntime.failureKey(request.profile.id), true).commit()

        assertThrows(IllegalStateException::class.java) {
            runBlocking {
                runtime.provision(ProvisionRequest(request, ProvisionSource.Synthetic)) { }
            }
        }

        assertEquals("lab-ss-1", runtime.inspect(request).generationId)
    }

    @Test
    fun `reset clears selected profile and preserves sibling`() = runBlocking {
        val hollowKnight = request("hollow-knight")
        val silksong = request("silksong")
        runtime.provision(ProvisionRequest(hollowKnight, ProvisionSource.Synthetic)) { }
        runtime.provision(ProvisionRequest(silksong, ProvisionSource.Synthetic)) { }

        runtime.reset(hollowKnight)

        assertFalse(runtime.inspect(hollowKnight).ready)
        assertTrue(runtime.inspect(silksong).ready)
    }

    @Test
    fun `game intent carries selected profile and current generation`() = runBlocking {
        val request = request("hollow-knight")
        runtime.provision(ProvisionRequest(request, ProvisionSource.Synthetic)) { }

        val intent = runtime.gameIntent(request)

        assertEquals("hollow-knight", intent.getStringExtra(LabLauncherRuntime.PROFILE_ID_EXTRA))
        assertEquals("lab-hk-1", intent.getStringExtra(LabLauncherRuntime.GENERATION_ID_EXTRA))
        assertEquals(LabGameActivity::class.java.name, intent.component?.className)
    }

    @Test
    fun `production sources are rejected by fake runtime`() {
        val request = request("silksong")

        assertThrows(IllegalArgumentException::class.java) {
            runBlocking {
                runtime.provision(
                    ProvisionRequest(request, ProvisionSource.Local(File(root, "game"))),
                ) { }
            }
        }
    }

    @Test
    fun `clean game exit is recorded per profile and generation`() {
        LabLauncherRuntime.recordCleanExit(context, "hollow-knight", "lab-hk-3")

        assertEquals("lab-hk-3", LabLauncherRuntime.lastCleanExit(context, "hollow-knight"))
        assertEquals(null, LabLauncherRuntime.lastCleanExit(context, "silksong"))
    }

    private fun request(profileId: String): RuntimeRequest {
        val profile = GameProfiles.require(profileId)
        return RuntimeRequest(
            context,
            profile,
            ProfileBuildPaths(
                File(root, "files").apply { mkdirs() },
                File(root, "external").apply { mkdirs() },
                profile,
            ),
        )
    }
}
