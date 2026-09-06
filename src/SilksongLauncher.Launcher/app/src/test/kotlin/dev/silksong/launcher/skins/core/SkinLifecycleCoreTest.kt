package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.SkinBindingToken
import org.junit.Assert.*
import org.junit.Test

class SkinLifecycleCoreTest {
    private val core = SkinLifecycleCore()
    private val hero = HeroBindingToken("hero-1")
    private val skin = SkinBindingToken("skin-1")
    private val nextHero = HeroBindingToken("hero-2")
    private val nextSkin = SkinBindingToken("skin-2")
    private fun bind() = core.observe(LifecycleState(), LifecycleSignal.Rebind(hero, skin)).state
    private fun before(n: ULong = 1uL, h: HeroBindingToken = hero, s: SkinBindingToken = skin) =
        LifecycleSignal.BeforeDeath(h, s, DeathOccurrenceToken(n))
    private fun after(n: ULong = 1uL, h: HeroBindingToken = hero, s: SkinBindingToken = skin) =
        LifecycleSignal.AfterDeath(h, s, DeathOccurrenceToken(n))
    private fun good(h: HeroBindingToken = hero, s: SkinBindingToken = skin) =
        HeroObservation(h, s, true, true, 1, true, true, false, false, false)
    private fun update(state: LifecycleState, observation: HeroObservation = good()) =
        core.observe(state, LifecycleSignal.Update(observation))
    private fun confirm(state: LifecycleState = bind(), n: ULong = 1uL): LifecycleDecision {
        val armed = core.observe(state, before(n, state.currentHero!!, state.currentSkin!!)).state
        return core.observe(armed, after(n, state.currentHero!!, state.currentSkin!!))
    }
    private fun deliver(state: LifecycleState, observation: HeroObservation = good()) =
        update(update(update(state, observation).state, observation).state, observation)
    private fun unchanged(state: LifecycleState, signal: LifecycleSignal, diagnosis: String) {
        val result = core.observe(state, signal)
        assertEquals(state, result.state)
        assertNull(result.confirmedEpoch)
        assertNull(result.stableToken)
        assertEquals(diagnosis, result.diagnosis)
    }

    @Test fun matchesRespawnEpochHeroAndSkin() {
        val confirmed = confirm(n = 7uL)
        assertEquals(DeathEpoch(1uL), confirmed.confirmedEpoch)
        assertEquals(DeathOccurrenceToken(7uL), confirmed.state.occurrenceHighWater)
        assertNull(confirmed.stableToken)
        val delivered = deliver(confirmed.state)
        assertEquals(StableRespawnToken(DeathEpoch(1uL), hero, skin), delivered.stableToken)
        assertNull(delivered.state.pendingEpoch)
        assertEquals(DeathEpoch(1uL), delivered.state.lastConfirmedEpoch)
    }

    @Test fun requiresThreeStableMatchingUpdates() {
        val armed = core.observe(bind(), before()).state
        val preconfirmation = deliver(armed)
        assertNull(preconfirmation.stableToken)
        assertEquals(0, preconfirmation.state.stableCount)
        val one = update(core.observe(preconfirmation.state, after()).state)
        assertEquals(1, one.state.stableCount)
        assertNull(one.stableToken)
        val two = update(one.state)
        assertEquals(2, two.state.stableCount)
        assertNull(two.stableToken)
        assertNotNull(update(two.state).stableToken)
    }

    @Test fun rebindRetainsPendingEpochAndRejectsOldTokens() {
        val two = update(update(confirm().state).state).state
        for (h in listOf(hero, nextHero)) {
            val rebound = core.observe(two, LifecycleSignal.Rebind(h, nextSkin)).state
            assertEquals(two.pendingEpoch, rebound.pendingEpoch)
            assertEquals(two.lastConfirmedEpoch, rebound.lastConfirmedEpoch)
            assertEquals(two.occurrenceHighWater, rebound.occurrenceHighWater)
            assertEquals(0, rebound.stableCount)
            unchanged(rebound, after(), "stale-binding")
            unchanged(rebound, LifecycleSignal.Update(good()), "stale-observation")
            assertEquals(StableRespawnToken(DeathEpoch(1uL), h, nextSkin), deliver(rebound, good(h, nextSkin)).stableToken)
        }
    }

