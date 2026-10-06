using System;
using UnityEngine;

public partial class HKDualScreen
{
    // The existing HUD camera observes current cached owners at its render boundary.
    // Discovery is limited to ownership/structural edges. A changed native phase can
    // measure its own current bounds; it is not a settled-state/FPS certificate.
    const int NativeHudRendererLimit = 256, NativeHudNodeLimit = 384, NativeHudAnimatorLayers = 8;
    const int NativeHudReservationLimit = 32;
    sealed class NativeHudOwner
    {
        public Renderer Renderer;
        public Transform Transform;
        public SpriteRenderer Sprite;
        public tk2dBaseSprite Tk;
        public ParticleSystem Particle;
        public MeshFilter MeshFilter;
        public Mesh Mesh;
        public Bounds MeshBounds;
        public Animator Animator;
        public readonly AnimatorStateInfo[] States = new AnimatorStateInfo[NativeHudAnimatorLayers];
        public readonly AnimatorStateInfo[] NextStates = new AnimatorStateInfo[NativeHudAnimatorLayers];
        public readonly float[] Transitions = new float[NativeHudAnimatorLayers];
        public readonly bool[] Transitioning = new bool[NativeHudAnimatorLayers];
        public int Layers;
        public Func<string> Text;
        public TMProOld.TMP_Text Tmp;
        public Transform TextTransform;
        public Action GenerateText;
        public bool TextGenerated, TextPropertiesDirty;
        public Func<Color> TextColor;
        public Func<bool> TextDirty;
        public Matrix4x4 Matrix;
        public Sprite SpriteIdentity;
        public Vector2 SpriteSize;
        public SpriteDrawMode DrawMode;
        public bool FlipX, FlipY, Included, Ready, UpperEffect;
        public int SpriteId, ParticleCount;
        public Vector3 SpriteScale;
        public object Collection;
        public string Content;
        public float ParticleTime;
        public Bounds Bounds;
        public readonly HKLowerLayout.Retry Retry = new HKLowerLayout.Retry();
    }
    // HUD-local structural notification only; no registry or general event bus.
    public sealed class NativeHudChildrenWatch : MonoBehaviour
    {
        internal HKDualScreen Owner;
        void OnTransformChildrenChanged() { if (Owner != null) Owner.NativeHudStructureChanged(); }
        void OnDestroy() { if (Owner != null) Owner.NativeHudStructureChanged(); Owner = null; }
    }
    struct NativeHudNode { public Transform Transform; public NativeHudChildrenWatch Watch; }
    bool nativeHudStructureDirty;
    void NativeHudStructureChanged() { nativeHudStructureDirty = true; nativeHudVignetteLookupRoot = null; }
    struct NativeHudHeaderSprite
    {
        public SpriteRenderer Owner;
        public Sprite Sprite;
        public Matrix4x4 Matrix;
        public Vector2 Size;
        public SpriteDrawMode Mode;
        public Bounds Bounds;
    }
    struct NativeHudHeaderLabel
    {
        public Transform Owner;
        public Matrix4x4 Matrix;
        public Bounds Ink, World;
    }
    readonly NativeHudHeaderLabel[] nativeHudHeaderLabels = new NativeHudHeaderLabel[3];
    readonly Bounds[] nativeHudReservationWorld = new Bounds[NativeHudReservationLimit];
    readonly Rect[] nativeHudReservationPixels = new Rect[NativeHudReservationLimit];
    readonly bool[] nativeHudReservationKnown = new bool[NativeHudReservationLimit], nativeHudReservationIncluded = new bool[NativeHudReservationLimit];
    Matrix4x4 nativeHudHeaderProjection, nativeHudHeaderMatrix;
    bool nativeHudHeaderCameraChanged;
    readonly NativeHudOwner[] nativeHudOwners = new NativeHudOwner[NativeHudRendererLimit];
    readonly NativeHudNode[] nativeHudNodes = new NativeHudNode[NativeHudNodeLimit];
    readonly NativeHudHeaderSprite[] nativeHudHeaderSprites = new NativeHudHeaderSprite[23];
    readonly Rect[] nativeHudReservations = new Rect[NativeHudReservationLimit];
    readonly Transform[] nativeHudRoleScratch = new Transform[NativeHudNodeLimit];
    Transform nativeHudVignetteLookupRoot;
    readonly HKLowerLayout.Retry nativeHudBindRetry = new HKLowerLayout.Retry();
    Transform nativeHudRoot, nativeHudHide, nativeHudVignette;
    Camera nativeHudCamera;
    int nativeHudOwnerCount, nativeHudNodeCount, nativeHudReservationCount, nativeHudSkinStamp;
    bool nativeHudBound, nativeHudBlocked, nativeHudFitReady;
    Rect nativeHudDestination;
    Vector3 nativeHudViewMin, nativeHudViewMax;
    Matrix4x4 nativeHudHideMatrix;
    Quaternion nativeHudRotation;
    Vector3 nativeHudSourcePosition;
    bool nativeHudReduced;
    float nativeHudWidth, nativeHudHeight;

