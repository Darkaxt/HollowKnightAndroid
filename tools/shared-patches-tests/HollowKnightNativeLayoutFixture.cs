#pragma warning disable CS0649, CS0414 // Unrelated teardown owner inputs intentionally remain at native defaults.
using System;
using System.Collections.Generic;
namespace HkPauseContracts;

// Asset/engine boundaries only. All pane layout, reference-cache construction,
// scrolling, text calibration, clipping and dispatch statements are extracted.
internal partial class HKDualScreen
{
    readonly List<CharmBoard> charmBoards=new();
    GameObject charmBoardsFor;
    Transform charmNameT,charmDescT,costPipRoot,equippedCharmNative;
    Renderer charmNameR,charmDescR;Renderer[] hkNotchRs;
    Sprite notchSprite;int lastDetailCharmN;
    Component nativeCharmNameSource,nativeCharmDescSource;
    internal Transform nativeCharmGrid;Renderer[] nativeCharmGridRenderers;
    internal SpriteRenderer nativeCharmPortrait;Renderer[] nativeCharmRetired;
    internal NativePaneLabel CharmName => nativeCharmName;
    internal NativePaneLabel CharmDescription => nativeCharmDesc;
    internal PaneGraphics CharmGraphics => nativeCharmGraphics;
    internal Transform CostPips => costPipRoot;
    internal int DetailRefreshes,ControlRefreshes;
    internal void NativeRetireStep() => TeardownCompanion();
    // Unrelated frame/map/input/asset owners consumed only by complete teardown.
    readonly Dictionary<Transform,Vector3> frameEdge=new(),frameBase=new();
    readonly List<SpriteRenderer> notchSRs=new();
    object notchTexLit,notchTexEmpty,notchSprLitFb,notchSprEmptyFb,notchSprFull,notchSprEmpty;
    LineRenderer selBox;
    object ctrlMyVerbTmp,ctrlTdT,ctrlTnR,ctrlTdR,ctrlHkRs,ctrlHkRsFor,ctrlSrcIconT;
    Transform paneCursor;GameObject paneCursorFor;
    Transform selCurTL,selCurTR,selCurBL,selCurBR,selCurFor,selectionMoveTarget;
    SpriteRenderer nativeSelectionTL,nativeSelectionBR,nativeSelectionGlow;
    Bounds selectionMoveFrom,selectionMoveNow;float selectionMoveT=1;bool selectionMoveShown;
    internal Transform NativeCursor => paneCursor;
    internal Transform SelectedItem => sel.item;
    internal void NativeSelectionStep(Transform item,int charm=0,string key=null) { sel.item=item;sel.charmN=charm;sel.invKey=key;PositionSelection(attrCam.orthographicSize); }
    internal void NativeCursorStep() => PositionSelection(attrCam.orthographicSize);
    internal void NativeTapStep(Vector3 world) => PollItemTap((world.x-compRoot.position.x)/BOTTOM_W+.5f,.5f-(world.y-compRoot.position.y)/BOTTOM_H);
    internal void NativeFitSpriteStep(SpriteRenderer renderer,Vector3 center,float width,float height) => FitSprite(renderer,center,width,height);
    internal Transform AddNativeCursor(GameObject pane)
    {
        var root=new GameObject("Cursor");root.transform.SetParent(pane.transform);
        foreach(var name in new[]{"TL","TR","BL","BR","Glow"})
        { var corner=new GameObject(name);corner.transform.SetParent(root.transform);corner.AddComponent<SpriteRenderer>(); }
        return root.transform;
    }
    string InvItemKey(Transform root) => root.name; // Localization key owner, not hit testing/geometry.
    SpriteRenderer ctrlMyGlyph,mapResetPillSR,benchPillSR;
    Transform mapResetT,benchPillT;Component mapResetTmp;Renderer noMapR;
    int tabColorCol,tabFleurMoveCol,lastEquipStamp,lastNotchTotal,lastNotchUsed,invStamp,charmStamp;
    float tabFleurMoveT,frameInnerBotFrac,frameInnerTopFrac,notchScanT,nudgedFocusAmt;
    bool mapAreaBValid,ctrlPlaced,ctrlGlyphPending,prewarmDone;
    string mapAreaBFor;object mapSrcRef,paneSrcRef;GameObject nudgedFocusFor;
    void DestroyOwnedAssets() { } // Native asset disposal ABI, no geometry/owner policy.
    void ReleaseLowerHudFixtureInputLock() { } // No live menu input owner in host tests.
    internal void NativeDetailStep(int kind,Transform item,int charm=0)
    { sel.kind=kind;sel.item=item;sel.invKey=item?.name;sel.charmN=charm;RefreshSelectedDetail(paneClone); }
    // Localization, TMP access and gameplay prompt owner. Prompt rendering is not
    // part of native pane layout and is not represented as layout integration.
    string CharmString(string key) => NativeLabels.TryGetValue("UI/"+key,out var s) ? s : key;
    void SetTmpTextByName(Transform pane,string name,string text)
    { var tmp=TmpOn(FindDeep(pane,name));if(tmp!=null) tmp.text=text;DetailRefreshes++; }
    void PopulateControlPrompt(GameObject pane,int kind,string name) => ControlRefreshes++;
    static bool IsUnderNamed(Transform t,string name) { for(;t!=null;t=t.parent) if(t.name==name) return true;return false; }
    sealed class CharmState { internal bool Has(int n) => PlayerData.instance?.GetBool("equippedCharm_"+n) ?? false; }
    readonly CharmState nativeCharmState=new();
    CharmState Charms() => nativeCharmState;
    static bool CharmGot(PlayerData pd,int n) => pd.GetBool("gotCharm_"+n);
    void EnsureNativeShellAssets()
    {
        // Shell asset owner admission only. No shell/native layout algorithm.
        // Replenish the owned donors after actual TeardownFrame resets them.
        if(frameRoot==null) frameRoot=new GameObject("FixtureShellAssets");
        if(shellRule==null) shellRule=new SpriteRenderer();
        if(mapMaskTopT==null) mapMaskTopT=new GameObject("FixtureQuadDonor").transform;
        if(mapMaskTopT.GetComponent<MeshFilter>()==null) mapMaskTopT.gameObject.AddComponent<MeshFilter>().sharedMesh=new object();
        if(mapMaskTopR==null) mapMaskTopR=mapMaskTopT.gameObject.AddComponent<MeshRenderer>();
        mapMaskTopR.sharedMaterial ??=new object();
    }
    internal GameObject NewNativePane(bool charms,int slots=21)
    {
        var pane=new GameObject(charms ? "NativeCharms" : "NativeInventory");pane.transform.SetParent(compRoot);
        if(charms) charmCloneCache=pane;else invCloneCache=pane;
        foreach(var name in new[]{"Text Name","Text Desc","Text Desc Low","Divider L","Divider R","Percentage","Text Completion"})
        {
            var t=new GameObject(name);t.transform.SetParent(pane.transform);var r=t.AddComponent<Renderer>();
            if(name.StartsWith("Text")) { var tmp=new Component(r);t.transform.TextComponents.Add(tmp); }
        }
        if(!charms)
        {
            var subject=new GameObject("Inv_Items");subject.transform.SetParent(pane.transform);
            var art=subject.AddComponent<Renderer>();art.BoundsSize=new(20,10,1);
            var equipment=new GameObject("Equipment");equipment.transform.SetParent(pane.transform);
            int i=0;
            foreach(var name in EQUIP_ORDER.Concat(new[]{"Trinket1","Trinket2","Trinket3","Trinket4"}).Take(slots))
            { var item=new GameObject(name);item.transform.SetParent(equipment.transform);item.transform.localPosition=new(i++%4,-i/4,0);item.AddComponent<Renderer>().BoundsSize=new(2,2,1); }
        }
        else
        {
            for(int i=1;i<=40;i++)
            {
                var board=new GameObject("CharmBoard"+i);board.transform.SetParent(pane.transform);board.transform.localPosition=new((i-1)%8*2+((i-1)/8%2)*.5f,-(i-1)/8*1.7f,0);
                var r=board.AddComponent<Renderer>();var icon=new GameObject("Icon"+i);icon.transform.SetParent(board.transform);icon.AddComponent<SpriteRenderer>();
                board.transform.TextComponents.Add(new InvCharmBackboard(r){charmNum=i,charmObject=icon});
                PlayerData.instance.Bools["gotCharm_"+i]=true;
            }
            var details=new GameObject("Details");details.transform.SetParent(pane.transform);
            var portrait=new GameObject("Detail Sprite");portrait.transform.SetParent(details.transform);portrait.AddComponent<SpriteRenderer>();
            var equip=new GameObject("Equipped Charms");equip.transform.SetParent(pane.transform);equip.AddComponent<Renderer>();
            var notches=new GameObject("Notches");notches.transform.SetParent(pane.transform);
            var pip=new GameObject("Sprite Full");pip.transform.SetParent(notches.transform);pip.AddComponent<SpriteRenderer>();
        }
        // The real BuildPaneClone -> CharmsPaneInit boundary populates detail
        // before first dispatch. Exercise that actual callee, including clearing
        // lastDetailCharmN from the retired ownership epoch.
        if(charms) PopulateCharmDetail(pane);
        return pane;
    }
    internal Component NativeSource(GameObject pane,string name) => TmpOn(FindDeep(pane.transform,name));
    internal void NativeLayoutStep(int id) { tab.cur=id;paneClone=CloneForTab(id);if(id==1) LayoutNativeInventory(paneClone);else ApplyFit(LayoutCharmsRedesign(paneClone));ApplyCompanionCamera(compRoot.position); }
}
internal sealed class InvCharmBackboard:MonoBehaviour
{
    public int charmNum;public GameObject charmObject;
    internal InvCharmBackboard(Renderer owner):base(owner) { }
    public int GetCharmNum() => charmNum;
}
