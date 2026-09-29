using System;
using System.Collections.Generic;
using DualSouls.Skins.HollowKnight.Runtime;
using DualSouls.Skins.Runtime;
using Xunit;

public class HollowKnightSkinLibraryTests
{
    [Fact] public void ProductionControllerBoundaryExists()
    {
        Assert.NotNull(typeof(SkinRuntimeSession).Assembly.GetType("DualSouls.Skins.Runtime.SkinLibraryRuntimeController"));
    }

    [Fact] public void Controller_accepts_only_its_explicit_profile()
    {
        var request = Request(); request.ProfileId = "silksong";
        var rules = new SkinRuntimeRules("silksong", 11, _ => true, (_, __) => true);
        int applies = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(rules, () => request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => new SkinApplyResult(SkinApplyStatus.Restored), value => observed = value);

        controller.Tick();
        Assert.Equal(1, applies);
        Assert.Equal("Applied", observed.Status);
        request.ProfileId = "hollow-knight";
        request.PackId = "b";
        controller.Tick();
        Assert.Equal(1, applies);
        Assert.Equal("Failed", observed.Status);
    }

    [Fact] public void Silksong_pending_rotation_restores_before_frozen_successor_and_retries_failed_restore()
    {
        var rules = new SkinRuntimeRules("silksong", 11, _ => true, (_, __) => true,
            restoreBeforeRotation: true);
        var request = Request(); request.ProfileId = "silksong";
        var actions = new List<string>();
        var failRestore = true;
        var controller = new SkinLibraryRuntimeController(rules, () => request,
            pack => { actions.Add("apply:" + pack.Id); return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => {
                actions.Add("restore");
                if (failRestore) return new SkinApplyResult(SkinApplyStatus.RestoreFailed);
                return new SkinApplyResult(SkinApplyStatus.Restored);
            }, _ => { }, null, _ => true);
        controller.Tick();
        request.Mode = "ROTATE";
        request.PackId = "b";
        request.TreeSha256 = new string('c', 64);
        request.PendingOccurrence = 1;
        controller.Tick();
        Assert.Equal(new[] { "apply:a", "restore" }, actions);

        failRestore = false;
        controller.Tick();
        Assert.Equal(new[] { "apply:a", "restore", "restore", "apply:b" }, actions);
    }

    [Fact] public void Same_pack_and_mode_scope_change_has_distinct_cache_identity_and_reapplies()
    {
        var request = Request(); var applied = new List<SkinPack>();
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules, () => request,
            pack => { applied.Add(pack); return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => new SkinApplyResult(SkinApplyStatus.Restored), _ => { });

        controller.Tick(); controller.Tick();
        request.SpriteScope = "CHARACTER"; request.ConfigSha256 = new string('c', 64); controller.Tick();
        request.Mode = "ROTATE"; request.ConfigSha256 = new string('d', 64); controller.Tick();

        Assert.Equal(3, applied.Count);
        Assert.Equal(new[] { "ALL", "CHARACTER", "CHARACTER" }, applied.ConvertAll(x => x.SpriteScope));
        Assert.Equal(new[] { "ON", "ON", "ROTATE" }, applied.ConvertAll(x => x.Mode));
        Assert.NotSame(applied[0], applied[1]);
        Assert.NotSame(applied[1], applied[2]);
    }

    [Fact] public void Off_validates_and_transports_scope_but_only_restores_baseline()
    {
        var request = Request(); request.Mode = "OFF"; request.SpriteScope = "CHARACTER";
        int applies = 0, restores = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules, () => request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); }, value => observed = value);

        controller.Tick(); request.SpriteScope = "ALL"; request.ConfigSha256 = new string('c', 64); controller.Tick();

        Assert.Equal(0, applies); Assert.Equal(1, restores); Assert.Equal("Restored", observed.Status);
    }

    [Fact] public void Invalid_scope_is_rejected_without_touching_visuals()
    {
        var request = Request(); request.SpriteScope = "EVERYTHING"; int writes = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules, () => request,
            _ => { writes++; return null; }, () => { writes++; return null; }, value => observed = value);
        controller.Tick();
        Assert.Equal(0, writes); Assert.Equal("Failed", observed.Status);
    }

    [Fact] public void OnAndRotateReuseImmutablePackAndOffRestoresOnce()
    {
        var request = Request(); var applied = new List<SkinPack>(); int restores = 0;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => request,
            pack => { applied.Add(pack); return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); }, _ => { });
        controller.Tick(); controller.Tick(); request.Mode = "ROTATE"; request.ConfigSha256 = new string('c', 64); controller.Tick();
        Assert.Equal(2, applied.Count); // identical identity still needs a new mode policy
        request.Mode = "OFF"; controller.Tick(); controller.Tick();
        Assert.Equal(1, restores);
        request.Mode = "ON"; controller.Tick();
        Assert.Equal(3, applied.Count); Assert.Same(applied[0], applied[2]);
    }
    [Fact] public void Pending_successor_requires_live_readiness_and_failed_busy_report_retries_same_pack()
    {
        var request = Request(); bool ready = false, busy = false, reportBusy = true; int attempts = 0;
        var candidates = new List<SkinPack>(); SkinLibraryObservation seen = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => busy ? null : request, pack => {
            candidates.Add(pack); return new SkinApplyResult(++attempts == 2 ? SkinApplyStatus.Failed : SkinApplyStatus.Applied);
        }, () => throw new Exception("must not restore working pack"), result => {
            seen = result; if (result.ActivePackId == "b" && reportBusy) { reportBusy = false; throw new Exception("report busy"); }
        }, null, _ => ready);
        controller.Tick(); request.PackId="b";request.TreeSha256=new string('c',64);request.Mode="ROTATE";
        request.RotationRun=new string('d',32);request.PendingOccurrence=1;
        controller.Tick(); Assert.Equal(1,attempts);Assert.Equal("AwaitingTargets",seen.Status);
        ready=true;controller.Tick();Assert.Equal("Failed",seen.Status);Assert.Equal("a",seen.ActivePackId);
        busy=true;controller.Tick();Assert.Equal(2,attempts);busy=false;controller.Tick();Assert.Equal(3,attempts);
        Assert.Same(candidates[1],candidates[2]);Assert.Equal("b",seen.ActivePackId);
        ready=false;controller.Tick();Assert.Equal("AwaitingTargets",seen.Status);Assert.Equal(3,attempts);
        ready=true;controller.Tick();Assert.Equal("Unchanged",seen.Status);Assert.Equal(1,seen.PendingOccurrence);Assert.Equal(3,attempts);
    }

    [Fact] public void Pending_vanilla_successor_restores_once_and_reports_default_active()
    {
        var request = Request(); int applies = 0, restores = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules, () => request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); },
            value => observed = value, null, _ => true);
        controller.Tick();
        request.Mode = "ROTATE"; request.Vanilla = true; request.PackId = null;
        request.TreeSha256 = null; request.Root = null; request.Textures = null; request.PendingOccurrence = 1;

        controller.Tick(); controller.Tick();

        Assert.Equal(1, applies); Assert.Equal(1, restores);
        Assert.Equal("Restored", observed.Status); Assert.Null(observed.ActivePackId);
        Assert.Null(observed.ActiveTreeSha256); Assert.Equal(1, observed.PendingOccurrence);
    }

    [Fact] public void BusyAndReadFailureKeepWorkingVisualAndRetry()
    {
        var request = Request(); int reads = 0, applies = 0, restores = 0;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => ++reads == 2 ? null : reads == 3 ? throw new InvalidOperationException("busy io") : request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); }, _ => { });
        controller.Tick(); controller.Tick(); controller.Tick(); controller.Tick();
        Assert.Equal(1, applies); Assert.Equal(0, restores);
    }
    [Fact] public void FailedCandidateAndAwaitingTargetsAreRetriedWithoutClaimingActive()
    {
        var request = Request(); int attempts = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => request,
            _ => new SkinApplyResult(++attempts == 1 ? SkinApplyStatus.AwaitingTargets : attempts == 2 ? SkinApplyStatus.Failed : SkinApplyStatus.Applied),
            () => throw new Exception("must not restore working visual"), result => observed = result);
        controller.Tick(); Assert.Null(observed.ActivePackId);
        controller.Tick(); Assert.Null(observed.ActivePackId);
        controller.Tick(); Assert.Equal("a", observed.ActivePackId); Assert.Equal(3, attempts);
    }
    [Fact] public void RestoreFailureRemainsVisibleAndRetries()
    {
        var request = Request(); int restores = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => request, _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => new SkinApplyResult(++restores == 1 ? SkinApplyStatus.RestoreFailed : SkinApplyStatus.Restored), x => observed = x);
        controller.Tick(); request.Mode = "OFF"; controller.Tick(); Assert.Equal("a", observed.ActivePackId);
        controller.Tick(); Assert.Null(observed.ActivePackId); Assert.Equal(2, restores);
    }
    [Fact] public void WrongProfileAndUnsafeModeDoNotTouchVisuals()
    {
        var request = Request(); request.ProfileId = "silksong"; int writes = 0;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => request, _ => { writes++; return null; }, () => { writes++; return null; }, _ => { });
        controller.Tick(); request.ProfileId = "hollow-knight"; request.Mode = "BOGUS"; controller.Tick(); Assert.Equal(0, writes);
    }
    [Fact] public void CachedSelectionDoesNotHideRuntimeRefreshFailure()
    {
        SkinLibraryObservation observation = null;
        var current = new SkinApplyResult(SkinApplyStatus.Applied);
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,Request, _ => current,
            () => new SkinApplyResult(SkinApplyStatus.Restored), x => observation = x, () => current);
        controller.Tick(); current = new SkinApplyResult(SkinApplyStatus.Failed, "refresh failed"); controller.Tick();
        Assert.Equal("Failed", observation.Status); Assert.Equal("refresh failed", observation.Detail);
        Assert.Equal("a", observation.ActivePackId);
    }
    [Fact] public void FailedReplacementReportsLastWorkingPackUntilRetrySucceeds()
    {
        var request = Request(); SkinLibraryObservation observation = null; int attempts = 0;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => request,
            _ => new SkinApplyResult(++attempts == 2 ? SkinApplyStatus.Failed : SkinApplyStatus.Applied),
            () => throw new Exception("must preserve working visual"), x => observation = x);
        controller.Tick(); request.PackId = "b"; request.TreeSha256 = new string('c',64); controller.Tick();
        Assert.Equal("a",observation.ActivePackId); Assert.Equal("Failed",observation.Status);
        controller.Tick(); Assert.Equal("b",observation.ActivePackId); Assert.Equal(3,attempts);
    }
    [Fact] public void ReportingFailureRetriesWithoutReapplyingOrRestoringVisuals()
    {
        int reports = 0, applies = 0;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,Request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => throw new Exception("must not restore"), _ => { if (++reports == 1) throw new Exception("report busy"); });
        controller.Tick(); controller.Tick(); Assert.Equal(2,reports); Assert.Equal(1,applies);
    }
    [Fact] public void Accepted_identical_noncompletion_evidence_is_not_republished_on_idle_polls()
    {
        var request = Request();
        var death = new HollowKnightSkinDeathAdapter(LiveFrame);
        int reads = 0, attempts = 0, reports = 0;
        string detail = "same failure";
        using var library = new HollowKnightSkinLibrary(
            () => { reads++; return request; },
            _ => { attempts++; return new SkinApplyResult(SkinApplyStatus.Failed, detail); },
            () => throw new Exception("must not restore"),
            _ => { reports++; return true; },
            () => new SkinApplyResult(SkinApplyStatus.Failed, detail),
            death,
            (_, __) => true,
            _ => true);

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
        detail = "changed failure"; library.Tick(5); library.Tick(6);
        Assert.Equal(4, reports);
    }

    [Fact] public void Unaccepted_identical_noncompletion_evidence_remains_retryable()
    {
        var death = new HollowKnightSkinDeathAdapter(LiveFrame);
        int reports = 0;
        using var library = new HollowKnightSkinLibrary(Request,
            _ => new SkinApplyResult(SkinApplyStatus.Failed, "same failure"),
            () => throw new Exception("must not restore"), _ => ++reports > 1, null, death,
            (_, __) => true, _ => true);
        library.Tick(0); library.Tick(1); library.Tick(2);
        Assert.Equal(2, reports);
    }

    [Theory]
    [InlineData("ON")]
    [InlineData("ROTATE")]
    public void PriorOffDoesNotSuppressActualRestoreAfterApplyAndRollbackFail(string mode)
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hk-library-off-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        // Host decoder boundary: only the actual session's bounded PNG header inspection is exercised.
        var png = new byte[45]; Array.Copy(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png, 8);
        png[11] = 13; png[12] = 73; png[13] = 72; png[14] = 68; png[15] = 82; png[19] = 1; png[23] = 1;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "sheet.png"), png);
        object vanilla = new object(), visual = vanilla; bool failWrites = false;
        var slot = new SkinSlot(new object(), "texture", "Knight.png", () => true, () => visual,
            value => {
                if (failWrites && ReferenceEquals(value, vanilla)) throw new System.IO.IOException("rollback write denied");
                visual = value;
                if (failWrites) throw new System.IO.IOException("apply setter mutated then failed");
            }, (texture, original) => texture.Value);
        using var session = new SkinRuntimeSession(new RecoveryDecoder(), () => new[] { slot },
            512L * 1024 * 1024, HollowKnightSkinPolicy.RuntimeRules);
        var request = Request(); request.Mode = "OFF"; request.Root = root;
        request.Textures = new Dictionary<string, string> { ["Knight.png"] = "sheet.png" };
        int restores = 0; SkinApplyResult current = null; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => request, pack => current = session.TryApply(pack),
            () => { restores++; return current = session.TryRestore(); }, x => observed = x, () => current);
        void Tick() { current = session.Refresh(); controller.Tick(); } // production runtime-before-library polling order

        Tick(); Assert.Equal(1, restores); Assert.Equal("Unchanged", observed.Status); Assert.Same(vanilla, visual);
        failWrites = true; request.Mode = mode; Tick();
        Assert.Equal("RestoreFailed", observed.Status); Assert.NotSame(vanilla, visual); Assert.Null(observed.ActivePackId);
        failWrites = false; current = session.Refresh(); Assert.Equal(SkinApplyStatus.Blocked, current.Status);
        request.Mode = "OFF"; Tick();
        Assert.Equal(2, restores); Assert.Equal("Restored", observed.Status); Assert.Same(vanilla, visual);
        Assert.Null(session.CurrentPack); Assert.Equal(SkinApplyStatus.Unchanged, session.Refresh().Status);
        Tick(); Assert.Equal(2, restores); Assert.Equal("Restored", observed.Status); Assert.Same(vanilla, visual);
        Assert.Equal(SkinApplyStatus.Unchanged, session.Refresh().Status);
    }
    [Fact] public void PriorOffDoesNotSuppressRestoreRetryAfterApplyThrows()
    {
        var request = Request(); request.Mode = "OFF"; int restores = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,() => request, _ => throw new InvalidOperationException("apply threw"),
            () => new SkinApplyResult(++restores == 2 ? SkinApplyStatus.RestoreFailed : SkinApplyStatus.Restored), x => observed = x);
        controller.Tick(); Assert.Equal(1, restores);
        request.Mode = "ON"; controller.Tick(); Assert.Equal("Failed", observed.Status); Assert.Equal("apply threw", observed.Detail);
        request.Mode = "OFF"; controller.Tick(); Assert.Equal(2, restores); Assert.Equal("RestoreFailed", observed.Status);
        controller.Tick(); Assert.Equal(3, restores); Assert.Equal("Restored", observed.Status);
        controller.Tick(); Assert.Equal(3, restores); Assert.Null(observed.ActivePackId);
    }
    [Fact] public void Stable_apply_stops_repeated_configuration_and_report_polling()
    {
        var request = Request();
        var death = new HollowKnightSkinDeathAdapter(LiveFrame);
        int reads = 0, reports = 0, applies = 0, restores = 0;
        using var library = new HollowKnightSkinLibrary(
            () => { reads++; return request; },
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Unchanged); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); },
            _ => { reports++; return true; },
            () => new SkinApplyResult(SkinApplyStatus.Unchanged),
            death,
            (_, __) => true,
            _ => true);

        library.Tick(0);
        library.Tick(1);
        library.Tick(10);

        Assert.Equal(1, reads);
        Assert.Equal(1, reports);
        Assert.Equal(1, applies);

        request.Mode = "OFF";
        library.Invalidate();
        library.Tick(11);
        Assert.Equal(2, reads);
        Assert.Equal(2, reports);
        Assert.Equal(1, restores);
    }

    [Fact] public void Stable_apply_retries_an_unaccepted_report_before_settling()
    {
        var request = Request();
        var death = new HollowKnightSkinDeathAdapter(LiveFrame);
        int reads = 0, reports = 0;
        using var library = new HollowKnightSkinLibrary(
            () => { reads++; return request; },
            _ => new SkinApplyResult(SkinApplyStatus.Unchanged),
            () => new SkinApplyResult(SkinApplyStatus.Restored),
            _ => ++reports > 1,
            () => new SkinApplyResult(SkinApplyStatus.Unchanged),
            death,
            (_, __) => true,
            _ => true);

        library.Tick(0);
        library.Tick(1);
        library.Tick(10);

        Assert.Equal(2, reads);
        Assert.Equal(2, reports);
    }

    [Fact] public void Production_poll_skips_configuration_transport_until_visual_targets_are_live()
    {
        var request = Request();
        var death = new HollowKnightSkinDeathAdapter(LiveFrame);
        int reads = 0;
        bool targetsReady = false;
        using var library = new HollowKnightSkinLibrary(
            () => { reads++; return request; },
            _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => new SkinApplyResult(SkinApplyStatus.Restored),
            _ => true,
            () => new SkinApplyResult(SkinApplyStatus.Unchanged),
            death,
            (_, __) => true,
            _ => true,
            () => targetsReady);

        library.Tick(0);
        library.Tick(10);
        Assert.Equal(0, reads);

        targetsReady = true;
        library.Tick(10);
        Assert.Equal(1, reads);
    }

    [Fact] public void Production_poll_samples_each_frame_and_retries_same_confirm_and_report()
    {
        var request=Request();request.Mode="ROTATE";request.RotationRun=new string('d',32);
        var frame=LiveFrame();var death=new HollowKnightSkinDeathAdapter(()=>frame);
        int confirms=0,reports=0,applies=0;
        using var library=new HollowKnightSkinLibrary(()=>request, _=>{applies++;return new SkinApplyResult(SkinApplyStatus.Applied);},
            ()=>new SkinApplyResult(SkinApplyStatus.Restored), observation=>{
                if(observation.PendingOccurrence==0)return true;
                Assert.Equal(1,observation.PendingOccurrence);
                if(observation.Status!="Applied"&&observation.Status!="Unchanged")return true;
                if(++reports==1)return false;
                request.PendingOccurrence=0;return true;
            }, ()=>new SkinApplyResult(SkinApplyStatus.Unchanged), death, (run,occurrence)=>{
                Assert.Equal(request.RotationRun,run);Assert.Equal(1,occurrence);
                if(++confirms==1)return false;
                request.LastDeath=1;request.PendingOccurrence=1;request.PackId="b";return true;
            }, _=>true);
        library.Tick(0);Assert.Equal(1,applies);
        death.OnDeath(frame.Hero,frame.Manager);frame.Dead=true;frame.Frame++;library.Tick(.1f);
        Assert.Equal(1,death.Occurrence);Assert.Equal(0,confirms);
        library.Tick(1);Assert.Equal(1,confirms);Assert.Equal(1,applies);
        library.Tick(2);Assert.Equal(2,confirms);Assert.Equal(1,applies);Assert.False(library.CanRefresh);
        frame.Dead=false;death.HeroInPosition(frame.Hero,frame.Manager);death.SceneCompleted(frame.Hero,frame.Manager);
        frame.Frame++;library.Tick(2.1f);frame.Frame++;library.Tick(2.2f);Assert.True(library.CanRefresh);
        library.Tick(3);Assert.Equal(2,applies);Assert.Equal(1,request.PendingOccurrence);
        frame.Paused=true;Assert.False(library.CanRefresh);library.Tick(4);Assert.Equal(1,reports);
        frame.Paused=false;frame.Frame++;library.Tick(4.1f);frame.Frame++;library.Tick(4.2f);
        library.Tick(5);Assert.Equal(2,reports);Assert.Equal(0,request.PendingOccurrence);Assert.Equal(2,applies);
        library.Tick(6);Assert.Equal(0,death.Occurrence);Assert.Equal(2,confirms);
    }
    [Fact] public void Pending_default_restore_is_committed_as_a_rotation_completion()
    {
        var request=Request();request.Mode="ROTATE";request.RotationRun=new string('d',32);
        var frame=LiveFrame();var death=new HollowKnightSkinDeathAdapter(()=>frame);
        int commits=0,restores=0;
        using var library=new HollowKnightSkinLibrary(()=>request,_=>new SkinApplyResult(SkinApplyStatus.Applied),
            ()=>{restores++;return new SkinApplyResult(SkinApplyStatus.Restored);},observation=>{
                if(observation.PendingOccurrence>0&&observation.Status=="Restored"){
                    Assert.Null(observation.ActivePackId);Assert.Null(observation.ActiveTreeSha256);
                    commits++;request.PendingOccurrence=0;return true;
                }
                return true;
            },()=>new SkinApplyResult(SkinApplyStatus.Unchanged),death,(run,occurrence)=>{
                request.LastDeath=occurrence;request.PendingOccurrence=occurrence;request.Vanilla=true;
                request.PackId=null;request.TreeSha256=null;request.Root=null;request.Textures=null;return true;
            },_=>true);
        library.Tick(0);death.OnDeath(frame.Hero,frame.Manager);frame.Dead=true;frame.Frame++;library.Tick(.1f);
        library.Tick(1);frame.Dead=false;death.HeroInPosition(frame.Hero,frame.Manager);
        death.SceneCompleted(frame.Hero,frame.Manager);frame.Frame++;library.Tick(1.1f);frame.Frame++;library.Tick(1.2f);

        library.Tick(2);

        Assert.Equal(1,commits);Assert.Equal(1,restores);Assert.Equal(0,death.Occurrence);
        Assert.True(library.CanRefresh);
    }

    [Fact] public void Production_poll_retries_cancel_but_consumes_manual_and_OFF_changes()
    {
        var request=Request();request.Mode="ROTATE";request.RotationRun="first";
        var frame=LiveFrame();var death=new HollowKnightSkinDeathAdapter(()=>frame);int cancels=0,restores=0;
        var library=new HollowKnightSkinLibrary(()=>request,_=>new SkinApplyResult(SkinApplyStatus.Applied),
            ()=>{restores++;return new SkinApplyResult(SkinApplyStatus.Restored);},_=>true,null,death,(_,__)=>true,
            run=>{cancels++;return false;});
        library.Tick(0);death.OnDeath(frame.Hero,frame.Manager);frame.Dead=true;frame.Frame++;library.Tick(.1f);
        frame.SaveId++;frame.Frame++;library.Tick(.2f);Assert.Equal("first",death.CancellationRun);
        library.Tick(1);library.Tick(2);Assert.Equal(2,cancels);
        request.RotationRun="manual";library.Tick(3);Assert.Equal("manual",death.Run);Assert.Null(death.CancellationRun);
        request.Mode="OFF";request.RotationRun=null;library.Invalidate();library.Tick(4);Assert.Null(death.Run);Assert.Equal(1,restores);
        library.Dispose();library.Tick(5);Assert.Equal(1,restores);
    }
    [Fact] public void Pending_restore_required_recovers_then_retries_without_advancing_candidate()
    {
        var request=Request();request.Mode="ROTATE";request.PendingOccurrence=1;request.RotationRun="run";
        int applies=0,restores=0;SkinLibraryObservation seen=null;
        var controller=new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules,()=>request,_=>new SkinApplyResult(++applies==1?SkinApplyStatus.RestoreFailed:SkinApplyStatus.Applied),
            ()=>new SkinApplyResult(++restores==1?SkinApplyStatus.RestoreFailed:SkinApplyStatus.Restored),x=>seen=x,null,_=>true);
        controller.Tick();Assert.Equal("RestoreFailed",seen.Status);
        controller.Tick();Assert.Equal(1,applies);Assert.Equal(1,restores);Assert.Equal("RestoreFailed",seen.Status);
        controller.Tick();Assert.Equal(1,applies);Assert.Equal(2,restores);Assert.Equal("AwaitingTargets",seen.Status);
        controller.Tick();Assert.Equal(2,applies);Assert.Equal("Applied",seen.Status);Assert.Equal(1,seen.PendingOccurrence);
    }
    [Fact] public void Accepted_rotation_report_allows_second_real_death_before_next_poll()
    {
        using var r=new AcknowledgementRig();r.FirstRespawn();
        Assert.Equal(1,r.Commits);Assert.Equal(0,r.Request.PendingOccurrence);
        r.Death.OnDeath(r.Frame.Hero,r.Frame.Manager);r.Frame.Dead=true;r.Tick(2.1f);
        Assert.Equal(2,r.Death.Occurrence); // no completion-consumption poll between the two deaths
        r.Tick(3);Assert.Equal(2,r.Confirms);Assert.Equal(2,r.Request.LastDeath);Assert.Equal(2,r.Request.PendingOccurrence);
        r.Respawn(3.1f,3.2f);Assert.Equal(1,r.Commits);r.Tick(4);
        Assert.Equal(2,r.Commits);Assert.Equal(0,r.Death.Occurrence);Assert.Equal(0,r.Request.PendingOccurrence);
        Assert.False(r.Death.Ready);Assert.True(r.Library.CanRefresh);Assert.Equal(3,r.Applies);
    }
    [Fact]
    public void Matching_default_only_noop_completes_without_respawn_or_extra_poll()
    {
        using var r=new AcknowledgementRig();r.NoOp=true;
        r.Request.Vanilla=true;r.Request.PackId=null;r.Request.TreeSha256=null;
        r.Request.Root=null;r.Request.Textures=null;
        r.Request.RotationDetail="Only the default skin is eligible; unchanged";
        r.FirstDeath();Assert.Equal(1,r.Request.LastDeath);Assert.Equal(0,r.Request.PendingOccurrence);
        Assert.Equal(0,r.Death.Occurrence);Assert.False(r.Death.Ready);Assert.Equal(0,r.Applies);
        r.Death.OnDeath(r.Frame.Hero,r.Frame.Manager);r.Tick(1.1f); // duplicate callback while still dead
        Assert.Equal(0,r.Death.Occurrence);Assert.Equal(1,r.Confirms);
        r.Frame.Dead=false;r.Death.OnDeath(r.Frame.Hero,r.Frame.Manager);r.Frame.Dead=true;r.Tick(1.2f);
        Assert.Equal(2,r.Death.Occurrence);r.Tick(2);
        Assert.Equal(2,r.Confirms);Assert.Equal(2,r.Request.LastDeath);Assert.Equal(0,r.Death.Occurrence);
    }
    [Theory]
    [InlineData("rejected")]
    [InlineData("busy")]
    [InlineData("stale")]
    [InlineData("failed")]
    [InlineData("awaiting")]
    [InlineData("not-live")]
    public void Noncompletion_report_keeps_exact_pending_occurrence(string outcome)
    {
        using var r=new AcknowledgementRig();r.Outcome=outcome;r.FirstRespawn();
        Assert.Equal(0,r.Commits);Assert.Equal(1,r.Death.Occurrence);Assert.Equal(1,r.Request.PendingOccurrence);
        r.Death.OnDeath(r.Frame.Hero,r.Frame.Manager);r.Frame.Dead=true;r.Tick(2.1f);r.Tick(3);
        Assert.Equal(1,r.Confirms);Assert.Equal(1,r.Request.LastDeath);Assert.Equal(1,r.Request.PendingOccurrence);
        Assert.Equal(1,r.Death.Occurrence);Assert.False(r.Death.Ready);
    }
    [Fact] public void Unacknowledged_noop_does_not_consume_occurrence_from_stale_marker()
    {
        using var r=new AcknowledgementRig();r.NoOp=true;r.ConfirmAccepted=false;r.FirstDeath();
        Assert.Equal(0,r.Request.LastDeath);Assert.Equal(1,r.Death.Occurrence);
        r.Respawn(1.1f,1.2f);r.Tick(2);
        Assert.Equal(0,r.Request.LastDeath);Assert.Equal(1,r.Death.Occurrence);Assert.False(r.Death.Recorded);
    }
    [Fact] public void Accepted_old_run_report_cannot_clear_reentrant_new_run_occurrence()
    {
        using var r=new AcknowledgementRig();r.AfterAccepted=()=>{
            r.Death.Configure("ROTATE","new-run");r.Death.OnDeath(r.Frame.Hero,r.Frame.Manager);
            r.Frame.Dead=true;r.Frame.Frame++;r.Death.Tick();
        };
        r.FirstRespawn();Assert.Equal(1,r.Commits);Assert.Equal("new-run",r.Death.Run);
        Assert.Equal(1,r.Death.Occurrence);Assert.False(r.Death.Recorded);Assert.False(r.Library.CanRefresh);
    }
    [Theory]
    [InlineData("recover")]
    [InlineData("retire")]
    [InlineData("cancel")]
    [InlineData("stale")]
    [InlineData("cancelled-selection")]
    public void Owner_tick_retries_executed_rotation_evidence_after_selection_commits_or_releases_retired_authority(string edge)
    {
        using var r = new AcknowledgementRig(); r.FailEvidenceAfterCommit = true; r.FirstRespawn();
        Assert.Equal(1, r.Commits); Assert.Equal(0, r.Request.PendingOccurrence);
        Assert.Equal(1, r.Death.Occurrence); Assert.Null(r.CompletedEvidence);
        int applies = r.Applies;
        if (edge == "retire")
        {
            r.Request.Mode = "OFF"; r.Request.RotationRun = null; r.Request.LastDeath = 0;
            r.Library.Invalidate();
        }
        else if (edge == "cancel")
        {
            r.Frame.SaveId++;
            r.Request.RotationRun = "replacement-run"; r.Request.LastDeath = 0;
        }
        else if (edge == "stale")
        {
            r.Request.PendingOccurrence = 1; // same run/occurrence, different request (e.g. queued death)
            r.FailEvidenceAfterCommit = false;
        }
        else if (edge == "cancelled-selection")
            r.Request.PackId = "a"; // same-run cancellation retained the previous selected skin
        r.Tick(3);
        if (edge == "recover")
        {
            Assert.Equal(1, r.RetriedEvidence);
            Assert.Equal(new string('a', 64), r.CompletedEvidence.ConfigSha256);
            Assert.Equal(1, r.CompletedEvidence.PendingOccurrence);
            Assert.Equal("Applied", r.CompletedEvidence.Status);
            Assert.Equal("b", r.CompletedEvidence.ActivePackId);
            Assert.Equal(0, r.Death.Occurrence);
            r.Tick(4); Assert.Equal(1, r.RetriedEvidence); Assert.Equal(applies, r.Applies);
        }
        else
        {
            Assert.Equal(0, r.RetriedEvidence);
            Assert.Equal(0, r.Death.Occurrence);
            r.Tick(4); Assert.Equal(0, r.RetriedEvidence);
        }
    }

    sealed class AcknowledgementRig : IDisposable
    {
        public readonly SkinLibraryRequest Request=HollowKnightSkinLibraryTests.Request();
        public readonly SkinDeathFrame Frame=LiveFrame();
        public readonly HollowKnightSkinDeathAdapter Death;
        public readonly HollowKnightSkinLibrary Library;
        public int Confirms,Commits,Applies;
        public bool NoOp,ConfirmAccepted=true,FailEvidenceAfterCommit;
        public int RetriedEvidence;
        public SkinLibraryObservation CompletedEvidence;
        SkinLibraryObservation committedReport;
        public string Outcome="accepted";
        public Action AfterAccepted;
        string selected="a";
        public AcknowledgementRig()
        {
            Request.Mode="ROTATE";Request.RotationRun=new string('d',32);
            Death=new HollowKnightSkinDeathAdapter(()=>Frame);
            Library=new HollowKnightSkinLibrary(()=>Request,_=>{
                Applies++;
                if(Request.PendingOccurrence>0) {
                    if(Outcome=="not-live")Frame.Paused=true;
                    if(Outcome=="failed")return new SkinApplyResult(SkinApplyStatus.Failed);
                    if(Outcome=="awaiting")return new SkinApplyResult(SkinApplyStatus.AwaitingTargets);
                }
                return new SkinApplyResult(SkinApplyStatus.Applied);
            },()=>new SkinApplyResult(SkinApplyStatus.Restored),observation=>{
                if (committedReport != null && observation.PendingOccurrence == committedReport.PendingOccurrence &&
                    observation.ConfigSha256 == committedReport.ConfigSha256 && observation.RotationRun == Request.RotationRun)
                {
                    RetriedEvidence++; CompletedEvidence = observation; return true;
                }
                if(observation.PendingOccurrence==0 || (observation.Status!="Applied"&&observation.Status!="Unchanged"))return true;
                if(Outcome=="busy")throw new InvalidOperationException("report transport busy");
                if(Outcome=="rejected")return false;
                if(Outcome=="stale")Request.ConfigSha256=new string('e',64);
                if(observation.ConfigSha256!=Request.ConfigSha256 || observation.RotationRun!=Request.RotationRun ||
                    observation.PendingOccurrence!=Request.PendingOccurrence || observation.ActivePackId!=Request.PackId)return false;
                Commits++;selected=observation.ActivePackId;Request.PendingOccurrence=0;AfterAccepted?.Invoke();
                if (FailEvidenceAfterCommit)
                {
                    committedReport = observation; Request.ConfigSha256 = new string('f', 64);
                    return false; // durable selection changed, bounded evidence write failed
                }
                return true;
            },()=>new SkinApplyResult(SkinApplyStatus.Unchanged),Death,(run,occurrence)=>{
                if(!ConfirmAccepted)return false;
                Confirms++;Request.LastDeath=occurrence;
                if(!NoOp){Request.PendingOccurrence=occurrence;Request.PackId=selected=="a"?"b":"a";}
                return true;
            },_=>true);
        }
        public void Tick(float time){Frame.Frame++;Library.Tick(time);}
        public void FirstDeath(){Tick(0);Death.OnDeath(Frame.Hero,Frame.Manager);Frame.Dead=true;Tick(.1f);Tick(1);}
        public void Respawn(float first,float second){Frame.Dead=false;Death.HeroInPosition(Frame.Hero,Frame.Manager);Death.SceneCompleted(Frame.Hero,Frame.Manager);Tick(first);Tick(second);}
        public void FirstRespawn(){FirstDeath();Respawn(1.1f,1.2f);Tick(2);}
        public void Dispose()=>Library.Dispose();
    }
    static SkinDeathFrame LiveFrame()=>new SkinDeathFrame {Hero=new object(),Manager=new object(),Hud=new object(),SaveId=1,
        MapZone="CROSSROADS",Gameplay=true,Playing=true,InPosition=true,WaitingToTransition=true,AcceptingInput=true,TargetsAvailable=true};
    sealed class RecoveryDecoder : ISkinTextureDecoder
    {
        public SkinTexture Decode(byte[] bytes, int width, int height) => new SkinTexture(new object(), width, height, () => { }, () => true);
    }
    static SkinLibraryRequest Request() => new SkinLibraryRequest {
        ProfileId = "hollow-knight", ConfigSha256 = new string('a', 64), Mode = "ON", SpriteScope = "ALL",
        PackId = "a", TreeSha256 = new string('b', 64),
        Root = System.IO.Path.GetTempPath(), Textures = new Dictionary<string, string> { ["Knight.png"] = "assets/a" }
    };
}
