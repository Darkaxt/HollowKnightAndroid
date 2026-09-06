using System;
using System.Collections.Generic;
using System.Linq;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinRotationCoreTests
{
    private readonly SkinRotationCore core = new();
    private readonly HeroBindingToken hero = new("hero-1");
    private readonly SkinBindingToken skin = new("skin-1");
    private static ActiveVisual.Pack Pack(string id) => new(id, new string('a',64), new string('b',64), new string('c',64));
    private static RotationDescriptor Descriptor(string id, string name = null, bool eligible = true) => new(name ?? id, Pack(id), eligible);
    private static RotationRing Ring(params RotationDescriptor[] entries) => RotationRing.TryCreate(entries);
    private RotationState State(RotationRing ring = null, string selected = "a", ActiveVisual active = null, SkinMode mode = SkinMode.ROTATE) =>
        core.Decide(new RotationState(ring ?? Ring(Descriptor("a"), Descriptor("b"), Descriptor("c")),
            new ActivationSnapshot(mode, selected, active ?? Pack("a"), 7)), new RotationEvent.Rebind(hero, skin)).State;
    private RotationEvent Death(ulong n = 1, HeroBindingToken h = null, SkinBindingToken s = null) => new RotationEvent.ConfirmDeath(new(n), h ?? hero, s ?? skin);
    private RotationEvent Stable(ulong n = 1, HeroBindingToken h = null, SkinBindingToken s = null) => new RotationEvent.StableRespawn(new(new(n), h ?? hero, s ?? skin));
    private void Unchanged(RotationState s, RotationEvent e) { var d = core.Decide(s,e); Assert.Equal(s,d.State); Assert.Empty(d.Intents); }

    [Fact] public void ConfirmationChoosesOnceAndStableEmitsReadinessNotVisualPermission()
    {
        var s = State(); var confirmed = core.Decide(s, Death());
        Assert.Equal(Pack("b"), confirmed.State.Pending?.Candidate?.CurrentObject);
        Assert.Equal(new DeathEpoch(1), confirmed.State.EpochHighWater);
        Assert.Equal(RotationPendingPhase.AWAITING_STABILITY, confirmed.State.Pending.Phase);
        Assert.Empty(confirmed.Intents); Assert.Equal(s.Activation, confirmed.State.Activation);
        var ready = core.Decide(confirmed.State, Stable()); var intent = Assert.Single(ready.Intents);
        Assert.Equal(RotationPendingPhase.INTENT_ISSUED, ready.State.Pending.Phase);
        Assert.Equal(new RotationReadyIntent(new(1),hero,skin,Descriptor("b"),s.Activation), intent);
        Assert.Equal(intent,ready.State.Pending.IssuedIntent); Assert.Equal(s.Activation,ready.State.Activation);
        Unchanged(ready.State,Stable()); Unchanged(ready.State,Death(2));
    }
    [Fact] public void RingUsesUnsignedUtf8NameThenAsciiIdNotInputOrUtf16Order()
    {
        var r = Ring(Descriptor("z","same"), Descriptor("a","same"), Descriptor("supplement","😀"), Descriptor("bmp",""), Descriptor("upper","A"),Descriptor("lower","a"));
        Assert.Equal(new[]{"upper","lower","a","z","bmp","supplement"},r.Entries.Select(x=>x.CurrentObject.Id));
        Assert.Equal(r.Entries,Ring(r.Entries.Reverse().ToArray()).Entries);
    }
    [Fact] public void RingAndDecisionsDefensivelyCopyCallerLists()
    {
        var source = new List<RotationDescriptor>{Descriptor("c"),Descriptor("a"),Descriptor("b")};
        var r = RotationRing.TryCreate(source); source.Clear();
        Assert.Equal(new[]{"a","b","c"},r.Entries.Select(x=>x.CurrentObject.Id));
        Assert.Throws<NotSupportedException>(()=>((IList<RotationDescriptor>)r.Entries).Clear());
        var ready = core.Decide(core.Decide(State(r),Death()).State,Stable());
        Assert.Throws<NotSupportedException>(()=>((IList<RotationReadyIntent>)ready.Intents).Clear());
    }
    [Fact] public void RingRejectsInvalidNamesIdsHashesDuplicatesAndOverBound()
    {
        foreach(var name in new[]{""," a","a ","Ａ","é","a\n","a‮b","\ud800",new string('x',81)}) Assert.Null(Ring(Descriptor("a",name)));
        foreach(var id in new[]{"","A","a/../b","a-",new string('a',65)}) Assert.Null(Ring(Descriptor(id)));
        foreach(var p in new[]{Pack("a") with {TreeSha256=new string('A',64)},Pack("a") with {ContentSha256="bad"},Pack("a") with {ImportReceiptSha256=""}}) Assert.Null(Ring(new RotationDescriptor("a",p,true)));
        Assert.Null(Ring(Descriptor("a"),Descriptor("a","different")));
        Assert.Null(Ring(Enumerable.Range(1,65).Select(i=>Descriptor("p"+i)).ToArray()));
        Assert.Equal(64,Ring(Enumerable.Range(1,64).Select(i=>Descriptor("p"+i)).ToArray()).Entries.Count);
        Assert.NotNull(Ring(Descriptor("a",string.Concat(Enumerable.Repeat("😀",80)))));
    }
    [Fact] public void ActiveAnchorWinsThenSelectedThenBeforeFirstWithWrap()
    {
        Assert.Equal("b",core.Decide(State(selected:"c"),Death()).State.Pending.Candidate.CurrentObject.Id);
        Assert.Equal("c",core.Decide(State(selected:"b",active:Pack("old")),Death()).State.Pending.Candidate.CurrentObject.Id);
        Assert.Equal("a",core.Decide(State(selected:null,active:new ActiveVisual.Vanilla()),Death()).State.Pending.Candidate.CurrentObject.Id);
        Assert.Equal("a",core.Decide(State(selected:"b",active:Pack("c")),Death()).State.Pending.Candidate.CurrentObject.Id);
        Assert.Equal("c",core.Decide(State(Ring(Descriptor("a",eligible:false),Descriptor("b"),Descriptor("c")),"b"),Death()).State.Pending.Candidate.CurrentObject.Id);
    }
    [Fact] public void ZeroOrOneEligibleNeverRotatesEvenIfDifferentFromActive()
    {
        foreach(var r in new[]{Ring(),Ring(Descriptor("b")),Ring(Descriptor("a",eligible:false),Descriptor("b"))})
        { var s=State(r);var d=core.Decide(s,Death());Assert.Null(d.State.Pending);Assert.Empty(d.Intents);Assert.Equal(s.Activation,d.State.Activation); }
        Assert.Equal("b",core.Decide(State(Ring(Descriptor("a"),Descriptor("b"))),Death()).State.Pending.Candidate.CurrentObject.Id);
    }
    [Fact] public void OffHistoryAndOnPinIgnoreDeathAndNeverChangeActivation()
    { foreach(var mode in new[]{SkinMode.OFF,SkinMode.ON}) {var s=State(mode:mode);Unchanged(s,Death());Unchanged(s,Stable());} }
    [Fact] public void PendingRejectsDuplicatesOldEpochAndFutureWithoutConsumingThem()
    {
        var s=core.Decide(State(),Death(7)).State;
        foreach(var n in new ulong[]{0,1,7,8,ulong.MaxValue}) Unchanged(s,Death(n));
        foreach(var n in new ulong[]{0,6,8}) Unchanged(s,Stable(n));
        Assert.Equal(new DeathEpoch(7),s.EpochHighWater);Unchanged(State(),Stable());Unchanged(State(),Death(0));
    }
    [Fact] public void RebindBeforeStableRetainsExactCandidateEpochAndInvalidatesOldTokens()
    {
        var pending=core.Decide(State(),Death(5)).State;var h=new HeroBindingToken("hero-2");var k=new SkinBindingToken("skin-2");
        var rebound=core.Decide(pending,new RotationEvent.Rebind(h,k)).State;
        Assert.Equal(pending.Pending.Candidate,rebound.Pending.Candidate);Assert.Equal(pending.EpochHighWater,rebound.EpochHighWater);
        Unchanged(rebound,Stable(5));Unchanged(rebound,Death(6));Unchanged(rebound,new RotationEvent.Rebind(h,k));
        Unchanged(rebound,Death(6,h,k));
        var ready=core.Decide(rebound,Stable(5,h,k));Assert.Equal(h,Assert.Single(ready.Intents).Hero);Assert.Equal(k,ready.Intents[0].Skin);
    }
    [Fact] public void RebindAfterIssueRetainsOldCorrelationWithoutReopeningDelivery()
    {
        var issued=core.Decide(core.Decide(State(),Death()).State,Stable()).State;var h=new HeroBindingToken("hero-2");var k=new SkinBindingToken("skin-2");
        var rebound=core.Decide(issued,new RotationEvent.Rebind(h,k)).State;
        Assert.Equal(issued.Pending.IssuedIntent,rebound.Pending.IssuedIntent);Assert.Equal(hero,rebound.Pending.IssuedIntent.Hero);Assert.Equal(h,rebound.Pending.Hero);
        Assert.Equal(RotationPendingPhase.INTENT_ISSUED,rebound.Pending.Phase);
        Unchanged(rebound,Stable());Unchanged(rebound,Stable(h:h,s:k));Unchanged(rebound,Death(2,h,k));
    }
    [Fact] public void AuthorityRequiresExplicitRebindAndValidNormalizedTokens()
    {
        var unbound=new RotationState(Ring(Descriptor("a"),Descriptor("b")),State().Activation);Unchanged(unbound,Death());Unchanged(unbound,Stable());
        foreach(var token in new[]{""," a","Ａ","\ud800",new string('x',257)})
        { Unchanged(unbound,new RotationEvent.Rebind(new(token),skin));Unchanged(unbound,new RotationEvent.Rebind(hero,new(token))); }
        Unchanged(State(),Death(h:new("other")));Unchanged(State(),Death(s:new("other")));
    }
    private static IEnumerable<string> Noncharacters() => Enumerable.Range(0xFDD0,32)
        .Concat(Enumerable.Range(0,17).SelectMany(plane => new[]{(plane << 16) + 0xFFFE,(plane << 16) + 0xFFFF}))
        .Select(char.ConvertFromUtf32);

    [Fact] public void NormalizedNoncharactersRemainExactDescriptorNamesAndOrdering()
    {
        foreach(var noncharacter in Noncharacters())
        {
            var name = "x" + noncharacter;
            RotationRing r = null;
            Assert.Null(Record.Exception(() => r = Ring(Descriptor("b",name),Descriptor("a","x"))));
            Assert.NotNull(r); Assert.Equal(new[]{"x",name},r.Entries.Select(x => x.Name));
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes(name),System.Text.Encoding.UTF8.GetBytes(r.Entries[1].Name));
            Assert.NotNull(Ring(Descriptor("a",string.Concat(Enumerable.Repeat(noncharacter,80)))));
            Assert.Null(Ring(Descriptor("a",string.Concat(Enumerable.Repeat(noncharacter,81)))));
        }
    }
    [Fact] public void NormalizedNoncharactersRemainValidHeroAndSkinAuthority()
    {
        foreach(var noncharacter in Noncharacters())
        {
            var h = new HeroBindingToken("hero" + noncharacter); var k = new SkinBindingToken("skin" + noncharacter);
            RotationDecision rebound = null;
            Assert.Null(Record.Exception(() => rebound = core.Decide(State(),new RotationEvent.Rebind(h,k))));
            Assert.Equal("rebound",rebound.Diagnosis); Assert.Equal(h,rebound.State.CurrentHero); Assert.Equal(k,rebound.State.CurrentSkin);
            var pending = core.Decide(rebound.State,Death(h:h,s:k)).State;
            var intent = Assert.Single(core.Decide(pending,Stable(h:h,s:k)).Intents);
            Assert.Equal(h,intent.Hero); Assert.Equal(k,intent.Skin);
            var maximum = new HeroBindingToken(string.Concat(Enumerable.Repeat(noncharacter,256)));
            Assert.Equal("rebound",core.Decide(State(),new RotationEvent.Rebind(maximum,k)).Diagnosis);
            Unchanged(State(),new RotationEvent.Rebind(new(string.Concat(Enumerable.Repeat(noncharacter,257))),k));
        }
    }
    [Fact] public void NoncharacterBoundariesNeverHideUnnormalizedOrMalformedSegments()
    {
        foreach(var noncharacter in Noncharacters())
        {
            // Noncharacters are CCC=0 boundaries: A cannot compose with the later ring mark.
            Assert.NotNull(Ring(Descriptor("a","A" + noncharacter + "̊")));
            foreach(var name in new[]{"Å" + noncharacter,noncharacter + "Ａ",noncharacter + "é","é" + noncharacter + "z",
                "x" + noncharacter + "\ud800","\udc00" + noncharacter})
            {
                Assert.Null(Ring(Descriptor("a",name)));
                Unchanged(State(),new RotationEvent.Rebind(new(name),skin));
                Unchanged(State(),new RotationEvent.Rebind(hero,new(name)));
            }
        }
    }

    [Fact] public void HistoryUsesEligibleIdAnchorButCandidateKeepsExactCurrentObject()
    {
        var current = Descriptor("b") with {CurrentObject = Pack("b") with {ImportReceiptSha256 = new string('d',64)}};
        var retained = Pack("a") with {TreeSha256 = new string('e',64),ContentSha256 = new string('f',64),ImportReceiptSha256 = new string('d',64)};
        var r = Ring(current,Descriptor("a"),Descriptor("c"));
        var initial = State(r,selected:"c",active:retained);
        var pending = core.Decide(initial,Death()).State;
        Assert.Equal(current,pending.Pending.Candidate); Assert.Equal(initial.Activation,pending.Activation);
        Assert.Equal(current,Assert.Single(core.Decide(pending,Stable()).Intents).Candidate);
        var otherReceipt = initial with {Activation = initial.Activation with {Active = retained with {ImportReceiptSha256 = new string('a',64)}}};
        Assert.Equal(current,core.Decide(otherReceipt,Death()).State.Pending.Candidate);
    }
    [Fact] public void SkinOnlyRebindAndForgedIssuedIdentityStayFailClosed()
    {
        var pending = core.Decide(State(),Death()).State;
        var next = new SkinBindingToken("skin-2");
        var rebound = core.Decide(pending,new RotationEvent.Rebind(hero,next)).State;
        Unchanged(rebound,Death(2,hero,next)); Unchanged(rebound,Stable());
        var issued = core.Decide(rebound,Stable(s:next)).State;
        var intent = issued.Pending.IssuedIntent;
        foreach(var bad in new[]{intent with {Epoch=new(2)},intent with {Candidate=Descriptor("c")},
            intent with {Prior=intent.Prior with {SkinStamp=8}},intent with {Hero=new("")}})
        {
            var forged = issued with {Pending=issued.Pending with {IssuedIntent=bad}};
            Assert.Equal("invalid-state",core.Decide(forged,Stable(s:next)).Diagnosis);
            Unchanged(forged,new RotationEvent.Rebind(hero,skin));
        }
        var last = core.Decide(issued,new RotationEvent.Rebind(hero,skin)).State;
        Unchanged(last,Stable());Unchanged(last,Death(2));Assert.Equal(intent,last.Pending.IssuedIntent);
        var maximumStamp = State() with {Activation=State().Activation with {SkinStamp=long.MaxValue}};
        Assert.Equal(long.MaxValue,Assert.Single(core.Decide(core.Decide(maximumStamp,Death()).State,Stable()).Intents).Prior.SkinStamp);
    }
    [Fact] public void HighWaterNeverWrapsAndForgedSnapshotsFailClosed()
    {
        var s=State() with {EpochHighWater=new(ulong.MaxValue)};Unchanged(s,Death());Unchanged(s,Death(ulong.MaxValue));Unchanged(s,Death(0));
        var max=core.Decide(State(),Death(ulong.MaxValue)).State;Assert.Equal(new DeathEpoch(ulong.MaxValue),max.Pending.Epoch);Assert.Single(core.Decide(max,Stable(ulong.MaxValue)).Intents);
        var pending=core.Decide(State(),Death()).State;
        foreach(var bad in new[]{State() with {CurrentSkin=null},State() with {Activation=State().Activation with {SkinStamp=-1}},
            pending with {EpochHighWater=new(0)},pending with {Pending=pending.Pending with {Candidate=Descriptor("missing")}},
            pending with {Pending=pending.Pending with {Phase=RotationPendingPhase.INTENT_ISSUED}},pending with {Activation=pending.Activation with {Mode=SkinMode.ON}}})
        {Unchanged(bad,Stable());Unchanged(bad,new RotationEvent.Rebind(hero,skin));}
    }
    [Fact] public void MalformedClrNullAndEnumValuesFailClosedBeforeDereference()
    {
        Assert.Null(RotationRing.TryCreate(null));Assert.Null(Ring((RotationDescriptor)null));Assert.Null(Ring(new RotationDescriptor(null,Pack("a"),true)));
        Assert.Null(Ring(new RotationDescriptor("a",null,true)));Assert.Null(Ring(new RotationDescriptor("a",Pack("a") with {Id=null},true)));
        var s=State();var p=core.Decide(s,Death()).State;
        foreach(var bad in new[]{null,s with {Ring=null},s with {Activation=null},s with {EpochHighWater=null},s with {Activation=s.Activation with {Mode=(SkinMode)99}},
            s with {Activation=s.Activation with {Active=null}},s with {CurrentHero=new(null)},p with {Pending=p.Pending with {Epoch=null}},
            p with {Pending=p.Pending with {Candidate=null}},p with {Pending=p.Pending with {Phase=(RotationPendingPhase)99}}}) Unchanged(bad,Stable());
        foreach(var e in new RotationEvent[]{null,new RotationEvent.Rebind(null,skin),new RotationEvent.ConfirmDeath(null,hero,skin),new RotationEvent.StableRespawn(null),new RotationEvent.StableRespawn(new(null,hero,skin))}) Unchanged(s,e);
    }
}
