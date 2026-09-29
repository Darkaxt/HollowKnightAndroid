using System;
using System.Collections.Generic;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SilksongSkinLibraryTests
{
    [Fact]
    public void Exact_bridge_occurrence_freezes_one_successor_then_applies_atomically_after_stable_respawn()
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
        Assert.Equal(new[] { "apply:a", "apply:b" }, actions);
        Assert.Equal(0, request.PendingOccurrence);
        Assert.Equal(1, confirms);
    }

    [Fact]
    public void Default_successor_restore_completes_the_exact_bridge_occurrence()
    {
        var frame = Frame();
        var death = new SilksongSkinDeathAdapter(() => frame);
        var request = Request("a");
        var restores = 0;
        var commits = 0;
        using var library = new SilksongSkinLibrary(() => request,
            _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); },
            observation => {
                if (observation.PendingOccurrence > 0 && observation.Status == "Restored")
                {
                    Assert.Null(observation.ActivePackId);
                    Assert.Null(observation.ActiveTreeSha256);
                    commits++;
                    request.PendingOccurrence = 0;
                }
                return true;
            }, () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, occurrence) => {
                request.LastDeath = occurrence;
                request.PendingOccurrence = occurrence;
                request.Vanilla = true;
                request.PackId = null;
                request.TreeSha256 = null;
                request.Root = null;
                request.Textures = null;
                return true;
            }, (_, __) => true, _ => true);

        library.Tick(0);
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
        frame.Dead = true;
        frame.Frame++;
        library.Tick(1);
        frame.Dead = false;
        frame.HeroInPosition = true;
        frame.SceneComplete = true;
        frame.Frame++;
        library.Tick(1.1f);
        frame.Frame++;
        library.Tick(1.2f);

        library.Tick(2);

        Assert.Equal(1, restores);
        Assert.Equal(1, commits);
        Assert.Equal(0, death.Occurrence);
        Assert.True(library.CanRefresh);
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

    [Fact]
    public void Stable_apply_stops_idle_configuration_and_evidence_polling()
    {
        var frame = Frame();
        var death = new SilksongSkinDeathAdapter(() => frame);
        var request = Request("a");
        int reads = 0, applies = 0, reports = 0;
        using var library = new SilksongSkinLibrary(
            () => { reads++; return request; },
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Unchanged); },
            () => new SkinApplyResult(SkinApplyStatus.Restored),
            _ => { reports++; return true; },
            () => new SkinApplyResult(SkinApplyStatus.Unchanged),
            death,
            (_, __) => true, (_, __) => true, _ => true);

        library.Tick(0);
        library.Tick(1);
        library.Tick(10);

        Assert.Equal(1, reads);
        Assert.Equal(1, applies);
        Assert.Equal(1, reports);

        library.Invalidate();
        library.Tick(10);
        Assert.Equal(2, reads);
        Assert.Equal(2, reports);
    }

    [Fact]
    public void Accepted_identical_noncompletion_evidence_is_not_republished_on_idle_polls()
    {
        var frame = Frame();
        var death = new SilksongSkinDeathAdapter(() => frame);
        var request = Request("a");
        int reads = 0, attempts = 0, reports = 0;
        using var library = new SilksongSkinLibrary(
            () => { reads++; return request; },
            _ => { attempts++; return new SkinApplyResult(SkinApplyStatus.Failed, "same failure"); },
            () => throw new Exception("must not restore"),
            _ => { reports++; return true; },
            () => new SkinApplyResult(SkinApplyStatus.Failed, "same failure"),
            death,
            (_, __) => true, (_, __) => true, _ => true);

        library.Tick(0);
        library.Tick(1);
        library.Tick(2);

        Assert.Equal(3, reads);
        Assert.Equal(3, attempts);
        Assert.Equal(1, reports);
        library.Invalidate(); library.Tick(3);
        Assert.Equal(2, reports); // explicit execution edge, even for identical evidence
        request.ConfigSha256 = new string('e', 64); library.Tick(4);
        Assert.Equal(3, reports);
    }

    [Fact]
    public void Unaccepted_identical_noncompletion_evidence_remains_retryable()
    {
        var frame = Frame(); var death = new SilksongSkinDeathAdapter(() => frame);
        int reports = 0;
        using var library = new SilksongSkinLibrary(() => Request("a"),
            _ => new SkinApplyResult(SkinApplyStatus.Failed, "same failure"),
            () => throw new Exception("must not restore"), _ => ++reports > 1, null, death,
            (_, __) => true, (_, __) => true, _ => true);
        library.Tick(0); library.Tick(1); library.Tick(2);
        Assert.Equal(2, reports);
    }

    [Theory]
    [InlineData("recover")]
    [InlineData("retire")]
    [InlineData("cancel")]
    [InlineData("stale")]
    [InlineData("cancelled-selection")]
    [InlineData("supersede")]
    public void Owner_tick_retries_executed_rotation_evidence_after_selection_commits_or_releases_retired_authority(string edge)
    {
        var frame = Frame(); var death = new SilksongSkinDeathAdapter(() => frame); var request = Request("a");
        SkinLibraryObservation executed = null, accepted = null;
        int reports = 0, commits = 0, applies = 0, restores = 0;
        using var library = new SilksongSkinLibrary(() => request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); }, observation => {
                if (observation.PendingOccurrence == 0 || observation.Status != "Applied") return true;
                if (edge == "supersede" && observation.PendingOccurrence == 2)
                {
                    accepted = observation; request.PendingOccurrence = 0; commits++; return true;
                }
                reports++;
                if (executed == null)
                {
                    executed = observation; commits++; request.PendingOccurrence = 0;
                    request.ConfigSha256 = new string('f', 64);
                    return false; // durable selection changed, bounded evidence write failed
                }
                Assert.Equal(executed.ConfigSha256, observation.ConfigSha256);
                Assert.Equal(executed.RotationRun, observation.RotationRun);
                Assert.Equal(executed.PendingOccurrence, observation.PendingOccurrence);
                Assert.Equal(executed.Status, observation.Status);
                Assert.Equal(executed.Detail, observation.Detail);
                Assert.Equal(executed.ActiveTreeSha256, observation.ActiveTreeSha256);
                if (reports == 2) return false;
                accepted = observation; return true;
            }, () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, occurrence) => {
                request.LastDeath = request.PendingOccurrence = occurrence;
                request.PackId = "b"; request.TreeSha256 = new string('c', 64); return true;
            }, (_, __) => true, _ => true);
        void Tick(float time) { frame.Frame++; library.Tick(time); }
        Tick(0);
        frame.BridgeOccurrence = 1;
        frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(1, frame.Hero, frame.Manager) };
        frame.Dead = true; Tick(1);
        frame.Dead = false; frame.HeroInPosition = frame.SceneComplete = true; Tick(1.1f); Tick(1.2f); Tick(2);
        Assert.Equal(1, commits); Assert.Equal(0, request.PendingOccurrence);
        Assert.Equal(1, death.Occurrence); Assert.Null(accepted);
        if (edge == "retire")
        {
            request.Mode = "OFF"; request.RotationRun = null; request.LastDeath = 0;
            library.Invalidate();
        }
        else if (edge == "cancel")
        {
            frame.Hero = new object(); // cancellation alone, without a durable run-change escape hatch
        }
        else if (edge == "stale")
            request.PendingOccurrence = 1; // updated pending configuration must be reported with fresh correlation
        else if (edge == "cancelled-selection")
        {
            request.PackId = "a"; request.TreeSha256 = new string('b', 64);
        }
        else if (edge == "supersede")
        {
            frame.BridgeOccurrence = 2;
            frame.BridgeOccurrences = new[] { new SilksongDeathOccurrence(2, frame.Hero, frame.Manager) };
            request.LastDeath = request.PendingOccurrence = 2;
            request.PackId = "c"; request.TreeSha256 = new string('d', 64);
        }
        Tick(3); Tick(4); Tick(5);
        if (edge == "recover")
        {
            Assert.Equal(3, reports); Assert.NotNull(accepted);
            Assert.Equal("b", accepted.ActivePackId); Assert.Equal(0, death.Occurrence);
            Assert.Equal(2, applies); Assert.Equal(0, restores);
        }
        else if (edge == "supersede")
        {
            Assert.Equal(2, reports); // one bounded replay, then newer authority proceeds
            Assert.Equal(2, commits); Assert.Equal(2, accepted.PendingOccurrence);
            Assert.Equal("c", accepted.ActivePackId); Assert.Equal(0, death.Occurrence);
        }
        else
        {
            Assert.Equal(1, reports); Assert.Null(accepted); Assert.Equal(0, death.Occurrence);
        }
    }

    [Fact]
    public void Settled_owner_resumes_configuration_polling_after_backlog_cancellation()
    {
        var frame = Frame(); var death = new SilksongSkinDeathAdapter(() => frame); var request = Request("a");
        int reads = 0, cancellations = 0;
        using var library = new SilksongSkinLibrary(() => { reads++; return request; },
            _ => new SkinApplyResult(SkinApplyStatus.Applied), () => new SkinApplyResult(SkinApplyStatus.Restored),
            _ => true, () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, __) => true, (_, __) => true, _ => {
                cancellations++; request.RotationRun = "renewed-run"; return true;
            });
        library.Tick(0);
        frame.BridgeOccurrence = SilksongSkinDeathAdapter.MaxPendingOccurrences + 1;
        frame.Frame++; library.Tick(.1f); // missing/overflowed bridge evidence cancels the run
        Assert.Equal(1, cancellations);
        library.Tick(1);
        Assert.Equal(2, reads); Assert.Equal("renewed-run", death.Run);
    }

    [Fact]
    public void Native_menu_invalidation_exposes_an_immediate_runtime_poll()
    {
        var frame = Frame(); var death = new SilksongSkinDeathAdapter(() => frame); var request = Request("a");
        int reads = 0;
        using var library = new SilksongSkinLibrary(() => { reads++; return request; },
            _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => new SkinApplyResult(SkinApplyStatus.Restored), _ => true,
            () => new SkinApplyResult(SkinApplyStatus.Unchanged), death,
            (_, __) => true, (_, __) => true, _ => true);

        library.Tick(0); request.SpriteScope = "ALL"; library.Tick(.1f);
        Assert.Equal(1, reads);
        library.Invalidate(); library.Tick(.1f);
        Assert.Equal(2, reads);
    }

    static SkinLibraryRequest Request(string id) => new SkinLibraryRequest {
        ProfileId = "silksong", ConfigSha256 = new string('a', 64), Mode = "ROTATE", SpriteScope = "CHARACTER",
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
