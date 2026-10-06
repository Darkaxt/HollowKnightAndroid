// Namespace-local engine boundary. No HK/native global aliases or shell policy.
// Host rectangles/activation model Unity data; no claim of GPU/TMP/display proof.
using System.Collections;
using System.Reflection;
namespace SsShellContracts.Engine;

public struct Vector2
{
    public float x,y;
    public Vector2(float x,float y){this.x=x;this.y=y;}
    public static Vector2 zero=>new(0,0); public static Vector2 one=>new(1,1);
    public float sqrMagnitude=>x*x+y*y; public float magnitude=>MathF.Sqrt(sqrMagnitude);
    public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
    public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
    public static Vector2 operator *(Vector2 a,float b)=>new(a.x*b,a.y*b);
    public static Vector2 operator /(Vector2 a,float b)=>new(a.x/b,a.y/b);
    public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y;
    public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b);
    public override bool Equals(object value)=>value is Vector2 v&&this==v;
    public override int GetHashCode()=>HashCode.Combine(x,y);
    public static float Distance(Vector2 a,Vector2 b)=>(a-b).magnitude;
    public static Vector2 Lerp(Vector2 a,Vector2 b,float t)=>a+(b-a)*t;
}
public struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 one=>new(1,1,1); public static Vector3 zero=>new(0,0,0);
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
}
public struct Bounds { public Vector3 center,size; public Bounds(Vector3 center,Vector3 size){this.center=center;this.size=size;} }
// Identity/translation model only, not native transform/rotation proof.
public struct Matrix4x4
{
    public static Matrix4x4 TRS(Vector3 p,Quaternion r,Vector3 s)=>new();
    public Matrix4x4 inverse=>this;
    public Vector3 MultiplyPoint3x4(Vector3 p)=>p;
}
public struct Quaternion {public static Quaternion Euler(float x,float y,float z)=>new();public static Vector3 operator *(Quaternion q,Vector3 v)=>v;}
public struct Rect
{
    public float x,y,width,height;
    public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
    public float xMin=>x;public float yMin=>y;public float xMax=>x+width;public float yMax=>y+height;
    public Vector2 position=>new(x,y); public Vector2 size=>new(width,height);public Vector2 center=>new(x+width*.5f,y+height*.5f);
    public bool Contains(Vector2 p)=>p.x>=x&&p.x<xMax&&p.y>=y&&p.y<yMax;
    public static bool operator ==(Rect a,Rect b)=>a.x==b.x&&a.y==b.y&&a.width==b.width&&a.height==b.height;
    public static bool operator !=(Rect a,Rect b)=>!(a==b);
    public override bool Equals(object o)=>o is Rect r&&x==r.x&&y==r.y&&width==r.width&&height==r.height;
    public override int GetHashCode()=>HashCode.Combine(x,y,width,height);
}
public struct Color
{
    public float r,g,b,a;
    public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
    public static Color white=>new(1,1,1,1); public static Color black=>new(0,0,0,1);public static Color clear=>new(0,0,0,0);
    public static bool operator ==(Color a,Color b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a;
    public static bool operator !=(Color a,Color b)=>!(a==b);
    public override bool Equals(object o)=>o is Color c&&this==c;
    public override int GetHashCode()=>HashCode.Combine(r,g,b,a);
}
public static class Mathf
{
    public static float Min(float a,float b)=>MathF.Min(a,b); public static int Min(int a,int b)=>Math.Min(a,b);
    public static float Max(float a,float b)=>MathF.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
    public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b);
    public static float Clamp01(float v)=>Clamp(v,0,1);public static float Abs(float v)=>MathF.Abs(v);
    public static float Pow(float a,float b)=>MathF.Pow(a,b);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
    public static int RoundToInt(float v)=>(int)MathF.Round(v);public static float Round(float v)=>MathF.Round(v); public static int CeilToInt(float v)=>(int)MathF.Ceiling(v);
    public static float Sqrt(float v)=>MathF.Sqrt(v);
    public static float Log(float v)=>MathF.Log(v);public static float Exp(float v)=>MathF.Exp(v);
    public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.000001f;
}
public class Object
{
    public string name;
    public bool Retired;
    public static bool operator ==(Object a,Object b)=>
        (ReferenceEquals(a,null)||a.Retired)?ReferenceEquals(b,null)||b.Retired:ReferenceEquals(a,b);
    public static bool operator !=(Object a,Object b)=>!(a==b);
    public override bool Equals(object o)=>ReferenceEquals(this,o);
    public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    public static void DontDestroyOnLoad(Object value){}
    public static void Destroy(Object value)
    {
        if(value is GameObject go){go.SetActive(false);go.InvokeAll("OnDestroy");go.transform.SetParent(null);go.Destroyed=true;foreach(var c in go.AllComponents)c.Retired=true;}
        value.Retired=true;
    }
}
public class Component : Object
{
    public GameObject gameObject;
    public Transform transform=>gameObject.transform;
    public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
    public static int InventoryQueries;
    public void GetComponentsInChildren<T>(bool includeInactive,List<T> into) where T:Component
    {
        InventoryQueries++;
        gameObject.CollectComponents(includeInactive,into);
    }
    public T[] GetComponentsInChildren<T>(bool includeInactive) where T:Component
    {
        var list=new List<T>();GetComponentsInChildren(includeInactive,list);return list.ToArray();
    }
}
public class Transform : Component
{
    internal readonly List<Transform> Children=new();
    Transform _parent;
    public static int ParentReads, ChildCountReads;
    public Transform parent { get { ParentReads++; return _parent; } set { _parent=value; } }
    internal Transform EngineParent=>_parent;
    public Vector3 localScale=Vector3.one,position;public Quaternion localRotation,rotation;
    public Vector3 lossyScale=>localScale;
    public void SetPositionAndRotation(Vector3 position,Quaternion rotation){this.position=position;this.rotation=rotation;}
    public Vector3 TransformPoint(Vector3 p)=>position+p;
    public int childCount { get { ChildCountReads++; return Children.Count; } }
    public Transform GetChild(int i)=>Children[i];
    public void SetParent(Transform value,bool worldPositionStays=false)
    {
        if(ReferenceEquals(_parent,value))return;
        var previous=_parent;previous?.Children.Remove(this);_parent=value;_parent?.Children.Add(this);
        previous?.gameObject.InvokeAll("OnTransformChildrenChanged");
        _parent?.gameObject.InvokeAll("OnTransformChildrenChanged");
        gameObject.InvokeAll("OnTransformParentChanged");
    }
    public void SetAsLastSibling(){if(parent==null)return;parent.Children.Remove(this);parent.Children.Add(this);}
    public void SetSiblingIndex(int index){if(parent==null)return;parent.Children.Remove(this);parent.Children.Insert(Math.Min(index,parent.Children.Count),this);}
    public Transform Find(string path)
    {
        Transform at=this;
        foreach(var segment in path.Split('/')){at=at?.Children.FirstOrDefault(c=>c.gameObject.name==segment);if(at==null)return null;}
        return at;
    }
}
public class RectTransform : Transform
{
    public Vector2 anchorMin,anchorMax,pivot=new(0,1),sizeDelta,anchoredPosition,offsetMin,offsetMax;
    public Rect rect
    {
        get
        {
            var p=parent as RectTransform;
            float w=(p?.rect.width??0)*(anchorMax.x-anchorMin.x)+sizeDelta.x+offsetMax.x-offsetMin.x;
            float h=(p?.rect.height??0)*(anchorMax.y-anchorMin.y)+sizeDelta.y+offsetMax.y-offsetMin.y;
            return new Rect(-pivot.x*w,-pivot.y*h,w,h);
        }
    }
}
public struct Scene{public bool Invalid;public bool IsValid()=>!Invalid;}
public class GameObject : Object
{
    public Scene scene;
    // Observe native construction for entrypoint tests; no entry/shell policy.
    public static Component LastAdded;
    readonly List<Component> components=new();
    internal IEnumerable<Component> AllComponents=>components;
    public Transform transform;
    public int layer;public bool activeSelf=true,Destroyed;
    // Native activeInHierarchy is an engine scalar, not a production Transform.parent call.
    public bool activeInHierarchy=>activeSelf&&!Destroyed&&(transform.EngineParent?.gameObject.activeInHierarchy??true);
    public GameObject(string name="host"){this.name=name;transform=new Transform{gameObject=this};components.Add(transform);}
    public T AddComponent<T>() where T:Component,new()
    {
        if(typeof(T)==typeof(RectTransform)&&transform is RectTransform existing)return (T)(Component)existing;
        if(typeof(T)==typeof(Canvas) && transform is not RectTransform) AddComponent<RectTransform>();
        var value=new T{gameObject=this};components.Add(value);
        if(value is RectTransform rt){components.Remove(transform);rt.parent=transform.parent;transform=rt;}
        if(activeInHierarchy)Invoke(value,"OnEnable");
        LastAdded=value;
        return value;
    }
    public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault(c=>c!=null);
    internal void CollectComponents<T>(bool includeInactive,List<T> into) where T:Component
    {
        if(!includeInactive&&!activeInHierarchy)return;
        foreach(var component in components)if(component is T value && value!=null)into.Add(value);
        foreach(var child in transform.Children)child.gameObject.CollectComponents(includeInactive,into);
    }
    public void SetActive(bool value)
    {
        if(value==activeSelf)return;
        bool before=activeInHierarchy;activeSelf=value;bool after=activeInHierarchy;
        if(before!=after)NotifyTree(after);
    }
    void NotifyTree(bool active)
    {
        InvokeAll(active?"OnEnable":"OnDisable");
        foreach(var child in transform.Children)if(child.gameObject.activeSelf)child.gameObject.NotifyTree(active);
    }
    internal void InvokeAll(string method){foreach(var c in components.ToArray())Invoke(c,method);}
    static void Invoke(Component c,string name)
    {
        var m=c.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(m!=null){try{m.Invoke(c,null);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();}}
    }
}
public class Coroutine{public IEnumerator Routine;}
public class MonoBehaviour:Component
{
    public Coroutine StartCoroutine(IEnumerator routine){var c=new Coroutine{Routine=routine};SsShellContracts.EngineBoundary.Coroutines.Add(c);return c;}
    public void StopCoroutine(Coroutine c){SsShellContracts.EngineBoundary.Coroutines.Remove(c);}
    public bool isActiveAndEnabled=>gameObject.activeInHierarchy;
}
public enum RenderMode{ScreenSpaceOverlay,ScreenSpaceCamera,WorldSpace}
public class Canvas:MonoBehaviour
{
    public bool enabled=true;public float planeDistance;
    public RenderMode renderMode;public int targetDisplay;public Camera worldCamera;
    public static void ForceUpdateCanvases(){}
}
public class CanvasScaler:MonoBehaviour
{
    public enum ScaleMode { ScaleWithScreenSize }
    public enum ScreenMatchMode { MatchWidthOrHeight }
    public ScaleMode uiScaleMode;public ScreenMatchMode screenMatchMode;
    public float matchWidthOrHeight,referencePixelsPerUnit=100;
    Vector2 resolution;
    public Vector2 referenceResolution
    {
        get=>resolution;
        set { resolution=value;((RectTransform)transform).sizeDelta=value; }
    }
    protected virtual void HandleScaleWithScreenSize(){}
    protected void SetScaleFactor(float value){}
    protected void SetReferencePixelsPerUnit(float value){}
}
public enum CameraClearFlags { SolidColor,Depth }
public class Camera:MonoBehaviour
{
    static readonly List<Camera> Cameras=new();
    public Camera(){Cameras.Add(this);}
    static IEnumerable<Camera> Active=>Cameras.Where(c=>c.gameObject!=null && c.isActiveAndEnabled);
    public static int allCamerasCount=>Active.Count();
    public static int GetAllCameras(Camera[] into){var active=Active.ToArray();Array.Copy(active,into,active.Length);return active.Length;}
    public bool orthographic,allowHDR,allowMSAA,useOcclusionCulling;
    public float nearClipPlane,farClipPlane,depth,orthographicSize,aspect;
    public CameraClearFlags clearFlags;public Color backgroundColor;
    public bool enabled;public RenderTexture targetTexture;public int cullingMask=32,targetDisplay;
    public new bool isActiveAndEnabled=>enabled&&gameObject.activeInHierarchy;
    public static event Action<Camera> onPreCull,onPostRender;
    public static int PreSubscribers=>onPreCull?.GetInvocationList().Length??0;
    public static int PostSubscribers=>onPostRender?.GetInvocationList().Length??0;
    public static void Pre(Camera c)=>onPreCull?.Invoke(c);public static void Post(Camera c)=>onPostRender?.Invoke(c);
}
public class Renderer:Component{public bool forceRenderingOff;public bool enabled=true;}
public class SpriteRenderer:Renderer{public Sprite sprite;}
public class CanvasRenderer:Component{public bool cull;}
public class Graphic:MonoBehaviour
{
    public Canvas canvas;
}
public class MeshFilter:Component{public Mesh sharedMesh;}
public class Mesh{public int vertexCount=4;}
public class RectMask2D:Component{}
public class Mask:Component{public bool showMaskGraphic;}
public class Image:Component
{
    public enum Type{Simple,Sliced}
    public Type type;public Sprite sprite;public Color color;public bool raycastTarget,useSpriteMesh,preserveAspect,enabled=true;
    public RectTransform rectTransform=>(RectTransform)transform;
}
public class RawImage:Component
{
    public RenderTexture texture;public Color color;public bool raycastTarget;public Rect uvRect;
    public RectTransform rectTransform=>(RectTransform)transform;
    public Canvas canvas
    {
        get{for(var p=transform;p!=null;p=p.parent){var c=p.GetComponent<Canvas>();if(c!=null)return c;}return null;}
    }
}
public class Sprite:Object{}
public class RenderTexture:Object
{
    public int width,height,Creates;bool created=true;
    public bool IsCreated()=>created;public bool Create(){Creates++;created=true;return true;}
    public void Release(){created=false;}
}
public class FontAsset{public object material=new();}
public enum TextAlignmentOptions{Left,Center,TopLeft,Top,TopRight,Bottom}
[Flags]public enum FontStyles{Normal=0,Bold=1,UpperCase=2}
public class TextMeshProUGUI:Component
{
    public string text="";public float fontSize,fontSizeMin,fontSizeMax;public Color color;public FontStyles fontStyle;
    public bool enableWordWrapping,enableAutoSizing,enableKerning,richText,isRightToLeftText;public int fontWeight;public FontAsset font;public object fontSharedMaterial;
    public RectTransform rectTransform=>(RectTransform)transform;
    public float characterSpacing, lineSpacing, paragraphSpacing;
    public int Measurements;
    // A deliberately modeled text-measurement boundary, not native TMP proof.
    public Vector2 GetPreferredValues(string t) { Measurements++; return new((t?.Length??0)*fontSize*.5f,fontSize); }
}
public enum TouchPhase{Began,Moved,Stationary,Ended,Canceled}
public static class Time{public static float unscaledTime=10,unscaledDeltaTime=.016f,realtimeSinceStartup=10;public static int frameCount=1;}
public static class Debug
{
    public static readonly List<string> Errors=new();
    public static void Log(object value){}public static void LogWarning(object value){}
    public static void LogError(object value)=>Errors.Add(value?.ToString()??"");
}
public class Display
{
    public int systemWidth=1240,systemHeight=1080,renderingWidth,renderingHeight,Activations;
    public void Activate()=>Activations++;
    public static Display[] displays={new(),new()};public static event Action onDisplaysUpdated;
    public static void Publish()=>onDisplaysUpdated?.Invoke();
}
public static class Resources
{
    public static Object[] Resident=Array.Empty<Object>();
    public static T[] FindObjectsOfTypeAll<T>(){SsShellContracts.EngineBoundary.Discovery++;return Resident.OfType<T>().Where(x=>x is not Object o||o!=null).ToArray();}
}
public class DefaultExecutionOrder:Attribute{public DefaultExecutionOrder(int order){}}
public enum RuntimeInitializeLoadType{AfterSceneLoad}
public class RuntimeInitializeOnLoadMethod:Attribute{public RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType type){}}
