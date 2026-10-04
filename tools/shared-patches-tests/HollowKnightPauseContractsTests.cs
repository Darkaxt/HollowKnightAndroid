using HkPauseContracts;
using FixturePlayMakerFSM = HkPauseContracts.PlayMakerFSM;
using Xunit;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightPauseContractsTests
{
    static void AssertGameplayHidden(HKDualScreen lower)
    {
        foreach (GameObject role in new[] { lower.Health, lower.Soul, lower.Geo, lower.mapClone,
                 lower.invCloneCache, lower.charmCloneCache, lower.journalCloneCache, lower.guideCloneCache, lower.frameRoot, lower.Frame, lower.Controls, lower.Heal })
            Assert.False(lower.Drawn(role));
        Assert.False(lower.bgCaptureCam.enabled);
        Assert.False(lower.bgShow);
        Assert.Equal(0, lower.attrCam.cullingMask);
        Assert.Equal(0, lower.promptCam.cullingMask);
        Assert.Equal(0, lower.clearCam.cullingMask);
        Assert.Equal(CameraClearFlags.SolidColor, lower.clearCam.clearFlags);
        Assert.True(lower.Drawn(lower.logoGo));
    }

    [Fact]
    public void GameplayPauseShowsLogoWithoutLifeSoulGeoCompanionOrBackdrop()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        Assert.True(lower.Drawn(lower.Health));
        Assert.True(lower.Drawn(lower.frameRoot));
        Assert.False(lower.bgCaptureCam.enabled);
        Assert.False(lower.Drawn(lower.logoGo));
        lower.Step(true);
        AssertGameplayHidden(lower);
    }

    [Fact]
    public void RepeatedPauseAndPauseOwnedSubmenusRetainPageAndRestoreOnNextFrame()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        GameObject map = lower.mapClone, inventory = lower.invCloneCache, charms = lower.charmCloneCache,
                   frame = lower.frameRoot;
        int updates = lower.Updates, touches = lower.TouchPolls, prewarms = lower.Prewarms;
        for (int cycle = 0; cycle < 5; cycle++)
        {
            // Upper Pause -> Options -> Mods -> Options -> Skins -> Options -> Pause.
            // All are owned by the same native paused game (no separate lower owner).
            foreach (string route in new[] { "Pause", "Options", "Mods", "Options", "Skins", "Options", "Pause" })
            {
                lower.Manager.MenuState = route;
                lower.Step(true);
                AssertGameplayHidden(lower);
                Assert.Equal(updates, lower.Updates);
                Assert.Equal(touches, lower.TouchPolls);
                Assert.Equal(prewarms, lower.Prewarms);
                Assert.Same(map, lower.mapClone);
                Assert.Same(inventory, lower.invCloneCache);
                Assert.Same(charms, lower.charmCloneCache);
                Assert.Same(frame, lower.frameRoot);
                Assert.Equal(2, lower.tab.cur);
                Assert.Equal(2, lower.tab.tap);
                Assert.Equal(2, lower.tab.built);
            }
            lower.Step(false); // First orchestration frame after unpause, no settle/retry window.
            Assert.Equal(1 << HKDualScreen.HUD_LAYER, lower.hudCam2.cullingMask);
            Assert.Equal(1 << HKDualScreen.ATTR_LAYER, lower.attrCam.cullingMask);
            Assert.Equal(1 << HKDualScreen.TUT_LAYER, lower.promptCam.cullingMask);
            Assert.True(lower.Drawn(lower.Health));
            Assert.True(lower.Drawn(frame));
            Assert.True(lower.Drawn(lower.Heal));
            Assert.False(lower.bgCaptureCam.enabled);
            Assert.False(lower.Drawn(lower.logoGo));
            Assert.Equal(++updates, lower.Updates);
            Assert.Equal(++touches, lower.TouchPolls);
            Assert.Equal(++prewarms, lower.Prewarms);
        }
        Assert.Equal(0, lower.Teardowns);
        Assert.Equal(0, lower.FrameTeardowns);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void UnpauseRestoresSupportedRoleMasksNotEverything(bool companion, bool popup, bool hudFaded)
    {
        HKDualScreen lower = new();
        lower.cfg.companion = companion ? 1 : 0;
        lower.Popup = popup;
        lower.HudFaded = hudFaded;
        lower.Step(false);
        int hud = lower.hudCam2.cullingMask, attr = lower.attrCam.cullingMask, prompt = lower.promptCam.cullingMask;
        bool hudEnabled = lower.hudCam2.enabled, attrEnabled = lower.attrCam.enabled, promptEnabled = lower.promptCam.enabled;
        lower.Step(true);
        Assert.True(lower.Drawn(lower.logoGo)); // Even a paused popup must not blank the title's HUD camera.
        Assert.Equal(0, lower.attrCam.cullingMask);
        Assert.Equal(0, lower.promptCam.cullingMask);
        lower.Step(false);
        Assert.Equal(hud, lower.hudCam2.cullingMask);
        Assert.Equal(attr, lower.attrCam.cullingMask);
        Assert.Equal(prompt, lower.promptCam.cullingMask);
        Assert.Equal(hudEnabled, lower.hudCam2.enabled);
        Assert.Equal(attrEnabled, lower.attrCam.enabled);
        Assert.Equal(promptEnabled, lower.promptCam.enabled);
    }

    [Fact]
    public void NativeInventoryAloneRetainsExistingHudAndCompanionPolicy()
    {
        HKDualScreen lower = new() { HudFaded = true };
        lower.Step(false, inventory: true);
        Assert.True(lower.Drawn(lower.Health));
        Assert.True(lower.Drawn(lower.frameRoot));
        Assert.Equal(1 << HKDualScreen.TUT_LAYER, lower.promptCam.cullingMask);
        Assert.False(lower.bgCaptureCam.enabled);
        Assert.Equal(1, lower.Updates);
        Assert.Equal(0, lower.Teardowns);
        lower.Step(true, inventory: true);
        AssertGameplayHidden(lower);
        lower.Step(false, inventory: true);
        Assert.True(lower.Drawn(lower.Health));
        Assert.True(lower.Drawn(lower.frameRoot));
        Assert.Equal(2, lower.Updates);
    }

    [Fact]
    public void FirstUnpausedFrameKeepsExistingHudThenTutorialRoutingOrder()
    {
        HKDualScreen lower = new();
        lower.RoutedHudPrompt = new();
        lower.RoutedHudPrompt.transform.SetParent(lower.Cameras.hudCanvas.transform);
        lower.Step(true);
        Assert.False(lower.Drawn(lower.RoutedHudPrompt));
        lower.Step(false);
        Assert.Equal(HKDualScreen.TUT_LAYER, lower.RoutedHudPrompt.layer);
        Assert.True(lower.Drawn(lower.RoutedHudPrompt));
        Assert.Equal(1 << HKDualScreen.TUT_LAYER, lower.promptCam.cullingMask);
    }

    [Fact]
    public void PausedHudRootReplacementIsHiddenAndRestoredOnFirstUnpausedFrame()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        lower.Step(true);
        GameObject replacement = new();
        GameObject newHealth = new();
        newHealth.transform.SetParent(replacement.transform);
        lower.Cameras.hudCanvas = replacement;
        lower.Step(true);
        Assert.False(lower.Drawn(newHealth));
        Assert.False(lower.Drawn(lower.Health));
        Assert.True(lower.Health.activeInHierarchy); // Return ownership; do not destroy or deactivate native HUD.
        Assert.Equal(HKDualScreen.UI_LAYER, lower.Health.layer);
        Assert.True(lower.Drawn(lower.logoGo));
        lower.Step(false);
        Assert.True(lower.Drawn(newHealth));
        Assert.False(lower.Drawn(lower.Health));
        Assert.Equal(HKDualScreen.UI_LAYER, lower.Health.layer);
        Assert.Equal(HKDualScreen.HUD_LAYER, replacement.layer);
        Assert.Equal(0, lower.Teardowns);
    }

    [Fact]
    public void UnchangedPausedRenderingTickDoesNotAllocateOrRebuild()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        lower.Step(true);
        AssertGameplayHidden(lower);
        int updates = lower.Updates, touches = lower.TouchPolls, prewarms = lower.Prewarms;
        for (int i = 0; i < 100; i++) lower.Step(true);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3600; i++) lower.Step(true);
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(updates, lower.Updates);
        Assert.Equal(touches, lower.TouchPolls);
        Assert.Equal(prewarms, lower.Prewarms);
        Assert.Equal(0, lower.Teardowns);
        Assert.Equal(0, lower.FrameTeardowns);
        AssertGameplayHidden(lower);
    }

    [Fact]
    public void AllFiveProductionRoutesDispatchToRealOwnedCaches()
    {
        HKDualScreen lower = new();
        GameObject[] caches = { lower.mapClone, lower.invCloneCache, lower.charmCloneCache, lower.journalCloneCache, lower.guideCloneCache };
        for(int id = 0; id < 5; id++) { Assert.Same(caches[id],lower.Route(id)); Assert.True(lower.Ready(id)); }
        Assert.Same(caches[0],lower.Route(-1)); Assert.Same(caches[0],lower.Route(5));
        lower.invRun.finalized = false; Assert.False(lower.Ready(1)); Assert.True(lower.Ready(3));
        lower.guideCloneCache = null; Assert.False(lower.Ready(4));
    }

    [Fact]
    public void SourceReplacementWhilePausedRetiresAllFiveCachesAndRearmsDonors()
    {
        HKDualScreen lower = new(); lower.Step(false); lower.Step(true);
        lower.iconRetry.Resolved(); lower.Manager.inventoryFSM = new UnityEngine.Object();
        lower.Step(true);
        for(int id=0;id<5;id++) Assert.Null(lower.Route(id));
        Assert.True(lower.iconRetry.Due(Time.frameCount));
        Assert.Equal(1,lower.Teardowns);
        Assert.Equal(2,lower.tab.tap);
        AssertGameplayHidden(lower);
    }

    [Fact]
    public void ActualTouchDispatchRetainsSameCellAndCancelsForeignOwners()
    {
        for(int col=0;col<5;col++)
        {
            HKDualScreen lower=new(); lower.tab.tap=-1;
            lower.transport.TouchX=(col+.5f)/5; lower.transport.TouchY=1010f/1080;
            lower.transport.contacts=1; lower.transport.TapSequence=1; lower.TouchStep();
            lower.transport.CleanTapX=lower.transport.TouchX; lower.transport.CleanTapY=lower.transport.TouchY;
            lower.transport.contacts=0; lower.transport.CleanTapSequence=1; lower.TouchStep();
            Assert.Equal(HKLowerLayout.TabAtColumn(col),lower.tab.tap);
        }
        for(int cancel=0;cancel<5;cancel++)
        {
            HKDualScreen lower=new(); lower.tab.cur=0; lower.tab.tap=-1;
            lower.transport.TouchX=.1f; lower.transport.TouchY=cancel==1 ? .5f : 1010f/1080;
            lower.transport.contacts=1; lower.transport.TapSequence=1; lower.TouchStep();
            if(cancel==2){ lower.transport.contacts=2; lower.TouchStep(); }
            if(cancel==3){ lower.slideT=.2f; lower.TouchStep(); }
            if(cancel==4){ lower.mapMarkerMode=true; lower.TouchStep(); }
            lower.transport.CleanTapX=cancel==0 ? .3f : .1f; lower.transport.CleanTapY=1010f/1080;
            lower.transport.CleanTapSequence=1; lower.transport.contacts=0; lower.TouchStep();
            Assert.Equal(-1,lower.tab.tap);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ActualTickConfigChangeClearsTouchButSameValueKeepsResidentRoute(int configured)
    {
        HKDualScreen lower=new(); lower.tab.tap=3; lower.cfg.compTab=configured;
        lower.tab.lastCfg=configured; lower.Step(true); Assert.Equal(3,lower.tab.tap);
        lower.cfg.compTab=(configured+1)%5; lower.Step(true); Assert.Equal(-1,lower.tab.tap);
    }

    [Fact]
    public void ActualReadOnlyJournalRefreshRepaintsAcquisitionAndRemainingNotes()
    {
        HKDualScreen lower = new(); var pd=HkPauseContracts.PlayerData.instance;
        lower.journalRecords.Add(new HKDualScreen.JournalRecord());
        pd.Bools["killedCrawler"]=true; pd.Ints["killsCrawler"]=4;
        lower.JournalDataStep(true); Assert.Empty(lower.journalVisible);
        int layouts=lower.JournalLayouts;
        pd.hasJournal=true; lower.JournalDataStep(false);
        Assert.Equal(layouts+1,lower.JournalLayouts); Assert.Single(lower.journalVisible);
        pd.Ints["killsCrawler"]=0; lower.JournalDataStep(false);
        Assert.True(HKLowerLayout.NotesUnlocked(lower.journalRecords[0].Killed,lower.journalRecords[0].Remaining));
        int reads=pd.Reads; lower.JournalDataStep(false);
        Assert.Equal(reads+3,pd.Reads);
        Assert.Equal(0,pd.Ints["killsCrawler"]); Assert.True(pd.Bools["killedCrawler"]);
    }

    [Fact]
    public void ActualJournalLabelBindingDistinguishesLocalizedLockedEmptyUnknownSelectedAndRemainingNotes()
    {
        HKDualScreen.NativeLabels.Clear();
        try
        {
            HKDualScreen.NativeLabels["UI/INV_NAME_JOURNAL"]="Diario del Cazador";
            HKDualScreen.NativeLabels["Journal/KILL_COUNT_1"]="Derrota";
            HKDualScreen.NativeLabels["Journal/KILL_COUNT_2"]="más para descifrar las notas del Cazador.";
            HKDualScreen.NativeLabels["Journal/NAME_CRAWLER"]="Reptador";
            HKDualScreen.NativeLabels["Journal/DESC_CRAWLER"]="Descripción nativa";
            HKDualScreen.NativeLabels["Journal/NOTE_CRAWLER"]="Notas nativas";
            HKDualScreen lower=new(); lower.journalRecords.Add(new());
            lower.BindJournalStep(null);
            Assert.Equal("Diario del Cazador\n—",lower.journalState.Text);
            Assert.Equal("",lower.journalNotes.Text);
            lower.journalHadBook=true; lower.BindJournalStep(null);
            Assert.Equal("Diario del Cazador\n0 / 1",lower.journalState.Text);
            lower.journalVisible.Add(0); lower.BindJournalStep(null);
            Assert.Equal("Diario del Cazador\n?",lower.journalState.Text);
            var record=lower.journalRecords[0]; record.Killed=true; record.Remaining=4;
            lower.BindJournalStep(record);
            Assert.Equal("",lower.journalState.Text); Assert.Equal("Reptador",lower.journalName.Text);
            Assert.Equal("Descripción nativa",lower.journalDescription.Text);
            Assert.Equal("Derrota 4 más para descifrar las notas del Cazador.",lower.journalNotes.Text);
            Assert.Equal(890,lower.journalNotes.ClipRect.x); Assert.Equal(330,lower.journalNotes.ClipRect.width);
            record.Remaining=0; lower.BindJournalStep(record); Assert.Equal("Notas nativas",lower.journalNotes.Text);
            record.Remaining=4; lower.journalFilled=true; lower.BindJournalStep(record); Assert.Equal("Notas nativas",lower.journalNotes.Text);
            Assert.Empty(HkPauseContracts.PlayerData.instance.Bools); Assert.Empty(HkPauseContracts.PlayerData.instance.Ints);
        }
        finally { HKDualScreen.NativeLabels.Clear(); }
    }

    [Theory]
    [InlineData(3)] [InlineData(4)]
    public void ActualPaneGraphicsBuilderBorrowsOnlyOwnedShellQuadAndNativeSpriteInputs(int id)
    {
        HKDualScreen lower=new(); Assert.True(lower.GraphicsBuild(id));
        var owned=id==3 ? lower.journalGraphics : lower.guideGraphics;
        Assert.Same(lower.tabTL.sprite,owned.TL.sprite); Assert.Same(lower.tabBR.sprite,owned.BR.sprite);
        Assert.Same(lower.tabGlow.sprite,owned.Glow.sprite); Assert.Same(lower.shellRule.sprite,owned.RuleLeft.sprite);
        foreach(var mask in new[]{owned.Top,owned.Bottom,owned.Left,owned.Right})
        {
            Assert.Same(lower.Route(id).transform,mask.transform.parent);
            Assert.Same(lower.mapMaskTopR.sharedMaterial,mask.sharedMaterial);
            Assert.Same(lower.mapMaskTopT.GetComponent<MeshFilter>().sharedMesh,mask.transform.GetComponent<MeshFilter>().sharedMesh);
            Assert.Equal(HKDualScreen.ATTR_LAYER,mask.gameObject.layer); Assert.Equal(10000,mask.sortingOrder);
        }
        Assert.False(owned.TL.enabled); Assert.False(owned.BR.enabled); Assert.False(owned.Glow.enabled);
        lower.tabTL.sprite=null; Assert.False(lower.GraphicsBuild(id));
        Assert.Same(owned,id==3 ? lower.journalGraphics : lower.guideGraphics);
    }

    [Fact]
    public void ActualJournalLayoutTapScrollClipsCursorAndRetainsLocalGlyphClipDuringSlideAndPause()
    {
        HKDualScreen lower=new(); Assert.True(lower.GraphicsBuild(3));
        lower.tab.cur=lower.tab.tap=3; var pd=HkPauseContracts.PlayerData.instance;
        pd.hasJournal=true; pd.Bools["killedCrawler"]=true; pd.Ints["killsCrawler"]=2;
        for(int i=0;i<30;i++) lower.journalRecords.Add(new());
        lower.JournalDataStep(true);
        lower.SelectionTap(3,45f/1240,300f/1080);
        Assert.Equal(0,lower.journalSelected); Assert.True(lower.journalGraphics.TL.enabled);
        Assert.Equal("NAME_CRAWLER",lower.journalName.Text);
        lower.ScrollStep(-.1f); Assert.Equal(1,lower.journalScrollRow);
        Assert.False(lower.journalGraphics.TL.enabled); Assert.Equal("NAME_CRAWLER",lower.journalName.Text);
        lower.ScrollStep(.1f); Assert.True(lower.journalGraphics.TL.enabled);
        int selected=lower.journalSelected;
        lower.SelectionTap(3,45f/1240,930f/1080); Assert.Equal(selected,lower.journalSelected); // clipped body padding is not a row
        var localClip=lower.journalNotes.ClipRenderers[0].Clip;
        float oldRule=lower.journalGraphics.RuleLeft.transform.position.x;
        lower.tab.cur=lower.tab.tap=4; lower.paneClone=lower.guideCloneCache;
        lower.slideOutClone=lower.journalCloneCache; lower.slideT=0;
        lower.slideDir=HKLowerLayout.SlideDirection(3,4); Time.unscaledDeltaTime=.1f;
        lower.SlideStep();
        float shift=lower.journalCloneCache.transform.position.x;
        Assert.Equal(oldRule+shift,lower.journalGraphics.RuleLeft.transform.position.x,3);
        Assert.Equal(localClip.x,lower.journalNotes.ClipRenderers[0].Clip.x);
        Assert.Equal(localClip.w,lower.journalNotes.ClipRenderers[0].Clip.w);
        float travel=lower.slideT; var root=lower.journalCloneCache;
        lower.Step(true); lower.Step(true);
        Assert.Equal(travel,lower.slideT); Assert.Same(root,lower.journalCloneCache);
        Assert.False(lower.Drawn(lower.journalGraphics.RuleLeft.gameObject));
        Assert.False(lower.Drawn(lower.journalGraphics.TL.gameObject));
        Assert.Equal(0,lower.Updates); Assert.Equal(0,lower.CaptureSetups);
    }

    [Fact]
    public void ActualGuideLayoutScrollHidesEveryOffscreenFallbackRendererAndMissingLocalizedRows()
    {
        HKDualScreen lower=new(); Assert.True(lower.GraphicsBuild(4)); lower.tab.cur=lower.tab.tap=4;
        var pd=HkPauseContracts.PlayerData.instance; pd.Bools["hasPin"]=true;
        for(int i=0;i<11;i++)
        {
            var record=new HKDualScreen.GuideRecord { Condition=HKDualScreen.NativeGuideConditions[i],Key="row-localization-"+i,Sheet="UI" };
            record.Icon.transform.SetParent(lower.guideCloneCache.transform); record.Label.Root.SetParent(lower.guideCloneCache.transform);
            lower.guideRecords.Add(record); pd.Bools[record.Condition]=true;
        }
        lower.GuideDataStep(true); lower.SelectionTap(4,200f/1240,280f/1080);
        Assert.Equal(0,lower.guideSelected); Assert.True(lower.guideGraphics.TL.enabled);
        Assert.Equal("row-localization-0",lower.guideDetail.Text);
        lower.ScrollStep(-.1f); Assert.Equal(1,lower.guideScrollRow);
        Assert.False(lower.guideGraphics.TL.enabled);
        Assert.All(lower.guideRecords[0].Label.ClipRenderers,r=>Assert.False(r.enabled));
        Assert.All(lower.guideRecords[10].Label.ClipRenderers,r=>Assert.True(r.enabled));
        int selected=lower.guideSelected; lower.SelectionTap(4,200f/1240,930f/1080);
        Assert.Equal(selected,lower.guideSelected);
        lower.guideRecords[0].Key=""; lower.GuideDataStep(true);
        Assert.DoesNotContain(0,lower.guideVisible); Assert.Equal(-1,lower.guideSelected);
        Assert.All(lower.guideRecords[0].Label.ClipRenderers,r=>Assert.False(r.enabled));
        Assert.Equal(0,pd.Ints.Count); Assert.Equal(12,pd.Bools.Count);
    }

    [Fact]
    public void ActualSiblingLanguageAndGeometryAdmissionIsPerOwnedPaneNotSharedLastRefreshedOwner()
    {
        HKDualScreen.NativeLabels.Clear(); HkPauseContracts.TeamCherry.Localization.Language.Code=0;
        try
        {
            HKDualScreen lower=new(); lower.GraphicsBuild(3); lower.GraphicsBuild(4);
            var pd=HkPauseContracts.PlayerData.instance;
            pd.hasJournal=true; pd.Bools["killedCrawler"]=true; pd.Ints["killsCrawler"]=1;
            pd.Bools["hasPin"]=true; pd.Bools["hasPinBench"]=true;
            lower.journalRecords.Add(new()); lower.guideRecords.Add(new() { Key="fixture-guide",Sheet="UI" });
            HKDualScreen.NativeLabels["Journal/NAME_CRAWLER"]="Old journal";
            HKDualScreen.NativeLabels["UI/fixture-guide"]="Old guide";
            lower.tab.cur=lower.tab.tap=3; lower.BuildStep(3); lower.SupplementaryStep(); lower.SelectionTap(3,45f/1240,300f/1080);
            lower.tab.cur=lower.tab.tap=4; lower.BuildStep(4); lower.SupplementaryStep(); lower.SelectionTap(4,200f/1240,280f/1080);
            lower.Step(true);
            HkPauseContracts.TeamCherry.Localization.Language.Code=1;
            HKDualScreen.NativeLabels["Journal/NAME_CRAWLER"]="Nuevo diario";
            HKDualScreen.NativeLabels["UI/fixture-guide"]="Nueva guía";
            lower.Step(true);
            Assert.Equal("Old journal",lower.journalName.Text); Assert.Equal("Old guide",lower.guideDetail.Text);
            lower.BuildStep(4); lower.Step(false); Assert.Equal("Nueva guía",lower.guideDetail.Text);
            Assert.Equal("Old journal",lower.journalName.Text);
            lower.tab.cur=lower.tab.tap=3; lower.BuildStep(3); lower.Step(false);
            Assert.Equal("Nuevo diario",lower.journalName.Text); Assert.Equal(1,lower.journalGraphics.Language);
            lower.BOTTOM_W=1600; lower.tab.cur=lower.tab.tap=4; lower.BuildStep(4); lower.Step(false);
            Assert.Equal(1600,lower.guideGraphics.Width); Assert.Equal(1240,lower.journalGraphics.Width);
            lower.tab.cur=lower.tab.tap=3; lower.BuildStep(3); lower.Step(false);
            Assert.Equal(1600,lower.journalGraphics.Width);
            Assert.Equal(415*1600f/1240-800,lower.journalGraphics.RuleLeft.transform.position.x,3);
        }
        finally { HKDualScreen.NativeLabels.Clear(); HkPauseContracts.TeamCherry.Localization.Language.Code=0; }
    }

    [Theory]
    [InlineData(3)] [InlineData(4)]
    public void ActualHealthySupplementaryTickDoesNotAllocateOrRediscoverOrRelayout(int id)
    {
        HKDualScreen lower=new(); lower.GraphicsBuild(id); lower.tab.cur=lower.tab.tap=id;
        var pd=HkPauseContracts.PlayerData.instance;
        pd.hasJournal=true; pd.Bools["killedCrawler"]=true; pd.Ints["killsCrawler"]=2;
        pd.Bools["hasPin"]=true; pd.Bools["hasPinBench"]=true;
        lower.journalRecords.Add(new()); lower.guideRecords.Add(new());
        if(id==3) lower.JournalDataStep(true); else lower.GuideDataStep(true);
        for(int i=0;i<300;i++) { Time.frameCount++; lower.SupplementaryStep(); }
        int journalLayouts=lower.JournalLayouts,guideLayouts=lower.GuideLayouts;
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<3600;i++) { Time.frameCount++; lower.SupplementaryStep(); }
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.Equal(journalLayouts,lower.JournalLayouts); Assert.Equal(guideLayouts,lower.GuideLayouts);
        Assert.Equal(0,lower.BuildAttempts); Assert.Empty(lower.Destroyed); Assert.Equal(0,lower.CaptureSetups);
    }

    [Theory]
    [InlineData(3,415f,875f,16f)] [InlineData(4,820f,0f,20f)]
    public void ActualPaneGraphicsOwnCanonicalGuttersBodyAndSideMasksAtScaledWidth(int id,float x,float second,float margin)
    {
        HKDualScreen lower=new(); lower.BOTTOM_W=1600;
        lower.GraphicsStep(id); var owned=id==3 ? lower.journalGraphics : lower.guideGraphics;
        var g=lower.LowerGeometry(); float sx=1600f/1240;
        Assert.Equal(x*sx-g.Width/2,owned.RuleLeft.transform.position.x,3);
        Assert.Equal(second>0,owned.RuleRight.enabled);
        if(second>0) Assert.Equal(second*sx-g.Width/2,owned.RuleRight.transform.position.x,3);
        Assert.Equal(g.BodyHeight-margin*2,owned.RuleLeft.transform.localScale.x,3);
        Assert.True(owned.Top.enabled); Assert.True(owned.Bottom.enabled); Assert.True(owned.Left.enabled); Assert.True(owned.Right.enabled);
        Assert.Equal(g.HudHeight+margin,owned.Top.transform.localScale.y,3);
        Assert.Equal(g.Height-g.TabTop+margin,owned.Bottom.transform.localScale.y,3);
        Assert.Equal(20*sx,owned.Left.transform.localScale.x,3);
        Assert.Equal(20*sx,owned.Right.transform.localScale.x,3);
        Assert.Equal(10000,owned.Top.sortingOrder); Assert.Equal(10000,owned.Left.sortingOrder);
    }

    [Fact]
    public void ActualOwnedCursorUsesNativeFixedPixelsAndPointFifteenTravelAndStowsOffscreenSelection()
    {
        HKDualScreen lower=new(); Time.unscaledDeltaTime=.075f;
        lower.SelectionStep(3,new Rect(20,256,100,100),true,0);
        var owned=lower.journalGraphics;
        Assert.Equal(64,owned.TL.bounds.size.x); Assert.Equal(64,owned.TL.bounds.size.y);
        Assert.Equal(64,owned.BR.bounds.size.x); Assert.Equal(64,owned.BR.bounds.size.y);
        Assert.Equal(124,owned.Glow.bounds.size.x); Assert.Equal(124,owned.Glow.bounds.size.y);
        float from=owned.Center.x;
        Time.frameCount++;
        lower.SelectionStep(3,new Rect(246,256,100,100),true,1);
        Assert.Equal(.5f,owned.Travel,3); Assert.Equal((from+296)/2,owned.Center.x,3);
        Time.frameCount++; lower.CursorStep(3); Assert.Equal(1,owned.Travel); Assert.Equal(296,owned.Center.x);
        lower.SelectionStep(3,new Rect(),false,1);
        Assert.False(owned.TL.enabled); Assert.False(owned.BR.enabled); Assert.False(owned.Glow.enabled);
        lower.SelectionStep(4,new Rect(20,260,780,66),true,0);
        Assert.Equal(64,lower.guideGraphics.TL.bounds.size.x); Assert.Equal(64,lower.guideGraphics.TL.bounds.size.y);
        lower.Step(true); var before=lower.guideGraphics.Center;
        lower.Step(true); Assert.Equal(before.x,lower.guideGraphics.Center.x); Assert.Equal(before.y,lower.guideGraphics.Center.y);
        lower.Manager.inventoryFSM=new UnityEngine.Object(); lower.Step(true);
        Assert.Null(lower.journalGraphics); Assert.Null(lower.guideGraphics);
        Time.unscaledDeltaTime=.1f;
    }

    [Fact]
    public void ActualLabelClipAppliesNativeLocalBoundsToEveryGlyphRendererIncludingFallbacks()
    {
        HKDualScreen lower=new(); lower.journalHadBook=true; lower.journalRecords.Add(new()); lower.journalVisible.Add(0);
        var record=lower.journalRecords[0]; record.Killed=true; record.Remaining=1;
        lower.BindJournalStep(record);
        foreach(var label in new[]{lower.journalName,lower.journalDescription,lower.journalNotes,lower.journalState})
        {
            Assert.All(label.ClipRenderers,r=>Assert.True(r.ClipWrites>0));
            Assert.All(label.ClipRenderers,r=>Assert.Equal(label.Text.Length>0,r.enabled));
            var r=label.ClipRenderers[0];
            var rect=label.ClipRect;
            var worldMin=new Vector3(rect.x-620,540-rect.y-rect.height,0);
            var worldMax=new Vector3(rect.x+rect.width-620,540-rect.y,0);
            var min=label.Root.InverseTransformPoint(worldMin); var max=label.Root.InverseTransformPoint(worldMax);
            Assert.Equal(min.x,r.Clip.x,3); Assert.Equal(min.y,r.Clip.y,3);
            Assert.Equal(max.x,r.Clip.z,3); Assert.Equal(max.y,r.Clip.w,3);
            Assert.False(r.InClip(new Vector2(r.Clip.x-1,r.Clip.y)));
            Assert.False(r.InClip(new Vector2(r.Clip.z+1,r.Clip.w)));
            Assert.False(r.InClip(new Vector2(r.Clip.x,r.Clip.y-1)));
            Assert.False(r.InClip(new Vector2(r.Clip.z,r.Clip.w+1)));
        }
    }

    [Fact]
    public void ActualReadOnlyGuideRefreshUsesNativePinGateNotGuessedMapOwnership()
    {
        HKDualScreen lower = new(); var pd=HkPauseContracts.PlayerData.instance;
        lower.guideRecords.Add(new HKDualScreen.GuideRecord());
        lower.guideRecords.Add(new HKDualScreen.GuideRecord { Condition="unknown" });
        pd.Bools["hasPinBench"]=true;
        lower.GuideDataStep(true); Assert.Empty(lower.guideVisible);
        pd.Bools["hasPin"]=true; lower.GuideDataStep(false);
        Assert.Equal(new[]{0},lower.guideVisible);
        Assert.Equal("NAME",lower.guideRecords[0].Text);
    }

    static (Transform root,Transform row,FixturePlayMakerFSM fsm) NativeGuide(int index)
    {
        Transform root=new(new GameObject()), row=new(new GameObject());
        FixturePlayMakerFSM fsm=new(); root.FsMs.Add(fsm);
        fsm.FsmVariables.Objects[HKDualScreen.NativeGuideVariables[index]]=new FsmGameObject { Value=row.gameObject };
        PlayerDataBoolTest Test(string key,string exit) => new() { boolName=new FsmString { Value=key },isFalse=new FsmEvent { Name=exit } };
        fsm.FsmStates=new[] {
            new FsmState { Name="Draw Pins", Actions=new object[] { Test("hasPin","NO PIN") } },
            new FsmState { Name=HKDualScreen.NativeGuideStates[index],Actions=new object[] { Test(HKDualScreen.NativeGuideConditions[index],"FINISHED") } }
        };
        return (root,row,fsm);
    }

    [Fact]
    public void ActualRootGuideResolverBindsAllElevenNativeTypedAcquiredRowsAndLocalization()
    {
        HKDualScreen.NativeLabels.Clear();
        try
        {
            HKDualScreen lower=new(); var pd=HkPauseContracts.PlayerData.instance; pd.Bools["hasPin"]=true;
            for(int i=0;i<11;i++)
            {
                var native=NativeGuide(i);
                string key=lower.ResolveGuide(native.root,native.row,i);
                Assert.Equal(HKDualScreen.NativeGuideConditions[i],key);
                // Serialized localization metadata is an input stand-in, not a
                // guessed native production key or English-only fallback.
                string localizedKey="fixture-key-"+i;
                HKDualScreen.NativeLabels["UI/"+localizedKey]="Etiqueta localizada "+i;
                lower.guideRecords.Add(new HKDualScreen.GuideRecord { Condition=key,Key=localizedKey,Sheet="UI" });
                pd.Bools[key]=i%2==0;
            }
            lower.GuideDataStep(true);
            Assert.Equal(new[]{0,2,4,6,8,10},lower.guideVisible);
            for(int i=0;i<11;i++)
            {
                var row=lower.guideRecords[i];
                Assert.Equal("Etiqueta localizada "+i,row.Text);
                if(i%2==0) Assert.Equal(row.Text,row.Label.Text);
                else Assert.All(row.Label.ClipRenderers,r=>Assert.False(r.enabled));
            }
            Assert.Equal(12,pd.Bools.Count); Assert.Empty(pd.Ints);
        }
        finally { HKDualScreen.NativeLabels.Clear(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void ActualGuideResolverRejectsMissingOrForeignRootStateActionAndRow(int defect)
    {
        HKDualScreen lower=new(); var native=NativeGuide(0);
        if(defect==0) native.fsm.FsmName="Guessed";
        if(defect==1) native.fsm.FsmVariables.Objects.Clear();
        if(defect==2) native.fsm.FsmVariables.Objects[HKDualScreen.NativeGuideVariables[0]].Value=new();
        if(defect==3) native.fsm.FsmStates[0].Name="Draw Guessed";
        if(defect==4) ((PlayerDataBoolTest)native.fsm.FsmStates[0].Actions[0]).boolName.Value="hasMap";
        if(defect==5) ((PlayerDataBoolTest)native.fsm.FsmStates[0].Actions[0]).isFalse.Name="FINISHED";
        if(defect==6) native.fsm.FsmStates[1].Name="Check Guessed";
        if(defect==7) ((PlayerDataBoolTest)native.fsm.FsmStates[1].Actions[0]).boolName.Value="unknown";
        if(defect==8) ((PlayerDataBoolTest)native.fsm.FsmStates[1].Actions[0]).isFalse.Name="NO PIN";
        if(defect==9) native.fsm.FsmStates[1].Actions=null;
        if(defect==10) native.fsm.FsmStates=null;
        Assert.Null(lower.ResolveGuide(native.root,native.row,0));
        int reads=0; Assert.False(HKLowerLayout.GuideVisible(lower.ResolveGuide(native.root,native.row,0),key=>{ reads++; return true; }));
        Assert.Equal(0,reads); Assert.Equal(0,HkPauseContracts.PlayerData.instance.Reads);
    }

    [Fact]
    public void ActualGuideResolverNullAndIndexInputsFailClosed()
    {
        HKDualScreen lower=new(); var native=NativeGuide(0);
        Assert.Null(lower.ResolveGuide(null,native.row,0)); Assert.Null(lower.ResolveGuide(native.root,null,0));
        Assert.Null(lower.ResolveGuide(native.root,native.row,-1)); Assert.Null(lower.ResolveGuide(native.root,native.row,11));
        native.root.FsMs.Clear(); native.row.FsMs.Add(native.fsm);
        Assert.Null(lower.ResolveGuide(native.root,native.row,0)); // no row-local driver fallback
    }

    [Theory]
    [InlineData(3)] [InlineData(4)]
    public void FailedSupplementaryBuildKeepsHealthySiblingAndRetryThrottle(int failed)
    {
        HKDualScreen lower = new();
        var sibling = lower.Route(failed==3 ? 4 : 3);
        var retry = failed==3 ? lower.journalRetry : lower.guideRetry;
        Assert.True(retry.Due(Time.frameCount));
        var discard=typeof(HKDualScreen).GetMethod("DiscardSupplementaryPane",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(discard);
        discard.Invoke(lower,new object[]{failed});
        Assert.Null(lower.Route(failed));
        Assert.Same(sibling,lower.Route(failed==3 ? 4 : 3));
        Assert.False(retry.Due(Time.frameCount+1));
        Assert.True(retry.Due(Time.frameCount+120));
    }

    [Theory]
    [InlineData(3)] [InlineData(4)]
    public void ActualBuildGuardDiscardsThrownPartialCacheWithoutAdmittingOrResettingRetry(int id)
    {
        HKDualScreen lower=new();
        var sibling=lower.Route(id==3 ? 4 : 3);
        if(id==3) lower.journalCloneCache=null; else lower.guideCloneCache=null;
        lower.ThrowPartialBuild=true;
        int frame=Time.frameCount;
        Assert.Null(lower.BuildStep(id));
        Assert.False(lower.Ready(id)); Assert.Null(lower.Route(id));
        Assert.Same(sibling,lower.Route(id==3 ? 4 : 3));
        Assert.True(sibling.activeSelf); Assert.Equal(1,lower.BuildWarnings);
        Assert.Single(lower.Destroyed); Assert.DoesNotContain(sibling,lower.Destroyed);
        Time.frameCount=frame+1;
        Assert.Null(lower.BuildStep(id)); Assert.Equal(1,lower.BuildAttempts);
        lower.ThrowPartialBuild=false; Time.frameCount=frame+120;
        Assert.NotNull(lower.BuildStep(id)); Assert.True(lower.Ready(id)); Assert.Equal(2,lower.BuildAttempts);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void CorrelatedPausedOwnerBoundaryRetiresExactlyOnce(int boundary)
    {
        HKDualScreen lower = new(); lower.Step(false); lower.Step(true);
        var old = Enumerable.Range(0,5).Select(lower.Route).ToArray();
        if(boundary == 0 || boundary == 3) HkPauseContracts.PlayerData.instance = new HkPauseContracts.PlayerData();
        if(boundary == 1 || boundary == 3) HkPauseContracts.HkStageHooks.SkinStamp++;
        if(boundary == 2 || boundary == 3) lower.Manager.gameMap = new UnityEngine.Object();
        if(boundary == 3) lower.Manager.inventoryFSM = new UnityEngine.Object();
        lower.Step(true);
        for(int id=0;id<5;id++) Assert.Null(lower.Route(id));
        Assert.Equal(1,lower.Teardowns);
        Assert.Equal(1,lower.FrameTeardowns);
        Assert.All(old,go=>Assert.Contains(go,lower.Destroyed));
        lower.Step(true);
        Assert.Equal(1,lower.Teardowns);
        Assert.Equal(1,lower.FrameTeardowns);
        Assert.Equal(2,lower.tab.tap);
        Assert.Equal(1,lower.Updates);
    }

    [Fact]
    public void ActualSlideDriverMovesAndStowsAllTwentyFiveRoutePairs()
    {
        for(int from=0;from<5;from++) for(int to=0;to<5;to++)
        {
            HKDualScreen lower = new();
            var outgoing=lower.Route(from); var incoming=lower.Route(to);
            lower.tab.cur=to; lower.paneClone=incoming;
            lower.slideOutClone=outgoing; lower.slideT=0;
            lower.slideDir=HKLowerLayout.SlideDirection(from,to);
            lower.slideCamValid=true; lower.slideStartCamPos=lower.attrCam.transform.position;
            Time.unscaledDeltaTime=.15f;
            lower.SlideStep();
            Assert.Equal(.5f,lower.slideT,3);
            Assert.Equal(0,outgoing.transform.position.y);
            if(from!=to) Assert.Equal(Math.Sign(lower.slideDir),Math.Sign(incoming.transform.position.x));
            incoming.transform.position=Vector3.zero;
            lower.SlideStep();
            Assert.Equal(1,lower.slideT); Assert.Null(lower.slideOutClone);
            Assert.True(incoming.activeSelf);
            Assert.Equal(from==to,outgoing.activeSelf);
        }
        Time.unscaledDeltaTime=.1f;
    }

    [Fact]
    public void JournalAndGuideRetainOwnershipAcrossUnchangedPausePlateau()
    {
        HKDualScreen lower = new(); var journal=lower.Route(3); var guide=lower.Route(4);
        lower.tab.cur=lower.tab.tap=lower.tab.built=3;
        lower.Step(false); lower.Step(true);
        for(int i=0;i<100;i++) lower.Step(true);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<3600;i++) lower.Step(true);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.Same(journal,lower.Route(3)); Assert.Same(guide,lower.Route(4));
        Assert.Equal(3,lower.tab.tap); Assert.Equal(0,lower.Teardowns);
    }

    [Theory]
    [InlineData(-1,0)] [InlineData(0,0)] [InlineData(1,1)] [InlineData(2,2)]
    [InlineData(3,3)] [InlineData(4,4)] [InlineData(5,0)]
    public void ActualPartialConfigFileTickAndUpdateSelectPersistedIdsAndKeepSameValueTouch(int configured,int expected)
    {
        string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"hk-config-contract-"+Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir); Application.persistentDataPath=dir;
        try
        {
            string path=System.IO.Path.Combine(dir,"hkds_layout.json");
            System.IO.File.WriteAllText(path,"{\"compTab\":"+configured+"}");
            HKDualScreen lower=new(); lower.tab.tap=-1; lower.tab.lastCfg=int.MinValue;
            lower.ConfigOwner=new(); lower.ConfigOwner.Read();
            lower.Step(false);
            Assert.Equal(expected,lower.tab.cur); Assert.Equal(-1,lower.tab.tap);
            Assert.Equal(1,lower.cfg.dualScreen); Assert.Equal(1,lower.cfg.companion);
            Assert.Equal(HKLowerLayout.ColumnForTab(expected),HKLowerLayout.ColumnForTab(lower.tab.cur));
            lower.tab.tap=3;
            System.IO.File.WriteAllText(path,"{\"compTab\":"+configured+",\"dim\":0.7}");
            lower.ConfigOwner.Read(); lower.Step(false);
            Assert.Equal(3,lower.tab.cur); Assert.Equal(3,lower.tab.tap);
            System.IO.File.WriteAllText(path,"{\"compTab\":4}");
            lower.ConfigOwner.Read(); lower.Step(false);
            Assert.Equal(configured==4 ? 3 : 4,lower.tab.cur);
            Assert.Equal(configured==4 ? 3 : -1,lower.tab.tap);
        }
        finally { Application.persistentDataPath=null; System.IO.Directory.Delete(dir,true); }
    }

    [Fact]
    public void ActualPartialConfigWithoutTabRestoresMapDefaultsAndMalformedReloadKeepsLastOwner()
    {
        string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"hk-config-contract-"+Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir); Application.persistentDataPath=dir;
        try
        {
            string path=System.IO.Path.Combine(dir,"hkds_layout.json");
            System.IO.File.WriteAllText(path,"{\"debug\":1}");
            HKDualScreen lower=new(); lower.tab.tap=-1; lower.tab.lastCfg=int.MinValue;
            lower.ConfigOwner=new(); lower.ConfigOwner.Read(); lower.Step(false);
            Assert.Equal(0,lower.tab.cur); Assert.Equal(1,lower.cfg.dualScreen); Assert.Equal(1,lower.cfg.companion);
            var owner=lower.cfg; System.IO.File.WriteAllText(path,"{\"compTab\":");
            lower.ConfigOwner.Read(); lower.Step(false);
            Assert.Same(owner,lower.cfg); Assert.Equal(0,lower.tab.cur);
        }
        finally { Application.persistentDataPath=null; System.IO.Directory.Delete(dir,true); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ActualQuitMenuRetiresAllFiveExactlyOnceAndClearsTouchStartupOverride(int selected)
    {
        HKDualScreen lower=new(); lower.tab.tap=selected; lower.Step(false); lower.HudFaded=true;
        var caches=Enumerable.Range(0,5).Select(lower.Route).ToArray(); var frame=lower.frameRoot;
        lower.MenuStep(); lower.MenuStep();
        Assert.Equal(-1,lower.tab.tap); Assert.Equal(-1,lower.tab.built);
        Assert.Equal(1,lower.Teardowns); Assert.Equal(1,lower.FrameTeardowns);
        Assert.All(caches,go=>Assert.Equal(1,lower.Destroyed.Count(item=>ReferenceEquals(item,go))));
        Assert.Equal(1,lower.Destroyed.Count(item=>ReferenceEquals(item,frame)));
        Assert.All(Enumerable.Range(0,5),id=>Assert.Null(lower.Route(id)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualDisplayOffRestoresLowerOwnershipOnceWithoutPollingOrCapture(bool loss)
    {
        HKDualScreen lower=new(); lower.Step(false);
        var caches=Enumerable.Range(0,5).Select(lower.Route).ToArray();
        int updates=lower.Updates,touches=lower.TouchPolls,prewarms=lower.Prewarms;
        if(loss) lower.LoseDisplay(); else lower.cfg.dualScreen=0;
        lower.Step(false); lower.Step(true); lower.Step(false);
        Assert.Equal(1,lower.RouteRestores); Assert.Equal(1,lower.InputReleases);
        Assert.Equal(1,lower.NameRestores); Assert.Equal(1,lower.DialogueRestores);
        Assert.Equal(HKDualScreen.UI_LAYER,lower.Health.layer);
        Assert.Equal(updates,lower.Updates); Assert.Equal(touches,lower.TouchPolls); Assert.Equal(prewarms,lower.Prewarms);
        Assert.Equal(0,lower.CaptureSetups); Assert.Equal(0,lower.Teardowns);
        for(int id=0;id<5;id++) Assert.Same(caches[id],lower.Route(id));
        Assert.False(lower.attrCam.enabled); Assert.False(lower.hudCam2.enabled); Assert.False(lower.promptCam.enabled);
        Assert.False(lower.clearCam.enabled); Assert.False(lower.bgCaptureCam.enabled);
        Assert.Equal(loss ? 0 : 1,lower.transport.ProductChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DualScreenOffRemainsDisabledAcrossPauseAndUnpause(bool transportLost)
    {
        HKDualScreen lower = new();
        lower.Step(false);
        if (transportLost) lower.directDisplayActive = false;
        else lower.cfg.dualScreen = 0;
        int hud = lower.hudCam2.cullingMask, attr = lower.attrCam.cullingMask, prompt = lower.promptCam.cullingMask;
        lower.Step(true);
        lower.Step(false);
        Assert.False(lower.hudCam2.enabled);
        Assert.False(lower.attrCam.enabled);
        Assert.False(lower.promptCam.enabled);
        Assert.False(lower.clearCam.enabled);
        Assert.Equal(hud, lower.hudCam2.cullingMask);
        Assert.Equal(attr, lower.attrCam.cullingMask);
        Assert.Equal(prompt, lower.promptCam.cullingMask);
        Assert.Equal(1, lower.Updates);
        Assert.Equal(0, lower.Teardowns);
    }
}
