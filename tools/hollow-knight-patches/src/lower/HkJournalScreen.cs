// HkJournalScreen — Hollow Knight's Hunter's Journal on the lower screen,
// laid out like Silksong's DsJournalScreen: a scrolling chooser of portraits on
// the left, the selected creature large in the middle, and its name,
// description and the hunter's notes on the right.
//
// Data is the game's own: the entry templates of the inventory's JournalList
// (JournalEntryStats: sprite, playerDataName, convoName) and the rules
// JournalList.UpdateEnemyList / JournalEntryStats apply to them --
//   listed     : pd.hasJournal && (killed<X> || fillJournal)
//   completed  : kills<X> <= 0  (JournalEntryStats.OnEnable shows the frame then;
//                kills<X> counts DOWN to zero as the requirement is met)
//   new        : newData<X>  (read only here; the game clears it itself)
// Text: NAME_/DESC_/NOTE_<convo> and KILL_COUNT_1/2 from the "Journal" sheet.
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
    /// <summary>The game's journal templates and art, found once and shared with TabArt.</summary>
    static class HkJournalSource
    {
        public sealed class Record
        {
            public Sprite Sprite, Frame;
            public string NameKey, DescKey, NotesKey, CursorKey;
            public FieldInfo Killed, Kills, New;
            // Live state, refreshed by the screen's signature pass.
            public bool IsKilled, IsNew;
            public int Remaining;
        }

        public static readonly List<Record> Records = new List<Record>();
        public static Sprite HunterSymbol;
        static bool _found;
        static float _nextTry;

        /// <summary>True once the templates have been read. Retries at most every two seconds.</summary>
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
                Transform camRoot = inv.transform.root;
                var journal = FindDeep(camRoot, "Journal");
                var list = journal != null ? journal.GetComponentInChildren<JournalList>(true) : null;
                if (list == null) list = camRoot.GetComponentInChildren<JournalList>(true);
                if (list == null || list.list == null || list.list.Length == 0) return false;

                // The hunter's mark sits in the journal pane beside the list.
                Transform symbol = null;
                for (Transform t = list.transform; t != null && t != camRoot && symbol == null; t = t.parent)
                    symbol = FindDeep(t, "hunter_symbol");
                var sr = symbol != null ? symbol.GetComponentInChildren<SpriteRenderer>(true) : null;
                HunterSymbol = sr != null ? sr.sprite : null;

                Records.Clear();
                Type pdType = typeof(PlayerData);
                for (int i = 0; i < list.list.Length; i++)
                {
                    var template = list.list[i];
                    var stats = template != null ? template.GetComponent<JournalEntryStats>() : null;
                    if (stats == null || string.IsNullOrEmpty(stats.playerDataName)
                        || string.IsNullOrEmpty(stats.convoName)) continue;
                    var killed = pdType.GetField("killed" + stats.playerDataName);
                    if (killed == null || killed.FieldType != typeof(bool)) continue;
                    var kills = pdType.GetField("kills" + stats.playerDataName);
                    if (kills != null && kills.FieldType != typeof(int)) kills = null;
                    var isNew = pdType.GetField("newData" + stats.playerDataName);
                    if (isNew != null && isNew.FieldType != typeof(bool)) isNew = null;

                    Sprite frame = null;
                    if (stats.frameObject != null)
                    {
                        var fr = stats.frameObject.GetComponentInChildren<SpriteRenderer>(true);
                        if (fr != null) frame = fr.sprite;
                    }
                    Records.Add(new Record {
                        Sprite = stats.sprite, Frame = frame,
                        NameKey = "NAME_" + stats.convoName,
                        DescKey = "DESC_" + stats.convoName,
                        NotesKey = "NOTE_" + stats.convoName,
                        CursorKey = "journal:" + Records.Count,
                        Killed = killed, Kills = kills, New = isNew,
                    });
                }
                _found = Records.Count > 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HkJournal] journal templates unavailable: " + e.Message);
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

        public static string Text(string key) { return Text(key, "Journal"); }

        public static string Text(string key, string sheet)
        {
            if (string.IsNullOrEmpty(key)) return "";
            try
            {
                return HkLanguage.Has(key, sheet)
                    ? (HkLanguage.Get(key, sheet) ?? "").Replace("<br>", "\n") : "";
            }
            catch { return ""; }
        }
    }

    public class HkJournalScreen : IDsScreen
    {
        class Cell
        {
            public RectTransform Root;
            public Image Ring, Art, Dot;
        }

        const float ListX = 20f;
        const float ListW = 380f;
        const int Columns = 3;
        const float CellGap = 26f;
        const float ArtInsetFraction = 0.14f;
        const float CursorBleed = 20f;
        const float PortraitX = 430f;
        const float PortraitW = 430f;
        const float DetailX = 890f;
        const float DetailW = 330f;
        const float DetailTitleSize = 40f;
        const float DetailBodySize = 30f;
        const float DetailTextTop = 68f;
        const float RuleH = 52f;
        const float RuleGap = 18f;
        const float RuleSymbolH = 44f;
        const float MinNotesH = 90f;
        const float RefreshSeconds = 0.5f;
        const float FlingDecay = 6f;

        readonly List<int> _visible = new List<int>();
        readonly List<Cell> _cells = new List<Cell>();
        readonly DsCursor _cursor = new DsCursor();

        RectTransform _host, _list, _portraitBox, _detail, _ruleRoot;
        Image _portrait, _glow, _ruleSymbol;
        TmpText _name, _desc, _notes, _empty;

        Rect _listRect;
        float _cell, _listTop, _listH, _colH, _portraitW, _portraitH;
        float _scroll, _maxScroll, _flingV;
        int _selected = -1;       // index into HkJournalSource.Records
        int _signature = int.MinValue;
        bool _filled;
        int _settle;
        float _nextRefresh;

        public string Id { get { return "journal"; } }
        public string Title { get { return "Journal"; } }

        public bool Available
        {
            get
            {
                if (!DsFrame.InGame) return false;
                try { var pd = PlayerData.instance; return pd != null && pd.hasJournal; }
                catch { return false; }
            }
        }

        /// <summary>The hunter's mark from the game's journal pane, else an entry portrait.</summary>
        /// <summary>The inventory's Hunter's Journal book.</summary>
        public static Sprite TabArt() { return HkSprites.Find("bestiary_hunter_mark"); }

        // ── build ───────────────────────────────────────────────────────────

        public void Build(RectTransform host)
        {
            _host = host;
            var layout = DsLayout.Current;
            float bodyH = layout.Body.height;

            _listTop = 16f;
            _listH = bodyH - _listTop - 16f;
            _listRect = layout.InBody(new Rect(ListX, _listTop, ListW, _listH));
            _cell = (ListW - CellGap * (Columns - 1)) / Columns;
            _colH = _listH;

            var clip = DsWidgets.Rect(host, "list-clip");
            DsWidgets.Place(clip, ListX - CursorBleed, _listTop - CursorBleed,
                            ListW + CursorBleed * 2f, _listH + CursorBleed * 2f);
            clip.gameObject.AddComponent<RectMask2D>();

            _list = DsWidgets.Rect(clip, "list");
            DsWidgets.Place(_list, CursorBleed, CursorBleed, ListW, _listH);

            _empty = DsWidgets.Label(_list, "empty", "", DsTheme.RowSize, DsTheme.InkDim, TmpAlign.Center);
            if (_empty != null) DsWidgets.Stretch(_empty.rectTransform);

            DsWidgets.VRule(host, "split-art", (ListX + ListW + PortraitX) * 0.5f, _listTop, _colH);
            DsWidgets.VRule(host, "split-detail", (PortraitX + PortraitW + DetailX) * 0.5f, _listTop, _colH);

            _portraitBox = DsWidgets.Rect(host, "portrait");
            DsWidgets.Place(_portraitBox, PortraitX, _listTop, PortraitW, _colH);

            _glow = DsWidgets.Icon(_portraitBox, "glow", null, Color.white);
            _glow.preserveAspect = true;
            float glowSize = Mathf.Min(PortraitW, _colH) * 1.25f;
            DsWidgets.Place(_glow.rectTransform, (PortraitW - glowSize) * 0.5f,
                            (_colH - glowSize) * 0.5f, glowSize, glowSize);

            _portrait = DsWidgets.Icon(_portraitBox, "art", null, Color.white);
            _portraitW = PortraitW - 48f;
            _portraitH = _colH - 48f;

            _detail = DsWidgets.Rect(host, "detail");
            DsWidgets.Place(_detail, DetailX, _listTop, DetailW, _colH);

            _name = DsWidgets.Label(_detail, "name", "", DetailTitleSize, DsTheme.Ink, TmpAlign.Left);
            if (_name != null) DsWidgets.Place(_name.rectTransform, 0f, 8f, DetailW, 52f);

            _desc = DsWidgets.Label(_detail, "desc", "", DetailBodySize, DsTheme.InkDim, TmpAlign.TopLeft);
            if (_desc != null)
                DsWidgets.Place(_desc.rectTransform, 0f, DetailTextTop, DetailW, _colH - DetailTextTop - 8f);

            _ruleRoot = DsWidgets.Rect(_detail, "rule");
            _ruleSymbol = DsWidgets.Icon(_ruleRoot, "mask", null, DsTheme.Rule);
            DsWidgets.SetActive(_ruleRoot, false);

            _notes = DsWidgets.Label(_detail, "notes", "", DetailBodySize, DsTheme.InkDim, TmpAlign.TopLeft);

            _cursor.CornerInset = _cell * 0.1465f;
            _cursor.Build(_list);

            Refresh(true);
        }

        public void OnShow() { _flingV = 0f; Refresh(true); }
        public void OnHide() { _flingV = 0f; }

        public void Tick(float dt)
        {
            _cursor.Tick(dt);
            if (_settle > 0) { _settle--; PaintDetail(); }

            if (_flingV != 0f)
            {
                float next = Mathf.Clamp(_scroll + _flingV * dt, 0f, _maxScroll);
                _flingV *= Mathf.Exp(-FlingDecay * dt);
                if (next == _scroll || Mathf.Abs(_flingV) < 30f) _flingV = 0f;
                if (next != _scroll) { _scroll = next; Paint(); }
            }

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh(false);
        }

        // ── data ────────────────────────────────────────────────────────────

        void Refresh(bool force)
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            PlayerData pd = null;
            try { pd = PlayerData.instance; } catch { }
            bool ready = pd != null && HkJournalSource.Ensure();

            int sig = 17;
            bool hasBook = false, fill = false;
            var records = HkJournalSource.Records;
            if (ready)
            {
                try
                {
                    hasBook = pd.hasJournal;
                    fill = pd.fillJournal;
                    for (int i = 0; i < records.Count; i++)
                    {
                        var r = records[i];
                        r.IsKilled = (bool)r.Killed.GetValue(pd);
                        r.Remaining = r.Kills != null ? (int)r.Kills.GetValue(pd) : 0;
                        r.IsNew = r.New != null && (bool)r.New.GetValue(pd);
                        sig = sig * 31 + (r.IsKilled ? 1 : 0) + (r.IsNew ? 2 : 0);
                        sig = sig * 31 + r.Remaining;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[HkJournal] " + e.Message); }
            }
            sig = sig * 31 + (ready ? 1 : 0) + (hasBook ? 2 : 0) + (fill ? 4 : 0);
            sig = sig * 31 + LanguageCode();

            if (!force && sig == _signature) return;
            _signature = sig;
            _filled = fill;

            _visible.Clear();
            if (ready && hasBook)
                for (int i = 0; i < records.Count; i++)
                    if (records[i].IsKilled || fill) _visible.Add(i);

            Rebuild();
            if (_selected >= 0 && !_visible.Contains(_selected)) _selected = -1;
            if (_selected < 0 && _visible.Count > 0) _selected = _visible[0];
            Paint();
            _settle = 1;
            PaintDetail();
        }

        static int LanguageCode()
        {
            try { return (int)HkLanguage.CurrentLanguage(); } catch { return 0; }
        }

        // ── layout ──────────────────────────────────────────────────────────

        void Rebuild()
        {
            var records = HkJournalSource.Records;
            if (_empty != null)
            {
                DsWidgets.SetActive(_empty, _visible.Count == 0);
                if (_visible.Count == 0) _empty.text = HkJournalSource.Text("INV_NAME_JOURNAL", "UI");
            }

            while (_cells.Count < _visible.Count) _cells.Add(MakeCell(_cells.Count));
            for (int i = 0; i < _cells.Count; i++) DsWidgets.SetActive(_cells[i].Root, i < _visible.Count);

            float artBox = _cell * (1f - ArtInsetFraction * 2f);
            for (int i = 0; i < _visible.Count; i++)
            {
                var r = records[_visible[i]];
                var c = _cells[i];
                bool complete = r.IsKilled && r.Remaining <= 0;

                if (r.Sprite != null)
                {
                    DsWidgets.FitCentred(c.Art, r.Sprite, artBox, artBox);
                    // Listed by fillJournal but never met: the game still lists it;
                    // draw it down so the met ones read first.
                    c.Art.color = r.IsKilled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }
                else { c.Art.sprite = null; c.Art.color = Color.clear; }

                // The game shows the entry's frame only once its notes are complete.
                Sprite ring = complete ? r.Frame : null;
                c.Ring.sprite = ring;
                c.Ring.color = ring != null ? Color.white : Color.clear;

                DsWidgets.SetActive(c.Dot, r.IsNew);
            }

            int rows = (_visible.Count + Columns - 1) / Columns;
            _maxScroll = Mathf.Max(0f, rows * (_cell + CellGap) - CellGap - _listH);
            _scroll = Mathf.Clamp(_scroll, 0f, _maxScroll);
        }

        Cell MakeCell(int index)
        {
            var root = DsWidgets.Rect(_list, "cell" + index);
            DsWidgets.Place(root, 0f, 0f, _cell, _cell);

            var art = DsWidgets.Icon(root, "art", null, Color.white);

            var ring = DsWidgets.Icon(root, "ring", null, Color.white);
            ring.preserveAspect = true;
            DsWidgets.Place(ring.rectTransform, 0f, 0f, _cell, _cell);

            var dot = DsWidgets.Icon(root, "new", DsTheme.Disc, DsTheme.Accent);
            DsWidgets.Place(dot.rectTransform, 2f, 2f, 16f, 16f);
            DsWidgets.SetActive(dot, false);

            return new Cell { Root = root, Ring = ring, Art = art, Dot = dot };
        }

        void Paint()
        {
            for (int i = 0; i < _visible.Count && i < _cells.Count; i++)
            {
                int col = i % Columns, row = i / Columns;
                DsWidgets.Place(_cells[i].Root, col * (_cell + CellGap),
                                row * (_cell + CellGap) - _scroll, _cell, _cell);
            }
            _cursor.BringToFront();
            PaintCursor();
        }

        void PaintCursor()
        {
            int slot = _selected >= 0 ? _visible.IndexOf(_selected) : -1;
            if (slot < 0) { _cursor.Hide(); return; }
            int col = slot % Columns, row = slot / Columns;
            var box = new Rect(col * (_cell + CellGap), row * (_cell + CellGap) - _scroll, _cell, _cell);
            _cursor.MoveTo(box, null, HkJournalSource.Records[_selected].CursorKey);
        }

        void PaintDetail()
        {
            var records = HkJournalSource.Records;
            bool ok = _selected >= 0 && _selected < records.Count;
            var r = ok ? records[_selected] : null;

            if (_portrait != null)
            {
                if (r != null && r.Sprite != null)
                {
                    DsWidgets.FitCentred(_portrait, r.Sprite, _portraitW, _portraitH);
                    _portrait.color = r.IsKilled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }
                else { _portrait.sprite = null; _portrait.color = Color.clear; }
            }

            if (_glow != null)
            {
                var cursor = DsFrame.SelectionCursor();
                bool lit = r != null && r.Sprite != null && cursor.Glow != null;
                _glow.sprite = cursor.Glow;
                _glow.color = lit ? cursor.GlowColor : Color.clear;
            }

            if (_name != null) _name.text = r != null ? HkJournalSource.Text(r.NameKey) : "";
            if (_desc == null) return;
            if (r == null)
            {
                _desc.text = "";
                ShowRule(false, 0f);
                if (_notes != null) _notes.text = "";
                return;
            }

            _desc.text = HkJournalSource.Text(r.DescKey);

            // The game's own split: the notes once the kill requirement is met
            // (or the journal is filled), otherwise how many more to defeat.
            bool complete = _filled || (r.IsKilled && r.Remaining <= 0);
            string below = complete ? HkJournalSource.Text(r.NotesKey) : LockedNote(r);

            if (string.IsNullOrEmpty(below))
            {
                ShowRule(false, 0f);
                if (_notes != null) _notes.text = "";
                return;
            }

            float descH = string.IsNullOrEmpty(_desc.text) ? 0f : MeasuredHeight(_desc, DetailW);
            float ruleY = DetailTextTop + descH + RuleGap;
            float room = _colH - RuleH - RuleGap * 2f - MinNotesH;
            if (ruleY > room) ruleY = Mathf.Max(DetailTextTop, room);
            ShowRule(true, ruleY);

            if (_notes != null)
            {
                _notes.text = below;
                _notes.color = complete ? DsTheme.InkDim : DsTheme.InkFaint;
                DsWidgets.Place(_notes.rectTransform, 0f, ruleY + RuleH + RuleGap,
                                DetailW, Mathf.Max(60f, _colH - ruleY - RuleH - RuleGap * 2f));
            }
        }

        /// <summary>KILL_COUNT_1 N KILL_COUNT_2, as the old lower screen and the game's pane word it.</summary>
        static string LockedNote(HkJournalSource.Record r)
        {
            int left = Mathf.Max(1, r.Remaining);
            string a = HkJournalSource.Text("KILL_COUNT_1");
            string b = HkJournalSource.Text("KILL_COUNT_2");
            if (a.Length == 0 && b.Length == 0) return "Defeat " + left + " more to complete the Hunter's Notes.";
            return a + " " + left + " " + b;
        }

        static float MeasuredHeight(TmpText label, float width)
        {
            if (label == null) return 0f;
            try
            {
                label.ForceMeshUpdate();
                float h = label.GetPreferredValues(label.text, width, 0f).y;
                if (h > 0f) return h;
            }
            catch { }
            try { return label.preferredHeight; } catch { return 0f; }
        }

        void ShowRule(bool on, float y)
        {
            if (_ruleRoot == null) return;
            DsWidgets.SetActive(_ruleRoot, on);
            if (!on) return;
            DsWidgets.Place(_ruleRoot, 0f, y, DetailW, RuleH);

            var sym = HkJournalSource.HunterSymbol;
            if (_ruleSymbol == null) return;
            if (sym == null) { _ruleSymbol.sprite = null; _ruleSymbol.color = Color.clear; return; }
            if (_ruleSymbol.sprite != sym)
            {
                _ruleSymbol.sprite = sym;
                var rr = sym.rect;
                float w = Mathf.Clamp(RuleSymbolH * (rr.width / Mathf.Max(rr.height, 1f)), 12f, 200f);
                DsWidgets.Place(_ruleSymbol.rectTransform, (DetailW - w) * 0.5f,
                                (RuleH - RuleSymbolH) * 0.5f, w, RuleSymbolH);
            }
            _ruleSymbol.color = DsTheme.Rule;
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
                    int hit = HitTest(p);
                    if (hit >= 0 && hit != _selected)
                    {
                        _selected = hit;
                        Paint();
                        _settle = 1;
                        PaintDetail();
                    }
                    break;

                case DsGestureType.Drag:
                    // Panel y is up: a finger moving up scrolls further down the list.
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

        int HitTest(Vector2 layoutPoint)
        {
            if (!_listRect.Contains(layoutPoint)) return -1;
            float x = layoutPoint.x - _listRect.x;
            float y = layoutPoint.y - _listRect.y + _scroll;
            int col = (int)(x / (_cell + CellGap));
            int row = (int)(y / (_cell + CellGap));
            if (col < 0 || col >= Columns || row < 0) return -1;
            if (x - col * (_cell + CellGap) > _cell) return -1;
            if (y - row * (_cell + CellGap) > _cell) return -1;
            int slot = row * Columns + col;
            return slot < _visible.Count ? _visible[slot] : -1;
        }
    }
}
#endif
