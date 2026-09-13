// DsPortMods — oracle-shaped 65/35 Mods presentation for the process-owned
// Silksong tweak session. Text and ornaments come only from validated resident
// InventoryPane UI; no synthetic artwork or gameplay capability lives here.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using DualSouls.Mods;
using DualSouls.Mods.Silksong;
using UnityEngine;
using PaneText = TMProOld.TextMeshPro;

public sealed class DsPortMods : IDisposable
{
    sealed class NativeLabel
    {
        public GameObject Root;
        public PaneText Text;
        public Renderer Renderer;
    }

    readonly DsPortFrame _frame;
    readonly Action<Func<DsGesture, bool>> _setConsumer;
    readonly List<NativeLabel> _rowLabels = new List<NativeLabel>();
    readonly List<NativeLabel> _rowValueLabels = new List<NativeLabel>();
    readonly List<NativeLabel> _groupLabels = new List<NativeLabel>();
    readonly List<Rect> _rowHits = new List<Rect>();
    readonly TweakPresenterPaintInvalidation _paint =
        new TweakPresenterPaintInvalidation();

    TweakSession _session;
    TweakMenuModel _menu;
    RectTransform _boundAnchor;
    GameObject _gear;
    NativeLabel _gearLabel;
    RectTransform _modal;
    DsRendererMaskCover _ground;
    NativeLabel _detailTitle;
    NativeLabel _detail;
    NativeLabel _status;
    GameObject _topOrnament;
    GameObject _bottomOrnament;
    Rect _gearHit;
    Rect _listHit;
    float _listScroll;
    int _selectedEntry = 1;
    int _builtEntryCount = -1;
    long _gearGeometryStamp = long.MinValue;
    bool _consumerAttached;
    bool _disposed;

