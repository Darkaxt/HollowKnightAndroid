// DsResidentUi — provenance-aware access to Silksong's resident inventory UI.
//
// Exact typed APIs are used where the game exposes them. Private serialized
// text is read only because InventoryPaneList.currentPaneText is the native
// Pane Name source and has no public accessor. Frame art is matched by the
// exact path-and-Sprite identities on the current game's live UICanvas. A
// missing resident Image produces a capability gap and no substitute art.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using PaneText = TMProOld.TextMeshPro;

public sealed class DsResidentUi
{
    static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    const string FrameTopPath = "_UIManager/UICanvas/OptionsMenuScreen/TopFleur";
    const string FrameTopSprite = "Warning_Fleur0008";
    const string FrameBottomPath = "_UIManager/UICanvas/KeepResPrompt/BottomFleur";
    const string FrameBottomSprite = "bottom_fleur0008";
    const string SelectedTopPath = "_UIManager/UICanvas/PauseMenuScreen/TopFleur";
    const string SelectedTopSprite = "pause_top_fleur0000";
    const string SelectedBottomPath = "_UIManager/UICanvas/PauseMenuScreen/BottomFleur";
    const string SelectedBottomSprite = "bottom_fleur0000";

    readonly HashSet<string> _reportedGaps = new HashSet<string>();
    readonly HashSet<string> _reportedSources = new HashSet<string>();
    readonly HashSet<string> _attemptedImageRoles = new HashSet<string>();
    readonly Dictionary<string, List<Image>> _imageIndex =
        new Dictionary<string, List<Image>>(StringComparer.Ordinal);
    InventoryPaneList _paneList;
    Transform _inventoryRoot;
    PaneText _paneName;
    bool _refreshAttempted;
    bool _imageIndexBuilt;

