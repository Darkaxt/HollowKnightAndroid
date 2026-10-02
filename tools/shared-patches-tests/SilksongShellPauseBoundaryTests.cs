using System.Collections;
using System.Reflection;
using SsShellContracts.Engine;
using Xunit;
using static SsShellContracts.FixtureAccess;
namespace SsShellContracts;

public partial class SilksongShellProductionTests
{
    [Theory][MemberData(nameof(RoutePairs))]
    public void All_25_pause_interrupted_routes_settle_selected_target_without_extra_activation(int from,int to)
    {
        using var live=new Live();var shell=live.Shell;shell.Show(from);shell.Tick(.3f);
        shell.Show(to);shell.Tick(.1f);var selected=Page(shell,to);
        if(selected is DsLoadoutScreen load)Set(load,"_choosingCrest",true);
        if(selected is DsMapScreen map)
        {
            Set(map,"_markerMode",true);Field<DsMapView>(map,"_view").Pan(new(7,11));
        }
        int ticks=Ticks(selected);
        var pan=selected is DsMapScreen selectedMap?Field<DsMapView>(selectedMap,"_view").PanState:default;
        live.Manager.Pause(true);live.Update();
        Assert.Equal(selected.Id,shell.ActiveId);Assert.Equal(-1,Field<int>(shell,"_slideFrom"));Assert.Equal(ticks,Ticks(selected));
        for(int i=0;i<5;i++){Assert.Equal(0,Host(shell,i).offsetMin.x);Assert.Equal(0,Host(shell,i).offsetMax.x);}
        Assert.False(Child(live.Root,"actions").gameObject.activeInHierarchy);
        Assert.True(Child(live.Root,"title-card/logo").gameObject.activeInHierarchy);
        live.Manager.Pause(false);live.Update();Assert.Same(selected,Page(shell,to));Assert.True(Ticks(selected)>ticks);
        if(selected is DsLoadoutScreen restored)Assert.True(Field<bool>(restored,"_choosingCrest"));
        if(selected is DsMapScreen restoredMap)
        {
            Assert.True(restoredMap.StripOverride);Assert.Equal(pan,Field<DsMapView>(restoredMap,"_view").PanState);
        }
        Assert.Empty(Debug.Errors);
    }

    [Fact]
    public void Actual_actions_and_marker_override_cannot_dispatch_through_paused_shell()
    {
        using var live=new Live();var shell=live.Shell;var map=(DsMapScreen)Page(shell,4);
        Set(map,"_markerMode",true);live.Update();
        live.Manager.Pause(true);
        var p=new Vector2(1100,70);
        shell.OnGesture(new(){Type=DsGestureType.Down,Position=p});shell.OnGesture(new(){Type=DsGestureType.Tap,Position=p});
        Assert.False(Field<bool>(map,"_erasing"));
        int invocations=0;
        var bar=Field<DsActionBar>(shell,"_actions");
        bar.Set(new(){new DsAction("NATIVE-ACTION-BOUNDARY",()=>invocations++)},default);
        p=new(1100,960);shell.OnGesture(new(){Type=DsGestureType.Tap,Position=p});
        Assert.Equal(0,invocations);
        Assert.False(Child(live.Root,"actions").gameObject.activeInHierarchy);
    }

    [Fact]
    public void Complete_HUD_suspend_restores_inflight_native_and_overlay_scopes_and_rejects_late_render()
    {
        using var live=new Live();var hud=Field<DsHudView>(live.Shell,"_hud");
        Call(hud,"LateUpdate");var capture=Field<Camera>(hud,"_capture");Assert.True(capture.enabled);
        Call(hud,"BeforeCamera",capture);
        Assert.True(Field<DsHudRenderScope<GameObject>>(hud,"_scope").Active);
        var overlay=Field<Transform>(hud,"_overlayRoot");
        Assert.True(overlay.GetComponent<Renderer>().forceRenderingOff);Assert.True(overlay.GetComponent<CanvasRenderer>().cull);
        Assert.Equal(3,hud.NativeTargets[0].layer);
        live.Manager.Pause(true);
        Assert.Equal(5,hud.NativeTargets[0].layer);Assert.False(overlay.GetComponent<Renderer>().forceRenderingOff);Assert.False(overlay.GetComponent<CanvasRenderer>().cull);
        Assert.False(Field<DsHudRenderScope<GameObject>>(hud,"_scope").Active);
        Assert.False(Field<DsHudRenderScope<GameObject>>(hud,"_canvasScope").Active);
        int captures=hud.Captures,binds=hud.Binds;
        Call(hud,"AfterCamera",capture);Call(hud,"BeforeCamera",capture);Call(hud,"LateUpdate");
        Assert.Equal(captures,hud.Captures);Assert.Equal(binds,hud.Binds);Assert.False(capture.enabled);
        Assert.Equal(0,Camera.PreSubscribers);Assert.Equal(0,Camera.PostSubscribers);
        Assert.Empty(Debug.Errors);
    }

