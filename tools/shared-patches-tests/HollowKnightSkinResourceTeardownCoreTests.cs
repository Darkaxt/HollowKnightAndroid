using System;
using System.Linq;
using System.Collections.Generic;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinResourceTeardownCoreTests
{
    private readonly SkinResourceCleanupCore core = new();
    private readonly SkinResourceCore admission = new();
    private static ResourceLifetimePlan Plan(int n,string binding="b") => new($"p{n}",binding,$"g{n}",$"s{n}");
    private ResourceCleanupDecision Step(ResourceCleanupState s,ResourceCleanupEvent e) { var d=core.Decide(s,e); Assert.True(d.Accepted,d.Diagnosis); return d; }
    private void Denied(ResourceCleanupState s,ResourceCleanupEvent e) { var d=core.Decide(s,e); Assert.False(d.Accepted); Assert.Same(s,d.State); Assert.Empty(d.Commands); }
    private ResourceCleanupState Live(ResourceCleanupState s,ResourceLifetimeEvent e) => Step(s,new ResourceCleanupEvent.Lifetime(e)).State;
    private ResourceCleanupState Begin(ResourceCleanupState s=null,int n=1,string binding="b") => Live(s??new(),new ResourceLifetimeEvent.BeginCandidate($"tx{n}",Plan(n,binding)));
    private ResourceCleanupState Acquire(ResourceCleanupState s,int n=1,bool borrowed=false,ResourceReferenceKind kind=ResourceReferenceKind.TARGET) { var p=s.Lifetime.Candidate.Target.PlanId; return Live(s,new ResourceLifetimeEvent.Admission(borrowed?new ResourceEvent.AcquireBorrowed(p,"default",1,1,kind):new ResourceEvent.Acquire(p,new VerifiedResourceSource("proof",$"source{n}",n,new string('a',64),1,1),kind))); }
    private ResourceCleanupState Ready(ResourceCleanupState s) { var op=s.Lifetime.Ledger.Scratch?.Operation; return op==null?s:Live(Live(s,new ResourceLifetimeEvent.Admission(new ResourceEvent.DecodeCompleted(op))),new ResourceLifetimeEvent.Admission(new ResourceEvent.ScratchReleased(op))); }
    private ResourceCleanupState Consume(ResourceCleanupState s) { var c=s.Lifetime.Candidate; return Live(Live(s,new ResourceLifetimeEvent.SealCandidate(c)),new ResourceLifetimeEvent.AwaitDurableCompletion(c)); }
    private ResourceCleanupState Active(bool borrowed=false) { var s=Consume(Ready(Acquire(Begin(),borrowed:borrowed))); return Live(s,new ResourceLifetimeEvent.DurableCompletionVerified(s.Lifetime.Candidate)); }
    private ResourceCleanupState Release(ResourceCleanupState s,string p=null) { var result=s; foreach(var r in s.Lifetime.Ledger.References.Where(r=>p==null||r.PlanId==p))result=Live(result,new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(r))); return result; }
    private ResourceCleanupDecision Stop(ResourceCleanupState s) => Step(s,new ResourceCleanupEvent.Teardown());
    private ResourceCleanupDecision Rollback(ResourceCleanupState s,ResourceLifetimePlan restored=null) => Step(s,new ResourceCleanupEvent.PostTransactionOutcome(s.Lifetime.Candidate,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,restored??s.Lifetime.Active));
    private ResourceCleanupDecision RollbackAck(ResourceCleanupState s,ResourceLifetimePlan p) => Step(s,new ResourceCleanupEvent.RollbackClonesInvalidated(s.Rollback.Closure,p));

    [Fact] public void idleTeardownPermanentlyClosesGate() {
        var d=Stop(new()); Assert.True(d.State.Closed); Assert.False(core.CanBeginCandidate(d.State)); Assert.False(core.CanApply(d.State)); Assert.Empty(d.Commands);
        var again=Stop(d.State); Assert.True(again.State.Closed); Assert.Empty(again.Commands); Denied(d.State,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.BeginCandidate("tx",Plan(1))));
    }
    [Fact] public void consumedRollbackNeedsCloneQualificationBeforeCandidateRemoval() {
        var s=Consume(Begin()); var c=s.Lifetime.Candidate; var d=Rollback(s); Assert.Equal(c,d.State.Cancellation); Assert.False(core.CanBeginCandidate(d.State)); Assert.Equal(c.Target,Assert.IsType<ResourceCleanupCommand.InvalidateRollbackClones>(Assert.Single(d.Commands)).Plan);
        var done=RollbackAck(d.State,c.Target).State; Assert.Null(done.Lifetime.Candidate); Assert.True(core.CanBeginCandidate(done)); Denied(done,new ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,null));
    }
    [Fact] public void unknownAndVisualOnlyKeepConsumedOwnershipAndLateSuccessReachable() {
        foreach(var kind in new[]{ResourcePostTransactionOutcome.UNKNOWN,ResourcePostTransactionOutcome.VISUAL_ONLY_ROLLBACK}) { var s=Consume(Ready(Acquire(Begin(Active(),2),2))); var c=s.Lifetime.Candidate; var d=Step(s,new ResourceCleanupEvent.PostTransactionOutcome(c,kind,null)); Assert.Same(s,d.State); Assert.Empty(d.Commands); Assert.True(d.State.Lifetime.AwaitingDurableCompletion); var stopped=Stop(d.State).State; Denied(stopped,new ResourceCleanupEvent.CancelPreparation(c)); var success=Live(stopped,new ResourceLifetimeEvent.DurableCompletionVerified(c)); Assert.Equal(c,success.Lifetime.AwaitingClones); Assert.Null(success.Shutdown); }
    }
    [Fact] public void outcomeMismatchAndPrematureProofCannotReleaseOwnership() {
        var s=Consume(Begin(Active(),2)); var c=s.Lifetime.Candidate;
        foreach(var wrong in new[]{c with{OperationId=99},c with{TransactionId="wrong"},c with{Prior=c.Prior with{Stamp="wrong"}},c with{Target=c.Target with{Binding="wrong"}},c with{Target=c.Target with{GenerationId="wrong"}},c with{Target=c.Target with{Stamp="wrong"}}})Denied(s,new ResourceCleanupEvent.PostTransactionOutcome(wrong,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.Prior));
        Denied(s,new ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,null)); Denied(Begin(),new ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.Prior));
    }
    [Fact] public void crossBindingRollbackRejectsUnchangedPriorOnObsoleteBinding() {
        var s=Consume(Ready(Acquire(Begin(Active(),2,"current"),2))); var c=s.Lifetime.Candidate;
        Assert.NotEqual(c.Prior.Binding,c.Target.Binding);
        Denied(s,new ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.Prior));
        Assert.True(s.Lifetime.AwaitingDurableCompletion); Assert.Equal(8,admission.Totals(s.Lifetime.Ledger).ProcessBytes);
        var restored=c.Prior with{Binding=c.Target.Binding,GenerationId="restored",Stamp="restored"}; var recovery=Rollback(s,restored); Assert.Equal(restored,recovery.State.Rollback.RestoredPrior); Assert.Equal(c,recovery.State.Cancellation);
    }
    [Fact] public void crossBindingRollbackRejectsArbitraryThirdBinding() {
        var s=Consume(Ready(Acquire(Begin(Active(),2,"current"),2))); var c=s.Lifetime.Candidate;
        var wrong=c.Prior with{Binding="third",GenerationId="restored",Stamp="restored"};
        Denied(s,new ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,wrong));
        Assert.True(s.Lifetime.AwaitingDurableCompletion); Assert.Equal(8,admission.Totals(s.Lifetime.Ledger).ProcessBytes);
        var restored=wrong with{Binding=c.Target.Binding}; var recovery=Rollback(s,restored); Assert.Equal(restored,recovery.State.Rollback.RestoredPrior); Assert.Equal(c,recovery.State.Cancellation);
    }
    [Fact] public void restoredIdentityQualifiesCandidateThenOldPriorThenCurrentShutdown() {
        foreach(var binding in new[]{"b","rebound"}) { var s=Consume(Ready(Acquire(Begin(Active(),2,binding)))); var c=s.Lifetime.Candidate; var restored=c.Prior with{Binding=binding,Stamp="restored",GenerationId=c.Target.GenerationId}; var d=Rollback(Stop(s).State,restored); Assert.Equal(c.Prior,d.State.Lifetime.Active);
            Denied(d.State,new ResourceCleanupEvent.RollbackClonesInvalidated(c,c.Prior)); d=RollbackAck(d.State,c.Target); Assert.Equal(c.Prior,Assert.IsType<ResourceCleanupCommand.InvalidateRollbackClones>(Assert.Single(d.Commands)).Plan);
            Denied(d.State,new ResourceCleanupEvent.RollbackClonesInvalidated(c,c.Target)); Denied(d.State,new ResourceCleanupEvent.RollbackCloneCountVerified(c,c.Prior,1)); d=Step(d.State,new ResourceCleanupEvent.RollbackCloneCountVerified(c,c.Prior,0)); Assert.Null(d.State.Shutdown);
            var state=Release(d.State,"p2"); Assert.Equal(restored,state.Lifetime.Active); var shutdown=state.Shutdown; Assert.Equal(restored,shutdown.Plan); Denied(state,new ResourceCleanupEvent.RollbackClonesInvalidated(c,c.Prior)); Denied(state,new ResourceCleanupEvent.ShutdownClonesInvalidated(shutdown with{Plan=c.Prior}));
            state=Step(state,new ResourceCleanupEvent.ShutdownClonesInvalidated(shutdown)).State; Assert.Empty(state.ShutdownDisposals); state=Release(state); var op=Assert.Single(state.ShutdownDisposals); Assert.Equal(4,admission.Totals(state.Lifetime.Ledger).ProcessBytes); state=Step(state,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(op)).State; Assert.True(state.Closed);
        }
    }
    [Fact] public void exactPriorRestorationKeepsSharedOwnedAndBorrowedMemberships() {
        foreach(var borrowed in new[]{false,true}) { var s=Consume(Ready(Acquire(Begin(Active(borrowed),2),borrowed:borrowed))); var c=s.Lifetime.Candidate; var d=RollbackAck(Rollback(s).State,c.Target); Assert.Empty(d.Commands); var done=Release(d.State,"p2"); Assert.Equal(c.Prior,done.Lifetime.Active); Assert.Equal(new[]{"p1"},Assert.Single(done.Lifetime.Ledger.Allocations).PlanIds); Assert.Empty(done.PendingDisposals); Assert.True(core.CanBeginCandidate(done)); }
    }
    [Fact] public void activeShutdownNeedsAllReferenceKindsAndExactCloneAndDisposeAcks() {
        var s=Begin(); foreach(ResourceReferenceKind k in Enum.GetValues(typeof(ResourceReferenceKind)))s=Acquire(s,kind:k); s=Consume(Ready(s)); s=Live(s,new ResourceLifetimeEvent.DurableCompletionVerified(s.Lifetime.Candidate)); var before=s; var d=Stop(s); s=d.State; var op=s.Shutdown;
        Assert.Equal(op,Assert.IsType<ResourceCleanupCommand.InvalidateShutdownClones>(Assert.Single(d.Commands)).Operation); Assert.Empty(Stop(s).Commands); Assert.Equal(before.Lifetime.Ledger.NextId,s.Lifetime.Ledger.NextId);
        Denied(s,new ResourceCleanupEvent.ShutdownCloneCountVerified(op,2)); Denied(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(op with{OperationId=99})); Denied(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(op with{Plan=op.Plan with{Stamp="wrong"}})); s=Step(s,new ResourceCleanupEvent.ShutdownCloneCountVerified(op,0)).State;
        var refs=s.Lifetime.Ledger.References; for(var i=0;i<refs.Count;i++) { d=Step(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.Admission(new ResourceEvent.ReleaseReference(refs[i])))); s=d.State; Assert.Equal(i==2?1:0,d.Commands.Count); }
        var disposal=Assert.Single(s.ShutdownDisposals); Denied(s,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal with{AllocationId=99})); Denied(s,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal with{OperationId=99})); Assert.Equal(4,admission.Totals(s.Lifetime.Ledger).ProcessBytes);
        var done=Step(s,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal)).State; Assert.True(done.Closed); Assert.Equal(s.Lifetime.NextOperationId,done.Lifetime.NextOperationId); Assert.Equal(s.Lifetime.Ledger.NextId,done.Lifetime.Ledger.NextId); Denied(done,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(disposal));
    }
    [Fact] public void borrowedShutdownUntracksOnlyAfterCloneAndReferenceClosure() { var s=Stop(Active(true)).State; var refs=Release(s); Assert.Single(refs.Lifetime.Ledger.Allocations); var d=Step(refs,new ResourceCleanupEvent.ShutdownClonesInvalidated(refs.Shutdown)); Assert.True(d.State.Closed); Assert.Empty(d.Commands); }
    [Fact] public void teardownPreparedAndSealedCandidatesUseExistingCancellation() {
        foreach(var seal in new[]{false,true}) { var s=Ready(Acquire(Begin())); var c=s.Lifetime.Candidate; if(seal)s=Live(s,new ResourceLifetimeEvent.SealCandidate(c)); s=Stop(s).State; Assert.Equal(c,s.Cancellation); Assert.False(core.CanApply(s)); Denied(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.AwaitDurableCompletion(c))); s=Release(s); var op=Assert.Single(s.PendingDisposals); s=Step(s,new ResourceCleanupEvent.DisposeAcknowledged(op)).State; Assert.True(s.Closed); Assert.False(core.CanBeginCandidate(s)); }
    }
    [Fact] public void lateDecoderAndScratchRacesNeverFreeOnStopRequest() {
        foreach(var created in new[]{false,true})foreach(var scratchFirst in new[]{false,true}) { var initial=Acquire(Begin()); var c=initial.Lifetime.Candidate; var op=initial.Lifetime.Ledger.Scratch.Operation; var d=Stop(initial); Assert.IsType<ResourceCleanupCommand.RequestDecoderStop>(Assert.Single(d.Commands)); var s=Release(d.State); if(scratchFirst)s=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)).State;
            Assert.Equal(8,admission.Totals(s.Lifetime.Ledger).ProcessBytes); Assert.False(s.Closed); s=Step(s,new ResourceCleanupEvent.DecodeResolved(c,op,created?ResourceDecodeOutcome.CREATED:ResourceDecodeOutcome.NEVER_CREATED)).State; if(!scratchFirst)s=Step(s,new ResourceCleanupEvent.ScratchReleased(c,op)).State;
            if(created) { Assert.False(s.Closed); s=Step(s,new ResourceCleanupEvent.DisposeAcknowledged(Assert.Single(s.PendingDisposals))).State; } Assert.True(s.Closed); Denied(s,new ResourceCleanupEvent.DecodeResolved(c,op,ResourceDecodeOutcome.CREATED)); }
    }
    [Fact] public void retirementDrainsBeforeActiveShutdownWithoutSecondSet() {
        var s=Consume(Ready(Acquire(Begin(Active(),2),2))); var c=s.Lifetime.Candidate; s=Live(s,new ResourceLifetimeEvent.DurableCompletionVerified(c)); s=Stop(s).State; Assert.Null(s.Shutdown); s=Live(s,new ResourceLifetimeEvent.CompanionClonesInvalidated(c)); Assert.NotNull(s.Lifetime.Retirement); Assert.Null(s.Shutdown); s=Release(s,"p1"); var op=Assert.Single(s.Lifetime.PendingDisposals); Assert.Null(s.Shutdown);
        var d=Step(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.DisposeAcknowledged(op))); s=d.State; Assert.Null(s.Lifetime.Retirement); Assert.NotNull(s.Shutdown); Assert.Single(d.Commands); Denied(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.DisposeAcknowledged(op))); s=Release(s); s=Step(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(s.Shutdown)).State; s=Step(s,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(Assert.Single(s.ShutdownDisposals))).State; Assert.True(s.Closed);
    }
    [Fact] public void overflowAndMalformedShutdownPreserveSnapshotsAndCharges() {
        var a=Active(); var l=a.Lifetime; var s=new ResourceCleanupState(new ResourceLifetimeState(l.Ledger,l.Active,nextOperationId:long.MaxValue)); Denied(s,new ResourceCleanupEvent.Teardown()); var stopped=Stop(a).State; var bad=new ResourceCleanupState(stopped.Lifetime,teardownRequested:true,shutdown:stopped.Shutdown with{Plan=Plan(9)}); Denied(bad,new ResourceCleanupEvent.Teardown()); Assert.Equal(4,admission.Totals(a.Lifetime.Ledger).ProcessBytes);
    }
    [Fact] public void teardownPreservesAlreadyPendingCancellationAndRetirementDisposals() {
        var prepared=Release(Ready(Acquire(Begin()))); var cancelled=Step(prepared,new ResourceCleanupEvent.CancelPreparation(prepared.Lifetime.Candidate)).State; var cancellationOp=Assert.Single(cancelled.PendingDisposals); var stoppedCancel=Stop(cancelled); Assert.Equal(cancelled.PendingDisposals,stoppedCancel.State.PendingDisposals); Assert.Empty(stoppedCancel.Commands); Assert.True(Step(stoppedCancel.State,new ResourceCleanupEvent.DisposeAcknowledged(cancellationOp)).State.Closed);
        foreach(var disposalPending in new[]{false,true}) { var s=Consume(Ready(Acquire(Begin(Active(),2),2))); var c=s.Lifetime.Candidate; s=Live(s,new ResourceLifetimeEvent.DurableCompletionVerified(c)); s=Live(s,new ResourceLifetimeEvent.CompanionClonesInvalidated(c)); if(disposalPending)s=Release(s,"p1"); var prior=s; var d=Stop(s); s=d.State; Assert.Null(s.Shutdown); Assert.Empty(d.Commands); Assert.Equal(prior.Lifetime.PendingDisposals,s.Lifetime.PendingDisposals); if(!disposalPending)s=Release(s,"p1"); var op=Assert.Single(s.Lifetime.PendingDisposals); s=Live(s,new ResourceLifetimeEvent.DisposeAcknowledged(op)); Assert.NotNull(s.Shutdown); }
    }
    [Fact] public void unknownOutcomeCanLaterDurablyRollbackAndNeverEmitsWrites() {
        var s=Consume(Ready(Acquire(Begin(Active(),2),2))); var c=s.Lifetime.Candidate; s=Step(s,new ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.UNKNOWN,null)).State; s=Rollback(s).State; s=RollbackAck(s,c.Target).State; s=Release(s,"p2"); var op=Assert.Single(s.PendingDisposals); Denied(s,new ResourceCleanupEvent.PostTransactionOutcome(c,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,c.Prior)); var done=Step(s,new ResourceCleanupEvent.DisposeAcknowledged(op)); Assert.Equal(c.Prior,done.State.Lifetime.Active); Assert.Empty(done.Commands); Assert.True(core.CanBeginCandidate(done.State));
    }
    [Fact] public void multipleShutdownDisposalsRetainChargeUntilEveryExactAck() {
        var s=Ready(Acquire(Begin())); s=Ready(Acquire(s,2)); s=Consume(s); s=Live(s,new ResourceLifetimeEvent.DurableCompletionVerified(s.Lifetime.Candidate)); s=Release(Stop(s).State); var d=Step(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(s.Shutdown)); s=d.State; Assert.Equal(2,d.Commands.Count); Assert.All(d.Commands,c=>Assert.IsType<ResourceCleanupCommand.DisposeShutdown>(c)); var ops=s.ShutdownDisposals;
        var first=Step(s,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(ops[1])); Assert.Empty(first.Commands); Assert.False(first.State.Closed); Assert.Equal(4,admission.Totals(first.State.Lifetime.Ledger).ProcessBytes); var last=Step(first.State,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(ops[0])); Assert.True(last.State.Closed); Assert.Equal(0,admission.Totals(last.State.Lifetime.Ledger).ProcessBytes); Assert.Equal(s.Lifetime.NextOperationId,last.State.Lifetime.NextOperationId); Assert.Equal(s.Lifetime.Ledger.NextId,last.State.Lifetime.Ledger.NextId);
    }
    [Fact] public void malformedRollbackCannotInventConsumedSealedOrigin() {
        var s=Begin(); var c=s.Lifetime.Candidate; var forged=new ResourceCleanupState(s.Lifetime,c,rollback:new ResourceRollbackResolution(c,null)); Denied(forged,new ResourceCleanupEvent.RollbackClonesInvalidated(c,c.Target));
    }
    [Fact] public void malformedEmptySnapshotsAreNotTerminalClosed() {
        Assert.False(new ResourceCleanupState(new ResourceLifetimeState(nextOperationId:0),teardownRequested:true).Closed); Assert.False(new ResourceCleanupState(teardownRequested:true,shutdownClonesQualified:true).Closed);
    }
    [Fact] public void overflowAtShutdownDisposalRetainsExactUnqualifiedSnapshot() {
        var active=Release(Active()); var l=active.Lifetime; var exhausted=new ResourceCleanupState(new ResourceLifetimeState(l.Ledger,l.Active,nextOperationId:long.MaxValue-1)); var stopped=Stop(exhausted).State; Assert.Equal(long.MaxValue,stopped.Lifetime.NextOperationId); Denied(stopped,new ResourceCleanupEvent.ShutdownClonesInvalidated(stopped.Shutdown)); Assert.False(stopped.ShutdownClonesQualified); Assert.Equal(4,admission.Totals(stopped.Lifetime.Ledger).ProcessBytes);
    }
    [Fact] public void shutdownFullIdentityRejectsEveryChangedFieldAndDoubleAck() {
        var s=Release(Stop(Active()).State); var h=s.Shutdown;
        foreach(var p in new[]{h.Plan with{PlanId="wrong"},h.Plan with{Binding="wrong"},h.Plan with{GenerationId="wrong"},h.Plan with{Stamp="wrong"}})Denied(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(h with{Plan=p}));
        s=Step(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(h)).State; var op=Assert.Single(s.ShutdownDisposals); Denied(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(h)); Denied(s,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(op with{Shutdown=h with{OperationId=h.OperationId+1}}));
        foreach(var p in new[]{h.Plan with{PlanId="wrong"},h.Plan with{Binding="wrong"},h.Plan with{GenerationId="wrong"},h.Plan with{Stamp="wrong"}})Denied(s,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(op with{Shutdown=h with{Plan=p}}));
    }
    [Fact] public void disposalCollectionsAreImmutableAndNoNewWorkIsAccepted() {
        var s=Release(Stop(Active()).State); s=Step(s,new ResourceCleanupEvent.ShutdownClonesInvalidated(s.Shutdown)).State; var list=s.ShutdownDisposals.ToList(); var copy=new ResourceCleanupState(s.Lifetime,teardownRequested:true,shutdown:s.Shutdown,shutdownClonesQualified:true,shutdownDisposals:list); list.Clear(); Assert.Single(copy.ShutdownDisposals); Assert.Throws<NotSupportedException>(()=>((IList<ResourceShutdownDisposal>)copy.ShutdownDisposals).Clear()); Denied(s,new ResourceCleanupEvent.Lifetime(new ResourceLifetimeEvent.Admission(new ResourceEvent.AcquireBorrowed("p1","default",1,1,ResourceReferenceKind.TARGET)))); Assert.Empty(Stop(s).Commands);
    }
    private ResourceCleanupState ForgedNullTargetRollback() {
        var s=Consume(Begin(Active(),2)); var forged=s.Lifetime.Candidate with{Target=null};
        return new ResourceCleanupState(s.Lifetime,forged,rollback:new ResourceRollbackResolution(forged,s.Lifetime.Active));
    }
    [Fact] public void malformedNullTargetRollbackCanBeginCandidateRejectsWithoutThrowing() {
        Assert.False(core.CanBeginCandidate(ForgedNullTargetRollback()));
    }
    [Fact] public void malformedNullTargetRollbackCanApplyRejectsWithoutThrowing() {
        Assert.False(core.CanApply(ForgedNullTargetRollback()));
    }
    [Fact] public void malformedNullTargetRollbackDecideRejectsWithoutThrowing() {
        var s=ForgedNullTargetRollback(); Denied(s,new ResourceCleanupEvent.Teardown());
    }
    [Fact] public void rollbackClosureMustMatchValidatedLifetimeBeforeAllEntrypoints() {
        var s=Consume(Begin(Active(),2)); var c=s.Lifetime.Candidate;
        foreach(var forged in new[]{c with{TransactionId="wrong"},c with{Target=c.Target with{Stamp="wrong"}},c with{Prior=c.Prior with{Binding="wrong"}}}) {
            var malformed=new ResourceCleanupState(s.Lifetime,forged,rollback:new ResourceRollbackResolution(forged,s.Lifetime.Active));
            Assert.False(core.CanBeginCandidate(malformed)); Assert.False(core.CanApply(malformed)); Denied(malformed,new ResourceCleanupEvent.Teardown());
        }
    }
    [Fact] public void malformedClrValuesAreTotalRejections() {
        var s=Consume(Begin()); var c=s.Lifetime.Candidate; Denied(null,new ResourceCleanupEvent.Teardown()); Denied(s,null); Denied(s,new ResourceCleanupEvent.PostTransactionOutcome(null,ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK,null)); Denied(s,new ResourceCleanupEvent.PostTransactionOutcome(c,(ResourcePostTransactionOutcome)99,null));
        var stopped=Stop(Active()).State; Denied(stopped,new ResourceCleanupEvent.ShutdownClonesInvalidated(null)); Denied(stopped,new ResourceCleanupEvent.ShutdownDisposeAcknowledged(null)); Denied(new ResourceCleanupState(stopped.Lifetime,teardownRequested:true,shutdown:stopped.Shutdown,shutdownClonesQualified:true,shutdownDisposals:new ResourceShutdownDisposal[]{null}),new ResourceCleanupEvent.Teardown());
        var rollback=Rollback(s).State; Denied(rollback,new ResourceCleanupEvent.RollbackClonesInvalidated(c,null)); Denied(new ResourceCleanupState(rollback.Lifetime,rollback.Cancellation,rollback:new ResourceRollbackResolution(c,null,(ResourceRollbackClonePhase)99)),new ResourceCleanupEvent.Teardown());
    }
}
