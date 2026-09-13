package dev.silksong.launcher.runtime

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class GameLifecycleAuthorityTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun `exclusive transient lease is trustworthy inactive and holds through mutation`() {
        val root = temporary.newFolder()
        val authority = GameLifecycleAuthority(root)

        val result = authority.runIfInactive {
            assertThrows(IllegalStateException::class.java) { authority.acquireForGame() }
            "written"
        }

        assertEquals(GameProcessState.INACTIVE, result.state)
        assertEquals("written", result.value)
        assertEquals(listOf("game-lifecycle.lock"), root.list()?.sorted())
        assertEquals(0L, File(root, "game-lifecycle.lock").length())
    }

    @Test fun `live game lease reports active and never runs mutation`() {
        val authority = GameLifecycleAuthority(temporary.newFolder())
        val owner = authority.acquireForGame()
        var mutated = false
        try {
            val result = authority.runIfInactive { mutated = true }

            assertEquals(GameProcessState.ACTIVE, result.state)
            assertNull(result.value)
            assertFalse(mutated)
            assertThrows(IllegalStateException::class.java) { authority.markLaunchPending() }
        } finally {
            owner.close()
        }
    }

    @Test fun `crashed owner becomes inactive when operating system releases transient lease`() {
        val authority = GameLifecycleAuthority(temporary.newFolder())
        authority.acquireForGame().close()

        val result = authority.runIfInactive { "safe" }

        assertEquals(GameProcessState.INACTIVE, result.state)
        assertEquals("safe", result.value)
    }

    @Test fun `game remains active until its transient lease is released`() {
        val authority = GameLifecycleAuthority(temporary.newFolder())
        val owner = authority.acquireForGame()

        assertEquals(GameProcessState.ACTIVE, authority.runIfInactive { Unit }.state)
        owner.close()
        assertEquals(GameProcessState.INACTIVE, authority.runIfInactive { "ok" }.state)
    }

    @Test fun `lease storage failure reports unknown rather than active or inactive`() {
        val invalidRoot = temporary.newFile()

        val result = GameLifecycleAuthority(invalidRoot).runIfInactive { error("must not mutate") }

        assertEquals(GameProcessState.UNKNOWN, result.state)
        assertNull(result.value)
        assertThrows(IllegalStateException::class.java) {
            GameLifecycleAuthority(invalidRoot).markLaunchPending()
        }
    }

    @Test fun `launch pending hands off to active game lease without an inactive gap`() {
        val authority = GameLifecycleAuthority(temporary.newFolder())
        authority.markLaunchPending()
        val owner = authority.acquireForGame()
        try {
            assertEquals(GameProcessState.ACTIVE, authority.runIfInactive { Unit }.state)
            authority.clearLaunchPending()
            assertEquals(GameProcessState.ACTIVE, authority.runIfInactive { Unit }.state)
        } finally {
            owner.close()
        }
        assertEquals(GameProcessState.INACTIVE, authority.runIfInactive { Unit }.state)
    }

    @Test fun `launcher pending ownership is unknown until cancellation or return clears it`() {
        val root = temporary.newFolder()
        val pending = GameLifecycleAuthority(root)
        pending.markLaunchPending()

        assertEquals(GameProcessState.UNKNOWN, pending.runIfInactive { Unit }.state)
        assertFalse(File(root, "game-lifecycle.state").exists())

        pending.markLaunchCancelled()
        assertEquals(GameProcessState.INACTIVE, pending.runIfInactive { Unit }.state)

        pending.markLaunchPending()
        pending.clearLaunchPending()
        assertEquals(GameProcessState.INACTIVE, pending.runIfInactive { Unit }.state)
    }
}
