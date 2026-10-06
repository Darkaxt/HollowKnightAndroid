// Engine/data boundaries for unchanged extracted production bodies. Not Unity profiling.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

namespace UnityEngine
{
    class Object
    {
        public bool Retired;
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a, null) || a.Retired) ? ReferenceEquals(b, null) || b.Retired : ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
        public T[] GetComponentsInChildren<T>(bool include) where T : Component => gameObject.GetComponentsInChildren<T>(include);
    }
    class MonoBehaviour : Component { public bool enabled = true; }
    class Transform : Component
    {
        public Transform parent;
        public Vector3 localScale = Vector3.one, localPosition, position;
        public Vector3 lossyScale => localScale;
        public readonly List<Transform> Children = new();
        public Transform root { get { var t=this;while(t.parent!=null)t=t.parent;return t; } }
        public int childCount { get { ChildReads++;return Children.Count; } }
        public static int ChildReads;
        public void SetParent(Transform value)
        {
            var before=parent;before?.Children.Remove(this);parent=value;parent?.Children.Add(this);
            before?.gameObject.Notify("OnTransformChildrenChanged");parent?.gameObject.Notify("OnTransformChildrenChanged");
            gameObject.Notify("OnTransformParentChanged");
        }
        public Transform GetChild(int index) => Children[index];
    }
    class GameObject : Object
    {
        string label;
        public static int NameReads;
        public string name { get { NameReads++;return label; } set { label=value; } }
        public int layer;
        public bool activeInHierarchy = true;
        public readonly Transform transform;
        readonly List<Component> components = new();
        public static int RendererQueries, FsmQueries;
        public static readonly List<GameObject> Resident=new();
        public static int Finds;
        public GameObject(string name = "") { this.name = name; transform = new Transform { gameObject = this }; components.Add(transform);Resident.Add(this); }
        public void Notify(string message)
        {
            foreach(var c in components.ToArray())c.GetType().GetMethod(message,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)?.Invoke(c,null);
        }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; components.Add(c); return c; }
        public T GetComponent<T>() where T : Component { foreach (var c in components) if (c is T match && c != null) return match; return null; }
        public T[] GetComponents<T>() where T : Component { var values = new List<T>(); foreach (var c in components) if (c is T match && c != null) values.Add(match); return values.ToArray(); }
        public T[] GetComponentsInChildren<T>(bool include) where T : Component
        {
            if (typeof(T) == typeof(Renderer)) RendererQueries++;
            if (typeof(T) == typeof(PlayMakerFSM)) FsmQueries++;
            var values = new List<T>(); Gather(this, include, values); return values.ToArray();
        }
        static void Gather<T>(GameObject go, bool include, List<T> values) where T : Component
        {
            if (include || go.activeInHierarchy) values.AddRange(go.GetComponents<T>());
            foreach (var child in go.transform.Children) Gather(child.gameObject, include, values);
        }
        public void SetActive(bool active) { activeInHierarchy = active; }
        public static GameObject Find(string name) { Finds++;foreach(var go in Resident)if(go!=null&&go.activeInHierarchy&&go.name==name)return go;return null; }
    }
    struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1, 1, 1);
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b, a.y*b, a.z*b);
    }
    struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x=x; this.y=y; this.z=z; this.w=w; } }
    struct Color { public float a; }
    static class Mathf { public static float Abs(float value) => Math.Abs(value); public static float Clamp(float value,float low,float high) => Math.Clamp(value,low,high); }
    static class Time { public static int frameCount; public static float unscaledTime; }
    class MaterialPropertyBlock
    {
        public static int Created;
        public Vector4 Clip;
        public MaterialPropertyBlock() { Created++; }
        public void Clear() { Clip = default; }
        public void SetVector(int id, Vector4 value) { Clip = value; }
    }
    class Renderer : Component
    {
        public bool enabled = true;
        public Vector4 Clip;
        public void GetPropertyBlock(MaterialPropertyBlock block) { block.Clip = Clip; }
        public void SetPropertyBlock(MaterialPropertyBlock block) { Clip = block.Clip; }
    }
}
namespace TMProOld
{
    class TextMeshPro : MonoBehaviour
    {
        string value = "";
        Color ink = new() { a = 1 };
        public int Meshes;
        public bool FailGeneration;
        public bool havePropertiesChanged { get; set; }
        public string text { get => value; set { this.value = value; havePropertiesChanged = true; } }
        public Color color { get => ink; set { ink = value; } }
        public void ForceMeshUpdate() { if (FailGeneration) throw new InvalidOperationException("fixture generation fault"); Meshes++; havePropertiesChanged = false; }
    }
}
class PlayMakerFSM : MonoBehaviour
{
    public string FsmName = "Area Title Control", ActiveStateName = "Box Up";
    public FsmVariables FsmVariables = new();
}
class FsmVariables { public FsmBool GetFsmBool(string name) => new(); }
class FsmBool { public bool Value = true; }
class Config { public int debug, compHudSync = 1, routeHudFx=1; public float compDlgNameScale = 1,healOffX=2,healOffY=3,healScale=1; }
partial class HKDualScreen
{
    readonly Config cfg = new();
    const int tutLayer = 7, TMP_CLIP_RECT = 1;
    static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> _tmpProps = new();
    Transform dlgBoxT, dlgNameT;
    PlayMakerFSM dlgBoxFsm;
    bool loreDialogueOpen, dlgNameRouted;
    float dlgBoxDownT = -1, dlgNameHoldT = -1;
    int dlgNameOrigLayer = -1;
    Vector3 dlgNameOrigLocal;
    static GameObject Instantiate(GameObject source, Transform parent) => throw new InvalidOperationException("unused engine clone boundary");
    static void Destroy(GameObject go) { go.Retired = true; }
    void Dbg(string message) { }
    static void WarnOnce(string site, Exception error) { }
    readonly Dictionary<Transform,int> routedLayers=new();
    void ZeroNameCard() { }
    float NameCardAlpha() => 0;

