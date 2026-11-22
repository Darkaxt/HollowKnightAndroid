// HkGuideScreen — Hollow Knight's Map Key (pin legend) on the lower screen.
//
// The rows, their pin icons and their localized labels are the game's own: the
// inventory's "Map Key/Keys/<row>" objects, each with a "Pin Icon"
// SpriteRenderer and a "Text" SetTextMeshProGameText (convName/sheetName).
// Visibility is the rule of the Map Key's Control FSM, which the old lower
// screen verified against the serialized FSM: "Draw Pins" requires pd.hasPin,
// then each "Check <row>" state requires that row's hasPin<X> bool. That
// mapping is fixed by the game data, so it is listed here directly.
//
// Layout follows the Silksong pages: a chooser on the left (pin icon + label
// rows, the game's cursor on the selected one) and the selected pin large on
// the right with its label and the Map / Quill ownership status.
//
// Read-only. Nothing here writes PlayerData.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;
using HkLanguage = TeamCherry.Localization.Language;

namespace HollowKnightPatches.Lower
{
    static class HkGuideSource
    {
        static readonly string[] Rows = {
            "Bench", "Key Stag", "Tram", "Spa", "Vendor", "Dreamer",
            "Dream Plant", "Cocoon", "Ghost", "Grub", "Black Egg" };
        static readonly string[] Conditions = {
            "hasPinBench", "hasPinStag", "hasPinTram", "hasPinSpa", "hasPinShop", "hasPinGuardian",
            "hasPinDreamPlant", "hasPinCocoon", "hasPinGhost", "hasPinGrub", "hasPinBlackEgg" };

        public sealed class Record
        {
            public Sprite Icon;
            public string Key, Sheet, CursorKey;
            public FieldInfo Owned;
            public bool IsOwned;
            public string Text = "";
        }

        public static readonly List<Record> Records = new List<Record>();
        static bool _found;
        static float _nextTry;

