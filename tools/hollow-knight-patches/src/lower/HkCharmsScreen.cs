// HkCharmsScreen — Hollow Knight's Charms pane on the lower screen, built from
// PlayerData rather than cloned from the game's "Charms" pane.
//
//   left   every charm, in the game's collection order (collected = its icon,
//          not yet found = the game's blank backboard, dim)
//   right  the equipped row, the notches (used / total, overcharm), then the
//          selected charm's name, description and notch cost, and EQUIP/UNEQUIP
//
// Equipping writes the save through the same clean path the gameplay mods use
// (HollowKnightGameplayFeatures.MaintainInnateCharm): the equippedCharm_N bool,
// PlayerData.EquipCharm/UnequipCharm, CalculateNotchesUsed, then the game's own
// refreshes -- GameManager.RefreshOvercharm, HeroController.CharmUpdate and the
// "CHARM INDICATOR CHECK" / "UPDATE BLUE HEALTH" broadcasts. It is only offered
// where the game would allow it: at a bench (PlayerData.atBench, which the
// equip-anywhere mod sets) and never while a Godhome binding holds the charms
// (BossSequenceController.BoundCharms).
//
// Overcharming follows the game: it needs at least one free notch, and until
// PlayerData.canOvercharm is set the charm refuses the first four attempts and
// unlocks it on the fifth (the attempt count the old lower screen reproduced
// from the game's FSM).

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;

namespace HollowKnightPatches.Lower
{
    public class HkCharmsScreen : IDsScreen, IDsActionBar
    {
        const int CharmCount = 40;
        // Silksong's Crest columns (DsLoadoutScreen): what is worn on the left,
        // the collection in a three-wide list, and the description on the right.
        const float LeftX = 20f, LeftW = 470f;
        const float ListX = 520f, ListW = 350f;
        const float DetailX = 900f, DetailW = 320f;
        const int ListColumns = 3;
        const float EquipSize = 96f, EquipGap = 14f;
        const int EquipPerRow = 4;
        const float PollSeconds = 0.5f;
        const int OvercharmAttempts = 5;
        const float ApplyCooldown = 0.35f;
        const float StatusSeconds = 2.5f;

        static readonly Color NotchEmpty = new Color(0.30f, 0.29f, 0.34f, 1f);
        static readonly Color NotchOver = new Color(0.86f, 0.36f, 0.56f, 1f);
        static readonly Color EquippedInGrid = new Color(1f, 1f, 1f, 0.35f);

        // ── PlayerData fields per charm, resolved once ──────────────────────

        static FieldInfo[] _fGot, _fEq, _fCost;
        static string[] _keys;

        static void EnsureFields()
        {
            if (_fGot != null) return;
            var t = typeof(PlayerData);
            var got = new FieldInfo[CharmCount + 1];
            var eq = new FieldInfo[CharmCount + 1];
            var cost = new FieldInfo[CharmCount + 1];
            var keys = new string[CharmCount + 1];
            for (int n = 1; n <= CharmCount; n++)
            {
                got[n] = t.GetField("gotCharm_" + n);
                eq[n] = t.GetField("equippedCharm_" + n);
                cost[n] = t.GetField("charmCost_" + n);
                keys[n] = "c" + n;
            }
            _fEq = eq; _fCost = cost; _keys = keys;
            _fGot = got;
        }

        static bool GetBool(FieldInfo f, PlayerData pd)
        {
            if (f == null) return false;
            try { return (bool)f.GetValue(pd); } catch { return false; }
        }

        static int GetInt(FieldInfo f, PlayerData pd)
        {
            if (f == null) return 0;
            try { return (int)f.GetValue(pd); } catch { return 0; }
        }

        // ── Art ─────────────────────────────────────────────────────────────

        static Transform _artRoot;
        static float _artNextTry;
        static int _artVersion;
        static Sprite _blank, _brokenHeart, _brokenGreed, _brokenStrength, _white, _black;
        static int[] _order;   // grid position -> charm number

        static void EnsureArt()
        {
            var gm = GameManager.UnsafeInstance;
            PlayMakerFSM fsm = null;
            try { fsm = gm != null ? gm.inventoryFSM : null; } catch { }
            Transform root = fsm != null ? fsm.transform : null;
            if (root == null || (_artRoot != null && ReferenceEquals(root, _artRoot))) return;
            if (Time.unscaledTime < _artNextTry) return;
            _artNextTry = Time.unscaledTime + 2f;
            try { HarvestArt(root); }
            catch (Exception e) { Debug.LogWarning("[HkCharms] art harvest: " + e.Message); }
        }

