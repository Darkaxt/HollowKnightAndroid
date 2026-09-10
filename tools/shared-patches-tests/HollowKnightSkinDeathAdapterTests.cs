using System;
using DualSouls.Skins.HollowKnight.Runtime;
using Xunit;

public class HollowKnightSkinDeathAdapterTests
{
    [Fact] public void Death_callback_only_arms_and_same_frame_dead_is_not_confirmation()
    {
        var r = new Rig(); r.Death(); Assert.Equal(0,r.Adapter.Occurrence);
        r.Frame.Dead = true; r.Adapter.Tick(); Assert.Equal(0,r.Adapter.Occurrence);
        r.Step(); Assert.Equal(1,r.Adapter.Occurrence); r.Death(); r.Step(); Assert.Equal(1,r.Adapter.Occurrence);
    }
    [Theory] [InlineData("DREAM_WORLD",0)] [InlineData("GODS_GLORY",0)] [InlineData("CROSSROADS",1)] [InlineData("CROSSROADS",2)]
    public void Dream_and_terminal_deaths_are_excluded(string zone,int perma)
    {
        var r = new Rig(); r.Frame.MapZone=zone; r.Frame.Permadeath=perma; r.Death();r.Frame.Dead=true;r.Step();
        Assert.Equal(0,r.Adapter.Occurrence);
    }
    [Fact] public void Nonfatal_hazard_and_ordinary_transition_do_not_count_but_lethal_normal_Die_does()
    {
        var r = new Rig(); r.Frame.Hazard=true;r.Position();r.Scene();r.Step();r.Step();Assert.Equal(0,r.Adapter.Occurrence);
        r.Frame.Hazard=false;r.Death();r.Frame.Dead=true;r.Step();Assert.Equal(1,r.Adapter.Occurrence);
    }
    [Fact] public void Postdeath_position_then_later_completion_and_two_distinct_stable_frames_are_required()
    {
        var r = new Rig(); r.Confirm();r.Frame.Dead=false;r.Scene();r.Step();r.Step();Assert.False(r.Adapter.Ready);
        r.Position();r.Step();r.Step();Assert.False(r.Adapter.Ready);r.Scene();r.Step();Assert.False(r.Adapter.Ready);
        r.Adapter.Tick();Assert.False(r.Adapter.Ready);r.Step();Assert.True(r.Adapter.Ready);
        r.Frame.Paused=true;Assert.False(r.Adapter.Ready);r.Step();r.Frame.Paused=false;r.Step();Assert.False(r.Adapter.Ready);
        r.Step();Assert.True(r.Adapter.Ready);
    }
    [Theory] [InlineData("gameplay")] [InlineData("playing")] [InlineData("paused")] [InlineData("position")]
    [InlineData("transition")] [InlineData("waiting")] [InlineData("dead")] [InlineData("hazard")]
    [InlineData("input")] [InlineData("control")] [InlineData("targets")]
    public void Readiness_is_live_and_retries_after_every_required_state_recovers(string flag)
    {
        var r = new Rig();r.Respawn();Assert.True(r.Adapter.Ready);
        void Change(bool bad) { switch(flag) {
            case "gameplay":r.Frame.Gameplay=!bad;break;case "playing":r.Frame.Playing=!bad;break;
            case "paused":r.Frame.Paused=bad;break;case "position":r.Frame.InPosition=!bad;break;
            case "transition":r.Frame.Transitioning=bad;break;case "waiting":r.Frame.WaitingToTransition=!bad;break;
            case "dead":r.Frame.Dead=bad;break;case "hazard":r.Frame.Hazard=bad;break;case "input":r.Frame.AcceptingInput=!bad;break;
            case "control":r.Frame.ControlRelinquished=bad;break;case "targets":r.Frame.TargetsAvailable=!bad;break; } }
        Change(true);Assert.False(r.Adapter.Ready);r.Step();Change(false);r.Step();Assert.False(r.Adapter.Ready);r.Step();Assert.True(r.Adapter.Ready);
    }
    [Fact] public void Replaced_hero_and_manager_reject_retired_events_but_new_respawn_can_become_ready()
    {
        var r = new Rig();r.Confirm();var oldHero=r.Frame.Hero;var oldManager=r.Frame.Manager;
        r.Frame.Hero=new object();r.Frame.Manager=new object();r.Frame.Dead=false;r.Step();
        r.Adapter.HeroInPosition(oldHero,oldManager);r.Adapter.SceneCompleted(oldHero,oldManager);r.Step();r.Step();Assert.False(r.Adapter.Ready);
        r.Position();r.Scene();r.Step();r.Step();Assert.True(r.Adapter.Ready);Assert.Equal(1,r.Adapter.Occurrence);
        r.Frame.Hud=new object();Assert.False(r.Adapter.Ready);r.Step();Assert.False(r.Adapter.Ready);r.Step();Assert.True(r.Adapter.Ready);
    }
    [Fact] public void Rebound_position_and_completion_before_next_frame_are_not_lost()
    {
        var r = new Rig();r.Confirm();r.Frame.Dead=false;r.Frame.Hero=new object();r.Frame.Manager=new object();
        r.Position();r.Scene();r.Step();r.Step();Assert.True(r.Adapter.Ready);
    }
    [Fact] public void Missing_manager_during_rebind_has_no_save_identity_and_does_not_cancel_confirmed_death()
    {
        var r=new Rig();r.Confirm();r.Frame.Manager=null;r.Frame.SaveId=0;r.Step();
        Assert.Equal(1,r.Adapter.Occurrence);Assert.False(r.Adapter.Ready);
        r.Frame.Manager=new object();r.Frame.SaveId=1;r.Frame.Dead=false;
        r.Position();r.Scene();r.Step();r.Step();Assert.True(r.Adapter.Ready);
    }
    [Fact] public void Retired_preconfirmation_owner_never_confirms()
    {
        var r = new Rig();r.Death();r.Frame.Hero=new object();r.Frame.Dead=true;r.Step();Assert.Equal(0,r.Adapter.Occurrence);
    }
    [Fact] public void Completion_dedup_survives_pending_clear_and_next_death_gets_next_occurrence()
    {
        var r = new Rig();r.Respawn();r.Adapter.Configure("ROTATE","run",1,1);Assert.True(r.Adapter.Recorded);
        r.Adapter.Configure("ROTATE","run",1,0);Assert.Equal(0,r.Adapter.Occurrence);
        r.Death();r.Frame.Dead=true;r.Step();Assert.Equal(2,r.Adapter.Occurrence);
    }
    [Theory] [InlineData("OFF")] [InlineData("manual")] [InlineData("save")] [InlineData("reject")] [InlineData("dispose")]
    public void Cancellation_invalidates_live_and_delayed_signals(string kind)
    {
        var r = new Rig();r.Respawn();
        switch(kind) { case "OFF":r.Adapter.Configure("OFF",null);break;case "manual":r.Adapter.Configure("ROTATE","newrun");break;
            case "save":r.Frame.SaveId++;r.Step();break;case "reject":r.Adapter.Cancel();break;case "dispose":r.Adapter.Dispose();break; }
        Assert.False(r.Adapter.Ready);Assert.Equal(0,r.Adapter.Occurrence);r.Position();r.Scene();r.Step();r.Step();Assert.False(r.Adapter.Ready);
    }
    sealed class Rig
    {
        public readonly SkinDeathFrame Frame = new SkinDeathFrame { Hero=new object(),Manager=new object(),Hud=new object(),SaveId=1,
            MapZone="CROSSROADS",Gameplay=true,Playing=true,InPosition=true,WaitingToTransition=true,AcceptingInput=true,TargetsAvailable=true };
        public readonly HollowKnightSkinDeathAdapter Adapter;
        public Rig() { Adapter=new HollowKnightSkinDeathAdapter(()=>Frame);Adapter.Configure("ROTATE","run");Adapter.Tick(); }
        public void Step() { Frame.Frame++;Adapter.Tick(); }
        public void Death() => Adapter.OnDeath(Frame.Hero,Frame.Manager);
        public void Position() => Adapter.HeroInPosition(Frame.Hero,Frame.Manager);
        public void Scene() => Adapter.SceneCompleted(Frame.Hero,Frame.Manager);
        public void Confirm() { Death();Frame.Dead=true;Step(); }
        public void Respawn() { Confirm();Frame.Dead=false;Position();Scene();Step();Step(); }
    }

    [Fact] public void ManagedDeathAdapterBoundaryExists()
    {
        Assert.NotNull(typeof(SkinRuntimeSession).Assembly.GetType("DualSouls.Skins.HollowKnight.Runtime.HollowKnightSkinDeathAdapter"));
    }
}
