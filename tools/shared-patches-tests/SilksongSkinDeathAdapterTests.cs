using System;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SilksongSkinDeathAdapterTests
{
    [Fact]
    public void Strict_normal_policy_excludes_every_non_normal_route_and_unstable_context()
    {
        Assert.True(SilksongDeathBridgePolicy.IsStrictNormal(new SilksongDeathClassification()));
        Action<SilksongDeathClassification>[] exclusions = {
            x => x.NonLethal = true,
            x => x.MemoryForcedNonLethal = true,
            x => x.Permadeath = true,
            x => x.DemoTerminal = true,
            x => x.DuplicateDie = true,
            x => x.Hazard = true,
            x => x.Paused = true,
            x => x.Cinematic = true,
            x => x.Gameplay = false,
            x => x.Transitioning = true,
            x => x.Loading = true,
        };
        foreach (var exclude in exclusions)
        {
            var classification = new SilksongDeathClassification();
            exclude(classification);
            Assert.False(SilksongDeathBridgePolicy.IsStrictNormal(classification));
        }
    }

    [Fact]
    public void Bridge_occurrence_requires_same_owner_before_confirmation()
    {
        var frame = StableFrame();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 1;
        frame.BridgeHero = frame.Hero;
        frame.BridgeManager = frame.Manager;
        adapter.Tick();
        Assert.Equal(1, adapter.Occurrence);

        frame.Hero = new object();
        adapter.Tick();
        Assert.Equal(0, adapter.Occurrence);
        Assert.False(adapter.Ready);
    }

    [Fact]
    public void Stable_respawn_requires_confirmation_exact_owners_and_two_distinct_playing_frames()
    {
        var frame = StableFrame();
        frame.Dead = true;
        frame.HeroInPosition = false;
        frame.SceneComplete = false;
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 8;
        frame.BridgeHero = frame.Hero;
        frame.BridgeManager = frame.Manager;
        adapter.Tick();
        adapter.Configure("ROTATE", "run", lastDeath: 8, pendingOccurrence: 8);
        Assert.True(adapter.Recorded);

        frame.Dead = false;
        frame.HeroInPosition = true;
        frame.SceneComplete = true;
        frame.Frame++;
        adapter.Tick();
        Assert.False(adapter.Ready);
        adapter.Tick();
        Assert.False(adapter.Ready);
        frame.Frame++;
        adapter.Tick();
        Assert.True(adapter.Ready);

        frame.Loading = true;
        frame.Frame++;
        adapter.Tick();
        Assert.False(adapter.Ready);
        frame.Loading = false;
        frame.Frame++;
        adapter.Tick();
        Assert.False(adapter.Ready);
        frame.Frame++;
        adapter.Tick();
        Assert.True(adapter.Ready);

        frame.HudOwners = new object();
        Assert.False(adapter.Ready);
    }

    [Fact]
    public void On_never_records_or_rotates_and_completed_occurrence_is_consumed_once()
    {
        var frame = StableFrame();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ON", "run");
        frame.BridgeOccurrence = 1;
        frame.BridgeHero = frame.Hero;
        frame.BridgeManager = frame.Manager;
        adapter.Tick();
        Assert.Equal(0, adapter.Occurrence);

        adapter.Configure("ROTATE", "run", lastDeath: 1);
        frame.BridgeOccurrence = 2;
        adapter.Tick();
        Assert.Equal(2, adapter.Occurrence);
        adapter.Configure("ROTATE", "run", lastDeath: 2, pendingOccurrence: 0);
        Assert.Equal(0, adapter.Occurrence);
        adapter.Tick();
        Assert.Equal(0, adapter.Occurrence);
    }

    static SilksongDeathFrame StableFrame() => new SilksongDeathFrame {
        Frame = 10,
        Hero = new object(),
        Manager = new object(),
        HudOwners = new object(),
        Gameplay = true,
        Playing = true,
        HeroInPosition = true,
        SceneComplete = true,
        AcceptingInput = true,
        TargetsAvailable = true,
    };
}
