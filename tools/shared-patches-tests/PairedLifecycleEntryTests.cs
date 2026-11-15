using Xunit;

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

    }
}