    @Test fun ignoresDuplicateStaleAndPreconfirmationSignals() {
        val bound = bind()
        unchanged(bound, after(), "unarmed-after-death")
        unchanged(bound, LifecycleSignal.Update(good()), "no-pending-epoch")
        unchanged(bound, before(h = nextHero), "stale-binding")
        unchanged(bound, before(s = nextSkin), "stale-binding")
        val armed = core.observe(bound, before()).state
        unchanged(armed, before(), "consumed-occurrence")
        unchanged(armed, before(2uL), "candidate-already-armed")
        unchanged(armed, after(h = nextHero), "stale-binding")
        unchanged(armed, after(s = nextSkin), "stale-binding")
        val confirmed = core.observe(armed, after()).state
        unchanged(confirmed, after(), "consumed-occurrence")
        unchanged(confirmed, before(), "consumed-occurrence")
    }

    @Test fun everyFalsePredicateResetsWindow() {
        val g = good()
        val failures = listOf(g.copy(acceptingInput = false), g.copy(fullDamageMode = false),
            g.copy(health = 0), g.copy(health = -1), g.copy(canTakeDamage = false),
            g.copy(playable = false), g.copy(paused = true), g.copy(cutscene = true), g.copy(sceneTransition = true))
        for (bad in failures) {
            val two = update(update(confirm().state).state).state
            val reset = update(two, bad)
            assertEquals("unstable-observation", reset.diagnosis)
            assertNull(reset.stableToken)
            assertEquals(0, reset.state.stableCount)
            val second = update(update(reset.state).state)
            assertNull(second.stableToken)
            assertNotNull(update(second.state).stableToken)
        }
    }

    @Test fun emittedEpochCannotEmitAgain() {
        val emitted = deliver(confirm().state).state
        repeat(5) { unchanged(emitted, LifecycleSignal.Update(good()), "no-pending-epoch") }
        unchanged(emitted, before(), "consumed-occurrence")
        unchanged(emitted, after(), "consumed-occurrence")
        assertEquals(DeathOccurrenceToken(1uL), emitted.occurrenceHighWater)
    }

    @Test fun repeatedSameHeroDeathConfirmsEpochTwo() {
        val emitted = deliver(confirm().state).state
        val second = confirm(emitted, 2uL)
        assertEquals(DeathEpoch(2uL), second.confirmedEpoch)
        assertEquals(hero, second.state.currentHero)
        assertEquals(skin, second.state.currentSkin)
        assertEquals(StableRespawnToken(DeathEpoch(2uL), hero, skin), deliver(second.state).stableToken)
    }

    @Test fun skinOnlyRebindAcceptsFirstDeath() {
        val rebound = core.observe(bind(), LifecycleSignal.Rebind(hero, nextSkin)).state
        val confirmed = confirm(rebound)
        assertEquals(DeathEpoch(1uL), confirmed.confirmedEpoch)
        assertEquals(StableRespawnToken(DeathEpoch(1uL), hero, nextSkin), deliver(confirmed.state, good(hero, nextSkin)).stableToken)
    }

    @Test fun skinOnlyRebindAfterDeliveryAcceptsFreshOccurrence() {
        val rebound = core.observe(deliver(confirm().state).state, LifecycleSignal.Rebind(hero, nextSkin)).state
        for (s in listOf(skin, nextSkin)) {
            val diagnosis = if (s == skin) "stale-binding" else "consumed-occurrence"
            unchanged(rebound, before(s = s), diagnosis)
            unchanged(rebound, after(s = s), diagnosis)
        }
        val second = confirm(rebound, 2uL)
        assertEquals(DeathEpoch(2uL), second.confirmedEpoch)
        assertNotNull(deliver(second.state, good(hero, nextSkin)).stableToken)
    }