    public bool Refresh()
    {
        if (_paneList != null && _inventoryRoot != null && _paneName != null) return true;
        if (_refreshAttempted) return false;
        _refreshAttempted = true;
        _paneList = null;
        _inventoryRoot = null;
        _paneName = null;
        try
        {
            var lists = Resources.FindObjectsOfTypeAll<InventoryPaneList>();
            for (int i = 0; i < lists.Length; i++)
            {
                var candidate = lists[i];
                if (candidate == null) continue;
                var scene = candidate.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) continue;
                var inventory = FindAncestor(candidate.transform, "Inventory");
                if (inventory == null) continue;
                _paneList = candidate;
                _inventoryRoot = inventory;
                break;
            }

            if (_paneList == null)
            {
                CapabilityGap("inventory-root", "live Inventory/InventoryPaneList not resident");
                return false;
            }

            var field = typeof(InventoryPaneList).GetField("currentPaneText", PrivateInstance);
            _paneName = field != null ? field.GetValue(_paneList) as PaneText : null;
            if (_paneName == null)
                CapabilityGap("pane-name", "InventoryPaneList.currentPaneText unavailable");
            else
                ResidentProvenance("pane-name", _paneName.transform,
                                   "InventoryPaneList.currentPaneText");
            return _paneName != null;
        }
        catch (Exception e)
        {
            CapabilityGap("inventory-root", e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    public InventoryPane GetPane(InventoryPaneList.PaneTypes role)
    {
        if (!Refresh()) return null;
        try
        {
            var pane = _paneList.GetPane(role);
            if (pane == null) CapabilityGap("pane-" + role, "InventoryPaneList.GetPane returned null");
            else ResidentProvenance("pane-" + role, pane.transform,
                                    "InventoryPaneList.GetPane(" + role + ")");
            return pane;
        }
        catch (Exception e)
        {
            CapabilityGap("pane-" + role, e.GetType().Name + ": " + e.Message);
            return null;
        }
    }

    public GameObject ClonePaneName(Transform parent, string cloneName)
    {
        if (!Refresh() || _paneName == null) return null;
        return DsPortUtil.CloneStaticResidentVisual(_paneName.gameObject, parent, cloneName,
                                                    DsPresentation.CONTENT_LAYER,
                                                    typeof(PaneText));
    }

    public GameObject CloneTopOrnament(Transform parent) =>
        CloneResidentImage("frame-top-ornament", FrameTopPath, FrameTopSprite,
                           959f, 106f, 959f, 106f, parent);

    public GameObject CloneBottomOrnament(Transform parent) =>
        CloneResidentImage("frame-bottom-ornament", FrameBottomPath, FrameBottomSprite,
                           355f, 134f, 303f, 66f, parent);

    public GameObject CloneSelectedTopFleur(Transform parent) =>
        CloneResidentImage("selected-top-fleur", SelectedTopPath, SelectedTopSprite,
                           426f, 123f, 426f, 123f, parent);

    public GameObject CloneSelectedBottomFleur(Transform parent) =>
        CloneResidentImage("selected-bottom-fleur", SelectedBottomPath, SelectedBottomSprite,
                           355f, 134f, 355f, 134f, parent);

    GameObject CloneResidentImage(string role, string exactSourcePath, string exactSpriteName,
                                  float expectedSpriteWidth, float expectedSpriteHeight,
                                  float expectedRectWidth, float expectedRectHeight,
                                  Transform parent)
    {
        if (!_attemptedImageRoles.Add(role)) return null;
        try
        {
            BuildImageIndex();
            List<Image> candidates;
            if (!_imageIndex.TryGetValue(exactSourcePath, out candidates))
            {
                CapabilityGap(role, "no live loaded Image at exact path " + exactSourcePath);
                return null;
            }

            var matches = new List<Image>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var source = candidates[i];
                if (source == null || source.sprite == null) continue;
                if (source.sprite.name != exactSpriteName) continue;
                Vector2 spriteSize = source.sprite.rect.size;
                var sourceRect = source.transform as RectTransform;
                if (sourceRect == null) continue;
                Vector2 sourceSize = sourceRect.sizeDelta;
                if (Mathf.Abs(spriteSize.x - expectedSpriteWidth) > 0.5f ||
                    Mathf.Abs(spriteSize.y - expectedSpriteHeight) > 0.5f ||
                    Mathf.Abs(sourceSize.x - expectedRectWidth) > 0.5f ||
                    Mathf.Abs(sourceSize.y - expectedRectHeight) > 0.5f) continue;
                matches.Add(source);
            }

            if (matches.Count != 1)
            {
                CapabilityGap(role, matches.Count == 0
                    ? "exact Image identity/dimensions did not match at " + exactSourcePath
                    : "duplicate exact Image matches at " + exactSourcePath + ": " + matches.Count);
                return null;
            }

            var match = matches[0];
            var matchRect = match.transform as RectTransform;
            Vector2 matchedSpriteSize = match.sprite.rect.size;
            Vector2 matchedSourceSize = matchRect.sizeDelta;
            ResidentProvenance(role, match.transform,
                "Image path=" + exactSourcePath + " sprite=" + exactSpriteName +
                " spriteRect=" + matchedSpriteSize.x.ToString("F0") + "x" +
                matchedSpriteSize.y.ToString("F0") + " sourceRect=" +
                matchedSourceSize.x.ToString("F0") + "x" + matchedSourceSize.y.ToString("F0"));
            var clone = DsPortUtil.CloneStaticResidentVisual(match.gameObject, parent,
                "DsResident-" + role, DsPresentation.CONTENT_LAYER, typeof(Image));
            var clonedImage = clone != null ? clone.GetComponent<Image>() : null;
            if (clonedImage != null && clonedImage.sprite != null &&
                clonedImage.sprite.name == exactSpriteName)
                return clone;

            if (clone != null) UnityEngine.Object.Destroy(clone);
            CapabilityGap(role, "validated source did not retain exact sprite after clone: " + exactSpriteName);
            return null;
        }
        catch (Exception e)
        {
            CapabilityGap(role, e.GetType().Name + ": " + e.Message);
            return null;
        }
        CapabilityGap(role, "no live validated Image at " + exactSourcePath +
                            " with exact sprite " + exactSpriteName +
                            " and Sprite/RectTransform dimensions");
        return null;
    }

    void BuildImageIndex()
    {
        if (_imageIndexBuilt) return;
        _imageIndexBuilt = true;
        _imageIndex.Clear();
        var images = Resources.FindObjectsOfTypeAll<Image>();
        for (int i = 0; i < images.Length; i++)
        {
            var image = images[i];
            if (image == null || image.gameObject.name.StartsWith("DsResident-", StringComparison.Ordinal))
                continue;
            var scene = image.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) continue;
            string path = HierarchyPath(image.transform);
            List<Image> atPath;
            if (!_imageIndex.TryGetValue(path, out atPath))
            {
                atPath = new List<Image>();
                _imageIndex[path] = atPath;
            }
            atPath.Add(image);
        }
    }

    static Transform FindAncestor(Transform start, string exactName)
    {
        for (var at = start; at != null; at = at.parent)
            if (string.Equals(at.name, exactName, StringComparison.Ordinal)) return at;
        return null;
    }

