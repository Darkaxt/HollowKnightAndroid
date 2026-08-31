// DsPortFrame — Dual Souls Bottom.Frame responsibilities on display 1.
//
// Visible chrome and text are validated resident Silksong clones. Owned black
// renderer covers are functional clipping for world-space page renderers, not
// replacement art. Page content remains empty until its named later stage.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PaneText = TMProOld.TextMeshPro;

public enum DsPageRole { Inventory, Loadout, Tasks, Journal, Map }

public sealed class DsPortFrame
{
    sealed class Tab
    {
        public DsPageRole Role;
        public InventoryPane Pane;
        public RectTransform Slot;
        public GameObject Label;
        public Bounds GlyphBounds;
        public float HitMinNorm;
        public float HitMaxNorm;
    }

    static readonly DsPageRole[] ApprovedPageOrder =
    {
        DsPageRole.Inventory,
        DsPageRole.Loadout,
        DsPageRole.Tasks,
        DsPageRole.Journal,
        DsPageRole.Map,
    };

    readonly DsPortLayers _layers;
    readonly DsResidentUi _resident;
    readonly Dictionary<DsPageRole, RectTransform> _pageHosts =
        new Dictionary<DsPageRole, RectTransform>();
    readonly List<Tab> _tabs = new List<Tab>();
    readonly DsRendererMaskCover[] _rendererMasks = new DsRendererMaskCover[4];

    RectTransform _frameRoot;
    RectTransform _tabRow;
    public RectTransform ContentMask { get; private set; }
    public RectTransform StatusAnchor { get; private set; }
    public RectTransform ModsAnchor { get; private set; }
    RectTransform _statusMask;
    RectTransform _modsMask;

    GameObject _topOrnament;
    GameObject _bottomOrnament;
    public GameObject SelectedTopFleur { get; private set; }
    public GameObject SelectedBottomFleur { get; private set; }

    public float InnerTop { get; private set; }
    public float InnerBottom { get; private set; }
    float _innerLeft;
    float _innerRight;

    DsPageRole _selected = DsPageRole.Inventory;
    DsPortFrameDecision _selectionState =
        DsPortFrameState.Initial(ApprovedPageOrder.Length, 0);
    float _slideT = 1f;
    bool _sourceStateKnown;
    bool _lastInGame;
    bool _buildAttempted;
    bool _built;
    bool _disposed;

    public DsPageRole SelectedRole => _selected;

    public DsPortFrame(DsPortLayers layers)
    {
        _layers = layers;
        _resident = new DsResidentUi();
    }

    public void Tick(float dt)
    {
        if (_disposed) return;
        bool inGame = DsGameData.InGame;
        if (!_sourceStateKnown || inGame != _lastInGame)
        {
            _sourceStateKnown = true;
            _lastInGame = inGame;
            InvalidateResidentSources();
        }
        if (!_built)
        {
            if (inGame && !_buildAttempted)
            {
                _buildAttempted = true;
                TryBuild();
            }
            return;
        }
        TickHorizontalSlide(dt);
        if ((Time.frameCount % 120) == 0)
        {
            CompleteMissingResidentRoles();
            LayoutNativeTabLabels();
            UpdateFrameGeometry();
        }
    }

    void TryBuild()
    {
        if (_built || !_resident.Refresh()) return;

        _frameRoot = DsPortUtil.CreateRoot(_layers.Frame, "DsPortFrameRoot",
            DsPresentation.CONTENT_LAYER, Vector2.zero, Vector2.one);

        // RectMask2D clips future uGUI children. Renderer pages use the four
        // screen-cover meshes below because RectMask2D cannot clip native
        // world TextMeshPro or SpriteRenderer geometry.
        ContentMask = DsPortUtil.CreateRoot(_layers.Pages, "ContentMask",
            DsPresentation.CONTENT_LAYER, new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.76f));
        ContentMask.gameObject.AddComponent<RectMask2D>();

