package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.SkinBindingToken

@JvmInline
value class DeathEpoch(val value: ULong)

/** Trusted caller correlates one before/after pair with a fresh monotonically increasing value. */
@JvmInline
value class DeathOccurrenceToken(val value: ULong)

/** Hero identity; unchanged across deaths while the hero identity remains the same. */
@JvmInline
value class HeroBindingToken(val value: String)

data class StableRespawnToken(val deathEpoch: DeathEpoch, val hero: HeroBindingToken, val skin: SkinBindingToken)

data class HeroObservation(
    val hero: HeroBindingToken,
    val skin: SkinBindingToken,
    val acceptingInput: Boolean,
    val fullDamageMode: Boolean,
    val health: Int,
    val canTakeDamage: Boolean,
    val playable: Boolean,
    val paused: Boolean,
    val cutscene: Boolean,
    val sceneTransition: Boolean,
)

/** Zero means no event yet. Both high-water values survive delivery and rebind. */
data class LifecycleState(
    val armedHero: HeroBindingToken? = null,
    val pendingEpoch: DeathEpoch? = null,
    val stableCount: Int = 0,
    val currentHero: HeroBindingToken? = null,
    val currentSkin: SkinBindingToken? = null,
    val lastConfirmedEpoch: DeathEpoch = DeathEpoch(0uL),
    val armedSkin: SkinBindingToken? = null,
    val armedOccurrence: DeathOccurrenceToken? = null,
    val occurrenceHighWater: DeathOccurrenceToken = DeathOccurrenceToken(0uL),
)

sealed interface LifecycleSignal {
    /** Trusted caller boundary; updates cannot appoint binding authority. */
    data class Rebind(val hero: HeroBindingToken, val skin: SkinBindingToken) : LifecycleSignal
    data class BeforeDeath(val hero: HeroBindingToken, val skin: SkinBindingToken, val occurrence: DeathOccurrenceToken) : LifecycleSignal
    data class AfterDeath(val hero: HeroBindingToken, val skin: SkinBindingToken, val occurrence: DeathOccurrenceToken) : LifecycleSignal
    data class Update(val observation: HeroObservation) : LifecycleSignal
}

data class LifecycleDecision(
    val state: LifecycleState,
    val confirmedEpoch: DeathEpoch?,
    val stableToken: StableRespawnToken?,
    val diagnosis: String,
)