    static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.parent = parent; parent.Children.Add(t); return t;
    }
    void Prepare()
    {
        Time.frameCount = 10; Time.unscaledTime = 1;
        var hud = new GameObject("hud").transform;
        var manager = Child(hud, "DialogueManager");
        dlgBoxT = Child(manager, "box"); dlgBoxFsm = dlgBoxT.gameObject.AddComponent<PlayMakerFSM>();
        dlgBoxFsm.FsmName = "Box Open";
        dlgNameT = Child(hud, "Title Small");
        dlgNameT.gameObject.AddComponent<PlayMakerFSM>();
        foreach (var part in NAME_PARTS) Child(dlgNameT, part).gameObject.AddComponent<TMProOld.TextMeshPro>().text = part;
        dlgNameClone = Child(dlgBoxT, "clone");
        dlgNameCloneTmps = new Component[NAME_PARTS.Length]; dlgNameCloneRs = new Renderer[NAME_PARTS.Length];
        for (int i = 0; i < NAME_PARTS.Length; i++)
        {
            var part = Child(dlgNameClone, NAME_PARTS[i]);
            dlgNameCloneTmps[i] = part.gameObject.AddComponent<TMProOld.TextMeshPro>();
            dlgNameCloneRs[i] = part.gameObject.AddComponent<Renderer>();
            dlgNameParts[i] = NAME_PARTS[i];
        }
    }
    int Meshes() { int n = 0; foreach (var c in dlgNameCloneTmps) if (c != null) n += ((TMProOld.TextMeshPro)c).Meshes; return n; }
    void Frames(bool show, int n) { for (int i = 0; i < n; i++) { Time.frameCount++; SetNameClone(show); } }
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    public static Dictionary<string, string> RunCases()
    {
        var results = new Dictionary<string, string>();
        void Run(string id, Action<HKDualScreen> test)
        {
            var h = new HKDualScreen(); h.Prepare();
            try { test(h); results[id] = "PASS"; }
            catch (Exception error) { results[id] = "FAIL: " + error.Message; }
        }
        Run("GP01_hidden_settled", h =>
        {
            h.Frames(false, 5); int mesh = h.Meshes(), queries = GameObject.RendererQueries, blocks = MaterialPropertyBlock.Created;
            h.Frames(false, 120);
            Check(h.Meshes() == mesh, "hidden clone keeps generating meshes");
            Check(GameObject.RendererQueries == queries, "hidden clone keeps discovering renderers");
            Check(MaterialPropertyBlock.Created == blocks, "hidden clone keeps allocating clipping scratch");
        });
        Run("GP01_visible_settled_and_content", h =>
        {
            h.Frames(true, 5); int mesh = h.Meshes(), queries = GameObject.RendererQueries;
            h.Frames(true, 120);
            Check(h.Meshes() == mesh && GameObject.RendererQueries == queries, "unchanged visible text repeats generation/discovery");
            h.dlgNameParts[0] = "different speaker"; h.Frames(true, 1);
            Check(h.Meshes() == mesh + 1, "changed part did not regenerate exactly once");
        });
        Run("GP01_dirty_and_failed_retry", h =>
        {
            h.Frames(true, 5); int mesh = h.Meshes();
            var text = (TMProOld.TextMeshPro)h.dlgNameCloneTmps[0];
            text.havePropertiesChanged = true; h.Frames(true, 1);
            Check(h.Meshes() == mesh + 1, "dirty text properties did not rearm");
            text.FailGeneration = true; h.dlgNameParts[0] = "new"; h.Frames(true, 1);
            text.FailGeneration = false; h.Frames(true, 1);
            Check(text.text == "new" && h.Meshes() == mesh + 2, "failed application did not retry");
        });
        Run("GP01_late_fallback_and_component", h =>
        {
            h.Frames(true, 1);
            var fallback = Child(h.dlgNameClone, "late TMP_SubMesh").gameObject.AddComponent<Renderer>();
            h.Frames(true, 1); Check(fallback.Clip.x == -32767 && fallback.Clip.z == 32767, "late fallback clip not sanitized");
            h.dlgNameCloneTmps[0].Retired = true;
            var replacement = h.dlgNameClone.Children[0].gameObject.AddComponent<TMProOld.TextMeshPro>();
            h.Frames(true, 1);
            Check(ReferenceEquals(h.dlgNameCloneTmps[0], replacement) && replacement.text == h.dlgNameParts[0] && replacement.Meshes == 1, "replacement component borrowed applied state or was not rebound");
        });
        Run("GP02_stable_native_card", h =>
        {
            h.RouteLoreDialogue(); h.NameCardText(); h.NpcTitleActive();
            var sources = h.dlgNameSrcTmps; int queries = GameObject.FsmQueries;
            for (int i = 0; i < 120; i++) { h.RouteLoreDialogue(); h.NameCardText(); h.NpcTitleActive(); }
            Check(ReferenceEquals(sources, h.dlgNameSrcTmps), "retained native source array invalidated each open frame");
            Check(GameObject.FsmQueries == queries, "retained native FSM rediscovered each open frame");
        });
        Run("GP02_retired_part_rebind", h =>
        {
            h.BindNameSources(); var retired = h.dlgNameSrcTmps[0]; retired.Retired = true;
            var replacement = retired.gameObject.AddComponent<TMProOld.TextMeshPro>(); replacement.text = "replacement";
            h.BindNameSources(); Check(ReferenceEquals(replacement, h.dlgNameSrcTmps[0]), "retired native TMP remained cached");
        });
        Run("GP01_later_fallback_structure", h =>
        {
            h.Frames(true, 10);
            var fallback = Child(h.dlgNameClone.Children[0], "later TMP_SubMesh").gameObject.AddComponent<Renderer>();
            int mesh = h.Meshes(); h.Frames(true, 1);
            Check(fallback.Clip.x == -32767 && fallback.Clip.z == 32767, "later fallback structural edge not sanitized");
            Check(h.Meshes() == mesh, "fallback structure unnecessarily regenerated text");
        });
        Run("GP01_clone_owner_replacement", h =>
        {
            h.Frames(true, 5); int mesh = h.Meshes();
            h.dlgNameClone = Child(h.dlgBoxT, "replacement clone"); h.Frames(true, 1);
            Check(h.Meshes() == mesh + NAME_PARTS.Length, "replacement clone borrowed prior applied state");
        });
        Run("GP02_native_title_text_settled", h =>
        {
            string first = h.NameCardText();
            for (int i = 0; i < 120; i++) Check(ReferenceEquals(first, h.NameCardText()), "unchanged native speaker title concatenated again");
            ((TMProOld.TextMeshPro)h.dlgNameSrcTmps[1]).text = "changed";
            Check(h.NameCardText().Contains("changed"), "changed native speaker part not sampled");
        });
        Run("GP02_missing_part_recovery", h =>
        {
            var missing = h.dlgNameT.Children[0].gameObject.GetComponent<TMProOld.TextMeshPro>(); missing.Retired = true;
            h.BindNameSources();
            var replacement = missing.gameObject.AddComponent<TMProOld.TextMeshPro>(); replacement.text = "late";
            Time.frameCount += 31; h.BindNameSources();
            Check(ReferenceEquals(replacement, h.dlgNameSrcTmps[0]), "initially missing native part did not recover");
        });
        Run("GP02_fsm_retirement", h =>
        {
            h.NpcTitleActive(); h.dlgTitleFsm.Retired = true;
            var replacement = h.dlgNameT.gameObject.AddComponent<PlayMakerFSM>(); h.NpcTitleActive();
            Check(ReferenceEquals(replacement, h.dlgTitleFsm), "retired native FSM did not recover");
        });
        Run("GP02_native_card_replacement", h =>
        {
            h.BindNameSources(); var old = h.dlgNameSrcTmps;
            h.dlgNameT = Child(h.dlgNameT.parent, "replacement card");
            foreach (var name in NAME_PARTS) Child(h.dlgNameT, name).gameObject.AddComponent<TMProOld.TextMeshPro>().text = "new owner";
            h.BindNameSources();
            Check(!ReferenceEquals(old, h.dlgNameSrcTmps) && h.NameCardText().Contains("new owner"), "replacement native card borrowed prior parts");
        });
        return results;
    }
}
class Program
{
    static int Main()
    {
        var results = HKDualScreen.RunCases();
        foreach(var result in HKDualScreen.RunDiscoveryCases())results.Add(result.Key,result.Value);
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var outcome in results.Values) if (outcome != "PASS") return 1;
        return 0;
    }
}
