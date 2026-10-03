using System;
using System.Collections.Generic;
using UnityEngine;

// [B7] BOTTOM SCREEN — Charms tab. The redesign layout (detail band on top, HK's native honeycomb grid below,
// equipped darkening, self-drawn notch-cost pips), the default/tapped charm detail, and the equip watch.
public partial class HKDualScreen
{
    // Per-clone charm cache (built once in EnsureCharmRefs; replaces two Transform dictionaries + a 40x
    // GetComponents/reflection scan that ran on EVERY layout call = every tap and every equip change).
    class CharmBoard { public Transform t; public int n; public GameObject icon; public Renderer br; public Vector3 natPos, s0, iconS0; }
    readonly List<CharmBoard> charmBoards = new List<CharmBoard>();
    GameObject charmBoardsFor;    // pane the cache belongs to (a rebuilt clone has new transforms)
    Transform charmNameT, charmDescT; Renderer charmNameR, charmDescR;   // detail text (cached; renderers must be FORCE-ENABLED — see LayoutCharmsRedesign)
    Renderer[] hkNotchRs;         // HK's own (invisibly-rendering) Notches cluster, kept off

    Transform costPipRoot;        // redesign: OUR OWN notch-cost pip row (HK's Notches renders invisibly even when layered)

    Sprite notchSprite;           // redesign: cached filled-notch sprite grabbed from HK's "Sprite Full"

    int lastDetailCharmN;         // redesign: the charm number PopulateCharmDetail last DISPLAYED (so cost pips match the name)

    int lastCharmEquipHash = int.MinValue;   // redesign: watch the equipped-charm set to re-darken the grid in place (no clone rebuild)

    // Collection action state belongs to the lower Charms activation, never the save or cloned FSM.
    NativePaneLabel nativeCharmAction; GameObject charmActionPane, charmActionVisiblePane;
    Rect charmActionRect; object charmActionTransport;
    bool charmActionVisible, charmActionArmed, charmActionUnequip;
    int charmActionEpoch, charmActionDownEpoch, charmActionDownClean, charmActionAttempts;
    float charmActionDownTime; Vector2 charmActionDownPoint;
    Transform charmActionItem; int charmActionNumber;
    CharmActionInputs charmActionDownOwners;
    CharmActionResult charmActionResult;
    enum CharmActionResult { Rejected, Tink, Crack1, Crack2, Applied, Partial, AppliedRefreshFailed, FeedbackUnavailable, FeedbackFailed, AppliedFeedbackUnavailable, AppliedFeedbackFailed }
    enum CharmFeedbackResult { Unavailable, Failed, Played }
    GameObject charmActionFeedbackPane, charmActionAudioPrefab;
    Transform charmActionFeedbackItem; int charmActionFeedbackNumber;
    bool charmActionFeedbackBound, charmActionFeedbackReady, charmDetailReady;
    AudioClip charmActionTinkClip, charmActionCrackClip, charmActionWindowClip, charmActionBreakClip;
    CharmVibrations charmActionVibrations;
    struct CharmActionInputs
    {
        public PlayerData player; public GameManager manager; public HeroController hero;
        public UnityEngine.Object upper; public int cost, slots, filled; public bool equipped, canOvercharm;
    }

    // World-space bounds of a TMP's DRAWN GLYPHS (reflection textBounds -> TransformPoint; renderer bounds
    // over-span the authored rect). False if the component/property is missing or the mesh is empty.
    static System.Reflection.PropertyInfo _tmpTextBoundsPI;
    static bool TryTmpGlyphBoundsWorld(Transform tmpT, out Vector3 wMin, out Vector3 wMax)
    {
        wMin = wMax = Vector3.zero;
        try
        {
            if (tmpT == null) return false;
            Component tmp = null;
            foreach (var ccc in tmpT.GetComponents<Component>()) { if (IsTextMeshProGraphic(ccc)) { tmp = ccc; break; } }
            if (tmp == null) return false;
            if (_tmpTextBoundsPI == null || _tmpTextBoundsPI.DeclaringType != tmp.GetType()) _tmpTextBoundsPI = tmp.GetType().GetProperty("textBounds");
            if (_tmpTextBoundsPI == null) return false;
            Bounds tb = (Bounds)_tmpTextBoundsPI.GetValue(tmp, null);
            if (tb.size.sqrMagnitude < 1e-8f) return false;
            Vector3 p0 = tmpT.TransformPoint(tb.min);
            Vector3 p1 = tmpT.TransformPoint(new Vector3(tb.min.x, tb.max.y, tb.min.z));
            Vector3 p2 = tmpT.TransformPoint(new Vector3(tb.max.x, tb.min.y, tb.min.z));
            Vector3 p3 = tmpT.TransformPoint(new Vector3(tb.max.x, tb.max.y, tb.min.z));
            wMin = Vector3.Min(Vector3.Min(p0, p1), Vector3.Min(p2, p3));
            wMax = Vector3.Max(Vector3.Max(p0, p1), Vector3.Max(p2, p3));
            return true;
        }
        catch { return false; }
    }

