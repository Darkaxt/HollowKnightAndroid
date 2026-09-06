using System;
using System.Collections.Generic;
using System.Linq;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinResourceCleanupCoreTests
{
    private readonly SkinResourceCleanupCore core = new();
    private readonly SkinResourceCore admission = new();
    private static ResourceLifetimePlan Plan(int n) => new($"p{n}", "binding", $"g{n}", $"s{n}");
    private static VerifiedResourceSource Source(int n = 1) => new("verified", $"source{n}", n, new string('a',64),1,1);
    private ResourceCleanupDecision Step(ResourceCleanupState s, ResourceCleanupEvent e) { var d=core.Decide(s,e); Assert.True(d.Accepted,d.Diagnosis); return d; }
    private void Denied(ResourceCleanupState s, ResourceCleanupEvent e) { var d=core.Decide(s,e); Assert.False(d.Accepted); Assert.Same(s,d.State); Assert.Empty(d.Commands); }
    private ResourceCleanupState Live(ResourceCleanupState s, ResourceLifetimeEvent e) => Step(s,new ResourceCleanupEvent.Lifetime(e)).State;
    private ResourceCleanupState Begin(ResourceCleanupState s=null,int n=1) => Live(s??new(),new ResourceLifetimeEvent.BeginCandidate($"tx{n}",Plan(n)));
    private ResourceCleanupState Acquire(ResourceCleanupState s,int n=1,bool borrowed=false,ResourceReferenceKind kind=ResourceReferenceKind.TARGET) => Live(s,new ResourceLifetimeEvent.Admission(borrowed ? new ResourceEvent.AcquireBorrowed(s.Lifetime.Candidate.Target.PlanId,"default",1,1,kind) : new ResourceEvent.Acquire(s.Lifetime.Candidate.Target.PlanId,Source(n),kind)));
    private ResourceCleanupState Ready(ResourceCleanupState s) { var op=s.Lifetime.Ledger.Scratch?.Operation; return op==null?s:Live(Live(s,new ResourceLifetimeEvent.Admission(new ResourceEvent.DecodeCompleted(op))),new ResourceLifetimeEvent.Admission(new ResourceEvent.ScratchReleased(op))); }
    private ResourceCleanupDecision Cancel(ResourceCleanupState s) => Step(s,new ResourceCleanupEvent.CancelPreparation(s.Lifetime.Candidate));
    private ResourceCleanupDecision Release(ResourceCleanupState s,string p="p1") { var d=new ResourceCleanupDecision(s,true,"test"); foreach(var r in s.Lifetime.Ledger.References.Where(r=>r.PlanId==p)) d=Step(d.State,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(r)))); return d; }
    private ResourceCleanupState Active() { var s=Ready(Acquire(Begin())); var c=s.Lifetime.Candidate; s=Live(s,new ResourceLifetimeEvent.SealCandidate(c)); s=Live(s,new ResourceLifetimeEvent.AwaitDurableCompletion(c)); return Live(s,new ResourceLifetimeEvent.DurableCompletionVerified(c)); }

    [Fact] public void emptyAndSealedCancellationReturnReadyWithoutWrites() {
        foreach(var seal in new[]{false,true}) { var s=Begin(); var c=s.Lifetime.Candidate; if(seal)s=Live(s,new ResourceLifetimeEvent.SealCandidate(c)); var d=Cancel(s); Assert.Empty(d.Commands); Assert.Null(d.State.Lifetime.Candidate); Assert.True(core.CanBeginCandidate(d.State)); Assert.Equal(s.Lifetime.NextOperationId,d.State.Lifetime.NextOperationId); Denied(d.State,new ResourceCleanupEvent.CancelPreparation(c)); }
    }
    [Fact] public void stopIntentRetainsReservationAndFreezesCandidate() {
        var s=Acquire(Begin()); var d=Cancel(s); var op=s.Lifetime.Ledger.Scratch.Operation;
        Assert.Equal(op,Assert.IsType<ResourceCleanupCommand.RequestDecoderStop>(Assert.Single(d.Commands)).Operation);
        Assert.Equal(8,admission.Totals(d.State.Lifetime.Ledger).ProcessBytes); Assert.False(core.CanApply(d.State)); Assert.False(core.CanBeginCandidate(d.State));
        Denied(d.State,new ResourceCleanupEvent.CancelPreparation(s.Lifetime.Candidate)); Denied(d.State,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.BeginCandidate("tx2",Plan(2))));
        Denied(d.State,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.Admission(new ResourceEvent.Acquire("p1",Source(),ResourceReferenceKind.TARGET))));
        Denied(d.State,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.SealCandidate(s.Lifetime.Candidate)));
    }
    [Fact] public void neverCreatedWaitsForScratchAndExactReferencesInEitherOrder() {
        foreach(var scratchFirst in new[]{false,true}) { var initial=Acquire(Begin()); var c=initial.Lifetime.Candidate; var op=initial.Lifetime.Ledger.Scratch.Operation; var s=Cancel(initial).State;
            if(scratchFirst)s=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)).State;
            s=Step(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.NEVER_CREATED)).State; Assert.Equal(8,admission.Totals(s.Lifetime.Ledger).ProcessBytes);
            if(!scratchFirst)s=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)).State;
            var d=Release(s); Assert.Empty(d.Commands); Assert.Empty(d.State.Lifetime.Ledger.Allocations); Assert.Null(d.State.Lifetime.Ledger.Scratch); Assert.True(core.CanBeginCandidate(d.State)); Assert.Equal(initial.Lifetime.Ledger.NextId,d.State.Lifetime.Ledger.NextId);
            Denied(d.State,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.CREATED));
        }
    }
    [Fact] public void lateCreatedRequiresScratchThenDisposalAcknowledgement() {
        foreach(var scratchFirst in new[]{false,true}) { var initial=Acquire(Begin()); var c=initial.Lifetime.Candidate; var op=initial.Lifetime.Ledger.Scratch.Operation; var s=Release(Cancel(initial).State).State;
            if(scratchFirst)s=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)).State;
            var d=Step(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.CREATED)); s=d.State;
            if(!scratchFirst) { Assert.Empty(s.PendingDisposals); d=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)); s=d.State; }
            var disposal=Assert.Single(s.PendingDisposals); Assert.Contains(d.Commands,x=>x is ResourceCleanupCommand.Dispose); Assert.Equal(4,admission.Totals(s.Lifetime.Ledger).ResidentBytes);
            Denied(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.NEVER_CREATED)); Denied(s,new ResourceCleanupEvent.DisposeAcknowledged(disposal with {OperationId=disposal.OperationId+1}));
            var drained=Step(s,new ResourceCleanupEvent.DisposeAcknowledged(disposal)).State; Assert.True(core.CanBeginCandidate(drained)); Assert.Equal(0,admission.Totals(drained.Lifetime.Ledger).ProcessBytes); Denied(drained,new ResourceCleanupEvent.DisposeAcknowledged(disposal));
        }
    }
    [Fact] public void alreadyCreatedCannotBecomeNeverCreated() {
        var s=Acquire(Begin()); var c=s.Lifetime.Candidate; var op=s.Lifetime.Ledger.Scratch.Operation; s=Live(s,new ResourceLifetimeEvent.Admission(new ResourceEvent.DecodeCompleted(op))); var d=Cancel(s); Assert.Empty(d.Commands); s=d.State;
        Denied(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.NEVER_CREATED)); Denied(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.CREATED)); s=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)).State; Assert.Single(Release(s).State.PendingDisposals);
    }
    [Fact] public void readyCandidateWaitsForEveryReferenceKindAndDisposesOnce() {
        var s=Begin(); foreach(ResourceReferenceKind k in Enum.GetValues(typeof(ResourceReferenceKind)))s=Acquire(s,kind:k); s=Cancel(Ready(s)).State;
        var refs=s.Lifetime.Ledger.References; for(var i=0;i<refs.Count;i++) { var d=Step(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(refs[i])))); s=d.State; Assert.Equal(i==refs.Count-1?1:0,d.Commands.Count); Denied(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(refs[i])))); } Assert.Single(s.PendingDisposals);
    }
    [Fact] public void sharedActiveMembershipAndReferencesSurviveCancel() {
        var a=Active(); var s=Release(Cancel(Acquire(Begin(a,2))).State,"p2").State; Assert.Equal(Plan(1),s.Lifetime.Active); Assert.Equal(new[]{"p1"},Assert.Single(s.Lifetime.Ledger.Allocations).PlanIds); Assert.Equal(a.Lifetime.Ledger.References,s.Lifetime.Ledger.References); Assert.Empty(s.PendingDisposals); Assert.True(core.CanBeginCandidate(s));
    }
    [Fact] public void borrowedDefaultsAreUntrackedNeverDisposed() { var d=Release(Cancel(Acquire(Begin(),borrowed:true)).State); Assert.Empty(d.Commands); Assert.Empty(d.State.Lifetime.Ledger.Allocations); Assert.True(core.CanBeginCandidate(d.State)); }
    [Fact] public void consumedGateCannotBeCancelledAndExactSuccessStillProgresses() {
        var s=Ready(Acquire(Begin(Active(),2),2)); var c=s.Lifetime.Candidate; s=Live(s,new ResourceLifetimeEvent.SealCandidate(c)); s=Live(s,new ResourceLifetimeEvent.AwaitDurableCompletion(c)); Denied(s,new ResourceCleanupEvent.CancelPreparation(c)); Assert.Equal(Plan(1),s.Lifetime.Active); s=Live(s,new ResourceLifetimeEvent.DurableCompletionVerified(c)); Assert.NotNull(s.Lifetime.AwaitingClones); Denied(s,new ResourceCleanupEvent.CancelPreparation(c));
    }
    [Fact] public void fullClosureAndDecoderCorrelationRejectStaleFacts() {
        var initial=Acquire(Begin()); var c=initial.Lifetime.Candidate; var op=initial.Lifetime.Ledger.Scratch.Operation; var s=Cancel(initial).State;
        foreach(var wrong in new[]{c with{OperationId=9},c with{TransactionId="wrong"},c with{Target=c.Target with{Binding="wrong"}},c with{Target=c.Target with{GenerationId="wrong"}},c with{Target=c.Target with{Stamp="wrong"}},c with{Target=c.Target with{PlanId="wrong"}}}) Denied(s,new ResourceCleanupEvent.DecodeResolved(wrong,op,ResourceDecodeOutcome.CREATED));
        foreach(var wrong in new[]{op with{OperationId=99},op with{AllocationId=99},op with{PlanId="wrong"}}) { Denied(s,new ResourceCleanupEvent.DecodeResolved(c,wrong,ResourceDecodeOutcome.NEVER_CREATED)); Denied(s,new ResourceCleanupEvent.ScratchReleased(c,wrong)); }
        var resolved=Step(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.NEVER_CREATED)).State; Denied(resolved,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.CREATED)); Denied(resolved,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.NEVER_CREATED));
    }
    [Fact] public void repeatedDrainPreservesBothHighwatersAndRejectsOldDisposals() {
        var s=new ResourceCleanupState(); ResourceDisposalOperation old=null; long rid=1,oid=1;
        for(var i=0;i<6;i++) { s=Release(Cancel(Ready(Acquire(Begin(s)))).State).State; if(old!=null)Denied(s,new ResourceCleanupEvent.DisposeAcknowledged(old)); var op=Assert.Single(s.PendingDisposals); s=Step(s,new ResourceCleanupEvent.DisposeAcknowledged(op)).State; Assert.True(s.Lifetime.Ledger.NextId>rid); Assert.True(s.Lifetime.NextOperationId>oid); rid=s.Lifetime.Ledger.NextId; oid=s.Lifetime.NextOperationId; old=op; }
    }
    [Fact] public void overflowRejectsEntireCleanupTransition() { var l=Release(Ready(Acquire(Begin()))).State.Lifetime; var s=new ResourceCleanupState(new ResourceLifetimeState(l.Ledger,l.Active,l.Candidate,nextOperationId:long.MaxValue)); Denied(s,new ResourceCleanupEvent.CancelPreparation(l.Candidate)); Assert.Equal(4,admission.Totals(s.Lifetime.Ledger).ResidentBytes); }
    [Fact] public void scratchAckIsRequiredEvenAfterNeverCreatedAndReferencesReleased() {
        var initial=Acquire(Begin()); var c=initial.Lifetime.Candidate; var op=initial.Lifetime.Ledger.Scratch.Operation;
        var s=Release(Cancel(initial).State).State; s=Step(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.NEVER_CREATED)).State;
        Assert.Equal(8,admission.Totals(s.Lifetime.Ledger).ProcessBytes); Assert.False(core.CanBeginCandidate(s));
        var d=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)); Assert.Empty(d.Commands); Assert.True(core.CanBeginCandidate(d.State)); Denied(d.State,new ResourceCleanupEvent.ScratchReleased(c,op));
    }
    [Fact] public void sealedReadyCancellationBlocksConsumedGateAndPendingIdentityAcquisition() {
        var s=Ready(Acquire(Begin())); var c=s.Lifetime.Candidate;
        s=Live(s,new ResourceLifetimeEvent.SealCandidate(c)); Assert.True(core.CanApply(s)); s=Cancel(s).State;
        Assert.False(core.CanApply(s)); Denied(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.AwaitDurableCompletion(c)));
        s=Release(s).State; Denied(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.Admission(new ResourceEvent.Acquire("p1",Source(),ResourceReferenceKind.TARGET))));
        var op=Assert.Single(s.PendingDisposals); Denied(s,new ResourceCleanupEvent.DisposeAcknowledged(op with{Closure=c with{Target=c.Target with{Stamp="wrong"}}})); Denied(s,new ResourceCleanupEvent.DisposeAcknowledged(op with{AllocationId=op.AllocationId+1}));
    }
    [Fact] public void multipleDisposalsAndSnapshotCollectionsAreIndependent() {
        var s=Ready(Acquire(Begin())); s=Ready(Acquire(s,2)); var d=Cancel(Release(s).State); Assert.Equal(2,d.State.PendingDisposals.Count);
        var mutable=d.State.PendingDisposals.ToList(); var copy=new ResourceCleanupState(d.State.Lifetime,d.State.Cancellation,pendingDisposals:mutable); mutable.Clear(); Assert.Equal(2,copy.PendingDisposals.Count);
        Assert.Throws<NotSupportedException>(()=>((IList<ResourceDisposalOperation>)copy.PendingDisposals).Clear()); Assert.Throws<NotSupportedException>(()=>((IList<ResourceCleanupCommand>)d.Commands).Clear());
        var first=Step(copy,new ResourceCleanupEvent.DisposeAcknowledged(copy.PendingDisposals[1])); Assert.Empty(first.Commands); Assert.False(core.CanBeginCandidate(first.State));
        var last=Step(first.State,new ResourceCleanupEvent.DisposeAcknowledged(copy.PendingDisposals[0])); Assert.Empty(last.Commands); Assert.True(core.CanBeginCandidate(last.State));
        var malformed=new ResourceCleanupState(copy.Lifetime,copy.Cancellation,pendingDisposals:copy.PendingDisposals.Concat(copy.PendingDisposals)); Denied(malformed,new ResourceCleanupEvent.DisposeAcknowledged(copy.PendingDisposals[0]));
    }
    [Fact] public void lifetimeValidationSeamRejectsMalformedOwnershipWithoutMutation() {
        var lifetime=new SkinResourceLifetimeCore(); var s=Acquire(Begin()); Assert.True(lifetime.IsValidState(s.Lifetime));
        var bad=new ResourceLifetimeState(s.Lifetime.Ledger,nextOperationId:s.Lifetime.NextOperationId); Assert.False(lifetime.IsValidState(bad)); Assert.False(lifetime.IsValidState(null)); Denied(new ResourceCleanupState(bad),new ResourceCleanupEvent.CancelPreparation(s.Lifetime.Candidate));
    }
    [Fact] public void malformedCancellationSnapshotsAreRejected() { var s=Acquire(Begin()); var bad=new ResourceCleanupState(s.Lifetime,s.Lifetime.Candidate,new ResourceCleanupDecoder(s.Lifetime.Ledger.Scratch.Operation with{OperationId=999})); Denied(bad,new ResourceCleanupEvent.CancelPreparation(s.Lifetime.Candidate)); Assert.False(core.CanApply(bad)); Assert.False(core.CanBeginCandidate(bad)); }
    [Fact] public void malformedClrValuesFailClosed() {
        var s=Cancel(Acquire(Begin())).State; var c=s.Cancellation; var op=s.Decoder.Operation;
        Denied(s,null); Denied(s,new ResourceCleanupEvent.Lifetime(null)); Denied(s,new ResourceCleanupEvent.CancelPreparation(null)); Denied(s,new ResourceCleanupEvent.DecodeResolved(c,null,ResourceDecodeOutcome.CREATED)); Denied(s,new ResourceCleanupEvent.DecodeResolved(c,op,(ResourceDecodeOutcome)99)); Denied(s,new ResourceCleanupEvent.DisposeAcknowledged(null));
        Denied(new ResourceCleanupState(s.Lifetime,c,new ResourceCleanupDecoder(op,(ResourceDecodeOutcome)99)),new ResourceCleanupEvent.ScratchReleased(c,op)); Denied(null,new ResourceCleanupEvent.CancelPreparation(c));
        Denied(new ResourceCleanupState(s.Lifetime,c,pendingDisposals:new ResourceDisposalOperation[]{null}),new ResourceCleanupEvent.CancelPreparation(c));
    }
}
