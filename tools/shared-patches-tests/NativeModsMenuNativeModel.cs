// Generator template, excluded from direct compilation. Instantiated once per profile.
// Managed boundaries only; never a native Unity scheduling/rendering claim.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using DualSouls.Mods.Silksong;

namespace NativeMenuFixture.PROFILE.Engine
{
    public static class World
    {
        public static readonly System.Collections.Generic.List<GameObject> Objects = new System.Collections.Generic.List<GameObject>();
        public static readonly System.Collections.Generic.List<Object> DestroyQueue = new System.Collections.Generic.List<Object>();
        public static readonly System.Collections.Generic.List<string> Points = new System.Collections.Generic.List<string>();
        public static readonly System.Collections.Generic.List<string> Logs = new System.Collections.Generic.List<string>();
        public static readonly System.Collections.Generic.List<Coroutine> Coroutines = new System.Collections.Generic.List<Coroutine>();
        public static readonly System.Collections.Generic.List<string> BridgeCalls = new System.Collections.Generic.List<string>();
        public static int FailAt = -1;
        public static string FailOnPoint;
        public static readonly System.Collections.Generic.List<string> FiredFailures = new System.Collections.Generic.List<string>();
        public static bool Instantiating;
        public static string CloneWithoutComponent;
        public static string CloneWithoutText;
        public static int NativeGameActions, ForeignCancelActions, ForeignSubmitActions;
        public static int AudioSubmit, AudioSelect, CursorSelect, BenchWrites;
        public static bool OpenLowerAvailable = true, LowerSuppressed;
        public static int LowerOpened;
        public static void Hit(string point)
        {
            if (Instantiating) return;
            Points.Add(point);
            if (Points.Count != FailAt && point != FailOnPoint) return;
            FailAt = -1;
            FailOnPoint = null;
            FiredFailures.Add(point);
            throw new InvalidOperationException("injected engine boundary: " + point);
        }
        public static void Reset()
        {
            Objects.Clear(); DestroyQueue.Clear(); Points.Clear(); Logs.Clear(); Coroutines.Clear(); BridgeCalls.Clear();
            FailAt = -1; FailOnPoint = null; FiredFailures.Clear();
            Instantiating = false; CloneWithoutComponent = CloneWithoutText = null;
            NativeGameActions = ForeignCancelActions = ForeignSubmitActions = 0;
            AudioSubmit = AudioSelect = CursorSelect = BenchWrites = LowerOpened = 0;
            OpenLowerAvailable = true; LowerSuppressed = false; Time.unscaledTime = 0;
            GameManager.UnsafeInstance = new GameManager { playerData = new PlayerData(), hero_ctrl = new HeroController() };
            HeroController.UnsafeInstance = GameManager.UnsafeInstance.hero_ctrl;
            EventSystems.EventSystem.current = new EventSystems.EventSystem();
            HollowKnightModsRuntime.Current = null; SilksongModsRuntime.Current = null;
            JsonUtility.Value = null; AndroidJavaClass.Snapshot = null;
        }
        public static void Invoke(object receiver, string method)
        {
            MethodInfo m = receiver.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return;
            try { m.Invoke(receiver, null); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }
        public static void Pump(int maximum = 100)
        {
            for (int tick = 0; tick < maximum && Coroutines.Any(c => !c.Done); tick++)
                foreach (Coroutine c in Coroutines.ToArray()) c.Step();
        }
        public static void FlushDestroy()
        {
            foreach (Object obj in DestroyQueue.ToArray()) DestroyNow(obj);
            DestroyQueue.Clear();
        }
        static void DestroyNow(Object obj)
        {
            if (obj == null || obj.Dead) return;
            if (obj is GameObject go)
            {
                go.SetActive(false);
                foreach (Transform child in go.transform.Children.ToArray()) DestroyNow(child.gameObject);
                foreach (Component c in go.Components.ToArray()) { Invoke(c, "OnDestroy"); c.Dead = true; }
                go.transform.SetParent(null, false); go.Dead = true;
            }
            else if (obj is Component c) { Invoke(c, "OnDestroy"); c.NativeGameObject.Components.Remove(c); c.Dead = true; }
        }
        public static void Submit(GameObject go)
        {
            foreach (Component c in go.Components.ToArray())
                if (c is EventSystems.ISubmitHandler h && (!(c is Behaviour b) || b.isActiveAndEnabled))
                    h.OnSubmit(new EventSystems.BaseEventData());
        }
        public static void Cancel(GameObject go)
        {
            foreach (Component c in go.Components.ToArray())
                if (c is EventSystems.ICancelHandler h && (!(c is Behaviour b) || b.isActiveAndEnabled))
                    h.OnCancel(new EventSystems.BaseEventData());
        }
    }
    // Managed collection boundaries allow before/after ownership/row/value failures
    // while preserving all production declaration tokens and orchestration.
    public class List<T> : System.Collections.Generic.List<T>
    {
        public List() { }
        public List(int capacity) : base(capacity) { }
        public new void Add(T value) { World.Hit("List.Add.before:" + typeof(T).Name); base.Add(value); World.Hit("List.Add.after:" + typeof(T).Name); }
        public new void AddRange(IEnumerable<T> values) { World.Hit("List.AddRange.before:" + typeof(T).Name); base.AddRange(values); World.Hit("List.AddRange.after:" + typeof(T).Name); }
    }
    public class Object
    {
        public bool Dead;
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null) && !o.Dead;
        public static bool operator ==(Object a, Object b) => (ReferenceEquals(a, null) || a.Dead) ? (ReferenceEquals(b, null) || b.Dead) : ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object o) { if (o != null) World.DestroyQueue.Add(o); }
        public static GameObject Instantiate(GameObject source, Transform parent, bool worldPositionStays)
        {
            World.Hit("Instantiate.before:" + source.name);
            World.Instantiating = true;
            var map = new Dictionary<Object, Object>();
            GameObject Clone(GameObject original, Transform targetParent)
            {
                var copy = new GameObject(original.name + "(Clone)");
                copy._active = original.activeSelf; copy.transform.SetParent(targetParent, false);
                map.Add(original, copy); map.Add(original.transform, copy.transform);
                var sr = original.transform as RectTransform; var dr = copy.transform as RectTransform;
                dr.anchoredPosition = sr.anchoredPosition; dr.sizeDelta = sr.sizeDelta;
                foreach (Component c in original.Components.Where(x => !(x is Transform)))
                {
                    if (c.GetType().Name == World.CloneWithoutComponent) continue;
                    if (c is UI.Text && original.name == World.CloneWithoutText) continue;
                    Component nc = (Component)Activator.CreateInstance(c.GetType()); nc.NativeGameObject = copy;
                    copy.Components.Add(nc); map.Add(c, nc);
                }
                foreach (Transform child in original.transform.Children) Clone(child.gameObject, copy.transform);
                return copy;
            }
            GameObject root = Clone(source, parent);
            foreach (var item in map)
                if (item.Key is GameObject original && item.Value is GameObject copy && !ReferenceEquals(copy, root)) copy.name = original.name;
            foreach (var item in map)
            {
                if (!(item.Key is Component from) || from is Transform) continue;
                Component to = (Component)item.Value;
                for (Type type = from.GetType(); type != typeof(Component) && type != null; type = type.BaseType)
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (field.IsInitOnly) continue;
                        object value = field.GetValue(from);
                        if (value is Object old && map.TryGetValue(old, out Object mapped)) value = mapped;
                        else if (value is Events.UnityEvent evt) value = evt.Copy();
                        else if (value is System.Collections.Generic.List<EventSystems.EventTrigger.Entry> entries) value = entries.Select(e => e.Copy()).ToList();
                        field.SetValue(to, value);
                    }
            }
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                World.Invoke(c, "Awake");
                if (c is Behaviour b && b.isActiveAndEnabled) { World.Invoke(c, "OnEnable"); World.Invoke(c, "Start"); }
            }
            World.Instantiating = false;
            World.Hit("Instantiate.after:" + source.name);
            return root;
        }
    }
    public class GameObject : Object
    {
        string _name;
        public string name { get => _name; set { World.Hit("name:" + value); _name = value; } }
        public bool _active = true;
        public bool activeSelf => _active;
        public bool activeInHierarchy => !Dead && _active && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public readonly System.Collections.Generic.List<Component> Components = new System.Collections.Generic.List<Component>();
        public Transform transform { get; }
        public Scene scene = new Scene();
        public GameObject(string name = "", params Type[] components)
        {
            _name = name; transform = new RectTransform { NativeGameObject = this }; Components.Add(transform); World.Objects.Add(this);
            foreach (Type t in components) AddComponent(t);
        }
        public void SetActive(bool active)
        {
            World.Hit("SetActive:" + name + ":" + active);
            bool before = activeInHierarchy; _active = active;
            if (before != activeInHierarchy)
                foreach (Behaviour b in GetComponentsInChildren<Behaviour>(true))
                    if (b.enabled) { World.Invoke(b, activeInHierarchy ? "OnEnable" : "OnDisable"); if (activeInHierarchy) World.Invoke(b, "Start"); }
        }
        public Component AddComponent(Type type)
        {
            World.Hit("AddComponent.before:" + type.Name);
            var component = (Component)Activator.CreateInstance(type); component.NativeGameObject = this; Components.Add(component);
            World.Invoke(component, "Awake");
            if (component is Behaviour b && b.isActiveAndEnabled) { World.Invoke(component, "OnEnable"); World.Invoke(component, "Start"); }
            World.Hit("AddComponent.after:" + type.Name);
            return component;
        }
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public T GetComponent<T>() where T : class { World.Hit("GetComponent:" + typeof(T).Name); return Components.OfType<T>().FirstOrDefault(c => !(c is Object o) || !o.Dead); }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        {
            World.Hit("GetComponentsInChildren:" + typeof(T).Name);
            var values = new System.Collections.Generic.List<T>();
            void Visit(GameObject g) { if (g.Dead || (!includeInactive && !g.activeInHierarchy)) return; values.AddRange(g.Components.OfType<T>().Where(c => !(c is Object o) || !o.Dead)); foreach (var child in g.transform.Children) Visit(child.gameObject); }
            Visit(this); return values.ToArray();
        }
    }
    public class Scene { public bool isLoaded = true; public bool Valid = true; public bool IsValid() => Valid; }
    public class MissingReferenceException : InvalidOperationException
    {
        public MissingReferenceException(string message) : base(message) { }
    }
    public class Component : Object
    {
        // Native ownership is engine-internal; public Component access fails after
        // native destruction even while this managed wrapper is still referenced.
        internal GameObject NativeGameObject;
        public GameObject gameObject => !Dead ? NativeGameObject
            : throw new MissingReferenceException("Component.gameObject: native " + GetType().Name + " has been destroyed.");
        public Transform transform => gameObject.transform;
        public string name { get => gameObject.name; set => gameObject.name = value; }
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentInChildren<T>(includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
        public T GetComponentInParent<T>() where T : class { for (Transform t = transform; t != null; t = t.parent) { T c = t.GetComponent<T>(); if (c != null) return c; } return null; }
    }
    public class Transform : Component
    {
        public readonly System.Collections.Generic.List<Transform> Children = new System.Collections.Generic.List<Transform>();
        Transform _parent;
        public Transform parent { get => _parent; set => SetParent(value, false); }
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public bool IsChildOf(Transform ancestor) { for (Transform t = this; t != null; t = t.parent) if (ReferenceEquals(t, ancestor)) return true; return false; }
        public void SetParent(Transform parent, bool worldPositionStays = false)
        {
            _parent?.Children.Remove(this); _parent = parent; parent?.Children.Add(this);
        }
        public Transform Find(string path)
        {
            World.Hit("Find:" + path); Transform current = this;
            foreach (string part in path.Split('/')) { current = current.Children.FirstOrDefault(c => c.name == part && !c.Dead); if (current == null) return null; }
            return current;
        }
    }
    public class RectTransform : Transform { public Vector2 anchoredPosition; public Vector2 sizeDelta = new Vector2(1000, 70); }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public class Behaviour : Component
    {
        bool _enabled = true;
        public bool enabled { get => _enabled; set { World.Hit("enabled:" + GetType().Name + ":" + value); bool before = isActiveAndEnabled; _enabled = value; if (before != isActiveAndEnabled) World.Invoke(this, value ? "OnEnable" : "OnDisable"); } }
        public bool isActiveAndEnabled => !Dead && enabled && gameObject != null && gameObject.activeInHierarchy;
    }
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator iterator) { World.Hit("StartCoroutine"); var c = new Coroutine(iterator); World.Coroutines.Add(c); c.Step(); return c; }
        public void StopCoroutine(Coroutine c) { c.Done = true; }
        public void StopAllCoroutines() { foreach (var c in World.Coroutines) c.Done = true; }
    }
    public class Coroutine
    {
        readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>(); public bool Done;
        public Coroutine(IEnumerator iterator) { _stack.Push(iterator); }
        public void Step()
        {
            if (Done) return;
            while (_stack.Count > 0)
            {
                IEnumerator next = _stack.Peek(); if (!next.MoveNext()) { _stack.Pop(); continue; }
                if (next.Current is IEnumerator child) { _stack.Push(child); continue; }
                return;
            }
            Done = true;
        }
    }
    public class Animator : Behaviour { }
    public class CanvasGroup : Behaviour { }
    public class Canvas : Behaviour { }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() where T : class => World.Objects.Where(o => !o.Dead).SelectMany(o => o.Components.OfType<T>()).ToArray(); }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value)); }
    public static class Debug { public static void Log(string message) { World.Hit("admission:" + message); World.Logs.Add(message); } public static void LogError(string message) => World.Logs.Add(message); public static void LogWarning(string message) => World.Logs.Add(message); }
    public static class Application { public static string persistentDataPath = "D:/Temp/native-mods-menu-build-01/no-sidecar"; }
    public static class JsonUtility
    {
        public static object Value;
        public static T FromJson<T>(string text) { if (Value is T result) return result; return System.Text.Json.JsonSerializer.Deserialize<T>(text, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }); }
        public static string ToJson(object value) { World.BenchWrites++; return System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }); }
    }
    public class AndroidJavaClass : IDisposable
    {
        public static object Snapshot; public AndroidJavaClass(string name) { }
        public T CallStatic<T>(string method, params object[] args) { World.BridgeCalls.Add(method); return Snapshot is T value ? value : default; }
        public T GetStatic<T>(string name) => default;
        public void Dispose() { }
    }
    public class AndroidJavaObject : IDisposable
    {
        public AndroidJavaObject(string name, params object[] args) { }
        public T Call<T>(string method, params object[] args) => default;
        public void Call(string method, params object[] args) { }
        public void Dispose() { }
    }
    public enum KeyCode { Joystick1Button0 = 330 }
    public static class Input { public static string[] GetJoystickNames() => Array.Empty<string>(); }
    public static class NativeSkinRuntime { public static int SkinStamp => 0; }
    public class HKLayout { }
    public static class HKDualScreen
    {
        public static void OpenBenchTeleportRoute() { World.LowerOpened++; if (!World.OpenLowerAvailable) throw new InvalidOperationException("lower companion absent"); }
    }
    public class HollowKnightModsRuntime
    {
        public static HollowKnightModsRuntime Current; public HollowKnightModsSession Session;
        public void InvalidateSkinLibrary() { }
    }
    public class SilksongModsRuntime
    {
        public static SilksongModsRuntime Current; public TweakSession Session;
        public void InvalidateSkinLibrary() { }
    }
    public class MenuScreen : MonoBehaviour
    {
        // HK_MENU_GROUPS
        // SS_BACK_BUTTON
        public UI.Selectable defaultHighlight;
    }
    public class MenuButtonList : MonoBehaviour { }
    public class UIManager : MonoBehaviour
    {
        public static UIManager instance;
        public Canvas UICanvas; public MenuScreen optionsMenuScreen;
        public GlobalEnums.UIState uiState = GlobalEnums.UIState.PAUSED;
        public IEnumerator HideMenu(MenuScreen menu) { World.Hit("HideMenu"); yield return null; if (menu != null) menu.gameObject.SetActive(false); }
        public IEnumerator ShowMenu(MenuScreen menu) { World.Hit("ShowMenu"); if (menu != null) menu.gameObject.SetActive(true); yield return null; menu?.defaultHighlight?.Select(); }
        public void UIGoToGameOptionsMenu() { World.NativeGameActions++; }
        public void UILeaveOptionsMenu() { World.ForeignCancelActions++; }
    }
    public class PlayerData
    {
        public bool atBench, isInvincible; public string respawnScene, respawnMarkerName;
        public int respawnType, health = 5, nailDamage = 5, nailSmithUpgrades, MPCharge;
        public bool respawnFacingRight;
        public int RespawnCalls; public bool ThrowRespawn; public object[] LastRespawn;
        public Action BeforeRespawn;
        public void SetBenchRespawn(string marker, string scene, int type, bool facingRight)
        { RespawnCalls++; LastRespawn = new object[] { marker, scene, type, facingRight }; BeforeRespawn?.Invoke(); if (ThrowRespawn) throw new InvalidOperationException("typed respawn failed"); }
        public bool GetBool(string field) => false;
    }
    public class GameManager
    {
        public static GameManager UnsafeInstance; public PlayerData playerData; public HeroController hero_ctrl;
        public int ReadyCalls; public bool ThrowReady; public bool? LastReady;
        public void ReadyForRespawn(bool fromDeath) { ReadyCalls++; LastReady = fromDeath; if (ThrowReady) throw new InvalidOperationException("typed ReadyForRespawn failed"); }
    }
    public class HeroController
    {
        public static HeroController UnsafeInstance;
        public float RUN_SPEED = 10, WALK_SPEED = 5;
        public void AddMPCharge(int amount) { }
        public void AddHealth(int amount) { }
    }
    public class HealthManager { public bool isDead; public int hp; }
    public struct HitInstance { public int DamageDealt; }
    public static class PlayMakerFSM { public static void BroadcastEvent(string name) { } }
    public static class HollowKnightGameplayFeatures
    {
        public static void RestoreAll() { }
        public static void SetStateTransferPreparation(Action action) { }
        public static void SaveState(int slot) { }
        public static void LoadState(int slot, Action<string> completed) { }
        public static void DeleteState(int slot) { }
        public static bool StateExists(int slot) => false;
        public static void Tick(params object[] flags) { }
        public static void RestorePlayerOwnedForStateTransfer() { }
        public static void BeforeAuthoritativeMapBoolSet(PlayerData player, string name, bool value) { }
        public static void BeginAuthoritativeMapUpdate(PlayerData player) { }
        public static void EndAuthoritativeMapUpdate(PlayerData player) { }
    }
    public static class SilksongGameplayFeatures
    {
        public class BenchRecord { public string scene; }
        public static readonly System.Collections.Generic.List<BenchRecord> Records = new System.Collections.Generic.List<BenchRecord>();
        public static IEnumerable<BenchRecord> BenchDestinations() => Records;
        public static void WarpToBench(string scene) { }
    }
    public enum TextAnchor { MiddleLeft, MiddleRight, MiddleCenter }
    public enum HorizontalWrapMode { Overflow, Wrap }
    public enum VerticalWrapMode { Overflow, Truncate }
}
namespace NativeMenuFixture.PROFILE.Engine.GlobalEnums
{
    public enum UIState { PAUSED, PLAYING, MAIN_MENU }
    public enum CancelAction { DoNothing, LeaveOptionsMenu, GoToPauseMenu }
}
namespace NativeMenuFixture.PROFILE.Engine.Events
{
    public class UnityEvent
    {
        public sealed class Listener { public Object Target; public string Method; public Action Action; }
        public readonly System.Collections.Generic.List<Listener> Listeners = new System.Collections.Generic.List<Listener>();
        public int GetPersistentEventCount() => Listeners.Count;
        public Object GetPersistentTarget(int i) => Listeners[i].Target;
        public string GetPersistentMethodName(int i) => Listeners[i].Method;
        public void AddListener(Action action) => Listeners.Add(new Listener { Action = action });
        public void RemoveAllListeners() { Listeners.Clear(); }
        public void Invoke() { foreach (var l in Listeners.ToArray()) { if (l.Action != null) l.Action(); else World.Invoke(l.Target, l.Method); } }
        public UnityEvent Copy() { var copy = new UnityEvent(); copy.Listeners.AddRange(Listeners); return copy; }
    }
}
namespace NativeMenuFixture.PROFILE.Engine.EventSystems
{
    public class BaseEventData { public void Use() { } }
    public class PointerEventData : BaseEventData { }
    public class AxisEventData : BaseEventData { public MoveDirection moveDir; }
    public enum MoveDirection { None, Left, Right, Up, Down }
    public interface ISubmitHandler { void OnSubmit(BaseEventData e); }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData e); }
    public interface IMoveHandler { void OnMove(AxisEventData e); }
    public interface ICancelHandler { void OnCancel(BaseEventData e); }
    public interface ISelectHandler { void OnSelect(BaseEventData e); }
    public enum EventTriggerType { Submit, Cancel, PointerClick }
    public class EventTrigger : Behaviour, ISubmitHandler, ICancelHandler
    {
        public class Entry
        {
            public EventTriggerType eventID; public Events.UnityEvent callback = new Events.UnityEvent();
            public Entry Copy() => new Entry { eventID = eventID, callback = callback.Copy() };
        }
        public System.Collections.Generic.List<Entry> triggers = new System.Collections.Generic.List<Entry>();
        public void OnSubmit(BaseEventData e) { foreach (Entry t in triggers.Where(t => t.eventID == EventTriggerType.Submit)) t.callback.Invoke(); }
        public void OnCancel(BaseEventData e) { foreach (Entry t in triggers.Where(t => t.eventID == EventTriggerType.Cancel)) t.callback.Invoke(); }
    }
    public class EventSystem
    {
        public static EventSystem current; public GameObject currentSelectedGameObject;
        public void SetSelectedGameObject(GameObject go)
        {
            currentSelectedGameObject = go;
            if (go == null) return;
            foreach (Component c in go.Components.ToArray())
                if (c is ISelectHandler s && (!(c is Behaviour b) || b.isActiveAndEnabled)) s.OnSelect(new BaseEventData());
        }
    }
}
namespace NativeMenuFixture.PROFILE.Engine.UI
{
    public struct Navigation
    {
        public enum Mode { None, Explicit }
        public Mode mode; public Selectable selectOnUp, selectOnDown, selectOnLeft, selectOnRight;
    }
    public class Selectable : Behaviour
    {
        public bool interactable = true;
        Navigation _navigation;
        public Navigation navigation { get => _navigation; set { World.Hit("navigation:" + name); _navigation = value; } }
        public void Select() { if (isActiveAndEnabled && interactable) EventSystems.EventSystem.current.SetSelectedGameObject(gameObject); }
    }
    public class MenuSelectable : Selectable, EventSystems.ICancelHandler, EventSystems.ISelectHandler
    {
        public GlobalEnums.CancelAction cancelAction;
        bool _hasCancel;
        protected void OnEnable() { var trigger = GetComponent<EventSystems.EventTrigger>(); _hasCancel = trigger != null && trigger.triggers.Any(t => t.eventID == EventSystems.EventTriggerType.Cancel); }
        public void OnCancel(EventSystems.BaseEventData e)
        {
            if (cancelAction != GlobalEnums.CancelAction.DoNothing) World.ForeignCancelActions++;
            if (cancelAction != GlobalEnums.CancelAction.DoNothing || _hasCancel) ForceDeselect();
        }
        public void OnSelect(EventSystems.BaseEventData e) { World.CursorSelect++; World.AudioSelect++; }
        public void ForceDeselect() { if (ReferenceEquals(EventSystems.EventSystem.current.currentSelectedGameObject, gameObject)) EventSystems.EventSystem.current.SetSelectedGameObject(null); }
    }
    public class MenuButton : MenuSelectable, EventSystems.ISubmitHandler, EventSystems.IPointerClickHandler
    {
        public enum MenuButtonType { Proceed, Activate }
        public MenuButtonType buttonType;
        // SS_SUBMIT_FIELD
        public void OnSubmit(EventSystems.BaseEventData e)
        {
            if (buttonType == MenuButtonType.Proceed) ForceDeselect();
            World.AudioSubmit++;
            // SS_SUBMIT_INVOKE
        }
        public void OnPointerClick(EventSystems.PointerEventData e) => OnSubmit(e);
    }
    public class Graphic : Behaviour { }
    public class WrongText : Graphic { public string text { get; set; } }
    public class Text : Graphic
    {
        string _text = "";
        public string text { get => _text; set { World.Hit("text:" + value); _text = value; } }
        public int fontSize, resizeTextMinSize, resizeTextMaxSize;
        public bool resizeTextForBestFit;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;
        public TextAnchor alignment;
    }
}
