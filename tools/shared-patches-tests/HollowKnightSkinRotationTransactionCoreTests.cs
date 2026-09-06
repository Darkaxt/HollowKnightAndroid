using System;
using System.Linq;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinRotationTransactionCoreTests
{
    private readonly SkinRotationTransactionCore core = new();
    private static string Id(int n) => $"00000000-0000-0000-0000-{n:x12}";
    private static string Hash(char c) => new(c,64);
    private static ActiveVisual.Pack Pack(string id="a") => new(id,Hash('a'),Hash('b'),Hash('c'));
    private static readonly HeroBindingToken Hero = new("hero");
    private static readonly SkinBindingToken Skin = new("skin");
    private RotationTransactionState State(SkinMode mode=SkinMode.OFF, ActiveVisual active=null)
    {
        var a=new ActivationSnapshot(mode,"a",active ?? new ActiveVisual.Vanilla(),7);
        var ring=RotationRing.TryCreate(new[]{new RotationDescriptor("A",Pack(),false),new RotationDescriptor("B",Pack("b"),true),new RotationDescriptor("C",Pack("c"),true)});
        return new(new RotationModeState(new RotationState(ring,a,Hero,Skin),new VerifiedRegistryHead(Id(1),Hash('d'),a,RotationInterlock.Clear())));
    }
    private RotationTransactionDecision Advance(RotationTransactionState s)=>core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.AdvanceMode()));
    private RotationTransactionDecision Tx(RotationTransactionState s,TransactionEvent e,HeroBindingToken hero=null)=>core.Decide(s,new RotationTransactionEvent.Transaction(hero ?? Hero,e));
    private static TransactionCorrelation Cor(RotationTransactionState s)=>new(s.Operation.Transaction.Envelope.TransactionId,s.Operation.Transaction.Envelope.Binding);
    private RotationTransactionState Armed(RotationTransactionState s)
    {
        var prepared=Tx(s,new TransactionEvent.Prepared(Cor(s))).State;
        var e=prepared.Operation.Transaction.Envelope;
        var n=100+(int)s.Mode.OperationHighWater*2;
        var r=new RegistryCommitReceipt(e.BaseGenerationId,e.BaseGenerationSha256,Id(n),n.ToString("x64"));
        var l=new RotationInterlock(InterlockState.ARMED,e.TransactionId,e.Operation,e.BaseGenerationId,e.BaseGenerationSha256,e.Prior,e.Target,e.Binding,e.PriorEstablishedOnBinding,null,null);
        return Tx(prepared,new TransactionEvent.ArmCommitted(Cor(s),r,new(r.NewGenerationId,r.NewGenerationSha256,e.Prior,l))).State;
    }
    private RotationTransactionEvent.Transaction Completion(RotationTransactionState s)
    {
        var t=s.Operation.Transaction;var arm=t.ArmCommitReceipt;
        var n=101+(int)s.Mode.OperationHighWater*2;
        var r=new RegistryCommitReceipt(arm.NewGenerationId,arm.NewGenerationSha256,Id(n),n.ToString("x64"));
        return new(Hero,new TransactionEvent.CompletionCommitted(Cor(s),r,new(r.NewGenerationId,r.NewGenerationSha256,t.PendingClosure,RotationInterlock.Clear())));
    }
    private RotationTransactionState Complete(RotationTransactionState s)=>core.Decide(s,Completion(s)).State;
    private void Noop(RotationTransactionState s,RotationTransactionEvent e)
    {var d=core.Decide(s,e);Assert.Equal(s,d.State);Assert.Empty(d.Commands);}

    [Fact] public void ModeOnUsesCurrentSelectedObjectAndDelegatesFullEnvelope()
    {
        var s=State(active:Pack() with {TreeSha256=Hash('9')});var d=Advance(s);
        var command=Assert.IsType<RotationTransactionCommand.Transaction>(Assert.Single(d.Commands));
        Assert.Equal(Hero,command.Hero);var prepare=Assert.IsType<SkinCommand.Prepare>(command.Command);
        Assert.Equal(Pack(),prepare.Envelope.Target.Active);Assert.Equal(SkinOperationKind.MODE_ON,prepare.Envelope.Operation);
        Assert.False(prepare.Envelope.PriorEstablishedOnBinding);Assert.Equal(s.Mode.Head,d.State.Mode.Head);
        Assert.Equal(1,d.State.Mode.OperationHighWater);
        Assert.Equal(new SkinCommand.Arm(prepare.Envelope),Assert.IsType<RotationTransactionCommand.Transaction>(Assert.Single(Tx(d.State,new TransactionEvent.Prepared(Cor(d.State))).Commands)).Command);
    }
    [Fact] public void PublicationRequiresExactDurableClosureNotVisualSuccess()
    {
        var initial=State();var armed=Armed(Advance(initial).State);
        var applied=Tx(armed,new TransactionEvent.ApplyVerified(Cor(armed))).State;
        Assert.Equal(initial.Mode.Head,applied.Mode.Head);Assert.NotNull(applied.Operation);
        var e=Completion(applied);var c=(TransactionEvent.CompletionCommitted)e.Event;
        Noop(applied,e with {Event=c with {VerifiedHead=c.VerifiedHead with {Activation=c.VerifiedHead.Activation with {SelectedPackId="b"}}}});
        Noop(applied,e with {Hero=new("other")});
        var done=core.Decide(applied,e).State;Assert.Null(done.Operation);Assert.Equal(SkinMode.ON,done.Mode.Rotation.Activation.Mode);
        Assert.Equal(Pack(),done.Mode.Rotation.Activation.Active);Assert.Equal(8,done.Mode.Rotation.Activation.SkinStamp);Assert.Null(done.Mode.LiveProof);Noop(done,e);
    }
    [Fact] public void ModeOffCancelsUnissuedPendingAndRestoresVanillaWithoutChangingSelected()
    {
        var s=State(SkinMode.ROTATE,Pack());
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new(9),Hero,Skin)))).State;
        var d=Advance(s);Assert.Null(d.State.Mode.Rotation.Pending);Assert.NotNull(d.State.Mode.CanceledPending);
        var e=d.State.Operation.Transaction.Envelope;Assert.Equal(SkinOperationKind.MODE_OFF,e.Operation);Assert.IsType<ActiveVisual.Vanilla>(e.Target.Active);Assert.Equal("a",e.Target.SelectedPackId);
        var armed=Armed(d.State);var done=Complete(Tx(armed,new TransactionEvent.ApplyVerified(Cor(armed))).State);
        Assert.Equal(SkinMode.OFF,done.Mode.Rotation.Activation.Mode);Assert.False(done.Mode.OffRequested);Assert.Null(done.Mode.CanceledPending);Assert.Equal(new DeathEpoch(9),done.Mode.Rotation.EpochHighWater);
    }
    [Fact] public void ExactIssuedReadinessIsConsumedOnceAndRetiredOnlyAfterClosure()
    {
        var s=State(SkinMode.ROTATE,Pack());
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new(4),Hero,Skin)))).State;
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.StableRespawn(new(new(4),Hero,Skin))))).State;
        var intent=s.Mode.Rotation.Pending.IssuedIntent;
        Noop(s,new RotationTransactionEvent.ConsumeReady(intent with {Candidate=intent.Candidate with {CurrentObject=Pack("c")}}));
        var begun=core.Decide(s,new RotationTransactionEvent.ConsumeReady(intent)).State;
        Assert.Equal(intent,begun.Operation.Readiness);Assert.Equal(intent.Candidate.CurrentObject,begun.Operation.Transaction.Envelope.Target.Active);
        Noop(begun,new RotationTransactionEvent.ConsumeReady(intent));
        var armed=Armed(begun);var applied=Tx(armed,new TransactionEvent.ApplyVerified(Cor(armed))).State;Assert.NotNull(applied.Mode.Rotation.Pending);
        var done=Complete(applied);Assert.Null(done.Mode.Rotation.Pending);Assert.Equal("b",done.Mode.Rotation.Activation.SelectedPackId);Assert.Equal(new DeathEpoch(4),done.Mode.Rotation.EpochHighWater);
        Noop(done,new RotationTransactionEvent.ConsumeReady(intent));
    }
    [Fact] public void RejectedTargetDelegatesRollbackAndRequiresDurableRollbackClosure()
    {
        var s=Armed(Advance(State()).State);var applied=Tx(s,new TransactionEvent.ApplyVerified(Cor(s))).State;
        var rollback=Tx(applied,new TransactionEvent.CompletionRejected(Cor(s),"CAS_REJECTED"));
        Assert.IsType<SkinCommand.Rollback>(Assert.IsType<RotationTransactionCommand.Transaction>(Assert.Single(rollback.Commands)).Command);
        var rolled=Tx(rollback.State,new TransactionEvent.RollbackVerified(Cor(s),true)).State;
        Assert.NotNull(rolled.Operation);Assert.Equal(State().Mode.Head,rolled.Mode.Head);
        var done=Complete(rolled);Assert.Null(done.Operation);Assert.Equal(SkinMode.OFF,done.Mode.Rotation.Activation.Mode);
    }
    [Fact] public void RebindPreservesOriginAndNeverPublishesObsoleteCompletionEvenAfterReturn()
    {
        var s=Armed(Advance(State()).State);s=Tx(s,new TransactionEvent.ApplyVerified(Cor(s))).State;var complete=Completion(s);
        var rebound=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.Rebind(new("newhero"),Skin)))) .State;
        Assert.Equal(s.Operation.Origin,rebound.Operation.Origin);Assert.True(rebound.Operation.ReboundBlocked);Noop(rebound,complete);
        var back=core.Decide(rebound,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.Rebind(Hero,Skin)))).State;
        Assert.True(back.Operation.ReboundBlocked);Noop(back,complete);
    }
    [Fact] public void BlockedTransactionCannotBeResetOrCompletedThroughWrapper()
    {
        var s=Armed(Advance(State()).State);s=Tx(s,new TransactionEvent.ApplyVerified(Cor(s))).State;var complete=Completion(s);
        var blocked=Tx(s,new TransactionEvent.CompletionIndeterminate(Cor(s))).State;
        Assert.Equal(TransactionPhase.BLOCKED,blocked.Operation.Transaction.Phase);Noop(blocked,complete);Assert.Equal(blocked,Advance(blocked).State);
    }
    [Fact] public void IssuedReadinessCannotBecomeFreshByReturningToOriginalBinding()
    {
        var s=State(SkinMode.ROTATE,Pack());
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new(4),Hero,Skin)))).State;
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.StableRespawn(new(new(4),Hero,Skin))))).State;
        var intent=s.Mode.Rotation.Pending.IssuedIntent;
        var rebound=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.Rebind(new("other"),Skin)))).State;
        var back=core.Decide(rebound,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.Rebind(Hero,Skin)))).State;
        Assert.Equal(intent,back.Mode.Rotation.Pending.IssuedIntent);
        Noop(back,new RotationTransactionEvent.ConsumeReady(intent));
        Assert.Equal("task67-readiness-rebound-resolution-required",core.Decide(back,new RotationTransactionEvent.ConsumeReady(intent)).Diagnosis);
        var disarmed=Advance(back).State;Assert.True(disarmed.ReadinessReboundBlocked);Assert.True(disarmed.Mode.OffRequested);
        Noop(disarmed,new RotationTransactionEvent.ConsumeReady(intent));
        Assert.False(core.IsStateValid(State() with {ReadinessReboundBlocked=true}));
    }
    [Fact] public void ReboundIssuedReadinessRequiresStickyFlagInPublicState()
    {
        var s=State(SkinMode.ROTATE,Pack());
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new(4),Hero,Skin)))).State;
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.StableRespawn(new(new(4),Hero,Skin))))).State;
        foreach(var binding in new[]{new RotationEvent.Rebind(new("otherhero"),Skin),new RotationEvent.Rebind(Hero,new("otherskin")),new RotationEvent.Rebind(new("otherhero"),new("otherskin"))})
        {
            var rebound=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(binding))).State;
            Assert.True(core.IsStateValid(rebound));Assert.True(rebound.ReadinessReboundBlocked);
            var forged=rebound with {ReadinessReboundBlocked=false};
            Assert.False(core.IsStateValid(forged));
            Noop(forged,new RotationTransactionEvent.ConsumeReady(forged.Mode.Rotation.Pending.IssuedIntent));
            Assert.Equal("invalid-state",core.Decide(forged,new RotationTransactionEvent.Mode(new RotationModeEvent.AdvanceMode())).Diagnosis);
        }
        var consumed=core.Decide(s,new RotationTransactionEvent.ConsumeReady(s.Mode.Rotation.Pending.IssuedIntent)).State;
        var inflight=core.Decide(consumed,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.Rebind(new("otherhero"),Skin)))).State;
        Assert.True(core.IsStateValid(inflight));Assert.True(inflight.Operation.ReboundBlocked);Assert.False(inflight.ReadinessReboundBlocked);
    }
    [Fact] public void PreStableRebindRetokensWithoutBlockingTheFirstReadiness()
    {
        var s=State(SkinMode.ROTATE,Pack());var h=new HeroBindingToken("newhero");var k=new SkinBindingToken("newskin");
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new(4),Hero,Skin)))).State;
        var candidate=s.Mode.Rotation.Pending.Candidate;
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.Rebind(h,k)))).State;
        Assert.False(s.ReadinessReboundBlocked);Assert.Equal(candidate,s.Mode.Rotation.Pending.Candidate);
        s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.Selector(new RotationEvent.StableRespawn(new(new(4),h,k))))).State;
        var ready=s.Mode.Rotation.Pending.IssuedIntent;Assert.Equal(h,ready.Hero);Assert.Equal(k,ready.Skin);
        var d=core.Decide(s,new RotationTransactionEvent.ConsumeReady(ready));Assert.Single(d.Commands);Assert.True(core.IsStateValid(d.State));
        Assert.Equal(h,Assert.IsType<RotationTransactionCommand.Transaction>(Assert.Single(d.Commands)).Hero);
    }
    [Fact] public void EstablishedVisualNoopUsesModeCasWhileTransactionCoreStillVetoesVisualExecution()
    {
        var s=State(active:Pack());s=s with {Mode=s.Mode with {LiveProof=new(Hero,new VerifiedLiveVisualProof.Pack(Skin,Pack()))}};
        var a=s.Mode.Rotation.Activation;
        var envelope=new TransactionEnvelope(Id(9),SkinOperationKind.MODE_ON,s.Mode.Head.GenerationId,s.Mode.Head.GenerationSha256,
            a,a with {Mode=SkinMode.ON,SkinStamp=a.SkinStamp+1},Skin,true);
        var veto=new SkinTransactionCore().Decide(new TransactionState(Binding:Skin,Activation:a),new TransactionEvent.Begin(envelope));
        Assert.Equal("invalid-envelope",veto.Diagnosis);Assert.Empty(veto.Commands);
        var d=Advance(s);Assert.Equal("commit-mode",d.Diagnosis);Assert.Null(d.State.Operation);
        Assert.IsType<RotationTransactionCommand.Mode>(Assert.Single(d.Commands));
        Assert.Equal(a with {Mode=SkinMode.ON},d.State.Mode.Operation.Target);
    }
    [Fact] public void SharedHighwaterSurvivesTwoModeAndVisualCyclesWithoutIdReuse()
    {
        var s=State();var ids=new System.Collections.Generic.HashSet<string>();
        for(var cycle=0;cycle<2;cycle++)
        {
            s=Advance(s).State;Assert.True(ids.Add(s.Operation.Transaction.Envelope.TransactionId));
            var a=Armed(s);s=Complete(Tx(a,new TransactionEvent.ApplyVerified(Cor(a))).State);
            s=Advance(s).State;var p=s.Mode.Operation;Assert.True(ids.Add(p.Correlation.OperationId));
            var n=1000+(int)s.Mode.OperationHighWater;
            s=core.Decide(s,new RotationTransactionEvent.Mode(new RotationModeEvent.ModeCommitted(p.Correlation,
                new(p.BaseHead.GenerationId,p.BaseHead.GenerationSha256,Id(n),n.ToString("x64")),new(Id(n),n.ToString("x64"),p.Target,RotationInterlock.Clear())))).State;
            s=Advance(s).State;Assert.True(ids.Add(s.Operation.Transaction.Envelope.TransactionId));
            a=Armed(s);s=Complete(Tx(a,new TransactionEvent.ApplyVerified(Cor(a))).State);
            Assert.Equal(SkinMode.OFF,s.Mode.Rotation.Activation.Mode);Assert.Equal((cycle+1)*3,s.Mode.OperationHighWater);
        }
        Assert.Equal(6,ids.Count);Assert.Equal(11,s.Mode.Rotation.Activation.SkinStamp);
    }
    [Fact] public void ForgedCommittedStateCannotPublishAndQueriesRejectMalformedValues()
    {
        var a=Armed(Advance(State()).State);var s=Tx(a,new TransactionEvent.ApplyVerified(Cor(a))).State;var c=Completion(s);
        var terminal=new SkinTransactionCore().Decide(s.Operation.Transaction,c.Event).State;
        Assert.True(new SkinTransactionCore().IsStateValid(terminal));
        var forged=s with {Operation=s.Operation with {Transaction=terminal}};
        Assert.False(core.IsStateValid(forged));Noop(forged,c);
        foreach(var bad in new[]{null,s with {Mode=null},s with {Operation=s.Operation with {Origin=null}},s with {Operation=s.Operation with {Transaction=null}},
            s with {Operation=s.Operation with {Transaction=s.Operation.Transaction with {Phase=(TransactionPhase)99}}},
            s with {Mode=s.Mode with {OperationHighWater=0}},s with {Mode=s.Mode with {Head=s.Mode.Head with {GenerationSha256=Hash('9')}}}})
        {Assert.False(core.IsStateValid(bad));Noop(bad,c);}
        Assert.False(new SkinTransactionCore().IsStateValid(null));
        Assert.False(new SkinTransactionCore().IsStateValid(s.Operation.Transaction with {Envelope=null}));
        Assert.False(new SkinTransactionCore().IsStateValid(s.Operation.Transaction with {Binding=null}));
        Noop(s,null);Noop(s,new RotationTransactionEvent.Transaction(null,c.Event));Noop(s,new RotationTransactionEvent.Transaction(Hero,null));
    }
    [Fact] public void OperationCannotLoseItsCurrentAuthoritativeBinding()
    {
        var s=Advance(State()).State;
        var forged=s with {Mode=s.Mode with {Rotation=s.Mode.Rotation with {CurrentHero=null,CurrentSkin=null}},Operation=s.Operation with {ReboundBlocked=true}};
        Assert.False(core.IsStateValid(forged));Noop(forged,new RotationTransactionEvent.Mode(new RotationModeEvent.AdvanceMode()));
    }
    [Fact] public void ExactArmAndClosureReceiptHeadMatrixRejectsThenAccepts()
    {
        var s=Advance(State()).State;var p=Tx(s,new TransactionEvent.Prepared(Cor(s))).State;var e=p.Operation.Transaction.Envelope;
        var r=new RegistryCommitReceipt(e.BaseGenerationId,e.BaseGenerationSha256,Id(20),Hash('e'));
        var l=new RotationInterlock(InterlockState.ARMED,e.TransactionId,e.Operation,e.BaseGenerationId,e.BaseGenerationSha256,e.Prior,e.Target,e.Binding,e.PriorEstablishedOnBinding,null,null);
        var h=new VerifiedRegistryHead(r.NewGenerationId,r.NewGenerationSha256,e.Prior,l);
        var arm=new TransactionEvent.ArmCommitted(Cor(s),r,h);
        foreach(var bad in new[]{arm with {CommitReceipt=r with {ExpectedGenerationSha256=Hash('0')}},arm with {VerifiedHead=h with {Interlock=l with {Target=e.Prior}}},
            arm with {VerifiedHead=h with {Activation=e.Target}},arm with {Correlation=Cor(s) with {Binding=new("stale")}}})Noop(p,new RotationTransactionEvent.Transaction(Hero,bad));
        Noop(p,new RotationTransactionEvent.Transaction(new("other"),arm));
        var applied=Tx(Tx(p,arm).State,new TransactionEvent.ApplyVerified(Cor(s))).State;var complete=Completion(applied);var c=(TransactionEvent.CompletionCommitted)complete.Event;
        foreach(var bad in new[]{c with {CommitReceipt=c.CommitReceipt with {ExpectedGenerationId=Id(88)}},c with {VerifiedHead=c.VerifiedHead with {Interlock=RotationInterlock.Clear() with {TransactionId=Id(9)}}},
            c with {VerifiedHead=c.VerifiedHead with {Activation=c.VerifiedHead.Activation with {Active=Pack() with {ImportReceiptSha256=Hash('8')}}}}})Noop(applied,complete with {Event=bad});
        Assert.Null(Complete(applied).Operation);
    }
    [Fact] public void TokenParityControlsPreserveAllOldBoundsAndNfkcRules()
    {
        var transaction=new SkinTransactionCore();
        foreach(var text in new[]{"skin/hero:1","é",string.Concat(Enumerable.Repeat("😀",256)),new string('x',256),"x￾y"})
        {
            var s=State();s=s with {Mode=s.Mode with {Rotation=s.Mode.Rotation with {CurrentSkin=new(text)}}};
            var d=Advance(s);Assert.Single(d.Commands);Assert.True(transaction.IsStateValid(d.State.Operation.Transaction));
        }
        foreach(var text in new[]{"", " x", "x ", " x", "x ", "a‮b", "a\0b", "\ud800", "\udc00", "Ａ", "é",new string('x',257),string.Concat(Enumerable.Repeat("😀",257)),"x￾Ａ"})
        {
            var s=State();s=s with {Mode=s.Mode with {Rotation=s.Mode.Rotation with {CurrentSkin=new(text)}}};
            Assert.False(core.IsStateValid(s));Assert.False(transaction.IsStateValid(new TransactionState(Binding:new(text),Activation:s.Mode.Rotation.Activation)));Assert.Empty(Advance(s).Commands);
        }
    }
    [Fact] public void ExhaustionDoesNotIssueOrOverflowAndCommandsAreImmutable()
    {
        var s=State();var max=s with {Mode=s.Mode with {OperationHighWater=long.MaxValue}};
        Assert.Equal("operation-id-exhausted",Advance(max).Diagnosis);Assert.Equal(max,Advance(max).State);
        var a=s.Mode.Rotation.Activation with {SkinStamp=long.MaxValue};var stamp=s with {Mode=s.Mode with {Rotation=s.Mode.Rotation with {Activation=a},Head=s.Mode.Head with {Activation=a}}};
        Assert.Equal("skin-stamp-exhausted",Advance(stamp).Diagnosis);Assert.Equal(stamp,Advance(stamp).State);
        Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<RotationTransactionCommand>)Advance(s).Commands).Clear());
    }
    [Fact] public void CompositionEntryPointExists()
    {
        Assert.NotNull(typeof(SkinRotationCore).Assembly.GetType("DualSouls.Skins.HollowKnight.Core.SkinRotationTransactionCore"));
    }
    [Fact] public void AllSelectorAcceptedNoncharactersCanEnterTransactionCore()
    {
        foreach (var n in Enumerable.Range(0xFDD0,32).Concat(Enumerable.Range(0,17).SelectMany(p=>new[]{(p<<16)+0xFFFE,(p<<16)+0xFFFF})))
        {
            var skin=new SkinBindingToken("skin"+char.ConvertFromUtf32(n));
            var a=new ActivationSnapshot(SkinMode.OFF,"a",new ActiveVisual.Vanilla(),0);
            var ring=RotationRing.TryCreate(new[]{new RotationDescriptor("A",new ActiveVisual.Pack("a",new string('a',64),new string('b',64),new string('c',64)),true)});
            var h=new VerifiedRegistryHead("00000000-0000-0000-0000-000000000001",new string('d',64),a,RotationInterlock.Clear());
            var mode=new RotationModeState(new RotationState(ring,a,new HeroBindingToken("hero"),skin),h);
            Assert.True(new SkinRotationCore().IsModeStateValid(mode));
            var e=new TransactionEnvelope("00000000-0000-0000-0000-000000000002",SkinOperationKind.MODE_ON,h.GenerationId,h.GenerationSha256,a,
                a with {Mode=SkinMode.ON,Active=ring.Entries[0].CurrentObject,SkinStamp=1},skin,false);
            var d=new SkinTransactionCore().Decide(new TransactionState(Binding:skin,Activation:a),new TransactionEvent.Begin(e));
            Assert.IsType<SkinCommand.Prepare>(Assert.Single(d.Commands));
            var composed=Advance(new RotationTransactionState(mode));
            Assert.Single(composed.Commands);Assert.True(core.IsStateValid(composed.State));
        }
    }
}
