using Xunit;
using DualSouls.DualScreen;
namespace HkPauseContracts;

// Touch transport/native state seams only; PollTouch, PollItemTap, pane layout,
// lifecycle, checked action and frame-cache bodies come from production.
[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightCharmActionContractsTests
{
    static HKDualScreen New(int id=1,int cost=1,int slots=3,int filled=0,params int[] equipped)
    {
        Time.frameCount++;Time.unscaledTime=1;BossSequenceController.BoundCharms=false;
        PlayMakerFSM.Broadcasts.Clear();PlayMakerFSM.ThrowBroadcast=null;HKDualScreen.GameInventoryOpen=false;
        HKDualScreen f=new();f.cfg.compTab=2;f.tab.cur=f.tab.tap=f.tab.built=2;
        CharmIconList.Instance=new(){spriteList=Enumerable.Range(0,41).Select(_=>new Sprite()).ToArray()};
        var pane=f.NewNativePane(true);
        f.attrCam.enabled=true;f.attrCam.cullingMask=1<<HKDualScreen.ATTR_LAYER;
        SetupLayer(pane.transform);f.NativeLayoutRouteStep(2);
        var pd=PlayerData.instance;pd.charmSlots=slots;pd.charmSlotsFilled=filled;pd.overcharmed=filled>slots;
        for(int i=1;i<=40;i++) { pd.Ints["charmCost_"+i]=1;HKDualScreen.NativeLabels["UI/CHARM_NAME_"+i]="Charm "+i; }
        pd.Ints["charmCost_"+id]=cost;f.SetEquipState(equipped);
        Choose(f,id);
        var board=f.SelectedItem.GetComponent("InvCharmBackboard") as InvCharmBackboard;
        var renderer=board.charmObject.GetComponent<SpriteRenderer>();
        Assert.True(renderer.gameObject.activeInHierarchy);Assert.True(renderer.enabled);
        Assert.Equal(HKDualScreen.ATTR_LAYER,renderer.gameObject.layer);
        Assert.True(f.attrCam.enabled);Assert.NotEqual(0,f.attrCam.cullingMask&(1<<renderer.gameObject.layer));
        return f;
    }
    static void SetupLayer(Transform root)
    {
        root.gameObject.layer=HKDualScreen.ATTR_LAYER;
        for(int i=0;i<root.childCount;i++) SetupLayer(root.GetChild(i));
    }
    static DirectDisplayContact Contact(float x,float y,int id=0) => new(id,x,y);
    static void Feed(HKDualScreen f,params DirectDisplayContact[] input)
    { f.transport.Feed(Time.unscaledTime,input);f.TouchStep(); }
    static void Down(HKDualScreen f,float x,float y) => Feed(f,Contact(x,y));
    static void Up(HKDualScreen f,float seconds=.1f)
    { Time.unscaledTime+=seconds;Feed(f); }
    static void Tap(HKDualScreen f,float x,float y) { Down(f,x,y);Up(f); }
    static void Choose(HKDualScreen f,int id)
    {
        var board=f.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard"+id);
        var component=board.GetComponent("InvCharmBackboard") as InvCharmBackboard;
        var point=component.charmObject.GetComponent<SpriteRenderer>().bounds.center;
        Tap(f,(point.x-f.compRoot.position.x)/f.BOTTOM_W+.5f,.5f-(point.y-f.compRoot.position.y)/f.BOTTOM_H);
        Assert.Same(board,f.SelectedItem);Assert.Equal("Charm "+id,f.CharmName.Text);
        // Cursor settles through the unchanged production 0.15-second travel.
        f.NativeCursorStep();f.NativeCursorStep();
    }
    static (float x,float y) ActionPoint(HKDualScreen f)
    {
        // The legal EQUIP/UNEQUIP plate now owns the bottom detail band,
        // not the title-to-prose gap. Tap its actual rendered center.
        var plate=f.paneClone.transform.Find("CanonicalCharmActionPlate")?.GetComponent<SpriteRenderer>();
        Assert.NotNull(plate);Assert.True(plate.enabled);
        var point=plate.bounds.center;
        return ((point.x-f.compRoot.position.x)/f.BOTTOM_W+.5f,
                .5f-(point.y-f.compRoot.position.y)/f.BOTTOM_H);
    }
    static void Confirm(HKDualScreen f) { var p=ActionPoint(f);Tap(f,p.x,p.y); }
    static void Equipped(HKDualScreen f,int id,int filled,bool over=false)
    {
        var pd=PlayerData.instance;Assert.True(pd.GetBool("equippedCharm_"+id));Assert.Contains(id,pd.equippedCharms);
        Assert.Equal(filled,pd.charmSlotsFilled);Assert.Equal(over,pd.overcharmed);
        Assert.Equal(1,f.Manager.Equips);Assert.Equal(0,f.Manager.Unequips);
        Assert.Equal(1,f.Manager.hero_ctrl.Updates);
        Assert.Equal(new[]{"CHARM INDICATOR CHECK","UPDATE BLUE HEALTH"},PlayMakerFSM.Broadcasts);
    }
    static void Unequipped(HKDualScreen f,int id,int filled,bool over=false)
    {
        var pd=PlayerData.instance;Assert.False(pd.GetBool("equippedCharm_"+id));Assert.DoesNotContain(id,pd.equippedCharms);
        Assert.Equal(filled,pd.charmSlotsFilled);Assert.Equal(over,pd.overcharmed);
        Assert.Equal(0,f.Manager.Equips);Assert.Equal(1,f.Manager.Unequips);Assert.Equal(1,f.Manager.hero_ctrl.Updates);
        Assert.Equal(new[]{"CHARM INDICATOR CHECK","UPDATE BLUE HEALTH"},PlayMakerFSM.Broadcasts);
    }
    [Fact] public void Red_EquipThroughSeparateCleanConfirmation()
    { var f=New();var pane=f.paneClone;var grid=f.nativeCharmGrid;Confirm(f);Equipped(f,1,1);Assert.Same(pane,f.paneClone);Assert.Same(grid,f.nativeCharmGrid); }
    [Fact] public void Red_UnequipUsesCurrentNativeCost()
    { var f=New(1,2,3,2,1);Confirm(f);Unequipped(f,1,0); }
    [Fact] public void Red_ZeroCostWithRoomEquips()
    { var f=New(1,0);Confirm(f);Equipped(f,1,0); }
    [Fact] public void Red_ExistingOvercharmPermissionEquips()
    { var f=New(1,4);PlayerData.instance.canOvercharm=true;Confirm(f);Equipped(f,1,4,true); }
    [Fact] public void Red_FifthOverflowAttemptUnlocksAndEquips()
    {
        var f=New(1,4);for(int i=0;i<4;i++) { Confirm(f);NoWrites(f,0);Assert.False(PlayerData.instance.canOvercharm); }
        Confirm(f);Equipped(f,1,4,true);Assert.True(PlayerData.instance.canOvercharm);
    }
    [Fact] public void Red_UnequipClearsOvercharmAtCapacity()
    { var f=New(1,2,3,5,1,2,3,4);Confirm(f);Unequipped(f,1,3); }
    [Fact] public void Red_UnequipRetainsOvercharmAboveCapacity()
    { var f=New(1,1,3,5,1,2,3,4,5);Confirm(f);Unequipped(f,1,4,true); }
    [Fact] public void Red_SameFrameReadIsExplicitlyInvalidated()
    {
        var f=New();Assert.False(f.CachedCharm(1));int frame=Time.frameCount;Confirm(f);
        Assert.Equal(frame,Time.frameCount);Assert.True(f.CachedCharm(1));Assert.Equal(1,f.CachedCharmCount);
    }
    static void NoWrites(HKDualScreen f,int filled)
    {
        Assert.Equal(0,f.Manager.Equips+f.Manager.Unequips);Assert.Equal(0,PlayerData.instance.Writes);
        Assert.Equal(filled,PlayerData.instance.charmSlotsFilled);Assert.Equal(0,f.Manager.hero_ctrl?.Updates ?? 0);Assert.Empty(PlayMakerFSM.Broadcasts);
    }
    [Fact] public void Healthy_RepeatedChooserTapsSelectAndDetailOnly()
    { var f=New();Choose(f,1);Choose(f,1);Choose(f,2);Choose(f,1);NoWrites(f,0); }
    [Theory]
    [InlineData("bench")] [InlineData("bound")] [InlineData("broken23")] [InlineData("broken24")] [InlineData("broken25")]
    [InlineData("brokenUnbreakable")] [InlineData("royal1")] [InlineData("royal2")] [InlineData("void4")]
    [InlineData("full")] [InlineData("zeroFull")] [InlineData("overFull")] [InlineData("unowned")]
    public void Healthy_NativeRejectionMatrixNeverMutates(string reason)
    {
        int id=reason=="broken24" ? 24 : reason=="broken25" ? 25 : reason.StartsWith("broken") ? 23 : reason.StartsWith("royal") || reason=="void4" ? 36 : 1;
        bool equipped=reason=="void4";bool full=reason.Contains("Full") || reason=="full";
        var f=New(id,reason=="zeroFull" ? 0 : 1,3,equipped ? 1 : full ? 3 : 0,equipped ? new[]{id} : full ? new[]{2,3,4} : Array.Empty<int>());
        var pd=PlayerData.instance;pd.atBench=reason!="bench";BossSequenceController.BoundCharms=reason=="bound";
        if(reason.StartsWith("broken")) pd.Bools["brokenCharm_"+id]=true;
        pd.Bools["unbreakableCharm_23"]=reason=="brokenUnbreakable";
        pd.royalCharmState=reason=="royal1" ? 1 : reason=="royal2" ? 2 : reason=="void4" ? 4 : 0;
        if(reason=="unowned") pd.Bools["gotCharm_"+id]=false;
        pd.canOvercharm=reason=="overFull";int filled=pd.charmSlotsFilled;var list=pd.equippedCharms.ToArray();Confirm(f);
        NoWrites(f,filled);Assert.Equal(list,pd.equippedCharms);Assert.Equal(equipped,pd.GetBool("equippedCharm_"+id));
    }
    [Fact] public void Healthy_FirstFourOverflowAttemptsKeepEquipmentAndNotchesUnchanged()
    { var f=New(1,4);for(int i=0;i<4;i++) { Confirm(f);NoWrites(f,0);Assert.Empty(PlayerData.instance.equippedCharms);Assert.False(PlayerData.instance.canOvercharm); } }
    [Theory]
    [InlineData("long")] [InlineData("thresholdTime")] [InlineData("drag")] [InlineData("multitouch")] [InlineData("slide")]
    [InlineData("pause")] [InlineData("hidden")] [InlineData("tab")] [InlineData("selection")] [InlineData("transport")]
    [InlineData("retiredTransport")] [InlineData("teardown")]
    public void Healthy_GestureAndLifetimeCancellationNeverReplays(string boundary)
    {
        var f=New();var p=ActionPoint(f);Down(f,p.x,p.y);
        switch(boundary)
        {
            case "drag":Feed(f,Contact(p.x+.04f,p.y));break;
            case "multitouch":Feed(f,Contact(p.x,p.y),Contact(p.x+.01f,p.y,1));break;
            case "slide":f.slideT=.5f;f.TouchStep();f.slideT=1;break;
            case "pause":f.Step(true);f.Step(false);break;
            case "hidden":f.compOn=0;f.Step(false);f.compOn=1;f.Step(false);break;
            case "tab":f.tab.cur=1;f.TouchStep();f.tab.cur=2;break;
            case "selection":f.NativeDetailStep(1,f.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard2"),2);break;
            case "transport":f.transport=new(){Owner=f};break;
            case "retiredTransport":f.transport.IsTransportActive=false;f.Step(false);f.transport.IsTransportActive=true;break;
            case "teardown":f.NativeRetireStep();break;
        }
        Up(f,boundary=="long" ? .5f : boundary=="thresholdTime" ? .35f : .1f);f.TouchStep();NoWrites(f,0);
    }
    [Theory]
    [InlineData("player")] [InlineData("manager")] [InlineData("hero")] [InlineData("managerPlayer")] [InlineData("heroPlayer")]
    [InlineData("managerHero")] [InlineData("nativeBusy")] [InlineData("nativeMissing")] [InlineData("readFailure")]
    [InlineData("costFailure")] [InlineData("negativeCost")] [InlineData("duplicateList")] [InlineData("flagListMismatch")]
    [InlineData("notchMismatch")] [InlineData("duringReadOwnerChange")]
    public void Healthy_UnavailableOrIncoherentPrecommitInputsRejectWithoutWrites(string reason)
    {
        var f=New();var pd=PlayerData.instance;var hero=f.Manager.hero_ctrl;var p=ActionPoint(f);Down(f,p.x,p.y);
        switch(reason)
        {
            case "player":PlayerData.instance=null;break;
            case "manager":GameManager.instance=null;break;
            case "hero":HeroController.instance=null;break;
            case "managerPlayer":f.Manager.playerData=new();break;
            case "heroPlayer":hero.playerData=new();break;
            case "managerHero":f.Manager.hero_ctrl=new(){playerData=pd};break;
            case "nativeBusy":f.Manager.inventoryFSM.ActiveStateName="Opened";break;
            case "nativeMissing":f.Manager.inventoryFSM=null;break;
            case "readFailure":pd.ThrowRead="gotCharm_1";break;
            case "costFailure":pd.ThrowRead="charmCost_1";break;
            case "negativeCost":pd.Ints["charmCost_1"]=-1;break;
            case "duplicateList":pd.equippedCharms.AddRange(new[]{2,2});break;
            case "flagListMismatch":pd.Bools["equippedCharm_1"]=true;break;
            case "notchMismatch":pd.charmSlotsFilled=1;break;
            case "duringReadOwnerChange":pd.OnRead=_=>GameManager.instance=null;break;
        }
        Up(f);f.TouchStep();PlayerData.instance=pd;f.Manager.hero_ctrl=hero;
        NoWrites(f,reason=="notchMismatch" ? 1 : 0);Assert.Equal(0,hero.Updates);
    }
    [Fact] public void Healthy_HiddenAndUnrelatedTicksAddStrictlyZeroAllocationDiscoveryAndNativeCalls()
    {
        var f=New();f.compOn=0;f.Step(false);f.Step(false);var pd=PlayerData.instance;
        int hierarchy=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries,reads=pd.Reads;
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<300;i++) f.Step(false);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);
        Assert.Equal(resources,Resources.Discoveries);Assert.Equal(reads,pd.Reads);NoWrites(f,0);
        f.compOn=1;f.tab.cur=f.tab.tap=f.tab.built=1;f.NewNativePane(false);f.NativeLayoutRouteStep(1);f.Step(false);f.Step(false);
        hierarchy=DiscoveryCounters.Hierarchy;resources=Resources.Discoveries;reads=pd.Reads;before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) f.Step(false);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);
        Assert.Equal(resources,Resources.Discoveries);Assert.Equal(reads,pd.Reads);NoWrites(f,0);
    }
    [Fact] public void Supplemental_DuplicateReleaseIsConsumedExactlyOnce()
    {
        var f=New();Confirm(f);f.TouchStep();f.TouchStep();Equipped(f,1,1);
        Assert.Same(f.charmCloneCache,f.paneClone);
    }
    [Theory] [InlineData("hero")] [InlineData("indicator")] [InlineData("blueHealth")]
    public void Supplemental_PostcommitRefreshFailureNeverReplays(string failure)
    {
        var f=New();if(failure=="hero") f.Manager.hero_ctrl.ThrowRefresh=true;
        else PlayMakerFSM.ThrowBroadcast=failure=="indicator" ? "CHARM INDICATOR CHECK" : "UPDATE BLUE HEALTH";
        Confirm(f);f.TouchStep();f.TouchStep();
        Assert.True(PlayerData.instance.GetBool("equippedCharm_1"));Assert.Equal(new[]{1},PlayerData.instance.equippedCharms);
        Assert.Equal(1,PlayerData.instance.charmSlotsFilled);Assert.Equal(1,f.Manager.Equips);Assert.Equal(0,f.Manager.Unequips);
        Assert.Equal(1,f.Manager.hero_ctrl.Updates);Assert.True(f.CachedCharm(1));
    }
    [Fact] public void Supplemental_PartialWriteFailureNeverRepairsOrReplays()
    {
        var f=New();f.Manager.ThrowListMutation=true;Confirm(f);int writes=PlayerData.instance.Writes;
        Assert.True(writes>0);f.TouchStep();f.TouchStep();Assert.Equal(writes,PlayerData.instance.Writes);
        Assert.Empty(PlayerData.instance.equippedCharms);Assert.Equal(0,f.Manager.Equips+f.Manager.Unequips);
        Assert.Equal(0,f.Manager.hero_ctrl.Updates);
    }
    [Fact] public void Supplemental_VisibilityEpochResetsOnlyLowerOvercharmAttempts()
    {
        var f=New(1,4);for(int i=0;i<4;i++) Confirm(f);
        f.tab.cur=1;f.TouchStep();f.tab.cur=2;f.TouchStep();
        for(int i=0;i<4;i++) { Confirm(f);NoWrites(f,0);Assert.False(PlayerData.instance.canOvercharm); }
        Confirm(f);Equipped(f,1,4,true);
    }

    static (HKDualScreen owner,NativeCharmFeedbackInputs native) NewFeedback(int cost=4,Action<NativeCharmFeedbackInputs> setup=null)
    {
        Time.frameCount++;Time.unscaledTime=1;BossSequenceController.BoundCharms=false;
        PlayMakerFSM.Broadcasts.Clear();PlayMakerFSM.ThrowBroadcast=null;HKDualScreen.GameInventoryOpen=false;
        HKDualScreen f=new();f.cfg.compTab=2;f.tab.cur=f.tab.tap=f.tab.built=2;
        CharmIconList.Instance=new(){spriteList=Enumerable.Range(0,41).Select(_=>new Sprite()).ToArray()};
        var pane=f.NewNativePane(true);var native=f.InstallNativeFeedback(pane);setup?.Invoke(native);
        f.attrCam.enabled=true;f.attrCam.cullingMask=1<<HKDualScreen.ATTR_LAYER;
        SetupLayer(pane.transform);f.NativeLayoutRouteStep(2);
        var pd=PlayerData.instance;pd.charmSlots=3;pd.charmSlotsFilled=0;pd.overcharmed=false;
        for(int i=1;i<=40;i++) { pd.Ints["charmCost_"+i]=1;HKDualScreen.NativeLabels["UI/CHARM_NAME_"+i]="Charm "+i; }
        pd.Ints["charmCost_1"]=cost;f.SetEquipState();Choose(f,1);
        Assert.True(f.SelectedItem.GetComponent<Renderer>().enabled);
        Assert.True(f.attrCam.enabled);Assert.NotEqual(0,f.attrCam.cullingMask&(1<<HKDualScreen.ATTR_LAYER));
        return (f,native);
    }
    [Fact] public void Correction33_OverflowStagesPlayExactTypedAudioAndVibrationsOnce()
    {
        var (f,n)=NewFeedback();f.EquipBuildStep();
        var expectedAudio=new[]{("sword_hit_reject",1f,1f),("sword_hit_reject",1f,1f),
            ("dream_damage",1f,1f),("sword_hit_window_1",1f,1f),
            ("dream_damage",1.15f,1f),("sword_hit_window_1",1f,1f),("mage_lord_glass_floor_break",1f,1f)};
        var stages=new[]{"Tink","Tink","Crack1","Crack2"};
        var clipsPerAttempt=new[]{1,2,4,6};
        for(int i=0;i<4;i++)
        {
            Confirm(f);NoWrites(f,0);Assert.False(PlayerData.instance.canOvercharm);
            Assert.Empty(PlayerData.instance.equippedCharms);Assert.Equal(stages[i],f.ActionOutcome);
            Assert.Equal(expectedAudio.Take(clipsPerAttempt[i]),n.Audio);
            Assert.Equal(i+1,n.Vibration.Calls.Count);
        }
        Confirm(f);Equipped(f,1,4,true);Assert.True(PlayerData.instance.canOvercharm);
        Assert.Equal("Applied",f.ActionOutcome);Assert.Equal(expectedAudio,n.Audio);
        Assert.Equal(new[]{"PlayFailedPlace","PlayFailedPlace","PlayOvercharmHit","PlayOvercharmHit","PlayOvercharmFinalHit"},n.Vibration.Calls);
        Assert.Equal(7,n.Spawns);Assert.All(n.SpawnCalls,c=>Assert.Same(n.Prefab,c.prefab));
        Assert.False(n.Fsm.enabled);Assert.False(n.Vibration.enabled);
        f.TouchStep();f.TouchStep();Assert.Equal(7,n.Spawns);Assert.Equal(5,n.Vibration.Calls.Count);
    }
    [Theory] [InlineData("audio")] [InlineData("spawn")] [InlineData("vibration")]
    public void Correction33_FailingNativeFeedbackIsHonestAndNeverReplayed(string failure)
    {
        var (f,n)=NewFeedback();
        n.ThrowAudio=failure=="audio" ? "sword_hit_reject" : null;n.ThrowSpawn=failure=="spawn";n.Vibration.Throw=failure=="vibration";
        Confirm(f);Assert.Equal("FeedbackFailed",f.ActionOutcome);NoWrites(f,0);
        Assert.Equal(1,n.Spawns);Assert.Single(n.Vibration.Calls);
        f.TouchStep();f.TouchStep();Assert.Equal(1,n.Spawns);Assert.Single(n.Vibration.Calls);NoWrites(f,0);
    }
    [Fact] public void Correction33_CrackAudioFailureStillAttemptsBothClipsAndVibrationOnce()
    {
        var (f,n)=NewFeedback();Confirm(f);Confirm(f);n.ThrowAudio="dream_damage";Confirm(f);
        Assert.Equal("FeedbackFailed",f.ActionOutcome);NoWrites(f,0);
        Assert.Equal(new[]{("dream_damage",1f,1f),("sword_hit_window_1",1f,1f)},n.Audio.Skip(2));
        Assert.Equal(4,n.Spawns);Assert.Equal(3,n.Vibration.Calls.Count);
        f.TouchStep();Assert.Equal(4,n.Spawns);Assert.Equal(3,n.Vibration.Calls.Count);
    }
    [Theory] [InlineData("pitch")] [InlineData("clip")] [InlineData("lifecycle")]
    public void Correction33_UnavailableTypedBindingIsBoundedAndNeverRediscovered(string missing)
    {
        var (f,n)=NewFeedback(setup:input=>{
            if(missing=="pitch") ((HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle)input.Fsm.FsmStates[2].Actions[0]).pitchMin.Value=1;
            if(missing=="clip") input.Clips[0].name="not-the-native-tink";
            if(missing=="lifecycle") input.Prefab.GetComponent<PlayAudioAndRecycle>().audioSource=null;
        });
        Confirm(f);Assert.Equal("FeedbackUnavailable",f.ActionOutcome);NoWrites(f,0);
        int hierarchy=DiscoveryCounters.Hierarchy,resources=Resources.Discoveries;
        for(int i=0;i<30;i++) f.TouchStep();
        Assert.Equal(hierarchy,DiscoveryCounters.Hierarchy);Assert.Equal(resources,Resources.Discoveries);
        Assert.Equal(0,n.Spawns);Assert.Empty(n.Audio);Assert.Empty(n.Vibration.Calls);
    }
    [Fact] public void Correction33_FifthFeedbackFailureRetainsMutationAndCompletionEffects()
    {
        var (f,n)=NewFeedback();f.EquipBuildStep();for(int i=0;i<4;i++) Confirm(f);
        n.Vibration.Throw=true;Confirm(f);Equipped(f,1,4,true);
        Assert.True(PlayerData.instance.canOvercharm);Assert.Equal("AppliedFeedbackFailed",f.ActionOutcome);
        Assert.Equal(7,n.Spawns);Assert.Equal(5,n.Vibration.Calls.Count);
        f.TouchStep();f.TouchStep();Assert.Equal(1,f.Manager.Equips);Assert.Equal(7,n.Spawns);Assert.Equal(5,n.Vibration.Calls.Count);
    }
    [Theory] [InlineData("drag")] [InlineData("pause")] [InlineData("transport")]
    public void Correction33_CancelledNativeFeedbackReleaseProducesNoNativeEffects(string boundary)
    {
        var (f,n)=NewFeedback();var p=ActionPoint(f);Down(f,p.x,p.y);
        if(boundary=="drag") Feed(f,Contact(p.x+.04f,p.y));
        if(boundary=="pause") { f.Step(true);f.Step(false); }
        if(boundary=="transport") f.transport=new(){Owner=f};
        Up(f);f.TouchStep();NoWrites(f,0);Assert.Equal(0,n.Spawns);Assert.Empty(n.Vibration.Calls);
    }
    [Theory] [InlineData("invalidFit")] [InlineData("detailText")] [InlineData("rowSprite")]
    public void Correction33_ActualOwningPresentationFailureReportsAppliedRefreshFailed(string failure)
    {
        var f=New();f.EquipBuildStep();f.EquipUpdateStep();Assert.False(f.CachedCharm(1));
        f.Manager.OnMutation=()=>{
            if(failure=="invalidFit") f.LoseNativeCharmNameDonor();
            if(failure=="detailText") f.ThrowDetailText="Text Name";
            if(failure=="rowSprite") CharmIconList.Instance.spriteList[1]=null;
        };
        Confirm(f);Assert.Equal("AppliedRefreshFailed",f.ActionOutcome);Equipped(f,1,1);
        Assert.True(f.CachedCharm(1));Assert.Equal(1,f.equipRowN);
        if(failure!="rowSprite") Assert.True(f.equipCharmSRs[0].enabled);
        else Assert.False(f.equipCharmSRs[0].enabled);
        int detailAttempts=f.DetailTextAttempts,reads=CharmIconList.SpriteReads,writes=PlayerData.instance.Writes;
        f.TouchStep();f.TouchStep();Assert.Equal(detailAttempts,f.DetailTextAttempts);Assert.Equal(reads,CharmIconList.SpriteReads);
        Assert.Equal(writes,PlayerData.instance.Writes);Assert.Equal(1,f.Manager.Equips);Assert.Equal(1,f.Manager.hero_ctrl.Updates);
    }
    [Fact] public void Correction33_HealthyOwningPresentationSuccessRefreshesExistingClone()
    {
        var f=New();f.EquipBuildStep();var pane=f.paneClone;var grid=f.nativeCharmGrid;
        Confirm(f);Assert.Equal("Applied",f.ActionOutcome);Equipped(f,1,1);
        Assert.Same(pane,f.paneClone);Assert.Same(grid,f.nativeCharmGrid);Assert.True(f.equipCharmSRs[0].enabled);
        Assert.Same(CharmIconList.Instance.spriteList[1],f.equipCharmSRs[0].sprite);
    }
    [Fact] public void Correction33_PendingFitCoalescesActualEquipWatchIntoOneCompanionDispatch()
    {
        var f=New();f.SetEquipState(1);f.PendingCharmFit=true;
        int layouts=f.NativeCharmLayouts,oldHash=f.EquipWatchHash;f.NativeEquipWatchStep();
        Assert.Equal(layouts,f.NativeCharmLayouts);Assert.NotEqual(oldHash,f.EquipWatchHash);Assert.True(f.PendingCharmFit);
        f.NativeCompanionStep();Assert.Equal(layouts+1,f.NativeCharmLayouts);Assert.False(f.PendingCharmFit);
        f.NativeEquipWatchStep();Assert.Equal(layouts+1,f.NativeCharmLayouts);
    }
    [Fact] public void Correction33_NoPendingFitRetainsImmediateExternalEquipmentDarkening()
    {
        var f=New();f.PendingCharmFit=false;f.SetEquipState(1);int layouts=f.NativeCharmLayouts;
        var icon=(f.SelectedItem.GetComponent("InvCharmBackboard") as InvCharmBackboard).charmObject.GetComponent<SpriteRenderer>();
        f.NativeEquipWatchStep();Assert.Equal(layouts+1,f.NativeCharmLayouts);Assert.False(f.PendingCharmFit);
        Assert.Equal(f.cfg.compCharmsDimEquip,icon.color.r);
        f.NativeEquipWatchStep();Assert.Equal(layouts+1,f.NativeCharmLayouts);
    }
}
