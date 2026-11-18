// SsCollectibles — #37 collectible pins and the completion checklist for Silksong.
//
// The table (SsCollectibleData) is generated from the game's own scenes by
// tools/collectibles. Pins hang on the GameMapScene rooms of the game's LIVE
// map (DsMapView renders that map with its own cameras), so they follow its
// reveal: a room the player has not mapped shows none. Being on the live map,
// they also show in the game's own map on the main screen.
//
// Pickups saved as scene bools are written to SceneData when the player leaves
// the room, so their pin goes then; PlayerData items (fleas, some tools) go at
// once.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using DualSouls.Mods;
using UnityEngine;

static class SsCollectibles
{
    public static readonly KeyValuePair<string, string>[] Kinds =
    {
        new KeyValuePair<string, string>("mask", "Mask shards"),
        new KeyValuePair<string, string>("spool", "Spool fragments"),
        new KeyValuePair<string, string>("tool", "Tools"),
        new KeyValuePair<string, string>("locket", "Memory lockets"),
        new KeyValuePair<string, string>("relic", "Relics"),
        new KeyValuePair<string, string>("flea", "Fleas"),
    };

    static CollectibleTable _table;
    static bool _tableFailed;

    public static CollectibleTable Table
    {
        get
        {
            if (_table == null && !_tableFailed)
            {
                try { _table = CollectibleTable.Parse(SsCollectibleData.Table); }
                catch (Exception e) { _tableFailed = true; Debug.LogWarning("[SsCollect] table: " + e.Message); }
            }
            return _table;
        }
    }

    public static float PinSize { get { return DsConfig.Int("pin_size_pct", 45) / 100f; } }

    public static bool Collected(CollectibleSpot s)
    {
        string field = s.PlayerDataField;
        if (field != null)
        {
            var pd = PlayerData.instance;
            return pd != null && pd.GetBool(field);
        }
        var sd = SceneData.instance;
        return sd != null && sd.PersistentBools.GetValueOrDefault(s.Scene, s.Key);
    }

    // ── rooms of one map ────────────────────────────────────────────────────

    public sealed class MapIndex
    {
        public readonly GameMap Map;
        readonly Dictionary<string, GameMapScene> _rooms = new Dictionary<string, GameMapScene>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _areas = new Dictionary<string, string>(StringComparer.Ordinal);

        public MapIndex(GameMap map)
        {
            Map = map;
            if (map == null) return;
            var root = map.transform;
            var all = map.GetComponentsInChildren<GameMapScene>(true);
            var areaNames = new HashSet<string>();
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null || _rooms.ContainsKey(r.Name)) continue;
                _rooms[r.Name] = r;
                // The area is the room's ancestor right under the map root.
                var t = r.transform;
                while (t.parent != null && t.parent != root) t = t.parent;
                string area = t.parent == root ? AreaLabel(t.name) : null;
                if (area != null) { _areas[r.Name] = area; areaNames.Add(t.name); }
            }
            Debug.Log("[SsCollect] map index: " + _rooms.Count + " rooms in areas " + string.Join(",", new List<string>(areaNames).ToArray()));
        }

        public string AreaOf(string scene)
        {
            string a;
            return _areas.TryGetValue(scene, out a) ? a : null;
        }

        public CollectiblePins.Room? RoomOf(string scene)
        {
            GameMapScene r;
            if (!_rooms.TryGetValue(scene, out r) || r == null) return null;
            var sprite = r.BoundsSprite;
            if (sprite == null) return null;
            return new CollectiblePins.Room
            {
                Transform = r.transform,
                SpriteSize = sprite.bounds.size,
                Shown = r.IsMapped && r.gameObject.activeInHierarchy,
            };
        }
    }

    /// <summary>A readable label from the map's zone object name ("Bone_Forest" → "Bone Forest").</summary>
    static string AreaLabel(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return null;
        string n = objectName.Replace('_', ' ').Trim();
        if (n.StartsWith("Zone ", StringComparison.OrdinalIgnoreCase)) n = n.Substring(5);
        if (n.StartsWith("Area ", StringComparison.OrdinalIgnoreCase)) n = n.Substring(5);
        return n.Length > 0 ? n : null;
    }

    // ── checklist ───────────────────────────────────────────────────────────

    static MapIndex _areaIndex;

    public static List<ChecklistArea> Checklist(out ChecklistArea totals)
    {
        totals = new ChecklistArea("All");
        var t = Table;
        if (t == null) return new List<ChecklistArea>();
        GameMap map = null;
        try { var gm = GameManager.instance; if (gm != null) map = gm.gameMap; } catch { }
        if (map != null && (_areaIndex == null || !ReferenceEquals(_areaIndex.Map, map))) _areaIndex = new MapIndex(map);
        var idx = map != null ? _areaIndex : null;
        return CollectibleChecklist.Build(t.Spots, s => { try { return Collected(s); } catch { return false; } },
                                          idx != null ? (Func<string, string>)idx.AreaOf : null, out totals);
    }

    // ── pins on the live map ────────────────────────────────────────────────

    static CollectiblePins _pins;
    static MapIndex _pinIndex;
    static float _nextPins;
    static FieldInfo _fiCompass;

    public static int PinsShown { get { return _pins != null ? _pins.Shown : 0; } }

    /// <summary>Keep the pins on the live map current; cheap, once a second.</summary>
    public static void TickPins(GameMap map, int layer)
    {
        var t = Table;
        if (t == null || map == null) return;
        bool rebound = _pinIndex == null || !ReferenceEquals(_pinIndex.Map, map);
        if (!rebound && Time.unscaledTime < _nextPins) return;
        _nextPins = Time.unscaledTime + 1f;
        try
        {
            if (_pins == null) _pins = new CollectiblePins(t);
            if (rebound)
            {
                _pins.Clear();          // the old map, and its pins, went with the scene
                _pinIndex = new MapIndex(map);
            }
            int sortLayer = 0, sortOrder = 0;
            if (_fiCompass == null) _fiCompass = typeof(GameMap).GetField("compassIcon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var compass = _fiCompass != null ? _fiCompass.GetValue(map) as GameObject : null;
            var cr = compass != null ? compass.GetComponentInChildren<SpriteRenderer>(true) : null;
            if (cr != null) { sortLayer = cr.sortingLayerID; sortOrder = cr.sortingOrder; }
            // The compass's own depth: map-local z -1, in the decorator camera's slice.
            float z = map.transform.TransformPoint(new Vector3(0f, 0f, -1f)).z;
            _pins.Refresh(_pinIndex.RoomOf, Collected, map.transform, PinSize, z, layer, sortLayer, sortOrder);
        }
        catch (Exception e) { Debug.LogWarning("[SsCollect] pins: " + e.Message); _nextPins = Time.unscaledTime + 10f; }
    }
}
#endif
