using UnityEngine;

// [COMPANION] Chrome that the game itself cannot supply.
//
// Most of the bottom-screen art is a REGENERABLE cache in persistentDataPath: the tab fleurs are cut
// out of HK's Menu atlas by TryBakeTabFleurs and the logo comes from a loaded texture, so deleting
// them costs one re-bake. The context-box SEPARATOR has no such source, and until 1.0.0 it shipped
// as a base64 PNG baked into this file — art traced from an exported game atlas.
//
// That made the build carry Team Cherry pixels (they also ended up as a literal inside
// global-metadata.dat), which is exactly what the port must not redistribute. The divider is now
// DRAWN AT RUNTIME from primitives: a hairline rule that fades out at both ends, a gap in the
// middle, and a small diamond ornament with flanking dots. It is original art, it costs one
// 1170x37 texture at startup, and it is still written to the normal cache path on first use, so
// dropping a hand-made hkds_sep_top.png in persistentDataPath overrides it exactly as before.
public static class HKEmbedded
{
    const int SEP_W = 1170, SEP_H = 37;

    // Called by BuildSeparator when no cached PNG exists. Only the two separator names are served;
    // everything else has a real baker (fleurs, logo) and must return null so the caller falls
    // through to it.
    public static Texture2D LoadTexture(string cacheName)
    {
        if (cacheName != "hkds_sep_top" && cacheName != "hkds_sep_bot") return null;
        try { return BuildDivider(); }
        catch (System.Exception e) { Debug.Log($"HKEmbedded: divider build failed {e.Message}"); return null; }
    }

    // White-on-transparent so the caller can tint it. Drawn in linear alpha; the shapes are kept
    // deliberately plain — this is a rule, not an emblem, and it sits under a busy HUD strip.
    static Texture2D BuildDivider()
    {
        int w = SEP_W, h = SEP_H, mid = h / 2, cx = w / 2;
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);

        const float endFade = 0.14f;   // fraction of the width spent fading in from each end
        int gap = 26;                  // clear space either side of the centre ornament

        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);
            float a = Mathf.Clamp01(Mathf.Min(t, 1f - t) / endFade);
            a = a * a * (3f - 2f * a);                       // smoothstep — no hard start
            int d = Mathf.Abs(x - cx);
            if (d < gap) a *= Mathf.Clamp01((d - 6f) / (gap - 6f));   // open a gap for the diamond
            if (a <= 0f) continue;
            Put(px, w, h, x, mid, a);
            Put(px, w, h, x, mid - 1, a * 0.35f);            // soft shoulders, 1px each side
            Put(px, w, h, x, mid + 1, a * 0.35f);
        }

        // centre diamond
        const int r = 7;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float m = (Mathf.Abs(dx) / (float)r) + (Mathf.Abs(dy) / (float)(r - 2));
                if (m > 1f) continue;
                Put(px, w, h, cx + dx, mid + dy, m > 0.72f ? 1f : 0.55f);   // outlined, softer core
            }

        // flanking dots
        for (int s = -1; s <= 1; s += 2)
            for (int k = 1; k <= 2; k++)
            {
                int ox = cx + s * (r + 9 + (k - 1) * 9);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (Mathf.Abs(dx) + Mathf.Abs(dy) <= 1)
                            Put(px, w, h, ox + dx, mid + dy, k == 1 ? 0.9f : 0.5f);
            }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HKDS_sep" };
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }

    // Additive-max write so overlapping shapes don't darken each other's edges.
    static void Put(Color32[] px, int w, int h, int x, int y, float a)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        int i = y * w + x;
        byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
        if (v > px[i].a) px[i].a = v;
    }
}
