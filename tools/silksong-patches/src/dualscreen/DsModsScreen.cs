// DsModsScreen — a modal built-in tweak menu shared in interaction with Dual Souls.
//
// The left side is a grouped list, the right side explains the selected row,
// and a second tap on that row cycles its value. The shell owns the modal as a
// persistent overlay, so opening it never changes the active HUD tab.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using DualSouls.Mods;
using DualSouls.Mods.Silksong;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;

public sealed class DsModsScreen
{
    sealed class Row
    {
        public TweakDescriptor Descriptor;
        public RectTransform Root;
        public Image Fill;
        public TmpText Label;
        public TmpText Value;
        public Rect Hit;
    }

    const float HeaderH = 88f;
    const float ListX = 20f;
    const float ListW = 540f;
    const float DetailX = 610f;
    const float RowH = 76f;
    const float GroupH = 48f;
    const float ControlH = 54f;

    readonly TweakController _controller;
    readonly List<Row> _rows = new List<Row>();
    readonly int _w;
    readonly int _h;

    RectTransform _root;
    RectTransform _close;
    RectTransform _master;
    RectTransform _reset;
    Image _masterFill;
    TmpText _masterLabel;
    TmpText _detailTitle;
    TmpText _detailValue;
    TmpText _detailDescription;
    TmpText _status;
    Rect _closeHit;
    Rect _masterHit;
    Rect _resetHit;
    int _selected;
    string _message = "";
    bool _messageIsError;

    public DsModsScreen(RectTransform parent, int width, int height)
    {
        _w = width;
        _h = height;
        _controller = new TweakController(
            new SilksongTweakAdapter(new SilksongGameTweakApi()),
            new UnityTweakStore());

        TweakActionResult initialized = _controller.Initialize();
        if (!initialized.Success)
        {
            _message = initialized.Error;
            _messageIsError = true;
        }

        Build(parent);
    }

    public bool Visible => _root != null && _root.gameObject.activeSelf;

    public void SetVisible(bool visible)
    {
        if (_root == null || _root.gameObject.activeSelf == visible) return;
        _root.gameObject.SetActive(visible);
        if (visible) Paint();
    }

    public void Tick()
    {
        _controller.Tick();
    }

    void Build(RectTransform parent)
    {
        _root = DsWidgets.Rect(parent, "mods-modal");
        DsWidgets.Stretch(_root);

        var ground = DsWidgets.Box(_root, "ground", DsTheme.Ground);
        DsWidgets.Stretch(ground.rectTransform);

        var header = DsWidgets.Box(_root, "header", DsTheme.Panel);
        DsWidgets.Place(header.rectTransform, 0f, 0f, _w, HeaderH);
        DsWidgets.HRule(_root, "header-rule", 0f, HeaderH, _w);

        _closeHit = new Rect(20f, 16f, 160f, ControlH);
        _close = DsWidgets.Panel(_root, "close", DsTheme.Ground, DsTheme.PanelEdge);
        DsWidgets.Place(_close, _closeHit.x, _closeHit.y, _closeHit.width, _closeHit.height);
        var closeLabel = DsWidgets.Label(_close, "label", "BACK", DsTheme.SmallSize,
                                         DsTheme.Ink, TmpAlign.Center, display: true);
        if (closeLabel != null) DsWidgets.Stretch(closeLabel.rectTransform);

        var title = DsWidgets.Label(_root, "title", "MODS", DsTheme.TitleSize,
                                    DsTheme.Ink, TmpAlign.Center, display: true);
        if (title != null) DsWidgets.Place(title.rectTransform, 390f, 0f, 460f, HeaderH);

        _masterHit = new Rect(_w - 300f, 16f, 280f, ControlH);
        _master = DsWidgets.Panel(_root, "master", DsTheme.Ground, DsTheme.PanelEdge);
        DsWidgets.Place(_master, _masterHit.x, _masterHit.y, _masterHit.width, _masterHit.height);
        _masterFill = _master.Find("fill").GetComponent<Image>();
        _masterLabel = DsWidgets.Label(_master, "label", "", DsTheme.SmallSize,
                                       DsTheme.Ink, TmpAlign.Center, display: true);
        if (_masterLabel != null) DsWidgets.Stretch(_masterLabel.rectTransform);

        BuildList();
        DsWidgets.VRule(_root, "body-rule", 585f, HeaderH + 20f, _h - HeaderH - 40f);
        BuildDetail();

        _root.gameObject.SetActive(false);
        Paint();
    }

