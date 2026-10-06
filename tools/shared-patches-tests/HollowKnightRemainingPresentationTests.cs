using Xunit;
using HkPauseContracts;
using HK=HkPauseContracts.HKDualScreen;
using GameObject=HkPauseContracts.GameObject;
using Time=HkPauseContracts.Time;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightRemainingPresentationTests
{
    // Exact serialized native-input-provenance/provenance.json b6e63d62a231d748...
    // Identity rotations; resolved BR flips represent the negative X/Y ancestor.
    // Local mesh bounds, not rect/atlas/pivot recomputed geometry or texture pixels.
    static HK Pane(int id,bool reflected=true)
    {
        HK f=new();Time.frameCount=1000;Time.unscaledTime=0;Time.unscaledDeltaTime=.05f;
        f.tab.cur=id;f.tab.tap=id;
        var corner=new Sprite { name="Inv_0014_selection_cursor",bounds=new(Vector3.zero,new(.550000011920929f,.550000011920929f,0)) };
        f.tabTL.sprite=f.tabBR.sprite=corner;
        f.tabBR.flipX=f.tabBR.flipY=reflected;
        f.tabGlow.sprite=new Sprite { name="light_effect_v02",bounds=new(new(-.07244312763214111f,.04829549789428711f,0),new(5.843749761581421f,5.167613506317139f,0)) };
        f.tabGlow.color=new(.6029411554336548f,.7535496950149536f,1,.5372549295425415f);
        Assert.True(f.GraphicsBuild(id));return f;
    }
    static HK.PaneGraphics Graphics(HK f,int id) => id==3 ? f.journalGraphics : f.guideGraphics;
    static void ColorEqual(Color a,Color b) { Assert.Equal(a.r,b.r);Assert.Equal(a.g,b.g);Assert.Equal(a.b,b.b);Assert.Equal(a.a,b.a); }
    static void CursorCentered(HK f,int id)
    {
        var v=Graphics(f,id);var target=new Vector2(v.Target.x+v.Target.width/2,v.Target.y+v.Target.height/2);
        Assert.Equal(target.x,v.Center.x,3);Assert.Equal(target.y,v.Center.y,3);
        Assert.Equal(target.x-f.BOTTOM_W/2f,v.Glow.bounds.center.x,3);
        Assert.Equal(f.BOTTOM_H/2f-target.y,v.Glow.bounds.center.y,3);
    }
    [Theory] [InlineData(3)] [InlineData(4)]
    public void GenuineReflectedNativeCornerSurvivesCompleteBuilder(int id)
    {
        var f=Pane(id);var v=Graphics(f,id);
        Assert.Same(f.tabBR.sprite,v.BR.sprite);Assert.Same(f.tabTL.sprite,v.TL.sprite);
        Assert.Equal(f.tabBR.transform.localRotation.Angle,v.BR.transform.localRotation.Angle);
        Assert.True(v.BR.flipX);Assert.True(v.BR.flipY);
    }
    [Theory] [InlineData(3)] [InlineData(4)]
    public void GenuineTranslucentBlueGlowSurvivesCompleteBuilder(int id)
    {
        var f=Pane(id);var v=Graphics(f,id);Assert.Same(f.tabGlow.sprite,v.Glow.sprite);ColorEqual(f.tabGlow.color,v.Glow.color);
    }
    [Theory] [InlineData(3)] [InlineData(4)]
    public void UnreflectedHiddenFirstShowAndTrimmedFitControls(int id)
    {
        var f=Pane(id,false);var v=Graphics(f,id);Assert.False(v.BR.flipX);Assert.False(v.BR.flipY);
        Assert.False(v.TL.enabled);Assert.False(v.BR.enabled);Assert.False(v.Glow.enabled);
        f.SelectionStep(id,new(50,300,100,120),true,1);Assert.Equal(1,v.Travel);CursorCentered(f,id);
        Assert.Equal(64,v.TL.bounds.size.x,3);Assert.Equal(64,v.TL.bounds.size.y,3);
        Assert.Equal(64,v.BR.bounds.size.x,3);Assert.Equal(64,v.BR.bounds.size.y,3);
        Assert.Equal(124,v.Glow.bounds.size.x,3);Assert.Equal(144,v.Glow.bounds.size.y,3);
        Assert.Equal((v.Target.width+24)/(v.Target.height+24),v.Glow.bounds.size.x/v.Glow.bounds.size.y,3);
        f.SelectionStep(id,default,false,-1);Assert.False(v.Glow.enabled);
        f.SelectionStep(id,new(300,350,140,100),true,2);Assert.Equal(1,v.Travel);CursorCentered(f,id);
    }
    static HK Content(int id)
    {
        var f=Pane(id);
        if(id==3)
        {
            f.journalHadBook=true;
            for(int i=0;i<36;i++) { f.journalRecords.Add(new HK.JournalRecord { Killed=true,Remaining=i });f.journalVisible.Add(i); }
            f.journalSelected=6;
        }
        else
        {
            for(int i=0;i<11;i++) { f.guideRecords.Add(new HK.GuideRecord { Visible=true,Text="Native pin "+i });f.guideVisible.Add(i); }
            f.guideSelected=2;
        }
        f.LayoutStep(id);return f;
    }
    [Theory] [InlineData(3)] [InlineData(4)]
    public void CompleteScrollRetargetsSettledSameIdentityWithoutTravelRestart(int id)
    {
        var f=Content(id);var v=Graphics(f,id);int selected=v.SelectedId;float oldY=v.Target.y;
        // 1/64 of the canvas is exactly 16.875px here, not a whole-canvas drag.
        // Both selected native records stay visible during this retarget.
        f.ScrollStep(-1f/64);Assert.Equal(selected,v.SelectedId);Assert.Equal(oldY-16.875f,v.Target.y);
        Assert.Equal(1,v.Travel);CursorCentered(f,id);
        Time.frameCount++;f.CursorStep(id);CursorCentered(f,id);
        f.ScrollStep(1f/64);Assert.Equal(selected,v.SelectedId);Assert.Equal(oldY,v.Target.y);
        Assert.Equal(1,v.Travel);CursorCentered(f,id);
    }
    [Theory] [InlineData(3)] [InlineData(4)]
    public void CompleteOffclipSuppressionPreservesNativeSelectionAndRestoresCursor(int id)
    {
        var f=Content(id);var v=Graphics(f,id);
        if(id==4) { f.guideSelected=0;f.LayoutStep(id); }
        int selected=id==3 ? f.journalSelected : f.guideSelected;
        var old=v.Target;
        f.ScrollStep(-1);
        Assert.Equal(selected,id==3 ? f.journalSelected : f.guideSelected);
        Assert.False(v.Selected);Assert.Equal(-1,v.SelectedId);
        Assert.False(v.TL.enabled);Assert.False(v.BR.enabled);Assert.False(v.Glow.enabled);
        f.ScrollStep(1);
        Assert.Equal(selected,id==3 ? f.journalSelected : f.guideSelected);
        Assert.True(v.Selected);Assert.Equal(selected,v.SelectedId);
        Assert.True(v.TL.enabled);Assert.True(v.BR.enabled);Assert.True(v.Glow.enabled);
        Assert.Equal(old.y,v.Target.y);Assert.Equal(1,v.Travel);CursorCentered(f,id);
    }
    [Theory] [InlineData(3)] [InlineData(4)]
    public void DifferentIdentityStartsOneCanonicalTransitionAndSameFrameDoesNotDoubleStep(int id)
    {
        var f=Content(id);var v=Graphics(f,id);var from=v.Center;
        if(id==3) f.journalSelected=7;else f.guideSelected=3;
        f.LayoutStep(id);Assert.Equal(0,v.Travel);Assert.Equal(from.x,v.Center.x);Assert.Equal(from.y,v.Center.y);
        Time.frameCount++;f.CursorStep(id);Assert.Equal(1f/3,v.Travel,5);
        float travel=v.Travel;f.LayoutStep(id);f.CursorStep(id);Assert.Equal(travel,v.Travel);
        Time.frameCount++;f.CursorStep(id);Assert.Equal(2f/3,v.Travel,5);
        Time.unscaledDeltaTime=.06f;
        Time.frameCount++;f.CursorStep(id);
        Time.unscaledDeltaTime=.05f;
        Assert.Equal(1,v.Travel);CursorCentered(f,id);
    }
    [Fact]
    public void KnownJournalContentRemainsNativeAcrossSelectionChanges()
    {
        var f=Content(3);
        Assert.All(f.JournalCells.Where(s=>s.enabled),s=>ColorEqual(Color.white,s.color));
        f.journalSelected=7;f.LayoutStep(3);
        Assert.All(f.JournalCells.Where(s=>s.enabled),s=>ColorEqual(Color.white,s.color));
        Assert.Same(f.journalRecords[7].Sprite,f.journalPortrait.sprite);Assert.True(f.journalGraphics.Selected);
        Assert.Equal(7,f.journalGraphics.SelectedId);Assert.Equal(36,f.journalVisible.Count);
        Assert.False(f.journalFilled);Assert.Contains("7",f.journalNotes.Text);
    }
    static CharmIconList Icons(params int[] ids)
    {
        var d=new CharmIconList { spriteList=new Sprite[41] };
        foreach(int n in ids) d.spriteList[n]=new Sprite { name="NativeCharm"+n,bounds=new(Vector3.zero,new(2,3,0)) };
        return d;
    }
    static HK Row(params int[] ids)
    {
        HK f=new();Time.frameCount=1000;Time.unscaledTime=0;f.cfg.compEquipRow=1;
        f.SetEquipState(ids);f.EquipBuildStep();return f;
    }
    static void Advance(HK f,int frames=120) { Time.frameCount+=frames;f.EquipUpdateStep(); }
    static void Shown(HK f,int n) { Assert.Equal(n,f.equipRowN);Assert.Equal(n,f.equipCharmSRs.Count(s=>s.enabled)); }
    [Fact]
    public void AbsentDonorRecoversUnchangedEquippedStateThroughBoundedRetry()
    {
        CharmIconList.Instance=null;var f=Row(1,5);f.EquipUpdateStep();Assert.Equal(2,f.equipRowN);Assert.All(f.equipCharmSRs,s=>Assert.False(s.enabled));
        int reads=CharmIconList.InstanceReads;for(int i=0;i<119;i++) Advance(f,1);Assert.Equal(reads,CharmIconList.InstanceReads);
    }
    [Fact]
    public void LateDonorRecoveryDoesNotNeedAnEquipHashChange()
    {
        CharmIconList.Instance=null;var f=Row(1,5);f.EquipUpdateStep();Assert.Equal(2,f.equipRowN);Assert.All(f.equipCharmSRs,s=>Assert.False(s.enabled));
        var donor=Icons(1,5);CharmIconList.Instance=donor;Advance(f);Shown(f,2);
        Assert.Same(donor.spriteList[1],f.equipCharmSRs[0].sprite);Assert.Same(donor.spriteList[5],f.equipCharmSRs[1].sprite);
    }
    [Fact]
    public void PartialNullSpritesRecoverWithoutHealthyPeriodicDiscovery()
    {
        var donor=Icons(1);CharmIconList.Instance=donor;var f=Row(1,5);f.EquipUpdateStep();Assert.True(f.equipCharmSRs[0].enabled);Assert.False(f.equipCharmSRs[1].enabled);
        int reads=CharmIconList.InstanceReads;for(int i=0;i<119;i++) Advance(f,1);Assert.Equal(reads,CharmIconList.InstanceReads);
        donor.spriteList[5]=Icons(5).spriteList[5];Advance(f,1);Shown(f,2);
        reads=CharmIconList.InstanceReads;int sprites=CharmIconList.SpriteReads;
        for(int i=0;i<400;i++) Advance(f,1);Assert.Equal(reads,CharmIconList.InstanceReads);Assert.Equal(sprites,CharmIconList.SpriteReads);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ActualDestroyedSpriteOrDonorRearmsAfterSuccess(bool destroyDonor)
    {
        var donor=Icons(1,5);CharmIconList.Instance=donor;var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
        var replacement=Icons(1,5);
        if(destroyDonor) { donor.Destroyed=true;CharmIconList.Instance=replacement; }
        else { donor.spriteList[5].Destroyed=true;donor.spriteList[5]=replacement.spriteList[5]; }
        Advance(f,1);Shown(f,2);Assert.Same(replacement.spriteList[5],f.equipCharmSRs[1].sprite);
    }
    [Fact]
    public void ConfigOffOnRestoresCachedRowWithoutUnrelatedEquipChange()
    {
        CharmIconList.Instance=Icons(1);var f=Row(1);f.EquipUpdateStep();Shown(f,1);
        f.cfg.compEquipRow=0;Advance(f,1);Assert.All(f.equipCharmSRs,s=>Assert.False(s.enabled));
        f.cfg.compEquipRow=1;Advance(f,1);Shown(f,1);
    }
    [Fact]
    public void NativeCountHeightMarginAndNormalEquipChangesAreControls()
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
        Assert.Equal(54*16f/f.BOTTOM_H,f.equipCharmSRs[1].bounds.size.y,5);
        Assert.Equal(8-40*16f/f.BOTTOM_H,f.equipCharmSRs[1].bounds.max.x,5);
        f.SetEquipState(5);Advance(f,1);Shown(f,1);Assert.Same(CharmIconList.Instance.spriteList[5],f.equipCharmSRs[0].sprite);
        f.SetEquipState();Advance(f,1);Shown(f,0);
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CompleteNonMapHeaderRoutesKeepHealthyEquippedRendererIdentity(int id)
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
        var pool=f.equipCharmSRs;var first=pool[0];int reads=CharmIconList.InstanceReads,sprites=CharmIconList.SpriteReads;
        Time.frameCount++;f.HeaderTickStep(id);Shown(f,2);
        Assert.Same(pool,f.equipCharmSRs);Assert.Same(first,pool[0]);
        Assert.Equal(reads,CharmIconList.InstanceReads);Assert.Equal(sprites,CharmIconList.SpriteReads);
    }
    [Fact]
    public void CompleteMapHeaderOwnsColumnAndReturnRestoresEquippedRow()
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
        var first=f.equipCharmSRs[0];int reads=CharmIconList.InstanceReads;
        Time.frameCount++;f.HeaderTickStep(0);Assert.All(f.equipCharmSRs,s=>Assert.False(s.enabled));
        Assert.Equal(reads,CharmIconList.InstanceReads);
        Time.frameCount++;f.HeaderTickStep(4);Shown(f,2);Assert.Same(first,f.equipCharmSRs[0]);
    }
    [Fact]
    public void CompletePauseGateHidesEquippedRowAndResumePreservesOwner()
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();f.Step(false);Shown(f,2);
        var first=f.equipCharmSRs[0];Assert.True(f.Drawn(first.gameObject));int reads=CharmIconList.InstanceReads;
        f.Step(true);Assert.False(f.Drawn(first.gameObject));Assert.Equal(reads,CharmIconList.InstanceReads);
        f.Step(false);Shown(f,2);Assert.True(f.Drawn(first.gameObject));Assert.Same(first,f.equipCharmSRs[0]);
    }
    [Fact]
    public void CompleteHiddenCompanionGateDoesNotDiscoverEquippedDonors()
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();f.Step(false);Shown(f,2);
        var first=f.equipCharmSRs[0];int reads=CharmIconList.InstanceReads;
        f.HudFaded=true;f.Step(false);Assert.False(f.Drawn(first.gameObject));Assert.Equal(reads,CharmIconList.InstanceReads);
        f.HudFaded=false;f.Step(false);Shown(f,2);Assert.True(f.Drawn(first.gameObject));Assert.Same(first,f.equipCharmSRs[0]);
    }
    [Fact]
    public void CompleteToastEdgeHidesThenRestoresUnchangedEquippedState()
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
        var first=f.equipCharmSRs[0];f.SetBenchToast("Native toast",10);Advance(f,1);
        Assert.All(f.equipCharmSRs,s=>Assert.False(s.enabled));
        Time.unscaledTime=11;Advance(f,1);Shown(f,2);Assert.Same(first,f.equipCharmSRs[0]);
    }
    [Fact]
    public void CompleteNativeOwnershipRetirementRebuildsEquippedRowFromNewDonor()
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
        var oldRoot=f.equipRowRoot;var oldRenderer=f.equipCharmSRs[0];
        f.Manager.inventoryFSM=new NativeOwner();f.Step(true);
        Assert.Null(f.equipRowRoot);Assert.Empty(f.equipCharmSRs);Assert.Contains(oldRoot.parent.gameObject,f.Destroyed);
        var donor=Icons(1,5);CharmIconList.Instance=donor;f.FrameBuildStep();f.EquipUpdateStep();Shown(f,2);
        Assert.NotSame(oldRenderer,f.equipCharmSRs[0]);Assert.Same(donor.spriteList[1],f.equipCharmSRs[0].sprite);
    }
    [Fact]
    public void CompleteConfigReloadRetiresAndRebuildsEquippedRowOwner()
    {
        CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
        var oldRenderer=f.equipCharmSRs[0];f.ConfigReloadStep();f.cfg.compEquipRow=0;f.ConfigReloadStep();
        Assert.Null(f.equipRowRoot);Assert.Empty(f.equipCharmSRs);
        f.cfg.compEquipRow=1;f.ConfigReloadStep();f.FrameBuildStep();f.EquipUpdateStep();Shown(f,2);
        Assert.NotSame(oldRenderer,f.equipCharmSRs[0]);
    }
    [Fact]
    public void HealthyCompleteUpdateHasStrictZeroAllocationDiscoveryAndListRecreation()
    {
        Exception error=null;var thread=new Thread(()=>
        {
            try
            {
                CharmIconList.Instance=Icons(1,5);var f=Row(1,5);f.EquipUpdateStep();Shown(f,2);
                for(int i=0;i<180;i++) Advance(f,1);
                int reads=CharmIconList.InstanceReads,sprites=CharmIconList.SpriteReads,resources=Resources.Discoveries,hierarchy=DiscoveryCounters.Hierarchy;
                var pool=f.equipCharmSRs;var first=pool[0];long start=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<3600;i++) Advance(f,1);
                long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
                Assert.Equal(0,allocated);Assert.Equal(reads,CharmIconList.InstanceReads);Assert.Equal(sprites,CharmIconList.SpriteReads);
                Assert.Equal(resources,Resources.Discoveries);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);Assert.Same(pool,f.equipCharmSRs);Assert.Same(first,pool[0]);Shown(f,2);
            }
            catch(Exception e) { error=e; }
        });thread.Start();thread.Join();if(error!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
