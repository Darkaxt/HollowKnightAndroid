using System;
using System.Linq;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinRotationOutcomeCoreTests
{
    private readonly SkinRotationTransactionCore core=new();
    private static readonly HeroBindingToken Hero=new("hero");
    private static readonly SkinBindingToken Skin=new("skin");
    private static string Id(int n)=>$"00000000-0000-0000-0000-{n:x12}";
    private static string Hash(int n)=>n.ToString("x64");
    private static ActiveVisual.Pack Pack(string id="a",int receipt=3)=>new(id,Hash(1),Hash(2),Hash(receipt));
    private static RotationTransactionState State(SkinMode mode=SkinMode.OFF,ActiveVisual active=null,int currentReceipt=3)
    {
        var a=new ActivationSnapshot(mode,"a",active??new ActiveVisual.Vanilla(),7);
        var ring=RotationRing.TryCreate(new[]{new RotationDescriptor("A",Pack(receipt:currentReceipt),true),new RotationDescriptor("B",Pack("b"),true)});
        return new(new(new(ring,a,Hero,Skin),new(Id(1),Hash(10),a,RotationInterlock.Clear())));
    }
    private RotationTransactionState Mode(RotationTransactionState s,RotationModeEvent e)=>core.Decide(s,new RotationTransactionEvent.Mode(e)).State;
    private RotationTransactionState Advance(RotationTransactionState s)=>Mode(s,new RotationModeEvent.AdvanceMode());
    private RotationTransactionState Proof(RotationTransactionState s,ActiveVisual v)=>Mode(s,new RotationModeEvent.VerifiedVisual(new(s.Mode.Rotation.CurrentHero,
        v is ActiveVisual.Pack p ? new VerifiedLiveVisualProof.Pack(s.Mode.Rotation.CurrentSkin,p) : new VerifiedLiveVisualProof.Vanilla(s.Mode.Rotation.CurrentSkin))));
    private RotationTransactionState Rebind(RotationTransactionState s)=>Mode(s,new RotationModeEvent.Selector(new RotationEvent.Rebind(new("newhero"),new("newskin"))));
    private static TransactionCorrelation Cor(RotationTransactionState s)=>new(s.Operation.Transaction.Envelope.TransactionId,s.Operation.Transaction.Envelope.Binding);
    private RotationTransactionState Tx(RotationTransactionState s,TransactionEvent e)=>core.Decide(s,new RotationTransactionEvent.Transaction(s.Operation.Origin.Rotation.CurrentHero,e)).State;
    private RotationTransactionState Armed(RotationTransactionState s)
    {
        s=Tx(s,new TransactionEvent.Prepared(Cor(s)));var e=s.Operation.Transaction.Envelope;var n=100+(int)s.Mode.OperationHighWater*3;
        var r=new RegistryCommitReceipt(e.BaseGenerationId,e.BaseGenerationSha256,Id(n),Hash(n));
        var l=new RotationInterlock(InterlockState.ARMED,e.TransactionId,e.Operation,e.BaseGenerationId,e.BaseGenerationSha256,e.Prior,e.Target,e.Binding,e.PriorEstablishedOnBinding,null,null);
        return Tx(s,new TransactionEvent.ArmCommitted(Cor(s),r,new(r.NewGenerationId,r.NewGenerationSha256,e.Prior,l)));
    }
    private static VerifiedRegistryHead Parent(RotationTransactionState s)
    {var t=s.Operation.Transaction;var r=t.FailureReceipt??t.ArmCommitReceipt;return new(r.NewGenerationId,r.NewGenerationSha256,t.Envelope.Prior,t.Interlock);}
    private static RegistryCommitReceipt Receipt(VerifiedRegistryHead p,int n=900)=>new(p.GenerationId,p.GenerationSha256,Id(n),Hash(n));
    private static VerifiedRegistryHead Child(RegistryCommitReceipt r,ActivationSnapshot a)=>new(r.NewGenerationId,r.NewGenerationSha256,a,RotationInterlock.Clear());
    private static RotationRecoveryAuthority Authority(RotationTransactionState s)=>new(s.Mode.Rotation.CurrentHero,s.Mode.Rotation.CurrentSkin);
    private static RotationOutcomeFence Fence(RotationTransactionState s)=>new(Cor(s),s.Operation.Origin.Rotation.CurrentHero,Parent(s));
    private static ActivationSnapshot Prior(RotationTransactionState s)
    {var e=s.Operation.Transaction.Envelope;return !e.PriorEstablishedOnBinding && e.Prior.Active is ActiveVisual.Pack ? e.Prior with {SkinStamp=e.Prior.SkinStamp+1}:e.Prior;}
    private static RotationRecoveryEvidence.PriorRestoredAndFenced Restored(RotationTransactionState s)
    {
        var a=Prior(s);var r=Receipt(Parent(s));var p=s.Operation;
        var proof=new HeroVerifiedVisual(p.Origin.Rotation.CurrentHero,a.Active is ActiveVisual.Pack pack ? new VerifiedLiveVisualProof.Pack(p.Origin.Rotation.CurrentSkin,pack):new VerifiedLiveVisualProof.Vanilla(p.Origin.Rotation.CurrentSkin));
        return new(p,Fence(s),proof,r,Child(r,a),Authority(s));
    }
    private static RotationRecoveryEvidence.IssuedClosureOutcome Issued(RotationTransactionState s)
    {var r=Receipt(Parent(s),900+(int)s.Mode.OperationHighWater);return new(s.Operation,Fence(s),r,Child(r,s.Operation.Transaction.PendingClosure),Authority(s));}
    private RotationTransactionState Recover(RotationTransactionState s,RotationRecoveryEvidence e)
    {var d=core.Decide(s,new RotationTransactionEvent.Recover(e));Assert.Empty(d.Commands);return d.State;}
    private void Noop(RotationTransactionState s,RotationRecoveryEvidence e)=>Assert.Equal(s,Recover(s,e));
    private void Resolved(RotationTransactionState s,RotationTransactionState before)
    {
        Assert.Null(s.Operation);Assert.Null(s.Mode.Operation);Assert.Null(s.Mode.Rotation.Pending);Assert.True(core.IsStateValid(s));
        Assert.Equal(before with {LastRecovery=null},s.LastRecovery.Before);Assert.Equal(before.Operation?.Transaction.Phase,s.LastRecovery.Before.Operation?.Transaction.Phase);
        Assert.Equal(before.Mode.OperationHighWater,s.Mode.OperationHighWater);Assert.Equal(before.Mode.Rotation.EpochHighWater,s.Mode.Rotation.EpochHighWater);
    }
    private RotationTransactionState Pending(ActiveVisual active=null)
    {var a=Armed(Advance(State(active:active)));return Tx(a,new TransactionEvent.ApplyFailed(Cor(a),"APPLY_FAILED"));}
    private RotationTransactionState Blocked(bool rollback=false)
    {
        var a=Armed(Advance(State()));
        if(rollback){a=Tx(a,new TransactionEvent.ApplyFailed(Cor(a),"FAILED"));a=Tx(a,new TransactionEvent.RollbackVerified(Cor(a),true));}
        else a=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));
        return Tx(a,new TransactionEvent.CompletionIndeterminate(Cor(a)));
    }
    private RotationTransactionState Failed(bool persisted)
    {
        var s=Pending(Pack());var t=s.Operation.Transaction;var l=t.Interlock with {State=InterlockState.ROLLBACK_FAILED,OriginalFailure=t.OriginalFailure,RollbackFailure="FAILED"};
        var r=Receipt(Parent(s),800);return Tx(s,new TransactionEvent.RollbackFailed(Cor(s),"FAILED",persisted?r:null,persisted?new(r.NewGenerationId,r.NewGenerationSha256,t.Envelope.Prior,l):null));
    }
    [Fact] public void PendingPriorRecoveryPreservesHistoricalPhaseAndAllowsNewOperation()
    {
        foreach(var active in new ActiveVisual[]{new ActiveVisual.Vanilla(),Pack()})foreach(var rebound in new[]{false,true})
        {
            var s=Pending(active);if(rebound)s=Rebind(s);var e=Restored(s);var done=Recover(s,e);Resolved(done,s);
            Assert.Equal(Prior(s),done.Mode.Rotation.Activation);Assert.Null(done.Mode.LiveProof);Noop(done,e);
            var next=Advance(done);Assert.NotNull(next.Operation);Assert.Equal(2,next.Mode.OperationHighWater);
            Assert.NotEqual(Cor(s).TransactionId,Cor(next).TransactionId);
        }
    }
    [Fact] public void BlockedIssuedTargetAndPriorCloseOnlyTheirStoredOutcome()
    {
        foreach(var rollback in new[]{false,true})foreach(var rebound in new[]{false,true})
        {
            var s=Blocked(rollback);if(rebound)s=Rebind(s);var e=Issued(s);
            var wrong=rollback?s.Operation.Transaction.Envelope.Target:Prior(s);
            Noop(s,e with {Head=e.Head with {Activation=wrong}});
            var done=Recover(s,e);Resolved(done,s);Assert.Equal(s.Operation.Transaction.PendingClosure,done.Mode.Rotation.Activation);Noop(done,e);
        }
    }
    [Fact] public void QualifiedPriorRestorationResolvesIndeterminateTargetWithoutInventingRollbackEvents()
    {
        var s=Blocked();var done=Recover(s,Restored(s));Resolved(done,s);Assert.Equal(Prior(s),done.Mode.Rotation.Activation);
        Assert.Null(done.LastRecovery.Before.Operation.Transaction.OriginalFailure);
        Assert.Equal(TransactionPhase.BLOCKED,done.LastRecovery.Before.Operation.Transaction.Phase);
    }
    [Fact] public void RollbackFailureRequiresFullExactStoredFailureParentOrArmWhenNotPersisted()
    {
        foreach(var persisted in new[]{false,true})
        {
            var s=Failed(persisted);var e=Restored(s);var parent=e.Fence.AuthoritativeParent;
            Noop(s,e with {Fence=e.Fence with {AuthoritativeParent=parent with {Interlock=RotationInterlock.Clear()}}});
            Noop(s,e with {Fence=e.Fence with {AuthoritativeParent=parent with {Interlock=parent.Interlock with {RollbackFailure="OTHER"}}}});
            if(persisted){var arm=s.Operation.Transaction.ArmCommitReceipt;Noop(s,e with {Fence=e.Fence with {AuthoritativeParent=parent with {GenerationId=arm.NewGenerationId,GenerationSha256=arm.NewGenerationSha256}}});}
            var done=Recover(s,e);Resolved(done,s);Assert.Equal(Prior(s),done.Mode.Rotation.Activation);
        }
    }
    [Fact] public void WrongCorrelationParentReceiptProofAndImportIdentityRemainExactlyRetryable()
    {
        var s=Rebind(Pending(Pack()));var e=Restored(s);var p=e.Fence.AuthoritativeParent;var proof=(VerifiedLiveVisualProof.Pack)e.Restoration.Proof;
        foreach(var bad in new[]{e with {Operation=e.Operation with {Origin=e.Operation.Origin with {OperationHighWater=9}}},
            e with {Fence=e.Fence with {Correlation=e.Fence.Correlation with {TransactionId=Id(55)}}},e with {Fence=e.Fence with {OriginalHero=new("other")}},
            e with {Fence=e.Fence with {AuthoritativeParent=p with {Activation=p.Activation with {SelectedPackId="b"}}}},
            e with {Receipt=e.Receipt with {ExpectedGenerationId=Id(55)}},e with {Receipt=e.Receipt with {NewGenerationId=Id(1)}},
            e with {Receipt=e.Receipt with {NewGenerationSha256="bad"}},e with {Head=e.Head with {Interlock=RotationInterlock.Clear() with {TransactionId=Id(1)}}},
            e with {Head=e.Head with {Activation=e.Head.Activation with {Active=Pack(receipt:9)}}},
            e with {Restoration=e.Restoration with {Hero=new("newhero")}},e with {Restoration=e.Restoration with {Proof=proof with {Binding=new("newskin")}}},
            e with {Restoration=e.Restoration with {Proof=proof with {Visual=Pack("b")}}},
            e with {Authority=e.Authority with {Hero=Hero}},e with {Authority=e.Authority with {LiveProof=e.Restoration}}})Noop(s,bad);
        Resolved(Recover(s,e),s);
    }
    [Fact] public void WrongOutcomeKindsAndVisualOnlyLegacyFactsDoNotResolve()
    {
        var s=Pending();var r=Receipt(Parent(s));
        Noop(s,new RotationRecoveryEvidence.IssuedClosureOutcome(s.Operation,Fence(s),r,Child(r,Prior(s)),Authority(s)));
        Noop(s,new RotationRecoveryEvidence.VisualFencedNotExecuted(s.Operation,s.Mode.Head,Authority(s)));
        var armed=Armed(Advance(State()));Noop(armed,Restored(armed));
        var blocked=Failed(true);Noop(blocked,new RotationRecoveryEvidence.IssuedClosureOutcome(blocked.Operation,Fence(blocked),r,Child(r,Prior(blocked)),Authority(blocked)));
        Resolved(Recover(s,Restored(s)),s);
    }
    private RotationModeEvent.ModeCommitted ModeCompletion(RotationTransactionState s)
    {var p=s.Mode.Operation;var r=Receipt(p.BaseHead,1200+(int)s.Mode.OperationHighWater);return new(p.Correlation,r,Child(r,p.Target));}
    [Fact] public void SelectedNoopRefreshesExactCurrentReceiptWithoutChangingStampOrProof()
    {
        var s=Proof(State(active:Pack(),currentReceipt:8),Pack(receipt:9));var begin=Advance(s);
        Assert.Null(begin.Operation);Assert.IsType<RotationModeCommand.CommitVerifiedSelectedOn>(Assert.IsType<RotationTransactionCommand.Mode>(Assert.Single(core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.AdvanceMode())).Commands)).Command);
        Assert.Equal(s.Mode.LiveProof,begin.Mode.Operation.SelectedProof);Assert.Equal(Pack(receipt:8),begin.Mode.Operation.Target.Active);
        var c=ModeCompletion(begin);Assert.Equal(begin,Mode(begin,c with {Head=c.Head with {Activation=c.Head.Activation with {Active=Pack()}}}));
        var done=Mode(begin,c);Assert.Equal(7,done.Mode.Rotation.Activation.SkinStamp);Assert.Equal(s.Mode.LiveProof,done.Mode.LiveProof);Assert.Equal(SkinMode.ON,done.Mode.Rotation.Activation.Mode);
    }
    [Fact] public void NoopProofRebindRequiresExactCurrentPackAndOriginalCasRecovery()
    {
        var begin=Advance(Proof(State(active:Pack(),currentReceipt:8),Pack(receipt:9)));var c=ModeCompletion(begin);var s=Rebind(begin);
        var e=new RotationRecoveryEvidence.ModeClosure(s.Mode.Operation,c,Authority(s));Noop(s,e);
        Noop(s,e with {Authority=e.Authority with {LiveProof=begin.Mode.LiveProof}});
        Noop(s,e with {Authority=e.Authority with {LiveProof=new(e.Authority.Hero,new VerifiedLiveVisualProof.Pack(e.Authority.Skin,Pack("b")))}});
        var good=e with {Authority=e.Authority with {LiveProof=new(e.Authority.Hero,new VerifiedLiveVisualProof.Pack(e.Authority.Skin,Pack(receipt:77)))}};
        var done=Recover(s,good);Resolved(done,s);Assert.Equal(Pack(receipt:8),done.Mode.Rotation.Activation.Active);
    }
    [Fact] public void NoopOverflowAndForgedProofRemainFailClosed()
    {
        var s=Proof(State(active:Pack()),Pack());var a=s.Mode.Rotation.Activation with {SkinStamp=long.MaxValue};
        var stamp=s with {Mode=s.Mode with {Rotation=s.Mode.Rotation with {Activation=a},Head=s.Mode.Head with {Activation=a}}};
        Assert.Equal(long.MaxValue,Advance(stamp).Mode.Operation.Target.SkinStamp);
        var max=s with {Mode=s.Mode with {OperationHighWater=long.MaxValue}};Assert.Equal(max,Advance(max));
        var p=Advance(s);foreach(var proof in new HeroVerifiedVisual[]{null,new(Hero,new VerifiedLiveVisualProof.Pack(Skin,Pack("b"))),new(Hero,null)})
        {var bad=p with {Mode=p.Mode with {Operation=p.Mode.Operation with {SelectedProof=proof}}};Assert.False(core.IsStateValid(bad));Assert.Equal(bad,Advance(bad));}
        var unknown=State(active:Pack());Assert.NotNull(Advance(unknown).Operation);
    }
    private RotationTransactionState Ready(RotationTransactionState s,ulong epoch)
    {
        s=Mode(s,new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new(epoch),s.Mode.Rotation.CurrentHero,s.Mode.Rotation.CurrentSkin)));
        return Mode(s,new RotationModeEvent.Selector(new RotationEvent.StableRespawn(new(new(epoch),s.Mode.Rotation.CurrentHero,s.Mode.Rotation.CurrentSkin))));
    }
    private RotationTransactionState CompleteVisual(RotationTransactionState s)
    {
        var a=Armed(s);a=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));var p=Parent(a);var r=Receipt(p,1500+(int)s.Mode.OperationHighWater);
        return Tx(a,new TransactionEvent.CompletionCommitted(Cor(a),r,Child(r,a.Operation.Transaction.PendingClosure)));
    }
    [Fact] public void SameBindingUnconsumedCancellationHasExactRetirementAndReachableOffClosure()
    {
        var ready=Ready(State(SkinMode.ROTATE,Pack()),4);var intent=ready.Mode.Rotation.Pending.IssuedIntent;var s=Advance(ready);
        var e=new RotationRecoveryEvidence.ReadinessFencedNotExecuted(intent,s.Mode.Head,Authority(s));
        Noop(s,e with {Intent=intent with {Epoch=new(8)}});var done=Recover(s,e);Resolved(done,s);Assert.True(done.Mode.OffRequested);Assert.Equal(SkinMode.ROTATE,done.Mode.Rotation.Activation.Mode);
        Noop(done,e);Assert.Equal(done,core.Decide(done,new RotationTransactionEvent.ConsumeReady(intent)).State);
        var off=CompleteVisual(Advance(done));Assert.Equal(SkinMode.OFF,off.Mode.Rotation.Activation.Mode);Assert.Equal(new DeathEpoch(4),off.Mode.Rotation.EpochHighWater);
        var consumed=core.Decide(ready,new RotationTransactionEvent.ConsumeReady(intent)).State;Noop(consumed,e);
    }
    [Fact] public void RepeatedFullCyclesDeathSuccessorRollbackCancellationAndRebindPreserveHighwaters()
    {
        var s=State();var ids=new System.Collections.Generic.HashSet<string>();
        for(var cycle=0;cycle<3;cycle++)
        {
            s=Advance(s);Assert.True(ids.Add(Cor(s).TransactionId));s=CompleteVisual(s);
            s=Advance(s);Assert.True(ids.Add(s.Mode.Operation.Correlation.OperationId));s=Mode(s,ModeCompletion(s));
            s=Ready(s,(ulong)(cycle*3+1));var intent=s.Mode.Rotation.Pending.IssuedIntent;
            s=core.Decide(s,new RotationTransactionEvent.ConsumeReady(intent)).State;Assert.True(ids.Add(Cor(s).TransactionId));
            if(cycle==1){s=Armed(s);s=Tx(s,new TransactionEvent.ApplyFailed(Cor(s),"FAILED"));s=Rebind(s);var before=s;s=Recover(s,Restored(s));Resolved(s,before);}
            else s=CompleteVisual(s);
            Assert.Equal(s,core.Decide(s,new RotationTransactionEvent.ConsumeReady(intent)).State);
            var prior=s.Mode.Rotation.Activation;var ready=Ready(s,(ulong)(cycle*3+2));Assert.NotEqual((prior.Active as ActiveVisual.Pack)?.Id,ready.Mode.Rotation.Pending.Candidate.CurrentObject.Id);
            var pending=Mode(s,new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new((ulong)(cycle*3+2)),s.Mode.Rotation.CurrentHero,s.Mode.Rotation.CurrentSkin)));
            s=Advance(pending);Assert.NotNull(s.Mode.CanceledPending);Assert.True(ids.Add(Cor(s).TransactionId));s=CompleteVisual(s);
            Assert.Equal(SkinMode.OFF,s.Mode.Rotation.Activation.Mode);Assert.Equal((cycle+1)*4,s.Mode.OperationHighWater);Assert.True(core.IsStateValid(s));
        }
        Assert.Equal(12,ids.Count);
    }
    [Fact] public void SelectedModeRetryAndCurrentProofForgeryCannotChangeIssuedAuthority()
    {
        var initial=Proof(State(active:Pack(),currentReceipt:8),Pack(receipt:9));var s=Advance(initial);var c=ModeCompletion(s);
        foreach(var failed in new[]{false,true})
        {
            var uncertain=Mode(s,failed ? new RotationModeEvent.ModeFailed(s.Mode.Operation.Correlation):new RotationModeEvent.ModeIndeterminate(s.Mode.Operation.Correlation));
            var retry=core.Decide(uncertain,new RotationTransactionEvent.Mode(new RotationModeEvent.RetryModeCommit(s.Mode.Operation.Correlation)));
            Assert.Equal(uncertain,retry.State);var command=Assert.IsType<RotationTransactionCommand.Mode>(Assert.Single(retry.Commands));
            Assert.Equal(s.Mode.Operation,Assert.IsType<RotationModeCommand.CommitVerifiedSelectedOn>(command.Command).Operation);
            Assert.Null(Mode(uncertain,c).Mode.Operation);
        }
        foreach(var proof in new HeroVerifiedVisual[]{null,new(Hero,new VerifiedLiveVisualProof.Pack(Skin,Pack(receipt:8)))})
        {var forged=s with {Mode=s.Mode with {LiveProof=proof}};Assert.False(core.IsStateValid(forged));Assert.Equal(forged,Mode(forged,c));}
        var rebound=Rebind(s);var back=Mode(rebound,new RotationModeEvent.Selector(new RotationEvent.Rebind(Hero,Skin)));
        Assert.True(back.Mode.Operation.ReboundBlocked);Assert.Equal(back,Mode(back,c));
        var forgedBack=back with {Mode=back.Mode with {LiveProof=initial.Mode.LiveProof}};Assert.False(core.IsStateValid(forgedBack));
    }
    [Fact] public void EstablishedPriorStampAndDeathIssuedOutcomeKeepEpochAndBoundedAudit()
    {
        var ready=Ready(Proof(State(SkinMode.ROTATE,Pack()),Pack(receipt:9)),ulong.MaxValue-1);var intent=ready.Mode.Rotation.Pending.IssuedIntent;
        var a=Armed(core.Decide(ready,new RotationTransactionEvent.ConsumeReady(intent)).State);
        var rollback=Tx(a,new TransactionEvent.ApplyFailed(Cor(a),"FAILED"));Assert.True(rollback.Operation.Transaction.Envelope.PriorEstablishedOnBinding);
        var restored=Recover(rollback,Restored(rollback));Resolved(restored,rollback);Assert.Equal(7,restored.Mode.Rotation.Activation.SkinStamp);
        Assert.Equal(restored,Ready(restored,ulong.MaxValue-1));
        var next=Ready(restored,ulong.MaxValue);var nextIntent=next.Mode.Rotation.Pending.IssuedIntent;
        a=Armed(core.Decide(next,new RotationTransactionEvent.ConsumeReady(nextIntent)).State);a=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));a=Tx(a,new TransactionEvent.CompletionIndeterminate(Cor(a)));a=Rebind(a);
        var e=Issued(a);var done=Recover(a,e);Resolved(done,a);Assert.Null(done.LastRecovery.Before.LastRecovery);
        Assert.Equal(nextIntent.Candidate.CurrentObject,done.Mode.Rotation.Activation.Active);Assert.Equal(nextIntent.Candidate.CurrentObject.Id,done.Mode.Rotation.Activation.SelectedPackId);
        Assert.Equal(new DeathEpoch(ulong.MaxValue),done.Mode.Rotation.EpochHighWater);Assert.Equal(done,Ready(done,ulong.MaxValue));
        var record=done.LastRecovery;var linked=record.Before;
        for(var n=0;n<10000;n++)linked=linked with {LastRecovery=new(linked,e)};
        var malformed=done with {LastRecovery=record with {Before=linked}};Assert.False(core.IsStateValid(malformed));
        var regressed=done with {Mode=done.Mode with {OperationHighWater=0}};Assert.False(core.IsStateValid(regressed));
    }
    [Fact] public void IssuedOutcomeRejectsEntireEnvelopeFenceReceiptAndCurrentAuthorityMatrix()
    {
        var s=Rebind(Blocked());var e=Issued(s);var p=e.Fence.AuthoritativeParent;
        foreach(var bad in new[]{e with {Fence=e.Fence with {Correlation=e.Fence.Correlation with {Binding=new("newskin")}}},
            e with {Fence=e.Fence with {AuthoritativeParent=p with {Interlock=p.Interlock with {Target=p.Activation}}}},
            e with {Operation=e.Operation with {Transaction=e.Operation.Transaction with {PendingClosure=Prior(s)}}},
            e with {Receipt=e.Receipt with {ExpectedGenerationSha256=Hash(77)}},e with {Head=e.Head with {GenerationId=Id(77)}},
            e with {Head=e.Head with {Activation=e.Head.Activation with {SkinStamp=7}}},
            e with {Head=e.Head with {Activation=e.Head.Activation with {Active=Pack(receipt:77)}}},
            e with {Authority=e.Authority with {Skin=Skin}},e with {Authority=e.Authority with {LiveProof=new(Hero,new VerifiedLiveVisualProof.Vanilla(Skin))}},
            e with {Fence=null},e with {Receipt=null},e with {Head=null}})Noop(s,bad);
        Resolved(Recover(s,e),s);
    }
    [Fact] public void LateFailureParentRequiresOneExactArmHopAndNeverOverridesStoredFailure()
    {
        var s=Failed(false);var e=Restored(s);var t=s.Operation.Transaction;var arm=Parent(s);var hop=Receipt(arm,800);
        var parent=new VerifiedRegistryHead(hop.NewGenerationId,hop.NewGenerationSha256,t.Envelope.Prior,
            t.Interlock with {State=InterlockState.ROLLBACK_FAILED,OriginalFailure=t.OriginalFailure,RollbackFailure=t.RollbackFailure});
        var r=Receipt(parent);var good=e with {Fence=e.Fence with {AuthoritativeParent=parent,LateFailureReceipt=hop},Receipt=r,Head=Child(r,Prior(s))};
        foreach(var bad in new[]{good with {Fence=good.Fence with {LateFailureReceipt=null}},
            good with {Fence=good.Fence with {LateFailureReceipt=hop with {ExpectedGenerationId=Id(77)}}},
            good with {Fence=good.Fence with {LateFailureReceipt=hop with {ExpectedGenerationSha256=Hash(77)}}},
            good with {Fence=good.Fence with {LateFailureReceipt=hop with {NewGenerationSha256="bad"}}},
            good with {Fence=good.Fence with {AuthoritativeParent=parent with {Activation=parent.Activation with {Active=Pack(receipt:77)}}}},
            good with {Fence=good.Fence with {AuthoritativeParent=parent with {Interlock=parent.Interlock with {RollbackFailure="OTHER"}}}},
            good with {Fence=good.Fence with {AuthoritativeParent=parent with {Interlock=parent.Interlock with {OriginalFailure="OTHER"}}}},
            good with {Fence=good.Fence with {AuthoritativeParent=parent with {Interlock=RotationInterlock.Clear()}}},
            good with {Receipt=r with {ExpectedGenerationId=arm.GenerationId}}})Noop(s,bad);
        var persisted=Failed(true);var stored=Restored(persisted);Noop(persisted,stored with {Fence=stored.Fence with {LateFailureReceipt=hop}});
        var pending=Pending(Pack());var pe=Restored(pending);Noop(pending,pe with {Fence=pe.Fence with {LateFailureReceipt=hop}});
        var done=Recover(s,good);Resolved(done,s);Assert.Null(done.LastRecovery.Before.Operation.Transaction.FailureReceipt);
        Assert.Equal(hop,Assert.IsType<RotationRecoveryEvidence.PriorRestoredAndFenced>(done.LastRecovery.Evidence).Fence.LateFailureReceipt);Noop(done,good);
    }
    [Fact] public void NullEvidenceAndMalformedFenceNeverThrowOrConsumeOwnership()
    {
        var s=Pending();var e=Restored(s);Noop(s,null);
        foreach(var bad in new[]{e with {Operation=null},e with {Fence=null},e with {Fence=e.Fence with {Correlation=null}},e with {Fence=e.Fence with {AuthoritativeParent=null}},
            e with {Restoration=null},e with {Restoration=new(Hero,null)},e with {Receipt=null},e with {Head=null},e with {Authority=null}})Noop(s,bad);
        Resolved(Recover(s,e),s);
    }
}
