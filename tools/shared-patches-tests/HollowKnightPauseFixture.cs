#pragma warning disable CS0649, CS0414 // Unrelated engine/config inputs intentionally stay at their native defaults.
using System;
using System.Collections.Generic;

// Only unrelated engine calls are stand-ins. The generator compiles Tick, HUD routing,
// role masks, backdrop policy and LogoTick verbatim from the shipped sources.
namespace HkPauseContracts;

internal partial class HKDualScreen
{
    internal const int HUD_LAYER = 6, ATTR_LAYER = 3, TUT_LAYER = 7, UI_LAYER = 5;
    internal int hudLayer = HUD_LAYER, tutLayer = TUT_LAYER;
    internal HKLayout cfg = new();
    internal readonly GameCameras Cameras = new();
    internal readonly GameManager Manager = new();
    internal Camera hudCam2 = new(), attrCam = new(), promptCam = new(), clearCam = new(), bgCaptureCam = new();
    internal readonly Transport transport = new();
    internal readonly Dimmer bgDimmer = new();
    internal bool directDisplayActive = true, dsWas = true, bgShow, popupBlack;
    internal readonly GameObject logoGo = new();
    internal bool logoNeedsBake, fleurBaked = true, wired = true, wasAtMenu, creditNow, loreDialogueOpen;
    internal bool HudFaded, Popup, InventoryOpen, Credit;
    internal int compOn = -1, lastSkinStamp, bgCullMask = 1, BOTTOM_W = 1240, BOTTOM_H = 1080;
    internal int hudLayerApplied = -1;
    internal Transform hudRootApplied;
    internal readonly Tab tab = new();
    internal GameObject mapClone = new(), invCloneCache = new(), charmCloneCache = new(), journalCloneCache = new(), guideCloneCache = new(), frameRoot = new();
    UnityEngine.Object supplementarySource;
    PlayerData supplementaryPlayer;
    UnityEngine.Object supplementaryMapSource;
    internal GameObject paneClone, slideOutClone;
    internal float slideT = 1f; internal int slideDir;
    internal Vector3 slideStartCamPos; internal bool slideCamValid;
    internal float slideStartOrtho=8;
    internal Vector3 slideStartPanePos,slideNormalLocalPos,slideNormalLocalScale=Vector3.one;
    internal void StowStep() => StowSlideClone();
    internal void ConfigReloadStep() => OnConfigReloaded();
    internal int EquipmentReassertions;
    bool equipGridDirty; int frameBuildHash;
    bool ctrlActive; int ctrlLayoutPending,ctrlLayoutWait;
    void ReassertEquipment(GameObject go) { EquipmentReassertions++;equipGridDirty=false; }
    internal void EquipBuildStep() => BuildEquipCharmRow();
    internal Transform equipRowRoot; internal readonly List<SpriteRenderer> equipCharmSRs=new();
    internal NativePaneLabel shellTitle;
    Transform areaNameT,noMapT; Component areaNameTmp; Renderer areaNameR;
    int shellTitleTab=-1,shellTitleLanguage=-1;
    float shellTitleWidth,shellTitleHeight,frameRefOrtho=8;
    string lastAreaZoneRaw="\u0001",lastAreaName="\u0001";bool mapWorldMode;
    string ZoneName(string zone) => zone;
    internal void HeaderStep(int id)
    {
        shellTitle=new NativePaneLabel();shellTitle.Container=new TextContainer();shellTitle.Tmp.Container=shellTitle.Container;
        TcSetSize(shellTitle.Container,new Vector2(1,1)); areaNameT=shellTitle.Root;areaNameTmp=shellTitle.Tmp;areaNameR=shellTitle.Renderer;
        PositionHeaderFixture(attrCam.orthographicSize,attrCam.aspect,1,id);
    }
    internal Transform TextDonor(Vector3 scale,bool auto)
    {
        var label=new NativePaneLabel(); label.Root.localScale=scale;label.Tmp.enableAutoSizing=auto;
        label.Renderer.BoundsSize=new Vector3(20,200,1);
        return label.Root;
    }
    internal NativePaneLabel CopyLabelStep(Transform donor,float pixels) => CopyPaneLabel(donor,compRoot,"copy",pixels);
    internal void LabelStep(NativePaneLabel label,string text,Rect rect) => SetPaneLabel(label,text,rect,ShellInk);
    static System.Reflection.PropertyInfo _tmpTextBoundsPI;
    internal readonly Transform compRoot = new GameObject().transform;
    internal void SlideStep() => TabSlideTick();
    internal readonly HKLowerLayout.Retry iconRetry = new();
    readonly HKLowerLayout.Retry frameRetry = new(),noMapRetry=new();
    bool noMapReady,shellTitleToast;string shellToastText;
    int headerSortUntil=-1,headerSortFrame=-1;
    Bounds shellTitleInk;
    SpriteRenderer noMapSymbol;
    readonly Sprite[] tabIcons = new Sprite[5];
    readonly SpriteRenderer[] frameTabs = new SpriteRenderer[5];
    internal readonly PaneRun invRun = new(), charmRun = new();
    internal sealed class PaneRun { internal bool finalized = true; }
    internal GameObject Route(int id) => CloneForTab(id);
    internal bool Ready(int id) => ReadyForTab(id);
    internal HKLowerLayout.Geometry LowerGeometry() => HKLowerLayout.Measure(BOTTOM_W, BOTTOM_H);
    internal void InitializeOwnership() { supplementarySource = Manager.inventoryFSM; supplementaryPlayer = PlayerData.instance; supplementaryMapSource = Manager.gameMap; }
    internal readonly List<int> routedLayers = new();
    internal int Teardowns, Updates, Prewarms, FrameTeardowns, CaptureSetups;
    internal int TouchPolls => transport.Polls;
    const int COMP_MAP = 0, COMP_INV = 1, COMP_CHARM = 2, COMP_JOURNAL = 3, COMP_GUIDE = 4;
    internal struct FitResult { internal Vector3 center; internal float ortho; internal bool valid; }
    internal FitResult fit;
    internal bool mapFitIsArea, mapAvailable;
    internal float mapUserZoom = 1, mapInnerYc;
    internal Vector2 mapUserPan;
    internal void FramePane(Vector3 center) => ApplyCompanionCamera(center);
    internal readonly GameObject Health = new(), Soul = new(), Geo = new(), Frame = new(), Controls = new(), Heal = new();

