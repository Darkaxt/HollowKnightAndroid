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
