// Fade-sync composition adapted from HKDualScreen.Bottom.Layering (MIT),
// igawa6/dualsouls 5c22451435b772acde0c7e6456f9019bc1baef73.
// The Silksong source is ScreenFaderState.instance/spriteRenderer, not HK FSM states.
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TeamCherry.NestedFadeGroup;
using HutongGames.PlayMaker;
using PM = HutongGames.PlayMaker.Actions;
#endif
public interface IDsPortFade
{
    bool TryRead(out object owner, out float alpha);
    void Present(object owner, float alpha);
    void Clear();
}

// Tests replace native access only; this same decision runs before rendering
// and again at gesture time. No inferred transition/death timer invents opacity.
public sealed class DsPortFadeState
{
    readonly IDsPortFade _native;
    public DsPortFadeState(IDsPortFade native) { _native = native; }
    public bool HasVisibleFade { get; private set; }
    public void Tick(bool canPresent)
    {
        HasVisibleFade = false;
        object owner; float alpha;
        if (!canPresent || !_native.TryRead(out owner, out alpha) || owner == null ||
            float.IsNaN(alpha) || float.IsInfinity(alpha) || alpha <= 0f)
        { _native.Clear(); return; }
        _native.Present(owner, System.Math.Min(1f, alpha));
        HasVisibleFade = true;
    }
    public bool ConsumeGesture(bool canPresent)
    {
        Tick(canPresent);
        return HasVisibleFade;
    }
}

public interface IDsPortScenery
{
    bool TryRead(out object owner, out object texture, out bool nativeBackground, out bool blurred);
    void Present(object owner, object texture, int width, int height, float brightness);
    void Clear();
}

public sealed class DsPortSceneryState
{
    readonly IDsPortScenery _native;
    object _owner, _texture;
    int _firstFrame;
    public DsPortSceneryState(IDsPortScenery native) { _native = native; }
    public bool HasVisibleScenery { get; private set; }
    public static bool TrySize(bool nativeBackground, bool blurred, int panelW, int panelH, out int width, out int height)
    {
        width = height = 0;
        if (!nativeBackground || !blurred || panelW <= 0 || panelH <= 0) return false;
        long divisor = System.Math.Max(6L, (System.Math.Max(panelW, panelH) + 255L) / 256L);
        width = (int)((panelW + divisor - 1) / divisor);
        height = (int)((panelH + divisor - 1) / divisor);
        return true;
    }
    public void Tick(bool visible, int frame, int panelW, int panelH)
    {
        HasVisibleScenery = false;
        object owner, texture; bool background, blurred; int width, height;
        if (!visible || !_native.TryRead(out owner, out texture, out background, out blurred) ||
            owner == null || texture == null || !TrySize(background, blurred, panelW, panelH, out width, out height))
        {
            _native.Clear(); _owner = _texture = null;
            return;
        }
        if (!object.ReferenceEquals(owner, _owner) || !object.ReferenceEquals(texture, _texture))
        {
            _native.Clear(); _owner = owner; _texture = texture; _firstFrame = frame;
            return;
        }
        // Do not display an unrendered replacement texture. Repeated calls in
        // the acquisition frame cannot earn readiness or carry the prior output.
        if (frame <= _firstFrame) return;
        _native.Present(owner, texture, width, height, .08f);
        HasVisibleScenery = true;
    }
}

// Restoration of one family must not strand another exact native owner. Each
// family keeps its own retained lease; this combiner never retires that lease.
public static class DsPortOverlayRestoration
{
    public static void All(params System.Action[] restore)
    {
        var errors = new System.Collections.Generic.List<System.Exception>();
        foreach (var action in restore)
            try { action(); } catch (System.Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new System.AggregateException(errors);
    }
}

// A local-pose carrier maps native WORLD geometry, not an assumed square
// parent basis. Returned scales/offsets are in the companion's local plane.
public static class DsPortOverlayPlane
{
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Positive(float value) => Finite(value) && value > .0001f;
    static bool Same(float first, float second) => Finite(first) && Finite(second) &&
        System.Math.Abs(first - second) <= .0001f * System.Math.Max(1f, System.Math.Max(System.Math.Abs(first), System.Math.Abs(second)));
    public static bool SameRect(float width, float height, float pivotX, float pivotY,
        float carrierWidth, float carrierHeight, float carrierPivotX, float carrierPivotY) =>
        Same(width, carrierWidth) && Same(height, carrierHeight) && Same(pivotX, carrierPivotX) && Same(pivotY, carrierPivotY);
    public static bool TryMap(float halfHeight, float aspect, float panelW, float panelH,
        float parentX, float parentY, float centerX, float centerY,
        out float x, out float y, out float offsetX, out float offsetY)
    {
        x = y = offsetX = offsetY = 0;
        if (!Positive(halfHeight) || !Positive(aspect) || !Positive(panelW) || !Positive(panelH) ||
            !Positive(parentX) || !Positive(parentY) || !Finite(centerX) || !Finite(centerY)) return false;
        float fit = System.Math.Min(panelW / (2 * halfHeight * aspect), panelH / (2 * halfHeight)) * .94f;
        float sx = parentX * fit, sy = parentY * fit, ox = -centerX * sx, oy = -centerY * sy;
        if (!Positive(sx) || !Positive(sy) || !Finite(ox) || !Finite(oy)) return false;
        x = sx; y = sy; offsetX = ox; offsetY = oy;
        return true;
    }
}

// Carrier admission uses the actual current action target and coordinate space,
// never a name match alone. Identity is reference-based even for native wrappers.
public static class DsPortOverlayTargets
{
    public static bool BridgeReference(object cached, object local, bool active) =>
        local != null && (object.ReferenceEquals(cached, local) || (!active && cached == null));

    public static bool Local(object root, object target, bool local, System.Func<object, object> parent)
    {
        if (!local || root == null || target == null || parent == null) return false;
        for (int depth = 0; depth < 256 && target != null; depth++, target = parent(target))
            if (object.ReferenceEquals(root, target)) return true;
        return false;
    }
    public static bool Registered(object owner, System.Collections.Generic.IEnumerable<object> registrations)
    {
        if (owner == null || registrations == null) return false;
        int visits = 0;
        foreach (var registration in registrations)
        {
            if (++visits > 4096) return false;
            if (object.ReferenceEquals(owner, registration)) return true;
        }
        return false;
    }
    public static object Island(object root, object target, System.Func<object, object> parent)
    {
        if (root == null || target == null || parent == null || object.ReferenceEquals(root, target)) return null;
        for (int depth = 0; depth < 256 && target != null; depth++)
        {
            object ancestor = parent(target);
            if (object.ReferenceEquals(ancestor, root)) return target;
            target = ancestor;
        }
        return null;
    }
    public static object VisualIsland(object root, object target, System.Func<object, object> parent, System.Func<object, bool> anchored)
    {
        if (anchored == null) return null;
        object ancestor = root;
        for (int depth = 0; depth < 256; depth++)
        {
            object island = Island(ancestor, target, parent);
            if (island == null || !anchored(island)) return island;
            ancestor = island;
        }
        throw new System.InvalidOperationException("Popup island ancestry bound exceeded");
    }
    public static bool LoreVisible(object owner, object current, bool live, bool running, bool hiding, float alpha) =>
        owner != null && object.ReferenceEquals(owner, current) && live &&
        (running || hiding || (!float.IsNaN(alpha) && !float.IsInfinity(alpha) && alpha > 0f));
    public static object Message(System.Type exactType, System.Collections.Generic.IEnumerable<object> registrations,
        System.Func<object, bool> live)
    {
        if (exactType == null || registrations == null || live == null) return null;
        object owner = null;
        int visits = 0;
        foreach (var registration in registrations)
        {
            if (++visits > 4096) throw new System.InvalidOperationException("Native message registration bound exceeded");
            if (registration == null || registration.GetType() != exactType || !live(registration)) continue;
            if (owner != null && !object.ReferenceEquals(owner, registration))
                throw new System.InvalidOperationException("Native running message owner is ambiguous");
            owner = registration;
        }
        return owner;
    }
    public static bool LocalTween(object root, object target, bool local, bool worldReference, bool orientation, bool path,
        System.Func<object, object> parent) => !worldReference && !orientation && !path && Local(root, target, local, parent);
}

// Host-executable statement of the same three-field generation policy emitted
// into UIMsgProxy. It contains no Unity access; the Cecil evidence separately
// verifies that DoMsg calls the equivalent native-owned methods at exact IL sites.
public sealed class DsPortCompanionDismissState
{
    int _generation, _armed, _requested;
    public void Begin()
    {
        unchecked { _generation++; }
        if (_generation == 0) _generation = 1;
        _armed = _requested = 0;
    }
    public void Arm() { _armed = _generation; _requested = 0; }
    public int Observe() => _generation != 0 && _armed == _generation ? _generation : 0;
    public bool Request(int generation)
    {
        if (generation == 0 || _generation != generation || _armed != generation) return false;
        _requested = generation;
        return true;
    }
    public bool Consume()
    {
        if (_generation == 0 || _armed != _generation || _requested != _generation) return false;
        _armed = _requested = 0;
        return true;
    }
    public void End() { _armed = _requested = 0; }
}

// Per-field restore state runs in the concrete carrier as well as linked tests.
public sealed class DsPortOverlayParentRestore
{
    bool _attempted, _returned, _nativeRebound;
    public bool Returned => _returned;
    public void Return(object carrier, object original, System.Func<object> currentParent, System.Action writeParent)
    {
        object current = currentParent();
        if (object.ReferenceEquals(current, carrier))
        {
            // A native callback can throw after SetParent already took effect.
            // Keep that attempt so retry still restores our outstanding sibling.
            _attempted = true;
            writeParent();
        }
        else if (!_attempted || !object.ReferenceEquals(current, original)) _nativeRebound = true;
        _returned = true;
    }
    public void Sibling(System.Action restore)
    {
        if (!_returned) throw new System.InvalidOperationException("Overlay sibling awaits native parent return");
        if (!_nativeRebound) restore();
    }
}

// Originally introduced for Dialogue; the opening-credit adapter also supplies
// its own source-proven acquire/presentation/restoration callbacks. Neither
// snapshots native local pose, animation, progression or fade state.
public sealed class DsPortDialogueLease
{
    readonly System.Func<bool> _current;
    readonly System.Func<object> _acquire;
    readonly System.Action<object> _present, _restore, _release;
    object _owned;
    bool _restoring, _restored;
    public DsPortDialogueLease(System.Func<bool> current, System.Func<object> acquire,
        System.Action<object> present, System.Action<object> restore, System.Action<object> release)
    { _current = current; _acquire = acquire; _present = present; _restore = restore; _release = release; }
    public bool Pending => _owned != null;
    public bool Tick()
    {
        if (_restoring) { Restore(); return false; }
        if (!_current()) { Restore(); return false; }
        try
        {
            if (_owned == null) _owned = _acquire() ?? throw new System.InvalidOperationException("Dialogue carrier acquisition missing");
            if (!_current()) { Restore(); return false; }
            _present(_owned);
            if (!_current()) { Restore(); return false; }
            return true;
        }
        catch { if (!_restoring) Restore(); throw; }
    }
    public void Restore()
    {
        if (_owned == null) return;
        _restoring = true;
        if (!_restored) { _restore(_owned); _restored = true; }
        _release(_owned);
        _owned = null; _restored = false; _restoring = false;
    }
}

#if UNITY_ANDROID && !UNITY_EDITOR
// Native fade/scenery, Dialogue/credits, exact AreaTitle, registered tutorial/
// PowerUp messages, popup visual islands and singleton memory/Needolin lore.
// Each concrete route has its own native authority; unknown graphs are not
// admitted by a generic object-name match. Native interaction remains separate.
public sealed class DsPortOverlays : System.IDisposable
{
    readonly NativeFade _fade;
    readonly DsPortFadeState _state;
    readonly NativeScenery _scenery;
    readonly DsPortSceneryState _sceneryState;
    readonly NativeDialogue _dialogue;
    readonly NativeOpeningCredits _credits;
    readonly NativeAreaTitle _areaTitle;
    readonly NativeTutorial _tutorial;
    readonly NativePowerUp _powerUp;
    readonly NativeSkillGet _skillGet;
    readonly NativeItems _items;
    readonly NativeLore _lore;
    bool _disposed;

