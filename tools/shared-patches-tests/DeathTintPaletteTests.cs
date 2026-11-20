using DualSouls.Mods;
using Xunit;

/// <summary>#24: each death draws a visibly different, never-repeating hero tint.</summary>
public sealed class DeathTintPaletteTests
{
    [Fact]
    public void ConsecutiveVariantsNeverRepeatTheirHue()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var palette = new DeathTintPalette(seed);
            palette.Next(out _, out _, out _);
            float previous = palette.Hue;
            for (int death = 0; death < 50; death++)
            {
                palette.Next(out _, out _, out _);
                Assert.True(DeathTintPalette.HueDistance(previous, palette.Hue) >= DeathTintPalette.MinHueGap - 1e-4f,
                    $"seed {seed} death {death}: {previous} -> {palette.Hue}");
                previous = palette.Hue;
            }
        }
    }

    [Fact]
    public void FiveDeathsGiveFiveDistinctBrightTints()
    {
        var palette = new DeathTintPalette(12345);
        var seen = new System.Collections.Generic.List<(float r, float g, float b)>();
        for (int death = 0; death < 5; death++)
        {
            palette.Next(out float r, out float g, out float b);
            Assert.InRange(palette.Saturation, DeathTintPalette.MinSaturation, DeathTintPalette.MaxSaturation);
            // Value 1: the strongest channel is full, so the multiply recolours without blacking out.
            Assert.Equal(1f, System.Math.Max(r, System.Math.Max(g, b)), 3);
            Assert.DoesNotContain((r, g, b), seen);
            seen.Add((r, g, b));
        }
        Assert.Equal(5, palette.Deaths);
    }

    [Fact]
    public void TheSameSessionSeedReplaysTheSameSequence()
    {
        var a = new DeathTintPalette(7);
        var b = new DeathTintPalette(7);
        for (int death = 0; death < 10; death++)
        {
            a.Next(out float ar, out float ag, out float ab);
            b.Next(out float br, out float bg, out float bb);
            Assert.Equal((ar, ag, ab), (br, bg, bb));
        }
    }
}
