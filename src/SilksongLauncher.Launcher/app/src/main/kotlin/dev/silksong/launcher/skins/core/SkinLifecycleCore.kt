package dev.silksong.launcher.skins.core

/** Pure reducer. The caller owns state, binding authority, and before/after occurrence correlation. */
class SkinLifecycleCore {
    fun observe(state: LifecycleState, signal: LifecycleSignal): LifecycleDecision {
        if (!valid(state)) return decision(state, "invalid-state")
        if (signal is LifecycleSignal.Rebind) {
            if (signal.hero == state.currentHero && signal.skin == state.currentSkin) {
                return decision(state, "same-binding")
            }
            return decision(state.copy(
                currentHero = signal.hero, currentSkin = signal.skin, stableCount = 0,
                armedHero = null, armedSkin = null, armedOccurrence = null,
            ), "rebound")
        }
        if (state.currentHero == null) return decision(state, "unbound")
        return when (signal) {
            is LifecycleSignal.BeforeDeath -> beforeDeath(state, signal)
            is LifecycleSignal.AfterDeath -> afterDeath(state, signal)
            is LifecycleSignal.Update -> update(state, signal.observation)
            is LifecycleSignal.Rebind -> decision(state, "invalid-signal")
        }
    }

    private fun beforeDeath(state: LifecycleState, signal: LifecycleSignal.BeforeDeath): LifecycleDecision = when {
        signal.hero != state.currentHero || signal.skin != state.currentSkin -> decision(state, "stale-binding")
        signal.occurrence.value == 0uL -> decision(state, "invalid-occurrence")
        signal.occurrence.value <= state.occurrenceHighWater.value -> decision(state, "consumed-occurrence")
        state.armedOccurrence != null -> decision(state, "candidate-already-armed")
        state.pendingEpoch != null -> decision(state, "pending-epoch")
        // Consume at arm, not confirmation: even a subsequently invalidated candidate cannot replay.
        else -> decision(state.copy(armedHero = signal.hero, armedSkin = signal.skin,
            armedOccurrence = signal.occurrence, occurrenceHighWater = signal.occurrence), "armed")
    }

    private fun afterDeath(state: LifecycleState, signal: LifecycleSignal.AfterDeath): LifecycleDecision {
        if (signal.hero != state.currentHero || signal.skin != state.currentSkin) return decision(state, "stale-binding")
        if (signal.occurrence.value == 0uL) return decision(state, "invalid-occurrence")
        if (state.armedOccurrence == null) {
            return decision(state, if (signal.occurrence.value <= state.occurrenceHighWater.value)
                "consumed-occurrence" else "unarmed-after-death")
        }
        if (signal.occurrence != state.armedOccurrence || signal.hero != state.armedHero || signal.skin != state.armedSkin) {
            return decision(state, "mismatched-occurrence")
        }
        if (state.lastConfirmedEpoch.value == ULong.MAX_VALUE) {
            return decision(state.copy(armedHero = null, armedSkin = null, armedOccurrence = null), "epoch-overflow")
        }
        val epoch = DeathEpoch(state.lastConfirmedEpoch.value + 1uL)
        return LifecycleDecision(state.copy(
            armedHero = null, armedSkin = null, armedOccurrence = null,
            pendingEpoch = epoch, stableCount = 0, lastConfirmedEpoch = epoch,
        ), epoch, null, "death-confirmed")
    }

    private fun update(state: LifecycleState, observation: HeroObservation): LifecycleDecision {
        if (observation.hero != state.currentHero || observation.skin != state.currentSkin) {
            return decision(state, "stale-observation")
        }
        val epoch = state.pendingEpoch ?: return decision(state, "no-pending-epoch")
        val stable = observation.acceptingInput && observation.fullDamageMode && observation.health > 0 &&
            observation.canTakeDamage && observation.playable && !observation.paused &&
            !observation.cutscene && !observation.sceneTransition
        if (!stable) return decision(state.copy(stableCount = 0), "unstable-observation")
        if (state.stableCount < 2) return decision(state.copy(stableCount = state.stableCount + 1), "stabilizing")
        return LifecycleDecision(state.copy(pendingEpoch = null, stableCount = 0), null,
            StableRespawnToken(epoch, observation.hero, observation.skin), "stable-respawn")
    }

    // Reject malformed caller snapshots rather than repairing them into fresh authority/proof.
    private fun valid(state: LifecycleState): Boolean {
        if ((state.currentHero == null) != (state.currentSkin == null)) return false
        if (state.lastConfirmedEpoch.value > state.occurrenceHighWater.value) return false
        if (state.stableCount !in 0..2) return false
        if (state.pendingEpoch == null && state.stableCount != 0) return false
        if (state.pendingEpoch != null && (state.pendingEpoch.value == 0uL || state.pendingEpoch != state.lastConfirmedEpoch)) return false
        if ((state.armedHero == null) != (state.armedOccurrence == null) ||
            (state.armedSkin == null) != (state.armedOccurrence == null)) return false
        if (state.currentHero == null && (state.occurrenceHighWater.value != 0uL || state.armedOccurrence != null || state.pendingEpoch != null)) return false
        if (state.armedOccurrence != null && (state.armedHero != state.currentHero || state.armedSkin != state.currentSkin ||
            state.armedOccurrence.value <= state.lastConfirmedEpoch.value ||
            state.armedOccurrence != state.occurrenceHighWater || state.pendingEpoch != null)) return false
        return true
    }

    private fun decision(state: LifecycleState, diagnosis: String) = LifecycleDecision(state, null, null, diagnosis)
}