    static int CharmNumOf(Transform t)
    {
        try
        {
            foreach (var mb in t.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name == "InvCharmBackboard")
                {
                    var m = mb.GetType().GetMethod("GetCharmNum"); if (m != null) return (int)m.Invoke(mb, null);
                    var f = mb.GetType().GetField("charmNum"); if (f != null) return (int)f.GetValue(mb);
                }
        }
        catch { }
        return 0;
    }

    // Build the per-clone caches: sorted board list (charm id, backboard transform/renderer, icon object,
    // NATIVE world position + scales — captured HERE, before any layout moves them), the detail text refs,
    // and HK's Notches renderers. One reflection pass per CLONE instead of per layout call.
    void EnsureCharmRefs(GameObject pane)
    {
        if (ReferenceEquals(charmBoardsFor, pane) && charmBoards.Count > 0) return;
        charmBoardsFor = pane; charmBoards.Clear(); costPipRoot = null;   // pips belonged to the old clone
        foreach (var comp in pane.GetComponentsInChildren<Component>(true))
        {
            if (comp == null || comp.GetType().Name != "InvCharmBackboard") continue;
            var b = new CharmBoard { t = comp.transform, n = CharmNumOf(comp.transform) };
            b.br = b.t.GetComponent<Renderer>();
            b.natPos = b.t.position; b.s0 = b.t.localScale;
            try { var f = comp.GetType().GetField("charmObject"); b.icon = f != null ? f.GetValue(comp) as GameObject : null; } catch { }
            if (b.icon != null) b.iconS0 = b.icon.transform.localScale;
            charmBoards.Add(b);
        }
        charmBoards.Sort((a, b) => a.n.CompareTo(b.n));   // fixed charm-ID order = main screen
        charmNameT = FindDeep(pane.transform, "Text Name"); charmNameR = charmNameT != null ? charmNameT.GetComponent<Renderer>() : null;
        charmDescT = FindDeep(pane.transform, "Text Desc"); charmDescR = charmDescT != null ? charmDescT.GetComponent<Renderer>() : null;
        var hkn = FindDeep(pane.transform, "Notches");
        hkNotchRs = hkn != null ? hkn.GetComponentsInChildren<Renderer>(true) : null;
    }

