package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test

class SkinRotationRecoveryCoreTest {
    private val core = SkinRotationTransactionCore()
    private val hero = HeroBindingToken("hero")
    private val skin = SkinBindingToken("skin")
    private val authority = RotationRecoveryAuthority(HeroBindingToken("newhero"), SkinBindingToken("newskin"))
    private fun id(n: Int) = "00000000-0000-0000-0000-${n.toString(16).padStart(12, '0')}"
    private fun hash(c: Char) = c.toString().repeat(64)
    private fun pack(id: String = "a") = ActiveVisual.Pack(id, hash('a'), hash('b'), hash('c'))
    private fun state(mode: SkinMode = SkinMode.OFF, active: ActiveVisual = ActiveVisual.Vanilla): RotationTransactionState {
        val a = ActivationSnapshot(mode, "a", active, 7)
        val ring = requireNotNull(RotationRing.tryCreate(listOf(RotationDescriptor("A", pack(), true), RotationDescriptor("B", pack("b"), true))))
        return RotationTransactionState(RotationModeState(RotationState(ring, a, hero, skin), VerifiedRegistryHead(id(1), hash('d'), a, RotationInterlock.clear())))
    }
    private fun mode(s: RotationTransactionState, e: RotationModeEvent) = core.decide(s, RotationTransactionEvent.Mode(e)).state
    private fun advance(s: RotationTransactionState) = mode(s, RotationModeEvent.AdvanceMode)
    private fun selector(s: RotationTransactionState, e: RotationEvent) = mode(s, RotationModeEvent.Selector(e))
    private fun rebind(s: RotationTransactionState, a: RotationRecoveryAuthority = authority) = selector(s, RotationEvent.Rebind(a.hero, a.skin))
    private fun cor(s: RotationTransactionState) = s.operation!!.transaction.envelope!!.let { TransactionCorrelation(it.transactionId, it.binding) }
    private fun tx(s: RotationTransactionState, e: TransactionEvent) = core.decide(s, RotationTransactionEvent.Transaction(hero, e)).state
    private fun armed(s: RotationTransactionState): RotationTransactionState {
        val p = tx(s, TransactionEvent.Prepared(cor(s))); val e = p.operation!!.transaction.envelope!!
        val r = RegistryCommitReceipt(e.baseGenerationId, e.baseGenerationSha256, id(2), hash('e'))
        val l = RotationInterlock(InterlockState.ARMED, e.transactionId, e.operation, e.baseGenerationId, e.baseGenerationSha256, e.prior, e.target, e.binding, e.priorEstablishedOnBinding, null, null)
        return tx(p, TransactionEvent.ArmCommitted(cor(s), r, VerifiedRegistryHead(id(2), hash('e'), e.prior, l)))
    }
    private fun completion(s: RotationTransactionState): TransactionEvent.CompletionCommitted {
        val t = s.operation!!.transaction; val r = t.armCommitReceipt!!
        return TransactionEvent.CompletionCommitted(cor(s), RegistryCommitReceipt(r.newGenerationId, r.newGenerationSha256, id(3), hash('f')),
            VerifiedRegistryHead(id(3), hash('f'), t.pendingClosure!!, RotationInterlock.clear()))
    }
    private fun issued(): RotationTransactionState {
        val p = selector(state(SkinMode.ROTATE, pack()), RotationEvent.ConfirmDeath(DeathEpoch(4uL), hero, skin))
        return selector(p, RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(4uL), hero, skin)))
    }
    private fun recover(s: RotationTransactionState, e: RotationRecoveryEvidence): RotationTransactionState {
        val d = core.decide(s, RotationTransactionEvent.Recover(e)); assertTrue(d.commands.isEmpty()); return d.state
    }
    private fun noop(s: RotationTransactionState, e: RotationRecoveryEvidence) { assertEquals(s, recover(s, e)) }
    private fun done(s: RotationTransactionState, before: RotationTransactionState) {
        assertNull(s.operation); assertNull(s.mode.operation); assertNull(s.mode.rotation.pending)
        assertFalse(s.readinessReboundBlocked); assertNotNull(s.lastRecovery); assertTrue(core.isStateValid(s))
        assertEquals(before.mode.operationHighWater, s.mode.operationHighWater); assertEquals(before.mode.rotation.epochHighWater, s.mode.rotation.epochHighWater)
        assertEquals(before.copy(lastRecovery = null), s.lastRecovery!!.before)
    }

    @Test fun exactConsumedClosureRecoversOnCurrentAuthorityWithoutOldProofOrReplay() {
        val ready = issued(); val intent = ready.mode.rotation.pending!!.issuedIntent!!
        val a = armed(core.decide(ready, RotationTransactionEvent.ConsumeReady(intent)).state)
        val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))); val parked = rebind(applied)
        val e = RotationRecoveryEvidence.VisualClosure(parked.operation!!, completion(applied), authority)
        val result = recover(parked, e); done(result, parked)
        assertEquals(pack("b"), result.mode.rotation.activation.active); assertEquals("b", result.mode.rotation.activation.selectedPackId)
        assertNull(result.mode.liveProof); assertEquals(TransactionPhase.APPLIED, result.lastRecovery!!.before.operation!!.transaction.phase)
        noop(result, e); assertEquals(result, core.decide(result, RotationTransactionEvent.ConsumeReady(intent)).state)
        val next = selector(result, RotationEvent.ConfirmDeath(DeathEpoch(5uL), authority.hero, authority.skin))
        assertNotNull(next.mode.rotation.pending); assertEquals(DeathEpoch(5uL), next.mode.rotation.epochHighWater)
    }
    @Test fun wrongClosureCorrelationHeadTargetEpochAndCurrentBindingRemainCorrectable() {
        val ready = issued(); val a = armed(core.decide(ready, RotationTransactionEvent.ConsumeReady(ready.mode.rotation.pending!!.issuedIntent!!)).state)
        val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))); val s = rebind(applied)
        val e = RotationRecoveryEvidence.VisualClosure(s.operation!!, completion(applied), authority)
        for (bad in listOf(e.copy(authority = authority.copy(hero = hero)), e.copy(authority = authority.copy(skin = skin)),
            e.copy(operation = e.operation.copy(readiness = e.operation.readiness!!.copy(epoch = DeathEpoch(8uL)))),
            e.copy(operation = e.operation.copy(origin = e.operation.origin.copy(head = e.operation.origin.head.copy(generationSha256 = hash('0'))))),
            e.copy(completion = e.completion.copy(correlation = e.completion.correlation.copy(binding = authority.skin))),
            e.copy(completion = e.completion.copy(commitReceipt = e.completion.commitReceipt.copy(expectedGenerationId = id(9)))),
            e.copy(completion = e.completion.copy(verifiedHead = e.completion.verifiedHead.copy(activation = e.completion.verifiedHead.activation.copy(selectedPackId = "a")))),
            e.copy(completion = e.completion.copy(verifiedHead = e.completion.verifiedHead.copy(interlock = RotationInterlock.clear().copy(transactionId = id(9))))),
            e.copy(authority = authority.copy(liveProof = HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Pack(skin, pack("b"))))))) noop(s, bad)
        val proof = HeroVerifiedVisual(authority.hero, VerifiedLiveVisualProof.Pack(authority.skin, pack("b")))
        val result = recover(s, e.copy(authority = authority.copy(liveProof = proof))); done(result, s); assertEquals(proof, result.mode.liveProof)
    }
    @Test fun rolledBackDurablePriorClosureRecoversWithoutSpeculativeCommands() {
        val a = armed(advance(state(active = pack())))
        val failed = tx(a, TransactionEvent.ApplyFailed(cor(a), "FAILED"))
        val rolled = tx(failed, TransactionEvent.RollbackVerified(cor(a), true)); val s = rebind(rolled)
        val result = recover(s, RotationRecoveryEvidence.VisualClosure(s.operation!!, completion(rolled), authority)); done(result, s)
        assertEquals(SkinMode.OFF, result.mode.rotation.activation.mode); assertEquals(pack(), result.mode.rotation.activation.active)
        assertEquals(8L, result.mode.rotation.activation.skinStamp); assertNull(result.mode.liveProof)
    }
    @Test fun preparingAndPreparedRequireExactFencedNonexecutionAndUnchangedBase() {
        val begin = advance(state()); val prepared = tx(begin, TransactionEvent.Prepared(cor(begin)))
        for (p in listOf(begin, prepared)) {
            val s = rebind(p); val e = RotationRecoveryEvidence.VisualFencedNotExecuted(s.operation!!, s.mode.head, authority = authority)
            noop(s, e.copy(head = e.head.copy(generationId = id(8))))
            noop(s, e.copy(receipt = RegistryCommitReceipt(id(1), hash('d'), id(3), hash('f'))))
            val result = recover(s, e); done(result, s); assertEquals(s.mode.head, result.mode.head)
            assertEquals(p.operation!!.transaction.phase, result.lastRecovery!!.before.operation!!.transaction.phase)
            val retry = advance(result); assertNotNull(retry.operation); assertEquals(2L, retry.mode.operationHighWater)
            assertNotEquals(p.operation.transaction.envelope!!.transactionId, retry.operation!!.transaction.envelope!!.transactionId)
        }
    }
    @Test fun armedAbortRequiresExactArmChildPriorClosureAndFencing() {
        for (active in listOf(ActiveVisual.Vanilla, pack())) {
            val s = rebind(armed(advance(state(active = active)))); val t = s.operation!!.transaction; val e = t.envelope!!; val arm = t.armCommitReceipt!!
            val prior = if (active is ActiveVisual.Pack) e.prior.copy(skinStamp = e.prior.skinStamp + 1) else e.prior
            val receipt = RegistryCommitReceipt(arm.newGenerationId, arm.newGenerationSha256, id(3), hash('f'))
            val evidence = RotationRecoveryEvidence.VisualFencedNotExecuted(s.operation, VerifiedRegistryHead(id(3), hash('f'), prior, RotationInterlock.clear()), receipt, authority)
            noop(s, evidence.copy(head = s.mode.head, receipt = null)); noop(s, evidence.copy(receipt = receipt.copy(expectedGenerationId = id(1))))
            noop(s, evidence.copy(head = evidence.head.copy(activation = e.target)))
            noop(s, evidence.copy(head = evidence.head.copy(interlock = t.interlock)))
            val result = recover(s, evidence); done(result, s); assertEquals(prior, result.mode.rotation.activation)
            assertEquals(TransactionPhase.ARMED, result.lastRecovery!!.before.operation!!.transaction.phase)
        }
    }
    @Test fun unconsumedRetirementNeverRetargetsOrReissuesAndNewDeathIsReachable() {
        val p = issued(); val intent = p.mode.rotation.pending!!.issuedIntent!!; val s = rebind(p)
        val e = RotationRecoveryEvidence.ReadinessFencedNotExecuted(intent, p.mode.head, authority)
        noop(s, e.copy(intent = intent.copy(epoch = DeathEpoch(3uL)))); noop(s, e.copy(intent = intent.copy(hero = authority.hero)))
        noop(s, e.copy(intent = intent.copy(candidate = intent.candidate.copy(currentObject = pack()))))
        noop(s, e.copy(head = e.head.copy(generationSha256 = hash('0'))))
        val result = recover(s, e); done(result, s); noop(result, e)
        assertEquals(result, core.decide(result, RotationTransactionEvent.ConsumeReady(intent)).state)
        assertEquals(result, selector(result, RotationEvent.ConfirmDeath(intent.epoch, authority.hero, authority.skin)))
        val next = selector(result, RotationEvent.ConfirmDeath(DeathEpoch(5uL), authority.hero, authority.skin))
        val stable = selector(next, RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(5uL), authority.hero, authority.skin)))
        assertEquals(DeathEpoch(5uL), stable.mode.rotation.pending!!.issuedIntent!!.epoch)
        assertEquals(1, core.decide(stable, RotationTransactionEvent.ConsumeReady(stable.mode.rotation.pending.issuedIntent!!)).commands.size)
    }
    @Test fun sameBindingReturnAndVisualOnlyProofNeverResolveButExactEvidenceDoes() {
        val p = issued(); val backAuthority = RotationRecoveryAuthority(hero, skin)
        val s = rebind(rebind(p), backAuthority); val intent = p.mode.rotation.pending!!.issuedIntent!!
        val proven = mode(s, RotationModeEvent.VerifiedVisual(HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Pack(skin, pack()))))
        assertTrue(proven.readinessReboundBlocked); assertEquals(proven, core.decide(proven, RotationTransactionEvent.ConsumeReady(intent)).state)
        done(recover(proven, RotationRecoveryEvidence.ReadinessFencedNotExecuted(intent, p.mode.head, backAuthority)), proven)
    }
    @Test fun h1ModeClosureUsesExactOriginalCasAndCurrentVanillaAuthorityForOff() {
        for (off in listOf(false, true)) {
            var initial = state(if (off) SkinMode.ROTATE else SkinMode.ON, pack())
            if (off) initial = mode(initial, RotationModeEvent.VerifiedVisual(HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Vanilla(skin))))
            val begun = advance(initial); val s = rebind(begun); val p = s.mode.operation!!
            val complete = RotationModeEvent.ModeCommitted(p.correlation, RegistryCommitReceipt(p.baseHead.generationId, p.baseHead.generationSha256, id(3), hash('f')),
                VerifiedRegistryHead(id(3), hash('f'), p.target, RotationInterlock.clear()))
            val current = if (off) authority.copy(liveProof = HeroVerifiedVisual(authority.hero, VerifiedLiveVisualProof.Vanilla(authority.skin))) else authority
            val e = RotationRecoveryEvidence.ModeClosure(p, complete, current)
            noop(s, e.copy(operation = p.copy(correlation = p.correlation.copy(hero = authority.hero))))
            noop(s, e.copy(completion = complete.copy(receipt = complete.receipt.copy(expectedGenerationId = id(8)))))
            noop(s, e.copy(completion = complete.copy(head = complete.head.copy(activation = initial.mode.rotation.activation))))
            if (off) { noop(s, e.copy(authority = authority)); noop(s, e.copy(authority = authority.copy(liveProof = HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Vanilla(skin))))) }
            val result = recover(s, e); done(result, s); assertEquals(p.target, result.mode.rotation.activation); assertEquals(current.liveProof, result.mode.liveProof); noop(result, e)
        }
    }
    @Test fun consumedRecoveryKeepsDisarmAndReachesFreshModeOffTransaction() {
        val ready = issued(); val a = armed(core.decide(ready, RotationTransactionEvent.ConsumeReady(ready.mode.rotation.pending!!.issuedIntent!!)).state)
        val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))); val s = advance(rebind(applied))
        val result = recover(s, RotationRecoveryEvidence.VisualClosure(s.operation!!, completion(applied), authority)); done(result, s)
        assertTrue(result.mode.offRequested); val off = advance(result)
        assertEquals(SkinOperationKind.MODE_OFF, off.operation!!.transaction.envelope!!.operation)
        assertEquals(authority.hero, off.operation.origin.rotation.currentHero)
        assertEquals(result.mode.rotation.activation.selectedPackId, off.operation.transaction.envelope!!.target.selectedPackId)
    }
    @Test fun visualModeOffClosureRetainsSelectionAndClearsDisarm() {
        val a = armed(advance(state(SkinMode.ROTATE, pack()))); val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))); val s = rebind(applied)
        val result = recover(s, RotationRecoveryEvidence.VisualClosure(s.operation!!, completion(applied), authority)); done(result, s)
        assertEquals(SkinMode.OFF, result.mode.rotation.activation.mode); assertEquals(ActiveVisual.Vanilla, result.mode.rotation.activation.active)
        assertEquals("a", result.mode.rotation.activation.selectedPackId); assertFalse(result.mode.offRequested); assertNull(result.mode.liveProof)
    }
    @Test fun lastAuditSurvivesNormalExecutionAndNextRecoveryReplacesRatherThanNestsIt() {
        val a = RotationRecoveryAuthority(hero, skin)
        val parked = rebind(rebind(advance(state())), a)
        val recovered = recover(parked, RotationRecoveryEvidence.VisualFencedNotExecuted(parked.operation!!, parked.mode.head, authority = a))
        val begun = advance(recovered); assertEquals(recovered.lastRecovery, begun.lastRecovery)
        val armed = armed(begun); val applied = tx(armed, TransactionEvent.ApplyVerified(cor(armed)))
        val committed = tx(applied, completion(applied)); assertEquals(recovered.lastRecovery, committed.lastRecovery); assertTrue(core.isStateValid(committed))
        val second = rebind(begun)
        val done = recover(second, RotationRecoveryEvidence.VisualFencedNotExecuted(second.operation!!, second.mode.head, authority = authority))
        assertNotEquals(recovered.lastRecovery, done.lastRecovery); assertNull(done.lastRecovery!!.before.lastRecovery); assertTrue(core.isStateValid(done))
    }
    @Test fun forgedNestedOrUnqualifiedAuditAndRegressedHighwaterAreInvalid() {
        val s = rebind(advance(state())); val e = RotationRecoveryEvidence.VisualFencedNotExecuted(s.operation!!, s.mode.head, authority = authority)
        val result = recover(s, e); val record = result.lastRecovery!!
        for (bad in listOf(result.copy(lastRecovery = record.copy(before = record.before.copy(lastRecovery = record))),
            result.copy(lastRecovery = record.copy(evidence = e.copy(authority = authority.copy(hero = hero)))),
            result.copy(mode = result.mode.copy(operationHighWater = 0)))) {
            assertFalse(core.isStateValid(bad)); assertEquals(bad, advance(bad))
        }
    }
    @Test fun blockedAndRollbackPendingRemainTask68WithoutConsumingFutureEvidence() {
        val a = armed(advance(state())); val applied = tx(a, TransactionEvent.ApplyVerified(cor(a))); val complete = completion(applied)
        for (p in listOf(tx(applied, TransactionEvent.CompletionIndeterminate(cor(a))), tx(applied, TransactionEvent.CompletionRejected(cor(a), "REJECTED")))) {
            val s = rebind(p)
            noop(s, RotationRecoveryEvidence.VisualClosure(s.operation!!, complete, authority))
            noop(s, RotationRecoveryEvidence.VisualFencedNotExecuted(s.operation, s.mode.head, authority = authority))
            assertNotNull(s.operation); assertNull(s.lastRecovery)
        }
    }
}
