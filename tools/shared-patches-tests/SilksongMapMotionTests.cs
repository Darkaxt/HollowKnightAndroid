using System.Reflection;
using SsShellContracts;
using SsShellContracts.Engine;
using Xunit;
using static SsShellContracts.FixtureAccess;
using GameMap=SsShellContracts.GameMap;
namespace SharedPatches.Tests;

// #45: the Silksong lower map moves like Hollow Knight's: a pinch zooms about the
// fingers, a released drag glides to a stop, and RESET eases back to the start.
[Collection("SS production shell")]
public class SilksongMapMotionTests
{
    readonly DsMapView view=new(new GameObject("map-host").transform);

    void Animate(float dt)=>typeof(DsMapView).GetMethod("Animate",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,new object[]{dt});

    [Fact]
    public void PinchKeepsThePointUnderTheFingersInPlace()
    {
        Set(view,"_mapUnitsPerPixel",0.01f);
        var fingers=new Vector2(100f,40f);
        Vector2 before=Field<Vector2>(view,"_pan")+fingers*0.01f;
        view.Zoom(2f,fingers);
        Vector2 after=Field<Vector2>(view,"_pan")+fingers*Field<float>(view,"_mapUnitsPerPixel");
        Assert.Equal(2f,view.ZoomLevel,3);
        Assert.Equal(before.x,after.x,4);Assert.Equal(before.y,after.y,4);
    }

    [Fact]
    public void AFlingGlidesInTheDragDirectionAndStops()
    {
        Set(view,"_mapUnitsPerPixel",0.01f);
        view.Fling(new Vector2(2000f,0f));
        Animate(0.016f);
        float first=Field<Vector2>(view,"_pan").x;
        Assert.True(first<0f);   // dragged right: the camera goes left, as Pan does
        for(int i=0;i<200;i++)Animate(0.016f);
        Assert.Equal(Vector2.zero,Field<Vector2>(view,"_fling"));
        float settled=Field<Vector2>(view,"_pan").x;
        Animate(0.016f);
        Assert.Equal(settled,Field<Vector2>(view,"_pan").x);
    }

    static Vector2 Clamp(Vector2 pan,float half,float aspect,bool world,float limit)=>
        (Vector2)typeof(DsMapView).GetMethod("ClampPan",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{pan,half,aspect,world,limit})!;

    [Fact]
    public void AFlingCannotCarryTheAreaMapOffThePanel()
    {
        // Thor, 2.0.47: one quick swipe threw Moss Grotto clean off the panel,
        // because the area map shared the full map's 60-unit radius.
        var held=Clamp(new Vector2(-40f,25f),2f,1.25f,false,60f);
        Assert.Equal(-2.5f,held.x,4);Assert.Equal(2f,held.y,4);
        var inside=Clamp(new Vector2(1f,-0.5f),2f,1.25f,false,60f);
        Assert.Equal(1f,inside.x,4);Assert.Equal(-0.5f,inside.y,4);
        // The full map is the whole world and keeps its wide radius.
        Assert.Equal(-40f,Clamp(new Vector2(-40f,0f),2f,1.25f,true,60f).x,4);
    }

    [Fact]
    public void TouchingTheMapStopsAGlide()
    {
        view.Fling(new Vector2(2000f,0f));
        view.StopMotion();
        Animate(0.016f);
        Assert.Equal(Vector2.zero,view.PanState);
    }

    [Fact]
    public void ResetEasesBackToTheStartAndADragCancelsIt()
    {
        view.Pan(new Vector2(-50f,30f));view.SetZoom(3f);
        view.ResetAnimated();
        Animate(0.1f);
        Assert.True(view.ZoomLevel<3f&&view.ZoomLevel>1f);
        Assert.NotEqual(Vector2.zero,view.PanState);
        for(int i=0;i<10;i++)Animate(0.1f);
        Assert.Equal(1f,view.ZoomLevel);Assert.Equal(Vector2.zero,view.PanState);
        Assert.False(view.ViewMoved);

        view.Pan(new Vector2(-50f,0f));view.ResetAnimated();Animate(0.05f);
        view.Pan(new Vector2(-10f,0f));
        Vector2 held=view.PanState;
        Animate(0.1f);
        Assert.Equal(held,view.PanState);
    }

    [Fact]
    public void ResetOnTheWholeMapRecentresOnTheHeroBeforeEasing()
    {
        var map=new GameObject("map").AddComponent<GameMap>();
        Set(view,"_map",map);
        view.Mode=DsMapView.Frame.World;Set(view,"_worldLatched",true);
        Set(view,"_worldCentreLocal",new Vector3(0f,0f,0f));
        view.NextWorldCentre=new Vector3(4f,-2f,0f);
        view.ResetAnimated();
        var centre=Field<Vector3>(view,"_worldCentreLocal");
        Assert.Equal(4f,centre.x);Assert.Equal(-2f,centre.y);
        // The view stays put this frame: the pan absorbs the re-latch, then eases out.
        Assert.Equal(new Vector2(-4f,2f),view.PanState);
        for(int i=0;i<10;i++)Animate(0.1f);
        Assert.Equal(Vector2.zero,view.PanState);
    }
}
