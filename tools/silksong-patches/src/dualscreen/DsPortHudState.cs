// Silksong live HUD routing only. No Unity dependency: tests replace node access,
// not this production bookkeeping. No native state besides root pose/parent and layers is writable.
using System;
using System.Collections.Generic;

public enum DsHudRole { Health, Silk, Counters, Tool, Status, Context }

// Exact Linux Silksong 1.0.29980 HUD admission contract. Unity discovery
// supplies this inventory; host tests execute these same production rules.
public sealed class DsHud29980Inventory
{
    public string[] RootNames, ExtrasChildren, ThreadChildren, SpoolChildren, ToolChildren;
    public string[] CounterChildren, CrestChildren, DeliveryChildren, BurstChildren, DripsChildren;
    public int[] ExtrasPlayMakerFsms, ExtrasPositioners, ExtrasEventRegisters, ExtrasAnimators;
    public int HealthDisplayDrivers, SilkSpools, BindOrbFrames, ToolHudIcons;
    public int CounterStacks, MoneyCounters, ShardCounters, ItemCounterTemplates, LiquidCounterTemplates;
    public int DeliveryHudIcons, CrestDeactivators, CrestAnimators, CrestParticleSystems;
    public int BurstCameraControls, BurstAnimators, BurstDisableAfterTime;
    public int DripsRootParticleSystems, DripsChildParticleSystems;
}

public static class DsHud29980Topology
{
    public static readonly string[] RootNames =
    {
        "Health", "Extras", "Thread", "Tool Icons", "Crest Get Effects",
        "Delivery Icon", "Counters", "Blue_Health_Overblue_HUD_burst",
        "Blue_Health_Overblue_HUD_drips",
    };
    static readonly string[] Extras = { "Reserve Bind", "Lava Bell HUD", "Maggot Charm" };
    static readonly string[] Thread = { "Spool" };
    static readonly string[] Spool =
        { "Bind Orb", "Thread Spool", "Spool Appear", "Bind Cancel Effects", "Curse Silk Cancel Effects" };
    static readonly string[] Tools = { "Tool Icon U", "Tool Icon N", "Tool Icon D" };
    static readonly string[] Counters =
        { "Geo Counter", "Shard Counter", "Item Counter Template", "Liquid Counter Template" };
    static readonly string[] Crest = { "Crest Change Flash", "white_light", "Pt Dots", "black_solid" };
    static readonly string[] Delivery = { "Parent", "burst_appear_generic" };
    static readonly string[] Burst = { "haze2", "particles" };
    static readonly string[] Drips = { "Blue_Health_Overblue_HUD_drips" };

    public static bool TryAdmit(DsHud29980Inventory value, out string reason)
    {
        if (value == null) return Reject("inventory missing", out reason);
        if (!Exact(value.RootNames, RootNames)) return Reject("Hud Canvas direct roots changed", out reason);
        if (!Exact(value.ExtrasChildren, Extras) || !Exact(value.ExtrasPlayMakerFsms, 1, 1, 1) ||
            !Exact(value.ExtrasPositioners, 1, 1, 1) ||
            !Exact(value.ExtrasEventRegisters, 4, 8, 3) || !Exact(value.ExtrasAnimators, 0, 1, 0))
            return Reject("Extras topology/owners changed", out reason);
        if (!Exact(value.ThreadChildren, Thread) || !Exact(value.SpoolChildren, Spool) ||
            value.SilkSpools != 1 || value.BindOrbFrames != 1)
            return Reject("Thread/Spool/Bind Orb ownership changed", out reason);
        if (!Exact(value.ToolChildren, Tools) || value.ToolHudIcons != 3)
            return Reject("Tool Icons ownership changed", out reason);
        if (!Exact(value.CounterChildren, Counters) || value.CounterStacks != 1 ||
            value.MoneyCounters != 1 || value.ShardCounters != 1 ||
            value.ItemCounterTemplates != 1 || value.LiquidCounterTemplates != 1)
            return Reject("Counters ownership changed", out reason);
        if (value.HealthDisplayDrivers != 2)
            return Reject("Health health_display ownership changed", out reason);
        if (!Exact(value.DeliveryChildren, Delivery) || value.DeliveryHudIcons != 1)
            return Reject("Delivery Icon topology/owner changed", out reason);
        if (!Exact(value.CrestChildren, Crest) || value.CrestDeactivators != 1 ||
            value.CrestAnimators != 1 || value.CrestParticleSystems != 1)
            return Reject("Crest Get Effects ownership changed", out reason);
        if (!Exact(value.BurstChildren, Burst) || value.BurstCameraControls != 1 ||
            value.BurstAnimators != 1 || value.BurstDisableAfterTime != 1)
            return Reject("overblue burst ownership changed", out reason);
        if (!Exact(value.DripsChildren, Drips) || value.DripsRootParticleSystems != 1 ||
            value.DripsChildParticleSystems != 1)
            return Reject("overblue drips ownership changed", out reason);
        reason = null;
        return true;
    }

