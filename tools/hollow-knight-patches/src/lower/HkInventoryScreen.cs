// HkInventoryScreen — Hollow Knight's Inventory pane on the lower screen,
// built from PlayerData rather than cloned from the game's inventory FSM.
//
// The old lower screen deep-cloned the game's "Inv" pane and then fought the
// clone's FSMs to keep it populated; when one donor was missing the pane stayed
// parked and drew nothing. This page reads the same PlayerData fields the
// game's FSMs read, in the game's own order, and draws them in Silksong's three
// columns: the Knight (HkKnightPanel: nail, mask and vessel pieces, spells round
// Focus, geo and essence), a three-wide grid of equipment and items, and the
// description.
//
// Art still comes from the game: the inventory prefab's own components carry
// every sprite as a serialized field (InvNailSprite.level1-5,
// InvItemDisplay.activeSprite, InvVesselFragments.*Sprite) and the spell /
// heart-piece / relic renderers hold theirs. Those are found once under
// GameManager.inventoryFSM's GameObject and cached; until then a cell shows the
// grid's neutral placeholder.
//
// Data is polled twice a second against a small signature; the grid is only
// rebuilt when that signature changes.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HollowKnightPatches.Lower
{
    public class HkInventoryScreen : IDsScreen
    {
        // ── Text ────────────────────────────────────────────────────────────

        static readonly Dictionary<string, string> _text = new Dictionary<string, string>(256);
        static int _textLanguage = int.MinValue;

        /// <summary>Drop cached text if the game's language changed. Call before a rebuild.</summary>
        internal static void CheckLanguage()
        {
            int lang = 0;
            try { lang = (int)TeamCherry.Localization.Language.CurrentLanguage(); } catch { }
            if (lang != _textLanguage) { _text.Clear(); _textLanguage = lang; }
        }

        /// <summary>
        /// A localised string from whichever loaded sheet has it ("" when none
        /// does). Cached per key; HK writes its line breaks as &lt;br&gt;.
        /// </summary>
        internal static string Loc(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            string v;
            if (_text.TryGetValue(key, out v)) return v;
            v = "";
            try
            {
                foreach (var sheet in TeamCherry.Localization.Language.GetSheets())
                {
                    if (!TeamCherry.Localization.Language.Has(key, sheet)) continue;
                    v = TeamCherry.Localization.Language.Get(key, sheet) ?? "";
                    break;
                }
            }
            catch { v = ""; }
            v = v.Replace("<br>", "\n");
            _text[key] = v;
            return v;
        }

        /// <summary>Loc of the first key that exists, else the fallback.</summary>
        internal static string LocFirst(string fallback, string a, string b = null, string c = null)
        {
            string v = Loc(a);
            if (v.Length == 0 && b != null) v = Loc(b);
            if (v.Length == 0 && c != null) v = Loc(c);
            return v.Length > 0 ? v : (fallback ?? "");
        }

        // ── Art ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The inventory prefab's own sprites, found once under the inventory
        /// FSM's GameObject (_GameCameras/HudCamera/Inventory, GameManager.cs
        /// 1783) and kept. Re-harvested only if that GameObject is replaced.
        /// </summary>
        internal static class Art
        {
            static Transform _root;
            static float _nextTry;
            public static int Version;

            public static readonly Sprite[] Nail = new Sprite[5];
            // InvItemDisplay.playerDataBool -> activeSprite.
            public static readonly Dictionary<string, Sprite> ByBool = new Dictionary<string, Sprite>(64);
            // GameObject name -> a static sprite (equipment items, relics, geo).
            public static readonly Dictionary<string, Sprite> ByName = new Dictionary<string, Sprite>(64);
            public static InvVesselFragments Vessel;

            // Renderers whose sprite the game's FSMs change with progress (spell
            // level, heart pieces). Read on every poll; no allocation.
            public static SpriteRenderer Fireball, Quake, Scream, Focus, HeartPieces;
            // The Dream Nail and Dreamgate carry no InvItemDisplay, so their art is the
            // renderer on the pane object itself (the grid drew its placeholder before).
            public static SpriteRenderer DreamNail, DreamGate;

            static readonly string[] Named =
            {
                "Dash Cloak", "Mantis Claw", "Super Dash", "Double Jump", "Acid Armour", "Lantern",
                "Map and Quill", "Kings Brand", "Tram Pass", "City Key", "Store Key", "White Key",
                "Love Key", "Xun Flower", "Simple Key", "Ore", "Rancid Egg",
                "Trinket1", "Trinket2", "Trinket3", "Trinket4", "Geo", "Godfinder",
            };

            public static bool Ready { get { return _root != null; } }

            public static void Ensure()
            {
                var gm = GameManager.UnsafeInstance;
                PlayMakerFSM fsm = null;
                try { fsm = gm != null ? gm.inventoryFSM : null; } catch { }
                Transform root = fsm != null ? fsm.transform : null;
                if (root == null) { if (_root != null) Forget(); return; }
                if (_root != null && ReferenceEquals(root, _root)) return;
                if (Time.unscaledTime < _nextTry) return;
                _nextTry = Time.unscaledTime + 2f;
                try { Harvest(root); }
                catch (Exception e) { Debug.LogWarning("[HkInventory] art harvest: " + e.Message); }
            }

            static void Forget()
            {
                _root = null;
                for (int i = 0; i < Nail.Length; i++) Nail[i] = null;
                ByBool.Clear(); ByName.Clear(); Vessel = null;
                Fireball = Quake = Scream = Focus = HeartPieces = null;
                DreamNail = DreamGate = null;
                Version++;
            }

            static void Harvest(Transform root)
            {
                Forget();

                var nail = root.GetComponentInChildren<InvNailSprite>(true);
                if (nail != null)
                {
                    Nail[0] = nail.level1; Nail[1] = nail.level2; Nail[2] = nail.level3;
                    Nail[3] = nail.level4; Nail[4] = nail.level5;
                }

                foreach (var d in root.GetComponentsInChildren<InvItemDisplay>(true))
                {
                    if (d == null || string.IsNullOrEmpty(d.playerDataBool) || d.activeSprite == null) continue;
                    if (!ByBool.ContainsKey(d.playerDataBool)) ByBool[d.playerDataBool] = d.activeSprite;
                }

                Vessel = root.GetComponentInChildren<InvVesselFragments>(true);

                // Named objects are looked up inside the Inventory pane ("Inv")
                // when it exists, so a same-named object in another pane cannot
                // donate the wrong art.
                Transform inv = null;
                var all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].name == "Inv") { inv = all[i]; break; }
                if (inv != null) all = inv.GetComponentsInChildren<Transform>(true);

                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    string n = t.name;
                    switch (n)
                    {
                        case "Spell Fireball": if (Fireball == null) Fireball = RendererOf(t); continue;
                        case "Spell Quake": if (Quake == null) Quake = RendererOf(t); continue;
                        case "Spell Scream": if (Scream == null) Scream = RendererOf(t); continue;
                        case "Spell Focus": if (Focus == null) Focus = RendererOf(t); continue;
                        case "Heart Pieces": if (HeartPieces == null) HeartPieces = RendererOf(t); continue;
                    }
                    string lower = n.ToLowerInvariant();
                    if (lower.Contains("dream"))
                    {
                        bool gate = lower.Contains("gate");
                        var r = RendererOf(t);
                        if (r != null && r.sprite != null)
                        {
                            if (gate && DreamGate == null) { DreamGate = r; Debug.Log("[HkInventory] dreamgate art: " + n + " " + r.sprite.name); }
                            else if (!gate && DreamNail == null) { DreamNail = r; Debug.Log("[HkInventory] dream nail art: " + n + " " + r.sprite.name); }
                        }
                    }
                    if (Array.IndexOf(Named, n) < 0 || ByName.ContainsKey(n)) continue;
                    Sprite s = null;
                    var disp = t.GetComponent<InvItemDisplay>();
                    if (disp != null) s = disp.activeSprite;
                    if (s == null) { var r = RendererOf(t); if (r != null) s = r.sprite; }
                    if (s != null) ByName[n] = s;
                }

                _root = root;
                Version++;
            }

            static SpriteRenderer RendererOf(Transform t)
            {
                var r = t.GetComponent<SpriteRenderer>();
                if (r != null && r.sprite != null) return r;
                var c = t.GetComponentInChildren<SpriteRenderer>(true);
                return c != null ? c : r;
            }

            public static Sprite Live(SpriteRenderer r)
            {
                try { return r != null ? r.sprite : null; } catch { return null; }
            }

            public static int LiveSignature()
            {
                int h = 17;
                h = h * 31 + Id(Live(Fireball));
                h = h * 31 + Id(Live(Quake));
                h = h * 31 + Id(Live(Scream));
                h = h * 31 + Id(Live(Focus));
                h = h * 31 + Id(Live(HeartPieces));
                h = h * 31 + Id(Live(DreamNail));
                return h;
            }

            static int Id(Sprite s) { return s != null ? s.GetInstanceID() : 0; }

            public static Sprite Bool(string key)
            {
                Sprite s; return ByBool.TryGetValue(key, out s) ? s : null;
            }

            public static Sprite Name(string key)
            {
                Sprite s; return ByName.TryGetValue(key, out s) ? s : null;
            }
        }

        static float _tabNextTry;

        /// <summary>The game's first nail sprite, or null until the inventory prefab is resident.</summary>
        public static Sprite TabArt()
        {
            if (!Art.Ready && Time.unscaledTime >= _tabNextTry)
            {
                _tabNextTry = Time.unscaledTime + 1f;
                Art.Ensure();
            }
            return Art.Nail[0];
        }

        // ── Layout ──────────────────────────────────────────────────────────

        // Silksong's columns (DsInventoryScreen), so the two pages line up.
        const float LeftX = 20f, LeftW = 420f;
        const float GridX = 470f, GridW = 400f;
        const float DetailX = 900f, DetailW = 320f;
        const float PollSeconds = 0.5f;

        readonly DsIconGrid _grid = new DsIconGrid();
        readonly HkKnightPanel _knight = new HkKnightPanel();
        HkKnightPanel.Kit _kit = new HkKnightPanel.Kit();
        readonly List<DsSection> _sections = new List<DsSection>(4);
        readonly DsSection _secEquip = new DsSection(null, DsTheme.InkDim);
        readonly DsSection _secItems = new DsSection(" ", DsTheme.InkDim);
        float _nextPoll;
        int _signature = int.MinValue;
        bool _built;

        public string Id { get { return "inventory"; } }
        public string Title { get { return "Inventory"; } }
        public bool Available { get { return DsFrame.InGame; } }

        public HkInventoryScreen()
        {
            _sections.Add(_secEquip);
            _sections.Add(_secItems);
        }

        public void Build(RectTransform host)
        {
            float colH = DsLayout.Current.Body.height - DsTheme.Pad * 2f;

            _knight.Build(host, LeftX, DsTheme.Pad, LeftW, colH);
            DsWidgets.VRule(host, "split-items", (LeftX + LeftW + GridX) * 0.5f, DsTheme.Pad, colH);
            DsWidgets.VRule(host, "split-detail", (GridX + GridW + DetailX) * 0.5f, DsTheme.Pad, colH);

            // One description column and one cursor for both halves, as on Silksong.
            _knight.OnSelect = (name, desc, layoutRect) =>
            {
                _grid.ShowDetail(name, desc);
                float bodyY = DsLayout.Current.Body.y;
                _grid.SetExternalTarget(new Rect(layoutRect.x, layoutRect.y - bodyY, layoutRect.width, layoutRect.height),
                                        DsFrame.SelectionCursor().GlowColor, name);
            };

            _grid.EmptyMessage = "Nothing collected yet";
            _grid.Build(host, 3, GridX, GridW, new Rect(DetailX, DsTheme.Pad, DetailW, colH), false,
                        GridX - (LeftX + LeftW + GridX) * 0.5f);
            _built = true;
            Refresh(true);
        }

        public void OnShow()
        {
            _signature = int.MinValue;
            if (_built) Refresh(true);
        }

        public void OnHide() { }

        public void Tick(float dt)
        {
            if (!_built) return;
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollSeconds;
                Refresh(false);
            }
            _grid.Tick();
        }

        public void OnGesture(DsGesture g)
        {
            if (!_built) return;
            if (g.Type == DsGestureType.Tap && _knight.OnTap(DsFrame.ToLayout(g.Position))) return;
            _grid.OnGesture(g);
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
                    _secEquip.Items.Clear(); _secItems.Items.Clear();
                    _grid.SetSections(_sections);
                    _knight.Set(null);
                }
                return;
            }

            Art.Ensure();
            var pd = PlayerData.instance;
            if (pd == null) return;

            int sig;
            try { sig = Signature(pd); }
            catch (Exception e) { Debug.LogWarning("[HkInventory] signature: " + e.Message); return; }
            if (!force && sig == _signature) return;
            _signature = sig;

            _grid.EmptyMessage = "Nothing collected yet";
            CheckLanguage();
            _secEquip.Items.Clear(); _secItems.Items.Clear();
            _kit = new HkKnightPanel.Kit();
            try { Collect(pd); }
            catch (Exception e) { Debug.LogWarning("[HkInventory] collect: " + e.Message); }
            _grid.SetSections(_sections);
            _knight.Set(_kit);
        }

        static int B(bool b) { return b ? 1 : 0; }

        static int Signature(PlayerData pd)
        {
            int h = 17;
            h = h * 31 + pd.nailSmithUpgrades;
            h = h * 31 + pd.fireballLevel;
            h = h * 31 + pd.quakeLevel;
            h = h * 31 + pd.screamLevel;
            h = h * 31 + pd.heartPieces;
            h = h * 31 + pd.maxHealthBase;
            h = h * 31 + pd.vesselFragments;
            h = h * 31 + pd.MPReserveMax;
            h = h * 31 + pd.geo;
            h = h * 31 + pd.simpleKeys;
            h = h * 31 + pd.ore;
            h = h * 31 + pd.rancidEggs;
            h = h * 31 + pd.trinket1;
            h = h * 31 + pd.trinket2;
            h = h * 31 + pd.trinket3;
            h = h * 31 + pd.trinket4;
            h = h * 31 + pd.dreamOrbs;
            int bits =
                B(pd.hasDash) | B(pd.hasShadowDash) << 1 | B(pd.hasWalljump) << 2 |
                B(pd.hasSuperDash) << 3 | B(pd.hasDoubleJump) << 4 | B(pd.hasAcidArmour) << 5 |
                B(pd.hasLantern) << 6 | B(pd.hasMap) << 7 | B(pd.hasQuill) << 8 |
                B(pd.hasKingsBrand) << 9 | B(pd.hasTramPass) << 10 | B(pd.hasCityKey) << 11 |
                B(pd.hasSlykey) << 12 | B(pd.hasWhiteKey) << 13 | B(pd.usedWhiteKey) << 14 |
                B(pd.hasLoveKey) << 15 | B(pd.hasXunFlower) << 16 | B(pd.xunFlowerBroken) << 17 |
                B(pd.hasDreamNail) << 18 | B(pd.dreamNailUpgraded) << 19 | B(pd.hasDreamGate) << 20 |
                B(pd.hasCyclone) << 21 | B(pd.hasDashSlash) << 22 | B(pd.hasUpwardSlash) << 23 |
                B(pd.hasGodfinder) << 24 | B(pd.foundTrinket1) << 25 | B(pd.foundTrinket2) << 26 |
                B(pd.foundTrinket3) << 27 | B(pd.foundTrinket4) << 28;
            h = h * 31 + bits;
            h = h * 31 + Art.Version;
            h = h * 31 + Art.LiveSignature();
            return h;
        }

        void Add(DsSection sec, string key, string suffix, Sprite icon, string badge = null,
                 string fallbackName = null, string suffixAlt = null)
        {
            sec.Items.Add(Make(key, suffix, icon, badge, fallbackName, suffixAlt));
        }

        static DsItem Make(string key, string suffix, Sprite icon, string badge = null,
                           string fallbackName = null, string suffixAlt = null)
        {
            string name = LocFirst(null, "INV_NAME_" + suffix, suffixAlt != null ? "INV_NAME_" + suffixAlt : null);
            string desc = Loc("INV_DESC_" + suffix);
            if (desc.Length == 0 && suffixAlt != null) desc = Loc("INV_DESC_" + suffixAlt);
            if (name.Length == 0) name = fallbackName ?? key;
            return new DsItem
            {
                Key = key,
                Name = name,
                Description = desc,
                Icon = icon,
                Tint = Color.white,
                Badge = badge,
            };
        }

        static string Count(int n) { return n > 0 ? n.ToString() : null; }

        void Collect(PlayerData pd)
        {
            // The Knight's own kit goes to the character column: the nail, Focus
            // with the spells, nail arts and Dream Nail round it, the mask and
            // vessel pieces, geo and essence.
            int up = Mathf.Clamp(pd.nailSmithUpgrades, 0, 4);
            _kit.Nail = Make("nail", "NAIL" + (up + 1), Art.Nail[up] ?? Art.Nail[0], null, "Nail");
            _kit.Focus = Make("focus", "SPELL_FOCUS", Art.Live(Art.Focus), null, "Focus");
            var ring = _kit.Ring;
            if (pd.fireballLevel > 0)
                ring.Add(Make("fireball", pd.fireballLevel >= 2 ? "SPELL_FIREBALL2" : "SPELL_FIREBALL1",
                    Art.Live(Art.Fireball), null, pd.fireballLevel >= 2 ? "Shade Soul" : "Vengeful Spirit",
                    pd.fireballLevel >= 2 ? "SPELL_FIREBALL1" : null));
            if (pd.quakeLevel > 0)
                ring.Add(Make("quake", pd.quakeLevel >= 2 ? "SPELL_QUAKE2" : "SPELL_QUAKE1",
                    Art.Live(Art.Quake), null, pd.quakeLevel >= 2 ? "Descending Dark" : "Desolate Dive",
                    pd.quakeLevel >= 2 ? "SPELL_QUAKE1" : null));
            if (pd.screamLevel > 0)
                ring.Add(Make("scream", pd.screamLevel >= 2 ? "SPELL_SCREAM2" : "SPELL_SCREAM1",
                    Art.Live(Art.Scream), null, pd.screamLevel >= 2 ? "Abyss Shriek" : "Howling Wraiths",
                    pd.screamLevel >= 2 ? "SPELL_SCREAM1" : null));
            if (pd.hasCyclone) ring.Add(Make("art_cyclone", "ART_CYCLONE", Art.Bool("hasCyclone"), null, "Cyclone Slash"));
            if (pd.hasDashSlash) ring.Add(Make("art_dash", "ART_DASH", Art.Bool("hasDashSlash"), null, "Dash Slash"));
            if (pd.hasUpwardSlash) ring.Add(Make("art_upper", "ART_UPPER", Art.Bool("hasUpwardSlash"), null, "Great Slash"));

            Sprite dn = null;
            if (pd.hasDreamNail)
            {
                dn = pd.dreamNailUpgraded ? Art.Bool("dreamNailUpgraded") : null;
                if (dn == null) dn = Art.Bool("hasDreamNail") ?? Art.Bool("dreamNail") ?? Art.Live(Art.DreamNail);
                ring.Add(Make("dreamnail", pd.dreamNailUpgraded ? "DREAMNAIL_B" : "DREAMNAIL_A", dn,
                    null, "Dream Nail", pd.dreamNailUpgraded ? "DREAMNAIL_A" : null));
            }

            // Mask shards and vessel fragments: always shown, as the game does.
            // Their art already shows how many pieces are held.
            bool masksFull = pd.maxHealthBase >= pd.maxHealthCap;
            int pieces = Mathf.Clamp(pd.heartPieces, 0, 3);
            _kit.Masks = Make("heartpieces", masksFull ? "HEARTPIECE_ALL" : "HEARTPIECE_" + pieces,
                Art.Live(Art.HeartPieces), null, "Mask Shards");

            bool vesselsFull = pd.MPReserveMax >= pd.MPReserveCap;
            int frags = Mathf.Clamp(pd.vesselFragments, 0, 2);
            Sprite vs = null;
            if (Art.Vessel != null)
                vs = vesselsFull ? Art.Vessel.fullSprite
                   : frags == 2 ? Art.Vessel.doublePieceSprite
                   : frags == 1 ? Art.Vessel.singlePieceSprite
                   : Art.Vessel.backboardSprite;
            _kit.Vessels = Make("vessels", vesselsFull ? "SOULORBS_ALL" : "SOULORBS_" + frags, vs,
                null, "Vessel Fragments", vesselsFull ? "SOULORBS_2" : "SOULORBS_NONE");

            // Counters, as Silksong's rosaries and shell shards: the figure is the description.
            _kit.Geo = Make("geo", "GEO", Art.Name("Geo"), pd.geo.ToString(), "Geo");
            _kit.HasEssence = pd.hasDreamNail;
            _kit.Essence = new DsItem
            {
                Key = "essence", Name = LocFirst("Essence", "INV_NAME_ESSENCE", "INV_NAME_DREAMORBS"),
                Description = pd.dreamOrbs.ToString(), Icon = dn, Tint = Color.white, Badge = pd.dreamOrbs.ToString(),
            };

            // Abilities, keys and consumables, in Build Equipment List's order
            // with its gates (old HKDualScreen.Bottom.Inventory EQUIP_ORDER).
            if (pd.hasDash || pd.hasShadowDash)
            {
                Sprite s = pd.hasShadowDash ? Art.Bool("hasShadowDash") : null;
                Add(_secEquip, "dash", pd.hasShadowDash ? "SHADOWDASH" : "DASH", s ?? Art.Name("Dash Cloak"),
                    null, pd.hasShadowDash ? "Shade Cloak" : "Mothwing Cloak", pd.hasShadowDash ? "DASH" : null);
            }
            if (pd.hasWalljump) Add(_secEquip, "walljump", "WALLJUMP", Art.Name("Mantis Claw"), null, "Mantis Claw");
            if (pd.hasSuperDash) Add(_secEquip, "superdash", "SUPERDASH", Art.Name("Super Dash"), null, "Crystal Heart");
            if (pd.hasDoubleJump) Add(_secEquip, "doublejump", "DOUBLEJUMP", Art.Name("Double Jump"), null, "Monarch Wings");
            if (pd.hasAcidArmour) Add(_secEquip, "acidarmour", "ACIDARMOUR", Art.Name("Acid Armour"), null, "Isma's Tear");
            if (pd.hasLantern) Add(_secEquip, "lantern", "LANTERN", Art.Name("Lantern"), null, "Lumafly Lantern");
            if (pd.hasMap || pd.hasQuill) Add(_secEquip, "mapquill", "MAPQUILL", Art.Name("Map and Quill"), null, "Map and Quill", pd.hasQuill ? "QUILL" : "MAP");
            if (pd.hasKingsBrand) Add(_secEquip, "kingsbrand", "KINGSBRAND", Art.Name("Kings Brand"), null, "King's Brand");
            if (pd.hasTramPass) Add(_secEquip, "trampass", "TRAM_PASS", Art.Name("Tram Pass"), null, "Tram Pass");
            if (pd.hasCityKey) Add(_secEquip, "citykey", "CITYKEY", Art.Name("City Key"), null, "City Crest");
            if (pd.hasSlykey) Add(_secEquip, "storekey", "STOREKEY", Art.Name("Store Key"), null, "Shopkeeper's Key");
            // Elegant Key: owned, not given to Sly, not yet used (the FSM's three branches).
            if (!pd.hasSlykey && pd.hasWhiteKey && !pd.usedWhiteKey)
                Add(_secEquip, "whitekey", "WHITEKEY", Art.Name("White Key"), null, "Elegant Key");
            if (pd.hasLoveKey) Add(_secEquip, "lovekey", "LOVEKEY", Art.Name("Love Key"), null, "Love Key");
            if (pd.hasXunFlower)
                Add(_secEquip, "flower", pd.xunFlowerBroken ? "FLOWER_BROKEN" : "FLOWER", Art.Name("Xun Flower"),
                    null, "Delicate Flower", "FLOWER");
            if (pd.hasGodfinder) Add(_secEquip, "godfinder", "GODFINDER", Art.Name("Godfinder"), null, "God Tuner");
            if (pd.hasDreamGate) Add(_secEquip, "dreamgate", "DREAMGATE", Art.Bool("hasDreamGate") ?? Art.Live(Art.DreamGate), null, "Dreamgate");

            if (pd.simpleKeys > 0) Add(_secItems, "simplekey", "SIMPLEKEY", Art.Name("Simple Key"), Count(pd.simpleKeys), "Simple Key");
            if (pd.ore > 0) Add(_secItems, "ore", "ORE", Art.Name("Ore"), Count(pd.ore), "Pale Ore");
            if (pd.rancidEggs > 0) Add(_secItems, "rancidegg", "RANCIDEGG", Art.Name("Rancid Egg"), Count(pd.rancidEggs), "Rancid Egg");

            // Relics: Wanderer's Journal, Hallownest Seal, King's Idol, Arcane Egg.
            if (pd.trinket1 > 0 || pd.foundTrinket1) Add(_secItems, "trinket1", "TRINKET1", Art.Name("Trinket1"), pd.trinket1.ToString(), "Wanderer's Journal");
            if (pd.trinket2 > 0 || pd.foundTrinket2) Add(_secItems, "trinket2", "TRINKET2", Art.Name("Trinket2"), pd.trinket2.ToString(), "Hallownest Seal");
            if (pd.trinket3 > 0 || pd.foundTrinket3) Add(_secItems, "trinket3", "TRINKET3", Art.Name("Trinket3"), pd.trinket3.ToString(), "King's Idol");
            if (pd.trinket4 > 0 || pd.foundTrinket4) Add(_secItems, "trinket4", "TRINKET4", Art.Name("Trinket4"), pd.trinket4.ToString(), "Arcane Egg");

        }
    }
}
#endif