    // Only identity/visibility checks run on healthy ticks. Native state reads and mesh work stay event-bound.
    void CharmActionVisibility(bool available)
    {
        available &= directDisplayActive && cfg.dualScreen != 0 && (compOn >= 0 ? compOn : cfg.companion) == 1 &&
                     cfg.compTouch == 1 && transport != null && transport.IsTransportActive &&
                     tab.cur == COMP_CHARM && slideT >= 1f && charmRun.finalized &&
                     paneClone != null && paneClone == charmCloneCache && paneClone.activeInHierarchy &&
                     attrCam != null && attrCam.enabled && (attrCam.cullingMask & (1 << ATTR_LAYER)) != 0;
        if(charmActionVisible == available && ReferenceEquals(charmActionTransport,transport) &&
           charmActionVisiblePane == paneClone) return;
        charmActionVisible=available;charmActionTransport=transport;charmActionVisiblePane=paneClone;
        charmActionEpoch++;charmActionAttempts=0;CharmActionCancel();
        if(nativeCharmAction != null) SetPaneLabelVisible(nativeCharmAction,available && sel.charmN > 0);
    }
    void CharmActionCancel() { charmActionArmed=false;charmActionItem=null;charmActionDownOwners=default; }
    void CharmActionRetire()
    {
        CharmActionVisibility(false);CharmActionCancel();charmActionAttempts=0;
        if(nativeCharmAction != null && nativeCharmAction.Root != null) Destroy(nativeCharmAction.Root.gameObject);
        nativeCharmAction=null;charmActionPane=null;
        charmActionFeedbackBound=charmActionFeedbackReady=false;
        charmActionFeedbackPane=null;charmActionFeedbackItem=null;charmActionFeedbackNumber=0;
        charmActionAudioPrefab=null;charmActionVibrations=null;
        charmActionTinkClip=charmActionCrackClip=charmActionWindowClip=charmActionBreakClip=null;
    }
    void CharmActionLayout(GameObject pane, CharmState state)
    {
        if(pane != charmActionPane)
        {
            CharmActionRetire();charmActionPane=pane;
        }
        if(lastDetailCharmN <= 0 || sel.kind != 1 || sel.item == null)
        { if(nativeCharmAction != null) SetPaneLabelVisible(nativeCharmAction,false);return; }
        CharmActionBindFeedback(pane);
        if(nativeCharmAction == null) nativeCharmAction=CopyPaneLabel(charmNameT,pane.transform,"CanonicalCharmAction",24);
        if(nativeCharmAction == null || nativeCharmName == null || nativeCharmDesc == null) return;
        // Use the measured dedicated control row, not a camera/grid offset. Cost pips own its left half.
        var name=nativeCharmName.TextRect;var prose=nativeCharmDesc.TextRect;
        charmActionRect=new Rect(name.x+name.width/2,name.y+name.height,name.width/2,
                                 Mathf.Max(0,prose.y-name.y-name.height));
        string key=state.Has(sel.charmN) ? "CTRL_UNEQUIP" : "CTRL_EQUIP";
        SetPaneLabel(nativeCharmAction,NativeText(key,"UI"),charmActionRect,ShellInk);
    }
    bool CharmActionSelectionVisible()
    {
        Bounds bounds;
        return charmActionVisible && nativeCharmAction != null && VisibleItemRenderer(nativeCharmAction.Renderer) &&
               nativeCharmAction.Renderer.gameObject.layer == ATTR_LAYER &&
               sel.kind == 1 && sel.charmN > 0 && sel.charmN <= 40 &&
               selectionMoveShown && selectionMoveT >= 1f && selectionMoveTarget == sel.item &&
               TrySelectionBounds(out bounds) && sel.icon != null && sel.icon.gameObject.layer == ATTR_LAYER;
    }
    bool CharmActionOwnersCurrent(CharmActionInputs input)
    {
        return input.player != null && input.manager != null && input.hero != null && input.upper != null &&
               ReferenceEquals(PlayerData.instance,input.player) && ReferenceEquals(GameManager.instance,input.manager) &&
               ReferenceEquals(HeroController.instance,input.hero) && ReferenceEquals(input.manager.playerData,input.player) &&
               ReferenceEquals(input.hero.playerData,input.player) && ReferenceEquals(input.manager.hero_ctrl,input.hero) &&
               ReferenceEquals(input.manager.inventoryFSM,input.upper) && !input.manager.IsGamePaused() &&
               !input.manager.IsInSceneTransition && input.manager.gameState == GlobalEnums.GameState.PLAYING &&
               input.manager.inventoryFSM.ActiveStateName == "Closed";
    }
    bool CharmActionDown(float x,float y)
    {
        CharmActionCancel();
        if(!CharmActionSelectionVisible() || !charmActionRect.Contains(new Vector2(x,y))) return false;
        try
        {
            var pd=PlayerData.instance;var gm=GameManager.instance;var hero=HeroController.instance;
            var owners=new CharmActionInputs { player=pd,manager=gm,hero=hero,upper=gm != null ? (UnityEngine.Object)gm.inventoryFSM : null };
            if(!CharmActionOwnersCurrent(owners)) return true;
            BuildCharmKeys();charmActionUnequip=pd.GetBool(K_EQ[sel.charmN]);
            if(!CharmActionOwnersCurrent(owners)) return true;
            charmActionDownOwners=owners;charmActionItem=sel.item;charmActionNumber=sel.charmN;
            charmActionDownEpoch=charmActionEpoch;charmActionDownClean=transport.CleanTapSequence;
            charmActionDownTime=Time.unscaledTime;
            charmActionDownPoint=new Vector2(x/BOTTOM_W,y/BOTTOM_H);charmActionArmed=true;
        }
        catch { CharmActionCancel(); }
        return true;
    }
    void CharmActionContact(int contacts)
    {
        if(!charmActionArmed) return;
        if(!CharmActionSelectionVisible() || charmActionDownEpoch != charmActionEpoch || sel.item != charmActionItem ||
           sel.charmN != charmActionNumber || contacts >= 2 || slideT < 1f)
        { CharmActionCancel();return; }
        if(contacts == 1)
        {
            var travel=new Vector2(transport.T0X,transport.T0Y)-charmActionDownPoint;
            if(travel.sqrMagnitude >= .03f*.03f || Time.unscaledTime-charmActionDownTime >= .35f) CharmActionCancel();
        }
    }
    bool CharmActionRelease(float nx,float ny)
    {
        if(!charmActionArmed || transport == null || transport.TouchCount != 0 ||
           transport.CleanTapSequence == charmActionDownClean) return false;
        var owners=charmActionDownOwners;int number=charmActionNumber;
        bool admitted=CharmActionSelectionVisible() && charmActionDownEpoch == charmActionEpoch &&
                      sel.item == charmActionItem && sel.charmN == number &&
                      Time.unscaledTime-charmActionDownTime < .35f &&
                      (new Vector2(nx,ny)-charmActionDownPoint).sqrMagnitude < .03f*.03f &&
                      charmActionRect.Contains(new Vector2(nx*BOTTOM_W,ny*BOTTOM_H));
        // Consume before any native callback; duplicate clean releases can never replay the operation.
        charmActionArmed=false;
        if(admitted) charmActionResult=CharmActionApply(owners,number);
        CharmActionCancel();return true;
    }
    bool CharmActionRead(ref CharmActionInputs input,int number)
    {
        if(!CharmActionOwnersCurrent(input)) return false;
        var pd=input.player;var list=pd.equippedCharms;
        if(list == null) return false;
        BuildCharmKeys();ulong mask=0;long total=0;
        for(int i=0;i<list.Count;i++)
        {
            int n=list[i];if(n <= 0 || n > 40 || (mask & (1UL << n)) != 0) return false;
            mask |= 1UL << n;int cost=pd.GetInt("charmCost_"+n);
            if(cost < 0) return false;total+=cost;
        }
        for(int n=1;n<=40;n++) if(pd.GetBool(K_EQ[n]) != ((mask & (1UL << n)) != 0)) return false;
        input.cost=pd.GetInt("charmCost_"+number);input.slots=pd.charmSlots;input.filled=pd.charmSlotsFilled;
        input.equipped=(mask & (1UL << number)) != 0;input.canOvercharm=pd.canOvercharm;
        return input.cost >= 0 && input.slots >= 0 && input.filled >= 0 && total == input.filled &&
               pd.overcharmed == (input.filled > input.slots) && input.equipped == charmActionUnequip &&
               pd.GetBool(K_GOT[number]) && CharmActionOwnersCurrent(input);
    }
    void CharmActionFeedback(string key,string sheet)
    {
        if(nativeCharmAction != null) SetPaneLabel(nativeCharmAction,NativeText(key,sheet),charmActionRect,ShellMuted);
    }
    // UI Charms 21194 / CharmVibrations 26954: bind only on a selected clone event.
    // The detached FSM remains disabled; only its exact serialized resource references are read.
    void CharmActionBindFeedback(GameObject pane)
    {
        if(charmActionFeedbackBound && charmActionFeedbackPane == pane &&
           charmActionFeedbackItem == sel.item && charmActionFeedbackNumber == sel.charmN) return;
        charmActionFeedbackBound=true;charmActionFeedbackReady=false;
        charmActionFeedbackPane=pane;charmActionFeedbackItem=sel.item;charmActionFeedbackNumber=sel.charmN;
        charmActionAudioPrefab=null;charmActionVibrations=null;
        charmActionTinkClip=charmActionCrackClip=charmActionWindowClip=charmActionBreakClip=null;
        try
        {
            PlayMakerFSM owner=null;
            foreach(var fsm in pane.GetComponents<PlayMakerFSM>())
                if(fsm != null && fsm.FsmName == "UI Charms")
                { if(owner != null) return;owner=fsm; }
            var vibration=pane.GetComponent<CharmVibrations>();
            if(owner == null || vibration == null || owner.FsmStates == null) return;
            int stages=0;
            foreach(var state in owner.FsmStates)
            {
                if(state == null) continue;
                int stage=state.Name == "Tink" ? 1 : state.Name == "Crack 1" ? 2 :
                          state.Name == "Crack 2" ? 4 : state.Name == "Break" ? 8 : 0;
                if(stage == 0) continue;
                if((stages & stage) != 0 || state.Actions == null) return;
                stages |= stage;int sounds=0;
                foreach(var action in state.Actions)
                {
                    var sound=action as HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle;
                    if(sound == null) continue;
                    string clipName=stage == 1 ? "sword_hit_reject" : stage == 8 ? "mage_lord_glass_floor_break" :
                                    sounds == 0 ? "dream_damage" : "sword_hit_window_1";
                    float pitch=stage == 4 && sounds == 0 ? 1.15f : 1f;
                    AudioClip clip;
                    if(!CharmActionBindSound(sound,pane,clipName,pitch,out clip)) return;
                    if(stage == 1) charmActionTinkClip=clip;
                    else if(stage == 8) charmActionBreakClip=clip;
                    else if(sounds == 0)
                    { if(charmActionCrackClip != null && charmActionCrackClip != clip) return;charmActionCrackClip=clip; }
                    else
                    { if(charmActionWindowClip != null && charmActionWindowClip != clip) return;charmActionWindowClip=clip; }
                    sounds++;
                }
                if(sounds != (stage == 1 || stage == 8 ? 1 : 2)) return;
            }
            if(stages != 15) return;
            charmActionVibrations=vibration;charmActionFeedbackReady=true;
        }
        catch { charmActionFeedbackReady=false; }
    }
    bool CharmActionBindSound(object action,GameObject pane,string clipName,float pitch,out AudioClip clip)
    {
        clip=null;
        var sound=action as HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle;
        if(sound == null) return false;
        if(sound.audioPlayer == null || sound.spawnPoint == null || sound.audioClip == null ||
           sound.pitchMin == null || sound.pitchMax == null || sound.volume == null || sound.delay == null ||
           sound.spawnPoint.Value != pane || sound.pitchMin.Value != pitch || sound.pitchMax.Value != pitch ||
           sound.volume.Value != 1f || sound.delay.Value != 0f) return false;
        var prefab=sound.audioPlayer.Value;clip=sound.audioClip.Value as AudioClip;
        if(prefab == null || prefab.name != "Audio Player UI" || clip == null || clip.name != clipName) return false;
        if(charmActionAudioPrefab != null) return charmActionAudioPrefab == prefab;
        // Retain prefab 4127's original lifecycle 25111 and serialized AudioSource 13924/mixer.
        var lifecycle=prefab.GetComponent<PlayAudioAndRecycle>();
        if(lifecycle == null || lifecycle.audioSource == null || lifecycle.audioSource != prefab.GetComponent<AudioSource>()) return false;
        charmActionAudioPrefab=prefab;return true;
    }
    bool CharmActionPlaySound(AudioClip clip,float pitch)
    {
        try
        {
            var instance=ObjectPoolExtensions.Spawn(charmActionAudioPrefab,charmActionFeedbackPane.transform.position,Quaternion.Euler(0f,1f,0f));
            if(instance == null) return false;
            var lifecycle=instance.GetComponent<PlayAudioAndRecycle>();
            var audio=instance.GetComponent<AudioSource>();
            if(lifecycle == null || audio == null || lifecycle.audioSource != audio) return false;
            audio.pitch=pitch;audio.volume=1f;audio.PlayOneShot(clip);return true;
        }
        catch { return false; }
    }
    CharmFeedbackResult CharmActionNativeFeedback(int attempt)
    {
        if(!charmActionFeedbackReady || charmActionFeedbackPane != paneClone || charmActionAudioPrefab == null ||
           charmActionVibrations == null || charmActionTinkClip == null || charmActionCrackClip == null ||
           charmActionWindowClip == null || charmActionBreakClip == null) return CharmFeedbackResult.Unavailable;
        bool played=true;
        if(attempt <= 2) played=CharmActionPlaySound(charmActionTinkClip,1f);
        else if(attempt <= 4)
        {
            played=CharmActionPlaySound(charmActionCrackClip,attempt == 4 ? 1.15f : 1f);
            // A failing first clip cannot suppress its native sibling or the haptic attempt.
            if(!CharmActionPlaySound(charmActionWindowClip,1f)) played=false;
        }
        else played=CharmActionPlaySound(charmActionBreakClip,1f);
        try
        {
            if(attempt <= 2) charmActionVibrations.PlayFailedPlace();
            else if(attempt <= 4) charmActionVibrations.PlayOvercharmHit();
            else charmActionVibrations.PlayOvercharmFinalHit();
        }
        catch { played=false; }
        return played ? CharmFeedbackResult.Played : CharmFeedbackResult.Failed;
    }
    CharmActionResult CharmActionApply(CharmActionInputs input,int number)
    {
        bool writesStarted=false,applied=false,refreshFailed=false;
        CharmFeedbackResult feedback=CharmFeedbackResult.Played;
        try
        {
            if(!CharmActionRead(ref input,number)) return CharmActionResult.Rejected;
            var pd=input.player;
            if(!pd.atBench) { CharmActionFeedback("CHARM_REMINDER","Prompts");return CharmActionResult.Rejected; }
            if(BossSequenceController.BoundCharms) { CharmActionFeedback("CHARM_REMINDER","Prompts");return CharmActionResult.Rejected; }
            if((number >= 23 && number <= 25 && pd.GetBool(K_BROKEN[number])) ||
               (number == 36 && (pd.royalCharmState == 1 || pd.royalCharmState == 2 || (input.equipped && pd.royalCharmState == 4))))
                return CharmActionResult.Rejected;
            if(!input.equipped && input.filled >= input.slots) return CharmActionResult.Rejected;
            long next=(long)input.filled+(input.equipped ? -input.cost : input.cost);
            if(next < 0 || next > int.MaxValue) return CharmActionResult.Rejected;
            bool unlock=!input.equipped && next > input.slots && !input.canOvercharm;
            // Attempts 1/2 tink, 3/4 crack; no reserved native notch write on a rejected lower attempt.
            if(unlock && ++charmActionAttempts < 5)
            {
                feedback=CharmActionNativeFeedback(charmActionAttempts);
                if(feedback == CharmFeedbackResult.Unavailable) return CharmActionResult.FeedbackUnavailable;
                if(feedback == CharmFeedbackResult.Failed) return CharmActionResult.FeedbackFailed;
                return charmActionAttempts <= 2 ? CharmActionResult.Tink : charmActionAttempts == 3 ? CharmActionResult.Crack1 : CharmActionResult.Crack2;
            }
            if(!CharmActionSelectionVisible() || !CharmActionOwnersCurrent(input)) return CharmActionResult.Rejected;
            writesStarted=true;
            if(unlock) pd.canOvercharm=true;
            pd.charmSlotsFilled=(int)next;pd.overcharmed=next > input.slots;
            pd.SetBool(K_EQ[number],!input.equipped);
            if(input.equipped) input.manager.UnequipCharm(number);else input.manager.EquipCharm(number);
            applied=true;
            if(unlock) feedback=CharmActionNativeFeedback(5);
            // Each demonstrated completion effect is attempted once even if a sibling callback fails.
            try { input.hero.CharmUpdate(); } catch { refreshFailed=true; }
            try { PlayMakerFSM.BroadcastEvent("CHARM INDICATOR CHECK"); } catch { refreshFailed=true; }
            try { PlayMakerFSM.BroadcastEvent("UPDATE BLUE HEALTH"); } catch { refreshFailed=true; }
        }
        catch(Exception e)
        {
            Dbg("HKDS charm action "+(writesStarted ? "partial/unconfirmed" : "rejected")+": "+e.Message);
            return writesStarted ? CharmActionResult.Partial : CharmActionResult.Rejected;
        }
        finally
        {
            if(writesStarted)
            {
                InvalidateCharmsFrame();
                try { PopulateCharmDetail(paneClone);if(!charmDetailReady) refreshFailed=true; }
                catch { refreshFailed=true; }
                lastEquipStamp=int.MinValue;
                try
                {
                    UpdateEquipCharmRow(attrCam.orthographicSize,attrCam.aspect);
                    // Observe the existing row owner; do not replay or repair missing sprite donors.
                    if(equipRowRoot != null && cfg.compEquipRow == 1 && benchToastUntil <= Time.unscaledTime && !equipRowReady)
                        refreshFailed=true;
                }
                catch { refreshFailed=true; }
            }
        }
        if(applied && refreshFailed) { Dbg("HKDS charm action applied; refresh failed (no replay)");return CharmActionResult.AppliedRefreshFailed; }
        if(applied && feedback == CharmFeedbackResult.Unavailable) return CharmActionResult.AppliedFeedbackUnavailable;
        if(applied && feedback == CharmFeedbackResult.Failed) return CharmActionResult.AppliedFeedbackFailed;
        return applied ? CharmActionResult.Applied : CharmActionResult.Rejected;
    }

