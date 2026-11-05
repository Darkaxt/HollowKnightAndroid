using Xunit;
using HkPauseContracts;
using HK=HkPauseContracts.HKDualScreen;
using GameObject=HkPauseContracts.GameObject;
using Renderer=HkPauseContracts.Renderer;
using Bounds=HkPauseContracts.Bounds;
using Rect=HkPauseContracts.Rect;
using HkPlayerData=HkPauseContracts.PlayerData;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightNativeLayoutIntegrationTests
{
    static void Close(float expected,float actual) => Assert.Equal(expected,actual,2);
    static Bounds Occupied(Transform root)
    {
        var rs=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
        Assert.NotEmpty(rs);var b=rs[0].bounds;foreach(var r in rs.Skip(1)) b.Encapsulate(r.bounds);return b;
    }
    static void RectAt(HK lower,Renderer mask,Rect rect)
    {
        Close(lower.compRoot.position.x+rect.x+rect.width/2-lower.BOTTOM_W/2f,mask.transform.position.x);
        Close(lower.compRoot.position.y+lower.BOTTOM_H/2f-rect.y-rect.height/2,mask.transform.position.y);
        Close(rect.width,mask.transform.localScale.x);Close(rect.height,mask.transform.localScale.y);
        Assert.True(mask.enabled);
    }
    static void Graphics(HK lower,HK.PaneGraphics v,HKLowerLayout.NativePaneColumns p)
    {
        var g=lower.LowerGeometry();
        Close(lower.compRoot.position.x+p.LeftGutter-g.Width/2,v.RuleLeft.transform.position.x);
        Close(lower.compRoot.position.x+p.RightGutter-g.Width/2,v.RuleRight.transform.position.x);
        Close(lower.compRoot.position.y+g.Height/2-p.Top-p.Height/2,v.RuleLeft.transform.position.y);
        Close(p.Height,v.RuleLeft.transform.localScale.x);
        foreach(var rule in new[]{v.RuleLeft,v.RuleRight})
        {
            Assert.Equal(.93f,rule.color.r);Assert.Equal(.91f,rule.color.g);
            Assert.Equal(.86f,rule.color.b);Assert.Equal(1f,rule.color.a);
        }
        RectAt(lower,v.Top,new(0,0,g.Width,p.Top));
        RectAt(lower,v.Bottom,new(0,p.Top+p.Height,g.Width,g.Height-p.Top-p.Height));
        RectAt(lower,v.Left,new(0,p.Top,p.SubjectX,p.Height));
        RectAt(lower,v.Right,new(p.DetailX+p.DetailWidth,p.Top,g.Width-p.DetailX-p.DetailWidth,p.Height));
    }
    static void Detail(HK lower,HK.NativePaneLabel name,HK.NativePaneLabel prose,HKLowerLayout.NativePaneColumns p,bool charms=false)
    {
        float scale=charms ? .78f : Math.Clamp(p.DetailWidth/500f,.78f,1);
        float titleHeight=charms ? 56 : 48*scale+10,proseTop=charms ? 100 : titleHeight+10,reserve=charms ? 78 : 0;
        Close(48*scale,name.UnitScale*name.Tmp.InkHeight);Close(36*scale,prose.UnitScale*prose.Tmp.InkHeight);
        Assert.False(name.Tmp.enableAutoSizing);Assert.False(prose.Tmp.enableAutoSizing);
        Assert.Equal(HkPauseContracts.TextAlignment.TopLeft,prose.Tmp.alignment);
        Close(p.DetailX,name.ClipRect.x);Close(p.Top+(charms ? 0 : 4),name.ClipRect.y);Close(titleHeight,name.ClipRect.height);
        Close(p.DetailX,prose.ClipRect.x);Close(p.Top+proseTop,prose.ClipRect.y);
        Close(p.DetailWidth,prose.ClipRect.width);Close(p.Height-proseTop-reserve,prose.ClipRect.height);
        foreach(var r in prose.ClipRenderers)
        {
            var lo=prose.Root.InverseTransformPoint(lower.compRoot.position+new Vector3(p.DetailX-lower.BOTTOM_W/2f,lower.BOTTOM_H/2f-p.Top-p.Height+reserve,0));
            Close(lo.x,r.Clip.x);Close(lo.y,r.Clip.y);
        }
    }
    [Theory]
    [InlineData(1240,1080)] [InlineData(800,720)] [InlineData(1240,1440)]
    public void CompleteInventoryDispatcherPlacesSubjectThreeColumnsDetailGuttersAndClips(int width,int height)
    {
        HK lower=new();lower.BOTTOM_W=width;lower.BOTTOM_H=height;lower.compRoot.position=new(20000,20000,0);
        var pane=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);
        var r=lower.refsInv;Assert.True(r.canonicalReady);Assert.Same(pane,r.pane);Assert.Equal(21,r.slots.Count);
        var p=HKLowerLayout.NativeColumns(lower.LowerGeometry(),false);var occupied=Occupied(r.subject);
        Close(20000+p.SubjectX+p.SubjectWidth/2-width/2f,occupied.center.x);
        Close(20000+height/2f-p.Top-p.Height/2,occupied.center.y);
        Close(p.SubjectWidth,occupied.size.x);Assert.True(occupied.size.y<=p.Height+.02f);
        for(int i=0;i<3;i++)
        {
            var b=Occupied(r.slots[i].Root);
            Close(20000+p.ChooserX+i*(p.Cell+p.Gap)+p.Cell/2-width/2f,b.center.x);
            Close(20000+height/2f-p.Top-p.Cell/2,b.center.y);Close(p.Cell*.88f,b.size.x);
        }
        Assert.All(r.sourceDetail,x=>Assert.False(x.enabled));Graphics(lower,r.graphics,p);Detail(lower,r.nameLabel,r.descLabel,p);
        Assert.False(r.nameLabel.Renderer.enabled);Assert.False(r.descLabel.Renderer.enabled);
        var before=occupied;lower.NativeSource(pane,"Text Name").text="Localized selection";
        lower.NativeSource(pane,"Text Desc").text=new string('W',2000);r.descLabel.Tmp.MeasuredTextHeight=50;
        lower.NativeLayoutRouteStep(1);occupied=Occupied(r.subject);
        Close(before.center.x,occupied.center.x);Close(before.center.y,occupied.center.y);Close(before.size.x,occupied.size.x);
        Assert.True(r.descLabel.ScrollMax>0);Assert.True(r.nameLabel.Renderer.enabled);
        Close(height/2f,lower.attrCam.orthographicSize);Close(20000,lower.attrCam.transform.position.x);Close(20000,lower.attrCam.transform.position.y);
    }
    [Theory]
    [InlineData(1240,1080)] [InlineData(800,720)] [InlineData(1240,1440)]
    public void CompleteCharmsDispatcherPreservesNativeHoneycombBesideDetailAndSubject(int width,int height)
    {
        HK lower=new();lower.BOTTOM_W=width;lower.BOTTOM_H=height;lower.cfg.compCharmsRedesign=0;
        var pane=lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        Assert.True(lower.fit.valid);Assert.NotNull(lower.nativeCharmGrid);
        var p=HKLowerLayout.NativeColumns(lower.LowerGeometry(),true);var b=Occupied(lower.nativeCharmGrid);
        Close(p.ChooserX+p.ChooserWidth/2-width/2f,b.center.x);
        Close(height/2f-(p.Top+p.Height/2),b.center.y);Close(p.ChooserWidth,b.size.x);
        Assert.True(b.size.y<=p.Height+.02f);Graphics(lower,lower.CharmGraphics,p);Detail(lower,lower.CharmName,lower.CharmDescription,p,true);
        Assert.False(lower.nativeCharmPortrait.enabled);
        var boards=lower.nativeCharmGrid.Children.Where(t=>t.name.StartsWith("CharmBoard")).ToArray();Assert.Equal(40,boards.Length);
        var dx=boards[1].position.x-boards[0].position.x;var stagger=boards[8].position.x-boards[0].position.x;
        Close(.25f,stagger/dx); // Captured authored stagger survives reparent + uniform fitting.
        HK.NativeLabels["UI/CHARM_NAME_1"]="Localized charm";HK.NativeLabels["UI/CHARM_DESC_1"]=new string('Z',4000);
        lower.CharmDescription.Tmp.MeasuredTextHeight=60;HkPlayerData.instance.Ints["charmCost_1"]=3;
        lower.NativeDetailStep(1,boards[0],1);lower.NativeLayoutRouteStep(2);
        var after=Occupied(lower.nativeCharmGrid);Close(b.center.x,after.center.x);Close(b.center.y,after.center.y);Close(b.size.x,after.size.x);
        Assert.True(lower.nativeCharmPortrait.enabled);Close(p.SubjectX+p.SubjectWidth/2-width/2f,lower.nativeCharmPortrait.bounds.center.x);
        Close(height/2f-lower.LowerGeometry().BodyCenterY,lower.nativeCharmPortrait.bounds.center.y);
        Assert.Equal(3,lower.CostPips.Children.Count(t=>t.gameObject.activeSelf));
        Assert.True(lower.CharmDescription.ScrollMax>0);Assert.Equal("Localized charm",lower.CharmName.Text);
        lower.cfg.compCharmOffY=200;lower.cfg.compCharmsCenterY=-70;lower.cfg.compCharmZoom=9;lower.FramePane(new(900,900,0));
        Close(height/2f,lower.attrCam.orthographicSize);Close(0,lower.attrCam.transform.position.y);
    }
    [Fact]
    public void ActualScrollSeparatesChooserProseAndCharmsBodyWithBoundedOffsets()
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);var r=lower.refsInv;
        lower.NativeSource(pane,"Text Desc").text="Localized long prose";r.descLabel.Tmp.MeasuredTextHeight=60;lower.NativeLayoutRouteStep(1);
        for(int i=0;i<30;i++) lower.NativeScrollStep(0,-.2f);
        var p=HKLowerLayout.NativeColumns(lower.LowerGeometry(),false);
        float end=7*(p.Cell+p.Gap)-p.Gap-p.Height;
        Close(end,(float)r.chooserOffset);Close(0,r.descLabel.ScrollOffset);
        Assert.All(r.slots.Take(6),slot=>Assert.All(slot.Renderers,x=>Assert.False(x.enabled)));
        Assert.All(r.slots.Skip(6),slot=>Assert.All(slot.Renderers,x=>Assert.True(x.enabled)));
        for(int i=0;i<30;i++) lower.NativeScrollStep(1,-.2f);
        Close(r.descLabel.ScrollMax,r.descLabel.ScrollOffset);Close(end,(float)r.chooserOffset);
        lower.NativeScrollStep(2,1);Close(r.descLabel.ScrollMax,r.descLabel.ScrollOffset);Close(end,(float)r.chooserOffset);
        for(int i=0;i<30;i++) {lower.NativeScrollStep(0,.2f);lower.NativeScrollStep(1,.2f);}
        Close(0,(float)r.chooserOffset);Close(0,r.descLabel.ScrollOffset);
        var charms=lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        lower.NativeSource(charms,"Text Desc").text="Charm prose";lower.CharmDescription.Tmp.MeasuredTextHeight=60;lower.NativeLayoutRouteStep(2);
        var b=Occupied(lower.nativeCharmGrid);lower.NativeScrollStep(0,-1);Close(0,lower.CharmDescription.ScrollOffset);
        lower.NativeScrollStep(1,-100);Close(lower.CharmDescription.ScrollMax,lower.CharmDescription.ScrollOffset);
        var after=Occupied(lower.nativeCharmGrid);Close(b.center.x,after.center.x);Close(b.center.y,after.center.y);
    }
    [Fact]
    public void ProductionLanguageRefreshActuallyRebindsSelectedDetailWithoutMovingBase()
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);var r=lower.refsInv;
        var item=r.slots[0].Root;HK.NativeLabels["UI/INV_NAME_"+item.name]="First";HK.NativeLabels["UI/INV_DESC_"+item.name]="First prose";
        lower.NativeDetailStep(0,item);var before=Occupied(r.subject);Assert.Equal("First",r.nameLabel.Text);
        r.descLabel.Tmp.MeasuredTextHeight=60;lower.NativeLayoutRouteStep(1);lower.NativeScrollStep(1,-.2f);
        Assert.True(r.descLabel.ScrollOffset>0);
        var next=r.slots[1].Root;HK.NativeLabels["UI/INV_NAME_"+next.name]="Next selection";
        lower.NativeDetailStep(0,next);Assert.Equal("Next selection",r.nameLabel.Text);Close(0,r.descLabel.ScrollOffset);
        lower.NativeDetailStep(0,item);
        HK.NativeLabels["UI/INV_NAME_"+item.name]="Localized second";HK.NativeLabels["UI/INV_DESC_"+item.name]="Localized second prose";
        TeamCherry.Localization.Language.Code++;Time.frameCount=30;lower.NativeLayoutRouteStep(1);
        Assert.Equal("Localized second",r.nameLabel.Text);Assert.True(lower.ControlRefreshes>=2);
        var after=Occupied(r.subject);Close(before.center.x,after.center.x);Close(before.size.x,after.size.x);
    }
    [Fact]
    public void ActualTouchDispatcherAdmitsOnlyNativeChooserAndProseDragOwners()
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);var r=lower.refsInv;
        lower.NativeSource(pane,"Text Desc").text="Measured long prose";r.descLabel.Tmp.MeasuredTextHeight=60;lower.NativeLayoutRouteStep(1);
        void Drag(float x,float y,int contacts=1)
        {
            lower.transport.TapSequence++;lower.transport.TouchX=x/1240;lower.transport.TouchY=y/1080;
            lower.transport.T0Y=y/1080;lower.transport.contacts=contacts;lower.TouchStep();
            lower.transport.T0Y-=.2f;lower.TouchStep();lower.transport.contacts=0;lower.TouchStep();
        }
        Drag(230,400);Assert.Equal(0,r.chooserOffset);Close(0,r.descLabel.ScrollOffset);
        Drag(455,400);Assert.Equal(0,r.chooserOffset);Close(0,r.descLabel.ScrollOffset);
        Drag(670,100);Assert.Equal(0,r.chooserOffset); // HUD owns this region.
        Drag(670,1000);Assert.Equal(0,r.chooserOffset); // Strip owns this region.
        Drag(670,400,2);Assert.Equal(0,r.chooserOffset);
        lower.slideT=.5f;Drag(670,400);Assert.Equal(0,r.chooserOffset);lower.slideT=1;
        Drag(670,400);Close(216,(float)r.chooserOffset);Close(0,r.descLabel.ScrollOffset);
        Drag(1050,400);Close(216,(float)r.chooserOffset);Assert.True(r.descLabel.ScrollOffset>0);
        lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);var before=Occupied(lower.nativeCharmGrid);
        Drag(690,400);var after=Occupied(lower.nativeCharmGrid);Close(before.center.y,after.center.y);Close(0,lower.CharmDescription.ScrollOffset);
    }
    [Fact]
    public void ActualSourceRetirementReleasesNativeCachesAndRebuildsNewOwnedGeometry()
    {
        HK lower=new();var inv=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);var oldRefs=lower.refsInv;
        var charm=lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);var oldGrid=lower.nativeCharmGrid;var oldName=lower.CharmName;
        lower.NativeDetailStep(1,oldGrid.Children[0],1);
        lower.LoseDisplay();Assert.Same(oldGrid,lower.nativeCharmGrid);Assert.Same(charm,lower.charmCloneCache);
        lower.DisplayStep(true);
        lower.Manager.inventoryFSM=new NativeOwner();lower.NativeLayoutRouteStep(2);
        Assert.Contains(inv,lower.Destroyed);Assert.Contains(charm,lower.Destroyed);
        Assert.Null(lower.refsInv);Assert.Null(lower.nativeCharmGrid);Assert.Null(lower.CharmName);Assert.Null(lower.CharmDescription);
        Assert.Null(lower.CostPips);Assert.False(inv.activeSelf);Assert.False(charm.activeSelf);
        var rebuiltInv=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);
        Assert.NotSame(oldRefs,lower.refsInv);Assert.Same(rebuiltInv,lower.refsInv.pane);Assert.True(lower.refsInv.canonicalReady);
        Assert.NotNull(lower.refsInv.graphics);Graphics(lower,lower.refsInv.graphics,HKLowerLayout.NativeColumns(lower.LowerGeometry(),false));
        var rebuiltCharm=lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        Assert.True(lower.fit.valid);Assert.NotNull(lower.CharmGraphics);Assert.NotEmpty(lower.CharmName.ClipRenderers);
        Assert.NotSame(oldGrid,lower.nativeCharmGrid);Assert.NotSame(oldName,lower.CharmName);
        Assert.Same(rebuiltCharm.transform,lower.nativeCharmGrid.parent);Assert.False(lower.nativeCharmPortrait.enabled);
        lower.NativeRetireStep();Assert.Contains(rebuiltInv,lower.Destroyed);Assert.Contains(rebuiltCharm,lower.Destroyed);
        Assert.Null(lower.refsInv);Assert.Null(lower.nativeCharmGrid);Assert.Null(lower.paneClone);
    }
    // Readiness regressions: removing actual-outcome admission must expose the
    // native-unit incoming body in the canonical pixel camera, even with valid chrome.
    static void InvalidGlyph(Component tmp)
    {
        tmp.AutomaticGlyphInput=false;tmp.text="M";
        Vector3[] q={new(0,0,0),new(0,1,0),new(1,1,0),new(1,0,0)};
        tmp.textInfo=new() { textComponent=tmp,characterCount=1,materialCount=1,
            characterInfo=new[]{new TMProOld.TMP_CharacterInfo { isVisible=true,materialReferenceIndex=0,vertexIndex=0,
                vertex_BL=new(){position=q[0]},vertex_TL=new(){position=q[1]},vertex_TR=new(){position=q[2]},vertex_BR=new(){position=q[3]}}},
            meshInfo=new[]{new TMProOld.TMP_MeshInfo { mesh=new(),vertices=q,vertexCount=3 }} };
    }
    static void Withheld(HK lower,GameObject pane)
    {
        Assert.True(pane.activeSelf);Close(lower.BOTTOM_H/2f,lower.attrCam.orthographicSize);
        Assert.Equal(1<<HK.ATTR_LAYER,lower.attrCam.cullingMask);
        // Geometric camera exclusion, not Drawn's mask-only approximation.
        foreach(var r in pane.GetComponentsInChildren<Renderer>(true))
        {
            var b=r.bounds;var lo=lower.attrCam.WorldToViewportPoint(b.min);var hi=lower.attrCam.WorldToViewportPoint(b.max);
            Assert.True(hi.y<0 || lo.y>1 || hi.x<0 || lo.x>1,"Uncalibrated incoming renderer intersects pixel camera: "+r.gameObject.name);
        }
        Assert.True(lower.NativeTab(0).enabled);Assert.True(lower.NativeTab(4).enabled);
        Assert.InRange(lower.attrCam.WorldToViewportPoint(lower.NativeTab(0).bounds.center).y,0,1);
    }
    [Theory]
    [InlineData(false,"Text Name")] [InlineData(false,"Text Desc")]
    [InlineData(true,"Text Name")] [InlineData(true,"Text Desc")]
    public void Readiness_ActualFailedGlyphAdmissionWithholdsIncomingAndRecoversSameOwnerOnlyAtDeadline(bool charms,string label)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(charms);var donor=lower.NativeSource(pane,label);InvalidGlyph(donor);
        Assert.Null(lower.CopyLabelStep(donor.transform,32));
        lower.Step(false);Withheld(lower,pane);Assert.False(lower.fit.valid);Assert.False(lower.Ready(charms ? 2 : 1));
        int attempts=lower.NativeInventoryLayouts+lower.NativeCharmLayouts;
        donor.AutomaticGlyphInput=true;
        while(Time.frameCount<119) lower.Step(false);
        Assert.Equal(attempts,lower.NativeInventoryLayouts+lower.NativeCharmLayouts);Withheld(lower,pane);
        lower.Step(false);Assert.Same(pane,lower.paneClone);Assert.True(lower.fit.valid);Assert.True(lower.Ready(charms ? 2 : 1));
        Close(0,pane.transform.localPosition.y);
        var p=HKLowerLayout.NativeColumns(lower.LowerGeometry(),charms);
        var b=Occupied(charms ? lower.nativeCharmGrid : lower.refsInv.subject);
        Close((charms ? p.ChooserX+p.ChooserWidth/2 : p.SubjectX+p.SubjectWidth/2)-620,b.center.x);
        Assert.True(b.size.y<=p.Height+.02f);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_RealActiveSettleCompletesAndReleasesExactCacheOwner(bool charms)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(charms,false);
        var fsm=new HkPauseContracts.PlayMakerFSM{FsmName=charms ? "UI Charms" : "Build Equipment List",ActiveStateName="Init"};pane.transform.FsMs.Add(fsm);
        int frames=charms ? 20 : 45;lower.Step(false);Withheld(lower,pane);Assert.False(lower.Ready(charms ? 2 : 1));
        if(!charms) { Assert.Contains("INVENTORY OPENED",fsm.Events);Assert.Contains("FINISHED",fsm.Events); }
        for(int i=1;i<frames;i++) lower.Step(false);
        Assert.False(fsm.enabled);Assert.True((charms ? lower.charmRun : lower.invRun).finalized);
        Assert.Same(pane,lower.paneClone);Assert.True(lower.Ready(charms ? 2 : 1));Close(0,pane.transform.localPosition.y);
    }
    [Fact]
    public void Readiness_MissingSubjectRetriesUnchangedOwnerWithoutDirtyOrTabChange()
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(false,slots:0);
        var subject=pane.transform.Find("Inv_Items");subject.SetParent(null);
        lower.Step(false);Withheld(lower,pane);Assert.False(lower.fit.valid);
        int attempts=lower.NativeInventoryLayouts;subject.SetParent(pane.transform);
        while(Time.frameCount<119) lower.Step(false);
        Assert.Equal(attempts,lower.NativeInventoryLayouts);lower.Step(false);
        Assert.Same(pane,lower.paneClone);Assert.True(lower.fit.valid);Assert.True(lower.Ready(1));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_PreviouslyCalibratedOwnerRevokesActualFailedOccupiedFit(bool charms)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(charms);lower.Step(false);Assert.True(lower.Ready(charms ? 2 : 1));
        var root=charms ? lower.nativeCharmGrid : lower.refsInv.subject;
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))
        { if(r is SpriteRenderer sr) sr.sprite=new Sprite{bounds=new(Vector3.zero,Vector3.zero)};else r.BoundsSize=Vector3.zero; }
        lower.SetSelectedContentDirty();lower.Step(false);
        Assert.False(lower.fit.valid);Assert.False(lower.Ready(charms ? 2 : 1));Withheld(lower,pane);
    }
    [Fact]
    public void Readiness_HealthyOutgoingAndInvalidIncomingUseRealTabCaptureCompensationAndRestore()
    {
        Time.frameCount=1;HK lower=new();lower.cfg.compTabSlide=1;var outgoing=lower.ColdNativeOwner(false);lower.Step(false);
        var renderer=lower.refsInv.subjectRenderers[0];var before=renderer.bounds;
        var corners=new[]{before.min,before.max}.Select(lower.attrCam.WorldToViewportPoint).ToArray();
        var normal=outgoing.transform.localPosition;var scale=outgoing.transform.localScale;
        var incoming=lower.NewNativePane(true,populate:false);InvalidGlyph(lower.NativeSource(incoming,"Text Name"));
        lower.cfg.compTab=2;lower.Step(false);
        Assert.Same(outgoing,lower.slideOutClone);Assert.True(outgoing.activeSelf);Withheld(lower,incoming);
        float e=1-MathF.Pow(1-lower.slideT,3);
        var after=renderer.bounds;var actual=new[]{after.min,after.max}.Select(lower.attrCam.WorldToViewportPoint).ToArray();
        for(int i=0;i<2;i++) { Close(corners[i].x-lower.slideDir*1.075f*e,actual[i].x);Close(corners[i].y,actual[i].y); }
        while(lower.slideT<1) lower.Step(false);
        Assert.Null(lower.slideOutClone);Assert.False(outgoing.activeSelf);Close(normal.x,outgoing.transform.localPosition.x);Close(normal.y,outgoing.transform.localPosition.y);Close(scale.x,outgoing.transform.localScale.x);
        lower.cfg.compTab=1;lower.Step(false);Assert.Null(lower.slideOutClone);Assert.True(lower.Ready(1));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_UnreadyBodyRejectsDebugTapCleanTapDragAndActionButRetainsTabNavigation(bool charms)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(charms);InvalidGlyph(lower.NativeSource(pane,"Text Name"));
        lower.Step(false);lower.cfg.debug=1;lower.cfg.compSimTapX=.5f;lower.cfg.compSimTapY=.5f;lower.cfg.compSimTapN++;
        int taps=lower.ItemTaps,details=lower.DetailRefreshes;lower.Step(false);
        lower.transport.contacts=1;lower.transport.TapSequence++;lower.transport.TouchX=.5f;lower.transport.TouchY=.5f;lower.transport.T0Y=.5f;lower.Step(false);
        lower.transport.T0Y=.3f;lower.Step(false);
        lower.transport.contacts=0;lower.transport.CleanTapSequence++;lower.transport.CleanTapX=.5f;lower.transport.CleanTapY=.5f;lower.Step(false);
        Assert.Equal(taps,lower.ItemTaps);Assert.Equal(details,lower.DetailRefreshes);Assert.Null(lower.SelectedItem);Assert.Empty(HkPlayerData.instance.equippedCharms);
        Withheld(lower,pane);
        lower.transport.contacts=1;lower.transport.TapSequence++;lower.transport.TouchX=.1f;lower.transport.TouchY=.95f;lower.Step(false);
        lower.transport.contacts=0;lower.transport.CleanTapSequence++;lower.transport.CleanTapX=.1f;lower.transport.CleanTapY=.95f;lower.Step(false);
        Assert.Equal(1,lower.tab.tap); // Canonical first tab is Inventory; strip admission survives withholding.
    }
    [Fact]
    public void Readiness_ExternalPromptAndSelectionStayHiddenWithoutDestructiveRetirementThenRecover()
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(false);lower.Step(false);
        var item=lower.refsInv.subject;lower.NativeSelectionStep(item);lower.PromptIngress();
        var subject=lower.refsInv.subjectRenderers[0];subject.BoundsSize=Vector3.zero;
        lower.SetSelectedContentDirty();lower.Step(false);
        Assert.False(PromptVisible(lower.PromptGlyph));Assert.False(PromptVisible(lower.PromptVerb));Assert.True(lower.PromptRetained);Withheld(lower,pane);
        subject.BoundsSize=new(20,10,1);while(Time.frameCount<119) lower.Step(false);
        Assert.True(lower.PromptRetained);lower.Step(false);Assert.True(lower.Ready(1));Assert.True(lower.PromptVerb.enabled);Assert.True(lower.PromptGlyph.enabled);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_FailedOwnerInterDeadlineDoesNoDiscoveryGenerationAllocationOrLayout(bool charms)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(charms);InvalidGlyph(lower.NativeSource(pane,"Text Name"));
        lower.Step(false);for(int i=0;i<5;i++) lower.Step(false);
        int hierarchy=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries,meshes=DiscoveryCounters.MeshGenerations,attempts=lower.NativeInventoryLayouts+lower.NativeCharmLayouts;
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<50;i++) lower.Step(false);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);Assert.Equal(meshes,DiscoveryCounters.MeshGenerations);
        Assert.Equal(attempts,lower.NativeInventoryLayouts+lower.NativeCharmLayouts);Withheld(lower,pane);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_ActualMissingShellGraphicsCanRecoverWithoutPaneReplacement(bool charms)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(charms);
        lower.NativeShellAssetsAvailable=false;var sprite=lower.shellRule.sprite;lower.shellRule.sprite=null;
        lower.Step(false);Withheld(lower,pane);Assert.False(lower.Ready(charms ? 2 : 1));
        lower.shellRule.sprite=sprite;while(Time.frameCount<119) lower.Step(false);Withheld(lower,pane);
        lower.Step(false);Assert.Same(pane,lower.paneClone);Assert.True(lower.Ready(charms ? 2 : 1));
    }
    [Fact]
    public void Readiness_SwitchMidSlideNeverPromotesFailedNativeToOutgoing()
    {
        Time.frameCount=1;HK lower=new();lower.cfg.compTabSlide=1;
        var inv=lower.ColdNativeOwner(false);lower.Step(false);var pos=inv.transform.localPosition;
        var charm=lower.NewNativePane(true,populate:false);InvalidGlyph(lower.NativeSource(charm,"Text Name"));
        lower.cfg.compTab=2;lower.Step(false);Assert.Same(inv,lower.slideOutClone);Withheld(lower,charm);
        lower.cfg.compTab=3;lower.Step(false);
        Assert.Null(lower.slideOutClone);Assert.False(charm.activeSelf);Assert.False(inv.activeSelf);
        Close(pos.x,inv.transform.localPosition.x);Close(pos.y,inv.transform.localPosition.y);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_ReadyOwnerAndUnfinishedOwnerRetainExactStateThroughPauseAndRetireOnSourceBoundary(bool finalized)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(false,finalized);lower.Step(false);
        var run=lower.invRun;int settle=run.settle,kicks=run.kicks,attempts=lower.NativeInventoryLayouts;
        bool ready=lower.Ready(1);var pos=pane.transform.localPosition;
        for(int i=0;i<150;i++) lower.Step(true);
        Assert.Same(pane,lower.invCloneCache);Assert.Equal(ready,lower.Ready(1));Assert.Equal(settle,run.settle);Assert.Equal(kicks,run.kicks);
        Assert.Equal(attempts,lower.NativeInventoryLayouts);Close(pos.y,pane.transform.localPosition.y);
        lower.Manager.inventoryFSM=new NativeOwner();lower.Step(true);
        Assert.Null(lower.invCloneCache);Assert.False(lower.Ready(1));Assert.Contains(pane,lower.Destroyed);
        var replacement=lower.ColdNativeOwner(false);InvalidGlyph(lower.NativeSource(replacement,"Text Name"));lower.Step(false);
        Assert.NotSame(pane,replacement);Assert.False(lower.Ready(1));Withheld(lower,replacement);
    }
    [Fact]
    public void Readiness_SelectedCharmActionRevokesAfterRealChooserFitFailure()
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(true);lower.Step(false);
        var item=lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard1");lower.NativeDetailStep(1,item,1);lower.Step(false);lower.Step(false);
        foreach(var r in lower.nativeCharmGrid.GetComponentsInChildren<Renderer>(true))
        { if(r is SpriteRenderer sr) sr.sprite=new Sprite{bounds=new(Vector3.zero,Vector3.zero)};else r.BoundsSize=Vector3.zero; }
        lower.SetSelectedContentDirty();lower.Step(false);
        int equips=lower.Manager.Equips,details=lower.DetailRefreshes;
        var p=HKLowerLayout.NativeColumns(lower.LowerGeometry(),true);float x=(p.DetailX+p.DetailWidth/2)/1240,y=(p.Top+p.Height-39)/1080;
        lower.transport.contacts=1;lower.transport.TapSequence++;lower.transport.TouchX=lower.transport.T0X=x;lower.transport.TouchY=lower.transport.T0Y=y;lower.Step(false);
        lower.transport.contacts=0;lower.transport.CleanTapSequence++;lower.transport.CleanTapX=x;lower.transport.CleanTapY=y;lower.Step(false);
        Assert.Equal(equips,lower.Manager.Equips);Assert.Equal(details,lower.DetailRefreshes);Assert.False(lower.Ready(2));Withheld(lower,pane);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_HealthyFinalPositioningRemainsZeroCostAcrossMultipleRetryDeadlines(bool charms)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(charms);lower.Step(false);
        for(int i=0;i<150;i++) lower.Step(false);
        int hierarchy=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries,meshes=DiscoveryCounters.MeshGenerations,attempts=lower.NativeInventoryLayouts+lower.NativeCharmLayouts;
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<360;i++) lower.Step(false);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);Assert.Equal(meshes,DiscoveryCounters.MeshGenerations);
        Assert.Equal(attempts,lower.NativeInventoryLayouts+lower.NativeCharmLayouts);Assert.Same(pane,lower.paneClone);Assert.True(lower.Ready(charms ? 2 : 1));
    }
    [Fact]
    public void Readiness_OptionalEmptyEquipmentAndEmptyProseRemainValid()
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(false,slots:0);lower.Step(false);
        Assert.True(lower.Ready(1));Assert.True(lower.fit.valid);Assert.Empty(lower.refsInv.slots);Assert.False(lower.refsInv.nameLabel.Renderer.enabled);Assert.False(lower.refsInv.descLabel.Renderer.enabled);
    }
    static bool PromptVisible(Renderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy;
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_ReviewFixOwnedPromptSubtreePreservesRendererStatesAndPendingLayout(bool pending)
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(false);lower.Step(false);
        lower.NativeSelectionStep(lower.refsInv.subject);lower.PromptIngress(pending);
        var fallback=lower.GeneratePromptSubmesh();var disabled=lower.GeneratePromptSubmesh(false);
        var selected=lower.SelectedItem;int pendingBefore=lower.PromptPending;
        var subject=lower.refsInv.subjectRenderers[0];subject.BoundsSize=Vector3.zero;
        lower.SetSelectedContentDirty();lower.Step(false);
        Assert.All(lower.PromptRenderers,r=>Assert.False(PromptVisible(r)));
        Assert.True(lower.PromptVerb.enabled);Assert.True(fallback.enabled);Assert.False(disabled.enabled);
        Assert.True(lower.PromptSelected);Assert.Equal(pendingBefore,lower.PromptPending);Assert.Same(selected,lower.SelectedItem);
        var late=lower.GeneratePromptSubmesh();Assert.False(PromptVisible(late));
        int hierarchy=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries,meshes=DiscoveryCounters.MeshGenerations;
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<50;i++) lower.Step(false);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);
        Assert.Equal(resources,Resources.Discoveries);Assert.Equal(meshes,DiscoveryCounters.MeshGenerations);
        Assert.All(lower.PromptRenderers,r=>Assert.False(PromptVisible(r)));Withheld(lower,pane);
        subject.BoundsSize=new(20,10,1);while(Time.frameCount<119) lower.Step(false);
        lower.Step(false);Assert.Same(pane,lower.paneClone);Assert.True(lower.Ready(1));Assert.Same(selected,lower.SelectedItem);
        Assert.True(PromptVisible(lower.PromptVerb));Assert.True(PromptVisible(fallback));Assert.True(PromptVisible(late));
        Assert.False(PromptVisible(disabled));Assert.False(disabled.enabled);Assert.True(lower.PromptRetained);Assert.Equal(0,lower.PromptPending);
        lower.NativeRetireStep();Assert.All(lower.PromptRenderers,r=>Assert.False(PromptVisible(r)));
    }
    [Fact]
    public void Readiness_ReviewFixPostPollTouchDragRevokesAlreadyPositionedOwnerAndRecoversAtDeadline()
    {
        Time.frameCount=1;HK lower=new();var pane=lower.ColdNativeOwner(false);lower.Step(false);
        lower.NativeSelectionStep(lower.refsInv.subject);lower.PromptIngress();var fallback=lower.GeneratePromptSubmesh();
        var selected=lower.SelectedItem;Assert.True(lower.Ready(1));Assert.True(PromptVisible(fallback));
        lower.transport.contacts=1;lower.transport.TapSequence++;lower.transport.TouchX=670f/1240;lower.transport.TouchY=.5f;lower.transport.T0Y=.5f;
        lower.Step(false);Assert.True(lower.Ready(1));Close(0,pane.transform.localPosition.y);
        var subject=lower.refsInv.subjectRenderers[0];subject.BoundsSize=Vector3.zero;
        int attempts=lower.NativeInventoryLayouts;Assert.False(lower.PendingCharmFit);
        // No dirty/config/identity event. UpdateCompanion sees its previous ready
        // outcome; the actual chooser drag fails layout later, inside PollTouch.
        lower.transport.T0Y=.4f;lower.Step(false);
        Assert.Equal(attempts+1,lower.NativeInventoryLayouts);Assert.False(lower.Ready(1));Assert.False(lower.fit.valid);
        Withheld(lower,pane);Assert.All(lower.PromptRenderers,r=>Assert.False(PromptVisible(r)));Assert.Same(selected,lower.SelectedItem);
        lower.transport.contacts=0;subject.BoundsSize=new(20,10,1);
        attempts=lower.NativeInventoryLayouts;while(Time.frameCount<119) lower.Step(false);
        Assert.Equal(attempts,lower.NativeInventoryLayouts);Withheld(lower,pane);
        lower.Step(false);Assert.Equal(attempts+1,lower.NativeInventoryLayouts);Assert.Same(pane,lower.paneClone);
        Assert.True(lower.Ready(1));Close(0,pane.transform.localPosition.y);Assert.True(PromptVisible(fallback));Assert.True(lower.PromptRetained);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Readiness_OutgoingLayoutRevocationCannotReuseCapturedAdmissionAndRecoversSameOwner(bool afterFirstSlideFrame)
    {
        Time.frameCount=1;HK lower=new();lower.cfg.compTabSlide=1;
        var outgoing=lower.ColdNativeOwner(false);lower.Step(false);Assert.True(lower.Ready(1));
        var normal=outgoing.transform.localPosition;var scale=outgoing.transform.localScale;
        var subject=lower.refsInv.subjectRenderers[0];var shape=subject.BoundsSize;
        var incoming=lower.NewNativePane(true,populate:false);
        Assert.False(lower.PendingCharmFit);
        if(!afterFirstSlideFrame) subject.BoundsSize=Vector3.zero;
        lower.cfg.compTab=2;lower.Step(false);
        if(afterFirstSlideFrame)
        {
            Assert.Same(outgoing,lower.slideOutClone);Assert.True(outgoing.activeSelf);
            // Revoke through the real cache-local detail/layout callee after a
            // healthy capture; no readiness field or final result is injected.
            subject.BoundsSize=Vector3.zero;lower.ClearCachedInventoryDetailStep();
            Assert.False(lower.Ready(1));lower.Step(false);
        }
        // The real transition's ClearInvDetail can revoke the formerly ready
        // owner AFTER capture. Its old permission must never reactivate it.
        Assert.False(lower.Ready(1));Assert.Null(lower.slideOutClone);Assert.False(outgoing.activeSelf);
        Close(normal.x,outgoing.transform.localPosition.x);Close(normal.y,outgoing.transform.localPosition.y);
        Close(scale.x,outgoing.transform.localScale.x);Close(scale.y,outgoing.transform.localScale.y);
        Assert.Same(incoming,lower.paneClone);Assert.True(lower.Ready(2));Assert.True(incoming.activeSelf);
        Close(0,incoming.transform.localPosition.x);Close(0,incoming.transform.localPosition.y);
        Close(lower.BOTTOM_H/2f,lower.attrCam.orthographicSize);Assert.True(lower.NativeTab(0).enabled);Assert.True(lower.NativeTab(4).enabled);
        // Revisit the same failed cache, then restore the actual ingress without
        // dirty/config/cache replacement. Existing cadence must release it.
        lower.cfg.compTab=1;lower.Step(false);Assert.Same(outgoing,lower.paneClone);Withheld(lower,outgoing);
        subject.BoundsSize=shape;int attempts=lower.NativeInventoryLayouts;
        while(Time.frameCount<119) lower.Step(false);
        Assert.Equal(attempts,lower.NativeInventoryLayouts);Withheld(lower,outgoing);
        lower.Step(false);Assert.Equal(attempts+1,lower.NativeInventoryLayouts);Assert.True(lower.Ready(1));Assert.Same(outgoing,lower.paneClone);
        Close(0,outgoing.transform.localPosition.y);
    }
    [Fact]
    public void WarmNativeDispatcherDoesNotAllocateOrRelayout()
    {
        foreach(var charms in new[]{false,true})
        {
            HK lower=new();lower.NewNativePane(charms);int id=charms ? 2 : 1;lower.NativeLayoutRouteStep(id);
            lower.tab.tap=id;for(int i=0;i<150;i++) lower.Step(false);
            int layouts=lower.NativeInventoryLayouts+lower.NativeCharmLayouts;
            long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<300;i++) lower.Step(false);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-start;
            Assert.Equal(0,bytes);
            Assert.Equal(layouts,lower.NativeInventoryLayouts+lower.NativeCharmLayouts);
        }
    }
}
