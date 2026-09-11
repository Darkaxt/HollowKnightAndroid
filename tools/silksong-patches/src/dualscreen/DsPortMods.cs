// DsPortMods — HK-style gear/modal presentation for the process-owned Silksong
// tweak session. All text and ornaments are cloned from current resident native
// InventoryPane visuals; the rejected synthetic shell path is not used.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
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
    readonly List<Rect> _rowHits = new List<Rect>();
    readonly TweakPresenterPaintInvalidation _paint =
        new TweakPresenterPaintInvalidation();

    TweakSession _session;
    TweakMenuModel _menu;
    RectTransform _boundAnchor;
    GameObject _gear;
    Mesh _gearMesh;
    Material _gearMaterial;
    RectTransform _modal;
    DsRendererMaskCover _ground;
    NativeLabel _title;
    NativeLabel _master;
    NativeLabel _group;
    NativeLabel _detail;
    NativeLabel _status;
    NativeLabel _reset;
    NativeLabel _close;
    GameObject _topOrnament;
    GameObject _bottomOrnament;
    Rect _gearHit;
    Rect _masterHit;
    Rect _previousGroupHit;
    Rect _nextGroupHit;
    Rect _resetHit;
    Rect _closeHit;
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
            if (_modal == null) BuildModal();
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
        if (_disposed || !_consumerAttached || _session == null || !_session.IsReady ||
            gesture.Type != DsGestureType.Tap)
            return false;

        Vector2 point = gesture.Position;
        if (!_menu.IsOpen)
        {
            if (!_gearHit.Contains(point)) return false;
            _menu.Open();
            _frame.SetModsOpen(true);
            return true;
        }

        if (_closeHit.Contains(point))
        {
            Close();
            return true;
        }
        if (_masterHit.Contains(point))
        {
            _menu.ToggleMaster();
            return true;
        }
        if (_previousGroupHit.Contains(point))
        {
            _menu.MoveGroup(-1);
            return true;
        }
        if (_nextGroupHit.Contains(point))
        {
            _menu.MoveGroup(1);
            return true;
        }
        if (_resetHit.Contains(point))
        {
            _menu.Reset();
            return true;
        }

        IReadOnlyList<TweakDescriptor> rows = _menu.CurrentRows;
        int first = _menu.WindowStart;
        for (int i = 0; i < _rowHits.Count && first + i < rows.Count; i++)
        {
            if (!_rowHits[i].Contains(point)) continue;
            int rowIndex = first + i;
            if (_menu.SelectedRowIndex == rowIndex) _menu.CycleSelected();
            else _menu.MoveRow(rowIndex - _menu.SelectedRowIndex);
            return true;
        }

        // An open modal owns its accepted content area even when no action was hit.
        return point.x >= DsPresentation.PanelW * 0.04f &&
               point.x <= DsPresentation.PanelW * 0.96f &&
               point.y >= DsPresentation.PanelH * 0.15f &&
               point.y <= DsPresentation.PanelH * 0.76f;
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
        if (_gearMaterial != null) UnityEngine.Object.Destroy(_gearMaterial);
        if (_gearMesh != null) UnityEngine.Object.Destroy(_gearMesh);
        _gear = null;
        _gearMaterial = null;
        _gearMesh = null;
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

        _gear = new GameObject("DsPortModsGear");
        _gear.layer = DsPresentation.CONTENT_LAYER;
        _gear.transform.SetParent(anchor, false);
        var filter = _gear.AddComponent<MeshFilter>();
        var renderer = _gear.AddComponent<MeshRenderer>();
        const int segments = 24;
        var vertices = new Vector3[segments * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            float outer = (i & 1) == 0 ? 1f : 0.82f;
            vertices[i * 2] = new Vector3(Mathf.Cos(angle) * 0.38f,
                                          Mathf.Sin(angle) * 0.38f, 0f);
            vertices[i * 2 + 1] = new Vector3(Mathf.Cos(angle) * outer,
                                              Mathf.Sin(angle) * outer, 0f);
            int next = (i + 1) % segments;
            int triangle = i * 6;
            triangles[triangle] = i * 2;
            triangles[triangle + 1] = next * 2 + 1;
            triangles[triangle + 2] = i * 2 + 1;
            triangles[triangle + 3] = i * 2;
            triangles[triangle + 4] = next * 2;
            triangles[triangle + 5] = next * 2 + 1;
        }
        _gearMesh = new Mesh { name = "DsPortModsGearMesh", vertices = vertices, triangles = triangles };
        _gearMesh.RecalculateBounds();
        filter.sharedMesh = _gearMesh;
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader == null)
        {
            DetachPresentation();
            return;
        }
        _gearMaterial = new Material(shader) { name = "DsPortModsGearMaterial", color = Color.white };
        renderer.sharedMaterial = _gearMaterial;
        renderer.sortingLayerID = 0;
        renderer.sortingOrder = DsPortLayers.FRAME_RENDER_ORDER + 100;
        RefreshGearGeometry();
        _gearGeometryStamp = ComputeGeometryPaintStamp();
    }

    void RefreshGearGeometry()
    {
        RectTransform anchor = _frame.ModsAnchor;
        if (_gear == null || anchor == null) return;
        float size = Mathf.Max(1f, Mathf.Min(anchor.rect.width, anchor.rect.height)) * 0.36f;
        _gear.transform.localScale = new Vector3(size, size, 1f);
        _gear.transform.localPosition = Vector3.zero;

        float w = Mathf.Max(1f, DsPresentation.PanelW);
        float h = Mathf.Max(1f, DsPresentation.PanelH);
        _gearHit = new Rect(w * 0.84f, h * 0.76f, w * 0.14f, h * 0.22f);
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
        _title = CreateLabel("DsPortModsTitle");
        _master = CreateLabel("DsPortModsMaster");
        _group = CreateLabel("DsPortModsGroup");
        for (int i = 0; i < _menu.VisibleRows; i++)
            _rowLabels.Add(CreateLabel("DsPortModsRow-" + i));
        _detail = CreateLabel("DsPortModsDetail");
        _status = CreateLabel("DsPortModsStatus");
        _reset = CreateLabel("DsPortModsReset");
        _close = CreateLabel("DsPortModsClose");

        if (_title == null || _master == null || _group == null ||
            _detail == null || _status == null || _reset == null || _close == null)
        {
            DetachPresentation();
            return;
        }
        for (int i = 0; i < _rowLabels.Count; i++)
            if (_rowLabels[i] == null) { DetachPresentation(); return; }
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
        float line = height * 0.055f;
        if (_ground != null)
            _ground.SetRect(Rect.MinMaxRect(
                -panelW * 0.46f, -panelH * 0.305f,
                 panelW * 0.46f,  panelH * 0.305f));

        SetLabel(_title, "MODS", Color.white, 0f, top - height * 0.06f, line * 1.2f, width * 0.5f);
        bool master = _session.Controller.MasterEnabled;
        SetLabel(_master, master ? "MASTER: ON  [TOGGLE]" : "MASTER: OFF  [TOGGLE]",
            master ? new Color(0.92f, 1f, 0.86f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f),
            0f, top - height * 0.16f, line, width * 0.8f);

        string groupName = _menu.Groups.Count == 0
            ? "NO GROUPS"
            : _menu.Groups[_menu.SelectedGroupIndex].ToUpperInvariant();
        SetLabel(_group, "<  " + groupName + "  " +
            (_menu.Groups.Count == 0 ? "0/0" : (_menu.SelectedGroupIndex + 1) + "/" + _menu.Groups.Count) + "  >",
            new Color(0.82f, 0.88f, 1f, 1f), 0f, top - height * 0.27f, line, width * 0.84f);

        _rowHits.Clear();
        IReadOnlyList<TweakDescriptor> rows = _menu.CurrentRows;
        int first = _menu.WindowStart;
        float rowStep = height * 0.09f;
        float firstRowY = top - height * 0.39f;
        for (int i = 0; i < _rowLabels.Count; i++)
        {
            bool shown = first + i < rows.Count;
            _rowLabels[i].Root.SetActive(shown);
            if (!shown) continue;
            int rowIndex = first + i;
            TweakDescriptor descriptor = rows[rowIndex];
            bool selected = rowIndex == _menu.SelectedRowIndex;
            string value = Friendly(_session.Controller.Value(descriptor.Id));
            Color color = !master
                ? new Color(0.54f, 0.54f, 0.54f, 1f)
                : selected ? Color.white : new Color(0.72f, 0.76f, 0.82f, 1f);
            float y = firstRowY - i * rowStep;
            SetLabel(_rowLabels[i], (selected ? "> " : "  ") +
                descriptor.Title.ToUpperInvariant() + "    " + value,
                color, 0f, y, line * 0.9f, width * 0.9f);
            float panelY = panelH * 0.455f + y;
            _rowHits.Add(new Rect(panelW * 0.04f, panelY - rowStep * 0.48f,
                                  panelW * 0.92f, rowStep * 0.96f));
        }

        TweakDescriptor selectedRow = _menu.Selected;
        string detail = selectedRow == null ? "NO MOD IS SELECTED." : selectedRow.Description;
        string status = _menu.Message;
        if (string.IsNullOrEmpty(status) && !master)
            status = "MASTER IS OFF - ENABLE IT TO CHANGE MODS.";
        if (string.IsNullOrEmpty(status))
            status = "TAP THE SELECTED ROW AGAIN TO CHANGE ITS VALUE.";
        SetLabel(_detail, detail, new Color(0.86f, 0.88f, 0.92f, 1f),
                 0f, bottom + height * 0.18f, line * 0.9f, width * 0.88f);
        SetLabel(_status, status, _menu.MessageIsError
            ? new Color(1f, 0.42f, 0.38f, 1f)
            : new Color(0.66f, 0.78f, 0.9f, 1f),
            0f, bottom + height * 0.10f, line * 0.82f, width * 0.88f);
        SetLabel(_reset, "RESET", Color.white,
                 left + width * 0.27f, bottom + height * 0.035f, line, width * 0.3f);
        SetLabel(_close, "CLOSE / BACK", Color.white,
                 left + width * 0.73f, bottom + height * 0.035f, line, width * 0.38f);

        float contentBottom = panelH * 0.15f;
        float contentTop = panelH * 0.76f;
        _masterHit = new Rect(panelW * 0.20f, contentTop - height * 0.21f,
                              panelW * 0.60f, height * 0.10f);
        _previousGroupHit = new Rect(panelW * 0.04f, contentTop - height * 0.34f,
                                     panelW * 0.28f, height * 0.12f);
        _nextGroupHit = new Rect(panelW * 0.68f, contentTop - height * 0.34f,
                                 panelW * 0.28f, height * 0.12f);
        _resetHit = new Rect(panelW * 0.04f, contentBottom, panelW * 0.46f, height * 0.09f);
        _closeHit = new Rect(panelW * 0.50f, contentBottom, panelW * 0.46f, height * 0.09f);

        PositionOrnament(_topOrnament, top - height * 0.105f, width * 0.25f);
        PositionOrnament(_bottomOrnament, bottom + height * 0.075f, width * 0.18f);
    }

    void SetLabel(NativeLabel label, string value, Color color,
                  float x, float y, float targetHeight, float maximumWidth)
    {
        label.Root.SetActive(true);
        label.Text.text = value ?? "";
        label.Text.color = color;
        label.Text.ForceMeshUpdate(true);
        label.Root.transform.localPosition = Vector3.zero;
        label.Root.transform.localRotation = Quaternion.identity;
        label.Root.transform.localScale = Vector3.one;
        Bounds bounds;
        if (!TryBounds(label.Renderer, _modal, out bounds)) return;
        float factor = targetHeight / Mathf.Max(0.001f, bounds.size.y);
        if (bounds.size.x * factor > maximumWidth)
            factor = maximumWidth / Mathf.Max(0.001f, bounds.size.x);
        label.Root.transform.localScale = Vector3.one * Mathf.Max(0.001f, factor);
        if (!TryBounds(label.Renderer, _modal, out bounds)) return;
        label.Root.transform.localPosition += new Vector3(x - bounds.center.x, y - bounds.center.y, 0f);
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
        _title = null;
        _master = null;
        _group = null;
        _detail = null;
        _status = null;
        _reset = null;
        _close = null;
        _topOrnament = null;
        _bottomOrnament = null;
        _rowLabels.Clear();
        _rowHits.Clear();
        _paint.Invalidate();
    }

    static string Friendly(string value)
    {
        return string.IsNullOrEmpty(value)
            ? "UNKNOWN"
            : value.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
    }
}
#endif
