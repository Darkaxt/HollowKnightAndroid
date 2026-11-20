using System;

namespace DualSouls.Mods
{
    /// <summary>
    /// #24 death recolour variants. Each death draws a new hero tint from a pseudorandom
    /// palette seeded by the session and the death number, never repeating the previous
    /// variant: its hue is at least <see cref="MinHueGap"/> round the colour wheel away.
    /// Tints stay bright (value 1) so the multiply tint recolours rather than darkens.
    /// </summary>
    public sealed class DeathTintPalette
    {
        public const float MinHueGap = 0.2f;
        public const float MinSaturation = 0.45f, MaxSaturation = 0.75f;

        readonly int _sessionSeed;
        public int Deaths { get; private set; }
        /// <summary>Current variant hue in [0, 1), or -1 before the first variant.</summary>
        public float Hue { get; private set; } = -1f;
        public float Saturation { get; private set; }

        public DeathTintPalette(int sessionSeed) { _sessionSeed = sessionSeed; }

        /// <summary>Draws the next variant, returned as RGB in [0, 1].</summary>
        public void Next(out float r, out float g, out float b)
        {
            Deaths++;
            var random = new Random(unchecked(_sessionSeed * 486187739 + Deaths));
            float hue;
            if (Hue < 0f) hue = (float)random.NextDouble();
            else
            {
                // Uniform over the arc that keeps MinHueGap from the previous hue on both sides.
                float offset = MinHueGap + (float)random.NextDouble() * (1f - 2f * MinHueGap);
                hue = Repeat(Hue + offset);
            }
            Hue = hue;
            Saturation = MinSaturation + (float)random.NextDouble() * (MaxSaturation - MinSaturation);
            HsvToRgb(Hue, Saturation, 1f, out r, out g, out b);
        }

        public static float HueDistance(float a, float b)
        {
            float d = Math.Abs(a - b) % 1f;
            return Math.Min(d, 1f - d);
        }

        static float Repeat(float value) { value %= 1f; return value < 0f ? value + 1f : value; }

        static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            float sector = h * 6f;
            int i = (int)Math.Floor(sector) % 6;
            float f = sector - (float)Math.Floor(sector);
            float p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }
    }
}
