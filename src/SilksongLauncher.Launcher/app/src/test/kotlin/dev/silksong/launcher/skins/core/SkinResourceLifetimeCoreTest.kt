package dev.silksong.launcher.skins.core

import org.junit.Assert.*
import org.junit.Test

class SkinResourceLifetimeCoreTest {
    private val core = SkinResourceLifetimeCore()
    private val admission = SkinResourceCore()
    private fun plan(n: Int) = ResourceLifetimePlan("p$n", "binding", "generation$n", "stamp$n")
    private fun source(n: Int) = VerifiedResourceSource("verified", "source$n", n + 1L, "a".repeat(64), 1, 1)
    private fun step(s: ResourceLifetimeState, e: ResourceLifetimeEvent): ResourceLifetimeDecision = core.decide(s, e).also { assertTrue(it.diagnosis, it.accepted) }
    private fun denied(s: ResourceLifetimeState, e: ResourceLifetimeEvent) {
        val d = core.decide(s, e); assertFalse(d.accepted); assertSame(s, d.state); assertTrue(d.commands.isEmpty())
    }
    private fun begin(s: ResourceLifetimeState = ResourceLifetimeState(), n: Int = 1) = step(s, ResourceLifetimeEvent.BeginCandidate("tx$n", plan(n))).state
    private fun acquire(s: ResourceLifetimeState, n: Int, borrowed: Boolean = false, kind: ResourceReferenceKind = ResourceReferenceKind.TARGET): ResourceLifetimeState {
        val p = s.candidate!!.target.planId
        return step(s, ResourceLifetimeEvent.Admission(if (borrowed) ResourceEvent.AcquireBorrowed(p, "default", 1, 1, kind)
            else ResourceEvent.Acquire(p, source(n), kind))).state
    }
    private fun ready(s: ResourceLifetimeState): ResourceLifetimeState {
        val op = s.ledger.scratch?.operation ?: return s
        val d = step(s, ResourceLifetimeEvent.Admission(ResourceEvent.DecodeCompleted(op)))
        assertTrue(d.commands.single() is ResourceLifetimeCommand.Admission)
        return step(d.state, ResourceLifetimeEvent.Admission(ResourceEvent.ScratchReleased(op))).state
    }
    private fun seal(s: ResourceLifetimeState) = step(s, ResourceLifetimeEvent.SealCandidate(s.candidate!!)).state
    private fun commit(s: ResourceLifetimeState): ResourceLifetimeDecision {
        val pending = step(seal(s), ResourceLifetimeEvent.AwaitDurableCompletion(s.candidate!!)).state
        return step(pending, ResourceLifetimeEvent.DurableCompletionVerified(s.candidate!!))
    }
    private fun active(borrowed: Boolean = false) = commit(ready(acquire(begin(), 1, borrowed))).state
    private fun displaced(s: ResourceLifetimeState = active(), shared: Boolean = false) = commit(ready(acquire(begin(s, 2), if (shared) 1 else 2)))
    private fun release(s: ResourceLifetimeState, p: String): ResourceLifetimeDecision {
        var d = ResourceLifetimeDecision(s, true, "test")
        for (r in s.ledger.references.filter { it.planId == p }) d = step(d.state, ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r)))
        return d
    }
    private fun clones(s: ResourceLifetimeState) = step(s, ResourceLifetimeEvent.CompanionClonesInvalidated(s.awaitingClones!!))

    @Test fun consumedValueApplyGateBlocksWhileDurableClosureIsPending() {
        val sealed = seal(ready(acquire(begin(), 1))); val c = sealed.candidate!!
        assertTrue(core.canApply(sealed))
        denied(sealed, ResourceLifetimeEvent.DurableCompletionVerified(c))
        denied(sealed, ResourceLifetimeEvent.AwaitDurableCompletion(c.copy(transactionId = "wrong")))
        val d = step(sealed, ResourceLifetimeEvent.AwaitDurableCompletion(c))
        assertTrue(d.commands.isEmpty()); assertTrue(d.state.awaitingDurableCompletion)
        assertFalse(core.canApply(d.state)); assertFalse(core.canBeginCandidate(d.state))
        denied(d.state, ResourceLifetimeEvent.AwaitDurableCompletion(c))
        denied(d.state, ResourceLifetimeEvent.BeginCandidate("tx2", plan(2)))
        assertNull(d.state.active); assertNull(d.state.retirement); assertNull(d.state.awaitingClones)
        denied(d.state, ResourceLifetimeEvent.SealCandidate(c))
        denied(d.state, ResourceLifetimeEvent.Admission(ResourceEvent.Acquire("p1", source(1), ResourceReferenceKind.TARGET)))
        val released = release(d.state, "p1").state
        assertFalse(core.canApply(released)); assertTrue(released.awaitingDurableCompletion)
        assertEquals(plan(1), step(released, ResourceLifetimeEvent.DurableCompletionVerified(c)).state.active)
        val malformed = ResourceLifetimeState(sealed.ledger, candidate = c, nextOperationId = sealed.nextOperationId, awaitingDurableCompletion = true)
        denied(malformed, ResourceLifetimeEvent.DurableCompletionVerified(c))
    }
    @Test fun differentBindingReplacementRetainsExactPriorCloneIdentity() {
        val old = active(); val target = plan(2).copy(binding = "new-binding", generationId = plan(1).generationId, stamp = plan(1).stamp)
        val candidate = step(old, ResourceLifetimeEvent.BeginCandidate("rebind", target)).state
        val waiting = commit(ready(acquire(candidate, 2))).state
        val c = waiting.awaitingClones!!
        assertEquals(plan(1), c.prior); assertEquals(target, c.target)
        denied(waiting, ResourceLifetimeEvent.CompanionClonesInvalidated(c.copy(prior = c.prior!!.copy(binding = target.binding))))
        val pending = release(clones(waiting).state, "p1").state
        val drained = step(pending, ResourceLifetimeEvent.DisposeAcknowledged(pending.pendingDisposals.single())).state
        assertEquals(target, drained.active); assertTrue(core.canBeginCandidate(drained))
    }
    @Test fun firstActivationNeedsNoPriorCloneProof() {
        val s = active(); assertEquals(plan(1), s.active); assertNull(s.awaitingClones); assertNull(s.retirement)
        assertNull(s.candidate); assertFalse(core.canApply(s)); assertTrue(core.canBeginCandidate(s))
    }
    @Test fun preparationAndScratchMustFinishBeforeValueApplyGate() {
        val s = acquire(begin(), 1); assertFalse(core.canApply(s))
        denied(s, ResourceLifetimeEvent.SealCandidate(s.candidate!!))
        denied(s, ResourceLifetimeEvent.DurableCompletionVerified(s.candidate!!))
        val op = s.ledger.scratch!!.operation
        val decoded = step(s, ResourceLifetimeEvent.Admission(ResourceEvent.DecodeCompleted(op))).state
        denied(decoded, ResourceLifetimeEvent.SealCandidate(decoded.candidate!!))
        denied(decoded, ResourceLifetimeEvent.Admission(ResourceEvent.Acquire("p1", source(2), ResourceReferenceKind.TARGET)))
        val sealed = seal(step(decoded, ResourceLifetimeEvent.Admission(ResourceEvent.ScratchReleased(op))).state)
        assertTrue(core.canApply(sealed))
        denied(sealed, ResourceLifetimeEvent.Admission(ResourceEvent.Acquire("p1", source(1), ResourceReferenceKind.TARGET)))
    }
    @Test fun durableAndCloneEvidenceMustMatchEntireStoredClosure() {
        val prepared = seal(ready(acquire(begin(active(), 2), 2))); val c = prepared.candidate!!
        val s = step(prepared, ResourceLifetimeEvent.AwaitDurableCompletion(c)).state
        val wrong = listOf(c.copy(operationId = c.operationId + 1), c.copy(transactionId = "other"),
            c.copy(prior = c.prior!!.copy(stamp = "wrong")), c.copy(target = c.target.copy(generationId = "wrong")),
            c.copy(target = c.target.copy(binding = "wrong")), c.copy(target = c.target.copy(planId = "wrong")))
        for (v in wrong) denied(s, ResourceLifetimeEvent.DurableCompletionVerified(v))
        val d = step(s, ResourceLifetimeEvent.DurableCompletionVerified(c))
        assertTrue(d.commands.single() is ResourceLifetimeCommand.InvalidateCompanionClones)
        assertNull(d.state.retirement); assertEquals(c, d.state.awaitingClones)
        denied(d.state, ResourceLifetimeEvent.DurableCompletionVerified(c))
        for (v in wrong) denied(d.state, ResourceLifetimeEvent.CompanionClonesInvalidated(v))
        denied(d.state, ResourceLifetimeEvent.RegisteredCloneCountVerified(c, 1))
        denied(d.state, ResourceLifetimeEvent.RegisteredCloneCountVerified(c.copy(operationId = 999), 0))
        val qualified = step(d.state, ResourceLifetimeEvent.RegisteredCloneCountVerified(c, 0)).state
        assertEquals(c, qualified.retirement)
        denied(qualified, ResourceLifetimeEvent.CompanionClonesInvalidated(c))
    }
    @Test fun pendingClosureClonesAndRetirementEachBlockNextCandidate() {
        val s = begin(active(), 2); denied(s, ResourceLifetimeEvent.BeginCandidate("tx3", plan(3)))
        val d = displaced(); assertFalse(core.canBeginCandidate(d.state)); assertFalse(core.canApply(d.state))
        denied(d.state, ResourceLifetimeEvent.BeginCandidate("tx3", plan(3)))
        val r = clones(d.state).state; denied(r, ResourceLifetimeEvent.BeginCandidate("tx3", plan(3)))
    }
    @Test fun allReferenceKindsDelayDisposalAndLastReleaseEmitsOnce() {
        var s = begin()
        for (k in ResourceReferenceKind.entries) s = acquire(s, 1, kind = k)
        s = clones(displaced(commit(ready(s)).state).state).state
        assertTrue(s.pendingDisposals.isEmpty())
        val refs = s.ledger.references.filter { it.planId == "p1" }
        for ((i, r) in refs.withIndex()) {
            val d = step(s, ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r))); s = d.state
            assertEquals(if (i == refs.lastIndex) 1 else 0, d.commands.size)
            denied(s, ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(r)))
        }
        assertEquals(1, s.pendingDisposals.size)
    }
    @Test fun disposeCommandKeepsChargesUntilExactAcknowledgement() {
        val s = clones(displaced().state).state
        val d = release(s, "p1"); val op = d.state.pendingDisposals.single()
        assertEquals(8, admission.totals(d.state.ledger)!!.residentBytes)
        assertEquals(4L, admission.totals(d.state.ledger)!!.planBytes["p1"])
        assertEquals(op, (d.commands.single() as ResourceLifetimeCommand.Dispose).operation)
        for (wrong in listOf(op.copy(operationId = op.operationId + 1), op.copy(allocationId = 999),
            op.copy(closure = op.closure.copy(transactionId = "wrong")))) denied(d.state, ResourceLifetimeEvent.DisposeAcknowledged(wrong))
        val drained = step(d.state, ResourceLifetimeEvent.DisposeAcknowledged(op)).state
        assertEquals(4, admission.totals(drained.ledger)!!.residentBytes); assertNull(drained.retirement)
        denied(drained, ResourceLifetimeEvent.DisposeAcknowledged(op)); assertTrue(core.canBeginCandidate(drained))
    }
    @Test fun sharedActiveAllocationSurvivesQualifiedOldMembershipClosure() {
        val s = clones(displaced(shared = true).state).state
        val d = release(s, "p1"); assertTrue(d.commands.isEmpty()); assertNull(d.state.retirement)
        assertEquals(listOf("p2"), d.state.ledger.allocations.single().planIds)
        assertEquals(1, d.state.ledger.references.size); assertEquals(4, admission.totals(d.state.ledger)!!.residentBytes)
    }
    @Test fun borrowedDefaultsAreForgottenWithoutDisposal() {
        val s = clones(displaced(active(borrowed = true)).state).state
        val d = release(s, "p1"); assertTrue(d.commands.isEmpty()); assertNull(d.state.retirement)
        assertTrue(d.state.ledger.allocations.all { it.ownership == ResourceOwnership.OWNED })
    }
    @Test fun disposalPendingCannotBeReacquiredAndUnqualifiedReleaseNeverFrees() {
        val d = release(displaced().state, "p1"); assertTrue(d.commands.isEmpty())
        assertEquals(2, d.state.ledger.allocations.size)
        val pending = clones(d.state).state
        denied(pending, ResourceLifetimeEvent.Admission(ResourceEvent.Acquire("p2", source(1), ResourceReferenceKind.TARGET)))
        assertEquals(2, pending.ledger.allocations.size)
    }
    @Test fun repeatedCyclesPreserveHighwatersAndRejectOldAcks() {
        var s = active(); var old: ResourceDisposalOperation? = null; var lastResource = s.ledger.nextId; var lastOperation = s.nextOperationId
        for (n in 2..8) {
            s = commit(ready(acquire(begin(s, n), n))).state
            s = clones(s).state; s = release(s, "p${n - 1}").state
            val op = s.pendingDisposals.single()
            if (old != null) denied(s, ResourceLifetimeEvent.DisposeAcknowledged(old))
            s = step(s, ResourceLifetimeEvent.DisposeAcknowledged(op)).state
            assertTrue(s.ledger.nextId > lastResource); assertTrue(s.nextOperationId > lastOperation)
            assertEquals(1, s.ledger.allocations.size); assertTrue(core.canBeginCandidate(s))
            lastResource = s.ledger.nextId; lastOperation = s.nextOperationId; old = op
        }
    }
    @Test fun emptyGenerationDrainStillPreservesOperationSequence() {
        var s = active(); s = commit(begin(s, 2)).state; s = release(clones(s).state, "p1").state
        val op = s.pendingDisposals.single(); s = step(s, ResourceLifetimeEvent.DisposeAcknowledged(op)).state
        assertTrue(s.ledger.allocations.isEmpty()); assertTrue(s.ledger.nextId > 1)
        val next = begin(s, 3); assertTrue(next.candidate!!.operationId > op.operationId)
        denied(next, ResourceLifetimeEvent.DisposeAcknowledged(op))
    }
    @Test fun invalidStateAndOverflowFailClosed() {
        val empty = ResourceLifetimeState(nextOperationId = Long.MAX_VALUE)
        denied(empty, ResourceLifetimeEvent.BeginCandidate("tx", plan(1)))
        val active = active()
        val bad = ResourceLifetimeState(active.ledger, active = null, nextOperationId = active.nextOperationId)
        denied(bad, ResourceLifetimeEvent.BeginCandidate("tx", plan(2))); assertFalse(core.canBeginCandidate(bad))
        denied(ResourceLifetimeState(), ResourceLifetimeEvent.BeginCandidate("", plan(1)))
        denied(active, ResourceLifetimeEvent.BeginCandidate("tx", plan(2).copy(stamp = plan(1).stamp)))
        val pending = release(clones(displaced().state).state, "p1").state
        val invalid = ResourceLifetimeState(pending.ledger, pending.active, retirement = pending.retirement,
            pendingDisposals = pending.pendingDisposals + pending.pendingDisposals, nextOperationId = pending.nextOperationId)
        denied(invalid, ResourceLifetimeEvent.DisposeAcknowledged(pending.pendingDisposals.single()))
    }
    @Test fun impossibleActiveHighwaterIsRejected() {
        val a = active()
        val reset = ResourceLifetimeState(a.ledger, a.active, nextOperationId = 1)
        denied(reset, ResourceLifetimeEvent.BeginCandidate("next", plan(2)))
    }
    @Test fun activeDecoderSnapshotIsRejected() {
        val a = active()
        val candidate = acquire(begin(a, 2), 2)
        val scratch = candidate.ledger.scratch!!
        val allocations = candidate.ledger.allocations.map { x -> if (x.id != scratch.operation.allocationId) x else
            ResourceAllocation(x.id, x.ownership, x.width, x.height, x.sources, listOf("p1", "p2"), phase = x.phase) }
        val forged = ResourceLifetimeState(ResourceState(allocations, candidate.ledger.references, scratch, candidate.ledger.nextId),
            candidate.active, candidate.candidate, nextOperationId = candidate.nextOperationId)
        denied(forged, ResourceLifetimeEvent.Admission(ResourceEvent.DecodeCompleted(scratch.operation)))
    }
    @Test fun severalDisposalsAcknowledgeIndependentlyWithoutReemission() {
        var a = ready(acquire(begin(), 1)); a = ready(acquire(a, 3)); a = commit(a).state
        val d = clones(release(displaced(a).state, "p1").state)
        assertEquals(2, d.commands.size); assertEquals(2, d.state.pendingDisposals.size)
        val ops = d.state.pendingDisposals
        denied(d.state, ResourceLifetimeEvent.DisposeAcknowledged(ops[0].copy(allocationId = ops[1].allocationId)))
        val unrelatedRelease = release(d.state, "p2"); assertTrue(unrelatedRelease.commands.isEmpty())
        val first = step(unrelatedRelease.state, ResourceLifetimeEvent.DisposeAcknowledged(ops[1]))
        assertTrue(first.commands.isEmpty()); assertNotNull(first.state.retirement)
        assertEquals(8, admission.totals(first.state.ledger)!!.residentBytes)
        denied(first.state, ResourceLifetimeEvent.DisposeAcknowledged(ops[1]))
        val last = step(first.state, ResourceLifetimeEvent.DisposeAcknowledged(ops[0]))
        assertTrue(last.commands.isEmpty()); assertNull(last.state.retirement)
        assertEquals(4, admission.totals(last.state.ledger)!!.residentBytes)
    }
    @Test fun disposalSequenceOverflowCannotPartiallyReleaseOrQualify() {
        val waiting = release(displaced().state, "p1").state
        val max = ResourceLifetimeState(waiting.ledger, waiting.active, awaitingClones = waiting.awaitingClones, nextOperationId = Long.MAX_VALUE)
        denied(max, ResourceLifetimeEvent.CompanionClonesInvalidated(max.awaitingClones!!))
        assertEquals(8, admission.totals(max.ledger)!!.residentBytes)
        val retiring = clones(displaced().state).state
        val maxRetired = ResourceLifetimeState(retiring.ledger, retiring.active, retirement = retiring.retirement, nextOperationId = Long.MAX_VALUE)
        denied(maxRetired, ResourceLifetimeEvent.Admission(ResourceEvent.ReleaseReference(maxRetired.ledger.references.first { it.planId == "p1" })))
    }
    @Test fun snapshotsAndCommandCollectionsAreImmutable() {
        val pending = release(clones(displaced().state).state, "p1")
        val mutable = pending.state.pendingDisposals.toMutableList()
        val copy = ResourceLifetimeState(pending.state.ledger, pending.state.active, retirement = pending.state.retirement,
            pendingDisposals = mutable, nextOperationId = pending.state.nextOperationId)
        mutable.clear(); assertEquals(1, copy.pendingDisposals.size)
        try { (copy.pendingDisposals as MutableList).clear(); fail("mutable") } catch (_: UnsupportedOperationException) { }
        try { (pending.commands as MutableList).clear(); fail("mutable") } catch (_: UnsupportedOperationException) { }
    }
}