    internal HKDualScreen()
    {
        var mask=new GameObject(); mapMaskTopT=mask.transform;
        mask.AddComponent<MeshFilter>().sharedMesh=new object();
        mapMaskTopR=mask.AddComponent<MeshRenderer>();mapMaskTopR.sharedMaterial=new object();
        transport.Owner=this;
        cfg.compTab=1;
        GameManager.instance = Manager;Manager.FixtureOwner=this; PlayerData.instance = new(); InitializeOwnership();
        var donor = TextDonor(Vector3.one,false);donor.gameObject.name="Pane Name";donor.SetParent(Manager.inventoryFSM.transform);
        HkStageHooks.SkinStamp = 0;
        GameObject anchor = new();
        GameObject middle = new();
        middle.transform.SetParent(anchor.transform);
        Cameras.hudCanvas.transform.SetParent(middle.transform);
        foreach (GameObject go in new[] { Health, Soul, Geo }) go.transform.SetParent(Cameras.hudCanvas.transform);
        foreach (GameObject go in new[] { mapClone, invCloneCache, charmCloneCache, journalCloneCache, guideCloneCache, frameRoot, Frame, Controls })
            go.layer = ATTR_LAYER;
        foreach(var cache in new[]{mapClone,invCloneCache,charmCloneCache,journalCloneCache,guideCloneCache}) cache.transform.SetParent(compRoot);
        WireGraphics(journalGraphics,journalCloneCache.transform); WireGraphics(guideGraphics,guideCloneCache.transform);
        foreach(var label in new[]{journalName,journalDescription,journalNotes,journalState}) label.Root.SetParent(journalCloneCache.transform);
        journalPortrait.transform.SetParent(journalCloneCache.transform);
        guideDetail.Root.SetParent(guideCloneCache.transform); guideState.Root.SetParent(guideCloneCache.transform);
        Heal.layer = TUT_LAYER;
        logoGo.layer = HUD_LAYER;
        promptCam.cullingMask = 1 << TUT_LAYER;
    }

    static void WireGraphics(PaneGraphics graphics,Transform owner)
    {
        foreach(var r in new Renderer[]{graphics.RuleLeft,graphics.RuleRight,graphics.TL,graphics.BR,graphics.Glow,graphics.Top,graphics.Bottom,graphics.Left,graphics.Right})
        { r.transform.SetParent(owner);r.transform.gameObject.layer=ATTR_LAYER; }
    }
    internal void Step(bool paused, bool inventory = false)
    {
        Manager.gameState = paused ? GlobalEnums.GameState.PAUSED : GlobalEnums.GameState.PLAYING;
        InventoryOpen = inventory;
        Time.frameCount++;
        Tick();
    }

    internal bool Drawn(GameObject go) => go != null && go.activeInHierarchy &&
        ((hudCam2.enabled && (hudCam2.cullingMask & (1 << go.layer)) != 0) ||
         (attrCam.enabled && (attrCam.cullingMask & (1 << go.layer)) != 0) ||
         (promptCam.enabled && (promptCam.cullingMask & (1 << go.layer)) != 0));

