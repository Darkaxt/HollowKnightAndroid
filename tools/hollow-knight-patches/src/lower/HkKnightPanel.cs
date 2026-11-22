// HkKnightPanel — the left column of Hollow Knight's Inventory tab: what the
// Knight IS. Silksong's DsHornetPanel is the canonical layout and this follows
// it piece for piece, with Hollow Knight's own inventory pane as the content:
//
//   Silksong                          Hollow Knight
//   needle lane                       nail lane
//   mask shards | spool fragments     mask shards | vessel fragments
//   skills around the silk core       spells, nail arts and Dream Nail around Focus
//   rosaries | shell shards           geo | essence
//
// The old lower screen showed this by cloning the game's "Inv" pane; when the
// page moved to the shared frame (#19) it became a plain grid and the Knight's
// kit was dumped in with the items. This restores the character column. Every
// sprite is the game's own (HkInventoryScreen.Art); nothing here draws art.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;

namespace HollowKnightPatches.Lower
{
    public class HkKnightPanel
    {
        /// <summary>What the panel shows; every entry may be absent (Icon null).</summary>
        public class Kit
        {
            public DsItem Nail, Focus, Masks, Vessels, Geo, Essence;
            public bool HasEssence;
            public readonly List<DsItem> Ring = new List<DsItem>();
        }

        class Slot
        {
            public RectTransform Root;
            public Image Image;
            public string Name, Desc;
            public Rect Art;   // drawn box inside Root
        }

        struct Hit { public Rect Box; public string Name, Desc; }

        // Silksong's numbers (DsHornetPanel), so both columns have the same proportions.
        const float NailW = 96f;
        const float MaskSize = 210f;
        const float VesselSize = 172f;
        const float ShardGap = 8f;
        const float CoreSize = 174f;
        const float SkillSize = 96f;
        const float RingTightness = 0.81f;
        const float MinArtW = 12f + NailW + 8f + MaskSize + ShardGap + VesselSize + 10f;
        const float MinArtH = 762f;

        public System.Action<string, string, Rect> OnSelect;

        readonly List<Slot> _fixed = new List<Slot>();
        readonly List<Slot> _ringSlots = new List<Slot>();
        readonly List<Hit> _hits = new List<Hit>();
        RectTransform _art;
        Slot _nail, _mask, _vessel, _core;
        Image _geoIcon, _essenceIcon;
        TmpText _geo, _essence;
        float _x, _y, _scale = 1f, _artX, _countY, _halfW;
        float _ringCx, _ringCy, _ringR;
        Kit _kit;

        public void Build(RectTransform host, float x, float y, float w, float h)
        {
            _x = x; _y = y;
            var panel = DsWidgets.Rect(host, "knight");
            DsWidgets.Place(panel, x, y, w, h);

            float artW = Mathf.Max(w, MinArtW), artH = Mathf.Max(h, MinArtH);
            _scale = Mathf.Min(w / artW, h / artH);
            _artX = (w - artW * _scale) * 0.5f;
            _art = DsWidgets.Rect(panel, "art");
            DsWidgets.Place(_art, _artX, 0f, artW, artH);
            _art.localScale = new Vector3(_scale, _scale, 1f);

            _nail = MakeSlot(_fixed, "nail", 12f, 34f, NailW, artH - 170f);
            float colX = 12f + NailW + 8f, colW = artW - colX - 10f;
            _mask = MakeSlot(_fixed, "masks", colX, 26f, MaskSize, MaskSize);
            _vessel = MakeSlot(_fixed, "vessels", colX + MaskSize + ShardGap, 44f, VesselSize, VesselSize);

            float ringTop = MaskSize + 40f, ringBottom = artH - 108f;
            _ringCx = colX + colW * 0.5f - 24f;
            _ringCy = (ringTop + ringBottom) * 0.5f;
            _ringR = (Mathf.Min(colW * 0.5f, (ringBottom - ringTop) * 0.5f) - SkillSize * 0.5f) * RingTightness;
            _core = MakeSlot(_fixed, "focus", _ringCx - CoreSize * 0.5f, _ringCy - CoreSize * 0.5f, CoreSize, CoreSize);

            _countY = artH - 88f;
            _halfW = artW * 0.5f;
            _geoIcon = DsWidgets.Icon(_art, "geo-i", null, Color.white);
            DsWidgets.Place(_geoIcon.rectTransform, 18f, _countY, 58f, 58f);
            _geo = DsWidgets.Label(_art, "geo-n", "", DsTheme.RowSize, DsTheme.Ink);
            if (_geo != null) DsWidgets.Place(_geo.rectTransform, 84f, _countY + 6f, 170f, 46f);
            _essenceIcon = DsWidgets.Icon(_art, "essence-i", null, Color.white);
            DsWidgets.Place(_essenceIcon.rectTransform, _halfW + 18f, _countY, 58f, 58f);
            _essence = DsWidgets.Label(_art, "essence-n", "", DsTheme.RowSize, DsTheme.Ink);
            if (_essence != null) DsWidgets.Place(_essence.rectTransform, _halfW + 84f, _countY + 6f, 200f, 46f);
        }

