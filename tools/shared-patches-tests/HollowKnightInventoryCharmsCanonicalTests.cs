using Xunit;
using HK = HkPauseContracts.HKDualScreen;
using HkPauseContracts;
using GameObject = HkPauseContracts.GameObject;
using Renderer = HkPauseContracts.Renderer;
using Bounds = HkPauseContracts.Bounds;
using Rect = HkPauseContracts.Rect;
using TextAlignment = HkPauseContracts.TextAlignment;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightInventoryCharmsCanonicalTests
{
    [Theory]
    [InlineData(1240,1080)]
    [InlineData(800,720)]
    [InlineData(1600,1200)]
    public void NativePaneCameraIgnoresSelectionFitAndLegacyNudges(int width,int height)
    {
        HK lower=new(); lower.BOTTOM_W=width; lower.BOTTOM_H=height;
        foreach(int id in new[]{1,2})
        {
            lower.tab.cur=id;
            lower.cfg.compInvOffX=55; lower.cfg.compCharmOffY=-55; lower.cfg.compCharmsCenterY=99;
            lower.cfg.compOffX=80;lower.cfg.compOffY=-90;
            lower.cfg.compPaneZoom=8;lower.cfg.compCharmZoom=9;
            lower.fit=new(){ valid=true,ortho=33,center=new(900,-200,0) };
            lower.FramePane(new(900,-200,0));
            Assert.Equal(height/2f,lower.attrCam.orthographicSize);
            Assert.Equal(lower.compRoot.position.x,lower.attrCam.transform.position.x);
            Assert.Equal(lower.compRoot.position.y,lower.attrCam.transform.position.y);
            lower.fit=new(){ valid=true,ortho=300,center=new(-900,200,0) };
            lower.FramePane(new(-900,200,0));
            Assert.Equal(height/2f,lower.attrCam.orthographicSize);
            Assert.Equal(lower.compRoot.position.x,lower.attrCam.transform.position.x);
            Assert.Equal(lower.compRoot.position.y,lower.attrCam.transform.position.y);
        }
    }

    [Theory]
    [InlineData(1240,1080)]
    [InlineData(800,720)]
    [InlineData(1600,1200)]
    public void CanonicalColumnsGuttersAndBodyClipsHaveExactMeasuredPlacement(int width,int height)
    {
        var g=HKLowerLayout.Measure(width,height);float sx=width/1240f;
        var inv=HKLowerLayout.NativeColumns(g,false);
        Assert.Equal(20*sx,inv.SubjectX);Assert.Equal(420*sx,inv.SubjectWidth);
        Assert.Equal(470*sx,inv.ChooserX);Assert.Equal(400*sx,inv.ChooserWidth);
        Assert.Equal(900*sx,inv.DetailX);Assert.Equal(320*sx,inv.DetailWidth);
        Assert.Equal(455*sx,inv.LeftGutter);Assert.Equal(885*sx,inv.RightGutter);
        Assert.Equal(g.HudHeight+20,inv.Top);Assert.Equal(g.BodyHeight-40,inv.Height);
        Assert.Equal((400*sx-20*sx)/3,inv.Cell);
        var charm=HKLowerLayout.NativeColumns(g,true);
        Assert.Equal(470*sx,charm.SubjectWidth);Assert.Equal(520*sx,charm.ChooserX);
        Assert.Equal(350*sx,charm.ChooserWidth);Assert.Equal(505*sx,charm.LeftGutter);
        Assert.Equal(900*sx,charm.DetailX);Assert.Equal(885*sx,charm.RightGutter);
        Assert.Equal(g.HudHeight+16,charm.Top);Assert.Equal(g.BodyHeight-36,charm.Height);
        Assert.True(charm.Top>=g.HudHeight);Assert.True(charm.Top+charm.Height<g.TabTop);
    }

    [Theory]
    [InlineData(1240,1080)]
    [InlineData(800,720)]
    [InlineData(1600,1200)]
    public void OccupiedNativeProjectionIsCenteredWithoutDetailOrInactivePadding(int width,int height)
    {
        HK lower=new();lower.BOTTOM_W=width;lower.BOTTOM_H=height;
        var occupied=new GameObject("Inv_Items");occupied.transform.SetParent(lower.compRoot);
        var art=occupied.AddComponent<Renderer>();art.BoundsSize=new(20,10,1);
        var detail=new Renderer();detail.BoundsSize=new(2000,2000,1);
        var columns=HKLowerLayout.NativeColumns(HKLowerLayout.Measure(width,height),false);
        var target=new Rect(columns.SubjectX,columns.Top,columns.SubjectWidth,columns.Height);
        lower.FitOccupiedStep(occupied.transform,new[]{art},target);
        var before=art.bounds;
        Assert.Equal(lower.compRoot.position.x+target.x+target.width/2-width/2f,before.center.x,3);
        Assert.Equal(lower.compRoot.position.y+height/2f-target.y-target.height/2,before.center.y,3);
        Assert.True(before.size.x<=target.width);Assert.True(before.size.y<=target.height);
        detail.BoundsSize=new(1,50000,1); // selected long localized prose is not an occupied-base input
        lower.FitOccupiedStep(occupied.transform,new[]{art},target);
        Assert.Equal(before.center.x,art.bounds.center.x,3);Assert.Equal(before.center.y,art.bounds.center.y,3);
        Assert.Equal(before.size.x,art.bounds.size.x,3);
    }

    [Theory]
    [InlineData(1240,1080)]
    [InlineData(800,720)]
    [InlineData(1600,1200)]
    public void NativeCharmOccupiedGridCentersInCanonicalColumnWithoutSelectedDetail(int width,int height)
    {
        HK lower=new();lower.BOTTOM_W=width;lower.BOTTOM_H=height;
        var g=HKLowerLayout.Measure(width,height);var p=HKLowerLayout.NativeColumns(g,true);
        var grid=new GameObject("CanonicalCharmChooser");grid.transform.SetParent(lower.compRoot);
        var left=new GameObject("board1");left.transform.SetParent(grid.transform);left.transform.localPosition=new(-7,3,0);
        var right=new GameObject("board40");right.transform.SetParent(grid.transform);right.transform.localPosition=new(9,-2,0);
        var a=left.AddComponent<Renderer>();var b=right.AddComponent<Renderer>();
        var target=new Rect(p.ChooserX,p.Top,p.ChooserWidth,p.Height);
        lower.FitOccupiedStep(grid.transform,new[]{a,b},target);
        var occupied=new Bounds(a.bounds.center,a.bounds.size);occupied.Encapsulate(b.bounds);
        Assert.Equal(lower.compRoot.position.y+height/2f-(p.Top+p.Height/2),occupied.center.y,3);
        Assert.Equal(lower.compRoot.position.x+p.ChooserX+p.ChooserWidth/2-width/2f,occupied.center.x,3);
        var before=occupied.center;
        lower.cfg.compCharmsCenterY=-70;lower.cfg.compCharmOffY=88;
        var label=lower.CopyLabelStep(lower.TextDonor(new(1,1,1),true),28.08f);
        lower.LabelStep(label,new string('W',2000),new(p.DetailX,p.Top+100,p.DetailWidth,p.Height-100));
        lower.FitOccupiedStep(grid.transform,new[]{a,b},target);
        occupied=new Bounds(a.bounds.center,a.bounds.size);occupied.Encapsulate(b.bounds);
        Assert.Equal(before.x,occupied.center.x,3);Assert.Equal(before.y,occupied.center.y,3);
    }

    [Fact]
    public void ProductionPaneDispatchUsesCanonicalLayoutEvenWhenLegacyRedesignIsDisabled()
    {
        HK lower=new();lower.InitializeOwnership();lower.cfg.compCharmsRedesign=0;
        lower.NativeLayoutRouteStep(1);Assert.Equal(1,lower.NativeInventoryLayouts);
        lower.NativeLayoutRouteStep(2);Assert.Equal(1,lower.NativeCharmLayouts);
        Assert.Equal(540,lower.attrCam.orthographicSize);
    }

    [Fact]
    public void CopiedNativeDetailKeepsFixedGlyphMetricAndTopLeftClip()
    {
        HK lower=new();
        var donor=lower.TextDonor(new(3,4,1),true);
        var label=lower.CopyLabelStep(donor,37.44f);
        lower.LabelStep(label,"Localized name",new(900,260,320,56));
        Assert.False(label.Tmp.enableAutoSizing);
        Assert.Equal(37.44f,label.Root.lossyScale.y*label.Tmp.InkHeight,3);
        Assert.Equal(900,label.ClipRect.x);Assert.Equal(260,label.ClipRect.y);
        Assert.Equal(TextAlignment.TopLeft,label.Tmp.alignment);
    }
}
