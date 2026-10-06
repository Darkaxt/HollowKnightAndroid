using System.Collections;
using SsShellContracts.Engine;
using Xunit;
using static SsShellContracts.FixtureAccess;
namespace SsShellContracts;

[CollectionDefinition("SS production shell",DisableParallelization=true)]
public class SilksongShellCollection{}

[Collection("SS production shell")]
public partial class SilksongShellProductionTests
{
    sealed class Live : IDisposable
    {
        internal readonly GameManager Manager;
        internal readonly DualScreenV2 Owner;
        internal DsShell Shell=>Field<DsShell>(Owner,"_shell");
        internal RectTransform Root=>Field<DsPresentation>(Owner,"_screen").Root;
        internal Live(bool paused=false,string menu="Pause")
        {
            Time.unscaledTime=10;Time.frameCount=1;DsGameData.InGame=true;
            DsTouch.SurfaceSize=new(1240,1080);Time.realtimeSinceStartup=10;
            Display.displays=new[]{new Display(),new Display()};
            SilksongPatches.Settings.Enabled=true;DsTouch.Ready=true;DsTouch.Events.Clear();Debug.Errors.Clear();
            Manager=new(){isPaused=paused};Manager.ui.uiState=paused?GlobalEnums.UIState.PAUSED:GlobalEnums.UIState.PLAYING;
            GameManager.SilentInstance=Manager;
            GameObject.LastAdded=null;
            typeof(DualScreenV2).GetMethod("Bootstrap",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
            Owner=Assert.IsType<DualScreenV2>(GameObject.LastAdded);
            Assert.Equal("__DualScreenV2__",Owner.gameObject.name);
            Call(Owner,"Start");EngineBoundary.DrainBringup();
            Update();
        }
        internal void Update(){Call(Owner,"Update");Time.frameCount++;}
        public void Dispose(){Call(Owner,"Shutdown");Call(Owner,"Shutdown");GameManager.SilentInstance=null;}
    }
    static IList Entries(DsShell shell)=>Field<IList>(shell,"_entries");
    static IDsScreen Page(DsShell shell,int i)=>Field<IDsScreen>(Entries(shell)[i],"Screen");
    static RectTransform Host(DsShell shell,int i)=>Field<RectTransform>(Entries(shell)[i],"Host");
    static RectTransform Child(RectTransform root,string path)=>(RectTransform)root.Find(path);
    static void Box(RectTransform rect,float x,float y,float w,float h)
    {
        Assert.NotNull(rect);Assert.Equal(x,rect.anchoredPosition.x,3);Assert.Equal(-y,rect.anchoredPosition.y,3);
        Assert.Equal(w,rect.sizeDelta.x,3);Assert.Equal(h,rect.sizeDelta.y,3);
    }
    static void Tap(DualScreenV2 owner,float x,float layoutY)
    {
        var p=new Vector2(x,1080-layoutY);
        DsTouch.Events.Add(new(){FingerId=1,Phase=TouchPhase.Began,Position=p,Time=Time.unscaledTime});
        DsTouch.Events.Add(new(){FingerId=1,Phase=TouchPhase.Ended,Position=p,Time=Time.unscaledTime+.05});
        Call(owner,"Update");Time.frameCount++;
    }
    static void LogoOnly(Live live)
    {
        foreach(var path in new[]{"body","hud-header","tabs","actions"})
            Assert.False(Child(live.Root,path).gameObject.activeInHierarchy,$"Gameplay role '{path}' remained visible during pause");
        Assert.True(Child(live.Root,"title-card/logo").gameObject.activeInHierarchy);
        Assert.Same(DsLogoArt.Sprite,Child(live.Root,"title-card/logo").GetComponent<Image>().sprite);
        var hud=Field<DsHudView>(live.Shell,"_hud");Assert.False(Field<bool>(hud,"_wanted"));
        var view=Field<DsMapView>(Page(live.Shell,4),"_view");Assert.False(Field<bool>(view,"_visible"));
        Call(hud,"LateUpdate");Call(view,"LateTick");
        Assert.False(Field<Camera>(hud,"_capture").enabled);
        Assert.False(Field<Camera>(view,"_rooms").enabled);Assert.False(Field<Camera>(view,"_decor").enabled);
    }

