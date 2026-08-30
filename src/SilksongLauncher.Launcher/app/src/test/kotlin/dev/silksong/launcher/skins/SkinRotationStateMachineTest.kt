package dev.silksong.launcher.skins

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SkinRotationStateMachineTest {
    @Test
    fun `death selects a different eligible skin without applying it`() {
        val machine = SkinRotationStateMachine(activeSkinId = "blue")

        val state = machine.onDeath(listOf("blue", "red", "gold"))

        assertEquals("blue", state.activeSkinId)
        assertEquals("red", state.pendingSkinId)
        assertEquals(RotationPhase.WAITING_FOR_STABLE_RESPAWN, state.phase)
    }

    @Test
    fun `repeated death before respawn keeps the original pending skin`() {
        val machine = SkinRotationStateMachine(activeSkinId = "blue")
        machine.onDeath(listOf("blue", "red", "gold"))

        val state = machine.onDeath(listOf("blue", "gold"))

        assertEquals("red", state.pendingSkinId)
    }

    @Test
    fun `stable respawn commits pending skin only after successful apply`() {
        val machine = SkinRotationStateMachine(activeSkinId = "blue")
        machine.onDeath(listOf("blue", "red"))
        var applied: String? = null

        val state = machine.onStableRespawn {
            applied = it
            true
        }

        assertEquals("red", applied)
        assertEquals("red", state.activeSkinId)
        assertNull(state.pendingSkinId)
        assertEquals(RotationPhase.ALIVE, state.phase)
    }

    @Test
    fun `failed apply retains last valid active skin`() {
        val machine = SkinRotationStateMachine(activeSkinId = "blue")
        machine.onDeath(listOf("blue", "red"))

        val state = machine.onStableRespawn { false }

        assertEquals("blue", state.activeSkinId)
        assertNull(state.pendingSkinId)
        assertEquals(RotationPhase.APPLY_FAILED, state.phase)
    }

    @Test
    fun `disabled rotation and one skin never create pending state`() {
        val disabled = SkinRotationStateMachine(activeSkinId = "blue", enabled = false)
        val single = SkinRotationStateMachine(activeSkinId = "blue")

        assertNull(disabled.onDeath(listOf("blue", "red")).pendingSkinId)
        assertNull(single.onDeath(listOf("blue")).pendingSkinId)
        assertFalse(disabled.state().enabled)
        assertTrue(single.state().enabled)
    }

    @Test
    fun `reset clears pending death and accepts known active skin`() {
        val machine = SkinRotationStateMachine(activeSkinId = "blue")
        machine.onDeath(listOf("blue", "red"))

        val state = machine.reset("gold")

        assertEquals("gold", state.activeSkinId)
        assertNull(state.pendingSkinId)
        assertEquals(RotationPhase.ALIVE, state.phase)
    }
}
