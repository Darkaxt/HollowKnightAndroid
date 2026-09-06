package dev.silksong.launcher.skins.core

import org.junit.Assert.*
import org.junit.Test

class SkinResourceCoreTest {
    private val core = SkinResourceCore()
    private val mib = 1024L * 1024
    private fun source(key: String = "a", bytes: Long = 4) = VerifiedResourceSource(
        "verification-1", key, 100, key.take(1).repeat(64), bytes / 4, 1)
    private fun acquire(s: ResourceState = ResourceState(), p: String = "p", v: VerifiedResourceSource = source()) =
        core.decide(s, ResourceEvent.Acquire(p, v, ResourceReferenceKind.TARGET))
    private fun ready(s: ResourceState): ResourceState {
        val op = s.scratch!!.operation
        val decoded = core.decide(s, ResourceEvent.DecodeCompleted(op)).state
        return core.decide(decoded, ResourceEvent.ScratchReleased(op)).state
    }
    private fun denied(s: ResourceState, e: ResourceEvent) {
        val d = core.decide(s, e)
        assertFalse(d.accepted); assertSame(s, d.state); assertTrue(d.commands.isEmpty())
    }

    @Test fun reservesBeforeDecodeAndChargesScratchAndResident() {
        val d = acquire(v = source(bytes = 32 * mib))
        assertTrue(d.accepted)
        assertEquals(32 * mib, core.totals(d.state)!!.residentBytes)
        assertEquals(64 * mib, core.totals(d.state)!!.processBytes)
        assertEquals(1, core.totals(d.state)!!.ownedAllocations)
        assertTrue(d.commands.any { it is ResourceCommand.Decode })
    }
    @Test fun textureLimitPlusOnePixelAndArithmeticOverflowRejectWithoutMutation() {
        for (v in listOf(source(bytes = 32 * mib + 4), source().copy(width = Long.MAX_VALUE),
            source().copy(width = Long.MAX_VALUE / 4 + 1), source().copy(width = 0), source().copy(height = -1))) {
            val s = ResourceState(); denied(s, ResourceEvent.Acquire("p", v, ResourceReferenceKind.TARGET))
        }
    }
    @Test fun verifiedFullContentSharesAcrossCanonicalSourcesAndPlans() {
        val first = acquire(); val s = ready(first.state)
        val second = acquire(s, "q", source().copy(canonicalSource = "other", proofScope = "verification-2"))
        assertTrue(second.accepted); assertEquals(1, second.state.allocations.size)
        assertEquals(2, second.state.references.size); assertNull(second.state.scratch)
        val t = core.totals(second.state)!!
        assertEquals(4, t.residentBytes); assertEquals(4L, t.planBytes["p"]); assertEquals(4L, t.planBytes["q"])
        assertFalse(second.commands.any { it is ResourceCommand.Decode })
    }
    @Test fun sameCanonicalSourceAcquisitionAddsDistinctReferencesWhileDecoding() {
        val s = acquire().state; val d = acquire(s)
        assertTrue(d.accepted); assertEquals(2, d.state.references.size)
        assertEquals(2, d.state.references.map { it.id }.toSet().size)
        assertEquals(s.scratch, d.state.scratch); assertFalse(d.commands.any { it is ResourceCommand.Decode })
    }
    @Test fun mismatchedIdentityDoesNotShareAndCanonicalContradictionRejects() {
        val base = source(); val s = ready(acquire(v = base).state)
        for (v in listOf(base.copy(encodedLength = 101), base.copy(sha256 = "b".repeat(64)),
            base.copy(width = 2), base.copy(height = 2))) {
            denied(s, ResourceEvent.Acquire("p", v, ResourceReferenceKind.TARGET))
            val other = acquire(s, v = v.copy(canonicalSource = "different"))
            assertTrue(other.accepted); assertEquals(2, other.state.allocations.size)
        }
        denied(s, ResourceEvent.Acquire("p", base.copy(format = "RGB24"), ResourceReferenceKind.TARGET))
    }
    @Test fun previewAndIncompleteVerificationNeverDecode() {
        for (v in listOf(source().copy(usage = ResourceUsage.PREVIEW), source().copy(sha256 = ""),
            source().copy(proofScope = ""), source().copy(encodedLength = 0))) {
            denied(ResourceState(), ResourceEvent.Acquire("p", v, ResourceReferenceKind.TARGET))
        }
    }
    @Test fun nextDecodeWaitsForExactCpuScratchReleaseAcknowledgement() {
        val s = acquire().state; val op = s.scratch!!.operation
        denied(s, ResourceEvent.Acquire("p", source("b"), ResourceReferenceKind.TARGET))
        denied(s, ResourceEvent.ScratchReleased(op))
        denied(s, ResourceEvent.DecodeCompleted(op.copy(allocationId = op.allocationId + 1)))
        val d = core.decide(s, ResourceEvent.DecodeCompleted(op))
        assertTrue(d.accepted); assertTrue(d.commands.single() is ResourceCommand.ReleaseUploadScratch)
        denied(d.state, ResourceEvent.DecodeCompleted(op))
        denied(d.state, ResourceEvent.Acquire("p", source("b"), ResourceReferenceKind.TARGET))
        denied(d.state, ResourceEvent.ScratchReleased(op.copy(operationId = op.operationId + 1)))
        val released = core.decide(d.state, ResourceEvent.ScratchReleased(op))
        assertTrue(released.accepted); assertNull(released.state.scratch)
        assertEquals(1, core.totals(released.state)!!.ownedAllocations)
        assertEquals(4, core.totals(released.state)!!.residentBytes)
        denied(released.state, ResourceEvent.ScratchReleased(op))
        assertTrue(acquire(released.state, v = source("b")).accepted)
    }
    @Test fun referenceReleaseIsExactAndNeverUnderflowsOrDropsSharedAllocation() {
        val s = acquire(acquire().state).state
        val ref = s.references.first()
        denied(s, ResourceEvent.ReleaseReference(ref.copy(planId = "wrong")))
        val one = core.decide(s, ResourceEvent.ReleaseReference(ref)).state
        assertEquals(1, one.references.size); denied(one, ResourceEvent.ReleaseReference(ref))
        val zero = core.decide(one, ResourceEvent.ReleaseReference(one.references.single())).state
        assertTrue(zero.references.isEmpty()); assertEquals(1, zero.allocations.size)
        assertEquals(4, core.totals(zero)!!.residentBytes)
    }
    @Test fun borrowedIdentityRemainsBorrowedAndNeverDecodes() {
        val e = ResourceEvent.AcquireBorrowed("p", "game-default", 16, 16, ResourceReferenceKind.ROLLBACK)
        val d = core.decide(ResourceState(), e)
        assertTrue(d.accepted); assertNull(d.state.scratch)
        assertEquals(0, core.totals(d.state)!!.ownedAllocations); assertEquals(0, core.totals(d.state)!!.processBytes)
        assertFalse(d.commands.any { it is ResourceCommand.Decode })
        val second = core.decide(d.state, e).state
        assertEquals(1, second.allocations.size); assertEquals(2, second.references.size)
        denied(second, e.copy(width = 17))
        var s = second
        for (r in second.references) s = core.decide(s, ResourceEvent.ReleaseReference(r)).state
        assertEquals(ResourceOwnership.BORROWED, s.allocations.single().ownership)
    }
    @Test fun planResidentExactLimitAndPlusOnePixel() {
        var s = ResourceState()
        for (key in listOf("a", "b", "c")) s = ready(acquire(s, v = source(key, 32 * mib)).state)
        assertEquals(96 * mib, core.totals(s)!!.planBytes["p"])
        denied(s, ResourceEvent.Acquire("p", source("d"), ResourceReferenceKind.TARGET))
    }
    @Test fun planAllocationExactLimitAndPlusOne() {
        var s = ResourceState()
        for (i in 0 until 205) s = ready(acquire(s, v = source().copy(canonicalSource = "s$i", encodedLength = i + 1L)).state)
        assertEquals(205, core.totals(s)!!.planAllocations["p"])
        denied(s, ResourceEvent.Acquire("p", source("b"), ResourceReferenceKind.TARGET))
    }
    @Test fun processAllocationExactLimitAndPlusOne() {
        var s = ResourceState()
        for (i in 0 until 410) s = ready(acquire(s, if (i < 205) "p" else "q",
            source().copy(canonicalSource = "s$i", encodedLength = i + 1L)).state)
        assertEquals(410, core.totals(s)!!.ownedAllocations)
        denied(s, ResourceEvent.Acquire("r", source("b"), ResourceReferenceKind.TARGET))
    }
    @Test fun processPeakExactLimitAndPlusOnePixel() {
        var s = ResourceState()
        for ((index, key) in listOf("a", "b", "c", "d", "e", "f").withIndex())
            s = ready(acquire(s, if (index < 3) "p" else "q", source(key, 32 * mib)).state)
        val exact = acquire(s, "r", source("1", 16 * mib))
        assertTrue(exact.accepted); assertEquals(224 * mib, core.totals(exact.state)!!.processBytes)
        denied(s, ResourceEvent.Acquire("r", source("1", 16 * mib + 4), ResourceReferenceKind.TARGET))
    }
    @Test fun snapshotsDefensivelyCopyCollectionsAndRejectMalformedLedger() {
        val valid = acquire().state
        val allocations = valid.allocations.toMutableList(); val refs = valid.references.toMutableList()
        val snapshot = ResourceState(allocations, refs, valid.scratch, valid.nextId)
        allocations.clear(); refs.clear()
        assertEquals(1, snapshot.allocations.size); assertEquals(1, snapshot.references.size)
        val malformed = ResourceState(valid.allocations, listOf(valid.references.single().copy(allocationId = 999)), valid.scratch, valid.nextId)
        assertNull(core.totals(malformed)); denied(malformed, ResourceEvent.DecodeCompleted(valid.scratch!!.operation))
        val duplicate = ResourceState(valid.allocations + valid.allocations, valid.references, valid.scratch, valid.nextId)
        denied(duplicate, ResourceEvent.DecodeCompleted(valid.scratch!!.operation))
    }
    @Test fun releasedReferencesDoNotErasePlanMembershipOrItsBudget() {
        var s = ResourceState()
        for (key in listOf("a", "b", "c")) s = ready(acquire(s, v = source(key, 32 * mib)).state)
        for (r in s.references.toList()) s = core.decide(s, ResourceEvent.ReleaseReference(r)).state
        assertTrue(s.references.isEmpty()); assertEquals(96 * mib, core.totals(s)!!.planBytes["p"])
        denied(s, ResourceEvent.Acquire("p", source("d"), ResourceReferenceKind.MATERIAL))
    }
    @Test fun sharedAllocationStillRequiresDestinationPlanBudget() {
        var s = ResourceState()
        for (key in listOf("a", "b", "c")) s = ready(acquire(s, v = source(key, 32 * mib)).state)
        s = ready(acquire(s, "q", source("d", 32 * mib)).state)
        denied(s, ResourceEvent.Acquire("p", source("d", 32 * mib), ResourceReferenceKind.MATERIAL))
        assertEquals(128 * mib, core.totals(s)!!.residentBytes)
    }
    @Test fun sharedAliasesKeepCanonicalContradictionEvidence() {
        val original = ready(acquire().state)
        val alias = source().copy(canonicalSource = "alias", proofScope = "second-verifier")
        val shared = acquire(original, v = alias).state
        denied(shared, ResourceEvent.Acquire("p", alias.copy(encodedLength = 101), ResourceReferenceKind.TARGET))
        assertEquals(1, original.allocations.single().sources.size)
        assertEquals(2, shared.allocations.single().sources.size)
    }
    @Test fun oldScratchAcknowledgementsCannotReleaseNewOperation() {
        val first = acquire().state; val old = first.scratch!!.operation
        val next = acquire(ready(first), v = source("b")).state
        denied(next, ResourceEvent.DecodeCompleted(old)); denied(next, ResourceEvent.ScratchReleased(old))
        denied(next, ResourceEvent.DecodeCompleted(next.scratch!!.operation.copy(planId = "wrong")))
    }
    @Test fun nestedStateCommandsAndTotalsAreImmutableSnapshots() {
        val d = acquire(); val a = d.state.allocations.single()
        val sources = a.sources.toMutableList(); val plans = a.planIds.toMutableList()
        val allocation = ResourceAllocation(a.id, a.ownership, a.width, a.height, sources, plans)
        sources.clear(); plans.clear()
        assertEquals(1, allocation.sources.size); assertEquals(listOf("p"), allocation.planIds)
        val commands = d.commands.toMutableList(); val decision = ResourceDecision(d.state, true, "test", commands)
        commands.clear(); assertEquals(2, decision.commands.size)
        val bytes = mutableMapOf("p" to 4L); val counts = mutableMapOf("p" to 1)
        val totals = ResourceTotals(4, 8, 1, bytes, counts); bytes.clear(); counts.clear()
        assertEquals(4L, totals.planBytes["p"]); assertEquals(1, totals.planAllocations["p"])
        try { (decision.commands as MutableList).clear(); fail("mutable commands") } catch (_: UnsupportedOperationException) { }
        try { (allocation.sources as MutableList).clear(); fail("mutable sources") } catch (_: UnsupportedOperationException) { }
    }
    @Test fun malformedScratchIdsAndAllocationFactsFailClosed() {
        val s = acquire().state; val a = s.allocations.single(); val scratch = s.scratch!!
        val bad = listOf(
            ResourceState(s.allocations, s.references, scratch.copy(bytes = scratch.bytes + 1), s.nextId),
            ResourceState(s.allocations, s.references, scratch.copy(decodeCompleted = true), s.nextId),
            ResourceState(s.allocations, s.references, null, s.nextId),
            ResourceState(s.allocations, s.references + s.references, scratch, s.nextId),
            ResourceState(s.allocations, s.references, scratch, a.id),
            ResourceState(listOf(ResourceAllocation(a.id, a.ownership, Long.MAX_VALUE, 2, a.sources, a.planIds)), s.references, scratch, s.nextId),
            ResourceState(listOf(ResourceAllocation(a.id, a.ownership, a.width, a.height, a.sources + a.sources, a.planIds)), s.references, scratch, s.nextId),
        )
        for (invalid in bad) {
            assertNull(core.totals(invalid)); denied(invalid, ResourceEvent.DecodeCompleted(scratch.operation))
        }
    }
    @Test fun allocationAndOperationSequenceOverflowFailsClosed() {
        val s = ResourceState(nextId = Long.MAX_VALUE)
        denied(s, ResourceEvent.Acquire("p", source(), ResourceReferenceKind.TARGET))
    }
}