    void ResetNativeHudView()
    {
        if (nativeHudCamera != null) Camera.onPreCull -= BeforeNativeHudCamera;
        nativeHudCamera = null; nativeHudRoot = nativeHudHide = null;
        for (int i = 0; i < nativeHudOwnerCount; i++) nativeHudOwners[i] = null;
        for (int i = 0; i < nativeHudNodeCount; i++)
        {
            var watch = nativeHudNodes[i].Watch;
            if (watch != null && watch.Owner == this) watch.Owner = null;
            nativeHudNodes[i] = default;
        }
        for (int i = 0; i < nativeHudHeaderSprites.Length; i++) nativeHudHeaderSprites[i] = default;
        for (int i = 0; i < nativeHudHeaderLabels.Length; i++) nativeHudHeaderLabels[i] = default;
        for (int i = 0; i < NativeHudReservationLimit; i++) nativeHudReservationKnown[i] = false;
        nativeHudOwnerCount = nativeHudNodeCount = nativeHudReservationCount = 0;
        nativeHudBound = nativeHudFitReady = false; nativeHudBindRetry.Reset();
    }

    void PrepareNativeHudView(bool blocked)
    {
        nativeHudBlocked = blocked;
        if (hudCameraStateOwner == null || hudCameraStateRoot == null) { hudCam2.cullingMask = 0; return; }
        if (nativeHudCamera != hudCam2 || nativeHudRoot != hudCameraStateRoot)
        {
            ResetNativeHudView();
            nativeHudCamera = hudCam2; nativeHudRoot = hudCameraStateRoot;
            Camera.onPreCull += BeforeNativeHudCamera;
        }
        // An unready epoch must never borrow the previous epoch's fit.
        if (blocked || !nativeHudFitReady) hudCam2.cullingMask = 0;
    }

    static bool NativeHudFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    static bool NativeHudBoundsValid(Bounds b)
    {
        return NativeHudFinite(b.center.x) && NativeHudFinite(b.center.y) && NativeHudFinite(b.center.z) &&
            NativeHudFinite(b.size.x) && NativeHudFinite(b.size.y) && NativeHudFinite(b.size.z) &&
            b.size.x >= 0 && b.size.y >= 0 && b.size.z >= 0 && b.size.x + b.size.y > .00001f;
    }
    static bool NativeHudSameRect(Rect a, Rect b)
    { return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height; }
    static bool NativeHudSameMatrix(Matrix4x4 a, Matrix4x4 b) { return a.Equals(b); }