    public DsPortOverlays(DsPortLayers layers)
    {
        _fade = new NativeFade(layers.Fade);
        _state = new DsPortFadeState(_fade);
        _scenery = new NativeScenery(layers.Content);
        _sceneryState = new DsPortSceneryState(_scenery);
        _dialogue = new NativeDialogue(layers.Overlays);
        _credits = new NativeOpeningCredits(layers.Overlays);
        _areaTitle = new NativeAreaTitle(layers.Overlays);
        _tutorial = new NativeTutorial(layers.Overlays);
        _powerUp = new NativePowerUp(layers.Overlays);
        _skillGet = new NativeSkillGet(layers.Overlays);
        _items = new NativeItems(layers.Overlays);
        _lore = new NativeLore(layers.Overlays);
    }

    public void LateTick(bool visible, bool gameplayScenery = false)
    {
        if (_disposed) return;
        _state.Tick(visible);
        _dialogue.Tick(visible);
        _credits.Tick(visible);
        _areaTitle.Tick(visible);
        _tutorial.Tick(visible);
        _powerUp.Tick(visible);
        _skillGet.Tick(visible);
        _items.Tick(visible);
        _lore.Tick(visible);
        _sceneryState.Tick(visible && gameplayScenery, UnityEngine.Time.frameCount,
            (int)DsPresentation.PanelW, (int)DsPresentation.PanelH);
    }

    public void ClearScenery()
    {
        if (!_disposed) _sceneryState.Tick(false, UnityEngine.Time.frameCount, 0, 0);
    }

    public bool OnGesture(DsGesture gesture, bool visible)
    {
        if (_disposed) return false;
        if (_state.ConsumeGesture(visible)) return true;
        if (_tutorial.ConsumeGesture(gesture, visible)) return true;
        if (_powerUp.ConsumeGesture(gesture, visible)) return true;
        if (_skillGet.ConsumeGesture(gesture, visible)) return true;
        if (!_items.Tick(visible) && _items.Pending) return true;
        if (!_lore.Tick(visible) && _lore.Pending) return true;
        bool dialogue = _dialogue.ConsumeGesture(gesture, visible);
        // Attribution is not modal. Only a failed, still-owned route is a barrier.
        bool creditBarrier = !_credits.Tick(visible) && _credits.Pending;
        bool titleBarrier = !_areaTitle.Tick(visible) && _areaTitle.Pending;
        return dialogue || creditBarrier || titleBarrier;
    }

    public void RestoreNative()
    {
        if (!_disposed) DsPortOverlayRestoration.All(_dialogue.Restore, _credits.Restore, _areaTitle.Restore, _tutorial.Restore, _powerUp.Restore, _skillGet.Restore, _items.Restore, _lore.Restore);
    }

    public void Dispose()
    {
        if (_disposed) return;
        RestoreNative();
        _state.Tick(false);
        ClearScenery();
        _fade.Dispose();
        _scenery.Dispose();
        _disposed = true;
    }

