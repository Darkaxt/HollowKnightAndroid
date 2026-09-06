package dev.silksong.launcher.skins.core

import org.junit.Assert.*
import org.junit.Test

class SkinResourceTeardownCoreTest {
    private val core = SkinResourceCleanupCore()
    private val admission = SkinResourceCore()
    private fun plan(n: Int, binding: String = "b") = ResourceLifetimePlan("p$n", binding, "g$n", "s$n")
    private fun step(s: ResourceCleanupState, e: ResourceCleanupEvent) = core.decide(s,e).also { assertTrue(it.diagnosis,it.accepted) }
    private fun denied(s: ResourceCleanupState, e: ResourceCleanupEvent) { val d=core.decide(s,e); assertFalse(d.accepted); assertSame(s,d.state); assertTrue(d.commands.isEmpty()) }
    private fun live(s: ResourceCleanupState, e: ResourceLifetimeEvent) = step(s,ResourceCleanupEvent.Lifetime(e)).state
    private fun begin(s: ResourceCleanupState = ResourceCleanupState(), n: Int = 1, binding: String = "b") = live(s,ResourceLifetimeEvent.BeginCandidate("tx$n",plan(n,binding)))
    private fun acquire(s: ResourceCleanupState, n: Int = 1, borrowed: Boolean = false, kind: ResourceReferenceKind = ResourceReferenceKind.TARGET): ResourceCleanupState {
        val p=s.lifetime.candidate!!.target.planId
        return live(s,ResourceLifetimeEvent.Admission(if(borrowed) ResourceEvent.AcquireBorrowed(p,"default",1,1,kind) else ResourceEvent.Acquire(p,VerifiedResourceSource("proof","source$n",n.toLong(),"a".repeat(64),1,1),kind)))
    }
    private fun ready(s: ResourceCleanupState): ResourceCleanupState { val op=s.lifetime.ledger.scratch?.operation ?: return s; return live(live(s,ResourceLifetimeEvent.Admission(ResourceEvent.DecodeCompleted(op))),ResourceLifetimeEvent.Admission(ResourceEvent.ScratchReleased(op))) }
    private fun consume(s: ResourceCleanupState): ResourceCleanupState { val c=s.lifetime.candidate!!; return live(live(s,ResourceLifetimeEvent.SealCandidate(c)),ResourceLifetimeEvent.AwaitDurableCompletion(c)) }
    private fun active(borrowed: Boolean = false) = consume(ready(acquire(begin(),borrowed=borrowed))).let { live(it,ResourceLifetimeEvent.DurableCompletionVerified(it.lifetime.candidate!!)) }
    private fun release(s: ResourceCleanupState, p: String? = null): ResourceCleanupState { var result=s; for(r in s.lifetime.ledger.references.filter { p==null || it.planId==p }) result=live(result,ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r))); return result }
    private fun stop(s: ResourceCleanupState) = step(s,ResourceCleanupEvent.Teardown)
    private fun rollback(s: ResourceCleanupState, restored: ResourceLifetimePlan? = s.lifetime.active) = step(s,ResourceCleanupEvent.PostTransactionOutcome(s.lifetime.candidate!!,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,restored))
    private fun rollbackAck(s: ResourceCleanupState, p: ResourceLifetimePlan) = step(s,ResourceCleanupEvent.RollbackClonesInvalidated(s.rollback!!.closure,p))

    @Test fun idleTeardownPermanentlyClosesGate() {
        val d=stop(ResourceCleanupState()); assertTrue(d.state.closed); assertFalse(core.canBeginCandidate(d.state)); assertFalse(core.canApply(d.state)); assertTrue(d.commands.isEmpty())
        val again=stop(d.state); assertTrue(again.state.closed); assertTrue(again.commands.isEmpty())
        denied(d.state,ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.BeginCandidate("tx",plan(1))))
    }
    @Test fun consumedRollbackNeedsCloneQualificationBeforeCandidateRemoval() {
        val s=consume(begin()); val c=s.lifetime.candidate!!; val d=rollback(s)
        assertEquals(c,d.state.cancellation); assertFalse(core.canBeginCandidate(d.state)); assertEquals(c.target,(d.commands.single() as ResourceCleanupCommand.InvalidateRollbackClones).plan)
        val done=rollbackAck(d.state,c.target).state; assertNull(done.lifetime.candidate); assertTrue(core.canBeginCandidate(done)); denied(done,ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,null))
    }
    @Test fun unknownAndVisualOnlyKeepConsumedOwnershipAndLateSuccessReachable() {
        for(kind in listOf(ResourcePostTransactionOutcome.UNKNOWN,ResourcePostTransactionOutcome.VISUAL_ONLY_ROLLBACK)) {
            val s=consume(ready(acquire(begin(active(),2),2))); val c=s.lifetime.candidate!!
            val d=step(s,ResourceCleanupEvent.PostTransactionOutcome(c,kind,null)); assertSame(s,d.state); assertTrue(d.commands.isEmpty()); assertTrue(d.state.lifetime.awaitingDurableCompletion)
            val stopped=stop(d.state).state; denied(stopped,ResourceCleanupEvent.CancelPreparation(c)); val success=live(stopped,ResourceLifetimeEvent.DurableCompletionVerified(c)); assertEquals(c,success.lifetime.awaitingClones); assertNull(success.shutdown)
        }
    }
    @Test fun outcomeMismatchAndPrematureProofCannotReleaseOwnership() {
        val s=consume(begin(active(),2)); val c=s.lifetime.candidate!!
        for(wrong in listOf(c.copy(operationId=99),c.copy(transactionId="wrong"),c.copy(prior=c.prior!!.copy(stamp="wrong")),c.copy(target=c.target.copy(binding="wrong")),c.copy(target=c.target.copy(generationId="wrong")),c.copy(target=c.target.copy(stamp="wrong")))) denied(s,ResourceCleanupEvent.PostTransactionOutcome(wrong,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.prior))
        denied(s,ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,null))
        denied(begin(),ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.prior))
    }
    @Test fun crossBindingRollbackRejectsUnchangedPriorOnObsoleteBinding() {
        val s=consume(ready(acquire(begin(active(),2,"current"),2))); val c=s.lifetime.candidate!!
        assertNotEquals(c.prior!!.binding,c.target.binding)
        denied(s,ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.prior))
        assertTrue(s.lifetime.awaitingDurableCompletion); assertEquals(8L,admission.totals(s.lifetime.ledger)!!.processBytes)
        val restored=c.prior!!.copy(binding=c.target.binding,generationId="restored",stamp="restored")
        val recovery=rollback(s,restored); assertEquals(restored,recovery.state.rollback!!.restoredPrior); assertEquals(c,recovery.state.cancellation)
    }
    @Test fun crossBindingRollbackRejectsArbitraryThirdBinding() {
        val s=consume(ready(acquire(begin(active(),2,"current"),2))); val c=s.lifetime.candidate!!
        val wrong=c.prior!!.copy(binding="third",generationId="restored",stamp="restored")
        denied(s,ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,wrong))
        assertTrue(s.lifetime.awaitingDurableCompletion); assertEquals(8L,admission.totals(s.lifetime.ledger)!!.processBytes)
        val restored=wrong.copy(binding=c.target.binding)
        val recovery=rollback(s,restored); assertEquals(restored,recovery.state.rollback!!.restoredPrior); assertEquals(c,recovery.state.cancellation)
    }
    @Test fun restoredIdentityQualifiesCandidateThenOldPriorThenCurrentShutdown() {
        for(binding in listOf("b","rebound")) {
            val s=consume(ready(acquire(begin(active(),2,binding)))); val c=s.lifetime.candidate!!; val restored=c.prior!!.copy(binding=binding,stamp="restored",generationId=c.target.generationId)
            var d=rollback(stop(s).state,restored); assertEquals(c.prior,d.state.lifetime.active)
            denied(d.state,ResourceCleanupEvent.RollbackClonesInvalidated(c,c.prior))
            d=rollbackAck(d.state,c.target); assertEquals(c.prior,(d.commands.single() as ResourceCleanupCommand.InvalidateRollbackClones).plan)
            denied(d.state,ResourceCleanupEvent.RollbackClonesInvalidated(c,c.target)); denied(d.state,ResourceCleanupEvent.RollbackCloneCountVerified(c,c.prior,1))
            d=step(d.state,ResourceCleanupEvent.RollbackCloneCountVerified(c,c.prior,0)); assertNull(d.state.shutdown)
            var state=release(d.state,"p2"); assertEquals(restored,state.lifetime.active); val shutdown=state.shutdown!!; assertEquals(restored,shutdown.plan)
            denied(state,ResourceCleanupEvent.RollbackClonesInvalidated(c,c.prior)); denied(state,ResourceCleanupEvent.ShutdownClonesInvalidated(shutdown.copy(plan=c.prior)))
            state=step(state,ResourceCleanupEvent.ShutdownClonesInvalidated(shutdown)).state; assertTrue(state.shutdownDisposals.isEmpty()); state=release(state)
            assertEquals(1,state.shutdownDisposals.size); val op=state.shutdownDisposals.single(); assertEquals(4L,admission.totals(state.lifetime.ledger)!!.processBytes)
            state=step(state,ResourceCleanupEvent.ShutdownDisposeAcknowledged(op)).state; assertTrue(state.closed)
        }
    }
    @Test fun exactPriorRestorationKeepsSharedOwnedAndBorrowedMemberships() {
        for(borrowed in listOf(false,true)) { val s=consume(ready(acquire(begin(active(borrowed),2),borrowed=borrowed))); val c=s.lifetime.candidate!!; val d=rollbackAck(rollback(s).state,c.target); assertTrue(d.commands.isEmpty()); val done=release(d.state,"p2"); assertEquals(c.prior,done.lifetime.active); assertEquals(listOf("p1"),done.lifetime.ledger.allocations.single().planIds); assertTrue(done.pendingDisposals.isEmpty()); assertTrue(core.canBeginCandidate(done)) }
    }
    @Test fun activeShutdownNeedsAllReferenceKindsAndExactCloneAndDisposeAcks() {
        var s=begin(); for(k in ResourceReferenceKind.values()) s=acquire(s,kind=k); s=consume(ready(s)); s=live(s,ResourceLifetimeEvent.DurableCompletionVerified(s.lifetime.candidate!!)); val before=s; var d=stop(s); s=d.state; val op=s.shutdown!!
        assertEquals(op,(d.commands.single() as ResourceCleanupCommand.InvalidateShutdownClones).operation); assertTrue(stop(s).commands.isEmpty()); assertEquals(before.lifetime.ledger.nextId,s.lifetime.ledger.nextId)
        denied(s,ResourceCleanupEvent.ShutdownCloneCountVerified(op,2)); denied(s,ResourceCleanupEvent.ShutdownClonesInvalidated(op.copy(operationId=99))); denied(s,ResourceCleanupEvent.ShutdownClonesInvalidated(op.copy(plan=op.plan.copy(stamp="wrong"))))
        s=step(s,ResourceCleanupEvent.ShutdownCloneCountVerified(op,0)).state
        for((i,r) in s.lifetime.ledger.references.withIndex()) { d=step(s,ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r)))); s=d.state; assertEquals(if(i==2)1 else 0,d.commands.size) }
        val disposal=s.shutdownDisposals.single(); denied(s,ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal.copy(allocationId=99))); denied(s,ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal.copy(operationId=99))); assertEquals(4L,admission.totals(s.lifetime.ledger)!!.processBytes)
        val done=step(s,ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal)).state; assertTrue(done.closed); assertEquals(s.lifetime.nextOperationId,done.lifetime.nextOperationId); assertEquals(s.lifetime.ledger.nextId,done.lifetime.ledger.nextId); denied(done,ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal))
    }
    @Test fun borrowedShutdownUntracksOnlyAfterCloneAndReferenceClosure() {
        val s=stop(active(true)).state; val refs=release(s); assertEquals(1,refs.lifetime.ledger.allocations.size)
        val d=step(refs,ResourceCleanupEvent.ShutdownClonesInvalidated(refs.shutdown!!)); assertTrue(d.state.closed); assertTrue(d.commands.isEmpty())
    }
    @Test fun teardownPreparedAndSealedCandidatesUseExistingCancellation() {
        for(seal in listOf(false,true)) { var s=ready(acquire(begin())); val c=s.lifetime.candidate!!; if(seal)s=live(s,ResourceLifetimeEvent.SealCandidate(c)); s=stop(s).state; assertEquals(c,s.cancellation); assertFalse(core.canApply(s)); denied(s,ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.AwaitDurableCompletion(c))); s=release(s); val op=s.pendingDisposals.single(); s=step(s,ResourceCleanupEvent.DisposeAcknowledged(op)).state; assertTrue(s.closed); assertFalse(core.canBeginCandidate(s)) }
    }
    @Test fun lateDecoderAndScratchRacesNeverFreeOnStopRequest() {
        for(created in listOf(false,true)) for(scratchFirst in listOf(false,true)) {
            val initial=acquire(begin()); val c=initial.lifetime.candidate!!; val op=initial.lifetime.ledger.scratch!!.operation; val d=stop(initial); assertTrue(d.commands.single() is ResourceCleanupCommand.RequestDecoderStop); var s=release(d.state)
            if(scratchFirst)s=step(s,ResourceCleanupEvent.ScratchReleased(c,op)).state
            assertEquals(8L,admission.totals(s.lifetime.ledger)!!.processBytes); assertFalse(s.closed)
            s=step(s,ResourceCleanupEvent.DecodeResolved(c,op,if(created)ResourceDecodeOutcome.CREATED else ResourceDecodeOutcome.NEVER_CREATED)).state
            if(!scratchFirst)s=step(s,ResourceCleanupEvent.ScratchReleased(c,op)).state
            if(created) { assertFalse(s.closed); s=step(s,ResourceCleanupEvent.DisposeAcknowledged(s.pendingDisposals.single())).state }
            assertTrue(s.closed); denied(s,ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.CREATED))
        }
    }
    @Test fun retirementDrainsBeforeActiveShutdownWithoutSecondSet() {
        var s=consume(ready(acquire(begin(active(),2),2))); val c=s.lifetime.candidate!!; s=live(s,ResourceLifetimeEvent.DurableCompletionVerified(c)); s=stop(s).state; assertNull(s.shutdown)
        s=live(s,ResourceLifetimeEvent.CompanionClonesInvalidated(c)); assertNotNull(s.lifetime.retirement); assertNull(s.shutdown); s=release(s,"p1"); val op=s.lifetime.pendingDisposals.single(); assertNull(s.shutdown)
        val d=step(s,ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.DisposeAcknowledged(op))); s=d.state; assertNull(s.lifetime.retirement); assertNotNull(s.shutdown); assertEquals(1,d.commands.size)
        denied(s,ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.DisposeAcknowledged(op))); s=release(s); s=step(s,ResourceCleanupEvent.ShutdownClonesInvalidated(s.shutdown!!)).state; s=step(s,ResourceCleanupEvent.ShutdownDisposeAcknowledged(s.shutdownDisposals.single())).state; assertTrue(s.closed)
    }
    @Test fun overflowAndMalformedShutdownPreserveSnapshotsAndCharges() {
        val a=active(); val l=a.lifetime; val s=ResourceCleanupState(ResourceLifetimeState(l.ledger,l.active,nextOperationId=Long.MAX_VALUE)); denied(s,ResourceCleanupEvent.Teardown)
        val stopped=stop(a).state; val bad=ResourceCleanupState(stopped.lifetime,teardownRequested=true,shutdown=stopped.shutdown!!.copy(plan=plan(9))); denied(bad,ResourceCleanupEvent.Teardown)
        assertEquals(4L,admission.totals(a.lifetime.ledger)!!.processBytes)
    }
    @Test fun teardownPreservesAlreadyPendingCancellationAndRetirementDisposals() {
        var prepared=ready(acquire(begin())); prepared=release(prepared)
        val cancelled=step(prepared,ResourceCleanupEvent.CancelPreparation(prepared.lifetime.candidate!!)).state
        val cancellationOp=cancelled.pendingDisposals.single(); val stoppedCancel=stop(cancelled); assertEquals(cancelled.pendingDisposals,stoppedCancel.state.pendingDisposals); assertTrue(stoppedCancel.commands.isEmpty()); assertTrue(step(stoppedCancel.state,ResourceCleanupEvent.DisposeAcknowledged(cancellationOp)).state.closed)
        for(disposalPending in listOf(false,true)) {
            var s=consume(ready(acquire(begin(active(),2),2))); val c=s.lifetime.candidate!!; s=live(s,ResourceLifetimeEvent.DurableCompletionVerified(c)); s=live(s,ResourceLifetimeEvent.CompanionClonesInvalidated(c))
            if(disposalPending)s=release(s,"p1")
            val prior=s; val d=stop(s); s=d.state; assertNull(s.shutdown); assertTrue(d.commands.isEmpty()); assertEquals(prior.lifetime.pendingDisposals,s.lifetime.pendingDisposals)
            if(!disposalPending)s=release(s,"p1")
            val op=s.lifetime.pendingDisposals.single(); s=live(s,ResourceLifetimeEvent.DisposeAcknowledged(op)); assertNotNull(s.shutdown)
        }
    }
    @Test fun unknownOutcomeCanLaterDurablyRollbackAndNeverEmitsWrites() {
        var s=consume(ready(acquire(begin(active(),2),2))); val c=s.lifetime.candidate!!
        s=step(s,ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.UNKNOWN,null)).state
        s=rollback(s).state; s=rollbackAck(s,c.target).state; s=release(s,"p2"); val op=s.pendingDisposals.single()
        denied(s,ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.prior)); val done=step(s,ResourceCleanupEvent.DisposeAcknowledged(op)); assertEquals(c.prior,done.state.lifetime.active); assertTrue(done.commands.isEmpty()); assertTrue(core.canBeginCandidate(done.state))
    }
    @Test fun multipleShutdownDisposalsRetainChargeUntilEveryExactAck() {
        var s=ready(acquire(begin())); s=ready(acquire(s,2)); s=consume(s); s=live(s,ResourceLifetimeEvent.DurableCompletionVerified(s.lifetime.candidate!!)); s=release(stop(s).state)
        val d=step(s,ResourceCleanupEvent.ShutdownClonesInvalidated(s.shutdown!!)); s=d.state; assertEquals(2,d.commands.size); assertTrue(d.commands.all { it is ResourceCleanupCommand.DisposeShutdown }); val ops=s.shutdownDisposals
        val first=step(s,ResourceCleanupEvent.ShutdownDisposeAcknowledged(ops[1])); assertTrue(first.commands.isEmpty()); assertFalse(first.state.closed); assertEquals(4L,admission.totals(first.state.lifetime.ledger)!!.processBytes)
        val last=step(first.state,ResourceCleanupEvent.ShutdownDisposeAcknowledged(ops[0])); assertTrue(last.state.closed); assertEquals(0L,admission.totals(last.state.lifetime.ledger)!!.processBytes); assertEquals(s.lifetime.nextOperationId,last.state.lifetime.nextOperationId); assertEquals(s.lifetime.ledger.nextId,last.state.lifetime.ledger.nextId)
    }
    @Test fun rollbackClosureMustMatchValidatedLifetimeBeforeAllEntrypoints() {
        val s=consume(begin(active(),2)); val c=s.lifetime.candidate!!
        for(forged in listOf(c.copy(transactionId="wrong"),c.copy(target=c.target.copy(stamp="wrong")),c.copy(prior=c.prior!!.copy(binding="wrong")))) {
            val malformed=ResourceCleanupState(s.lifetime,forged,rollback=ResourceRollbackResolution(forged,s.lifetime.active))
            assertFalse(core.canBeginCandidate(malformed)); assertFalse(core.canApply(malformed)); denied(malformed,ResourceCleanupEvent.Teardown)
        }
    }
    @Test fun malformedRollbackCannotInventConsumedSealedOrigin() {
        val s=begin(); val c=s.lifetime.candidate!!
        val forged=ResourceCleanupState(s.lifetime,c,rollback=ResourceRollbackResolution(c,null))
        denied(forged,ResourceCleanupEvent.RollbackClonesInvalidated(c,c.target))
    }
    @Test fun malformedEmptySnapshotsAreNotTerminalClosed() {
        assertFalse(ResourceCleanupState(ResourceLifetimeState(nextOperationId=0),teardownRequested=true).closed)
        assertFalse(ResourceCleanupState(teardownRequested=true,shutdownClonesQualified=true).closed)
    }
    @Test fun overflowAtShutdownDisposalRetainsExactUnqualifiedSnapshot() {
        val active=release(active()); val l=active.lifetime
        val exhausted=ResourceCleanupState(ResourceLifetimeState(l.ledger,l.active,nextOperationId=Long.MAX_VALUE-1))
        val stopped=stop(exhausted).state; assertEquals(Long.MAX_VALUE,stopped.lifetime.nextOperationId)
        denied(stopped,ResourceCleanupEvent.ShutdownClonesInvalidated(stopped.shutdown!!)); assertFalse(stopped.shutdownClonesQualified); assertEquals(4L,admission.totals(stopped.lifetime.ledger)!!.processBytes)
    }
    @Test fun shutdownFullIdentityRejectsEveryChangedFieldAndDoubleAck() {
        var s=release(stop(active()).state); val h=s.shutdown!!
        for(p in listOf(h.plan.copy(planId="wrong"),h.plan.copy(binding="wrong"),h.plan.copy(generationId="wrong"),h.plan.copy(stamp="wrong"))) denied(s,ResourceCleanupEvent.ShutdownClonesInvalidated(h.copy(plan=p)))
        s=step(s,ResourceCleanupEvent.ShutdownClonesInvalidated(h)).state; val op=s.shutdownDisposals.single()
        denied(s,ResourceCleanupEvent.ShutdownClonesInvalidated(h)); denied(s,ResourceCleanupEvent.ShutdownDisposeAcknowledged(op.copy(shutdown=h.copy(operationId=h.operationId+1))))
        for(p in listOf(h.plan.copy(planId="wrong"),h.plan.copy(binding="wrong"),h.plan.copy(generationId="wrong"),h.plan.copy(stamp="wrong"))) denied(s,ResourceCleanupEvent.ShutdownDisposeAcknowledged(op.copy(shutdown=h.copy(plan=p))))
    }
    @Test fun disposalCollectionsAreImmutableAndNoNewWorkIsAccepted() {
        var s=release(stop(active()).state); s=step(s,ResourceCleanupEvent.ShutdownClonesInvalidated(s.shutdown!!)).state
        val list=s.shutdownDisposals.toMutableList(); val copy=ResourceCleanupState(s.lifetime,teardownRequested=true,shutdown=s.shutdown,shutdownClonesQualified=true,shutdownDisposals=list); list.clear(); assertEquals(1,copy.shutdownDisposals.size)
        assertThrows(UnsupportedOperationException::class.java) { (copy.shutdownDisposals as MutableList).clear() }
        denied(s,ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.Admission(ResourceEvent.AcquireBorrowed("p1","default",1,1,ResourceReferenceKind.TARGET))))
        assertTrue(stop(s).commands.isEmpty())
    }
}
