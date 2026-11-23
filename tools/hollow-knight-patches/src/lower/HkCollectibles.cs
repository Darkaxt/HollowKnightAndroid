// HkCollectibles — #37 collectible pins and the completion checklist for Hollow Knight.
//
// The table (HkCollectibleData) is generated from the game's own scenes by
// tools/collectibles. Pins hang on the rooms of HkMapView's private map clone
// and (#69) on the game's own map, so they appear exactly where the player has
// mapped and nowhere else.
//
// Scene-bool pickups (grubs, mask shards, vessel fragments) are written to
// SceneData when the player leaves the room, so their pin goes then; charms
// and whispering roots are PlayerData and go at once.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DualSouls.Mods;
using UnityEngine;

namespace HollowKnightPatches.Lower
{
    static class HkCollectibles
    {
        public static float PinSize { get { return DsConfig.Int("pin_size_pct", 30) / 100f; } }

        public static readonly KeyValuePair<string, string>[] Kinds =
        {
            new KeyValuePair<string, string>("grub", "Grubs"),
            new KeyValuePair<string, string>("mask", "Mask shards"),
            new KeyValuePair<string, string>("vessel", "Vessel fragments"),
            new KeyValuePair<string, string>("charm", "Charms"),
            new KeyValuePair<string, string>("root", "Whispering roots"),
        };

        static CollectibleTable _table;
        static bool _tableFailed;

        public static CollectibleTable Table
        {
            get
            {
                if (_table == null && !_tableFailed)
                {
                    try { _table = CollectibleTable.Parse(HkCollectibleData.Table); }
                    catch (Exception e) { _tableFailed = true; Debug.LogWarning("[HkCollect] table: " + e.Message); }
                }
                return _table;
            }
        }

        /// <summary>Whether the current save holds this collectible.</summary>
        public static bool Collected(CollectibleSpot s)
        {
            var pd = PlayerData.instance;
            if (pd == null) return false;
            string field = s.PlayerDataField;
            if (field != null) return pd.GetBool(field);
            string list = s.PlayerDataList;
            if (list != null)
            {
                var l = ListField(pd, list);
                return l != null && l.Contains(s.Scene);
            }
            var sd = SceneData.instance;
            if (sd == null) return false;
            var st = sd.FindMyState(new PersistentBoolData { id = s.Key, sceneName = s.Scene });
            return st != null && st.activated;
        }

        static readonly Dictionary<string, FieldInfo> _lists = new Dictionary<string, FieldInfo>();

        static IList ListField(PlayerData pd, string name)
        {
            FieldInfo f;
            if (!_lists.TryGetValue(name, out f))
                _lists[name] = f = typeof(PlayerData).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null ? f.GetValue(pd) as IList : null;
        }

        /// <summary>Changes whenever something collectible is picked up (PlayerData side).</summary>
        public static int Stamp()
        {
            var pd = PlayerData.instance;
            if (pd == null) return 0;
            int roots = 0;
            try { var l = ListField(pd, "scenesEncounteredDreamPlantC"); roots = l != null ? l.Count : 0; } catch { }
            unchecked { return pd.grubsCollected * 7919 + pd.heartPieces * 104729 + pd.vesselFragments * 1299709 + pd.charmsOwned * 31 + roots; }
        }

        // ── rooms of one map ────────────────────────────────────────────────

        /// <summary>The rooms and areas of one GameMap instance (the clone or the real map).</summary>
        public sealed class MapIndex
        {
            public readonly GameObject Root;
            readonly Dictionary<string, Transform> _rooms = new Dictionary<string, Transform>(StringComparer.Ordinal);
            readonly Dictionary<string, string> _areas = new Dictionary<string, string>(StringComparer.Ordinal);

            public MapIndex(GameMap m)
            {
                Root = m != null ? m.gameObject : null;
                if (m == null) return;
                Add(m.areaAncientBasin, "Ancient Basin"); Add(m.areaCity, "City of Tears");
                Add(m.areaCliffs, "Howling Cliffs"); Add(m.areaCrossroads, "Forgotten Crossroads");
                Add(m.areaCrystalPeak, "Crystal Peak"); Add(m.areaDeepnest, "Deepnest");
                Add(m.areaFogCanyon, "Fog Canyon"); Add(m.areaFungalWastes, "Fungal Wastes");
                Add(m.areaGreenpath, "Greenpath"); Add(m.areaKingdomsEdge, "Kingdom's Edge");
                Add(m.areaQueensGardens, "Queen's Gardens"); Add(m.areaRestingGrounds, "Resting Grounds");
                Add(m.areaDirtmouth, "Dirtmouth"); Add(m.areaWaterways, "Royal Waterways");
            }

