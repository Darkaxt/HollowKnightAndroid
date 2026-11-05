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
    internal string ActionOutcome => charmActionResult.ToString();
    internal bool PendingCharmFit { get => paneNeedsFit; set => paneNeedsFit=value; }
    internal int EquipWatchHash => lastCharmEquipHash;
    internal void NativeEquipWatchStep() => CharmsTick();
    internal void NativeCompanionStep() => UpdateCompanion(Cameras.hudCamera);
    internal void ClearCachedInventoryDetailStep() => ClearInvDetailLocal(invCloneCache); // Actual layout-bearing callee, not an injected outcome.
    internal void LoseNativeCharmNameDonor() { nativeCharmName=null;charmNameT=null; }
    internal string ThrowDetailText;
    internal int DetailTextAttempts;
    internal NativeCharmFeedbackInputs InstallNativeFeedback(GameObject pane)
    {
        // Exact serialized native state/resource shape, not a modeled dispatcher.
        var inputs=new NativeCharmFeedbackInputs();
        inputs.Vibration=pane.AddComponent<CharmVibrations>();
        inputs.Vibration.enabled=false; // Detached clone scripts remain disabled.
        var prefab=new GameObject("Audio Player UI");
        var audio=prefab.AddComponent<AudioSource>();audio.Inputs=inputs;
        prefab.AddComponent<PlayAudioAndRecycle>().audioSource=audio;
        inputs.Prefab=prefab;
        var clips=new[]{new AudioClip{name="sword_hit_reject"},new AudioClip{name="dream_damage"},
                        new AudioClip{name="sword_hit_window_1"},new AudioClip{name="mage_lord_glass_floor_break"}};
        inputs.Clips=clips;
        object Sound(int clip,float pitch=1) => new HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle {
            audioPlayer=new FsmGameObject{Value=prefab},spawnPoint=new FsmGameObject{Value=pane},
            audioClip=new FsmObject{Value=clips[clip]},pitchMin=new FsmFloat{Value=pitch},
            pitchMax=new FsmFloat{Value=pitch},volume=new FsmFloat{Value=1},delay=new FsmFloat{Value=0}};
        var fsm=new PlayMakerFSM{enabled=false,FsmName="UI Charms",FsmStates=new[]{
            new FsmState{Name="Tink",Actions=new[]{Sound(0)}},
            new FsmState{Name="Crack 1",Actions=new[]{Sound(1),Sound(2)}},
            new FsmState{Name="Crack 2",Actions=new[]{Sound(1,1.15f),Sound(2)}},
            new FsmState{Name="Break",Actions=new[]{Sound(3)}}}};
        inputs.Fsm=fsm;pane.transform.FsMs.Add(fsm);
        return inputs;
    }
    internal void NativeRetireStep() => TeardownCompanion();
    // Unrelated frame/map/input/asset owners consumed only by complete teardown.
    readonly Dictionary<Transform,Vector3> frameEdge=new(),frameBase=new();
    readonly List<SpriteRenderer> notchSRs=new();
    object notchTexLit,notchTexEmpty,notchSprLitFb,notchSprEmptyFb,notchSprFull,notchSprEmpty;
    LineRenderer selBox;
    Component ctrlMyVerbTmp;Transform ctrlTdT,ctrlSrcIconT,ctrlHkRsFor,ctrlMyVerbT;
    Renderer ctrlTnR,ctrlTdR;Renderer[] ctrlHkRs;
    Vector3 ctrlGlyphFinal,ctrlVerbFinal;float ctrlGlyphScale;int ctrlVerbKind;
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
    // Localization/native prompt selection remains an engine ingress. Previously
    // selected overlays exercise the real extracted final prompt bodies below.
    string CharmString(string key) => NativeLabels.TryGetValue("UI/"+key,out var s) ? s : key;
    void SetTmpTextByName(Transform pane,string name,string text)
    {
        DetailTextAttempts++;
        if(ThrowDetailText==name) throw new InvalidOperationException("native detail text unavailable");
        var tmp=TmpOn(FindDeep(pane,name));if(tmp!=null) tmp.text=text;DetailRefreshes++;
    }
    void PopulateControlPrompt(GameObject pane,int kind,string name) => ControlRefreshes++;
    static bool IsUnderNamed(Transform t,string name) { for(;t!=null;t=t.parent) if(t.name==name) return true;return false; }
    int lastCharmEquipHash=int.MinValue;
    internal bool CachedCharm(int id) => Charms().Has(id);
    internal int CachedCharmCount => Charms().count;
    internal void SetEquipState(params int[] ids)
    {
        for(int n=1;n<=40;n++) PlayerData.instance.Bools["equippedCharm_"+n]=ids.Contains(n);
        PlayerData.instance.equippedCharms.Clear();PlayerData.instance.equippedCharms.AddRange(ids);
        _charmsFrame=-1;
    }
    internal bool NativeShellAssetsAvailable=true;
    void EnsureNativeShellAssets()
    {
        if(!NativeShellAssetsAvailable) return;
        // Shell asset owner admission only. No shell/native layout algorithm.
        // Replenish the owned donors after actual TeardownFrame resets them.
        if(frameRoot==null) frameRoot=new GameObject("FixtureShellAssets");
        if(shellRule==null) shellRule=new SpriteRenderer();
        if(mapMaskTopT==null) mapMaskTopT=new GameObject("FixtureQuadDonor").transform;
        if(mapMaskTopT.GetComponent<MeshFilter>()==null) mapMaskTopT.gameObject.AddComponent<MeshFilter>().sharedMesh=new object();
        if(mapMaskTopR==null) mapMaskTopR=mapMaskTopT.gameObject.AddComponent<MeshRenderer>();
        mapMaskTopR.sharedMaterial ??=new object();
    }
    internal GameObject NewNativePane(bool charms,int slots=21,bool populate=true)
    {
        var pane=new GameObject(charms ? "NativeCharms" : "NativeInventory");pane.transform.SetParent(compRoot);
        if(charms) charmCloneCache=pane;else invCloneCache=pane;
        var supplied=RunFor(charms);supplied.finalized=true;supplied.kicks=supplied.settle=0;
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
        SetLayerRecursive(pane.transform,ATTR_LAYER);
        if(charms && populate) PopulateCharmDetail(pane);
        return pane;
    }
    // Cache-supplied typed ingress: native construction/OnEnable is outside this fixture.
    // No layout/resource/readiness result is assigned here. All lifecycle decisions
    // after this point are the extracted production dispatcher, builder and settle.
    internal GameObject ColdNativeOwner(bool charms,bool finalized=true,int slots=21)
    {
        NativeFinalPositioning=true;
        foreach(var name in new[]{"Inv","Charms"})
            if(Manager.inventoryFSM.transform.Find(name)==null) { var node=new GameObject(name);node.transform.SetParent(Manager.inventoryFSM.transform); }
        var pane=NewNativePane(charms,slots,false);
        var run=RunFor(charms);run.finalized=finalized;run.kicks=finalized || charms ? 0 : 8;run.settle=finalized ? 0 : charms ? 20 : 45;
        paneSrcRef=Manager.inventoryFSM;prewarmDone=true;
        tab.cur=tab.built=-1;tab.tap=-1;tab.lastCfg=charms ? 2 : 1;cfg.compTab=charms ? 2 : 1;
        EnsureNativeShellAssets();
        for(int i=0;i<5;i++) { tabIcons[i]=new Sprite();frameTabs[i]=ShellSprite("F_Tab"+i,frameRoot.transform,tabIcons[i]); }
        tabTL=new();tabBR=new();tabGlow=new();
        iconRetry.Resolved();
        Resources.Sprites=new[]{new Sprite{name="No_Map_symbol"},new Sprite{name="map_mark_0000_scarab"},new Sprite{name="map_mark_0001_pill"},new Sprite{name="map_mark_0002_chit"},new Sprite{name="map_mark_0003_shell"}};
        BuildMapControls(Manager.inventoryFSM.transform);
        return pane;
    }
    internal void PromptIngress(bool pending=false)
    {
        // Previously selected overlay inputs, including the independent owned
        // subtree boundary; no visibility/readiness outcome is supplied here.
        var root=new GameObject("Owned prompt").transform;root.SetParent(compRoot);
        typeof(HKDualScreen).GetField("ctrlPromptRoot",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.SetValue(this,root);
        var glyph=new GameObject("Prompt glyph");glyph.transform.SetParent(root);glyph.layer=ATTR_LAYER;
        ctrlMyGlyph=glyph.AddComponent<SpriteRenderer>();
        ctrlMyVerbT=new GameObject("Prompt verb").transform;ctrlMyVerbT.SetParent(root);
        ctrlMyVerbT.gameObject.layer=ATTR_LAYER;var r=ctrlMyVerbT.gameObject.AddComponent<Renderer>();
        ctrlMyVerbTmp=new Component(r);ctrlMyVerbT.TextComponents.Add(ctrlMyVerbTmp);ctrlMyVerbTmp.text="PRESS";
        typeof(HKDualScreen).GetField("ctrlMyVerbRenderer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.SetValue(this,r);
        ctrlGlyphFinal=ctrlMyGlyph.transform.position;ctrlVerbFinal=ctrlMyVerbT.position;
        ctrlActive=true;ctrlPlaced=!pending;ctrlLayoutPending=pending ? 1 : 0;
    }
    internal Renderer GeneratePromptSubmesh(bool enabled=true)
    {
        // Existing TMP engine-generation ingress creates the fallback child;
        // production owns its inherited visibility, not a fixture callback.
        ctrlMyVerbTmp.SpawnFallbackOnMesh=true;ctrlMyVerbTmp.ForceMeshUpdate();
        var renderer=ctrlMyVerbTmp.LastFallback;renderer.gameObject.layer=ATTR_LAYER;
        renderer.enabled=enabled;return renderer;
    }
    internal Renderer[] PromptRenderers => ctrlMyGlyph.transform.parent.GetComponentsInChildren<Renderer>(true);
    internal int PromptPending => ctrlLayoutPending;
    internal bool PromptSelected => ctrlActive;
    internal Renderer PromptGlyph => ctrlMyGlyph;
    internal Renderer PromptVerb => ctrlMyVerbTmp?.GetComponent<Renderer>();
    internal bool PromptRetained => ctrlActive && ctrlPlaced;
    internal void SupplyNativeCacheInputs()
    {
        // After a tested retirement, supply replacement caches at the explicit
        // engine boundary. No build/OnEnable/layout outcome is simulated here.
        NewNativePane(false,populate:false);NewNativePane(true,populate:false);
        prewarmDone=true;paneSrcRef=Manager.inventoryFSM;InitializeOwnership();
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

// Typed native audio/haptic ABI seams. Calls originate in extracted production.
internal sealed class NativeCharmFeedbackInputs
{
    internal GameObject Prefab;internal AudioClip[] Clips;internal PlayMakerFSM Fsm;
    internal CharmVibrations Vibration;
    internal int Spawns;
    internal bool ThrowSpawn,MissingSpawnAudio,MissingSpawnLifecycle;
    internal string ThrowAudio;
    internal readonly List<(string clip,float pitch,float volume)> Audio=new();
    internal readonly List<(GameObject prefab,Vector3 position,Quaternion rotation)> SpawnCalls=new();
}
internal sealed class AudioClip:UnityEngine.Object { internal string name; }
internal sealed class AudioSource
{
    public AudioSource() { }
    internal NativeCharmFeedbackInputs Inputs;
    internal float pitch=1,volume=1;
    internal void PlayOneShot(AudioClip clip)
    {
        Inputs.Audio.Add((clip.name,pitch,volume));
        if(Inputs.ThrowAudio==clip.name) throw new InvalidOperationException("native one-shot unavailable");
    }
}
internal sealed class PlayAudioAndRecycle:MonoBehaviour
{
    public PlayAudioAndRecycle() { }
    internal AudioSource audioSource;
}
internal sealed class CharmVibrations:MonoBehaviour
{
    public CharmVibrations() { }
    internal bool Throw;
    internal readonly List<string> Calls=new();
    void Record(string name) { Calls.Add(name);if(Throw) throw new InvalidOperationException("native vibration unavailable"); }
    internal void PlayFailedPlace() => Record("PlayFailedPlace");
    internal void PlayOvercharmHit() => Record("PlayOvercharmHit");
    internal void PlayOvercharmFinalHit() => Record("PlayOvercharmFinalHit");
}
internal static class ObjectPoolExtensions
{
    internal static GameObject Spawn(this GameObject prefab,Vector3 position,Quaternion rotation)
    {
        var inputs=prefab.GetComponent<AudioSource>().Inputs;
        inputs.Spawns++;inputs.SpawnCalls.Add((prefab,position,rotation));
        if(inputs.ThrowSpawn) throw new InvalidOperationException("native pool unavailable");
        var instance=new GameObject();
        AudioSource audio=null;
        if(!inputs.MissingSpawnAudio) { audio=instance.AddComponent<AudioSource>();audio.Inputs=inputs; }
        if(!inputs.MissingSpawnLifecycle) instance.AddComponent<PlayAudioAndRecycle>().audioSource=audio;
        return instance;
    }
}
internal sealed class FsmObject { internal UnityEngine.Object Value; }
internal sealed class FsmFloat { internal float Value; }
internal static class HutongGames
{
    internal static class PlayMaker
    {
        internal static class Actions
        {
            internal sealed class AudioPlayerOneShotSingle
            {
                internal FsmGameObject audioPlayer,spawnPoint;
                internal FsmObject audioClip;
                internal FsmFloat pitchMin,pitchMax,volume,delay;
            }
        }
    }
}
