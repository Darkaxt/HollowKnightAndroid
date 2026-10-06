using Xunit;
using Hk = HkPauseContracts;

namespace HkPauseContracts
{
    [Collection("HollowKnightPauseOwners")]
    public class HollowKnightLifecycleEntryTests
    {
        static HKDualScreen StartOwner()
        {
            Assert.Null(HKDualScreen.LifecycleOwner);
            var owner=HKDualScreen.EnsureStartedStep();
            owner.LifecycleStartup=true;
            return owner;
        }

        [Fact]
        public void Android_boot_is_dormant_and_explicit_start_binds_one_measured_owner()
        {
            Application.platform=RuntimePlatform.Android;
            HKDualScreen.BootStep();Assert.Null(HKDualScreen.LifecycleOwner);
            var owner=StartOwner();
            try
            {
                Assert.True(owner.gameObject.Persistent);
                Assert.Same(owner,HKDualScreen.EnsureStartedStep());
                owner.transport=null;
                var adapter=new Transport { Owner=owner,PanelWidth=1600,PanelHeight=900 };
                owner.BindStep(adapter);owner.BindStep(adapter);
                Assert.Same(adapter,owner.transport);
                Assert.Throws<InvalidOperationException>(()=>owner.BindStep(new Transport()));
                Assert.Equal(1600,owner.bottomWidth);Assert.Equal(900,owner.bottomHeight);
                foreach(var camera in new[]{owner.clearCam,owner.attrCam,owner.hudCam2,owner.promptCam})
                    Assert.Equal(1600f/900,camera.aspect);
                var routine=owner.StartStep();Assert.True(routine.MoveNext());
                Assert.Equal(1,owner.CameraSetups);Assert.Equal(1,owner.LogoSetups);Assert.Equal(1,owner.ForcedConfigLoads);
                var eof=routine.Current;Assert.IsType<WaitForEndOfFrame>(eof);
                Assert.True(routine.MoveNext());Assert.Same(eof,routine.Current);
            }
            finally { owner.ShutdownStep(); }
            Assert.Null(HKDualScreen.LifecycleOwner);
        }

        [Fact]
        public void Startup_waits_for_transport_and_shutdown_cancels_without_camera_setup()
        {
            var owner=StartOwner();owner.transport=null;
            var routine=owner.StartStep();
            Assert.True(routine.MoveNext());Assert.Null(routine.Current);
            Assert.Equal(0,owner.CameraSetups);Assert.Equal(0,owner.LogoSetups);
            owner.ShutdownStep();Assert.False(routine.MoveNext());
            Assert.Null(HKDualScreen.LifecycleOwner);
        }

        [Fact]
        public void Actual_frame_entry_retries_failed_display_restore_before_gameplay_and_reactivates_same_owner()
        {
            var owner=StartOwner();var adapter=owner.transport;
            try
            {
                var routine=owner.StartStep();Assert.True(routine.MoveNext());
                owner.ThrowNextInputRelease=true;
                Assert.Throws<InvalidOperationException>(()=>owner.LoseDisplay());
                Assert.True(owner.directDisplayRestorePending);
                int updates=owner.Updates;
                owner.ThrowNextInputRelease=true;
                Assert.True(routine.MoveNext());Assert.True(owner.directDisplayRestorePending);
                Assert.Equal(updates,owner.Updates);Assert.Equal(0,adapter.RestoreCompleted);
                Assert.Contains(Debug.Logs,x=>x.Contains("native input release unavailable"));
                Assert.True(routine.MoveNext());Assert.False(owner.directDisplayRestorePending);
                Assert.Equal(1,adapter.RestoreCompleted);Assert.Same(owner,HKDualScreen.LifecycleOwner);
                foreach(var camera in new[]{owner.clearCam,owner.attrCam,owner.hudCam2,owner.promptCam}) Assert.False(camera.enabled);
                owner.DisplayStep(true);Assert.True(routine.MoveNext());
                Assert.True(owner.directDisplayActive);Assert.Same(adapter,owner.transport);
                Assert.True(owner.Updates>updates);
            }
            finally { owner.ShutdownStep(); }
        }

        [Fact]
        public void Final_restore_failure_retains_owner_until_actual_frame_retry_then_allows_fresh_owner()
        {
            var owner=StartOwner();var adapter=owner.transport;var root=owner.gameObject;
            var routine=owner.StartStep();Assert.True(routine.MoveNext());
            owner.ThrowNextInputRelease=true;
            Assert.Throws<InvalidOperationException>(()=>owner.ShutdownStep());
            Assert.True(owner.directDisplayFinalTeardownPending);Assert.True(owner.directDisplayRestorePending);
            Assert.Same(owner,HKDualScreen.EnsureStartedStep());Assert.Equal(0,adapter.TeardownCompleted);
            Assert.True(routine.MoveNext());Assert.False(routine.MoveNext());
            Assert.True(owner.directDisplayShuttingDown);Assert.False(owner.directDisplayFinalTeardownPending);
            Assert.Equal(1,adapter.TeardownCompleted);Assert.Same(owner,adapter.CompletedOwner);
            Assert.Null(owner.transport);Assert.Null(HKDualScreen.LifecycleOwner);
            Assert.Contains(root,owner.Destroyed);
            int destroyed=owner.Destroyed.Count;owner.ShutdownStep();Assert.Equal(destroyed,owner.Destroyed.Count);
            var fresh=StartOwner();Assert.NotSame(owner,fresh);fresh.ShutdownStep();
        }
    }
}

