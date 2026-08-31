package io.github.darkaxt.dualsouls.lab

import android.content.Context
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfileBuildPaths
import dev.silksong.launcher.runtime.ProvisionRequest
import dev.silksong.launcher.runtime.ProvisionSource
import dev.silksong.launcher.runtime.EvidenceKind
import dev.silksong.launcher.runtime.LauncherRuntimeProvider
import dev.silksong.launcher.runtime.RuntimeRequest
import java.io.File
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LabLauncherIntegrationTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context: Context = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private val runtime = LabLauncherRuntime()

    @Test
    fun launcherRendersBothProfilesAndPersistsSelection() {
        stopLauncherProcess()
        context.getSharedPreferences("selected-game", Context.MODE_PRIVATE).edit().clear().commit()
        shell("am start -W -n $PACKAGE/dev.silksong.launcher.LauncherActivity")

        assertEquals(EvidenceKind.EMULATOR_FAKE, LauncherRuntimeProvider.from(context).evidenceKind)
        val hollowKnight = device.findObject(By.res(PACKAGE, "radio_hollow_knight"))
        val silksong = device.findObject(By.res(PACKAGE, "radio_silksong"))
        assertNotNull(hollowKnight)
        assertNotNull(silksong)
        assertTrue(hollowKnight.contentDescription.toString().startsWith("Hollow Knight, "))
        assertTrue(silksong.contentDescription.toString().startsWith("Hollow Knight: Silksong, "))
        hollowKnight.click()
        shell("am start -W -n $PACKAGE/dev.silksong.launcher.LauncherActivity")

        assertTrue(device.findObject(By.res(PACKAGE, "radio_hollow_knight")).isChecked)
        assertTrue(shell("run-as $PACKAGE cat shared_prefs/selected-game.xml").contains("hollow-knight"))
        val oldPid = processId("$PACKAGE:launcher")
        assertTrue(oldPid.isNotBlank())
        stopLauncherProcess()
        shell("am start -W -n $PACKAGE/dev.silksong.launcher.LauncherActivity")
        assertNotEquals(oldPid, processId("$PACKAGE:launcher"))
        assertTrue(shell("run-as $PACKAGE cat shared_prefs/selected-game.xml").contains("hollow-knight"))
    }

    @Test
    fun profilesPublishRecoverAndResetIndependently() = runBlocking {
        clearLabState()
        val hollowKnight = request("hollow-knight")
        val silksong = request("silksong")
        runtime.provision(ProvisionRequest(hollowKnight, ProvisionSource.Synthetic)) { }
        runtime.provision(ProvisionRequest(silksong, ProvisionSource.Synthetic)) { }
        context.getSharedPreferences(LabLauncherRuntime.CONTROL_PREFS, Context.MODE_PRIVATE)
            .edit().putBoolean(LabLauncherRuntime.failureKey("silksong"), true).commit()

        val failure = runCatching {
            runtime.provision(ProvisionRequest(silksong, ProvisionSource.Synthetic)) { }
        }.exceptionOrNull()
        assertTrue(failure is IllegalStateException)
        assertEquals("lab-ss-1", LabLauncherRuntime().inspect(silksong).generationId)

        val failedJob = "job-${silksong.profile.runtimeStorageKey}-2"
        assertTrue(GenerationPublisher(silksong.paths.profilePaths).discard(failedJob))
        runtime.reset(hollowKnight)
        assertFalse(runtime.inspect(hollowKnight).ready)
        assertEquals("lab-ss-1", LabLauncherRuntime().inspect(silksong).generationId)
    }

    @Test
    fun gameProcessExitsBeforeTheOtherProfileStarts() = runBlocking {
        clearLabState()
        val hollowKnight = request("hollow-knight")
        val silksong = request("silksong")
        val hkState = runtime.provision(ProvisionRequest(hollowKnight, ProvisionSource.Synthetic)) { }
        val ssState = runtime.provision(ProvisionRequest(silksong, ProvisionSource.Synthetic)) { }

        startGame(hollowKnight.profile.id, requireNotNull(hkState.generationId))
        val oldPid = processId("$PACKAGE:game")
        assertTrue(oldPid.isNotBlank())
        cleanExit(hollowKnight.profile.id, requireNotNull(hkState.generationId))
        assertNull(processId("$PACKAGE:game").ifBlank { null })

        startGame(silksong.profile.id, requireNotNull(ssState.generationId))
        val newPid = processId("$PACKAGE:game")
        assertTrue(newPid.isNotBlank())
        assertNotEquals(oldPid, newPid)
        cleanExit(silksong.profile.id, requireNotNull(ssState.generationId))
        assertNull(processId("$PACKAGE:game").ifBlank { null })
        assertEquals(ssState.generationId, LabLauncherRuntime.lastCleanExit(context, silksong.profile.id))
    }

    private fun startGame(profileId: String, generationId: String) {
        val result = shell(
            "am start -W -n $PACKAGE/io.github.darkaxt.dualsouls.lab.LabGameActivity " +
                "--es ${LabLauncherRuntime.PROFILE_ID_EXTRA} $profileId " +
                "--es ${LabLauncherRuntime.GENERATION_ID_EXTRA} $generationId",
        )
        assertTrue(result.contains("Status: ok"))
    }

    private fun cleanExit(profileId: String, generationId: String) {
        val result = shell(
            "am broadcast -n $PACKAGE/io.github.darkaxt.dualsouls.lab.LabGameControlReceiver " +
                "-a $CLEAN_EXIT_ACTION --es ${LabLauncherRuntime.PROFILE_ID_EXTRA} $profileId " +
                "--es ${LabLauncherRuntime.GENERATION_ID_EXTRA} $generationId",
        )
        assertTrue(result.contains("result=0"))
    }

    private fun processId(processName: String): String = shell("pidof $processName").trim()

    private fun stopLauncherProcess() {
        shell(
            "am broadcast -n $PACKAGE/io.github.darkaxt.dualsouls.lab.LabLauncherControlReceiver " +
                "-a $CLEAN_LAUNCHER_EXIT_ACTION",
        )
        assertNull(processId("$PACKAGE:launcher").ifBlank { null })
    }

    private fun shell(command: String): String = device.executeShellCommand(command)

    private fun request(profileId: String): RuntimeRequest {
        val profile = GameProfiles.require(profileId)
        return RuntimeRequest(
            context,
            profile,
            ProfileBuildPaths(
                context.filesDir,
                requireNotNull(context.getExternalFilesDir(null)),
                profile,
            ),
        )
    }

    private fun clearLabState() {
        for (profile in GameProfiles.all) {
            val request = request(profile.id)
            runtime.reset(request)
        }
        context.getSharedPreferences(LabLauncherRuntime.CONTROL_PREFS, Context.MODE_PRIVATE)
            .edit().clear().commit()
        context.getSharedPreferences(LabLauncherRuntime.STATE_PREFS, Context.MODE_PRIVATE)
            .edit().clear().commit()
    }

    private companion object {
        const val PACKAGE = "io.github.darkaxt.dualsouls.emutest"
        const val CLEAN_EXIT_ACTION = "io.github.darkaxt.dualsouls.lab.CLEAN_EXIT"
        const val CLEAN_LAUNCHER_EXIT_ACTION = "io.github.darkaxt.dualsouls.lab.CLEAN_LAUNCHER_EXIT"
    }
}