    [Fact]
    public void Actual_Start_Bringup_attaches_exactly_five_native_routes()
    {
        using var live=new Live();var shell=live.Shell;
        Assert.Equal(5,Entries(shell).Count);
        var ids=new[]{"inventory","loadout","tasks","journal","map"};
        var types=new[]{typeof(DsInventoryScreen),typeof(DsLoadoutScreen),typeof(DsTasksScreen),typeof(DsJournalScreen),typeof(DsMapScreen)};
        for(int i=0;i<5;i++)
        {
            Assert.Equal(ids[i],Page(shell,i).Id);Assert.Equal(types[i],Page(shell,i).GetType());
            Assert.Equal((InventoryPaneList.PaneTypes)i,Field<InventoryPaneList.PaneTypes>(Entries(shell)[i],"Pane"));
        }
        var host=Field<DualSouls.DualScreen.DirectDisplayHost>(live.Owner,"_host");
        Assert.True(host.IsActive);Assert.Same(Field(live.Owner,"_shellContent"),Field(host,"_content"));
        Assert.Equal("map",shell.ActiveId);Assert.True(Field<bool>(Entries(shell)[4],"Built"));
        for(int i=0;i<4;i++)Assert.False(Field<bool>(Entries(shell)[i],"Built"));
        Assert.Empty(Debug.Errors);
    }

    [Fact]
    public void Complete_real_shell_and_all_pane_builds_have_exact_regions()
    {
        using var live=new Live();var shell=live.Shell;var root=live.Root;
        Box(Child(root,"hud-header"),0,0,1240,240);Box(Child(root,"body"),0,240,1240,700);Box(Child(root,"tabs"),0,940,1240,140);
        Assert.NotNull(Child(root,"body").GetComponent<RectMask2D>());
        for(int i=0;i<5;i++)Box(Field<RectTransform>(Entries(shell)[i],"Tab"),i*248,0,248,140);
        for(int i=0;i<5;i++){shell.Show(i);shell.Tick(.3f);Assert.False(Field<bool>(Entries(shell)[i],"Broken"));}
        var inv=Host(shell,0);Box(Child(inv,"hornet-art-boundary"),20,20,420,660);
        Box(Child(inv,"grid-clip"),455,8,427,684);Box(Child(inv,"detail"),900,20,320,582);
        Box(Child(inv,"split-items"),454,20,2,660);Box(Child(inv,"split-detail"),884,20,2,660);
        Assert.Equal(new Rect(900,842,320,78),((IDsActionBar)Page(shell,0)).ActionPane);
        var loadout=Host(shell,1);Box(Child(loadout,"crest"),20,16,470,664);
        Box(Child(loadout,"grid-clip"),505,8,377,684);Box(Child(loadout,"detail"),900,16,320,510);
        Box(Child(loadout,"split-tools"),504,16,2,664);Box(Child(loadout,"split-detail"),884,16,2,664);
        Assert.Equal(new Rect(900,766,320,154),((IDsActionBar)Page(shell,1)).ActionPane);
        var tasks=Host(shell,2);Box(Child(tasks,"list-clip"),4,4,812,692);Box(Child(tasks,"list-clip/list"),16,16,780,660);
        Box(Child(tasks,"detail"),840,20,380,582);Box(Child(tasks,"split"),819,20,2,660);
        Assert.Equal(new Rect(840,842,380,78),((IDsActionBar)Page(shell,2)).ActionPane);
        var journal=Host(shell,3);Box(Child(journal,"list-clip"),0,-4,420,708);Box(Child(journal,"list-clip/list"),20,20,380,668);
        Box(Child(journal,"portrait"),430,16,430,668);Box(Child(journal,"detail"),890,16,330,668);
        Box(Child(journal,"split-art"),414,16,2,668);Box(Child(journal,"split-detail"),874,16,2,668);
        var map=Host(shell,4);Box(Child(map,"map"),20,20,1200,676);
        Assert.Equal(new Rect(20,260,1200,676),Field<Rect>(Page(shell,4),"_mapRect"));
        var fade=Child(map,"map/edge-fade").GetComponent<Image>();Assert.Equal(Image.Type.Sliced,fade.type);Assert.False(fade.useSpriteMesh);Assert.False(fade.raycastTarget);
        Box(Child(map,"map/zoom-track"),1161,12,29,652);
        Assert.Equal(new Rect(931.5f,838,220,78),((IDsActionBar)Page(shell,4)).ActionPane);
        Assert.Empty(Debug.Errors);
    }

