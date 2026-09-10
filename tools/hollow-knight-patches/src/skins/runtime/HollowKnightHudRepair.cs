using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Runtime
{
    // Pixel algorithms adapted from igawa6/dualsouls Assets/HKMods.cs, lines 421-539 and 1023-1143,
    // commit 5c22451435b772acde0c7e6456f9019bc1baef73, source-only MIT label; see NOTICE.md.
    public readonly struct SkinHudPixel
    {
        public readonly float R, G, B, A;
        public SkinHudPixel(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
    }
    public sealed class SkinHudRect
    {
        public readonly string Name;
        public readonly float U0, V0, U1, V1;
        public SkinHudRect(string name, float u0, float v0, float u1, float v1)
        { Name = name; U0 = u0; V0 = v0; U1 = u1; V1 = v1; }
    }
    public interface ISkinHudPixels
    {
        int Width { get; }
        int Height { get; }
        SkinHudPixel Sample(float u, float v);
        SkinHudPixel Pixel(int x, int y);
        void WriteRow(int x, int y, SkinHudPixel[] pixels, int count);
    }
    public static class HollowKnightHudRepair
    {
        // Includes bounded rectangle/signature metadata, one float RGBA row and Unity's conversion row.
        public const long ScratchBytes = 4L * 1024 * 1024;
        public static void Repair(ISkinHudPixels skin, ISkinHudPixels vanilla, ISkinHudPixels output, IReadOnlyList<SkinHudRect> definitions)
        {
            foreach (var pixels in new[] { skin, vanilla, output })
                if (pixels.Width < 1 || pixels.Height < 1 || pixels.Width > 8192 || pixels.Height > 8192 ||
                    (long)pixels.Width * pixels.Height > 33554432) throw new InvalidDataException("HUD pixel dimensions exceed bound.");
            if (ReferenceEquals(skin, output)) throw new InvalidOperationException("HUD repair requires a private output copy.");
            new Work(skin, vanilla, output).Run(definitions);
        }
        sealed class Work
        {
            readonly ISkinHudPixels skin, vanilla, output;
            readonly SkinHudPixel[] row;
            long remainingSamples = 33554432;
            public Work(ISkinHudPixels skin, ISkinHudPixels vanilla, ISkinHudPixels output)
            { this.skin = skin; this.vanilla = vanilla; this.output = output; row = new SkinHudPixel[output.Width]; }
            SkinHudPixel Sample(ISkinHudPixels image, float u, float v)
            {
                if (--remainingSamples < 0) throw new InvalidDataException("HUD repair sample-work bound exceeded.");
                return image.Sample(u, v);
            }
            public void Run(IReadOnlyList<SkinHudRect> definitions)
            {
                if (definitions.Count > 4096) throw new InvalidDataException("HUD definition count exceeds bound.");
                var rects = new Dictionary<string, SkinHudRect>();
                var blanks = new HashSet<string>();
                foreach (var rect in definitions)
                {
                    if (rect == null || string.IsNullOrEmpty(rect.Name) || rects.ContainsKey(rect.Name)) continue;
                    if (!(rect.U0 >= 0 && rect.V0 >= 0 && rect.U1 <= 1 && rect.V1 <= 1 && rect.U1 > rect.U0 && rect.V1 > rect.V0))
                        throw new InvalidDataException("HUD UV rectangle is invalid.");
                    rects.Add(rect.Name, rect); float alpha = 0;
                    for (int gy = 0; gy < 8; gy++) for (int gx = 0; gx < 8; gx++)
                        alpha += Sample(skin, rect.U0 + (rect.U1 - rect.U0) * (gx + 0.5f) / 8,
                            rect.V0 + (rect.V1 - rect.V0) * (gy + 0.5f) / 8).A;
                    if (alpha / 64 < 0.01f) blanks.Add(rect.Name);
                }
                foreach (var name in blanks)
                {
                    var rect = rects[name]; SkinHudRect sibling = null; int last = -1;
                    string family = name.Length > 4 ? name.Substring(0, name.Length - 4) : name;
                    if (family == "idle_v02")
                        foreach (var pair in rects)
                        {
                            if (pair.Key.Length <= 4 || blanks.Contains(pair.Key) || pair.Key.Substring(0, pair.Key.Length - 4) != "appear_v02") continue;
                            if (int.TryParse(pair.Key.Substring(pair.Key.Length - 4), out var index) && index > last) { last = index; sibling = pair.Value; }
                        }
                    if (sibling != null) CopyFitted(rect, sibling);
                    else Copy(rect, vanilla, rect);
                }
                var coins = rects.Values.Where(x => x.Name.StartsWith("HUD_coin_v02", StringComparison.Ordinal) && !blanks.Contains(x.Name)).ToList();
                if (coins.Count > 64) throw new InvalidDataException("HUD coin family count exceeds bound.");
                if (coins.Count < 3) return;
                var signatures = coins.ToDictionary(x => x, Signature);
                SkinHudRect medoid = null; float score = -2;
                foreach (var candidate in coins)
                {
                    float sum = 0;
                    foreach (var other in coins) if (!ReferenceEquals(candidate, other)) sum += Correlation(signatures[candidate], signatures[other]);
                    float mean = sum / (coins.Count - 1);
                    if (mean > score) { score = mean; medoid = candidate; }
                }
                if (medoid == null || score < 0.4f) return;
                foreach (var rect in coins)
                    if (!ReferenceEquals(rect, medoid) && Correlation(signatures[rect], signatures[medoid]) < 0.55f) Copy(rect, skin, medoid);
            }
            float[] Signature(SkinHudRect rect)
            {
                var result = new float[12 * 12 * 4]; int k = 0;
                for (int gy = 0; gy < 12; gy++) for (int gx = 0; gx < 12; gx++)
                {
                    var p = Sample(skin, rect.U0 + (rect.U1 - rect.U0) * (gx + 0.5f) / 12,
                        rect.V0 + (rect.V1 - rect.V0) * (gy + 0.5f) / 12);
                    result[k++] = p.R * p.A; result[k++] = p.G * p.A; result[k++] = p.B * p.A; result[k++] = p.A;
                }
                return result;
            }
            static float Correlation(float[] a, float[] b)
            {
                float ma = 0, mb = 0; for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
                ma /= a.Length; mb /= b.Length; float num = 0, da = 0, db = 0;
                for (int i = 0; i < a.Length; i++) { float x = a[i] - ma, y = b[i] - mb; num += x * y; da += x * x; db += y * y; }
                return num / ((float)Math.Sqrt(da * db) + 1e-6f);
            }
            static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
            static (int X, int Y, int W, int H) Bounds(ISkinHudPixels image, SkinHudRect rect)
            {
                int x = Clamp((int)Math.Floor(rect.U0 * image.Width), 0, image.Width - 1);
                int y = Clamp((int)Math.Floor(rect.V0 * image.Height), 0, image.Height - 1);
                int x1 = Clamp((int)Math.Ceiling(rect.U1 * image.Width), x + 1, image.Width);
                int y1 = Clamp((int)Math.Ceiling(rect.V1 * image.Height), y + 1, image.Height);
                return (x, y, x1 - x, y1 - y);
            }
            void Copy(SkinHudRect destination, ISkinHudPixels source, SkinHudRect src)
            {
                var b = Bounds(output, destination);
                for (int y = 0; y < b.H; y++)
                {
                    for (int x = 0; x < b.W; x++) row[x] = Sample(source, src.U0 + (src.U1 - src.U0) * (x + 0.5f) / b.W,
                        src.V0 + (src.V1 - src.V0) * (y + 0.5f) / b.H);
                    output.WriteRow(b.X, b.Y + y, row, b.W);
                }
            }
            (float U0, float V0, float U1, float V1) AlphaBox(ISkinHudPixels image, SkinHudRect rect)
            {
                var b = Bounds(image, rect); int minX = b.W, minY = b.H, maxX = -1, maxY = -1;
                for (int y = 0; y < b.H; y++) for (int x = 0; x < b.W; x++)
                {
                    if (--remainingSamples < 0) throw new InvalidDataException("HUD repair sample-work bound exceeded.");
                    if (image.Pixel(b.X + x, b.Y + y).A > 0.06f)
                    { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                }
                return maxX < 0 ? (0, 0, 1, 1) : ((float)minX / b.W, (float)minY / b.H, (float)(maxX + 1) / b.W, (float)(maxY + 1) / b.H);
            }
            void CopyFitted(SkinHudRect destination, SkinHudRect source)
            {
                var d = AlphaBox(vanilla, destination); var s = AlphaBox(skin, source); var b = Bounds(output, destination);
                float dw = (d.U1 - d.U0) * b.W, dh = (d.V1 - d.V0) * b.H;
                float sw = (source.U1 - source.U0) * skin.Width * (s.U1 - s.U0);
                float sh = (source.V1 - source.V0) * skin.Height * (s.V1 - s.V0);
                float aspect = sw / Math.Max(1e-4f, sh), width = dw, height = dw / Math.Max(1e-4f, aspect);
                if (height > dh) { height = dh; width = dh * aspect; }
                float x0 = d.U0 * b.W + (dw - width) * 0.5f, y0 = d.V0 * b.H + (dh - height) * 0.5f;
                for (int y = 0; y < b.H; y++)
                {
                    for (int x = 0; x < b.W; x++)
                    {
                        float fx = (x + 0.5f - x0) / Math.Max(1e-4f, width), fy = (y + 0.5f - y0) / Math.Max(1e-4f, height);
                        row[x] = fx < 0 || fx >= 1 || fy < 0 || fy >= 1 ? default : Sample(skin,
                            source.U0 + (source.U1 - source.U0) * (s.U0 + (s.U1 - s.U0) * fx),
                            source.V0 + (source.V1 - source.V0) * (s.V0 + (s.V1 - s.V0) * fy));
                    }
                    output.WriteRow(b.X, b.Y + y, row, b.W);
                }
            }
        }
    }
}