namespace SsShellContracts
{
    public partial class SilksongShellProductionTests
    {
        [Fact]
        public void Lifecycle_display_loss_rebind_uses_actual_presentation_without_duplicate_shell_or_rig()
        {
            using var live=new Live();
            var screen=FixtureAccess.Field<DsPresentation>(live.Owner,"_screen");
            var root=screen.Root;var camera=screen.Camera;var shell=live.Shell;
            Assert.Equal(6,screen.ContentLayer);Assert.Equal(3,screen.OverlayLayer);
            Assert.Equal(1,Engine.Display.displays[1].Activations);
            Engine.GameObject.LastAdded=null;
            FixtureAccess.Call(live.Owner,"Bootstrap");Assert.Null(Engine.GameObject.LastAdded);
            Assert.Same(live.Owner,DualScreenV2.Instance);
            Engine.Display.displays=new[]{new Engine.Display()};Engine.Display.Publish();
            Assert.False(screen.Ready);Assert.False(screen.Camera.enabled);Assert.False(screen.Canvas.enabled);
            Assert.False(FixtureAccess.Field<DualSouls.DualScreen.DirectDisplayHost>(live.Owner,"_host").IsActive);
            var panel=new Engine.Display();Engine.Display.displays=new[]{new Engine.Display(),panel};Engine.Display.Publish();
            EngineBoundary.DrainBringup();live.Update();
            Assert.True(screen.Ready);Assert.Equal(1,panel.Activations);
            Assert.Same(root,screen.Root);Assert.Same(camera,screen.Camera);Assert.Same(shell,live.Shell);
            Assert.True(FixtureAccess.Field<DualSouls.DualScreen.DirectDisplayHost>(live.Owner,"_host").IsActive);
        }

        [Fact]
        public void Lifecycle_pause_resume_restores_on_first_eligible_entry_without_rebuilding_or_extra_activation()
        {
            using var live=new Live();var screen=FixtureAccess.Field<DsPresentation>(live.Owner,"_screen");
            var host=FixtureAccess.Field<DualSouls.DualScreen.DirectDisplayHost>(live.Owner,"_host");
            var root=screen.Root;var shell=live.Shell;
            FixtureAccess.Call(live.Owner,"OnApplicationPause",true);
            Assert.False(host.IsActive);Assert.False(screen.Camera.enabled);Assert.False(screen.Canvas.enabled);
            FixtureAccess.Call(live.Owner,"OnApplicationPause",false);live.Update();
            Assert.True(host.IsActive);Assert.True(screen.Camera.enabled);Assert.True(screen.Canvas.enabled);
            Assert.Same(root,screen.Root);Assert.Same(shell,live.Shell);
            Assert.Equal(1,Engine.Display.displays[1].Activations);
            live.Manager.Pause(true);live.Update();LogoOnly(live);
            live.Manager.Pause(false);live.Update();
            Assert.True(((Engine.RectTransform)root.Find("body")).gameObject.activeInHierarchy);
        }

        [Fact]
        public void Lifecycle_profile_retirement_releases_real_rig_and_events_without_retiring_other_profile()
        {
            var hk=Hk.HKDualScreen.EnsureStartedStep();hk.LifecycleStartup=true;
            try
            {
                using(var live=new Live())
                {
                    var screen=FixtureAccess.Field<DsPresentation>(live.Owner,"_screen");
                    var camera=screen.Camera.gameObject;var canvas=screen.Canvas.gameObject;
                    FixtureAccess.Call(live.Owner,"Shutdown");FixtureAccess.Call(live.Owner,"Shutdown");
                    Assert.Null(DualScreenV2.Instance);Assert.Null(screen.Root);Assert.False(screen.Ready);
                    Assert.True(camera.Destroyed);Assert.True(canvas.Destroyed);
                    Assert.Equal(0,live.Manager.PauseSubscriptions);
                    Assert.Same(hk,Hk.HKDualScreen.LifecycleOwner);
                }
                using var fresh=new Live();Assert.Same(fresh.Owner,DualScreenV2.Instance);
                hk.ShutdownStep();Assert.Same(fresh.Owner,DualScreenV2.Instance);
                Assert.True(FixtureAccess.Field<DsPresentation>(fresh.Owner,"_screen").Ready);
            }
            finally { hk.ShutdownStep(); }
        }
    }
}