    public static IEnumerable<object[]> RoutePairs()
    {
        for(int from=0;from<5;from++)for(int to=0;to<5;to++)yield return new object[]{from,to};
    }
    [Theory][MemberData(nameof(RoutePairs))]
    public void All_25_routes_run_complete_cubic_slide_and_preserve_lazy_identity(int from,int to)
    {
        using var live=new Live();var shell=live.Shell;
        shell.Show(from);shell.Tick(.3f);var page=Page(shell,to);var host=Host(shell,to);
        shell.Show(to);
        if(from==to){Assert.Equal(-1,Field<int>(shell,"_slideFrom"));Assert.Same(page,Page(shell,to));return;}
        float direction=to>from?1:-1;
        Assert.Equal(from,Field<int>(shell,"_slideFrom"));Assert.Equal(direction*1240,host.offsetMin.x);
        var outgoing=Page(shell,from);var incoming=Page(shell,to);
        int before=Ticks(outgoing);
        shell.Tick(.15f);
        Assert.Equal(direction*155,host.offsetMin.x,3);Assert.Equal(-direction*1085,Host(shell,from).offsetMin.x,3);
        Assert.True(Ticks(outgoing)>before,"Outgoing complete page Tick was not driven during slide");
        shell.Tick(.15f);Assert.Equal(-1,Field<int>(shell,"_slideFrom"));
        Assert.Equal(0,host.offsetMin.x);Assert.Equal(0,host.offsetMax.x);
        Assert.Equal(0,Host(shell,from).offsetMin.x);Assert.False(Host(shell,from).gameObject.activeSelf);
        shell.Show(from);shell.Tick(.3f);shell.Show(to);shell.Tick(.3f);
        Assert.Same(page,Page(shell,to));Assert.Same(host,Host(shell,to));Assert.Same(incoming,Page(shell,to));
        Assert.Empty(Debug.Errors);
    }
    static int Ticks(IDsScreen screen)=>screen switch
    {
        DsInventoryScreen inv=>Field<DsIconGrid>(inv,"Grid").Ticks,
        DsLoadoutScreen load=>Field<DsIconGrid>(load,"_grid").Ticks,
        DsTasksScreen tasks=>Field<DsCursor>(tasks,"_cursor").Ticks,
        DsJournalScreen journal=>Field<DsCursor>(journal,"_cursor").Ticks,
        DsMapScreen map=>Field<DsMapView>(map,"_view").Polls,
        _=>throw new InvalidOperationException(),
    };