        static void HarvestArt(Transform root)
        {
            _blank = _brokenHeart = _brokenGreed = _brokenStrength = _white = _black = null;
            _order = null;

            CharmDisplay display = null;
            var boards = root.GetComponentsInChildren<InvCharmBackboard>(true);
            var nums = new List<int>(CharmCount);
            var pos = new List<Vector3>(CharmCount);
            for (int i = 0; i < boards.Length; i++)
            {
                var b = boards[i];
                if (b == null) continue;
                if (_blank == null && b.blankSprite != null) _blank = b.blankSprite;
                if (display == null && b.charmObject != null) display = b.charmObject.GetComponent<CharmDisplay>();
                if (b.charmNum >= 1 && b.charmNum <= CharmCount && !nums.Contains(b.charmNum))
                {
                    nums.Add(b.charmNum);
                    pos.Add(b.transform.position);
                }
            }
            if (display == null) display = root.GetComponentInChildren<CharmDisplay>(true);
            if (display == null)
            {
                var builder = root.GetComponentInChildren<BuildEquippedCharms>(true);
                if (builder != null && builder.gameObjectList != null)
                    for (int i = 0; i < builder.gameObjectList.Count && display == null; i++)
                        if (builder.gameObjectList[i] != null)
                            display = builder.gameObjectList[i].GetComponent<CharmDisplay>();
            }
            if (display != null)
            {
                _brokenHeart = display.brokenGlassHP;
                _brokenGreed = display.brokenGlassGeo;
                _brokenStrength = display.brokenGlassAttack;
                _white = display.whiteCharm;
                _black = display.blackCharm;
            }

            // The game's own collection layout: rows top to bottom, then left to
            // right. Used only when every charm has exactly one backboard.
            if (nums.Count == CharmCount)
            {
                var idx = new int[CharmCount];
                for (int i = 0; i < CharmCount; i++) idx[i] = i;
                Array.Sort(idx, (a, b) =>
                {
                    float dy = pos[b].y - pos[a].y;
                    if (Mathf.Abs(dy) > 0.3f) return dy > 0f ? 1 : -1;
                    return pos[a].x.CompareTo(pos[b].x);
                });
                var order = new int[CharmCount];
                for (int i = 0; i < CharmCount; i++) order[i] = nums[idx[i]];
                _order = order;
            }

            _artRoot = root;
            _artVersion++;
        }

        static Sprite IconFor(int n, PlayerData pd)
        {
            if (n == 23 && pd.brokenCharm_23 && _brokenHeart != null) return _brokenHeart;
            if (n == 24 && pd.brokenCharm_24 && _brokenGreed != null) return _brokenGreed;
            if (n == 25 && pd.brokenCharm_25 && _brokenStrength != null) return _brokenStrength;
            if (n == 36)
            {
                if (pd.royalCharmState > 3 && _black != null) return _black;
                if (pd.royalCharmState == 3 && _white != null) return _white;
            }
            var list = CharmIconList.Instance;
            if (list == null || list.spriteList == null || n >= list.spriteList.Length) return null;
            try { return list.GetSprite(n); } catch { return null; }
        }

        /// <summary>A charm icon from the game's CharmIconList, or null until it is resident.</summary>
        public static Sprite TabArt()
        {
            var list = CharmIconList.Instance;
            if (list == null || list.spriteList == null) return null;
            if (list.spriteList.Length > 0 && list.spriteList[0] != null) return list.spriteList[0];
            return list.spriteList.Length > 1 ? list.spriteList[1] : null;
        }

        // ── Text ────────────────────────────────────────────────────────────