    public DsPortMods(DsPortFrame frame, Action<Func<DsGesture, bool>> setConsumer)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _setConsumer = setConsumer ?? throw new ArgumentNullException(nameof(setConsumer));
        _frame.BeforeCompositionDestroyed += DetachPresentation;
    }

    public void Tick(bool presentationVisible)
    {
        if (_disposed) return;
        TweakSession session = ResolveSession();
        if (!presentationVisible || session == null || !session.IsReady || !_frame.HudReady)
        {
            DetachPresentation();
            return;
        }

        if (!ReferenceEquals(_session, session))
        {
            DetachPresentation();
            _session = session;
            _menu = session.Menu;
        }
        if (!ReferenceEquals(_boundAnchor, _frame.ModsAnchor))
        {
            DetachPresentation();
            _session = session;
            _menu = session.Menu;
            BuildGear();
        }
        if (_gear == null) return;

        long geometryStamp = ComputeGeometryPaintStamp();
        if (_gearGeometryStamp != geometryStamp)
        {
            RefreshGearGeometry();
            _gearGeometryStamp = geometryStamp;
        }

        AttachConsumer();
        _session.SetPresenterAttached(true);
        _frame.SetModsOpen(_menu.IsOpen);
        if (_menu.IsOpen)
        {
            int entryCount = TweakPresenterListLayout.EntryCount(_menu);
            if (_modal == null || _builtEntryCount != entryCount) BuildModal();
            if (_modal != null)
            {
                long modelStamp = TweakPresenterModelPaintStamp.Compute(
                    _session, _menu, _session.Controller);
                if (_paint.ShouldPaint(modelStamp, geometryStamp))
                {
                    Paint();
                    _paint.Acknowledge(modelStamp, geometryStamp);
                }
            }
        }
        else
        {
            DestroyModal();
        }
    }

    public bool OnGesture(DsGesture gesture)
    {
        if (_disposed || !_consumerAttached || _session == null || !_session.IsReady)
            return false;

        Vector2 point = gesture.Position;
        if (!_menu.IsOpen)
        {
            if (gesture.Type != DsGestureType.Tap || !_gearHit.Contains(point)) return false;
            _menu.Open();
            _frame.SetModsOpen(true);
            _paint.Invalidate();
            return true;
        }

        if (gesture.Type == DsGestureType.Tap && _gearHit.Contains(point))
        {
            Close();
            return true;
        }

        // Let the native frame execute an exact tab tap before the modal consumes
        // every other lower-panel gesture. TabPressed closes this pane first.
        if (_frame.TryConsumeGesture(gesture)) return true;

        if (gesture.Type == DsGestureType.Drag && _listHit.Contains(point))
        {
            float oldScroll = _listScroll;
            float height = DsPresentation.PanelH * 0.61f;
            _listScroll += gesture.Delta.y;
            _listScroll = TweakPresenterListLayout.ClampScroll(
                _listScroll,
                TweakPresenterListLayout.EntryCount(_menu),
                height * 0.115f,
                height * 0.95f);
            if (Mathf.Abs(_listScroll - oldScroll) > 0.001f) _paint.Invalidate();
            return true;
        }

        if (gesture.Type == DsGestureType.Tap)
        {
            for (int i = 0; i < _rowHits.Count; i++)
            {
                if (!_rowHits[i].Contains(point)) continue;
                ActivateEntry(i);
                return true;
            }
        }

        return true; // Open modal owns every non-tab lower-panel gesture.
    }

    void ActivateEntry(int hit)
    {
        if (_menu == null || hit < 0 || hit >= TweakPresenterListLayout.EntryCount(_menu))
            return;
        TweakPresenterListEntry entry = TweakPresenterListLayout.EntryAt(_menu, hit);
        if (entry.Kind == TweakPresenterListEntryKind.Header) return;
        if (hit != _selectedEntry)
        {
            _selectedEntry = hit;
            SynchronizeRowSelection(entry);
            _paint.Invalidate();
            return;
        }

        if (entry.Kind == TweakPresenterListEntryKind.Master)
            _menu.ToggleMaster();
        else if (entry.Kind == TweakPresenterListEntryKind.Reset)
            _menu.Reset();
        else if (entry.Kind == TweakPresenterListEntryKind.Row)
        {
            SynchronizeRowSelection(entry);
            if (_menu.Selected != null && _menu.Selected.IsAvailable)
                _menu.CycleSelected();
        }
    }

    void SynchronizeRowSelection(TweakPresenterListEntry entry)
    {
        if (entry.Kind != TweakPresenterListEntryKind.Row) return;
        _menu.MoveGroup(entry.GroupIndex - _menu.SelectedGroupIndex);
        _menu.MoveRow(entry.RowIndex - _menu.SelectedRowIndex);
    }

    public void Close()
    {
        if (_menu != null) _menu.Close();
        _frame.SetModsOpen(false);
        DestroyModal();
    }

    public void DetachPresentation()
    {
        if (_consumerAttached)
        {
            _setConsumer(null);
            _consumerAttached = false;
        }
        if (_session != null) _session.SetPresenterAttached(false);
        _frame.SetModsOpen(false);
        DestroyModal();
        if (_gear != null) UnityEngine.Object.Destroy(_gear);
        _gear = null;
        _gearLabel = null;
        _boundAnchor = null;
        _gearGeometryStamp = long.MinValue;
        _paint.Invalidate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        DetachPresentation();
        _frame.BeforeCompositionDestroyed -= DetachPresentation;
        _session = null;
        _menu = null;
        _disposed = true;
    }

    static TweakSession ResolveSession()
    {
        SilksongModsRuntime runtime = SilksongModsRuntime.Current;
        return runtime != null ? runtime.Session : null;
    }

    void BuildGear()
    {
        RectTransform anchor = _frame.ModsAnchor;
        if (anchor == null) return;
        _boundAnchor = anchor;
        _gear = _frame.CloneModsLabel(anchor, "DsPortModsNativeEntry");
        if (_gear == null) return;
        PaneText text = _gear.GetComponentInChildren<PaneText>(true);
        Renderer renderer = _gear.GetComponentInChildren<Renderer>(true);
        if (text == null || renderer == null)
        {
            UnityEngine.Object.Destroy(_gear);
            _gear = null;
            return;
        }
        renderer.sortingOrder = DsPortLayers.FRAME_RENDER_ORDER + 100;
        _gearLabel = new NativeLabel { Root = _gear, Text = text, Renderer = renderer };
        SetLabelText(_gearLabel, "MODS", Color.white);
        RefreshGearGeometry();
        _gearGeometryStamp = ComputeGeometryPaintStamp();
    }

    void RefreshGearGeometry()
    {
        RectTransform anchor = _frame.ModsAnchor;
        if (_gearLabel == null || anchor == null) return;
        float panelW = Mathf.Max(1f, DsPresentation.PanelW);
        float panelH = Mathf.Max(1f, DsPresentation.PanelH);
        PlaceLabelCenter(_gearLabel, anchor, Vector2.zero,
            panelH * 0.034f, panelW * 0.085f);
        _gearHit = new Rect(panelW * 0.84f, panelH * 0.76f,
                            panelW * 0.14f, panelH * 0.22f);
    }

    long ComputeGeometryPaintStamp()
    {
        float panelW = Mathf.Max(1f, DsPresentation.PanelW);
        float panelH = Mathf.Max(1f, DsPresentation.PanelH);
        Rect anchor = _frame.ModsAnchor != null ? _frame.ModsAnchor.rect : new Rect();
        Rect content = _frame.ContentMask != null ? _frame.ContentMask.rect : new Rect();
        Vector3 scale = _frame.ContentMask != null
            ? _frame.ContentMask.lossyScale
            : Vector3.one;
        long geometry = TweakPresenterGeometryPaintStamp.Compute(
            -panelW * 0.5f, panelW * 0.5f,
            -panelH * 0.5f, panelH * 0.5f,
            scale.x,
            anchor.x, anchor.y, anchor.width, anchor.height,
            content.x, content.y, scale.z,
            content.width, content.height);
        return TweakPresenterGeometryPaintStamp.WithLayoutRevision(
            geometry, _frame.LayoutRevision);
    }

    void BuildModal()
    {
        RectTransform parent = _frame.ContentMask;
        if (parent == null || _menu == null) return;
        DestroyModal();
        _modal = DsPortUtil.CreateRoot(parent, "DsPortModsModal",
            DsPresentation.CONTENT_LAYER, Vector2.zero, Vector2.one);
        _modal.SetAsLastSibling();

        _ground = new DsRendererMaskCover(
            _modal, "DsPortModsGround", DsPresentation.CONTENT_LAYER,
            DsPortLayers.PAGE_RENDER_ORDER - 10);
        _ground.SetRect(Rect.MinMaxRect(
            -DsPresentation.PanelW * 0.46f, -DsPresentation.PanelH * 0.305f,
             DsPresentation.PanelW * 0.46f,  DsPresentation.PanelH * 0.305f));

        _topOrnament = _frame.CloneModsOrnament(_modal, "DsPortModsTopFleur", true);
        _bottomOrnament = _frame.CloneModsOrnament(_modal, "DsPortModsBottomFleur", false);
        int entryCount = TweakPresenterListLayout.EntryCount(_menu);
        for (int i = 0; i < entryCount; i++)
        {
            TweakPresenterListEntry entry = TweakPresenterListLayout.EntryAt(_menu, i);
            NativeLabel label = CreateLabel("DsPortModsEntry-" + i);
            _rowLabels.Add(label);
            _rowHits.Add(default(Rect));
            if (entry.Kind == TweakPresenterListEntryKind.Header) _groupLabels.Add(label);
            _rowValueLabels.Add(entry.Kind == TweakPresenterListEntryKind.Header
                ? null
                : CreateLabel("DsPortModsValue-" + i));
        }
        _detailTitle = CreateLabel("DsPortModsDetailTitle");
        _detail = CreateLabel("DsPortModsDetail");
        _status = CreateLabel("DsPortModsStatus");

        if (_detailTitle == null || _detail == null || _status == null)
        {
            DetachPresentation();
            return;
        }
        for (int i = 0; i < _rowLabels.Count; i++)
            if (_rowLabels[i] == null ||
                (TweakPresenterListLayout.EntryAt(_menu, i).Kind !=
                 TweakPresenterListEntryKind.Header && _rowValueLabels[i] == null))
            {
                DetachPresentation();
                return;
            }
        _builtEntryCount = entryCount;
        _paint.Invalidate();
    }

    NativeLabel CreateLabel(string name)
    {
        GameObject root = _frame.CloneModsLabel(_modal, name);
        if (root == null) return null;
        PaneText text = root.GetComponentInChildren<PaneText>(true);
        Renderer renderer = root.GetComponentInChildren<Renderer>(true);
        if (text == null || renderer == null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }
        return new NativeLabel { Root = root, Text = text, Renderer = renderer };
    }

    void Paint()
    {
        float panelW = Mathf.Max(1f, DsPresentation.PanelW);
        float panelH = Mathf.Max(1f, DsPresentation.PanelH);
        float width = panelW * 0.92f;
        float height = panelH * 0.61f;
        float left = -width * 0.5f;
        float bottom = -height * 0.5f;
        float top = height * 0.5f;
        float split = left + width * TweakPresenterListLayout.LeftFraction;
        float listLeft = left + width * 0.025f;
        float listRight = split - width * 0.025f;
        float listTop = top - height * 0.025f;
        float listBottom = bottom + height * 0.025f;
        float visibleHeight = listTop - listBottom;
        float rowStep = height * 0.115f;
        float line = rowStep * 0.45f;
        int entryCount = TweakPresenterListLayout.EntryCount(_menu);
        _selectedEntry = Mathf.Clamp(_selectedEntry, 1, entryCount - 1);
        _listScroll = TweakPresenterListLayout.ClampScroll(
            _listScroll, entryCount, rowStep, visibleHeight);
        float panelContentCenterY = panelH * 0.455f;
        _listHit = new Rect(panelW * 0.04f,
            panelContentCenterY + listBottom,
            width * TweakPresenterListLayout.LeftFraction,
            visibleHeight);

        if (_ground != null)
            _ground.SetRect(Rect.MinMaxRect(
                -panelW * 0.46f, -panelH * 0.305f,
                 panelW * 0.46f,  panelH * 0.305f));

        bool master = _session.Controller.MasterEnabled;
        for (int i = 0; i < entryCount; i++)
        {
            TweakPresenterListEntry entry = TweakPresenterListLayout.EntryAt(_menu, i);
            float y = listTop - rowStep * 0.5f - i * rowStep + _listScroll;
            bool shown = y + rowStep * 0.5f >= listBottom &&
                         y - rowStep * 0.5f <= listTop;
            _rowLabels[i].Root.SetActive(shown);
            NativeLabel valueLabel = _rowValueLabels[i];
            if (valueLabel != null) valueLabel.Root.SetActive(shown);
            if (!shown)
            {
                _rowHits[i] = default(Rect);
                continue;
            }

            bool selected = i == _selectedEntry;
            string title;
            string value = "";
            Color color;
            if (entry.Kind == TweakPresenterListEntryKind.Header)
            {
                title = entry.GroupIndex < 0
                    ? "GENERAL"
                    : _menu.Groups[entry.GroupIndex].ToUpperInvariant();
                color = new Color(0.85f, 0.76f, 0.52f, 0.82f);
            }
            else if (entry.Kind == TweakPresenterListEntryKind.Master)
            {
                title = (selected ? "> " : "  ") + "MASTER";
                value = master ? "ON" : "OFF";
                color = selected ? new Color(1f, 1f, 0.82f, 1f) : Color.white;
            }
            else if (entry.Kind == TweakPresenterListEntryKind.Reset)
            {
                title = (selected ? "> " : "  ") + "RESET ALL MODS";
                color = selected ? new Color(1f, 0.85f, 0.52f, 1f) : Color.white;
            }
            else
            {
                TweakDescriptor descriptor = _menu.RowsForGroup(entry.GroupIndex)[entry.RowIndex];
                title = (selected ? "> " : "  ") + descriptor.Title.ToUpperInvariant();
                value = descriptor.IsAvailable
                    ? Friendly(_session.Controller.Value(descriptor.Id))
                    : "DEFERRED";
                color = !descriptor.IsAvailable
                    ? (selected ? new Color(1f, 0.72f, 0.38f, 1f)
                                : new Color(0.64f, 0.50f, 0.36f, 1f))
                    : !master
                        ? (selected ? new Color(0.78f, 0.78f, 0.78f, 1f)
                                    : new Color(0.54f, 0.54f, 0.54f, 1f))
                        : (selected ? new Color(1f, 1f, 0.82f, 1f)
                                    : new Color(0.82f, 0.84f, 0.88f, 1f));
            }

            SetLabelText(_rowLabels[i], title, color);
            PlaceLabelLeft(_rowLabels[i], listLeft, y,
                entry.Kind == TweakPresenterListEntryKind.Header ? line * 0.78f : line,
                (listRight - listLeft) * 0.72f);
            if (valueLabel != null)
            {
                SetLabelText(valueLabel, value, color);
                PlaceLabelRight(valueLabel, listRight, y, line,
                    (listRight - listLeft) * 0.30f);
            }
            _rowHits[i] = entry.Kind == TweakPresenterListEntryKind.Header
                ? default(Rect)
                : new Rect(panelW * 0.04f,
                    panelContentCenterY + y - rowStep * 0.5f,
                    width * TweakPresenterListLayout.LeftFraction,
                    rowStep);
        }

        TweakPresenterListEntry selectedEntry =
            TweakPresenterListLayout.EntryAt(_menu, _selectedEntry);
        TweakDescriptor selectedRow = selectedEntry.Kind == TweakPresenterListEntryKind.Row
            ? _menu.RowsForGroup(selectedEntry.GroupIndex)[selectedEntry.RowIndex]
            : null;
        string detailTitle;
        string detail;
        if (selectedEntry.Kind == TweakPresenterListEntryKind.Master)
        {
            detailTitle = "MASTER";
            detail = "The gate for every available mod. OFF restores the game baseline.\n\nTap the row again to change.";
        }
        else if (selectedEntry.Kind == TweakPresenterListEntryKind.Reset)
        {
            detailTitle = "RESET ALL MODS";
            detail = "Return every mod value to its default.\n\nTap the row again to reset.";
        }
        else if (selectedRow != null)
        {
            detailTitle = selectedRow.Title.ToUpperInvariant();
            detail = selectedRow.Description;
            if (!selectedRow.IsAvailable)
                detail += "\n\n" + selectedRow.TrackingId + ": " + selectedRow.UnavailableReason;
            else
                detail += "\n\nTap the row again to change.";
        }
        else
        {
            detailTitle = "MODS";
            detail = "Choose a row on the left.";
        }

        string status = _menu.Message;
        if (string.IsNullOrEmpty(status) && selectedRow != null && !selectedRow.IsAvailable)
            status = selectedRow.TrackingId + ": " + selectedRow.UnavailableReason;
        else if (string.IsNullOrEmpty(status) && !master &&
                 selectedEntry.Kind == TweakPresenterListEntryKind.Row)
            status = "MASTER IS OFF. ENABLE IT BEFORE CHANGING AVAILABLE MODS.";
        if (string.IsNullOrEmpty(status)) status = "GEAR OR TAB: CLOSE";

        float detailLeft = split + width * 0.035f;
        float detailWidth = width - (detailLeft - left) - width * 0.025f;
        SetLabelText(_detailTitle, detailTitle, Color.white);
        PlaceLabelTopLeft(_detailTitle, detailLeft, listTop, line * 1.05f, detailWidth);
        SetLabelText(_detail, Wrap(detail, 25),
            selectedRow != null && !selectedRow.IsAvailable
                ? new Color(1f, 0.72f, 0.38f, 1f)
                : new Color(0.86f, 0.88f, 0.92f, 1f));
        PlaceLabelTopLeft(_detail, detailLeft, listTop - rowStep * 1.15f,
                          line * 0.72f, detailWidth);
        SetLabelText(_status, Wrap(status, 25), _menu.MessageIsError
            ? new Color(1f, 0.42f, 0.38f, 1f)
            : new Color(0.66f, 0.78f, 0.9f, 1f));
        PlaceLabelTopLeft(_status, detailLeft, bottom + height * 0.19f,
                          line * 0.68f, detailWidth);

        PositionOrnament(_topOrnament, top - height * 0.02f, width * 0.16f);
        PositionOrnament(_bottomOrnament, bottom + height * 0.02f, width * 0.13f);
    }

    static void SetLabelText(NativeLabel label, string value, Color color)
    {
        label.Root.SetActive(true);
        label.Text.text = value ?? "";
        label.Text.color = color;
        label.Text.ForceMeshUpdate(true);
    }

    void PlaceLabelLeft(NativeLabel label, float x, float y,
                        float targetLineHeight, float maximumWidth)
    {
        Bounds bounds;
        if (!ScaleLabel(label, targetLineHeight, maximumWidth, out bounds)) return;
        label.Root.transform.localPosition += new Vector3(x - bounds.min.x, y - bounds.center.y, 0f);
    }

    void PlaceLabelRight(NativeLabel label, float x, float y,
                         float targetLineHeight, float maximumWidth)
    {
        Bounds bounds;
        if (!ScaleLabel(label, targetLineHeight, maximumWidth, out bounds)) return;
        label.Root.transform.localPosition += new Vector3(x - bounds.max.x, y - bounds.center.y, 0f);
    }

    void PlaceLabelTopLeft(NativeLabel label, float x, float y,
                           float targetLineHeight, float maximumWidth)
    {
        Bounds bounds;
        if (!ScaleLabel(label, targetLineHeight, maximumWidth, out bounds)) return;
        label.Root.transform.localPosition += new Vector3(x - bounds.min.x, y - bounds.max.y, 0f);
    }

    bool ScaleLabel(NativeLabel label, float targetLineHeight,
                    float maximumWidth, out Bounds bounds)
    {
        label.Root.transform.localPosition = Vector3.zero;
        label.Root.transform.localRotation = Quaternion.identity;
        label.Root.transform.localScale = Vector3.one;
        if (!TryBounds(label.Renderer, _modal, out bounds)) return false;
        string value = label.Text.text ?? "";
        int lines = 1;
        for (int i = 0; i < value.Length; i++) if (value[i] == '\n') lines++;
        float lineHeight = bounds.size.y / Mathf.Max(1, lines);
        float factor = targetLineHeight / Mathf.Max(0.001f, lineHeight);
        if (bounds.size.x * factor > maximumWidth)
            factor = maximumWidth / Mathf.Max(0.001f, bounds.size.x);
        label.Root.transform.localScale = Vector3.one * Mathf.Max(0.001f, factor);
        return TryBounds(label.Renderer, _modal, out bounds);
    }

    static void PlaceLabelCenter(NativeLabel label, Transform relativeTo,
                                 Vector2 center, float targetHeight, float maximumWidth)
    {
        label.Root.transform.localPosition = Vector3.zero;
        label.Root.transform.localRotation = Quaternion.identity;
        label.Root.transform.localScale = Vector3.one;
        label.Text.ForceMeshUpdate(true);
        Bounds bounds;
        if (!TryBounds(label.Renderer, relativeTo, out bounds)) return;
        float factor = targetHeight / Mathf.Max(0.001f, bounds.size.y);
        if (bounds.size.x * factor > maximumWidth)
            factor = maximumWidth / Mathf.Max(0.001f, bounds.size.x);
        label.Root.transform.localScale = Vector3.one * Mathf.Max(0.001f, factor);
        if (!TryBounds(label.Renderer, relativeTo, out bounds)) return;
        label.Root.transform.localPosition += new Vector3(
            center.x - bounds.center.x, center.y - bounds.center.y, 0f);
    }

    void PositionOrnament(GameObject ornament, float y, float targetWidth)
    {
        if (ornament == null) return;
        Renderer renderer = ornament.GetComponentInChildren<Renderer>(true);
        if (renderer == null) return;
        ornament.transform.localPosition = Vector3.zero;
        ornament.transform.localRotation = Quaternion.identity;
        ornament.transform.localScale = Vector3.one;
        Bounds bounds;
        if (!TryBounds(renderer, _modal, out bounds)) return;
        float scale = targetWidth / Mathf.Max(0.001f, bounds.size.x);
        ornament.transform.localScale = Vector3.one * scale;
        if (!TryBounds(renderer, _modal, out bounds)) return;
        ornament.transform.localPosition += new Vector3(-bounds.center.x, y - bounds.center.y, 0f);
    }

    static bool TryBounds(Renderer renderer, Transform relativeTo, out Bounds local)
    {
        local = new Bounds();
        if (renderer == null || relativeTo == null) return false;
        Bounds world = renderer.bounds;
        if (world.size.x <= 0.0001f || world.size.y <= 0.0001f) return false;
        Vector3 min = world.min;
        Vector3 max = world.max;
        bool have = false;
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 point = relativeTo.InverseTransformPoint(new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z));
                    if (!have) { local = new Bounds(point, Vector3.zero); have = true; }
                    else local.Encapsulate(point);
                }
        return have;
    }

    static string Friendly(string value)
    {
        return string.IsNullOrEmpty(value)
            ? "UNKNOWN"
            : value.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
    }

    static string Wrap(string value, int columns)
    {
        if (string.IsNullOrEmpty(value) || columns < 2) return value ?? "";
        var output = new StringBuilder(value.Length + 16);
        int lineLength = 0;
        int index = 0;
        while (index < value.Length)
        {
            if (value[index] == '\n')
            {
                output.Append('\n');
                lineLength = 0;
                index++;
                continue;
            }
            while (index < value.Length && value[index] == ' ') index++;
            int start = index;
            while (index < value.Length && value[index] != ' ' && value[index] != '\n') index++;
            int length = index - start;
            if (length == 0) continue;
            if (lineLength > 0 && lineLength + 1 + length > columns)
            {
                output.Append('\n');
                lineLength = 0;
            }
            else if (lineLength > 0)
            {
                output.Append(' ');
                lineLength++;
            }
            output.Append(value, start, length);
            lineLength += length;
        }
        return output.ToString();
    }

    void AttachConsumer()
    {
        if (_consumerAttached) return;
        _setConsumer(OnGesture);
        _consumerAttached = true;
    }

    void DestroyModal()
    {
        if (_ground != null) _ground.Dispose();
        _ground = null;
        if (_modal != null) UnityEngine.Object.Destroy(_modal.gameObject);
        _modal = null;
        _detailTitle = null;
        _detail = null;
        _status = null;
        _topOrnament = null;
        _bottomOrnament = null;
        _rowLabels.Clear();
        _rowValueLabels.Clear();
        _groupLabels.Clear();
        _rowHits.Clear();
        _listHit = default(Rect);
        _builtEntryCount = -1;
        _paint.Invalidate();
    }
}
#endif
