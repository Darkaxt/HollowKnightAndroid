using SsShellContracts;
using SsShellContracts.Engine;
using Xunit;
using static SsShellContracts.FixtureAccess;
using Object=SsShellContracts.Engine.Object;
using SsManager=SsShellContracts.GameManager;
using GameCameras=SsShellContracts.GameCameras;
using GameMap=SsShellContracts.GameMap;
using CameraRenderToMesh=SsShellContracts.CameraRenderToMesh;
namespace SharedPatches.Tests;

[Collection("SS production shell")]
public class SilksongMapDiscoveryPerformanceTests:IDisposable
{
    readonly SsManager oldManager=SsManager.SilentInstance;
    readonly GameCameras oldCameras=GameCameras.instance;
    readonly Object[] oldResident=Resources.Resident;
    readonly DsMapView view=new(new GameObject("map-host").transform);
    public SilksongMapDiscoveryPerformanceTests()
    {
        Resources.Resident=Array.Empty<Object>();GameCameras.instance=new();
        SsManager.SilentInstance=new(){gameMap=new GameObject("map").AddComponent<GameMap>(),sceneName="room-a"};
        Time.frameCount+=100;view.Build(900,700);
    }
    public void Dispose(){SsManager.SilentInstance=oldManager;GameCameras.instance=oldCameras;Resources.Resident=oldResident;}
    static Camera Source(float near,float far)
    {
        var go=new GameObject("source");go.AddComponent<CameraRenderToMesh>();
        var camera=go.AddComponent<Camera>();camera.nearClipPlane=near;camera.farClipPlane=far;return camera;
    }
    static void Pair()
    {
        var rooms=Source(42,50);var decor=Source(30,42);
        Resources.Resident=new Object[]{rooms.GetComponent<CameraRenderToMesh>(),decor.GetComponent<CameraRenderToMesh>()};
    }
    [Fact]
    public void Missing_pair_has_bounded_retries_and_context_loss_still_recovers()
    {
        Assert.False(view.PollSources());int scans=EngineBoundary.Discovery;
        view.Texture.Release();
        for(int i=0;i<29;i++){Time.frameCount++;Assert.False(view.PollSources());}
        Assert.Equal(scans,EngineBoundary.Discovery);Assert.True(view.Texture.IsCreated());Assert.Equal(1,view.Texture.Creates);
        Pair();Time.frameCount++;Assert.True(view.PollSources());
        scans=EngineBoundary.Discovery;
        for(int i=0;i<90;i++){Time.frameCount++;Assert.True(view.PollSources());}
        Assert.Equal(scans,EngineBoundary.Discovery);
    }
    [Theory]
    [InlineData("scene")]
    [InlineData("map")]
    [InlineData("camera-owner")]
    [InlineData("hud-owner")]
    [InlineData("lost-map")]
    public void Actual_owner_recovery_rearms_before_retry_deadline(string edge)
    {
        Assert.False(view.PollSources());int scans=EngineBoundary.Discovery;
        Time.frameCount++;
        if(edge=="scene")SsManager.SilentInstance.sceneName="room-b";
        if(edge=="map")SsManager.SilentInstance.gameMap=new GameObject("new-map").AddComponent<GameMap>();
        if(edge=="camera-owner")GameCameras.instance=new();
        if(edge=="hud-owner")GameCameras.instance.hudCamera=new GameObject("new-hud").AddComponent<Camera>();
        if(edge=="lost-map")
        {
            var map=SsManager.SilentInstance.gameMap;SsManager.SilentInstance.gameMap=null;
            Assert.False(view.PollSources());SsManager.SilentInstance.gameMap=map;
        }
        Pair();Assert.True(view.PollSources());Assert.Equal(scans+1,EngineBoundary.Discovery);
    }
    [Fact]
    public void Retired_camera_pair_rebinds_immediately_then_settles_again()
    {
        Pair();Assert.True(view.PollSources());
        Field<Camera>(view,"_srcRooms").Retired=true;Resources.Resident=Array.Empty<Object>();
        Time.frameCount++;Assert.False(view.PollSources());int scans=EngineBoundary.Discovery;
        for(int i=0;i<5;i++){Time.frameCount++;Assert.False(view.PollSources());}
        Assert.Equal(scans,EngineBoundary.Discovery);
        SsManager.SilentInstance.sceneName="new-room";Pair();Assert.True(view.PollSources());
        Assert.NotNull(Field<Camera>(view,"_srcRooms"));Assert.NotNull(Field<Camera>(view,"_srcDecor"));
    }
}
