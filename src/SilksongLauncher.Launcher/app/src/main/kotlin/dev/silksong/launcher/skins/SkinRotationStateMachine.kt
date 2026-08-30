package dev.silksong.launcher.skins

enum class RotationPhase {
    ALIVE,
    WAITING_FOR_STABLE_RESPAWN,
    DEAD_NO_CHANGE,
    APPLY_FAILED,
}

data class SkinRotationState(
    val enabled: Boolean,
    val activeSkinId: String?,
    val pendingSkinId: String?,
    val phase: RotationPhase,
)

class SkinRotationStateMachine(
    activeSkinId: String?,
    enabled: Boolean = true,
) {
    private var state = SkinRotationState(
        enabled = enabled,
        activeSkinId = activeSkinId,
        pendingSkinId = null,
        phase = RotationPhase.ALIVE,
    )

    @Synchronized
    fun state(): SkinRotationState = state

    @Synchronized
    fun onDeath(eligibleIds: List<String>): SkinRotationState {
        if (state.phase == RotationPhase.WAITING_FOR_STABLE_RESPAWN) return state
        val eligible = eligibleIds.filter(String::isNotBlank).distinct()
        if (!state.enabled || eligible.size < 2) {
            state = state.copy(pendingSkinId = null, phase = RotationPhase.DEAD_NO_CHANGE)
            return state
        }

        val activeIndex = eligible.indexOf(state.activeSkinId)
        val selected = if (activeIndex < 0) eligible.first() else eligible[(activeIndex + 1) % eligible.size]
        if (selected == state.activeSkinId) {
            state = state.copy(pendingSkinId = null, phase = RotationPhase.DEAD_NO_CHANGE)
        } else {
            state = state.copy(
                pendingSkinId = selected,
                phase = RotationPhase.WAITING_FOR_STABLE_RESPAWN,
            )
        }
        return state
    }

    @Synchronized
    fun onStableRespawn(apply: (String) -> Boolean): SkinRotationState {
        val pending = state.pendingSkinId
        if (pending == null) {
            state = state.copy(phase = RotationPhase.ALIVE)
            return state
        }
        state = if (runCatching { apply(pending) }.getOrDefault(false)) {
            state.copy(
                activeSkinId = pending,
                pendingSkinId = null,
                phase = RotationPhase.ALIVE,
            )
        } else {
            state.copy(pendingSkinId = null, phase = RotationPhase.APPLY_FAILED)
        }
        return state
    }

    @Synchronized
    fun reset(activeSkinId: String?): SkinRotationState {
        state = state.copy(
            activeSkinId = activeSkinId,
            pendingSkinId = null,
            phase = RotationPhase.ALIVE,
        )
        return state
    }
}
