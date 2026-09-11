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
            }, _ => true);

        library.Tick(0);
        Assert.Equal(new[] { "apply:a" }, actions);
        frame.BridgeOccurrence = 1;
        frame.BridgeHero = frame.Hero;
        frame.BridgeManager = frame.Manager;
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
            (_, __) => { confirms++; return true; }, _ => true);
        library.Tick(0);
        frame.BridgeOccurrence = 1;
        frame.BridgeHero = frame.Hero;
        frame.BridgeManager = frame.Manager;
        frame.Frame++;
        library.Tick(1);
        Assert.Equal(0, confirms);
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
