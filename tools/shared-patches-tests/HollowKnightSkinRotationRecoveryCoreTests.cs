using System;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinRotationRecoveryCoreTests
{
    private readonly SkinRotationTransactionCore core = new();
    private static readonly HeroBindingToken Hero = new("hero");
    private static readonly SkinBindingToken Skin = new("skin");
    private static readonly RotationRecoveryAuthority Authority = new(new("newhero"), new("newskin"));
    private static string Id(int n) => $"00000000-0000-0000-0000-{n:x12}";
    private static string Hash(char c) => new(c,64);
    private static ActiveVisual.Pack Pack(string id="a") => new(id,Hash('a'),Hash('b'),Hash('c'));
    private RotationTransactionState State(SkinMode mode=SkinMode.OFF, ActiveVisual active=null)
    {
        var a=new ActivationSnapshot(mode,"a",active ?? new ActiveVisual.Vanilla(),7);
        var ring=RotationRing.TryCreate(new[]{new RotationDescriptor("A",Pack(),true),new RotationDescriptor("B",Pack("b"),true)});
        return new(new RotationModeState(new RotationState(ring,a,Hero,Skin),new VerifiedRegistryHead(Id(1),Hash('d'),a,RotationInterlock.Clear())));
    }
    private RotationTransactionState Mode(RotationTransactionState s,RotationModeEvent e)=>core.Decide(s,new RotationTransactionEvent.Mode(e)).State;
    private RotationTransactionState Advance(RotationTransactionState s)=>Mode(s,new RotationModeEvent.AdvanceMode());
    private RotationTransactionState Selector(RotationTransactionState s,RotationEvent e)=>Mode(s,new RotationModeEvent.Selector(e));
    private RotationTransactionState Rebind(RotationTransactionState s,RotationRecoveryAuthority a=null)=>Selector(s,new RotationEvent.Rebind((a ?? Authority).Hero,(a ?? Authority).Skin));
    private static TransactionCorrelation Cor(RotationTransactionState s)=>new(s.Operation.Transaction.Envelope.TransactionId,s.Operation.Transaction.Envelope.Binding);
    private RotationTransactionState Tx(RotationTransactionState s,TransactionEvent e)=>core.Decide(s,new RotationTransactionEvent.Transaction(Hero,e)).State;
    private RotationTransactionState Armed(RotationTransactionState s)
    {
        var p=Tx(s,new TransactionEvent.Prepared(Cor(s)));var e=p.Operation.Transaction.Envelope;
        var r=new RegistryCommitReceipt(e.BaseGenerationId,e.BaseGenerationSha256,Id(2),Hash('e'));
        var l=new RotationInterlock(InterlockState.ARMED,e.TransactionId,e.Operation,e.BaseGenerationId,e.BaseGenerationSha256,e.Prior,e.Target,e.Binding,e.PriorEstablishedOnBinding,null,null);
        return Tx(p,new TransactionEvent.ArmCommitted(Cor(s),r,new(Id(2),Hash('e'),e.Prior,l)));
    }
    private static TransactionEvent.CompletionCommitted Completion(RotationTransactionState s)
    {
        var t=s.Operation.Transaction;var arm=t.ArmCommitReceipt;
        return new(Cor(s),new(arm.NewGenerationId,arm.NewGenerationSha256,Id(3),Hash('f')),new(Id(3),Hash('f'),t.PendingClosure,RotationInterlock.Clear()));
    }
    private RotationTransactionState Issued()
    {
        var p=Selector(State(SkinMode.ROTATE,Pack()),new RotationEvent.ConfirmDeath(new(4),Hero,Skin));
        return Selector(p,new RotationEvent.StableRespawn(new(new(4),Hero,Skin)));
    }
    private RotationTransactionState Recover(RotationTransactionState s,RotationRecoveryEvidence e)
    {var d=core.Decide(s,new RotationTransactionEvent.Recover(e));Assert.Empty(d.Commands);return d.State;}
    private void Noop(RotationTransactionState s,RotationRecoveryEvidence e)=>Assert.Equal(s,Recover(s,e));
    private void Done(RotationTransactionState s,RotationTransactionState before)
    {
        Assert.Null(s.Operation);Assert.Null(s.Mode.Operation);Assert.Null(s.Mode.Rotation.Pending);Assert.False(s.ReadinessReboundBlocked);
        Assert.NotNull(s.LastRecovery);Assert.True(core.IsStateValid(s));Assert.Equal(before.Mode.OperationHighWater,s.Mode.OperationHighWater);
        Assert.Equal(before.Mode.Rotation.EpochHighWater,s.Mode.Rotation.EpochHighWater);Assert.Equal(before with {LastRecovery=null},s.LastRecovery.Before);
    }
    [Fact] public void ExactConsumedClosureRecoversWithCurrentAuthorityWithoutOldProofOrReplay()
    {
        var ready=Issued();var intent=ready.Mode.Rotation.Pending.IssuedIntent;
        var a=Armed(core.Decide(ready,new RotationTransactionEvent.ConsumeReady(intent)).State);
        var applied=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));var s=Rebind(applied);
        var e=new RotationRecoveryEvidence.VisualClosure(s.Operation,Completion(applied),Authority);
        var result=Recover(s,e);Done(result,s);Assert.Equal(Pack("b"),result.Mode.Rotation.Activation.Active);Assert.Equal("b",result.Mode.Rotation.Activation.SelectedPackId);
        Assert.Null(result.Mode.LiveProof);Assert.Equal(TransactionPhase.APPLIED,result.LastRecovery.Before.Operation.Transaction.Phase);
        Noop(result,e);Assert.Equal(result,core.Decide(result,new RotationTransactionEvent.ConsumeReady(intent)).State);
        var next=Selector(result,new RotationEvent.ConfirmDeath(new(5),Authority.Hero,Authority.Skin));Assert.NotNull(next.Mode.Rotation.Pending);Assert.Equal(new DeathEpoch(5),next.Mode.Rotation.EpochHighWater);
    }
    [Fact] public void WrongReceiptTargetEpochAndCurrentBindingRemainCorrectable()
    {
        var ready=Issued();var a=Armed(core.Decide(ready,new RotationTransactionEvent.ConsumeReady(ready.Mode.Rotation.Pending.IssuedIntent)).State);
        var applied=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));var s=Rebind(applied);var e=new RotationRecoveryEvidence.VisualClosure(s.Operation,Completion(applied),Authority);
        foreach(var bad in new[]{e with {Authority=Authority with {Hero=Hero}},e with {Authority=Authority with {Skin=Skin}},
            e with {Operation=e.Operation with {Readiness=e.Operation.Readiness with {Epoch=new(8)}}},
            e with {Operation=e.Operation with {Origin=e.Operation.Origin with {Head=e.Operation.Origin.Head with {GenerationSha256=Hash('0')}}}},
            e with {Completion=e.Completion with {Correlation=e.Completion.Correlation with {Binding=Authority.Skin}}},
            e with {Completion=e.Completion with {CommitReceipt=e.Completion.CommitReceipt with {ExpectedGenerationId=Id(9)}}},
            e with {Completion=e.Completion with {VerifiedHead=e.Completion.VerifiedHead with {Activation=e.Completion.VerifiedHead.Activation with {SelectedPackId="a"}}}},
            e with {Completion=e.Completion with {VerifiedHead=e.Completion.VerifiedHead with {Interlock=RotationInterlock.Clear() with {TransactionId=Id(9)}}}},
            e with {Authority=Authority with {LiveProof=new(Hero,new VerifiedLiveVisualProof.Pack(Skin,Pack("b")))}}})Noop(s,bad);
        var proof=new HeroVerifiedVisual(Authority.Hero,new VerifiedLiveVisualProof.Pack(Authority.Skin,Pack("b")));
        var result=Recover(s,e with {Authority=Authority with {LiveProof=proof}});Done(result,s);Assert.Equal(proof,result.Mode.LiveProof);
    }
    [Fact] public void RolledBackDurablePriorClosureRecoversWithoutSpeculativeCommands()
    {
        var a=Armed(Advance(State(active:Pack())));var failed=Tx(a,new TransactionEvent.ApplyFailed(Cor(a),"FAILED"));
        var rolled=Tx(failed,new TransactionEvent.RollbackVerified(Cor(a),true));var s=Rebind(rolled);
        var result=Recover(s,new RotationRecoveryEvidence.VisualClosure(s.Operation,Completion(rolled),Authority));Done(result,s);
        Assert.Equal(SkinMode.OFF,result.Mode.Rotation.Activation.Mode);Assert.Equal(Pack(),result.Mode.Rotation.Activation.Active);Assert.Equal(8,result.Mode.Rotation.Activation.SkinStamp);Assert.Null(result.Mode.LiveProof);
    }
    [Fact] public void PreparingAndPreparedNeedExactFencedNonexecutionAndUnchangedBase()
    {
        var begin=Advance(State());var prepared=Tx(begin,new TransactionEvent.Prepared(Cor(begin)));
        foreach(var p in new[]{begin,prepared})
        {
            var s=Rebind(p);var e=new RotationRecoveryEvidence.VisualFencedNotExecuted(s.Operation,s.Mode.Head,Authority);
            Noop(s,e with {Head=e.Head with {GenerationId=Id(8)}});Noop(s,e with {Receipt=new(Id(1),Hash('d'),Id(3),Hash('f'))});
            var result=Recover(s,e);Done(result,s);Assert.Equal(s.Mode.Head,result.Mode.Head);Assert.Equal(p.Operation.Transaction.Phase,result.LastRecovery.Before.Operation.Transaction.Phase);
            var retry=Advance(result);Assert.NotNull(retry.Operation);Assert.Equal(2,retry.Mode.OperationHighWater);Assert.NotEqual(p.Operation.Transaction.Envelope.TransactionId,retry.Operation.Transaction.Envelope.TransactionId);
        }
    }
    [Fact] public void ArmedAbortNeedsExactArmChildPriorClosureAndFencing()
    {
        foreach(var active in new ActiveVisual[]{new ActiveVisual.Vanilla(),Pack()})
        {
            var s=Rebind(Armed(Advance(State(active:active))));var t=s.Operation.Transaction;var e=t.Envelope;var arm=t.ArmCommitReceipt;
            var prior=active is ActiveVisual.Pack ? e.Prior with {SkinStamp=e.Prior.SkinStamp+1} : e.Prior;
            var receipt=new RegistryCommitReceipt(arm.NewGenerationId,arm.NewGenerationSha256,Id(3),Hash('f'));
            var evidence=new RotationRecoveryEvidence.VisualFencedNotExecuted(s.Operation,new(Id(3),Hash('f'),prior,RotationInterlock.Clear()),Authority,receipt);
            Noop(s,evidence with {Head=s.Mode.Head,Receipt=null});Noop(s,evidence with {Receipt=receipt with {ExpectedGenerationId=Id(1)}});
            Noop(s,evidence with {Head=evidence.Head with {Activation=e.Target}});Noop(s,evidence with {Head=evidence.Head with {Interlock=t.Interlock}});
            var result=Recover(s,evidence);Done(result,s);Assert.Equal(prior,result.Mode.Rotation.Activation);Assert.Equal(TransactionPhase.ARMED,result.LastRecovery.Before.Operation.Transaction.Phase);
        }
    }
    [Fact] public void UnconsumedRetirementNeverRetargetsOrReissuesAndNewDeathIsReachable()
    {
        var p=Issued();var intent=p.Mode.Rotation.Pending.IssuedIntent;var s=Rebind(p);var e=new RotationRecoveryEvidence.ReadinessFencedNotExecuted(intent,p.Mode.Head,Authority);
        Noop(s,e with {Intent=intent with {Epoch=new(3)}});Noop(s,e with {Intent=intent with {Hero=Authority.Hero}});
        Noop(s,e with {Intent=intent with {Candidate=intent.Candidate with {CurrentObject=Pack()}}});Noop(s,e with {Head=e.Head with {GenerationSha256=Hash('0')}});
        var result=Recover(s,e);Done(result,s);Noop(result,e);Assert.Equal(result,core.Decide(result,new RotationTransactionEvent.ConsumeReady(intent)).State);
        Assert.Equal(result,Selector(result,new RotationEvent.ConfirmDeath(intent.Epoch,Authority.Hero,Authority.Skin)));
        var next=Selector(result,new RotationEvent.ConfirmDeath(new(5),Authority.Hero,Authority.Skin));var stable=Selector(next,new RotationEvent.StableRespawn(new(new(5),Authority.Hero,Authority.Skin)));
        Assert.Equal(new DeathEpoch(5),stable.Mode.Rotation.Pending.IssuedIntent.Epoch);Assert.Single(core.Decide(stable,new RotationTransactionEvent.ConsumeReady(stable.Mode.Rotation.Pending.IssuedIntent)).Commands);
    }
    [Fact] public void SameBindingReturnAndVisualOnlyProofDoNotResolveButExactEvidenceDoes()
    {
        var p=Issued();var backAuthority=new RotationRecoveryAuthority(Hero,Skin);var s=Rebind(Rebind(p),backAuthority);var intent=p.Mode.Rotation.Pending.IssuedIntent;
        var proven=Mode(s,new RotationModeEvent.VerifiedVisual(new(Hero,new VerifiedLiveVisualProof.Pack(Skin,Pack()))));
        Assert.True(proven.ReadinessReboundBlocked);Assert.Equal(proven,core.Decide(proven,new RotationTransactionEvent.ConsumeReady(intent)).State);
        Done(Recover(proven,new RotationRecoveryEvidence.ReadinessFencedNotExecuted(intent,p.Mode.Head,backAuthority)),proven);
    }
    [Fact] public void H1ModeClosureUsesOriginalCasAndCurrentVanillaAuthorityForOff()
    {
        foreach(var off in new[]{false,true})
        {
            var initial=State(off ? SkinMode.ROTATE : SkinMode.ON,Pack());if(off)initial=Mode(initial,new RotationModeEvent.VerifiedVisual(new(Hero,new VerifiedLiveVisualProof.Vanilla(Skin))));
            var begun=Advance(initial);var s=Rebind(begun);var p=s.Mode.Operation;
            var complete=new RotationModeEvent.ModeCommitted(p.Correlation,new(p.BaseHead.GenerationId,p.BaseHead.GenerationSha256,Id(3),Hash('f')),new(Id(3),Hash('f'),p.Target,RotationInterlock.Clear()));
            var current=off ? Authority with {LiveProof=new(Authority.Hero,new VerifiedLiveVisualProof.Vanilla(Authority.Skin))} : Authority;
            var e=new RotationRecoveryEvidence.ModeClosure(p,complete,current);
            Noop(s,e with {Operation=p with {Correlation=p.Correlation with {Hero=Authority.Hero}}});Noop(s,e with {Completion=complete with {Receipt=complete.Receipt with {ExpectedGenerationId=Id(8)}}});
            Noop(s,e with {Completion=complete with {Head=complete.Head with {Activation=initial.Mode.Rotation.Activation}}});
            if(off){Noop(s,e with {Authority=Authority});Noop(s,e with {Authority=Authority with {LiveProof=new(Hero,new VerifiedLiveVisualProof.Vanilla(Skin))}});}
            var result=Recover(s,e);Done(result,s);Assert.Equal(p.Target,result.Mode.Rotation.Activation);Assert.Equal(current.LiveProof,result.Mode.LiveProof);Noop(result,e);
        }
    }
    [Fact] public void ConsumedRecoveryKeepsDisarmAndReachesFreshModeOffTransaction()
    {
        var ready=Issued();var a=Armed(core.Decide(ready,new RotationTransactionEvent.ConsumeReady(ready.Mode.Rotation.Pending.IssuedIntent)).State);
        var applied=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));var s=Advance(Rebind(applied));
        var result=Recover(s,new RotationRecoveryEvidence.VisualClosure(s.Operation,Completion(applied),Authority));Done(result,s);
        Assert.True(result.Mode.OffRequested);var off=Advance(result);Assert.Equal(SkinOperationKind.MODE_OFF,off.Operation.Transaction.Envelope.Operation);
        Assert.Equal(Authority.Hero,off.Operation.Origin.Rotation.CurrentHero);Assert.Equal(result.Mode.Rotation.Activation.SelectedPackId,off.Operation.Transaction.Envelope.Target.SelectedPackId);
    }
    [Fact] public void VisualModeOffClosureRetainsSelectionAndClearsDisarm()
    {
        var a=Armed(Advance(State(SkinMode.ROTATE,Pack())));var applied=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));var s=Rebind(applied);
        var result=Recover(s,new RotationRecoveryEvidence.VisualClosure(s.Operation,Completion(applied),Authority));Done(result,s);
        Assert.Equal(SkinMode.OFF,result.Mode.Rotation.Activation.Mode);Assert.IsType<ActiveVisual.Vanilla>(result.Mode.Rotation.Activation.Active);
        Assert.Equal("a",result.Mode.Rotation.Activation.SelectedPackId);Assert.False(result.Mode.OffRequested);Assert.Null(result.Mode.LiveProof);
    }
    [Fact] public void LastAuditSurvivesExecutionAndNextRecoveryReplacesRatherThanNestsIt()
    {
        var a=new RotationRecoveryAuthority(Hero,Skin);var parked=Rebind(Rebind(Advance(State())),a);
        var recovered=Recover(parked,new RotationRecoveryEvidence.VisualFencedNotExecuted(parked.Operation,parked.Mode.Head,a));
        var begun=Advance(recovered);Assert.Equal(recovered.LastRecovery,begun.LastRecovery);
        var armed=Armed(begun);var applied=Tx(armed,new TransactionEvent.ApplyVerified(Cor(armed)));var committed=Tx(applied,Completion(applied));
        Assert.Equal(recovered.LastRecovery,committed.LastRecovery);Assert.True(core.IsStateValid(committed));
        var second=Rebind(begun);var done=Recover(second,new RotationRecoveryEvidence.VisualFencedNotExecuted(second.Operation,second.Mode.Head,Authority));
        Assert.NotEqual(recovered.LastRecovery,done.LastRecovery);Assert.Null(done.LastRecovery.Before.LastRecovery);Assert.True(core.IsStateValid(done));
    }
    [Fact] public void ForgedNestedOrUnqualifiedAuditAndRegressedHighwaterAreInvalid()
    {
        var s=Rebind(Advance(State()));var e=new RotationRecoveryEvidence.VisualFencedNotExecuted(s.Operation,s.Mode.Head,Authority);var result=Recover(s,e);var record=result.LastRecovery;
        foreach(var bad in new[]{result with {LastRecovery=record with {Before=record.Before with {LastRecovery=record}}},
            result with {LastRecovery=record with {Evidence=e with {Authority=Authority with {Hero=Hero}}}},
            result with {Mode=result.Mode with {OperationHighWater=0}},result with {LastRecovery=record with {Before=null}},result with {LastRecovery=record with {Evidence=null}}})
        {Assert.False(core.IsStateValid(bad));Assert.Equal(bad,Advance(bad));}
    }
    [Fact] public void BlockedAndRollbackPendingRemainTask68WithoutConsumingEvidence()
    {
        var a=Armed(Advance(State()));var applied=Tx(a,new TransactionEvent.ApplyVerified(Cor(a)));var complete=Completion(applied);
        foreach(var p in new[]{Tx(applied,new TransactionEvent.CompletionIndeterminate(Cor(a))),Tx(applied,new TransactionEvent.CompletionRejected(Cor(a),"REJECTED"))})
        {
            var s=Rebind(p);Noop(s,new RotationRecoveryEvidence.VisualClosure(s.Operation,complete,Authority));Noop(s,new RotationRecoveryEvidence.VisualFencedNotExecuted(s.Operation,s.Mode.Head,Authority));Assert.NotNull(s.Operation);Assert.Null(s.LastRecovery);
        }
    }
    [Fact] public void PublicNullAndUnknownValuesFailClosedWithoutConsumingResolution()
    {
        var s=Rebind(Advance(State()));var e=new RotationRecoveryEvidence.VisualFencedNotExecuted(s.Operation,s.Mode.Head,Authority);
        foreach(var bad in new RotationRecoveryEvidence[]{null,e with {Operation=null},e with {Head=null},e with {Authority=null},e with {Authority=Authority with {Hero=null}},e with {Authority=Authority with {Skin=null}},e with {Authority=Authority with {LiveProof=new(Authority.Hero,null)}}})Noop(s,bad);
        Assert.False(core.IsStateValid(s with {Operation=s.Operation with {Transaction=s.Operation.Transaction with {Phase=(TransactionPhase)99}}}));
        Done(Recover(s,e),s);
    }
}
