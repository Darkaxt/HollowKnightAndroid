using HkPauseContracts;
using Xunit;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightNativeHudConfinementTests
{
    static HKDualScreen Owner()
    {
        var h=new HKDualScreen();
        foreach(var r in h.Cameras.hudCanvas.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer=HKDualScreen.HUD_LAYER;
        return h;
    }
    static void Confined(HKDualScreen h,Renderer r)
    {
        var b=r.bounds;
        for(int i=0;i<8;i++)
        {
            var p=h.hudCam2.WorldToViewportPoint(new(i%2==0 ? b.min.x : b.max.x,(i&2)==0 ? b.min.y : b.max.y,(i&4)==0 ? b.min.z : b.max.z));
            Assert.InRange(p.x,-.0001f,1.0001f); Assert.InRange(p.y,-.0001f,1.0001f);
        }
        var g=h.LowerGeometry();var rect=h.hudCam2.rect;
        Assert.InRange(rect.x*h.BOTTOM_W,6,h.BOTTOM_W*.74f);
        Assert.InRange((1-rect.y-rect.height)*h.BOTTOM_H,6,g.HudHeight);
        Assert.True(rect.width*h.BOTTOM_W<=g.Width*.74f);
        Assert.True((1-rect.y)*h.BOTTOM_H<=g.HudHeight+.001f);
        Assert.Equal(1<<HKDualScreen.HUD_LAYER,h.hudCam2.cullingMask);
    }
    [Fact]
    public void Current_resident_geometry_fits_the_canonical_header_not_the_full_panel()
    {
        var h=Owner(); h.NativeHudCameraStep();
        foreach(var r in h.Cameras.hudCanvas.GetComponentsInChildren<Renderer>(true)) Confined(h,r);
    }
    [Fact]
    public void Obsolete_config_pan_zoom_cannot_move_native_hud_outside_the_header()
    {
        var h=Owner(); h.cfg.zoomMul=.001f;h.cfg.panX=900;h.cfg.panY=-900;
        h.NativeHudCameraStep();Confined(h,h.Geo.GetComponent<Renderer>());
    }
    [Fact]
    public void Current_sprite_and_rotated_nonuniform_transform_changes_refresh_only_that_owner()
    {
        var h=Owner();h.NativeHudCameraStep();
        var r=h.Geo.GetComponent<SpriteRenderer>();
        r.sprite=new Sprite { bounds=new(new(3,0,0),new(24,8,0)) };
        r.transform.localScale=new(2,.5f,1);r.transform.localRotation=Quaternion.Euler(0,0,37);
        DiscoveryCounters.BoundsReads=DiscoveryCounters.Hierarchy=0;h.NativeHudCameraStep();
        Assert.Equal(1,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Hierarchy);Confined(h,r);
    }
    [Fact]
    public void Native_particle_advancement_refreshes_current_effect_bounds_without_managed_trigger()
    {
        var h=Owner();var go=new GameObject("Current soul particles");go.transform.SetParent(h.Soul.transform);go.layer=HKDualScreen.HUD_LAYER;
        var ps=go.AddComponent<ParticleSystem>();ps.isPlaying=true;ps.particleCount=12;
        var r=go.AddComponent<ParticleSystemRenderer>();h.NativeHudCameraStep();
        ps.time=.1f;r.BoundsSize=new(80,20,1);
        DiscoveryCounters.BoundsReads=DiscoveryCounters.Hierarchy=0;h.NativeHudCameraStep();
        Assert.Equal(1,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Hierarchy);Confined(h,r);
    }
    [Fact]
    public void Unchanged_native_phase_has_zero_bounds_discovery_and_managed_allocation_work()
    {
        var h=Owner();var go=new GameObject("Stopped or unchanged effect");go.transform.SetParent(h.Health.transform);go.layer=HKDualScreen.HUD_LAYER;
        var ps=go.AddComponent<ParticleSystem>();ps.isPlaying=true;ps.particleCount=2;go.AddComponent<ParticleSystemRenderer>();
        for(int i=0;i<40;i++) h.NativeHudCameraStep();
        DiscoveryCounters.BoundsReads=DiscoveryCounters.Hierarchy=0;
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<200;i++) h.NativeHudCameraStep();
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Hierarchy);Assert.Equal(0,allocated);
    }
    [Fact]
    public void Native_animator_phase_refreshes_its_current_renderer_and_not_unrelated_owners()
    {
        var h=Owner();var animator=h.Health.AddComponent<Animator>();h.NativeHudCameraStep();
        h.Health.GetComponent<Renderer>().BoundsSize=new(70,18,1);animator.State=new(){fullPathHash=1,normalizedTime=.25f};
        DiscoveryCounters.BoundsReads=DiscoveryCounters.Hierarchy=0;h.NativeHudCameraStep();
        Assert.Equal(1,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Hierarchy);Confined(h,h.Health.GetComponent<Renderer>());
    }
    [Fact]
    public void New_resident_child_is_discovered_on_structural_change_then_returns_to_idle()
    {
        var h=Owner();h.NativeHudCameraStep();
        var go=new GameObject("Added health effect");go.transform.SetParent(h.Health.transform);go.layer=HKDualScreen.HUD_LAYER;
        go.transform.localPosition=new(40,10,0);var r=go.AddComponent<SpriteRenderer>();
        h.NativeHudCameraStep();Confined(h,r);
        DiscoveryCounters.BoundsReads=DiscoveryCounters.Hierarchy=0;h.NativeHudCameraStep();
        Assert.Equal(0,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Hierarchy);
    }
    [Fact]
    public void Low_health_vignette_stays_single_owned_on_upper_layer_after_relayer_refresh()
    {
        var h=Owner();var go=new GameObject("Low Health Vignette");go.transform.SetParent(h.Health.transform);
        var r=go.AddComponent<SpriteRenderer>();r.sprite=new Sprite {bounds=new(Vector3.zero,new(1000,1000,0))};
        Time.frameCount=9;h.Step(false);
        Assert.Equal(HKDualScreen.UI_LAYER,go.layer);Assert.False(h.Drawn(go));
        h.NativeHudCameraStep();Confined(h,h.Health.GetComponent<Renderer>());
    }
    [Fact]
    public void Native_parent_reduction_is_not_automatically_zoomed_back_up()
    {
        var h=Owner();h.NativeHudCameraStep();float normal=h.hudCam2.orthographicSize;
        var parent=h.Cameras.hudCanvas.transform.parent;
        parent.localScale=new(.85f,.85f,1);parent.localPosition=new(.25f,-.15f,0);HudGlobalHide.IsReduced=true;
        h.NativeHudCameraStep();Assert.True(h.hudCam2.orthographicSize>=normal);
        Assert.Equal(.85f,parent.localScale.x);Assert.Equal(.25f,parent.localPosition.x);Confined(h,h.Geo.GetComponent<Renderer>());
    }
    [Fact]
    public void Unchanged_generated_header_has_zero_hierarchy_queries_projection_reconstruction_and_allocations()
    {
        var h=Owner();h.BuildHeaderStep();h.HeaderTickStep(1);
        for(int i=0;i<40;i++) h.NativeHudCameraStep();
        DiscoveryCounters.ChildReads=DiscoveryCounters.Projections=DiscoveryCounters.Hierarchy=DiscoveryCounters.BoundsReads=0;
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<200;i++) h.NativeHudCameraStep();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,DiscoveryCounters.ChildReads);Assert.Equal(0,DiscoveryCounters.Projections);Assert.Equal(0,DiscoveryCounters.Hierarchy);Assert.Equal(0,DiscoveryCounters.BoundsReads);Assert.Equal(0,allocated);
    }
    [Fact]
    public void Bound_native_text_transform_refresh_uses_cached_generated_quads_without_discovery_or_generation()
    {
        var h=Owner();var t=h.TextDonor(Vector3.one,false);t.SetParent(h.Geo.transform);t.gameObject.layer=HKDualScreen.HUD_LAYER;
        var tmp=t.GetComponent<Component>();tmp.text="123";tmp.color=Color.white;h.NativeHudCameraStep();
        int generation=tmp.MeshGenerations;t.localPosition=new(12,2,0);
        DiscoveryCounters.Hierarchy=DiscoveryCounters.BoundsReads=0;h.NativeHudCameraStep();
        Assert.Equal(0,DiscoveryCounters.Hierarchy);Assert.Equal(generation,tmp.MeshGenerations);Assert.Equal(0,DiscoveryCounters.BoundsReads);
    }
    [Fact]
    public void Native_mesh_shape_change_updates_only_its_cached_renderer()
    {
        var h=Owner();var go=new GameObject("Native liquid mesh");go.transform.SetParent(h.Soul.transform);go.layer=HKDualScreen.HUD_LAYER;
        var mesh=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();h.NativeHudCameraStep();
        mesh.bounds=new(new(4,0,0),new(50,16,1));
        DiscoveryCounters.BoundsReads=DiscoveryCounters.Hierarchy=0;h.NativeHudCameraStep();
        Assert.Equal(1,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Hierarchy);Confined(h,r);
    }
    [Fact]
    public void Retired_renderer_replacement_cannot_reuse_the_original_bounds()
    {
        var h=Owner();h.NativeHudCameraStep();var original=h.Geo.GetComponent<Renderer>();h.Geo.RemoveRenderer();
        var replacement=h.Geo.AddComponent<SpriteRenderer>();replacement.sprite=new(){bounds=new(Vector3.zero,new(90,20,0))};
        h.NativeHudCameraStep();Assert.True(original.Destroyed);Confined(h,replacement);
    }
    [Fact]
    public void Native_text_missing_buffers_fails_closed_and_same_content_recovers_on_bounded_retry()
    {
        var h=Owner();var t=h.TextDonor(Vector3.one,false);t.SetParent(h.Geo.transform);t.gameObject.layer=HKDualScreen.HUD_LAYER;
        var tmp=t.GetComponent<Component>();tmp.text="123456";tmp.color=Color.white;tmp.ThrowGeneration=true;
        h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);
        tmp.ThrowGeneration=false;Time.frameCount+=119;h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);
        Time.frameCount++;h.NativeHudCameraStep();Assert.Equal(1<<HKDualScreen.HUD_LAYER,h.hudCam2.cullingMask);
        int generations=tmp.MeshGenerations;h.NativeHudCameraStep();Assert.Equal(generations,tmp.MeshGenerations);
    }
    [Fact]
    public void Persistent_native_text_dirty_flag_does_not_restart_failed_generation_every_frame()
    {
        var h=Owner();var t=h.TextDonor(Vector3.one,false);t.SetParent(h.Geo.transform);t.gameObject.layer=HKDualScreen.HUD_LAYER;
        t.GetComponents<Renderer>()[1].enabled=false; // One admitted glyph owner; the donor also supplies an optional fallback renderer.
        var tmp=t.GetComponent<Component>();tmp.text="123";tmp.color=Color.white;tmp.havePropertiesChanged=true;tmp.ThrowGeneration=true;
        h.NativeHudCameraStep();Assert.Equal(1,tmp.GenerationAttempts);
        Time.frameCount++;h.NativeHudCameraStep();Assert.Equal(1,tmp.GenerationAttempts);Assert.Equal(0,h.hudCam2.cullingMask);
        tmp.ThrowGeneration=false;Time.frameCount+=119;h.NativeHudCameraStep();Assert.Equal(2,tmp.GenerationAttempts);
        Assert.Equal(1<<HKDualScreen.HUD_LAYER,h.hudCam2.cullingMask);
        h.NativeHudCameraStep();Assert.Equal(2,tmp.GenerationAttempts);
        tmp.havePropertiesChanged=true;h.NativeHudCameraStep();Assert.Equal(3,tmp.GenerationAttempts);
    }
    static void Disjoint(HKDualScreen h,Bounds world)
    {
        var lo=h.attrCam.WorldToViewportPoint(world.min);var hi=h.attrCam.WorldToViewportPoint(world.max);
        float left=lo.x*h.BOTTOM_W,right=hi.x*h.BOTTOM_W,top=(1-hi.y)*h.BOTTOM_H,bottom=(1-lo.y)*h.BOTTOM_H;
        var d=h.hudCam2.rect;float dl=d.x*h.BOTTOM_W,dr=(d.x+d.width)*h.BOTTOM_W,dt=(1-d.y-d.height)*h.BOTTOM_H,db=(1-d.y)*h.BOTTOM_H;
        Assert.True(dr<=left-5.99f || dl>=right+5.99f || db<=top-5.99f || dt>=bottom+5.99f,$"HUD {dl},{dt},{dr},{db} overlaps current reservation {left},{top},{right},{bottom}");
    }
    [Fact]
    public void Generated_title_toast_and_actual_equipped_icons_reserve_current_header_geometry()
    {
        var h=Owner();h.BuildHeaderStep();h.HeaderTickStep(1);h.EquipBuildStep();
        for(int i=0;i<3;i++) {var r=h.equipCharmSRs[i];r.enabled=true;r.transform.position=h.attrCam.ViewportToWorldPoint(new(.45f+i*.12f,.94f,2));r.transform.localScale=new(2,2,1);}
        h.NativeHudCameraStep();
        Assert.True(h.hudCam2.cullingMask!=0,string.Join(";",typeof(HKDualScreen).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Where(f=>f.Name.StartsWith("nativeHud")).Select(f=>f.Name+"="+f.GetValue(h))));
        Disjoint(h,h.HeaderGlyphBounds);
        for(int i=0;i<3;i++) Disjoint(h,h.equipCharmSRs[i].bounds);
        h.SetBenchToast("LONG LOCALIZED BENCH TOAST",100);h.HeaderTickStep(0);h.NativeHudCameraStep();Disjoint(h,h.HeaderGlyphBounds);
    }
    [Fact]
    public void Generated_action_plates_and_ink_share_the_current_destination_generation()
    {
        var h=Owner();h.tab.cur=0;h.BuildActionsStep();h.mapAnyAvailable=h.mapAvailable=h.mapContentVisible=true;h.mapGm=new GameMap();
        h.PositionActionsStep();h.NativeHudCameraStep();
        Assert.True(h.mapViewAction.InkReady);Disjoint(h,h.mapViewAction.Hit);Disjoint(h,h.mapMarkerAction.Hit);
        var before=h.hudCam2.rect;h.mapAnyAvailable=h.mapAvailable=false;h.PositionActionsStep();h.NativeHudCameraStep();
        Assert.True(h.hudCam2.rect.width>=before.width);
    }
    [Fact]
    public void Display_resize_reuses_current_native_bounds_without_discovery()
    {
        var h=Owner();h.NativeHudCameraStep();h.BOTTOM_W=900;h.BOTTOM_H=600;
        DiscoveryCounters.BoundsReads=DiscoveryCounters.Hierarchy=0;h.NativeHudCameraStep();
        Assert.Equal(0,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Hierarchy);Confined(h,h.Geo.GetComponent<Renderer>());
    }
    [Fact]
    public void Current_mesh_depth_is_inside_the_existing_hud_camera_clip_planes()
    {
        var h=Owner();var go=new GameObject("Native depth domain");go.transform.SetParent(h.Soul.transform);go.layer=HKDualScreen.HUD_LAYER;
        var mesh=new Mesh {bounds=new(Vector3.zero,new(5,3,3000))};go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();h.NativeHudCameraStep();Confined(h,r);
        var bounds=r.bounds;float z=h.hudCam2.transform.position.z;
        Assert.True(h.hudCam2.nearClipPlane<=bounds.min.z-z);Assert.True(h.hudCam2.farClipPlane>=bounds.max.z-z);
    }
    [Fact]
    public void Routed_hp_up_prompt_is_owned_but_excluded_from_resident_fit()
    {
        var h=Owner();h.Heal.transform.SetParent(h.Health.transform);var r=h.Heal.AddComponent<SpriteRenderer>();r.sprite=new(){bounds=new(Vector3.zero,new(900,900,0))};
        h.RoutedHudPrompt=h.Heal;h.Step(false);h.NativeHudCameraStep();
        Assert.Equal(HKDualScreen.TUT_LAYER,h.Heal.layer);Assert.True(h.Drawn(h.Heal));Assert.True(h.hudCam2.orthographicSize<10);
    }
    [Fact]
    public void Nonfinite_current_geometry_blanks_without_poisoning_other_camera_roles()
    {
        var h=Owner();h.NativeHudCameraStep();h.Geo.transform.localPosition=new(float.NaN,0,0);h.NativeHudCameraStep();
        Assert.Equal(0,h.hudCam2.cullingMask);Assert.True(h.attrCam.enabled);Assert.True(h.promptCam.enabled);
    }
    [Fact]
    public void Actually_empty_replacement_has_no_ready_hud_fit_and_remains_blank()
    {
        var h=Owner();h.NativeHudCameraStep();h.Cameras.hudCanvas=new GameObject("Explicit empty native owner");
        h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);Assert.True(h.attrCam.enabled);Assert.True(h.promptCam.enabled);
    }
    [Fact]
    public void Smaller_structural_rebind_releases_unused_owner_slots()
    {
        var h=Owner();h.NativeHudCameraStep();h.Geo.RemoveRenderer();h.NativeHudCameraStep();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var owners=(Array)typeof(HKDualScreen).GetField("nativeHudOwners",flags)!.GetValue(h)!;
        int count=(int)typeof(HKDualScreen).GetField("nativeHudOwnerCount",flags)!.GetValue(h)!;
        Assert.Equal(2,count);
        for(int i=count;i<owners.Length;i++) Assert.Null(owners.GetValue(i));
    }
    [Fact]
    public void Particle_renderer_without_its_required_simulation_owner_fails_closed()
    {
        var h=Owner();var go=new GameObject("Unready particle owner");go.transform.SetParent(h.Soul.transform);
        go.layer=HKDualScreen.HUD_LAYER;go.AddComponent<ParticleSystemRenderer>();
        h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);
        Assert.True(h.attrCam.enabled);Assert.True(h.promptCam.enabled);
        go.AddComponent<ParticleSystem>().particleCount=1;
        Time.frameCount+=119;h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);
        Time.frameCount++;h.NativeHudCameraStep();Assert.Equal(1<<HKDualScreen.HUD_LAYER,h.hudCam2.cullingMask);
    }
    [Fact]
    public void Unrelated_camera_and_retired_subscription_do_not_refresh_or_revive_hud()
    {
        var h=Owner();h.NativeHudCameraStep();DiscoveryCounters.Hierarchy=DiscoveryCounters.BoundsReads=DiscoveryCounters.Projections=0;
        Camera.PreCull(h.attrCam);
        Assert.Equal(0,DiscoveryCounters.Hierarchy);Assert.Equal(0,DiscoveryCounters.BoundsReads);Assert.Equal(0,DiscoveryCounters.Projections);
        HudGlobalHide.IsHidden=true;h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);
        HudGlobalHide.IsHidden=false;h.Health.transform.SetParent(new GameObject("Retired hierarchy").transform);
        Camera.PreCull(h.hudCam2);Assert.Equal(0,h.hudCam2.cullingMask);
    }
    [Fact]
    public void Pending_or_budget_overflow_geometry_blanks_only_the_hud_and_never_uses_stale_fit()
    {
        var h=Owner();h.NativeHudCameraStep();
        for(int i=0;i<257;i++) {var go=new GameObject("Over budget "+i);go.transform.SetParent(h.Health.transform);go.layer=HKDualScreen.HUD_LAYER;go.AddComponent<SpriteRenderer>();}
        h.NativeHudCameraStep();Assert.Equal(0,h.hudCam2.cullingMask);Assert.True(h.attrCam.enabled);Assert.True(h.promptCam.enabled);
    }
}