        _statusMask = DsPortUtil.CreateRoot(_frameRoot, "StatusMask",
            DsPresentation.CONTENT_LAYER, new Vector2(0.04f, 0.78f), new Vector2(0.82f, 0.96f));
        _statusMask.gameObject.AddComponent<RectMask2D>();
        StatusAnchor = DsPortUtil.CreateRoot(_statusMask, "StatusAnchor",
            DsPresentation.CONTENT_LAYER, Vector2.zero, Vector2.one);

        _modsMask = DsPortUtil.CreateRoot(_frameRoot, "ModsMask",
            DsPresentation.CONTENT_LAYER, new Vector2(0.86f, 0.78f), new Vector2(0.96f, 0.96f));
        _modsMask.gameObject.AddComponent<RectMask2D>();
        ModsAnchor = DsPortUtil.CreateRoot(_modsMask, "ModsAnchor",
            DsPresentation.CONTENT_LAYER, Vector2.zero, Vector2.one);

        _tabRow = DsPortUtil.CreateRoot(_frameRoot, "BottomCentredTabRow",
            DsPresentation.CONTENT_LAYER, new Vector2(0.09f, 0f), new Vector2(0.91f, 0.15f));

        BuildRendererMasks();
        CompleteMissingResidentRoles();
        BuildBottomCentredTabs();
        if (_tabs.Count > 0 && !HasTab(_selected)) _selected = _tabs[0].Role;
        _selectionState = DsPortFrameState.Initial(
            ApprovedPageOrder.Length, Array.IndexOf(ApprovedPageOrder, _selected));
        ApplyTabSelectionAlpha();
        LayoutNativeTabLabels();
        for (int i = 0; i < ApprovedPageOrder.Length; i++)
            GetOrCreatePageHost(ApprovedPageOrder[i]);
        ApplyPageVisibility();
        UpdateFrameGeometry();
        _built = _tabs.Count > 0;

