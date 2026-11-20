#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DualSouls.Mods
{
    /// <summary>
    /// #37 collectible pins on a game map. Each pin is a small disc parented to its
    /// room's map object, so it follows the map's own pan, zoom and reveal: a room
    /// the player has not mapped is inactive and so are its pins. A collected item's
    /// pin is switched off on the next <see cref="Refresh"/>.
    /// </summary>
    internal sealed class CollectiblePins
    {
        /// <summary>A room on the map: its transform and the size of its bounds sprite in local units.</summary>
        public struct Room
        {
            public Transform Transform;
            public Vector2 SpriteSize;
            /// <summary>False hides the room's pins even when its object is active (Silksong rough rooms).</summary>
            public bool Shown;
        }

        readonly CollectibleTable _table;
        readonly Dictionary<CollectibleSpot, GameObject> _pins = new Dictionary<CollectibleSpot, GameObject>();
        static Sprite _dot;

        public CollectiblePins(CollectibleTable table)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        public int Shown { get; private set; }

        readonly Dictionary<CollectibleSpot, string> _reasons = new Dictionary<CollectibleSpot, string>();

        /// <summary>#47 QA: why the last <see cref="Refresh"/> did or did not pin a spot
        /// (shown, no-room, room-hidden, collected, offset), or null before the first refresh.</summary>
        public string ReasonOf(CollectibleSpot spot)
        {
            string r;
            return spot != null && _reasons.TryGetValue(spot, out r) ? r : null;
        }

        public static Color ColorOf(string kind)
        {
            switch (kind)
            {
                case "grub": return new Color(0.55f, 0.95f, 0.45f, 1f);
                case "mask": return new Color(1f, 1f, 1f, 1f);
                case "vessel": return new Color(0.45f, 0.75f, 1f, 1f);
                case "charm": return new Color(1f, 0.55f, 0.85f, 1f);
                case "root": return new Color(1f, 0.85f, 0.35f, 1f);
                case "spool": return new Color(0.55f, 0.85f, 1f, 1f);
                case "tool": return new Color(1f, 0.6f, 0.3f, 1f);
                case "locket": return new Color(0.85f, 0.6f, 1f, 1f);
                case "flea": return new Color(0.55f, 0.95f, 0.45f, 1f);
                default: return new Color(0.9f, 0.9f, 0.9f, 1f);
            }
        }

        static Sprite Dot
        {
            get
            {
                if (_dot != null) return _dot;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "DsPinDot", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                float c = (n - 1) / 2f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        // White core, dark rim: readable on light and dark rooms alike.
                        float a = Mathf.Clamp01(c + 0.5f - d);
                        byte v = d < c - 4f ? (byte)255 : (byte)30;
                        px[y * n + x] = new Color32(v, v, v, (byte)(a * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                _dot = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
                _dot.name = "DsPinDot";
                return _dot;
            }
        }

        /// <param name="roomOf">The map object of a scene, or false when the map has none.</param>
        /// <param name="collected">Whether the save already holds this collectible.</param>
        /// <param name="mapRoot">The map's root: pins are sized in its local units.</param>
        /// <param name="size">Pin diameter in map-root local units.</param>
        /// <param name="worldZ">World Z for every pin (the slice the map's camera draws pins in).</param>
        public void Refresh(Func<string, Room?> roomOf, Func<CollectibleSpot, bool> collected, Transform mapRoot,
                            float size, float worldZ, int layer, int sortingLayerId, int sortingOrder)
        {
            int shown = 0;
            foreach (var spot in _table.Spots)
            {
                GameObject pin;
                _pins.TryGetValue(spot, out pin);
                Room? room = roomOf(spot.Scene);
                bool want = room.HasValue && room.Value.Transform != null && room.Value.Shown;
                string reason = !room.HasValue || room.Value.Transform == null ? "no-room" : want ? "shown" : "room-hidden";
                bool got = false;
                if (want)
                {
                    try { got = collected(spot); } catch { got = false; }
                    want = !got;
                    if (got) reason = "collected";
                }
                _reasons[spot] = reason;
                if (!want)
                {
                    if (pin != null && pin.activeSelf) pin.SetActive(false);
                    continue;
                }
                var r = room.Value;
                if (pin == null || pin.transform.parent != r.Transform)
                {
                    if (pin != null) UnityEngine.Object.Destroy(pin);
                    pin = Make(spot, r, mapRoot, size, worldZ, layer, sortingLayerId, sortingOrder);
                    _pins[spot] = pin;
                    if (pin == null) { _reasons[spot] = "offset"; continue; }
                }
                if (!pin.activeSelf) pin.SetActive(true);
                shown++;
            }
            Shown = shown;
        }

        GameObject Make(CollectibleSpot spot, Room room, Transform mapRoot, float size, float worldZ,
                        int layer, int sortingLayerId, int sortingOrder)
        {
            float ox, oy;
            if (!_table.TryRoomOffset(spot, room.SpriteSize.x, room.SpriteSize.y, out ox, out oy)) return null;
            var go = new GameObject("DsPin " + spot.Kind) { layer = layer };
            var t = go.transform;
            t.SetParent(room.Transform, false);
            t.localPosition = new Vector3(ox, oy, 0f);
            var p = t.position;
            t.position = new Vector3(p.x, p.y, worldZ);
            // Diameter `size` in map-root units, whatever the room's own scale.
            float rootScale = mapRoot != null ? Mathf.Abs(mapRoot.lossyScale.x) : 1f;
            float roomScale = Mathf.Abs(room.Transform.lossyScale.x);
            float local = roomScale > 1e-6f ? size * rootScale / roomScale : size;
            t.localScale = new Vector3(local, local, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Dot;
            sr.color = ColorOf(spot.Kind);
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = sortingOrder;
            return go;
        }

        /// <summary>Drop every pin (the map they hung on was rebuilt).</summary>
        public void Clear()
        {
            foreach (var pin in _pins.Values)
                if (pin != null) UnityEngine.Object.Destroy(pin);
            _pins.Clear();
            _reasons.Clear();
            Shown = 0;
        }
    }
}
#endif