    static bool Exact(string[] actual, string[] expected)
    {
        if (actual == null || actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++)
            if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal)) return false;
        return true;
    }

    static bool Exact(int[] actual, params int[] expected)
    {
        if (actual == null || actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
        return true;
    }

    static bool Reject(string message, out string reason) { reason = message; return false; }
}

public struct DsHudPose
{
    public float X, Y, Z, QX, QY, QZ, QW, SX, SY, SZ;
    public DsHudPose(float x, float y, float z, float qx, float qy, float qz, float qw,
                     float sx, float sy, float sz)
    { X=x; Y=y; Z=z; QX=qx; QY=qy; QZ=qz; QW=qw; SX=sx; SY=sy; SZ=sz; }
}

public struct DsHudEligibility
{
    public readonly bool Transport, Frame, Gameplay, Paused, Inventory, Transition;
    public DsHudEligibility(bool transport, bool frame, bool gameplay, bool paused, bool inventory, bool transition)
    { Transport=transport; Frame=frame; Gameplay=gameplay; Paused=paused; Inventory=inventory; Transition=transition; }
    public bool CanRoute => Transport && Frame && Gameplay && !Paused && !Inventory && !Transition;
}

public sealed class DsHudRoute
{
    public readonly string Key;
    public readonly DsHudRole Role;
    public readonly object Node;
    public readonly DsHudPose Target;
    public DsHudRoute(string key, DsHudRole role, object node, DsHudPose target)
    { Key=key; Role=role; Node=node; Target=target; }
}

public struct DsHudClone
{
    public readonly object Template, Instance;
    public DsHudClone(object template, object instance) { Template = template; Instance = instance; }
}

public interface IDsHudNodes
{
    bool Alive(object node);
    bool ActiveInHierarchy(object node);
    object Parent(object node);
    int Sibling(object node);
    DsHudPose Pose(object node);
    int Layer(object node);
    IEnumerable<object> Children(object node);
    IEnumerable<DsHudClone> NativeClones(IEnumerable<object> roots);
    void SetParent(object node, object parent);
    void SetSibling(object node, int index);
    void SetPose(object node, DsHudPose pose);
    void SetLayer(object node, int layer);
}

public sealed class DsPortHudState
{
    sealed class Original
    {
        public object Node, Parent;
        public int Sibling;
        public DsHudPose Pose;
    }

    readonly IDsHudNodes _nodes;
    readonly List<Original> _originals = new List<Original>();
    readonly Dictionary<object, int> _layers = new Dictionary<object, int>();
    object _owner, _target;
    int _layer;
    DsHudRoute[] _routes;

    public DsPortHudState(IDsHudNodes nodes) { _nodes = nodes; }
    public bool IsBound => _originals.Count != 0;

    public bool Bind(object owner, DsHudRoute[] routes, object target, int layer, DsHudEligibility eligibility)
    {
        if (!eligibility.CanRoute || owner == null || !_nodes.Alive(target) || !Valid(routes, target))
        {
            Restore();
            return false;
        }
        bool same = ReferenceEquals(owner, _owner) && Equals(target, _target) &&
                    _routes != null && routes.Length == _routes.Length;
        if (same)
            for (int i = 0; i < routes.Length; i++)
                if (!Equals(routes[i].Node, _routes[i].Node) || routes[i].Key != _routes[i].Key)
                { same = false; break; }
        if (!same)
        {
            Restore();
            foreach (var route in routes)
            {
                object parent = _nodes.Parent(route.Node);
                if (!_nodes.Alive(parent) || !_nodes.ActiveInHierarchy(parent)) return false;
            }
            // Capture every sibling and root pose before moving the first root.
            for (int i = 0; i < routes.Length; i++)
            {
                object node = routes[i].Node;
                _originals.Add(new Original { Node = node, Parent = _nodes.Parent(node),
                    Sibling = _nodes.Sibling(node), Pose = _nodes.Pose(node) });
            }
        }
        _owner = owner;
        _target = target;
        _layer = layer;
        _routes = routes;
        try { LateTick(eligibility); }
        catch { Restore(); throw; }
        return IsBound;
    }