    bool TryResolveSceneManagers(out GameCameras gc, out GameManager gm) { gc = Cameras; gm = Manager; return true; }
    bool TryRunLowerHudFixture(GameCameras gc, GameManager gm) => false;
    // Filesystem/JsonUtility are unrelated engine boundaries in the pause fixture.
    // File-reload cases opt in to the separately extracted production LoadConfig.
    internal HKConfigFixture ConfigOwner;
    void LoadConfig(bool force) { if(ConfigOwner!=null) { ConfigOwner.Read(force); cfg=ConfigOwner.cfg; } }
    void SyncDumpHook() { }
    void PushInputSettings() { }
    bool PollInventoryToggle(GameCameras gc, bool paused) => InventoryOpen;
    internal static bool GameInventoryOpen;
    void PollCompanionCombo() { }
    void StripPrivateLayers() { }
    bool HudFadedInGameplay(GameCameras gc) => HudFaded;
    void TeardownCompanion() { Teardowns++;TeardownCompanionBody(); }
    internal readonly List<GameObject> Destroyed = new();
    void Destroy(GameObject go) { AssertSingleDestroy(go); Destroyed.Add(go); go.SetActive(false); }
    void AssertSingleDestroy(GameObject go) { if (Destroyed.Contains(go)) throw new InvalidOperationException("duplicate owned destruction"); }
    internal string ResolveGuide(Transform root,Transform row,int index) => GuideRowCondition(root,row,index);
    internal static string[] NativeGuideConditions => GuideConditions;
    internal static string[] NativeGuideStates => GuideStates;
    internal static string[] NativeGuideVariables => GuideVariables;
    internal int BuildAttempts, BuildWarnings,ShellWarnings;
    internal bool ThrowPartialBuild;
    internal GameObject BuildStep(int id) => BuildSupplementaryPane(id);
    GameObject BuildJournalPane() => PartialBuild(3,journalRetry);
    GameObject BuildGuidePane() => PartialBuild(4,guideRetry);
    GameObject PartialBuild(int id,HKLowerLayout.Retry retry)
    {
        if(!retry.Due(Time.frameCount)) return null;
        BuildAttempts++;
        var go=new GameObject();
        if(id==3) journalCloneCache=go; else guideCloneCache=go;
        if(ThrowPartialBuild) throw new InvalidOperationException("injected after cache assignment, before owned visual/data completion");
        retry.Resolved(); return go;
    }
    void WarnOnce(string key, Exception error)
    {
        if(key=="read-only pane build") { BuildWarnings++; return; }
        if(key=="frame build" || key=="map action build") { ShellWarnings++; return; }
        throw error;
    }
    internal readonly HKLowerLayout.Retry journalRetry = new(), guideRetry = new();
    internal readonly List<JournalRecord> journalRecords = new();
    internal readonly List<int> journalVisible = new(), guideVisible = new();
    readonly List<SpriteRenderer> journalCells = new();
    internal readonly List<GuideRecord> guideRecords = new();
    Func<string,bool> guideRead; PlayerData guideReadPlayer;
    internal sealed class JournalRecord { internal Sprite Sprite=new(); internal string KilledKey="killedCrawler", KillsKey="killsCrawler",NameKey="NAME_CRAWLER",DescKey="DESC_CRAWLER",NotesKey="NOTE_CRAWLER"; internal bool Killed; internal int Remaining; }
    internal sealed class GuideRecord { internal SpriteRenderer Icon=new(); internal NativePaneLabel Label=new(); internal string Condition="hasPinBench", Key="NAME", Sheet="Map", Text; internal bool Visible; }
    internal int JournalLayouts, GuideLayouts;
    void LayoutJournal() { JournalLayouts++; LayoutJournalBody(); }
    void LayoutGuide() { GuideLayouts++; LayoutGuideBody(); }
    internal void LayoutStep(int id) { if(id==3) LayoutJournal(); else LayoutGuide(); }
    static string LocalizedLabel(string fallback,string key) => NativeText(key,"UI");
    internal static readonly Dictionary<string,string> NativeLabels=new();
    static string NativeText(string key,string sheet) => string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sheet) ? "" : NativeLabels.TryGetValue(sheet+"/"+key,out var text) ? text : key;
    internal sealed class NativePaneLabel
    {
        internal Transform Root=new GameObject().transform; internal Component Tmp; internal Component Container;
        internal Renderer Renderer; internal Renderer[] ClipRenderers;
        internal readonly MaterialPropertyBlock ClipBlock=new();
        internal float UnitScale=1; internal string Text="\u0001"; internal Rect ClipRect,TextRect; internal float ScrollOffset,ScrollMax;
        internal NativePaneLabel() { Renderer=new Renderer(Root);Tmp=new Component(Renderer);ClipRenderers=new[]{Renderer,new Renderer(Root)};Root.Renderers.AddRange(ClipRenderers);Root.TextComponents.Add(Tmp); }
    }
    internal void BindJournalStep(JournalRecord record) => BindJournalLabels(record,new Rect(20,256,380,668),new Rect(890,256,330,56),new Rect(890,322,330,320),new Rect(890,630,330,294));
    static System.Reflection.PropertyInfo TmpProp(Component tmp,string name) => tmp.GetType().GetProperty(name);
    static void SetTmpColor(Component tmp,Color color) { tmp.color=color; }
    static void TcSetSize(Component container,Vector2 size) { container.size=size; }
    const int TMP_CLIP_RECT=1;
    internal static readonly Color ShellInk=new(.93f,.91f,.86f,1),ShellMuted=new(.62f,.60f,.58f,1);
    internal sealed class PaneGraphics
    {
        internal SpriteRenderer RuleLeft=new(),RuleRight=new(),TL=new(),BR=new(),Glow=new();
        internal Renderer Top=new(),Bottom=new(),Left=new(),Right=new();
        internal bool Selected; internal int SelectedId=-1; internal Rect Target;
        internal Vector2 From,Center; internal float Travel=1; internal int CursorFrame=-1;
        internal int Language=-1; internal float Width,Height;
    }
    internal PaneGraphics journalGraphics=new(),guideGraphics=new();
    internal SpriteRenderer shellRule=new(),tabTL=new(),tabBR=new(),tabGlow=new();
    internal Transform mapMaskTopT; internal Renderer mapMaskTopR;
    internal bool GraphicsBuild(int id) => BuildPaneGraphics(CloneForTab(id),id);
    internal void GraphicsStep(int id) => PositionPaneGraphics(id==3 ? journalGraphics : guideGraphics,id);
    internal void SelectionStep(int id,Rect rect,bool show,int selected)
    {
        var graphics=id==3 ? journalGraphics : guideGraphics;
        SetPaneSelection(graphics,rect,show,selected); PaneCursorTick(graphics);
    }
    internal void CursorStep(int id) => PaneCursorTick(id==3 ? journalGraphics : guideGraphics);
    internal void JournalDataStep(bool force) => RefreshJournal(force);
    internal void GuideDataStep(bool force) => RefreshGuide(force);
    internal SpriteRenderer journalPortrait=new();
    internal NativePaneLabel guideDetail=new(),guideState=new();
    TMProOld.TMP_FontAsset shellCapsFont;
    internal NativePaneLabel journalName=new(),journalDescription=new(),journalNotes=new(),journalState=new();
    const float SelectionMoveSeconds=.15f;
    internal int journalSelected=-1,guideSelected=-1,journalScrollRow,guideScrollRow;
    int supplementaryNextRefresh,supplementaryLanguage;
    float supplementaryWidth, supplementaryHeight;
    internal bool journalHadBook,journalFilled;
    bool supplementaryDragValid;
    internal bool directDisplayRestorePending;
    GameCameras resolvedGameCameras => Cameras;
    internal int RouteRestores,InputReleases,NameRestores,DialogueRestores;
    internal void DisplayStep(bool active) => SetDirectDisplayActive(active);
    internal void LoseDisplay() => SetDirectDisplayActive(false);
    internal void MenuStep() { Manager.gameState=GlobalEnums.GameState.MAIN_MENU; Time.frameCount++; Tick(); }
    void ReleaseLowerHudFixtureInputLockOrThrow() => InputReleases++;
    void RestoreNameCardOrThrow() => NameRestores++;
    void RestoreDialogueShapeOrThrow() => DialogueRestores++;
    void RestoreRoutedLayers() { RouteRestores++; routedLayers.Clear(); }
    void RestoreNameCard() { }
    void PublishLegacyLifebloodFlashMode() { }
    void FixWipedTilemap() { }
    internal GameObject RoutedHudPrompt;
    void MainGameHooks(GameCameras gc)
    {
        // Model the existing routing hook's effect, not any pause/mask policy.
        if (RoutedHudPrompt != null) SetLayerRecursive(RoutedHudPrompt.transform, tutLayer);
    }
    bool CreditShowing() => Credit;
    bool BottomOverlayActive() => Popup;
    void SetupBgCapture(GameCameras gc) => CaptureSetups++;
    void ApplyMainFocusLift() { }
    void FixEmptyCounterDetail() { }
    void InvStateDiag() { }
    void DebugPopupTick() { }
    void SyncBottomFade() { }
    void PrewarmTick() => Prewarms++;
    void EnsureCompRoot() => Updates++;
    Selection sel=new(){kind=-1};
    int mapStowStamp,compFrameTick; internal bool mapAnyAvailable; bool paneNeedsFit;
    internal GameMap mapGm; internal bool mapContentVisible,mapNeedsSetup;
    internal int AnimatedResets,MarkerWrites;
    internal void BuildActionsStep() => BuildMapControls(Manager.inventoryFSM.transform.root);
    internal void PositionActionsStep() => PositionMapControls(attrCam.orthographicSize,attrCam.aspect,.5f,-.5f,tab.cur==0);
    internal bool MapActionTap(Vector3 p) => HandleMapControlTap(p);
    void ResetMapViewAnimated() => AnimatedResets++;
    Renderer mapResetR;
    void ResetMapView() { }
    int mapAreaBTries,lastPinStamp;
    internal int NativePinStamp;
    int PinStamp() => NativePinStamp;
    int lastCleanTapSeq;float resetAnimT=1,pinchLastDist=-1,resetStartZoom;bool dragLastValid;
    Vector2 dragLastN,resetStartPan;
    void BenchPinTap(Vector3 world) { } // Existing native bench-travel owner, not Map presentation.
    readonly List<UnityEngine.Object> frameAssets=new();
    T Own<T>(T asset) where T:UnityEngine.Object { frameAssets.Add(asset);return asset; }
    internal int OwnedAssetCount => frameAssets.Count;
    internal int DestroyedAssetCount;
    void Destroy(UnityEngine.Object asset) { DestroyedAssetCount++; }
    internal Sprite DecodeInvalidMapArt() => DecodeMapArt("bm90IGEgcG5n","invalid",0);
    float benchToastUntil;string benchToastText;
    void UpdateNotchRow(float s,float asp,int id) { }
    void UpdateEquipCharmRow(float s,float asp) { }
    void HideControlPrompt(GameObject go) { }
    void BuildCompanionTab(int id) { paneClone=id==0 ? null : (id==3 || id==4) ? BuildSupplementaryPane(id) : CloneForTab(id); }
    int MapContentStamp() => 0;
    void MapTick() { }
    Bounds mapAreaB; string lastMapZone="CROSSROADS";
    internal int MapMeasurements;
    // Native area/world measurement ABI supplies authored clone-local extents.
    bool TryAreaBounds(GameMap m,string zone,out Bounds b) { MapMeasurements++;b=mapAreaB;return true; }
    bool TryWorldBounds(GameMap m,out Bounds b) { MapMeasurements++;b=mapAreaB;return true; }
    bool TryMapBounds(GameObject go,out Vector3 center,out float size) { center=default;size=0;return false; }
    internal void PrimeMapFit(Bounds b,bool world=false) { mapAreaB=b;mapWorldMode=world;mapAreaBValid=true;mapAreaBFor=world ? "__WORLD__" : "CROSSROADS";mapAreaBTries=0; }
    internal void MapFitStep() { Vector3 center=default;MapFrameTick(ref center); }
    internal bool MapTouchStep(int count,float x,float y) { transport.T0X=x;transport.T0Y=y;return MapControlTouchTick(count); }
    internal int CleanTapConsumed => lastCleanTapSeq;
    internal void MapPinchStep(int count,float x,float y,float x1=0,float y1=0) { transport.contacts=count;transport.T0X=x;transport.T0Y=y;transport.T1X=x1;transport.T1Y=y1;MapPinchTick(); }
    PaneRun RunFor(bool charms) => charms ? charmRun : invRun;
    void PaneSettleTick(GameObject go,PaneRun run,bool inventory,bool active) { }
    void CharmsTick() { }
    void RefreshInvCounters(GameObject go) { }
    void ApplyFit(FitResult value) { if(value.valid) fit=value; }
    internal void FitOccupiedStep(Transform root,Renderer[] renderers,Rect target) => FitOccupiedNative(root,renderers,target);
    internal int NativeInventoryLayouts,NativeCharmLayouts;
    internal void NativeLayoutRouteStep(int id) { tab.tap=id;tab.cur=id;tab.built=id;paneClone=CloneForTab(id);paneNeedsFit=true;UpdateCompanion(Cameras.hudCamera); }
    internal PaneRefs refsInv,refsCharm;
    internal NativePaneLabel nativeCharmName,nativeCharmDesc;internal PaneGraphics nativeCharmGraphics;
    void LayoutNativeInventory(GameObject pane) { NativeInventoryLayouts++;LayoutNativeInventoryBody(pane); }
    FitResult LayoutCharmsRedesign(GameObject go) { NativeCharmLayouts++;return LayoutCharmsRedesignBody(go); }
    bool TryPaneBounds(GameObject go,out Vector3 center,out float size,bool charms) { center=default;size=0;return false; }
    internal void SupplementaryStep() => SupplementaryTick();
    void BuildFrame() => EnsureNativeShellAssets();
    internal int NoMapBuilds;
    internal void FrameBuildStep() => BuildFrameBody();
    internal SpriteRenderer NativeTab(int col) => frameTabs[col];
    internal SpriteRenderer NativeTabBr => tabBR;
    internal void ClearShellCursorDonors() { tabTL=tabBR=tabGlow=null; }
    internal NativePaneLabel MarkerCount(int id) => mapStripCounts[id];
    internal void MissingMarkerCount(int id) { mapStripCounts[id]=null; }
    internal void BuildHeaderStep() => BuildAreaName(Manager.inventoryFSM.transform.root);
    internal void HeaderTickStep(int id) => PositionHeaderFixture(attrCam.orthographicSize,attrCam.aspect,1,id);
    internal void SetBenchToast(string text,float until) { benchToastText=text;benchToastUntil=until; }
    internal void BuildNoMapStep() => BuildNoMapLabel(Manager.inventoryFSM.transform.root);
    internal SpriteRenderer NoMapSymbol => noMapT?.GetComponent<SpriteRenderer>();
    internal Renderer MapClipMask(int side) => side==0 ? mapMaskTopR : side==1 ? mapMaskBotR : side==2 ? mapMaskLeftR : mapMaskRightR;
    internal Rect MapBody => MapBodyRect();
    internal Vector3 SliderEndpoints => new(mapZoomX,mapZoomTopY,mapZoomBottomY);
    internal bool WorldMapMode => mapWorldMode;
    internal bool MarkerErasing => mapMarkerErase;
    internal Bounds HeaderGlyphBounds { get { TryTmpGlyphBoundsWorld(shellTitle.Root,out var min,out var max);return new Bounds((min+max)*.5f,max-min); } }
    internal void MissingSliderSprite() => mapZoomTrack.sprite=null;
    internal Sprite SliderSprite => mapZoomTrack.sprite;
    internal void ResolveAllShellDonors() { BuildTabRow(Manager.inventoryFSM.transform.root);iconRetry.Resolved(); }
    internal void ResolveShellStep() => ResolveTabDonors(null);
    internal bool ThrowShellRule;
    Sprite CreateShellRule() { if(ThrowShellRule) throw new InvalidOperationException("injected cold shell failure after root assignment"); return CreateShellRuleBody(); }
    internal void RetryNoMapStep(bool missing=false) { if(missing) noMapT=null; Time.frameCount+=120;ResolveTabDonors(null); }
    static Transform FindDeep(Transform root,string name)
    {
        DiscoveryCounters.Hierarchy++;
        if(root==null) return null;
        if(root.name==name) return root;
        foreach(var child in root.Children) { var found=FindDeep(child,name);if(found!=null) return found; }
        return null;
    }
    void BuildNoMapLabel(Transform root) { NoMapBuilds++;BuildNoMapLabelBody(root); }
    Transform mapMaskBotT; Renderer mapMaskBotR;
    internal bool ThrowMapMask;
    Transform BuildMapMask(string name) { if(ThrowMapMask)throw new InvalidOperationException("injected after header/no-map/control build");return BuildMapMaskBody(name); }
    static bool IsTextMeshProGraphic(Component c) => c != null && c.GetType()==typeof(Component);
    static Component TmpOn(Transform root) => root?.TextComponents.FirstOrDefault(c=>IsTextMeshProGraphic(c));
    void DestroyImmediate(Component c) { c.transform?.TextComponents.Remove(c);if(c is TextContainer && c.transform!=null) foreach(var tmp in c.transform.TextComponents) if(tmp.Container==c) tmp.Container=null; }
    internal bool ThrowNextLabelClone;
    static GameObject Instantiate(GameObject source,Transform parent)
    {
        if(GameManager.instance?.FixtureOwner?.ThrowNextLabelClone == true) { GameManager.instance.FixtureOwner.ThrowNextLabelClone=false;throw new InvalidOperationException("injected native TMP clone failure"); }
        var go=new GameObject();go.transform.SetParent(parent);go.transform.localScale=source.transform.localScale;
        var renderer=go.AddComponent<Renderer>(); var original=TmpOn(source.transform);
        if(original != null)
        {
            var copy=new Component(renderer) { enableAutoSizing=original.enableAutoSizing,InkHeight=original.InkHeight };
            renderer.BoundsSize=original.GetComponent<Renderer>().BoundsSize;
            go.transform.TextComponents.Add(copy);
            var container=new TMProOld.TextContainer(renderer);copy.Container=container;go.transform.TextComponents.Add(container);
            if(source.transform.TextComponents.OfType<GameplayLabelDriver>().Any()) go.transform.TextComponents.Add(new GameplayLabelDriver(renderer));
        }
        return go;
    }
    void PositionFrame() { }
    internal void FramePositionStep() => PositionFrameBody();
    void PositionHudStrip(float s,float asp,float zf,int tab) => PositionHeaderFixture(s,asp,zf,tab);
    float tabFleurMoveFromX,tabFleurMoveX;
    const float TabCaretMoveSeconds=.15f;
    internal SpriteRenderer MapFade => FindDeep(frameRoot.transform,"F_MapEdgeFade")?.GetComponent<SpriteRenderer>();
    internal SpriteRenderer MarkerStripIcon(int id) => FindDeep(frameRoot.transform,"F_MapMarker"+id)?.GetComponent<SpriteRenderer>();
    void ReassertControlPrompt() { }
    internal int TouchPollsRecorded, JournalTaps, GuideTaps, ItemTaps;
    readonly HKLowerLayout.TabGesture lowerTabGesture = new();
    int lastSimTapN, lastTapSeq, lowerCleanTabSeq; bool lowerTouchDownBody;
    internal bool MapMarkerMode { get => mapMarkerMode; set => SetMapMarkerMode(value); }
    float supplementaryDragY; int supplementaryDragRegion;
    internal void TouchStep() { TouchPollsRecorded++; PollTouch(); }
    void JournalTap(float x,float y) { JournalTaps++; JournalTapBody(x,y); }
    void GuideTap(float x,float y) { GuideTaps++; GuideTapBody(x,y); }
    internal void SelectionTap(int id,float x,float y) { if(id==3) JournalTap(x,y);else GuideTap(x,y); }
    void PollItemTap(float x,float y) { ItemTaps++;PollItemTapBody(x,y); }
    void ScrollSupplementary(float delta) => ScrollSupplementaryBody(delta);
    internal void NativeScrollStep(int region,float delta) { supplementaryDragRegion=region;ScrollNativePane(delta); }
    internal void ScrollStep(float delta) => ScrollSupplementary(delta);
    void CenterAttribution() { }
    void ApplyHalo() { }
    void SetupLogo() => throw new InvalidOperationException("unexpected logo rebake");
    void TryBakeTabFleurs() => throw new InvalidOperationException("unexpected fleur rebake");
    void TeardownFrame() { FrameTeardowns++;TeardownFrameBody(); }
    void PushToBottom() { }
    void CenterDialogue() { }
    void CenterTutorial() { }
    internal string LastDiagnostic;
    void Dbg(string text) { LastDiagnostic=text; }
    void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root.Children) SetLayerRecursive(child, layer);
    }
}