    [Fact]
    public void Complete_ShellContent_replace_and_dispose_are_idempotent_without_capture_or_event_orphans()
    {
        using var live=new Live();var previous=live.Shell;var oldHud=Field<DsHudView>(previous,"_hud");
        Assert.Equal(1,Camera.PreSubscribers);Assert.Equal(1,Camera.PostSubscribers);
        live.Manager.Pause(true);Call(live.Owner,"RebuildShell");live.Update();
        Assert.True(Field<bool>(previous,"_disposed"));Assert.True(Field<bool>(oldHud,"_stopped"));
        Assert.Equal(0,Camera.PreSubscribers);Assert.Equal(0,Camera.PostSubscribers);LogoOnly(live);
        var content=Field(live.Owner,"_shellContent");var retained=live.Shell;
        Call(content,"Dispose");Call(content,"Dispose");Call(content,"Replace",retained);
        Assert.Null(Field(content,"_shell"));Assert.True(Field<bool>(retained,"_disposed"));
        Assert.Equal(0,Camera.PreSubscribers);Assert.Equal(0,Camera.PostSubscribers);
        Call(live.Owner,"Shutdown");Assert.Equal(0,live.Manager.PauseSubscriptions);
    }

    sealed class FailedPageBoundary : IDsScreen
    {
        public string Id=>"tasks";
        public string Title=>"TASKS";
        public bool Available=>true;
        public int Builds,Shows,Hides,Ticks,Gestures;
        public void Build(RectTransform host){Builds++;throw new InvalidOperationException("injected native page build boundary");}
        public void OnShow(){Shows++;}
        public void OnHide(){Hides++;}
        public void Tick(float dt){Ticks++;}
        public void OnGesture(DsGesture gesture){Gestures++;}
    }

    [Fact]
    public void Failed_page_build_is_contained_through_pause_resume_and_shell_release()
    {
        using var live=new Live();var shell=live.Shell;var fault=new FailedPageBoundary();
        var entry=Entries(shell)[2];Set(entry,"Screen",fault);
        shell.Show(2);shell.Tick(.3f);
        Assert.Equal(1,fault.Builds);Assert.True(Field<bool>(entry,"Broken"));Assert.Single(Debug.Errors);
        int shows=fault.Shows,hides=fault.Hides,ticks=fault.Ticks;
        live.Manager.Pause(true);live.Update();LogoOnly(live);
        shell.OnGesture(new(){Type=DsGestureType.Tap,Position=new(100,700)});
        live.Manager.Pause(false);live.Update();
        Assert.Equal(shows,fault.Shows);Assert.Equal(hides,fault.Hides);Assert.Equal(ticks,fault.Ticks);Assert.Equal(0,fault.Gestures);
        shell.Show(4);shell.Tick(.3f);Assert.Equal("map",shell.ActiveId);
        Assert.True(Field<bool>(Field<DsMapView>(Page(shell,4),"_view"),"_visible"));
        Call(live.Owner,"Shutdown");Call(live.Owner,"Shutdown");
        Assert.Equal(0,live.Manager.PauseSubscriptions);Assert.Equal(0,Camera.PreSubscribers);Assert.Equal(0,Camera.PostSubscribers);
        Assert.Single(Debug.Errors);
    }

