// HkTabGlyph — turns Hollow Knight's coloured tab sprites into white glyphs.
//
// Silksong's tab bar uses the game's own white inventory glyphs; Hollow
// Knight has no such set, so its tabs borrow item art (nail, charm, quill...).
// The shared bar only tints icons (white when selected, dim otherwise), and a
// tint cannot turn colour into white. This makes a monochrome copy once per
// source sprite: alpha is kept, colour becomes a light grey ramp of the
// original brightness, so interior detail survives as shading and the shared
// tint then behaves exactly as it does on Silksong's glyphs.
//
// Game textures are not CPU-readable, so the sprite's region is copied through
// a RenderTexture. Any failure falls back to the original sprite.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HollowKnightPatches.Lower
{
    static class HkTabGlyph
    {
        // Darkest shade the ramp maps to; 1 is plain white.
        const float Floor = 0.62f;

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

        static Sprite Make(Sprite sprite)
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

                var px = tex.GetPixels32();
                float lo = 1f, hi = 0f;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a < 32) continue;
                    float l = Luma(px[i]);
                    if (l < lo) lo = l;
                    if (l > hi) hi = l;
                }
                float span = hi - lo > 0.05f ? hi - lo : 1f;
                for (int i = 0; i < px.Length; i++)
                {
                    float t = Mathf.Clamp01((Luma(px[i]) - lo) / span);
                    byte v = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(Floor, 1f, t));
                    px[i] = new Color32(v, v, v, px[i].a);
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);

                var made = Sprite.Create(tex, new Rect(0, 0, w, h),
                                         new Vector2(sprite.pivot.x / r.width, sprite.pivot.y / r.height),
                                         sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                made.name = sprite.name + "_glyph";
                tex = null;
                return made;
            }
            catch (Exception) { return null; }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        static float Luma(Color32 c) { return (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f; }
    }
}