    public void ResidentProvenance(string role, Transform source, string contractPath)
    {
        if (source == null) return;
        string key = role + "|" + source.GetInstanceID();
        if (!_reportedSources.Add(key)) return;
        Debug.Log("[DualScreen][resident] role=" + role + " contract=" + contractPath +
                  " source=" + HierarchyPath(source) + " scene=" + source.gameObject.scene.name);
    }

    public void CapabilityGap(string role, string detail)
    {
        string key = role + "|" + detail;
        if (!_reportedGaps.Add(key)) return;
        Debug.LogWarning("[DualScreen][capability-gap] role=" + role + " " + detail +
                         "; no synthetic visual created");
    }

    static string HierarchyPath(Transform source)
    {
        if (source == null) return "<null>";
        string path = source.name;
        for (var at = source.parent; at != null; at = at.parent) path = at.name + "/" + path;
        return path;
    }

    public sealed class HudRoot
    {
        public string Key;
        public DsHudRole Role;
        public Transform Root;
        public Component Driver;
    }

    public sealed class HudSources
    {
        public GameCameras Cameras;
        public HUDCamera Camera;
        public GameObject Gameplay;
        public PlayMakerFSM Slide;
        public SilkSpool Spool;
        public HudRoot[] Roots;
        public bool OwnsCurrentRig(GameCameras cameras, HUDCamera camera)
        {
            if (Cameras != cameras || Camera != camera || camera == null ||
                Gameplay != camera.GameplayChild || Slide != cameras.hudCanvasSlideOut ||
                Spool != cameras.silkSpool) return false;
            foreach (var root in Roots) if (root.Root == null || root.Driver == null) return false;
            return true;
        }
    }

    HudSources _hudSources;
    GameCameras _hudCameras;
    HUDCamera _hudCamera;
    GameObject _hudGameplay;
    PlayMakerFSM _hudSlide;
    SilkSpool _hudSpool;
    float _nextHudProbe;