    @Test fun invalidatedArmedOccurrenceCannotReplayAfterRebind() {
        val armed = core.observe(bind(), before(5uL)).state
        for (h in listOf(hero, nextHero)) {
            val rebound = core.observe(armed, LifecycleSignal.Rebind(h, nextSkin)).state
            assertNull(rebound.armedOccurrence)
            assertNull(rebound.armedHero)
            assertNull(rebound.armedSkin)
            assertEquals(DeathOccurrenceToken(5uL), rebound.occurrenceHighWater)
            assertEquals(DeathEpoch(0uL), rebound.lastConfirmedEpoch)
            unchanged(rebound, before(5uL, h, nextSkin), "consumed-occurrence")
            unchanged(rebound, after(5uL, h, nextSkin), "consumed-occurrence")
            unchanged(rebound, after(5uL), "stale-binding")
            assertEquals(DeathEpoch(1uL), confirm(rebound, 6uL).confirmedEpoch)
        }
    }

    @Test fun outOfOrderAfterNeverReplacesArmedOccurrence() {
        val bound = bind()
        unchanged(bound, after(10uL), "unarmed-after-death")
        val armed = core.observe(bound, before(5uL)).state
        for (n in listOf(1uL, 4uL, 6uL, ULong.MAX_VALUE)) unchanged(armed, after(n), "mismatched-occurrence")
        assertEquals(DeathEpoch(1uL), core.observe(armed, after(5uL)).confirmedEpoch)
    }

    @Test fun epochOverflowBlocks() {
        val near = bind().copy(lastConfirmedEpoch = DeathEpoch(ULong.MAX_VALUE - 1uL),
            occurrenceHighWater = DeathOccurrenceToken(ULong.MAX_VALUE - 1uL))
        val maximum = confirm(near, ULong.MAX_VALUE)
        assertEquals(DeathEpoch(ULong.MAX_VALUE), maximum.confirmedEpoch)
        val delivered = deliver(maximum.state).state
        unchanged(delivered, before(0uL), "invalid-occurrence")
        unchanged(delivered, before(ULong.MAX_VALUE), "consumed-occurrence")
        val exhausted = delivered.copy(armedHero = hero, armedSkin = skin, armedOccurrence = DeathOccurrenceToken(ULong.MAX_VALUE))
        // At epoch exhaustion no unsigned occurrence can remain strictly newer; forged arming is invalid.
        unchanged(exhausted, after(ULong.MAX_VALUE), "invalid-state")
    }

    @Test fun occurrenceHighWaterExhaustionNeverWraps() {
        val bound = bind()
        unchanged(bound, before(0uL), "invalid-occurrence")
        unchanged(bound, after(0uL), "invalid-occurrence")
        val armed = core.observe(bound, before(ULong.MAX_VALUE)).state
        assertEquals(DeathOccurrenceToken(ULong.MAX_VALUE), armed.occurrenceHighWater)
        val confirmed = core.observe(armed, after(ULong.MAX_VALUE))
        assertEquals(DeathEpoch(1uL), confirmed.confirmedEpoch)
        val delivered = deliver(confirmed.state).state
        for (n in listOf(0uL, 1uL, ULong.MAX_VALUE - 1uL, ULong.MAX_VALUE)) {
            unchanged(delivered, before(n), if (n == 0uL) "invalid-occurrence" else "consumed-occurrence")
        }
        val rebound = core.observe(delivered, LifecycleSignal.Rebind(nextHero, nextSkin)).state
        unchanged(rebound, before(1uL, nextHero, nextSkin), "consumed-occurrence")
    }

