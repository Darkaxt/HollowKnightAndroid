// Map render transaction adapted from Bottom.Map (MIT),
// igawa6/dualsouls 5c22451435b772acde0c7e6456f9019bc1baef73.
using System;
using System.Collections.Generic;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Reflection;
using Gameplay = GlobalSettings.Gameplay;
using TeamCherry.SharedUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#endif

// Exact-instance rollback entries are registered before each presentation write.
// Successful entries are retired individually, so a later retry cannot overwrite
// a field already returned to the native producer. Resources outlive all writes.
public sealed class DsPortMapRestoreQueue
{
    readonly List<Action> _restore = new List<Action>(), _release = new List<Action>();
    bool _restoring;
    public void Own(Action release) => _release.Add(release ?? throw new ArgumentNullException(nameof(release)));
    public void Change(Action restore, Action write)
    {
        if (_restoring) throw new InvalidOperationException("Map restoration-only ownership");
        _restore.Add(restore ?? throw new ArgumentNullException(nameof(restore)));
        write();
    }
    public void Restore()
    {
        _restoring = true;
        Drain(_restore);
        Drain(_release);
    }
    static void Drain(List<Action> actions)
    {
        var errors = new List<Exception>();
        for (int i = 0; i < actions.Count; i++)
        {
            if (actions[i] == null) continue;
            try { actions[i](); actions[i] = null; }
            catch (Exception e) { errors.Add(e); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        actions.Clear();
    }
}

// A bounded observation retry, not an initializer: readiness remains native.
public sealed class DsPortMapResidency
{
    object _owner; string _scene; long _epoch; double _next;
    bool _waiting;
    public bool Probe(object owner, string scene, long epoch, double now)
    {
        bool changed = !ReferenceEquals(owner, _owner) || scene != _scene || epoch != _epoch;
        if (!changed && _waiting && now < _next) return false;
        _owner = owner; _scene = scene; _epoch = epoch; _next = now + .5;
        return true;
    }
    public void Wait() => _waiting = true;
    public void Ready() => _waiting = false;
}

public sealed class DsPortMapFreshness
{
    readonly Func<IEnumerable<object>> _read;
    public DsPortPageSnapshot Captured { get; }
    public DsPortMapFreshness(Func<IEnumerable<object>> read)
    { _read = read; Captured = new DsPortPageSnapshot(read()); }
    public DsPortMapFreshness(IEnumerable<object> values)
    { Captured = new DsPortPageSnapshot(values); }
    public bool Current() => _read != null && Same(_read());
    public bool Same(IEnumerable<object> values) => Captured.Same(new DsPortPageSnapshot(values));
}

public readonly struct DsPortMapAuthorityToken
{
    readonly object _owner, _map, _host, _camera, _viewport;
    readonly string _scene;
    readonly long _epoch;
    readonly int _width, _height;
    public DsPortMapAuthorityToken(object owner, object map, object host, object camera, object viewport,
        string scene, long epoch, int width, int height)
    {
        _owner = owner; _map = map; _host = host; _camera = camera; _viewport = viewport;
        _scene = scene; _epoch = epoch; _width = width; _height = height;
    }
    public bool Same(DsPortMapAuthorityToken other) =>
        ReferenceEquals(_owner, other._owner) && ReferenceEquals(_map, other._map) &&
        ReferenceEquals(_host, other._host) && ReferenceEquals(_camera, other._camera) &&
        ReferenceEquals(_viewport, other._viewport) && _scene == other._scene &&
        _epoch == other._epoch && _width == other._width && _height == other._height;
}

// Production lifecycle authority for the adapter-owned map graph. Unity allocation
// counts are not executable in host tests; these counters measure the same admitted
// construction, bounded snapshot observation, reuse, and successful retirement calls.
public sealed class DsPortMapRetainedGraph<T> where T : class
{
    public const double SnapshotIntervalSeconds = .125;
    readonly double _interval;
    T _graph;
    DsPortMapAuthorityToken _authority;
    DsPortPageSnapshot _snapshot;
    double _nextSnapshot;
    int _commandBuffers;
    bool _retiring, _retirementRequired;
    public DsPortMapRetainedGraph(double interval = SnapshotIntervalSeconds)
    {
        if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval));
        _interval = interval;
    }
    public T Graph => _graph;
    public int ConstructionCount { get; private set; }
    public int RetirementCount { get; private set; }
    public int SnapshotCount { get; private set; }
    public int ReuseCount { get; private set; }
    public int DynamicRefreshCount { get; private set; }
    public int CommandBufferConstructionCount { get; private set; }
    public int CommandBufferReleaseCount { get; private set; }
    public void Admit(T graph, DsPortMapAuthorityToken authority, IEnumerable<object> snapshot,
        double now, int commandBuffers) =>
        Admit(graph, authority, new DsPortPageSnapshot(snapshot), now, commandBuffers);
    public void Admit(T graph, DsPortMapAuthorityToken authority, DsPortPageSnapshot snapshot,
        double now, int commandBuffers)
    {
        if (_graph != null || _retiring || _retirementRequired) throw new InvalidOperationException("Map replacement requires exact prior retirement");
        if (graph == null || snapshot == null || commandBuffers < 0 || double.IsNaN(now) || double.IsInfinity(now))
            throw new ArgumentOutOfRangeException();
        _snapshot = snapshot;
        _graph = graph; _authority = authority; _nextSnapshot = now + _interval;
        _commandBuffers = commandBuffers; ConstructionCount++;
        CommandBufferConstructionCount += commandBuffers;
    }
    public bool TryReuse(DsPortMapAuthorityToken authority, bool immediateEligible, bool primaryShowing,
        double now, Func<IEnumerable<object>> readSnapshot, Action<T> retire, out T graph)
    {
        graph = null;
        if (_graph == null) return false;
        if (_retirementRequired)
        {
            Retire(retire); return false;
        }
        if (!immediateEligible || primaryShowing || !_authority.Same(authority))
        {
            Retire(retire); return false;
        }
        if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
        if (now >= _nextSnapshot)
        {
            var observed = new DsPortPageSnapshot(readSnapshot());
            SnapshotCount++; _nextSnapshot = now + _interval;
            if (!_snapshot.Same(observed)) { Retire(retire); return false; }
        }
        graph = _graph; ReuseCount++; return true;
    }
    public void RecordDynamicRefresh(T graph)
    {
        if (!ReferenceEquals(graph, _graph) || _retiring || _retirementRequired)
            throw new InvalidOperationException("Map dynamic refresh requires the exact reusable graph");
        DynamicRefreshCount++;
    }
    public void Retire(Action<T> retire)
    {
        if (_graph == null) return;
        if (_retiring) throw new InvalidOperationException("Map retirement reentered");
        _retiring = true; _retirementRequired = true;
        try
        {
            var graph = _graph;
            retire(graph);
            _graph = null; _snapshot = null; _nextSnapshot = 0; _retirementRequired = false;
            RetirementCount++; CommandBufferReleaseCount += _commandBuffers; _commandBuffers = 0;
        }
        finally { _retiring = false; }
    }
}

public struct DsPortMapDrawKey
{
    public object Group;
    public int Layer, Order, Queue, Stable;
    public float Distance;
}

public struct DsPortMapSortSlot
{
    public int Binding;
    public DsPortMapDrawKey[] Keys;
    public int KeyCount;
}

// This exact scratch type is used by production SetupCamera. Its arrays and comparer
// are retained at graph construction, so Reset/Add/Sort have no steady-frame setup.
public sealed class DsPortMapSortScratch
{
    sealed class SlotComparer : IComparer<DsPortMapSortSlot>
    {
        internal static readonly SlotComparer Instance = new SlotComparer();
        public int Compare(DsPortMapSortSlot left, DsPortMapSortSlot right)
        {
            int count = Math.Min(left.KeyCount, right.KeyCount);
            for (int i = 0; i < count; i++)
            {
                var a = left.Keys[i]; var b = right.Keys[i];
                if (a.Group != null && ReferenceEquals(a.Group, b.Group)) continue;
                int value = DsPortMapTransaction.CompareDraw(a.Layer, a.Order, a.Queue, a.Distance, a.Stable,
                    b.Layer, b.Order, b.Queue, b.Distance, b.Stable);
                if (value != 0) return value;
            }
            return left.KeyCount.CompareTo(right.KeyCount);
        }
    }

    readonly DsPortMapSortSlot[] _slots;
    int _count;
    public DsPortMapSortScratch(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _slots = new DsPortMapSortSlot[capacity];
    }
    public int Count => _count;
    public int SortCount { get; private set; }
    public int FrameCollectionConstructionCount => 0;
    public int FrameClosureConstructionCount => 0;
    public int FrameRestoreQueueConstructionCount => 0;
    public int FrameMaterialArrayReadCount => 0;
    public int FrameComponentQueryCount => 0;
    public int FrameReflectionLookupCount => 0;
    public void Reset() => _count = 0;
    public void Add(DsPortMapSortSlot slot)
    {
        if (_count == _slots.Length) throw new InvalidOperationException("Map retained sort scratch capacity changed");
        _slots[_count++] = slot;
    }
    public void Sort()
    {
        for (int root = _count / 2 - 1; root >= 0; root--) SiftDown(root, _count);
        for (int end = _count - 1; end > 0; end--)
        {
            Swap(0, end); SiftDown(0, end);
        }
        SortCount++;
    }
    void SiftDown(int root, int count)
    {
        while (true)
        {
            int child = root * 2 + 1;
            if (child >= count) return;
            if (child + 1 < count && SlotComparer.Instance.Compare(_slots[child], _slots[child + 1]) < 0) child++;
            if (SlotComparer.Instance.Compare(_slots[root], _slots[child]) >= 0) return;
            Swap(root, child); root = child;
        }
    }
    void Swap(int left, int right)
    {
        var value = _slots[left]; _slots[left] = _slots[right]; _slots[right] = value;
    }
    public DsPortMapSortSlot At(int index)
    {
        if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
        return _slots[index];
    }
}

