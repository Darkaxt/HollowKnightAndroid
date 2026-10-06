using HkPauseContracts;
using Xunit;

[Collection("HollowKnightPauseOwners")]
public class HollowKnightAnimatorPerformanceTests
{
    static (HKDualScreen,Animator,Renderer) Shared()
    {
        var h=new HKDualScreen();
        foreach(var r in h.Cameras.hudCanvas.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer=HKDualScreen.HUD_LAYER;
        var animator=h.Health.AddComponent<Animator>(); animator.layerCount=2;
        var child=new GameObject("shared animated effect");child.transform.SetParent(h.Health.transform);child.layer=HKDualScreen.HUD_LAYER;
        var renderer=child.AddComponent<SpriteRenderer>();
        h.NativeHudCameraStep();return(h,animator,renderer);
    }
    [Fact]
    public void Shared_animator_observes_once_per_layer_per_pass_and_dirties_both_renderers()
    {
        var (h,a,child)=Shared();
        int reads=a.CurrentReads,next=a.NextReads,moving=a.MovingReads;
        a.State=new(){fullPathHash=1,normalizedTime=.25f};
        h.Health.GetComponent<Renderer>().BoundsSize=new(70,18,1);child.BoundsSize=new(60,16,1);
        DiscoveryCounters.BoundsReads=0;h.NativeHudCameraStep();
        Assert.Equal(2,a.CurrentReads-reads);Assert.Equal(2,a.NextReads-next);Assert.Equal(2,a.MovingReads-moving);
        Assert.Equal(2,DiscoveryCounters.BoundsReads);Assert.Equal(1<<HKDualScreen.HUD_LAYER,h.hudCam2.cullingMask);
        // A second pass in the same engine frame is a distinct observation.
        reads=a.CurrentReads;a.State.normalizedTime=.5f;DiscoveryCounters.BoundsReads=0;h.NativeHudCameraStep();
        Assert.Equal(2,a.CurrentReads-reads);Assert.Equal(2,DiscoveryCounters.BoundsReads);
    }
    [Fact]
    public void Transition_time_remains_conservative_and_unchanged_pass_remains_settled()
    {
        var (h,a,_)=Shared();a.Transitioning=true;a.Transition.normalizedTime=.2f;
        int transition=a.TransitionReads;DiscoveryCounters.BoundsReads=0;h.NativeHudCameraStep();
        Assert.Equal(2,a.TransitionReads-transition);Assert.Equal(2,DiscoveryCounters.BoundsReads);
        transition=a.TransitionReads;DiscoveryCounters.BoundsReads=0;h.NativeHudCameraStep();
        Assert.Equal(2,a.TransitionReads-transition);Assert.Equal(0,DiscoveryCounters.BoundsReads);
        a.Transition.normalizedTime=.3f;DiscoveryCounters.BoundsReads=0;h.NativeHudCameraStep();Assert.Equal(2,DiscoveryCounters.BoundsReads);
    }
    [Fact]
    public void Distinct_animators_do_not_share_observation_history()
    {
        var (h,a,child)=Shared();var separate=child.gameObject.AddComponent<Animator>();separate.layerCount=1;
        // A native structural notification admits the newly distinct owner.
        child.transform.SetParent(h.Soul.transform);h.NativeHudCameraStep();
        int first=a.CurrentReads,second=separate.CurrentReads;
        a.State.normalizedTime=.2f;DiscoveryCounters.BoundsReads=0;h.NativeHudCameraStep();
        Assert.Equal(2,a.CurrentReads-first);Assert.Equal(1,separate.CurrentReads-second);Assert.Equal(1,DiscoveryCounters.BoundsReads);
    }
    [Fact]
    public void Retired_animator_replacement_rebinds_without_test_repairing_owner_records()
    {
        var (h,a,_)=Shared();a.Retired=true;
        var replacement=h.Health.AddComponent<Animator>();replacement.State.normalizedTime=.5f;
        h.NativeHudCameraStep();Assert.True(replacement.CurrentReads>0);Assert.Equal(1<<HKDualScreen.HUD_LAYER,h.hudCam2.cullingMask);
    }
    [Fact]
    public void Unsupported_layer_growth_is_still_fail_closed_and_can_recover()
    {
        var (h,a,_)=Shared();a.layerCount=9;h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);
        a.layerCount=2;h.NativeHudCameraStep();Assert.Equal(1<<HKDualScreen.HUD_LAYER,h.hudCam2.cullingMask);
    }
}
