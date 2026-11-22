using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DualSouls.Skins.Runtime;
using Xunit;

// #63: a pack covering every collection is far larger than the memory budget, so both games decode only the
// targets discovery finds live, and each refresh follows the scene.
public sealed class SkinLiveOnlyDecodeTests
{
    const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==";

    [Fact]
    public void Only_live_targets_are_decoded_and_refresh_follows_the_scene()
    {
        var rig = new Rig(1024 * 1024);
        var hero = rig.Add("hero.png");
        var boss = rig.Add("boss.png", alive: false);
        var pack = rig.Pack("hero.png", "boss.png");

        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(pack).Status);
        Assert.Equal(1, rig.Decoder.Decoded);
        Assert.NotSame(hero.Original, hero.Value);
        Assert.Same(boss.Original, boss.Value);

        // The boss collection loads: only its texture is decoded, the hero texture is reused.
        boss.Alive = true;
        var heroSkin = hero.Value;
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.Refresh().Status);
        Assert.Equal(2, rig.Decoder.Decoded);
        Assert.Same(heroSkin, hero.Value);
        Assert.NotSame(boss.Original, boss.Value);

        // The boss leaves the scene: its decoded texture is released, the hero keeps its skin.
        boss.Alive = false;
        rig.Session.Refresh();
        Assert.Equal(1, rig.Decoder.Released);
        Assert.Same(heroSkin, hero.Value);
        Assert.Equal(2, rig.Decoder.Decoded);
    }

    [Fact]
    public void A_full_budget_applies_the_highest_priority_targets_and_reports_the_rest()
    {
        // Each 1x1 target costs its encoded length plus 8 accounted bytes: room for exactly one.
        long one = Convert.FromBase64String(Png).Length + 8;
        var rig = new Rig(one + one / 2, target => target == "hero.png" ? 0 : 1);
        var world = rig.Add("world.png");
        var hero = rig.Add("hero.png");

        var result = rig.Session.TryApply(rig.Pack("world.png", "hero.png"));

        Assert.Equal(SkinApplyStatus.Applied, result.Status);
        Assert.NotSame(hero.Original, hero.Value);
        Assert.Same(world.Original, world.Value);
        Assert.Equal(new[] { "world.png" }, rig.Session.BudgetSkipped);
        Assert.Contains("Memory budget full", result.Detail);
    }

    sealed class Rig
    {
        public readonly CountingDecoder Decoder = new CountingDecoder();
        public readonly SkinRuntimeSession Session;
        readonly List<Slot> slots = new List<Slot>();
        readonly string root = Path.Combine(AppContext.BaseDirectory, "runtime-fixtures", Guid.NewGuid().ToString("N"));

        public Rig(long budget, Func<string, int> order = null)
        {
            Directory.CreateDirectory(root);
            var rules = new SkinRuntimeRules("live", 8, _ => true, (_, __) => true, order ?? (_ => 0));
            Session = new SkinRuntimeSession(Decoder, () => slots.Select(x => x.Binding()).ToList(), budget, rules, liveOnly: true);
        }

        public Slot Add(string target, bool alive = true)
        {
            File.WriteAllBytes(Path.Combine(root, target), Convert.FromBase64String(Png));
            var slot = new Slot(target) { Alive = alive };
            slots.Add(slot);
            return slot;
        }

        public SkinPack Pack(params string[] targets) =>
            new SkinPack("pack", root, targets.ToDictionary(x => x, x => x));
    }

    sealed class Slot
    {
        readonly string target;
        public readonly object Original = new object();
        public object Value;
        public bool Alive;
        public Slot(string target) { this.target = target; Value = Original; }
        public SkinSlot Binding() => new SkinSlot(this, "mainTexture", target, () => Alive, () => Value,
            value => Value = value, (texture, _) => texture.Value);
    }

    sealed class CountingDecoder : ISkinTextureDecoder
    {
        public int Decoded, Released;
        public SkinTexture Decode(byte[] bytes, int width, int height)
        {
            Decoded++;
            bool released = false;
            return new SkinTexture(new object(), width, height, () => { released = true; Released++; }, () => released);
        }
    }
}