internal partial class HKConfigFixture
{
    internal HKLayout cfg=new(); internal int Reloads;
    int cfgTick; long cfgMtime,cfgBadMtime;
    internal void Read(bool force=true) => LoadConfig(force);
    void OnConfigReloaded() => Reloads++;
    static void LogUnknownConfigKeys(string text) { }
}
internal static class Application { internal static string persistentDataPath; }
internal static class JsonUtility
{
    // Bound the Unity JSON ABI to flat serialized public fields, not config policy.
    internal static void FromJsonOverwrite(string text,object target)
    {
        using var json=System.Text.Json.JsonDocument.Parse(text);
        foreach(var prop in json.RootElement.EnumerateObject())
        {
            var field=target.GetType().GetField(prop.Name);
            if(field==null) continue;
            object value=field.FieldType==typeof(int) ? (object)prop.Value.GetInt32() :
                         field.FieldType==typeof(float) ? (object)prop.Value.GetSingle() : prop.Value.GetString();
            field.SetValue(target,value);
        }
    }
}
internal static class Debug { internal static void Log(string text) { } }

internal sealed class Layout
{
    internal int dualScreen = 1, companion = 1, compTab = 1, debug, compPopupBlack = 1, bgMask;
    internal float dim = .5f, bgBlur = 2, panX, panY, zoomMul = 1, creditScale = 1;
    internal int compTouch=1, compTapSelect=1, compSimTapN;
    internal float compSimTapX=-1, compSimTapY;
    internal int compBox, compFrame;
    internal float compBoxX, compBoxY, compBoxW = 1, compBoxH = 1, compZoom = 1, compFrameFit = 1;
    internal float compOffX, compOffY, compMapCenterY, compInvOffX, compCharmOffY;
    internal float compCharmZoom = 1, compPaneZoom = 1, compTabSlideTime = .3f;
}
internal sealed class Tab { internal int tap = 2, cur = 2, built = 2, lastCfg = 1; }
internal sealed class Transport
{
    internal int TargetDisplayIndex = 1;
    internal int contacts, Polls, TapSequence, CleanTapSequence;
    internal float TouchX, TouchY, CleanTapX, CleanTapY, T0Y,T0X,T1X,T1Y;
    internal int TouchCount { get { Polls++; return contacts; } }
    internal HKDualScreen Owner;
    internal int ProductChanges;
    internal void SetProductEnabled(bool requested) { ProductChanges++; Owner.DisplayStep(requested); }
}
internal sealed class Dimmer { internal float Brightness, BlurFactor; }
internal sealed class GameCameras
{
    internal GameObject hudCanvas = new();
    internal readonly Camera hudCamera = new(), mainCamera = new();
}
internal sealed class PlayerData
{
    internal static PlayerData instance;
    internal bool hasJournal;
    internal bool equippedCharm_2,equippedCharm_40,hasDreamGate,mapAllRooms;
    internal string shadeScene,dreamGateScene;
    internal float dreamGateX;
    internal readonly List<string> scenesMapped=new();
    internal bool hasMarker_b=true,hasMarker_r=true,hasMarker_y=true,hasMarker_w=true;
    internal int spareMarkers_b=6,spareMarkers_r=6,spareMarkers_y=6,spareMarkers_w=6;
    internal readonly List<Vector3> placedMarkers_b=new(),placedMarkers_r=new(),placedMarkers_y=new(),placedMarkers_w=new();
    internal readonly Dictionary<string,bool> Bools = new();
    internal readonly Dictionary<string,int> Ints = new();
    internal int Reads;
    internal bool GetBool(string key) { Reads++; return Bools.TryGetValue(key,out var value) && value; }
    internal int GetInt(string key) { Reads++; return Ints.TryGetValue(key,out var value) ? value : 0; }
}
internal sealed class GameManager
{
    internal static GameManager instance;
    internal HKDualScreen FixtureOwner;
    internal dynamic inventoryFSM = new NativeOwner(); internal UnityEngine.Object gameMap = new UnityEngine.Object();
    internal string Zone="";internal string GetCurrentMapZone() => Zone;
    internal string sceneName="";
    internal bool IsInSceneTransition;
    internal object tilemap=new object();
    internal GlobalEnums.GameState gameState = GlobalEnums.GameState.PLAYING;
    internal string MenuState = "GAMEPLAY";
    // Exact HK pause authority: Options changes menu state, not GameState.PAUSED.
    internal bool IsGamePaused() => gameState == GlobalEnums.GameState.PAUSED;
}
internal static class GlobalEnums { internal enum GameState { PLAYING, PAUSED, MAIN_MENU } }
internal static class HkStageHooks
{
    internal static int SkinStamp;
    internal static bool BlackBackground,TweaksAvailable,TweaksMenuVisible;
    internal static void ClearLegacyFlashMode() { }
    internal static void Tick(HKLayout cfg, bool debug) { }
}
internal static class Time { internal static int frameCount; internal static float unscaledDeltaTime = .1f,unscaledTime; }
internal static class Mathf
{
    internal static float Clamp01(float value) => Math.Clamp(value,0,1);
    internal static float Log(float value) => MathF.Log(value);
    internal static float Exp(float value) => MathF.Exp(value);
    internal static float Sqrt(float value) => MathF.Sqrt(value);
    internal static int Max(int a,int b) => Math.Max(a,b);
    internal static float Abs(float value) => Math.Abs(value);
    internal static int FloorToInt(float value) => (int)Math.Floor(value);
    internal static int Clamp(int value,int min,int max) => Math.Clamp(value,min,max);
    internal static float Lerp(float from,float to,float t) => from+(to-from)*t;
    internal static float MoveTowards(float from,float to,float step) => Math.Abs(to-from)<=step ? to : from+Math.Sign(to-from)*step;
    internal static float Max(float a, float b) => Math.Max(a, b);
    internal static int Min(int a,int b) => Math.Min(a,b);
    internal static float Min(float a, float b) => Math.Min(a, b);
    internal static float Pow(float a, float b) => MathF.Pow(a,b);
    internal static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
}
internal struct Rect
{
    internal bool Contains(Vector2 p) => p.x>=x && p.y>=y && p.x<x+width && p.y<y+height;
    internal float x, y, width, height;
    internal Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
}
internal struct Vector2
{
    internal float x, y;
    internal float sqrMagnitude => x*x+y*y;
    public static Vector2 operator -(Vector2 a,Vector2 b) => new(a.x-b.x,a.y-b.y);
    internal static Vector2 zero => default;
    internal static Vector2 Lerp(Vector2 a,Vector2 b,float t) => new(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t);
    public static Vector2 operator +(Vector2 a,Vector2 b) => new(a.x+b.x,a.y+b.y);
    internal Vector2(float x, float y) { this.x = x; this.y = y; }
}
internal struct Quaternion
{
    internal float Angle;
    internal static Quaternion identity => default;
    internal static Quaternion Euler(float x,float y,float z) => new(){Angle=z*MathF.PI/180};
    internal Vector3 Apply(Vector3 p) {float c=MathF.Cos(Angle),s=MathF.Sin(Angle);return new(c*p.x-s*p.y,s*p.x+c*p.y,p.z);}
    internal Vector3 Inverse(Vector3 p) => new Quaternion{Angle=-Angle}.Apply(p);
}
internal enum CameraClearFlags { SolidColor, Depth }
internal struct Color { internal float r,g,b,a;internal static readonly Color black = new(0,0,0,1),white=new(1,1,1,1); internal Color(float r,float g,float b,float a) { this.r=r;this.g=g;this.b=b;this.a=a; } }
internal struct Vector3
{
    internal float sqrMagnitude => x*x+y*y+z*z;
    internal static Vector3 Min(Vector3 a,Vector3 b) => new(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));
    internal static Vector3 Max(Vector3 a,Vector3 b) => new(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));
    internal float x, y, z;
    internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator *(Vector3 a,float scale) => new(a.x*scale,a.y*scale,a.z*scale);
    internal static Vector3 Scale(Vector3 a,Vector3 b) => new(a.x*b.x,a.y*b.y,a.z*b.z);
    internal static Vector3 one => new(1,1,1);
    internal static Vector3 zero => new();
    internal static Vector3 Lerp(Vector3 a,Vector3 b,float t) => a+(b-a)*t;
    internal string ToString(string format) => $"({x.ToString(format)},{y.ToString(format)},{z.ToString(format)})";
}
internal sealed class Camera
{
    internal bool enabled = true, orthographic;
    internal Rect rect;
    internal int cullingMask, targetDisplay;
    internal float aspect = 1, orthographicSize = 8, depth;
    internal object targetTexture;
    internal Color backgroundColor;
    internal CameraClearFlags clearFlags;
    internal readonly Transform transform = new();
    internal void CopyFrom(Camera source) { }
    internal Vector3 ViewportToWorldPoint(Vector3 p) => transform.position+new Vector3((p.x-.5f)*orthographicSize*2*aspect,(p.y-.5f)*orthographicSize*2,p.z);
}
internal sealed class GameObject
{
    internal bool activeSelf = true;
    internal bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
    internal int layer;
    internal readonly Transform transform;
    readonly Dictionary<Type,object> Components=new();
    internal string name;
    internal GameObject(string name=null) { this.name=name;transform = new(this); }
    internal T[] GetComponentsInChildren<T>(bool inactive=true) where T:class => transform.GetComponentsInChildren<T>(inactive);
    internal T[] GetComponents<T>() where T:class => transform.GetComponents<T>();
    internal T AddComponent<T>() where T:new()
    {
        var component=new T(); Components[typeof(T)]=component;
        if(component is Renderer renderer) { renderer.transform=transform;transform.Renderers.Add(renderer); }
        return component;
    }
    internal void SetComponent<T>(T component) where T:class => Components[typeof(T)]=component;
    internal T GetComponent<T>() where T:class => Components.TryGetValue(typeof(T),out var component) ? component as T : Components.Values.OfType<T>().FirstOrDefault() ?? transform.TextComponents.OfType<T>().FirstOrDefault() ?? transform.Renderers.OfType<T>().FirstOrDefault();
    internal Component GetComponent(string name) => transform.TextComponents.FirstOrDefault(c=>c.GetType().Name==name);
    internal T GetComponentInChildren<T>(bool inactive=true) where T:class => transform.GetComponentInChildren<T>(inactive);
    internal void RemoveRenderer() { foreach(var key in Components.Where(p=>p.Value is Renderer).Select(p=>p.Key).ToArray()) Components.Remove(key);transform.Renderers.Clear(); }
    internal void SetActive(bool active) => activeSelf = active;
}
internal sealed class Transform
{
    internal readonly GameObject gameObject;
    internal readonly List<Transform> Children = new();
    internal Transform parent;
    internal Vector3 localPosition,localScale=Vector3.one;
    internal Vector3 position
    {
        get => parent!=null ? parent.TransformPoint(localPosition) : localPosition;
        set => localPosition=parent!=null ? parent.InverseTransformPoint(value) : value;
    }
    internal Vector3 lossyScale { get { var p=parent!=null ? parent.lossyScale : Vector3.one;return new Vector3(localScale.x*p.x,localScale.y*p.y,localScale.z*p.z); } }
    internal Vector3 TransformVector(Vector3 p) {var q=localRotation.Apply(new(p.x*localScale.x,p.y*localScale.y,p.z*localScale.z));return parent!=null ? parent.TransformVector(q) : q;}
    internal Vector3 TransformPoint(Vector3 p) {var q=localRotation.Apply(new(p.x*localScale.x,p.y*localScale.y,p.z*localScale.z))+localPosition;return parent!=null ? parent.TransformPoint(q) : q;}
    internal Transform root => parent!=null ? parent.root : this;
    internal Vector3 InverseTransformPoint(Vector3 point) {var q=localRotation.Inverse((parent!=null ? parent.InverseTransformPoint(point) : point)-localPosition);return new(q.x/localScale.x,q.y/localScale.y,q.z/localScale.z);}
    internal Quaternion rotation,localRotation;
    internal readonly List<Renderer> Renderers=new();
    internal T[] GetComponentsInChildren<T>(bool inactive=true) where T:class { DiscoveryCounters.Hierarchy++;return GetComponents<T>().Concat(Children.Where(c=>inactive || c.gameObject.activeInHierarchy).SelectMany(c=>c.GetComponentsInChildren<T>(inactive))).ToArray(); }
    internal Transform(GameObject go = null) { gameObject = go; }
    internal readonly List<PlayMakerFSM> FsMs=new();
    internal readonly List<Component> TextComponents=new();
    internal T[] GetComponents<T>() where T:class => (typeof(T)==typeof(Transform) ? new object[]{this} : Array.Empty<object>()).Concat(FsMs.Cast<object>()).Concat(TextComponents).Concat(Renderers).OfType<T>().ToArray();
    internal T GetComponentInChildren<T>(bool inactive) where T:class => GetComponentsInChildren<T>(inactive).FirstOrDefault();
    internal T GetComponent<T>() where T:class => gameObject.GetComponent<T>();
    internal Component GetComponent(string name) => gameObject.GetComponent(name);
    internal string name => gameObject?.name;
    internal int childCount => Children.Count;
    internal Transform GetChild(int index) => Children[index];
    internal Transform Find(string name) => Children.FirstOrDefault(c=>c.name==name);
    internal bool IsChildOf(Transform owner) { for(var t=parent;t!=null;t=t.parent) if(t==owner) return true;return false; }
    internal void SetParent(Transform value,bool worldPositionStays=false)
    {
        var p=position;var s=lossyScale;
        parent?.Children.Remove(this);parent=value;value?.Children.Add(this);
        if(worldPositionStays) { position=p;var ps=value?.lossyScale ?? Vector3.one;localScale=new(s.x/ps.x,s.y/ps.y,s.z/ps.z); }
    }
}
internal sealed class PlayMakerFSM
{
    internal bool enabled=true;
    internal string FsmName="Control";
    internal readonly FsmVariables FsmVariables=new();
    internal FsmState[] FsmStates=Array.Empty<FsmState>();
}
internal sealed class FsmVariables
{
    internal readonly Dictionary<string,FsmGameObject> Objects=new();
    internal FsmGameObject FindFsmGameObject(string key) => Objects.TryGetValue(key,out var value) ? value : null;
}
internal sealed class FsmGameObject { internal GameObject Value; }
internal sealed class FsmState { internal string Name; internal object[] Actions=Array.Empty<object>(); }
internal sealed class PlayerDataBoolTest { internal FsmString boolName; internal FsmEvent isFalse; }
internal sealed class FsmString { internal string Value; }
internal sealed class FsmEvent { internal string Name; }

