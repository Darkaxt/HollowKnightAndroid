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
}
public struct Quaternion {public static Quaternion Euler(float x,float y,float z)=>new();}
public struct Rect
{
    public float x,y,width,height;
    public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
    public float xMin=>x;public float yMin=>y;public float xMax=>x+width;public float yMax=>y+height;
    public Vector2 position=>new(x,y); public Vector2 size=>new(width,height);public Vector2 center=>new(x+width*.5f,y+height*.5f);
    public bool Contains(Vector2 p)=>p.x>=x&&p.x<xMax&&p.y>=y&&p.y<yMax;
    public static bool operator ==(Rect a,Rect b)=>a.Equals(b);
    public static bool operator !=(Rect a,Rect b)=>!a.Equals(b);
    public override bool Equals(object o)=>o is Rect r&&x==r.x&&y==r.y&&width==r.width&&height==r.height;
    public override int GetHashCode()=>HashCode.Combine(x,y,width,height);
}
public struct Color
{
    public float r,g,b,a;
    public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
    public static Color white=>new(1,1,1,1); public static Color black=>new(0,0,0,1);public static Color clear=>new(0,0,0,0);
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
    public static void DontDestroyOnLoad(Object value){}
    public static void Destroy(Object value)
    {
        if(value is GameObject go){go.SetActive(false);go.InvokeAll("OnDestroy");go.transform.SetParent(null);go.Destroyed=true;}
    }
}
public class Component : Object
{
    public GameObject gameObject;
    public Transform transform=>gameObject.transform;
    public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
    public void GetComponentsInChildren<T>(bool includeInactive,List<T> into) where T:Component
    {
        gameObject.CollectComponents(includeInactive,into);
    }
}
public class Transform : Component
{
    internal readonly List<Transform> Children=new();
    public Transform parent;
    public Vector3 localScale=Vector3.one;public Quaternion localRotation;
    public int childCount=>Children.Count;
    public Transform GetChild(int i)=>Children[i];
    public void SetParent(Transform value,bool worldPositionStays=false){parent?.Children.Remove(this);parent=value;parent?.Children.Add(this);}
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
public class GameObject : Object
{
    // Observe native construction for entrypoint tests; no entry/shell policy.
    public static Component LastAdded;
    readonly List<Component> components=new();
    public Transform transform;
    public int layer;public bool activeSelf=true,Destroyed;
    public bool activeInHierarchy=>activeSelf&&!Destroyed&&(transform.parent?.gameObject.activeInHierarchy??true);
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
    public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault();
    internal void CollectComponents<T>(bool includeInactive,List<T> into) where T:Component
    {
        if(!includeInactive&&!activeInHierarchy)return;
        foreach(var component in components)if(component is T value)into.Add(value);
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
    public float nearClipPlane,farClipPlane,depth;
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
    public int width,height;bool created=true;
    public bool IsCreated()=>created;public bool Create(){created=true;return true;}
}
public class FontAsset{public object material=new();}
public enum TextAlignmentOptions{Left,Center,TopLeft,Top,TopRight,Bottom}
[Flags]public enum FontStyles{Normal=0,Bold=1,UpperCase=2}
public class TextMeshProUGUI:Component
{
    public string text="";public float fontSize,fontSizeMin,fontSizeMax;public Color color;public FontStyles fontStyle;
    public bool enableWordWrapping,enableAutoSizing;public FontAsset font;public object fontSharedMaterial;
    public RectTransform rectTransform=>(RectTransform)transform;
    // A deliberately modeled text-measurement boundary, not native TMP proof.
    public Vector2 GetPreferredValues(string t)=>new((t?.Length??0)*fontSize*.5f,fontSize);
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
    public static T[] FindObjectsOfTypeAll<T>(){SsShellContracts.EngineBoundary.Discovery++;return Array.Empty<T>();}
}
public class DefaultExecutionOrder:Attribute{public DefaultExecutionOrder(int order){}}
public enum RuntimeInitializeLoadType{AfterSceneLoad}
public class RuntimeInitializeOnLoadMethod:Attribute{public RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType type){}}