    bool Valid(DsHudRoute[] routes, object target)
    {
        if (routes == null) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var roots = new HashSet<object>();
        var counts = new int[6];
        foreach (var route in routes)
        {
            if (route == null || string.IsNullOrEmpty(route.Key) || !keys.Add(route.Key) ||
                !_nodes.Alive(route.Node) || !roots.Add(route.Node) ||
                (int)route.Role < 0 || (int)route.Role >= counts.Length) return false;
            counts[(int)route.Role]++;
        }
        // Exact 1.0.29980 Hud Canvas topology: Health plus its two sibling
        // overblue effects, one Thread, one Counters root, one Tool Icons root,
        // one status root (Extras), and two contextual roots.
        int[] expected = { 3, 1, 1, 1, 1, 2 };
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] != expected[i]) return false;
        // Independent minimal roots only. Never route an ancestor of an owned host.
        for (object at = target; _nodes.Alive(at); at = _nodes.Parent(at))
            if (roots.Contains(at)) return false;
        foreach (var route in routes)
            for (object at = _nodes.Parent(route.Node); _nodes.Alive(at); at = _nodes.Parent(at))
                if (roots.Contains(at)) return false;
        return true;
    }

    public void LateTick(DsHudEligibility eligibility)
    {
        if (!IsBound) return;
        if (!eligibility.CanRoute || !_nodes.Alive(_target)) { Restore(); return; }
        foreach (var original in _originals)
            if (!_nodes.Alive(original.Node) || !_nodes.Alive(original.Parent) ||
                !_nodes.ActiveInHierarchy(original.Parent)) { Restore(); return; }
        // Native drivers may spawn children or reparent roots in Update. Their new
        // layers are captured before changing them, never their animated child pose.
        ReconcileLayers();
        foreach (var route in _routes)
        {
            if (!Equals(_nodes.Parent(route.Node), _target)) _nodes.SetParent(route.Node, _target);
            _nodes.SetPose(route.Node, route.Target);
            Relayer(route.Node);
        }
    }

    void ReconcileLayers()
    {
        var roots = new List<object>();
        foreach (var original in _originals)
            if (_nodes.Alive(original.Node)) roots.Add(original.Node);
        var sources = new Dictionary<object, object>();
        foreach (var clone in _nodes.NativeClones(roots))
            MapClone(clone.Instance, clone.Template, sources);
        foreach (var root in roots) CaptureLayers(root, sources);
    }

    void MapClone(object instance, object template, Dictionary<object, object> sources)
    {
        // Captured instances no longer require live template topology. CaptureLayers
        // still visits their descendants and rejects unknown private-layer nodes
        // unless a separate native clone relation supplies their provenance.
        if (!_nodes.Alive(instance) || _layers.ContainsKey(instance)) return;
        if (!_nodes.Alive(template)) throw new InvalidOperationException("HUD clone template unavailable");
        sources[instance] = template;
        // Instantiate preserves the exact template child order. The native notch
        // driver changes activation only; never infer provenance from a name/root layer.
        var children = new List<object>(_nodes.Children(instance));
        var originals = new List<object>(_nodes.Children(template));
        if (children.Count != originals.Count)
            throw new InvalidOperationException("HUD clone template topology changed");
        for (int i = 0; i < children.Count; i++) MapClone(children[i], originals[i], sources);
    }

    int OriginalLayer(object node, Dictionary<object, object> sources)
    {
        int layer;
        if (_layers.TryGetValue(node, out layer)) return layer;
        layer = _nodes.Layer(node);
        if (layer != _layer) return layer;
        object template;
        if (!sources.TryGetValue(node, out template) || Equals(template, node))
            throw new InvalidOperationException("New private-layer HUD node has no native layer provenance");
        return OriginalLayer(template, sources);
    }

    void CaptureLayers(object node, Dictionary<object, object> sources)
    {
        if (!_nodes.Alive(node)) return;
        if (!_layers.ContainsKey(node)) _layers.Add(node, OriginalLayer(node, sources));
        foreach (object child in _nodes.Children(node)) CaptureLayers(child, sources);
    }

    void Relayer(object node)
    {
        if (!_nodes.Alive(node)) return;
        if (_nodes.Layer(node) != _layer) _nodes.SetLayer(node, _layer);
        foreach (object child in _nodes.Children(node)) Relayer(child);
    }

    public void Restore()
    {
        if (!IsBound) return;
        var errors = new List<Exception>();
        // Pause/unload can arrive immediately after native Instantiate, before
        // LateUpdate. Adopt those descendants while the template snapshot exists.
        try { ReconcileLayers(); }
        catch (Exception e) { errors.Add(e); }
        // Reattach all surviving roots before restoring sibling indices. Ascending
        // original indices also preserve intervening, never-moved native siblings.
        _originals.Sort((a, b) => a.Sibling.CompareTo(b.Sibling));
        foreach (var original in _originals)
        {
            if (!_nodes.Alive(original.Node)) continue;
            try
            {
                _nodes.SetParent(original.Node, _nodes.Alive(original.Parent) ? original.Parent : null);
                _nodes.SetPose(original.Node, original.Pose);
            }
            catch (Exception e) { errors.Add(e); }
        }
        foreach (var original in _originals)
        {
            if (!_nodes.Alive(original.Node)) continue;
            try { _nodes.SetSibling(original.Node, original.Sibling); }
            catch (Exception e) { errors.Add(e); }
        }
        foreach (var pair in _layers)
        {
            if (!_nodes.Alive(pair.Key)) continue;
            try { _nodes.SetLayer(pair.Key, pair.Value); }
            catch (Exception e) { errors.Add(e); }
        }
        // If a live mutation failed, retain originals for a retry and do NOT let
        // RestoreBefore destroy its containers. Destroyed nodes aren't failures.
        if (errors.Count != 0) throw new AggregateException("Native HUD restore failed", errors);
        _originals.Clear();
        _layers.Clear();
        _routes = null;
        _owner = _target = null;
    }

    public void RestoreBefore(Action change)
    {
        Restore();
        change();
    }
}

