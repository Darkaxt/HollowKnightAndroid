package dev.silksong.launcher.runtime

import org.junit.Assert.assertEquals
import org.junit.Test

class LaunchEligibilityTest {
    @Test
    fun `verified generation is eligible when no game process exists`() {
        val state = RuntimeState(
            ready = true,
            generationId = "gen-ready",
            detail = "ready",
            condition = RuntimeCondition.READY,
        )

        val result = LaunchEligibility.evaluate(state, gameProcessActive = false)

        assertEquals(LaunchEligibilityCode.READY, result.code)
        assertEquals("gen-ready", result.runtime.generationId)
    }

    @Test
    fun `missing generation routes to setup`() {
        val state = RuntimeState(
            ready = false,
            generationId = null,
            detail = "not configured",
            condition = RuntimeCondition.NOT_CONFIGURED,
        )

        val result = LaunchEligibility.evaluate(state, gameProcessActive = false)

        assertEquals(LaunchEligibilityCode.NOT_READY, result.code)
    }

    @Test
    fun `live game process blocks a profile switch without changing runtime state`() {
        val state = RuntimeState(
            ready = true,
            generationId = "gen-hk",
            detail = "ready",
            condition = RuntimeCondition.READY,
        )

        val result = LaunchEligibility.evaluate(state, gameProcessActive = true)

        assertEquals(LaunchEligibilityCode.GAME_PROCESS_ACTIVE, result.code)
        assertEquals(state, result.runtime)
    }

    @Test
    fun `runtime conditions expose the four launcher card states`() {
        assertEquals("Not configured", RuntimeCondition.NOT_CONFIGURED.label)
        assertEquals("Building", RuntimeCondition.BUILDING.label)
        assertEquals("Ready", RuntimeCondition.READY.label)
        assertEquals("Needs repair", RuntimeCondition.NEEDS_REPAIR.label)
    }
}
