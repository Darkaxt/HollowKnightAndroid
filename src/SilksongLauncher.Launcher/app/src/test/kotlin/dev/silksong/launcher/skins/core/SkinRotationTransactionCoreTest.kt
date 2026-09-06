package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test

class SkinRotationTransactionCoreTest {
    private val core = SkinRotationTransactionCore()
    private val hero = HeroBindingToken("hero")
    private val skin = SkinBindingToken("skin")
    private fun id(n: Int) = "00000000-0000-0000-0000-${n.toString(16).padStart(12, '0')}"
    private fun hash(c: Char) = c.toString().repeat(64)
    private fun pack(id: String = "a") = ActiveVisual.Pack(id, hash('a'), hash('b'), hash('c'))
    private fun state(mode: SkinMode = SkinMode.OFF, active: ActiveVisual = ActiveVisual.Vanilla): RotationTransactionState {
        val a = ActivationSnapshot(mode, "a", active, 7)
        val ring = requireNotNull(RotationRing.tryCreate(listOf(RotationDescriptor("A", pack(), false),
            RotationDescriptor("B", pack("b"), true), RotationDescriptor("C", pack("c"), true))))
        return RotationTransactionState(RotationModeState(RotationState(ring, a, hero, skin), VerifiedRegistryHead(id(1), hash('d'), a, RotationInterlock.clear())))
    }
    private fun advance(s: RotationTransactionState) = core.decide(s, RotationTransactionEvent.Mode(RotationModeEvent.AdvanceMode))
    private fun cor(s: RotationTransactionState): TransactionCorrelation {
        val e = requireNotNull(s.operation).transaction.envelope!!
        return TransactionCorrelation(e.transactionId, e.binding)
    }
    private fun tx(s: RotationTransactionState, e: TransactionEvent, h: HeroBindingToken = hero) = core.decide(s, RotationTransactionEvent.Transaction(h, e))
    private fun armed(s: RotationTransactionState): RotationTransactionState {
        val p = tx(s, TransactionEvent.Prepared(cor(s))).state
        val e = p.operation!!.transaction.envelope!!
        val n = 100 + s.mode.operationHighWater.toInt() * 2
        val r = RegistryCommitReceipt(e.baseGenerationId, e.baseGenerationSha256, id(n), n.toString(16).padStart(64, '0'))
        val l = RotationInterlock(InterlockState.ARMED, e.transactionId, e.operation, e.baseGenerationId, e.baseGenerationSha256,
            e.prior, e.target, e.binding, e.priorEstablishedOnBinding, null, null)
        return tx(p, TransactionEvent.ArmCommitted(cor(p), r, VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, e.prior, l))).state
    }
    private fun completion(s: RotationTransactionState): RotationTransactionEvent.Transaction {
        val t = s.operation!!.transaction; val arm = t.armCommitReceipt!!
        val n = 101 + s.mode.operationHighWater.toInt() * 2
        val r = RegistryCommitReceipt(arm.newGenerationId, arm.newGenerationSha256, id(n), n.toString(16).padStart(64, '0'))
        return RotationTransactionEvent.Transaction(hero, TransactionEvent.CompletionCommitted(cor(s), r,
            VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, t.pendingClosure!!, RotationInterlock.clear())))
    }
    private fun complete(s: RotationTransactionState) = core.decide(s, completion(s)).state
    private fun noop(s: RotationTransactionState, e: RotationTransactionEvent) {
        val d = core.decide(s, e); assertEquals(s, d.state); assertTrue(d.commands.isEmpty())
    }
    private fun selector(s: RotationTransactionState, e: RotationEvent) = core.decide(s, RotationTransactionEvent.Mode(RotationModeEvent.Selector(e))).state

    @Test fun modeOnUsesCurrentSelectedObjectAndOriginalHeroEnvelope() {
        val s = state(active = pack().copy(treeSha256 = hash('9'))); val d = advance(s)
        val command = d.commands.single() as RotationTransactionCommand.Transaction
        assertEquals(hero, command.hero)
        val e = (command.command as SkinCommand.Prepare).envelope
        assertEquals(pack(), e.target.active); assertEquals(SkinOperationKind.MODE_ON, e.operation)
        assertFalse(e.priorEstablishedOnBinding); assertEquals(s.mode.head, d.state.mode.head); assertEquals(1L, d.state.mode.operationHighWater)
        assertEquals(SkinCommand.Arm(e), (tx(d.state, TransactionEvent.Prepared(cor(d.state))).commands.single() as RotationTransactionCommand.Transaction).command)
    }
    @Test fun onlyExactDurableClosurePublishesAndVisualSuccessDoesNot() {
        val initial = state(); val a = armed(advance(initial).state)
        val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))).state
        assertEquals(initial.mode.head, applied.mode.head); assertNotNull(applied.operation)
        val e = completion(applied); val c = e.event as TransactionEvent.CompletionCommitted
        noop(applied, e.copy(hero = HeroBindingToken("other")))
        noop(applied, e.copy(event = c.copy(verifiedHead = c.verifiedHead.copy(activation = c.verifiedHead.activation.copy(selectedPackId = "b")))))
        val done = complete(applied); assertNull(done.operation); assertEquals(pack(), done.mode.rotation.activation.active)
        assertEquals(SkinMode.ON, done.mode.rotation.activation.mode); assertEquals(8L, done.mode.rotation.activation.skinStamp)
        assertNull(done.mode.liveProof); noop(done, e)
    }
    @Test fun offCancelsOnlyUnissuedPendingAndRestoresVanillaKeepingSelection() {
        val s = selector(state(SkinMode.ROTATE, pack()), RotationEvent.ConfirmDeath(DeathEpoch(9uL), hero, skin))
        val d = advance(s); assertNull(d.state.mode.rotation.pending); assertNotNull(d.state.mode.canceledPending)
        val e = d.state.operation!!.transaction.envelope!!
        assertEquals(SkinOperationKind.MODE_OFF, e.operation); assertEquals(ActiveVisual.Vanilla, e.target.active); assertEquals("a", e.target.selectedPackId)
        val a = armed(d.state); val done = complete(tx(a, TransactionEvent.ApplyVerified(cor(a))).state)
        assertEquals(SkinMode.OFF, done.mode.rotation.activation.mode); assertFalse(done.mode.offRequested)
        assertNull(done.mode.canceledPending); assertEquals(DeathEpoch(9uL), done.mode.rotation.epochHighWater)
    }
    @Test fun exactIssuedReadinessChangesBothSelectedAndActiveAfterClosureOnly() {
        val p = selector(state(SkinMode.ROTATE, pack()), RotationEvent.ConfirmDeath(DeathEpoch(4uL), hero, skin))
        val s = selector(p, RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(4uL), hero, skin)))
        val intent = s.mode.rotation.pending!!.issuedIntent!!
        noop(s, RotationTransactionEvent.ConsumeReady(intent.copy(candidate = intent.candidate.copy(currentObject = pack("c")))))
        val begun = core.decide(s, RotationTransactionEvent.ConsumeReady(intent)).state
        assertEquals(intent, begun.operation!!.readiness); assertEquals(intent.candidate.currentObject, begun.operation.transaction.envelope!!.target.active)
        noop(begun, RotationTransactionEvent.ConsumeReady(intent))
        val a = armed(begun); val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))).state
        assertNotNull(applied.mode.rotation.pending)
        val done = complete(applied); assertNull(done.mode.rotation.pending); assertEquals("b", done.mode.rotation.activation.selectedPackId)
        assertEquals(pack("b"), done.mode.rotation.activation.active); assertEquals(DeathEpoch(4uL), done.mode.rotation.epochHighWater)
        noop(done, RotationTransactionEvent.ConsumeReady(intent))
    }
    @Test fun rejectedTargetDelegatesRollbackAndDurableRollbackClosure() {
        val a = armed(advance(state()).state); val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))).state
        val rollback = tx(applied, TransactionEvent.CompletionRejected(cor(a), "CAS_REJECTED"))
        assertTrue((rollback.commands.single() as RotationTransactionCommand.Transaction).command is SkinCommand.Rollback)
        val rolled = tx(rollback.state, TransactionEvent.RollbackVerified(cor(a), true)).state
        assertNotNull(rolled.operation); assertEquals(state().mode.head, rolled.mode.head)
        val done = complete(rolled); assertNull(done.operation); assertEquals(SkinMode.OFF, done.mode.rotation.activation.mode)
    }
    @Test fun reboundOriginIsRetainedAndOldCompletionCannotPublishAfterReturning() {
        val a = armed(advance(state()).state); val s = tx(a, TransactionEvent.ApplyVerified(cor(a))).state; val c = completion(s)
        val rebound = selector(s, RotationEvent.Rebind(HeroBindingToken("newhero"), skin))
        assertEquals(s.operation!!.origin, rebound.operation!!.origin); assertTrue(rebound.operation.reboundBlocked); noop(rebound, c)
        val back = selector(rebound, RotationEvent.Rebind(hero, skin)); assertTrue(back.operation!!.reboundBlocked); noop(back, c)
    }
    @Test fun indeterminateRemainsTerminalWithoutSpeculativeRollbackOrReset() {
        val a = armed(advance(state()).state); val s = tx(a, TransactionEvent.ApplyVerified(cor(a))).state; val c = completion(s)
        val blocked = tx(s, TransactionEvent.CompletionIndeterminate(cor(s))).state
        assertEquals(TransactionPhase.BLOCKED, blocked.operation!!.transaction.phase); noop(blocked, c); assertEquals(blocked, advance(blocked).state)
    }
    @Test fun establishedVisualNoopUsesModeCasWhileTransactionCoreStillVetoesVisualExecution() {
        val s = state(active = pack()); val proven = s.copy(mode = s.mode.copy(liveProof = HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Pack(skin, pack()))))
        val a = s.mode.rotation.activation
        val envelope = TransactionEnvelope(id(9), SkinOperationKind.MODE_ON, s.mode.head.generationId, s.mode.head.generationSha256,
            a, a.copy(mode = SkinMode.ON, skinStamp = a.skinStamp + 1), skin, true)
        val veto = SkinTransactionCore().decide(TransactionState(binding = skin, activation = a), TransactionEvent.Begin(envelope))
        assertEquals("invalid-envelope", veto.diagnosis); assertTrue(veto.commands.isEmpty())
        val d = advance(proven); assertEquals("commit-mode", d.diagnosis); assertNull(d.state.operation)
        assertTrue(d.commands.single() is RotationTransactionCommand.Mode)
        assertEquals(a.copy(mode = SkinMode.ON), d.state.mode.operation!!.target)
    }
    @Test fun issuedReadinessCannotBecomeFreshByReturningToOriginalBinding() {
        val p = selector(state(SkinMode.ROTATE, pack()), RotationEvent.ConfirmDeath(DeathEpoch(4uL), hero, skin))
        val s = selector(p, RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(4uL), hero, skin)))
        val intent = s.mode.rotation.pending!!.issuedIntent!!
        val rebound = selector(s, RotationEvent.Rebind(HeroBindingToken("other"), skin))
        val back = selector(rebound, RotationEvent.Rebind(hero, skin))
        assertEquals(intent, back.mode.rotation.pending!!.issuedIntent)
        noop(back, RotationTransactionEvent.ConsumeReady(intent))
        assertEquals("task67-readiness-rebound-resolution-required", core.decide(back, RotationTransactionEvent.ConsumeReady(intent)).diagnosis)
        val disarmed = advance(back).state; assertTrue(disarmed.readinessReboundBlocked); assertTrue(disarmed.mode.offRequested)
        noop(disarmed, RotationTransactionEvent.ConsumeReady(intent)); assertFalse(core.isStateValid(state().copy(readinessReboundBlocked = true)))
    }
    @Test fun reboundIssuedReadinessRequiresStickyFlagInPublicState() {
        val p = selector(state(SkinMode.ROTATE, pack()), RotationEvent.ConfirmDeath(DeathEpoch(4uL), hero, skin))
        val s = selector(p, RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(4uL), hero, skin)))
        for (binding in listOf(RotationEvent.Rebind(HeroBindingToken("otherhero"), skin),
            RotationEvent.Rebind(hero, SkinBindingToken("otherskin")), RotationEvent.Rebind(HeroBindingToken("otherhero"), SkinBindingToken("otherskin")))) {
            val rebound = selector(s, binding)
            assertTrue(core.isStateValid(rebound)); assertTrue(rebound.readinessReboundBlocked)
            val forged = rebound.copy(readinessReboundBlocked = false)
            assertFalse(core.isStateValid(forged))
            noop(forged, RotationTransactionEvent.ConsumeReady(forged.mode.rotation.pending!!.issuedIntent!!))
            assertEquals("invalid-state", advance(forged).diagnosis)
        }
        val consumed = core.decide(s, RotationTransactionEvent.ConsumeReady(s.mode.rotation.pending!!.issuedIntent!!)).state
        val inflight = selector(consumed, RotationEvent.Rebind(HeroBindingToken("otherhero"), skin))
        assertTrue(core.isStateValid(inflight)); assertTrue(inflight.operation!!.reboundBlocked); assertFalse(inflight.readinessReboundBlocked)
    }
    @Test fun preStableRebindRetokensWithoutBlockingFirstReadiness() {
        val p = selector(state(SkinMode.ROTATE, pack()), RotationEvent.ConfirmDeath(DeathEpoch(4uL), hero, skin))
        val h = HeroBindingToken("newhero"); val k = SkinBindingToken("newskin")
        val rebound = selector(p, RotationEvent.Rebind(h, k)); assertFalse(rebound.readinessReboundBlocked)
        assertEquals(p.mode.rotation.pending!!.candidate, rebound.mode.rotation.pending!!.candidate)
        val s = selector(rebound, RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(4uL), h, k)))
        val ready = s.mode.rotation.pending!!.issuedIntent!!; assertEquals(h, ready.hero); assertEquals(k, ready.skin)
        val d = core.decide(s, RotationTransactionEvent.ConsumeReady(ready)); assertEquals(1, d.commands.size); assertTrue(core.isStateValid(d.state))
        assertEquals(h, (d.commands.single() as RotationTransactionCommand.Transaction).hero)
    }
    @Test fun sharedHighwaterSurvivesTwoModeAndVisualCyclesWithoutReuse() {
        var s = state(); val ids = mutableSetOf<String>()
        repeat(2) { cycle ->
            s = advance(s).state; assertTrue(ids.add(s.operation!!.transaction.envelope!!.transactionId))
            var a = armed(s); s = complete(tx(a, TransactionEvent.ApplyVerified(cor(a))).state)
            s = advance(s).state; val p = s.mode.operation!!; assertTrue(ids.add(p.correlation.operationId))
            val n = 1000 + s.mode.operationHighWater.toInt(); val digest = n.toString(16).padStart(64, '0')
            s = core.decide(s, RotationTransactionEvent.Mode(RotationModeEvent.ModeCommitted(p.correlation,
                RegistryCommitReceipt(p.baseHead.generationId, p.baseHead.generationSha256, id(n), digest),
                VerifiedRegistryHead(id(n), digest, p.target, RotationInterlock.clear())))).state
            s = advance(s).state; assertTrue(ids.add(s.operation!!.transaction.envelope!!.transactionId))
            a = armed(s); s = complete(tx(a, TransactionEvent.ApplyVerified(cor(a))).state)
            assertEquals(SkinMode.OFF, s.mode.rotation.activation.mode); assertEquals((cycle + 1) * 3L, s.mode.operationHighWater)
        }
        assertEquals(6, ids.size); assertEquals(11L, s.mode.rotation.activation.skinStamp)
    }
    @Test fun forgedCommittedAndInconsistentOperationStatesCannotPublish() {
        val a = armed(advance(state()).state); val s = tx(a, TransactionEvent.ApplyVerified(cor(a))).state; val c = completion(s)
        val terminal = SkinTransactionCore().decide(s.operation!!.transaction, c.event).state
        assertTrue(SkinTransactionCore().isStateValid(terminal))
        val forged = s.copy(operation = s.operation.copy(transaction = terminal))
        assertFalse(core.isStateValid(forged)); noop(forged, c)
        for (bad in listOf(s.copy(mode = s.mode.copy(operationHighWater = 0)),
            s.copy(mode = s.mode.copy(head = s.mode.head.copy(generationSha256 = hash('9')))),
            s.copy(operation = s.operation.copy(transaction = s.operation.transaction.copy(envelope = null))))) {
            assertFalse(core.isStateValid(bad)); noop(bad, c)
        }
        assertFalse(SkinTransactionCore().isStateValid(s.operation.transaction.copy(binding = null)))
    }
    @Test fun operationCannotLoseCurrentAuthoritativeBinding() {
        val s = advance(state()).state
        val forged = s.copy(mode = s.mode.copy(rotation = s.mode.rotation.copy(currentHero = null, currentSkin = null)),
            operation = s.operation!!.copy(reboundBlocked = true))
        assertFalse(core.isStateValid(forged)); noop(forged, RotationTransactionEvent.Mode(RotationModeEvent.AdvanceMode))
    }
    @Test fun exactArmAndClosureReceiptHeadMismatchesRemainCorrectable() {
        val s = advance(state()).state; val p = tx(s, TransactionEvent.Prepared(cor(s))).state; val e = p.operation!!.transaction.envelope!!
        val r = RegistryCommitReceipt(e.baseGenerationId, e.baseGenerationSha256, id(20), hash('e'))
        val l = RotationInterlock(InterlockState.ARMED, e.transactionId, e.operation, e.baseGenerationId, e.baseGenerationSha256,
            e.prior, e.target, e.binding, e.priorEstablishedOnBinding, null, null)
        val h = VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, e.prior, l)
        val arm = TransactionEvent.ArmCommitted(cor(s), r, h)
        for (bad in listOf(arm.copy(commitReceipt = r.copy(expectedGenerationSha256 = hash('0'))),
            arm.copy(verifiedHead = h.copy(interlock = l.copy(target = e.prior))), arm.copy(verifiedHead = h.copy(activation = e.target)),
            arm.copy(correlation = cor(s).copy(binding = SkinBindingToken("stale"))))) noop(p, RotationTransactionEvent.Transaction(hero, bad))
        noop(p, RotationTransactionEvent.Transaction(HeroBindingToken("other"), arm))
        val applied = tx(tx(p, arm).state, TransactionEvent.ApplyVerified(cor(s))).state
        val complete = completion(applied); val c = complete.event as TransactionEvent.CompletionCommitted
        for (bad in listOf(c.copy(commitReceipt = c.commitReceipt.copy(expectedGenerationId = id(88))),
            c.copy(verifiedHead = c.verifiedHead.copy(interlock = RotationInterlock.clear().copy(transactionId = id(9)))),
            c.copy(verifiedHead = c.verifiedHead.copy(activation = c.verifiedHead.activation.copy(active = pack().copy(importReceiptSha256 = hash('8'))))))) noop(applied, complete.copy(event = bad))
        assertNull(complete(applied).operation)
    }
    @Test fun allNoncharactersAndOldTokenParityControlsPassThroughComposition() {
        val scalarTokens = ((0xFDD0..0xFDEF).toList() + (0..16).flatMap { listOf((it shl 16) + 0xFFFE, (it shl 16) + 0xFFFF) }).map { "skin" + String(Character.toChars(it)) }
        for (text in scalarTokens + listOf("skin/hero:1", "é", "😀".repeat(256), "x".repeat(256))) {
            val base = state(); val s = base.copy(mode = base.mode.copy(rotation = base.mode.rotation.copy(currentSkin = SkinBindingToken(text))))
            val d = advance(s); assertEquals(1, d.commands.size); assertTrue(SkinTransactionCore().isStateValid(d.state.operation!!.transaction))
        }
        for (text in listOf("", " x", "x ", " x", "x ", "a‮b", "a${0.toChar()}b", "\ud800", "\udc00", "Ａ", "é", "x".repeat(257), "😀".repeat(257), "x￾Ａ")) {
            val base = state(); val s = base.copy(mode = base.mode.copy(rotation = base.mode.rotation.copy(currentSkin = SkinBindingToken(text))))
            assertFalse(core.isStateValid(s)); assertFalse(SkinTransactionCore().isStateValid(TransactionState(binding = SkinBindingToken(text), activation = s.mode.rotation.activation)))
            assertTrue(advance(s).commands.isEmpty())
        }
    }
    @Test fun exhaustionNeverIssuesOrOverflowsAndCommandsAreImmutable() {
        val s = state(); val max = s.copy(mode = s.mode.copy(operationHighWater = Long.MAX_VALUE))
        assertEquals("operation-id-exhausted", advance(max).diagnosis); assertEquals(max, advance(max).state)
        val a = s.mode.rotation.activation.copy(skinStamp = Long.MAX_VALUE)
        val stamp = s.copy(mode = s.mode.copy(rotation = s.mode.rotation.copy(activation = a), head = s.mode.head.copy(activation = a)))
        assertEquals("skin-stamp-exhausted", advance(stamp).diagnosis); assertEquals(stamp, advance(stamp).state)
        assertThrows(UnsupportedOperationException::class.java) { (advance(s).commands as MutableList).clear() }
    }
    @Test fun compositionEntryPointExists() {
        assertNotNull(runCatching { Class.forName("dev.silksong.launcher.skins.core.SkinRotationTransactionCore") }.getOrNull())
    }
}