public static class DsPortMapTransaction
{
    public static int CompareDraw(int layerA, int orderA, int queueA, float distanceA, int stableA,
        int layerB, int orderB, int queueB, float distanceB, int stableB)
    {
        int value = layerA.CompareTo(layerB);
        if (value == 0) value = orderA.CompareTo(orderB);
        if (value == 0) value = queueA.CompareTo(queueB);
        if (value == 0) value = queueA <= 2500 ? distanceA.CompareTo(distanceB) : distanceB.CompareTo(distanceA);
        return value != 0 ? value : stableA.CompareTo(stableB);
    }
    // Finite GameMap.SetupMap + GameMapScene.SetMapped/SetNotMapped recipe.
    // Selector -1 means absent sprite / grey color; 0 original; 1 full sprite;
    // sprite alternatives begin at 2 and color alternatives at 1. No source cache.
    public sealed class RoomArt
    {
        public bool LocalMapped, LocalVisited, Mapped, Visited, AllChildren, Visible;
        public int Sprite, Color;
    }
    public static RoomArt RoomRecipe(int state, bool fullSprite, bool savedMapped, bool savedVisited,
        bool inheritedMapped, bool inheritedVisited, bool quill, bool hidden,
        bool[] spriteConditions, bool[] colorConditions, bool hide)
    {
        if (state < 0 || state > 2) throw new InvalidOperationException("Map room state unadmitted");
        bool mapped = savedMapped || inheritedMapped;
        bool all = state == 2 || (mapped && !hidden);
        var art = new RoomArt { LocalMapped = all && quill, LocalVisited = mapped || savedVisited,
            AllChildren = all, Visible = !hide, Sprite = state == 0 ? -1 : 0,
            Color = state == 1 && !fullSprite ? -1 : 0 };
        art.Mapped = art.LocalMapped || inheritedMapped;
        art.Visited = art.LocalVisited || inheritedVisited;
        if (all && quill)
        {
            art.Sprite = state == 1 && fullSprite ? 1 : 0; art.Color = 0;
            for (int i = 0; i < spriteConditions.Length; i++) if (spriteConditions[i]) { art.Sprite = i + 2; break; }
            for (int i = 0; i < colorConditions.Length; i++) if (colorConditions[i]) { art.Color = i + 1; break; }
        }
        return art;
    }
    public static bool OtherMapped(string[] dependencies, HashSet<string> mapped)
    {
        bool any = false;
        if (dependencies != null) foreach (string name in dependencies)
        { if (name == null) continue; any = true; if (!mapped.Contains(name)) return false; }
        return any;
    }
    public static float LayoutOffset(float offset, int index, int count)
    {
        if (count <= 0 || index < 0 || index >= count || float.IsNaN(offset) || float.IsInfinity(offset))
            throw new ArgumentOutOfRangeException();
        return offset * index - offset * (count - 1) / 2f;
    }
    public static object TextGeometry(object filterMesh, object retainedMesh, string path) =>
        filterMesh ?? retainedMesh ?? throw new InvalidOperationException("NEEDS_CONTEXT ungenerated native text: " + path);

    // Source: MapPin.CanBeActive. World visibility preference is applied only
    // AFTER this policy; hideIfOtherActive also ignores that preference natively.
    public static bool PinCanBeActive(bool active, bool hiddenByOther, bool zoneAllowed,
        bool hasParent, bool mapped, bool visited, bool parentHidden) =>
        !hiddenByOther && zoneAllowed && active && (!hasParent || mapped || (!parentHidden && visited));
    public static string CompassScene(string overridden, bool nonCapMaze, string current) =>
        !string.IsNullOrEmpty(overridden) ? overridden : nonCapMaze ? "DustMazeCompassMarker" : current;

    // MapMarkerArrow projects initialPos to its viewport's nearest boundary. The
    // companion owns a rectangular output viewport, never the primary collider.
    public static void ProjectToViewport(float x, float y, float minX, float minY, float maxX, float maxY,
        out float projectedX, out float projectedY)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) ||
            float.IsNaN(minX) || float.IsInfinity(minX) || float.IsNaN(minY) || float.IsInfinity(minY) ||
            float.IsNaN(maxX) || float.IsInfinity(maxX) || float.IsNaN(maxY) || float.IsInfinity(maxY))
            throw new ArgumentOutOfRangeException();
        if (minX > maxX || minY > maxY) throw new ArgumentOutOfRangeException();
        projectedX = Math.Max(minX, Math.Min(maxX, x));
        projectedY = Math.Max(minY, Math.Min(maxY, y));
    }
    public static T[][] CopyMarkers<T>(int kinds, int perKind, Func<int, IList<T>> read)
    {
        if (kinds < 0 || perKind < 0) throw new ArgumentOutOfRangeException();
        var result = new T[kinds][];
        for (int i = 0; i < kinds; i++)
        {
            var source = read(i);
            int count = source == null ? 0 : Math.Min(perKind, source.Count);
            result[i] = new T[count];
            for (int j = 0; j < count; j++) result[i][j] = source[j];
        }
        return result;
    }

    // Capture is read-only. The returned action retains exact presentation objects,
    // never resolves replacement owners and never restores PlayerData/save fields.
    public static bool Draw(Func<bool> eligible, Func<Action> capture, Action setup, Action render)
    {
        if (!eligible()) return false;
        Action restore = capture() ?? throw new InvalidOperationException("Map presentation restoration missing");
        if (!eligible()) return false;
        try
        {
            setup();
            if (!eligible()) return false;
            render();
            return eligible();
        }
        finally { restore(); }
    }
}