    // [B7] Equip/unequip watch: equipping changes only darkening (not the charm set), so re-lay-out IN PLACE
    // instead of rebuilding the clone (the rebuild path broke the render). Fires only on an equip change.
    void CharmsTick()
    {
        if (tab.cur == COMP_CHARM)
        {
            int eh = Charms().hash;   // frame-cached (was a 40x string-concat + GetBool loop EVERY frame on this tab)
            if (eh != lastCharmEquipHash)
            {
                lastCharmEquipHash = eh;
                // Pending config/language/show work is laid out by this frame's UpdateCompanion.
                // Without pending work, external equip changes still re-darken immediately in place.
                if (!paneNeedsFit) try { ApplyFit(LayoutCharmsRedesign(paneClone)); } catch { }
            }
        }
    }

    // [B7] Charms pane fit: (a) the charm-grid backboards (the clean square grid), then fold in the POPULATED right
    // detail (Text Name/Desc, X-range only) and the equipped-charm row so the whole grid+detail frames like the real menu.
    void CharmsPaneBounds(GameObject go, ref Bounds b, ref bool have, ref int kept)
    {
        // (a) charm-grid backboards — the clean square grid.
        foreach (var comp in go.GetComponentsInChildren<Component>())
        {
            if (comp == null || comp.GetType().Name != "InvCharmBackboard") continue;
            var r = comp.GetComponent<Renderer>() ?? comp.GetComponentInChildren<Renderer>();
            if (r == null || !r.enabled) continue;
            var rb = r.bounds;
            if (rb.size.sqrMagnitude < 1e-8f || rb.size.x > 200f || rb.size.y > 200f) continue;
            if (!have) { b = rb; have = true; } else b.Encapsulate(rb); kept++;
        }
        if (have)
        {
            // CHARM: fold in the POPULATED right detail by its RENDERED extent (Text Name/Desc draw real
            // glyphs now), X-range only, so the whole grid+detail frames and the description's right edge
            // is never cropped. (Anchoring to the transform position under-measured it -> right crop.)
            foreach (var nm in new[] { "Text Name", "Text Desc" })
            {
                var tt = FindDeep(go.transform, nm); var rr = tt != null ? tt.GetComponent<Renderer>() : null;
                if (rr != null && rr.enabled && rr.bounds.size.x > 1e-3f && rr.bounds.size.x < 60f && rr.bounds.size.y < 60f)
                { b.Encapsulate(new Vector3(rr.bounds.min.x, b.center.y, b.center.z)); b.Encapsulate(new Vector3(rr.bounds.max.x, b.center.y, b.center.z)); }
            }
            // Equipped-charm icon row + notch pips (Equipped Charms subtree) so they frame WITH the grid like
            // the real menu. Filter any far stray (a pip sub-sprite HK's charm_cost_indicator FSM parked at y+50).
            var eqC = FindDeep(go.transform, "Equipped Charms");
            if (eqC != null)
                foreach (var r in eqC.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    var rb = r.bounds;
                    if (rb.size.sqrMagnitude < 1e-8f || rb.size.x > 30f || rb.size.y > 30f) continue;
                    if ((rb.center - b.center).sqrMagnitude > 900f) continue;
                    b.Encapsulate(rb);
                }
        }
    }

