// DsPortLayers — empty ownership boundaries for the Dual Souls composition.
//
// Stage 1 establishes where later port modules attach and nothing more. These
// roots deliberately contain no widgets, layout, labels, art, or page state.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

public sealed class DsPortLayers
{
    // World renderers ignore RectTransform sibling order, so the same page <
    // functional mask < frame < tab < HUD relationship is also explicit here.
    public const int PAGE_RENDER_ORDER = 1000;
    public const int MASK_RENDER_ORDER = 3000;
    public const int FRAME_RENDER_ORDER = 4000;
    public const int TAB_RENDER_ORDER = 6000;
    public const int HUD_RENDER_ORDER = 7000;

    public RectTransform Content { get; private set; }
    public RectTransform Frame { get; private set; }
    public RectTransform Pages { get; private set; }
    public RectTransform HUD { get; private set; }
    public RectTransform Overlays { get; private set; }
    public RectTransform Fade { get; private set; }

    public DsPortLayers(DsPresentation presentation)
    {
        Content = CreateRoot("Content", presentation.Root, DsPresentation.CONTENT_LAYER);
        Frame = CreateRoot("Frame", presentation.Root, DsPresentation.CONTENT_LAYER);
        Pages = CreateRoot("Pages", presentation.Root, DsPresentation.CONTENT_LAYER);
        HUD = CreateRoot("HUD", presentation.Root, DsPresentation.CONTENT_LAYER);
        Overlays = CreateRoot("Overlays", presentation.OverlayRoot, DsPresentation.OVERLAY_LAYER);
        Fade = CreateRoot("Fade", presentation.OverlayRoot, DsPresentation.OVERLAY_LAYER);
        ConfigureCanvasOrder(Pages, PAGE_RENDER_ORDER);
        ConfigureCanvasOrder(Frame, FRAME_RENDER_ORDER);
        ConfigureCanvasOrder(HUD, HUD_RENDER_ORDER);
        ApplyCompositionOrder();
    }

    static void ConfigureCanvasOrder(RectTransform root, int sortingOrder)
    {
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingLayerID = 0;
        canvas.sortingOrder = sortingOrder;
    }

    // Match the reference composition roles explicitly. Pages render below
    // frame chrome, and the persistent HUD remains above both. Overlays and
    // fade keep the same relationship on their separate proven layer.
    void ApplyCompositionOrder()
    {
        Content.SetSiblingIndex(0);
        Pages.SetSiblingIndex(1);
        Frame.SetSiblingIndex(2);
        HUD.SetSiblingIndex(3);
        Overlays.SetSiblingIndex(0);
        Fade.SetSiblingIndex(1);
    }

    static RectTransform CreateRoot(string name, RectTransform parent, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        var root = go.AddComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        return root;
    }

    public void SetVisible(bool visible)
    {
        SetVisible(Content, visible);
        SetVisible(Frame, visible);
        SetVisible(Pages, visible);
        SetVisible(HUD, visible);
        SetVisible(Overlays, visible);
        SetVisible(Fade, visible);
    }

    static void SetVisible(RectTransform root, bool visible)
    {
        if (root != null && root.gameObject.activeSelf != visible)
            root.gameObject.SetActive(visible);
    }

    public void Dispose()
    {
        Destroy(Content);
        Destroy(Frame);
        Destroy(Pages);
        Destroy(HUD);
        Destroy(Overlays);
        Destroy(Fade);

        Content = null;
        Frame = null;
        Pages = null;
        HUD = null;
        Overlays = null;
        Fade = null;
    }

    static void Destroy(RectTransform root)
    {
        if (root != null) Object.Destroy(root.gameObject);
    }
}
#endif
