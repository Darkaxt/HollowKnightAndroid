using System;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker.Actions;
using Bounds = UnityEngine.Bounds;

// Detached, read-only native Journal / Map Key adapters. Only copied TMP graphics
// and SpriteRenderers are owned here; native list, entry and FSM drivers never run.
public partial class HKDualScreen
{
    GameObject journalCloneCache, guideCloneCache;
    UnityEngine.Object supplementarySource;
    PlayerData supplementaryPlayer;
    UnityEngine.Object supplementaryMapSource;
    readonly HKLowerLayout.Retry journalRetry = new HKLowerLayout.Retry(), guideRetry = new HKLowerLayout.Retry();
    int supplementaryNextRefresh, supplementaryLanguage = -1;
    float supplementaryWidth, supplementaryHeight;
    bool supplementaryDragValid, journalHadBook, journalFilled; float supplementaryDragY;
    int supplementaryDragRegion;
    int journalScrollRow, guideScrollRow, journalSelected = -1, guideSelected = -1;
    readonly List<JournalRecord> journalRecords = new List<JournalRecord>();
    readonly List<int> journalVisible = new List<int>();
    readonly List<SpriteRenderer> journalCells = new List<SpriteRenderer>();
    SpriteRenderer journalPortrait;
    NativePaneLabel journalName, journalDescription, journalNotes, journalState;
    NativePaneLabel guideDetail, guideState;
    readonly List<GuideRecord> guideRecords = new List<GuideRecord>();
    Func<string,bool> guideRead; PlayerData guideReadPlayer;
    readonly List<int> guideVisible = new List<int>();
    TMProOld.TMP_FontAsset shellCapsFont;

