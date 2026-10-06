using Xunit;
using HkPauseContracts;
using HK=HkPauseContracts.HKDualScreen;
using GameObject=HkPauseContracts.GameObject;
using Renderer=HkPauseContracts.Renderer;
using Bounds=HkPauseContracts.Bounds;
using HkPlayerData=HkPauseContracts.PlayerData;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightRenderingCorrectionsTests
{
    [Theory]
    [InlineData(180,1)] [InlineData(180,-1)] [InlineData(0,-1)] [InlineData(90,1)]
    public void ActualFitSpriteCentersAffineOffPivotArt(float angle,float reflection)
    {
        HK lower=new();var parent=new GameObject("NativeCornerParent");parent.transform.localScale=new(reflection*2,3,1);
        var art=new SpriteRenderer();art.transform.SetParent(parent.transform);art.sprite.bounds=new(new(.25f,.4f,0),new(2,1,0));art.transform.localRotation=Quaternion.Euler(0,0,angle);
        lower.NativeFitSpriteStep(art,new(200,300,0),22,22);Assert.Equal(200,art.bounds.center.x,3);Assert.Equal(300,art.bounds.center.y,3);
        Assert.True(art.bounds.size.x<=22.001f);Assert.True(art.bounds.size.y<=22.001f);
    }
    [Fact]
    public void ActualNativeSelectionGlowUsesCanonicalPixelsAndCenteredArt()
    {
        HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(1);
        lower.NativeSelectionStep(lower.refsInv.slots[0].Root);var glow=cursor.Find("Glow").GetComponent<SpriteRenderer>();
        var target=lower.refsInv.slots[0].Renderers[0].bounds;
        Assert.Equal(target.size.x+24,glow.bounds.size.x,3);Assert.Equal(target.size.y+24,glow.bounds.size.y,3);Assert.Equal(cursor.position.x,glow.bounds.center.x,3);Assert.Equal(cursor.position.y,glow.bounds.center.y,3);
    }

    static void AdmitShellDonors(HK lower)
    {
        NativeSelectionInputs.Attach(lower.Manager.inventoryFSM.transform.gameObject,false);
        lower.ClearShellCursorDonors();lower.ResolveAllShellDonors();
        // Typed graphical inputs only, not an alternate layout. Production
        // PositionFrame fits and measures all five aspect-ratio donors.
        for(int i=0;i<5;i++) lower.NativeTab(i).sprite=new Sprite {name="admitted-tab-"+i,bounds=new(Vector3.zero,new(2,1,0))};
    }
    // Independent current SS DsShell/DsCursor defaults: fixed density caps,
    // 40px strip clearance, 64px corners, 12px bounded inset and glow pad.
    [Theory]
    [InlineData(1240,1080)] [InlineData(1240,900)] [InlineData(1240,640)]
    [InlineData(2400,480)] [InlineData(1,1)] [InlineData(100,1080)]
    public void ActualTabDensityUsesMeasuredCanvas(float width,float height)
    {
        var g=HKLowerLayout.Measure(width,height);
        float want=Math.Max(0,Math.Min(g.CellWidth,Math.Min(88,g.TabHeight-40)));
        Assert.Equal(want,g.IconMax,5);
    }
    [Theory]
    [InlineData(1080)] [InlineData(900)] [InlineData(640)]
    public void ActualTabCornersAndGlowUseArtRectangleNotCellOrFixedSquare(int height)
    {
        HK lower=new();lower.BOTTOM_H=height;AdmitShellDonors(lower);lower.tab.cur=1;
        lower.attrCam.orthographicSize=height/2f;lower.FramePositionStep();
        var art=lower.NativeTab(0).bounds;float inset=Math.Min(12,Math.Min(art.size.x,art.size.y)/3);
        Assert.Equal(64,Math.Max(lower.NativeTabTl.bounds.size.x,lower.NativeTabTl.bounds.size.y),3);
        Assert.Equal(art.min.x+inset,lower.NativeTabTl.bounds.center.x,3);
        Assert.Equal(art.max.y-inset,lower.NativeTabTl.bounds.center.y,3);
        Assert.Equal(art.max.x-inset,lower.NativeTabBr.bounds.center.x,3);
        Assert.Equal(art.min.y+inset,lower.NativeTabBr.bounds.center.y,3);
        Assert.Equal(art.size.x+24,lower.NativeTabGlow.bounds.size.x,3);
        Assert.Equal(art.size.y+24,lower.NativeTabGlow.bounds.size.y,3);
        Assert.Equal(1,lower.NativeTab(0).color.r);Assert.Equal(1,lower.NativeTab(0).color.a);
        Assert.Equal(.62f,lower.NativeTab(1).color.r);Assert.Equal(.60f,lower.NativeTab(1).color.g);
        Assert.Equal(.58f,lower.NativeTab(1).color.b);Assert.Equal(1,lower.NativeTab(1).color.a);
    }
    [Theory]
    [InlineData(3)] [InlineData(4)]
    public void ActualSupplementaryCursorUsesCanonicalRectangleAndBoundedInset(int route)
    {
        HK lower=new();var g=route==3 ? lower.journalGraphics : lower.guideGraphics;
        lower.SelectionStep(route,new(20,300,380,66),true,0);
        Assert.Equal(64,Math.Max(g.TL.bounds.size.x,g.TL.bounds.size.y),3);
        Assert.Equal(-620+20+12,g.TL.bounds.center.x,3);
        Assert.Equal(540-300-12,g.TL.bounds.center.y,3);
        Assert.Equal(404,g.Glow.bounds.size.x,3);Assert.Equal(90,g.Glow.bounds.size.y,3);
        lower.SelectionStep(route,new(20,300,6,9),true,0);
        Assert.Equal(-620+20+2,g.TL.bounds.center.x,3);
        Assert.Equal(540-300-2,g.TL.bounds.center.y,3);
    }
    [Theory]
    [InlineData(1080)] [InlineData(900)] [InlineData(640)]
    public void ActualMarkerDensityPaletteAndTravelMatchCanonicalStrip(int height)
    {
        float previous=Time.unscaledDeltaTime;try
        {
            Time.unscaledDeltaTime=.05f;HK lower=new();lower.BOTTOM_H=height;lower.attrCam.orthographicSize=height/2f;
            lower.ResolveAllShellDonors();lower.BuildActionsStep();lower.MarkerChoose(0);lower.MarkerStripStep();
            var g=lower.LowerGeometry();float size=Math.Max(0,Math.Min(96,g.TabHeight-40));
            var blue=lower.MarkerStripIcon(0);Assert.Equal(size,Math.Max(blue.bounds.size.x,blue.bounds.size.y),3);
            Assert.Equal(1,blue.color.r);Assert.Equal(1,blue.color.a);Assert.Equal(.45f,lower.MarkerStripIcon(1).color.a);
            Assert.Equal(52,Math.Max(lower.NativeTabTl.bounds.size.x,lower.NativeTabTl.bounds.size.y),3);
            Assert.Equal(blue.bounds.min.x+10,lower.NativeTabTl.bounds.center.x,3);
            float from=lower.NativeTabTl.bounds.center.x;
            lower.MarkerChoose(1);lower.MarkerStripStep();var red=lower.MarkerStripIcon(1);
            float to=red.bounds.min.x+10;Assert.Equal((to-from)/3,lower.NativeTabTl.bounds.center.x-from,3);
            lower.MarkerStripStep();lower.MarkerStripStep();Assert.Equal(to,lower.NativeTabTl.bounds.center.x,3);
            HkPlayerData.instance.spareMarkers_r=0;lower.MarkerStripStep();Assert.Equal(.25f,red.color.a);
        }
        finally {Time.unscaledDeltaTime=previous;}
    }
    [Theory]
    [InlineData(1080)] [InlineData(900)] [InlineData(640)]
    public void ActualMarkerTimingCompletesEqualAndUnequalPartitions(int height)
    {
        float previous=Time.unscaledDeltaTime;try
        {
            // Independent 0.15s boundaries, including many small updates and
            // unequal partitions; no endpoint epsilon or geometry rounding.
            foreach(var partition in new[] {
                new[]{.05f,.05f,.05f},new[]{.075f,.075f},
                new[]{.025f,.025f,.025f,.025f,.025f,.025f},
                new[]{.03f,.04f,.08f},new[]{.02f,.08f,.05f},
                Enumerable.Repeat(.01f,15).ToArray()})
            {
                Time.unscaledDeltaTime=0;HK lower=new();lower.BOTTOM_H=height;lower.attrCam.orthographicSize=height/2f;
                lower.ResolveAllShellDonors();lower.BuildActionsStep();lower.MarkerChoose(0);lower.MarkerStripStep();
                var from=lower.MarkerCaretBounds;Assert.Equal(1,lower.MarkerCaretFraction);
                lower.MarkerChoose(1);double elapsed=0;
                foreach(float dt in partition)
                {
                    Time.unscaledDeltaTime=dt;elapsed+=dt;lower.MarkerStripStep();
                    var target=lower.MarkerStripIcon(1).bounds;
                    float fraction=(float)Math.Min(1,elapsed/.15);
                    Assert.Equal(fraction,lower.MarkerCaretFraction);
                    if(fraction<1)
                    {
                        Assert.Equal(from.center.x+(target.center.x-from.center.x)*fraction,lower.MarkerCaretBounds.center.x,3);
                        Assert.Equal(from.size.x+(target.size.x-from.size.x)*fraction,lower.MarkerCaretBounds.size.x,3);
                    }
                }
                var end=lower.MarkerStripIcon(1).bounds;
                Assert.Equal(1,lower.MarkerCaretFraction);
                Assert.Equal(end.center.x,lower.MarkerCaretBounds.center.x);
                Assert.Equal(end.center.y,lower.MarkerCaretBounds.center.y);
                Assert.Equal(end.size.x,lower.MarkerCaretBounds.size.x);
                Assert.Equal(end.min.x+10,lower.NativeTabTl.bounds.center.x,3);
                Time.unscaledDeltaTime=0;lower.MarkerStripStep();
                Assert.Equal(end.center.x,lower.MarkerCaretBounds.center.x);
            }
        }
        finally {Time.unscaledDeltaTime=previous;}
    }
    [Fact]
    public void ActualMarkerTimingInterruptionStartsAtCurrentBoundsAndHideSnaps()
    {
        float previous=Time.unscaledDeltaTime;try
        {
            Time.unscaledDeltaTime=0;HK lower=new();lower.ResolveAllShellDonors();lower.BuildActionsStep();
            lower.MarkerChoose(0);lower.MarkerStripStep();var a=lower.MarkerCaretBounds;
            Time.unscaledDeltaTime=.05f;lower.MarkerChoose(1);lower.MarkerStripStep();var b=lower.MarkerStripIcon(1).bounds;
            Assert.Equal(a.center.x+(b.center.x-a.center.x)/3,lower.MarkerCaretBounds.center.x,3);
            var interrupted=lower.MarkerCaretBounds;
            Time.unscaledDeltaTime=0;lower.MarkerChoose(2);lower.MarkerStripStep();
            Assert.Equal(interrupted.center.x,lower.MarkerCaretBounds.center.x);Assert.Equal(0,lower.MarkerCaretFraction);
            Time.unscaledDeltaTime=.075f;lower.MarkerStripStep();var c=lower.MarkerStripIcon(2).bounds;
            Assert.Equal(interrupted.center.x+(c.center.x-interrupted.center.x)/2,lower.MarkerCaretBounds.center.x,3);
            lower.MarkerStripStep();Assert.Equal(c.center.x,lower.MarkerCaretBounds.center.x);Assert.Equal(1,lower.MarkerCaretFraction);
            Time.unscaledDeltaTime=.03f;lower.MarkerChoose(3);lower.MarkerStripStep();lower.MarkerStripStep(false);
            Time.unscaledDeltaTime=0;lower.MarkerChoose(0);lower.MarkerStripStep();
            Assert.Equal(a.center.x,lower.MarkerCaretBounds.center.x);Assert.Equal(1,lower.MarkerCaretFraction);
            Time.unscaledDeltaTime=.05f;lower.MarkerChoose(1);lower.MarkerStripStep();lower.MarkerControlsTeardownStep();
            lower.BuildActionsStep();Time.unscaledDeltaTime=0;lower.MarkerStripStep();
            Assert.Equal(lower.MarkerStripIcon(1).bounds.center.x,lower.MarkerCaretBounds.center.x);Assert.Equal(1,lower.MarkerCaretFraction);
        }
        finally {Time.unscaledDeltaTime=previous;}
    }
    [Fact]
    public void ActualMarkerTimingHealthyMovingFramesHaveNoScansBoundsReadsOrAllocation()
    {
        float previous=Time.unscaledDeltaTime;try
        {
            HK lower=new();lower.ResolveAllShellDonors();lower.BuildActionsStep();
            Time.unscaledDeltaTime=.05f;
            for(int i=0;i<150;i++) {lower.MarkerChoose(i%5);lower.MarkerStripStep();}
            int reads=DiscoveryCounters.BoundsReads,scans=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries;
            long bytes=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<300;i++) {lower.MarkerChoose(i%5);lower.MarkerStripStep();}
            Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-bytes);
            Assert.Equal(reads,DiscoveryCounters.BoundsReads);Assert.Equal(scans,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);
        }
        finally {Time.unscaledDeltaTime=previous;}
    }
    static void AssertIdle(Action tick)
    {
        for(int i=0;i<150;i++) { Time.frameCount++;tick(); }int reads=DiscoveryCounters.BoundsReads,scans=DiscoveryCounters.Hierarchy;
        long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<300;i++) { Time.frameCount++;tick(); }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-bytes);
        Assert.Equal(reads,DiscoveryCounters.BoundsReads);Assert.Equal(scans,DiscoveryCounters.Hierarchy);
    }
    [Fact]
    public void ActualShellIdleHasNoBoundsDiscoveryOrAllocationAfterWarmup()
    {
        HK lower=new();AdmitShellDonors(lower);lower.tab.cur=1;lower.attrCam.orthographicSize=540;
        AssertIdle(lower.FramePositionStep);
        lower.BOTTOM_H=640;lower.attrCam.orthographicSize=320;lower.FramePositionStep();
        Assert.Equal(140*640/1080f-40,Math.Max(lower.NativeTab(0).bounds.size.x,lower.NativeTab(0).bounds.size.y),3);
        AssertIdle(lower.FramePositionStep);
        var tab=lower.NativeTab(0);tab.sprite=new(){bounds=new(new(.2f,.3f,0),new(1,3,0))};lower.FramePositionStep();
        Assert.Equal(tab.bounds.size.x+24,lower.NativeTabGlow.bounds.size.x,3);AssertIdle(lower.FramePositionStep);
    }
    [Theory]
    [InlineData(3)] [InlineData(4)]
    public void ActualSupplementaryCursorIdleHasNoBoundsDiscoveryOrAllocation(int route)
    {
        HK lower=new();lower.SelectionStep(route,new(20,300,380,66),true,0);AssertIdle(()=>lower.CursorStep(route));
        var g=route==3 ? lower.journalGraphics : lower.guideGraphics;
        g.TL.transform.parent.localScale=new(-2,3,1);lower.CursorStep(route);
        Assert.Equal(64,Math.Max(g.TL.bounds.size.x,g.TL.bounds.size.y),3);AssertIdle(()=>lower.CursorStep(route));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualNativeSelectedItemIdleHasNoBoundsDiscoveryOrAllocation(bool child)
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.AddNativeCursor(pane);
        if(child) {var root=pane.transform.Find("Equipment").Children[0];root.gameObject.RemoveRenderer();var art=new GameObject("ChildGraphic");art.transform.SetParent(root);art.AddComponent<Renderer>();}
        foreach(var r in pane.GetComponentsInChildren<Renderer>(true)) if(r is not SpriteRenderer) r.gameObject.AddComponent<MeshFilter>().sharedMesh=new Mesh{bounds=new(Vector3.zero,r.BoundsSize)};
        lower.NativeLayoutRouteStep(1);lower.NativeSelectionStep(lower.refsInv.slots[0].Root);AssertIdle(lower.NativeCursorStep);
        var renderer=lower.refsInv.slots[0].Renderers[0];renderer.transform.localPosition+=new Vector3(7,11,0);
        lower.NativeCursorStep();Assert.Equal(renderer.bounds.center.x,lower.NativeCursor.position.x,3);
        Assert.Equal(renderer.bounds.center.y,lower.NativeCursor.position.y,3);AssertIdle(lower.NativeCursorStep);
    }
    static (HK Lower,NativeSelectionInputs.Input Native,Transform First,Transform Second,Renderer FirstArt,Renderer SecondArt) NativeMovingSelection(bool charms)
    {
        HK lower=new();var pane=lower.NewNativePane(charms);var native=NativeSelectionInputs.Attach(pane,charms);
        lower.NativeLayoutRouteStep(charms ? 2 : 1);
        var first=charms ? lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard1") : lower.refsInv.slots[0].Root;
        var second=charms ? lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard2") : lower.refsInv.slots[1].Root;
        var a=charms ? lower.nativeCharmGrid.Children.First(t=>t.name=="Icon1").GetComponent<Renderer>() : lower.refsInv.slots[0].Renderers[0];
        var b=charms ? lower.nativeCharmGrid.Children.First(t=>t.name=="Icon2").GetComponent<Renderer>() : lower.refsInv.slots[1].Renderers[0];
        // Known native shape inputs let the selected-item owner invalidate only
        // its own measurement; no alternative cursor/fit implementation.
        foreach(var art in new[]{a,b}) if(art is not SpriteRenderer)
            art.gameObject.AddComponent<MeshFilter>().sharedMesh=new Mesh{bounds=new(Vector3.zero,art.BoundsSize)};
        Assert.Equal(a.bounds.size.x,b.bounds.size.x);Assert.Equal(a.bounds.size.y,b.bounds.size.y);
        lower.NativeSelectionStep(first,charms ? 1 : 0);
        return (lower,native,first,second,a,b);
    }
    static void AssertNativeMovingCursor(NativeSelectionInputs.Input native,Bounds target,Vector3 center)
    {
        float inset=Math.Min(12,Math.Min(target.size.x,target.size.y)/3);
        Assert.Equal(center.x-target.extents.x+inset,native.TL.bounds.center.x,3);
        Assert.Equal(center.y+target.extents.y-inset,native.TL.bounds.center.y,3);
        Assert.Equal(center.x+target.extents.x-inset,native.BR.bounds.center.x,3);
        Assert.Equal(center.y-target.extents.y+inset,native.BR.bounds.center.y,3);
        Assert.Equal(center.x,native.Glow.bounds.center.x,3);Assert.Equal(center.y,native.Glow.bounds.center.y,3);
        Assert.Equal(64,Math.Max(native.TL.bounds.size.x,native.TL.bounds.size.y),3);
        Assert.Equal(64,Math.Max(native.BR.bounds.size.x,native.BR.bounds.size.y),3);
        Assert.Equal(target.size.x+24,native.Glow.bounds.size.x,3);Assert.Equal(target.size.y+24,native.Glow.bounds.size.y,3);
        Assert.Equal("Inv_0014_selection_cursor",native.TL.sprite.name);Assert.Equal("Inv_0014_selection_cursor",native.BR.sprite.name);
        Assert.Equal("light_effect_v02",native.Glow.sprite.name);
        Assert.True(native.BR.transform.parent.localScale.x<0 && native.BR.transform.parent.localScale.y<0);
        Assert.Equal(0f,native.BR.transform.parent.localRotation.Angle);Assert.Equal(0f,native.BR.transform.localRotation.Angle);
        Assert.Equal(.6029411554336548f,native.Glow.color.r);Assert.Equal(.7535496950149536f,native.Glow.color.g);
        Assert.Equal(1,native.Glow.color.b);Assert.Equal(.5372549295425415f,native.Glow.color.a);
        Assert.NotEqual(native.Glow.transform.position.x,native.Glow.bounds.center.x);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualNativeEqualSizeCursorTravelDoesNotRemeasureParentedOrnaments(bool charms)
    {
        float previous=Time.unscaledDeltaTime;try
        {
            Time.unscaledDeltaTime=0;var f=NativeMovingSelection(charms);
            for(int i=0;i<150;i++) f.Lower.NativeCursorStep();
            var from=f.Native.Cursor.position;var target=f.SecondArt.bounds;
            f.Lower.NativeSelectionStep(f.Second,charms ? 2 : 0);
            Assert.Equal(from.x,f.Native.Cursor.position.x);
            // Selection changes legitimately bind/measure the new native item.
            // Once bound, cursor-root interpolation must not reread its children.
            Time.unscaledDeltaTime=.05f;
            for(int step=1;step<=3;step++)
            {
                int reads=DiscoveryCounters.BoundsReads,scans=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries;
                f.Lower.NativeCursorStep();
                Assert.Equal(reads,DiscoveryCounters.BoundsReads);Assert.Equal(scans,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);
                var center=new Vector3(from.x+(target.center.x-from.x)*step/3,from.y+(target.center.y-from.y)*step/3,target.center.z-.2f);
                Assert.Equal(center.x,f.Native.Cursor.position.x,3);Assert.Equal(center.y,f.Native.Cursor.position.y,3);
                AssertNativeMovingCursor(f.Native,target,center);
            }
        }
        finally {Time.unscaledDeltaTime=previous;}
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualNativeContinuousSelectedTranslationRemeasuresOnlySelectedArtWithoutScansOrAllocation(bool charms)
    {
        var f=NativeMovingSelection(charms);var delta=new Vector3(1,-1,0);
        for(int i=0;i<150;i++) {f.FirstArt.transform.localPosition+=delta;f.Lower.NativeCursorStep();}
        int reads=DiscoveryCounters.BoundsReads,scans=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries;
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) {f.FirstArt.transform.localPosition+=delta;f.Lower.NativeCursorStep();}
        long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
        // Each changed selected-art position requires one native measurement;
        // the three translated cursor ornaments require none, rather than six.
        Assert.Equal(reads+300,DiscoveryCounters.BoundsReads);
        Assert.Equal(scans,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);Assert.Equal(0,allocated);
        var target=f.FirstArt.bounds;var center=target.center-new Vector3(0,0,.2f);
        Assert.Equal(center.x,f.Native.Cursor.position.x,3);Assert.Equal(center.y,f.Native.Cursor.position.y,3);
        AssertNativeMovingCursor(f.Native,target,center);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualNativeExternalOrnamentParentTranslationRepinsUnchangedRequestedCentersWithoutRemeasurement(bool charms)
    {
        var f=NativeMovingSelection(charms);for(int i=0;i<150;i++) f.Lower.NativeCursorStep();
        var target=f.FirstArt.bounds;var center=f.Native.Cursor.position;
        var tl=f.Native.TL.transform.parent;var br=f.Native.BR.transform.parent;var back=f.Native.Glow.transform.parent;
        var tlScale=tl.localScale;var brScale=br.localScale;var glowScale=f.Native.Glow.transform.localScale;
        int reads=DiscoveryCounters.BoundsReads,scans=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries;
        // These descendants remain externally moved even after PositionSelection
        // reassigns Cursor.position to the SAME requested target center.
        tl.localPosition+=new Vector3(17,-9,0);br.localPosition+=new Vector3(-13,7,0);back.localPosition+=new Vector3(11,5,0);
        f.Lower.NativeCursorStep();
        Assert.Equal(reads,DiscoveryCounters.BoundsReads);Assert.Equal(scans,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);
        Assert.Equal(center.x,f.Native.Cursor.position.x);Assert.Equal(center.y,f.Native.Cursor.position.y);
        AssertNativeMovingCursor(f.Native,target,center);
        Assert.Equal(tlScale.x,tl.localScale.x);Assert.Equal(tlScale.y,tl.localScale.y);
        Assert.Equal(brScale.x,br.localScale.x);Assert.Equal(brScale.y,br.localScale.y);
        Assert.Equal(glowScale.x,f.Native.Glow.transform.localScale.x);Assert.Equal(glowScale.y,f.Native.Glow.transform.localScale.y);
    }
    [Fact]
    public void ActualSelectedSpriteAnimationInvalidatesMeasurementWithoutRestartingTravel()
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(1);
        var root=lower.refsInv.slots[0].Root;root.gameObject.RemoveRenderer();var sprite=root.gameObject.AddComponent<SpriteRenderer>();
        sprite.sprite=new(){bounds=new(new(.2f,.3f,0),new(1,2,0))};lower.NativeSelectionStep(root);AssertIdle(lower.NativeCursorStep);
        sprite.sprite=new(){bounds=new(new(-.2f,.5f,0),new(3,1,0))};lower.NativeCursorStep();
        var glow=lower.NativeCursor.Find("Glow").GetComponent<SpriteRenderer>();
        Assert.Equal(sprite.bounds.center.x,glow.bounds.center.x,3);Assert.Equal(sprite.bounds.size.x+24,glow.bounds.size.x,3);
        AssertIdle(lower.NativeCursorStep);
    }
    [Fact]
    public void ActualMapControlsAndMarkerIdleHaveNoRepeatedBoundsDiscoveryOrAllocation()
    {
        HK lower=new();lower.ResolveAllShellDonors();lower.BuildActionsStep();lower.tab.cur=0;
        lower.mapGm=new();lower.mapAvailable=lower.mapAnyAvailable=lower.mapContentVisible=true;
        lower.PositionActionsStep();AssertIdle(lower.PositionActionsStep);
        lower.MarkerChoose(0);AssertIdle(()=>lower.MarkerStripStep());
    }

    [Fact]
    public void ActualCopyPaneLabelKeepsNativeDerivedContainerButRemovesGameplayDriver()
    {
        HK lower=new();var donor=lower.TextDonor(new(1,1,1),true);
        donor.TextComponents.Add(new GameplayLabelDriver(donor.GetComponentsInChildren<Renderer>(true)[0]));
        var label=lower.CopyLabelStep(donor,37.44f);Assert.NotNull(label.Container);
        Assert.IsType<TMProOld.TextContainer>(label.Container);Assert.Empty(label.Root.GetComponentsInChildren<GameplayLabelDriver>(true));
        lower.LabelStep(label,"Measured text",new(900,256,320,56));
        Assert.Equal(320/label.UnitScale,label.Container.size.x,3);
        Assert.Equal(label.Container.size.x,label.Tmp.LastMeshSize.x,3);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualSelectedCursorIdleDoesNotAllocate(bool child)
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.AddNativeCursor(pane);
        if(child) {var root=pane.transform.Find("Equipment").Children[0];root.gameObject.RemoveRenderer();var art=new GameObject("ChildGraphic");art.transform.SetParent(root);art.AddComponent<Renderer>();}
        lower.NativeLayoutRouteStep(1);lower.NativeSelectionStep(lower.refsInv.slots[0].Root);
        for(int i=0;i<150;i++) lower.NativeCursorStep();long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) lower.NativeCursorStep();Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualSelectionHidesScrolledSlotAndRestoresSameDetail(bool child)
    {
        HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);
        if(child) { var item=pane.transform.Find("Equipment").Children[0];item.gameObject.RemoveRenderer();var icon=new GameObject("ChildGraphic");icon.transform.SetParent(item);icon.AddComponent<Renderer>().BoundsSize=new(2,2,1); }
        lower.NativeLayoutRouteStep(1);var r=lower.refsInv;var selected=r.slots[0].Root;
        lower.NativeDetailStep(4,selected);lower.NativeSelectionStep(selected);Assert.True(cursor.gameObject.activeSelf);
        var detail=r.nameLabel.Text;lower.NativeScrollStep(0,-.2f);lower.NativeCursorStep();
        Assert.All(r.slots[0].Renderers,x=>Assert.False(x.enabled));Assert.Same(selected,lower.SelectedItem);
        Assert.Equal(detail,r.nameLabel.Text);Assert.False(cursor.gameObject.activeSelf);
        lower.NativeScrollStep(0,.2f);lower.NativeCursorStep();Assert.True(cursor.gameObject.activeSelf);
        Assert.Same(selected,lower.SelectedItem);Assert.Equal(detail,r.nameLabel.Text);
        Assert.False(cursor.Find("TR").gameObject.activeSelf);Assert.False(cursor.Find("BL").gameObject.activeSelf);
    }
    [Fact]
    public void ActualSelectionRejectsInactiveDirectItem()
    {
        HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(1);
        var item=lower.refsInv.slots[0].Root;lower.NativeSelectionStep(item);item.gameObject.SetActive(false);lower.NativeCursorStep();Assert.False(cursor.gameObject.activeSelf);
    }
    [Fact]
    public void ActualTapCannotChooseHiddenInventoryAtStaleBounds()
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);var hidden=lower.refsInv.slots[0].Root;
        var old=hidden.position;lower.NativeScrollStep(0,-.2f);lower.NativeTapStep(old);
        Assert.All(lower.refsInv.slots[0].Renderers,r=>Assert.False(r.enabled));
        Assert.NotSame(hidden,lower.SelectedItem);
        // A continuous 216px scroll does not preserve the old integer-row hit.
        // Prove a hit on the current, actually visible native slot instead.
        var visible=lower.refsInv.slots[6];var art=visible.Renderers[0];
        Assert.True(art.enabled);Assert.True(art.gameObject.activeInHierarchy);
        var point=art.bounds.center;var body=HKLowerLayout.NativeColumns(lower.LowerGeometry(),false);
        float y=lower.BOTTOM_H/2f-(point.y-lower.compRoot.position.y);
        Assert.InRange(y,body.Top,body.Top+body.Height);
        lower.NativeTapStep(point);
        Assert.Same(visible.Root,lower.SelectedItem);Assert.Equal("Map and Quill",lower.SelectedItem.name);
    }
    [Fact]
    public void ActualCharmTapRejectsDisabledIconIndependentlyOfCursor()
    {
        HK lower=new();lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        var icon=lower.nativeCharmGrid.Children.First(t=>t.name=="Icon1");var point=icon.position;
        icon.GetComponent<SpriteRenderer>().enabled=false;lower.NativeTapStep(point);Assert.Null(lower.SelectedItem);
        icon.GetComponent<SpriteRenderer>().enabled=true;lower.NativeTapStep(point);Assert.Equal("CharmBoard1",lower.SelectedItem?.name);
    }
    [Fact]
    public void ActualSelectionKeepsNativePointFifteenSecondMoveTiming()
    {
        float previous=Time.unscaledDeltaTime;try
        {
            Time.unscaledDeltaTime=.05f;HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(1);
            var a=lower.refsInv.slots[0].Root;var b=lower.refsInv.slots[1].Root;lower.NativeSelectionStep(a);float from=cursor.position.x;
            lower.NativeSelectionStep(b);Assert.Equal(from+(b.position.x-from)/3,cursor.position.x,3);
            lower.NativeCursorStep();lower.NativeCursorStep();Assert.Equal(b.position.x,cursor.position.x,3);
        }
        finally {Time.unscaledDeltaTime=previous;}
    }

    [Fact]
    public void ActualCharmSelectionAndTapRejectDisabledIcon()
    {
        HK lower=new();var pane=lower.NewNativePane(true);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(2);
        var board=lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard1");var icon=lower.nativeCharmGrid.Children.First(t=>t.name=="Icon1");
        lower.NativeSelectionStep(board,1);Assert.True(cursor.gameObject.activeSelf);var point=icon.position;
        icon.GetComponent<SpriteRenderer>().enabled=false;lower.NativeCursorStep();Assert.False(cursor.gameObject.activeSelf);
        lower.NativeSelectionStep(null);lower.NativeTapStep(point);Assert.Null(lower.SelectedItem);
        icon.GetComponent<SpriteRenderer>().enabled=true;lower.NativeTapStep(point);Assert.Same(board,lower.SelectedItem);lower.NativeCursorStep();Assert.True(cursor.gameObject.activeSelf);
    }
    [Fact]
    public void ActualHeightLimitedCharmsChooserFitsExactCanonicalMask()
    {
        HK lower=new();lower.BOTTOM_W=2400;lower.BOTTOM_H=480;lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        var p=HKLowerLayout.NativeColumns(lower.LowerGeometry(),true);Bounds occupied=default;bool any=false;
        foreach(var r in lower.nativeCharmGrid.GetComponentsInChildren<Renderer>(true))
        { if(!r.enabled || !r.gameObject.activeInHierarchy)continue;if(!any){occupied=r.bounds;any=true;}else occupied.Encapsulate(r.bounds); }
        Assert.True(any);Assert.Equal(p.Height,occupied.size.y,3);
        float top=lower.BOTTOM_H/2f-occupied.max.y,bottom=lower.BOTTOM_H/2f-occupied.min.y;
        Assert.True(top>=p.Top-.001f,$"top {top} < {p.Top}");Assert.True(bottom<=p.Top+p.Height+.001f,$"bottom {bottom} > {p.Top+p.Height}");
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ActualNativeCharmCostRendersExactFilledNotches(int cost)
    {
        HK lower=new();lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        HkPlayerData.instance.Ints["charmCost_36"]=cost;var item=lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard36");
        lower.NativeDetailStep(1,item,36);Assert.Equal(cost,lower.CostPips?.Children.Count(t=>t.gameObject.activeSelf) ?? 0);
        HkPlayerData.instance.Ints["charmCost_36"]=3;lower.NativeDetailStep(1,item,36);
        HkPlayerData.instance.Ints["charmCost_36"]=0;lower.NativeDetailStep(1,item,36);Assert.Equal(0,lower.CostPips.Children.Count(t=>t.gameObject.activeSelf));
    }
}