    // A bounded exact-role lookup, shared by relayer and owner binding. The native
    // vignette remains an upper full-game effect, including during pause/restore.
    void PreserveNativeHudVignette(Transform root)
    {
        if (root == null) return;
        if (nativeHudVignetteLookupRoot == root)
        {
            if (nativeHudVignette != null && nativeHudVignette.gameObject.layer != UI_LAYER) SetLayerRecursive(nativeHudVignette, UI_LAYER);
            return;
        }
        nativeHudVignetteLookupRoot = root; nativeHudVignette = null;
        int count = 1;
        nativeHudRoleScratch[0] = root;
        for (int i = 0; i < count; i++)
        {
            var t = nativeHudRoleScratch[i];
            if (t.name == "Low Health Vignette") { nativeHudVignette = t; SetLayerRecursive(t, UI_LAYER); break; }
            for (int j = 0; j < t.childCount; j++)
            {
                if (count == NativeHudNodeLimit) break;
                nativeHudRoleScratch[count++] = t.GetChild(j);
            }
        }
        for (int i = 0; i < count; i++) nativeHudRoleScratch[i] = null;
    }

    static Func<T> NativeHudGetter<T>(Component component, string property)
    {
        if (component == null) return null;
        var p = component.GetType().GetProperty(property);
        var m = p != null ? p.GetGetMethod() : null;
        return m != null && p.PropertyType == typeof(T) ? (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), component, m) : null;
    }

    bool BindNativeHudOwners()
    {
        for (int i = 0; i < nativeHudNodeCount; i++)
        {
            if (nativeHudNodes[i].Watch != null && nativeHudNodes[i].Watch.Owner == this) nativeHudNodes[i].Watch.Owner = null;
            nativeHudNodes[i] = default;
        }
        for (int i = 0; i < nativeHudOwnerCount; i++) nativeHudOwners[i] = null;
        nativeHudOwnerCount = nativeHudNodeCount = 0; nativeHudHide = null;
        nativeHudStructureDirty = false;
        int ancestors = 0;
        for (var p = nativeHudRoot; p != null; p = p.parent)
        {
            if (++ancestors > NativeHudNodeLimit) return false;
            if (p.GetComponent<HudGlobalHide>() != null) { nativeHudHide = p; break; }
        }
        nativeHudNodes[nativeHudNodeCount++] = new NativeHudNode { Transform = nativeHudRoot };
        for (int n = 0; n < nativeHudNodeCount; n++)
        {
            var t = nativeHudNodes[n].Transform;
            if (t.name == "Low Health Vignette") { nativeHudVignette = t; SetLayerRecursive(t, UI_LAYER); }
            bool upperEffect = nativeHudVignette != null && (t == nativeHudVignette || t.IsChildOf(nativeHudVignette));
            if (!upperEffect && t.gameObject.layer != tutLayer) t.gameObject.layer = hudLayer;
            var watch = t.GetComponent<NativeHudChildrenWatch>();
            if (watch == null) watch = t.gameObject.AddComponent<NativeHudChildrenWatch>();
            watch.Owner = this;
            nativeHudNodes[n] = new NativeHudNode { Transform = t, Watch = watch };
            for (int j = 0; j < t.childCount; j++)
            {
                if (nativeHudNodeCount == NativeHudNodeLimit) return false;
                nativeHudNodes[nativeHudNodeCount++] = new NativeHudNode { Transform = t.GetChild(j) };
            }
            var rs = t.GetComponents<Renderer>();
            for (int j = 0; j < rs.Length; j++)
            {
                if (nativeHudOwnerCount == NativeHudRendererLimit) return false;
                var r = rs[j];
                var o = new NativeHudOwner { Renderer = r, Transform = t, UpperEffect = upperEffect, Sprite = r as SpriteRenderer,
                    Tk = t.GetComponent<tk2dBaseSprite>(), MeshFilter = t.GetComponent<MeshFilter>(),
                    Particle = r is ParticleSystemRenderer ? t.GetComponent<ParticleSystem>() : null };
                if (r is ParticleSystemRenderer && o.Particle == null) return false;
                for (var p = t; p != null; p = p.parent)
                {
                    if (o.Animator == null) o.Animator = p.GetComponent<Animator>();
                    if (p == nativeHudRoot) break;
                }
                if (o.Animator != null && o.Animator.layerCount > NativeHudAnimatorLayers) return false;
                var tmp = TmpOn(t);
                for (var p = t.parent; tmp == null && p != null && t != nativeHudRoot; p = p.parent)
                { tmp = TmpOn(p); if (p == nativeHudRoot) break; }
                if (tmp != null)
                {
                    o.Tmp = tmp as TMProOld.TMP_Text; o.TextTransform = tmp.transform;
                    o.Text = NativeHudGetter<string>(tmp, "text"); o.TextColor = NativeHudGetter<Color>(tmp, "color");
                    o.TextDirty = NativeHudGetter<bool>(tmp, "havePropertiesChanged");
                    var generate = tmp.GetType().GetMethod("ForceMeshUpdate", Type.EmptyTypes);
                    if (o.Text == null || o.Tmp == null || generate == null) return false;
                    o.GenerateText = (Action)Delegate.CreateDelegate(typeof(Action), tmp, generate);
                }
                nativeHudOwners[nativeHudOwnerCount++] = o;
            }
        }
        nativeHudSkinStamp = HkStageHooks.SkinStamp;
        nativeHudBound = true; nativeHudBindRetry.Resolved();
        return true;
    }

    static bool NativeHudAnimatorAdvanced(NativeHudOwner o)
    {
        var a = o.Animator;
        if (a == null || !a.enabled || !a.gameObject.activeInHierarchy) return false;
        int layers = a.layerCount;
        if (layers > NativeHudAnimatorLayers) return true;
        bool changed = o.Layers != layers; o.Layers = layers;
        for (int i = 0; i < layers; i++)
        {
            var s = a.GetCurrentAnimatorStateInfo(i); var next = a.GetNextAnimatorStateInfo(i);
            bool moving = a.IsInTransition(i); float phase = moving ? a.GetAnimatorTransitionInfo(i).normalizedTime : 0;
            changed |= s.fullPathHash != o.States[i].fullPathHash || s.normalizedTime != o.States[i].normalizedTime ||
                next.fullPathHash != o.NextStates[i].fullPathHash || next.normalizedTime != o.NextStates[i].normalizedTime ||
                moving != o.Transitioning[i] || phase != o.Transitions[i];
            o.States[i] = s; o.NextStates[i] = next; o.Transitioning[i] = moving; o.Transitions[i] = phase;
        }
        return changed;
    }

    bool RefreshNativeHudOwner(NativeHudOwner o, bool skinChanged, out bool changed)
    {
        changed = false;
        var r = o.Renderer; var t = o.Transform;
        if (r == null || t == null) return false;
        bool included = r.enabled && r.gameObject.activeInHierarchy && r.gameObject.layer == hudLayer;
        if (o.UpperEffect) included = false;
        if (o.Sprite != null && (o.Sprite.sprite == null || o.Sprite.color.a <= 0)) included = false;
        if (o.Particle != null && o.Particle.particleCount == 0) included = false;
        string text = o.Text != null ? o.Text() : null;
        if (o.Text != null && (string.IsNullOrWhiteSpace(text) || (o.TextColor != null && o.TextColor().a <= 0))) included = false;
        changed = included != o.Included; o.Included = included;
        if (!included) { o.Ready = false; return true; }
        var matrix = t.localToWorldMatrix;
        bool propertiesDirty = o.TextDirty != null && o.TextDirty();
        bool textChanged = text != o.Content || (propertiesDirty && !o.TextPropertiesDirty);
        o.TextPropertiesDirty = propertiesDirty;
        bool dirty = changed || skinChanged || !NativeHudSameMatrix(matrix, o.Matrix) || textChanged;
        o.Matrix = matrix; o.Content = text;
        if (textChanged || skinChanged) o.TextGenerated = false;
        if (o.Sprite != null)
        {
            var s = o.Sprite;
            dirty |= s.sprite != o.SpriteIdentity || s.size.x != o.SpriteSize.x || s.size.y != o.SpriteSize.y ||
                s.drawMode != o.DrawMode || s.flipX != o.FlipX || s.flipY != o.FlipY;
            o.SpriteIdentity = s.sprite; o.SpriteSize = s.size; o.DrawMode = s.drawMode; o.FlipX = s.flipX; o.FlipY = s.flipY;
        }
        if (o.Tk != null)
        {
            dirty |= o.SpriteId != o.Tk.spriteId || o.Collection != (object)o.Tk.Collection || !SamePoint(o.SpriteScale, o.Tk.scale);
            o.SpriteId = o.Tk.spriteId; o.Collection = o.Tk.Collection; o.SpriteScale = o.Tk.scale;
        }
        if (o.MeshFilter != null)
        {
            // Mesh.bounds is the engine's cached local shape value, not a renderer
            // bounds scan or a vertex walk. Liquid/mesh writers can change it in place.
            var mesh = o.MeshFilter.sharedMesh as Mesh;
            if (mesh == null) return false;
            var shape = mesh.bounds;
            dirty |= mesh != o.Mesh || !SameBounds(shape, o.MeshBounds);
            o.Mesh = mesh; o.MeshBounds = shape;
        }
        if (o.Particle != null)
        {
            dirty |= o.ParticleTime != o.Particle.time || o.ParticleCount != o.Particle.particleCount;
            o.ParticleTime = o.Particle.time; o.ParticleCount = o.Particle.particleCount;
        }
        dirty |= NativeHudAnimatorAdvanced(o);
        if (o.Animator != null && o.Animator.layerCount > NativeHudAnimatorLayers) return false;
        if (dirty) { o.Ready = false; o.Retry.Reset(); }
        if (!o.Ready && o.Retry.Due(Time.frameCount))
        {
            Bounds b;
            if (o.Text != null)
            {
                Vector3 lo, hi;
                if (!o.TextGenerated)
                {
                    try { o.GenerateText(); } catch { return false; }
                    if (o.Text() != text) return false;
                    o.TextGenerated = true;
                    o.TextPropertiesDirty = o.TextDirty != null && o.TextDirty();
                }
                if (o.Tmp == null || o.Tmp.textInfo == null || o.Tmp.textInfo.characterCount > 4096 ||
                    !TryGeneratedGlyphBoundsWorld(o.Tmp, o.TextTransform, out lo, out hi)) return false;
                b = new Bounds((lo + hi) * .5f, hi - lo);
            }
            else b = r.bounds;
            if (!NativeHudBoundsValid(b)) return false;
            o.Bounds = b; o.Ready = true; o.Retry.Resolved(); changed = true;
        }
        return o.Ready;
    }

    static Vector3 NativeHudCorner(Bounds b, int i)
    { return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z); }

    bool MeasureNativeHudView(Quaternion rotation)
    {
        var inv = Quaternion.Inverse(rotation);
        var lo = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var hi = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool have = false;
        for (int i = 0; i < nativeHudOwnerCount; i++)
        {
            var o = nativeHudOwners[i]; if (!o.Included) continue;
            if (!o.Ready) return false;
            for (int j = 0; j < 8; j++)
            {
                var p = NativeHudCorner(o.Bounds, j); var current = inv * p;
                lo = Vector3.Min(lo, current); hi = Vector3.Max(hi, current);
                if (nativeHudHide != null && HudGlobalHide.IsReduced)
                {
                    // HudGlobalHide's native normal state is position zero/scale one
                    // on this specific ancestor. Never rewrite the source transform.
                    var local = nativeHudHide.InverseTransformPoint(p);
                    var normal = nativeHudHide.localRotation * local;
                    if (nativeHudHide.parent != null) normal = nativeHudHide.parent.TransformPoint(normal);
                    normal = inv * normal; lo = Vector3.Min(lo, normal); hi = Vector3.Max(hi, normal);
                }
                have = true;
            }
        }
        if (!have || !NativeHudFinite(lo.x) || !NativeHudFinite(hi.y)) return false;
        nativeHudViewMin = lo; nativeHudViewMax = hi; return true;
    }

    Bounds NativeHudHeaderInk(NativePaneLabel label, Bounds ink, int slot)
    {
        var old = nativeHudHeaderLabels[slot]; var matrix = label.Root.localToWorldMatrix;
        if (old.Owner != label.Root || !NativeHudSameMatrix(old.Matrix, matrix) || !SameBounds(old.Ink, ink))
        {
            var lo = label.Root.TransformPoint(NativeHudCorner(ink, 0)); var hi = lo;
            for (int i = 1; i < 8; i++)
            { var p = label.Root.TransformPoint(NativeHudCorner(ink, i)); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            old = new NativeHudHeaderLabel { Owner = label.Root, Matrix = matrix, Ink = ink, World = new Bounds((lo + hi) * .5f, hi - lo) };
            nativeHudHeaderLabels[slot] = old;
        }
        return old.World;
    }

    bool AddNativeHudReservation(Bounds world, int slot, ref int count, ref bool changed)
    {
        if (!NativeHudBoundsValid(world) || attrCam == null) return false;
        if (!nativeHudReservationKnown[slot] || nativeHudHeaderCameraChanged || !SameBounds(world, nativeHudReservationWorld[slot]))
        {
            var lo = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0);
            var hi = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0);
            for (int i = 0; i < 8; i++)
            {
                var p = attrCam.WorldToViewportPoint(NativeHudCorner(world, i));
                var pixel = new Vector3(p.x * BOTTOM_W, (1 - p.y) * BOTTOM_H, 0);
                lo = Vector3.Min(lo, pixel); hi = Vector3.Max(hi, pixel);
            }
            var g = LowerGeometry();
            nativeHudReservationIncluded[slot] = hi.y > 0 && lo.y < g.HudHeight && hi.x > 0 && lo.x < g.Width;
            nativeHudReservationPixels[slot] = new Rect(lo.x, lo.y, hi.x - lo.x, hi.y - lo.y);
            nativeHudReservationWorld[slot] = world; nativeHudReservationKnown[slot] = true;
        }
        if (!nativeHudReservationIncluded[slot]) return true;
        if (count == NativeHudReservationLimit) return false;
        var rect = nativeHudReservationPixels[slot];
        changed |= count >= nativeHudReservationCount || !NativeHudSameRect(nativeHudReservations[count], rect);
        nativeHudReservations[count++] = rect; return true;
    }

    bool AddNativeHudHeaderSprite(SpriteRenderer r, int slot, ref int count, ref bool changed)
    {
        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.color.a <= 0) return true;
        if (r.sprite == null) return false;
        var old = nativeHudHeaderSprites[slot]; var matrix = r.transform.localToWorldMatrix;
        if (old.Owner != r || old.Sprite != r.sprite || !NativeHudSameMatrix(old.Matrix, matrix) ||
            old.Size.x != r.size.x || old.Size.y != r.size.y || old.Mode != r.drawMode)
        {
            old = new NativeHudHeaderSprite { Owner = r, Sprite = r.sprite, Matrix = matrix, Size = r.size, Mode = r.drawMode, Bounds = r.bounds };
            nativeHudHeaderSprites[slot] = old;
        }
        return AddNativeHudReservation(old.Bounds, 1 + slot, ref count, ref changed);
    }

    bool AddNativeHudAction(MapActionButton button, int slot, ref int count, ref bool changed)
    {
        if (button == null || button.Root == null || !button.Root.gameObject.activeInHierarchy || !button.InkReady ||
            button.Plate == null || !button.Plate.enabled) return true;
        if (!AddNativeHudReservation(button.Hit, 24 + slot * 2, ref count, ref changed)) return false;
        return AddNativeHudReservation(NativeHudHeaderInk(button.Graphic, button.Ink, 1 + slot), 25 + slot * 2, ref count, ref changed);
    }

    bool RefreshNativeHudDestination(out bool changed)
    {
        changed = false; int count = 0;
        if (attrCam == null) return false;
        var projection = attrCam.projectionMatrix; var matrix = attrCam.transform.localToWorldMatrix;
        nativeHudHeaderCameraChanged = nativeHudWidth != BOTTOM_W || nativeHudHeight != BOTTOM_H ||
            !NativeHudSameMatrix(projection, nativeHudHeaderProjection) || !NativeHudSameMatrix(matrix, nativeHudHeaderMatrix);
        nativeHudHeaderProjection = projection; nativeHudHeaderMatrix = matrix;
        if (shellTitle != null && shellTitle.Root != null && shellTitle.Root.gameObject.activeInHierarchy &&
            shellTitleInkReady && shellTitle.Renderer != null && shellTitle.Renderer.enabled)
            if (!AddNativeHudReservation(NativeHudHeaderInk(shellTitle, shellTitleInk, 0), 0, ref count, ref changed)) return false;
        if (!AddNativeHudHeaderSprite(benchPillSR, 0, ref count, ref changed)) return false;
        if (equipCharmSRs.Count > 11 || notchSRs.Count > 11) return false;
        for (int i = 0; i < equipCharmSRs.Count; i++) if (!AddNativeHudHeaderSprite(equipCharmSRs[i], 1 + i, ref count, ref changed)) return false;
        for (int i = 0; i < notchSRs.Count; i++) if (!AddNativeHudHeaderSprite(notchSRs[i], 12 + i, ref count, ref changed)) return false;
        if (!AddNativeHudAction(mapViewAction, 0, ref count, ref changed) || !AddNativeHudAction(mapMarkerAction, 1, ref count, ref changed)) return false;
        changed |= count != nativeHudReservationCount || nativeHudWidth != BOTTOM_W || nativeHudHeight != BOTTOM_H;
        nativeHudReservationCount = count; nativeHudWidth = BOTTOM_W; nativeHudHeight = BOTTOM_H;
        if (!changed && nativeHudFitReady) return true;
        var g = LowerGeometry();
        // Subpixel inward bias keeps the six-pixel margin after float viewport conversion.
        const float pad = 6.01f;
        float maxW = Mathf.Min(g.Width - 2 * pad, g.Width * .74f), maxH = g.HudHeight - 2 * pad;
        float best = 0; Rect destination = default;
        // At most 33x33 candidates and 32 intersections on a relevant layout edge.
        // Both dimensions may shrink, but the native HUD remains top-left anchored.
        for (int x = -1; x < count; x++) for (int y = -1; y < count; y++)
        {
            // Keep candidate edges strictly inside obstacle edges after float rounding.
            float w = x < 0 ? maxW : Mathf.Min(maxW, nativeHudReservations[x].x - 2 * pad - .01f);
            float h = y < 0 ? maxH : Mathf.Min(maxH, nativeHudReservations[y].y - 2 * pad - .01f);
            if (w <= 0 || h <= 0 || w * h <= best) continue;
            bool free = true;
            for (int i = 0; i < count; i++)
            {
                var r = nativeHudReservations[i];
                if (r.x - pad < pad + w && r.x + r.width + pad > pad && r.y - pad < pad + h && r.y + r.height + pad > pad)
                { free = false; break; }
            }
            if (free) { best = w * h; destination = new Rect(pad, pad, w, h); }
        }
        if (best <= 0) return false;
        nativeHudDestination = destination; return true;
    }

    void BeforeNativeHudCamera(Camera camera)
    {
        if (camera != nativeHudCamera || camera != hudCam2 || nativeHudBlocked || HudGlobalHide.IsHidden) return;
        camera.cullingMask = 0;
        try { UpdateNativeHudCamera(camera); }
        catch { nativeHudFitReady = false; }
    }

    void UpdateNativeHudCamera(Camera camera)
    {
        if (nativeHudRoot == null || hudCameraStateSource == null || !nativeHudRoot.gameObject.activeInHierarchy) return;
        bool rebound = !nativeHudBound || nativeHudStructureDirty;
        for (int i = 0; !rebound && i < nativeHudOwnerCount; i++)
        {
            var o = nativeHudOwners[i];
            rebound = o.Renderer == null || (o.Renderer is ParticleSystemRenderer && o.Particle == null);
        }
        if (rebound)
        {
            if (nativeHudBound) { nativeHudBound = false; nativeHudBindRetry.Reset(); }
            nativeHudFitReady = false;
            if (!nativeHudBindRetry.Due(Time.frameCount) || !BindNativeHudOwners()) return;
        }
        bool dirty = rebound || !nativeHudFitReady;
        bool skinChanged = nativeHudSkinStamp != HkStageHooks.SkinStamp;
        nativeHudSkinStamp = HkStageHooks.SkinStamp;
        bool ready = true;
        for (int i = 0; i < nativeHudOwnerCount; i++)
        {
            bool changed;
            ready &= RefreshNativeHudOwner(nativeHudOwners[i], skinChanged, out changed); dirty |= changed;
        }
        if (!ready) { nativeHudFitReady = false; return; }
        var rotation = hudCameraStateSource.transform.rotation;
        var hideMatrix = nativeHudHide != null ? nativeHudHide.localToWorldMatrix : Matrix4x4.identity;
        // Quaternion equality is a native value comparison, never reflection/boxing.
        dirty |= !rotation.Equals(nativeHudRotation) || !NativeHudSameMatrix(hideMatrix, nativeHudHideMatrix) ||
            nativeHudReduced != HudGlobalHide.IsReduced || !SamePoint(nativeHudSourcePosition, hudCameraStateSource.transform.position);
        nativeHudRotation = rotation; nativeHudHideMatrix = hideMatrix;
        nativeHudReduced = HudGlobalHide.IsReduced; nativeHudSourcePosition = hudCameraStateSource.transform.position;
        bool destinationChanged;
        if (!RefreshNativeHudDestination(out destinationChanged)) { nativeHudFitReady = false; return; }
        if (dirty && !MeasureNativeHudView(rotation)) { nativeHudFitReady = false; return; }
        if (dirty || destinationChanged)
        {
            var d = nativeHudDestination; float aspect = d.width / d.height;
            var size = nativeHudViewMax - nativeHudViewMin;
            float ortho = Mathf.Max(size.y * .5f, size.x * .5f / aspect) * 1.001f;
            if (!NativeHudFinite(ortho) || ortho <= .00001f) { nativeHudFitReady = false; return; }
            var center = (nativeHudViewMin + nativeHudViewMax) * .5f;
            center.z = (Quaternion.Inverse(rotation) * hudCameraStateSource.transform.position).z;
            camera.rect = new Rect(d.x / BOTTOM_W, 1 - (d.y + d.height) / BOTTOM_H, d.width / BOTTOM_W, d.height / BOTTOM_H);
            camera.aspect = aspect; camera.orthographic = true; camera.orthographicSize = ortho;
            camera.nearClipPlane = Mathf.Min(hudCameraStateNear, nativeHudViewMin.z - center.z - .01f);
            camera.farClipPlane = Mathf.Max(hudCameraStateFar, nativeHudViewMax.z - center.z + .01f);
            camera.transform.rotation = rotation; camera.transform.position = rotation * center;
            camera.ResetProjectionMatrix();
        }
        nativeHudFitReady = true; camera.cullingMask = 1 << hudLayer;
    }
}