        public static bool Ensure()
        {
            if (_found) return true;
            if (Time.unscaledTime < _nextTry) return false;
            _nextTry = Time.unscaledTime + 2f;
            try
            {
                var gm = GameManager.instance;
                var inv = gm != null ? gm.inventoryFSM : null;
                if (inv == null) return false;
                var root = FindDeep(inv.transform.root, "Map Key");
                var keys = root != null ? FindDeep(root, "Keys") : null;
                if (keys == null) return false;

                Records.Clear();
                Type pdType = typeof(PlayerData);
                for (int i = 0; i < Rows.Length; i++)
                {
                    var row = FindDeep(keys, Rows[i]);
                    if (row == null) continue;
                    var pin = FindDeep(row, "Pin Icon");
                    var text = FindDeep(row, "Text");
                    var sr = pin != null ? pin.GetComponent<SpriteRenderer>() : null;
                    var loc = text != null ? text.GetComponent<SetTextMeshProGameText>() : null;
                    var owned = pdType.GetField(Conditions[i]);
                    if (sr == null || sr.sprite == null || loc == null || string.IsNullOrEmpty(loc.convName)
                        || string.IsNullOrEmpty(loc.sheetName) || owned == null || owned.FieldType != typeof(bool))
                        continue;
                    Records.Add(new Record {
                        Icon = sr.sprite, Key = loc.convName, Sheet = loc.sheetName,
                        CursorKey = "guide:" + Rows[i], Owned = owned,
                    });
                }
                _found = Records.Count > 0;
                if (_found && Records.Count != Rows.Length)
                    Debug.LogWarning("[HkGuide] Map Key rows found: " + Records.Count + "/" + Rows.Length);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HkGuide] Map Key unavailable: " + e.Message);
            }
            return _found;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        public static string Text(string key, string sheet)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sheet)) return "";
            try
            {
                return HkLanguage.Has(key, sheet)
                    ? (HkLanguage.Get(key, sheet) ?? "").Replace("<br>", "\n") : "";
            }
            catch { return ""; }
        }

        /// <summary>A key from whichever sheet holds it; empty if none does.</summary>
        public static string AnySheet(string key)
        {
            try
            {
                foreach (var sheet in HkLanguage.GetSheets())
                    if (HkLanguage.Has(key, sheet))
                        return (HkLanguage.Get(key, sheet) ?? "").Replace("<br>", "\n");
            }
            catch { }
            return "";
        }
    }

    public class HkGuideScreen : IDsScreen
    {
        class Row
        {
            public RectTransform Root;
            public Image Icon;
            public TmpText Label;
        }

        const float ListX = 20f;
        const float ListW = 780f;   // Silksong Tasks columns
        const float RowH = 84f;
        const float IconBox = 64f;
        const float CursorBleed = 20f;
        const float DetailX = 840f;
        const float DetailW = 380f;
        const float RefreshSeconds = 0.5f;
        const float FlingDecay = 6f;

        readonly List<int> _visible = new List<int>();
        readonly List<Row> _rows = new List<Row>();
        readonly DsCursor _cursor = new DsCursor();

        RectTransform _list, _detail, _iconBox;
        Image _bigIcon, _glow;
        TmpText _empty, _name, _mapLabel, _quillLabel, _count;

        Rect _listRect;
        float _listTop, _listH, _colH, _scroll, _maxScroll, _flingV;
        int _selected = -1;
        int _signature = int.MinValue;
        bool _hasMap, _hasQuill;
        float _nextRefresh;
        string _mapName = "", _quillName = "", _countText = "";
        int _textLanguage = int.MinValue;

        public string Id { get { return "guide"; } }
        public string Title { get { return "Guide"; } }

        public bool Available
        {
            get
            {
                if (!DsFrame.InGame) return false;
                try
                {
                    var pd = PlayerData.instance;
                    if (pd == null) return false;
                    return pd.hasMap || pd.hasPin || pd.hasPinBench || pd.hasPinStag || pd.hasPinTram
                        || pd.hasPinSpa || pd.hasPinShop || pd.hasPinGuardian || pd.hasPinDreamPlant
                        || pd.hasPinCocoon || pd.hasPinGhost || pd.hasPinGrub || pd.hasPinBlackEgg;
                }
                catch { }
                return false;
            }
        }

        /// <summary>The bench pin from the game's Map Key.</summary>
        public static Sprite TabArt()
        {
            if (!HkGuideSource.Ensure()) return null;
            var records = HkGuideSource.Records;
            return records.Count > 0 ? records[0].Icon : null;
        }

        // ── build ───────────────────────────────────────────────────────────

        public void Build(RectTransform host)
        {
            var layout = DsLayout.Current;
            float bodyH = layout.Body.height;
            _listTop = 16f;
            _listH = bodyH - _listTop - 16f;
            _colH = _listH;
            _listRect = layout.InBody(new Rect(ListX, _listTop, ListW, _listH));

            var clip = DsWidgets.Rect(host, "list-clip");
            DsWidgets.Place(clip, ListX - CursorBleed, _listTop - CursorBleed,
                            ListW + CursorBleed * 2f, _listH + CursorBleed * 2f);
            clip.gameObject.AddComponent<RectMask2D>();
            _list = DsWidgets.Rect(clip, "list");
            DsWidgets.Place(_list, CursorBleed, CursorBleed, ListW, _listH);

            _empty = DsWidgets.Label(_list, "empty", "", DsTheme.RowSize, DsTheme.InkDim, TmpAlign.Center);
            if (_empty != null) DsWidgets.Stretch(_empty.rectTransform);

            DsWidgets.VRule(host, "split-detail", (ListX + ListW + DetailX) * 0.5f, _listTop, _colH);

            _detail = DsWidgets.Rect(host, "detail");
            DsWidgets.Place(_detail, DetailX, _listTop, DetailW, _colH);

            float iconH = Mathf.Min(DetailW, _colH * 0.45f);
            _iconBox = DsWidgets.Rect(_detail, "icon");
            DsWidgets.Place(_iconBox, 0f, 0f, DetailW, iconH);
            _glow = DsWidgets.Icon(_iconBox, "glow", null, Color.white);
            _glow.preserveAspect = true;
            float glowSize = iconH * 1.1f;
            DsWidgets.Place(_glow.rectTransform, (DetailW - glowSize) * 0.5f, (iconH - glowSize) * 0.5f,
                            glowSize, glowSize);
            _bigIcon = DsWidgets.Icon(_iconBox, "pin", null, Color.white);

            float y = iconH + 12f;
            _name = DsWidgets.Label(_detail, "name", "", DsTheme.TitleSize, DsTheme.Ink, TmpAlign.Center);
            if (_name != null) DsWidgets.Place(_name.rectTransform, 0f, y, DetailW, 110f);
            y += 122f;

            DsWidgets.HRule(_detail, "rule", 40f, y, DetailW - 80f);
            y += 24f;

            _mapLabel = DsWidgets.Label(_detail, "map", "", DsTheme.RowSize, DsTheme.Ink, TmpAlign.Left);
            if (_mapLabel != null) DsWidgets.Place(_mapLabel.rectTransform, 24f, y, DetailW - 48f, 48f);
            y += 52f;
            _quillLabel = DsWidgets.Label(_detail, "quill", "", DsTheme.RowSize, DsTheme.Ink, TmpAlign.Left);
            if (_quillLabel != null) DsWidgets.Place(_quillLabel.rectTransform, 24f, y, DetailW - 48f, 48f);
            y += 52f;
            _count = DsWidgets.Label(_detail, "count", "", DsTheme.RowSize, DsTheme.InkDim, TmpAlign.Left);
            if (_count != null) DsWidgets.Place(_count.rectTransform, 24f, y, DetailW - 48f, 48f);

            _cursor.Build(_list);
            Refresh(true);
        }

        public void OnShow() { _flingV = 0f; Refresh(true); }
        public void OnHide() { _flingV = 0f; }

        public void Tick(float dt)
        {
            _cursor.Tick(dt);

            if (_flingV != 0f)
            {
                float next = Mathf.Clamp(_scroll + _flingV * dt, 0f, _maxScroll);
                _flingV *= Mathf.Exp(-FlingDecay * dt);
                if (next == _scroll || Mathf.Abs(_flingV) < 30f) _flingV = 0f;
                if (next != _scroll) { _scroll = next; Paint(); }
            }

            if (Time.unscaledTime < _nextRefresh) return;
            Refresh(false);
        }

        // ── data ────────────────────────────────────────────────────────────

        void Refresh(bool force)
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            PlayerData pd = null;
            try { pd = PlayerData.instance; } catch { }
            bool ready = pd != null && HkGuideSource.Ensure();
            var records = HkGuideSource.Records;

            bool hasPin = false;
            int sig = 17;
            if (ready)
            {
                try
                {
                    hasPin = pd.hasPin;
                    _hasMap = pd.hasMap;
                    _hasQuill = pd.hasQuill;
                    for (int i = 0; i < records.Count; i++)
                    {
                        var r = records[i];
                        r.IsOwned = (bool)r.Owned.GetValue(pd);
                        sig = sig * 31 + (r.IsOwned ? 1 : 0);
                    }
                }
                catch (Exception e) { Debug.LogWarning("[HkGuide] " + e.Message); }
            }
            int language = LanguageCode();
            sig = sig * 31 + (ready ? 1 : 0) + (hasPin ? 2 : 0) + (_hasMap ? 4 : 0) + (_hasQuill ? 8 : 0);
            sig = sig * 31 + language;
            if (!force && sig == _signature) return;
            _signature = sig;

            if (language != _textLanguage)
            {
                _textLanguage = language;
                for (int i = 0; i < records.Count; i++)
                    records[i].Text = HkGuideSource.Text(records[i].Key, records[i].Sheet);
                _mapName = HkGuideSource.AnySheet("INV_NAME_MAP");
                if (_mapName.Length == 0) _mapName = "Map";
                _quillName = HkGuideSource.AnySheet("INV_NAME_QUILL");
                if (_quillName.Length == 0) _quillName = "Quill";
                if (_empty != null) _empty.text = "No map pins bought";
            }

            _visible.Clear();
            if (ready && hasPin)
                for (int i = 0; i < records.Count; i++)
                    if (records[i].IsOwned && records[i].Text.Length > 0) _visible.Add(i);

            Rebuild();
            if (_selected >= 0 && !_visible.Contains(_selected)) _selected = -1;
            if (_selected < 0 && _visible.Count > 0) _selected = _visible[0];
            Paint();
            PaintDetail();
        }

        static int LanguageCode()
        {
            try { return (int)HkLanguage.CurrentLanguage(); } catch { return 0; }
        }

        // ── layout ──────────────────────────────────────────────────────────

        void Rebuild()
        {
            var records = HkGuideSource.Records;
            DsWidgets.SetActive(_empty, _visible.Count == 0);

            while (_rows.Count < _visible.Count) _rows.Add(MakeRow(_rows.Count));
            for (int i = 0; i < _rows.Count; i++) DsWidgets.SetActive(_rows[i].Root, i < _visible.Count);

            for (int i = 0; i < _visible.Count; i++)
            {
                var r = records[_visible[i]];
                var row = _rows[i];
                DsWidgets.FitCentred(row.Icon, r.Icon, IconBox, IconBox);
                if (row.Label != null) row.Label.text = r.Text;
            }

            _maxScroll = Mathf.Max(0f, _visible.Count * RowH - _listH);
            _scroll = Mathf.Clamp(_scroll, 0f, _maxScroll);

            _countText = _visible.Count + " / " + records.Count;
        }

        Row MakeRow(int index)
        {
            var root = DsWidgets.Rect(_list, "row" + index);
            DsWidgets.Place(root, 0f, 0f, ListW, RowH);

            var iconHolder = DsWidgets.Rect(root, "icon-box");
            DsWidgets.Place(iconHolder, 16f, (RowH - IconBox) * 0.5f, IconBox, IconBox);
            var icon = DsWidgets.Icon(iconHolder, "icon", null, Color.white);

            var label = DsWidgets.Label(root, "label", "", DsTheme.RowSize, DsTheme.InkDim, TmpAlign.Left);
            if (label != null)
            {
                label.enableWordWrapping = false;
                DsWidgets.Place(label.rectTransform, 16f + IconBox + 24f, 0f, ListW - IconBox - 56f, RowH);
            }
            return new Row { Root = root, Icon = icon, Label = label };
        }

        void Paint()
        {
            for (int i = 0; i < _visible.Count && i < _rows.Count; i++)
            {
                DsWidgets.Place(_rows[i].Root, 0f, i * RowH - _scroll, ListW, RowH);
                if (_rows[i].Label != null)
                    _rows[i].Label.color = _visible[i] == _selected ? DsTheme.Ink : DsTheme.InkDim;
            }
            _cursor.BringToFront();

            int slot = _selected >= 0 ? _visible.IndexOf(_selected) : -1;
            if (slot < 0) { _cursor.Hide(); return; }
            _cursor.MoveTo(new Rect(0f, slot * RowH - _scroll, ListW, RowH), null,
                           HkGuideSource.Records[_selected].CursorKey);
        }

        void PaintDetail()
        {
            var records = HkGuideSource.Records;
            var r = _selected >= 0 && _selected < records.Count ? records[_selected] : null;

            if (r != null && r.Icon != null)
                DsWidgets.FitCentred(_bigIcon, r.Icon, DetailW * 0.6f, _iconBox.sizeDelta.y * 0.8f);
            else { _bigIcon.sprite = null; _bigIcon.color = Color.clear; }

            var cursor = DsFrame.SelectionCursor();
            _glow.sprite = cursor.Glow;
            _glow.color = r != null && cursor.Glow != null ? cursor.GlowColor : Color.clear;

            if (_name != null) _name.text = r != null ? r.Text : "";
            if (_mapLabel != null)
            {
                _mapLabel.text = _mapName;
                _mapLabel.color = _hasMap ? DsTheme.Ink : DsTheme.InkFaint;
            }
            if (_quillLabel != null)
            {
                _quillLabel.text = _quillName;
                _quillLabel.color = _hasQuill ? DsTheme.Ink : DsTheme.InkFaint;
            }
            if (_count != null) _count.text = _countText;
        }

        // ── input ───────────────────────────────────────────────────────────

        public void OnGesture(DsGesture g)
        {
            Vector2 p = DsFrame.ToLayout(g.Position);
            switch (g.Type)
            {
                case DsGestureType.Down:
                    _flingV = 0f;
                    break;

                case DsGestureType.Tap:
                    if (!_listRect.Contains(p)) break;
                    int slot = (int)((p.y - _listRect.y + _scroll) / RowH);
                    if (slot >= 0 && slot < _visible.Count && _visible[slot] != _selected)
                    {
                        _selected = _visible[slot];
                        Paint();
                        PaintDetail();
                    }
                    break;

                case DsGestureType.Drag:
                    if (_listRect.Contains(p))
                    {
                        _scroll = Mathf.Clamp(_scroll + g.Delta.y, 0f, _maxScroll);
                        Paint();
                    }
                    break;

                case DsGestureType.Fling:
                    if (_listRect.Contains(p)) _flingV = g.Delta.y;
                    break;
            }
        }
    }
}
#endif
