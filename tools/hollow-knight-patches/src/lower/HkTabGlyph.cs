// HkTabGlyph — turns Hollow Knight's coloured tab sprites into white glyphs.
//
// Silksong's tab bar uses the game's own white inventory glyphs; Hollow
// Knight has no such set, so its tabs borrow item art (nail, charm, quill...).
// The shared bar only tints icons (white when selected, dim otherwise), and a
// tint cannot turn colour into white. This makes a white copy once per source
// sprite, so the shared tint then behaves exactly as it does on Silksong's
// glyphs. Darker parts of the art can be cut out (see CutLow/CutHigh) so
// interior detail survives as see-through shapes, like Silksong's glyphs.
//
// Game textures are not CPU-readable, so the sprite's region is copied through
// a RenderTexture. Any failure falls back to the original sprite.
//
// QA: "tabart" reports the settings, "tabart dump" writes each source sprite
// and its glyph as PNG files, "tabart set <lo> <hi> <minKept>" changes the cut
// live (the bar picks up the new glyphs within a second).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HollowKnightPatches.Lower
{
    static class HkTabGlyph
    {
        // Normalised brightness below CutLow is cut out, above CutHigh is solid.
        // Tuned on the Thor against the five real tab sprites (#68).
        static float CutLow = 0.10f, CutHigh = 0.25f;
        // Keep the shape solid if cutting would leave less than this share.
        static float MinKept = 0.25f;

        static readonly Dictionary<Sprite, Sprite> Made = new Dictionary<Sprite, Sprite>();

        public static Func<Sprite> Of(Func<Sprite> source)
        {
            return () =>
            {
                var sprite = source();
                if (sprite == null) return null;
                Sprite glyph;
                if (!Made.TryGetValue(sprite, out glyph))
                {
                    glyph = Make(sprite) ?? sprite;
                    Made[sprite] = glyph;
                }
                return glyph;
            };
        }

        public static string Command(string[] a)
        {
            string settings = "cut=" + CutLow + ".." + CutHigh + " minKept=" + MinKept + " glyphs=" + Made.Count;
            if (a.Length >= 2 && a[1] == "dump") return Dump() + " " + settings;
            if (a.Length >= 5 && a[1] == "set")
            {
                float lo, hi, kept;
                if (!float.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lo) ||
                    !float.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out hi) ||
                    !float.TryParse(a[4], NumberStyles.Float, CultureInfo.InvariantCulture, out kept))
                    return "error tabart set <lo> <hi> <minKept>";
                CutLow = lo; CutHigh = hi; MinKept = kept;
                foreach (var kv in Made)
                    if (kv.Value != null && kv.Value != kv.Key) UnityEngine.Object.Destroy(kv.Value.texture);
                Made.Clear();
                return "ok cut=" + CutLow + ".." + CutHigh + " minKept=" + MinKept;
            }
            return settings;
        }

        static string Dump()
        {
            string dir = Path.Combine(Application.persistentDataPath, "qa-tabart");
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (var kv in Made)
            {
                var src = Copy(kv.Key);
                if (src == null) continue;
                try { File.WriteAllBytes(Path.Combine(dir, Safe(kv.Key.name) + ".png"), src.EncodeToPNG()); n++; }
                finally { UnityEngine.Object.Destroy(src); }
                if (kv.Value != null && kv.Value != kv.Key && kv.Value.texture != null)
                    File.WriteAllBytes(Path.Combine(dir, Safe(kv.Key.name) + "_glyph.png"), kv.Value.texture.EncodeToPNG());
            }
            return "ok dumped=" + n + " dir=" + dir;
        }

        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // A CPU-readable RGBA copy of the sprite's region, or null.
        static Texture2D Copy(Sprite sprite)
        {
            var src = sprite.texture;
            if (src == null) return null;
            Rect r = sprite.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            if (w <= 0 || h <= 0) return null;

            var previous = RenderTexture.active;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt,
                              new Vector2(r.width / src.width, r.height / src.height),
                              new Vector2(r.x / src.width, r.y / src.height));
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                    { name = "dualsouls_tab_glyph", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false);
                var done = tex;
                tex = null;
                return done;
            }
            catch (Exception) { return null; }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        static Sprite Make(Sprite sprite)
        {
            var tex = Copy(sprite);
            if (tex == null) return null;
            try
            {
                Rect r = sprite.textureRect;
                var px = tex.GetPixels32();
                // Brightness range of the visible pixels, ignoring the outer 5%
                // at each end so a few highlights or outlines don't set it.
                var lum = new List<float>(px.Length);
                for (int i = 0; i < px.Length; i++)
                    if (px[i].a >= 32) lum.Add(Luma(px[i]));
                if (lum.Count == 0) return null;
                lum.Sort();
                float lo = lum[lum.Count / 20], hi = lum[lum.Count - 1 - lum.Count / 20];
                float span = hi - lo > 0.05f ? hi - lo : 1f;

                // White shape; the darker parts of the art become see-through.
                // If that would erase most of the shape, keep it solid instead.
                var alpha = new byte[px.Length];
                int kept = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    float t = Mathf.Clamp01((Luma(px[i]) - lo) / span);
                    float a = px[i].a * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CutLow, CutHigh, t));
                    alpha[i] = (byte)Mathf.RoundToInt(a);
                    if (px[i].a >= 32 && alpha[i] >= 128) kept++;
                }
                bool cut = kept >= lum.Count * MinKept;
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32(255, 255, 255, cut ? alpha[i] : px[i].a);
                tex.SetPixels32(px);
                tex.Apply(false);

                var made = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                         new Vector2(sprite.pivot.x / r.width, sprite.pivot.y / r.height),
                                         sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                made.name = sprite.name + "_glyph";
                tex = null;
                return made;
            }
            catch (Exception) { return null; }
            finally { if (tex != null) UnityEngine.Object.Destroy(tex); }
        }

        static float Luma(Color32 c) { return (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f; }
    }
}