#if UNITY_ANDROID && !UNITY_EDITOR
// Render the existing native room/pin renderers explicitly, including inactive
// zone parents. Never activate/clone a GameMap or run its saved-marker setup.
// This uses the native initialized visual graph; cold/stale native setup is a
// reported capability boundary, not permission to write progress or awaken pins.
public sealed class DsPortMap : IDisposable
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly DsPortFrame _frame;
    readonly HashSet<string> _reported = new HashSet<string>();
    readonly DsPortMapResidency _residency = new DsPortMapResidency();
    readonly DsPortMapRetainedGraph<Source> _retained = new DsPortMapRetainedGraph<Source>();
    readonly Action<Source> _retireGraph;
    RawImage _image;
    RenderTexture _output;
    Action _pendingRestore;
    Vector2 _pan;
    float _zoom = 1f, _unitsPerPixel;
    bool _ready, _eligible, _disposed;
    object _lastMap;
    Source _presented;
    bool _outgoing;

    sealed class Source
    {
        public GameManager Game;
        public PlayerData Data;
        public SceneData SceneData;
        public HashSet<string> Mapped, Visited;
        public bool Hidden, Lost;
        public readonly List<Func<bool>> Watches = new List<Func<bool>>();
        public readonly List<Room> RoomsList = new List<Room>();
        public readonly Dictionary<GameMapScene, Room> RoomStates = new Dictionary<GameMapScene, Room>();
        public readonly Dictionary<Transform, Transform> DonorTransforms = new Dictionary<Transform, Transform>();
        public readonly Dictionary<SpriteRenderer, Material> Materials = new Dictionary<SpriteRenderer, Material>();
        public readonly Dictionary<Transform, Vector3> Positions = new Dictionary<Transform, Vector3>();
        public readonly Dictionary<Transform, Vector3> Scales = new Dictionary<Transform, Vector3>();
        public DsPortMapRestoreQueue Queue;
        public GameObject Donors;
        public readonly DsPortOwnedText Text = new DsPortOwnedText();
        public GameCameras Cameras;
        public GameMap Map;
        public RectTransform Host;
        public Camera Rooms, Decor, RoomsSource, DecorSource;
        public CameraState RoomsState, DecorState;
        public CameraOwnerAccess RoomsAccess, DecorAccess;
        public DrawBinding[] DrawPlan;
        public GroupBinding[] GroupPlan;
        public DsPortMapAuthorityToken Authority;
        public int ViewWidth, ViewHeight;
        public Transform GameplayRoot;
        public CameraRenderToMesh RoomsOwner, DecorOwner;
        public MeshRenderer RoomsQuad, DecorQuad;
        public long Epoch;
        public string Scene;
        public readonly Dictionary<Renderer, Renderer> RendererSources = new Dictionary<Renderer, Renderer>();
        public readonly Dictionary<SpriteRenderer, Room> RoomDonors = new Dictionary<SpriteRenderer, Room>();
        public readonly Dictionary<Renderer, MaterialPropertyBlock> PropertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        public DsPortMapFreshness Freshness;
        public Func<IEnumerable<object>> SnapshotReader;
        public RenderTexture PresentationActive;
        public bool PresentationPending;
        public readonly List<Renderer> Draw = new List<Renderer>();
        public readonly List<KeyValuePair<Transform, Vector3>> Markers = new List<KeyValuePair<Transform, Vector3>>();
        public readonly Dictionary<MapPin, bool> PinOverrides = new Dictionary<MapPin, bool>();
        public readonly Dictionary<Transform, bool> RootOverrides = new Dictionary<Transform, bool>();
        public readonly Dictionary<MapPin, bool> PinActivity = new Dictionary<MapPin, bool>();
        public MapPin.PinVisibilityStates PinVisibility;
        public GameObject CorpseRoot, CorpseArrow, CorpseArrowTemplate;
        public Transform CorpseDonor, CorpseArrowDonor;
        public readonly List<Renderer> CorpseArrowDraw = new List<Renderer>();
        public bool CorpseArrowVisible;
        public Vector2 CorpseLocal;
        public Vector3 Center;
        public float Half, Aspect;
        public readonly List<KeyValuePair<Transform, Quaternion>> Rotations = new List<KeyValuePair<Transform, Quaternion>>();
        public Bounds Bounds;
    }

    sealed class Room
    {
        public Transform Node, Parent;
        public int Zone;
        public bool Unlocked, Resolving;
        public GameMapScene Native;
        public SpriteRenderer Renderer;
        public Sprite Original, Full, Selected, BoundsSprite;
        public Color OriginalColor, Color;
        public DsPortMapTransaction.RoomArt Art;
    }

    sealed class CameraOwnerAccess
    {
        public readonly CameraRenderToMesh Owner;
        public readonly FieldInfo ActiveSource, TargetCamera, MeshRenderer, SourceCamera;
        public CameraOwnerAccess(CameraRenderToMesh owner)
        {
            Owner = owner;
            ActiveSource = RequireField(owner.GetType(), "activeSource");
            TargetCamera = RequireField(owner.GetType(), "targetCamera");
            MeshRenderer = RequireField(owner.GetType(), "meshRenderer");
            SourceCamera = RequireField(owner.GetType(), "sourceCamera");
        }
        public bool Current(Camera target, MeshRenderer quad, Camera source) => Owner != null &&
            ReferenceEquals(TargetCamera.GetValue(Owner), target) && ReferenceEquals(MeshRenderer.GetValue(Owner), quad) &&
            ReferenceEquals(SourceCamera.GetValue(Owner), source);
        public object ReadActiveSource() => ActiveSource.GetValue(Owner);
    }

    sealed class GroupBinding
    {
        public SortingGroup Group;
        public Transform Donor;
        public int Stable;
        public bool Enabled, SortAtRoot;
    }

    struct DrawBinding
    {
        public Renderer Renderer;
        public SpriteRenderer Sprite;
        public Material Material;
        public int Submesh, Stable;
        public bool CorpseArrow;
        public GroupBinding[] Groups;
        public DsPortMapDrawKey[] Keys;
    }

    sealed class CameraState
    {
        public readonly Camera Camera;
        public readonly CommandBuffer Commands;
        public readonly DsPortMapSortScratch Scratch;
        public readonly int[] GroupFrames, GroupQueues;
        public int Frame;
        public CameraState(Camera camera, int slots, int groups, DsPortMapRestoreQueue queue)
        {
            Camera = camera;
            Commands = new CommandBuffer { name = "Companion native map" };
            Scratch = new DsPortMapSortScratch(slots);
            GroupFrames = new int[groups]; GroupQueues = new int[groups];
            queue.Own(Commands.Release);
        }
    }

    public DsPortMap(DsPortFrame frame)
    {
        _frame = frame;
        _retireGraph = RetireGraph;
        _frame.BeforeCompositionDestroyed += Invalidate;
        _frame.SelectionChanged += SelectionChanged;
    }
    static FieldInfo RequireField(Type type, string name)
    {
        for (Type t = type; t != null; t = t.BaseType)
        {
            var field = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new InvalidOperationException("Map native field missing: " + name);
    }
    static object Get(object owner, string name) => RequireField(owner.GetType(), name).GetValue(owner);
    static bool Within(Transform node, Transform root) => node != null && root != null && (node == root || node.IsChildOf(root));
    static void Attempt(Action action, List<Exception> errors)
    { try { action(); } catch (Exception e) { errors.Add(e); } }
    void Report(Exception error)
    {
        string problem = error.GetBaseException().Message;
        if (_reported.Add(problem)) Debug.LogWarning("[DualScreen][capability-gap] Map: " + problem);
    }
    public void Tick(bool eligible)
    {
        if (_disposed) return;
        _eligible = eligible; _ready = false;
        try
        {
            var resident = _retained.Graph;
            if (resident != null && resident.PresentationPending) RestorePresentation(resident);
            _pendingRestore?.Invoke();
            if (_outgoing && KeepOutgoing()) return;
        }
        catch (Exception e) { Report(e); return; }
        _outgoing = false; _presented = null;
        if (_image != null) _image.enabled = false;
        try
        {
            Source source;
            var existing = _retained.Graph;
            if (existing != null && _retained.TryReuse(CurrentAuthority(existing), Current(existing),
                PrimaryShowing(existing), Time.unscaledTime, existing.SnapshotReader,
                _retireGraph, out source))
            {
                if (RefreshForDraw(source)) Draw(source);
                return;
            }
            // A failed retirement leaves the exact graph resident and blocks any
            // replacement until its release callback succeeds on a later tick.
            if (_retained.Graph != null) return;
            source = BuildGraph();
            if (source != null && RefreshForDraw(source)) Draw(source);
        }
        catch (Exception e) { _residency.Wait(); Report(e); }
    }
    Source BuildGraph()
    {
        var source = CaptureSource();
        if (source == null) return null;
        if (!ReferenceEquals(_lastMap, source.Map)) { _lastMap = source.Map; _pan = Vector2.zero; _zoom = 1f; }
        if (!_residency.Probe(source.Map, source.Scene, source.Epoch, Time.unscaledTime)) return null;
        var lifetime = new DsPortMapRestoreQueue(); source.Queue = lifetime;
        Action restore = () => { lifetime.Restore(); _pendingRestore = null; };
        _pendingRestore = restore;
        try
        {
            ReadNativeDisplay(source);
            BuildDrawPlan(source);
            _residency.Ready();
            EnsureOutput(source.Host);
            source.ViewWidth = _output.width; source.ViewHeight = _output.height;
            source.RoomsState = new CameraState(source.Rooms, source.DrawPlan.Length, source.GroupPlan.Length, lifetime);
            source.DecorState = new CameraState(source.Decor, source.DrawPlan.Length, source.GroupPlan.Length, lifetime);
            source.Authority = CurrentAuthority(source);
            _retained.Admit(source, source.Authority, source.Freshness.Captured,
                Time.unscaledTime, 2);
            _pendingRestore = null;
            return source;
        }
        finally
        {
            // Partial construction owns no publishable graph. Its exact release
            // remains pending and therefore blocks another construction on error.
            if (!ReferenceEquals(_retained.Graph, source)) restore();
        }
    }
    bool RefreshForDraw(Source source)
    {
        bool live = source.Authority.Same(CurrentAuthority(source)) && Current(source) && !PrimaryShowing(source);
        if (!live) { _retained.Retire(_retireGraph); return false; }
        try { RefreshMutableDonors(source); _retained.RecordDynamicRefresh(source); }
        catch { _retained.Retire(_retireGraph); throw; }
        live = source.Authority.Same(CurrentAuthority(source)) && Current(source) && !PrimaryShowing(source);
        if (!live) _retained.Retire(_retireGraph);
        return live;
    }
    bool Live(Source source) => source.Authority.Same(CurrentAuthority(source)) &&
        Current(source) && !PrimaryShowing(source);
    static void RestorePresentation(Source source)
    {
        if (!source.PresentationPending) return;
        RenderTexture.active = source.PresentationActive;
        source.PresentationActive = null; source.PresentationPending = false;
    }
    void Draw(Source source)
    {
        if (!Live(source)) { _retained.Retire(_retireGraph); return; }
        PrepareView(source);
        source.PresentationActive = RenderTexture.active;
        source.PresentationPending = true;
        bool rendered = false;
        try
        {
            if (Live(source))
            {
                Setup(source, source.RoomsState, source.DecorState);
                if (Live(source))
                {
                    Graphics.ExecuteCommandBuffer(source.RoomsState.Commands);
                    if (Live(source))
                    {
                        Graphics.ExecuteCommandBuffer(source.DecorState.Commands);
                        rendered = Live(source);
                    }
                }
            }
        }
        finally { RestorePresentation(source); }
        if (!Live(source)) { _retained.Retire(_retireGraph); return; }
        if (!rendered) return;
        _image.texture = _output; _image.enabled = true; _ready = true; _presented = source;
    }
    Source CaptureSource()
    {
        if (!_eligible || !_frame.HudReady || _frame.SelectedRole != DsPageRole.Map || !DsGameData.InGame) return null;
        var game = GameManager.SilentInstance; var data = PlayerData.instance; var cameras = GameCameras.SilentInstance;
        if (game == null || data == null || cameras == null || game.gameMap == null ||
            !ReferenceEquals(game.playerData, data) || game.isPaused || game.IsInSceneTransition || data.isInventoryOpen || !data.HasAnyMap) return null;
        var host = _frame.GetOrCreatePageHost(DsPageRole.Map);
        if (host == null || !host.gameObject.activeInHierarchy) return null;
        var source = new Source { Game = game, Data = data, SceneData = SceneData.instance, Cameras = cameras, Map = game.gameMap,
            Host = host, Epoch = _frame.SelectionEpoch, Scene = game.sceneName };
        if (source.SceneData == null) return null;
        var hud = cameras.hudCamera != null ? cameras.hudCamera.GetComponent<HUDCamera>() : null;
        if (hud == null || hud.GameplayChild == null) return null;
        var owners = hud.GameplayChild.GetComponentsInChildren<CameraRenderToMesh>(true);
        foreach (var owner in owners)
        {
            if ((CameraRenderToMesh.ActiveSources)Get(owner, "activeSource") != CameraRenderToMesh.ActiveSources.GameMap) continue;
            var camera = Get(owner, "targetCamera") as Camera;
            var quad = Get(owner, "meshRenderer") as MeshRenderer;
            if (camera == null || quad == null || !Within(camera.transform, hud.GameplayChild.transform) ||
                !Within(quad.transform, hud.GameplayChild.transform) || !camera.orthographic ||
                camera.farClipPlane <= camera.nearClipPlane)
                throw new InvalidOperationException("Map camera/quad ownership changed");
            if (source.Rooms == null) { source.Rooms = camera; source.RoomsOwner = owner; source.RoomsQuad = quad; }
            else if (source.Decor == null) { source.Decor = camera; source.DecorOwner = owner; source.DecorQuad = quad; }
            else throw new InvalidOperationException("Map native render camera pair ambiguous");
        }
        if (source.Rooms == null || source.Decor == null) return null;
        if (source.Rooms.farClipPlane < source.Decor.farClipPlane)
        {
            var c = source.Rooms; source.Rooms = source.Decor; source.Decor = c;
            var o = source.RoomsOwner; source.RoomsOwner = source.DecorOwner; source.DecorOwner = o;
            var q = source.RoomsQuad; source.RoomsQuad = source.DecorQuad; source.DecorQuad = q;
        }
        if (source.Rooms == source.Decor || !Mathf.Approximately(source.Rooms.nearClipPlane, source.Decor.farClipPlane))
            throw new InvalidOperationException("Map native camera depth slices do not abut");
        source.GameplayRoot = hud.GameplayChild.transform;
        source.RoomsSource = Get(source.RoomsOwner, "sourceCamera") as Camera;
        source.DecorSource = Get(source.DecorOwner, "sourceCamera") as Camera;
        if (source.RoomsSource == null || source.DecorSource == null) throw new InvalidOperationException("Map source camera owner unavailable");
        source.RoomsAccess = new CameraOwnerAccess(source.RoomsOwner);
        source.DecorAccess = new CameraOwnerAccess(source.DecorOwner);
        if (Quaternion.Angle(source.Rooms.transform.rotation, Quaternion.identity) > .001f || Quaternion.Angle(source.Decor.transform.rotation, Quaternion.identity) > .001f)
            throw new InvalidOperationException("Map camera orientation outside native XY framing contract");
        return PrimaryShowing(source) ? null : source;
    }
    static void ViewportSize(RectTransform host, out int width, out int height)
    {
        width = host == null ? 0 : Mathf.Clamp(Mathf.CeilToInt(host.rect.width), 64, 2048);
        height = host == null ? 0 : Mathf.Clamp(Mathf.CeilToInt(host.rect.height), 64, 2048);
    }
    DsPortMapAuthorityToken CurrentAuthority(Source source)
    {
        ViewportSize(source.Host, out int width, out int height);
        var game = GameManager.SilentInstance;
        return new DsPortMapAuthorityToken(game, game != null ? game.gameMap : null,
            source.Host, source.Rooms, source.Decor, game != null ? game.sceneName : null,
            _frame.SelectionEpoch, width, height);
    }
    bool Current(Source source, bool presentationOnly = false)
    {
        try
        {
            return _eligible && !_disposed && (presentationOnly ? _frame.IsOutgoingPresentation(DsPageRole.Map) :
                _frame.SelectedRole == DsPageRole.Map && _frame.SelectionEpoch == source.Epoch) &&
                source.Host != null && source.Host.gameObject.activeInHierarchy && source.Map != null &&
                ReferenceEquals(GameManager.SilentInstance, source.Game) && ReferenceEquals(PlayerData.instance, source.Data) &&
                ReferenceEquals(GameCameras.SilentInstance, source.Cameras) && ReferenceEquals(SceneData.instance, source.SceneData) && ReferenceEquals(source.Game.gameMap, source.Map) &&
                ReferenceEquals(source.Game.playerData, source.Data) && source.Game.sceneName == source.Scene &&
                !source.Game.isPaused && !source.Game.IsInSceneTransition && !source.Data.isInventoryOpen &&
                source.RoomsOwner != null && source.DecorOwner != null && source.RoomsQuad != null && source.DecorQuad != null &&
                Within(source.RoomsOwner.transform, source.GameplayRoot) && Within(source.DecorOwner.transform, source.GameplayRoot) &&
                Within(source.Rooms.transform, source.GameplayRoot) && Within(source.Decor.transform, source.GameplayRoot) &&
                Within(source.RoomsQuad.transform, source.GameplayRoot) && Within(source.DecorQuad.transform, source.GameplayRoot) &&
                source.RoomsAccess != null && source.DecorAccess != null &&
                source.RoomsAccess.Current(source.Rooms, source.RoomsQuad, source.RoomsSource) &&
                source.DecorAccess.Current(source.Decor, source.DecorQuad, source.DecorSource);
        }
        catch { return false; }
    }
    static bool PrimaryShowing(Source source) => source.Rooms == null || source.Decor == null ||
        source.Rooms.enabled || source.Decor.enabled ||
        (source.RoomsQuad != null && source.RoomsQuad.enabled && source.RoomsQuad.gameObject.activeInHierarchy) ||
        (source.DecorQuad != null && source.DecorQuad.enabled && source.DecorQuad.gameObject.activeInHierarchy);

    // Only explicit presentation recipe roots are traversed. Unity assets are
    // retained identities; PlayerData is never serialized, hashed or restored.
    static IEnumerable<object> ReadSerializedInputs(object value, int depth = 0, bool fields = false)
    {
        if (depth > 16) throw new InvalidOperationException("Map serialized recipe exceeds finite depth");
        yield return value;
        if (value == null || value is string || value is PlayerDataBase || (!fields && value is Object)) yield break;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is Vector2 || value is Vector3 || value is Vector4 ||
            value is Color || value is Quaternion || value is Rect || value is Matrix4x4 || value is Bounds) yield break;
        if (value is Array array)
        {
            yield return array.Length;
            foreach (var entry in array) foreach (var scalar in ReadSerializedInputs(entry, depth + 1)) yield return scalar;
            yield break;
        }
        if (value is System.Collections.IList list)
        {
            yield return list.Count;
            foreach (var entry in list) foreach (var scalar in ReadSerializedInputs(entry, depth + 1)) yield return scalar;
            yield break;
        }
        for (var owner = type; owner != null && owner != typeof(MonoBehaviour) && owner != typeof(Component) && owner != typeof(Object); owner = owner.BaseType)
            foreach (var field in owner.GetFields(Flags | BindingFlags.DeclaredOnly))
                if (!field.IsStatic && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), true)))
                    foreach (var scalar in ReadSerializedInputs(field.GetValue(value), depth + 1)) yield return scalar;
    }
    static IEnumerable<object> ReadRecipeInputs(Source source)
    {
        yield return source.Data.HasAnyMap;
        yield return source.Data.HeroCorpseScene; yield return source.Data.HeroDeathScenePos; yield return source.Data.HeroDeathSceneSize;
        var compass = Gameplay.CompassTool; yield return compass; yield return compass != null && compass.IsEquipped;
        yield return source.Map.IsLostInAbyssPreMap(); yield return source.Map.GetCurrentMapZone();
        var hero = HeroController.instance; yield return hero;
        if (hero != null) yield return hero.transform.position;
        var maze = MazeController.NewestInstance; yield return maze; if (maze != null) yield return maze.IsCapScene;
        var tilemap = source.Game.tilemap; yield return tilemap;
        if (tilemap != null) { yield return tilemap.width; yield return tilemap.height; }
        foreach (string name in new[] { "mapZoneInfo", "mapMarkerTemplates", "mainQuestPins", "fleaPinParents", "compassIcon", "shadeMarker", "overriddenSceneName", "overriddenSceneRegion" })
            foreach (var scalar in ReadSerializedInputs(Get(source.Map, name))) yield return scalar;
        yield return source.RoomsAccess.ReadActiveSource(); yield return source.DecorAccess.ReadActiveSource();
        foreach (var camera in new[] { source.Rooms, source.Decor })
        {
            yield return camera; yield return camera.transform.localToWorldMatrix;
            yield return camera.cullingMask; yield return camera.nearClipPlane; yield return camera.farClipPlane;
            yield return camera.orthographic; yield return camera.worldToCameraMatrix; yield return camera.projectionMatrix;
            yield return camera.transparencySortMode; yield return camera.transparencySortAxis;
        }
        yield return GraphicsSettings.transparencySortMode; yield return GraphicsSettings.transparencySortAxis;
        foreach (var component in source.Map.GetComponentsInChildren<Component>(true))
        {
            if (component == null) throw new InvalidOperationException("Map recipe contains missing native script");
            yield return component;
            if (component is Transform node)
            {
                yield return node.parent; yield return node.GetSiblingIndex(); yield return node.name;
                yield return node.localToWorldMatrix; yield return node.gameObject.activeSelf; yield return node.gameObject.layer;
            }
            if (component is Renderer renderer)
            {
                yield return renderer.enabled; yield return renderer.sortingLayerID; yield return renderer.sortingOrder;
                yield return SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
                foreach (var material in renderer.sharedMaterials)
                { yield return material; if (material != null) { yield return material.shader; yield return material.renderQueue; } }
                if (renderer is SpriteRenderer sprite)
                {
                    yield return sprite.sprite; yield return sprite.color; yield return sprite.flipX; yield return sprite.flipY;
                    yield return sprite.drawMode; yield return sprite.size; yield return sprite.tileMode;
                    yield return sprite.maskInteraction; yield return sprite.spriteSortPoint;
                }
            }
            if (component is MeshFilter filter) yield return filter.sharedMesh;
            if (component is SortingGroup group)
            { yield return group.enabled; yield return group.sortingLayerID; yield return group.sortingOrder; yield return group.sortAtRoot; }
            if (component is GameMapScene || component is MapPin || component is PositionConditions || component is MapPinConditional ||
                component is DeactivateIfPlayerdataFalse || component is GameMapPinLayout || component is ShadeMarkerArrow || component is TMProOld.TextMeshPro)
                foreach (var scalar in ReadSerializedInputs(component, fields: true)) yield return scalar;
        }
    }
    static IEnumerable<object> ReadConsumedInputs(Source source)
    {
        foreach (var value in ReadRecipeInputs(source)) yield return value;
        var mapped = new List<string>(source.Data.scenesMapped); mapped.Sort(StringComparer.Ordinal);
        yield return "mapped"; yield return mapped.Count;
        foreach (var value in mapped) yield return value;
        var visited = new List<string>(source.Data.scenesVisited); visited.Sort(StringComparer.Ordinal);
        yield return "visited"; yield return visited.Count;
        foreach (var value in visited) yield return value;
        yield return "watches"; yield return source.Watches.Count;
        foreach (var watch in source.Watches) yield return watch();
    }
    void ReadNativeDisplay(Source source)
    {
        // Rebuild owned records only when the bounded consumed-content snapshot
        // changes. Same-count membership and condition changes remain real changes.
        source.Mapped = new HashSet<string>(source.Data.scenesMapped);
        source.Visited = new HashSet<string>(source.Data.scenesVisited);
        source.Hidden = Watch(source, CollectableItemManager.IsInHiddenMode);
        source.Lost = Watch(source, source.Map.IsLostInAbyssPostMap);
        source.PinVisibility = MapPin.CurrentState;
        source.Watches.Add(() => MapPin.CurrentState == source.PinVisibility);
        if (source.PinVisibility != MapPin.PinVisibilityStates.PinsAndKey && source.PinVisibility != MapPin.PinVisibilityStates.Pins && source.PinVisibility != MapPin.PinVisibilityStates.None)
            throw new InvalidOperationException("Map native pin preference invalid");
        ReadSerializedRooms(source);
        foreach (var room in source.RoomsList) if (room.Native != null) ResolveRoom(source, room);
        var extraRoots = ReadPinGroups(source);
        foreach (var room in source.RoomsList) PrepareRoomChildren(source, room);
        ReadConditions(source);
        ReadLayouts(source);
        var unique = new HashSet<Renderer>(); bool haveBounds = false;
        foreach (var room in source.RoomsList)
        {
            if (!room.Unlocked || (source.Hidden && room.Zone != (int)GlobalEnums.MapZone.THE_SLAB) ||
                (source.Lost && room.Zone != (int)GlobalEnums.MapZone.ABYSS)) continue;
            source.RootOverrides[room.Node] = true;
            foreach (var renderer in room.Node.GetComponentsInChildren<Renderer>(true))
            {
                if (!VisibleBelow(source, renderer, room.Node) || renderer.GetComponentInParent<MapNextAreaDisplay>(true) != null || !unique.Add(renderer)) continue;
                var donor = OwnedRenderer(source, renderer);
                if (donor != null) source.Draw.Add(donor);
            }
            // Only selected room sprite vertices contribute, never text or pins.
            if (room.Native == null || room.Renderer == null || !room.Art.Visible || room.Selected == null || (bool)Get(room.Native, "excludeBounds")) continue;
            var transform = DonorTransform(source, room.Node);
            foreach (var vertex in room.Selected.vertices)
            {
                Vector3 point = transform.TransformPoint(vertex);
                if (!haveBounds) { source.Bounds = new Bounds(point, Vector3.zero); haveBounds = true; }
                else source.Bounds.Encapsulate(point);
            }
        }
        if (!haveBounds || source.Bounds.size.x <= 0 || source.Bounds.size.y <= 0)
            throw new InvalidOperationException("Map owned room sprite bounds unavailable");
        foreach (var root in extraRoots) AddNativeRoot(source, root, unique);
        ReadCompassAndCorpse(source, unique);
        var templates = (GameObject[])Get(source.Map, "mapMarkerTemplates");
        if (templates == null) throw new InvalidOperationException("Map serialized marker templates unavailable");
        var saved = source.Data.placedMarkers;
        var positions = DsPortMapTransaction.CopyMarkers(templates.Length, 9,
            i => saved != null && i < saved.Length && saved[i] != null ? saved[i].List : null);
        source.Watches.Add(() => MarkersCurrent(source, saved, positions));
        if (!source.Hidden)
        {
            for (int kind = 0; kind < positions.Length; kind++)
                for (int index = 0; index < positions[kind].Length; index++)
                {
                    var template = templates[kind]; var position = positions[kind][index];
                    if (template == null || !Finite(position.x) || !Finite(position.y)) throw new InvalidOperationException("Map saved-marker donor unavailable");
                    // Serialized templates are native assets, not instantiated InvMarker
                    // scripts. Copy renderers only, one bounded donor per saved marker.
                    var parent = template.transform.parent;
                    if (parent == null || !Within(parent, source.Map.transform)) throw new InvalidOperationException("Map marker template parent escaped owner");
                    CopyTemplateRenderers(source, template, DonorTransform(source, parent),
                        new Vector3(position.x, position.y, template.transform.localPosition.z), template.transform.localRotation, template.transform.localScale);
                }
        }
        source.SnapshotReader = () => ReadConsumedInputs(source);
        source.Freshness = new DsPortMapFreshness(source.SnapshotReader());
    }
    static bool Watch(Source source, Func<bool> read)
    { bool value = read(); source.Watches.Add(() => read() == value); return value; }
    static bool DataBool(Source source, string name) => Watch(source, () => source.Data.GetVariable<bool>(name));
    static bool Test(Source source, PlayerDataTest test)
    {
        if (test == null || test.TestGroups == null) throw new InvalidOperationException("Map condition unavailable");
        var data = (PlayerDataBase)Get(test, "playerDataOverride") ?? source.Data;
        bool Read()
        {
            if (test.TestGroups.Length == 0) return true;
            foreach (var group in test.TestGroups) if (group.IsFulfilled(data)) return true;
            return false;
        }
        return Watch(source, Read);
    }
    static void ReadSerializedRooms(Source source)
    {
        var zones = (Array)Get(source.Map, "mapZoneInfo");
        if (zones == null) throw new InvalidOperationException("Map serialized zones unavailable");
        for (int zone = 0; zone < zones.Length; zone++)
        {
            var info = zones.GetValue(zone); if (info == null) continue;
            var parents = Get(info, "Parents") as Array; if (parents == null) continue;
            foreach (var parent in parents)
            {
                var root = Get(parent, "Parent") as GameObject;
                if (root == null) continue;
                if (!Within(root.transform, source.Map.transform)) throw new InvalidOperationException("Map zone parent escaped owner");
                var pdBool = (string)Get(parent, "PlayerDataBool");
                bool unlocked = !string.IsNullOrEmpty(pdBool) && (DataBool(source, "mapAllRooms") || DataBool(source, pdBool));
                ReadPosition(source, root.GetComponent<PositionConditions>());
                int count = root.transform.childCount;
                source.Watches.Add(() => root != null && root.transform.childCount == count && ReferenceEquals(Get(parent, "Parent"), root));
                int index = 0;
                foreach (Transform child in root.transform)
                {
                    int order = index++; string name = child.name;
                    source.Watches.Add(() => child != null && child.parent == root.transform && child.GetSiblingIndex() == order && child.name == name);
                    var room = new Room { Node = child, Parent = root.transform, Zone = zone, Unlocked = unlocked,
                        Native = child.GetComponent<GameMapScene>(), Renderer = child.GetComponent<SpriteRenderer>() };
                    source.RoomsList.Add(room);
                    if (room.Native != null)
                    {
                        if (source.RoomStates.ContainsKey(room.Native)) throw new InvalidOperationException("Map scene serialized twice");
                        source.RoomStates.Add(room.Native, room);
                    }
                }
            }
        }
    }
    static void ResolveRoom(Source source, Room room)
    {
        if (room.Art != null) return;
        if (room.Resolving) throw new InvalidOperationException("Map mapped-parent cycle");
        room.Resolving = true;
        var native = room.Native; Room inherited = null;
        var parent = Get(native, "mappedParent") as GameMapScene;
        if (parent != null)
        {
            if (!source.RoomStates.TryGetValue(parent, out inherited)) throw new InvalidOperationException("Map mappedParent outside serialized room graph: " + native.name);
            ResolveRoom(source, inherited);
        }
        bool checkedSprite = (bool)Get(native, "checkedSprite");
        room.Original = checkedSprite ? Get(native, "initialSprite") as Sprite : room.Renderer != null ? room.Renderer.sprite : null;
        room.OriginalColor = checkedSprite ? (Color)Get(native, "initialColor") : room.Renderer != null ? room.Renderer.color : Color.white;
        room.OriginalColor.a = 1;
        room.Full = Get(native, "fullSprite") as Sprite;
        var deps = (GameMapScene[])Get(native, "mappedIfAllMapped");
        var names = new string[deps == null ? 0 : deps.Length];
        for (int i = 0; i < names.Length; i++) names[i] = deps[i] != null ? deps[i].name : null;
        var sprites = (Array)Get(native, "altFullSprites"); var colors = (Array)Get(native, "altColors");
        bool[] spriteTests = new bool[sprites.Length], colorTests = new bool[colors.Length];
        for (int i = 0; i < sprites.Length; i++) spriteTests[i] = Test(source, (PlayerDataTest)Get(sprites.GetValue(i), "Condition"));
        for (int i = 0; i < colors.Length; i++) colorTests[i] = Test(source, (PlayerDataTest)Get(colors.GetValue(i), "Condition"));
        var hide = (PlayerDataTest)Get(native, "hideCondition");
        room.Art = DsPortMapTransaction.RoomRecipe((int)native.InitialState, room.Full != null,
            DataBool(source, "mapAllRooms") || source.Mapped.Contains(room.Node.name) || DsPortMapTransaction.OtherMapped(names, source.Mapped),
            source.Visited.Contains(room.Node.name), inherited != null && inherited.Art.Mapped, inherited != null && inherited.Art.Visited,
            DataBool(source, "hasQuill"), source.Hidden, spriteTests, colorTests,
            hide != null && hide.TestGroups != null && hide.TestGroups.Length > 0 && Test(source, hide));
        room.Selected = room.Art.Sprite < 0 ? null : room.Art.Sprite == 0 ? room.Original : room.Art.Sprite == 1 ? room.Full : Get(sprites.GetValue(room.Art.Sprite - 2), "Sprite") as Sprite;
        room.Color = room.Art.Color < 0 ? Color.grey : room.Art.Color == 0 ? room.OriginalColor : (Color)Get(colors.GetValue(room.Art.Color - 1), "Color");
        room.BoundsSprite = (bool)Get(native, "unmappedNoBounds") && !room.Art.Mapped ? null : native.InitialState == GameMapScene.States.Rough && room.Full != null ? room.Full : room.Original;
        room.Resolving = false;
    }
    static void ReadPosition(Source source, PositionConditions condition)
    {
        if (condition == null) return;
        var node = condition.transform;
        if (!Within(node, source.Map.transform)) throw new InvalidOperationException("Map position condition escaped owner");
        var initial = (bool)Get(condition, "hasStarted") ? (Vector3)Get(condition, "initialLocalPos") : node.localPosition;
        var desired = node.localPosition; desired.x = initial.x; desired.y = initial.y;
        var scale = Vector3.one;
        foreach (var candidate in (Array)Get(condition, "positionsOrdered"))
            if (Test(source, (PlayerDataTest)Get(candidate, "Condition")))
            { var offset = (Vector2)Get(candidate, "Offset"); desired.x += offset.x; desired.y += offset.y; scale = (Vector3)Get(candidate, "Scale"); break; }
        source.Positions[node] = desired; source.Scales[node] = scale;
    }
    static void PrepareRoomChildren(Source source, Room room)
    {
        if (room.Native == null) return;
        foreach (Transform child in room.Node)
        {
            bool active;
            if (room.Art.AllChildren)
                active = child.name != "pin_blue_health" || child.gameObject.activeSelf ||
                    (Watch(source, () => source.Data.scenesEncounteredCocoon.Contains(room.Node.name)) && DataBool(source, "hasPinCocoon"));
            else active = room.Art.LocalVisited && ((room.Native.InitialState == GameMapScene.States.Rough &&
                (child.GetComponent<MapPin>() != null || child.GetComponent<GameMapPinLayout>() != null)) || child.GetComponent<TMProOld.TextMeshPro>() != null);
            source.RootOverrides[child] = active;
        }
    }
    static void ReadConditions(Source source)
    {
        foreach (var condition in source.Map.GetComponentsInChildren<DeactivateIfPlayerdataFalse>(true))
        {
            var target = condition.objectToDeactivate != null ? condition.objectToDeactivate : condition.gameObject;
            if (!Within(target.transform, source.Map.transform)) throw new InvalidOperationException("Map bool-condition target escaped owner");
            if (!DataBool(source, condition.boolName)) source.RootOverrides[target.transform] = false;
        }
        foreach (var condition in source.Map.GetComponentsInChildren<MapPinConditional>(true))
        {
            bool Fulfilled(object value)
            {
                if (!Test(source, (PlayerDataTest)Get(value, "PlayerDataTest"))) return false;
                var persistent = (MapPinConditional.PersistentBoolMatch)Get(value, "PersistentBool");
                if (string.IsNullOrEmpty(persistent.Id) || string.IsNullOrEmpty(persistent.SceneName)) return true;
                return Watch(source, () => (source.SceneData.PersistentBools.TryGetValue(persistent.SceneName, persistent.Id, out var found) && found.Value) == persistent.ExpectedValue);
            }
            if (!Fulfilled(Get(condition, "visibleCondition"))) { source.RootOverrides[condition.transform] = false; continue; }
            var renderer = condition.GetComponent<SpriteRenderer>();
            if (renderer == null) throw new InvalidOperationException("Map conditional sprite missing");
            var initial = Get(condition, "spriteRenderer") as SpriteRenderer;
            source.Materials[renderer] = Fulfilled(Get(condition, "materialCondition")) ? (Material)Get(condition, "material") :
                initial != null ? (Material)Get(condition, "initialMaterial") : renderer.sharedMaterial;
        }
    }
    static void ReadLayouts(Source source)
    {
        foreach (var layout in source.Map.GetComponentsInChildren<GameMapPinLayout>(true))
        {
            var visible = new List<Transform>();
            foreach (Transform child in layout.transform)
            {
                // Native Evaluate enables children before finite IEvaluateHook
                // conditions. Unknown hooks are a precise host admission gap.
                foreach (var component in child.GetComponents<MonoBehaviour>())
                    if ((component is GameMapPinLayout.IEvaluateHook && !(component is DeactivateIfPlayerdataFalse)) || component is GameMapPinLayout.ILayoutHook)
                        throw new InvalidOperationException("Map layout hook unadmitted: " + component.GetType().FullName);
                bool active = !source.RootOverrides.TryGetValue(child, out bool value) || value;
                var pin = child.GetComponent<MapPin>();
                if (pin != null) active &= source.PinVisibility != MapPin.PinVisibilityStates.None && PinCanBeActive(source, pin, new HashSet<MapPin>());
                source.RootOverrides[child] = active;
                if (active) visible.Add(child);
            }
            var offset = (Vector2)Get(layout, "itemOffset");
            for (int i = 0; i < visible.Count; i++)
            {
                var position = visible[i].localPosition;
                position.x = DsPortMapTransaction.LayoutOffset(offset.x, i, visible.Count);
                position.y = DsPortMapTransaction.LayoutOffset(offset.y, i, visible.Count);
                source.Positions[visible[i]] = position;
            }
        }
    }
    static bool MarkersCurrent(Source source, WrappedVector2List[] saved, Vector2[][] positions)
    {
        if (!ReferenceEquals(source.Data.placedMarkers, saved)) return false;
        for (int i = 0; i < positions.Length; i++)
        {
            var list = saved != null && i < saved.Length && saved[i] != null ? saved[i].List : null;
            if (Math.Min(9, list == null ? 0 : list.Count) != positions[i].Length) return false;
            for (int j = 0; j < positions[i].Length; j++) if (list[j] != positions[i][j]) return false;
        }
        return true;
    }
    static List<GameObject> ReadPinGroups(Source source)
    {
        var roots = new List<GameObject>();
        var main = Get(source.Map, "mainQuestPins") as GameObject;
        if (main != null) { source.RootOverrides.Add(main.transform, true); roots.Add(main); }
        var groups = (Transform[])Get(source.Map, "fleaPinParents");
        for (int group = 0; group < groups.Length; group++)
        {
            var root = groups[group]; if (root == null) continue;
            if (!Within(root, source.Map.transform)) throw new InvalidOperationException("Map flea group escaped native owner");
            bool active = DataBool(source, CaravanTroupeHunter.PdBools[(CaravanTroupeHunter.PinGroups)group]);
            source.RootOverrides[root] = active;
            if (!active) continue;
            roots.Add(root.gameObject);
            foreach (Transform child in root)
            {
                var pin = child.GetComponent<MapPin>();
                if (pin != null) source.PinOverrides[pin] = !DataBool(source, child.gameObject.name);
            }
        }
        return roots;
    }
    static bool PinCanBeActive(Source source, MapPin pin, HashSet<MapPin> visiting)
    {
        if (source.PinActivity.TryGetValue(pin, out bool cached)) return cached;
        if (!Within(pin.transform, source.Map.transform) || pin.GetType() != typeof(MapPin))
            throw new InvalidOperationException("Map pin ownership/type changed");
        if (!visiting.Add(pin)) throw new InvalidOperationException("Map pin hide policy cycle");
        try
        {
            var other = Get(pin, "hideIfOtherActive") as MapPin;
            bool hiddenByOther = other != null && PinCanBeActive(source, other, visiting);
            var parent = pin.GetComponentInParent<GameMapScene>(true);
            int condition = (int)Get(pin, "activeCondition");
            if (condition != 0 && condition != 1) throw new InvalidOperationException("Map pin active condition unadmitted");
            bool zoneAllowed = condition == 0 || (parent != null && source.Map.GetMapZoneForScene(parent.transform) == source.Map.GetCurrentMapZone());
            bool active = source.PinOverrides.TryGetValue(pin, out bool value) ? value : Watch(source, () => pin != null && pin.IsActive);
            Room room = null;
            if (parent != null && !source.RoomStates.TryGetValue(parent, out room)) throw new InvalidOperationException("Map pin parent outside owned room graph");
            bool result = DsPortMapTransaction.PinCanBeActive(active, hiddenByOther, zoneAllowed, parent != null,
                room != null && room.Art.Mapped, room != null && room.Art.Visited,
                parent != null && parent.InitialState == GameMapScene.States.Hidden);
            source.PinActivity.Add(pin, result);
            return result;
        }
        finally { visiting.Remove(pin); }
    }
    static void AddNativeRoot(Source source, GameObject root, HashSet<Renderer> unique, GameObject omit = null, bool nativePresentation = false)
    {
        if (root == null || !Within(root.transform, source.Map.transform)) throw new InvalidOperationException("Map projection root escaped native owner");
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if ((omit != null && Within(renderer.transform, omit.transform)) || !VisibleBelow(source, renderer, root.transform) ||
                renderer.GetComponentInParent<MapNextAreaDisplay>(true) != null) continue;
            if (!unique.Add(renderer)) continue;
            var donor = OwnedRenderer(source, renderer); if (donor != null) source.Draw.Add(donor);
        }
    }
    static void PositionNativeRoot(Source source, GameObject root, Vector2 position, HashSet<Renderer> unique, GameObject omit = null)
    {
        if (root == null || !Within(root.transform, source.Map.transform)) throw new InvalidOperationException("Map position root escaped native owner");
        if (!Finite(position.x) || !Finite(position.y)) throw new InvalidOperationException("Map projected native position invalid");
        source.Markers.Add(new KeyValuePair<Transform, Vector3>(DonorTransform(source, root.transform),
            new Vector3(position.x, position.y, root.transform.localPosition.z)));
        AddNativeRoot(source, root, unique, omit, true);
    }
    static Vector2 OwnedMapPosition(Source source, string name, Vector2 scenePosition, Vector2 sceneSize, int zone = -1)
    {
        Room room = null;
        foreach (var candidate in source.RoomsList)
            if (candidate.Node.name == name && (zone < 0 || candidate.Zone == zone)) { room = candidate; break; }
        if (room == null) return new Vector2(-1000, -1000);
        // Native GetSceneInfo adds scene+parent local positions (not world pose).
        // Replace only the condition-owned parent values, retaining this ABI.
        Vector3 parent = source.Positions.TryGetValue(room.Parent, out var position) ? position : room.Parent.localPosition;
        Vector2 center = room.Node.localPosition + parent;
        if (room.BoundsSprite == null) return center;
        if (!Finite(sceneSize.x) || !Finite(sceneSize.y) || sceneSize.x <= 0 || sceneSize.y <= 0 || sceneSize.x == float.MaxValue || sceneSize.y == float.MaxValue)
            throw new InvalidOperationException("Map native scene size unavailable: " + name);
        Vector2 size = (Vector2)room.BoundsSprite.bounds.size * (Vector2)room.Node.localScale;
        return new Vector2(center.x - size.x / 2 + scenePosition.x / sceneSize.x * size.x,
            center.y - size.y / 2 + scenePosition.y / sceneSize.y * size.y);
    }
    static void ReadCompassAndCorpse(Source source, HashSet<Renderer> unique)
    {
        var compass = Gameplay.CompassTool;
        if (compass != null && compass.IsEquipped && !source.Map.IsLostInAbyssPreMap())
        {
            string overridden = (string)Get(source.Map, "overriddenSceneName");
            var maze = MazeController.NewestInstance;
            string sceneName = DsPortMapTransaction.CompassScene(overridden, maze != null && !maze.IsCapScene, source.Scene);
            var hero = HeroController.instance; var tilemap = source.Game.tilemap;
            if (hero == null || tilemap == null) throw new InvalidOperationException("Map compass hero/tilemap unavailable");
            int zone = !string.IsNullOrEmpty(overridden) ? (int)Get(source.Map, "overriddenSceneRegion") : -1;
            var position = OwnedMapPosition(source, sceneName, hero.transform.position, new Vector2(tilemap.width, tilemap.height), zone);
            if (position != new Vector2(-1000, -1000))
                PositionNativeRoot(source, (GameObject)Get(source.Map, "compassIcon"), position, unique);
        }
        var shade = Get(source.Map, "shadeMarker") as ShadeMarkerArrow;
        if (shade == null) throw new InvalidOperationException("Map native corpse marker unavailable");
        var corpse = OwnedMapPosition(source, source.Data.HeroCorpseScene, source.Data.HeroDeathScenePos, source.Data.HeroDeathSceneSize);
        if (corpse != new Vector2(-1000f, -1000f))
        {
            source.CorpseRoot = shade.gameObject; source.CorpseLocal = corpse;
            source.CorpseArrow = Get(shade, "arrow") as GameObject;
            source.CorpseArrowTemplate = Get(shade, "arrowPrefab") as GameObject;
            PositionNativeRoot(source, shade.gameObject, corpse, unique, source.CorpseArrow);
            source.CorpseDonor = DonorTransform(source, shade.transform);
            int arrowStart = source.Draw.Count;
            if (source.CorpseArrow != null)
            {
                source.CorpseArrowDonor = DonorTransform(source, source.CorpseArrow.transform);
                AddNativeRoot(source, source.CorpseArrow, unique, null, true);
            }
            else
            {
                if (source.CorpseArrowTemplate == null) throw new InvalidOperationException("Map corpse arrow serialized donor unavailable");
                source.CorpseArrowDonor = CopyTemplateRenderers(source, source.CorpseArrowTemplate,
                    source.CorpseDonor, Vector3.zero, Quaternion.identity, Vector3.one);
            }
            while (source.Draw.Count > arrowStart)
            {
                source.CorpseArrowDraw.Add(source.Draw[arrowStart]);
                source.Draw.RemoveAt(arrowStart);
            }
            source.Rotations.Add(new KeyValuePair<Transform, Quaternion>(
                source.CorpseArrowDonor, source.CorpseArrowDonor.localRotation));
        }
    }
    static Transform DonorTransform(Source source, Transform native)
    {
        if (source.DonorTransforms.TryGetValue(native, out var retained)) return retained;
        if (!Within(native, source.Map.transform)) throw new InvalidOperationException("Map donor transform escaped owner");
        Transform node;
        if (native == source.Map.transform)
        {
            source.Donors = new GameObject("Native Map Renderer Donors");
            var root = source.Donors;
            // One ordered resource action: a failed root/submesh retirement must
            // block font/material disposal even though the queue drains peers.
            source.Queue.Own(() =>
            {
                source.Text.RetireCold();
                if (root != null) Object.DestroyImmediate(root);
                source.Text.Clear();
            });
            source.Donors.SetActive(false);
            node = root.transform; node.position = native.position; node.rotation = native.rotation; node.localScale = native.lossyScale;
            // A renderer-only TRS root cannot represent inherited shear.
            var wanted = native.localToWorldMatrix; var actual = node.localToWorldMatrix;
            for (int i = 0; i < 16; i++) if (Mathf.Abs(wanted[i] - actual[i]) > .0001f)
                throw new InvalidOperationException("Map native root shear unadmitted");
        }
        else
        {
            var parent = DonorTransform(source, native.parent);
            node = new GameObject("Native " + native.name).transform;
            node.SetParent(parent, false); node.localPosition = native.localPosition;
            node.localRotation = native.localRotation; node.localScale = native.localScale;
        }
        node.gameObject.layer = native.gameObject.layer;
        if (source.Positions.TryGetValue(native, out var position)) node.localPosition = position;
        if (source.Scales.TryGetValue(native, out var scale)) node.localScale = scale;
        source.DonorTransforms.Add(native, node);
        return node;
    }
    static Renderer OwnedRenderer(Source source, Renderer native)
    {
        Room room = null;
        var scene = native.GetComponent<GameMapScene>();
        if (scene != null) source.RoomStates.TryGetValue(scene, out room);
        if (room != null && native == room.Renderer && (!room.Art.Visible || room.Selected == null)) return null;
        var node = DonorTransform(source, native.transform);
        var donor = CopyRenderer(source, native, node);
        if (room != null && native == room.Renderer)
        {
            var sprite = (SpriteRenderer)donor;
            sprite.sprite = room.Selected; sprite.color = room.Color; sprite.sortingOrder = 11;
            source.RoomDonors.Add(sprite, room);
        }
        return donor;
    }
    static Transform CopyTemplateRenderers(Source source, GameObject template, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var root = new GameObject("Native " + template.name).transform;
        root.SetParent(parent, false); root.localPosition = position; root.localRotation = rotation; root.localScale = scale;
        var transforms = new Dictionary<Transform, Transform> { { template.transform, root } };
        Transform TemplateTransform(Transform native)
        {
            if (transforms.TryGetValue(native, out var node)) return node;
            if (!Within(native, template.transform)) throw new InvalidOperationException("Map template renderer escaped donor");
            var ownedParent = TemplateTransform(native.parent);
            node = new GameObject("Native " + native.name).transform; node.SetParent(ownedParent, false);
            node.localPosition = native.localPosition; node.localRotation = native.localRotation; node.localScale = native.localScale;
            transforms.Add(native, node); return node;
        }
        foreach (var renderer in template.GetComponentsInChildren<Renderer>(true))
        {
            bool active = renderer.enabled;
            for (var node = renderer.transform; node != template.transform; node = node.parent) active &= node.gameObject.activeSelf;
            if (active) source.Draw.Add(CopyRenderer(source, renderer, TemplateTransform(renderer.transform)));
        }
        return root;
    }
    static Renderer CopyRenderer(Source source, Renderer native, Transform target)
    {
        if (native.GetType() != typeof(SpriteRenderer) && native.GetType() != typeof(MeshRenderer))
            throw new InvalidOperationException("Map unadmitted native renderer " + native.GetType().FullName);
        if (target == null)
        {
            target = new GameObject("Native Marker Renderer").transform;
            target.SetParent(DonorTransform(source, source.Map.transform), false);
        }
        target.gameObject.layer = native.gameObject.layer;
        Renderer donor;
        if (native is SpriteRenderer sprite)
        {
            var copy = target.gameObject.AddComponent<SpriteRenderer>();
            copy.sprite = sprite.sprite; copy.color = sprite.color; copy.flipX = sprite.flipX; copy.flipY = sprite.flipY;
            copy.drawMode = sprite.drawMode; copy.size = sprite.size; copy.tileMode = sprite.tileMode;
            copy.maskInteraction = sprite.maskInteraction; copy.spriteSortPoint = sprite.spriteSortPoint;
            donor = copy;
        }
        else
        {
            var filter = native.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            var text = native.GetComponent<TMProOld.TextMeshPro>();
            if (text != null)
            {
                // OnDisable detaches the filter, but m_mesh may remain native and
                // generated. Do not touch the lazy mesh/font/settings getters.
                var retained = Get(text, "m_mesh") as Mesh;
                if ((mesh == null || mesh.vertexCount == 0) && (retained == null || retained.vertexCount == 0))
                {
                    source.Watches.Add(source.Text.WatchCold(text));
                    var cold = source.Text.CopyCold(text, target.gameObject);
                    source.Text.Prepare(cold.gameObject);
                    var generated = source.Text.GenerateCold(cold);
                    var main = cold.GetComponent<Renderer>();
                    var propertyBlock = new MaterialPropertyBlock(); native.GetPropertyBlock(propertyBlock);
                    foreach (var renderer in generated)
                    {
                        renderer.SetPropertyBlock(propertyBlock);
                        source.RendererSources[renderer] = native;
                        source.PropertyBlocks[renderer] = propertyBlock;
                        if (renderer != main) source.Draw.Add(renderer);
                    }
                    return main;
                }
                mesh = (Mesh)DsPortMapTransaction.TextGeometry(mesh != null && mesh.vertexCount != 0 ? mesh : null, retained, native.name);
            }
            if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("Map native geometry unavailable: " + native.name);
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            donor = target.gameObject.AddComponent<MeshRenderer>();
        }
        donor.sharedMaterials = native.sharedMaterials;
        if (native is SpriteRenderer conditional && source.Materials.TryGetValue(conditional, out var material)) donor.sharedMaterial = material;
        if (donor.sharedMaterial == null) throw new InvalidOperationException("Map native donor material unavailable: " + native.name);
        donor.sortingLayerID = native.sortingLayerID; donor.sortingOrder = native.sortingOrder;
        var block = new MaterialPropertyBlock(); native.GetPropertyBlock(block); donor.SetPropertyBlock(block);
        source.RendererSources[donor] = native;
        source.PropertyBlocks[donor] = block;
        return donor;
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool VisibleBelow(Source source, Renderer renderer, Transform root)
    {
        var worldOnly = renderer.GetComponent<DisplayOnWorldMapOnly>();
        var roomNative = renderer.GetComponent<GameMapScene>();
        if (roomNative != null && source.RoomStates.TryGetValue(roomNative, out var ownRoom))
        { if (!ownRoom.Art.Visible || ownRoom.Selected == null) return false; }
        else if (worldOnly != null)
        {
            var parent = renderer.GetComponentInParent<GameMapScene>(true);
            if (parent != null && (!source.RoomStates.TryGetValue(parent, out var room) || (!room.Art.Mapped && parent.InitialState == GameMapScene.States.Hidden))) return false;
        }
        else if (!renderer.enabled) return false;
        for (var node = renderer.transform; ; node = node.parent)
        {
            if (node == null) return false;
            if (source.RootOverrides.TryGetValue(node, out bool activeRoot) && !activeRoot) return false;
            var pin = node.GetComponent<MapPin>();
            if (pin != null)
            {
                if (source.PinVisibility == MapPin.PinVisibilityStates.None || !PinCanBeActive(source, pin, new HashSet<MapPin>())) return false;
            }
            else if (node != root && !source.RootOverrides.ContainsKey(node) && !node.gameObject.activeSelf) return false;
            if (node == root) return true;
        }
    }
    static void AdmitRenderer(Renderer renderer)
    {
        if (renderer.GetType() != typeof(SpriteRenderer) && renderer.GetType() != typeof(MeshRenderer))
            throw new InvalidOperationException("Map unadmitted native renderer " + renderer.GetType().Name);
        if (renderer.sharedMaterial == null) throw new InvalidOperationException("Map native material unavailable");
        if (renderer is MeshRenderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
                throw new InvalidOperationException("Map native text/mesh not initialized");
        }
    }
    void EnsureOutput(RectTransform host)
    {
        int width = Mathf.Clamp(Mathf.CeilToInt(host.rect.width), 64, 2048);
        int height = Mathf.Clamp(Mathf.CeilToInt(host.rect.height), 64, 2048);
        if (_output == null || _output.width != width || _output.height != height)
        {
            if (_output != null) { _output.Release(); Object.Destroy(_output); }
            _output = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Companion native map", filterMode = FilterMode.Bilinear };
        }
        if (!_output.IsCreated() && !_output.Create()) throw new InvalidOperationException("Map output creation failed");
        if (_image == null || _image.transform.parent != host)
        {
            if (_image != null) Object.Destroy(_image.gameObject);
            var node = new GameObject("Native Map Output", typeof(RectTransform));
            node.layer = DsPresentation.CONTENT_LAYER;
            var rect = (RectTransform)node.transform; rect.SetParent(host, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            _image = node.AddComponent<RawImage>(); _image.raycastTarget = false; _image.color = Color.white; _image.enabled = false;
        }
    }
    static void SetMarker(Source source, Transform donor, Vector3 desired)
    {
        for (int i = 0; i < source.Markers.Count; i++)
            if (ReferenceEquals(source.Markers[i].Key, donor))
            { source.Markers[i] = new KeyValuePair<Transform, Vector3>(donor, desired); donor.localPosition = desired; return; }
        throw new InvalidOperationException("Map retained marker binding unavailable");
    }
    static void SetRotation(Source source, Transform donor, Quaternion desired)
    {
        for (int i = 0; i < source.Rotations.Count; i++)
            if (ReferenceEquals(source.Rotations[i].Key, donor))
            { source.Rotations[i] = new KeyValuePair<Transform, Quaternion>(donor, desired); donor.localRotation = desired; return; }
        throw new InvalidOperationException("Map retained rotation binding unavailable");
    }
    static void RefreshMutableDonors(Source source)
    {
        foreach (var binding in source.DonorTransforms)
        {
            var native = binding.Key; var donor = binding.Value;
            if (native == null || donor == null)
                throw new InvalidOperationException("Map retained transform binding unavailable");
            if (native == source.Map.transform)
            {
                donor.position = native.position; donor.rotation = native.rotation; donor.localScale = native.lossyScale;
            }
            else
            {
                donor.localPosition = native.localPosition; donor.localRotation = native.localRotation;
                donor.localScale = native.localScale;
            }
            if (source.Positions.TryGetValue(native, out var position)) donor.localPosition = position;
            if (source.Scales.TryGetValue(native, out var scale)) donor.localScale = scale;
        }
        foreach (var binding in source.RendererSources)
        {
            var donor = binding.Key; var native = binding.Value;
            if (donor == null || native == null)
                throw new InvalidOperationException("Map retained renderer binding unavailable");
            if (!source.PropertyBlocks.TryGetValue(donor, out var block) || block == null)
                throw new InvalidOperationException("Map retained renderer property block unavailable");
            if (donor is SpriteRenderer copy && native is SpriteRenderer sprite)
            {
                copy.sprite = sprite.sprite; copy.color = sprite.color; copy.flipX = sprite.flipX; copy.flipY = sprite.flipY;
                copy.drawMode = sprite.drawMode; copy.size = sprite.size; copy.tileMode = sprite.tileMode;
                copy.maskInteraction = sprite.maskInteraction; copy.spriteSortPoint = sprite.spriteSortPoint;
                if (source.RoomDonors.TryGetValue(copy, out var room))
                { copy.sprite = room.Selected; copy.color = room.Color; }
            }
            block.Clear(); native.GetPropertyBlock(block); donor.SetPropertyBlock(block);
        }
    }
    void PrepareView(Source source)
    {
        source.Aspect = _output.width / (float)_output.height;
        source.Half = Mathf.Max(source.Bounds.extents.y, source.Bounds.extents.x / source.Aspect) * 1.04f / _zoom;
        if (!Finite(source.Half) || source.Half <= 0) throw new InvalidOperationException("Map framing invalid");
        source.Center = source.Bounds.center + new Vector3(_pan.x, _pan.y, 0);
        _unitsPerPixel = 2f * source.Half / _output.height;
        source.CorpseArrowVisible = false;
        if (source.CorpseRoot == null) return;
        var marker = source.CorpseRoot.transform;
        var desired = new Vector3(source.CorpseLocal.x, source.CorpseLocal.y, marker.localPosition.z);
        SetMarker(source, source.CorpseDonor, desired);
        var world = marker.parent.TransformPoint(desired);
        DsPortMapTransaction.ProjectToViewport(world.x, world.y,
            source.Center.x - source.Half * source.Aspect, source.Center.y - source.Half,
            source.Center.x + source.Half * source.Aspect, source.Center.y + source.Half,
            out float projectedX, out float projectedY);
        if (world.x == projectedX && world.y == projectedY) return;
        if (source.CorpseArrow != null && !Within(source.CorpseArrow.transform, marker))
            throw new InvalidOperationException("Map native corpse arrow escaped owner");
        world.x = projectedX; world.y = projectedY;
        var local = marker.parent.InverseTransformPoint(world);
        SetMarker(source, source.CorpseDonor, local);
        // Exact native direction-to-angle consumer; only retained adapter donors
        // move. Native initialPos, activity, quick-map and viewport stay untouched.
        float angle = (source.CorpseLocal - (Vector2)local).DirectionToAngle();
        SetRotation(source, source.CorpseArrowDonor, Quaternion.Euler(0, 0, angle));
        source.CorpseArrowVisible = true;
    }
    void BuildDrawPlan(Source source)
    {
        var bindings = new List<DrawBinding>();
        var groups = new Dictionary<SortingGroup, GroupBinding>();
        var groupQueues = new Dictionary<GroupBinding, int>();
        int stableGroup = 0;
        void AddRenderer(Renderer renderer, bool corpseArrow)
        {
            if (!source.RendererSources.TryGetValue(renderer, out var native))
                throw new InvalidOperationException("Map draw lost exact renderer donor");
            var ancestry = new List<SortingGroup>();
            for (var node = native.transform; node != null; node = node.parent)
            {
                var component = node.GetComponent<SortingGroup>();
                if (component == null || !component.enabled) continue;
                ancestry.Insert(0, component);
                if (component.sortAtRoot) break;
            }
            var groupPlan = new GroupBinding[ancestry.Count];
            for (int i = 0; i < ancestry.Count; i++)
            {
                var component = ancestry[i];
                if (!groups.TryGetValue(component, out var group))
                {
                    group = new GroupBinding { Group = component, Donor = DonorTransform(source, component.transform),
                        Stable = stableGroup++, Enabled = component.enabled, SortAtRoot = component.sortAtRoot };
                    groups.Add(component, group);
                }
                groupPlan[i] = group;
            }
            var materials = renderer.sharedMaterials;
            for (int submesh = 0; submesh < materials.Length; submesh++)
            {
                var material = materials[submesh]; if (material == null) continue;
                int queue = material.renderQueue;
                for (int i = 0; i < groupPlan.Length; i++)
                {
                    if (groupQueues.TryGetValue(groupPlan[i], out int previous) && previous != queue)
                        throw new InvalidOperationException("Map sorting group mixed native queues need concrete pass authority");
                    groupQueues[groupPlan[i]] = queue;
                }
                bindings.Add(new DrawBinding { Renderer = renderer, Sprite = renderer as SpriteRenderer,
                    Material = material, Submesh = submesh, Stable = bindings.Count, CorpseArrow = corpseArrow,
                    Groups = groupPlan, Keys = new DsPortMapDrawKey[groupPlan.Length + 1] });
            }
        }
        for (int i = 0; i < source.Draw.Count; i++) AddRenderer(source.Draw[i], false);
        for (int i = 0; i < source.CorpseArrowDraw.Count; i++) AddRenderer(source.CorpseArrowDraw[i], true);
        source.DrawPlan = bindings.ToArray();
        source.GroupPlan = new GroupBinding[groups.Count];
        foreach (var group in groups.Values) source.GroupPlan[group.Stable] = group;
    }

    static float SortDistance(Camera camera, Vector3 point, int queue)
    {
        var mode = camera.transparencySortMode;
        var axis = camera.transparencySortAxis;
        if (mode == TransparencySortMode.Default) { mode = GraphicsSettings.transparencySortMode; axis = GraphicsSettings.transparencySortAxis; }
        if (mode == TransparencySortMode.Default) mode = camera.orthographic ? TransparencySortMode.Orthographic : TransparencySortMode.Perspective;
        if (queue <= 2500) mode = camera.orthographic ? TransparencySortMode.Orthographic : TransparencySortMode.Perspective;
        var delta = point - camera.transform.position;
        float result = mode == TransparencySortMode.CustomAxis ? Vector3.Dot(delta, axis) :
            mode == TransparencySortMode.Perspective ? delta.sqrMagnitude : Vector3.Dot(delta, camera.transform.forward);
        if (!Finite(result)) throw new InvalidOperationException("Map native sorting distance unavailable");
        return result;
    }
    void RunDrawPlan(Source source, CameraState state)
    {
        var camera = state.Camera; var scratch = state.Scratch;
        state.Frame++;
        if (state.Frame == 0) { Array.Clear(state.GroupFrames, 0, state.GroupFrames.Length); state.Frame = 1; }
        scratch.Reset();
        int mask = camera.cullingMask;
        for (int bindingIndex = 0; bindingIndex < source.DrawPlan.Length; bindingIndex++)
        {
            ref var binding = ref source.DrawPlan[bindingIndex];
            if (binding.CorpseArrow && !source.CorpseArrowVisible) continue;
            var renderer = binding.Renderer; var material = binding.Material;
            if (renderer == null || material == null) throw new InvalidOperationException("Map retained draw binding unavailable");
            if ((mask & (1 << renderer.gameObject.layer)) == 0) continue;
            float depth = camera.transform.InverseTransformPoint(renderer.bounds.center).z;
            if (depth < camera.nearClipPlane || depth > camera.farClipPlane) continue;
            int queue = material.renderQueue;
            for (int keyIndex = 0; keyIndex < binding.Groups.Length; keyIndex++)
            {
                var group = binding.Groups[keyIndex];
                if (group.Group == null || group.Donor == null || group.Group.enabled != group.Enabled ||
                    group.Group.sortAtRoot != group.SortAtRoot)
                    throw new InvalidOperationException("Map retained sorting group topology changed before structural poll");
                int groupIndex = group.Stable;
                if (state.GroupFrames[groupIndex] == state.Frame && state.GroupQueues[groupIndex] != queue)
                    throw new InvalidOperationException("Map sorting group mixed native queues need concrete pass authority");
                state.GroupFrames[groupIndex] = state.Frame; state.GroupQueues[groupIndex] = queue;
                ref var key = ref binding.Keys[keyIndex];
                key.Group = group.Group;
                key.Layer = SortingLayer.GetLayerValueFromID(group.Group.sortingLayerID);
                key.Order = group.Group.sortingOrder; key.Queue = queue; key.Stable = group.Stable;
                key.Distance = keyIndex == 0 ? SortDistance(camera, group.Donor.position, queue) : 0f;
            }
            ref var rendererKey = ref binding.Keys[binding.Groups.Length];
            rendererKey.Group = null;
            rendererKey.Layer = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
            rendererKey.Order = renderer.sortingOrder; rendererKey.Queue = queue; rendererKey.Stable = binding.Stable;
            var point = binding.Sprite != null && binding.Sprite.spriteSortPoint == SpriteSortPoint.Pivot ?
                renderer.transform.position : renderer.bounds.center;
            rendererKey.Distance = binding.Groups.Length == 0 ? SortDistance(camera, point, queue) : 0f;
            scratch.Add(new DsPortMapSortSlot { Binding = bindingIndex, Keys = binding.Keys, KeyCount = binding.Keys.Length });
        }
        scratch.Sort();
        for (int i = 0; i < scratch.Count; i++)
        {
            var slot = scratch.At(i); ref var binding = ref source.DrawPlan[slot.Binding];
            state.Commands.DrawRenderer(binding.Renderer, binding.Material, binding.Submesh);
        }
    }
    void Setup(Source source, CameraState rooms, CameraState decor)
    {
        SetupCamera(source, rooms, source.Center, source.Half, source.Aspect, true);
        SetupCamera(source, decor, source.Center, source.Half, source.Aspect, false);
    }
    void SetupCamera(Source source, CameraState state, Vector3 center, float half, float aspect, bool clearColor)
    {
        var camera = state.Camera; var commands = state.Commands;
        var offset = camera.transform.position - center; offset.z = 0;
        // Only owned commands execute: native Camera.Render callbacks/buffers,
        // automatic aspect/projection and source targetTexture remain untouched.
        // Reuse the two lifetime-owned buffers; never append across frames.
        commands.Clear();
        commands.SetRenderTarget(_output);
        commands.SetViewport(new Rect(0, 0, _output.width, _output.height));
        commands.ClearRenderTarget(true, clearColor, Color.clear);
        commands.SetViewProjectionMatrices(camera.worldToCameraMatrix * Matrix4x4.Translate(offset),
            GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-half * aspect, half * aspect, -half, half, camera.nearClipPlane, camera.farClipPlane), true));
        RunDrawPlan(source, state);
        commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
    }
    public bool OnGesture(DsGesture gesture)
    {
        if (!_ready || _disposed || !_eligible) return false;
        var source = _retained.Graph;
        if (source == null || !source.Authority.Same(CurrentAuthority(source)) || !Current(source) ||
            PrimaryShowing(source) || !source.Freshness.Same(ReadConsumedInputs(source)))
        {
            Invalidate(); return false;
        }
        Vector2 point;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(source.Host, gesture.Position, DsPresentation.UiCamera, out point) || !source.Host.rect.Contains(point)) return false;
        if (gesture.Type == DsGestureType.Drag) { _pan -= gesture.Delta * _unitsPerPixel; return true; }
        if (gesture.Type == DsGestureType.Pinch) { if (Finite(gesture.Scale) && gesture.Scale > 0) _zoom = Mathf.Clamp(_zoom * gesture.Scale, .25f, 6f); return true; }
        return gesture.Type == DsGestureType.Tap;
    }
    void RetireGraph(Source source)
    {
        if (source == null || source.Queue == null) throw new InvalidOperationException("Map retained graph release unavailable");
        RestorePresentation(source);
        source.Queue.Restore();
        if (ReferenceEquals(_presented, source)) _presented = null;
        source.RoomsState = null; source.DecorState = null;
    }
    bool KeepOutgoing() => _presented != null && _image != null && _output != null && _image.enabled &&
        ReferenceEquals(_image.texture, _output) && _pendingRestore == null && !_presented.PresentationPending && DsGameData.InGame &&
        ReferenceEquals(_image.transform.parent, _presented.Host) && Current(_presented, presentationOnly: true) && !PrimaryShowing(_presented);
    void SelectionChanged()
    {
        _ready = false;
        try
        {
            _pendingRestore?.Invoke();
            _outgoing = KeepOutgoing();
            if (!_outgoing) Invalidate();
        }
        catch
        {
            _outgoing = false; _presented = null;
            if (_image != null) _image.enabled = false;
            throw;
        }
    }
    public void Invalidate()
    {
        _outgoing = false; _presented = null;
        _ready = false;
        if (_image != null) _image.enabled = false;
        _pendingRestore?.Invoke();
        _retained.Retire(_retireGraph);
    }
    public void Dispose()
    {
        if (_disposed) return;
        Invalidate();
        _frame.BeforeCompositionDestroyed -= Invalidate; _frame.SelectionChanged -= SelectionChanged;
        if (_image != null) Object.Destroy(_image.gameObject);
        if (_output != null) { _output.Release(); Object.Destroy(_output); }
        _image = null; _output = null; _disposed = true;
    }
}
#endif