        // Variant keys for the charms whose name changes with their state; each
        // falls back to the plain CHARM_NAME_N / CHARM_DESC_N when absent.
        static string VariantSuffix(int n, PlayerData pd)
        {
            switch (n)
            {
                case 23: return pd.brokenCharm_23 ? "_BRK" : pd.fragileHealth_unbreakable ? "_G" : null;
                case 24: return pd.brokenCharm_24 ? "_BRK" : pd.fragileGreed_unbreakable ? "_G" : null;
                case 25: return pd.brokenCharm_25 ? "_BRK" : pd.fragileStrength_unbreakable ? "_G" : null;
                case 36:
                    switch (pd.royalCharmState)
                    {
                        case 1: return "_A";
                        case 2: return "_B";
                        case 3: return "_C";
                        case 4: return "_D";
                    }
                    return null;
                case 40: return pd.grimmChildLevel == 5 ? "_N" : null;
            }
            return null;
        }

        static string CharmText(string prefix, int n, string suffix)
        {
            string baseKey = prefix + n;
            string v = suffix != null ? HkInventoryScreen.Loc(baseKey + suffix) : "";
            return v.Length > 0 ? v : HkInventoryScreen.Loc(baseKey);
        }

        static string _equipLabel = "EQUIP", _unequipLabel = "UNEQUIP";

        // ── Widgets ─────────────────────────────────────────────────────────

        readonly DsIconGrid _grid = new DsIconGrid();
        readonly List<DsSection> _sections = new List<DsSection>(1);
        readonly DsSection _all = new DsSection(null, DsTheme.InkDim);

        const int EquipPool = 16;
        const int NotchPool = 16;
        const int CostPool = 8;
        readonly Image[] _eqIcons = new Image[EquipPool];
        readonly Rect[] _eqHit = new Rect[EquipPool];      // body-local, top-left
        readonly int[] _eqNum = new int[EquipPool];
        int _eqShown;
        readonly Image[] _notches = new Image[NotchPool];
        readonly Image[] _pips = new Image[CostPool];
        TmpText _equippedLabel, _notchLabel, _costLabel, _status;

        float _rx, _rw, _eqTop, _eqAreaH, _notchY, _costY;
        float _lx = LeftX, _lw = LeftW;
        Rect _actionPane;
        bool _built;

        // ── State (refreshed on the poll, read per frame) ───────────────────

        readonly bool[] _got = new bool[CharmCount + 1];
        readonly int[] _cost = new int[CharmCount + 1];
        int _signature = int.MinValue;
        float _nextPoll;
        string _lastSel;
        int _selNum;
        int _attempts;
        float _nextApply;
        float _statusUntil;
        readonly Action _toggle;
        // #56 loadout presets, in the header: which preset, SAVE and LOAD.
        int _preset = 1;
        bool _presetSaved;
        string _presetLabel = "PRESET 1";
        readonly Action _cyclePreset, _savePreset, _loadPreset;

        public string Id { get { return "charms"; } }
        public string Title { get { return "Charms"; } }
        public bool Available { get { return DsFrame.InGame; } }
        public Rect ActionPane { get { return _actionPane; } }

        public HkCharmsScreen()
        {
            _sections.Add(_all);
            _toggle = Toggle;
            _cyclePreset = () => { _preset = _preset % HkCharmPresets.Count + 1; _presetSaved = HkCharmPresets.Exists(_preset); _presetLabel = "PRESET " + _preset; };
            _savePreset = () => { SetStatus(HkCharmPresets.Save(_preset)); _presetSaved = HkCharmPresets.Exists(_preset); Refresh(true); };
            _loadPreset = () => { SetStatus(HkCharmPresets.Load(_preset)); Refresh(true); };
        }