    [Theory][InlineData("Pause")][InlineData("Options")][InlineData("Mods")][InlineData("Skins")]
    public void Accepted_current_manager_pause_is_persistent_logo_only_while_gameplay_stays_true(string menu)
    {
        using var live=new Live();Assert.True(DsGameData.InGame);
        live.Manager.Pause(true);live.Manager.ui.VisibleNativeMenu=menu;
        Assert.Equal(menu,live.Manager.ui.VisibleNativeMenu);LogoOnly(live);
        int ticks=Ticks(Page(live.Shell,4));live.Update();LogoOnly(live);
        Assert.Equal(ticks,Ticks(Page(live.Shell,4)));
        Assert.Equal("map",live.Shell.ActiveId);
        live.Manager.Pause(false);live.Update();Assert.True(Child(live.Root,"body").gameObject.activeInHierarchy);
        Assert.False(Child(live.Root,"title-card").gameObject.activeSelf);Assert.True(Ticks(Page(live.Shell,4))>ticks);
    }

    [Theory][InlineData(true,false)][InlineData(false,true)]
    public void Native_scalar_reconciliation_precedes_input_dispatch_without_pause_event(bool paused,bool uiPaused)
    {
        using var live=new Live();live.Manager.isPaused=paused;live.Manager.ui.uiState=uiPaused?GlobalEnums.UIState.PAUSED:GlobalEnums.UIState.PLAYING;
        Tap(live.Owner,620,1010);
        Assert.Equal("map",live.Shell.ActiveId);LogoOnly(live);
    }

    [Fact]
    public void Startup_and_rebuild_already_paused_never_reacquire_gameplay_presentation()
    {
        using var live=new Live(true);LogoOnly(live);
        var original=live.Shell;Call(live.Owner,"RebuildShell");Assert.NotSame(original,live.Shell);
        live.Update();LogoOnly(live);
        Assert.True(Field<bool>(original,"_disposed"));
        live.Manager.Pause(false);live.Update();Assert.Equal("map",live.Shell.ActiveId);Assert.True(Child(live.Root,"body").gameObject.activeInHierarchy);
    }

    [Fact]
    public void Pause_preserves_map_marker_pan_zoom_state_and_releases_only_held_slider()
    {
        using var live=new Live();var map=(DsMapScreen)Page(live.Shell,4);var view=Field<DsMapView>(map,"_view");
        view.SetMode(DsMapView.Frame.World);view.Pan(new(17,23));view.SetZoom(2.25f);
        Set(map,"_markerMode",true);Set(map,"_markerPick",1);Set(map,"_erasing",true);Set(map,"_lastTouch",Time.unscaledTime);
        live.Update();Assert.True(Field<bool>(live.Shell,"_stripShown"));
        var slider=Field<DsZoomSlider>(map,"_slider");Assert.True(slider.Grab(slider.Hit.center,view.ZoomLevel));Set(map,"_sliderGesture",true);
        var state=new{view.PanState,view.ZoomLevel,view.Mode,Pick=Field<int>(map,"_markerPick"),Erasing=Field<bool>(map,"_erasing")};
        for(int cycle=0;cycle<3;cycle++)
        {
            live.Manager.Pause(true);live.Update();LogoOnly(live);Assert.False(slider.Held);Assert.False(Field<bool>(map,"_sliderGesture"));
            Assert.Equal(state.PanState,view.PanState);Assert.Equal(state.ZoomLevel,view.ZoomLevel);Assert.Equal(state.Mode,view.Mode);
            Assert.True(map.StripOverride);Assert.Equal(state.Pick,Field<int>(map,"_markerPick"));Assert.Equal(state.Erasing,Field<bool>(map,"_erasing"));
            live.Manager.Pause(false);live.Update();Assert.True(Field<bool>(live.Shell,"_stripShown"));Assert.True(Field<bool>(view,"_visible"));
            Assert.Equal(state.PanState,view.PanState);Assert.Equal(state.ZoomLevel,view.ZoomLevel);
        }
    }