        Slot MakeSlot(List<Slot> into, string name, float x, float y, float w, float h)
        {
            var slot = new Slot { Root = DsWidgets.Rect(_art, name) };
            DsWidgets.Place(slot.Root, x, y, w, h);
            into.Add(slot);
            return slot;
        }

        /// <summary>Redraw from a new kit. Cheap enough for the page's signature-gated refresh.</summary>
        public void Set(Kit kit)
        {
            _kit = kit;
            Draw(_nail, kit != null ? kit.Nail : default(DsItem));
            Draw(_mask, kit != null ? kit.Masks : default(DsItem));
            Draw(_vessel, kit != null ? kit.Vessels : default(DsItem));
            Draw(_core, kit != null ? kit.Focus : default(DsItem));

            for (int i = 0; i < _ringSlots.Count; i++)
                if (_ringSlots[i].Root != null) Object.Destroy(_ringSlots[i].Root.gameObject);
            _ringSlots.Clear();
            int n = kit != null ? kit.Ring.Count : 0;
            for (int i = 0; i < n; i++)
            {
                // Evenly spaced, clockwise from the top, as Silksong's skills.
                float a = -Mathf.PI * 0.5f + Mathf.PI * 2f * i / n;
                var slot = MakeSlot(_ringSlots, "ring" + i,
                                    _ringCx + Mathf.Cos(a) * _ringR - SkillSize * 0.5f,
                                    _ringCy + Mathf.Sin(a) * _ringR - SkillSize * 0.5f, SkillSize, SkillSize);
                Draw(slot, kit.Ring[i]);
            }

            SetCounter(_geoIcon, _geo, kit != null ? kit.Geo : default(DsItem), true);
            SetCounter(_essenceIcon, _essence, kit != null ? kit.Essence : default(DsItem), kit != null && kit.HasEssence);
            RebuildHits();
        }

        static void Draw(Slot slot, DsItem item)
        {
            if (slot.Image != null) { Object.Destroy(slot.Image.gameObject); slot.Image = null; }
            slot.Name = item.Name; slot.Desc = item.Description; slot.Art = default(Rect);
            if (item.Icon == null) return;
            float boxW = slot.Root.sizeDelta.x, boxH = slot.Root.sizeDelta.y;
            var r = item.Icon.rect;
            float aspect = r.height > 0f ? r.width / r.height : 1f;
            float w = boxW, h = boxH;
            if (boxW / boxH > aspect) w = boxH * aspect; else h = boxW / aspect;
            slot.Art = new Rect((boxW - w) * 0.5f, (boxH - h) * 0.5f, w, h);
            var img = DsWidgets.Icon(slot.Root, "art", item.Icon, Color.white);
            img.useSpriteMesh = true;
            img.preserveAspect = true;
            DsWidgets.Place(img.rectTransform, slot.Art.x, slot.Art.y, w, h);
            slot.Image = img;
        }

        static void SetCounter(Image icon, TmpText label, DsItem item, bool show)
        {
            if (icon != null)
            {
                icon.sprite = item.Icon;
                icon.useSpriteMesh = true;
                icon.preserveAspect = true;
                icon.enabled = show && item.Icon != null;
            }
            if (label != null) label.text = show ? (item.Badge ?? "") : "";
        }

        void RebuildHits()
        {
            _hits.Clear();
            AddSlotHits(_fixed);
            AddSlotHits(_ringSlots);
            if (_kit != null)
            {
                AddHit(new Rect(12f, _countY - 6f, 248f, 70f), _kit.Geo);
                if (_kit.HasEssence) AddHit(new Rect(_halfW + 12f, _countY - 6f, 278f, 70f), _kit.Essence);
            }
        }

        void AddSlotHits(List<Slot> slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.Image == null || s.Art.width <= 0f) continue;
                var p = s.Root.anchoredPosition;
                AddHit(new Rect(p.x + s.Art.x, -p.y + s.Art.y, s.Art.width, s.Art.height),
                       new DsItem { Name = s.Name, Description = s.Desc });
            }
        }

        // Art space -> layout space, the way DsHornetPanel maps its slots.
        void AddHit(Rect art, DsItem item)
        {
            if (string.IsNullOrEmpty(item.Name) && string.IsNullOrEmpty(item.Description)) return;
            _hits.Add(new Hit
            {
                Box = new Rect(_x + _artX + art.x * _scale, DsLayout.Current.Body.y + _y + art.y * _scale,
                               art.width * _scale, art.height * _scale),
                Name = item.Name,
                Desc = item.Description,
            });
        }

        /// <summary>True if the tap was ours. Smallest target wins, as on Silksong.</summary>
        public bool OnTap(Vector2 layoutPoint)
        {
            int best = -1;
            float bestArea = float.MaxValue;
            for (int i = 0; i < _hits.Count; i++)
            {
                var b = _hits[i].Box;
                if (!b.Contains(layoutPoint)) continue;
                float area = b.width * b.height;
                if (area < bestArea) { bestArea = area; best = i; }
            }
            if (best < 0) return false;
            if (OnSelect != null) OnSelect(_hits[best].Name, _hits[best].Desc, _hits[best].Box);
            return true;
        }
    }
}
#endif
