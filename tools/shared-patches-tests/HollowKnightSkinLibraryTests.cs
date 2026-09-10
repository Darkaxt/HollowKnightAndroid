using System;
using System.Collections.Generic;
using DualSouls.Skins.HollowKnight.Runtime;
using Xunit;

public class HollowKnightSkinLibraryTests
{
    [Fact] public void ProductionControllerBoundaryExists()
    {
        Assert.NotNull(typeof(SkinRuntimeSession).Assembly.GetType("DualSouls.Skins.HollowKnight.Runtime.SkinLibraryRuntimeController"));
    }

    [Fact] public void OnAndRotateReuseImmutablePackAndOffRestoresOnce()
    {
        var request = Request(); var applied = new List<SkinPack>(); int restores = 0;
        var controller = new SkinLibraryRuntimeController(() => request,
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
        var controller = new SkinLibraryRuntimeController(() => busy ? null : request, pack => {
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

    [Fact] public void BusyAndReadFailureKeepWorkingVisualAndRetry()
    {
        var request = Request(); int reads = 0, applies = 0, restores = 0;
        var controller = new SkinLibraryRuntimeController(() => ++reads == 2 ? null : reads == 3 ? throw new InvalidOperationException("busy io") : request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => { restores++; return new SkinApplyResult(SkinApplyStatus.Restored); }, _ => { });
        controller.Tick(); controller.Tick(); controller.Tick(); controller.Tick();
        Assert.Equal(1, applies); Assert.Equal(0, restores);
    }
    [Fact] public void FailedCandidateAndAwaitingTargetsAreRetriedWithoutClaimingActive()
    {
        var request = Request(); int attempts = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(() => request,
            _ => new SkinApplyResult(++attempts == 1 ? SkinApplyStatus.AwaitingTargets : attempts == 2 ? SkinApplyStatus.Failed : SkinApplyStatus.Applied),
            () => throw new Exception("must not restore working visual"), result => observed = result);
        controller.Tick(); Assert.Null(observed.ActivePackId);
        controller.Tick(); Assert.Null(observed.ActivePackId);
        controller.Tick(); Assert.Equal("a", observed.ActivePackId); Assert.Equal(3, attempts);
    }
    [Fact] public void RestoreFailureRemainsVisibleAndRetries()
    {
        var request = Request(); int restores = 0; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(() => request, _ => new SkinApplyResult(SkinApplyStatus.Applied),
            () => new SkinApplyResult(++restores == 1 ? SkinApplyStatus.RestoreFailed : SkinApplyStatus.Restored), x => observed = x);
        controller.Tick(); request.Mode = "OFF"; controller.Tick(); Assert.Equal("a", observed.ActivePackId);
        controller.Tick(); Assert.Null(observed.ActivePackId); Assert.Equal(2, restores);
    }
    [Fact] public void WrongProfileAndUnsafeModeDoNotTouchVisuals()
    {
        var request = Request(); request.ProfileId = "silksong"; int writes = 0;
        var controller = new SkinLibraryRuntimeController(() => request, _ => { writes++; return null; }, () => { writes++; return null; }, _ => { });
        controller.Tick(); request.ProfileId = "hollow-knight"; request.Mode = "BOGUS"; controller.Tick(); Assert.Equal(0, writes);
    }
    [Fact] public void CachedSelectionDoesNotHideRuntimeRefreshFailure()
    {
        SkinLibraryObservation observation = null;
        var current = new SkinApplyResult(SkinApplyStatus.Applied);
        var controller = new SkinLibraryRuntimeController(Request, _ => current,
            () => new SkinApplyResult(SkinApplyStatus.Restored), x => observation = x, () => current);
        controller.Tick(); current = new SkinApplyResult(SkinApplyStatus.Failed, "refresh failed"); controller.Tick();
        Assert.Equal("Failed", observation.Status); Assert.Equal("refresh failed", observation.Detail);
        Assert.Equal("a", observation.ActivePackId);
    }
    [Fact] public void FailedReplacementReportsLastWorkingPackUntilRetrySucceeds()
    {
        var request = Request(); SkinLibraryObservation observation = null; int attempts = 0;
        var controller = new SkinLibraryRuntimeController(() => request,
            _ => new SkinApplyResult(++attempts == 2 ? SkinApplyStatus.Failed : SkinApplyStatus.Applied),
            () => throw new Exception("must preserve working visual"), x => observation = x);
        controller.Tick(); request.PackId = "b"; request.TreeSha256 = new string('c',64); controller.Tick();
        Assert.Equal("a",observation.ActivePackId); Assert.Equal("Failed",observation.Status);
        controller.Tick(); Assert.Equal("b",observation.ActivePackId); Assert.Equal(3,attempts);
    }
    [Fact] public void ReportingFailureRetriesWithoutReapplyingOrRestoringVisuals()
    {
        int reports = 0, applies = 0;
        var controller = new SkinLibraryRuntimeController(Request,
            _ => { applies++; return new SkinApplyResult(SkinApplyStatus.Applied); },
            () => throw new Exception("must not restore"), _ => { if (++reports == 1) throw new Exception("report busy"); });
        controller.Tick(); controller.Tick(); Assert.Equal(2,reports); Assert.Equal(1,applies);
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
        using var session = new SkinRuntimeSession(new RecoveryDecoder(), () => new[] { slot });
        var request = Request(); request.Mode = "OFF"; request.Root = root;
        request.Textures = new Dictionary<string, string> { ["Knight.png"] = "sheet.png" };
        int restores = 0; SkinApplyResult current = null; SkinLibraryObservation observed = null;
        var controller = new SkinLibraryRuntimeController(() => request, pack => current = session.TryApply(pack),
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
        var controller = new SkinLibraryRuntimeController(() => request, _ => throw new InvalidOperationException("apply threw"),
            () => new SkinApplyResult(++restores == 2 ? SkinApplyStatus.RestoreFailed : SkinApplyStatus.Restored), x => observed = x);
        controller.Tick(); Assert.Equal(1, restores);
        request.Mode = "ON"; controller.Tick(); Assert.Equal("Failed", observed.Status); Assert.Equal("apply threw", observed.Detail);
        request.Mode = "OFF"; controller.Tick(); Assert.Equal(2, restores); Assert.Equal("RestoreFailed", observed.Status);
        controller.Tick(); Assert.Equal(3, restores); Assert.Equal("Restored", observed.Status);
        controller.Tick(); Assert.Equal(3, restores); Assert.Null(observed.ActivePackId);
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
        request.Mode="OFF";request.RotationRun=null;library.Tick(4);Assert.Null(death.Run);Assert.Equal(1,restores);
        library.Dispose();library.Tick(5);Assert.Equal(1,restores);
    }
    [Fact] public void Pending_restore_required_recovers_then_retries_without_advancing_candidate()
    {
        var request=Request();request.Mode="ROTATE";request.PendingOccurrence=1;request.RotationRun="run";
        int applies=0,restores=0;SkinLibraryObservation seen=null;
        var controller=new SkinLibraryRuntimeController(()=>request,_=>new SkinApplyResult(++applies==1?SkinApplyStatus.RestoreFailed:SkinApplyStatus.Applied),
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
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Matching_zero_or_one_noop_completes_without_respawn_or_extra_poll(int eligibleCount)
    {
        using var r=new AcknowledgementRig();r.NoOp=true;
        r.Request.RotationDetail=eligibleCount==0?"No eligible skins":"Only selected skin is eligible";
        r.FirstDeath();Assert.Equal(1,r.Request.LastDeath);Assert.Equal(0,r.Request.PendingOccurrence);
        Assert.Equal(0,r.Death.Occurrence);Assert.False(r.Death.Ready);Assert.Equal(1,r.Applies);
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
    sealed class AcknowledgementRig : IDisposable
    {
        public readonly SkinLibraryRequest Request=HollowKnightSkinLibraryTests.Request();
        public readonly SkinDeathFrame Frame=LiveFrame();
        public readonly HollowKnightSkinDeathAdapter Death;
        public readonly HollowKnightSkinLibrary Library;
        public int Confirms,Commits,Applies;
        public bool NoOp,ConfirmAccepted=true;
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
                if(observation.PendingOccurrence==0 || (observation.Status!="Applied"&&observation.Status!="Unchanged"))return true;
                if(Outcome=="busy")throw new InvalidOperationException("report transport busy");
                if(Outcome=="rejected")return false;
                if(Outcome=="stale")Request.ConfigSha256=new string('e',64);
                if(observation.ConfigSha256!=Request.ConfigSha256 || observation.RotationRun!=Request.RotationRun ||
                    observation.PendingOccurrence!=Request.PendingOccurrence || observation.ActivePackId!=Request.PackId)return false;
                Commits++;selected=observation.ActivePackId;Request.PendingOccurrence=0;AfterAccepted?.Invoke();return true;
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
        ProfileId = "hollow-knight", ConfigSha256 = new string('a', 64), Mode = "ON", PackId = "a", TreeSha256 = new string('b', 64),
        Root = System.IO.Path.GetTempPath(), Textures = new Dictionary<string, string> { ["Knight.png"] = "assets/a" }
    };
}
