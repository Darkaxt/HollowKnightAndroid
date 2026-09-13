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

    @Test fun `missing state with exclusive lease is trustworthy inactive and holds lease through mutation`() {
        val authority = GameLifecycleAuthority(temporary.newFolder())

        val result = authority.runIfInactive {
            assertThrows(IllegalStateException::class.java) { authority.acquireForGame() }
            "written"
        }

        assertEquals(GameProcessState.INACTIVE, result.state)
        assertEquals("written", result.value)
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

    @Test fun `crashed active owner fails unknown after operating system releases lease`() {
        val authority = GameLifecycleAuthority(temporary.newFolder())
        authority.acquireForGame().close()

        val result = authority.runIfInactive { error("must not mutate") }

        assertEquals(GameProcessState.UNKNOWN, result.state)
        assertNull(result.value)
    }

    @Test fun `clean stop is inactive only after game lease is released`() {
        val authority = GameLifecycleAuthority(temporary.newFolder())
        val owner = authority.acquireForGame()
        owner.markStopped()

        assertEquals(GameProcessState.ACTIVE, authority.runIfInactive { Unit }.state)
        owner.close()
        assertEquals(GameProcessState.INACTIVE, authority.runIfInactive { "ok" }.state)
    }

    @Test fun `lease storage failure reports unknown rather than active or inactive`() {
        val invalidRoot = temporary.newFile()

        val result = GameLifecycleAuthority(invalidRoot).runIfInactive { error("must not mutate") }

        assertEquals(GameProcessState.UNKNOWN, result.state)
        assertNull(result.value)
    }

    @Test fun `launch pending and malformed state fail unknown while cancellation closes pending launch`() {
        val pending = GameLifecycleAuthority(temporary.newFolder())
        pending.markLaunchPending()
        assertEquals(GameProcessState.UNKNOWN, pending.runIfInactive { Unit }.state)
        pending.markLaunchCancelled()
        assertEquals(GameProcessState.INACTIVE, pending.runIfInactive { Unit }.state)

        val malformedRoot = temporary.newFolder()
        File(malformedRoot, "game-lifecycle.state").writeText("garbage")
        assertEquals(
            GameProcessState.UNKNOWN,
            GameLifecycleAuthority(malformedRoot).runIfInactive { Unit }.state,
        )
    }
}
