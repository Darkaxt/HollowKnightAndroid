using System;
using System.Collections.Generic;
using System.Linq;

// Typed engine boundaries only. No skin binding, admission, mapping or session decisions.
namespace SkinUnityNativeModel
{
    public class Object
    {
        internal static readonly List<Object> All = new List<Object>();
        static readonly List<Object> pending = new List<Object>();
        public string name;
        public bool Destroyed { get; private set; }
        public bool DestroyRequested => pending.Contains(this);
        public Object() { All.Add(this); }
        public static bool operator ==(Object a, Object b) =>
            ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value) { if (value != null && !pending.Contains(value)) pending.Add(value); }
        public static void FlushDestroy() { foreach (var value in pending) value.Destroyed = true; pending.Clear(); }
        public static void Reset() { pending.Clear(); All.Clear(); }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public sealed class GameObject : Object
    {
        readonly List<Component> components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; }
        public T Add<T>() where T : Component, new() { var value = new T { gameObject = this }; components.Add(value); return value; }
        public T GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault(x => x != null);
        public T[] GetComponents<T>() where T : Component => components.OfType<T>().Where(x => x != null).ToArray();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            (includeInactive || activeSelf ? GetComponents<T>() : Array.Empty<T>()).Concat(
                transform.Children.Where(x => x != null).SelectMany(x => x.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        public void SetActive(bool value) { activeSelf = value; }
    }
    public sealed class Transform : Component
    {
        public readonly List<Transform> Children = new List<Transform>();
        public Transform parent;
        public new string name => gameObject.name;
        public Transform root => parent == null ? this : parent.root;
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public void SetParent(Transform value) { parent?.Children.Remove(this); parent = value; value?.Children.Add(this); }
    }
    public class Texture : Object { public int width, height; }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Clamp }
    public enum FilterMode { Bilinear }
    public enum RenderTextureFormat { ARGB32 }
    public enum SpriteMeshType { FullRect }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Rect { public Rect(float x, float y, float w, float h) { } }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public class Texture2D : Texture
    {
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
        public Texture2D(int width, int height, TextureFormat format, bool mipmaps) { this.width = width; this.height = height; }
        public Color GetPixelBilinear(float x, float y) => default;
        public Color GetPixel(int x, int y) => default;
        public void SetPixel(int x, int y, Color color) { }
        public void ReadPixels(Rect source, int x, int y) { }
        public void Apply(bool mipmaps) { }
    }
    public class RenderTexture : Texture
    {
        public static RenderTexture active;
        public static int TemporaryCount;
        public static RenderTexture GetTemporary(int width, int height, int depth, RenderTextureFormat format)
        { TemporaryCount++; return new RenderTexture { width = width, height = height }; }
        public static void ReleaseTemporary(RenderTexture value) { TemporaryCount--; Destroy(value); }
    }
    public static class Graphics
    {
        public static bool FailBlit;
        public static void Blit(Texture source, RenderTexture target) { if (FailBlit) throw new InvalidOperationException("modeled readback failure"); }
        public static bool ConvertTexture(Texture source, Texture target) => true;
    }
    public static class ImageConversion
    {
        public static bool FailDecode;
        public static int Decodes;
        public static bool LoadImage(Texture2D texture, byte[] bytes, bool nonReadable) { Decodes++; return !FailDecode; }
    }
    public class Material : Object
    {
        Texture texture;
        public bool FailNextWrite;
        public Texture mainTexture
        {
            get => texture;
            set { texture = value; if (FailNextWrite) { FailNextWrite = false; throw new InvalidOperationException("modeled setter mutated then failed"); } }
        }
        public Material() { }
        public Material(Material original) { texture = original.mainTexture; }
    }
    public class Renderer : Component
    {
        Material material;
        public bool FailNextWrite;
        public Material sharedMaterial
        {
            get => material;
            set { material = value; if (FailNextWrite) { FailNextWrite = false; throw new InvalidOperationException("modeled renderer setter failure"); } }
        }
    }
    public class MeshRenderer : Renderer { }
    public class Sprite : Object
    {
        public Texture2D texture;
        public float pixelsPerUnit;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float ppu, uint extrude, SpriteMeshType type) =>
            new Sprite { texture = texture, pixelsPerUnit = ppu };
    }
    public class SpriteRenderer : Renderer { public Sprite sprite; }
    public class Camera : Component { }
    public class tk2dSpriteDefinition { public string name; public Vector2[] uvs; }
    public class tk2dSpriteCollectionData : Component
    {
        public string spriteCollectionName;
        public Material[] materials, materialInsts;
        public Material material;
        public tk2dSpriteDefinition[] spriteDefinitions;
        public tk2dSpriteCollectionData inst => this;
    }
    public class tk2dSprite : Component { public tk2dSpriteCollectionData Collection; }
    public class tk2dSpriteAnimator : Component { public tk2dSpriteAnimation Library; }
    public class tk2dSpriteAnimation : Object { public Clip[] clips; }
    public class Clip { public Frame[] frames; }
    public class Frame { public tk2dSpriteCollectionData spriteCollection; }
    public class HeroAnimationController : Component
    {
        tk2dSpriteAnimation windyAnimLib;
        HeroControllerConfig config;
        public void SetLibraries(tk2dSpriteAnimation library, HeroControllerConfig value) { windyAnimLib = library; config = value; }
    }
    public class HeroControllerConfig : Object { tk2dSpriteAnimation heroAnimOverrideLib; }
    public class HeroController : Component
    {
        public static HeroController instance;
        public static HeroController UnsafeInstance => instance;
        public static HeroController SilentInstance => instance;
    }
    public class GameManager : Object
    {
        public static GameManager instance;
        public static GameManager UnsafeInstance => instance;
    }
    public class GameCameras : Component
    {
        public static GameCameras instance;
        public static GameCameras SilentInstance => instance;
        public GameObject hudCanvas;
        public Camera hudCamera;
        public PlayMakerFSM hudCanvasSlideOut;
        public SilkSpool silkSpool;
    }
    public class HUDCamera : Component { public GameObject GameplayChild; }
    public class HudCanvas : Component { }
    public class SilkSpool : Component { }
    public class CharmIconList : Component { public static CharmIconList Instance; public Sprite[] spriteList; }
    public class CharmDisplay : Component { }
    public class InvNailSprite : Component
    {
        public Sprite level1, level2, level3, level4, level5;
        SpriteRenderer spriteRenderer;
    }
    public class InvItemDisplay : Component { public Sprite activeSprite, inactiveSprite; SpriteRenderer spriteRenderer; }
    public class PlayMakerFSM : Component { public string FsmName; public Fsm Fsm; }
    public class Fsm { public bool Initialized; public FsmState[] States; }
    public class FsmState { public string Name; public bool ActionsLoaded; public object[] Actions; }
    namespace HutongGames.PlayMaker { public class FsmObject { public Object Value; } }
    public static class Resources
    {
        public static int Scans;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { Scans++; return Object.All.OfType<T>().Where(x => x != null).ToArray(); }
    }
    public static class Time { public static float unscaledTime; }
    public static class Debug { public static readonly List<string> Warnings = new List<string>(); public static void LogWarning(string value) => Warnings.Add(value); }
    namespace SceneManagement
    {
        public struct Scene { }
        public enum LoadSceneMode { Single }
        public static class SceneManager
        {
            public static event Action<Scene, LoadSceneMode> sceneLoaded;
            public static event Action<Scene> sceneUnloaded;
            public static int Subscribers => (sceneLoaded?.GetInvocationList().Length ?? 0) + (sceneUnloaded?.GetInvocationList().Length ?? 0);
            public static void Load() => sceneLoaded?.Invoke(default, LoadSceneMode.Single);
            public static void Unload() => sceneUnloaded?.Invoke(default);
        }
    }
}