// Silksong manager-event ownership; Unity supplies the actual current manager.
public sealed class DsHudManagerCallbacks
{
    public sealed class Subscription
    {
        public Action<bool, bool> State;
        public Action<bool> Pause;
        public Action Unloading, Finished;
    }

    readonly Func<object> _current;
    readonly Action _restore, _before, _rearm;
    object _owner;
    public bool TransitionPending { get; private set; }

    public DsHudManagerCallbacks(Func<object> current, Action restore, Action before, Action rearm)
    { _current = current; _restore = restore; _before = before; _rearm = rearm; }

    public Subscription Bind(object owner, bool transition)
    {
        _owner = owner;
        TransitionPending = owner == null || transition;
        if (TransitionPending) _before(); else _rearm();
        return new Subscription
        {
            State = (boundary, playing) =>
            {
                if (!Accept(owner)) return;
                if (boundary) Before(); else if (!playing) _restore();
            },
            Pause = paused => { if (Accept(owner) && paused) _restore(); },
            Unloading = () => { if (Accept(owner)) Before(); },
            Finished = () =>
            {
                if (!Accept(owner) || !TransitionPending) return;
                TransitionPending = false;
                _rearm(); // never binds; native readiness remains independently required
            },
        };
    }

    bool Accept(object owner) => owner != null && ReferenceEquals(owner, _owner) &&
        ReferenceEquals(owner, _current());
    void Before() { TransitionPending = true; _before(); }
    public void Unbind() { _owner = null; TransitionPending = true; }
}

// Local Silksong enclosing-release decision. DirectDisplayHost remains unchanged
// and may attempt this callback after failed deactivation/content disposal.
public sealed class DsHudReleaseState
{
    readonly Action _restore, _disposeContent, _release, _retire;
    public bool ShutdownRequested { get; private set; }
    public bool Completed { get; private set; }
    public bool Pending => ShutdownRequested && !Completed;
    public bool BlocksReplacement => !Completed;
    public bool CanRoute => !ShutdownRequested && !Completed;
    public Exception LastFailure { get; private set; }

    public DsHudReleaseState(Action restore, Action disposeContent, Action release, Action retire)
    { _restore = restore; _disposeContent = disposeContent; _release = release; _retire = retire; }

    public void RequestShutdown(Action disposeHost)
    {
        if (Completed) return;
        ShutdownRequested = true;
        try { disposeHost(); }
        catch (Exception e) { LastFailure = e; }
    }

    public void ReleasePresentation()
    {
        if (Completed) return;
        ShutdownRequested = true;
        // The shared host deliberately continues after a content failure. This
        // local gate must retry restoration itself before releasing ANY parent.
        _restore();
        _disposeContent();
        _release();
        _retire();
        Completed = true;
        LastFailure = null;
    }

    public bool Retry()
    {
        if (!Pending) return Completed;
        try { ReleasePresentation(); return true; }
        catch (Exception e) { LastFailure = e; return false; }
    }
}
