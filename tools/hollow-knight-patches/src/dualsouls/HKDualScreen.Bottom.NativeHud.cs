using System;
using UnityEngine;

public partial class HKDualScreen
{
    // The existing HUD camera observes current cached owners at its render boundary.
    // Discovery is limited to ownership/structural edges. A changed native phase can
    // measure its own current bounds; it is not a settled-state/FPS certificate.
    const int NativeHudRendererLimit = 256, NativeHudNodeLimit = 384, NativeHudAnimatorLayers = 8;
    // A failed measurement retries every 120 frames until it resolves.
    sealed class NativeHudRetry
    {
        int next; bool ready;
        public bool Due(int frame) { if (ready || frame < next) return false; next = frame + 120; return true; }
        public void Resolved() { ready = true; }
        public void Reset() { ready = false; next = 0; }
    }
    sealed class NativeHudAnimation
    {
        public Animator Owner;
        public readonly AnimatorStateInfo[] States = new AnimatorStateInfo[NativeHudAnimatorLayers];
        public readonly AnimatorStateInfo[] NextStates = new AnimatorStateInfo[NativeHudAnimatorLayers];
        public readonly float[] Transitions = new float[NativeHudAnimatorLayers];
        public readonly bool[] Transitioning = new bool[NativeHudAnimatorLayers];
        public int Layers;
        public bool Observed, Changed;
    }
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
        public NativeHudAnimation Animation;
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
        // Drawn but never measured: the soul fill parks below the orb when empty and
        // is only ever visible inside the orb frame, which the fit already covers; the
        // geo Add/Subtract popups sit below the counter and only flash during a change.
        public bool FitExempt;
        public int SpriteId, ParticleCount;
        public Vector3 SpriteScale;
        public object Collection;
        public string Content;
        public float ParticleTime;
        public Bounds Bounds;
        public readonly NativeHudRetry Retry = new NativeHudRetry();
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
    readonly NativeHudOwner[] nativeHudOwners = new NativeHudOwner[NativeHudRendererLimit];
    readonly NativeHudAnimation[] nativeHudAnimations = new NativeHudAnimation[NativeHudRendererLimit];
    int nativeHudAnimationCount;
    readonly NativeHudNode[] nativeHudNodes = new NativeHudNode[NativeHudNodeLimit];
    readonly Transform[] nativeHudRoleScratch = new Transform[NativeHudNodeLimit];
    Transform nativeHudVignetteLookupRoot;
    readonly NativeHudRetry nativeHudBindRetry = new NativeHudRetry();
    Transform nativeHudRoot, nativeHudHide, nativeHudVignette;
    Camera nativeHudCamera;
    int nativeHudOwnerCount, nativeHudNodeCount, nativeHudSkinStamp;
    bool nativeHudBound, nativeHudBlocked, nativeHudFitReady;
    Rect nativeHudDestination;
    Vector3 nativeHudViewMin, nativeHudViewMax;
    Matrix4x4 nativeHudHideMatrix;
    Quaternion nativeHudRotation;
    Vector3 nativeHudSourcePosition;
    bool nativeHudReduced;
    float nativeHudWidth, nativeHudHeight;
    // Silksong's fixed scale (DsHudFrame.MaskPixelPitch): one mask pitch is 55 header layout
    // units, whatever the mask count or the soul orb is doing. The pitch is the live distance
    // between masks 1 and 2 (health_display "Health Number"), measured in the HUD camera frame.
    const float NativeHudMaskPixelPitch = 55f;
    Transform nativeHudMask1, nativeHudMask2;
    float nativeHudPitch, nativeHudMaskY;
    // Panel pixels from the destination's top to the line beneath the mask row; 0 = unmeasured.
    float nativeHudRowSplit;