    sealed class JournalRecord
    {
        public Sprite Sprite;
        public string KilledKey, KillsKey, NameKey, DescKey, NotesKey;
        public bool Killed; public int Remaining;
    }
    sealed class GuideRecord
    {
        public SpriteRenderer Icon; public NativePaneLabel Label;
        public string Condition, Key, Sheet, Text;
        public bool Visible;
    }
    PaneGraphics journalGraphics, guideGraphics;
    sealed class PaneGraphics
    {
        public SpriteRenderer RuleLeft,RuleRight,TL,BR,Glow;
        public Renderer Top,Bottom,Left,Right;
        public bool Selected; public int SelectedId=-1;
        public Rect Target;
        public Vector2 From,Center; public float Travel=1f; public int CursorFrame=-1;
        public int Language=-1; public float Width,Height;
        public ShellSpriteFit TLFit,BRFit,GlowFit;
    }
    sealed class NativePaneLabel
    {
        public Transform Root; public Component Tmp, Container; public Renderer Renderer;
        public Renderer[] ClipRenderers; public readonly MaterialPropertyBlock ClipBlock=new MaterialPropertyBlock();
        public Rect ClipRect, TextRect;
        public float ScrollOffset, ScrollMax;
        public float UnitScale; public string Text = "\u0001";
    }
    GameObject CloneForTab(int id)
    {
        switch(HKLowerLayout.NormalizeTab(id))
        {
            case COMP_INV: return invCloneCache;
            case COMP_CHARM: return charmCloneCache;
            case COMP_JOURNAL: return journalCloneCache;
            case COMP_GUIDE: return guideCloneCache;
            default: return mapClone;
        }
    }
    bool ReadyForTab(int id)
    {
        var clone = CloneForTab(id);
        if(clone == null) return false;
        if(id == COMP_INV) return invRun.finalized;
        if(id == COMP_CHARM) return charmRun.finalized;
        return true;
    }
    void StowSupplementaryExcept(int id)
    {
        if(id != COMP_JOURNAL && journalCloneCache != null) journalCloneCache.SetActive(false);
        if(id != COMP_GUIDE && guideCloneCache != null) guideCloneCache.SetActive(false);
    }
    void SyncSupplementarySource()
    {
        var manager = GameManager.instance;
        var inv = manager != null ? manager.inventoryFSM : null;
        var map = manager != null ? manager.gameMap : null;
        var pd = PlayerData.instance;
        int skin = HkStageHooks.SkinStamp;
        if(!ReferenceEquals(supplementarySource,(UnityEngine.Object)inv) ||
           !ReferenceEquals(supplementaryPlayer,pd) ||
           !ReferenceEquals(supplementaryMapSource,(UnityEngine.Object)map) || skin != lastSkinStamp)
        {
            // Identity admission precedes every refresh, even while paused. One
            // correlated boundary retires all five caches, not five independent
            // refresh paths which could display the preceding save's artwork.
            if(mapClone != null || invCloneCache != null || charmCloneCache != null ||
               journalCloneCache != null || guideCloneCache != null || frameRoot != null)
                InvalidateCompanionClones();
            supplementarySource = inv; supplementaryPlayer = pd; supplementaryMapSource = map;
            lastSkinStamp = skin;
            iconRetry.Reset();
            for(int i=0;i<5;i++) tabIcons[i]=null;
            tab.built=-1;
        }
    }
    // A failed attempt discards only that pane. It deliberately leaves the
    // pending donor deadline and the healthy sibling's presentation untouched.
    void DiscardSupplementaryPane(int id)
    {
        var failed = id == COMP_JOURNAL ? journalCloneCache : guideCloneCache;
        if(failed != null && slideOutClone == failed) StowSlideClone();
        if(failed != null && paneClone == failed) paneClone=null;
        if(failed != null) Destroy(failed);
        if(id == COMP_JOURNAL)
        {
            journalCloneCache=null; journalRecords.Clear(); journalVisible.Clear(); journalCells.Clear();
            journalPortrait=null; journalName=journalDescription=journalNotes=journalState=null;
            journalSelected=-1; journalScrollRow=0; journalGraphics=null; journalHadBook=journalFilled=false;
        }
        else
        {
            guideCloneCache=null; guideRecords.Clear(); guideVisible.Clear(); guideRead=null; guideReadPlayer=null;
            guideDetail=guideState=null; guideSelected=-1; guideScrollRow=0; guideGraphics=null;
        }
        if(tab.built == id) tab.built=-1;
    }
    void RetireSupplementaryPanes()
    {
        DiscardSupplementaryPane(COMP_JOURNAL);
        DiscardSupplementaryPane(COMP_GUIDE);
        supplementarySource=null; supplementaryPlayer=null; supplementaryMapSource=null;
        journalRetry.Reset(); guideRetry.Reset();
        supplementaryNextRefresh=0; supplementaryLanguage=-1; supplementaryWidth=supplementaryHeight=0;
        supplementaryDragValid=false; shellCapsFont=null;
    }
    Vector3 PanePixel(float x,float y,float z=0f)
    {
        var g=LowerGeometry(); return compRoot.position + new Vector3(x-g.Width/2,g.Height/2-y,z);
    }
    // Only a native text child is copied below inactive staging. No enclosing
    // Journal/Map Key/template GO, FadeGroup, input FSM or JournalEntryStats.
    NativePaneLabel CopyPaneLabel(Transform donor,Transform parent,string name,float pixels,bool caps=false)
    {
        if(donor == null) return null;
        var staging=new GameObject("HKReadOnlyTextStaging"); staging.SetActive(false);
        GameObject go=null;
        try
        {
            go=Instantiate(donor.gameObject,staging.transform); go.SetActive(false); go.name=name;
            foreach(var driver in go.GetComponentsInChildren<MonoBehaviour>(true))
                if(driver != null && !IsTextMeshProGraphic(driver) && !(driver is TMProOld.TextContainer)) DestroyImmediate(driver);
            SanitizeDetachedTmpClone(go); SetLayerRecursive(go.transform,ATTR_LAYER);
            go.transform.SetParent(parent,false);
            go.transform.localPosition=Vector3.zero; go.transform.localRotation=Quaternion.identity; go.transform.localScale=Vector3.one;
            go.SetActive(true);
            Component tmp=TmpOn(go.transform);
            if(tmp == null) { Destroy(go); return null; }
            if(caps && shellCapsFont == null)
                foreach(var font in Resources.FindObjectsOfTypeAll<TMProOld.TMP_FontAsset>())
                    if(font != null && font.name.IndexOf("Trajan",StringComparison.OrdinalIgnoreCase)>=0){ shellCapsFont=font; break; }
            if(caps && shellCapsFont != null)
            {
                TmpProp(tmp,"font")?.SetValue(tmp,shellCapsFont,null);
                TmpProp(tmp,"fontSharedMaterial")?.SetValue(tmp,shellCapsFont.material,null);
            }
            var label=new NativePaneLabel { Root=go.transform,Tmp=tmp,Renderer=tmp.GetComponent<Renderer>() };
            foreach(var c in go.GetComponents<Component>()) if(c != null && c.GetType().Name=="TextContainer") { label.Container=c; break; }
            SetTmpFont(go.transform,40f);
            TmpProp(tmp,"text")?.SetValue(tmp,"M",null);
            TmpProp(tmp,"enableWordWrapping")?.SetValue(tmp,true,null);
            var overflow=TmpProp(tmp,"overflowMode");
            if(overflow != null && overflow.PropertyType.IsEnum) overflow.SetValue(tmp,Enum.Parse(overflow.PropertyType,"Overflow"),null);
            var alignment=TmpProp(tmp,"alignment");
            if(alignment != null) alignment.SetValue(tmp,Enum.Parse(alignment.PropertyType,"TopLeft"),null);
            tmp.GetType().GetMethod("ForceMeshUpdate",Type.EmptyTypes)?.Invoke(tmp,null);
            NeutralizeDetachedTmpClip(go);
            if(label.Renderer == null) { Destroy(go); return null; }
            // Projected pixel metrics calibrated once against the native cap,
            // rather than transplanting SS's incompatible TMPro sizes/ABI.
            Vector3 inkMin,inkMax;
            if(!TryTmpGlyphBoundsWorld(go.transform,out inkMin,out inkMax) || inkMax.y-inkMin.y<=.001f)
            { Destroy(go); return null; } // exact legacy ink ABI unavailable: retry, never calibrate padded container bounds
            label.UnitScale=pixels/(inkMax.y-inkMin.y);
            go.transform.localScale=Vector3.one*label.UnitScale;
            foreach(var r in go.GetComponentsInChildren<Renderer>(true)){ r.sortingLayerName="Inventory"; r.sortingOrder=100; }
            SetPaneLabel(label,"",new Rect(0,0,1,1),ShellInk);
            return label;
        }
        catch(Exception e){ if(go != null) Destroy(go); WarnOnce("read-only text donor",e); return null; }
        finally{ Destroy(staging); }
    }
    // Borrow only the shell's already-owned quad/material and native sprites.
    // No native cursor/FSM/clip driver is ever copied or activated.
    Renderer CopyPaneMask(Transform parent,string name)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false); go.layer=ATTR_LAYER;
        go.AddComponent<MeshFilter>().sharedMesh=mapMaskTopT.GetComponent<MeshFilter>().sharedMesh;
        var r=go.AddComponent<MeshRenderer>(); r.sharedMaterial=mapMaskTopR.sharedMaterial;
        r.sortingLayerName="Inventory"; r.sortingOrder=10000; return r;
    }
    bool BuildPaneGraphics(GameObject go,int id)
    {
        if(shellRule == null || shellRule.sprite == null || tabTL == null || tabBR == null || tabGlow == null ||
           tabTL.sprite == null || tabBR.sprite == null || tabGlow.sprite == null || mapMaskTopT == null || mapMaskTopR == null) return false;
        var quad=mapMaskTopT.GetComponent<MeshFilter>();
        if(quad == null || quad.sharedMesh == null || mapMaskTopR.sharedMaterial == null) return false;
        var v=new PaneGraphics();
        if(id==COMP_JOURNAL) journalGraphics=v; else guideGraphics=v;
        v.RuleLeft=ShellSprite("BodyGutterLeft",go.transform,shellRule.sprite,110);
        if(id==COMP_JOURNAL) v.RuleRight=ShellSprite("BodyGutterRight",go.transform,shellRule.sprite,110);
        v.TL=ShellSprite("BodyCursorTL",go.transform,tabTL.sprite,150);
        v.TL.transform.localRotation=tabTL.transform.localRotation; v.TL.flipX=tabTL.flipX; v.TL.flipY=tabTL.flipY;
        v.BR=ShellSprite("BodyCursorBR",go.transform,tabBR.sprite,150);
        v.BR.transform.localRotation=tabBR.transform.localRotation; v.BR.flipX=tabBR.flipX; v.BR.flipY=tabBR.flipY;
        v.Glow=ShellSprite("BodyCursorGlow",go.transform,tabGlow.sprite,90);
        v.Glow.transform.localRotation=tabGlow.transform.localRotation; v.Glow.flipX=tabGlow.flipX; v.Glow.flipY=tabGlow.flipY;
        v.Glow.color=tabGlow.color;
        v.Top=CopyPaneMask(go.transform,"BodyClipTop"); v.Bottom=CopyPaneMask(go.transform,"BodyClipBottom");
        v.Left=CopyPaneMask(go.transform,"BodyClipLeft"); v.Right=CopyPaneMask(go.transform,"BodyClipRight");
        PositionPaneGraphics(v,id); SetPaneSelection(v,default,false,-1); PaneCursorTick(v);
        return true;
    }
    void PositionPaneGraphics(PaneGraphics v,int id)
    {
        if(v == null) return;
        var g=LowerGeometry(); float sx=g.Width/1240f,margin=id==COMP_JOURNAL ? 16f : 20f;
        float top=g.HudHeight+margin, bottom=g.TabTop-margin, height=Mathf.Max(1,bottom-top);
        PositionPaneRule(v.RuleLeft,(id==COMP_JOURNAL ? 415f : 820f)*sx,top,height);
        if(v.RuleRight != null) { v.RuleRight.enabled=id==COMP_JOURNAL; if(id==COMP_JOURNAL) PositionPaneRule(v.RuleRight,875*sx,top,height); }
        PositionPaneMask(v.Top,new Rect(0,0,g.Width,top));
        PositionPaneMask(v.Bottom,new Rect(0,bottom,g.Width,g.Height-bottom));
        PositionPaneMask(v.Left,new Rect(0,top,20*sx,height));
        PositionPaneMask(v.Right,new Rect(g.Width-20*sx,top,20*sx,height));
    }
    void PositionPaneRule(SpriteRenderer rule,float x,float top,float height)
    {
        if(rule == null || rule.sprite == null) return;
        var size=rule.sprite.bounds.size;
        rule.transform.localRotation=Quaternion.Euler(0,0,90);
        rule.transform.localScale=new Vector3(height/Mathf.Max(.001f,size.x),2f/Mathf.Max(.001f,size.y),1);
        rule.transform.position=PanePixel(x,top+height/2); rule.color=ShellInk;
    }
    void PositionPaneMask(Renderer mask,Rect rect)
    {
        if(mask == null) return;
        mask.enabled=true;
        mask.transform.position=PanePixel(rect.x+rect.width/2,rect.y+rect.height/2,.02f);
        mask.transform.localScale=new Vector3(rect.width,rect.height,1);
    }
    void SetPaneSelection(PaneGraphics v,Rect rect,bool show,int selected)
    {
        if(v == null) return;
        var target=new Vector2(rect.x+rect.width/2,rect.y+rect.height/2);
        if(show && (!v.Selected || v.SelectedId<0)) { v.Center=target; v.Travel=1; }
        else if(show && v.SelectedId!=selected)
        { v.From=v.Center; v.Travel=0; }
        v.Selected=show; v.SelectedId=show ? selected : -1; v.Target=rect;
    }
    void PaneCursorTick(PaneGraphics v)
    {
        if(v == null) return;
        v.TL.enabled=v.BR.enabled=v.Glow.enabled=v.Selected;
        if(!v.Selected) return;
        if(v.CursorFrame != Time.frameCount)
        {
            v.Travel=Mathf.Min(1,v.Travel+Time.unscaledDeltaTime/SelectionMoveSeconds);
            v.CursorFrame=Time.frameCount;
        }
        var target=new Vector2(v.Target.x+v.Target.width/2,v.Target.y+v.Target.height/2);
        if(v.Travel<1) v.Center=new Vector2(Mathf.Lerp(v.From.x,target.x,v.Travel),Mathf.Lerp(v.From.y,target.y,v.Travel));
        else v.Center=target;
        var bounds=new Bounds(PanePixel(v.Center.x,v.Center.y),new Vector3(Mathf.Max(0,v.Target.width),Mathf.Max(0,v.Target.height),0));
        PositionShellCursor(v.TL,v.BR,v.Glow,bounds,1,64,0,ref v.TLFit,ref v.BRFit,ref v.GlowFit);
    }
    void SetPaneLabelVisible(NativePaneLabel label,bool shown)
    {
        if(label == null) return;
        bool enabled=shown && label.Text.Length>0;
        if(label.Renderer != null) label.Renderer.enabled=enabled;
        if(label.ClipRenderers != null)
            foreach(var r in label.ClipRenderers) if(r != null) r.enabled=enabled;
    }
    void ApplyPaneLabelClip(NativePaneLabel label,Rect rect)
    {
        label.ClipRect=rect;
        if(label.ClipRenderers == null) label.ClipRenderers=label.Root.GetComponentsInChildren<Renderer>(true);
        // Exact HK TextMeshProClipRect ABI: bounds in TMP ROOT local space,
        // applied to all generated fallback/submesh renderers with the same block.
        var min=label.Root.InverseTransformPoint(PanePixel(rect.x,rect.y+rect.height));
        var max=label.Root.InverseTransformPoint(PanePixel(rect.x+rect.width,rect.y));
        var bounds=new Vector4(min.x,min.y,max.x,max.y);
        foreach(var r in label.ClipRenderers)
        {
            if(r == null) continue;
            r.gameObject.layer=ATTR_LAYER; r.sortingLayerName="Inventory"; r.sortingOrder=100;
            r.enabled=label.Text.Length>0;
            label.ClipBlock.Clear(); r.GetPropertyBlock(label.ClipBlock);
            label.ClipBlock.SetVector(TMP_CLIP_RECT,bounds); r.SetPropertyBlock(label.ClipBlock);
        }
    }
    void SetPaneLabel(NativePaneLabel label,string text,Rect rect,Color color)
    {
        if(label == null || label.Root == null) return;
        text=text ?? "";
        float scale=label.UnitScale;
        label.Root.localScale=Vector3.one*scale;
        bool resized=label.TextRect.width!=rect.width || label.TextRect.height!=rect.height;
        label.TextRect=rect;
        if(label.Container != null && resized) TcSetSize(label.Container,new Vector2(rect.width/Mathf.Max(.001f,scale),rect.height/Mathf.Max(.001f,scale)));
        if(label.Text != text || resized)
        {
            label.Text=text; TmpProp(label.Tmp,"text")?.SetValue(label.Tmp,text,null);
            label.Tmp.GetType().GetMethod("ForceMeshUpdate",Type.EmptyTypes)?.Invoke(label.Tmp,null);
            label.ClipRenderers=label.Root.GetComponentsInChildren<Renderer>(true);
        }
        SetTmpColor(label.Tmp,color); label.Renderer.enabled=text.Length>0;
        if(text.Length>0)
        {
            // Anchor the real generated glyph ink to the top-left of its column.
            label.Root.position=PanePixel(rect.x,rect.y);
            Vector3 inkMin,inkMax;
            bool haveInk=TryTmpGlyphBoundsWorld(label.Root,out inkMin,out inkMax);
            var b=label.Renderer.bounds;
            if(!haveInk) { inkMin=b.min; inkMax=b.max; }
            label.ScrollMax=Mathf.Max(0,inkMax.y-inkMin.y-rect.height);
            label.ScrollOffset=Mathf.Clamp(label.ScrollOffset,0,label.ScrollMax);
            label.Root.position += new Vector3(PanePixel(rect.x,rect.y).x-inkMin.x,PanePixel(rect.x,rect.y).y-inkMax.y+label.ScrollOffset,0);
        }
        ApplyPaneLabelClip(label,rect);
    }
    static string NativeText(string key,string sheet)
    {
        if(string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sheet)) return "";
        try{ return TeamCherry.Localization.Language.Has(key,sheet) ? (TeamCherry.Localization.Language.Get(key,sheet) ?? "").Replace("<br>","\n") : ""; }
        catch{ return ""; }
    }
    GameObject BuildSupplementaryPane(int id)
    {
        var existing=CloneForTab(id); if(existing != null){ existing.SetActive(true); supplementaryNextRefresh=0; return existing; }
        if(supplementarySource == null || compRoot == null || PlayerData.instance == null) return null;
        if(id != COMP_JOURNAL && id != COMP_GUIDE) return null;
        try
        {
            BuildFrame(); // shell quad/native graphic donors are required owned inputs
            return id == COMP_JOURNAL ? BuildJournalPane() : BuildGuidePane();
        }
        catch(Exception e)
        {
            // Cache assignment occurs before fallible donor/data/mesh work. Never
            // admit that partial owner through the existing-cache fast path.
            DiscardSupplementaryPane(id);
            WarnOnce("read-only pane build",e);
            return null;
        }
    }
    GameObject BuildJournalPane()
    {
        if(!journalRetry.Due(Time.frameCount)) return null;
        var inv=GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
        var root=inv != null ? FindDeep(inv.transform.root,"Journal") : null;
        var list=root != null ? root.GetComponentInChildren<JournalList>(true) : null;
        var donor=root != null ? FindDeep(root,"Text Name") : null;
        var prose=root != null ? FindDeep(root,"Text Desc") : null;
        if(list == null || list.list == null || donor == null || prose == null) return null;
        var go=new GameObject("HKJournalReadOnly"); go.transform.SetParent(compRoot,false);
        journalCloneCache=go;
        if(!BuildPaneGraphics(go,COMP_JOURNAL)) { DiscardSupplementaryPane(COMP_JOURNAL); return null; }
        foreach(var template in list.list)
        {
            var stats=template != null ? template.GetComponent<JournalEntryStats>() : null;
            if(stats == null || string.IsNullOrEmpty(stats.playerDataName) || string.IsNullOrEmpty(stats.convoName)) continue;
            journalRecords.Add(new JournalRecord { Sprite=stats.sprite,
                KilledKey=HKLowerLayout.JournalKilledKey(stats.playerDataName), KillsKey=HKLowerLayout.JournalKillsKey(stats.playerDataName),
                NameKey=HKLowerLayout.JournalNameKey(stats.convoName), DescKey=HKLowerLayout.JournalDescriptionKey(stats.convoName),
                NotesKey=HKLowerLayout.JournalNotesKey(stats.convoName) });
        }
        journalPortrait=ShellSprite("JournalPortrait",go.transform,null,100);
        journalName=CopyPaneLabel(donor,go.transform,"JournalName",40,true);
        journalDescription=CopyPaneLabel(prose,go.transform,"JournalDescription",30);
        journalNotes=CopyPaneLabel(prose,go.transform,"JournalNotes",30);
        journalState=CopyPaneLabel(donor,go.transform,"JournalState",40,true);
        if(journalRecords.Count == 0 || journalName == null || journalDescription == null || journalNotes == null || journalState == null)
        { DiscardSupplementaryPane(COMP_JOURNAL); return null; }
        supplementaryNextRefresh=0; RefreshJournal(true);
        journalRetry.Resolved(); return go;
    }
    void RefreshJournal(bool force)
    {
        var pd=PlayerData.instance; if(pd == null || journalCloneCache == null) return;
        bool fill=pd.GetBool("fillJournal");
        bool changed=force || journalHadBook != pd.hasJournal || journalFilled != fill;
        journalHadBook=pd.hasJournal; journalFilled=fill; journalVisible.Clear();
        for(int i=0;i<journalRecords.Count;i++)
        {
            var record=journalRecords[i]; bool killed=pd.GetBool(record.KilledKey);
            int remaining=pd.GetInt(record.KillsKey);
            changed |= killed != record.Killed || remaining != record.Remaining;
            record.Killed=killed; record.Remaining=remaining;
            if(pd.hasJournal && (killed || fill)) journalVisible.Add(i);
        }
        if(!pd.hasJournal && journalSelected>=0){ journalSelected=-1; changed=true; }
        if(journalSelected>=0 && !journalVisible.Contains(journalSelected)){ journalSelected=-1; changed=true; }
        if(!changed) return;
        LayoutJournal();
    }
    void BindJournalLabels(JournalRecord record,Rect stateRect,Rect nameRect,Rect descRect,Rect notesRect)
    {
        // HK has no localized UNKNOWN/EMPTY/LOCKED Journal keys. Use the native
        // item name with language-independent state glyphs/counts, never invented
        // English fallback keys. Undiscovered entries remain excluded as in the
        // native JournalList; '?' is the unselected read-only detail state.
        string state="";
        if(record == null)
        {
            string detail=!journalHadBook ? "—" : journalVisible.Count==0 ? "0 / "+journalRecords.Count : "?";
            state=NativeText("INV_NAME_JOURNAL","UI")+"\n"+detail;
        }
        bool notes=record != null && (journalFilled || HKLowerLayout.NotesUnlocked(record.Killed,record.Remaining));
        string noteText=record == null ? "" : notes ? NativeText(record.NotesKey,"Journal") :
            NativeText("KILL_COUNT_1","Journal")+" "+Mathf.Max(0,record.Remaining)+" "+NativeText("KILL_COUNT_2","Journal");
        SetPaneLabel(journalState,state,stateRect,ShellMuted);
        SetPaneLabel(journalName,record != null ? NativeText(record.NameKey,"Journal") : "",nameRect,ShellInk);
        SetPaneLabel(journalDescription,record != null ? NativeText(record.DescKey,"Journal") : "",descRect,ShellInk);
        SetPaneLabel(journalNotes,noteText,notesRect,notes ? ShellInk : ShellMuted);
    }
    void LayoutJournal()
    {
        var g=LowerGeometry(); float sx=g.Width/1240f, top=g.HudHeight+16, height=Mathf.Max(1,g.BodyHeight-32);
        float cell=(380*sx-52*sx)/3, pitch=cell+26*sx;
        PositionPaneGraphics(journalGraphics,COMP_JOURNAL);
        Rect selection=default; bool selectionShown=false;
        int rows=Mathf.Max(1,Mathf.FloorToInt(height/pitch)), count=rows*3;
        journalScrollRow=Mathf.Clamp(journalScrollRow,0,Mathf.Max(0,(journalVisible.Count+2)/3-rows));
        while(journalCells.Count<count) journalCells.Add(ShellSprite("JournalCell"+journalCells.Count,journalCloneCache.transform,null,100));
        for(int slot=0;slot<journalCells.Count;slot++)
        {
            int item=journalScrollRow*3+slot;
            var sr=journalCells[slot]; bool shown=slot<count && item<journalVisible.Count;
            sr.sprite=shown ? journalRecords[journalVisible[item]].Sprite : null; sr.enabled=shown && sr.sprite != null;
            sr.color=Color.white; // Native content art; the cursor owns selected treatment.
            if(shown)
            {
                FitSprite(sr,PanePixel(20*sx+(slot%3)*pitch+cell/2,top+(slot/3)*pitch+cell/2),cell*.9f,cell*.9f);
                if(journalVisible[item]==journalSelected)
                { selection=new Rect(20*sx+(slot%3)*pitch,top+(slot/3)*pitch,cell,cell); selectionShown=true; }
            }
        }
        SetPaneSelection(journalGraphics,selection,selectionShown,journalSelected); PaneCursorTick(journalGraphics);
        bool selected=journalSelected>=0 && journalSelected<journalRecords.Count;
        var record=selected ? journalRecords[journalSelected] : null;
        journalPortrait.sprite=record != null ? record.Sprite : null; journalPortrait.enabled=selected && journalPortrait.sprite != null;
        FitSprite(journalPortrait,PanePixel(645*sx,top+height/2),382*sx,Mathf.Max(1,height-48));
        float descTop=Mathf.Min(top+66,top+height);
        float proseHeight=Mathf.Max(0,top+height-descTop);
        float gap=Mathf.Min(12,proseHeight);
        float descHeight=(proseHeight-gap)*.52f;
        BindJournalLabels(record,new Rect((journalVisible.Count>0 ? 890 : 20)*sx,top,(journalVisible.Count>0 ? 330 : 380)*sx,height),new Rect(890*sx,top,330*sx,Mathf.Min(56,height)),
            new Rect(890*sx,descTop,330*sx,descHeight),new Rect(890*sx,descTop+descHeight+gap,330*sx,proseHeight-descHeight-gap));
    }
    void JournalTap(float nx,float ny)
    {
        var g=LowerGeometry(); float x=nx*g.Width, y=ny*g.Height;
        float sx=g.Width/1240f, cell=(380*sx-52*sx)/3, pitch=cell+26*sx;
        float top=g.HudHeight+16,height=Mathf.Max(1,g.BodyHeight-32);
        int rows=Mathf.Max(1,Mathf.FloorToInt(height/pitch));
        int col=Mathf.FloorToInt((x-20*sx)/pitch), row=Mathf.FloorToInt((y-top)/pitch);
        if(col<0 || col>=3 || row<0 || row>=rows || x>400*sx || y>=g.TabTop-16) return;
        int item=(journalScrollRow+row)*3+col;
        if(item>=journalVisible.Count) return;
        ResetJournalDetailScroll(); journalSelected=journalVisible[item]; LayoutJournal();
    }
    static readonly string[] GuideRows = { "Bench","Key Stag","Tram","Spa","Vendor","Dreamer","Dream Plant","Cocoon","Ghost","Grub","Black Egg" };
    static readonly string[] GuideConditions = { "hasPinBench","hasPinStag","hasPinTram","hasPinSpa","hasPinShop","hasPinGuardian","hasPinDreamPlant","hasPinCocoon","hasPinGhost","hasPinGrub","hasPinBlackEgg" };
    static readonly string[] GuideStates = { "Check Bench","Check Stag","Check Tram","Check Spa","Check Vendor","Check Dreamer","Check Dream Plant","Check Cocoon","Check Ghost","Check Grub","Check Black Egg" };
    static readonly string[] GuideVariables = { "Pin Bench","Pin Stag","Pin Tram","Pin Spa","Pin Vendor","Pin Dreamer","Pin Dream Plant","Pin Cocoon","Pin Ghost","Pin Grub","Pin Black Egg" };
    string GuideRowCondition(Transform root,Transform row,int index)
    {
        // Exact resources.assets Control FSM: Draw Pins tests hasPin (false
        // -> NO PIN); each Check state tests its acquired pin (false -> FINISHED).
        // The Pin * variable binds that state to the serialized native row.
        if(root == null || row == null || index<0 || index>=GuideRows.Length) return null;
        foreach(var fsm in root.GetComponents<PlayMakerFSM>())
        {
            if(fsm == null || fsm.FsmName != "Control" || fsm.FsmVariables == null || fsm.FsmStates == null) continue;
            var binding=fsm.FsmVariables.FindFsmGameObject(GuideVariables[index]);
            if(binding == null || binding.Value != row.gameObject) continue;
            bool gate=false; string condition=null;
            foreach(var state in fsm.FsmStates)
            {
                if(state == null || state.Actions == null) continue;
                foreach(var action in state.Actions)
                {
                    var test=action as PlayerDataBoolTest;
                    if(test == null || test.boolName == null || test.isFalse == null) continue;
                    if(state.Name == "Draw Pins" && test.boolName.Value == "hasPin" && test.isFalse.Name == "NO PIN") gate=true;
                    if(state.Name == GuideStates[index] && test.boolName.Value == GuideConditions[index] && test.isFalse.Name == "FINISHED") condition=test.boolName.Value;
                }
            }
            if(gate) return condition;
        }
        return null;
    }
    GameObject BuildGuidePane()
    {
        if(!guideRetry.Due(Time.frameCount)) return null;
        var inv=GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
        var root=inv != null ? FindDeep(inv.transform.root,"Map Key") : null;
        var keys=root != null ? FindDeep(root,"Keys") : null;
        if(keys == null) return null;
        var go=new GameObject("HKGuideReadOnly"); go.transform.SetParent(compRoot,false); guideCloneCache=go;
        if(!BuildPaneGraphics(go,COMP_GUIDE)) { DiscardSupplementaryPane(COMP_GUIDE); return null; }
        Transform prose=null;
        for(int i=0;i<GuideRows.Length;i++)
        {
            var row=FindDeep(keys,GuideRows[i]); if(row == null) continue;
            var pin=FindDeep(row,"Pin Icon"); var donor=FindDeep(row,"Text");
            var sr=pin != null ? pin.GetComponent<SpriteRenderer>() : null;
            var localized=donor != null ? donor.GetComponent<SetTextMeshProGameText>() : null;
            if(sr == null || sr.sprite == null || donor == null || localized == null) continue;
            if(string.IsNullOrEmpty(localized.convName) || string.IsNullOrEmpty(localized.sheetName) || NativeText(localized.convName,localized.sheetName).Length==0)
            { DiscardSupplementaryPane(COMP_GUIDE); return null; }
            string condition=GuideRowCondition(root,row,i);
            if(condition == null)
            {
                WarnOnce("native Guide condition",new InvalidOperationException("Map Key Control condition unavailable: "+GuideRows[i]));
                DiscardSupplementaryPane(COMP_GUIDE); return null;
            }
            var label=CopyPaneLabel(donor,go.transform,"GuideRow"+i,30);
            if(label == null) continue;
            prose=donor;
            guideRecords.Add(new GuideRecord { Icon=ShellSprite("GuidePin"+i,go.transform,sr.sprite,100),Label=label,
                Condition=condition,Key=localized.convName,Sheet=localized.sheetName });
        }
        if(prose == null || guideRecords.Count != GuideRows.Length){ DiscardSupplementaryPane(COMP_GUIDE); return null; }
        guideDetail=CopyPaneLabel(prose,go.transform,"GuideDetail",30);
        guideState=CopyPaneLabel(prose,go.transform,"GuideState",40,true);
        if(guideDetail == null || guideState == null){ DiscardSupplementaryPane(COMP_GUIDE); return null; }
        supplementaryNextRefresh=0; RefreshGuide(true); guideRetry.Resolved(); return go;
    }
    void RefreshGuide(bool force)
    {
        var pd=PlayerData.instance; if(pd == null || guideCloneCache == null) return;
        if(!ReferenceEquals(guideReadPlayer,pd)) { guideReadPlayer=pd; guideRead=pd.GetBool; }
        bool changed=force; guideVisible.Clear();
        for(int i=0;i<guideRecords.Count;i++)
        {
            var record=guideRecords[i];
            if(force) record.Text=NativeText(record.Key,record.Sheet);
            bool visible=pd.GetBool("hasPin") && HKLowerLayout.GuideVisible(record.Condition,guideRead) && !string.IsNullOrEmpty(record.Text);
            changed |= visible != record.Visible; record.Visible=visible;
            if(visible) guideVisible.Add(i);
        }
        if(guideSelected>=0 && !guideVisible.Contains(guideSelected)){ guideSelected=-1; changed=true; }
        if(changed) LayoutGuide();
    }
    void LayoutGuide()
    {
        var g=LowerGeometry(); float sx=g.Width/1240f, top=g.HudHeight+20, height=Mathf.Max(1,g.BodyHeight-40);
        const float pitch=66;
        PositionPaneGraphics(guideGraphics,COMP_GUIDE);
        Rect selection=default; bool selectionShown=false;
        int rows=Mathf.Max(1,Mathf.FloorToInt(height/pitch));
        guideScrollRow=Mathf.Clamp(guideScrollRow,0,Mathf.Max(0,guideVisible.Count-rows));
        foreach(var record in guideRecords){ record.Icon.enabled=false; SetPaneLabelVisible(record.Label,false); }
        for(int slot=0;slot<rows && guideScrollRow+slot<guideVisible.Count;slot++)
        {
            int index=guideVisible[guideScrollRow+slot]; var record=guideRecords[index];
            record.Icon.enabled=true; record.Icon.color=index==guideSelected ? ShellInk : ShellMuted;
            FitSprite(record.Icon,PanePixel(60*sx,top+slot*pitch+pitch/2),64*sx,52);
            SetPaneLabel(record.Label,record.Text,new Rect(110*sx,top+slot*pitch+12,670*sx,54),index==guideSelected ? ShellInk : ShellMuted);
            if(index==guideSelected) { selection=new Rect(20*sx,top+slot*pitch,780*sx,pitch); selectionShown=true; }
        }
        SetPaneSelection(guideGraphics,selection,selectionShown,guideSelected); PaneCursorTick(guideGraphics);
        SetPaneLabel(guideState,guideVisible.Count==0 ? LocalizedLabel("Map Key","PANE_MAP_KEY") : "",new Rect(20*sx,top,780*sx,height),ShellMuted);
        SetPaneLabel(guideDetail,guideSelected>=0 ? guideRecords[guideSelected].Text : "",new Rect(840*sx,top,380*sx,height),ShellInk);
    }
    void GuideTap(float nx,float ny)
    {
        var g=LowerGeometry(); if(nx*g.Width<20*g.Width/1240f || nx*g.Width>800*g.Width/1240f) return;
        float y=ny*g.Height,top=g.HudHeight+20,height=Mathf.Max(1,g.BodyHeight-40);
        int rows=Mathf.Max(1,Mathf.FloorToInt(height/66));
        int slot=Mathf.FloorToInt((y-top)/66);
        int item=guideScrollRow+slot;
        if(slot<0 || slot>=rows || y>=g.TabTop-20 || item>=guideVisible.Count) return;
        guideSelected=guideVisible[item]; LayoutGuide();
    }
    void SupplementaryTick()
    {
        var current=CloneForTab(tab.cur); if(current == null) return;
        current.transform.localPosition=Vector3.zero;
        var visuals=tab.cur==COMP_JOURNAL ? journalGraphics : guideGraphics;
        PaneCursorTick(visuals);
        if(Time.frameCount<supplementaryNextRefresh) return;
        supplementaryNextRefresh=Time.frameCount+30;
        int language=(int)TeamCherry.Localization.Language.CurrentLanguage();
        bool force=language != supplementaryLanguage || supplementaryWidth != BOTTOM_W || supplementaryHeight != BOTTOM_H ||
            visuals == null || visuals.Language != language || visuals.Width != BOTTOM_W || visuals.Height != BOTTOM_H;
        supplementaryLanguage=language; supplementaryWidth=BOTTOM_W; supplementaryHeight=BOTTOM_H;
        if(tab.cur==COMP_JOURNAL) { if(force) ResetJournalDetailScroll(); RefreshJournal(force); }
        else if(tab.cur==COMP_GUIDE) RefreshGuide(force);
        // Stowed siblings keep their own language/geometry admission. Updating
        // this owner must not make a stale sibling look current on its next show.
        if(visuals != null) { visuals.Language=language; visuals.Width=BOTTOM_W; visuals.Height=BOTTOM_H; }
    }
    void ResetJournalDetailScroll()
    {
        if(journalDescription != null) journalDescription.ScrollOffset=0;
        if(journalNotes != null) journalNotes.ScrollOffset=0;
    }
    void ScrollSupplementary(float delta)
    {
        if(Mathf.Abs(delta)<.04f) return;
        int direction=delta<0 ? 1 : -1;
        if(tab.cur==COMP_JOURNAL)
        {
            if(supplementaryDragRegion==1 || supplementaryDragRegion==2)
            {
                var label=supplementaryDragRegion==1 ? journalDescription : journalNotes;
                if(label != null) { label.ScrollOffset=Mathf.Clamp(label.ScrollOffset-delta*BOTTOM_H,0,label.ScrollMax); LayoutJournal(); }
            }
            else if(supplementaryDragRegion==0) { journalScrollRow+=direction; LayoutJournal(); }
        }
        else if(tab.cur==COMP_GUIDE){ guideScrollRow+=direction; LayoutGuide(); }
        supplementaryDragY=transport.T0Y;
    }
}
