using System;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SilksongSkinDeathAdapterTests
{
    [Fact]
    public void Bridge_occurrence_requires_same_owner_before_confirmation()
    {
        var frame = StableFrame();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
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
        frame.BridgeOccurrence = 7;
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 8;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(8, frame.Hero, frame.Manager) };
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
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
        adapter.Tick();
        Assert.Equal(0, adapter.Occurrence);

        adapter.Configure("ROTATE", "run", lastDeath: 1);
        frame.BridgeOccurrence = 2;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(2, frame.Hero, frame.Manager) };
        adapter.Tick();
        Assert.Equal(2, adapter.Occurrence);
        adapter.Configure("ROTATE", "run", lastDeath: 2, pendingOccurrence: 0);
        Assert.Equal(0, adapter.Occurrence);
        adapter.Tick();
        Assert.Equal(0, adapter.Occurrence);
    }

    [Fact]
    public void Bridge_backlog_preserves_death_before_poll_in_strict_order()
    {
        var frame = StableFrame();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 2;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(1, frame.Hero, frame.Manager),
            new SilksongDeathOccurrence(2, frame.Hero, frame.Manager),
        };

        adapter.Tick();
        Assert.Equal(1, adapter.Occurrence);
        Assert.Equal(1, adapter.PendingOccurrences);

        adapter.Configure("ROTATE", "run", lastDeath: 1, pendingOccurrence: 0);
        Assert.Equal(2, adapter.Occurrence);
        Assert.Equal(0, adapter.PendingOccurrences);
    }

    [Fact]
    public void Bridge_backlog_retains_death_while_first_successor_is_pending()
    {
        var frame = StableFrame();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
        adapter.Tick();
        adapter.Configure("ROTATE", "run", lastDeath: 1, pendingOccurrence: 1);

        frame.BridgeOccurrence = 2;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(1, frame.Hero, frame.Manager),
            new SilksongDeathOccurrence(2, frame.Hero, frame.Manager),
        };
        adapter.Tick();
        Assert.Equal(1, adapter.Occurrence);
        Assert.Equal(1, adapter.PendingOccurrences);

        adapter.Configure("ROTATE", "run", lastDeath: 1, pendingOccurrence: 0);
        Assert.Equal(2, adapter.Occurrence);
    }

    [Fact]
    public void Bridge_backlog_retires_every_occurrence_already_acknowledged_without_a_successor()
    {
        var frame = StableFrame();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 3;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(1, frame.Hero, frame.Manager),
            new SilksongDeathOccurrence(2, frame.Hero, frame.Manager),
            new SilksongDeathOccurrence(3, frame.Hero, frame.Manager),
        };
        adapter.Tick();
        Assert.Equal(new long[] { 1, 2, 3 }, adapter.PendingBridgeOccurrences);

        adapter.Configure("ROTATE", "run", lastDeath: 3, pendingOccurrence: 0);

        Assert.Equal(0, adapter.Occurrence);
        Assert.Empty(adapter.PendingBridgeOccurrences);
        Assert.Equal(0, adapter.PendingOccurrences);
    }

    [Fact]
    public void Counter_jump_retains_distinct_per_occurrence_owners_in_order()
    {
        var frame = StableFrame();
        var firstHero = frame.Hero;
        var secondHero = new object();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 2;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(1, firstHero, frame.Manager),
            new SilksongDeathOccurrence(2, secondHero, frame.Manager),
        };

        adapter.Tick();
        Assert.Equal(1, adapter.Occurrence);
        Assert.Equal(1, adapter.PendingOccurrences);
        Assert.Empty(adapter.PendingCancellations);

        frame.Hero = secondHero;
        frame.Frame++;
        adapter.Tick();
        Assert.Equal(new long[] { 1 }, adapter.PendingCancellations);
        Assert.Equal(2, adapter.Occurrence);
        Assert.Equal(0, adapter.PendingOccurrences);
    }

    [Fact]
    public void Owner_replacement_cancels_only_stale_active_death_and_retains_newer_death()
    {
        var frame = StableFrame();
        var firstHero = frame.Hero;
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, firstHero, frame.Manager) };
        adapter.Tick();
        adapter.Configure("ROTATE", "run", lastDeath: 1, pendingOccurrence: 1);

        var secondHero = new object();
        frame.Hero = secondHero;
        frame.BridgeOccurrence = 2;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(1, firstHero, frame.Manager),
            new SilksongDeathOccurrence(2, secondHero, frame.Manager),
        };
        frame.Frame++;
        adapter.Tick();

        Assert.False(adapter.BacklogFaulted);
        Assert.Equal(new long[] { 1 }, adapter.PendingCancellations);
        Assert.Equal(2, adapter.Occurrence);
        Assert.Equal(0, adapter.PendingOccurrences);
    }

    [Fact]
    public void Unacknowledged_stale_cancellations_count_toward_bounded_overflow()
    {
        var frame = StableFrame();
        var manager = frame.Manager;
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        var owners = new SilksongDeathOccurrence[SilksongSkinDeathAdapter.MaxPendingOccurrences];
        for (var index = 0; index < owners.Length; index++)
            owners[index] = new SilksongDeathOccurrence(index + 1, new object(), manager);
        frame.Hero = owners[owners.Length - 1].Hero;
        frame.BridgeOccurrence = owners.Length;
        frame.BridgeOccurrences = owners;

        adapter.Tick();
        Assert.Equal(owners.Length - 1, adapter.PendingCancellations.Count);
        Assert.Equal(owners.Length, adapter.Occurrence);

        frame.BridgeOccurrence++;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(frame.BridgeOccurrence, frame.Hero, manager),
        };
        adapter.Tick();

        Assert.True(adapter.BacklogFaulted);
        Assert.Equal(0, adapter.Occurrence);
        Assert.Empty(adapter.PendingCancellations);
    }

    [Fact]
    public void Bridge_backlog_overflow_fails_closed_without_skipping_to_latest()
    {
        var frame = StableFrame();
        var adapter = new SilksongSkinDeathAdapter(() => frame);
        adapter.Configure("ROTATE", "run");
        frame.BridgeOccurrence = SilksongSkinDeathAdapter.MaxPendingOccurrences + 1;
        frame.BridgeOccurrences = Array.Empty<SilksongDeathOccurrence>();

        adapter.Tick();

        Assert.True(adapter.BacklogFaulted);
        Assert.Equal(0, adapter.Occurrence);
        Assert.Equal(0, adapter.PendingOccurrences);
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
