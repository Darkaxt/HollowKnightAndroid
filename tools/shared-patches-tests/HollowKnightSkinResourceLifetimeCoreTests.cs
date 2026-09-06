using System;
using System.Collections.Generic;
using System.Linq;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinResourceLifetimeCoreTests
{
    private readonly SkinResourceLifetimeCore core = new();
    private readonly SkinResourceCore admission = new();
    private static ResourceLifetimePlan Plan(int n) => new($"p{n}", "binding", $"generation{n}", $"stamp{n}");
    private static VerifiedResourceSource Source(int n) => new("verified", $"source{n}", n + 1L, new string('a', 64), 1, 1);
    private ResourceLifetimeDecision Step(ResourceLifetimeState s, ResourceLifetimeEvent e)
    { var d = core.Decide(s, e); Assert.True(d.Accepted, d.Diagnosis); return d; }
    private void Denied(ResourceLifetimeState s, ResourceLifetimeEvent e)
    { var d = core.Decide(s, e); Assert.False(d.Accepted); Assert.Same(s, d.State); Assert.Empty(d.Commands); }
    private ResourceLifetimeState Begin(ResourceLifetimeState s = null, int n = 1) => Step(s ?? new(), new ResourceLifetimeEvent.BeginCandidate($"tx{n}", Plan(n))).State;
    private ResourceLifetimeState Acquire(ResourceLifetimeState s, int n, bool borrowed = false, ResourceReferenceKind kind = ResourceReferenceKind.TARGET) =>
        Step(s, new ResourceLifetimeEvent.Admission(borrowed ? new ResourceEvent.AcquireBorrowed(s.Candidate.Target.PlanId, "default", 1, 1, kind) :
            new ResourceEvent.Acquire(s.Candidate.Target.PlanId, Source(n), kind))).State;
    private ResourceLifetimeState Ready(ResourceLifetimeState s)
    {
        var op = s.Ledger.Scratch?.Operation; if (op == null) return s;
        var d = Step(s, new ResourceLifetimeEvent.Admission(new ResourceEvent.DecodeCompleted(op)));
        Assert.IsType<ResourceLifetimeCommand.Admission>(Assert.Single(d.Commands));
        return Step(d.State, new ResourceLifetimeEvent.Admission(new ResourceEvent.ScratchReleased(op))).State;
    }
    private ResourceLifetimeState Seal(ResourceLifetimeState s) => Step(s, new ResourceLifetimeEvent.SealCandidate(s.Candidate)).State;
    private ResourceLifetimeDecision Commit(ResourceLifetimeState s)
    {
        var pending = Step(Seal(s), new ResourceLifetimeEvent.AwaitDurableCompletion(s.Candidate)).State;
        return Step(pending, new ResourceLifetimeEvent.DurableCompletionVerified(s.Candidate));
    }
    private ResourceLifetimeState Active(bool borrowed = false) => Commit(Ready(Acquire(Begin(), 1, borrowed))).State;
    private ResourceLifetimeDecision Displaced(ResourceLifetimeState s = null, bool shared = false) => Commit(Ready(Acquire(Begin(s ?? Active(), 2), shared ? 1 : 2)));
    private ResourceLifetimeDecision Release(ResourceLifetimeState s, string p)
    {
        var d = new ResourceLifetimeDecision(s, true, "test");
        foreach (var r in s.Ledger.References.Where(r => r.PlanId == p)) d = Step(d.State, new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(r)));
        return d;
    }
    private ResourceLifetimeDecision Clones(ResourceLifetimeState s) => Step(s, new ResourceLifetimeEvent.CompanionClonesInvalidated(s.AwaitingClones));

    [Fact] public void consumedValueApplyGateBlocksWhileDurableClosureIsPending()
    {
        var sealedState = Seal(Ready(Acquire(Begin(), 1))); var c = sealedState.Candidate;
        Assert.True(core.CanApply(sealedState)); Denied(sealedState, new ResourceLifetimeEvent.DurableCompletionVerified(c));
        Denied(sealedState, new ResourceLifetimeEvent.AwaitDurableCompletion(c with { TransactionId = "wrong" }));
        var d = Step(sealedState, new ResourceLifetimeEvent.AwaitDurableCompletion(c));
        Assert.Empty(d.Commands); Assert.True(d.State.AwaitingDurableCompletion);
        Assert.False(core.CanApply(d.State)); Assert.False(core.CanBeginCandidate(d.State));
        Denied(d.State, new ResourceLifetimeEvent.AwaitDurableCompletion(c));
        Denied(d.State, new ResourceLifetimeEvent.BeginCandidate("tx2", Plan(2)));
        Assert.Null(d.State.Active); Assert.Null(d.State.Retirement); Assert.Null(d.State.AwaitingClones);
        Denied(d.State, new ResourceLifetimeEvent.SealCandidate(c));
        Denied(d.State, new ResourceLifetimeEvent.Admission(new ResourceEvent.Acquire("p1", Source(1), ResourceReferenceKind.TARGET)));
        var released = Release(d.State, "p1").State; Assert.False(core.CanApply(released)); Assert.True(released.AwaitingDurableCompletion);
        Assert.Equal(Plan(1), Step(released, new ResourceLifetimeEvent.DurableCompletionVerified(c)).State.Active);
        var malformed = new ResourceLifetimeState(sealedState.Ledger, candidate: c, nextOperationId: sealedState.NextOperationId, awaitingDurableCompletion: true);
        Denied(malformed, new ResourceLifetimeEvent.DurableCompletionVerified(c));
    }
    [Fact] public void differentBindingReplacementRetainsExactPriorCloneIdentity()
    {
        var old = Active(); var target = Plan(2) with { Binding = "new-binding", GenerationId = Plan(1).GenerationId, Stamp = Plan(1).Stamp };
        var candidate = Step(old, new ResourceLifetimeEvent.BeginCandidate("rebind", target)).State;
        var waiting = Commit(Ready(Acquire(candidate, 2))).State; var c = waiting.AwaitingClones;
        Assert.Equal(Plan(1), c.Prior); Assert.Equal(target, c.Target);
        Denied(waiting, new ResourceLifetimeEvent.CompanionClonesInvalidated(c with { Prior = c.Prior with { Binding = target.Binding } }));
        var pending = Release(Clones(waiting).State, "p1").State;
        var drained = Step(pending, new ResourceLifetimeEvent.DisposeAcknowledged(Assert.Single(pending.PendingDisposals))).State;
        Assert.Equal(target, drained.Active); Assert.True(core.CanBeginCandidate(drained));
    }
    [Fact] public void firstActivationNeedsNoPriorCloneProof()
    {
        var s = Active(); Assert.Equal(Plan(1), s.Active); Assert.Null(s.AwaitingClones); Assert.Null(s.Retirement);
        Assert.Null(s.Candidate); Assert.False(core.CanApply(s)); Assert.True(core.CanBeginCandidate(s));
    }
    [Fact] public void preparationAndScratchMustFinishBeforeValueApplyGate()
    {
        var s = Acquire(Begin(), 1); Assert.False(core.CanApply(s));
        Denied(s, new ResourceLifetimeEvent.SealCandidate(s.Candidate)); Denied(s, new ResourceLifetimeEvent.DurableCompletionVerified(s.Candidate));
        var op = s.Ledger.Scratch.Operation;
        var decoded = Step(s, new ResourceLifetimeEvent.Admission(new ResourceEvent.DecodeCompleted(op))).State;
        Denied(decoded, new ResourceLifetimeEvent.SealCandidate(decoded.Candidate));
        Denied(decoded, new ResourceLifetimeEvent.Admission(new ResourceEvent.Acquire("p1", Source(2), ResourceReferenceKind.TARGET)));
        var sealedState = Seal(Step(decoded, new ResourceLifetimeEvent.Admission(new ResourceEvent.ScratchReleased(op))).State);
        Assert.True(core.CanApply(sealedState));
        Denied(sealedState, new ResourceLifetimeEvent.Admission(new ResourceEvent.Acquire("p1", Source(1), ResourceReferenceKind.TARGET)));
    }
    [Fact] public void durableAndCloneEvidenceMustMatchEntireStoredClosure()
    {
        var prepared = Seal(Ready(Acquire(Begin(Active(), 2), 2))); var c = prepared.Candidate;
        var s = Step(prepared, new ResourceLifetimeEvent.AwaitDurableCompletion(c)).State;
        var wrong = new[] { c with { OperationId = c.OperationId + 1 }, c with { TransactionId = "other" },
            c with { Prior = c.Prior with { Stamp = "wrong" } }, c with { Target = c.Target with { GenerationId = "wrong" } },
            c with { Target = c.Target with { Binding = "wrong" } }, c with { Target = c.Target with { PlanId = "wrong" } } };
        foreach (var v in wrong) Denied(s, new ResourceLifetimeEvent.DurableCompletionVerified(v));
        var d = Step(s, new ResourceLifetimeEvent.DurableCompletionVerified(c));
        Assert.IsType<ResourceLifetimeCommand.InvalidateCompanionClones>(Assert.Single(d.Commands));
        Assert.Null(d.State.Retirement); Assert.Equal(c, d.State.AwaitingClones);
        Denied(d.State, new ResourceLifetimeEvent.DurableCompletionVerified(c));
        foreach (var v in wrong) Denied(d.State, new ResourceLifetimeEvent.CompanionClonesInvalidated(v));
        Denied(d.State, new ResourceLifetimeEvent.RegisteredCloneCountVerified(c, 1));
        Denied(d.State, new ResourceLifetimeEvent.RegisteredCloneCountVerified(c with { OperationId = 999 }, 0));
        var qualified = Step(d.State, new ResourceLifetimeEvent.RegisteredCloneCountVerified(c, 0)).State;
        Assert.Equal(c, qualified.Retirement); Denied(qualified, new ResourceLifetimeEvent.CompanionClonesInvalidated(c));
    }
    [Fact] public void pendingClosureClonesAndRetirementEachBlockNextCandidate()
    {
        var s = Begin(Active(), 2); Denied(s, new ResourceLifetimeEvent.BeginCandidate("tx3", Plan(3)));
        var d = Displaced(); Assert.False(core.CanBeginCandidate(d.State)); Assert.False(core.CanApply(d.State));
        Denied(d.State, new ResourceLifetimeEvent.BeginCandidate("tx3", Plan(3)));
        Denied(Clones(d.State).State, new ResourceLifetimeEvent.BeginCandidate("tx3", Plan(3)));
    }
    [Fact] public void allReferenceKindsDelayDisposalAndLastReleaseEmitsOnce()
    {
        var s = Begin(); foreach (ResourceReferenceKind k in Enum.GetValues(typeof(ResourceReferenceKind))) s = Acquire(s, 1, kind: k);
        s = Clones(Displaced(Commit(Ready(s)).State).State).State; Assert.Empty(s.PendingDisposals);
        var refs = s.Ledger.References.Where(r => r.PlanId == "p1").ToArray();
        for (var i = 0; i < refs.Length; i++)
        {
            var d = Step(s, new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(refs[i]))); s = d.State;
            Assert.Equal(i == refs.Length - 1 ? 1 : 0, d.Commands.Count);
            Denied(s, new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(refs[i])));
        }
        Assert.Single(s.PendingDisposals);
    }
    [Fact] public void disposeCommandKeepsChargesUntilExactAcknowledgement()
    {
        var d = Release(Clones(Displaced().State).State, "p1"); var op = Assert.Single(d.State.PendingDisposals);
        Assert.Equal(8, admission.Totals(d.State.Ledger).ResidentBytes); Assert.Equal(4, admission.Totals(d.State.Ledger).PlanBytes["p1"]);
        Assert.Equal(op, Assert.IsType<ResourceLifetimeCommand.Dispose>(Assert.Single(d.Commands)).Operation);
        foreach (var wrong in new[] { op with { OperationId = op.OperationId + 1 }, op with { AllocationId = 999 },
            op with { Closure = op.Closure with { TransactionId = "wrong" } } }) Denied(d.State, new ResourceLifetimeEvent.DisposeAcknowledged(wrong));
        var drained = Step(d.State, new ResourceLifetimeEvent.DisposeAcknowledged(op)).State;
        Assert.Equal(4, admission.Totals(drained.Ledger).ResidentBytes); Assert.Null(drained.Retirement);
        Denied(drained, new ResourceLifetimeEvent.DisposeAcknowledged(op)); Assert.True(core.CanBeginCandidate(drained));
    }
    [Fact] public void sharedActiveAllocationSurvivesQualifiedOldMembershipClosure()
    {
        var d = Release(Clones(Displaced(shared: true).State).State, "p1"); Assert.Empty(d.Commands); Assert.Null(d.State.Retirement);
        Assert.Equal(new[] { "p2" }, Assert.Single(d.State.Ledger.Allocations).PlanIds);
        Assert.Single(d.State.Ledger.References); Assert.Equal(4, admission.Totals(d.State.Ledger).ResidentBytes);
    }
    [Fact] public void borrowedDefaultsAreForgottenWithoutDisposal()
    {
        var d = Release(Clones(Displaced(Active(borrowed: true)).State).State, "p1"); Assert.Empty(d.Commands); Assert.Null(d.State.Retirement);
        Assert.All(d.State.Ledger.Allocations, a => Assert.Equal(ResourceOwnership.OWNED, a.Ownership));
    }
    [Fact] public void disposalPendingCannotBeReacquiredAndUnqualifiedReleaseNeverFrees()
    {
        var d = Release(Displaced().State, "p1"); Assert.Empty(d.Commands); Assert.Equal(2, d.State.Ledger.Allocations.Count);
        var pending = Clones(d.State).State;
        Denied(pending, new ResourceLifetimeEvent.Admission(new ResourceEvent.Acquire("p2", Source(1), ResourceReferenceKind.TARGET)));
        Assert.Equal(2, pending.Ledger.Allocations.Count);
    }
    [Fact] public void repeatedCyclesPreserveHighwatersAndRejectOldAcks()
    {
        var s = Active(); ResourceDisposalOperation old = null; var lastResource = s.Ledger.NextId; var lastOperation = s.NextOperationId;
        for (var n = 2; n <= 8; n++)
        {
            s = Commit(Ready(Acquire(Begin(s, n), n))).State; s = Release(Clones(s).State, $"p{n - 1}").State;
            var op = Assert.Single(s.PendingDisposals); if (old != null) Denied(s, new ResourceLifetimeEvent.DisposeAcknowledged(old));
            s = Step(s, new ResourceLifetimeEvent.DisposeAcknowledged(op)).State;
            Assert.True(s.Ledger.NextId > lastResource); Assert.True(s.NextOperationId > lastOperation);
            Assert.Single(s.Ledger.Allocations); Assert.True(core.CanBeginCandidate(s));
            lastResource = s.Ledger.NextId; lastOperation = s.NextOperationId; old = op;
        }
    }
    [Fact] public void emptyGenerationDrainStillPreservesOperationSequence()
    {
        var s = Commit(Begin(Active(), 2)).State; s = Release(Clones(s).State, "p1").State;
        var op = Assert.Single(s.PendingDisposals); s = Step(s, new ResourceLifetimeEvent.DisposeAcknowledged(op)).State;
        Assert.Empty(s.Ledger.Allocations); Assert.True(s.Ledger.NextId > 1);
        var next = Begin(s, 3); Assert.True(next.Candidate.OperationId > op.OperationId);
        Denied(next, new ResourceLifetimeEvent.DisposeAcknowledged(op));
    }
    [Fact] public void invalidStateAndOverflowFailClosed()
    {
        Denied(new ResourceLifetimeState(nextOperationId: long.MaxValue), new ResourceLifetimeEvent.BeginCandidate("tx", Plan(1)));
        var active = Active(); var bad = new ResourceLifetimeState(active.Ledger, nextOperationId: active.NextOperationId);
        Denied(bad, new ResourceLifetimeEvent.BeginCandidate("tx", Plan(2))); Assert.False(core.CanBeginCandidate(bad));
        Denied(new(), new ResourceLifetimeEvent.BeginCandidate("", Plan(1)));
        Denied(active, new ResourceLifetimeEvent.BeginCandidate("tx", Plan(2) with { Stamp = Plan(1).Stamp }));
        var pending = Release(Clones(Displaced().State).State, "p1").State;
        var invalid = new ResourceLifetimeState(pending.Ledger, pending.Active, retirement: pending.Retirement,
            pendingDisposals: pending.PendingDisposals.Concat(pending.PendingDisposals), nextOperationId: pending.NextOperationId);
        Denied(invalid, new ResourceLifetimeEvent.DisposeAcknowledged(Assert.Single(pending.PendingDisposals)));
    }
    [Fact] public void impossibleActiveHighwaterIsRejected()
    {
        var a = Active(); var reset = new ResourceLifetimeState(a.Ledger, a.Active, nextOperationId: 1);
        Denied(reset, new ResourceLifetimeEvent.BeginCandidate("next", Plan(2)));
    }
    [Fact] public void activeDecoderSnapshotIsRejected()
    {
        var a = Active();
        var candidate = Acquire(Begin(a, 2), 2); var scratch = candidate.Ledger.Scratch;
        var allocations = candidate.Ledger.Allocations.Select(x => x.Id != scratch.Operation.AllocationId ? x :
            new ResourceAllocation(x.Id, x.Ownership, x.Width, x.Height, x.Sources, new[] { "p1", "p2" }, phase: x.Phase));
        var forged = new ResourceLifetimeState(new ResourceState(allocations, candidate.Ledger.References, scratch, candidate.Ledger.NextId),
            candidate.Active, candidate.Candidate, nextOperationId: candidate.NextOperationId);
        Denied(forged, new ResourceLifetimeEvent.Admission(new ResourceEvent.DecodeCompleted(scratch.Operation)));
    }
    [Fact] public void severalDisposalsAcknowledgeIndependentlyWithoutReemission()
    {
        var a = Ready(Acquire(Begin(), 1)); a = Ready(Acquire(a, 3)); a = Commit(a).State;
        var d = Clones(Release(Displaced(a).State, "p1").State);
        Assert.Equal(2, d.Commands.Count); Assert.Equal(2, d.State.PendingDisposals.Count); var ops = d.State.PendingDisposals;
        Denied(d.State, new ResourceLifetimeEvent.DisposeAcknowledged(ops[0] with { AllocationId = ops[1].AllocationId }));
        var unrelatedRelease = Release(d.State, "p2"); Assert.Empty(unrelatedRelease.Commands);
        var first = Step(unrelatedRelease.State, new ResourceLifetimeEvent.DisposeAcknowledged(ops[1]));
        Assert.Empty(first.Commands); Assert.NotNull(first.State.Retirement); Assert.Equal(8, admission.Totals(first.State.Ledger).ResidentBytes);
        Denied(first.State, new ResourceLifetimeEvent.DisposeAcknowledged(ops[1]));
        var last = Step(first.State, new ResourceLifetimeEvent.DisposeAcknowledged(ops[0]));
        Assert.Empty(last.Commands); Assert.Null(last.State.Retirement); Assert.Equal(4, admission.Totals(last.State.Ledger).ResidentBytes);
    }
    [Fact] public void disposalSequenceOverflowCannotPartiallyReleaseOrQualify()
    {
        var waiting = Release(Displaced().State, "p1").State;
        var max = new ResourceLifetimeState(waiting.Ledger, waiting.Active, awaitingClones: waiting.AwaitingClones, nextOperationId: long.MaxValue);
        Denied(max, new ResourceLifetimeEvent.CompanionClonesInvalidated(max.AwaitingClones)); Assert.Equal(8, admission.Totals(max.Ledger).ResidentBytes);
        var retiring = Clones(Displaced().State).State;
        var maxRetired = new ResourceLifetimeState(retiring.Ledger, retiring.Active, retirement: retiring.Retirement, nextOperationId: long.MaxValue);
        Denied(maxRetired, new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(maxRetired.Ledger.References.First(r => r.PlanId == "p1"))));
    }
    [Fact] public void snapshotsAndCommandCollectionsAreImmutable()
    {
        var pending = Release(Clones(Displaced().State).State, "p1"); var mutable = pending.State.PendingDisposals.ToList();
        var copy = new ResourceLifetimeState(pending.State.Ledger, pending.State.Active, retirement: pending.State.Retirement,
            pendingDisposals: mutable, nextOperationId: pending.State.NextOperationId);
        mutable.Clear(); Assert.Single(copy.PendingDisposals);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceDisposalOperation>)copy.PendingDisposals).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceLifetimeCommand>)pending.Commands).Clear());
    }
    [Fact] public void malformedClrLifetimeValuesFailClosed()
    {
        var s = new ResourceLifetimeState(); Denied(s, null); Denied(s, new ResourceLifetimeEvent.BeginCandidate("tx", null));
        Denied(s, new ResourceLifetimeEvent.Admission(null)); Denied(s, new ResourceLifetimeEvent.DurableCompletionVerified(null));
        Denied(s, new ResourceLifetimeEvent.DisposeAcknowledged(null));
        Denied(new ResourceLifetimeState(pendingDisposals: new ResourceDisposalOperation[] { null }), new ResourceLifetimeEvent.BeginCandidate("tx", Plan(1)));
    }
}
