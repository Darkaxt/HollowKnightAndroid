// Typed native-data/art/construction seams only. Production shell/entry/layout,
// page Build, HUD suppression, slide/action and input bodies are generated intact.
#pragma warning disable CS0649, CS0414
using System.Collections;
using System.Reflection;
using SsShellContracts.Engine;
using Object=SsShellContracts.Engine.Object;
using TmpText=SsShellContracts.Engine.TextMeshProUGUI;
using TmpAlign=SsShellContracts.Engine.TextAlignmentOptions;
namespace SsShellContracts;

internal static class EngineBoundary
{
    internal static readonly List<Coroutine> Coroutines=new();
    internal static int Discovery,Io,Serialization,Probes;
    internal static void DrainBringup()
    {
        // Only Bringup requests are driven; frame-end cleanup remains resident.
        foreach(var c in Coroutines.ToArray())
            if(c.Routine.GetType().Name.Contains("Bringup")){Drain(c.Routine);Coroutines.Remove(c);}
    }
    static void Drain(IEnumerator routine)
    {
        // Advance only the typed engine clock at yielded frame boundaries. The
        // unchanged presentation owns activation/settle/readiness decisions.
        int frames=0;
        while(routine.MoveNext())
        {
            if(++frames>1000) throw new InvalidOperationException("host coroutine did not terminate within the fixture frame budget");
            if(routine.Current is IEnumerator child) Drain(child);
            else Time.realtimeSinceStartup += Time.unscaledDeltaTime;
        }
    }
}
public static class FixtureAccess
{
    public static object Field(object target,string name)
    {
        for(var t=target.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);if(f!=null)return f.GetValue(target);}
        throw new MissingFieldException(target.GetType().Name,name);
    }
    public static T Field<T>(object target,string name)=>(T)Field(target,name);
    public static void Set(object target,string name,object value)
    {
        for(var t=target.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);if(f!=null){f.SetValue(target,value);return;}}
        throw new MissingFieldException(target.GetType().Name,name);
    }
    public static void Call(object target,string method,params object[] args)
    {
        try{target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(target,args);}
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();}
    }
    public static Action<T> Delegate<T>(object target,string method)=>(Action<T>)target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.CreateDelegate(typeof(Action<T>),target);
    public static Action Delegate(object target,string method)=>(Action)target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.CreateDelegate(typeof(Action),target);
}
public static class DsConfig
{
    public static int Int(string key,int fallback)=>fallback;
    public static bool Bool(string key,bool fallback)=>fallback;
    public static string Str(string key,string fallback)=>fallback;
}
public static class SilksongPatches
{
    public static readonly SettingsBoundary Settings=new();
    public class SettingsBoundary{public bool Enabled=true;public bool GetBool(string key,bool fallback)=>key=="dualscreen_enabled"?Enabled:fallback;}
}
public static class SilksongProcessStartup
{
    public static void Run(Action mods,Func<bool> eligible,Action create){mods();if(eligible())create();}
}
public static class SilksongModsRuntime{public static void EnsureStarted(){}}
public static class DsGameData{public static bool InGame=true;public static string IdleReason=>"No save";}
public static partial class DsTouch
{
    public struct Point{public int FingerId;public TouchPhase Phase;public Vector2 Position;public double Time;}
    static bool _ready=true;static int _frame=-1;
    static readonly List<Point> Batch=new();
    public static bool Ready
    {
        get
        {
            // JNI's readiness query consumes the native frame batch even when
            // companion gesture polling is suppressed. Model that I/O boundary.
            if(_frame!=Time.frameCount){_frame=Time.frameCount;Batch.Clear();Batch.AddRange(Events);Events.Clear();}
            return _ready;
        }
        set{_ready=value;_frame=-1;Batch.Clear();}
    }
    public static Vector2 SurfaceSize=new(1240,1080);
    public static long Generation=1;
    public static readonly List<Point> Events=new();
    public static bool Begin(){Ready=true;return true;}public static void Stop(){Ready=false;Events.Clear();Generation++;}
    public static void CollectSecondScreen(List<Point> into){into.Clear();if(Ready)into.AddRange(Batch);}
}
public class DsTestCard{public DsTestCard(RectTransform root,int w,int h){}public void Tick(){}}
public static class DsProbe
{
    public static void MaybeRun(){}public static void MaybeDumpSprites(){}public static void MaybeDumpFonts(){}
}
public static partial class DsTheme
{
    public static FontAsset Display=new();public static bool HasFont=false;
    public static void ForgetFont(){}
    public static readonly Sprite White=new(){name="white"},Disc=new(){name="disc"},Rounded=new(){name="rounded"},EdgeFade=new(){name="edge-fade"};
}
public static class DsLogoArt{public static readonly Sprite Sprite=new(){name="existing-logo"};}
public static class DsRuleArt{public static readonly Sprite Horizontal=new(),Vertical=new();}
public static class DsSliderArt{public static readonly Sprite Track=new(),Thumb=new();}
public static class DsTrashArt{public static readonly Sprite Sprite=new();}
public static class DsGameArt
{
    public sealed class CursorArt{public bool Ok=false;public Sprite Corner,Glow;public Color GlowColor=Color.white;}
    static readonly CursorArt Cursor=new();
    public static CursorArt SelectionCursor()=>Cursor;
    public static Sprite TabIcon(InventoryPaneList.PaneTypes pane)=>null;
    public static Sprite MarkerIcon(int index)=>null;
}
public static partial class DsWidgets
{
    // Native font/art construction boundary. Geometry is production Place/Stretch.
    public static TmpText Label(Transform parent,string name,string text,float size,Color color,TmpAlign alignment=TmpAlign.Left,bool display=false)
    {
        var label=Rect(parent,name).gameObject.AddComponent<TmpText>();label.text=text;label.fontSize=size;label.color=color;return label;
    }
    public static void FitCentred(Image image,Sprite sprite,float w,float h){image.sprite=sprite;image.rectTransform.sizeDelta=new(w,h);}
}
public class DsCursor
{
    public const float MoveSeconds=.2f;public float CornerInsetFraction,CornerInset;public int Ticks;
    public void Build(RectTransform host,int glowIndex=0){}
    public void Tick(float dt){Ticks++;}public void Hide(){}public void MoveTo(Rect r,Color? glow,string key){}
}
public class DsHornetPanel
{
    public Action<string,string,Rect> OnSelect;
    public void Build(RectTransform host,float x,float y,float w,float h)
    {
        // Hornet native-art construction is modeled; parent geometry is supplied
        // solely by the unchanged real Inventory.Build call, not copied policy.
        DsWidgets.Place(DsWidgets.Rect(host,"hornet-art-boundary"),x,y,w,h);
    }
    public void Refresh(){}public bool OnTap(Vector2 p)=>false;
}
public class DsToolTween{public int Cancels;public void Build(RectTransform host){}public void Cancel(){Cancels++;}public void Tick(float dt){}}
public partial class DsIconGrid
{
    public int Ticks,Gestures;public string SelectedKey=>_selectedKey;
    public void SetItems(IEnumerable<DsItem> items){}
    public void SetSections(List<DsSection> items){}
    public void Tick(){Ticks++;}public void OnGesture(DsGesture gesture){Gestures++;}
    public void ShowDetail(string name,string desc){}
    public void SetExternalTarget(Rect rect,Color color,string key){}
}
public partial class DsInventoryScreen
{
    protected override void Collect(List<DsItem> into){}
    public void CollectActions(List<DsAction> into){}
}
public partial class DsLoadoutScreen
{
    public int NativeRefreshes;
    bool AtBench()=>true;
    void Refresh(bool force){NativeRefreshes++;}
    void TickCrestPicker(float dt){}
    void BuildCrestPicker()
    {
        // Native crest art/data is modeled; real ShowCrestPicker owns visibility.
        if(_picker==null)_picker=DsWidgets.Rect(_host,"crest-picker-native-art-boundary");
    }
    public void OnGesture(DsGesture gesture){_grid.OnGesture(gesture);}
    public void CollectActions(List<DsAction> into){}
}
public partial class DsTasksScreen
{
    public int NativeRefreshes;
    void Refresh(bool force){NativeRefreshes++;}void Paint(){}void PaintDetail(){}
}
public partial class DsJournalScreen
{
    public int NativeRefreshes;
    void Refresh(bool force){NativeRefreshes++;}void PaintDetail(){}void Paint(){}
}
public partial class DsMapView
{
    Camera _rooms,_decor;bool _visible,_contentDark;float _settleUntil;
    GameMap _map=new();bool GameMapShowing;public int ArrowHides,Polls,Drives;
    public RenderTexture Texture=new();public Vector2 PanState=>_pan;
    public bool HasAnyMap=true;
    public GlobalEnums.MapZone CurrentZone=>GlobalEnums.MapZone.NONE;public string ZoneName=>"";
    readonly Transform _parent;
    public DsMapView(Transform parent){_parent=parent;}
    public void Build(int w,int h)
    {
        Texture.width=w;Texture.height=h;
        var rig=new GameObject("map-art-boundary");rig.transform.SetParent(_parent);
        _rooms=rig.AddComponent<Camera>();_decor=rig.AddComponent<Camera>();
        SetVisible(false);
    }
    public bool Poll(){Polls++;return true;}public bool Drive(){Drives++;return true;}
    void HideNextAreaArrows(){ArrowHides++;}
    public void RefreshMarkers(){}
    public bool TryToMapLocal(Vector2 uv,out Vector2 local){local=uv;return true;}
    public bool TryToViewport(Vector2 local,out Vector2 uv){uv=local;return true;}
}
public class GameMap{}
public static class DsMarkers
{
    public const int TypeCount=2;
    public static bool Unlocked(int i)=>true;public static int Remaining(int i)=>5;
    public static bool Place(int i,Vector2 p)=>true;public static bool RemoveAt(int t,int i)=>true;
    public static List<Vector2> Placed(int t)=>null;
}
public class HUDCamera:Component{public GameObject GameplayChild;}
public class GameCameras
{
    public static GameCameras instance=new();public Camera hudCamera,mainCamera;public bool IsHudVisible=true;
}
public static class HudGlobalHide{public static bool IsHidden=false;}
public class InventoryPaneList{public enum PaneTypes{Inv,Tools,Quests,Journal,Map}}
public class ToolItem{}
public class FullQuestBase{public enum ListCounterTypes{None,Dots,Bar}}
public class GameManager
{
    public delegate void GameStateEvent(GlobalEnums.GameState state);public delegate void PausedEvent(bool paused);public delegate void EnterSceneEvent();
    public static GameManager SilentInstance;
    public bool IsInSceneTransition,isPaused;public UIManager ui=new();
    public event GameStateEvent GameStateChange;public event PausedEvent GamePausedChange;
    public event Action UnloadingLevel;public event EnterSceneEvent OnFinishedEnteringScene;
    public int Subscribers=0;
    public void Pause(bool value){isPaused=value;ui.uiState=value?GlobalEnums.UIState.PAUSED:GlobalEnums.UIState.PLAYING;GamePausedChange?.Invoke(value);}
    public void Boundary(){GameStateChange?.Invoke(GlobalEnums.GameState.LOADING);}
    public void Unload(){UnloadingLevel?.Invoke();}
    public void Finish(){IsInSceneTransition=false;OnFinishedEnteringScene?.Invoke();}
    public int PauseSubscriptions=>GamePausedChange?.GetInvocationList().Length??0;
}
public class UIManager{public GlobalEnums.UIState uiState=GlobalEnums.UIState.PLAYING;public string VisibleNativeMenu="Pause";}
public partial class DsHudView
{
    readonly DsHudRenderScope<GameObject> _scope=new(go=>go!=null,go=>go.layer,(go,v)=>go.layer=v);
    readonly DsHudRenderScope<GameObject> _canvasScope=new(go=>go!=null,go=>go.layer,(go,v)=>go.layer=v);
    readonly DsHudSuppressionScope<Renderer> _overlayRendererScope=new(r=>r!=null,r=>r.forceRenderingOff,(r,v)=>r.forceRenderingOff=v);
    readonly DsHudSuppressionScope<CanvasRenderer> _overlayCanvasScope=new(r=>r!=null,r=>r.cull,(r,v)=>r.cull=v);
    readonly List<Renderer> _overlayRenderers=new(),_renderers=new(),_scratch=new();
    readonly List<CanvasRenderer> _overlayCanvasRenderers=new();
    readonly List<Transform> _roots=new();
    Transform _overlayRoot;
    Camera _capture,_scopedCamera;RenderTexture _texture;RawImage _image,_silkImage,_toolsImage;TmpText _fallback;
    GameCameras _gameCameras;Transform _hudRoot;Coroutine _canvasCleanup;
    int _capturedFrame=-1,_savedMask,_canvasFrame=-1,_hiddenFrame=-1;
    bool _wanted,_stopped,_failed,_presented,_submitted,_showTop;
    float _waitingSince;string _waitingReason;
    public readonly List<GameObject> NativeTargets=new();
    public int Binds,Captures,CanvasRestores;
    public Rect TitleSpace=>new(0,150,900,80);
    public void Build(RectTransform host,float width,float height)
    {
        // HUD camera/art construction boundary; actual admission, visibility,
        // scopes, before/after render, stop/disable and late-update execute intact.
        _image=DsWidgets.Rect(host,"hud-photo-boundary").gameObject.AddComponent<RawImage>();
        _texture=new(){width=(int)width,height=(int)height};_image.texture=_texture;
        _capture=new GameObject("hud-camera-boundary").AddComponent<Camera>();_capture.transform.SetParent(host);
        _hudRoot=new GameObject("native-hud-boundary").transform;
        _gameCameras=GameCameras.instance;
        var native=new GameObject("native-health"){layer=5};native.AddComponent<Renderer>();NativeTargets.Add(native);_roots.Add(native.transform);
        _overlayRoot=new GameObject("captured-backdrop-boundary").transform;
        _overlayRoot.gameObject.AddComponent<Renderer>();_overlayRoot.gameObject.AddComponent<CanvasRenderer>();
    }
    IEnumerator RestoreCanvasAtFrameEnd(){yield break;}
    bool TryBind(){Binds++;return true;}
    void Waiting(string reason){}
    void Fail(Exception error){_failed=true;Debug.LogError(error);}
    void PrepareCanvasScope(){}
    void Diagnostic(){}
    readonly List<GameObject> _targets=new();
    bool FrameCamera(){Captures++;return _targets.Count>0;}
    void ApplySplit(){}
}
