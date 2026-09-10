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
        public Component[] Drivers;
    }

    public sealed class HudSources
    {
        public GameCameras Cameras;
        public HUDCamera Camera;
        public GameObject Gameplay;
        public PlayMakerFSM Slide;
        public SilkSpool Spool;
        public Transform HudCanvas;
        public HudRoot[] Roots;
        public bool OwnsCurrentRig(GameCameras cameras, HUDCamera camera)
        {
            if (Cameras != cameras || Camera != camera || camera == null ||
                Gameplay != camera.GameplayChild || Slide != cameras.hudCanvasSlideOut ||
                Spool != cameras.silkSpool || HudCanvas == null || Roots == null ||
                Roots.Length != DsHud29980Topology.RootNames.Length) return false;
            foreach (var root in Roots)
            {
                // A bound root intentionally no longer has HudCanvas as parent;
                // identity and driver ancestry remain the current-rig authority.
                if (root.Root == null || root.Drivers == null) return false;
                foreach (var driver in root.Drivers)
                    if (driver == null || (driver.transform != root.Root && !driver.transform.IsChildOf(root.Root)))
                        return false;
            }
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

    const string HudCanvasPath = "Anchor TL/Hud Canvas Offset/Hud Canvas";
    static readonly DsHudRole[] HudRootRoles =
    {
        DsHudRole.Health, DsHudRole.Status, DsHudRole.Silk, DsHudRole.Tool,
        DsHudRole.Context, DsHudRole.Context, DsHudRole.Counters,
        DsHudRole.Health, DsHudRole.Health,
    };

    // The combat HUD is instantiated at runtime, not present in the cached
    // static HUD art bundle. Start at the typed current HUD rig, then admit only
    // the exact coherent direct children of its 1.0.29980 Hud Canvas.
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
        if (_hudSources != null)
        {
            if (_hudSources.OwnsCurrentRig(cameras, camera))
            { sources = _hudSources; return true; }
            _hudSources = null;
            _nextHudProbe = 0f;
        }
        if (Time.unscaledTime < _nextHudProbe) return false;
        _nextHudProbe = Time.unscaledTime + 0.5f;
        if (gameplay == null || slide == null || spool == null ||
            !slide.transform.IsChildOf(gameplay.transform)) return false;
        try
        {
            var hudCanvas = gameplay.transform.Find(HudCanvasPath);
            if (hudCanvas == null || hudCanvas.childCount != DsHud29980Topology.RootNames.Length)
                throw new InvalidOperationException("exact Hud Canvas topology unavailable");
            if (slide.transform != hudCanvas && !slide.transform.IsChildOf(hudCanvas) &&
                !hudCanvas.IsChildOf(slide.transform))
                throw new InvalidOperationException("typed slide owner does not own exact Hud Canvas");

            var direct = new Transform[DsHud29980Topology.RootNames.Length];
            for (int i = 0; i < DsHud29980Topology.RootNames.Length; i++)
                direct[i] = DirectHudChild(hudCanvas, DsHud29980Topology.RootNames[i]);

            var healthFsms = HudComponents<PlayMakerFSM>(direct[0]);
            var healthDisplays = new List<PlayMakerFSM>();
            foreach (var fsm in healthFsms)
                if (fsm.FsmName == "health_display") healthDisplays.Add(fsm);

            var extras = new[]
            {
                DirectHudChild(direct[1], "Reserve Bind"),
                DirectHudChild(direct[1], "Lava Bell HUD"),
                DirectHudChild(direct[1], "Maggot Charm"),
            };
            var extrasFsms = new int[3];
            var extrasPositioners = new int[3];
            var extrasRegisters = new int[3];
            var extrasAnimators = new int[3];
            for (int i = 0; i < extras.Length; i++)
            {
                extrasFsms[i] = extras[i].GetComponents<PlayMakerFSM>().Length;
                extrasPositioners[i] = extras[i].GetComponents<PositionRelativeTo>().Length;
                extrasRegisters[i] = extras[i].GetComponents<EventRegister>().Length;
                extrasAnimators[i] = extras[i].GetComponents<Animator>().Length;
            }
            bool exactExtrasOwners = HudComponents<PlayMakerFSM>(direct[1]).Count == 3 &&
                HudComponents<PositionRelativeTo>(direct[1]).Count == 3 &&
                HudComponents<EventRegister>(direct[1]).Count == 15 &&
                HudComponents<Animator>(direct[1]).Count == 1;

            var spoolRoot = DirectHudChild(direct[2], "Spool");
            var bindOrbRoot = DirectHudChild(spoolRoot, "Bind Orb");
            var spoolDrivers = spoolRoot.GetComponents<SilkSpool>();
            var allSpoolDrivers = HudComponents<SilkSpool>(direct[2]);
            var bindDrivers = HudComponents<BindOrbHudFrame>(direct[2]);
            bool exactBindOwner = bindDrivers.Count == 1 &&
                (bindDrivers[0].transform == bindOrbRoot || bindDrivers[0].transform.IsChildOf(bindOrbRoot));

            var toolDrivers = HudComponents<ToolHudIcon>(direct[3]);
            bool exactToolOwners = toolDrivers.Count == 3;
            string[] toolNames = { "Tool Icon U", "Tool Icon N", "Tool Icon D" };
            foreach (var toolName in toolNames)
                if (DirectHudChild(direct[3], toolName).GetComponents<ToolHudIcon>().Length != 1)
                    exactToolOwners = false;

            var crestFlash = DirectHudChild(direct[4], "Crest Change Flash");
            var crestParticles = DirectHudChild(direct[4], "Pt Dots");
            var crestDeactivators = HudComponents<DeactivateAfter2dtkAnimation>(direct[4]);
            var crestAnimators = HudComponents<tk2dSpriteAnimator>(direct[4]);
            bool exactCrestDeactivator = crestDeactivators.Count == 1 &&
                crestFlash.GetComponents<DeactivateAfter2dtkAnimation>().Length == 1;
            bool exactCrestAnimator = crestAnimators.Count == 1 &&
                crestFlash.GetComponents<tk2dSpriteAnimator>().Length == 1;
            bool exactCrestParticles = HudComponents<ParticleSystem>(direct[4]).Count == 1 &&
                crestParticles.GetComponents<ParticleSystem>().Length == 1;

            var deliveryDrivers = HudComponents<DeliveryHudIcon>(direct[5]);
            bool exactDeliveryOwner = deliveryDrivers.Count == 1 &&
                direct[5].GetComponents<DeliveryHudIcon>().Length == 1;

            var stacks = HudComponents<CurrencyCounterStack>(direct[6]);
            var counters = HudComponents<CurrencyCounter>(direct[6]);
            var geoRoot = DirectHudChild(direct[6], "Geo Counter");
            var shardRoot = DirectHudChild(direct[6], "Shard Counter");
            int money = CurrencyOwnerCount(geoRoot, CurrencyType.Money);
            int shards = CurrencyOwnerCount(shardRoot, CurrencyType.Shard);
            bool exactCounterOwners = stacks.Count == 1 &&
                direct[6].GetComponents<CurrencyCounterStack>().Length == 1 &&
                counters.Count == 2;
            var itemTemplates = DirectHudChild(direct[6], "Item Counter Template")
                .GetComponents<ItemCurrencyCounter>();
            var liquidTemplates = DirectHudChild(direct[6], "Liquid Counter Template")
                .GetComponents<LiquidReserveCounter>();
            bool exactItemTemplate = itemTemplates.Length == 1 &&
                HudComponents<ItemCurrencyCounter>(direct[6]).Count == 1;
            bool exactLiquidTemplate = liquidTemplates.Length == 1 &&
                HudComponents<LiquidReserveCounter>(direct[6]).Count == 1;

            var burstCameraControls = direct[7].GetComponents<CameraControlAnimationEvents>();
            var burstAnimators = direct[7].GetComponents<Animator>();
            var burstTimers = direct[7].GetComponents<DisableAfterTime>();
            var dripsChild = DirectHudChild(direct[8], "Blue_Health_Overblue_HUD_drips");
            var dripsRootParticles = direct[8].GetComponents<ParticleSystem>();
            var dripsChildParticles = dripsChild.GetComponents<ParticleSystem>();

            var inventory = new DsHud29980Inventory
            {
                RootNames = DirectChildNames(hudCanvas),
                ExtrasChildren = DirectChildNames(direct[1]),
                ThreadChildren = DirectChildNames(direct[2]),
                SpoolChildren = DirectChildNames(spoolRoot),
                ToolChildren = DirectChildNames(direct[3]),
                CounterChildren = DirectChildNames(direct[6]),
                CrestChildren = DirectChildNames(direct[4]),
                DeliveryChildren = DirectChildNames(direct[5]),
                BurstChildren = DirectChildNames(direct[7]),
                DripsChildren = DirectChildNames(direct[8]),
                ExtrasPlayMakerFsms = exactExtrasOwners ? extrasFsms : null,
                ExtrasPositioners = exactExtrasOwners ? extrasPositioners : null,
                ExtrasEventRegisters = exactExtrasOwners ? extrasRegisters : null,
                ExtrasAnimators = exactExtrasOwners ? extrasAnimators : null,
                HealthDisplayDrivers = healthDisplays.Count,
                SilkSpools = allSpoolDrivers.Count == 1 ? spoolDrivers.Length : 0,
                BindOrbFrames = exactBindOwner ? bindDrivers.Count : 0,
                ToolHudIcons = exactToolOwners ? toolDrivers.Count : 0,
                CounterStacks = exactCounterOwners ? stacks.Count : 0,
                MoneyCounters = money,
                ShardCounters = shards,
                ItemCounterTemplates = exactItemTemplate ? itemTemplates.Length : 0,
                LiquidCounterTemplates = exactLiquidTemplate ? liquidTemplates.Length : 0,
                DeliveryHudIcons = exactDeliveryOwner ? deliveryDrivers.Count : 0,
                CrestDeactivators = exactCrestDeactivator ? crestDeactivators.Count : 0,
                CrestAnimators = exactCrestAnimator ? crestAnimators.Count : 0,
                CrestParticleSystems = exactCrestParticles ? 1 : 0,
                BurstCameraControls = burstCameraControls.Length,
                BurstAnimators = burstAnimators.Length,
                BurstDisableAfterTime = burstTimers.Length,
                DripsRootParticleSystems = dripsRootParticles.Length,
                DripsChildParticleSystems = dripsChildParticles.Length,
            };
            string topologyGap;
            if (!DsHud29980Topology.TryAdmit(inventory, out topologyGap))
                throw new InvalidOperationException(topologyGap);
            if (spool != spoolDrivers[0])
                throw new InvalidOperationException("typed SilkSpool is not Thread/Spool");

            var driverGroups = new List<Component>[DsHud29980Topology.RootNames.Length];
            for (int i = 0; i < driverGroups.Length; i++) driverGroups[i] = new List<Component>();
            foreach (var driver in healthFsms) driverGroups[0].Add(driver);
            foreach (var driver in HudComponents<PlayMakerFSM>(direct[1])) driverGroups[1].Add(driver);
            foreach (var driver in HudComponents<PositionRelativeTo>(direct[1])) driverGroups[1].Add(driver);
            foreach (var driver in HudComponents<EventRegister>(direct[1])) driverGroups[1].Add(driver);
            foreach (var driver in HudComponents<Animator>(direct[1])) driverGroups[1].Add(driver);
            driverGroups[2].Add(spoolDrivers[0]); driverGroups[2].Add(bindDrivers[0]);
            foreach (var driver in toolDrivers) driverGroups[3].Add(driver);
            foreach (var driver in crestDeactivators) driverGroups[4].Add(driver);
            foreach (var driver in crestAnimators) driverGroups[4].Add(driver);
            driverGroups[4].Add(crestParticles.GetComponent<ParticleSystem>());
            driverGroups[5].Add(deliveryDrivers[0]);
            driverGroups[6].Add(stacks[0]);
            foreach (var driver in counters) driverGroups[6].Add(driver);
            driverGroups[6].Add(itemTemplates[0]); driverGroups[6].Add(liquidTemplates[0]);
            foreach (var driver in burstCameraControls) driverGroups[7].Add(driver);
            foreach (var driver in burstAnimators) driverGroups[7].Add(driver);
            foreach (var driver in burstTimers) driverGroups[7].Add(driver);
            foreach (var driver in dripsRootParticles) driverGroups[8].Add(driver);
            foreach (var driver in dripsChildParticles) driverGroups[8].Add(driver);

            var roots = new HudRoot[DsHud29980Topology.RootNames.Length];
            for (int i = 0; i < roots.Length; i++)
                roots[i] = new HudRoot { Key = DsHud29980Topology.RootNames[i], Role = HudRootRoles[i],
                    Root = direct[i], Drivers = driverGroups[i].ToArray() };

            bool same = _hudSources != null && _hudSources.HudCanvas == hudCanvas &&
                _hudSources.Roots.Length == roots.Length;
            if (same)
                for (int i = 0; i < roots.Length; i++)
                {
                    var old = _hudSources.Roots[i];
                    if (old.Root != roots[i].Root || old.Key != roots[i].Key ||
                        old.Role != roots[i].Role || old.Drivers.Length != roots[i].Drivers.Length)
                    { same = false; break; }
                    for (int d = 0; d < roots[i].Drivers.Length; d++)
                        if (old.Drivers[d] != roots[i].Drivers[d]) { same = false; break; }
                    if (!same) break;
                }
            if (!same)
                _hudSources = new HudSources { Cameras = cameras, Camera = camera, Gameplay = gameplay,
                    Slide = slide, Spool = spool, HudCanvas = hudCanvas, Roots = roots };
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

    static int CurrencyOwnerCount(Transform owner, CurrencyType expected)
    {
        var drivers = owner.GetComponents<CurrencyCounter>();
        if (drivers.Length != 1) return 0;
        return (CurrencyType)HudField(drivers[0], "currencyType") == expected ? 1 : 0;
    }

    static string[] DirectChildNames(Transform parent)
    {
        var names = new string[parent.childCount];
        for (int i = 0; i < names.Length; i++) names[i] = parent.GetChild(i).name;
        return names;
    }

    static Transform DirectHudChild(Transform parent, string exactName)
    {
        Transform match = null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (!string.Equals(child.name, exactName, StringComparison.Ordinal)) continue;
            if (match != null) throw new InvalidOperationException("duplicate direct HUD root: " + exactName);
            match = child;
        }
        if (match == null) throw new InvalidOperationException("missing direct HUD root: " + exactName);
        return match;
    }

    static List<T> HudComponents<T>(Transform scope) where T : Component
    {
        var result = new List<T>();
        if (scope == null) return result;
        foreach (var component in scope.GetComponentsInChildren<T>(true))
            if (component != null) result.Add(component);
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