    void BuildList()
    {
        float y = HeaderH + 24f;
        string group = null;
        for (int i = 0; i < _controller.Descriptors.Count; i++)
        {
            TweakDescriptor descriptor = _controller.Descriptors[i];
            if (!string.Equals(group, descriptor.Group, StringComparison.Ordinal))
            {
                group = descriptor.Group;
                var groupLabel = DsWidgets.Label(_root, "group-" + group, group,
                                                 DsTheme.SmallSize, DsTheme.InkDim,
                                                 TmpAlign.Left, display: true);
                if (groupLabel != null)
                    DsWidgets.Place(groupLabel.rectTransform, ListX + 8f, y, ListW - 16f, GroupH);
                y += GroupH;
            }

            var row = new Row { Descriptor = descriptor, Hit = new Rect(ListX, y, ListW, RowH) };
            row.Root = DsWidgets.Rect(_root, "mod-" + descriptor.Id);
            DsWidgets.Place(row.Root, ListX, y, ListW, RowH);
            row.Fill = DsWidgets.Box(row.Root, "fill", Color.clear);
            DsWidgets.Stretch(row.Fill.rectTransform);
            row.Label = DsWidgets.Label(row.Root, "label", descriptor.Title,
                                        DsTheme.BodySize, DsTheme.Ink, TmpAlign.Left, display: true);
            if (row.Label != null) DsWidgets.Place(row.Label.rectTransform, 14f, 0f, 340f, RowH);
            row.Value = DsWidgets.Label(row.Root, "value", "", DsTheme.BodySize,
                                        DsTheme.InkDim, TmpAlign.Right, display: true);
            if (row.Value != null) DsWidgets.Place(row.Value.rectTransform, 350f, 0f, 170f, RowH);
            _rows.Add(row);
            y += RowH;
        }
    }

    void BuildDetail()
    {
        _detailTitle = DsWidgets.Label(_root, "detail-title", "", DsTheme.TitleSize,
                                       DsTheme.Ink, TmpAlign.Left, display: true);
        if (_detailTitle != null)
            DsWidgets.Place(_detailTitle.rectTransform, DetailX, HeaderH + 42f, _w - DetailX - 28f, 70f);

        _detailValue = DsWidgets.Label(_root, "detail-value", "", DsTheme.BodySize,
                                       DsTheme.Accent, TmpAlign.Left, display: true);
        if (_detailValue != null)
            DsWidgets.Place(_detailValue.rectTransform, DetailX, HeaderH + 122f, _w - DetailX - 28f, 54f);

        _detailDescription = DsWidgets.Label(_root, "detail-description", "", DsTheme.BodySize,
                                             DsTheme.InkDim, TmpAlign.TopLeft);
        if (_detailDescription != null)
            DsWidgets.Place(_detailDescription.rectTransform, DetailX, HeaderH + 202f,
                            _w - DetailX - 38f, 360f);

        var hint = DsWidgets.Label(_root, "hint", "TAP THE SELECTED ROW AGAIN TO CHANGE IT",
                                   DsTheme.SmallSize, DsTheme.InkFaint, TmpAlign.Left, display: true);
        if (hint != null)
            DsWidgets.Place(hint.rectTransform, DetailX, _h - 246f, _w - DetailX - 38f, 42f);

        _status = DsWidgets.Label(_root, "status", "", DsTheme.SmallSize,
                                  DsTheme.InkDim, TmpAlign.Left);
        if (_status != null)
            DsWidgets.Place(_status.rectTransform, DetailX, _h - 190f, _w - DetailX - 38f, 58f);

        _resetHit = new Rect(_w - 300f, _h - 104f, 272f, ControlH);
        _reset = DsWidgets.Panel(_root, "reset", DsTheme.Ground, DsTheme.PanelEdge);
        DsWidgets.Place(_reset, _resetHit.x, _resetHit.y, _resetHit.width, _resetHit.height);
        var resetLabel = DsWidgets.Label(_reset, "label", "RESET VALUES", DsTheme.SmallSize,
                                         DsTheme.Ink, TmpAlign.Center, display: true);
        if (resetLabel != null) DsWidgets.Stretch(resetLabel.rectTransform);
    }