// Renderer/TMP ABI stand-ins only. Geometry, binding, masking, clipping and
// cursor decisions are extracted production methods, never repeated here.
internal class Renderer
{
    internal Transform transform;
    internal object sharedMaterial;
    internal GameObject gameObject => transform.gameObject;
    internal bool enabled=true; internal string sortingLayerName="Inventory"; internal int sortingOrder=10000,ClipWrites;
    internal int sortingLayerID { get => SortingLayer.NameToID(sortingLayerName);set => sortingLayerName=SortingLayer.IDToName(value); }
    internal Vector4 Clip;
    public Renderer() : this(null) { }
    internal Renderer(Transform owner) { transform=owner ?? new GameObject().transform; }
    internal Vector3 BoundsSize=Vector3.one;
    internal virtual Bounds LocalBounds => new(Vector3.zero,BoundsSize);
    internal Bounds bounds { get {var b=LocalBounds;var lo=new Vector3(float.PositiveInfinity,float.PositiveInfinity,0);var hi=new Vector3(float.NegativeInfinity,float.NegativeInfinity,0);for(int i=0;i<4;i++){var p=transform.TransformVector(new(i%2==0 ? b.min.x : b.max.x,i<2 ? b.min.y : b.max.y,0));lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}return new Bounds(transform.position+(lo+hi)*.5f,hi-lo);} }
    internal T GetComponent<T>() where T:class => gameObject.GetComponent<T>();
    internal void GetPropertyBlock(MaterialPropertyBlock block) => block.Value=Clip;
    internal void SetPropertyBlock(MaterialPropertyBlock block) { Clip=block.Value;ClipWrites++; }
    internal bool InClip(Vector2 p) => p.x>=Clip.x && p.y>=Clip.y && p.x<=Clip.z && p.y<=Clip.w;
}
internal sealed class LineRenderer:Renderer
{
    internal bool useWorldSpace;internal int positionCount,numCapVertices;
    internal Color startColor,endColor;internal float startWidth,endWidth;internal Material material;
    internal readonly Vector3[] points=new Vector3[2];internal void SetPosition(int index,Vector3 point) => points[index]=point;
}
internal sealed class Shader { internal static Shader Find(string name) => new(); }
internal sealed class Material:UnityEngine.Object { internal Color color;internal Material(Shader shader) { } }
internal sealed partial class GameMap
{
    internal GameObject areaAncientBasin,areaCity,areaCliffs,areaCrossroads,areaCrystalPeak,areaDeepnest,areaFogCanyon,areaFungalWastes,areaGreenpath,areaKingdomsEdge,areaQueensGardens,areaRestingGrounds,areaDirtmouth,areaWaterways;
    internal GameObject compassIcon,flamePins,dreamerPins,shadeMarker,dreamGateMarker;
    internal GameObject[] mapMarkersBlue=Slots(),mapMarkersRed=Slots(),mapMarkersYellow=Slots(),mapMarkersWhite=Slots();
    internal int Setups; internal bool ThrowSetup;
    internal void SetupMapMarkers() { if(ThrowSetup)throw new InvalidOperationException("native redraw failure");Setups++; }
    static GameObject[] Slots() => Enumerable.Range(0,6).Select(i=>{var go=new GameObject();go.AddComponent<SpriteRenderer>();return go;}).ToArray();
}
internal sealed class Mesh:UnityEngine.Object { internal string name;internal Vector3[] vertices;internal Vector2[] uv;internal Color[] colors;internal int[] triangles;internal void RecalculateBounds() { } }
internal sealed class MeshFilter { public MeshFilter() { } internal object sharedMesh;internal Mesh mesh { get => sharedMesh as Mesh;set => sharedMesh=value; } }
internal sealed class MeshRenderer:Renderer { public MeshRenderer() { } }
internal enum SpriteDrawMode { Simple,Sliced }
internal sealed class SpriteRenderer:Renderer
{
    internal SpriteDrawMode drawMode;internal Vector2 size;internal bool flipX,flipY;
    public SpriteRenderer() { sortingLayerName="Default";sortingOrder=0; }
    internal Sprite sprite=new(); internal Color color;
    internal override Bounds LocalBounds => drawMode==SpriteDrawMode.Sliced ? new(Vector3.zero,new(size.x,size.y,0)) : sprite?.bounds ?? default;
}
internal sealed class Sprite:UnityEngine.Object
{
    internal string name;internal Rect rect=new(0,0,1,1);internal Vector4 border;internal Texture2D texture;
    internal Bounds bounds=new(Vector3.zero,Vector3.one);
    internal static Sprite Create(Texture2D t,Rect rect,Vector2 pivot,float pixels=100,uint extrude=0,SpriteMeshType mesh=SpriteMeshType.FullRect,Vector4 border=default)
      => new(){texture=t,rect=rect,border=border,bounds=new(Vector3.zero,new(rect.width/pixels,rect.height/pixels,0))};
}
internal enum SpriteMeshType { FullRect }
internal enum TextureFormat { RGBA32 }
internal enum TextureWrapMode { Clamp }
internal enum FilterMode { Bilinear }
internal struct Color32 { internal byte r,g,b,a;internal Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
internal sealed class Texture2D:UnityEngine.Object
{
    internal int width,height;internal string name;internal TextureWrapMode wrapMode;internal FilterMode filterMode;internal Color32[] Pixels;
    internal Texture2D(int w,int h,TextureFormat format,bool mipmap) { width=w;height=h; }
    internal void SetPixels32(Color32[] pixels) => Pixels=pixels;
    internal void Apply(bool update=true) { }
    internal bool LoadImage(byte[] png) { if(png.Length<24 || png[0]!=137 || png[1]!=80)return false;width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4));height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4));return true; }
}
internal sealed class InvNailSprite { internal Sprite level1; }
internal sealed class CharmIconList { internal static CharmIconList Instance;internal Sprite[] spriteList;internal Sprite GetSprite(int n) => spriteList?[n]; }
internal sealed class JournalList { internal GameObject[] list; }
internal sealed class JournalEntryStats { internal Sprite sprite; }
internal struct Bounds
{
    internal Vector3 center,size;
    internal Bounds(Vector3 center,Vector3 size) { this.center=center;this.size=size; }
    internal Vector3 min => center-size*.5f;
    internal Vector3 extents => size*.5f;
    internal Vector3 max => center+size*.5f;
    internal void Encapsulate(Bounds other)
    {
        var lo=Vector3.Min(min,other.min);var hi=Vector3.Max(max,other.max);
        center=(lo+hi)*.5f;size=hi-lo;
    }
    internal void Expand(Vector3 extra) { size+=extra; }
    internal bool Contains(Vector3 p) => p.x>=min.x && p.x<=max.x && p.y>=min.y && p.y<=max.y && p.z>=min.z && p.z<=max.z;
}
internal sealed class MaterialPropertyBlock
{
    internal Vector4 Value;
    internal void Clear() => Value=default;
    internal void SetVector(int property,Vector4 value) => Value=value;
}
internal struct Vector4
{
    internal float x,y,z,w;
    internal Vector4(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; }
}
internal class Component:UnityEngine.Object
{
    readonly Renderer renderer;
    internal Component(Renderer renderer=null) { this.renderer=renderer; }
    internal Transform transform => renderer?.transform;
    internal GameObject gameObject => transform?.gameObject;
    internal T[] GetComponentsInChildren<T>(bool inactive=true) where T:class => transform.GetComponentsInChildren<T>(inactive);
    internal T GetComponent<T>() where T:class => renderer as T;
    public string text { get;set; }
    public Color color { get;set; }
    internal bool SpawnFallbackOnMesh;
    internal Renderer LastFallback;
    public bool enableAutoSizing { get;set; }=true;
    public bool enableWordWrapping { get;set; }
    public float fontSize { get;set; }=40;
    public TextAlignment alignment { get;set; }
    public TextOverflow overflowMode { get;set; }
    public Vector2 size { get;set; }=new(1,1);
    internal Component Container;
    internal Vector2 LastMeshSize;
    internal float InkHeight=1;
    internal float? MeasuredTextHeight; // TMP mesh owner supplies measured localized prose ink, not a layout algorithm.
    internal Vector3 InkCenter;
    public Bounds textBounds => new(InkCenter,new Vector3(1,MeasuredTextHeight ?? InkHeight,1));
    public void ForceMeshUpdate() { LastMeshSize=Container?.size ?? size;if(SpawnFallbackOnMesh) { var go=new GameObject("TMP fallback submesh");go.transform.SetParent(transform);LastFallback=go.AddComponent<Renderer>();LastFallback.sortingOrder=0;SpawnFallbackOnMesh=false; } }
}
internal enum TextAlignment { TopLeft }
internal enum TextOverflow { Overflow }
// Exact HK ABI: TMProOld.TextContainer -> UnityEngine.EventSystems.UIBehaviour -> MonoBehaviour.
internal class TextContainer:MonoBehaviour { internal TextContainer(Renderer owner=null):base(owner) { } }
internal sealed class GameplayLabelDriver:MonoBehaviour { internal GameplayLabelDriver(Renderer owner):base(owner) { } }
internal class MonoBehaviour:Component { internal bool enabled=true; internal MonoBehaviour(Renderer owner=null):base(owner) { } }
internal sealed class NativeOwner:UnityEngine.Object { public Transform transform=new GameObject().transform; }
internal static class DiscoveryCounters { internal static int Hierarchy; }
internal static class Resources
{
    internal static int Discoveries;
    internal static Sprite[] Sprites = new[]{new Sprite{name="No_Map_symbol",rect=new(0,0,194,256),bounds=new(Vector3.zero,new(1.94f,2.56f,0))},new Sprite{name="map_mark_0000_scarab"},new Sprite{name="map_mark_0001_pill"},new Sprite{name="map_mark_0002_chit"},new Sprite{name="map_mark_0003_shell"}};
    internal static T[] FindObjectsOfTypeAll<T>() { Discoveries++;return typeof(T)==typeof(Sprite) ? (T[])(object)Sprites : Array.Empty<T>(); }
}
internal static partial class TMProOld { internal sealed class TextContainer:HkPauseContracts.TextContainer { internal TextContainer(Renderer owner):base(owner) { } } internal sealed class TMP_FontAsset { internal string name;internal object material; } }
internal static class TeamCherry
{
    internal static class Localization
    {
        internal static class Language { internal static int Code; internal static int CurrentLanguage() => Code; }
    }
}
