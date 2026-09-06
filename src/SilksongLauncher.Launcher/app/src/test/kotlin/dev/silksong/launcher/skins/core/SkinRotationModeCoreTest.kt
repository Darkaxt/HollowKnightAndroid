package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test

class SkinRotationModeCoreTest {
    private val core = SkinRotationCore()
    private val hero = HeroBindingToken("hero")
    private val skin = SkinBindingToken("skin")
    private fun hash(c: Char) = c.toString().repeat(64)
    private fun id(n: Int) = "00000000-0000-0000-0000-${n.toString(16).padStart(12, '0')}"
    private fun pack(id: String = "a") = ActiveVisual.Pack(id, hash('a'), hash('b'), hash('c'))
    private fun state(mode: SkinMode = SkinMode.ON, selected: String? = "a", active: ActiveVisual = pack()): RotationModeState {
        val a = ActivationSnapshot(mode, selected, active, 7)
        val ring = requireNotNull(RotationRing.tryCreate(listOf(RotationDescriptor("a", pack(), false),
            RotationDescriptor("b", pack("b"), true), RotationDescriptor("c", pack("c"), true))))
        return RotationModeState(RotationState(ring, a, hero, skin), VerifiedRegistryHead(id(1), hash('d'), a, RotationInterlock.clear()))
    }
    private fun advance(s: RotationModeState) = core.decide(s, RotationModeEvent.AdvanceMode)
    private fun proof(s: RotationModeState, visual: ActiveVisual? = null) = core.decide(s, RotationModeEvent.VerifiedVisual(
        HeroVerifiedVisual(hero, if (visual is ActiveVisual.Pack) VerifiedLiveVisualProof.Pack(skin, visual) else VerifiedLiveVisualProof.Vanilla(skin)))).state
    private fun completion(s: RotationModeState): RotationModeEvent.ModeCommitted {
        val p = s.operation!!
        return RotationModeEvent.ModeCommitted(p.correlation, RegistryCommitReceipt(p.baseHead.generationId, p.baseHead.generationSha256, id(2), hash('e')),
            VerifiedRegistryHead(id(2), hash('e'), p.target, RotationInterlock.clear()))
    }
    private fun unchanged(s: RotationModeState, e: RotationModeEvent) { val d = core.decide(s, e); assertEquals(s, d.state); assertTrue(d.commands.isEmpty()) }
    private fun death(n: ULong = 1uL) = RotationModeEvent.Selector(RotationEvent.ConfirmDeath(DeathEpoch(n), hero, skin))
    private fun stable(n: ULong = 1uL) = RotationModeEvent.Selector(RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(n), hero, skin)))

    @Test fun onToRotatePublishesOnlyExactChildAndPreservesAllHistory() {
        val s = state(); val d = advance(s)
        assertTrue(d.commands.single() is RotationModeCommand.CommitOnToRotate)
        assertEquals(s.rotation.activation, d.state.rotation.activation); assertEquals(s.head, d.state.head)
        assertEquals(s.rotation.activation.copy(mode = SkinMode.ROTATE), d.state.operation!!.target)
        assertNull(d.state.rotation.pending); assertEquals(1L, d.state.operationHighWater)
        val complete = completion(d.state); val done = core.decide(d.state, complete).state
        assertEquals(complete.head, done.head); assertEquals(complete.head.activation, done.rotation.activation)
        assertNull(done.operation); assertEquals(1L, done.operationHighWater); unchanged(done, complete)
    }
    @Test fun offMissingSelectionAndSelectedHistoryNeverFabricateVisualTransactions() {
        val absent = state(SkinMode.OFF, null); assertEquals("NO_SELECTED_SKIN", advance(absent).diagnosis); unchanged(absent, RotationModeEvent.AdvanceMode)
        val selected = state(SkinMode.OFF); assertEquals("transaction-required", advance(selected).diagnosis); unchanged(selected, RotationModeEvent.AdvanceMode)
        val missing = state(SkinMode.OFF, "missing"); assertEquals("selected-object-unavailable", advance(missing).diagnosis); assertEquals(missing, advance(missing).state)
        unchanged(state(), death()); unchanged(state(), stable())
    }
    @Test fun freshVanillaOffPreservesRecordedPackReceiptSelectionEligibilityAndStamp() {
        val s = proof(state(SkinMode.ROTATE)); val issued = advance(s)
        assertTrue(issued.commands.single() is RotationModeCommand.CommitVerifiedVanillaOff)
        assertTrue(issued.state.offRequested); assertEquals(s.rotation.activation, issued.state.rotation.activation)
        val done = core.decide(issued.state, completion(issued.state)).state
        assertEquals(s.rotation.activation.copy(mode = SkinMode.OFF), done.rotation.activation)
        assertSame(s.rotation.ring, done.rotation.ring); assertFalse(done.offRequested)
        assertEquals("transaction-required", advance(done).diagnosis)
    }
    @Test fun receiptHeadAndFullClearMismatchesAreEntireStateNoopsThenCorrectable() {
        for (s in listOf(advance(state()).state, advance(proof(state(SkinMode.ROTATE))).state)) {
        val e = completion(s)
        val badHeads = listOf(e.head.copy(generationId = id(8)), e.head.copy(generationSha256 = hash('f')),
            e.head.copy(activation = e.head.activation.copy(skinStamp = 8)), e.head.copy(activation = e.head.activation.copy(selectedPackId = "b")),
            e.head.copy(activation = e.head.activation.copy(active = pack().copy(importReceiptSha256 = hash('f')))),
            e.head.copy(activation = e.head.activation.copy(mode = if (e.head.activation.mode == SkinMode.OFF) SkinMode.ON else SkinMode.OFF)),
            e.head.copy(interlock = RotationInterlock.clear().copy(transactionId = id(5))))
        for (h in badHeads) unchanged(s, e.copy(head = h))
        for (r in listOf(e.receipt.copy(expectedGenerationId = id(9)), e.receipt.copy(expectedGenerationSha256 = hash('f')),
            e.receipt.copy(newGenerationId = id(1)), e.receipt.copy(newGenerationSha256 = hash('d')), e.receipt.copy(newGenerationSha256 = "bad"))) unchanged(s, e.copy(receipt = r))
        unchanged(s, e.copy(correlation = e.correlation.copy(operationId = id(9))))
        unchanged(s, e.copy(correlation = e.correlation.copy(hero = HeroBindingToken("old"))))
        unchanged(s, e.copy(correlation = e.correlation.copy(skin = SkinBindingToken("old"))))
        assertEquals(s.operation!!.target, core.decide(s, e).state.rotation.activation)
        }
    }
    @Test fun missingOrPackProofCancelsBeforeRestorationDiagnosticAndNeverResurrects() {
        for (initial in listOf(state(SkinMode.ROTATE), state(SkinMode.ROTATE, active = ActiveVisual.Vanilla), proof(state(SkinMode.ROTATE), pack()))) {
            val pending = core.decide(initial, death(ULong.MAX_VALUE)).state
            val d = advance(pending); assertEquals("transaction-required", d.diagnosis); assertTrue(d.commands.isEmpty())
            assertTrue(d.state.offRequested); assertNull(d.state.rotation.pending); assertEquals(pending.rotation.pending, d.state.canceledPending)
            assertEquals(pending.rotation.activation, d.state.rotation.activation); assertEquals(DeathEpoch(ULong.MAX_VALUE), d.state.rotation.epochHighWater)
            unchanged(d.state, stable(ULong.MAX_VALUE)); unchanged(d.state, death())
            val corrected = advance(proof(d.state)); assertEquals(1, corrected.commands.size)
            assertEquals(SkinMode.OFF, core.decide(corrected.state, completion(corrected.state)).state.rotation.activation.mode)
        }
    }
    @Test fun alreadyIssuedReadinessStaysOwnedWhileOffIntentDisarmsIt() {
        val selected = core.decide(state(SkinMode.ROTATE), death()).state
        val ready = core.decide(selected, stable()).state; assertEquals(RotationPendingPhase.INTENT_ISSUED, ready.rotation.pending!!.phase)
        val off = advance(proof(ready)); assertEquals("issued-rotation-busy", off.diagnosis); assertTrue(off.commands.isEmpty())
        assertTrue(off.state.offRequested); assertEquals(ready.rotation.pending, off.state.rotation.pending); assertNull(off.state.canceledPending)
        unchanged(off.state, stable()); unchanged(off.state, death(2uL)); unchanged(off.state, RotationModeEvent.AdvanceMode)
    }
    @Test fun modeFailureAndUncertaintyRetainPublicationAndExactRetryAllowsLateCompletion() {
        for (failed in listOf(false, true)) {
            val issued = advance(proof(state(SkinMode.ROTATE))).state
            val signal = if (failed) RotationModeEvent.ModeFailed(issued.operation!!.correlation) else RotationModeEvent.ModeIndeterminate(issued.operation!!.correlation)
            val blocked = core.decide(issued, signal).state
            assertEquals(issued.head, blocked.head); assertEquals(issued.rotation.activation, blocked.rotation.activation); assertTrue(blocked.offRequested)
            unchanged(blocked, RotationModeEvent.AdvanceMode)
            val retry = core.decide(blocked, RotationModeEvent.RetryModeCommit(blocked.operation!!.correlation))
            assertEquals(blocked, retry.state); assertEquals(issued.operation.copy(phase = ModeCommitPhase.ISSUED), retry.commands.single().operation)
            assertNull(core.decide(blocked, completion(blocked)).state.operation)
        }
    }
    @Test fun heroOnlyRebindClearsProofAndParksIssuedCorrelationEvenAfterReturning() {
        val s = proof(state(SkinMode.ROTATE)); val issued = advance(s).state
        val rebind = RotationModeEvent.Selector(RotationEvent.Rebind(HeroBindingToken("hero2"), skin))
        val rebound = core.decide(issued, rebind).state
        assertNull(rebound.liveProof); assertTrue(rebound.operation!!.reboundBlocked); assertEquals(issued.operation!!.correlation, rebound.operation.correlation)
        unchanged(rebound, completion(issued)); unchanged(rebound, RotationModeEvent.RetryModeCommit(issued.operation.correlation))
        val back = core.decide(rebound, RotationModeEvent.Selector(RotationEvent.Rebind(hero, skin))).state
        unchanged(back, completion(issued)); assertTrue(core.isModeStateValid(back))
        val idle = core.decide(s, rebind).state; assertNull(idle.liveProof); assertEquals("transaction-required", advance(idle).diagnosis)
    }
    @Test fun rebindBeforeStabilityRetainsCandidateEpochButClearsProof() {
        val p = core.decide(proof(state(SkinMode.ROTATE)), death(7uL)).state
        val h = HeroBindingToken("hero2"); val k = SkinBindingToken("skin2")
        val next = core.decide(p, RotationModeEvent.Selector(RotationEvent.Rebind(h, k))).state
        assertNull(next.liveProof); assertEquals(p.rotation.pending!!.candidate, next.rotation.pending!!.candidate)
        assertEquals(p.rotation.epochHighWater, next.rotation.epochHighWater); unchanged(next, stable(7uL))
        val ready = core.decide(next, RotationModeEvent.Selector(RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(7uL), h, k)))).state
        assertEquals(h, ready.rotation.pending!!.issuedIntent!!.hero); assertTrue(core.isModeStateValid(ready))
    }
    @Test fun liveProofRequiresExactAuthorityButVisualEqualityExcludesReceipt() {
        val s = state(); assertFalse(core.isPriorEstablished(s))
        val proof = proof(s, pack().copy(importReceiptSha256 = hash('f'))); assertTrue(core.isPriorEstablished(proof))
        assertFalse(core.isPriorEstablished(proof(s, pack().copy(treeSha256 = hash('f')))))
        assertFalse(core.isPriorEstablished(proof(s))); assertEquals(s.head, proof.head)
        for (value in listOf(HeroVerifiedVisual(HeroBindingToken("old"), VerifiedLiveVisualProof.Vanilla(skin)),
            HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Vanilla(SkinBindingToken("old"))))) unchanged(s, RotationModeEvent.VerifiedVisual(value))
    }
    @Test fun inconsistentStateAndOverflowAreFailClosedAndQueriesValidateEverything() {
        val s = state(); val issued = advance(s).state
        for (bad in listOf(s.copy(head = s.head.copy(activation = s.head.activation.copy(skinStamp = 8))),
            s.copy(offRequested = true), s.copy(operationHighWater = -1), issued.copy(operationHighWater = 0),
            issued.copy(operation = issued.operation!!.copy(target = issued.operation.target.copy(skinStamp = 8))),
            issued.copy(operation = issued.operation.copy(vanillaProof = HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Vanilla(skin)))))) {
            assertFalse(core.isModeStateValid(bad)); assertFalse(core.isPriorEstablished(bad)); unchanged(bad, RotationModeEvent.AdvanceMode)
        }
        val max = s.copy(operationHighWater = Long.MAX_VALUE); assertEquals("operation-id-exhausted", advance(max).diagnosis); assertEquals(max, advance(max).state)
        val stamp = s.rotation.activation.copy(skinStamp = Long.MAX_VALUE)
        val maxStamp = s.copy(rotation = s.rotation.copy(activation = stamp), head = s.head.copy(activation = stamp))
        assertEquals(Long.MAX_VALUE, advance(maxStamp).state.operation!!.target.skinStamp)
        val done = core.decide(issued, completion(issued)).state; val off = advance(proof(done)).state
        assertEquals(2L, off.operationHighWater); assertNotEquals(issued.operation!!.correlation.operationId, off.operation!!.correlation.operationId)
        assertThrows(UnsupportedOperationException::class.java) { (advance(s).commands as MutableList).clear() }
    }
    @Test fun allNoncharactersRemainTotalInProofAuthority() {
        for (n in (0xFDD0..0xFDEF).toList() + (0..16).flatMap { listOf((it shl 16) + 0xFFFE, (it shl 16) + 0xFFFF) }) {
            val scalar = String(Character.toChars(n)); val h = HeroBindingToken("h$scalar"); val k = SkinBindingToken("s$scalar")
            val rebound = core.decide(state(SkinMode.ROTATE), RotationModeEvent.Selector(RotationEvent.Rebind(h, k))).state
            val proven = core.decide(rebound, RotationModeEvent.VerifiedVisual(HeroVerifiedVisual(h, VerifiedLiveVisualProof.Vanilla(k)))).state
            assertEquals(1, advance(proven).commands.size); assertTrue(core.isModeStateValid(proven))
        }
    }
    @Test fun forgedCurrentProofCannotContradictIssuedOffAuthority() {
        val issued = advance(proof(state(SkinMode.ROTATE))).state
        val forged = issued.copy(liveProof = HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Pack(skin, pack())))
        assertFalse(core.isModeStateValid(forged)); unchanged(forged, completion(issued))
        val missing = issued.copy(liveProof = null); assertFalse(core.isModeStateValid(missing)); unchanged(missing, completion(issued))
        val rebound = core.decide(issued, RotationModeEvent.Selector(RotationEvent.Rebind(HeroBindingToken("newhero"), skin))).state
        val forgedRebound = rebound.copy(liveProof = HeroVerifiedVisual(HeroBindingToken("newhero"), VerifiedLiveVisualProof.Vanilla(skin)))
        assertFalse(core.isModeStateValid(forgedRebound))
    }
    @Test fun advanceModeIsTheSolePayloadFreeModeRequest() {
        val events = runCatching { Class.forName("dev.silksong.launcher.skins.core.RotationModeEvent") }.getOrNull()
        assertNotNull(events)
        val advance = events!!.declaredClasses.singleOrNull { it.simpleName == "AdvanceMode" }
        assertNotNull(advance)
        assertTrue(advance!!.declaredFields.none { it.type == SkinMode::class.java })
        assertTrue(events.declaredClasses.none { type -> type.declaredFields.any { it.type == SkinMode::class.java } })
    }
}