    void ResetNativeHudView()
    {
        if (nativeHudCamera != null) Camera.onPreCull -= BeforeNativeHudCamera;
        nativeHudCamera = null; nativeHudRoot = nativeHudHide = null;
        for (int i = 0; i < nativeHudOwnerCount; i++) nativeHudOwners[i] = null;
        for (int i = 0; i < nativeHudAnimationCount; i++) nativeHudAnimations[i] = null;
        nativeHudAnimationCount = 0;
        for (int i = 0; i < nativeHudNodeCount; i++)
        {
            var watch = nativeHudNodes[i].Watch;
            if (watch != null && watch.Owner == this) watch.Owner = null;
            nativeHudNodes[i] = default;
        }
        nativeHudOwnerCount = nativeHudNodeCount = 0;
        nativeHudBound = nativeHudFitReady = false; nativeHudBindRetry.Reset();
        nativeHudMask1 = nativeHudMask2 = null; nativeHudPitch = nativeHudRowSplit = 0f;
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
        for (int i = 0; i < nativeHudAnimationCount; i++) nativeHudAnimations[i] = null;
        nativeHudAnimationCount = 0;
        nativeHudOwnerCount = nativeHudNodeCount = 0; nativeHudHide = null;
        nativeHudMask1 = nativeHudMask2 = null;
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
            if (nativeHudMask1 == null || nativeHudMask2 == null) NativeHudNoteMask(t);
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
                Animator animator = null;
                for (var p = t; p != null; p = p.parent)
                {
                    if (animator == null) animator = p.GetComponent<Animator>();
                    if (p.name == "SoulOrb_fill" || p.name == "Add Text" || p.name == "Subtract Text") o.FitExempt = true;
                    if (p == nativeHudRoot) break;
                }
                if (animator != null)
                {
                    if (animator.layerCount > NativeHudAnimatorLayers) return false;
                    for (int k = 0; k < nativeHudAnimationCount; k++)
                        if (nativeHudAnimations[k].Owner == animator) { o.Animation = nativeHudAnimations[k]; break; }
                    if (o.Animation == null)
                    {
                        if (nativeHudAnimationCount == NativeHudRendererLimit) return false;
                        o.Animation = new NativeHudAnimation { Owner = animator };
                        nativeHudAnimations[nativeHudAnimationCount++] = o.Animation;
                    }
                }
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

    // Bind-time only: masks 1 and 2 by their own health_display number, as Silksong finds them.
    void NativeHudNoteMask(Transform t)
    {
        var fsms = t.GetComponents<PlayMakerFSM>();
        for (int i = 0; i < fsms.Length; i++)
        {
            var f = fsms[i];
            if (f == null || f.FsmName != "health_display" || f.FsmVariables == null) continue;
            var number = f.FsmVariables.FindFsmInt("Health Number");
            if (number == null) continue;
            if (number.Value == 1) nativeHudMask1 = t;
            else if (number.Value == 2) nativeHudMask2 = t;
        }
    }

    static bool NativeHudAnimatorAdvanced(NativeHudOwner owner)
    {
        var o = owner.Animation;
        if (o == null) return false;
        if (o.Observed) return o.Changed;
        var a = o.Owner;
        if (a == null || !a.enabled || !a.gameObject.activeInHierarchy)
        { o.Observed = true; o.Changed = false; return false; }
        int layers = a.layerCount;
        if (layers > NativeHudAnimatorLayers)
        { o.Observed = true; o.Changed = true; return true; }
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
        o.Changed = changed; o.Observed = true;
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
        if (o.Animation != null && o.Animation.Owner != null && o.Animation.Owner.layerCount > NativeHudAnimatorLayers) return false;
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

    static bool SamePoint(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
    static bool SameBounds(Bounds a, Bounds b) { return SamePoint(a.center, b.center) && SamePoint(a.size, b.size); }

    // Already-bound native consumers reuse generated buffers without discovery or
    // generation. The same four-corner validation owns both call paths.
    static bool TryGeneratedGlyphBoundsWorld(TMProOld.TMP_Text tmp, Transform tmpT, out Vector3 wMin, out Vector3 wMax)
    {
        wMin = wMax = Vector3.zero;
        try
        {
            if (tmp == null || tmpT == null) return false;
            var info = tmp.textInfo;
            if (info == null || !ReferenceEquals(info.textComponent, tmp) || info.characterInfo == null ||
                info.characterCount <= 0 || info.characterCount > info.characterInfo.Length ||
                info.meshInfo == null || info.materialCount <= 0 || info.materialCount > info.meshInfo.Length) return false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            bool have = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                if (!character.isVisible) continue;
                int material = character.materialReferenceIndex, vertex = character.vertexIndex;
                if (material < 0 || material >= info.materialCount || vertex < 0) return false;
                var mesh = info.meshInfo[material];
                if (mesh.mesh == null || mesh.vertices == null || mesh.vertexCount < 4 ||
                    mesh.vertexCount > mesh.vertices.Length || vertex > mesh.vertexCount - 4) return false;
                // Four generated vertices, including overhang, skew and fallback material groups.
                // Do not substitute ascender/descender/xAdvance or the padded/decorated material mesh extent.
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 local = corner == 0 ? character.vertex_BL.position : corner == 1 ? character.vertex_TL.position :
                                    corner == 2 ? character.vertex_TR.position : character.vertex_BR.position;
                    Vector3 buffer = mesh.vertices[vertex + corner];
                    if (float.IsNaN(local.x) || float.IsInfinity(local.x) || float.IsNaN(local.y) || float.IsInfinity(local.y) ||
                        float.IsNaN(local.z) || float.IsInfinity(local.z) || local.x != buffer.x || local.y != buffer.y || local.z != buffer.z) return false;
                    Vector3 world = tmpT.TransformPoint(local);
                    if (float.IsNaN(world.x) || float.IsInfinity(world.x) || float.IsNaN(world.y) || float.IsInfinity(world.y) ||
                        float.IsNaN(world.z) || float.IsInfinity(world.z)) return false;
                    if (!have) { min = max = world; have = true; }
                    else { min = Vector3.Min(min, world); max = Vector3.Max(max, world); }
                }
            }
            Vector3 extent = max - min;
            if (!have || float.IsNaN(extent.x) || float.IsInfinity(extent.x) || float.IsNaN(extent.y) || float.IsInfinity(extent.y) ||
                float.IsNaN(extent.z) || float.IsInfinity(extent.z) || extent.sqrMagnitude <= 1e-8f) return false;
            wMin = min; wMax = max;
            return true;
        }
        catch { return false; }
    }

    static Vector3 NativeHudCorner(Bounds b, int i)
    { return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z); }