            void Add(GameObject area, string label)
            {
                if (area == null) return;
                var all = area.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == area.transform || _rooms.ContainsKey(t.name)) continue;
                    if (t.name.StartsWith(PinPrefix, StringComparison.Ordinal)) continue;   // our own pins
                    if (t.GetComponent<SpriteRenderer>() == null) continue;
                    _rooms[t.name] = t;
                    _areas[t.name] = label;
                }
            }

            public string AreaOf(string scene)
            {
                string a;
                return _areas.TryGetValue(scene, out a) ? a : null;
            }

            /// <summary>#47 QA: why a room does or does not take pins.</summary>
            public string Why(string scene)
            {
                Transform t;
                if (!_rooms.TryGetValue(scene, out t) || t == null) return "missing";
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) return "no-sprite";
                if (!t.gameObject.activeInHierarchy) return "inactive(" + (t.gameObject.activeSelf ? "parent" : "self") + ")";
                return "drawn";
            }

            public CollectiblePins.Room? RoomOf(string scene)
            {
                Transform t;
                if (!_rooms.TryGetValue(scene, out t) || t == null) return null;
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) return null;
                return new CollectiblePins.Room
                {
                    Transform = t,
                    SpriteSize = sr.sprite.bounds.size,
                    Shown = t.gameObject.activeInHierarchy,
                };
            }
        }

        /// <summary>Name prefix of every CollectiblePins object.</summary>
        public const string PinPrefix = "DsPin ";

        // ── #69 pins on the game's own map ──────────────────────────────────

        static CollectiblePins _livePins;
        static MapIndex _liveIndex;
        static float _nextLive;
        static int _liveLayer, _liveOrder;

        /// <summary>Pins shown on the game's own map at the last refresh.</summary>
        public static int LivePinsShown { get { return _livePins != null ? _livePins.Shown : 0; } }

        /// <summary>Keep pins on the game's own map current, as Silksong does; cheap, once a second.</summary>
        public static void TickLivePins()
        {
            var t = Table;
            if (t == null) return;
            var gm = GameManager.UnsafeInstance;
            var go = gm != null ? gm.gameMap : null;
            var map = go != null ? go.GetComponent<GameMap>() : null;
            if (map == null) return;
            bool rebound = _liveIndex == null || _liveIndex.Root != map.gameObject;
            if (!rebound && Time.unscaledTime < _nextLive) return;
            _nextLive = Time.unscaledTime + 1f;
            try
            {
                if (_livePins == null) _livePins = new CollectiblePins(t);
                if (rebound)
                {
                    _livePins.Clear();          // the old map, and its pins, went with the save
                    _liveIndex = new MapIndex(map);
                    // Above every room fill of the game's own map (its compass sits below them).
                    _liveLayer = 0; _liveOrder = int.MinValue;
                    foreach (var sr in map.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr.name.StartsWith(PinPrefix, StringComparison.Ordinal)) continue;
                        if (sr.sortingOrder >= _liveOrder) { _liveOrder = sr.sortingOrder; _liveLayer = sr.sortingLayerID; }
                    }
                    _liveOrder = _liveOrder == int.MinValue ? 0 : _liveOrder + 1;
                }
                _livePins.Refresh(_liveIndex.RoomOf, Collected, map.transform, PinSize,
                                  map.transform.position.z - 0.5f, map.gameObject.layer, _liveLayer, _liveOrder);
            }
            catch (Exception e) { Debug.LogWarning("[HkCollect] live pins: " + e.Message); _nextLive = Time.unscaledTime + 10f; }
        }

        // ── checklist ───────────────────────────────────────────────────────

        static MapIndex _areaIndex;

        /// <summary>The checklist by map area, using whichever map instance is current.</summary>
        public static List<ChecklistArea> Checklist(GameMap map, out ChecklistArea totals)
        {
            totals = new ChecklistArea("All");
            var t = Table;
            if (t == null) return new List<ChecklistArea>();
            if (map == null) { var gm = GameManager.UnsafeInstance; var go = gm != null ? gm.gameMap : null; map = go != null ? go.GetComponent<GameMap>() : null; }
            if (map != null && (_areaIndex == null || _areaIndex.Root != map.gameObject)) _areaIndex = new MapIndex(map);
            var idx = _areaIndex;
            return CollectibleChecklist.Build(t.Spots, s => { try { return Collected(s); } catch { return false; } },
                                              idx != null ? (Func<string, string>)idx.AreaOf : null, out totals);
        }
    }
}
#endif
