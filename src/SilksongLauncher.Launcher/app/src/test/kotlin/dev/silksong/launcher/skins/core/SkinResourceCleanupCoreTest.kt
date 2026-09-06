package dev.silksong.launcher.skins.core

import org.junit.Assert.*
import org.junit.Test

class SkinResourceCleanupCoreTest {
    private val core = SkinResourceCleanupCore()
    private val lifetime = SkinResourceLifetimeCore()
    private val admission = SkinResourceCore()
    private fun plan(n: Int) = ResourceLifetimePlan("p$n", "binding", "g$n", "s$n")
    private fun source(n: Int = 1) = VerifiedResourceSource("verified", "source$n", n.toLong(), "a".repeat(64), 1, 1)
    private fun step(s: ResourceCleanupState, e: ResourceCleanupEvent): ResourceCleanupDecision = core.decide(s, e).also { assertTrue(it.diagnosis, it.accepted) }
    private fun denied(s: ResourceCleanupState, e: ResourceCleanupEvent) { val d = core.decide(s, e); assertFalse(d.accepted); assertSame(s, d.state); assertTrue(d.commands.isEmpty()) }
    private fun live(s: ResourceCleanupState, e: ResourceLifetimeEvent) = step(s, ResourceCleanupEvent.Lifetime(e)).state
    private fun begin(s: ResourceCleanupState = ResourceCleanupState(), n: Int = 1) = live(s, ResourceLifetimeEvent.BeginCandidate("tx$n", plan(n)))
    private fun acquire(s: ResourceCleanupState, n: Int = 1, borrowed: Boolean = false, kind: ResourceReferenceKind = ResourceReferenceKind.TARGET) = live(s, ResourceLifetimeEvent.Admission(if (borrowed) ResourceEvent.AcquireBorrowed(s.lifetime.candidate!!.target.planId, "default", 1, 1, kind) else ResourceEvent.Acquire(s.lifetime.candidate!!.target.planId, source(n), kind)))
    private fun ready(s: ResourceCleanupState): ResourceCleanupState { val op = s.lifetime.ledger.scratch?.operation ?: return s; return live(live(s, ResourceLifetimeEvent.Admission(ResourceEvent.DecodeCompleted(op))), ResourceLifetimeEvent.Admission(ResourceEvent.ScratchReleased(op))) }
    private fun cancel(s: ResourceCleanupState) = step(s, ResourceCleanupEvent.CancelPreparation(s.lifetime.candidate!!))
    private fun release(s: ResourceCleanupState, p: String = "p1"): ResourceCleanupDecision { var d = ResourceCleanupDecision(s, true, "test"); for (r in s.lifetime.ledger.references.filter { it.planId == p }) d = step(d.state, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r)))); return d }
    private fun active(): ResourceCleanupState { var s = ready(acquire(begin())); val c = s.lifetime.candidate!!; s = live(s, ResourceLifetimeEvent.SealCandidate(c)); s = live(s, ResourceLifetimeEvent.AwaitDurableCompletion(c)); return live(s, ResourceLifetimeEvent.DurableCompletionVerified(c)) }

    @Test fun emptyAndSealedCancellationReturnReadyWithoutWrites() {
        for (sealed in listOf(false, true)) { var s = begin(); val c = s.lifetime.candidate!!; if (sealed) s = live(s, ResourceLifetimeEvent.SealCandidate(c)); val d = cancel(s); assertTrue(d.commands.isEmpty()); assertNull(d.state.lifetime.candidate); assertTrue(core.canBeginCandidate(d.state)); assertEquals(s.lifetime.nextOperationId, d.state.lifetime.nextOperationId); denied(d.state, ResourceCleanupEvent.CancelPreparation(c)) }
    }
    @Test fun stopIntentRetainsReservationAndFreezesCandidate() {
        val s = acquire(begin()); val d = cancel(s); val op = s.lifetime.ledger.scratch!!.operation
        assertEquals(op, (d.commands.single() as ResourceCleanupCommand.RequestDecoderStop).operation)
        assertEquals(8L, admission.totals(d.state.lifetime.ledger)!!.processBytes); assertFalse(core.canApply(d.state)); assertFalse(core.canBeginCandidate(d.state))
        denied(d.state, ResourceCleanupEvent.CancelPreparation(s.lifetime.candidate!!)); denied(d.state, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.BeginCandidate("tx2", plan(2))))
        denied(d.state, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.Admission(ResourceEvent.Acquire("p1", source(), ResourceReferenceKind.TARGET))))
        denied(d.state, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.SealCandidate(s.lifetime.candidate!!)))
    }
    @Test fun neverCreatedWaitsForScratchAndExactReferencesInEitherOrder() {
        for (scratchFirst in listOf(false, true)) { val initial = acquire(begin()); val c = initial.lifetime.candidate!!; val op = initial.lifetime.ledger.scratch!!.operation; var s = cancel(initial).state
            if (scratchFirst) s = step(s, ResourceCleanupEvent.ScratchReleased(c, op)).state
            s = step(s, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.NEVER_CREATED)).state
            assertEquals(8L, admission.totals(s.lifetime.ledger)!!.processBytes)
            if (!scratchFirst) s = step(s, ResourceCleanupEvent.ScratchReleased(c, op)).state
            val d = release(s); assertTrue(d.commands.isEmpty()); assertTrue(d.state.lifetime.ledger.allocations.isEmpty()); assertNull(d.state.lifetime.ledger.scratch); assertTrue(core.canBeginCandidate(d.state)); assertEquals(initial.lifetime.ledger.nextId, d.state.lifetime.ledger.nextId)
            denied(d.state, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.CREATED))
        }
    }
    @Test fun lateCreatedRequiresScratchThenDisposalAcknowledgement() {
        for (scratchFirst in listOf(false, true)) { val initial = acquire(begin()); val c = initial.lifetime.candidate!!; val op = initial.lifetime.ledger.scratch!!.operation; var s = release(cancel(initial).state).state
            if (scratchFirst) s = step(s, ResourceCleanupEvent.ScratchReleased(c, op)).state
            var d = step(s, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.CREATED)); s = d.state
            if (!scratchFirst) { assertTrue(s.pendingDisposals.isEmpty()); d = step(s, ResourceCleanupEvent.ScratchReleased(c, op)); s = d.state }
            val disposal = s.pendingDisposals.single(); assertTrue(d.commands.any { it is ResourceCleanupCommand.Dispose }); assertEquals(4L, admission.totals(s.lifetime.ledger)!!.residentBytes)
            denied(s, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.NEVER_CREATED)); denied(s, ResourceCleanupEvent.DisposeAcknowledged(disposal.copy(operationId = disposal.operationId + 1)))
            val drained = step(s, ResourceCleanupEvent.DisposeAcknowledged(disposal)).state; assertTrue(core.canBeginCandidate(drained)); assertEquals(0L, admission.totals(drained.lifetime.ledger)!!.processBytes); denied(drained, ResourceCleanupEvent.DisposeAcknowledged(disposal))
        }
    }
    @Test fun alreadyCreatedCannotBecomeNeverCreated() {
        var s = acquire(begin()); val c = s.lifetime.candidate!!; val op = s.lifetime.ledger.scratch!!.operation; s = live(s, ResourceLifetimeEvent.Admission(ResourceEvent.DecodeCompleted(op))); val d = cancel(s); assertTrue(d.commands.isEmpty()); s = d.state
        denied(s, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.NEVER_CREATED)); denied(s, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.CREATED)); s = step(s, ResourceCleanupEvent.ScratchReleased(c, op)).state; assertEquals(1, release(s).state.pendingDisposals.size)
    }
    @Test fun readyCandidateWaitsForEveryReferenceKindAndDisposesOnce() {
        var s = begin(); for (k in ResourceReferenceKind.values()) s = acquire(s, kind = k); s = ready(s); s = cancel(s).state
        val refs = s.lifetime.ledger.references; for ((i,r) in refs.withIndex()) { val d = step(s, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r)))); s = d.state; assertEquals(if (i == refs.lastIndex) 1 else 0, d.commands.size); denied(s, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r)))) }
        assertEquals(1, s.pendingDisposals.size)
    }
    @Test fun sharedActiveMembershipAndReferencesSurviveCancel() {
        val a = active(); var s = acquire(begin(a, 2)); s = release(cancel(s).state, "p2").state; assertEquals(plan(1), s.lifetime.active); assertEquals(listOf("p1"), s.lifetime.ledger.allocations.single().planIds); assertEquals(a.lifetime.ledger.references, s.lifetime.ledger.references); assertTrue(s.pendingDisposals.isEmpty()); assertTrue(core.canBeginCandidate(s))
    }
    @Test fun borrowedDefaultsAreUntrackedNeverDisposed() { val d = release(cancel(acquire(begin(), borrowed = true)).state); assertTrue(d.commands.isEmpty()); assertTrue(d.state.lifetime.ledger.allocations.isEmpty()); assertTrue(core.canBeginCandidate(d.state)) }
    @Test fun consumedGateCannotBeCancelledAndExactSuccessStillProgresses() { var s = ready(acquire(begin(active(), 2), 2)); val c = s.lifetime.candidate!!; s = live(s, ResourceLifetimeEvent.SealCandidate(c)); s = live(s, ResourceLifetimeEvent.AwaitDurableCompletion(c)); denied(s, ResourceCleanupEvent.CancelPreparation(c)); assertEquals(plan(1), s.lifetime.active); s = live(s, ResourceLifetimeEvent.DurableCompletionVerified(c)); assertNotNull(s.lifetime.awaitingClones); denied(s, ResourceCleanupEvent.CancelPreparation(c)) }
    @Test fun fullClosureAndDecoderCorrelationRejectStaleFacts() {
        val initial = acquire(begin()); val c = initial.lifetime.candidate!!; val op = initial.lifetime.ledger.scratch!!.operation; val s = cancel(initial).state
        for (wrong in listOf(c.copy(operationId = 9), c.copy(transactionId = "wrong"), c.copy(target = c.target.copy(binding = "wrong")), c.copy(target = c.target.copy(generationId = "wrong")), c.copy(target = c.target.copy(stamp = "wrong")), c.copy(target = c.target.copy(planId = "wrong")))) denied(s, ResourceCleanupEvent.DecodeResolved(wrong, op, ResourceDecodeOutcome.CREATED))
        for (wrong in listOf(op.copy(operationId = 99), op.copy(allocationId = 99), op.copy(planId = "wrong"))) { denied(s, ResourceCleanupEvent.DecodeResolved(c, wrong, ResourceDecodeOutcome.NEVER_CREATED)); denied(s, ResourceCleanupEvent.ScratchReleased(c, wrong)) }
        val resolved = step(s, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.NEVER_CREATED)).state; denied(resolved, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.CREATED)); denied(resolved, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.NEVER_CREATED))
    }
    @Test fun repeatedDrainPreservesBothHighwatersAndRejectsOldDisposals() {
        var s = ResourceCleanupState(); var old: ResourceDisposalOperation? = null; var rid = 1L; var oid = 1L
        repeat(6) { s = release(cancel(ready(acquire(begin(s)))).state).state; if (old != null) denied(s, ResourceCleanupEvent.DisposeAcknowledged(old!!)); val op = s.pendingDisposals.single(); s = step(s, ResourceCleanupEvent.DisposeAcknowledged(op)).state; assertTrue(s.lifetime.ledger.nextId > rid); assertTrue(s.lifetime.nextOperationId > oid); rid = s.lifetime.ledger.nextId; oid = s.lifetime.nextOperationId; old = op }
    }
    @Test fun overflowRejectsEntireCleanupTransition() { val prepared = release(ready(acquire(begin()))).state; val l = prepared.lifetime; val s = ResourceCleanupState(ResourceLifetimeState(l.ledger, l.active, l.candidate, nextOperationId = Long.MAX_VALUE)); denied(s, ResourceCleanupEvent.CancelPreparation(l.candidate!!)); assertEquals(4L, admission.totals(s.lifetime.ledger)!!.residentBytes) }
    @Test fun scratchAckIsRequiredEvenAfterNeverCreatedAndReferencesReleased() {
        val initial = acquire(begin()); val c = initial.lifetime.candidate!!; val op = initial.lifetime.ledger.scratch!!.operation
        var s = release(cancel(initial).state).state
        s = step(s, ResourceCleanupEvent.DecodeResolved(c, op, ResourceDecodeOutcome.NEVER_CREATED)).state
        assertEquals(8L, admission.totals(s.lifetime.ledger)!!.processBytes); assertFalse(core.canBeginCandidate(s))
        val d = step(s, ResourceCleanupEvent.ScratchReleased(c, op)); assertTrue(d.commands.isEmpty()); assertTrue(core.canBeginCandidate(d.state))
        denied(d.state, ResourceCleanupEvent.ScratchReleased(c, op))
    }
    @Test fun sealedReadyCancellationBlocksConsumedGateAndPendingIdentityAcquisition() {
        var s = ready(acquire(begin())); val c = s.lifetime.candidate!!
        s = live(s, ResourceLifetimeEvent.SealCandidate(c)); assertTrue(core.canApply(s)); s = cancel(s).state
        assertFalse(core.canApply(s)); denied(s, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.AwaitDurableCompletion(c)))
        s = release(s).state; denied(s, ResourceCleanupEvent.Lifetime(ResourceLifetimeEvent.Admission(ResourceEvent.Acquire("p1", source(), ResourceReferenceKind.TARGET))))
        val op = s.pendingDisposals.single()
        denied(s, ResourceCleanupEvent.DisposeAcknowledged(op.copy(closure = c.copy(target = c.target.copy(stamp = "wrong")))))
        denied(s, ResourceCleanupEvent.DisposeAcknowledged(op.copy(allocationId = op.allocationId + 1)))
    }
    @Test fun multipleDisposalsAndSnapshotCollectionsAreIndependent() {
        var s = ready(acquire(begin())); s = ready(acquire(s, 2)); val d = cancel(release(s).state)
        assertEquals(2, d.state.pendingDisposals.size)
        val mutable = d.state.pendingDisposals.toMutableList(); val copy = ResourceCleanupState(d.state.lifetime, d.state.cancellation, pendingDisposals = mutable)
        mutable.clear(); assertEquals(2, copy.pendingDisposals.size)
        assertThrows(UnsupportedOperationException::class.java) { (copy.pendingDisposals as MutableList).clear() }
        assertThrows(UnsupportedOperationException::class.java) { (d.commands as MutableList).clear() }
        val first = step(copy, ResourceCleanupEvent.DisposeAcknowledged(copy.pendingDisposals[1])); assertTrue(first.commands.isEmpty()); assertFalse(core.canBeginCandidate(first.state))
        val last = step(first.state, ResourceCleanupEvent.DisposeAcknowledged(copy.pendingDisposals[0])); assertTrue(last.commands.isEmpty()); assertTrue(core.canBeginCandidate(last.state))
        val malformed = ResourceCleanupState(copy.lifetime, copy.cancellation, pendingDisposals = copy.pendingDisposals + copy.pendingDisposals)
        denied(malformed, ResourceCleanupEvent.DisposeAcknowledged(copy.pendingDisposals[0]))
    }
    @Test fun lifetimeValidationSeamRejectsMalformedOwnershipWithoutMutation() {
        val s = acquire(begin()); assertTrue(lifetime.isValidState(s.lifetime))
        val bad = ResourceLifetimeState(s.lifetime.ledger, nextOperationId = s.lifetime.nextOperationId)
        assertFalse(lifetime.isValidState(bad)); denied(ResourceCleanupState(bad), ResourceCleanupEvent.CancelPreparation(s.lifetime.candidate!!))
    }
    @Test fun malformedCancellationSnapshotsAreRejected() { val s = acquire(begin()); val bad = ResourceCleanupState(s.lifetime, s.lifetime.candidate, ResourceCleanupDecoder(s.lifetime.ledger.scratch!!.operation.copy(operationId = 999))); denied(bad, ResourceCleanupEvent.CancelPreparation(s.lifetime.candidate!!)); assertFalse(core.canApply(bad)); assertFalse(core.canBeginCandidate(bad)) }
}
