// Routes the SAME Silksong live drivers/visuals into the HK-inspired status band.
// The direct-display engine is unchanged. Live objects belong to layers.HUD,
// never to cloned fleurs, tabs, status anchors or other disposable frame chrome.
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class DsPortHud
{
    readonly DsPortLayers _layers;
    readonly DsPortFrame _frame;
    readonly DsResidentUi _resident = new DsResidentUi();
    readonly DsPortHudState _state = new DsPortHudState(new UnityNodes());
    DsResidentUi.HudSources _sources;
    int _layoutRevision = -1;
    bool _disposed;

    public DsPortHud(DsPortLayers layers, DsPortFrame frame)
    {
        _layers = layers;
        _frame = frame;
        _frame.BeforeCompositionDestroyed += Restore;
    }

    // Explicit native sampling, not DsGameData.InGame alone. These are existing
    // managed getters/fields; no PlayerData injection, mutation or save editing.
    public DsHudEligibility SampleEligibility(bool transport, bool transitionBoundary)
    {
        try
        {
            var gm = GameManager.instance;
            var gc = GameCameras.SilentInstance;
            bool gameplay = DsGameData.InGame && gm != null && gc != null &&
                gm.GameState == GlobalEnums.GameState.PLAYING && !gc.IsInCinematic &&
                gc.IsHudVisible && HudCanvas.IsVisible;
            return new DsHudEligibility(transport, _frame.HudReady, gameplay,
                gm == null || gm.isPaused,
                !PlayerData.HasInstance || PlayerData.instance.isInventoryOpen,
                transitionBoundary || gm == null || gm.IsInSceneTransition || gm.IsLoadingSceneTransition);
        }
        catch (Exception e)
        {
            _resident.CapabilityGap("hud-eligibility", e.GetType().Name);
            return new DsHudEligibility(false, false, false, true, true, true);
        }
    }

    public void CheckEligibility(bool transport, bool transitionBoundary)
    {
        if (_disposed) return;
        if (!SampleEligibility(transport, transitionBoundary).CanRoute) Restore();
    }

    public void LateTick(bool transport, bool transitionBoundary)
    {
        if (_disposed) return;
        var eligibility = SampleEligibility(transport, transitionBoundary);
        if (!eligibility.CanRoute) { Restore(); return; }
        DsResidentUi.HudSources sources;
        if (!_resident.TryGetHudSources(out sources)) { Restore(); return; }
        if (_sources != sources || _layoutRevision != _frame.LayoutRevision || !_state.IsBound)
            Bind(sources, eligibility);
        else
            _state.LateTick(eligibility);
    }

    public bool Bind(DsResidentUi.HudSources sources, DsHudEligibility eligibility)
    {
        if (_disposed || sources == null) return false;
        if (_sources != sources) Restore();
        var routes = new List<DsHudRoute>();
        int toolCount = 0, toolIndex = 0;
        foreach (var root in sources.Roots) if (root.Role == DsHudRole.Tool) toolCount++;
        foreach (var source in sources.Roots)
        {
            Rect slot;
            Bounds nativeBounds;
            int index = source.Role == DsHudRole.Tool ? toolIndex++ : 0;
            if (!_frame.TryGetHudSlot(source.Role, index, toolCount, out slot) ||
                !TryNativeBounds(source.Root, out nativeBounds))
            {
                Restore();
                _resident.CapabilityGap("hud-slot-" + source.Key, "native geometry not ready; routing deferred");
                return false;
            }
            float fit = Mathf.Min(slot.width / nativeBounds.size.x, slot.height / nativeBounds.size.y) * 0.92f;
            var center = slot.center;
            // Preserve every child transform/animation. Only the moved root gets
            // a companion pose, with its native geometry uniformly fitted.
            var pose = new DsHudPose(center.x - nativeBounds.center.x * fit,
                center.y - nativeBounds.center.y * fit, 0f, 0f, 0f, 0f, 1f, fit, fit, fit);
            routes.Add(new DsHudRoute(source.Key, source.Role, source.Root, pose));
        }
        if (!_state.Bind(sources, routes.ToArray(), _layers.HUD, DsPresentation.CONTENT_LAYER, eligibility))
        {
            _sources = null;
            _resident.CapabilityGap("live-hud", "missing, duplicate or overlapping essential roots; no partial routing");
            return false;
        }
        _sources = sources;
        _layoutRevision = _frame.LayoutRevision;
        foreach (var source in sources.Roots)
            _resident.ResidentProvenance("live-" + source.Key, source.Root,
                source.Driver.GetType().Name + " instance=" + source.Driver.GetInstanceID());
        return true;
    }

    // Inspect local mesh/sprite/Graphic geometry, including currently inactive
    // native variants. Particle bounds are effects, not layout extents. Never
    // enable/normalize a renderer or edit sorting, colors, counters or drivers.
    static bool TryNativeBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        if (root == null) return false;
        bool have = false;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null)
                IncludeBounds(root, filter.transform, filter.sharedMesh.bounds, ref bounds, ref have);
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer.sprite != null)
                IncludeBounds(root, renderer.transform, renderer.sprite.bounds, ref bounds, ref have);
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            var rect = graphic.rectTransform.rect;
            IncludeBounds(root, graphic.transform, new Bounds(rect.center, rect.size), ref bounds, ref have);
        }
        return have && bounds.size.x > 0.0001f && bounds.size.y > 0.0001f;
    }

    static void IncludeBounds(Transform root, Transform child, Bounds source, ref Bounds result, ref bool have)
    {
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            {
                var point = root.InverseTransformPoint(child.TransformPoint(new Vector3(
                    x == 0 ? source.min.x : source.max.x, y == 0 ? source.min.y : source.max.y, source.center.z)));
                if (!have) { result = new Bounds(point, Vector3.zero); have = true; }
                else result.Encapsulate(point);
            }
    }

    public void Restore()
    {
        _state.Restore();
        _sources = null;
        _layoutRevision = -1;
    }

    public void RestoreBefore(Action containerChange)
    {
        _state.RestoreBefore(containerChange);
        _sources = null;
        _layoutRevision = -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Restore();
        _frame.BeforeCompositionDestroyed -= Restore;
        _disposed = true;
    }

    sealed class UnityNodes : IDsHudNodes
    {
        static Transform Node(object node) => node as Transform;
        public bool Alive(object node) => Node(node) != null;
        public bool ActiveInHierarchy(object node) => Node(node) != null && Node(node).gameObject.activeInHierarchy;
        public object Parent(object node) => Node(node).parent;
        public int Sibling(object node) => Node(node).GetSiblingIndex();
        public DsHudPose Pose(object node)
        {
            var n = Node(node); var p = n.localPosition; var q = n.localRotation; var s = n.localScale;
            return new DsHudPose(p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z);
        }
        public int Layer(object node) => Node(node).gameObject.layer;
        public IEnumerable<object> Children(object node)
        {
            var n = Node(node);
            for (int i = 0; i < n.childCount; i++) yield return n.GetChild(i);
        }
        public IEnumerable<DsHudClone> NativeClones(IEnumerable<object> roots)
        {
            // Exact managed RadialHudIcon.UpdateDisplay relation: Instantiate
            // (templateNotch, templateNotch.transform.parent), then notches.Add.
            // Read only these native fields; provenance/topology stays in the tested state.
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var templateField = typeof(RadialHudIcon).GetField("templateNotch", flags);
            var clonesField = typeof(RadialHudIcon).GetField("notches", flags);
            if (templateField == null || clonesField == null)
                throw new MissingFieldException("RadialHudIcon notch provenance unavailable");
            foreach (var root in roots)
            {
                if (!Alive(root)) continue;
                foreach (var driver in Node(root).GetComponentsInChildren<RadialHudIcon>(true))
                {
                    var template = templateField.GetValue(driver) as GameObject;
                    var clones = clonesField.GetValue(driver) as IEnumerable<GameObject>;
                    if (clones == null) continue;
                    foreach (var clone in clones)
                        if (clone != null)
                            yield return new DsHudClone(template != null ? template.transform : null, clone.transform);
                }
            }
        }
        public void SetParent(object node, object parent) => Node(node).SetParent(Node(parent), false);
        public void SetSibling(object node, int index) => Node(node).SetSiblingIndex(index);
        public void SetPose(object node, DsHudPose pose)
        {
            var n = Node(node);
            n.localPosition = new Vector3(pose.X, pose.Y, pose.Z);
            n.localRotation = new Quaternion(pose.QX, pose.QY, pose.QZ, pose.QW);
            n.localScale = new Vector3(pose.SX, pose.SY, pose.SZ);
        }
        public void SetLayer(object node, int layer) => Node(node).gameObject.layer = layer;
    }
}
#endif
