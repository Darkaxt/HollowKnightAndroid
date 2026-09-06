using System;
using System.Linq;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinRotationModeCoreTests
{
    private readonly SkinRotationCore core = new();
    private readonly HeroBindingToken hero = new("hero");
    private readonly SkinBindingToken skin = new("skin");
    private static string Hash(char c) => new(c,64);
    private static string Id(int n) => $"00000000-0000-0000-0000-{n:x12}";
    private static ActiveVisual.Pack Pack(string id="a") => new(id,Hash('a'),Hash('b'),Hash('c'));
    private RotationModeState State(SkinMode mode=SkinMode.ON, string selected="a", ActiveVisual active=null)
    {
        var a = new ActivationSnapshot(mode,selected,active ?? Pack(),7);
        var r = new RotationState(RotationRing.TryCreate(new[]{new RotationDescriptor("a",Pack(),false),new RotationDescriptor("b",Pack("b"),true),new RotationDescriptor("c",Pack("c"),true)}),a,hero,skin);
        return new(r,new VerifiedRegistryHead(Id(1),Hash('d'),a,RotationInterlock.Clear()));
    }
    private RotationModeDecision Advance(RotationModeState s) => core.Decide(s,new RotationModeEvent.AdvanceMode());
    private RotationModeState Proof(RotationModeState s, ActiveVisual visual=null) => core.Decide(s,new RotationModeEvent.VerifiedVisual(new(hero,
        visual is ActiveVisual.Pack p ? new VerifiedLiveVisualProof.Pack(skin,p) : new VerifiedLiveVisualProof.Vanilla(skin)))).State;
    private RotationModeEvent.ModeCommitted Completion(RotationModeState s,int id=2)
    {
        var p=s.Operation;
        return new(p.Correlation,new(p.BaseHead.GenerationId,p.BaseHead.GenerationSha256,Id(id),Hash('e')),
            new(Id(id),Hash('e'),p.Target,RotationInterlock.Clear()));
    }
    private void Unchanged(RotationModeState s,RotationModeEvent e)
    { var d=core.Decide(s,e); Assert.Equal(s,d.State);Assert.Empty(d.Commands); }
    private RotationModeEvent Death(ulong n=1) => new RotationModeEvent.Selector(new RotationEvent.ConfirmDeath(new(n),hero,skin));
    private RotationModeEvent Stable(ulong n=1) => new RotationModeEvent.Selector(new RotationEvent.StableRespawn(new(new(n),hero,skin)));

    [Fact] public void OnToRotatePublishesOnlyExactChildAndPreservesAllHistory()
    {
        var s=State();var d=Advance(s);Assert.IsType<RotationModeCommand.CommitOnToRotate>(Assert.Single(d.Commands));
        Assert.Equal(s.Rotation.Activation,d.State.Rotation.Activation);Assert.Equal(s.Head,d.State.Head);
        Assert.Equal(s.Rotation.Activation with {Mode=SkinMode.ROTATE},d.State.Operation.Target);
        Assert.Null(d.State.Rotation.Pending);Assert.Equal(1,d.State.OperationHighWater);
        var complete=Completion(d.State);var done=core.Decide(d.State,complete).State;
        Assert.Equal(complete.Head,done.Head);Assert.Equal(complete.Head.Activation,done.Rotation.Activation);
        Assert.Null(done.Operation);Assert.Equal(1,done.OperationHighWater);Unchanged(done,complete);
    }
    [Fact] public void OffMissingSelectionAndSelectedHistoryNeverFabricateVisualTransactions()
    {
        var absent=State(SkinMode.OFF,null);Assert.Equal("NO_SELECTED_SKIN",Advance(absent).Diagnosis);Unchanged(absent,new RotationModeEvent.AdvanceMode());
        var selected=State(SkinMode.OFF);Assert.Equal("transaction-required",Advance(selected).Diagnosis);Unchanged(selected,new RotationModeEvent.AdvanceMode());
        var missing=State(SkinMode.OFF,"missing");Assert.Equal("selected-object-unavailable",Advance(missing).Diagnosis);Assert.Equal(missing,Advance(missing).State);
        Unchanged(State(),Death());Unchanged(State(),Stable());
    }
    [Fact] public void FreshVanillaOffPreservesRecordedPackReceiptSelectionEligibilityAndStamp()
    {
        var s=Proof(State(SkinMode.ROTATE));var issued=Advance(s);
        Assert.IsType<RotationModeCommand.CommitVerifiedVanillaOff>(Assert.Single(issued.Commands));
        Assert.True(issued.State.OffRequested);Assert.Equal(s.Rotation.Activation,issued.State.Rotation.Activation);
        var done=core.Decide(issued.State,Completion(issued.State)).State;
        Assert.Equal(s.Rotation.Activation with {Mode=SkinMode.OFF},done.Rotation.Activation);
        Assert.Same(s.Rotation.Ring,done.Rotation.Ring);Assert.False(done.OffRequested);
        Assert.Equal("transaction-required",Advance(done).Diagnosis);
    }
    [Fact] public void ReceiptHeadAndFullClearMismatchesAreEntireStateNoopsThenCorrectable()
    {
        foreach(var s in new[]{Advance(State()).State,Advance(Proof(State(SkinMode.ROTATE))).State})
        {
        var e=Completion(s);
        var badHeads=new[]{e.Head with {GenerationId=Id(8)},e.Head with {GenerationSha256=Hash('f')},
            e.Head with {Activation=e.Head.Activation with {SkinStamp=8}},e.Head with {Activation=e.Head.Activation with {SelectedPackId="b"}},
            e.Head with {Activation=e.Head.Activation with {Active=Pack() with {ImportReceiptSha256=Hash('f')}}},
            e.Head with {Activation=e.Head.Activation with {Mode=e.Head.Activation.Mode==SkinMode.OFF ? SkinMode.ON : SkinMode.OFF}},
            e.Head with {Interlock=RotationInterlock.Clear() with {TransactionId=Id(5)}}};
        foreach(var h in badHeads)Unchanged(s,e with {Head=h});
        foreach(var r in new[]{e.Receipt with {ExpectedGenerationId=Id(9)},e.Receipt with {ExpectedGenerationSha256=Hash('f')},
            e.Receipt with {NewGenerationId=Id(1)},e.Receipt with {NewGenerationSha256=Hash('d')},e.Receipt with {NewGenerationSha256="bad"}})Unchanged(s,e with {Receipt=r});
        Unchanged(s,e with {Correlation=e.Correlation with {OperationId=Id(9)}});
        Unchanged(s,e with {Correlation=e.Correlation with {Hero=new("old")}});
        Unchanged(s,e with {Correlation=e.Correlation with {Skin=new("old")}});
        Assert.Equal(s.Operation.Target,core.Decide(s,e).State.Rotation.Activation);
        }
    }
    [Fact] public void MissingOrPackProofCancelsBeforeRestorationDiagnosticAndNeverResurrects()
    {
        foreach(var initial in new[]{State(SkinMode.ROTATE),State(SkinMode.ROTATE,active:new ActiveVisual.Vanilla()),Proof(State(SkinMode.ROTATE),Pack())})
        {
            var pending=core.Decide(initial,Death(ulong.MaxValue)).State;
            var d=Advance(pending);Assert.Equal("transaction-required",d.Diagnosis);Assert.Empty(d.Commands);
            Assert.True(d.State.OffRequested);Assert.Null(d.State.Rotation.Pending);Assert.Equal(pending.Rotation.Pending,d.State.CanceledPending);
            Assert.Equal(pending.Rotation.Activation,d.State.Rotation.Activation);Assert.Equal(new DeathEpoch(ulong.MaxValue),d.State.Rotation.EpochHighWater);
            Unchanged(d.State,Stable(ulong.MaxValue));Unchanged(d.State,Death());
            var corrected=Advance(Proof(d.State));Assert.Single(corrected.Commands);
            Assert.Equal(SkinMode.OFF,core.Decide(corrected.State,Completion(corrected.State)).State.Rotation.Activation.Mode);
        }
    }
    [Fact] public void AlreadyIssuedReadinessStaysOwnedWhileOffIntentDisarmsIt()
    {
        var selected=core.Decide(State(SkinMode.ROTATE),Death()).State;
        var ready=core.Decide(selected,Stable()).State;Assert.Equal(RotationPendingPhase.INTENT_ISSUED,ready.Rotation.Pending.Phase);
        var off=Advance(Proof(ready));Assert.Equal("issued-rotation-busy",off.Diagnosis);Assert.Empty(off.Commands);
        Assert.True(off.State.OffRequested);Assert.Equal(ready.Rotation.Pending,off.State.Rotation.Pending);Assert.Null(off.State.CanceledPending);
        Unchanged(off.State,Stable());Unchanged(off.State,Death(2));Unchanged(off.State,new RotationModeEvent.AdvanceMode());
    }
    [Fact] public void ModeFailureAndUncertaintyRetainPublicationAndExactRetryAllowsLateCompletion()
    {
        foreach(var failed in new[]{false,true})
        {
            var issued=Advance(Proof(State(SkinMode.ROTATE))).State;
            var signal=failed ? (RotationModeEvent)new RotationModeEvent.ModeFailed(issued.Operation.Correlation) : new RotationModeEvent.ModeIndeterminate(issued.Operation.Correlation);
            var blocked=core.Decide(issued,signal).State;
            Assert.Equal(issued.Head,blocked.Head);Assert.Equal(issued.Rotation.Activation,blocked.Rotation.Activation);Assert.True(blocked.OffRequested);
            Unchanged(blocked,new RotationModeEvent.AdvanceMode());
            var retry=core.Decide(blocked,new RotationModeEvent.RetryModeCommit(blocked.Operation.Correlation));
            Assert.Equal(blocked,retry.State);Assert.Equal(issued.Operation with {Phase=ModeCommitPhase.ISSUED},Assert.Single(retry.Commands).Operation);
            Assert.Null(core.Decide(blocked,Completion(blocked)).State.Operation);
        }
    }
    [Fact] public void HeroOnlyRebindClearsProofAndParksIssuedCorrelationEvenAfterReturning()
    {
        var s=Proof(State(SkinMode.ROTATE));var issued=Advance(s).State;
        var rebind=new RotationModeEvent.Selector(new RotationEvent.Rebind(new("hero2"),skin));
        var rebound=core.Decide(issued,rebind).State;
        Assert.Null(rebound.LiveProof);Assert.True(rebound.Operation.ReboundBlocked);Assert.Equal(issued.Operation.Correlation,rebound.Operation.Correlation);
        Unchanged(rebound,Completion(issued));Unchanged(rebound,new RotationModeEvent.RetryModeCommit(issued.Operation.Correlation));
        var back=core.Decide(rebound,new RotationModeEvent.Selector(new RotationEvent.Rebind(hero,skin))).State;
        Unchanged(back,Completion(issued));Assert.True(core.IsModeStateValid(back));
        var idle=core.Decide(s,rebind).State;Assert.Null(idle.LiveProof);Assert.Equal("transaction-required",Advance(idle).Diagnosis);
    }
    [Fact] public void RebindBeforeStabilityRetainsCandidateEpochButClearsProof()
    {
        var p=core.Decide(Proof(State(SkinMode.ROTATE)),Death(7)).State;
        var h=new HeroBindingToken("hero2");var k=new SkinBindingToken("skin2");
        var next=core.Decide(p,new RotationModeEvent.Selector(new RotationEvent.Rebind(h,k))).State;
        Assert.Null(next.LiveProof);Assert.Equal(p.Rotation.Pending.Candidate,next.Rotation.Pending.Candidate);
        Assert.Equal(p.Rotation.EpochHighWater,next.Rotation.EpochHighWater);Unchanged(next,Stable(7));
        var ready=core.Decide(next,new RotationModeEvent.Selector(new RotationEvent.StableRespawn(new(new(7),h,k)))).State;
        Assert.Equal(h,ready.Rotation.Pending.IssuedIntent.Hero);Assert.True(core.IsModeStateValid(ready));
    }
    [Fact] public void LiveProofRequiresExactAuthorityButVisualEqualityExcludesReceipt()
    {
        var s=State();Assert.False(core.IsPriorEstablished(s));
        var proof=Proof(s,Pack() with {ImportReceiptSha256=Hash('f')});Assert.True(core.IsPriorEstablished(proof));
        Assert.False(core.IsPriorEstablished(Proof(s,Pack() with {TreeSha256=Hash('f')})));
        Assert.False(core.IsPriorEstablished(Proof(s)));Assert.Equal(s.Head,proof.Head);
        foreach(var value in new[]{new HeroVerifiedVisual(new("old"),new VerifiedLiveVisualProof.Vanilla(skin)),
            new HeroVerifiedVisual(hero,new VerifiedLiveVisualProof.Vanilla(new("old")))}) Unchanged(s,new RotationModeEvent.VerifiedVisual(value));
    }
    [Fact] public void InconsistentStateAndOverflowAreFailClosedAndQueriesValidateEverything()
    {
        var s=State();var issued=Advance(s).State;
        foreach(var bad in new[]{s with {Head=s.Head with {Activation=s.Head.Activation with {SkinStamp=8}}},
            s with {OffRequested=true},s with {OperationHighWater=-1},issued with {OperationHighWater=0},
            issued with {Operation=issued.Operation with {Target=issued.Operation.Target with {SkinStamp=8}}},
            issued with {Operation=issued.Operation with {VanillaProof=new(hero,new VerifiedLiveVisualProof.Vanilla(skin))}}})
        {Assert.False(core.IsModeStateValid(bad));Assert.False(core.IsPriorEstablished(bad));Unchanged(bad,new RotationModeEvent.AdvanceMode());}
        var max=s with {OperationHighWater=long.MaxValue};Assert.Equal("operation-id-exhausted",Advance(max).Diagnosis);Assert.Equal(max,Advance(max).State);
        var stamp=s.Rotation.Activation with {SkinStamp=long.MaxValue};var maxStamp=s with {Rotation=s.Rotation with {Activation=stamp},Head=s.Head with {Activation=stamp}};
        Assert.Equal(long.MaxValue,Advance(maxStamp).State.Operation.Target.SkinStamp);
        var done=core.Decide(issued,Completion(issued)).State;var off=Advance(Proof(done)).State;
        Assert.Equal(2,off.OperationHighWater);Assert.NotEqual(issued.Operation.Correlation.OperationId,off.Operation.Correlation.OperationId);
        Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<RotationModeCommand>)Advance(s).Commands).Clear());
    }
    [Fact] public void MalformedClrValuesAndAllNoncharactersRemainTotal()
    {
        var s=State();var issued=Advance(s).State;var c=Completion(issued);
        foreach(var bad in new[]{null,s with {Rotation=null},s with {Head=null},s with {Head=s.Head with {Interlock=null}},
            s with {LiveProof=new(hero,null)},issued with {Operation=issued.Operation with {Correlation=null}},
            issued with {Operation=issued.Operation with {Phase=(ModeCommitPhase)99}},issued with {Operation=issued.Operation with {Target=null}}})
        {Assert.False(core.IsModeStateValid(bad));Unchanged(bad,new RotationModeEvent.AdvanceMode());}
        foreach(var e in new RotationModeEvent[]{null,new RotationModeEvent.Selector(null),new RotationModeEvent.VerifiedVisual(null),
            c with {Receipt=null},c with {Head=null},c with {Correlation=null},new RotationModeEvent.RetryModeCommit(null)})Unchanged(issued,e);
        foreach(var n in Enumerable.Range(0xFDD0,32).Concat(Enumerable.Range(0,17).SelectMany(p=>new[]{(p<<16)+0xFFFE,(p<<16)+0xFFFF})))
        {
            var h=new HeroBindingToken("h"+char.ConvertFromUtf32(n));var k=new SkinBindingToken("s"+char.ConvertFromUtf32(n));
            var rebound=core.Decide(State(SkinMode.ROTATE),new RotationModeEvent.Selector(new RotationEvent.Rebind(h,k))).State;
            var proven=core.Decide(rebound,new RotationModeEvent.VerifiedVisual(new(h,new VerifiedLiveVisualProof.Vanilla(k)))).State;
            Assert.Single(Advance(proven).Commands);Assert.True(core.IsModeStateValid(proven));
        }
    }

    [Fact] public void ForgedCurrentProofCannotContradictIssuedOffAuthority()
    {
        var issued=Advance(Proof(State(SkinMode.ROTATE))).State;
        var forged=issued with {LiveProof=new(hero,new VerifiedLiveVisualProof.Pack(skin,Pack()))};
        Assert.False(core.IsModeStateValid(forged));Unchanged(forged,Completion(issued));
        var missing=issued with {LiveProof=null};Assert.False(core.IsModeStateValid(missing));Unchanged(missing,Completion(issued));
        var rebound=core.Decide(issued,new RotationModeEvent.Selector(new RotationEvent.Rebind(new("newhero"),skin))).State;
        var forgedRebound=rebound with {LiveProof=new(new("newhero"),new VerifiedLiveVisualProof.Vanilla(skin))};
        Assert.False(core.IsModeStateValid(forgedRebound));
    }

    [Fact] public void AdvanceModeIsTheSolePayloadFreeModeRequest()
    {
        var assembly = typeof(SkinRotationCore).Assembly;
        var events = assembly.GetType("DualSouls.Skins.HollowKnight.Core.RotationModeEvent");
        Assert.NotNull(events);
        var advance = events.GetNestedType("AdvanceMode");
        Assert.NotNull(advance);
        Assert.NotNull(advance.GetConstructor(Type.EmptyTypes));
        Assert.DoesNotContain(events.GetNestedTypes(), t => t.GetProperties().Any(p => p.PropertyType == typeof(SkinMode)));
    }
}