        public void Build(RectTransform host)
        {
            EnsureFields();
            var layout = DsLayout.Current;
            float colH = layout.Body.height - DsTheme.Pad * 2f;
            _rx = DetailX; _rw = DetailW;

            DsWidgets.VRule(host, "split-list", (LeftX + LeftW + ListX) * 0.5f, DsTheme.Pad, colH);
            DsWidgets.VRule(host, "split-detail", (ListX + ListW + DetailX) * 0.5f, DsTheme.Pad, colH);

            // Left column: the equipped charms and the notches, where Silksong
            // shows the crest and what is slotted into it.
            float y = DsTheme.Pad + 14f;
            _equippedLabel = DsWidgets.Label(host, "equipped-label", "Equipped", DsTheme.TitleSize,
                                             DsTheme.Ink, TmpAlign.Center);
            if (_equippedLabel != null) DsWidgets.Place(_equippedLabel.rectTransform, _lx, y, _lw, 52f);
            y += 52f + 28f;
            _eqTop = y;
            _eqAreaH = EquipSize * 3f + EquipGap * 2f;
            for (int i = 0; i < EquipPool; i++)
            {
                _eqIcons[i] = DsWidgets.Icon(host, "equipped" + i, null, Color.white);
                _eqIcons[i].gameObject.SetActive(false);
            }
            y += _eqAreaH + 10f;

            _notchLabel = DsWidgets.Label(host, "notch-label", "", DsTheme.SmallSize, DsTheme.InkDim, TmpAlign.Center);
            if (_notchLabel != null) DsWidgets.Place(_notchLabel.rectTransform, _lx, y, _lw, 30f);
            y += 38f;
            _notchY = y;
            for (int i = 0; i < NotchPool; i++)
            {
                _notches[i] = DsWidgets.Circle(host, "notch" + i, NotchEmpty);
                _notches[i].gameObject.SetActive(false);
            }

            // Right column: the description from the top, as on Silksong.
            y = DsTheme.Pad;

            // Right column, bottom: cost pips, then the action band.
            float band = DsActionBar.PaneBand(1);
            float bottom = DsTheme.Pad + colH - band;
            _costY = bottom - 40f;
            _costLabel = DsWidgets.Label(host, "cost-label", "", DsTheme.SmallSize, DsTheme.InkDim, TmpAlign.Left);
            if (_costLabel != null) DsWidgets.Place(_costLabel.rectTransform, _rx, _costY, 120f, 30f);
            for (int i = 0; i < CostPool; i++)
            {
                _pips[i] = DsWidgets.Circle(host, "pip" + i, DsTheme.Ink);
                DsWidgets.Place(_pips[i].rectTransform, _rx + 110f + i * 30f, _costY + 4f, 22f, 22f);
                _pips[i].gameObject.SetActive(false);
            }
            _status = DsWidgets.Label(host, "status", "", DsTheme.SmallSize, DsTheme.Accent, TmpAlign.Right);
            if (_status != null) DsWidgets.Place(_status.rectTransform, _rx, _costY - 36f, _rw, 30f);

            _actionPane = layout.InBody(new Rect(_rx, bottom, _rw, band));

            _grid.EmptyMessage = "No charms yet";
            _grid.Build(host, ListColumns, ListX, ListW,
                        new Rect(_rx, y, _rw, Mathf.Max(80f, _costY - 40f - y)), false,
                        ListX - (LeftX + LeftW + ListX) * 0.5f);
            _built = true;
            Refresh(true);
        }

        public void OnShow()
        {
            _attempts = 0;
            _presetSaved = HkCharmPresets.Exists(_preset);
            _signature = int.MinValue;
            if (!_built) return;
            Refresh(true);
        }

        public void OnHide()
        {
            _attempts = 0;
        }

        public void Tick(float dt)
        {
            if (!_built) return;
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollSeconds;
                Refresh(false);
            }
            _grid.Tick();

            string sel = _grid.SelectedKey;
            if (!ReferenceEquals(sel, _lastSel))
            {
                _lastSel = sel;
                _selNum = KeyToNum(sel);
                _attempts = 0;
                PaintCost();
            }
            if (_status != null && _statusUntil > 0f && Time.unscaledTime >= _statusUntil)
            {
                _statusUntil = 0f;
                _status.text = "";
            }
        }

        public void OnGesture(DsGesture g)
        {
            if (!_built) return;
            if (g.Type == DsGestureType.Tap)
            {
                Vector2 p = DsFrame.ToLayout(g.Position);
                var local = new Vector2(p.x, p.y - DsLayout.Current.Body.y);
                for (int i = 0; i < _eqShown; i++)
                {
                    if (!_eqHit[i].Contains(local)) continue;
                    int n = _eqNum[i];
                    if (n >= 1 && n <= CharmCount) _grid.SelectByKey(_keys[n]);
                    return;
                }
            }
            _grid.OnGesture(g);
        }

        static int KeyToNum(string key)
        {
            if (key == null || _keys == null) return 0;
            for (int n = 1; n <= CharmCount; n++) if (ReferenceEquals(_keys[n], key) || _keys[n] == key) return n;
            return 0;
        }

        // ── Data ────────────────────────────────────────────────────────────