    // [B7] Charms clone init: the equipped row isn't self-built on a detached clone, and the right-side detail is
    // populated for a default charm (a tapped charm wins later).
    void CharmsPaneInit(GameObject pane)
    {
        var bec = pane.GetComponentInChildren<BuildEquippedCharms>(true);   // equipped row isn't self-built
        if (bec != null) try { bec.SendMessage("BuildCharmList", SendMessageOptions.DontRequireReceiver); } catch { }
        PopulateCharmDetail(pane);            // name+desc on the right (default charm)
    }

    // [B7] Charms clone fingerprint: which charms EXIST (got/broken).
    // NOTE: charmSlots/charmSlotsFilled are intentionally NOT in the stamp — they change on every equip/unequip but
    // don't change which charms EXIST, so they must not force a clone rebuild (that rebuild path left the detail
    // container inactive -> grid/name/detail vanished). Darkening updates via the reuse path's re-layout + the
    // per-frame equip watch instead. equippedCharm is likewise NOT in the stamp (equipping changes only darkening).
    int CharmsStampHash(PlayerData pd, int h)
    {
        for (int i = 1; i <= 40; i++)
        {
            if (CharmGot(pd, i)) h = h * 31 + i;
            if (CharmBroken(pd, i)) h = h * 31 + 293 + i;
        }
        return h;
    }