    [Fact]
    public void Unchanged_paused_gate_and_added_healthy_scalar_gate_allocate_exactly_zero_over_300_warm_ticks()
    {
        using var live=new Live();var shell=live.Shell;
        var pause=typeof(DualScreenV2).GetMethod("OnGamePauseChanged",BindingFlags.Instance|BindingFlags.NonPublic);
        var native=typeof(DualScreenV2).GetMethod("NativeGamePaused",BindingFlags.Instance|BindingFlags.NonPublic);
        Assert.NotNull(pause);Assert.NotNull(native);
        var apply=(Action<bool>)pause.CreateDelegate(typeof(Action<bool>),live.Owner);
        var read=(Func<bool>)native.CreateDelegate(typeof(Func<bool>),live.Owner);
        var tick=Delegate<float>(shell,"Tick");
        var gesture=Delegate<DsGesture>(shell,"OnGesture");var g=new DsGesture{Type=DsGestureType.Down,Position=new(10,10)};
        // Existing healthy Tick delegates allocate. Measure only added admission,
        // not that pre-existing Tick; paused Tick/gesture bodies return at gate.
        for(int i=0;i<300;i++)apply(read());
        int io=EngineBoundary.Io,discovery=EngineBoundary.Discovery,serialization=EngineBoundary.Serialization;
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++)apply(read());
        long healthy=GC.GetAllocatedBytesForCurrentThread()-start;Assert.Equal(0L,healthy);
        live.Manager.Pause(true);
        for(int i=0;i<300;i++){apply(read());tick(.016f);gesture(g);}
        start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++){apply(read());tick(.016f);gesture(g);}
        long paused=GC.GetAllocatedBytesForCurrentThread()-start;Assert.Equal(0L,paused);
        Assert.Equal(io,EngineBoundary.Io);Assert.Equal(discovery,EngineBoundary.Discovery);Assert.Equal(serialization,EngineBoundary.Serialization);
        LogoOnly(live);
    }

    [Fact]
    public void Pause_retains_tasks_journal_inventory_data_selection_and_scroll_without_rebuild()
    {
        using var live=new Live();var shell=live.Shell;
        foreach(int index in new[]{0,2,3})
        {
            shell.Show(index);shell.Tick(.3f);var screen=Page(shell,index);
            object resident=index==0?Field<DsIconGrid>(screen,"Grid"):screen;
            Set(resident,"_selectedKey","retained-key");Set(resident,"_scroll",137f);
            if(index==2)Set(screen,"_showCompleted",true);
            var entry=Entries(shell)[index];var host=Host(shell,index);
            for(int cycle=0;cycle<3;cycle++)
            {
                live.Manager.Pause(true);live.Update();
                Assert.Equal("retained-key",Field<string>(resident,"_selectedKey"));Assert.Equal(137f,Field<float>(resident,"_scroll"));
                live.Manager.Pause(false);live.Update();
                Assert.Same(screen,Page(shell,index));Assert.Same(entry,Entries(shell)[index]);Assert.Same(host,Host(shell,index));
                Assert.Equal("retained-key",Field<string>(resident,"_selectedKey"));Assert.Equal(137f,Field<float>(resident,"_scroll"));
                if(index==2)Assert.True(Field<bool>(screen,"_showCompleted"));
            }
        }
    }

    // Observes lifecycle calls but always forwards into the complete real page.
    // In particular it does NOT replace the destructive OnHide under test.
    sealed class LifecycleObserver : IDsScreen, IDsPresentationSuspend
    {
        internal readonly IDsScreen Real;
        internal int Shows,Hides,Suspends;
        internal LifecycleObserver(IDsScreen real){Real=real;}
        public string Id=>Real.Id;
        public string Title=>Real.Title;
        public bool Available=>Real.Available;
        public void Build(RectTransform host)=>Real.Build(host);
        public void OnShow(){Shows++;Real.OnShow();}
        public void OnHide(){Hides++;Real.OnHide();}
        public void Tick(float dt)=>Real.Tick(dt);
        public void OnGesture(DsGesture gesture)=>Real.OnGesture(gesture);
        public void SuspendPresentation(){Suspends++;if(Real is IDsPresentationSuspend suspend)suspend.SuspendPresentation();}
    }

    static LifecycleObserver Observe(DsShell shell,int index)
    {
        var observer=new LifecycleObserver(Page(shell,index));
        Set(Entries(shell)[index],"Screen",observer);return observer;
    }
    static void SeedMap(DsMapScreen map)
    {
        Call(map,"SetMarkerMode",true);
        var strip=new List<DsStripItem>();map.CollectStrip(strip);
        map.OnStripSelect(1);map.OnStripSelect(strip.Count-1);
        var view=Field<DsMapView>(map,"_view");
        view.ResetView();Field<DsZoomSlider>(map,"_slider").Release();Set(map,"_sliderGesture",false);
        // Native framing is modeled; execute real gesture/pan arithmetic with a
        // nonzero units-per-pixel value, rather than a vacuous zero-pan fixture.
        Set(view,"_mapUnitsPerPixel",.5f);
        map.OnGesture(new(){Type=DsGestureType.Drag,Position=new(400,500),Delta=new(17,23)});
        map.OnGesture(new(){Type=DsGestureType.Pinch,Position=new(400,500),Scale=2.25f});
        Assert.NotEqual(Vector2.zero,view.PanState);Assert.Equal(2.25f,view.ZoomLevel);
        var slider=Field<DsZoomSlider>(map,"_slider");
        Assert.True(slider.Grab(slider.Hit.center,view.ZoomLevel));Set(map,"_sliderGesture",true);
    }
    static void SeedLoadout(DsLoadoutScreen loadout)
    {
        Call(loadout,"ShowCrestPicker",true);
        Set(loadout,"_crestPick",2);Set(loadout,"_stripNow",81f);Set(loadout,"_stripT",.4f);
        var grid=Field<DsIconGrid>(loadout,"_grid");Set(grid,"_scroll",123f);Set(grid,"_selectedKey","retained-tool");
        Assert.True(Field<RectTransform>(loadout,"_picker").gameObject.activeSelf);
    }

    public static IEnumerable<object[]> OutgoingPauseCases()
    {
        foreach(int from in new[]{4,1})foreach(float elapsed in new[]{0f,.15f,.299f})
            yield return new object[]{from,elapsed};
    }

    [Theory][MemberData(nameof(OutgoingPauseCases))]
    public void Pause_retains_real_outgoing_and_selected_state_without_lifecycle_or_capture_leaks(int from,float elapsed)
    {
        using var live=new Live();var shell=live.Shell;int to=from==4?1:4;
        shell.Show(from);shell.Tick(.3f);
        var map=(DsMapScreen)Page(shell,4);
        // Build both pages before seeding, then return to the actual slide source.
        shell.Show(to);shell.Tick(.3f);shell.Show(from);shell.Tick(.3f);
        var loadout=(DsLoadoutScreen)Page(shell,1);var view=Field<DsMapView>(map,"_view");
        SeedMap(map);SeedLoadout(loadout);
        var outgoing=Observe(shell,from);var selected=Observe(shell,to);
        shell.Show(to);shell.Tick(elapsed);
        // Ordinary route OnShow may reset Map pan. Seed both AFTER that genuine
        // route activation; the subsequent pause alone must not reset anything.
        SeedMap(map);SeedLoadout(loadout);
        Assert.Equal(from,Field<int>(shell,"_slideFrom"));
        Assert.True(Host(shell,from).gameObject.activeSelf);Assert.True(Host(shell,to).gameObject.activeSelf);
        Assert.NotEqual(0,Host(shell,to).offsetMin.x);
        var pan=view.PanState;var zoom=view.ZoomLevel;var mode=view.Mode;
        int marker=Field<int>(map,"_markerPick"),cancels=Field<DsToolTween>(loadout,"_tween").Cancels;
        int outShows=outgoing.Shows,outHides=outgoing.Hides,inShows=selected.Shows,inHides=selected.Hides;
        int outTicks=Ticks(outgoing.Real),inTicks=Ticks(selected.Real);
        var outHost=Host(shell,from);var inHost=Host(shell,to);var picker=Field<RectTransform>(loadout,"_picker");
        var rooms=Field<Camera>(view,"_rooms");var decor=Field<Camera>(view,"_decor");
        // Adverse native-camera parenting seam: hiding the page/body cannot
        // accidentally satisfy capture ownership by deactivating its hierarchy.
        var detached=new GameObject("independent-native-map-capture-boundary");
        rooms.transform.SetParent(detached.transform);decor.transform.SetParent(detached.transform);
        view.SetVisible(true);Assert.True(rooms.enabled);Assert.True(decor.enabled);
        var hud=Field<DsHudView>(shell,"_hud");Call(hud,"LateUpdate");
        var capture=Field<Camera>(hud,"_capture");Call(hud,"BeforeCamera",capture);
        Assert.True(Field<DsHudRenderScope<GameObject>>(hud,"_scope").Active);
        live.Manager.Pause(true);
        Assert.False(Field<bool>(view,"_visible"));Assert.True(detached.activeInHierarchy);
        Assert.False(rooms.enabled);Assert.False(decor.enabled);
        Assert.False(Field<bool>(map,"_sliderGesture"));Assert.False(Field<DsZoomSlider>(map,"_slider").Held);
        Assert.Equal(5,hud.NativeTargets[0].layer);Assert.False(Field<DsHudRenderScope<GameObject>>(hud,"_scope").Active);
        Assert.False(Field<DsHudRenderScope<GameObject>>(hud,"_canvasScope").Active);
        var overlay=Field<Transform>(hud,"_overlayRoot");
        Assert.False(overlay.GetComponent<Renderer>().forceRenderingOff);Assert.False(overlay.GetComponent<CanvasRenderer>().cull);
        int arrows=view.ArrowHides,hudCaptures=hud.Captures,hudBinds=hud.Binds;
        live.Update();Call(view,"LateTick");Call(hud,"AfterCamera",capture);Call(hud,"BeforeCamera",capture);Call(hud,"LateUpdate");
        Assert.Equal(arrows,view.ArrowHides);Assert.Equal(hudCaptures,hud.Captures);Assert.Equal(hudBinds,hud.Binds);
        Assert.False(capture.enabled);Assert.Equal(0,Camera.PreSubscribers);Assert.Equal(0,Camera.PostSubscribers);
        Assert.Equal(outTicks,Ticks(outgoing.Real));Assert.Equal(inTicks,Ticks(selected.Real));
        Assert.True(map.StripOverride,"Pause destroyed real outgoing Map marker mode through ordinary OnHide");
        Assert.True(Field<bool>(loadout,"_choosingCrest"),"Pause destroyed real outgoing Loadout crest chooser through ordinary OnHide");
        Assert.Equal(cancels,Field<DsToolTween>(loadout,"_tween").Cancels);
        Assert.Equal(marker,Field<int>(map,"_markerPick"));Assert.True(Field<bool>(map,"_erasing"));
        Assert.Equal(pan,view.PanState);Assert.Equal(zoom,view.ZoomLevel);Assert.Equal(mode,view.Mode);
        Assert.Equal(2,Field<int>(loadout,"_crestPick"));Assert.Equal(81f,Field<float>(loadout,"_stripNow"));Assert.Equal(.4f,Field<float>(loadout,"_stripT"));
        Assert.True(picker.gameObject.activeSelf);Assert.Equal("retained-tool",Field<DsIconGrid>(loadout,"_grid").SelectedKey);
        Assert.Equal(123f,Field<float>(Field<DsIconGrid>(loadout,"_grid"),"_scroll"));
        Assert.Equal(-1,Field<int>(shell,"_slideFrom"));Assert.False(outHost.gameObject.activeSelf);Assert.True(inHost.gameObject.activeSelf);
        foreach(var host in new[]{outHost,inHost}){Assert.Equal(Vector2.zero,host.offsetMin);Assert.Equal(Vector2.zero,host.offsetMax);}
        foreach(var path in new[]{"body","hud-header","tabs","actions"})Assert.False(Child(live.Root,path).gameObject.activeInHierarchy);
        Assert.True(Child(live.Root,"title-card/logo").gameObject.activeInHierarchy);
        for(int cycle=0;cycle<3;cycle++)
        {
            live.Manager.Pause(false);live.Update();shell.Tick(.3f);
            Assert.Equal(outShows,outgoing.Shows);Assert.Equal(outHides,outgoing.Hides);
            Assert.Equal(inShows,selected.Shows);Assert.Equal(inHides,selected.Hides);
            Assert.Equal(outTicks,Ticks(outgoing.Real));Assert.True(Ticks(selected.Real)>inTicks);
            Assert.Same(outHost,Host(shell,from));Assert.Same(inHost,Host(shell,to));Assert.Same(picker,Field<RectTransform>(loadout,"_picker"));
            Assert.True(map.StripOverride);Assert.True(Field<bool>(loadout,"_choosingCrest"));Assert.True(picker.gameObject.activeSelf);
            Assert.Equal(cancels,Field<DsToolTween>(loadout,"_tween").Cancels);
            Assert.Equal(marker,Field<int>(map,"_markerPick"));Assert.True(Field<bool>(map,"_erasing"));
            Assert.Equal(pan,view.PanState);Assert.Equal(zoom,view.ZoomLevel);Assert.Equal(mode,view.Mode);
            Assert.Equal(2,Field<int>(loadout,"_crestPick"));Assert.Equal(81f,Field<float>(loadout,"_stripNow"));Assert.Equal(.4f,Field<float>(loadout,"_stripT"));
            Assert.Equal("retained-tool",Field<DsIconGrid>(loadout,"_grid").SelectedKey);Assert.Equal(123f,Field<float>(Field<DsIconGrid>(loadout,"_grid"),"_scroll"));
            Assert.False(outHost.gameObject.activeSelf);Assert.True(inHost.gameObject.activeInHierarchy);
            Assert.Equal(to==4,Field<bool>(view,"_visible"));Assert.Equal(to==4,rooms.enabled);Assert.Equal(to==4,decor.enabled);
            Assert.True(Field<bool>(hud,"_wanted"));Assert.False(Child(live.Root,"title-card").gameObject.activeSelf);
            live.Manager.Pause(true);live.Update();Assert.False(Field<bool>(view,"_visible"));Assert.False(rooms.enabled);Assert.False(decor.enabled);
        }
        // A genuinely new route after pause is still ordinary page lifetime,
        // not a deferred destructive callback from the interrupted old slide.
        live.Manager.Pause(false);live.Update();shell.Show(from);shell.Tick(.3f);
        Assert.Equal(outShows+1,outgoing.Shows);Assert.Equal(outHides,outgoing.Hides);
        Assert.Equal(inShows,selected.Shows);Assert.Equal(inHides+1,selected.Hides);
        if(from==4)
        {
            Assert.Equal(Vector2.zero,view.PanState);
            Assert.False(Field<bool>(loadout,"_choosingCrest"));Assert.Equal(cancels+1,Field<DsToolTween>(loadout,"_tween").Cancels);
        }
        else Assert.False(map.StripOverride);
        Assert.Empty(Debug.Errors);
    }

    public static IEnumerable<object[]> OrdinaryOutgoingCases()
    {
        foreach(var item in OutgoingPauseCases())foreach(string boundary in new[]{"slide","route","transport","idle","scene","dispose"})
            yield return new[]{item[0],item[1],boundary};
    }
    [Theory][MemberData(nameof(OrdinaryOutgoingCases))]
    public void Ordinary_outgoing_slide_route_transport_idle_scene_dispose_still_run_real_lifecycle(int from,float elapsed,string boundary)
    {
        using var live=new Live();var shell=live.Shell;int to=from==4?1:4;
        shell.Show(from);shell.Tick(.3f);var outgoing=Observe(shell,from);var incoming=Observe(shell,to);
        if(outgoing.Real is DsMapScreen map)SeedMap(map);else SeedLoadout((DsLoadoutScreen)outgoing.Real);
        shell.Show(to);shell.Tick(elapsed);int shows=incoming.Shows,hides=incoming.Hides;
        switch(boundary)
        {
            case "slide":shell.Tick(.3f);break;
            case "route":shell.Show(2);break;
            case "transport":shell.SetVisible(false);break;
            case "idle":shell.SetIdle(true);break;
            case "scene":shell.SetTransitioning(true);break;
            case "dispose":shell.Dispose();shell.Dispose();break;
        }
        Assert.Equal(1,outgoing.Hides);Assert.Equal(0,outgoing.Suspends);
        if(outgoing.Real is DsMapScreen hiddenMap)
        {
            Assert.False(hiddenMap.StripOverride);Assert.False(Field<bool>(Field<DsMapView>(hiddenMap,"_view"),"_visible"));
        }
        else
        {
            var hiddenLoadout=(DsLoadoutScreen)outgoing.Real;
            Assert.False(Field<bool>(hiddenLoadout,"_choosingCrest"));Assert.False(Field<RectTransform>(hiddenLoadout,"_picker").gameObject.activeSelf);
            Assert.Equal(1,Field<DsToolTween>(hiddenLoadout,"_tween").Cancels);
        }
        if(boundary=="route")shell.Tick(.3f);
        Assert.Equal(-1,Field<int>(shell,"_slideFrom"));Assert.Equal(Vector2.zero,Host(shell,from).offsetMin);Assert.Equal(Vector2.zero,Host(shell,from).offsetMax);
        Assert.False(Host(shell,from).gameObject.activeSelf);
        Assert.Equal(hides+(boundary=="slide"?0:1),incoming.Hides);
        if(boundary=="transport")shell.SetVisible(true);
        if(boundary=="idle")shell.SetIdle(false);
        if(boundary=="scene")shell.SetTransitioning(false);
        Assert.Equal(shows+(boundary is "transport" or "idle" or "scene"?1:0),incoming.Shows);
        if(boundary is "slide" or "route")
        {
            shell.Show(from);shell.Tick(.3f);Assert.Equal(1,outgoing.Shows);
            // Genuine future navigation retains the ordinary OnShow semantics.
            if(outgoing.Real is DsMapScreen restoredMap)Assert.Equal(Vector2.zero,Field<DsMapView>(restoredMap,"_view").PanState);
        }
        Assert.Empty(Debug.Errors);
    }
}
