using System.Reflection;
using Xunit;
using HkPauseContracts;
using HK = HkPauseContracts.HKDualScreen;
using Rect = HkPauseContracts.Rect;
using Color = HkPauseContracts.Color;
using Renderer = HkPauseContracts.Renderer;
using SpriteRenderer = HkPauseContracts.SpriteRenderer;
using Vector3 = HkPauseContracts.Vector3;
using TextAlignment = HkPauseContracts.TextAlignment;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightCompleteLowerLayoutTests
{
    static T Field<T>(HK f,string name) => (T)typeof(HK).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(f);
    static void Near(float expected,float actual) => Assert.Equal(expected,actual,3);
    static void Ink(Color expected,Color actual) { Near(expected.r,actual.r);Near(expected.g,actual.g);Near(expected.b,actual.b);Near(expected.a,actual.a); }
    static Transform Find(Transform root,string name) => root.name==name ? root : root.Children.Select(c=>Find(c,name)).FirstOrDefault(t=>t!=null);
    static HK Supplementary(int id,int width=1240,int height=1080)
    {
        var f=new HK { BOTTOM_W=width,BOTTOM_H=height };
        f.InstallCompleteSupplementaryInputs();f.tab.cur=f.tab.tap=id;
        f.paneClone=f.BuildStep(id);Assert.NotNull(f.paneClone);
        return f;
    }
    static HK Inventory(int width=1240,int height=1080)
    {
        var f=new HK { BOTTOM_W=width,BOTTOM_H=height };f.NewNativePane(false);f.NativeLayoutStep(1);return f;
    }
    static HK Map(int width=1240,int height=1080)
    {
        var f=new HK { BOTTOM_W=width,BOTTOM_H=height };f.tab.cur=f.tab.tap=0;f.mapGm=new();
        f.mapAvailable=f.mapAnyAvailable=f.mapContentVisible=true;f.mapNeedsSetup=false;
        f.frameRoot=null;f.FrameBuildStep();f.BuildActionsStep();f.PositionActionsStep();return f;
    }
    static float PixelY(HK f,Renderer r) => f.BOTTOM_H/2f-(r.bounds.center.y-f.compRoot.position.y);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void N01_OrdinaryTabsUseOpaqueDimDistinctFromMarkerTint(int tab)
    {
        var f=Map();f.tab.cur=tab;f.FramePositionStep();
        for(int col=0;col<5;col++) Ink(HKLowerLayout.TabAtColumn(col)==tab ? Color.white : HK.ShellMuted,f.NativeTab(col).color);
        f.tab.cur=0;f.MapMarkerMode=true;f.MarkerStripStep();
        Near(.45f,f.MarkerStripIcon(1).color.a);
        f.MapMarkerMode=false;f.tab.cur=tab;f.FramePositionStep();
        for(int col=0;col<5;col++) Ink(HKLowerLayout.TabAtColumn(col)==tab ? Color.white : HK.ShellMuted,f.NativeTab(col).color);
    }

    [Theory]
    [InlineData(1240,1080)] [InlineData(800,720)] [InlineData(1600,1200)]
    public void I01_InventoryProseFollowsItsOwnNameMetricWithoutNotchInset(int width,int height)
    {
        var f=Inventory(width,height);var p=HKLowerLayout.NativeColumns(f.LowerGeometry(),false);
        f.NativeSource(f.paneClone,"Text Name").text="Little native name";
        f.NativeSource(f.paneClone,"Text Desc").text=new string('W',2000);f.NativeLayoutStep(1);
        var name=f.refsInv.nameLabel;var prose=f.refsInv.descLabel;
        float scale=Math.Clamp(p.DetailWidth/500f,.78f,1),title=48*scale,body=36*scale,titleHeight=title+10,proseTop=titleHeight+10;
        Near(p.Top+4,name.TextRect.y);Near(titleHeight,name.TextRect.height);
        Near(p.Top+proseTop,prose.TextRect.y);Near(p.Height-proseTop,prose.TextRect.height);
        Near(title,name.UnitScale*name.Tmp.InkHeight);Near(body,prose.UnitScale*prose.Tmp.InkHeight);
        Assert.True(name.ClipRect.y+name.ClipRect.height<prose.ClipRect.y);
        foreach(var r in prose.ClipRenderers) Assert.Equal(prose.Renderer.Clip.y,r.Clip.y);
    }

    [Theory]
    [InlineData(1240,1080)] [InlineData(800,720)]
    public void I02_InventoryChooserAccumulatesFractionalPixelsPartitionInvariant(int width,int height)
    {
        var a=Inventory(width,height);var first=a.refsInv.slots[0].Renderers[0];float start=PixelY(a,first);
        a.NativeScrollStep(0,-.001f);Near(start-1.08f*height/1080,PixelY(a,first));
        a.NativeScrollStep(0,.001f);Near(start,PixelY(a,first));
        a.NativeScrollStep(0,-.06f);float end=PixelY(a,first);
        var b=Inventory(width,height);for(int i=0;i<20;i++)b.NativeScrollStep(0,-.003f);
        Near(end,PixelY(b,b.refsInv.slots[0].Renderers[0]));
        var selected=b.refsInv.slots[3].Root;b.NativeSelectionStep(selected);b.NativeScrollStep(0,-.001f);b.NativeCursorStep();
        Near(selected.GetComponent<Renderer>().bounds.center.y,b.NativeCursor?.position.y ?? selected.position.y);
        b.NativeTapStep(selected.GetComponent<Renderer>().bounds.center);Assert.Same(selected,b.SelectedItem);
    }

    [Theory]
    [InlineData(1240,1080)] [InlineData(800,720)]
    public void C01_ExistingCharmActionOwnsBottomRoundedPlateAndReservedProse(int width,int height)
    {
        var f=new HK { BOTTOM_W=width,BOTTOM_H=height };var pane=f.NewNativePane(true);f.NativeLayoutStep(2);
        var item=Find(pane.transform,"CharmBoard1");HkPauseContracts.PlayerData.instance.Ints["charmCost_1"]=1;f.NativeDetailStep(1,item,1);
        var p=HKLowerLayout.NativeColumns(f.LowerGeometry(),true);
        var hit=Field<Rect>(f,"charmActionRect");var label=Field<HK.NativePaneLabel>(f,"nativeCharmAction");
        Near(p.DetailX+14,hit.x);Near(p.Top+p.Height-78+12,hit.y);Near(p.DetailWidth-28,hit.width);Near(54,hit.height);
        Assert.True(f.CharmDescription.ClipRect.y+f.CharmDescription.ClipRect.height<=p.Top+p.Height-78);
        Near(34,label.UnitScale*label.Tmp.InkHeight);Assert.Same(Resources.CapsFont,label.Tmp.font);Assert.Same(label.Tmp.font.material,label.Tmp.fontSharedMaterial);
        var plate=Find(pane.transform,"CanonicalCharmActionPlate")?.GetComponent<SpriteRenderer>();Assert.NotNull(plate);
        Assert.Equal(SpriteDrawMode.Sliced,plate.drawMode);Ink(Color.white,plate.color);Ink(Color.black,label.Tmp.color);
        Near(hit.width,plate.bounds.size.x);Near(hit.height,plate.bounds.size.y);
        var ink=label.Tmp.textBounds;Near(plate.bounds.center.x,label.Root.TransformPoint(ink.center).x);Near(plate.bounds.center.y,label.Root.TransformPoint(ink.center).y);
        Assert.Equal("CTRL_EQUIP",label.Text);Assert.NotNull(f.CostPips);
    }

    [Theory]
    [InlineData(1240,600)] [InlineData(800,600)]
    public void G01_AllElevenNativeGuideRowsUseContinuousPixelLayoutAndHits(int width,int height)
    {
        var a=Supplementary(4,width,height);Assert.Equal(11,a.guideRecords.Count);
        float first=PixelY(a,a.guideRecords[0].Icon);a.ScrollStep(-.001f);Near(first-.001f*height,PixelY(a,a.guideRecords[0].Icon));
        a.ScrollStep(.001f);Near(first,PixelY(a,a.guideRecords[0].Icon));a.ScrollStep(-.06f);
        float end=PixelY(a,a.guideRecords[0].Icon);var b=Supplementary(4,width,height);for(int i=0;i<20;i++)b.ScrollStep(-.003f);
        Near(end,PixelY(b,b.guideRecords[0].Icon));
        var g=b.LowerGeometry();b.SelectionTap(4,200/1240f,(g.HudHeight+20+66-36+2)/height);Assert.Equal(1,b.guideSelected);
        Assert.Equal(0,HkPauseContracts.PlayerData.instance.Writes);
    }

    [Fact]
    public void G02_SelectedGuideNativeNameUses42BodyTitleRole()
    {
        var f=Supplementary(4);f.guideSelected=0;f.LayoutStep(4);
        Near(42,f.guideDetail.UnitScale*f.guideDetail.Tmp.InkHeight);
        Assert.Same(Resources.BodyFont,f.guideDetail.Tmp.font);Assert.Same(Resources.BodyFont.material,f.guideDetail.Tmp.fontSharedMaterial);
        Assert.Equal(f.guideRecords[0].Text,f.guideDetail.Text);Assert.Equal(TextAlignment.TopLeft,f.guideDetail.Tmp.alignment);
        Assert.Equal(0,HkPauseContracts.PlayerData.instance.Writes);
    }

    [Theory]
    [InlineData(36)] [InlineData(180)] [InlineData(2000)]
    public void J01_JournalUsesActualMeasuredDescriptionHunterRuleAndIndependentNotes(int measuredPixels)
    {
        var f=Supplementary(3);f.journalSelected=0;
        f.journalDescription.Tmp.MeasuredTextHeight=measuredPixels/f.journalDescription.UnitScale;
        f.journalNotes.Tmp.MeasuredTextHeight=2000/f.journalNotes.UnitScale;f.LayoutStep(3);
        float top=f.LowerGeometry().HudHeight+16;float bottom=f.LowerGeometry().TabTop-16;
        var d=f.journalDescription;var n=f.journalNotes;
        Near(top+68,d.TextRect.y);
        float available=bottom-(top+68)-18-52-18-90;
        Near(Math.Min(measuredPixels,available),d.ClipRect.height);
        Near(d.ClipRect.y+d.ClipRect.height+18+52+18,n.ClipRect.y);Assert.True(n.ClipRect.height>=90);
        var symbol=Find(f.paneClone.transform,"JournalHunterSymbol")?.GetComponent<SpriteRenderer>();Assert.NotNull(symbol);Assert.Equal("hunter_symbol",symbol.sprite.name);
        Near(44,symbol.bounds.size.y);Near(d.ClipRect.y+d.ClipRect.height+18+26,PixelY(f,symbol));
        f.NativeScrollStep(1,-.1f); // Independent native Inventory owner is not the Journal owner.
        float oldNotes=n.ScrollOffset;
        typeof(HK).GetField("supplementaryDragRegion",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,1);f.ScrollStep(-.1f);
        Assert.Equal(oldNotes,n.ScrollOffset);if(measuredPixels>available) Assert.True(d.ScrollOffset>0);
        foreach(var r in d.ClipRenderers) Assert.Equal(d.Renderer.Clip.y,r.Clip.y);
        Assert.Equal(0,HkPauseContracts.PlayerData.instance.Writes);
    }

    [Theory]
    [InlineData(0)] [InlineData(5)]
    public void J02_JournalDimDescriptionAndDimOrFaintRemainingKillNotesAreOpaque(int remaining)
    {
        var f=Supplementary(3);f.journalSelected=0;f.journalRecords[0].Remaining=remaining;f.LayoutStep(3);
        Ink(HK.ShellMuted,f.journalDescription.Tmp.color);
        Ink(remaining==0 ? HK.ShellMuted : new Color(.38f,.37f,.40f,1),f.journalNotes.Tmp.color);
        Assert.Equal(0,HkPauseContracts.PlayerData.instance.Writes);
    }

    [Fact]
    public void J03_MixedCaseJournalNameUsesActualPairedBodyFontNotCapsDonor()
    {
        var f=Supplementary(3);f.journalSelected=0;f.LayoutStep(3);
        Assert.Same(Resources.BodyFont,f.journalName.Tmp.font);Assert.Same(Resources.BodyFont.material,f.journalName.Tmp.fontSharedMaterial);
        Near(40,f.journalName.UnitScale*f.journalName.Tmp.InkHeight);Assert.Contains("Little",f.journalName.Text);
        Assert.Same(Resources.CapsFont,f.journalState.Tmp.font);
    }

    [Theory]
    [InlineData(1240,0)] [InlineData(800,13.5f)]
    public void J04_JournalRejectsBoth26PixelGapAxesAndCornerAtFractionalScroll(int width,float scroll)
    {
        var f=Supplementary(3,width);if(scroll>0)f.ScrollStep(-scroll/f.BOTTOM_H);
        float sx=width/1240f,cell=(380*sx-52*sx)/3,pitch=cell+26*sx,top=f.LowerGeometry().HudHeight+16-scroll;
        f.journalSelected=7;f.journalDescription.ScrollOffset=12;f.journalNotes.ScrollOffset=17;
        foreach(var point in new[]{(20*sx+cell+13*sx,top+cell/2),(20*sx+cell/2,top+cell+13*sx),(20*sx+cell+13*sx,top+cell+13*sx)})
        {
            f.SelectionTap(3,point.Item1/width,point.Item2/f.BOTTOM_H);Assert.Equal(7,f.journalSelected);
            Near(12,f.journalDescription.ScrollOffset);Near(17,f.journalNotes.ScrollOffset);
        }
        f.SelectionTap(3,(20*sx+pitch+cell/2)/width,(top+pitch+cell/2)/f.BOTTOM_H);Assert.Equal(4,f.journalSelected);
    }

    [Theory]
    [InlineData(1240,1080)] [InlineData(800,720)]
    public void J05_JournalChooserPixelMotionIsPartitionInvariantAndKeepsNativeReadOnlyData(int width,int height)
    {
        var a=Supplementary(3,width,height);float start=PixelY(a,a.JournalCells[0]);a.ScrollStep(-.001f);Near(start-.001f*height,PixelY(a,a.JournalCells[0]));
        a.ScrollStep(.001f);Near(start,PixelY(a,a.JournalCells[0]));a.ScrollStep(-.06f);float end=PixelY(a,a.JournalCells[0]);
        var b=Supplementary(3,width,height);for(int i=0;i<20;i++)b.ScrollStep(-.003f);Near(end,PixelY(b,b.JournalCells[0]));
        Assert.Equal(90,b.journalVisible.Count);Assert.Equal(0,HkPauseContracts.PlayerData.instance.Writes);
    }

    [Theory]
    [InlineData(1240,6)] [InlineData(600,10)] [InlineData(1600,0)]
    public void M01_MarkerBadgeIs34WhiteBodyTopRightInActualAdaptiveCell(int width,int spare)
    {
        var f=Map(width);HkPauseContracts.PlayerData.instance.spareMarkers_b=spare;f.MapMarkerMode=true;f.MarkerStripStep();
        var label=f.MarkerCount(0);Near(34,label.UnitScale*label.Tmp.InkHeight);Ink(Color.white,label.Tmp.color);
        Assert.Same(Resources.BodyFont,label.Tmp.font);Assert.Same(Resources.BodyFont.material,label.Tmp.fontSharedMaterial);Assert.Equal(TextAlignment.TopRight,label.Tmp.alignment);
        var bounds=label.Tmp.textBounds;var max=label.Root.TransformPoint(bounds.max);
        var g=f.LowerGeometry();float pixel=2*f.attrCam.orthographicSize/f.BOTTOM_H;
        Near(f.attrCam.transform.position.x-g.Width*pixel/2+(width/5f-6)*pixel,max.x);
        Near(f.attrCam.transform.position.y+g.Height*pixel/2-(g.TabTop+6)*pixel,max.y);
        Assert.Equal(spare.ToString(),label.Text);Assert.Null(Find(f.frameRoot.transform,"F_MapMarkerCount4"));
    }

    [Fact]
    public void M02_MarkerStripOmitsOrdinaryGlowAndRestoresItOnExit()
    {
        var f=Map();
        f.NativeTab(HKLowerLayout.ColumnForTab(0)).sprite=new() { name="admitted-map-tab",bounds=new(Vector3.zero,new Vector3(2,1,0)) };
        f.FramePositionStep();Assert.True(f.NativeTabGlow.enabled);
        f.MapMarkerMode=true;f.MarkerStripStep();Assert.False(f.NativeTabGlow.enabled);
        Near(52*2*f.attrCam.orthographicSize/f.BOTTOM_H,f.NativeTabTl.bounds.size.y);
        f.MapMarkerMode=false;f.FramePositionStep();Assert.True(f.NativeTabGlow.enabled);
    }

    [Theory]
    [InlineData(1240)] [InlineData(800)] [InlineData(1600)]
    public void M03_ResetOnlyForMovedViewAndFixed192PlateLeftOfActualSlider(int width)
    {
        var f=Map(width);Assert.False(f.mapResetAction.Root.gameObject.activeSelf);
        f.mapUserPan=new(2,0);f.PositionActionsStep();Assert.True(f.mapResetAction.Root.gameObject.activeSelf);
        var hit=f.mapResetAction.Hit;float pixel=2*f.attrCam.orthographicSize/f.BOTTOM_H;
        Near(192*pixel,hit.size.x);Near(54*pixel,hit.size.y);
        Assert.True(hit.max.x<Field<float>(f,"mapZoomX")-Field<float>(f,"mapZoomHitHalfWidth"));
        Assert.True(f.MapActionTap(hit.center));
        for(int i=0;i<10;i++)f.MapPinchStep(0,.5f,.5f);
        f.PositionActionsStep();Assert.False(f.mapResetAction.Root.gameObject.activeSelf);
        Near(1,f.mapUserZoom);Near(0,f.mapUserPan.sqrMagnitude);
    }
}
