using SsShellContracts;
using SsShellContracts.Engine;
using Xunit;
using static SsShellContracts.FixtureAccess;
namespace SharedPatches.Tests;

public partial class SilksongGameplayPerformanceTests
{
    [Fact]
    public void Settled_real_render_callbacks_do_not_revalidate_the_whole_native_hierarchy()
    {
        DsTouch.Ready=true; DsGameData.InGame=true;
        var canvas=new GameObject("secondary-canvas").AddComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.targetDisplay=DsPresentation.DISPLAY;
        var host=new GameObject("hud-host").AddComponent<RectTransform>();host.SetParent(canvas.transform);
        var hud=new GameObject("hud-owner").AddComponent<DsHudView>();hud.Build(host,900,230);hud.SetVisible(true);
        var primary=new GameObject("primary").AddComponent<Camera>();primary.enabled=true;primary.targetDisplay=0;primary.cullingMask=1<<5;
        try
        {
            var root=Field<Transform>(hud,"_hudRoot");
            for(int i=0;i<32;i++){var node=new GameObject("resident"+i);node.transform.SetParent(root);}
            var late=Delegate(hud,"LateUpdate");var pre=Delegate<Camera>(hud,"BeforeCamera");var post=Delegate<Camera>(hud,"AfterCamera");
            var capture=Field<Camera>(hud,"_capture");var canvasScope=Field<DsHudRenderScope<GameObject>>(hud,"_canvasScope");
            Time.frameCount++;late();pre(capture);post(capture);canvasScope.Restore();
            for(int i=0;i<20;i++)
            {
                Time.frameCount++;Transform.ParentReads=Transform.ChildCountReads=0;int queries=Component.InventoryQueries;
                late();pre(capture);post(capture);pre(primary);post(primary);canvasScope.Restore();
                Assert.Equal(1<<5,primary.cullingMask);Assert.Equal(5,hud.NativeTargets[0].layer);
                Assert.Equal(queries,Component.InventoryQueries);
                // Canvas ancestry is live and legitimate. These bounds target only
                // duplicated full-inventory validation, not every native scalar read.
                Assert.Equal(0,Transform.ChildCountReads);
                Assert.InRange(Transform.ParentReads,0,20);
                Assert.Equal(Time.frameCount,Field<int>(hud,"_hiddenFrame"));
            }
        }
        finally {hud.Stop();}
    }
}
