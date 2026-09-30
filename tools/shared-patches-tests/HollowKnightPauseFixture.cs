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
        GameManager.instance = Manager; PlayerData.instance = new(); InitializeOwnership();
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
    void TeardownCompanion() { Teardowns++; RetireCompanionCaches(); slideOutClone = null; slideT = 1f; slideCamValid = false; }
    internal readonly List<GameObject> Destroyed = new();
    void Destroy(GameObject go) { AssertSingleDestroy(go); Destroyed.Add(go); go.SetActive(false); }
    void AssertSingleDestroy(GameObject go) { if (Destroyed.Contains(go)) throw new InvalidOperationException("duplicate owned destruction"); }
    internal string ResolveGuide(Transform root,Transform row,int index) => GuideRowCondition(root,row,index);
    internal static string[] NativeGuideConditions => GuideConditions;
    internal static string[] NativeGuideStates => GuideStates;
    internal static string[] NativeGuideVariables => GuideVariables;
    internal int BuildAttempts, BuildWarnings;
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
    SpriteRenderer ShellSprite(string name,Transform parent,Sprite sprite,int order=30080)
    { var renderer=new SpriteRenderer();renderer.sprite=sprite;renderer.sortingLayerName="Inventory";renderer.sortingOrder=order;renderer.gameObject.layer=ATTR_LAYER;renderer.transform.SetParent(parent);return renderer; }
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
    static void SetTmpColor(Component tmp,Color color) { }
    static void TcSetSize(Component container,Vector2 size) { container.size=size; }
    const int TMP_CLIP_RECT=1;
    static readonly Color ShellInk=new(),ShellMuted=new();
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
    void SetMapMarkerMode(bool value) => mapMarkerMode=value;
    internal sealed class Selection { internal string invKey; internal int charmN,kind; internal Transform item; }
    readonly Selection sel=new();
    int mapStowStamp,compFrameTick; internal bool mapAnyAvailable; bool paneNeedsFit;
    internal object mapGm; internal bool mapContentVisible,mapNeedsSetup;
    internal MapActionButton mapResetAction; MapActionButton mapViewAction,mapMarkerAction,mapMarkerTypeAction;
    object mapControlPill;
    internal sealed class MapActionButton
    {
        internal Transform Root;internal Component Label;internal Renderer LabelRenderer;internal SpriteRenderer Plate;
        internal Bounds Hit;
    }
    internal int AnimatedResets,MarkerWrites;
    internal void BuildActionsStep() => BuildMapControlsFixture(Manager.inventoryFSM.transform.root);
    internal void PositionActionsStep() => PositionMapControlsFixture(attrCam.orthographicSize,attrCam.aspect,.5f,-.5f,tab.cur==0);
    internal bool MapActionTap(Vector3 p) => HandleMapControlTap(p);
    object MakePillSprite() => new();
    MapActionButton BuildMapActionButton(Transform root,string name,string text)
    {
        var label=new NativePaneLabel();var plate=new SpriteRenderer();
        return new MapActionButton { Root=label.Root,Label=label.Tmp,LabelRenderer=label.Renderer,Plate=plate };
    }
    void SetMapAction(MapActionButton button,bool show,string text,Vector3 center,float zf,Color plate,Color ink)
    {
        if(!ValidButton(button)) return;button.Root.gameObject.SetActive(show);
        button.Hit=new Bounds(center,new Vector3(.2f,.2f,20));
    }
    bool AnyMarkerUnlocked() => true;
    void SetWorldMapMode(bool value) => mapWorldMode=value;
    void CycleMarkerType() { }
    bool PlaceOrRemoveMarker(Vector3 world) { MarkerWrites++;return true; }
    void ResetMapViewAnimated() => AnimatedResets++;
    Renderer mapResetR;
    float mapControlLeftX=-1000,mapControlRightX=1000,mapControlTopY=1000,mapControlBottomY=-1000;
    void HideControlPrompt(GameObject go) { }
    void ClearInvDetail(GameObject go) { }
    void BuildCompanionTab(int id) { paneClone=id==0 ? null : (id==3 || id==4) ? BuildSupplementaryPane(id) : CloneForTab(id); }
    int MapContentStamp() => 0;
    void MapTick() { }
    void MapFrameTick(ref Vector3 center) { }
    void MapPinchTick() { }
    PaneRun RunFor(bool charms) => charms ? charmRun : invRun;
    void PaneSettleTick(GameObject go,PaneRun run,bool inventory,bool active) { }
    void CharmsTick() { }
    void RefreshInvCounters(GameObject go) { }
    void ApplyFit(FitResult value) { if(value.valid) fit=value; }
    FitResult LayoutCharmsRedesign(GameObject go) => default;
    bool TryPaneBounds(GameObject go,out Vector3 center,out float size,bool charms) { center=default;size=0;return false; }
    internal void SupplementaryStep() => SupplementaryTick();
    void BuildFrame() { }
    internal int NoMapBuilds;
    internal void FrameBuildStep() => BuildFrameBody();
    Sprite CreateShellRule() => new();
    internal void RetryNoMapStep(bool missing=false) { if(missing) noMapT=null; Time.frameCount+=120;ResolveTabDonors(null); }
    static Transform FindDeep(Transform root,string name) => null;
    void BuildAreaName(Transform root) { }
    void BuildNoMapLabel(Transform root) { NoMapBuilds++;noMapT=new GameObject().transform; }
    void BuildMapControls(Transform root) { }
    Transform mapMaskBotT; Renderer mapMaskBotR;
    Transform BuildMapMask(string name) { var go=new GameObject();go.AddComponent<Renderer>();return go.transform; }
    static bool IsTextMeshProGraphic(Component c) => c != null && c.GetType()==typeof(Component);
    static Component TmpOn(Transform root) => root.TextComponents.FirstOrDefault(c=>IsTextMeshProGraphic(c));
    void SanitizeDetachedTmpClone(GameObject go) { }
    void DestroyImmediate(Component c) { }
    static GameObject Instantiate(GameObject source,Transform parent)
    {
        var go=new GameObject();go.transform.SetParent(parent);go.transform.localScale=source.transform.localScale;
        var renderer=go.AddComponent<Renderer>(); var original=TmpOn(source.transform);
        if(original != null)
        {
            var copy=new Component(renderer) { enableAutoSizing=original.enableAutoSizing,InkHeight=original.InkHeight };
            renderer.BoundsSize=original.GetComponent<Renderer>().BoundsSize;
            go.transform.TextComponents.Add(copy);
            var container=new TextContainer();copy.Container=container;go.transform.TextComponents.Add(container);
        }
        return go;
    }
    void PositionFrame() { }
    void ReassertControlPrompt() { }
    internal int TouchPollsRecorded, JournalTaps, GuideTaps, ItemTaps;
    readonly HKLowerLayout.TabGesture lowerTabGesture = new();
    int lastSimTapN, lastTapSeq, lowerCleanTabSeq; bool lowerTouchDownBody;
    internal bool mapMarkerMode;
    float supplementaryDragY; int supplementaryDragRegion;
    internal void TouchStep() { TouchPollsRecorded++; PollTouch(); }
    void JournalTap(float x,float y) { JournalTaps++; JournalTapBody(x,y); }
    void GuideTap(float x,float y) { GuideTaps++; GuideTapBody(x,y); }
    internal void SelectionTap(int id,float x,float y) { if(id==3) JournalTap(x,y);else GuideTap(x,y); }
    void PollItemTap(float x,float y) => ItemTaps++;
    void ScrollSupplementary(float delta) => ScrollSupplementaryBody(delta);
    internal void ScrollStep(float delta) => ScrollSupplementary(delta);
    void CenterAttribution() { }
    void ApplyHalo() { }
    void SetupLogo() => throw new InvalidOperationException("unexpected logo rebake");
    void TryBakeTabFleurs() => throw new InvalidOperationException("unexpected fleur rebake");
    void TeardownFrame() { FrameTeardowns++; RetireSupplementaryPanes(); if(frameRoot != null) Destroy(frameRoot); frameRoot = null; }
    void PushToBottom() { }
    void CenterDialogue() { }
    void CenterTutorial() { }
    void Dbg(string text) { }
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
    internal float TouchX, TouchY, CleanTapX, CleanTapY, T0Y;
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
    internal readonly Dictionary<string,bool> Bools = new();
    internal readonly Dictionary<string,int> Ints = new();
    internal int Reads;
    internal bool GetBool(string key) { Reads++; return Bools.TryGetValue(key,out var value) && value; }
    internal int GetInt(string key) { Reads++; return Ints.TryGetValue(key,out var value) ? value : 0; }
}
internal sealed class GameManager
{
    internal static GameManager instance;
    internal dynamic inventoryFSM = new NativeOwner(); internal UnityEngine.Object gameMap = new UnityEngine.Object();
    internal string GetCurrentMapZone() => "";
    internal GlobalEnums.GameState gameState = GlobalEnums.GameState.PLAYING;
    internal string MenuState = "GAMEPLAY";
    // Exact HK pause authority: Options changes menu state, not GameState.PAUSED.
    internal bool IsGamePaused() => gameState == GlobalEnums.GameState.PAUSED;
}
internal static class GlobalEnums { internal enum GameState { PLAYING, PAUSED, MAIN_MENU } }
internal static class HkStageHooks
{
    internal static int SkinStamp;
    internal static bool BlackBackground;
    internal static void ClearLegacyFlashMode() { }
    internal static void Tick(HKLayout cfg, bool debug) { }
}
internal static class Time { internal static int frameCount; internal static float unscaledDeltaTime = .1f; }
internal static class Mathf
{
    internal static int Max(int a,int b) => Math.Max(a,b);
    internal static float Abs(float value) => Math.Abs(value);
    internal static int FloorToInt(float value) => (int)Math.Floor(value);
    internal static int Clamp(int value,int min,int max) => Math.Clamp(value,min,max);
    internal static float Lerp(float from,float to,float t) => from+(to-from)*t;
    internal static float Max(float a, float b) => Math.Max(a, b);
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
    internal Vector2(float x, float y) { this.x = x; this.y = y; }
}
internal static class Quaternion { internal static readonly object identity = new(); internal static object Euler(float x,float y,float z) => identity; }
internal enum CameraClearFlags { SolidColor, Depth }
internal struct Color { internal static readonly Color black = new(),white=new(); internal Color(float r,float g,float b,float a) { } }
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
    internal static Vector3 one => new(1,1,1);
    internal static Vector3 zero => new();
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
    internal T GetComponent<T>() where T:class => Components.TryGetValue(typeof(T),out var component) ? component as T : null;
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
        get { var p=parent!=null ? parent.position : Vector3.zero;var s=parent!=null ? parent.lossyScale : Vector3.one;return p+new Vector3(localPosition.x*s.x,localPosition.y*s.y,localPosition.z*s.z); }
        set { var p=parent!=null ? parent.position : Vector3.zero;var s=parent!=null ? parent.lossyScale : Vector3.one;localPosition=new Vector3((value.x-p.x)/s.x,(value.y-p.y)/s.y,(value.z-p.z)/s.z); }
    }
    internal Vector3 lossyScale { get { var p=parent!=null ? parent.lossyScale : Vector3.one;return new Vector3(localScale.x*p.x,localScale.y*p.y,localScale.z*p.z); } }
    internal Vector3 TransformPoint(Vector3 p) { var s=lossyScale;return position+new Vector3(p.x*s.x,p.y*s.y,p.z*s.z); }
    internal Transform root => parent!=null ? parent.root : this;
    internal Vector3 InverseTransformPoint(Vector3 point) { var p=point-position;var s=lossyScale;return new Vector3(p.x/s.x,p.y/s.y,p.z/s.z); }
    internal object rotation,localRotation;
    internal readonly List<Renderer> Renderers=new();
    internal T[] GetComponentsInChildren<T>(bool inactive) where T:class => Renderers.Cast<object>().Concat(TextComponents).OfType<T>().ToArray();
    internal Transform(GameObject go = null) { gameObject = go; }
    internal readonly List<PlayMakerFSM> FsMs=new();
    internal readonly List<Component> TextComponents=new();
    internal T[] GetComponents<T>() where T:class => FsMs.Cast<object>().Concat(TextComponents).OfType<T>().ToArray();
    internal T GetComponentInChildren<T>(bool inactive) where T:class => GetComponentsInChildren<T>(inactive).FirstOrDefault();
    internal T GetComponent<T>() where T:class => gameObject.GetComponent<T>();
    internal void SetParent(Transform value,bool worldPositionStays=false) { parent = value; value.Children.Add(this); }
}
internal sealed class PlayMakerFSM
{
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
    internal Vector4 Clip;
    public Renderer() : this(null) { }
    internal Renderer(Transform owner) { transform=owner ?? new GameObject().transform; }
    internal Vector3 BoundsSize=Vector3.one;
    internal Bounds bounds { get { var s=transform.lossyScale;return new Bounds(transform.position,new Vector3(BoundsSize.x*s.x,BoundsSize.y*s.y,BoundsSize.z*s.z)); } }
    internal void GetPropertyBlock(MaterialPropertyBlock block) => block.Value=Clip;
    internal void SetPropertyBlock(MaterialPropertyBlock block) { Clip=block.Value;ClipWrites++; }
    internal bool InClip(Vector2 p) => p.x>=Clip.x && p.y>=Clip.y && p.x<=Clip.z && p.y<=Clip.w;
}
internal sealed class MeshFilter { public MeshFilter() { } internal object sharedMesh; }
internal sealed class MeshRenderer:Renderer { public MeshRenderer() { } }
internal sealed class SpriteRenderer:Renderer
{
    public SpriteRenderer() { sortingLayerName="Default";sortingOrder=0; }
    internal Sprite sprite=new(); internal Color color;
}
internal sealed class Sprite { internal string name;internal Rect rect=new(0,0,1,1); internal Bounds bounds=new(Vector3.zero,Vector3.one); }
internal sealed class InvNailSprite { internal Sprite level1; }
internal sealed class CharmIconList { internal static CharmIconList Instance;internal Sprite[] spriteList; }
internal sealed class JournalList { internal GameObject[] list; }
internal sealed class JournalEntryStats { internal Sprite sprite; }
internal struct Bounds
{
    internal Vector3 center,size;
    internal Bounds(Vector3 center,Vector3 size) { this.center=center;this.size=size; }
    internal Vector3 min => center-size*.5f;
    internal Vector3 max => center+size*.5f;
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
internal class Component
{
    readonly Renderer renderer;
    internal Component(Renderer renderer=null) { this.renderer=renderer; }
    internal T GetComponent<T>() where T:class => renderer as T;
    public string text { get;set; }
    public bool enableAutoSizing { get;set; }=true;
    public bool enableWordWrapping { get;set; }
    public float fontSize { get;set; }=40;
    public TextAlignment alignment { get;set; }
    public TextOverflow overflowMode { get;set; }
    public Vector2 size { get;set; }=new(1,1);
    internal Component Container;
    internal Vector2 LastMeshSize;
    internal float InkHeight=1;
    public Bounds textBounds => new(Vector3.zero,new Vector3(1,InkHeight,1));
    public void ForceMeshUpdate() { LastMeshSize=Container?.size ?? size; }
}
internal enum TextAlignment { TopLeft }
internal enum TextOverflow { Overflow }
internal sealed class TextContainer:Component { }
internal class MonoBehaviour:Component { }
internal sealed class NativeOwner:UnityEngine.Object { public Transform transform=new GameObject().transform; }
internal static class Resources { internal static T[] FindObjectsOfTypeAll<T>() => Array.Empty<T>(); }
internal static class TMProOld { internal sealed class TMP_FontAsset { internal string name;internal object material; } }
internal static class TeamCherry
{
    internal static class Localization
    {
        internal static class Language { internal static int Code; internal static int CurrentLanguage() => Code; }
    }
}