    // Populate the Charms pane's detail (name + description + cost pips) for the TAPPED charm — or EMPTY when
    // nothing is selected (user: match the Inventory tab, which shows no detail until a tap). compCharmDetailN
    // remains a debug override (>0 forces that id). A detached clone has no live cursor to drive "UI Charms",
    // so the TMP text is set directly through the localization sheet.
    void PopulateCharmDetail(GameObject pane)
    {
        charmDetailReady=false;
        try
        {
            int n = sel.charmN > 0 ? sel.charmN : cfg.compCharmDetailN;   // <=0 -> empty detail (no default charm)
            lastDetailCharmN = n;   // 0 -> LayoutCharmsRedesign draws NO cost pips; >0 -> pips match this charm
            string name=n > 0 ? CharmString("CHARM_NAME_" + n) : "";
            string description=n > 0 ? CharmString("CHARM_DESC_" + n) : "";
            SetTmpTextByName(pane.transform, "Text Name", name);
            SetTmpTextByName(pane.transform, "Text Desc", description);
            // Detail charm ICON (Details/Detail Sprite): its "Update Sprite" FSM defaults ID=0 -> "NO CHARM" ->
            // disables the renderer, so it's blank on the clone. Set the sprite directly + disable its FSMs.
            // (The redesign band hides this icon again — kept for the compCharmsRedesign=0 path.)
            var det = FindDeep(pane.transform, "Detail Sprite");
            if (det != null)
            {
                foreach (var f in det.GetComponents<PlayMakerFSM>()) if (f != null) f.enabled = false;
                var sr = det.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (n > 0) { try { var cil = CharmIconList.Instance; if (cil != null) sr.sprite = cil.GetSprite(n); } catch { } }
                    sr.enabled = n > 0; sr.color = Color.white;
                }
            }
            if(nativeCharmDesc!=null) nativeCharmDesc.ScrollOffset=0;
            var fr=LayoutCharmsRedesign(pane);if(pane==paneClone) { ApplyFit(fr);paneNeedsFit=true; }
            // The shared text setter and layout retain their existing behavior. This owner reports
            // swallowed text writes or an invalid fit rather than treating a nonthrowing call as success.
            charmDetailReady=fr.valid && CharmActionDetailTextMatches(pane,"Text Name",name) &&
                                         CharmActionDetailTextMatches(pane,"Text Desc",description);
        }
        catch (Exception e) { Dbg($"HKDS charm detail err {e.Message}"); }
    }
    bool CharmActionDetailTextMatches(GameObject pane,string name,string expected)
    {
        try
        {
            var tmp=TmpOn(FindDeep(pane.transform,name));
            var property=tmp != null ? tmp.GetType().GetProperty("text") : null;
            return property != null && string.Equals(property.GetValue(tmp,null) as string,expected,StringComparison.Ordinal);
        }
        catch { return false; }
    }

    PaneGraphics nativeCharmGraphics; NativePaneLabel nativeCharmName,nativeCharmDesc;
    Component nativeCharmNameSource,nativeCharmDescSource;
    Transform nativeCharmGrid,equippedCharmNative; Renderer[] nativeCharmGridRenderers;
    SpriteRenderer nativeCharmPortrait; Renderer[] nativeCharmRetired;

    // Canonical Loadout hierarchy with HK's own honeycomb content. The occupied
    // grid alone fits the chooser; selection/prose/legacy JSON never move its target.
    FitResult LayoutCharmsRedesign(GameObject pane)
    {
        if(pane == null || compRoot == null) return default;
        try
        {
            EnsureCharmRefs(pane);
            if(charmBoards.Count == 0) return default;
            var pd=PlayerData.instance;var cs=Charms();
            if(nativeCharmGrid == null || nativeCharmGrid.parent != pane.transform)
            {
                nativeCharmRetired=pane.GetComponentsInChildren<Renderer>(true);
                var root=new GameObject("CanonicalCharmChooser");root.transform.SetParent(pane.transform,false);nativeCharmGrid=root.transform;
                var renderers=new List<Renderer>();
                foreach(var b in charmBoards)
                {
                    b.t.SetParent(nativeCharmGrid,true);
                    renderers.AddRange(b.t.GetComponentsInChildren<Renderer>(true));
                    if(b.icon != null)
                    {
                        b.icon.transform.position=b.t.position-new Vector3(0,0,.02f);
                        b.icon.transform.SetParent(nativeCharmGrid,true);renderers.AddRange(b.icon.GetComponentsInChildren<Renderer>(true));
                    }
                }
                nativeCharmGridRenderers=renderers.ToArray();
                nativeCharmNameSource=TmpOn(charmNameT);nativeCharmDescSource=TmpOn(charmDescT);
                nativeCharmName=CopyPaneLabel(charmNameT,pane.transform,"CanonicalCharmName",37.44f);
                nativeCharmDesc=CopyPaneLabel(charmDescT,pane.transform,"CanonicalCharmDescription",28.08f);
                nativeCharmGraphics=BuildNativePaneGraphics(pane);
                var portrait=FindDeep(pane.transform,"Detail Sprite");nativeCharmPortrait=portrait != null ? portrait.GetComponent<SpriteRenderer>() : null;
                equippedCharmNative=FindDeep(pane.transform,"Equipped Charms");
                notchSprite=null;
                foreach(var sr in pane.GetComponentsInChildren<SpriteRenderer>(true))
                    if(sr != null && sr.gameObject.name=="Sprite Full" && sr.sprite != null) { notchSprite=sr.sprite;break; }
            }
            if(nativeCharmName == null) nativeCharmName=CopyPaneLabel(charmNameT,pane.transform,"CanonicalCharmName",37.44f);
            if(nativeCharmDesc == null) nativeCharmDesc=CopyPaneLabel(charmDescT,pane.transform,"CanonicalCharmDescription",28.08f);
            if(nativeCharmGraphics == null) nativeCharmGraphics=BuildNativePaneGraphics(pane);
            if(nativeCharmName == null || nativeCharmDesc == null || nativeCharmGraphics == null) return default;
            foreach(var b in charmBoards)
            {
                b.t.gameObject.SetActive(true);if(b.br != null) b.br.enabled=true;
                if(b.icon == null) continue;
                bool got=pd == null || b.n<=0 || CharmGot(pd,b.n);b.icon.SetActive(got);
                if(got)
                {
                    float dim=cs.Has(b.n) ? Mathf.Clamp01(cfg.compCharmsDimEquip) : 1;
                    // Cached renderer array is shared by the whole grid; only equip-change/populate reaches here.
                    foreach(var sr in nativeCharmGridRenderers)
                        if(sr is SpriteRenderer sprite && (sprite.transform==b.icon.transform || sprite.transform.IsChildOf(b.icon.transform)))
                        { sprite.enabled=true;sprite.color=new Color(dim,dim,dim,1); }
                }
            }
            var g=LowerGeometry();var p=HKLowerLayout.NativeColumns(g,true);
            // The chooser shares the exact padded column bounds with its masks.
            FitOccupiedNative(nativeCharmGrid,nativeCharmGridRenderers,new Rect(p.ChooserX,p.Top,p.ChooserWidth,p.Height));
            if(nativeCharmRetired!=null) foreach(var r in nativeCharmRetired)
                if(r!=null && r!=nativeCharmPortrait && !r.transform.IsChildOf(nativeCharmGrid) && !IsUnderNamed(r.transform,"Cursor")) r.enabled=false;
            if(charmNameR != null) charmNameR.enabled=false;if(charmDescR != null) charmDescR.enabled=false;
            if(hkNotchRs != null) foreach(var r in hkNotchRs) if(r != null) r.enabled=false;
            if(equippedCharmNative != null) equippedCharmNative.gameObject.SetActive(false);
            PositionNativePaneGraphics(nativeCharmGraphics,true);
            LayoutNativeDetail(nativeCharmName,nativeCharmDesc,nativeCharmNameSource,nativeCharmDescSource,true);
            if(nativeCharmPortrait != null)
            {
                for(var ancestor=nativeCharmPortrait.transform.parent;ancestor!=null && ancestor!=pane.transform;ancestor=ancestor.parent)
                    if(!ancestor.gameObject.activeSelf) ancestor.gameObject.SetActive(true);
                nativeCharmPortrait.enabled=lastDetailCharmN>0 && nativeCharmPortrait.sprite != null;
                FitSprite(nativeCharmPortrait,PanePixel(p.SubjectX+p.SubjectWidth/2,g.BodyCenterY),p.SubjectWidth*.88f,p.Height*.88f);
            }
            int cost=lastDetailCharmN>0 ? Mathf.Max(0,pd != null ? pd.GetInt("charmCost_"+lastDetailCharmN) : 1) : 0;
            if(notchSprite != null && cost>0 && costPipRoot == null)
            {
                var root=new GameObject("HKDS CostPips");root.transform.SetParent(pane.transform,false);costPipRoot=root.transform;
                for(int i=0;i<6;i++) ShellSprite("pip",costPipRoot,notchSprite,100);
            }
            if(costPipRoot != null)
                for(int i=0;i<costPipRoot.childCount;i++)
                {
                    var t=costPipRoot.GetChild(i);t.gameObject.SetActive(i<cost);
                    if(i<cost) FitSprite(t.GetComponent<SpriteRenderer>(),PanePixel(p.DetailX+12+i*26,p.Top+76),20,20);
                }
            CharmActionLayout(pane,cs);
            return new FitResult { center=compRoot.position,ortho=BOTTOM_H/2f,valid=true };
        }
        catch(Exception e) { Dbg("HKDS canonical charms: "+e.Message);return default; }
    }
}