        void Refresh(bool force)
        {
            if (!DsFrame.InGame)
            {
                if (_signature != 0 || force)
                {
                    _signature = 0;
                    _grid.EmptyMessage = "No game loaded";
                    _all.Items.Clear();
                    _grid.SetSections(_sections);
                    _eqShown = 0;
                    for (int i = 0; i < EquipPool; i++) DsWidgets.SetActive(_eqIcons[i], false);
                    for (int i = 0; i < NotchPool; i++) DsWidgets.SetActive(_notches[i], false);
                    if (_notchLabel != null) _notchLabel.text = "";
                }
                return;
            }

            EnsureArt();
            var pd = PlayerData.instance;
            if (pd == null) return;

            int sig;
            try { sig = Read(pd); }
            catch (Exception e) { Debug.LogWarning("[HkCharms] read: " + e.Message); return; }
            if (!force && sig == _signature) return;
            _signature = sig;

            HkInventoryScreen.CheckLanguage();
            string eqText = HkInventoryScreen.Loc("CTRL_EQUIP");
            string unText = HkInventoryScreen.Loc("CTRL_UNEQUIP");
            _equipLabel = eqText.Length > 0 ? eqText.ToUpperInvariant() : "EQUIP";
            _unequipLabel = unText.Length > 0 ? unText.ToUpperInvariant() : "UNEQUIP";

            try { Rebuild(pd); }
            catch (Exception e) { Debug.LogWarning("[HkCharms] rebuild: " + e.Message); }
        }

        /// <summary>Refresh the per-charm caches and return the state signature.</summary>
        int Read(PlayerData pd)
        {
            int h = 17;
            for (int n = 1; n <= CharmCount; n++)
            {
                _got[n] = GetBool(_fGot[n], pd);
                _cost[n] = GetInt(_fCost[n], pd);
                h = h * 31 + (_got[n] ? 1 : 0);
                h = h * 31 + _cost[n];
            }
            var list = pd.equippedCharms;
            if (list != null)
                for (int i = 0; i < list.Count; i++) h = h * 37 + list[i];
            h = h * 31 + pd.charmSlots;
            h = h * 31 + pd.charmSlotsFilled;
            h = h * 31 + (pd.overcharmed ? 1 : 0);
            h = h * 31 + (pd.canOvercharm ? 1 : 0);
            h = h * 31 + pd.royalCharmState;
            h = h * 31 + pd.grimmChildLevel;
            h = h * 31 + (pd.brokenCharm_23 ? 1 : 0) + (pd.brokenCharm_24 ? 2 : 0) + (pd.brokenCharm_25 ? 4 : 0);
            h = h * 31 + (pd.fragileHealth_unbreakable ? 1 : 0) + (pd.fragileGreed_unbreakable ? 2 : 0) +
                (pd.fragileStrength_unbreakable ? 4 : 0);
            h = h * 31 + _artVersion;
            h = h * 31 + (CharmIconList.Instance != null ? 1 : 0);
            return h;
        }