        if (!_built)
        {
            _resident.CapabilityGap("frame-tabs", "native Pane Name clone could not build any semantic tab");
            DestroyComposition();
        }
    }

    void BuildRendererMasks()
    {
        for (int i = 0; i < _rendererMasks.Length; i++)
            if (_rendererMasks[i] == null)
                _rendererMasks[i] = new DsRendererMaskCover(_frameRoot,
                    "RendererMaskCover-" + i, DsPresentation.CONTENT_LAYER,
                    DsPortLayers.MASK_RENDER_ORDER + i);
    }

    void CompleteMissingResidentRoles()
    {
        if (_frameRoot == null) return;
        if (_topOrnament == null)
            _topOrnament = _resident.CloneTopOrnament(_frameRoot);
        if (_bottomOrnament == null)
            _bottomOrnament = _resident.CloneBottomOrnament(_frameRoot);
        if (SelectedTopFleur == null)
            SelectedTopFleur = _resident.CloneSelectedTopFleur(_frameRoot);
        if (SelectedBottomFleur == null)
            SelectedBottomFleur = _resident.CloneSelectedBottomFleur(_frameRoot);

        NormalizeResidentVisual(_topOrnament, DsPortLayers.FRAME_RENDER_ORDER);
        NormalizeResidentVisual(_bottomOrnament, DsPortLayers.FRAME_RENDER_ORDER + 20);
        NormalizeResidentVisual(SelectedTopFleur, DsPortLayers.FRAME_RENDER_ORDER + 40);
        NormalizeResidentVisual(SelectedBottomFleur, DsPortLayers.FRAME_RENDER_ORDER + 60);
    }

    static void NormalizeResidentVisual(GameObject visual, int rendererOrder)
    {
        if (visual == null) return;
        DsPortUtil.NormalizeRenderers(visual, rendererOrder);
        visual.transform.SetAsLastSibling();
    }

    void BuildBottomCentredTabs()
    {
        if (_tabRow == null || _tabs.Count != 0) return;
        for (int i = 0; i < ApprovedPageOrder.Length; i++)
        {
            var role = ApprovedPageOrder[i];
            var pane = _resident.GetPane(ToPaneType(role));
            if (pane == null) continue;

            var slot = DsPortUtil.CreateRoot(_tabRow, "Tab-" + role,
                DsPresentation.CONTENT_LAYER, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f));
            slot.sizeDelta = Vector2.one;

            var label = _resident.ClonePaneName(slot, "NativePaneName-" + role);
            var text = label != null ? label.GetComponent<PaneText>() : null;
            if (text == null)
            {
                _resident.CapabilityGap("tab-" + role,
                    "currentPaneText clone did not retain TMProOld.TextMeshPro");
                UnityEngine.Object.Destroy(slot.gameObject);
                continue;
            }
            try
            {
                text.text = pane.DisplayName;
                text.ForceMeshUpdate(true);
            }
            catch (Exception e)
            {
                _resident.CapabilityGap("tab-" + role,
                    "pane.DisplayName/mesh update failed: " + e.GetType().Name);
                UnityEngine.Object.Destroy(slot.gameObject);
                continue;
            }
            label.transform.localPosition = Vector3.zero;
            label.transform.localRotation = Quaternion.identity;
            DsPortUtil.NormalizeRenderers(label, DsPortLayers.TAB_RENDER_ORDER + i * 10);
            _tabs.Add(new Tab { Role = role, Pane = pane, Slot = slot, Label = label });
        }
    }

    void LayoutNativeTabLabels()
    {
        if (_tabs.Count == 0 || _tabRow == null || _frameRoot == null) return;
        float panelW = Mathf.Max(1f, DsPresentation.PanelW);
        float panelH = Mathf.Max(1f, DsPresentation.PanelH);
        float targetHeight = panelH * 0.034f;
        float gap = panelW * 0.024f;
        var widths = new float[_tabs.Count];
        float total = gap * (_tabs.Count - 1);

        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var text = tab.Label.GetComponent<PaneText>();
            var renderer = tab.Label.GetComponent<Renderer>();
            if (text == null || renderer == null) continue;
            tab.Label.transform.localPosition = Vector3.zero;
            text.ForceMeshUpdate(true);
            Bounds glyph;
            if (!TryRendererBoundsLocal(renderer, tab.Slot, out glyph)) continue;
            float scale = targetHeight / Mathf.Max(0.001f, glyph.size.y);
            tab.Label.transform.localScale = tab.Label.transform.localScale * scale;
            text.ForceMeshUpdate(true);
            if (!TryRendererBoundsLocal(renderer, tab.Slot, out glyph)) continue;
            widths[i] = glyph.size.x;
            total += widths[i];
        }

        float available = panelW * 0.80f;
        if (total > available && total > 0.001f)
        {
            float fit = available / total;
            total = gap * (_tabs.Count - 1);
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Label.transform.localScale = _tabs[i].Label.transform.localScale * fit;
                widths[i] *= fit;
                total += widths[i];
            }
        }

        float cursor = -total * 0.5f;
        for (int i = 0; i < _tabs.Count; i++)
        {
            float width = Mathf.Max(panelW * 0.04f, widths[i]);
            _tabs[i].Slot.sizeDelta = new Vector2(width + gap, panelH * 0.12f);
            _tabs[i].Slot.anchoredPosition = new Vector2(cursor + width * 0.5f, 0f);
            cursor += width + gap;
        }

        AlignTabBaselines();
        Canvas.ForceUpdateCanvases();
        for (int i = 0; i < _tabs.Count; i++)
        {
            var renderer = _tabs[i].Label.GetComponent<Renderer>();
            Bounds glyph;
            if (renderer != null && TryRendererBoundsLocal(renderer, _frameRoot, out glyph))
                _tabs[i].GlyphBounds = glyph;
        }
        UpdateResponsiveHitSlots(panelW);
    }

    void ApplyTabSelectionAlpha()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var text = tab.Label != null ? tab.Label.GetComponent<PaneText>() : null;
            if (text == null) continue;
            var color = text.color;
            var alpha = DsPortFrameState.LabelAlpha(tab.Role == _selected);
            text.color = new Color(color.r, color.g, color.b, alpha);
        }
    }

    void AlignTabBaselines()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var text = tab.Label.GetComponent<PaneText>();
            var renderer = tab.Label.GetComponent<Renderer>();
            if (text == null || renderer == null) continue;
            text.ForceMeshUpdate(true);
            Bounds glyph;
            if (!TryRendererBoundsLocal(renderer, tab.Slot, out glyph)) continue;
            tab.Label.transform.localPosition += new Vector3(-glyph.center.x, -glyph.min.y, 0f);
            text.ForceMeshUpdate(true);
        }
    }

    void UpdateResponsiveHitSlots(float panelW)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            float left = _tabs[i].GlyphBounds.min.x - panelW * 0.018f;
            float right = _tabs[i].GlyphBounds.max.x + panelW * 0.018f;
            if (i > 0) left = (_tabs[i - 1].GlyphBounds.center.x + _tabs[i].GlyphBounds.center.x) * 0.5f;
            if (i + 1 < _tabs.Count) right = (_tabs[i].GlyphBounds.center.x + _tabs[i + 1].GlyphBounds.center.x) * 0.5f;
            _tabs[i].HitMinNorm = Mathf.Clamp01(left / panelW + 0.5f);
            _tabs[i].HitMaxNorm = Mathf.Clamp01(right / panelW + 0.5f);
        }
    }

    static InventoryPaneList.PaneTypes ToPaneType(DsPageRole role)
    {
        switch (role)
        {
            case DsPageRole.Inventory: return InventoryPaneList.PaneTypes.Inv;
            case DsPageRole.Loadout: return InventoryPaneList.PaneTypes.Tools;
            case DsPageRole.Tasks: return InventoryPaneList.PaneTypes.Quests;
            case DsPageRole.Journal: return InventoryPaneList.PaneTypes.Journal;
            default: return InventoryPaneList.PaneTypes.Map;
        }
    }

    public RectTransform GetOrCreatePageHost(DsPageRole role)
    {
        RectTransform host;
        if (_pageHosts.TryGetValue(role, out host) && host != null) return host;
        if (ContentMask == null) return null;
        host = DsPortUtil.CreateRoot(ContentMask, "PageHost-" + role,
            DsPresentation.CONTENT_LAYER, Vector2.zero, Vector2.one);
        host.gameObject.SetActive(false);
        _pageHosts[role] = host;
        return host;
    }

    public void NormalizePageVisual(GameObject visual)
    {
        DsPortUtil.NormalizeRenderers(visual, DsPortLayers.PAGE_RENDER_ORDER);
    }

    public void OnGesture(DsGesture gesture)
    {
        if (_disposed || !_built || gesture.Type != DsGestureType.Tap || _tabs.Count == 0) return;
        float panelH = Mathf.Max(1f, DsPresentation.PanelH);
        if (gesture.Position.y > panelH * 0.15f) return;
        float normalizedX = gesture.Position.x / Mathf.Max(1f, DsPresentation.PanelW);
        for (int i = 0; i < _tabs.Count; i++)
            if (DsPortFrameState.ContainsHit(normalizedX, _tabs[i].HitMinNorm,
                                             _tabs[i].HitMaxNorm))
            {
                Select(_tabs[i].Role);
                return;
            }
    }

    public void Select(DsPageRole role)
    {
        if (_disposed || !_built || role == _selected || !HasTab(role)) return;
        BeginHorizontalSlide(_selected, role);
        _selected = role;
        ApplyTabSelectionAlpha();
        PositionSelectedFleursFromGlyphBounds();
        UpdateDynamicInnerEdges();
    }

    bool HasTab(DsPageRole role)
    {
        for (int i = 0; i < _tabs.Count; i++) if (_tabs[i].Role == role) return true;
        return false;
    }

    public void BeginHorizontalSlide(DsPageRole from, DsPageRole to)
    {
        NormalizePageHostsForInterruptedSlide(from);
        int toIndex = Array.IndexOf(ApprovedPageOrder, to);
        _selectionState = DsPortFrameState.BeginSelection(_selectionState, toIndex);
        _slideT = 0f;
        var outgoing = GetOrCreatePageHost(ApprovedPageOrder[_selectionState.OutgoingIndex]);
        var incoming = GetOrCreatePageHost(ApprovedPageOrder[_selectionState.IncomingIndex]);
        if (outgoing != null) outgoing.gameObject.SetActive(true);
        if (incoming != null)
        {
            incoming.gameObject.SetActive(true);
            incoming.anchoredPosition = new Vector2(_selectionState.Direction * ContentWidth(), 0f);
        }
    }

    void NormalizePageHostsForInterruptedSlide(DsPageRole current)
    {
        foreach (var pair in _pageHosts)
        {
            if (pair.Value == null) continue;
            pair.Value.anchoredPosition = Vector2.zero;
            pair.Value.gameObject.SetActive(pair.Key == current);
        }
    }

    void TickHorizontalSlide(float dt)
    {
        if (_slideT >= 1f) return;
        _slideT = Mathf.Min(1f, _slideT + Mathf.Max(0f, dt) / 0.24f);
        float eased = 1f - Mathf.Pow(1f - _slideT, 3f);
        float width = ContentWidth();
        RectTransform outgoing, incoming;
        _pageHosts.TryGetValue(ApprovedPageOrder[_selectionState.OutgoingIndex], out outgoing);
        _pageHosts.TryGetValue(ApprovedPageOrder[_selectionState.IncomingIndex], out incoming);
        if (outgoing != null)
            outgoing.anchoredPosition = new Vector2(-_selectionState.Direction * width * eased, 0f);
        if (incoming != null)
            incoming.anchoredPosition = new Vector2(
                _selectionState.Direction * width * (1f - eased), 0f);
        if (_slideT < 1f) return;
        _selectionState = DsPortFrameState.CompleteSelection(_selectionState);
        SetOnlySelectedPageActive(ApprovedPageOrder[_selectionState.SelectedIndex]);
    }

    void SetOnlySelectedPageActive(DsPageRole selected)
    {
        foreach (var pair in _pageHosts)
        {
            if (pair.Value == null) continue;
            pair.Value.anchoredPosition = Vector2.zero;
            int hostIndex = Array.IndexOf(ApprovedPageOrder, pair.Key);
            pair.Value.gameObject.SetActive(
                pair.Key == selected && HasTab(pair.Key) &&
                DsPortFrameState.IsHostActive(_selectionState, hostIndex));
        }
    }

    float ContentWidth()
    {
        if (ContentMask != null && ContentMask.rect.width > 1f) return ContentMask.rect.width;
        return Mathf.Max(1f, DsPresentation.PanelW * 0.92f);
    }

    void ApplyPageVisibility()
    {
        SetOnlySelectedPageActive(_selected);
    }

    void UpdateFrameGeometry()
    {
        PositionFrameOrnaments();
        PositionSelectedFleursFromGlyphBounds();
        UpdateDynamicInnerEdges();
    }

    void PositionFrameOrnaments()
    {
        float panelW = Mathf.Max(1f, DsPresentation.PanelW);
        float panelH = Mathf.Max(1f, DsPresentation.PanelH);
        if (ScaleResidentAspect(_topOrnament, panelW * 0.92f))
            PlaceVisualByBounds(_topOrnament, new Vector2(0f, panelH * 0.30f));
        if (ScaleResidentAspect(_bottomOrnament, panelW * 0.90f))
            PlaceVisualByBounds(_bottomOrnament, new Vector2(0f, -panelH * 0.30f));
    }

    bool ScaleResidentAspect(GameObject visual, float targetWidth)
    {
        if (visual == null) return false;
        var image = visual.GetComponent<Image>();
        if (image != null && image.sprite != null)
        {
            Vector2 nativeSize = image.sprite.rect.size;
            if (nativeSize.x <= 0.0001f || nativeSize.y <= 0.0001f) return false;
            var rect = visual.transform as RectTransform;
            if (rect == null) return false;
            image.preserveAspect = true;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = nativeSize;
            float uniform = targetWidth / nativeSize.x;
            rect.localRotation = Quaternion.identity;
            rect.localScale = new Vector3(uniform, uniform, uniform);
            return true;
        }
        var spriteRenderer = visual.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null || spriteRenderer.sprite == null) return false;
        Vector3 rendererSize = spriteRenderer.sprite.bounds.size;
        if (rendererSize.x <= 0.0001f || rendererSize.y <= 0.0001f) return false;
        float rendererScale = targetWidth / rendererSize.x;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = new Vector3(rendererScale, rendererScale, rendererScale);
        return true;
    }

    void PositionSelectedFleursFromGlyphBounds()
    {
        Tab selected = null;
        for (int i = 0; i < _tabs.Count; i++)
            if (_tabs[i].Role == _selected) { selected = _tabs[i]; break; }
        if (selected == null || selected.GlyphBounds.size.x <= 0f) return;
        float gap = Mathf.Max(1f, DsPresentation.PanelH) * 0.012f;
        if (ScaleResidentAspect(SelectedTopFleur, selected.GlyphBounds.size.x * 1.15f))
            PlaceVisualAtEdge(SelectedTopFleur, selected.GlyphBounds.center.x,
                              selected.GlyphBounds.max.y + gap, true);
        if (ScaleResidentAspect(SelectedBottomFleur, selected.GlyphBounds.size.x * 0.92f))
            PlaceVisualAtEdge(SelectedBottomFleur, selected.GlyphBounds.center.x,
                              selected.GlyphBounds.min.y - gap, false);
    }

    void PlaceVisualAtEdge(GameObject visual, float centerX, float edgeY, bool above)
    {
        if (visual == null) return;
        visual.transform.localPosition = Vector3.zero;
        Bounds bounds;
        if (!TryVisualBoundsLocal(visual, _frameRoot, out bounds)) return;
        float desiredY = above ? edgeY + bounds.extents.y : edgeY - bounds.extents.y;
        PlaceVisualByBounds(visual, new Vector2(centerX, desiredY));
    }

    void PlaceVisualByBounds(GameObject visual, Vector2 desiredCenter)
    {
        if (visual == null || _frameRoot == null) return;
        Bounds bounds;
        if (!TryVisualBoundsLocal(visual, _frameRoot, out bounds)) return;
        Vector3 local = visual.transform.localPosition;
        visual.transform.localPosition = local + new Vector3(desiredCenter.x - bounds.center.x,
                                                              desiredCenter.y - bounds.center.y, 0f);
    }

    void UpdateDynamicInnerEdges()
    {
        float panelW = Mathf.Max(1f, DsPresentation.PanelW);
        float panelH = Mathf.Max(1f, DsPresentation.PanelH);
        float gap = panelH * 0.012f;
        Bounds topBounds, bottomBounds;
        bool haveTop = TryVisualBoundsLocal(_topOrnament, _frameRoot, out topBounds);
        bool haveBottom = TryVisualBoundsLocal(_bottomOrnament, _frameRoot, out bottomBounds);
        InnerTop = haveTop ? topBounds.min.y - gap : panelH * 0.22f;
        InnerBottom = haveBottom ? bottomBounds.max.y + gap : -panelH * 0.22f;
        if (InnerBottom >= InnerTop)
        {
            InnerTop = panelH * 0.22f;
            InnerBottom = -panelH * 0.22f;
        }
        _innerLeft = -panelW * 0.46f;
        _innerRight = panelW * 0.46f;

        if (ContentMask != null)
        {
            ContentMask.anchorMin = new Vector2(0.04f, Mathf.Clamp01(InnerBottom / panelH + 0.5f));
            ContentMask.anchorMax = new Vector2(0.96f, Mathf.Clamp01(InnerTop / panelH + 0.5f));
            ContentMask.offsetMin = Vector2.zero;
            ContentMask.offsetMax = Vector2.zero;
        }
        float statusBottom = Mathf.Clamp01(InnerTop / panelH + 0.5f);
        if (_statusMask != null) _statusMask.anchorMin = new Vector2(0.04f, statusBottom);
        if (_modsMask != null) _modsMask.anchorMin = new Vector2(0.86f, statusBottom);
        UpdateRendererMasks();
    }

    void UpdateRendererMasks()
    {
        float halfW = Mathf.Max(1f, DsPresentation.PanelW) * 0.5f;
        float halfH = Mathf.Max(1f, DsPresentation.PanelH) * 0.5f;
        if (_rendererMasks[0] == null) return;
        _rendererMasks[0].SetRect(Rect.MinMaxRect(-halfW, InnerTop, halfW, halfH));
        _rendererMasks[1].SetRect(Rect.MinMaxRect(-halfW, -halfH, halfW, InnerBottom));
        _rendererMasks[2].SetRect(Rect.MinMaxRect(-halfW, InnerBottom, _innerLeft, InnerTop));
        _rendererMasks[3].SetRect(Rect.MinMaxRect(_innerRight, InnerBottom, halfW, InnerTop));
    }

    static bool TryVisualBoundsLocal(GameObject visual, Transform localRoot, out Bounds local)
    {
        local = new Bounds();
        if (visual == null || localRoot == null) return false;
        var rect = visual.transform as RectTransform;
        if (rect != null)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 point = localRoot.InverseTransformPoint(corners[i]);
                if (i == 0) local = new Bounds(point, Vector3.zero); else local.Encapsulate(point);
            }
            if (local.size.x > 0.0001f && local.size.y > 0.0001f) return true;
        }
        return TryRendererBoundsLocal(visual.GetComponent<Renderer>(), localRoot, out local);
    }

    static bool TryRendererBoundsLocal(Renderer renderer, Transform localRoot, out Bounds local)
    {
        local = new Bounds();
        if (renderer == null || localRoot == null) return false;
        Bounds world = renderer.bounds;
        if (world.size.x <= 0.0001f || world.size.y <= 0.0001f) return false;
        Vector3 min = world.min, max = world.max;
        bool have = false;
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 point = localRoot.InverseTransformPoint(new Vector3(
                        x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z));
                    if (!have) { local = new Bounds(point, Vector3.zero); have = true; }
                    else local.Encapsulate(point);
                }
        return have && local.size.x > 0.0001f && local.size.y > 0.0001f;
    }

    public void InvalidateResidentSources()
    {
        if (_disposed) return;
        DestroyComposition();
        _resident.Forget();
        _buildAttempted = false;
        _built = false;
        _slideT = 1f;
    }

    void DestroyComposition()
    {
        for (int i = 0; i < _rendererMasks.Length; i++)
        {
            if (_rendererMasks[i] != null) _rendererMasks[i].Dispose();
            _rendererMasks[i] = null;
        }
        _pageHosts.Clear();
        _tabs.Clear();
        if (ContentMask != null) UnityEngine.Object.Destroy(ContentMask.gameObject);
        if (_frameRoot != null) UnityEngine.Object.Destroy(_frameRoot.gameObject);
        ContentMask = null;
        StatusAnchor = null;
        ModsAnchor = null;
        _statusMask = null;
        _modsMask = null;
        _tabRow = null;
        _frameRoot = null;
        _topOrnament = null;
        _bottomOrnament = null;
        SelectedTopFleur = null;
        SelectedBottomFleur = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyComposition();
        _resident.Forget();
    }
}
#endif