    sealed class NativeDialogue
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo Instance = typeof(DialogueBox).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        readonly RectTransform _target;
        readonly DsResidentUi _resident = new DsResidentUi();
        DialogueBox _owner;
        GameCameras _cameras;
        DsPortDialogueLease _lease;
        bool _visible;
        sealed class Clip
        {
            public TextMeshProClipRect Driver;
            public Transform Relative, Anchor;
            public Vector2 Min, Max;
            public bool RelativeRestored, MinRestored, MaxRestored;
        }
        sealed class Retained
        {
            public Transform Root, Parent, Carrier;
            public int Sibling;
            public bool ParentRestored;
            public readonly DsPortOverlayParentRestore ParentRestore = new DsPortOverlayParentRestore();
            public readonly Dictionary<GameObject, int> Layers = new Dictionary<GameObject, int>();
            public readonly List<Clip> Clips = new List<Clip>();
            public readonly Dictionary<NestedFadeGroupBase, NestedFadeGroup> FadeParents = new Dictionary<NestedFadeGroupBase, NestedFadeGroup>();
            public readonly DsPortMapRestoreQueue Presentation = new DsPortMapRestoreQueue();
        }
        public NativeDialogue(RectTransform target) { _target = target; }
        static FieldInfo Field(object value, string name)
        {
            for (var type = value.GetType(); type != null; type = type.BaseType)
            { var field = type.GetField(name, Fields | BindingFlags.DeclaredOnly); if (field != null) return field; }
            throw new InvalidOperationException("Dialogue native field unavailable: " + name);
        }
        static object Get(object value, string name) => Field(value, name).GetValue(value);
        static bool Within(Transform node, Transform root) => node != null && root != null && (node == root || node.IsChildOf(root));
        bool Current()
        {
            var game = GameManager.SilentInstance;
            return _visible && _target != null && _target.gameObject.activeInHierarchy && _owner != null &&
                ReferenceEquals(Instance.GetValue(null), _owner) && ReferenceEquals(GameCameras.SilentInstance, _cameras) &&
                game != null && !game.IsInSceneTransition && _owner.gameObject.activeInHierarchy &&
                ((bool)Get(_owner, "isBoxOpen") || (bool)Get(_owner, "isDialogueRunning"));
        }
        public bool Tick(bool visible)
        {
            _visible = visible;
            try
            {
                if (_lease != null && !Current()) { Restore(); return false; }
                if (_lease == null)
                {
                    _owner = Instance?.GetValue(null) as DialogueBox; _cameras = GameCameras.SilentInstance;
                    if (!Current()) return false;
                    _lease = new DsPortDialogueLease(Current, Acquire, Present, RestorePresentation, Release);
                }
                return _lease.Tick();
            }
            catch (Exception e)
            {
                _resident.CapabilityGap("native-dialogue", e.GetBaseException().Message);
                return false;
            }
        }
        public bool ConsumeGesture(DsGesture gesture, bool visible)
        {
            if (!Tick(visible)) return _lease != null && _lease.Pending;
            // Exact DialogueBox.Update input guard and native dispatch; one tap
            // advances/reveals, never starts or ends a conversation from browsing.
            if (gesture.Type == DsGestureType.Tap && Current() && (bool)Get(_owner, "isDialogueRunning") &&
                ((bool)Get(_owner, "isPrintingText") || (bool)Get(_owner, "waitingToAdvance")))
                typeof(DialogueBox).GetMethod("AdvanceConversation", Fields).Invoke(_owner, null);
            return true;
        }
        object Acquire()
        {
            var root = _owner.transform; var parent = root.parent;
            var camera = _cameras != null ? _cameras.hudCamera : null;
            var group = Get(_owner, "group") as NestedFadeGroupBase;
            var text = Get(_owner, "textMesh") as Component;
            var animator = Get(_owner, "animator") as Animator;
            if (parent == null || camera == null || !camera.orthographic || camera.orthographicSize <= 0 ||
                group == null || text == null || animator == null || !Within(group.transform, root) || !Within(text.transform, root) || !Within(animator.transform, root))
                throw new InvalidOperationException("Dialogue exact native parent/camera/driver graph unavailable");
            if (Quaternion.Angle(parent.rotation, Quaternion.identity) > .001f || Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > .001f ||
                Quaternion.Angle(_target.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Dialogue native XY coordinate basis unadmitted");
            var retained = new Retained { Root = root, Parent = parent, Sibling = root.GetSiblingIndex() };
            foreach (var fade in root.GetComponentsInChildren<NestedFadeGroupBase>(true)) retained.FadeParents.Add(fade, fade.ParentGroup);
            foreach (var clip in root.GetComponentsInChildren<TextMeshProClipRect>(true))
                retained.Clips.Add(new Clip { Driver = clip, Relative = Get(clip, "relativeTo") as Transform, Min = (Vector2)Get(clip, "min"), Max = (Vector2)Get(clip, "max") });
            // Empty carrier stays under original ancestry: native automatic fade
            // resolution still finds the same parent, without an override write.
            var carrier = new GameObject("Native Dialogue Coordinate Carrier").transform;
            try { carrier.SetParent(retained.Parent, false); retained.Carrier = carrier; return retained; }
            catch { UnityEngine.Object.Destroy(carrier.gameObject); throw; }
        }
        void Present(object value)
        {
            var retained = (Retained)value;
            if (!Current() || retained.Root == null || retained.Parent == null || retained.Carrier == null || !retained.Parent.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Dialogue retained owner/parent unavailable");
            if (retained.Root.parent != retained.Parent && retained.Root.parent != retained.Carrier)
                throw new InvalidOperationException("Dialogue native root was rebound outside owned carrier");
            var camera = _cameras.hudCamera; var basis = retained.Parent.lossyScale;
            if (basis.x <= .0001f || basis.y <= .0001f || basis.z <= .0001f || camera.aspect <= 0)
                throw new InvalidOperationException("Dialogue native parent scale unavailable");
            float height = 2 * camera.orthographicSize / basis.y;
            float width = 2 * camera.orthographicSize * camera.aspect / basis.x;
            float fit = Mathf.Min(_target.rect.width / width, _target.rect.height / height) * .94f;
            var scale = _target.lossyScale;
            if (float.IsNaN(fit) || float.IsInfinity(fit) || fit <= 0 || scale.x <= 0 || scale.y <= 0)
                throw new InvalidOperationException("Dialogue companion fit unavailable");
            var center = retained.Parent.InverseTransformPoint(camera.transform.position);
            retained.Carrier.rotation = Quaternion.identity;
            retained.Carrier.localScale = new Vector3(fit * scale.x / basis.x, fit * scale.y / basis.y, 1 / basis.z);
            retained.Carrier.position = _target.TransformPoint(new Vector3(_target.rect.center.x - center.x * fit, _target.rect.center.y - center.y * fit, 0));
            foreach (var node in retained.Root.GetComponentsInChildren<Transform>(true))
            {
                var go = node.gameObject;
                if (!retained.Layers.ContainsKey(go))
                {
                    int layer = go.layer; retained.Layers.Add(go, layer);
                    retained.Presentation.Change(() => { if (go != null) go.layer = layer; }, () => go.layer = DsPresentation.OVERLAY_LAYER);
                }
                else go.layer = DsPresentation.OVERLAY_LAYER;
            }
            if (retained.Root.parent != retained.Carrier) retained.Root.SetParent(retained.Carrier, false);
            foreach (var fade in retained.FadeParents)
                if (fade.Key != null && fade.Key.ParentGroup != fade.Value) throw new InvalidOperationException("Dialogue native fade ancestry changed");
            foreach (var clip in retained.Clips)
            {
                if (clip.Driver == null) continue;
                if (clip.Anchor == null)
                {
                    // Native clip driver remains enabled. Its fixed world-space
                    // offsets are expressed in the carrier's explicit XY mapping.
                    clip.Anchor = new GameObject("Native Dialogue Clip Anchor").transform;
                    clip.Anchor.SetParent(retained.Carrier, false);
                    var saved = clip;
                    retained.Presentation.Change(() => { if (saved.Driver != null) Field(saved.Driver, "relativeTo").SetValue(saved.Driver, saved.Relative); saved.RelativeRestored = true; }, () => { });
                    retained.Presentation.Change(() => { if (saved.Driver != null) Field(saved.Driver, "min").SetValue(saved.Driver, saved.Min); saved.MinRestored = true; }, () => { });
                    retained.Presentation.Change(() => { if (saved.Driver != null) Field(saved.Driver, "max").SetValue(saved.Driver, saved.Max); saved.MaxRestored = true; }, () => { });
                    retained.Presentation.Change(() =>
                    {
                        if (!saved.RelativeRestored || !saved.MinRestored || !saved.MaxRestored) throw new InvalidOperationException("Dialogue clip awaits field restoration");
                        RefreshClip(saved);
                    }, () => { });
                }
                Transform relative = clip.Relative;
                if (relative == null || Within(relative, retained.Root))
                    Field(clip.Driver, "relativeTo").SetValue(clip.Driver, relative);
                else
                {
                    clip.Anchor.localPosition = retained.Parent.InverseTransformPoint(relative.position);
                    Field(clip.Driver, "relativeTo").SetValue(clip.Driver, clip.Anchor);
                }
                var ratio = new Vector2(fit * scale.x / basis.x, fit * scale.y / basis.y);
                Field(clip.Driver, "min").SetValue(clip.Driver, Vector2.Scale(clip.Min, ratio));
                Field(clip.Driver, "max").SetValue(clip.Driver, Vector2.Scale(clip.Max, ratio));
                RefreshClip(clip);
            }
        }
        static void RefreshClip(Clip clip)
        {
            // Exact native driver computes its own property blocks after our
            // coordinate mapping, and again after restoration before primary draw.
            if (clip.Driver != null && clip.Driver.isActiveAndEnabled)
                typeof(TextMeshProClipRect).GetMethod("LateUpdate", Fields).Invoke(clip.Driver, null);
        }
        static void RestorePresentation(object value)
        {
            var retained = (Retained)value;
            if (!retained.ParentRestored)
            {
                // Keep attempted-parent authority across exceptions after the
                // write. Sibling restoration is independent and must still retry.
                retained.ParentRestore.Return(retained.Carrier, retained.Parent != null ? retained.Parent : null,
                    () => retained.Root != null ? retained.Root.parent : null,
                    () => { if (retained.Root != null) retained.Root.SetParent(retained.Parent != null ? retained.Parent : null, false); });
                retained.ParentRestore.Sibling(() =>
                {
                    if (retained.Root != null && retained.Parent != null && retained.Root.parent == retained.Parent)
                        retained.Root.SetSiblingIndex(retained.Sibling);
                });
                retained.ParentRestored = true;
            }
            retained.Presentation.Restore();
        }
        static void Release(object value)
        {
            var retained = (Retained)value;
            if (retained.Carrier == null) return;
            if (retained.Root != null && Within(retained.Root, retained.Carrier)) throw new InvalidOperationException("Dialogue carrier still owns live native survivor");
            foreach (Transform child in retained.Carrier)
            {
                bool owned = false;
                foreach (var clip in retained.Clips) if (clip.Anchor == child) { owned = true; break; }
                if (!owned) throw new InvalidOperationException("Dialogue carrier contains an unowned survivor");
            }
            UnityEngine.Object.Destroy(retained.Carrier.gameObject);
        }
        public void Restore()
        {
            _lease?.Restore(); // Failure preserves lease and exact survivor.
            _lease = null; _owner = null; _cameras = null;
        }
    }

    // OpeningGameplayCredits.Start writes progression. We never invoke it or
    // clone its owner: pd being bound proves native Start has already run. Its
    // retained Animator advances naturally; only an empty ancestor is mapped.
    sealed class NativeOpeningCredits
    {
        static readonly FieldInfo DataField = typeof(OpeningGameplayCredits).GetField("pd", BindingFlags.Instance | BindingFlags.NonPublic);
        readonly RectTransform _target;
        readonly DsResidentUi _resident = new DsResidentUi();
        OpeningGameplayCredits _owner;
        Animator _animator;
        PlayerData _data;
        GameCameras _cameras;
        UnityEngine.Camera _camera;
        DsPortDialogueLease _lease;
        float _nextProbe;
        bool _visible;
        public bool Pending => _lease != null && _lease.Pending;
        sealed class Retained
        {
            public Transform Root, Parent, Carrier;
            public int Sibling;
            public readonly DsPortOverlayParentRestore ParentRestore = new DsPortOverlayParentRestore();
            public readonly Dictionary<GameObject, int> Layers = new Dictionary<GameObject, int>();
            public readonly Dictionary<NestedFadeGroupBase, NestedFadeGroup> FadeParents = new Dictionary<NestedFadeGroupBase, NestedFadeGroup>();
            public readonly DsPortMapRestoreQueue Presentation = new DsPortMapRestoreQueue();
        }
        public NativeOpeningCredits(RectTransform target) { _target = target; }
        static bool Within(Transform node, Transform root) => node != null && root != null && (node == root || node.IsChildOf(root));
        bool Current()
        {
            var game = GameManager.SilentInstance;
            return _visible && _target != null && _target.gameObject.activeInHierarchy && _owner != null &&
                _owner.gameObject.scene.IsValid() && _owner.gameObject.scene.isLoaded && _owner.isActiveAndEnabled &&
                _animator != null && _animator.isActiveAndEnabled && ReferenceEquals(_owner.animator, _animator) &&
                _data != null && _data.openingCreditsPlayed && ReferenceEquals(DataField?.GetValue(_owner), _data) && ReferenceEquals(PlayerData.instance, _data) &&
                _cameras != null && ReferenceEquals(GameCameras.SilentInstance, _cameras) && ReferenceEquals(_cameras.hudCamera, _camera) &&
                game != null && !game.IsInSceneTransition;
        }
        public bool Tick(bool visible)
        {
            _visible = visible;
            try
            {
                if (_lease != null && !Current()) { Restore(); return false; }
                if (_lease == null)
                {
                    if (!visible || Time.unscaledTime < _nextProbe) return false;
                    _nextProbe = Time.unscaledTime + .5f;
                    OpeningGameplayCredits found = null;
                    foreach (var candidate in UnityEngine.Object.FindObjectsByType<OpeningGameplayCredits>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        if (!candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded || !candidate.isActiveAndEnabled) continue;
                        if (DataField?.GetValue(candidate) == null) continue;
                        if (found != null) throw new InvalidOperationException("Opening credits owner is ambiguous");
                        found = candidate;
                    }
                    _owner = found; _animator = found != null ? found.animator : null;
                    _data = found != null ? DataField.GetValue(found) as PlayerData : null;
                    _cameras = GameCameras.SilentInstance; _camera = _cameras != null ? _cameras.hudCamera : null;
                    if (!Current()) return false;
                    _lease = new DsPortDialogueLease(Current, Acquire, Present, RestorePresentation, Release);
                }
                return _lease.Tick();
            }
            catch (Exception error)
            {
                _resident.CapabilityGap("native-opening-credits", error.GetBaseException().Message);
                return false;
            }
        }
        void ValidateGraph(Transform root)
        {
            if (!Within(_animator.transform, root) || _animator.applyRootMotion || _animator.runtimeAnimatorController == null ||
                _animator.GetBehaviours<StateMachineBehaviour>().Length != 0)
                throw new InvalidOperationException("Opening credits animator graph requires local-pose authority");
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Opening credits contains a missing component");
                var type = component.GetType();
                if (component == _owner || component == _animator || type == typeof(Transform) || type == typeof(RectTransform) ||
                    type == typeof(SpriteRenderer) || type == typeof(MeshRenderer) || type == typeof(MeshFilter) ||
                    type == typeof(TMProOld.TextMeshPro) || type == typeof(TMProOld.TextContainer) || type == typeof(TMProOld.TMP_SubMesh) ||
                    type == typeof(NestedFadeGroup) || ValidateFadeBridge(component, root)) continue;
                // In particular, world-coordinate FSMs, unknown animation/event
                // drivers and fixed-world clip rectangles cannot inherit this map.
                throw new InvalidOperationException("Opening credits component unadmitted: " + type.FullName);
            }
        }
        object Acquire()
        {
            var root = _owner.transform; var parent = root.parent;
            if (parent == null || _camera == null || !_camera.orthographic || _camera.orthographicSize <= 0 ||
                Quaternion.Angle(parent.rotation, Quaternion.identity) > .001f ||
                Quaternion.Angle(_camera.transform.rotation, Quaternion.identity) > .001f ||
                Quaternion.Angle(_target.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Opening credits exact native parent/camera XY basis unavailable");
            ValidateGraph(root);
            var retained = new Retained { Root = root, Parent = parent, Sibling = root.GetSiblingIndex() };
            foreach (var fade in root.GetComponentsInChildren<NestedFadeGroupBase>(true)) retained.FadeParents.Add(fade, fade.ParentGroup);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if ((_camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                    throw new InvalidOperationException("Opening credits renderer does not belong to retained HUD camera");
            var carrier = new GameObject("Native Opening Credits Coordinate Carrier").transform;
            try { carrier.SetParent(parent, false); retained.Carrier = carrier; return retained; }
            catch { UnityEngine.Object.Destroy(carrier.gameObject); throw; }
        }
        void Present(object value)
        {
            var retained = (Retained)value;
            if (!Current() || retained.Root == null || retained.Parent == null || retained.Carrier == null ||
                !retained.Parent.gameObject.activeInHierarchy || retained.Carrier.parent != retained.Parent)
                throw new InvalidOperationException("Opening credits retained ancestry replaced");
            if (retained.Root.parent != retained.Parent && retained.Root.parent != retained.Carrier)
                throw new InvalidOperationException("Opening credits native parent rebound");
            ValidateGraph(retained.Root);
            var basis = retained.Parent.lossyScale; var scale = _target.lossyScale;
            if (basis.x <= .0001f || basis.y <= .0001f || basis.z <= .0001f || _camera.aspect <= 0 ||
                Quaternion.Angle(retained.Parent.rotation, Quaternion.identity) > .001f ||
                Quaternion.Angle(_camera.transform.rotation, Quaternion.identity) > .001f ||
                Quaternion.Angle(_target.rotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("Opening credits native coordinate basis changed");
            var center = retained.Parent.InverseTransformPoint(_camera.transform.position);
            if (!_camera.orthographic || !DsPortOverlayPlane.TryMap(_camera.orthographicSize, _camera.aspect,
                _target.rect.width, _target.rect.height, basis.x, basis.y, center.x, center.y,
                out float sx, out float sy, out float ox, out float oy) ||
                float.IsNaN(scale.x) || float.IsInfinity(scale.x) || float.IsNaN(scale.y) || float.IsInfinity(scale.y) ||
                scale.x <= 0 || scale.y <= 0)
                throw new InvalidOperationException("Opening credits companion fit unavailable");
            retained.Carrier.rotation = Quaternion.identity;
            retained.Carrier.localScale = new Vector3(sx * scale.x / basis.x, sy * scale.y / basis.y, 1 / basis.z);
            retained.Carrier.position = _target.TransformPoint(new Vector3(_target.rect.center.x + ox, _target.rect.center.y + oy, 0));
            if (retained.Root.parent != retained.Carrier)
            {
                retained.Presentation.Change(() => retained.ParentRestore.Return(retained.Carrier,
                    retained.Parent != null ? retained.Parent : null,
                    () => retained.Root != null ? retained.Root.parent : null,
                    () => { if (retained.Root != null) retained.Root.SetParent(retained.Parent != null ? retained.Parent : null, false); }), () => { });
                retained.Presentation.Change(() => retained.ParentRestore.Sibling(() =>
                {
                    if (retained.Root != null && retained.Parent != null && retained.Root.parent == retained.Parent)
                        retained.Root.SetSiblingIndex(retained.Sibling);
                }), () => retained.Root.SetParent(retained.Carrier, false));
            }
            foreach (var node in retained.Root.GetComponentsInChildren<Transform>(true))
            {
                var go = node.gameObject;
                if (!retained.Layers.ContainsKey(go))
                {
                    int layer = go.layer;
                    if (layer == DsPresentation.CONTENT_LAYER || layer == DsPresentation.OVERLAY_LAYER)
                        throw new InvalidOperationException("Opening credits child has no native layer provenance");
                    retained.Layers.Add(go, layer);
                    retained.Presentation.Change(() => { if (go != null) go.layer = layer; }, () => go.layer = DsPresentation.OVERLAY_LAYER);
                }
                else go.layer = DsPresentation.OVERLAY_LAYER;
            }
            foreach (var fade in retained.FadeParents)
                if (fade.Key != null && fade.Key.ParentGroup != fade.Value) throw new InvalidOperationException("Opening credits fade ancestry changed");
        }
        static void RestorePresentation(object value) => ((Retained)value).Presentation.Restore();
        static void Release(object value)
        {
            var retained = (Retained)value;
            if (retained.Carrier == null) return;
            if ((retained.Root != null && Within(retained.Root, retained.Carrier)) || retained.Carrier.childCount != 0)
                throw new InvalidOperationException("Opening credits carrier still owns a native survivor");
            UnityEngine.Object.Destroy(retained.Carrier.gameObject);
        }
        public void Restore()
        {
            _lease?.Restore();
            _lease = null; _owner = null; _animator = null; _data = null; _cameras = null; _camera = null;
        }
    }

    const BindingFlags NativeFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static FieldInfo OverlayField(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, NativeFields | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new InvalidOperationException("Native overlay field unavailable: " + name);
    }
    static object OverlayGet(object value, string name) => OverlayField(value, name).GetValue(value);
    static bool OverlayWithin(Transform node, Transform root) => node != null && root != null && (node == root || node.IsChildOf(root));
    static bool ValidateFadeBridge(Component component, Transform root)
    {
        var type = component.GetType();
        if (type != typeof(NestedFadeGroupSpriteRenderer) && type != typeof(NestedFadeGroupTextMeshPro)) return false;
        var fade = (NestedFadeGroupBase)component;
        bool active = fade.isActiveAndEnabled;
        bool reference;
        if (type == typeof(NestedFadeGroupSpriteRenderer))
            reference = DsPortOverlayTargets.BridgeReference(OverlayGet(fade, "spriteRenderer"),
                fade.GetComponent<SpriteRenderer>(), active);
        else
            reference = DsPortOverlayTargets.BridgeReference(OverlayGet(fade, "textMesh"),
                fade.GetComponent<TMProOld.TextMeshPro>(), active) &&
                DsPortOverlayTargets.BridgeReference(OverlayGet(fade, "meshRenderer"), fade.GetComponent<MeshRenderer>(), active);
        if (!reference || !OverlayWithin(fade.transform, root) || fade.ParentOverride == null)
            throw new InvalidOperationException("Overlay native fade bridge visual authority changed");
        NestedFadeGroup expected = null;
        if (fade.ParentOverride.IsEnabled) expected = fade.ParentOverride.Value;
        else
        {
            Transform node = fade.transform;
            int depth = 0;
            for (; node != null && depth < 256; depth++, node = node.parent)
            {
                var group = node.GetComponent<NestedFadeGroup>();
                if (group != null && group.enabled) { expected = group; break; }
            }
            if (node != null && depth == 256) throw new InvalidOperationException("Overlay fade parent ancestry bound exceeded");
        }
        // Retain the native enclosing fade above an island as well as local
        // groups. A foreign override is not an admitted visual-parent authority.
        if (expected != null && !OverlayWithin(fade.transform, expected.transform))
            throw new InvalidOperationException("Overlay native fade bridge parent is outside its ancestry");
        if (fade.ParentGroup != expected && (active || fade.ParentGroup != null))
            throw new InvalidOperationException("Overlay native fade bridge parent is stale");
        return true;
    }
    static Transform ResolveTarget(PlayMakerFSM fsm, FsmOwnerDefault target)
    {
        if (target == null) return null;
        var go = target.OwnerOption == OwnerDefaultOption.UseOwner ? fsm.gameObject : target.GameObject?.Value;
        return go != null ? go.transform : null;
    }
    static void ValidateTitleActions(PlayMakerFSM fsm, Transform root)
    {
        var states = fsm.FsmStates;
        if (states == null || states.Length == 0 || states.Length > 4096)
            throw new InvalidOperationException("AreaTitle FSM has no bounded resident action graph");
        int count = 0;
        Func<object, object> parent = value => ((Transform)value).parent;
        foreach (var state in states)
        {
            if (state == null || state.Actions == null) throw new InvalidOperationException("AreaTitle FSM actions not initialised");
            foreach (var action in state.Actions)
            {
                if (action == null || ++count > 65536 || !ReferenceEquals(action.Fsm, fsm.Fsm))
                    throw new InvalidOperationException("AreaTitle FSM action owner/bound unavailable");
                var type = action.GetType();
                bool admitted = false;
                // Exact source-proven transform access, including READS. A world
                // GetPosition could feed later local motion and is not harmless.
                if (type == typeof(PM.SetPosition))
                {
                    var position = (PM.SetPosition)action;
                    admitted = DsPortOverlayTargets.Local(root, ResolveTarget(fsm, position.gameObject), position.space == Space.Self, parent);
                }
                else if (type == typeof(PM.GetPosition))
                {
                    var position = (PM.GetPosition)action;
                    admitted = DsPortOverlayTargets.Local(root, ResolveTarget(fsm, position.gameObject), position.space == Space.Self, parent);
                }
                else if (type == typeof(PM.iTweenMoveTo))
                {
                    var move = (PM.iTweenMoveTo)action;
                    if (move.transformPosition != null && move.lookAtObject != null && move.lookAtVector != null &&
                        move.orientToPath != null && move.transforms != null && move.vectors != null)
                        admitted = DsPortOverlayTargets.LocalTween(root, ResolveTarget(fsm, move.gameObject), move.space == Space.Self,
                            !move.transformPosition.IsNone || !move.lookAtObject.IsNone || !move.lookAtVector.IsNone,
                            !move.orientToPath.IsNone && move.orientToPath.Value, move.transforms.Length != 0 || move.vectors.Length != 0, parent);
                }
                else if (type == typeof(PM.SetScale))
                    admitted = DsPortOverlayTargets.Local(root, ResolveTarget(fsm, ((PM.SetScale)action).gameObject), true, parent);
                else if (type == typeof(PM.SetTextMeshProText))
                {
                    var target = ResolveTarget(fsm, ((PM.SetTextMeshProText)action).gameObject);
                    admitted = DsPortOverlayTargets.Local(root, target, true, parent) && target.GetComponent<TMProOld.TextMeshPro>() != null;
                }
                else if (type == typeof(PM.SetTextMeshProColor))
                {
                    var target = ResolveTarget(fsm, ((PM.SetTextMeshProColor)action).gameObject);
                    admitted = DsPortOverlayTargets.Local(root, target, true, parent) && target.GetComponent<TMProOld.TextMeshPro>() != null;
                }
                else if (type == typeof(PM.ActivateGameObject))
                    admitted = DsPortOverlayTargets.Local(root, ResolveTarget(fsm, ((PM.ActivateGameObject)action).gameObject), true, parent);
                // These exact retained implementations operate only on scalar
                // data / this FSM's transitions, with no scene-coordinate target.
                else if (type == typeof(PM.Wait) || type == typeof(PM.BoolTest) || type == typeof(PM.StringSwitch) ||
                    type == typeof(PM.SetBoolValue) || type == typeof(PM.GetLanguageString)) admitted = true;
                if (!admitted) throw new InvalidOperationException("AreaTitle action target/coordinate unadmitted: " + type.FullName + " in " + state.Name);
            }
        }
    }

    // The event/controller objects are not the visual. Awake/Start and every FSM
    // action remain native-owned; an uninitialised exact singleton retries on Tick.
    sealed class NativeAreaTitle
    {
        readonly RectTransform _target;
        readonly DsResidentUi _resident = new DsResidentUi();
        AreaTitle _owner;
        GameCameras _cameras;
        UnityEngine.Camera _camera;
        DsPortDialogueLease _lease;
        bool _visible;
        public bool Pending => _lease != null && _lease.Pending;
        public NativeAreaTitle(RectTransform target) { _target = target; }
        bool Current()
        {
            var game = GameManager.SilentInstance;
            return _visible && _target != null && _target.gameObject.activeInHierarchy && _owner != null &&
                ReferenceEquals(ManagerSingleton<AreaTitle>.UnsafeInstance, _owner) && _owner.Initialised &&
                _owner.isActiveAndEnabled && _owner.gameObject.scene.IsValid() && _owner.gameObject.scene.isLoaded &&
                _cameras != null && ReferenceEquals(GameCameras.SilentInstance, _cameras) &&
                _camera != null && ReferenceEquals(_cameras.hudCamera, _camera) && game != null && !game.IsInSceneTransition;
        }
        void Validate(Transform root)
        {
            bool control = false;
            foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm.transform == root && fsm.FsmName == "Area Title Control") control = true;
                ValidateTitleActions(fsm, root);
            }
            if (!control) throw new InvalidOperationException("AreaTitle exact Area Title Control visual FSM unavailable; retry next overlay tick");
            NativePlaneCarrier.ValidateComponents(root, component => component == _owner || component.GetType() == typeof(PlayMakerFSM) || ValidateTween(component, root));
        }
        static bool ValidateTween(Component component, Transform root)
        {
            if (component.GetType() == typeof(iTweenFSMEvents))
            {
                var bridge = (iTweenFSMEvents)component;
                var action = bridge.itweenFSMAction;
                if (action == null || action.GetType() != typeof(PM.iTweenMoveTo) ||
                    !ReferenceEquals(OverlayGet(action, "itweenEvents"), bridge)) return false;
                foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
                    foreach (var state in fsm.FsmStates)
                        foreach (var candidate in state.Actions)
                            if (ReferenceEquals(candidate, action) && ResolveTarget(fsm, ((PM.iTweenMoveTo)action).gameObject) == bridge.transform) return true;
                return false;
            }
            if (component.GetType() != typeof(iTween)) return false;
            var tween = (iTween)component;
            var args = OverlayGet(tween, "tweenArguments") as System.Collections.Hashtable;
            if (args == null || tween.type != "move" || tween.method != "to" || !(bool)OverlayGet(tween, "isLocal") ||
                (bool)OverlayGet(tween, "physics") || OverlayGet(tween, "thisTransform") as Transform != tween.transform ||
                args["target"] as GameObject != tween.gameObject || !(args["islocal"] is bool local) || !local ||
                args.Contains("looktarget") || args.Contains("path") || (args["orienttopath"] is bool orient && orient) ||
                !Equals(args["onstart"], "iTweenOnStart") || !Equals(args["oncomplete"], "iTweenOnComplete") ||
                args.Contains("onupdate") || args.Contains("onstarttarget") || args.Contains("oncompletetarget") ||
                !(args["onstartparams"] is int id) || !Equals(args["oncompleteparams"], id)) return false;
            foreach (var bridge in tween.GetComponents<iTweenFSMEvents>())
                if (bridge.itweenID == id && ValidateTween(bridge, root)) return true;
            return false;
        }
        public bool Tick(bool visible)
        {
            _visible = visible;
            try
            {
                if (_lease != null && !Current()) { Restore(); return false; }
                if (_lease == null)
                {
                    _owner = ManagerSingleton<AreaTitle>.UnsafeInstance;
                    _cameras = GameCameras.SilentInstance; _camera = _cameras != null ? _cameras.hudCamera : null;
                    if (!Current()) return false;
                    _lease = new DsPortDialogueLease(Current,
                        () => NativePlaneCarrier.Acquire(_owner.transform, _target, _camera, Validate, "AreaTitle"),
                        value => { if (!Current()) throw new InvalidOperationException("AreaTitle replaced"); ((NativePlaneCarrier)value).Present(); },
                        value => ((NativePlaneCarrier)value).Restore(), value => ((NativePlaneCarrier)value).Release());
                }
                return _lease.Tick();
            }
            catch (Exception error) { _resident.CapabilityGap("native-area-title", error.GetBaseException().Message); return false; }
        }
        public void Restore()
        {
            _lease?.Restore();
            _lease = null; _owner = null; _cameras = null; _camera = null;
        }
    }

    // DoMsg owns the complete fade/show/wait/hide sequence. The build-time bridge
    // admits one exact-instance request only while that same coroutine wait is armed;
    // normal native skip input and every post-wait consequence remain unchanged.
    sealed class NativeTutorial
    {
        readonly NativeRegisteredMessage _route;
        public NativeTutorial(RectTransform target)
        { _route = new NativeRegisteredMessage(target, typeof(ToolTutorialMsg), Validate, "tool-tutorial"); }
        static void Validate(UIMsgProxy owner, Transform root)
        {
            NativeRegisteredMessage.LocalReference(owner, "rootFader", root, true);
            NativeRegisteredMessage.LocalReference(owner, "toolIcon", root, false);
            NativeRegisteredMessage.LocalReference(owner, "ring", root, false);
        }
        public bool Tick(bool visible) => _route.Tick(visible);
        public bool ConsumeGesture(DsGesture gesture, bool visible) => _route.ConsumeGesture(gesture, visible);
        public void Restore() => _route.Restore();
    }

    // PowerUpGetMsg's native sequence includes EvaHeal. The already-selected
    // art/text/button groups remain native; no Spawn/Setup or afterMsg replay.
    sealed class NativePowerUp
    {
        readonly NativeRegisteredMessage _route;
        public NativePowerUp(RectTransform target)
        { _route = new NativeRegisteredMessage(target, typeof(PowerUpGetMsg), Validate, "power-up"); }
        static void Validate(UIMsgProxy owner, Transform root)
        {
            foreach (string field in new[] { "prefixText", "nameText", "descTextTop", "descTextBot",
                "lineSprite", "solidSprite", "glowSprite", "promptSprite", "promptGroup" })
                NativeRegisteredMessage.LocalReference(owner, field, root, true);
            // A prompt-less native message does not require an unused group.
            // Any extant reference still must belong to this exact visual root.
            bool prompt = ((Transform)OverlayGet(owner, "promptGroup")).gameObject.activeSelf;
            NativeRegisteredMessage.LocalReference(owner, "singleGroup", root, prompt);
            NativeRegisteredMessage.LocalReference(owner, "modifierGroup", root, prompt);
            bool single = prompt && ((GameObject)OverlayGet(owner, "singleGroup")).activeSelf;
            bool modifier = prompt && ((GameObject)OverlayGet(owner, "modifierGroup")).activeSelf;
            foreach (string field in new[] { "promptButtonSingle", "promptButtonSingleText" })
                NativeRegisteredMessage.LocalReference(owner, field, root, single);
            foreach (string field in new[] { "promptButtonModifier", "promptButtonModifierText", "upModifier", "downModifier" })
                NativeRegisteredMessage.LocalReference(owner, field, root, modifier);
        }
        public bool Tick(bool visible) => _route.Tick(visible);
        public bool ConsumeGesture(DsGesture gesture, bool visible) => _route.ConsumeGesture(gesture, visible);
        public void Restore() => _route.Restore();
    }

    // SkillGetMsg.Spawn owns ToolPaneHasNew, HUD routing, the hero input blocker,
    // Setup and completion events. Route only its already-running exact instance;
    // native DoMsg retains progression and consumes the generation-bound request.
    sealed class NativeSkillGet
    {
        readonly NativeRegisteredMessage _route;
        public NativeSkillGet(RectTransform target)
        { _route = new NativeRegisteredMessage(target, typeof(SkillGetMsg), Validate, "skill-get"); }
        static void Validate(UIMsgProxy owner, Transform root)
        {
            foreach (string field in new[] { "crestGroup", "crestSprite", "crestGlowSprite",
                "skillSprite", "skillGlowSprite", "skillSilhouetteSprite", "skillIconSprite",
                "prefixText", "nameText", "descText" })
                NativeRegisteredMessage.LocalReference(owner, field, root, true);
        }
        public bool Tick(bool visible) => _route.Tick(visible);
        public bool ConsumeGesture(DsGesture gesture, bool visible) => _route.ConsumeGesture(gesture, visible);
        public void Restore() => _route.Restore();
    }

    // Common UIMsgBase native coroutine authority, not a generic visual guess.
    // Every concrete family supplies its exact type and serialized local fields.
    sealed class NativeRegisteredMessage
    {
        static readonly FieldInfo Preventers = typeof(WorldRumbleManager).GetField("rumblePreventers", NativeFields);
        static readonly FieldInfo AudioInstance = typeof(GlobalSettings.GlobalSettingsBase<GlobalSettings.Audio>).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo AudioFound = typeof(GlobalSettings.GlobalSettingsBase<GlobalSettings.Audio>).GetField("_foundInstance", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo CompanionDismissObserve = typeof(UIMsgProxy).GetMethod("DsPortCompanionDismissObserve",
            NativeFields, null, Type.EmptyTypes, null);
        static readonly MethodInfo CompanionDismiss = typeof(UIMsgProxy).GetMethod("DsPortCompanionDismissRequest",
            NativeFields, null, new[] { typeof(int) }, null);
        readonly RectTransform _target;
        readonly DsResidentUi _resident = new DsResidentUi();
        readonly Type _type;
        readonly Action<UIMsgProxy, Transform> _fields;
        readonly string _name;
        UIMsgProxy _owner;
        GameCameras _cameras;
        WorldRumbleManager _rumble;
        UnityEngine.Camera _camera;
        DsPortDialogueLease _lease;
        bool _visible, _faulted;
        public bool Pending => _lease != null && _lease.Pending;
        public NativeRegisteredMessage(RectTransform target, Type type, Action<UIMsgProxy, Transform> fields, string name)
        { _target = target; _type = type; _fields = fields; _name = name; }
        UIMsgProxy Message() => _rumble == null ? null : DsPortOverlayTargets.Message(_type,
            Preventers?.GetValue(_rumble) as IEnumerable<object>, value =>
            {
                var message = (UIMsgProxy)value;
                return message != null && message.isActiveAndEnabled &&
                    message.gameObject.scene.IsValid() && message.gameObject.scene.isLoaded;
            }) as UIMsgProxy;
        bool Current()
        {
            var game = GameManager.SilentInstance;
            return _visible && _target != null && _target.gameObject.activeInHierarchy && _owner != null &&
                _owner.GetType() == _type && _owner.isActiveAndEnabled &&
                _owner.gameObject.scene.IsValid() && _owner.gameObject.scene.isLoaded &&
                _cameras != null && ReferenceEquals(GameCameras.SilentInstance, _cameras) &&
                _camera != null && ReferenceEquals(_cameras.hudCamera, _camera) &&
                ReferenceEquals(_cameras.worldRumbleManager, _rumble) && ReferenceEquals(Message(), _owner) &&
                game != null && !game.IsInSceneTransition;
        }
        public static void LocalReference(object owner, string field, Transform root, bool required)
        {
            var value = OverlayGet(owner, field);
            Transform target = value is Component component ? component.transform : (value as GameObject)?.transform;
            if (target == null ? required : !OverlayWithin(target, root))
                throw new InvalidOperationException("Tutorial native visual reference unavailable/outside owner: " + field);
        }
        static bool LocalButton(Component component, Transform root)
        {
            if (component.GetType() != typeof(ActionButtonIcon)) return false;
            var button = (ActionButtonIcon)component;
            if ((button.label != null && !OverlayWithin(button.label.transform, root)) ||
                (button.textContainer != null && !OverlayWithin(button.textContainer.transform, root)))
                throw new InvalidOperationException("Tutorial native button label/container is outside visual owner");
            var renderer = OverlayGet(button, "sr") as SpriteRenderer;
            if (renderer != null && renderer.gameObject != button.gameObject)
                throw new InvalidOperationException("Tutorial button renderer was rebound");
            return true;
        }
        void Validate(Transform root)
        {
            LocalReference(_owner, "animator", root, true);
            _fields(_owner, root);
            LocalReference(_owner, "stop", root, false);
            // Native DoMsg passes owner.position to its UI sound. Require the
            // exact already-resident 2D prefab; never call lazy settings Get().
            var audio = AudioInstance?.GetValue(null) as GlobalSettings.Audio;
            if (audio == null || AudioFound?.GetValue(null) as bool? != true)
                throw new InvalidOperationException("Tutorial UI audio settings not resident; retry next overlay tick");
            var prefab = OverlayGet(audio, "defaultUIAudioSourcePrefab") as AudioSource;
            if (prefab == null || prefab.spatialBlend != 0f)
                throw new InvalidOperationException("Tutorial UI sound has unadmitted world-position authority");
            NativePlaneCarrier.ValidateComponents(root, component => component == _owner || LocalButton(component, root));
        }
        public bool Tick(bool visible)
        {
            _visible = visible; _faulted = false;
            try
            {
                if (_lease != null && !Current()) Restore();
                if (_lease == null)
                {
                    _cameras = GameCameras.SilentInstance;
                    _camera = _cameras != null ? _cameras.hudCamera : null;
                    _rumble = _cameras != null ? _cameras.worldRumbleManager : null;
                    _owner = null;
                    if (!visible || _rumble == null) return false;
                    _owner = Message();
                    if (!Current()) return false;
                    _lease = new DsPortDialogueLease(Current,
                        () => NativePlaneCarrier.Acquire(_owner.transform, _target, _camera, Validate, _name),
                        value => { if (!Current()) throw new InvalidOperationException("Tutorial native message changed"); ((NativePlaneCarrier)value).Present(); },
                        value => ((NativePlaneCarrier)value).Restore(), value => ((NativePlaneCarrier)value).Release());
                }
                return _lease.Tick();
            }
            catch (Exception error)
            {
                _faulted = visible;
                _resident.CapabilityGap("native-" + _name, error.GetBaseException().Message);
                return false;
            }
        }
        public bool ConsumeGesture(DsGesture gesture, bool visible)
        {
            Tick(visible);
            bool current = false;
            try
            {
                current = Current();
                if (gesture.Type == DsGestureType.Tap && current)
                {
                    if (CompanionDismissObserve == null || CompanionDismiss == null)
                        throw new InvalidOperationException("native companion dismissal bridge unavailable");
                    int generation = (int)CompanionDismissObserve.Invoke(_owner, null);
                    // Recheck exact registration after observing the armed token;
                    // Request independently rejects generation/arm changes.
                    if (generation != 0 && Current())
                        CompanionDismiss.Invoke(_owner, new object[] { generation });
                    current = Current();
                }
            }
            catch (Exception error)
            {
                current = false; _faulted = visible;
                _resident.CapabilityGap("native-" + _name, error.GetBaseException().Message);
            }
            return Pending || _faulted || current;
        }
        public void Restore()
        {
            _lease?.Restore();
            _lease = null; _owner = null; _cameras = null; _camera = null; _rumble = null;
        }
    }

    sealed class NativeLore
    {
        readonly NativeLoreBox _memory, _needolin;
        public NativeLore(RectTransform target)
        {
            _memory = new NativeLoreBox(target, typeof(MemoryMsgBox), "fadeGroup", "appearRoutine", null, MemoryFields, null, "memory-lore");
            _needolin = new NativeLoreBox(target, typeof(NeedolinMsgBox), "boxFade", "cycleTextsRoutine", "hideRoutine", NeedolinFields, NeedolinEvent, "needolin-lore");
        }
        static void MemoryFields(MonoBehaviour owner, Transform root)
        {
            foreach (string field in new[] { "regularFolder", "whiteBackFolder" })
                NativeRegisteredMessage.LocalReference(owner, field, root, true);
            var texts = OverlayGet(owner, "textDisplays") as TMProOld.TMP_Text[];
            if (texts == null || texts.Length == 0 || texts.Length > 4096)
                throw new InvalidOperationException("Memory native text-display references unavailable or over bound");
            foreach (var text in texts)
                if (text == null || !OverlayWithin(text.transform, root))
                    throw new InvalidOperationException("Memory native text-display target is outside exact visual owner");
        }
        static void NeedolinFields(MonoBehaviour owner, Transform root)
        {
            foreach (string field in new[] { "animator", "primaryText", "primaryBackboard", "secondaryText", "secondaryBackboard" })
                NativeRegisteredMessage.LocalReference(owner, field, root, true);
        }
        static bool NeedolinEvent(Component component, MonoBehaviour owner)
        {
            if (component.GetType() != typeof(EventRegister)) return false;
            var register = (EventRegister)component;
            if (register.gameObject != owner.gameObject || register.SubscribedEvent != "DIALOGUE BOX APPEARING" ||
                OverlayGet(register, "targetFsm") as PlayMakerFSM != null || Convert.ToInt32(OverlayGet(register, "aliasEventMode")) != 0)
                throw new InvalidOperationException("Needolin native event bridge has unadmitted targets or alias");
            var callback = OverlayGet(register, "ReceivedEvent") as Delegate;
            var calls = callback?.GetInvocationList();
            if (calls == null || calls.Length == 0 || calls.Length > 4096)
                throw new InvalidOperationException("Needolin native event callback authority unavailable");
            foreach (var call in calls)
                if (!ReferenceEquals(call.Target, owner) || call.Method.DeclaringType != typeof(NeedolinMsgBox) || call.Method.Name != "HideNeedolinMsgBox")
                    throw new InvalidOperationException("Needolin native event has an unadmitted continuation target");
            return true;
        }
        public bool Pending => _memory.Pending || _needolin.Pending;
        public bool Tick(bool visible)
        {
            bool memory = _memory.Tick(visible);
            bool needolin = _needolin.Tick(visible);
            return memory && needolin;
        }
        public void Restore() => DsPortOverlayRestoration.All(_memory.Restore, _needolin.Restore);
    }

    // These singleton boxes own proximity/text selection, fade and hide timing.
    // Unlike UIMsgBase they are nonmodal and have no companion continuation call.
    sealed class NativeLoreBox
    {
        readonly RectTransform _target;
        readonly Type _type;
        readonly FieldInfo _instance;
        readonly string _fade, _running, _hiding, _name;
        readonly Action<MonoBehaviour, Transform> _fields;
        readonly Func<Component, MonoBehaviour, bool> _component;
        readonly DsResidentUi _resident = new DsResidentUi();
        MonoBehaviour _owner;
        GameCameras _cameras;
        UnityEngine.Camera _camera;
        DsPortDialogueLease _lease;
        bool _visible;
        public NativeLoreBox(RectTransform target, Type type, string fade, string running, string hiding,
            Action<MonoBehaviour, Transform> fields, Func<Component, MonoBehaviour, bool> component, string name)
        {
            _target = target; _type = type; _fade = fade; _running = running; _hiding = hiding;
            _fields = fields; _component = component; _name = name;
            _instance = type.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        }
        public bool Pending => _lease != null && _lease.Pending;
        bool Current()
        {
            var game = GameManager.SilentInstance;
            if (!_visible || _target == null || !_target.gameObject.activeInHierarchy || _owner == null ||
                _owner.GetType() != _type || !_owner.isActiveAndEnabled || !_owner.gameObject.scene.IsValid() ||
                !_owner.gameObject.scene.isLoaded || _cameras == null || !ReferenceEquals(GameCameras.SilentInstance, _cameras) ||
                _camera == null || !ReferenceEquals(_cameras.hudCamera, _camera) || game == null || game.IsInSceneTransition ||
                !ReferenceEquals(OverlayGet(_owner, "gm"), game)) return false;
            var fade = OverlayGet(_owner, _fade) as NestedFadeGroupBase;
            return DsPortOverlayTargets.LoreVisible(_owner, _instance?.GetValue(null), fade != null,
                OverlayGet(_owner, _running) != null, _hiding != null && OverlayGet(_owner, _hiding) != null,
                fade != null ? fade.AlphaSelf : 0f);
        }
        void Validate(Transform root)
        {
            NativeRegisteredMessage.LocalReference(_owner, _fade, root, true);
            _fields(_owner, root);
            NativePlaneCarrier.ValidateComponents(root, value => value == _owner || (_component != null && _component(value, _owner)));
        }
        public bool Tick(bool visible)
        {
            _visible = visible;
            try
            {
                if (_lease != null && !Current()) Restore();
                if (_lease == null)
                {
                    _owner = _instance?.GetValue(null) as MonoBehaviour;
                    _cameras = GameCameras.SilentInstance;
                    _camera = _cameras != null ? _cameras.hudCamera : null;
                    if (!Current()) return true;
                    _lease = new DsPortDialogueLease(Current,
                        () => NativePlaneCarrier.Acquire(_owner.transform, _target, _camera, Validate, _name),
                        value => ((NativePlaneCarrier)value).Present(),
                        value => ((NativePlaneCarrier)value).Restore(), value => ((NativePlaneCarrier)value).Release());
                }
                _lease.Tick();
                return true;
            }
            catch (Exception error) { _resident.CapabilityGap("native-" + _name, error.GetBaseException().Message); return false; }
        }
        public void Restore()
        {
            _lease?.Restore();
            _lease = null; _owner = null; _cameras = null; _camera = null;
        }
    }

    // Popups retain native stacking/world-position authority and auto-dismissal.
    // Each live display coroutine receives a separate exact-owner lease.
    sealed class NativeItems
    {
        static readonly FieldInfo Last = typeof(UIMsgPopupBaseBase).GetField("LastActiveMsgShared", BindingFlags.Static | BindingFlags.NonPublic);
        readonly RectTransform _target;
        readonly DsResidentUi _resident = new DsResidentUi();
        readonly List<Entry> _entries = new List<Entry>();
        bool _visible, _scanned;
        object _lastSeen;
        float _nextScan;
        sealed class Entry
        {
            public CollectableUIMsg Owner;
            public GameCameras Cameras;
            public UnityEngine.Camera Camera;
            public DsPortDialogueLease Lease;
        }
        public NativeItems(RectTransform target) { _target = target; }
        public bool Pending => _entries.Exists(entry => entry.Lease.Pending);
        bool Current(Entry entry)
        {
            var owner = entry.Owner;
            var game = GameManager.SilentInstance;
            return _visible && _target != null && _target.gameObject.activeInHierarchy && owner != null &&
                owner.GetType() == typeof(CollectableUIMsg) && owner.isActiveAndEnabled &&
                owner.gameObject.scene.IsValid() && owner.gameObject.scene.isLoaded &&
                OverlayGet(owner, "displayRoutine") != null && entry.Cameras != null &&
                ReferenceEquals(GameCameras.SilentInstance, entry.Cameras) && entry.Camera != null &&
                ReferenceEquals(entry.Cameras.hudCamera, entry.Camera) && game != null && !game.IsInSceneTransition;
        }
        public bool Tick(bool visible)
        {
            _visible = visible;
            bool healthy = true;
            // Attempt every retained owner even if a peer's restoration fails.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                try { if (!entry.Lease.Tick() && !entry.Lease.Pending) _entries.RemoveAt(i); }
                catch (Exception error) { healthy = false; Gap(error); }
            }
            if (!visible) { _scanned = false; return healthy; }
            try
            {
                object last = Last?.GetValue(null);
                if (_scanned && ReferenceEquals(last, _lastSeen) && Time.unscaledTime < _nextScan) return healthy;
                _scanned = true; _lastSeen = last; _nextScan = Time.unscaledTime + .25f;
                var candidates = UnityEngine.Object.FindObjectsByType<CollectableUIMsg>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (candidates.Length > 4096) throw new InvalidOperationException("Native popup discovery bound exceeded");
                foreach (var owner in candidates)
                {
                    if (_entries.Exists(entry => ReferenceEquals(entry.Owner, owner))) continue;
                    var cameras = GameCameras.SilentInstance;
                    var entry = new Entry { Owner = owner, Cameras = cameras, Camera = cameras != null ? cameras.hudCamera : null };
                    if (!Current(entry)) continue;
                    entry.Lease = new DsPortDialogueLease(() => Current(entry),
                        () => new NativePopupVisuals(owner, _target, entry.Camera),
                        value => ((NativePopupVisuals)value).Present(),
                        value => ((NativePopupVisuals)value).Restore(), value => ((NativePopupVisuals)value).Release());
                    _entries.Add(entry); // Retain before the first routing mutation.
                    try { entry.Lease.Tick(); }
                    catch (Exception error) { healthy = false; Gap(error); }
                }
            }
            catch (Exception error) { healthy = false; Gap(error); }
            return healthy;
        }
        void Gap(Exception error) => _resident.CapabilityGap("native-item-popup", error.GetBaseException().Message);
        public void Restore()
        {
            var restores = new List<Action>();
            foreach (var entry in _entries) restores.Add(entry.Lease.Restore);
            DsPortOverlayRestoration.All(restores.ToArray());
            _entries.Clear(); _scanned = false; _lastSeen = null;
        }
    }

    sealed class NativePopupVisuals
    {
        readonly CollectableUIMsg _owner;
        readonly RectTransform _target;
        readonly UnityEngine.Camera _camera;
        readonly List<NativePlaneCarrier> _carriers = new List<NativePlaneCarrier>();
        readonly Dictionary<TextMeshProClipRect, Transform> _clipSources = new Dictionary<TextMeshProClipRect, Transform>();
        readonly DsPortMapRestoreQueue _restore = new DsPortMapRestoreQueue();
        bool _returned;
        public NativePopupVisuals(CollectableUIMsg owner, RectTransform target, UnityEngine.Camera camera)
        { _owner = owner; _target = target; _camera = camera; }
        Transform NativeParent(Transform node)
        {
            foreach (var carrier in _carriers)
                if (ReferenceEquals(node, carrier.Root)) return carrier.OriginalParent;
            return node != null ? node.parent : null;
        }
        List<Transform> NativeChildren(Transform parent)
        {
            var children = new List<Transform>();
            foreach (Transform child in parent)
            {
                Transform native = child;
                foreach (var carrier in _carriers)
                    if (ReferenceEquals(child, carrier.Carrier)) { native = carrier.Root; break; }
                if (native != null) children.Add(native);
            }
            return children;
        }
        Transform Island(Transform target, HashSet<Transform> anchors) =>
            DsPortOverlayTargets.VisualIsland(_owner.transform, target,
                value => NativeParent((Transform)value), value => anchors.Contains((Transform)value)) as Transform;
        static bool Layout(Component component, Transform root)
        {
            if (component.GetType() != typeof(UnityEngine.UI.HorizontalLayoutGroup)) return false;
            var children = OverlayGet(component, "m_RectChildren") as IEnumerable<RectTransform>;
            if (children == null) throw new InvalidOperationException("Popup native layout target list unavailable");
            int count = 0;
            foreach (var child in children)
                if (++count > 4096 || child == null || child.parent != component.transform || !OverlayWithin(child, root))
                    throw new InvalidOperationException("Popup native layout has an external or stale child target");
            return true;
        }
        bool Component(Component component, Transform root) => component == _owner ||
            component.GetType() == typeof(UnityEngine.Rendering.SortingGroup) || Layout(component, root);
        List<Transform> Validate()
        {
            var root = _owner.transform;
            foreach (string field in new[] { "icon", "upgradeIcon", "nameText", "layoutGroup", "sortingGroup", "fadeGroup", "replaceBurstEffect" })
                NativeRegisteredMessage.LocalReference(_owner, field, root, false);
            NativePlaneCarrier.ValidateComponents(root, component => Component(component, root));
            // A root LayoutGroup is allowed. Preserve its actual native target
            // parents, then route descendants below those coordinate anchors.
            var anchors = new HashSet<Transform> { root };
            var layouts = root.GetComponentsInChildren<UnityEngine.UI.LayoutGroup>(true);
            bool changed = true;
            for (int pass = 0; changed && pass < 256; pass++)
            {
                changed = false;
                foreach (var layout in layouts)
                    if (anchors.Contains(layout.transform))
                        foreach (var child in NativeChildren(layout.transform))
                            if (child is RectTransform && anchors.Add(child)) changed = true;
            }
            if (changed) throw new InvalidOperationException("Popup layout target closure bound exceeded");
            var islands = new List<Transform>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var island = Island(renderer.transform, anchors);
                if (island == null) throw new InvalidOperationException("Popup renderer shares native stack or layout coordinate authority");
                if (!islands.Contains(island)) islands.Add(island);
            }
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                bool contained = islands.Exists(island => OverlayWithin(animator.transform, island));
                if (!contained)
                    foreach (var clip in animator.runtimeAnimatorController.animationClips)
                        if (clip != null && !clip.empty)
                            throw new InvalidOperationException("Popup animation has unproven cross-island transform bindings");
            }
            foreach (var driver in root.GetComponentsInChildren<TextMeshProClipRect>(true))
            {
                if (!_clipSources.TryGetValue(driver, out var relative))
                { relative = OverlayGet(driver, "relativeTo") as Transform; _clipSources.Add(driver, relative); }
                if (relative != null && OverlayWithin(relative, root) && !anchors.Contains(relative) &&
                    !ReferenceEquals(Island(driver.transform, anchors), Island(relative, anchors)))
                    throw new InvalidOperationException("Cross-island popup clip reference needs native coordinate authority");
            }
            return islands;
        }
        public void Present()
        {
            var islands = Validate();
            foreach (var retained in _carriers)
                if (!islands.Contains(retained.Root)) throw new InvalidOperationException("Popup native visual island targets changed; restore before reacquisition");
            foreach (var island in islands)
            {
                var carrier = _carriers.Find(value => ReferenceEquals(value.Root, island));
                if (carrier == null)
                {
                    carrier = NativePlaneCarrier.Acquire(island, _target, _camera,
                        visual => NativePlaneCarrier.ValidateComponents(visual, component => Component(component, visual)), "Item Popup Visual");
                    _carriers.Add(carrier);
                    _restore.Change(carrier.Restore, () => { });
                    _restore.Own(carrier.Release);
                }
                carrier.Present();
            }
        }
        public void Restore() { _restore.Restore(); _returned = true; }
        public void Release()
        {
            if (!_returned) throw new InvalidOperationException("Popup islands await independent restoration");
            _carriers.Clear(); _clipSources.Clear();
        }
    }

    // Same-instance local-pose carrier shared by the concrete native families.
    // Native drivers retain their exact ancestry above the empty inserted parent.
    // No native pose/active/animation/fade/progression value is written or restored.
    sealed class NativePlaneCarrier
    {
        readonly Transform _root, _parent, _carrier;
        readonly RectTransform _target;
        readonly UnityEngine.Camera _camera;
        readonly Action<Transform> _validate;
        readonly int _sibling;
        readonly bool _hadParent;
        readonly Dictionary<GameObject, int> _layers = new Dictionary<GameObject, int>();
        readonly Dictionary<NestedFadeGroupBase, NestedFadeGroup> _fades = new Dictionary<NestedFadeGroupBase, NestedFadeGroup>();
        readonly Dictionary<TextMeshProClipRect, Clip> _clips = new Dictionary<TextMeshProClipRect, Clip>();
        readonly DsPortMapRestoreQueue _restore = new DsPortMapRestoreQueue();
        readonly DsPortOverlayParentRestore _parentRestore = new DsPortOverlayParentRestore();
        bool _parentRegistered;
        public Transform Root => _root;
        public Transform OriginalParent => _parent;
        public Transform Carrier => _carrier;
        sealed class Clip
        {
            public TextMeshProClipRect Driver;
            public Transform Relative, Anchor;
            public Vector2 Min, Max;
            public bool RelativeReturned, MinReturned, MaxReturned;
        }
        NativePlaneCarrier(Transform root, RectTransform target, UnityEngine.Camera camera, Action<Transform> validate, string name)
        {
            _root = root; _parent = root.parent; _hadParent = _parent != null; _sibling = root.GetSiblingIndex();
            _target = target; _camera = camera; _validate = validate;
            foreach (var fade in root.GetComponentsInChildren<NestedFadeGroupBase>(true)) _fades.Add(fade, fade.ParentGroup);
            _carrier = (_parent is RectTransform
                ? new GameObject("Native " + name + " Coordinate Carrier", typeof(RectTransform))
                : new GameObject("Native " + name + " Coordinate Carrier")).transform;
            try
            {
                _carrier.SetParent(_parent, false);
                if (_carrier is RectTransform rect && _parent is RectTransform original)
                {
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.sizeDelta = Vector2.zero; rect.pivot = original.pivot;
                }
            }
            catch { UnityEngine.Object.Destroy(_carrier.gameObject); throw; }
        }
        public static NativePlaneCarrier Acquire(Transform root, RectTransform target, UnityEngine.Camera camera, Action<Transform> validate, string name)
        {
            if (root == null || target == null || camera == null || !camera.orthographic)
                throw new InvalidOperationException(name + " native visual/camera unavailable");
            validate(root);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if ((camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                    throw new InvalidOperationException(name + " renderer lacks retained HUD camera provenance");
            return new NativePlaneCarrier(root, target, camera, validate, name);
        }
        public static void ValidateComponents(Transform root, Func<Component, bool> family)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            if (components.Length > 65536) throw new InvalidOperationException("Overlay component bound exceeded");
            foreach (var component in components)
            {
                if (component == null) throw new InvalidOperationException("Overlay missing native component");
                var type = component.GetType();
                if (type == typeof(Animator))
                {
                    var animator = (Animator)component;
                    if (animator.applyRootMotion || animator.runtimeAnimatorController == null || animator.GetBehaviours<StateMachineBehaviour>().Length != 0)
                        throw new InvalidOperationException("Overlay animator needs local-pose/controller authority");
                    continue;
                }
                if (type == typeof(Transform) || type == typeof(RectTransform) || type == typeof(SpriteRenderer) ||
                    type == typeof(MeshRenderer) || type == typeof(MeshFilter) || type == typeof(TMProOld.TextMeshPro) ||
                    type == typeof(TMProOld.TextContainer) || type == typeof(TMProOld.TMP_SubMesh) ||
                    type == typeof(NestedFadeGroup) || type == typeof(TextMeshProClipRect) || ValidateFadeBridge(component, root) || family(component)) continue;
                throw new InvalidOperationException("Overlay component coordinate/lifecycle unadmitted: " + type.FullName);
            }
        }
        public void Present()
        {
            if (_root == null || _carrier == null || _target == null || _camera == null ||
                (_hadParent && (_parent == null || !_parent.gameObject.activeInHierarchy)) || _carrier.parent != _parent ||
                (_root.parent != _parent && _root.parent != _carrier))
                throw new InvalidOperationException("Overlay retained native ancestry unavailable");
            _validate(_root); // Current variable targets/coordinates and newly spawned drivers.
            if (_carrier is RectTransform rect && _parent is RectTransform original)
            {
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero; rect.pivot = original.pivot;
                if (!DsPortOverlayPlane.SameRect(original.rect.width, original.rect.height, original.pivot.x, original.pivot.y,
                    rect.rect.width, rect.rect.height, rect.pivot.x, rect.pivot.y))
                    throw new InvalidOperationException("Overlay carrier no longer matches native parent rect authority");
            }
            var basis = _parent != null ? _parent.lossyScale : Vector3.one;
            var center = _parent != null ? _parent.InverseTransformPoint(_camera.transform.position) : _camera.transform.position;
            var scale = _target.lossyScale;
            if ((_parent != null && Quaternion.Angle(_parent.rotation, Quaternion.identity) > .001f) ||
                Quaternion.Angle(_camera.transform.rotation, Quaternion.identity) > .001f ||
                Quaternion.Angle(_target.rotation, Quaternion.identity) > .001f || basis.z <= .0001f ||
                !_camera.orthographic || !DsPortOverlayPlane.TryMap(_camera.orthographicSize, _camera.aspect,
                    _target.rect.width, _target.rect.height, basis.x, basis.y, center.x, center.y,
                    out float sx, out float sy, out float ox, out float oy) ||
                !FinitePositive(scale.x) || !FinitePositive(scale.y) || !FinitePositive(basis.z))
                throw new InvalidOperationException("Overlay native/companion XY mapping unavailable");
            // Only the empty adapter carrier receives coordinate writes.
            _carrier.rotation = Quaternion.identity;
            _carrier.localScale = new Vector3(sx * scale.x / basis.x, sy * scale.y / basis.y, 1 / basis.z);
            _carrier.position = _target.TransformPoint(new Vector3(_target.rect.center.x + ox, _target.rect.center.y + oy, 0));
            if (!_parentRegistered)
            {
                _restore.Change(() => _parentRestore.Return(_carrier, _parent != null ? _parent : null,
                    () => _root != null ? _root.parent : null,
                    () => { if (_root != null) _root.SetParent(_parent != null ? _parent : null, false); }), () => { });
                _restore.Change(() => _parentRestore.Sibling(() =>
                {
                    if (_root != null && _root.parent == _parent) _root.SetSiblingIndex(_sibling);
                }), () => { });
                _parentRegistered = true;
            }
            if (_root.parent != _carrier) _root.SetParent(_carrier, false);
            foreach (var node in _root.GetComponentsInChildren<Transform>(true))
            {
                var go = node.gameObject;
                if (!_layers.ContainsKey(go))
                {
                    int layer = go.layer;
                    if (layer == DsPresentation.CONTENT_LAYER || layer == DsPresentation.OVERLAY_LAYER)
                        throw new InvalidOperationException("Overlay child lacks original layer provenance");
                    _layers.Add(go, layer);
                    _restore.Change(() => { if (go != null) go.layer = layer; }, () => go.layer = DsPresentation.OVERLAY_LAYER);
                }
                else go.layer = DsPresentation.OVERLAY_LAYER;
            }
            foreach (var fade in _root.GetComponentsInChildren<NestedFadeGroupBase>(true))
            {
                if (!_fades.ContainsKey(fade)) _fades.Add(fade, fade.ParentGroup);
                if (fade.ParentGroup != _fades[fade]) throw new InvalidOperationException("Overlay native fade ancestry changed");
            }
            foreach (var driver in _root.GetComponentsInChildren<TextMeshProClipRect>(true))
            {
                if (!_clips.TryGetValue(driver, out var clip))
                {
                    clip = new Clip { Driver = driver, Relative = OverlayGet(driver, "relativeTo") as Transform,
                        Min = (Vector2)OverlayGet(driver, "min"), Max = (Vector2)OverlayGet(driver, "max") };
                    _clips.Add(driver, clip);
                    var saved = clip;
                    _restore.Change(() => { if (saved.Driver != null) OverlayField(saved.Driver, "relativeTo").SetValue(saved.Driver, saved.Relative); saved.RelativeReturned = true; }, () => { });
                    _restore.Change(() => { if (saved.Driver != null) OverlayField(saved.Driver, "min").SetValue(saved.Driver, saved.Min); saved.MinReturned = true; }, () => { });
                    _restore.Change(() => { if (saved.Driver != null) OverlayField(saved.Driver, "max").SetValue(saved.Driver, saved.Max); saved.MaxReturned = true; }, () => { });
                    _restore.Change(() =>
                    {
                        if (!_parentRestore.Returned || !saved.RelativeReturned || !saved.MinReturned || !saved.MaxReturned)
                            throw new InvalidOperationException("Overlay native clip awaits coordinate restoration");
                        Refresh(saved.Driver);
                    }, () => { });
                }
                if (clip.Relative == null || OverlayWithin(clip.Relative, _root))
                    OverlayField(driver, "relativeTo").SetValue(driver, clip.Relative);
                else
                {
                    if (clip.Anchor == null)
                    {
                        clip.Anchor = new GameObject("Native Overlay Clip Anchor").transform;
                        clip.Anchor.SetParent(_carrier, false);
                    }
                    clip.Anchor.localPosition = _parent != null ? _parent.InverseTransformPoint(clip.Relative.position) : clip.Relative.position;
                    OverlayField(driver, "relativeTo").SetValue(driver, clip.Anchor);
                }
                var ratio = new Vector2(sx * scale.x / basis.x, sy * scale.y / basis.y);
                OverlayField(driver, "min").SetValue(driver, Vector2.Scale(clip.Min, ratio));
                OverlayField(driver, "max").SetValue(driver, Vector2.Scale(clip.Max, ratio));
                Refresh(driver);
            }
        }
        static bool FinitePositive(float value) => value > .0001f && !float.IsInfinity(value) && !float.IsNaN(value);
        static void Refresh(TextMeshProClipRect driver)
        {
            if (driver != null && driver.isActiveAndEnabled) typeof(TextMeshProClipRect).GetMethod("LateUpdate", NativeFields).Invoke(driver, null);
        }
        public void Restore() => _restore.Restore();
        public void Release()
        {
            if (_carrier == null) return;
            if (_root != null && OverlayWithin(_root, _carrier)) throw new InvalidOperationException("Overlay carrier retains native visual");
            foreach (Transform child in _carrier)
            {
                bool own = false;
                foreach (var clip in _clips.Values) if (clip.Anchor == child) { own = true; break; }
                if (!own) throw new InvalidOperationException("Overlay carrier contains an unowned survivor");
            }
            foreach (var clip in _clips.Values) if (clip.Anchor != null) UnityEngine.Object.Destroy(clip.Anchor.gameObject);
            UnityEngine.Object.Destroy(_carrier.gameObject);
        }
    }

    // Consume only the existing, post-LightBlur background pass. The native
    // producer remains wholly game-owned: no Camera.CopyFrom, Camera.Render,
    // activation, layer/clip/material edits or native texture release occurs.
    sealed class NativeScenery : IDsPortScenery, System.IDisposable
    {
        const System.Reflection.BindingFlags Fields = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic;
        static readonly System.Reflection.FieldInfo CameraField = typeof(LightBlurredBackground).GetField("backgroundCamera", Fields);
        static readonly System.Reflection.FieldInfo TextureField = typeof(LightBlurredBackground).GetField("renderTexture", Fields);
        static readonly System.Reflection.FieldInfo OwnerField = typeof(LightBlurredBackground).GetField("gameCameras", Fields);
        static readonly System.Reflection.FieldInfo SupportedField = typeof(LightBlur).GetField("effectIsSupported", Fields);
        static readonly System.Reflection.FieldInfo MaterialField = typeof(LightBlur).GetField("blurMaterial", Fields);
        readonly UnityEngine.RectTransform _parent;
        readonly DsResidentUi _resident = new DsResidentUi();
        UnityEngine.UI.RawImage _image;
        UnityEngine.RenderTexture _output;

        public NativeScenery(UnityEngine.RectTransform parent) { _parent = parent; }

        public bool TryRead(out object owner, out object texture, out bool nativeBackground, out bool blurred)
        {
            owner = texture = null; nativeBackground = blurred = false;
            if (CameraField == null || TextureField == null || OwnerField == null || SupportedField == null || MaterialField == null)
            {
                _resident.CapabilityGap("native-scenery", "native background/blur managed metadata unavailable");
                return false;
            }
            var current = GameCameras.SilentInstance;
            var producer = current != null ? current.GetComponent<LightBlurredBackground>() : null;
            if (producer == null || !producer.isActiveAndEnabled ||
                !object.ReferenceEquals(OwnerField.GetValue(producer), current)) return false;
            var camera = CameraField.GetValue(producer) as UnityEngine.Camera;
            var source = TextureField.GetValue(producer) as UnityEngine.RenderTexture;
            if (camera == null || !camera.isActiveAndEnabled || source == null || !source.IsCreated() ||
                camera.targetTexture != source) return false;
            var plane = BlurPlane.ClosestBlurPlane;
            var hero = HeroController.instance;
            int excluded = (1 << 5) | (1 << DsPresentation.CONTENT_LAYER) | (1 << DsPresentation.OVERLAY_LAYER);
            // Native UpdateCameraClipPlanes defines this Z slice. Require it to
            // sit behind both its authored blur plane and the actual hero plane;
            // never consume an unrestricted gameplay-camera texture.
            nativeBackground = plane != null && hero != null && plane.PlaneZ > hero.transform.position.z &&
                camera.transform.forward.z > .999f && camera.farClipPlane > camera.nearClipPlane &&
                camera.transform.position.z + camera.nearClipPlane >= plane.PlaneZ &&
                (camera.cullingMask & excluded) == 0;
            var effect = camera.GetComponent<LightBlur>();
            blurred = effect != null && effect.isActiveAndEnabled && effect.BlurPassCount > 0 &&
                (bool)SupportedField.GetValue(effect) && MaterialField.GetValue(effect) as UnityEngine.Material != null;
            owner = producer; texture = source;
            return true;
        }

        public void Present(object owner, object texture, int width, int height, float brightness)
        {
            object currentOwner, currentTexture; bool background, blurred;
            if (!TryRead(out currentOwner, out currentTexture, out background, out blurred) ||
                !background || !blurred || !object.ReferenceEquals(currentOwner, owner) ||
                !object.ReferenceEquals(currentTexture, texture))
            {
                Clear();
                throw new System.InvalidOperationException("Native scenery owner/output changed during presentation");
            }
            if (_output == null || _output.width != width || _output.height != height)
            {
                ReleaseOutput();
                _output = new UnityEngine.RenderTexture(width, height, 0, UnityEngine.RenderTextureFormat.ARGB32);
                _output.name = "Companion Native Scenery";
                _output.filterMode = UnityEngine.FilterMode.Bilinear;
                _output.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                if (!_output.Create())
                {
                    ReleaseOutput();
                    throw new System.InvalidOperationException("Companion scenery render texture creation failed");
                }
            }
            if (_image == null)
            {
                var go = new UnityEngine.GameObject("Native Scenery Backdrop");
                go.layer = DsPresentation.CONTENT_LAYER;
                var rect = go.AddComponent<UnityEngine.RectTransform>();
                rect.SetParent(_parent, false);
                rect.anchorMin = UnityEngine.Vector2.zero; rect.anchorMax = UnityEngine.Vector2.one;
                rect.offsetMin = rect.offsetMax = UnityEngine.Vector2.zero;
                _image = go.AddComponent<UnityEngine.UI.RawImage>();
                _image.raycastTarget = false;
            }
            var previous = UnityEngine.RenderTexture.active;
            try { UnityEngine.Graphics.Blit((UnityEngine.RenderTexture)texture, _output); }
            finally { UnityEngine.RenderTexture.active = previous; }
            _image.texture = _output;
            _image.color = new UnityEngine.Color(brightness, brightness, brightness, 1f);
            _image.enabled = true;
        }

        public void Clear()
        {
            if (_image != null) { _image.enabled = false; _image.texture = null; }
        }

        void ReleaseOutput()
        {
            Clear();
            if (_output != null) { _output.Release(); UnityEngine.Object.Destroy(_output); }
            _output = null;
        }

        public void Dispose()
        {
            ReleaseOutput();
            if (_image != null) UnityEngine.Object.Destroy(_image.gameObject);
            _image = null;
        }
    }

    // Primary fader remains the same native instance in its original hierarchy,
    // layer and camera. Its read-only sprite/tint drive an owned secondary fill;
    // no native component is cloned, disabled, reparented or destroyed.
    sealed class NativeFade : IDsPortFade, System.IDisposable
    {
        static readonly System.Reflection.FieldInfo Instance = typeof(ScreenFaderState).GetField("instance",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        static readonly System.Reflection.FieldInfo Renderer = typeof(ScreenFaderState).GetField("spriteRenderer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        readonly UnityEngine.RectTransform _parent;
        readonly DsResidentUi _resident = new DsResidentUi();
        UnityEngine.UI.Image _image;
        ScreenFaderState _owner;
        UnityEngine.SpriteRenderer _source;

        public NativeFade(UnityEngine.RectTransform parent) { _parent = parent; }

        public bool TryRead(out object owner, out float alpha)
        {
            owner = null; alpha = 0f;
            if (Instance == null || Renderer == null)
            {
                _resident.CapabilityGap("native-fade", "ScreenFaderState managed owner/renderer metadata unavailable");
                return false;
            }
            _owner = Instance.GetValue(null) as ScreenFaderState;
            _source = _owner != null ? Renderer.GetValue(_owner) as UnityEngine.SpriteRenderer : null;
            if (_owner == null || _source == null || !_owner.gameObject.scene.IsValid() ||
                !_owner.gameObject.scene.isLoaded || !_source.gameObject.activeInHierarchy ||
                !_source.enabled || _source.sprite == null || _source.sharedMaterial == null) return false;
            owner = _owner;
            alpha = ScreenFaderState.Alpha;
            return true;
        }

        public void Present(object owner, float alpha)
        {
            if (!object.ReferenceEquals(owner, _owner) || _owner == null || _source == null ||
                !object.ReferenceEquals(Instance.GetValue(null), owner))
                throw new System.InvalidOperationException("Native fade owner changed during presentation");
            if (_image == null)
            {
                var go = new UnityEngine.GameObject("Native Fade Sync");
                go.layer = DsPresentation.OVERLAY_LAYER;
                var rect = go.AddComponent<UnityEngine.RectTransform>();
                rect.SetParent(_parent, false);
                rect.anchorMin = UnityEngine.Vector2.zero;
                rect.anchorMax = UnityEngine.Vector2.one;
                rect.offsetMin = rect.offsetMax = UnityEngine.Vector2.zero;
                var canvas = go.AddComponent<UnityEngine.Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingLayerID = 0;
                canvas.sortingOrder = 32000;
                _image = go.AddComponent<UnityEngine.UI.Image>();
                _image.raycastTarget = false;
                _image.type = UnityEngine.UI.Image.Type.Simple;
                _image.preserveAspect = false;
            }
            _image.sprite = _source.sprite;
            _image.material = _source.sharedMaterial;
            var color = _source.color;
            color.a = alpha;
            _image.color = color;
            _image.enabled = true;
        }

        public void Clear()
        {
            if (_image != null) _image.enabled = false;
            _owner = null; _source = null;
        }

        public void Dispose()
        {
            Clear();
            if (_image != null) UnityEngine.Object.Destroy(_image.gameObject);
            _image = null;
        }
    }
}
#endif
