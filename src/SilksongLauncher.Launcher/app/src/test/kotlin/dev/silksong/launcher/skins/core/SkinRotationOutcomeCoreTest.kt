package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test

class SkinRotationOutcomeCoreTest {
    private val core = SkinRotationTransactionCore()
    private val hero = HeroBindingToken("hero")
    private val skin = SkinBindingToken("skin")
    private fun id(n: Int) = "00000000-0000-0000-0000-${n.toString(16).padStart(12, '0')}"
    private fun hash(n: Int) = n.toString(16).padStart(64, '0')
    private fun pack(id: String = "a", receipt: Int = 3) = ActiveVisual.Pack(id, hash(1), hash(2), hash(receipt))
    private fun state(mode: SkinMode = SkinMode.OFF, active: ActiveVisual = ActiveVisual.Vanilla, currentReceipt: Int = 3): RotationTransactionState {
        val a = ActivationSnapshot(mode, "a", active, 7)
        val ring = requireNotNull(RotationRing.tryCreate(listOf(RotationDescriptor("A", pack(receipt = currentReceipt), true), RotationDescriptor("B", pack("b"), true))))
        return RotationTransactionState(RotationModeState(RotationState(ring, a, hero, skin), VerifiedRegistryHead(id(1), hash(10), a, RotationInterlock.clear())))
    }
    private fun mode(s: RotationTransactionState, e: RotationModeEvent) = core.decide(s, RotationTransactionEvent.Mode(e)).state
    private fun advance(s: RotationTransactionState) = mode(s, RotationModeEvent.AdvanceMode)
    private fun proof(s: RotationTransactionState, v: ActiveVisual) = mode(s, RotationModeEvent.VerifiedVisual(HeroVerifiedVisual(s.mode.rotation.currentHero!!,
        if (v is ActiveVisual.Pack) VerifiedLiveVisualProof.Pack(s.mode.rotation.currentSkin!!, v) else VerifiedLiveVisualProof.Vanilla(s.mode.rotation.currentSkin!!))))
    private fun rebind(s: RotationTransactionState) = mode(s, RotationModeEvent.Selector(RotationEvent.Rebind(HeroBindingToken("newhero"), SkinBindingToken("newskin"))))
    private fun cor(s: RotationTransactionState) = s.operation!!.transaction.envelope!!.let { TransactionCorrelation(it.transactionId, it.binding) }
    private fun tx(s: RotationTransactionState, e: TransactionEvent) = core.decide(s, RotationTransactionEvent.Transaction(s.operation!!.origin.rotation.currentHero!!, e)).state
    private fun armed(initial: RotationTransactionState): RotationTransactionState {
        val s = tx(initial, TransactionEvent.Prepared(cor(initial))); val e = s.operation!!.transaction.envelope!!; val n = 100 + s.mode.operationHighWater.toInt() * 3
        val r = RegistryCommitReceipt(e.baseGenerationId, e.baseGenerationSha256, id(n), hash(n))
        val l = RotationInterlock(InterlockState.ARMED, e.transactionId, e.operation, e.baseGenerationId, e.baseGenerationSha256, e.prior, e.target, e.binding, e.priorEstablishedOnBinding, null, null)
        return tx(s, TransactionEvent.ArmCommitted(cor(s), r, VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, e.prior, l)))
    }
    private fun parent(s: RotationTransactionState): VerifiedRegistryHead {
        val t = s.operation!!.transaction; val r = t.failureReceipt ?: t.armCommitReceipt!!
        return VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, t.envelope!!.prior, t.interlock)
    }
    private fun receipt(p: VerifiedRegistryHead, n: Int = 900) = RegistryCommitReceipt(p.generationId, p.generationSha256, id(n), hash(n))
    private fun child(r: RegistryCommitReceipt, a: ActivationSnapshot) = VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, a, RotationInterlock.clear())
    private fun authority(s: RotationTransactionState) = RotationRecoveryAuthority(s.mode.rotation.currentHero!!, s.mode.rotation.currentSkin!!)
    private fun fence(s: RotationTransactionState) = RotationOutcomeFence(cor(s), s.operation!!.origin.rotation.currentHero!!, parent(s))
    private fun prior(s: RotationTransactionState): ActivationSnapshot {
        val e = s.operation!!.transaction.envelope!!
        return if (!e.priorEstablishedOnBinding && e.prior.active is ActiveVisual.Pack) e.prior.copy(skinStamp = e.prior.skinStamp + 1) else e.prior
    }
    private fun restored(s: RotationTransactionState): RotationRecoveryEvidence.PriorRestoredAndFenced {
        val a = prior(s); val r = receipt(parent(s)); val p = s.operation!!
        val v = if (a.active is ActiveVisual.Pack) VerifiedLiveVisualProof.Pack(p.origin.rotation.currentSkin!!, a.active) else VerifiedLiveVisualProof.Vanilla(p.origin.rotation.currentSkin!!)
        return RotationRecoveryEvidence.PriorRestoredAndFenced(p, fence(s), HeroVerifiedVisual(p.origin.rotation.currentHero!!, v), r, child(r, a), authority(s))
    }
    private fun issued(s: RotationTransactionState): RotationRecoveryEvidence.IssuedClosureOutcome {
        val r = receipt(parent(s), 900 + s.mode.operationHighWater.toInt()); return RotationRecoveryEvidence.IssuedClosureOutcome(s.operation!!, fence(s), r, child(r, s.operation.transaction.pendingClosure!!), authority(s))
    }
    private fun recover(s: RotationTransactionState, e: RotationRecoveryEvidence): RotationTransactionState {
        val d = core.decide(s, RotationTransactionEvent.Recover(e)); assertTrue(d.commands.isEmpty()); return d.state
    }
    private fun noop(s: RotationTransactionState, e: RotationRecoveryEvidence) = assertEquals(s, recover(s, e))
    private fun resolved(s: RotationTransactionState, before: RotationTransactionState) {
        assertNull(s.operation); assertNull(s.mode.operation); assertNull(s.mode.rotation.pending); assertTrue(core.isStateValid(s))
        assertEquals(before.copy(lastRecovery = null), s.lastRecovery!!.before); assertEquals(before.operation?.transaction?.phase, s.lastRecovery.before.operation?.transaction?.phase)
        assertEquals(before.mode.operationHighWater, s.mode.operationHighWater); assertEquals(before.mode.rotation.epochHighWater, s.mode.rotation.epochHighWater)
    }
    private fun pending(active: ActiveVisual = ActiveVisual.Vanilla): RotationTransactionState {
        val a = armed(advance(state(active = active))); return tx(a, TransactionEvent.ApplyFailed(cor(a), "APPLY_FAILED"))
    }
    private fun blocked(rollback: Boolean = false): RotationTransactionState {
        var a = armed(advance(state()))
        a = if (rollback) { val p = tx(a, TransactionEvent.ApplyFailed(cor(a), "FAILED")); tx(p, TransactionEvent.RollbackVerified(cor(p), true)) }
            else tx(a, TransactionEvent.ApplyVerified(cor(a)))
        return tx(a, TransactionEvent.CompletionIndeterminate(cor(a)))
    }
    private fun failed(persisted: Boolean): RotationTransactionState {
        val s = pending(pack()); val t = s.operation!!.transaction
        val l = t.interlock.copy(state = InterlockState.ROLLBACK_FAILED, originalFailure = t.originalFailure, rollbackFailure = "FAILED")
        val r = receipt(parent(s), 800)
        return tx(s, TransactionEvent.RollbackFailed(cor(s), "FAILED", if (persisted) r else null,
            if (persisted) VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, t.envelope!!.prior, l) else null))
    }
    @Test fun pendingPriorRecoveryPreservesHistoricalPhaseAndAllowsNewOperation() {
        for (active in listOf(ActiveVisual.Vanilla, pack())) for (rebound in listOf(false, true)) {
            var s = pending(active); if (rebound) s = rebind(s)
            val e = restored(s); val done = recover(s, e); resolved(done, s)
            assertEquals(prior(s), done.mode.rotation.activation); assertNull(done.mode.liveProof); noop(done, e)
            val next = advance(done); assertNotNull(next.operation); assertEquals(2L, next.mode.operationHighWater); assertNotEquals(cor(s).transactionId, cor(next).transactionId)
        }
    }
    @Test fun blockedIssuedTargetAndPriorCloseOnlyTheirStoredOutcome() {
        for (rollback in listOf(false, true)) for (rebound in listOf(false, true)) {
            var s = blocked(rollback); if (rebound) s = rebind(s)
            val e = issued(s); val wrong = if (rollback) s.operation!!.transaction.envelope!!.target else prior(s)
            noop(s, e.copy(head = e.head.copy(activation = wrong)))
            val done = recover(s, e); resolved(done, s); assertEquals(s.operation!!.transaction.pendingClosure, done.mode.rotation.activation); noop(done, e)
        }
    }
    @Test fun qualifiedPriorRestorationResolvesIndeterminateTargetWithoutInventingRollbackEvents() {
        val s = blocked(); val done = recover(s, restored(s)); resolved(done, s); assertEquals(prior(s), done.mode.rotation.activation)
        assertNull(done.lastRecovery!!.before.operation!!.transaction.originalFailure)
        assertEquals(TransactionPhase.BLOCKED, done.lastRecovery.before.operation!!.transaction.phase)
    }
    @Test fun rollbackFailureRequiresFullExactStoredFailureParentOrArmWhenNotPersisted() {
        for (persisted in listOf(false, true)) {
            val s = failed(persisted); val e = restored(s); val p = e.fence.authoritativeParent
            noop(s, e.copy(fence = e.fence.copy(authoritativeParent = p.copy(interlock = RotationInterlock.clear()))))
            noop(s, e.copy(fence = e.fence.copy(authoritativeParent = p.copy(interlock = p.interlock.copy(rollbackFailure = "OTHER")))))
            if (persisted) { val arm = s.operation!!.transaction.armCommitReceipt!!; noop(s, e.copy(fence = e.fence.copy(authoritativeParent = p.copy(generationId = arm.newGenerationId, generationSha256 = arm.newGenerationSha256)))) }
            val done = recover(s, e); resolved(done, s); assertEquals(prior(s), done.mode.rotation.activation)
        }
    }
    @Test fun wrongCorrelationParentReceiptProofAndImportIdentityRemainExactlyRetryable() {
        val s = rebind(pending(pack())); val e = restored(s); val p = e.fence.authoritativeParent; val v = e.restoration.proof as VerifiedLiveVisualProof.Pack
        for (bad in listOf(e.copy(operation = e.operation.copy(origin = e.operation.origin.copy(operationHighWater = 9))),
            e.copy(fence = e.fence.copy(correlation = e.fence.correlation.copy(transactionId = id(55)))), e.copy(fence = e.fence.copy(originalHero = HeroBindingToken("other"))),
            e.copy(fence = e.fence.copy(authoritativeParent = p.copy(activation = p.activation.copy(selectedPackId = "b")))),
            e.copy(receipt = e.receipt.copy(expectedGenerationId = id(55))), e.copy(receipt = e.receipt.copy(newGenerationId = id(1))),
            e.copy(receipt = e.receipt.copy(newGenerationSha256 = "bad")), e.copy(head = e.head.copy(interlock = RotationInterlock.clear().copy(transactionId = id(1)))),
            e.copy(head = e.head.copy(activation = e.head.activation.copy(active = pack(receipt = 9)))),
            e.copy(restoration = e.restoration.copy(hero = HeroBindingToken("newhero"))), e.copy(restoration = e.restoration.copy(proof = v.copy(binding = SkinBindingToken("newskin")))),
            e.copy(restoration = e.restoration.copy(proof = v.copy(visual = pack("b")))),
            e.copy(authority = e.authority.copy(hero = hero)), e.copy(authority = e.authority.copy(liveProof = e.restoration)))) noop(s, bad)
        resolved(recover(s, e), s)
    }
    @Test fun wrongOutcomeKindsAndVisualOnlyLegacyFactsDoNotResolve() {
        val s = pending(); val r = receipt(parent(s))
        noop(s, RotationRecoveryEvidence.IssuedClosureOutcome(s.operation!!, fence(s), r, child(r, prior(s)), authority(s)))
        noop(s, RotationRecoveryEvidence.VisualFencedNotExecuted(s.operation, s.mode.head, authority = authority(s)))
        val a = armed(advance(state())); noop(a, restored(a))
        val b = failed(true); noop(b, RotationRecoveryEvidence.IssuedClosureOutcome(b.operation!!, fence(b), r, child(r, prior(b)), authority(b)))
        resolved(recover(s, restored(s)), s)
    }
    private fun modeCompletion(s: RotationTransactionState): RotationModeEvent.ModeCommitted {
        val p = s.mode.operation!!; val r = receipt(p.baseHead, 1200 + s.mode.operationHighWater.toInt()); return RotationModeEvent.ModeCommitted(p.correlation, r, child(r, p.target))
    }
    @Test fun selectedNoopRefreshesExactCurrentReceiptWithoutChangingStampOrProof() {
        val s = proof(state(active = pack(), currentReceipt = 8), pack(receipt = 9)); val begin = advance(s)
        assertNull(begin.operation)
        assertTrue((core.decide(s, RotationTransactionEvent.Mode(RotationModeEvent.AdvanceMode)).commands.single() as RotationTransactionCommand.Mode).command is RotationModeCommand.CommitVerifiedSelectedOn)
        assertEquals(s.mode.liveProof, begin.mode.operation!!.selectedProof); assertEquals(pack(receipt = 8), begin.mode.operation.target.active)
        val c = modeCompletion(begin); assertEquals(begin, mode(begin, c.copy(head = c.head.copy(activation = c.head.activation.copy(active = pack())))))
        val done = mode(begin, c); assertEquals(7L, done.mode.rotation.activation.skinStamp); assertEquals(s.mode.liveProof, done.mode.liveProof); assertEquals(SkinMode.ON, done.mode.rotation.activation.mode)
    }
    @Test fun noopProofRebindRequiresExactCurrentPackAndOriginalCasRecovery() {
        val begin = advance(proof(state(active = pack(), currentReceipt = 8), pack(receipt = 9))); val c = modeCompletion(begin); val s = rebind(begin)
        val e = RotationRecoveryEvidence.ModeClosure(s.mode.operation!!, c, authority(s)); noop(s, e)
        noop(s, e.copy(authority = e.authority.copy(liveProof = begin.mode.liveProof)))
        noop(s, e.copy(authority = e.authority.copy(liveProof = HeroVerifiedVisual(e.authority.hero, VerifiedLiveVisualProof.Pack(e.authority.skin, pack("b"))))))
        val good = e.copy(authority = e.authority.copy(liveProof = HeroVerifiedVisual(e.authority.hero, VerifiedLiveVisualProof.Pack(e.authority.skin, pack(receipt = 77)))))
        val done = recover(s, good); resolved(done, s); assertEquals(pack(receipt = 8), done.mode.rotation.activation.active)
    }
    @Test fun noopOverflowAndForgedProofRemainFailClosed() {
        val s = proof(state(active = pack()), pack()); val a = s.mode.rotation.activation.copy(skinStamp = Long.MAX_VALUE)
        val stamp = s.copy(mode = s.mode.copy(rotation = s.mode.rotation.copy(activation = a), head = s.mode.head.copy(activation = a)))
        assertEquals(Long.MAX_VALUE, advance(stamp).mode.operation!!.target.skinStamp)
        val max = s.copy(mode = s.mode.copy(operationHighWater = Long.MAX_VALUE)); assertEquals(max, advance(max))
        val p = advance(s)
        for (v in listOf(null, HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Pack(skin, pack("b"))))) {
            val bad = p.copy(mode = p.mode.copy(operation = p.mode.operation!!.copy(selectedProof = v))); assertFalse(core.isStateValid(bad)); assertEquals(bad, advance(bad))
        }
        assertNotNull(advance(state(active = pack())).operation)
    }
    private fun ready(initial: RotationTransactionState, epoch: ULong): RotationTransactionState {
        val s = mode(initial, RotationModeEvent.Selector(RotationEvent.ConfirmDeath(DeathEpoch(epoch), initial.mode.rotation.currentHero!!, initial.mode.rotation.currentSkin!!)))
        return mode(s, RotationModeEvent.Selector(RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(epoch), s.mode.rotation.currentHero!!, s.mode.rotation.currentSkin!!))))
    }
    private fun completeVisual(s: RotationTransactionState): RotationTransactionState {
        var a = armed(s); a = tx(a, TransactionEvent.ApplyVerified(cor(a))); val r = receipt(parent(a), 1500 + s.mode.operationHighWater.toInt())
        return tx(a, TransactionEvent.CompletionCommitted(cor(a), r, child(r, a.operation!!.transaction.pendingClosure!!)))
    }
    @Test fun sameBindingUnconsumedCancellationHasExactRetirementAndReachableOffClosure() {
        val ready = ready(state(SkinMode.ROTATE, pack()), 4uL); val intent = ready.mode.rotation.pending!!.issuedIntent!!; val s = advance(ready)
        val e = RotationRecoveryEvidence.ReadinessFencedNotExecuted(intent, s.mode.head, authority(s))
        noop(s, e.copy(intent = intent.copy(epoch = DeathEpoch(8uL)))); val done = recover(s, e); resolved(done, s)
        assertTrue(done.mode.offRequested); assertEquals(SkinMode.ROTATE, done.mode.rotation.activation.mode)
        noop(done, e); assertEquals(done, core.decide(done, RotationTransactionEvent.ConsumeReady(intent)).state)
        val off = completeVisual(advance(done)); assertEquals(SkinMode.OFF, off.mode.rotation.activation.mode); assertEquals(DeathEpoch(4uL), off.mode.rotation.epochHighWater)
        val consumed = core.decide(ready, RotationTransactionEvent.ConsumeReady(intent)).state; noop(consumed, e)
    }
    @Test fun selectedModeRetryAndCurrentProofForgeryCannotChangeIssuedAuthority() {
        val initial = proof(state(active = pack(), currentReceipt = 8), pack(receipt = 9)); val s = advance(initial); val c = modeCompletion(s)
        for (failed in listOf(false, true)) {
            val uncertain = mode(s, if (failed) RotationModeEvent.ModeFailed(s.mode.operation!!.correlation) else RotationModeEvent.ModeIndeterminate(s.mode.operation!!.correlation))
            val retry = core.decide(uncertain, RotationTransactionEvent.Mode(RotationModeEvent.RetryModeCommit(s.mode.operation!!.correlation)))
            assertEquals(uncertain, retry.state)
            assertEquals(s.mode.operation, ((retry.commands.single() as RotationTransactionCommand.Mode).command as RotationModeCommand.CommitVerifiedSelectedOn).operation)
            assertNull(mode(uncertain, c).mode.operation)
        }
        for (v in listOf(null, HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Pack(skin, pack(receipt = 8))))) {
            val forged = s.copy(mode = s.mode.copy(liveProof = v)); assertFalse(core.isStateValid(forged)); assertEquals(forged, mode(forged, c))
        }
        val back = mode(rebind(s), RotationModeEvent.Selector(RotationEvent.Rebind(hero, skin)))
        assertTrue(back.mode.operation!!.reboundBlocked); assertEquals(back, mode(back, c))
        assertFalse(core.isStateValid(back.copy(mode = back.mode.copy(liveProof = initial.mode.liveProof))))
    }
    @Test fun establishedPriorStampAndDeathIssuedOutcomeKeepEpochAndBoundedAudit() {
        val ready = ready(proof(state(SkinMode.ROTATE, pack()), pack(receipt = 9)), ULong.MAX_VALUE - 1uL); val intent = ready.mode.rotation.pending!!.issuedIntent!!
        var a = armed(core.decide(ready, RotationTransactionEvent.ConsumeReady(intent)).state)
        val rollback = tx(a, TransactionEvent.ApplyFailed(cor(a), "FAILED")); assertTrue(rollback.operation!!.transaction.envelope!!.priorEstablishedOnBinding)
        val restored = recover(rollback, restored(rollback)); resolved(restored, rollback); assertEquals(7L, restored.mode.rotation.activation.skinStamp)
        assertEquals(restored, ready(restored, ULong.MAX_VALUE - 1uL))
        val next = ready(restored, ULong.MAX_VALUE); val nextIntent = next.mode.rotation.pending!!.issuedIntent!!
        a = armed(core.decide(next, RotationTransactionEvent.ConsumeReady(nextIntent)).state); a = tx(a, TransactionEvent.ApplyVerified(cor(a))); a = tx(a, TransactionEvent.CompletionIndeterminate(cor(a))); a = rebind(a)
        val e = issued(a); val done = recover(a, e); resolved(done, a); assertNull(done.lastRecovery!!.before.lastRecovery)
        assertEquals(nextIntent.candidate.currentObject, done.mode.rotation.activation.active); assertEquals(nextIntent.candidate.currentObject.id, done.mode.rotation.activation.selectedPackId)
        assertEquals(DeathEpoch(ULong.MAX_VALUE), done.mode.rotation.epochHighWater); assertEquals(done, ready(done, ULong.MAX_VALUE))
        val record = done.lastRecovery; var linked = record.before
        repeat(10000) { linked = linked.copy(lastRecovery = RotationRecoveryRecord(linked, e)) }
        assertFalse(core.isStateValid(done.copy(lastRecovery = record.copy(before = linked))))
        assertFalse(core.isStateValid(done.copy(mode = done.mode.copy(operationHighWater = 0))))
    }
    @Test fun issuedOutcomeRejectsEntireEnvelopeFenceReceiptAndCurrentAuthorityMatrix() {
        val s = rebind(blocked()); val e = issued(s); val p = e.fence.authoritativeParent
        for (bad in listOf(e.copy(fence = e.fence.copy(correlation = e.fence.correlation.copy(binding = SkinBindingToken("newskin")))),
            e.copy(fence = e.fence.copy(authoritativeParent = p.copy(interlock = p.interlock.copy(target = p.activation)))),
            e.copy(operation = e.operation.copy(transaction = e.operation.transaction.copy(pendingClosure = prior(s)))),
            e.copy(receipt = e.receipt.copy(expectedGenerationSha256 = hash(77))), e.copy(head = e.head.copy(generationId = id(77))),
            e.copy(head = e.head.copy(activation = e.head.activation.copy(skinStamp = 7))),
            e.copy(head = e.head.copy(activation = e.head.activation.copy(active = pack(receipt = 77)))),
            e.copy(authority = e.authority.copy(skin = skin)), e.copy(authority = e.authority.copy(liveProof = HeroVerifiedVisual(hero, VerifiedLiveVisualProof.Vanilla(skin)))))) noop(s, bad)
        resolved(recover(s, e), s)
    }
    @Test fun lateFailureParentRequiresOneExactArmHopAndNeverOverridesStoredFailure() {
        val s = failed(false); val e = restored(s); val t = s.operation!!.transaction; val arm = parent(s); val hop = receipt(arm, 800)
        val parent = VerifiedRegistryHead(hop.newGenerationId, hop.newGenerationSha256, t.envelope!!.prior,
            t.interlock.copy(state = InterlockState.ROLLBACK_FAILED, originalFailure = t.originalFailure, rollbackFailure = t.rollbackFailure))
        val r = receipt(parent); val good = e.copy(fence = e.fence.copy(authoritativeParent = parent, lateFailureReceipt = hop), receipt = r, head = child(r, prior(s)))
        for (bad in listOf(good.copy(fence = good.fence.copy(lateFailureReceipt = null)),
            good.copy(fence = good.fence.copy(lateFailureReceipt = hop.copy(expectedGenerationId = id(77)))),
            good.copy(fence = good.fence.copy(lateFailureReceipt = hop.copy(expectedGenerationSha256 = hash(77)))),
            good.copy(fence = good.fence.copy(lateFailureReceipt = hop.copy(newGenerationSha256 = "bad"))),
            good.copy(fence = good.fence.copy(authoritativeParent = parent.copy(activation = parent.activation.copy(active = pack(receipt = 77))))),
            good.copy(fence = good.fence.copy(authoritativeParent = parent.copy(interlock = parent.interlock.copy(rollbackFailure = "OTHER")))),
            good.copy(fence = good.fence.copy(authoritativeParent = parent.copy(interlock = parent.interlock.copy(originalFailure = "OTHER")))),
            good.copy(fence = good.fence.copy(authoritativeParent = parent.copy(interlock = RotationInterlock.clear()))),
            good.copy(receipt = r.copy(expectedGenerationId = arm.generationId)))) noop(s, bad)
        val persisted = failed(true); val stored = restored(persisted); noop(persisted, stored.copy(fence = stored.fence.copy(lateFailureReceipt = hop)))
        val pending = pending(pack()); val pe = restored(pending); noop(pending, pe.copy(fence = pe.fence.copy(lateFailureReceipt = hop)))
        val done = recover(s, good); resolved(done, s); assertNull(done.lastRecovery!!.before.operation!!.transaction.failureReceipt)
        assertEquals(hop, (done.lastRecovery.evidence as RotationRecoveryEvidence.PriorRestoredAndFenced).fence.lateFailureReceipt); noop(done, good)
    }
    @Test fun repeatedFullCyclesDeathSuccessorRollbackCancellationAndRebindPreserveHighwaters() {
        var s = state(); val ids = mutableSetOf<String>()
        repeat(3) { cycle ->
            s = advance(s); assertTrue(ids.add(cor(s).transactionId)); s = completeVisual(s)
            s = advance(s); assertTrue(ids.add(s.mode.operation!!.correlation.operationId)); s = mode(s, modeCompletion(s))
            s = ready(s, (cycle * 3 + 1).toULong()); val intent = s.mode.rotation.pending!!.issuedIntent!!
            s = core.decide(s, RotationTransactionEvent.ConsumeReady(intent)).state; assertTrue(ids.add(cor(s).transactionId))
            if (cycle == 1) { s = armed(s); s = tx(s, TransactionEvent.ApplyFailed(cor(s), "FAILED")); s = rebind(s); val before = s; s = recover(s, restored(s)); resolved(s, before) }
            else s = completeVisual(s)
            assertEquals(s, core.decide(s, RotationTransactionEvent.ConsumeReady(intent)).state)
            val prior = s.mode.rotation.activation; val ready = ready(s, (cycle * 3 + 2).toULong())
            assertNotEquals((prior.active as? ActiveVisual.Pack)?.id, ready.mode.rotation.pending!!.candidate.currentObject.id)
            val pending = mode(s, RotationModeEvent.Selector(RotationEvent.ConfirmDeath(DeathEpoch((cycle * 3 + 2).toULong()), s.mode.rotation.currentHero!!, s.mode.rotation.currentSkin!!)))
            s = advance(pending); assertNotNull(s.mode.canceledPending); assertTrue(ids.add(cor(s).transactionId)); s = completeVisual(s)
            assertEquals(SkinMode.OFF, s.mode.rotation.activation.mode); assertEquals((cycle + 1) * 4L, s.mode.operationHighWater); assertTrue(core.isStateValid(s))
        }
        assertEquals(12, ids.size)
    }
}