    [Fact]
    public void Pause_settles_interrupted_slide_without_resetting_selected_loadout_or_resident_scroll()
    {
        using var live=new Live();var shell=live.Shell;
        shell.Show(1);shell.Tick(.1f);var loadout=Page(shell,1);Set(loadout,"_choosingCrest",true);
        Set(Field<DsIconGrid>(loadout,"_grid"),"_scroll",123f);Set(Field<DsIconGrid>(loadout,"_grid"),"_selectedKey","tool-selected");
        int cancels=Field<DsToolTween>(loadout,"_tween").Cancels;
        live.Manager.Pause(true);live.Update();
        Assert.Equal("loadout",shell.ActiveId);Assert.Equal(-1,Field<int>(shell,"_slideFrom"));
        for(int i=0;i<5;i++){Assert.Equal(0,Host(shell,i).offsetMin.x);Assert.Equal(0,Host(shell,i).offsetMax.x);}
        Assert.True(Field<bool>(loadout,"_choosingCrest"));Assert.Equal(cancels,Field<DsToolTween>(loadout,"_tween").Cancels);
        Assert.Equal(123f,Field<float>(Field<DsIconGrid>(loadout,"_grid"),"_scroll"));
        live.Manager.Pause(false);live.Update();Assert.Same(loadout,Page(shell,1));Assert.True(Field<bool>(loadout,"_choosingCrest"));
        Assert.Equal("tool-selected",Field<DsIconGrid>(loadout,"_grid").SelectedKey);
    }

    [Fact]
    public void Held_input_and_action_or_marker_strip_cannot_escape_pause()
    {
        using var live=new Live();var map=(DsMapScreen)Page(live.Shell,4);
        var point=new Vector2(30,700);DsTouch.Events.Add(new(){FingerId=9,Phase=TouchPhase.Began,Position=point,Time=10});live.Update();
        Set(map,"_markerMode",true);live.Update();live.Manager.Pause(true);
        Tap(live.Owner,1100,1010);Assert.False(Field<bool>(map,"_erasing"));
        DsTouch.Events.Add(new(){FingerId=9,Phase=TouchPhase.Ended,Position=point,Time=11});live.Update();
        live.Manager.Pause(false);live.Update();Assert.Equal("map",live.Shell.ActiveId);Assert.False(Field<bool>(map,"_erasing"));
        Assert.False(Field<DsInput>(live.Owner,"_input").SingleTouchActive);
    }

    [Fact]
    public void Replacement_manager_and_stale_pause_callbacks_respect_transition_and_transport_precedence()
    {
        using var live=new Live();var callbacks=Field<DsHudManagerCallbacks>(live.Owner,"_managerCallbacks");
        var oldPause=Field<GameManager.PausedEvent>(live.Owner,"_pauseHandler");
        var replacement=new GameManager{isPaused=true,IsInSceneTransition=true};replacement.ui.uiState=GlobalEnums.UIState.PAUSED;
        GameManager.SilentInstance=replacement;live.Update();oldPause(false);LogoOnly(live);
        Assert.Equal(0,live.Manager.PauseSubscriptions);Assert.Equal(1,replacement.PauseSubscriptions);
        replacement.Pause(false);live.Update();Assert.True(Field<bool>(live.Shell,"_transitioning"));Assert.False(Field<bool>(live.Shell,"_operational"));
        replacement.Finish();live.Update();Assert.True(Field<bool>(live.Shell,"_operational"));
        replacement.Pause(true);Call(live.Owner,"OnApplicationPause",true);
        var presentation=Field<DsPresentation>(live.Owner,"_screen");
        Assert.False(presentation.Camera.enabled);Assert.False(presentation.OverlayCamera.enabled);
        Assert.False(presentation.Canvas.enabled);Assert.False(presentation.OverlayCanvas.enabled);
        Assert.False(Field<DualSouls.DualScreen.DirectDisplayHost>(live.Owner,"_host").IsActive);
        Call(live.Owner,"OnApplicationPause",false);live.Update();LogoOnly(live);
        replacement.Pause(false);live.Update();Assert.True(Child(live.Root,"body").gameObject.activeInHierarchy);
    }
}