    // The combat HUD is instantiated at runtime, not present in the cached
    // static HUD art bundle. Discover typed owners, never screenshot-derived paths.
    // The exact serialized fields below were inspected in 1.0.29980 managed code.
    public bool TryGetHudSources(out HudSources sources)
    {
        sources = null;
        var cameras = GameCameras.SilentInstance;
        var camera = cameras != null && cameras.hudCamera != null
            ? cameras.hudCamera.GetComponent<HUDCamera>() : null;
        var gameplay = camera != null ? camera.GameplayChild : null;
        var slide = cameras != null ? cameras.hudCanvasSlideOut : null;
        var spool = cameras != null ? cameras.silkSpool : null;
        bool changed = _hudCameras != cameras || _hudCamera != camera ||
            _hudGameplay != gameplay || _hudSlide != slide || _hudSpool != spool;
        if (changed)
        {
            _hudSources = null;
            _nextHudProbe = 0f;
            _hudCameras = cameras; _hudCamera = camera; _hudGameplay = gameplay;
            _hudSlide = slide; _hudSpool = spool;
        }
        if (_hudSources != null && !_hudSources.OwnsCurrentRig(cameras, camera))
        { _hudSources = null; _nextHudProbe = 0f; }
        if (Time.unscaledTime < _nextHudProbe)
        { sources = _hudSources; return sources != null; }
        _nextHudProbe = Time.unscaledTime + 0.5f;
        if (gameplay == null || slide == null || spool == null ||
            !slide.transform.IsChildOf(gameplay.transform)) return false;
        try
        {
            // Include our cached routed roots in the typed search: once routed,
            // they intentionally no longer descend from the native slide owner.
            var scopes = new List<Transform> { slide.transform };
            if (_hudSources != null)
                foreach (var root in _hudSources.Roots) scopes.Add(root.Root);
            var roots = new List<HudRoot>();
            var health = new HashSet<Transform>();
            Component healthDriver = null;
            foreach (var fsm in HudComponents<PlayMakerFSM>(scopes))
                if (fsm.FsmName == "health_display" &&
                    fsm.GetComponentInChildren<tk2dSprite>(true) != null)
                { health.Add(fsm.transform); healthDriver = fsm; }
            Transform healthRoot = null;
            foreach (var candidate in health) healthRoot = CommonHudRoot(healthRoot, candidate);
            if (healthRoot == null || healthRoot == slide.transform)
                throw new InvalidOperationException("missing or ambiguous health_display tk2d subtree");
            roots.Add(new HudRoot { Key = "health", Role = DsHudRole.Health,
                Root = healthRoot, Driver = healthDriver });
            roots.Add(new HudRoot { Key = "silk", Role = DsHudRole.Silk, Driver = spool,
                Root = HudVisualRoot(spool, "chunkParent", "capR", "capRAnchored", "seg1",
                    "bindNotch", "silkFailedAnimator", "spoolParent", "activeParent", "brokenParent",
                    "cursedParent", "cursedAnimator", "silkFinalCutsceneBurst", "act3EndingParent",
                    "act3EndingBarScaler", "act3EndingBarInverseScalers") });
            foreach (var counter in HudComponents<CurrencyCounter>(scopes))
            {
                var kind = (CurrencyType)HudField(counter, "currencyType");
                if (kind != CurrencyType.Money && kind != CurrencyType.Shard) continue;
                roots.Add(new HudRoot { Key = "currency-" + kind,
                    Role = kind == CurrencyType.Money ? DsHudRole.Money : DsHudRole.Shards,
                    Driver = counter, Root = HudVisualRoot(counter, "icon", "geoTextMesh", "subTextMesh",
                        "addTextMesh", "limitTextMesh", "fadeGroup", "rollerFade", "amountLayoutGroup", "failAnimator") });
            }
            foreach (var bind in HudComponents<BindOrbHudFrame>(scopes))
                roots.Add(new HudRoot { Key = "bind", Role = DsHudRole.Bind, Driver = bind,
                    Root = HudVisualRoot(bind, "changeParticle", "hunterV2Bar", "hunterV3BarA",
                        "hunterV3BarB", "hunterV3ExtraHitEffect", "reaperModeEffect") });
            foreach (var tool in HudComponents<ToolHudIcon>(scopes))
                roots.Add(new HudRoot { Key = "tool-" + HudField(tool, "binding"),
                    Role = DsHudRole.Tool, Driver = tool,
                    Root = HudVisualRoot(tool, "icon", "radialImage", "radialImageBg", "templateNotch",
                        "animator", "skillZapIcon") });
            // Reject unrelated/common container expansion before the routing state
            // validates all essential role counts, uniqueness and independence.
            foreach (var root in roots)
                if (root.Root == null || root.Root == slide.transform || root.Root == gameplay.transform)
                    throw new InvalidOperationException("non-independent HUD visual dependencies: " + root.Key);
            roots.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            bool same = _hudSources != null && _hudSources.Roots.Length == roots.Count;
            if (same)
                for (int i = 0; i < roots.Count; i++)
                    if (_hudSources.Roots[i].Root != roots[i].Root || _hudSources.Roots[i].Driver != roots[i].Driver ||
                        _hudSources.Roots[i].Key != roots[i].Key) { same = false; break; }
            if (!same)
                _hudSources = new HudSources { Cameras = cameras, Camera = camera, Gameplay = gameplay,
                    Slide = slide, Spool = spool, Roots = roots.ToArray() };
            sources = _hudSources;
            return true;
        }
        catch (Exception e)
        {
            _hudSources = null;
            CapabilityGap("live-hud", e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    static List<T> HudComponents<T>(List<Transform> scopes) where T : Component
    {
        var result = new List<T>();
        var seen = new HashSet<int>();
        foreach (var scope in scopes)
        {
            if (scope == null) continue;
            foreach (var component in scope.GetComponentsInChildren<T>(true))
                if (component != null && seen.Add(component.GetInstanceID())) result.Add(component);
        }
        return result;
    }

    static object HudField(Component driver, string name)
    {
        for (Type type = driver.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Public | PrivateInstance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(driver);
        }
        throw new MissingFieldException(driver.GetType().Name, name);
    }

    static Transform HudVisualRoot(Component driver, params string[] fields)
    {
        Transform root = driver.transform;
        foreach (string field in fields)
        {
            object value = HudField(driver, field);
            var array = value as Array;
            if (array != null)
                foreach (object element in array) root = IncludeHudVisual(root, element);
            else root = IncludeHudVisual(root, value);
        }
        return root;
    }

    static Transform IncludeHudVisual(Transform root, object value)
    {
        var component = value as Component;
        var go = value as GameObject;
        var visual = component != null ? component.transform : go != null ? go.transform : null;
        if (visual == null || !visual.gameObject.scene.IsValid()) return root;
        return CommonHudRoot(root, visual);
    }

    static Transform CommonHudRoot(Transform a, Transform b)
    {
        if (a == null) return b;
        for (var at = a; at != null; at = at.parent)
            if (b == at || b.IsChildOf(at)) return at;
        throw new InvalidOperationException("HUD dependencies do not share a live root");
    }

    public void Forget()
    {
        _paneList = null;
        _inventoryRoot = null;
        _paneName = null;
        _refreshAttempted = false;
        _imageIndexBuilt = false;
        _attemptedImageRoles.Clear();
        _imageIndex.Clear();
    }
}
#endif
