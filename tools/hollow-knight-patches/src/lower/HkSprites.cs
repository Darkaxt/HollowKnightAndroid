// HkSprites — finds the game's own sprites by asset name for tab art.
//
// One resident-object scan answers every name requested so far; until a
// name resolves, scans repeat at most every five seconds, and stop after two
// minutes so an absent asset cannot keep costing a full sprite walk.

using System.Collections.Generic;
using UnityEngine;

namespace HollowKnightPatches.Lower
{
    static class HkSprites
    {
        const float RescanSeconds = 5f;
        const int MaxScans = 24;
        static int _scans;
        static readonly Dictionary<string, Sprite> Found = new Dictionary<string, Sprite>();
        static readonly HashSet<string> Wanted = new HashSet<string>();
        static float _nextScan;

        public static Sprite Find(string name)
        {
            Sprite sprite;
            if (Found.TryGetValue(name, out sprite) && sprite != null) return sprite;
            Wanted.Add(name);
            if (Time.unscaledTime < _nextScan || _scans >= MaxScans) return null;
            _scans++;
            _nextScan = Time.unscaledTime + RescanSeconds;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Sprite>();
                for (int i = 0; i < all.Length; i++)
                {
                    var s = all[i];
                    if (s == null || !Wanted.Contains(s.name) || s.rect.width <= 0f || s.rect.height <= 0f) continue;
                    Sprite known;
                    if (!Found.TryGetValue(s.name, out known) || known == null) Found[s.name] = s;
                }
            }
            catch { }
            return Found.TryGetValue(name, out sprite) ? sprite : null;
        }

        /// <summary>Resident sprite names containing <paramref name="part"/>, for the QA command channel.</summary>
        public static string List(string part, int max)
        {
            var names = new SortedSet<string>();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && s.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) names.Add(s.name);
            var sb = new System.Text.StringBuilder();
            sb.Append(names.Count).Append(" match");
            int n = 0;
            foreach (var name in names) { if (n++ == max) { sb.Append(" ..."); break; } sb.Append(' ').Append(name); }
            return sb.ToString();
        }
    }
}