    // HK's HUD camera never shows what lies outside its own view, and nothing that
    // spans most of that view is HUD furniture (blankers, fades, glows): neither may
    // widen the fit, or the real HUD shrinks to a sliver of the header band.
    const float NativeHudOverlayFraction = .5f;
    int nativeHudExtremeMinX = -1, nativeHudExtremeMaxX = -1, nativeHudExtremeMinY = -1, nativeHudExtremeMaxY = -1;
    float nativeHudNextFitLog;

    bool MeasureNativeHudView(Quaternion rotation)
    {
        var inv = Quaternion.Inverse(rotation);
        var src = hudCameraStateSource;
        if (src == null || !src.orthographic) return false;
        Vector3 eye = inv * src.transform.position;
        float halfH = src.orthographicSize, halfW = halfH * src.aspect;
        if (!NativeHudFinite(halfW) || halfW <= 0f || halfH <= 0f) return false;
        float viewMinX = eye.x - halfW, viewMaxX = eye.x + halfW, viewMinY = eye.y - halfH, viewMaxY = eye.y + halfH;
        var lo = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var hi = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool have = false;
        int minX = -1, maxX = -1, minY = -1, maxY = -1, dropped = 0;
        for (int i = 0; i < nativeHudOwnerCount; i++)
        {
            var o = nativeHudOwners[i]; if (!o.Included || o.FitExempt) continue;
            if (!o.Ready) return false;
            var olo = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var ohi = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int j = 0; j < 8; j++)
            {
                var p = NativeHudCorner(o.Bounds, j); var current = inv * p;
                olo = Vector3.Min(olo, current); ohi = Vector3.Max(ohi, current);
                if (nativeHudHide != null && HudGlobalHide.IsReduced)
                {
                    // HudGlobalHide's native normal state is position zero/scale one
                    // on this specific ancestor. Never rewrite the source transform.
                    var local = nativeHudHide.InverseTransformPoint(p);
                    var normal = nativeHudHide.localRotation * local;
                    if (nativeHudHide.parent != null) normal = nativeHudHide.parent.TransformPoint(normal);
                    normal = inv * normal; olo = Vector3.Min(olo, normal); ohi = Vector3.Max(ohi, normal);
                }
            }
            if (ohi.x - olo.x > 2f * halfW * NativeHudOverlayFraction || ohi.y - olo.y > 2f * halfH * NativeHudOverlayFraction)
            { dropped++; continue; }
            olo.x = Mathf.Max(olo.x, viewMinX); ohi.x = Mathf.Min(ohi.x, viewMaxX);
            olo.y = Mathf.Max(olo.y, viewMinY); ohi.y = Mathf.Min(ohi.y, viewMaxY);
            if (olo.x >= ohi.x || olo.y >= ohi.y) { dropped++; continue; }
            if (olo.x < lo.x) minX = i; if (ohi.x > hi.x) maxX = i;
            if (olo.y < lo.y) minY = i; if (ohi.y > hi.y) maxY = i;
            lo = Vector3.Min(lo, olo); hi = Vector3.Max(hi, ohi);
            have = true;
        }
        if (!have || !NativeHudFinite(lo.x) || !NativeHudFinite(hi.y)) return false;
        nativeHudViewMin = lo; nativeHudViewMax = hi;
        nativeHudPitch = 0f;
        if (nativeHudMask1 != null && nativeHudMask2 != null && nativeHudMask1.gameObject.activeInHierarchy)
        {
            Vector3 first = inv * nativeHudMask1.position, second = inv * nativeHudMask2.position;
            float pitch = second.x - first.x;
            if (NativeHudFinite(pitch) && pitch > .00001f && NativeHudFinite(first.y))
            { nativeHudPitch = pitch; nativeHudMaskY = first.y; }
        }
        LogNativeHudFit(minX, maxX, minY, maxY, dropped, halfW, halfH);
        return true;
    }

    // Names the owners that set each edge of the fit whenever they change (at most every two seconds).
    void LogNativeHudFit(int minX, int maxX, int minY, int maxY, int dropped, float halfW, float halfH)
    {
        if (minX == nativeHudExtremeMinX && maxX == nativeHudExtremeMaxX && minY == nativeHudExtremeMinY && maxY == nativeHudExtremeMaxY) return;
        if (Time.unscaledTime < nativeHudNextFitLog) return;
        nativeHudNextFitLog = Time.unscaledTime + 2f;
        nativeHudExtremeMinX = minX; nativeHudExtremeMaxX = maxX; nativeHudExtremeMinY = minY; nativeHudExtremeMaxY = maxY;
        var size = nativeHudViewMax - nativeHudViewMin;
        Debug.Log("[HKDS hudfit] fit=" + (size.x / (2f * halfW)).ToString("F2") + "x" + (size.y / (2f * halfH)).ToString("F2") +
            " of view, dropped=" + dropped + " left='" + NativeHudOwnerName(minX) + "' right='" + NativeHudOwnerName(maxX) +
            "' bottom='" + NativeHudOwnerName(minY) + "' top='" + NativeHudOwnerName(maxY) + "'");
    }

    string NativeHudOwnerName(int i)
    {
        var t = i >= 0 && i < nativeHudOwnerCount && nativeHudOwners[i] != null ? nativeHudOwners[i].Transform : null;
        return t == null ? "?" : (t.parent != null ? t.parent.name + "/" : "") + t.name;
    }

    // The shell's header band (HKDualScreen.Shell.cs) is the HUD's destination.
    bool RefreshNativeHudDestination(out bool changed)
    {
        changed = false;
        if (!shellHudDestinationValid) return false;
        var d = shellHudDestination;
        changed = !NativeHudSameRect(d, nativeHudDestination) || nativeHudWidth != BOTTOM_W || nativeHudHeight != BOTTOM_H;
        nativeHudDestination = d; nativeHudWidth = BOTTOM_W; nativeHudHeight = BOTTOM_H;
        return d.width > 0f && d.height > 0f;
    }

    // Silksong's hud_zoom percentage (50-200); anything outside that range keeps 100.
    static float NativeHudZoom
    {
        get { int zoom = DsConfig.Int("hud_zoom", 100); return zoom < 50 || zoom > 200 ? 1f : zoom / 100f; }
    }

    // The band beneath the live mask row, in header layout units, or empty while the HUD
    // is not fitted and drawn (Silksong's DsHudView.TitleSpace).
    Rect NativeHudTitleSpace()
    {
        var band = shellHudBandLayout;
        if (!nativeHudFitReady || nativeHudRowSplit <= 1f || hudCam2 == null || hudCam2.cullingMask == 0 ||
            !shellHudDestinationValid || nativeHudDestination.height <= 0f || band.height <= 0f)
            return new Rect(0f, 0f, 0f, 0f);
        float split = nativeHudRowSplit * band.height / nativeHudDestination.height;
        return new Rect(band.x, band.y + split, band.width, Mathf.Max(0f, band.height - split));
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
            rebound = o.Renderer == null || (o.Renderer is ParticleSystemRenderer && o.Particle == null) ||
                (o.Animation != null && o.Animation.Owner == null);
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
        for (int i = 0; i < nativeHudAnimationCount; i++) nativeHudAnimations[i].Observed = false;
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
            // The whole-extent fit is only the ceiling (never crop, as Silksong caps by its
            // layout bounds); the scale itself is Silksong's fixed pixels per mask, so the HUD
            // keeps its size as masks and soul change. hud_zoom scales about the top-left anchor.
            float ortho = Mathf.Max(size.y * .5f, size.x * .5f / aspect) * 1.001f;
            float panelPerLayout = shellHudBandLayout.height > 0f ? d.height / shellHudBandLayout.height : 0f;
            if (nativeHudPitch > 0f && panelPerLayout > 0f)
            {
                float maskPixels = NativeHudMaskPixelPitch * shellHudUiScale * panelPerLayout;
                ortho = Mathf.Max(ortho, d.height * nativeHudPitch / (2f * maskPixels));
            }
            ortho /= NativeHudZoom;
            if (!NativeHudFinite(ortho) || ortho <= .00001f) { nativeHudFitReady = false; return; }
            var center = (nativeHudViewMin + nativeHudViewMax) * .5f;
            float cx, cy;
            HollowKnightPatches.Lower.HkShellPolicy.HudAnchor(nativeHudViewMin.x, nativeHudViewMax.y,
                ortho, ortho * aspect, out cx, out cy);
            // Silksong's hud_x / hud_y knobs: hundredths of a HUD unit, moving the camera.
            center.x = cx + DsConfig.Int("hud_x", 0) / 100f; center.y = cy + DsConfig.Int("hud_y", 0) / 100f;
            // The title row starts below the live mask row (Silksong: between masks and silk).
            nativeHudRowSplit = 0f;
            if (nativeHudPitch > 0f)
                nativeHudRowSplit = Mathf.Clamp((center.y + ortho - (nativeHudMaskY - nativeHudPitch * .6f)) * d.height / (2f * ortho),
                    0f, d.height);
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
