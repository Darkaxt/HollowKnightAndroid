using System;
using System.Collections.Generic;
using System.Globalization;

namespace DualSouls.Mods
{
    /// <summary>
    /// #37 one collectible's place in the world, from the generated table of each
    /// game (tools/collectibles). <see cref="Key"/> is the save key: "pd:&lt;field&gt;"
    /// is a PlayerData bool shared by every spot that names it, "pdlist:&lt;field&gt;"
    /// a PlayerData scene list, anything else is the id of the pickup's persistent
    /// bool in <see cref="Scene"/>.
    /// </summary>
    public sealed class CollectibleSpot
    {
        public CollectibleSpot(string scene, string kind, string key, float x, float y)
        {
            Scene = scene; Kind = kind; Key = key; X = x; Y = y;
        }

        public string Scene { get; }
        public string Kind { get; }
        public string Key { get; }
        public float X { get; }
        public float Y { get; }

        public bool IsPlayerDataKey { get { return Key.StartsWith("pd:", StringComparison.Ordinal); } }
        public string PlayerDataField { get { return IsPlayerDataKey ? Key.Substring(3) : null; } }

        /// <summary>"pdlist:&lt;field&gt;": collected once the PlayerData string list holds this spot's scene.</summary>
        public string PlayerDataList { get { return Key.StartsWith("pdlist:", StringComparison.Ordinal) ? Key.Substring(7) : null; } }

        /// <summary>One identity per collectible: a PlayerData key may be placed in several scene variants.</summary>
        public string Identity { get { return IsPlayerDataKey ? Key : Scene + "/" + Kind + "/" + Key; } }
    }

    /// <summary>
    /// #37 the generated collectible table: "S|scene|width|height" gives a room's
    /// tilemap size and "P|scene|kind|key|x|y" one collectible.
    /// </summary>
    public sealed class CollectibleTable
    {
        readonly Dictionary<string, KeyValuePair<float, float>> _sizes = new Dictionary<string, KeyValuePair<float, float>>(StringComparer.Ordinal);
        readonly List<CollectibleSpot> _spots = new List<CollectibleSpot>();

        public IReadOnlyList<CollectibleSpot> Spots { get { return _spots; } }

        public static CollectibleTable Parse(string text)
        {
            var t = new CollectibleTable();
            if (string.IsNullOrEmpty(text)) return t;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('|');
                if (f[0] == "S" && f.Length == 4)
                {
                    t._sizes[f[1]] = new KeyValuePair<float, float>(Num(f[2]), Num(f[3]));
                }
                else if (f[0] == "P" && f.Length == 6)
                {
                    t._spots.Add(new CollectibleSpot(f[1], f[2], f[3], Num(f[4]), Num(f[5])));
                }
                else
                {
                    throw new FormatException("Bad collectible table line: " + line);
                }
            }
            return t;
        }

        static float Num(string s) { return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }

        public bool TrySceneSize(string scene, out float width, out float height)
        {
            KeyValuePair<float, float> s;
            if (_sizes.TryGetValue(scene, out s) && s.Key > 0f && s.Value > 0f)
            {
                width = s.Key; height = s.Value; return true;
            }
            width = height = 0f;
            return false;
        }

        /// <summary>
        /// The spot's offset from the centre of its room sprite, in the sprite's
        /// units: the formula GameMap uses for the player's compass (both games).
        /// </summary>
        public bool TryRoomOffset(CollectibleSpot spot, float spriteWidth, float spriteHeight, out float x, out float y)
        {
            float w, h;
            if (spot == null || !TrySceneSize(spot.Scene, out w, out h) || spriteWidth <= 0f || spriteHeight <= 0f)
            {
                x = y = 0f;
                return false;
            }
            x = -spriteWidth / 2f + spot.X / w * spriteWidth;
            y = -spriteHeight / 2f + spot.Y / h * spriteHeight;
            return true;
        }
    }

    /// <summary>One area's line of the #37 checklist: missing and total per kind.</summary>
    public sealed class ChecklistArea
    {
        readonly Dictionary<string, int[]> _counts = new Dictionary<string, int[]>(StringComparer.Ordinal);

        public ChecklistArea(string name) { Name = name; }

        public string Name { get; }
        public int Missing { get; private set; }

        public int MissingOf(string kind) { int[] c; return _counts.TryGetValue(kind, out c) ? c[0] : 0; }
        public int TotalOf(string kind) { int[] c; return _counts.TryGetValue(kind, out c) ? c[1] : 0; }

        internal void Add(string kind, bool collected)
        {
            int[] c;
            if (!_counts.TryGetValue(kind, out c)) _counts[kind] = c = new int[2];
            c[1]++;
            if (!collected) { c[0]++; Missing++; }
        }
    }

    /// <summary>#37 the completion checklist: what is still missing, by area, in table order.</summary>
    public static class CollectibleChecklist
    {
        public const string OtherArea = "Elsewhere";

        /// <param name="areaOf">The area a scene belongs to; null or empty puts it under <see cref="OtherArea"/>.</param>
        public static List<ChecklistArea> Build(IEnumerable<CollectibleSpot> spots, Func<CollectibleSpot, bool> collected,
                                                Func<string, string> areaOf, out ChecklistArea totals)
        {
            if (spots == null) throw new ArgumentNullException(nameof(spots));
            if (collected == null) throw new ArgumentNullException(nameof(collected));
            totals = new ChecklistArea("All");
            var areas = new List<ChecklistArea>();
            var byName = new Dictionary<string, ChecklistArea>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in spots)
            {
                if (!seen.Add(s.Identity)) continue;
                string name = areaOf != null ? areaOf(s.Scene) : null;
                if (string.IsNullOrEmpty(name)) name = OtherArea;
                ChecklistArea a;
                if (!byName.TryGetValue(name, out a)) { byName[name] = a = new ChecklistArea(name); areas.Add(a); }
                bool got = collected(s);
                a.Add(s.Kind, got);
                totals.Add(s.Kind, got);
            }
            return areas;
        }
    }
}