    @Test fun repeatedBindPreservesWindow() {
        val armed = core.observe(bind(), before()).state
        unchanged(armed, LifecycleSignal.Rebind(hero, skin), "same-binding")
        val two = update(update(core.observe(armed, after()).state).state).state
        unchanged(two, LifecycleSignal.Rebind(HeroBindingToken("hero-1"), SkinBindingToken("skin-1")), "same-binding")
        assertNotNull(update(two).stableToken)
    }

    @Test fun unboundUpdatesCannotCreateAuthority() {
        for (signal in listOf(LifecycleSignal.Update(good()), before(), after())) unchanged(LifecycleState(), signal, "unbound")
    }

    @Test fun pendingEpochCannotBeOverwritten() {
        val pending = confirm().state
        unchanged(pending, before(2uL), "pending-epoch")
        unchanged(pending, after(2uL), "unarmed-after-death")
        assertEquals(DeathOccurrenceToken(1uL), pending.occurrenceHighWater)
    }

    @Test fun staleObservationPreservesConsecutiveWindow() {
        val two = update(update(confirm().state).state).state
        for (stale in listOf(good(nextHero), good(s = nextSkin), good(nextHero, nextSkin).copy(health = 0))) {
            unchanged(two, LifecycleSignal.Update(stale), "stale-observation")
        }
        assertNotNull(update(two).stableToken)
    }

    @Test fun repeatedStaleInterleavingCannotStarveRespawn() {
        var state = confirm().state
        for (step in 1..3) {
            repeat(8) {
                val stale = update(state, good(nextHero, nextSkin).copy(paused = true))
                assertEquals(state, stale.state)
                assertNull(stale.stableToken)
                state = stale.state
            }
            val current = update(state)
            assertEquals(step == 3, current.stableToken != null)
            state = current.state
        }
    }

    @Test fun invalidStatesFailClosed() {
        val pending = confirm().state
        val invalid = listOf(pending.copy(pendingEpoch = null, armedHero = hero, armedSkin = skin,
            armedOccurrence = DeathOccurrenceToken(1uL)), pending.copy(stableCount = -1), pending.copy(stableCount = 3),
            pending.copy(pendingEpoch = DeathEpoch(0uL)), pending.copy(lastConfirmedEpoch = DeathEpoch(2uL)),
            pending.copy(currentSkin = null), pending.copy(armedHero = hero),
            pending.copy(occurrenceHighWater = DeathOccurrenceToken(0uL)), bind().copy(stableCount = 1),
            bind().copy(armedHero = nextHero), LifecycleState(occurrenceHighWater = DeathOccurrenceToken(1uL)),
            bind().copy(armedHero = hero, armedSkin = skin, armedOccurrence = DeathOccurrenceToken(2uL),
                occurrenceHighWater = DeathOccurrenceToken(1uL)))
        for (state in invalid) {
            for (signal in listOf(LifecycleSignal.Update(good()), LifecycleSignal.Rebind(nextHero, nextSkin), before(), after())) {
                unchanged(state, signal, "invalid-state")
            }
        }
    }

    @Test fun reducerIsDeterministicAndValuesHaveStructuralEquality() {
        val state = confirm().state
        val signal = LifecycleSignal.Update(good())
        assertEquals(core.observe(state, signal), SkinLifecycleCore().observe(state.copy(), signal.copy(observation = good())))
        assertEquals(0, state.stableCount)
        assertEquals(before(), before(1uL, HeroBindingToken("hero-1"), SkinBindingToken("skin-1")))
        assertNotEquals(before(), before(2uL))
        val a = StableRespawnToken(DeathEpoch(1uL), hero, skin)
        val b = StableRespawnToken(DeathEpoch(1uL), HeroBindingToken("hero-1"), SkinBindingToken("skin-1"))
        assertEquals(a, b)
        assertEquals(a.hashCode(), b.hashCode())
        assertNotEquals(a, b.copy(deathEpoch = DeathEpoch(2uL)))
    }
}
