using Xunit;
using HkPauseContracts;
using HK=HkPauseContracts.HKDualScreen;
using HkPlayerData=HkPauseContracts.PlayerData;
using Bounds=HkPauseContracts.Bounds;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightMapHeaderCanonicalTests
{
    static HK Map()
    {
        var f=new HK(); f.tab.cur=f.tab.tap=0;f.attrCam.orthographicSize=540;f.attrCam.aspect=1240f/1080;f.attrCam.rect=new(0,0,1,1);
        f.mapGm=new GameMap();f.mapAnyAvailable=f.mapAvailable=f.mapContentVisible=true;
        f.frameRoot=null;f.FrameBuildStep();f.BuildActionsStep();return f;
    }
    [Fact]
    public void NL02_ActualColdBuildDiscardsPartialRootAndRecoversWithoutUnrelatedTeardown()
    {
        var f=new HK();f.frameRoot=null;f.ThrowShellRule=true;
        var failure=Record.Exception(f.FrameBuildStep);
        Assert.Null(f.frameRoot);
        Assert.Equal(0,f.FrameTeardowns);Assert.Equal(0,f.Teardowns);
        f.ThrowShellRule=false;Time.frameCount+=120;f.FrameBuildStep();
        Assert.NotNull(f.frameRoot);Assert.NotNull(f.shellRule);
        Assert.NotNull(f.NativeTab(0));Assert.NotNull(f.NativeTab(4));
        Assert.Equal(0,f.FrameTeardowns);Assert.Equal(0,f.Teardowns);
    }
    [Fact]
    public void ActualLateColdFailureResetsNoMapRetryAndRollsBackOnlyAttemptAssets()
    {
        var f=new HK();f.frameRoot=null;int assets=f.OwnedAssetCount;f.ThrowMapMask=true;f.FrameBuildStep();
        Assert.Null(f.frameRoot);Assert.Equal(assets,f.OwnedAssetCount);Assert.True(f.DestroyedAssetCount>0);
        Assert.Equal(0,f.Teardowns);Assert.Equal(0,f.FrameTeardowns);
        f.ThrowMapMask=false;Time.frameCount+=119;f.FrameBuildStep();Assert.Null(f.frameRoot);
        Time.frameCount++;f.FrameBuildStep();Assert.NotNull(f.frameRoot);Assert.NotNull(f.NoMapSymbol);
        Assert.NotNull(f.mapResetAction);Assert.NotNull(f.MapFade);
    }
    [Fact]
    public void ActualFailedMapArtDecodeImmediatelyDisposesUnownedTexture()
    {
        var f=Map();int count=f.OwnedAssetCount,destroyed=f.DestroyedAssetCount;
        Assert.Null(f.DecodeInvalidMapArt());Assert.Equal(count,f.OwnedAssetCount);Assert.Equal(destroyed+1,f.DestroyedAssetCount);
    }
    [Fact]
    public void NL03_ActualExistingAndNewFallbackHeaderRenderersSortAboveHudMask()
    {
        var f=new HK();f.BuildHeaderStep();
        Assert.All(f.shellTitle.Root.GetComponentsInChildren<Renderer>(true),r=>Assert.Equal(30090,r.sortingOrder));
        f.shellTitle.Tmp.SpawnFallbackOnMesh=true;f.HeaderTickStep(0);
        Assert.NotNull(f.shellTitle.Tmp.LastFallback);
        Assert.Equal(30090,f.shellTitle.Tmp.LastFallback.sortingOrder);
    }
    [Fact]
    public void NL05_ActualIndependentlyMissingResetControlRetriesAfterIconsResolved()
    {
        var f=Map();var healthy=f.mapViewAction.Root;f.ResolveAllShellDonors();
        f.mapResetAction.Label=null;Time.frameCount+=120;f.ResolveShellStep();
        Assert.NotNull(f.mapResetAction.Label);Assert.Same(healthy,f.mapViewAction.Root);
    }
    [Fact]
    public void ActualFailedIndividualControlWaitsFullDeadlineAndPreservesHealthySiblings()
    {
        var f=Map();var view=f.mapViewAction.Root;var marker=f.mapMarkerAction.Root;f.mapResetAction.Label=null;
        f.ThrowNextLabelClone=true;Time.frameCount+=120;f.ResolveShellStep();
        Assert.Null(f.mapResetAction);Assert.Equal(1,f.ShellWarnings);
        int scans=DiscoveryCounters.Hierarchy;
        for(int i=0;i<119;i++) { Time.frameCount++;f.ResolveShellStep(); }
        Assert.Null(f.mapResetAction);Assert.Equal(scans,DiscoveryCounters.Hierarchy);
        Time.frameCount++;f.ResolveShellStep();Assert.NotNull(f.mapResetAction);
        Assert.Same(view,f.mapViewAction.Root);Assert.Same(marker,f.mapMarkerAction.Root);
    }
    [Fact]
    public void ActualNoMapNativeArtArrivalRetriesIndependentlyAfterIconsResolve()
    {
        var sprites=Resources.Sprites;
        try
        {
            Resources.Sprites=sprites.Where(s=>s.name!="No_Map_symbol").ToArray();
            var f=Map();f.ResolveAllShellDonors();Assert.Null(f.NoMapSymbol);int scans=Resources.Discoveries;
            Resources.Sprites=sprites;
            for(int i=0;i<119;i++) { Time.frameCount++;f.HeaderTickStep(0); }
            Assert.Null(f.NoMapSymbol);Assert.Equal(scans,Resources.Discoveries);
            Time.frameCount++;f.HeaderTickStep(0);Assert.NotNull(f.NoMapSymbol);
        }
        finally { Resources.Sprites=sprites; }
    }
    [Theory]
    [InlineData(1240,1080,540)] [InlineData(1600,1200,200)] [InlineData(900,800,800)]
    public void ActualFourMasksClipAtBodyBoundaryAcrossPanelSizesAndCameraPanZoom(int width,int height,float ortho)
    {
        var f=Map();f.BOTTOM_W=width;f.BOTTOM_H=height;f.attrCam.orthographicSize=ortho;f.attrCam.aspect=(float)width/height;
        f.attrCam.transform.position=new(987,-123,20);f.FramePositionStep();var body=f.MapBody;float pixel=2*ortho/height;
        float X(float x)=>987+(x-width/2f)*pixel;float Y(float y)=>-123+(height/2f-y)*pixel;
        var top=f.MapClipMask(0);var bottom=f.MapClipMask(1);var left=f.MapClipMask(2);var right=f.MapClipMask(3);
        Assert.Equal(Y(body.y),top.bounds.min.y,3);Assert.Equal(Y(body.y+body.height),bottom.bounds.max.y,3);
        Assert.Equal(X(body.x),left.bounds.max.x,3);Assert.Equal(X(body.x+body.width),right.bounds.min.x,3);
        Assert.Equal(body.height*pixel,left.bounds.size.y,3);Assert.Equal(body.height*pixel,right.bounds.size.y,3);
        foreach(var mask in new[]{top,bottom,left,right})
        { Assert.True(mask.enabled);Assert.Equal("Inventory",mask.sortingLayerName);Assert.Equal(10000,mask.sortingOrder);Assert.Equal(1,((Material)mask.sharedMaterial).color.a); }
        Assert.Equal(body.width*pixel,f.MapFade.bounds.size.x,3);Assert.Equal(body.height*pixel,f.MapFade.bounds.size.y,3);
    }
    [Fact]
    public void NL09_ActualBenchToastReplacesLocalizedTitleAndRestoresAfterExpiry()
    {
        var f=Map();f.BuildHeaderStep();f.Manager.Zone="CROSSROADS";f.SetBenchToast("TELEPORTING",10);
        Time.unscaledTime=9;f.HeaderTickStep(0);Assert.Equal("TELEPORTING",f.shellTitle.Tmp.text);
        Time.unscaledTime=10.01f;f.HeaderTickStep(0);Assert.Equal("CROSSROADS",f.shellTitle.Tmp.text);
    }
    [Fact]
    public void ActualMapActionsOccupyHeaderResetAndSliderBodyRight()
    {
        var f=Map();f.mapUserPan=new(2,0);Time.unscaledTime=0;f.PositionActionsStep();var g=f.LowerGeometry();
        float Y(Bounds b)=>g.Height/2-b.center.y;
        Assert.InRange(Y(f.mapViewAction.Hit),27,g.HudHeight-27);
        Assert.InRange(Y(f.mapMarkerAction.Hit),27,g.HudHeight-27);
        Assert.True(f.mapResetAction.Hit.min.y>=g.Height/2-(g.TabTop-4));
        Assert.Equal(54,f.mapResetAction.Plate.bounds.size.y,3);
        Assert.Equal(SpriteDrawMode.Sliced,f.mapResetAction.Plate.drawMode);
        Assert.Equal(0,f.mapResetAction.Label.color.r);Assert.Equal(1,f.mapResetAction.Label.color.a);
        var slider=f.SliderEndpoints;Assert.Equal(1210-29f/2-620,slider.x,3);
        Assert.Equal(540-272,slider.y,3);Assert.Equal(540-924,slider.z,3);
    }
    [Fact]
    public void ActualMapResetAndSliderHoldThreeSecondsThenFadePointSix()
    {
        var f=Map();f.mapUserPan=new(2,0);Time.unscaledTime=0;f.PositionActionsStep();
        Time.unscaledTime=3;f.PositionActionsStep();Assert.Equal(1,f.mapResetAction.Plate.color.a);
        Time.unscaledTime=3.3f;f.PositionActionsStep();Assert.Equal(.5f,f.mapResetAction.Plate.color.a,3);
        Time.unscaledTime=3.61f;f.PositionActionsStep();Assert.False(f.mapResetAction.Root.gameObject.activeSelf);
        Assert.True(f.mapViewAction.Root.gameObject.activeSelf);
    }
    [Fact]
    public void ActualNoMapUsesCenteredNativeSymbolWithoutTextFallback()
    {
        var f=Map();f.BuildNoMapStep();f.mapAvailable=false;f.HeaderTickStep(0);
        Assert.NotNull(f.NoMapSymbol);Assert.Equal("No_Map_symbol",f.NoMapSymbol.sprite.name);
        Assert.True(f.NoMapSymbol.enabled);Assert.Equal(0,f.NoMapSymbol.bounds.center.x,3);
        Assert.Equal(540-598,f.NoMapSymbol.bounds.center.y,3);
        Assert.Equal(260,f.NoMapSymbol.bounds.size.y,3);
    }
    [Fact]
    public void ActualFullMapRemainsReachableWhenOnlyCurrentAreaHasNoMap()
    {
        var f=Map();f.mapAvailable=false;f.mapContentVisible=false;f.PositionActionsStep();
        Assert.True(f.mapViewAction.Root.gameObject.activeSelf);
        Assert.True(f.MapActionTap(f.mapViewAction.Hit.center));Assert.True(f.WorldMapMode);Assert.True(f.mapNeedsSetup);
    }
    [Fact]
    public void ActualMapFourEdgeFadeIsSlicedAndFortyPixelsAtCanonicalBodyPadding()
    {
        var f=Map();f.FramePositionStep();
        Assert.NotNull(f.MapFade);Assert.Equal(SpriteDrawMode.Sliced,f.MapFade.drawMode);
        Assert.Equal(1200,f.MapFade.bounds.size.x,3);Assert.Equal(676,f.MapFade.bounds.size.y,3);
        Assert.Equal(0,f.MapFade.bounds.center.x,3);Assert.Equal(540-598,f.MapFade.bounds.center.y,3);
    }
    [Fact]
    public void ActualMarkerStripReplacesTabsWithNativeIconsAndOwnsStripTaps()
    {
        var f=Map();f.mapMarkerMode=true;f.FramePositionStep();
        Assert.NotNull(f.MarkerStripIcon(0));Assert.True(f.MarkerStripIcon(0).enabled);
        Assert.False(f.NativeTab(0).enabled);
        Assert.True(f.MapActionTap(new(0,-470,4)));Assert.Equal(0,f.tab.cur);
    }
    [Fact]
    public void ActualMarkerCountsStayVisibleAndTrackNativeSpareChanges()
    {
        var f=Map();f.mapMarkerMode=true;f.FramePositionStep();
        Assert.Equal("6",f.MarkerCount(0).Tmp.text);Assert.True(f.MarkerCount(0).Renderer.enabled);
        HkPlayerData.instance.spareMarkers_b=0;f.FramePositionStep();
        Assert.Equal("0",f.MarkerCount(0).Tmp.text);Assert.True(f.MarkerCount(0).Renderer.enabled);
    }
    [Fact]
    public void ActualMissingMarkerCountRecoversUnchangedNativeSpareText()
    {
        var f=Map();f.mapMarkerMode=true;f.FramePositionStep();var icon=f.MarkerStripIcon(0);
        f.MissingMarkerCount(0);Time.frameCount+=120;f.ResolveShellStep();f.FramePositionStep();
        Assert.Equal("6",f.MarkerCount(0).Tmp.text);Assert.True(f.MarkerCount(0).Renderer.enabled);Assert.Same(icon,f.MarkerStripIcon(0));
    }
    [Fact]
    public void ActualMarkerCountPositionsNativeInkNotPaddedContainer()
    {
        var f=Map();f.mapMarkerMode=true;f.MarkerCount(0).Tmp.InkCenter=new(5,7,0);f.FramePositionStep();
        var label=f.MarkerCount(0);var ink=label.Root.TransformPoint(label.Tmp.textBounds.max);
        Assert.Equal(248-6-620,ink.x,3);Assert.Equal(540-940-6,ink.y,3);
    }
    [Fact]
    public void ActualActionFallbackGenerationAndLateSettleSortAboveActionPlate()
    {
        var f=Map();f.PositionActionsStep();f.mapViewAction.Label.SpawnFallbackOnMesh=true;
        Assert.True(f.MapActionTap(f.mapViewAction.Hit.center));f.PositionActionsStep();
        Assert.Equal(30050,f.mapViewAction.Label.LastFallback.sortingOrder);
        var child=new GameObject("late action fallback");child.transform.SetParent(f.mapViewAction.Root);
        var renderer=child.AddComponent<Renderer>();Time.frameCount++;f.PositionActionsStep();
        Assert.Equal(30050,renderer.sortingOrder);
    }
    [Fact]
    public void ActualShellNativeBrRetainsSignedDonorOrientationWithoutInventedHalfTurn()
    {
        var f=new HK();NativeSelectionInputs.Attach(f.Manager.inventoryFSM.transform.gameObject,false);
        f.ClearShellCursorDonors();f.frameRoot=null;f.FrameBuildStep();
        Assert.NotNull(f.NativeTabBr);Assert.True(f.NativeTabBr.flipX && f.NativeTabBr.flipY);
        Assert.Equal(0,f.NativeTabBr.transform.localRotation.Angle);
        f.FramePositionStep();Assert.True(f.NativeTabBr.flipX && f.NativeTabBr.flipY);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualHealthyMapHeaderAndControlsAllocateAndDiscoverExactlyZero(bool markers)
    {
        var f=Map();f.mapMarkerMode=markers;f.ResolveAllShellDonors();
        Time.unscaledTime=0;for(int i=0;i<150;i++) {Time.frameCount++;f.HeaderTickStep(0);f.PositionActionsStep();f.ResolveShellStep();f.FramePositionStep();}
        using var allocationScope = new StrictAllocationScope();
        int hierarchy=DiscoveryCounters.Hierarchy,scans=Resources.Discoveries;long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) {Time.frameCount++;f.HeaderTickStep(0);f.PositionActionsStep();f.ResolveShellStep();f.FramePositionStep();}
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);Assert.Equal(scans,Resources.Discoveries);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);
    }
    [Fact]
    public void ActualLateGeneratedHeaderFallbackReceivesHeaderSortingOnBoundedSettle()
    {
        var f=Map();f.HeaderTickStep(0);
        var child=new HkPauseContracts.GameObject("late fallback");child.transform.SetParent(f.shellTitle.Root);
        var r=child.AddComponent<Renderer>();r.sortingOrder=0;
        Time.frameCount++;f.HeaderTickStep(0);Assert.Equal(30090,r.sortingOrder);
    }
    [Fact]
    public void ActualEdgeFadeHasExactCubicFortyPixelRampAndTransparentCore()
    {
        var f=Map();var sprite=f.MapFade.sprite;var tex=sprite.texture;
        Assert.Equal(82,tex.width);Assert.Equal(82,tex.height);Assert.Equal(40,sprite.border.x);Assert.Equal(40,sprite.border.y);
        for(int i=0;i<=40;i++)
        {
            byte expected=(byte)(MathF.Pow(1-i/40f,3)*255);
            Assert.Equal(expected,tex.Pixels[i*82+41].a);Assert.Equal(expected,tex.Pixels[41*82+i].a);
            Assert.Equal(expected,tex.Pixels[(81-i)*82+41].a);Assert.Equal(expected,tex.Pixels[41*82+81-i].a);
        }
        Assert.Equal(0,tex.Pixels[40*82+40].a);Assert.Equal(0,tex.Pixels[41*82+41].a);
    }
    [Fact]
    public void ActualHeaderUsesFiftyTwoPixelInkNotPaddedRendererContainer()
    {
        var f=Map();f.HeaderTickStep(0);var ink=f.HeaderGlyphBounds;
        Assert.Equal(52,ink.size.y,3);Assert.Equal(0,ink.center.x,3);
        Assert.Equal(540-234,ink.min.y,3);
    }
    [Fact]
    public void ActualMapHeaderActionsDoNotShareTheEquippedCharmRow()
    {
        var f=Map();foreach(var sr in f.equipCharmSRs) sr.enabled=true;
        f.HeaderTickStep(0);Assert.All(f.equipCharmSRs,sr=>Assert.False(sr.enabled));
    }
    [Fact]
    public void ActualMissingSliderSpriteRetriesWithoutRebuildingHealthyButtons()
    {
        var f=Map();var view=f.mapViewAction.Root;f.MissingSliderSprite();Time.frameCount+=120;f.ResolveShellStep();
        Assert.NotNull(f.SliderSprite);Assert.Same(view,f.mapViewAction.Root);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualMapFitUsesCanonicalPaddedRectEvenWithLegacyFrameDisabled(bool world)
    {
        var f=Map();f.cfg.compFrame=0;f.cfg.compMapMargin=0;
        f.PrimeMapFit(new Bounds(new(7,9,0),new(1200,676,0)),world);f.MapFitStep();
        Assert.Equal(540,f.fit.ortho,3);Assert.Equal(-58f/540,f.mapInnerYc,5);
        Assert.Equal(7,f.fit.center.x);Assert.Equal(9,f.fit.center.y);
        int scans=f.MapMeasurements;long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) f.MapFitStep();
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(scans,f.MapMeasurements);
    }
    [Fact]
    public void ActualFadedSliderCannotStealAnAlreadyStartedBodyPan()
    {
        var f=Map();Time.unscaledTime=0;f.PositionActionsStep();Time.unscaledTime=4;f.PositionActionsStep();
        float x=(f.SliderEndpoints.x+620)/1240;
        Assert.False(f.MapTouchStep(1,x,.5f));Time.unscaledTime=4.1f;
        Assert.False(f.MapTouchStep(1,x,.4f));Assert.Equal(1,f.mapUserZoom);
    }
    [Fact]
    public void ActualSliderRequiresDownOnTrackAndCancelsAfterPinch()
    {
        var f=Map();Time.unscaledTime=0;f.PositionActionsStep();
        float x=(f.SliderEndpoints.x+620)/1240;
        Assert.False(f.MapTouchStep(1,.5f,.5f));Assert.False(f.MapTouchStep(1,x,.4f));
        f.MapTouchStep(0,x,.4f);Assert.True(f.MapTouchStep(1,x,.5f));
        Assert.False(f.MapTouchStep(2,x,.4f));Assert.False(f.MapTouchStep(1,x,.3f));
        f.MapTouchStep(0,x,.3f);Assert.True(f.MapTouchStep(1,x,.5f));
        Assert.True(f.MapTouchStep(1,x,.3f));Assert.True(f.mapUserZoom>1);
        f.transport.CleanTapSequence=79;Assert.True(f.MapTouchStep(0,x,.3f));Assert.Equal(79,f.CleanTapConsumed);
    }
    [Theory]
    [InlineData(.005f,.4f,.005f,.6f)]
    [InlineData(.4f,.1f,.6f,.1f)]
    [InlineData(.4f,.9f,.6f,.9f)]
    public void ActualPinchOutsidePaddedMapBodyCannotZoom(float ax,float ay,float bx,float by)
    {
        var f=Map();f.attrCam.rect=new(0,0,1,1);f.PositionActionsStep();
        f.MapPinchStep(2,ax,ay,bx,by);f.MapPinchStep(2,ax,ay,bx+.1f,by+.1f);
        Assert.Equal(1,f.mapUserZoom);Assert.Equal(0,f.mapUserPan.sqrMagnitude);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualMarkerStripRequiresSameCellDownAndCancelsPinch(bool pinch)
    {
        var f=Map();f.attrCam.rect=new(0,0,1,1);f.mapMarkerMode=true;f.PositionActionsStep();
        float downX=pinch ? .9f : .1f;
        f.MapPinchStep(1,downX,.94f);
        if(pinch) f.MapPinchStep(2,downX,.94f,.85f,.94f);
        f.transport.CleanTapSequence++;f.transport.CleanTapX=.9f;f.transport.CleanTapY=.94f;
        f.MapPinchStep(0,downX,.94f);
        Assert.False(f.MarkerErasing);Assert.Equal(0,f.tab.cur);
        f.MapPinchStep(1,.9f,.94f);f.transport.CleanTapSequence++;f.transport.CleanTapX=.9f;
        f.MapPinchStep(0,.9f,.94f);Assert.True(f.MarkerErasing);
        f.MapPinchStep(1,.1f,.94f);f.transport.CleanTapSequence++;f.transport.CleanTapX=.1f;
        f.MapPinchStep(0,.1f,.94f);Assert.False(f.MarkerErasing);
    }
    static (HK Owner,NativeMapDonors Donors) DonorMap()
    {
        var f=Map();var donors=new NativeMapDonors(f);f.FramePositionStep();return(f,donors);
    }
    static int Compare(Renderer a,Renderer b)
    {
        int layer=SortingLayer.GetLayerValueFromID(a.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(b.sortingLayerID));
        return layer!=0 ? layer : a.sortingOrder.CompareTo(b.sortingOrder);
    }
    static void ArtOnlyOrder(HK f,NativeMapDonors donors,IEnumerable<Renderer> extra=null)
    {
        var fade=f.MapFade;
        foreach(int id in donors.Art) Assert.True(Compare(donors.Renderers[id],fade)<0,$"native art {id} must draw before fade");
        foreach(int id in donors.Annotations)
        {
            var r=donors.Renderers[id];
            // A detached, replaced native fallback is no longer the clone's drawing owner.
            if(!r.transform.IsChildOf(f.mapClone.transform)) continue;
            Assert.True(Compare(fade,r)<0,$"native annotation {id} must draw after art-only fade");
            for(int side=0;side<4;side++) Assert.True(Compare(r,f.MapClipMask(side))<0,$"native annotation {id} must remain below outer hard clip");
        }
        if(extra!=null) foreach(var r in extra) { Assert.True(Compare(fade,r)<0);for(int side=0;side<4;side++) Assert.True(Compare(r,f.MapClipMask(side))<0); }
        for(int side=0;side<4;side++) Assert.True(Compare(f.MapClipMask(side),f.mapViewAction.LabelRenderer)<0);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A21_DonorNativeAnnotationsStayAboveArtOnlyFadeAcrossPanZoom(bool moved)
    {
        var (f,donors)=DonorMap();
        if(moved)
        {
            f.cfg.compMapPinch=1;f.PositionActionsStep();
            f.MapPinchStep(2,.4f,.4f,.6f,.6f);f.MapPinchStep(2,.35f,.35f,.65f,.65f);Assert.True(f.mapUserZoom>1);
            f.MapPinchStep(1,.5f,.5f);f.MapPinchStep(1,.55f,.55f);Assert.True(f.mapUserPan.sqrMagnitude>0);
            Assert.Empty(HkPlayerData.instance.placedMarkers_b);Assert.Equal(6,HkPlayerData.instance.spareMarkers_b);
            f.attrCam.transform.position=new(987,-123,20);f.attrCam.orthographicSize=200;f.mapClone.transform.localScale=new(3,3,1);f.mapClone.transform.localPosition=new(56,-98,0);f.FramePositionStep();
        }
        donors.NativeSortingTick();ArtOnlyOrder(f,donors);
    }
    [Fact]
    public void A21_DonorRecreatedTmpFallbackGetsBoundedOrderingRecovery()
    {
        var (f,donors)=DonorMap();var fallback=donors.RecreateFallback();
        for(int i=0;i<3;i++) { Time.frameCount++;f.FramePositionStep(); }
        donors.NativeSortingTick();ArtOnlyOrder(f,donors,new[]{fallback});
    }
    [Fact]
    public void A21_DonorDynamicMarkerRefreshDoesNotPutNativeMarkerUnderFade()
    {
        var (f,donors)=DonorMap();var go=new GameObject("B1 dynamic native marker");go.transform.SetParent(donors.Objects["Game_Map/Map Markers"].transform);
        var marker=go.AddComponent<SpriteRenderer>();marker.sortingLayerID=957720295;marker.sortingOrder=0;marker.sharedMaterial=donors.Renderers[16517].sharedMaterial;
        f.mapGm.mapMarkersBlue[0]=go;f.mapMarkerMode=true;f.PositionActionsStep();Assert.True(f.MapActionTap(new(0,0,4)));
        f.FramePositionStep();ArtOnlyOrder(f,donors,new[]{marker});
        Assert.Single(HkPlayerData.instance.placedMarkers_b);Assert.Equal(5,HkPlayerData.instance.spareMarkers_b);Assert.Equal(1,f.mapGm.Setups);
    }
    [Fact]
    public void A21_DonorRoleSetupPreservesNativeVisibilityMaterialsFontAndAuthoredOrdering()
    {
        var f=Map();var donors=new NativeMapDonors(f);
        var states=donors.Renderers.ToDictionary(p=>p.Key,p=>(p.Value.enabled,p.Value.gameObject.activeSelf,p.Value.sharedMaterial,p.Value.sortingLayerID,p.Value.sortingOrder));
        var texts=donors.Labels.ToDictionary(p=>p.Key,p=>(p.Value.text,p.Value.fontSharedMaterial,p.Value.enabled));
        var tints=donors.Renderers.Where(p=>p.Value is SpriteRenderer).ToDictionary(p=>p.Key,p=>((SpriteRenderer)p.Value).color);
        f.FramePositionStep();donors.NativeSortingTick();
        foreach(var pair in states) { var r=donors.Renderers[pair.Key];Assert.Equal(pair.Value.enabled,r.enabled);Assert.Equal(pair.Value.activeSelf,r.gameObject.activeSelf);Assert.Same(pair.Value.sharedMaterial,r.sharedMaterial); }
        foreach(var pair in texts) { var label=donors.Labels[pair.Key];Assert.Equal(pair.Value.text,label.text);Assert.Same(pair.Value.fontSharedMaterial,label.fontSharedMaterial);Assert.Equal(pair.Value.enabled,label.enabled); }
        foreach(var pair in tints) { var tint=((SpriteRenderer)donors.Renderers[pair.Key]).color;Assert.Equal(pair.Value.r,tint.r);Assert.Equal(pair.Value.g,tint.g);Assert.Equal(pair.Value.b,tint.b);Assert.Equal(pair.Value.a,tint.a); }
        foreach(int a in donors.Annotations) foreach(int b in donors.Annotations)
        {
            var x=states[a];var y=states[b];int oldLayer=SortingLayer.GetLayerValueFromID(x.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(y.sortingLayerID));
            int authored=oldLayer!=0 ? oldLayer : x.sortingOrder.CompareTo(y.sortingOrder);
            Assert.Equal(Math.Sign(authored),Math.Sign(Compare(donors.Renderers[a],donors.Renderers[b])));
        }
    }
    [Fact]
    public void A21_DonorHealthyTicksAllocateAndDiscoverExactlyZeroAcrossThreeHundredTicks()
    {
        var (f,donors)=DonorMap();f.ResolveAllShellDonors();
        for(int i=0;i<150;i++) { Time.frameCount++;f.FramePositionStep(); }
        int hierarchy=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries;long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) { Time.frameCount++;f.FramePositionStep(); }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void T01CallerMissingGlyphsStayHiddenAndRetrySameTextAtBoundedDeadline(int owner)
    {
        var f=Map();f.mapMarkerMode=owner==2;
        var tmp=owner==0 ? f.shellTitle.Tmp : owner==1 ? f.mapViewAction.Label : f.MarkerCount(0).Tmp;
        var renderer=owner==0 ? f.shellTitle.Renderer : owner==1 ? f.mapViewAction.LabelRenderer : f.MarkerCount(0).Renderer;
        void Tick() { if(owner==0) f.HeaderTickStep(0); else f.PositionActionsStep(); }
        tmp.AutomaticGlyphInput=false;tmp.textInfo=null;
        Tick();Assert.False(renderer.enabled);
        if(owner==1) { Assert.False(f.mapViewAction.Plate.enabled);Assert.False(f.MapActionTap(f.mapViewAction.Hit.center)); }
        string requested=tmp.text;int generations=tmp.MeshGenerations;
        for(int i=0;i<119;i++) { Time.frameCount++;Tick();Assert.False(renderer.enabled); }
        Assert.Equal(generations,tmp.MeshGenerations);
        tmp.AutomaticGlyphInput=true;Time.frameCount++;Tick();
        Assert.Equal(requested,tmp.text);Assert.True(renderer.enabled);
        if(owner==1) Assert.True(f.mapViewAction.Plate.enabled);
        Time.frameCount+=3;Tick();
        int walks=DiscoveryCounters.Hierarchy;generations=tmp.MeshGenerations;
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) { Time.frameCount++;Tick(); }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.Equal(generations,tmp.MeshGenerations);Assert.Equal(walks,DiscoveryCounters.Hierarchy);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void T01CallerContentChangeRetiresPreviousGlyphDomainBeforeFailedGeneration(int owner)
    {
        var f=Map();f.mapMarkerMode=owner==2;
        void Tick() { if(owner==0) f.HeaderTickStep(0); else f.PositionActionsStep(); }
        Tick();
        var tmp=owner==0 ? f.shellTitle.Tmp : owner==1 ? f.mapViewAction.Label : f.MarkerCount(0).Tmp;
        var renderer=owner==0 ? f.shellTitle.Renderer : owner==1 ? f.mapViewAction.LabelRenderer : f.MarkerCount(0).Renderer;
        Assert.True(renderer.enabled);
        var oldHit=f.mapViewAction.Hit;
        if(owner==0) f.Manager.Zone="GREENPATH";
        else if(owner==1) Assert.True(f.MapActionTap(oldHit.center));
        else HkPlayerData.instance.spareMarkers_b=3;
        tmp.AutomaticGlyphInput=false;tmp.textInfo=null;Tick();
        Assert.False(renderer.enabled);
        if(owner==1) { Assert.False(f.mapViewAction.Plate.enabled);Assert.False(f.MapActionTap(oldHit.center)); }
        tmp.AutomaticGlyphInput=true;Time.frameCount+=120;Tick();Assert.True(renderer.enabled);
    }
    static Renderer NewNativeMarker(HK f,NativeMapDonors donors)
    {
        var go=new GameObject("B1 recreated native marker");go.transform.SetParent(donors.Objects["Game_Map/Map Markers"].transform);
        var r=go.AddComponent<SpriteRenderer>();r.sortingLayerID=957720295;r.sortingOrder=0;r.sharedMaterial=donors.Renderers[16517].sharedMaterial;
        f.mapGm.mapMarkersBlue[0]=go;return r;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A21_CompleteNativeMapTickSetupAndPinWatchOrderNewAnnotationsImmediately(bool pinsOnly)
    {
        var (f,donors)=DonorMap();Time.frameCount+=10;f.FramePositionStep();f.NativeMapSettled();Renderer marker=null;
        f.mapGm.SetupObserver=p=>marker=NewNativeMarker(f,donors);
        if(pinsOnly) { f.NativePinStamp++;Time.frameCount=(Time.frameCount/20+1)*20; }
        else f.mapNeedsSetup=true;
        f.NativeMapTickStep();Assert.NotNull(marker);ArtOnlyOrder(f,donors,new[]{marker});
        Assert.Equal(pinsOnly ? 1 : 0,f.mapGm.PinSetups);Assert.Equal(pinsOnly ? 0 : 1,f.mapGm.FullSetups);
        Assert.Empty(HkPlayerData.instance.placedMarkers_b);Assert.Equal(6,HkPlayerData.instance.spareMarkers_b);
    }
    [Fact]
    public void A21_NativeTmpEventCatchesLateSubmeshThenStopsDiscoveryAtExactDeadline()
    {
        var (f,donors)=DonorMap();f.ResolveAllShellDonors();Time.frameCount+=10;f.FramePositionStep();
        var label=donors.Labels[12599];label.NativeMeshChanged();
        // Explicit engine-delay injection: instantiate the serialized UI-layer
        // submesh after its owner's event, rather than native AddSubTextObject's
        // immediate parent-order copy. The two-tick recovery is executed, not copied.
        var go=new GameObject("late serialized fallback");go.transform.SetParent(label.transform);
        var late=go.AddComponent<MeshRenderer>();late.sortingLayerID=629535577;late.sortingOrder=0;
        go.transform.TextComponents.Add(new TMProOld.TMP_SubMesh(late){textComponent=label,enabled=true});
        Time.frameCount++;f.FramePositionStep();ArtOnlyOrder(f,donors,new[]{late});
        int one=DiscoveryCounters.Hierarchy;Time.frameCount++;f.FramePositionStep();Assert.True(DiscoveryCounters.Hierarchy>one);
        int two=DiscoveryCounters.Hierarchy;long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) { Time.frameCount++;f.FramePositionStep(); }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(two,DiscoveryCounters.Hierarchy);
    }
    [Fact]
    public void A21_UnrelatedNativeTmpEventCannotScanOrReorderMapOwner()
    {
        var (f,donors)=DonorMap();Time.frameCount+=10;f.FramePositionStep();
        var other=new GameObject("top-screen native label").AddComponent<MeshRenderer>();var label=new TMProOld.TextMeshPro(other);
        int walks=DiscoveryCounters.Hierarchy;var orders=donors.Renderers.Values.Select(r=>(r.sortingLayerID,r.sortingOrder)).ToArray();
        label.NativeMeshChanged();Assert.Equal(walks,DiscoveryCounters.Hierarchy);
        Assert.Equal(orders,donors.Renderers.Values.Select(r=>(r.sortingLayerID,r.sortingOrder)).ToArray());
    }
    [Fact]
    public void A21_CompleteRetirementRestoresOnlyOwnedSortingAndRebuildSubscribesExactlyOnce()
    {
        var f=Map();var donors=new NativeMapDonors(f);int baseline=TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count;
        var states=donors.Renderers.ToDictionary(p=>p.Key,p=>(p.Value.sortingLayerID,p.Value.sortingOrder));
        f.FramePositionStep();Assert.Equal(baseline+1,TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count);
        f.RetireMapCachesStep();Assert.Equal(baseline,TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count);Assert.Null(f.mapClone);
        foreach(var p in states) { Assert.Equal(p.Value.sortingLayerID,donors.Renderers[p.Key].sortingLayerID);Assert.Equal(p.Value.sortingOrder,donors.Renderers[p.Key].sortingOrder); }
        int walks=DiscoveryCounters.Hierarchy;donors.TextChanged();Assert.Equal(walks,DiscoveryCounters.Hierarchy);
        f.mapClone=new GameObject();f.mapClone.transform.SetParent(f.compRoot);f.mapGm=new GameMap();f.mapContentVisible=f.mapAnyAvailable=f.mapAvailable=true;
        f.FrameBuildStep();var rebuilt=new NativeMapDonors(f);f.FramePositionStep();f.MapRolesStep();
        Assert.Equal(baseline+1,TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count);ArtOnlyOrder(f,rebuilt);
        int own=f.Destroyed.Count(g=>g==donors.Objects["Game_Map"]);Assert.Equal(1,own);
        f.RetireMapCachesStep();Assert.Equal(baseline,TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count);
    }
    [Fact]
    public void A21_ColdShellFailureCannotRetireHealthyNativeMapRoleOwner()
    {
        var f=new HK();f.tab.cur=f.tab.tap=0;f.mapGm=new GameMap();f.mapContentVisible=f.mapAvailable=f.mapAnyAvailable=true;f.frameRoot=null;
        f.attrCam.orthographicSize=540;f.attrCam.aspect=1240f/1080;
        var donors=new NativeMapDonors(f);int baseline=TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count;f.MapRolesStep();
        f.ThrowMapMask=true;f.FrameBuildStep();Assert.Null(f.frameRoot);
        Assert.Equal(baseline+1,TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count);Assert.DoesNotContain(f.mapClone,f.Destroyed);
        f.ThrowMapMask=false;Time.frameCount+=120;f.FrameBuildStep();f.FramePositionStep();ArtOnlyOrder(f,donors);
        Assert.Equal(baseline+1,TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count);f.RetireMapCachesStep();Assert.Equal(baseline,TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Count);
    }
    [Fact]
    public void A21_NativeRolesUseTypedAreaHierarchyRatherThanGuessedObjectNames()
    {
        var f=Map();var donors=new NativeMapDonors(f);f.mapGm.areaGreenpath.name="changed native area title";
        donors.Renderers[15772].gameObject.name="pin_compass_text";donors.Renderers[11817].gameObject.name="room art";
        donors.Renderers[16517].gameObject.name="Crossroads_999";f.FramePositionStep();ArtOnlyOrder(f,donors);
    }
    [Fact]
    public void ActualNativeMarkerActionsPreserveCountsAndRedraw()
    {
        var f=Map();f.mapMarkerMode=true;f.PositionActionsStep();var p=HkPlayerData.instance;
        Assert.True(f.MapActionTap(new(0,0,4)));Assert.Single(p.placedMarkers_b);Assert.Equal(5,p.spareMarkers_b);Assert.Equal(1,f.mapGm.Setups);
        Assert.True(f.MapActionTap(new(0,0,4)));Assert.Empty(p.placedMarkers_b);Assert.Equal(6,p.spareMarkers_b);Assert.Equal(2,f.mapGm.Setups);
    }
}
