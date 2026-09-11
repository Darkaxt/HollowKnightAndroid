using System;
using System.Collections.Generic;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SilksongSkinLibraryTests
{
    [Fact]
    public void Exact_bridge_occurrence_freezes_one_successor_then_restores_and_applies_after_stable_respawn()
    {
        var frame = Frame();
        var death = new SilksongSkinDeathAdapter(() => frame);
        var request = Request("a");
        var actions = new List<string>();
        var confirms = 0;
        using var library = new SilksongSkinLibrary(() => request,
            pack => { actions.Add("apply:" + pack.Id); return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { actions.Add("restore"); return new SkinApplyResult(SkinApplyStatus.Restored); },
            observation => {
                if (observation.PendingOccurrence > 0 && observation.Status == "Applied") {
                    request.PendingOccurrence = 0;
                    return true;
                }
                return true;
            }, () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (run, occurrence) => {
                confirms++;
                request.LastDeath = occurrence;
                request.PendingOccurrence = occurrence;
                request.PackId = "b";
                request.TreeSha256 = new string('c', 64);
                return true;
            }, (_, __) => true, _ => true);

        library.Tick(0);
        Assert.Equal(new[] { "apply:a" }, actions);
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
        frame.Dead = true;
        frame.Frame++;
        library.Tick(.1f);
        library.Tick(1);
        Assert.Equal(1, confirms);
        Assert.Equal(new[] { "apply:a" }, actions);

        frame.Dead = false;
        frame.HeroInPosition = true;
        frame.SceneComplete = true;
        frame.Frame++;
        library.Tick(1.1f);
        frame.Frame++;
        library.Tick(1.2f);
        Assert.True(library.CanRefresh);
        library.Tick(2);
        Assert.Equal(new[] { "apply:a", "restore", "apply:b" }, actions);
        Assert.Equal(0, request.PendingOccurrence);
        Assert.Equal(1, confirms);
    }

    [Fact]
    public void On_mode_never_confirms_a_bridge_occurrence()
    {
        var frame = Frame();
        var request = Request("a");
        request.Mode = "ON";
        var death = new SilksongSkinDeathAdapter(() => frame);
        var confirms = 0;
        using var library = new SilksongSkinLibrary(() => request,
            _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => new SkinApplyResult(SkinApplyStatus.Restored), _ => true,
            () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, __) => { confirms++; return true; }, (_, __) => true, _ => true);
        library.Tick(0);
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
        frame.Frame++;
        library.Tick(1);
        Assert.Equal(0, confirms);
    }

    [Fact]
    public void Stale_first_owner_is_cancelled_without_losing_replacement_owner_death()
    {
        var frame = Frame();
        var firstHero = frame.Hero;
        var secondHero = new object();
        var death = new SilksongSkinDeathAdapter(() => frame);
        var request = Request("a");
        var confirmed = new List<long>();
        var cancelled = new List<long>();
        var cancelledRuns = 0;
        using var library = new SilksongSkinLibrary(() => request,
            _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => new SkinApplyResult(SkinApplyStatus.Restored),
            observation => {
                if (observation.PendingOccurrence == 2 && observation.Status == "Applied")
                    request.PendingOccurrence = 0;
                return true;
            }, () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, occurrence) => {
                if (!confirmed.Contains(occurrence)) confirmed.Add(occurrence);
                if (request.PendingOccurrence == 0)
                {
                    request.LastDeath = occurrence;
                    request.PendingOccurrence = occurrence;
                    request.PackId = "b";
                }
                return true;
            }, (_, occurrence) => {
                cancelled.Add(occurrence);
                if (occurrence == request.PendingOccurrence)
                    request.PendingOccurrence = 0;
                return true;
            }, _ => { cancelledRuns++; return true; });

        library.Tick(0);
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, firstHero, frame.Manager) };
        frame.Dead = true;
        frame.Frame++;
        library.Tick(1);
        Assert.Equal(new long[] { 1 }, confirmed);

        frame.Hero = secondHero;
        frame.BridgeOccurrence = 2;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(1, firstHero, frame.Manager),
            new SilksongDeathOccurrence(2, secondHero, frame.Manager),
        };
        frame.Frame++;
        library.Tick(1.1f);
        library.Tick(2);

        Assert.Equal(new long[] { 1 }, cancelled);
        Assert.Equal(new long[] { 1, 2 }, confirmed);
        Assert.Equal(2, death.Occurrence);
        Assert.True(death.Recorded);
        Assert.Equal(0, cancelledRuns);
    }

    [Theory]
    [InlineData("ROTATE", "replacement-run")]
    [InlineData("OFF", null)]
    public void Durable_run_change_discards_impossible_local_cancellation_and_polling_recovers(
        string nextMode, string nextRun)
    {
        var frame = Frame();
        var firstHero = frame.Hero;
        var death = new SilksongSkinDeathAdapter(() => frame);
        var request = Request("a");
        var applies = new List<string>();
        var cancellationCalls = 0;
        var restores = 0;
        using var library = new SilksongSkinLibrary(() => request,
            pack => { applies.Add(pack.Id); return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); },
            _ => true, () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, occurrence) => {
                request.LastDeath = occurrence;
                request.PendingOccurrence = occurrence;
                return true;
            }, (_, __) => { cancellationCalls++; return true; }, _ => true);

        library.Tick(0);
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, firstHero, frame.Manager) };
        frame.Dead = true;
        frame.Frame++;
        library.Tick(1);
        frame.Hero = new object();
        frame.Frame++;
        library.Tick(1.1f);
        Assert.Equal(new long[] { 1 }, death.PendingCancellations);

        request.Mode = nextMode;
        request.RotationRun = nextRun;
        request.PendingOccurrence = 0;
        request.LastDeath = 0;
        request.PackId = "replacement";
        request.TreeSha256 = new string('d', 64);
        library.Tick(2);

        Assert.Equal(nextRun, death.Run);
        Assert.Empty(death.PendingCancellations);
        Assert.Equal(0, cancellationCalls);
        if (nextMode == "OFF") Assert.True(restores > 0);
        else Assert.Contains("replacement", applies);
    }

    [Fact]
    public void Second_normal_death_waits_while_first_apply_is_pending_then_confirms_in_order()
    {
        var frame = Frame();
        var death = new SilksongSkinDeathAdapter(() => frame);
        var request = Request("a");
        var confirmed = new List<long>();
        using var library = new SilksongSkinLibrary(() => request,
            _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => new SkinApplyResult(SkinApplyStatus.Restored),
            observation => {
                if (observation.PendingOccurrence > 0 && observation.Status == "Applied")
                    request.PendingOccurrence = 0;
                return true;
            }, () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, occurrence) => {
                confirmed.Add(occurrence);
                request.LastDeath = occurrence;
                request.PendingOccurrence = occurrence;
                request.PackId = occurrence == 1 ? "b" : "a";
                return true;
            }, (_, __) => true, _ => true);

        library.Tick(0);
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
        frame.Dead = true;
        frame.Frame++;
        library.Tick(1);
        Assert.Equal(new long[] { 1 }, confirmed);

        frame.BridgeOccurrence = 2;
        frame.BridgeOccurrences = new[] {
            new SilksongDeathOccurrence(1, frame.Hero, frame.Manager),
            new SilksongDeathOccurrence(2, frame.Hero, frame.Manager),
        };
        frame.Frame++;
        library.Tick(1.1f);
        Assert.Equal(new long[] { 1 }, confirmed);

        frame.Dead = false;
        frame.HeroInPosition = true;
        frame.SceneComplete = true;
        frame.Frame++;
        library.Tick(1.2f);
        frame.Frame++;
        library.Tick(1.3f);
        library.Tick(2);
        library.Tick(3);

        Assert.Equal(new long[] { 1, 2 }, confirmed);
    }

    static SkinLibraryRequest Request(string id) => new SkinLibraryRequest {
        ProfileId = "silksong", ConfigSha256 = new string('a', 64), Mode = "ROTATE",
        PackId = id, TreeSha256 = new string('b', 64), Root = Environment.CurrentDirectory,
        Textures = new Dictionary<string, string> {
            [SilksongSkinTargets.All[0].CanonicalPath] = "atlas0.png"
        }, RotationRun = "run"
    };

    static SilksongDeathFrame Frame() => new SilksongDeathFrame {
        Frame = 1, Hero = new object(), Manager = new object(), HudOwners = new object(),
        Gameplay = true, Playing = true, AcceptingInput = true, TargetsAvailable = true,
    };
}