    public void OnGesture(DsGesture gesture)
    {
        if (!Visible || gesture.Type != DsGestureType.Tap) return;
        Vector2 point = DsPresentation.ToLayout(gesture.Position);

        if (_closeHit.Contains(point))
        {
            SetVisible(false);
            return;
        }
        if (_masterHit.Contains(point))
        {
            Run(_controller.SetMaster(!_controller.MasterEnabled),
                _controller.MasterEnabled ? "Mods enabled." : "Mods disabled; game baseline restored.");
            return;
        }
        if (_resetHit.Contains(point))
        {
            Run(_controller.Reset(), "All values reset.");
            return;
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            if (!_rows[i].Hit.Contains(point)) continue;
            if (_selected == i)
                Run(_controller.Cycle(_rows[i].Descriptor.Id), "Value saved.");
            else
            {
                _selected = i;
                _message = "";
                _messageIsError = false;
                Paint();
            }
            return;
        }
    }

    void Run(TweakActionResult result, string success)
    {
        _message = result.Success ? success : result.Error;
        _messageIsError = !result.Success;
        Paint();
    }

    void Paint()
    {
        bool enabled = _controller.MasterEnabled;
        if (_masterFill != null) _masterFill.color = enabled ? DsTheme.Accent : DsTheme.Ground;
        if (_masterLabel != null)
        {
            _masterLabel.text = enabled ? "MASTER  ON" : "MASTER  OFF";
            _masterLabel.color = enabled ? DsTheme.Ground : DsTheme.Ink;
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            Row row = _rows[i];
            bool selected = i == _selected;
            row.Fill.color = selected ? DsTheme.Panel : Color.clear;
            if (row.Label != null) row.Label.color = enabled
                ? (selected ? DsTheme.Ink : DsTheme.InkDim)
                : DsTheme.InkFaint;
            if (row.Value != null)
            {
                row.Value.text = Friendly(_controller.Value(row.Descriptor.Id));
                row.Value.color = enabled && selected ? DsTheme.Accent : DsTheme.InkFaint;
            }
        }

        if (_rows.Count > 0)
        {
            Row selected = _rows[Mathf.Clamp(_selected, 0, _rows.Count - 1)];
            if (_detailTitle != null) _detailTitle.text = selected.Descriptor.Title;
            if (_detailValue != null)
                _detailValue.text = "VALUE  " + Friendly(_controller.Value(selected.Descriptor.Id));
            if (_detailDescription != null) _detailDescription.text = selected.Descriptor.Description;
        }
        if (_status != null)
        {
            _status.text = _message;
            _status.color = _messageIsError ? DsTheme.Accent : DsTheme.InkDim;
        }
    }

    static string Friendly(string value)
    {
        if (value == "vanilla") return "VANILLA";
        if (value == "prevent_death") return "PREVENT DEATH";
        if (value == "invincible") return "INVINCIBLE";
        if (value == "on") return "ON";
        if (value == "off") return "OFF";
        return (value ?? "").Replace('_', ' ').ToUpperInvariant();
    }
}
#endif
