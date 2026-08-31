package dev.silksong.launcher.shortcuts

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.LauncherActivity
import dev.silksong.launcher.R
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.runtime.RuntimeCondition
import dev.silksong.launcher.runtime.RuntimeState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class GameShortcutControllerTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()

    @Test
    fun `unsupported launcher is reported without requesting a shortcut`() {
        val gateway = RecordingGateway(supported = false)
        val result = controller(gateway).request(
            GameProfiles.require("hollow-knight"),
            readyState("hk-ready"),
        )

        assertEquals(GameShortcutResult.UNSUPPORTED, result)
        assertTrue(gateway.requests.isEmpty())
    }

    @Test
    fun `existing pinned shortcut is not requested twice`() {
        val gateway = RecordingGateway(pinned = setOf("game-hollow-knight"))
        val controller = controller(gateway)
        val result = controller.request(
            GameProfiles.require("hollow-knight"),
            readyState("hk-ready"),
        )

        assertEquals(GameShortcutResult.ALREADY_PINNED, result)
        assertTrue(controller.isPinned(GameProfiles.require("hollow-knight")))
        assertFalse(controller.isPinned(GameProfiles.require("silksong")))
        assertTrue(gateway.requests.isEmpty())
    }

    @Test
    fun `unready profile cannot create a dead shortcut`() {
        val gateway = RecordingGateway()
        val result = controller(gateway).request(
            GameProfiles.require("silksong"),
            RuntimeState(
                ready = false,
                generationId = null,
                detail = "missing",
                condition = RuntimeCondition.NOT_CONFIGURED,
            ),
        )

        assertEquals(GameShortcutResult.NOT_READY, result)
        assertTrue(gateway.requests.isEmpty())
    }

    @Test
    fun `both shortcuts carry only their registered profile and supplied icon`() {
        val gateway = RecordingGateway()
        val controller = controller(gateway)

        assertEquals(
            GameShortcutResult.REQUESTED,
            controller.request(GameProfiles.require("hollow-knight"), readyState("hk-ready")),
        )
        assertEquals(
            GameShortcutResult.REQUESTED,
            controller.request(GameProfiles.require("silksong"), readyState("ss-ready")),
        )

        assertEquals(2, gateway.requests.size)
        val hollowKnight = gateway.requests[0]
        val silksong = gateway.requests[1]
        assertEquals("game-hollow-knight", hollowKnight.id)
        assertEquals(R.drawable.shortcut_hollow_knight, hollowKnight.iconResource)
        assertEquals("hollow-knight", hollowKnight.intent.getStringExtra(GameShortcutContract.PROFILE_ID_EXTRA))
        assertEquals(1, hollowKnight.intent.extras?.keySet()?.size)
        assertEquals(LauncherActivity::class.java.name, hollowKnight.intent.component?.className)
        assertEquals(GameShortcutContract.ACTION_DIRECT_LAUNCH, hollowKnight.intent.action)
        assertEquals("game-silksong", silksong.id)
        assertEquals(R.drawable.shortcut_silksong, silksong.iconResource)
        assertEquals("silksong", silksong.intent.getStringExtra(GameShortcutContract.PROFILE_ID_EXTRA))
        assertEquals(1, silksong.intent.extras?.keySet()?.size)
        assertEquals(LauncherActivity::class.java.name, silksong.intent.component?.className)
    }

    private fun controller(gateway: ShortcutGateway) = GameShortcutController(context, gateway)

    private fun readyState(generation: String) = RuntimeState(
        ready = true,
        generationId = generation,
        detail = "ready",
        condition = RuntimeCondition.READY,
    )

    private class RecordingGateway(
        private val supported: Boolean = true,
        private val pinned: Set<String> = emptySet(),
    ) : ShortcutGateway {
        val requests = mutableListOf<GameShortcutRequest>()

        override fun isSupported(): Boolean = supported

        override fun isPinned(id: String): Boolean = id in pinned

        override fun request(shortcut: GameShortcutRequest): Boolean {
            requests += shortcut
            return true
        }
    }
}