        void Rebuild(PlayerData pd)
        {
            var equipped = pd.equippedCharms;

            // Grid: all forty, in the game's collection order.
            _all.Items.Clear();
            for (int i = 0; i < CharmCount; i++)
            {
                int n = _order != null ? _order[i] : i + 1;
                bool got = _got[n];
                bool on = equipped != null && equipped.Contains(n);
                if (got)
                {
                    string suffix = VariantSuffix(n, pd);
                    _all.Items.Add(new DsItem
                    {
                        Key = _keys[n],
                        Name = CharmText("CHARM_NAME_", n, suffix),
                        Description = CharmText("CHARM_DESC_", n, suffix),
                        Icon = IconFor(n, pd),
                        Tint = on ? EquippedInGrid : Color.white,
                    });
                }
                else
                {
                    _all.Items.Add(new DsItem
                    {
                        Key = _keys[n],
                        Name = "",
                        Description = "",
                        Icon = _blank,
                        Tint = Color.white,
                        Dim = true,
                    });
                }
            }
            _grid.SetSections(_sections);

            // Equipped row, in equip order. Wraps onto a second row, and shrinks
            // when even two rows would not hold them.
            int count = equipped != null ? Mathf.Min(equipped.Count, EquipPool) : 0;
            // Rows of four at Silksong's slot size, centred in the column; more
            // than three rows (an overcharmed build) shrink to fit.
            float gap = EquipGap;
            int perRow = Mathf.Max(EquipPerRow, Mathf.CeilToInt(count / 3f));
            float size = Mathf.Min(EquipSize, (_lw - gap * (perRow - 1)) / perRow);
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)perRow));
            _eqShown = 0;
            for (int i = 0; i < EquipPool; i++)
            {
                var img = _eqIcons[i];
                if (i >= count) { DsWidgets.SetActive(img, false); continue; }
                int n = equipped[i];
                Sprite s = n >= 1 && n <= CharmCount ? IconFor(n, pd) : null;
                int inRow = Mathf.Min(perRow, count - (i / perRow) * perRow);
                float x = _lx + (_lw - inRow * size - (inRow - 1) * gap) * 0.5f + (i % perRow) * (size + gap);
                float y = _eqTop + (_eqAreaH - rows * size - (rows - 1) * gap) * 0.5f + (i / perRow) * (size + gap);
                DsWidgets.Place(img.rectTransform, x, y, size, size);
                img.sprite = s;
                img.color = s != null ? Color.white : DsTheme.Locked;
                if (s == null) img.sprite = DsTheme.White;
                DsWidgets.SetActive(img, true);
                _eqHit[_eqShown] = new Rect(x, y, size, size);
                _eqNum[_eqShown] = n;
                _eqShown++;
            }
            if (_equippedLabel != null) _equippedLabel.text = count > 0 ? "Equipped" : "Equipped: none";

            // Notches: the slots, filled up to what is used, and the overflow
            // of an overcharm drawn past them in the overcharm colour.
            int slots = Mathf.Max(0, pd.charmSlots);
            int filled = Mathf.Max(0, pd.charmSlotsFilled);
            int dots = Mathf.Min(NotchPool, Mathf.Max(slots, filled));
            float d = Mathf.Min(30f, (_lw - 8f * Mathf.Max(0, dots - 1)) / Mathf.Max(1, dots));
            float dotsX = _lx + (_lw - dots * d - Mathf.Max(0, dots - 1) * 8f) * 0.5f;
            for (int i = 0; i < NotchPool; i++)
            {
                var c = _notches[i];
                if (i >= dots) { DsWidgets.SetActive(c, false); continue; }
                DsWidgets.Place(c.rectTransform, dotsX + i * (d + 8f), _notchY, d, d);
                c.color = i >= slots ? NotchOver : i < filled ? (pd.overcharmed ? NotchOver : DsTheme.Ink) : NotchEmpty;
                DsWidgets.SetActive(c, true);
            }
            if (_notchLabel != null)
            {
                _notchLabel.text = pd.overcharmed
                    ? "Notches " + filled + " / " + slots + "   OVERCHARMED"
                    : "Notches " + filled + " / " + slots;
                _notchLabel.color = pd.overcharmed ? NotchOver : DsTheme.InkDim;
            }

            PaintCost();
        }

        void PaintCost()
        {
            if (!_built) return;
            int n = _selNum;
            bool show = n >= 1 && n <= CharmCount && _got[n];
            int cost = show ? Mathf.Clamp(_cost[n], 0, CostPool) : 0;
            if (_costLabel != null) _costLabel.text = show ? "Cost" : "";
            for (int i = 0; i < CostPool; i++) DsWidgets.SetActive(_pips[i], show && i < cost);
        }

        void SetStatus(string text)
        {
            if (_status == null) return;
            _status.text = text ?? "";
            _statusUntil = Time.unscaledTime + StatusSeconds;
        }

        // ── EQUIP / UNEQUIP ─────────────────────────────────────────────────

        /// <summary>
        /// Why the selected charm cannot be changed right now, or null when it
        /// can. Reads only plain fields and cached values: this runs every frame.
        /// </summary>
        string BlockReason(PlayerData pd, int n, bool equipped)
        {
            var gm = GameManager.UnsafeInstance;
            if (gm == null || HeroController.UnsafeInstance == null) return "Unavailable";
            if (gm.gameState != GlobalEnums.GameState.PLAYING || gm.IsInSceneTransition) return "Unavailable";
            if (gm.inventoryFSM != null && gm.inventoryFSM.ActiveStateName != "Closed") return "Close the inventory";
            if (BossSequenceController.BoundCharms) return "Charms are bound";
            // The equip-anywhere mod only forces atBench while the game's own inventory is
            // open, which this pane refuses above; honour the mod here directly.
            if (!pd.atBench && !DualSouls.Mods.HollowKnight.HollowKnightGameplayFeatures.EquipAnywhereActive) return "Rest at a bench";
            if ((n == 23 && pd.brokenCharm_23) || (n == 24 && pd.brokenCharm_24) || (n == 25 && pd.brokenCharm_25))
                return "Broken";
            if (n == 36 && (pd.royalCharmState == 1 || pd.royalCharmState == 2)) return "Incomplete";
            if (n == 36 && equipped && pd.royalCharmState == 4) return "Cannot be removed";
            if (!equipped && pd.charmSlotsFilled >= pd.charmSlots) return "No free notches";
            return null;
        }

        public void CollectActions(List<DsAction> into)
        {
            if (!_built || !DsFrame.InGame) return;
            into.Add(new DsAction(_presetLabel, _cyclePreset));
            into.Add(new DsAction("SAVE", _savePreset));
            into.Add(new DsAction("LOAD", _presetSaved ? _loadPreset : null, !_presetSaved));
            int n = _selNum;
            if (n < 1 || n > CharmCount || !_got[n]) return;
            var pd = PlayerData.instance;
            if (pd == null || pd.equippedCharms == null) return;

            bool equipped = pd.equippedCharms.Contains(n);
            string reason = BlockReason(pd, n, equipped);
            if (reason != null)
            {
                into.Add(new DsAction(reason, null, true, DsActionPlace.Pane));
                return;
            }
            bool ready = Time.unscaledTime >= _nextApply;
            into.Add(new DsAction(equipped ? _unequipLabel : _equipLabel, ready ? _toggle : null, !ready,
                                  DsActionPlace.Pane));
        }

        void Toggle()
        {
            if (Time.unscaledTime < _nextApply) return;
            _nextApply = Time.unscaledTime + ApplyCooldown;

            int n = _selNum;
            var pd = PlayerData.instance;
            var gm = GameManager.UnsafeInstance;
            var hero = HeroController.UnsafeInstance;
            if (!DsFrame.InGame || n < 1 || n > CharmCount || pd == null || gm == null || hero == null) return;
            if (!ReferenceEquals(gm.playerData, pd) || pd.equippedCharms == null) return;

            bool writes = false;
            try
            {
                // Everything asked again from the save itself: the button was
                // built a frame ago and two taps can land in one.
                if (!GetBool(_fGot[n], pd)) return;
                bool equipped = pd.equippedCharms.Contains(n);
                if (GetBool(_fEq[n], pd) != equipped)
                {
                    Debug.LogWarning("[HkCharms] charm " + n + " list/bool disagree; not touching it");
                    return;
                }
                string reason = BlockReason(pd, n, equipped);
                if (reason != null) { SetStatus(reason); return; }

                bool unlock = false;
                if (!equipped)
                {
                    int cost = GetInt(_fCost[n], pd);
                    bool over = pd.charmSlotsFilled + cost > pd.charmSlots;
                    if (over && !pd.canOvercharm)
                    {
                        _attempts++;
                        if (_attempts < OvercharmAttempts)
                        {
                            SetStatus(_attempts <= 2 ? "Not enough notches" : "The charm strains...");
                            return;
                        }
                        unlock = true;
                    }
                }

                writes = true;
                if (unlock) pd.canOvercharm = true;
                pd.SetBool("equippedCharm_" + n, !equipped);
                if (equipped) pd.UnequipCharm(n); else pd.EquipCharm(n);
                pd.CalculateNotchesUsed();
                gm.RefreshOvercharm();
                _attempts = 0;
                if (unlock) SetStatus("OVERCHARMED");
                else if (pd.overcharmed && !equipped) SetStatus("OVERCHARMED");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HkCharms] " + (writes ? "equip partially applied: " : "equip rejected: ") + e.Message);
            }

            if (!writes) return;
            // The game's own refreshes, each attempted even if a sibling throws.
            try { hero.CharmUpdate(); } catch (Exception e) { Debug.LogWarning("[HkCharms] CharmUpdate: " + e.Message); }
            try { PlayMakerFSM.BroadcastEvent("CHARM INDICATOR CHECK"); } catch { }
            try { PlayMakerFSM.BroadcastEvent("UPDATE BLUE HEALTH"); } catch { }
            Refresh(true);
        }
    }
}
#endif
