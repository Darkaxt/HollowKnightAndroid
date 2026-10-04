using System;
using System.Collections.Generic;
using UnityEngine;

// B2: canonical measured shell, five native routes and retained pane lifecycle.
public partial class HKDualScreen
{
    Transform compRoot;            // our own parent (DontDestroyOnLoad); pan = move it, zoom = scale it

    InputHandler lowerHudFixtureInputHandler;
    UnityEngine.EventSystems.EventSystem lowerHudFixtureEventSystem;
    InControl.HollowKnightInputModule lowerHudFixtureInputModule;
    bool lowerHudFixtureInputLockHeld;
    bool lowerHudFixtureInputWasAccepting;
    bool lowerHudFixtureNavigationWasEnabled;
    bool lowerHudFixtureMouseWasEnabled;
    bool lowerHudFixtureInputLockFailureLogged;
    bool lowerHudFixtureInputRestoreFailureLogged;

    GameObject paneClone;          // ALIAS -> the pane clone currently shown (invCloneCache or charmCloneCache)

    GameObject invCloneCache;      // cached Inventory pane clone (built once; SetActive-toggled on tab switch)

    GameObject charmCloneCache;    // cached Charms pane clone (built once; SetActive-toggled)

    UnityEngine.Object paneSrcRef; // identity of the inventoryFSM we cloned the panes from (same purpose)

    int invStamp = int.MinValue;   // PlayerData fingerprint at the inv clone's last (re)build (dirty-gate: rebuild on change)

    int charmStamp = int.MinValue; // PlayerData fingerprint at the charm clone's last (re)build


    // Tab state: cur = the EFFECTIVE tab this frame (a bottom-screen tap wins over cfg.compTab; drives build + fit);
    // built = which tab's clone is currently built/shown (-1 none); tap = the tapped tab (-1 = follow config; a config
    // change clears it); lastCfg = last cfg.compTab seen (edge detect for that clear).
    struct TabState { public int cur, built, tap, lastCfg; }
    TabState tab = new TabState { built = -1, tap = -1, lastCfg = int.MinValue };
    bool paneNeedsFit;             // recompute the Charms/Inv pane fit-to-box next frame

    int compFrameTick;             // throttle for the map auto-frame bounds scan

    // The companion's content FIT (what the box frames): centre + orthographic size + whether a fit exists yet. Owned
    // here (B2); WRITTEN by the map framing burst (B6, via TryMapBounds), the pane fit dispatch (B4/B7, via TryPaneBounds)
    // and LayoutCharmsRedesign (B7, RETURNS one); invalidated on tab build / map availability loss / teardown.
    struct FitResult { public Vector3 center; public float ortho; public bool valid; }
    FitResult fit;
    void ApplyFit(FitResult r) { if (r.valid) fit = r; }


    GameObject frameRoot;
    readonly Dictionary<Transform, Vector3> frameEdge = new Dictionary<Transform, Vector3>();
    readonly Dictionary<Transform, Vector3> frameBase = new Dictionary<Transform, Vector3>();
    readonly SpriteRenderer[] frameTabs = new SpriteRenderer[5];
    readonly Sprite[] tabIcons = new Sprite[5];
    readonly HKLowerLayout.Retry iconRetry = new HKLowerLayout.Retry();
    SpriteRenderer tabTL, tabBR, tabGlow, shellRule;
    Transform mapMaskTopT, mapMaskBotT;
    Renderer mapMaskTopR, mapMaskBotR;
    Transform mapResetT; Component mapResetTmp; Renderer mapResetR;
    SpriteRenderer mapResetPillSR;
    float frameRefOrtho = 8f;
    int tabColorCol = -1, tabFleurMoveCol = -1;
    float tabFleurMoveFromX, tabFleurMoveX, tabFleurMoveT = 1f;
    const float TabCaretMoveSeconds = 0.15f;
    // Kept solely for byte-identical LogoTick compatibility; no obsolete art scan.
    bool fleurBaked = true;
    void TryBakeTabFleurs() { fleurBaked = true; }
    HKLowerLayout.Geometry LowerGeometry() { return HKLowerLayout.Measure(BOTTOM_W, BOTTOM_H); }
    float ShellPixel { get { return attrCam != null ? 2f * attrCam.orthographicSize / Mathf.Max(1, BOTTOM_H) : 1f; } }
    Vector3 ShellPoint(float x, float y, float z = 4f)
    {
        var g = LowerGeometry();
        return attrCam.transform.position + new Vector3((x - g.Width / 2f) * ShellPixel,
            (g.Height / 2f - y) * ShellPixel, z);
    }
    static readonly Color ShellInk = new Color(.93f, .91f, .86f, 1f);
    static readonly Color ShellMuted = new Color(.62f, .60f, .58f, 1f);
    SpriteRenderer ShellSprite(string name, Transform parent, Sprite sprite, int order = 30080)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = ATTR_LAYER;
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sortingLayerName = "Inventory";
        sr.sortingOrder = order; sr.enabled = sprite != null; return sr;
    }
    // Fixed owner slots, not a renderer registry. A fit stores its output affine
    // state and rendered ink; moving a healthy ornament only translates that ink.
    struct ShellSpriteFit
    {
        public SpriteRenderer Renderer; public Sprite Sprite;
        public Bounds SpriteBounds, Ink;
        public Vector3 X,Y,Position,Offset; public float Width,Height;
        public bool Stretch,FlipX,FlipY,Valid;
    }
    readonly ShellSpriteFit[] shellTabFits = new ShellSpriteFit[5];
    ShellSpriteFit shellTLFit,shellBRFit,shellGlowFit;
    static bool SamePoint(Vector3 a,Vector3 b) { return a.x==b.x && a.y==b.y && a.z==b.z; }
    static bool SameBounds(Bounds a,Bounds b) { return SamePoint(a.center,b.center) && SamePoint(a.size,b.size); }
    static void FitShellSprite(SpriteRenderer sr,Vector3 center,float width,float height,ref ShellSpriteFit cache,bool stretch=false)
    {
        if(sr==null || sr.sprite==null) { cache=default;return; }
        width=Mathf.Max(0,width);height=Mathf.Max(0,height);
        var t=sr.transform;var x=t.TransformVector(new Vector3(1,0,0));var y=t.TransformVector(new Vector3(0,1,0));
        var shape=sr.sprite.bounds;
        bool healthy=cache.Valid && cache.Renderer==sr && cache.Sprite==sr.sprite && SameBounds(cache.SpriteBounds,shape) &&
            cache.Width==width && cache.Height==height && cache.Stretch==stretch && cache.FlipX==sr.flipX && cache.FlipY==sr.flipY &&
            SamePoint(cache.X,x) && SamePoint(cache.Y,y);
        if(!healthy)
        {
            var parent=t.parent!=null ? t.parent.lossyScale : Vector3.one;
            t.localScale=new Vector3(1f/Mathf.Max(.001f,Mathf.Abs(parent.x)),1f/Mathf.Max(.001f,Mathf.Abs(parent.y)),1);
            var b=sr.bounds;
            if(stretch)
            {
                // Solve projected x/y coverage without changing signed native
                // parent scales or orientation (including quarter-turn donors).
                x=t.TransformVector(new Vector3(shape.size.x,0,0));y=t.TransformVector(new Vector3(0,shape.size.y,0));
                float a=Mathf.Abs(x.x),c=Mathf.Abs(x.y),d=Mathf.Abs(y.y),e=Mathf.Abs(y.x),det=a*d-e*c;
                float sx=det!=0 ? (width*d-height*e)/det : 0,sy=det!=0 ? (height*a-width*c)/det : 0;
                if(sx<0 || sy<0 || det==0) sx=sy=Mathf.Min(width/Mathf.Max(.001f,b.size.x),height/Mathf.Max(.001f,b.size.y));
                var local=t.localScale;t.localScale=new Vector3(local.x*sx,local.y*sy,local.z);
            }
            else
            {
                float scale=Mathf.Min(width/Mathf.Max(.001f,b.size.x),height/Mathf.Max(.001f,b.size.y));
                var local=t.localScale;t.localScale=new Vector3(local.x*scale,local.y*scale,local.z);
            }
            cache.Ink=sr.bounds;cache.Offset=cache.Ink.center-t.position;
            cache.Renderer=sr;cache.Sprite=sr.sprite;cache.SpriteBounds=shape;cache.Width=width;cache.Height=height;cache.Stretch=stretch;
            cache.FlipX=sr.flipX;cache.FlipY=sr.flipY;
            cache.X=t.TransformVector(new Vector3(1,0,0));cache.Y=t.TransformVector(new Vector3(0,1,0));cache.Valid=true;
        }
        if(!SamePoint(cache.Ink.center,center) || !healthy || !SamePoint(cache.Position,t.position)) { t.position=center-cache.Offset;cache.Ink.center=center; }
        cache.Position=t.position;
    }
    static void PositionShellCursor(SpriteRenderer tl,SpriteRenderer br,SpriteRenderer glow,Bounds target,float pixel,float corner,float baseInset,
        ref ShellSpriteFit tlFit,ref ShellSpriteFit brFit,ref ShellSpriteFit glowFit,bool boundedInset=true)
    {
        float inset=boundedInset ? Mathf.Min((baseInset+12)*pixel,Mathf.Min(target.size.x,target.size.y)/3) : baseInset*pixel;
        FitShellSprite(tl,new Vector3(target.min.x+inset,target.max.y-inset,target.center.z),corner*pixel,corner*pixel,ref tlFit);
        FitShellSprite(br,new Vector3(target.max.x-inset,target.min.y+inset,target.center.z),corner*pixel,corner*pixel,ref brFit);
        FitShellSprite(glow,target.center,target.size.x+24*pixel,target.size.y+24*pixel,ref glowFit,true);
    }
    static void FitSprite(SpriteRenderer sr, Vector3 center, float maxWidth, float maxHeight)
    {
        if (sr == null || sr.sprite == null) return;
        var parent=sr.transform.parent != null ? sr.transform.parent.lossyScale : Vector3.one;
        sr.transform.localScale = new Vector3(1f/Mathf.Max(.001f,Mathf.Abs(parent.x)),1f/Mathf.Max(.001f,Mathf.Abs(parent.y)),1f);
        // Measure after native rotation/reflection, then center the rendered ink,
        // not the sprite's untransformed pivot offset.
        var b=sr.bounds;
        float scale=Mathf.Min(maxWidth/Mathf.Max(.001f,b.size.x),maxHeight/Mathf.Max(.001f,b.size.y));
        var local=sr.transform.localScale;
        sr.transform.localScale=new Vector3(local.x*scale,local.y*scale,local.z);
        sr.transform.position += center-sr.bounds.center;
    }
    // Commissioned production SS divider, copied verbatim; not game/placeholder art.
    const string ShellRulePng = "iVBORw0KGgoAAAANSUhEUgAABEwAAAACCAYAAABCHEm1AAAACXBIWXMAAAsTAAALEwEAmpwYAAABoklEQVRoge1Yy47DMAjEUbX//717iPfQWoumMICTw1bLSFYTmAH8UGN7zDkf8sSUX+hnSdh3MMj7IDaPMxxb1occyyYicjic1Y4gxxFoGV/UuxUniu3ZMrrDiSXK5sWJfKwGVttVf1aLc5Jpei5R760hlgd9Gb0QnQQ2jIF8pre4FU6kY1zUMD3apjzn5uvVGo1Go9H4b/h+tVPev5GIafjYGQJ9lp5xkV/hYEyWw4vj2ay4FsfTISfSn6RO9GXa0kda5r+iZf4z4c/4xMkVxY841THRsbyaMnPJ1kBU14qNmszas353fGK8WxzGv4r0GeLhEHewOuAlZ3zUzEKcTP41Yd7BSQ9+NS/iFPvSxAJOutYOg+P1DZ+rc1Adb6yJ1Zrtxydgtw94AZEdbytfRnvXWHtxcAO1kwPXRXV8LP1Ofnz/1LXZaDQajcZd0PsW/M5WDyy4l4j0Op8+cGX1UUys6a4DmHcgZNz1jP3MaKPLLKav5PuLwD7odeH5dvu9o/MuKaLYen1aFx4esP8n4WbB8mZq2l1jqLH+BypxbtnX/wDHRTzww1ZPywAAAABJRU5ErkJggg==";
    Sprite CreateShellRule()
    {
        var tex = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false));
        if (!tex.LoadImage(Convert.FromBase64String(ShellRulePng))) return null;
        tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
        return Own(Sprite.Create(tex, new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100f));
    }
    readonly HKLowerLayout.Retry frameRetry = new HKLowerLayout.Retry();
    void BuildFrame()
    {
        if (frameRoot != null || attrCam == null || !frameRetry.Due(Time.frameCount)) return;
        int assetsBefore = frameAssets.Count;
        try
        {
            frameRoot = new GameObject("HKCompFrame"); frameRoot.transform.SetParent(attrCam.transform, false);
            frameRefOrtho = attrCam.orthographicSize;
            shellRule = ShellSprite("F_HeaderRule", frameRoot.transform, CreateShellRule(), 30040);
            var inv = GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
            var root = inv != null ? inv.transform.root : null;
            BuildTabRow(root);
            if (root != null) { BuildAreaName(root); if (noMapT == null) BuildNoMapLabel(root); BuildMapControls(root); }
            BuildEquipCharmRow();
            mapMaskTopT = BuildMapMask("HKDS BodyMaskTop"); mapMaskBotT = BuildMapMask("HKDS BodyMaskBottom");
            mapMaskTopR = mapMaskTopT.GetComponent<Renderer>(); mapMaskBotR = mapMaskBotT.GetComponent<Renderer>();
            BuildMapEdges();
            frameRetry.Resolved();
        }
        catch (Exception e)
        {
            // A cold shell failure must not publish its partial root as ready,
            // or tear down healthy content caches/materials belonging to siblings.
            DiscardPartialFrame();
            for (int i = frameAssets.Count - 1; i >= assetsBefore; i--)
            { if (frameAssets[i] != null) Destroy(frameAssets[i]); frameAssets.RemoveAt(i); }
            WarnOnce("frame build", e);
        }
    }
    void DiscardPartialFrame()
    {
        if (frameRoot != null) Destroy(frameRoot);
        frameRoot = null; shellRule = tabTL = tabBR = tabGlow = null;
        for (int i = 0; i < 5; i++) { frameTabs[i] = null;shellTabFits[i]=default; }
        shellTLFit=shellBRFit=shellGlowFit=default;
        iconRetry.Reset();
        mapMaskTopT = mapMaskBotT = null; mapMaskTopR = mapMaskBotR = null;
        areaNameT = null; areaNameTmp = null; areaNameR = null; shellTitle = null;
        noMapT = null; noMapR = null; noMapSymbol = null;noMapFit=default;
        equipRowRoot = null; equipCharmSRs.Clear(); lastEquipStamp = int.MinValue;
        TeardownMapControls();
    }
    void BuildTabRow(Transform root)
    {
        for (int col = 0; col < 5; col++)
            frameTabs[col] = ShellSprite("F_Tab" + col, frameRoot.transform, null);
        ResolveTabDonors(root);
    }
    void CopyShellSpriteOrientation(SpriteRenderer target, SpriteRenderer donor)
    {
        target.transform.localRotation = donor.transform.localRotation;
        target.flipX = donor.flipX != (donor.transform.lossyScale.x < 0);
        target.flipY = donor.flipY != (donor.transform.lossyScale.y < 0);
    }
    void ResolveTabDonors(Transform root)
    {
        // Control readiness is independent of icon/header readiness.
        if (frameRoot != null) BuildMapControls(root);
        if (!iconRetry.Due(Time.frameCount)) return;
        if (root == null)
        {
            var inv = GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
            root = inv != null ? inv.transform.root : null;
        }
        if (root != null)
        {
            var nail = FindDeep(root, "Nail");
            var nativeNail = nail != null ? nail.GetComponent<InvNailSprite>() : null;
            if (nativeNail != null && nativeNail.level1 != null && nativeNail.level1.name == "Inv_0033_inv_nail_01") tabIcons[0] = nativeNail.level1;
            CharmIconList charms = null; try { charms = CharmIconList.Instance; } catch { }
            if (charms != null && charms.spriteList != null && charms.spriteList.Length > 0 &&
                charms.spriteList[0] != null && charms.spriteList[0].name == "charm_sprite_01") tabIcons[1] = charms.spriteList[0];
            var key = FindDeep(root, "Map Key");
            var keys = key != null ? FindDeep(key, "Keys") : null;
            var vendor = keys != null ? FindDeep(keys, "Vendor") : null;
            var pin = vendor != null ? FindDeep(vendor, "Pin Icon") : null;
            var sr = pin != null ? pin.GetComponent<SpriteRenderer>() : null;
            if (sr != null && sr.sprite != null && sr.sprite.name == "pins_combined") tabIcons[2] = sr.sprite;
            var journal = FindDeep(root, "Journal");
            var list = journal != null ? journal.GetComponentInChildren<JournalList>(true) : null;
            if (list != null && list.list != null)
                foreach (var template in list.list)
                {
                    var stats = template != null ? template.GetComponent<JournalEntryStats>() : null;
                    if (stats != null && stats.sprite != null && stats.sprite.name == "bestiary_hunter_mark_f") { tabIcons[3] = stats.sprite; break; }
                }
            var cursor = FindDeep(root, "Cursor");
            var tl = cursor != null ? FindDeep(cursor,"TL") : null;
            var br = cursor != null ? FindDeep(cursor,"BR") : null;
            var glow = cursor != null ? FindDeep(cursor,"Glow") : null;
            if (tabTL == null && tl != null) { var donor = tl.GetComponentInChildren<SpriteRenderer>(true); if(donor != null) { tabTL = ShellSprite("F_TabTL",frameRoot.transform,donor.sprite,30100); CopyShellSpriteOrientation(tabTL,donor); } }
            if (tabBR == null && br != null) { var donor = br.GetComponentInChildren<SpriteRenderer>(true); if(donor != null) { tabBR = ShellSprite("F_TabBR",frameRoot.transform,donor.sprite,30100); CopyShellSpriteOrientation(tabBR,donor); } }
            if (tabGlow == null && glow != null) { var donor = glow.GetComponentInChildren<SpriteRenderer>(true); if(donor != null) { tabGlow = ShellSprite("F_TabGlow",frameRoot.transform,donor.sprite,30070); CopyShellSpriteOrientation(tabGlow,donor); tabGlow.color=donor.color; } }
            if (areaNameT == null) BuildAreaName(root);
            if (noMapT == null) BuildNoMapLabel(root);
            if (mapViewAction == null) BuildMapControls(root);
        }
        // Guarded typed lookup; Map and Quill's renderer is a known stale flower.
        if (tabIcons[3] == null || tabIcons[4] == null)
        {
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var sprite in all)
            {
                if(sprite == null) continue;
                if (sprite.name == "inv_item_map_quill_combined" && sprite.rect.width > 0 && sprite.rect.height > 0) tabIcons[4] = sprite;
            }
            if (tabIcons[3] == null)
            {
                foreach (var stats in Resources.FindObjectsOfTypeAll<JournalEntryStats>())
                    if (stats != null && stats.sprite != null && stats.sprite.name == "bestiary_hunter_mark_f") { tabIcons[3] = stats.sprite; break; }
            }
        }
        bool complete = areaNameT != null && tabTL != null && tabBR != null && tabGlow != null;
        for(int col=0;col<5;col++) { frameTabs[col].sprite = tabIcons[col]; frameTabs[col].enabled = tabIcons[col] != null; complete &= tabIcons[col] != null; }
        if (complete) iconRetry.Resolved();
    }
    float AnimateTabFleurX(int activeCol, float targetX)
    {
        if (tabFleurMoveCol < 0) { tabFleurMoveCol = activeCol; tabFleurMoveX = targetX; tabFleurMoveT = 1f; }
        else if (activeCol != tabFleurMoveCol) { tabFleurMoveCol = activeCol; tabFleurMoveFromX = tabFleurMoveX; tabFleurMoveT = 0f; }
        if(tabFleurMoveT < 1f) tabFleurMoveT = Mathf.Min(1f,tabFleurMoveT + Time.unscaledDeltaTime / TabCaretMoveSeconds);
        tabFleurMoveX = Mathf.Lerp(tabFleurMoveFromX,targetX,tabFleurMoveT); return tabFleurMoveX;
    }
    void PositionFrame()
    {
        if(frameRoot == null || attrCam == null) return;
        ResolveTabDonors(null);
        var g = LowerGeometry(); float unit = ShellPixel;
        frameInnerTopFrac = 1f - 2f * g.HudHeight / g.Height;
        frameInnerBotFrac = 1f - 2f * g.TabTop / g.Height;
        if(shellRule != null && shellRule.sprite != null)
        {
            var size=shellRule.sprite.bounds.size;
            shellRule.transform.localScale = new Vector3((g.Width-40f)*unit/size.x,2f*unit/size.y,1f);
            shellRule.transform.position = ShellPoint(g.Width/2f,g.HudHeight); shellRule.color=ShellInk;
        }
        int activeCol=HKLowerLayout.ColumnForTab(tab.cur);
        bool ownsStrip = mapMarkerMode && tab.cur == COMP_MAP;
        for(int col=0;col<5;col++)
        {
            var sr=frameTabs[col]; if(sr == null) continue;
            sr.enabled = !ownsStrip && sr.sprite != null;
            sr.color = col == activeCol ? Color.white : new Color(1,1,1,.45f);
            FitShellSprite(sr,ShellPoint((col+.5f)*g.CellWidth,g.TabTop+g.TabHeight/2),g.IconMax*unit,g.IconMax*unit,ref shellTabFits[col]);
        }
        var selected=frameTabs[activeCol];
        bool cursorShow = !ownsStrip && selected != null && selected.sprite != null;
        float x=AnimateTabFleurX(activeCol,(activeCol+.5f)*g.CellWidth);
        Bounds art=cursorShow ? shellTabFits[activeCol].Ink : default;
        art.center=ShellPoint(x,g.TabTop+g.TabHeight/2,3.8f);
        if(tabTL != null) tabTL.enabled=cursorShow;
        if(tabBR != null) tabBR.enabled=cursorShow;
        if(tabGlow != null) tabGlow.enabled=cursorShow;
        if(cursorShow) PositionShellCursor(tabTL,tabBR,tabGlow,art,unit,64,0,ref shellTLFit,ref shellBRFit,ref shellGlowFit);
        PositionHudStrip(attrCam.orthographicSize,attrCam.aspect,attrCam.orthographicSize/Mathf.Max(.01f,frameRefOrtho),tab.cur);
        PositionSelection(attrCam.orthographicSize);
        // Fixed HUD and strip clip every pane, including both sliding owners.
        float hudH=g.HudHeight*unit, tabsH=g.TabHeight*unit;
        if(mapMaskTopT != null){ mapMaskTopR.enabled=true; mapMaskTopT.position=ShellPoint(g.Width/2,g.HudHeight/2,4.5f); mapMaskTopT.localScale=new Vector3(g.Width*unit,hudH,1); }
        if(mapMaskBotT != null){ mapMaskBotR.enabled=true; mapMaskBotT.position=ShellPoint(g.Width/2,g.TabTop+g.TabHeight/2,4.5f); mapMaskBotT.localScale=new Vector3(g.Width*unit,tabsH,1); }
        PositionMapEdges();
        PositionMapControls(attrCam.orthographicSize,attrCam.aspect,frameInnerTopFrac,frameInnerBotFrac,tab.cur==COMP_MAP);
    }
    void TeardownFrame()
    {
        CharmActionRetire();
        // Label material sanitizer and rule assets belong to this ownership epoch.
        RetireSupplementaryPanes();
        if(frameRoot != null){ Destroy(frameRoot); frameRoot=null; }
        DestroyOwnedAssets(); frameEdge.Clear(); frameBase.Clear();
        for(int i=0;i<5;i++){ frameTabs[i]=null; tabIcons[i]=null;shellTabFits[i]=default; }
        shellTLFit=shellBRFit=shellGlowFit=default;
        iconRetry.Reset(); frameRetry.Reset(); tabTL=tabBR=tabGlow=shellRule=null;
        mapMaskTopT=mapMaskBotT=null; mapMaskTopR=mapMaskBotR=null; mapResetT=null; mapResetTmp=null; mapResetR=null; mapResetPillSR=null;
        tabColorCol=-1; tabFleurMoveCol=-1; tabFleurMoveT=1f; frameInnerBotFrac=frameInnerTopFrac=float.NaN;
        selBox=null; sel.Clear(); paneCursor=null; paneCursorFor=null;
        selectionTLFit=selectionBRFit=selectionGlowFit=noMapFit=default;
        costPipRoot=null; charmBoardsFor=null;
        nativeCharmGrid=null;nativeCharmGridRenderers=null;nativeCharmRetired=null;nativeCharmName=nativeCharmDesc=null;
        nativeCharmNameSource=nativeCharmDescSource=null;nativeCharmGraphics=null;nativeCharmPortrait=null;equippedCharmNative=null;
        areaNameT=null; areaNameTmp=null; areaNameR=null; lastAreaZoneRaw="\u0001"; lastAreaName="\u0001";
        noMapT=null; noMapR=null; noMapSymbol=null; noMapReady=false; noMapRetry.Reset();
        shellTitle=null; shellTitleToast=false; shellToastText=null; headerSortUntil=headerSortFrame=-1;
        benchPillSR=null; benchPillT=null;
        equipRowRoot=null; equipCharmSRs.Clear(); lastEquipStamp=int.MinValue;
        notchSRs.Clear(); notchTexLit=notchTexEmpty=null; notchSprLitFb=notchSprEmptyFb=null;
        lastNotchTotal=lastNotchUsed=-1; notchSprFull=notchSprEmpty=null; notchScanT=0;
        TeardownMapControls();
    }

    // Procedural white capsule (pill) sprite for the RESET button background. Built once, Own()-tracked.
    Sprite MakePillSprite()
    {
        const int W = 64, H = 32, R = 15;
        var tex = Own(new Texture2D(W, H, TextureFormat.RGBA32, false));
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cx = Mathf.Clamp(x, R, W - 1 - R);   // capsule: distance to the centre SEGMENT
                float dx = x - cx, dy = y - (H - 1) * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)(d <= R - 1f ? 255 : d >= R ? 0 : (byte)((R - d) * 255f));
                px[y * W + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px); tex.Apply(false);
        return Own(Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f));
    }

    // A full-width black quad used to clip the zoomed map at the inner rect's edge (sortingOrder BELOW the
    // raised chrome, ABOVE HK's map sprites). Sized/positioned per frame in PositionFrame; MAP tab only.
    // The caller caches the returned quad's Renderer (mapMaskTopR/BotR) — PositionFrame runs per frame and
    // must not GetComponent there.
    Transform BuildMapMask(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(frameRoot.transform, false);
        go.layer = ATTR_LAYER;
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = Own(new Mesh { name = name });
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds(); mf.mesh = mesh;
        Shader fsh = Shader.Find("Sprites/Default-ColorFlash") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        mr.sharedMaterial = Own(new Material(fsh) { color = Color.black });
        mr.sortingLayerName = "Inventory";   // HK's map/pane sprites ALL live on the topmost "Inventory" sorting layer — a Default-layer mask loses regardless of order
        mr.sortingOrder = 10000;             // above map content (low orders), below the raised chrome
        mr.enabled = false;
        return go.transform;
    }

    // ---- bottom-screen companion (Map / Inventory / Charms) ------------------------------------
    // Additive feature: render a CLONE of HK's real pane onto the reclaimed ATTR layer (attrCam),
    // composited between the backdrop and the untouched HUD. Config-gated (cfg.companion). Cloning
    // (vs reusing HK's panes) keeps us independent of the Inventory Control FSM and of the top-screen
    // menu. Clones are built once and cached (SetActive-toggled on tab switch; rebuilt only when the
    // PlayerData that drives their content changes — see PaneStamp).
    const int COMP_MAP = 0, COMP_INV = 1, COMP_CHARM = 2, COMP_JOURNAL = 3, COMP_GUIDE = 4;

    // Tab-slide animation, mirroring the MAIN screen's pane carousel ("Tween Panes" = two simultaneous
    // iTweenMoveTo: old pane out one side, new pane in from the other), with the camera framing eased between
    // the two tabs' fits. Only ready (finalized) clones slide; a first build shows instantly as before.
    GameObject slideOutClone; float slideT = 1f; int slideDir; Vector3 slideStartCamPos; bool slideCamValid;
    float slideStartOrtho;
    Vector3 slideStartPanePos, slideNormalLocalPos, slideNormalLocalScale;

    // Per-frame slide driver. Runs AFTER ApplyCompanionCamera set the new tab's target framing: the camera is
    // eased from the outgoing tab's captured framing toward it, the incoming clone starts one view-width away in
    // the travel direction, and the outgoing clone (kept alive just for the slide; FSMs frozen) leaves the other
    // side — the same easeOut feel as HK's iTweenMoveTo pane carousel.
    void TabSlideTick()
    {
        if (slideT >= 1f) { if (slideOutClone != null) StowSlideClone(); return; }
        slideT = Mathf.Min(1f, slideT + Time.unscaledDeltaTime / Mathf.Max(0.05f, cfg.compTabSlideTime));
        float e = 1f - Mathf.Pow(1f - slideT, 3f);   // easeOutCubic
        // PURE HORIZONTAL swipe (user: no diagonal). The camera SNAPS to the new tab's framing (no blend — easing
        // it added the two framings' vertical/zoom difference under the slide = the diagonal). The outgoing pane is
        // COMPENSATED by the camera jump so it stays screen-anchored, then leaves horizontally.
        float W = attrCam != null ? attrCam.orthographicSize * attrCam.aspect * 2.15f : 20f;
        var incoming = tab.cur == COMP_MAP ? mapClone : paneClone;
        if (incoming != null && incoming != slideOutClone)
            incoming.transform.position += new Vector3(slideDir * W * (1f - e), 0f, 0f);   // slides IN from the travel side
        if (slideOutClone != null)
        {
            if (!slideOutClone.activeSelf) slideOutClone.SetActive(true);   // BuildCompanionTab stowed it — keep it visible for the slide
            float ratio = slideCamValid && attrCam != null ? attrCam.orthographicSize / Mathf.Max(.001f, slideStartOrtho) : 1f;
            Vector3 camera = attrCam != null ? attrCam.transform.position : slideStartCamPos;
            Vector3 relative = slideStartPanePos - slideStartCamPos;
            // Affine camera-relative compensation preserves every renderer point,
            // not just the root, across native-unit <-> pixel-unit camera snaps.
            slideOutClone.transform.localScale = new Vector3(slideNormalLocalScale.x * ratio, slideNormalLocalScale.y * ratio, slideNormalLocalScale.z);
            slideOutClone.transform.position = new Vector3(camera.x + relative.x * ratio - slideDir * W * e,
                camera.y + relative.y * ratio, slideStartPanePos.z);
        }
        if (slideT >= 1f) StowSlideClone();
    }

    // Finish the slide: put the outgoing clone back into its normal stowed state and pinned position.
    void StowSlideClone()
    {
        if (slideOutClone == null) { slideCamValid = false; return; }
        var cur = tab.cur == COMP_MAP ? mapClone : paneClone;
        if (slideOutClone != cur) slideOutClone.SetActive(false);
        slideOutClone.transform.localPosition = slideNormalLocalPos;
        slideOutClone.transform.localScale = slideNormalLocalScale;
        slideOutClone = null; slideCamValid = false; slideT = 1f;
    }

    // Config hot-reload re-apply. Live per-frame knobs need nothing (they're read every frame). Two classes need help:
    //   * BUILD-TIME knobs (only read inside BuildFrame/Build*: which widgets exist, separators on/off, area name/
    //     no-map on/off) — BuildFrame early-returns while frameRoot exists, so before this an edit was a SILENT no-op
    //     until the frame happened to be torn down. Hash them; on change tear the frame down (rebuilt next frame).
    //   * the Inventory equipment grid offset (compEquipGridDY) — re-lay a finalized INV clone directly (works stowed).
    int frameBuildHash;
    void OnConfigReloaded()
    {
        paneNeedsFit = true;   // re-fit the pane/charms layout instantly (live tuning), then lock again
        int h = 17;
        h = h * 31 + cfg.compSepTop; h = h * 31 + cfg.compSepBot; h = h * 31 + cfg.compAreaName;
        h = h * 31 + cfg.compNoMapMsg; h = h * 31 + cfg.compEquipRow;
        if (h != frameBuildHash) { if (frameBuildHash != 0 && frameRoot != null) { InvalidateCompanionClones(); Dbg("HKDS cfg: build-time frame knob changed -> ownership rebuilt"); } frameBuildHash = h; }
        if (ctrlActive) { ctrlLayoutPending = 1; ctrlLayoutWait = 0; }   // B5: re-measure + re-place the control-prompt line with the new compCtrl* values (live tuning)
        equipGridDirty = true;   // ReassertEquipment re-lays the grid on its next pass...
        // Layout/data mutation is deferred to the admitted unpaused update;
        // LoadConfig itself runs before the pause authority is sampled.
    }

    // our own parent, parked far from the live game world so the clones can't overlap real geometry
    void EnsureCompRoot()
    {
        if (compRoot != null) return;
        var go = new GameObject("HKCompanionRoot");
        DontDestroyOnLoad(go);
        go.transform.position = new Vector3(20000f, 20000f, 0f);   // known point far from live geometry
        compRoot = go.transform;
    }

    // Controlled device-proof seam for lower-frame regressions. It is default-off,
    // accepted only at the title/main menu with the direct-display transport active,
    // and deliberately returns before every gameplay/session hook in Tick(). The
    // fixture uses the production frame and positioning path, never selects a save,
    // starts a session, polls touch, sends gameplay events, or writes PlayerData.
    // Disabling it tears down all owned frame objects before normal orchestration.
    bool TryRunLowerHudFixture(GameCameras cameras, GameManager manager)
    {
        bool atMenu = false;
        try { atMenu = manager != null && manager.gameState == GlobalEnums.GameState.MAIN_MENU; }
        catch { }
        bool requested = cfg.debug == 1 && cfg.compLowerHudFixture == 1 &&
                         directDisplayActive && cfg.dualScreen != 0 && atMenu;
        if (!requested)
        {
            if (lowerHudFixtureActive)
            {
                TeardownCompanion();
                if (attrCam != null) attrCam.cullingMask = 0;
                lowerHudFixtureActive = false;
                Debug.Log("HKDS lower-HUD fixture stopped");
            }
            else if (lowerHudFixtureInputLockHeld)
                ReleaseLowerHudFixtureInputLock();
            if (lowerHudFixtureInputLockHeld)
            {
                BlankLowerHudFixture();
                PushToBottom();
                return true;
            }
            return false;
        }

        if (!lowerHudFixtureActive)
        {
            // A menu transition can leave a fully built gameplay companion in
            // memory. Never reuse it as fixture evidence: destroy all owned
            // state first, then acquire the native menu-input lock, and only
            // then build the production frame from the resident menu donor.
            TeardownCompanion();
            if (!TryAcquireLowerHudFixtureInputLock())
            {
                BlankLowerHudFixture();
                PushToBottom();
                return true;
            }
            lowerHudFixtureActive = true;
            Debug.Log("HKDS lower-HUD fixture active (MAIN_MENU, input-free, save-neutral)");
        }
        else if (!TryAcquireLowerHudFixtureInputLock())
        {
            lowerHudFixtureActive = false;
            TeardownCompanion();
            BlankLowerHudFixture();
            PushToBottom();
            return true;
        }

        StripPrivateLayers();
        if (!ApplyDualScreenToggle())
        {
            PushToBottom();
            return true;
        }

        // Menu-resident native inventory chrome supplies the exact Pane Name TMP
        // donor. Only the real production frame/tab path is exercised; content
        // panes, HUD routing, tutorial hooks, Mods/skins and touch are not ticked.
        TryBakeTabFleurs();
        EnsureCompRoot();
        fit.valid = false;
        tab.cur = HKLowerLayout.NormalizeTab(cfg.compTab);
        ApplyCompanionCamera(compRoot.position);
        BuildFrame();
        PositionFrame();

        if (clearCam != null) clearCam.enabled = true;
        if (attrCam != null) attrCam.cullingMask = 1 << ATTR_LAYER;
        if (hudCam2 != null) hudCam2.cullingMask = 0;
        if (promptCam != null) promptCam.cullingMask = 0;
        if (logoGo != null && logoGo.activeSelf) logoGo.SetActive(false);
        PushToBottom();
        return true;
    }

    void BlankLowerHudFixture()
    {
        if (clearCam != null) clearCam.enabled = true;
        if (attrCam != null) attrCam.cullingMask = 0;
        if (hudCam2 != null) hudCam2.cullingMask = 0;
        if (promptCam != null) promptCam.cullingMask = 0;
        if (logoGo != null && logoGo.activeSelf) logoGo.SetActive(false);
    }

    bool TryAcquireLowerHudFixtureInputLock()
    {
        if (lowerHudFixtureInputLockHeld)
        {
            bool stillLocked = lowerHudFixtureInputHandler != null &&
                               lowerHudFixtureEventSystem != null &&
                               lowerHudFixtureInputModule != null &&
                               !lowerHudFixtureInputHandler.acceptingInput &&
                               !lowerHudFixtureEventSystem.sendNavigationEvents &&
                               !lowerHudFixtureInputModule.allowMouseInput;
            if (stillLocked) return true;
            ReleaseLowerHudFixtureInputLock();
            if (lowerHudFixtureInputLockHeld) return false;
        }

        InputHandler handler = null;
        UnityEngine.EventSystems.EventSystem eventSystem = null;
        InControl.HollowKnightInputModule inputModule = null;
        bool wasAccepting = false, navigationWasEnabled = false, mouseWasEnabled = false;
        bool captured = false;
        try
        {
            handler = InputHandler.Instance;
            eventSystem = UnityEngine.EventSystems.EventSystem.current;
            UIManager ui = null;
            var managers = Resources.FindObjectsOfTypeAll<UIManager>();
            for (int i = 0; i < managers.Length; i++)
                if (managers[i] != null && managers[i].inputModule != null) { ui = managers[i]; break; }
            inputModule = ui != null ? ui.inputModule : null;
            if (handler == null || eventSystem == null || inputModule == null)
                throw new InvalidOperationException("native menu input owners are not resident");

            wasAccepting = handler.acceptingInput;
            navigationWasEnabled = eventSystem.sendNavigationEvents;
            mouseWasEnabled = inputModule.allowMouseInput;
            captured = true;

            handler.acceptingInput = false;
            eventSystem.sendNavigationEvents = false;
            inputModule.allowMouseInput = false;
            if (handler.acceptingInput || eventSystem.sendNavigationEvents || inputModule.allowMouseInput)
                throw new InvalidOperationException("native menu input lock did not hold");

            lowerHudFixtureInputHandler = handler;
            lowerHudFixtureEventSystem = eventSystem;
            lowerHudFixtureInputModule = inputModule;
            lowerHudFixtureInputWasAccepting = wasAccepting;
            lowerHudFixtureNavigationWasEnabled = navigationWasEnabled;
            lowerHudFixtureMouseWasEnabled = mouseWasEnabled;
            lowerHudFixtureInputLockHeld = true;
            lowerHudFixtureInputLockFailureLogged = false;
            lowerHudFixtureInputRestoreFailureLogged = false;
            return true;
        }
        catch (Exception e)
        {
            var restoreFailures = new List<Exception>();
            if (captured)
            {
                try { if (handler != null) handler.acceptingInput = wasAccepting; } catch (Exception re) { restoreFailures.Add(re); }
                try { if (eventSystem != null) eventSystem.sendNavigationEvents = navigationWasEnabled; } catch (Exception re) { restoreFailures.Add(re); }
                try { if (inputModule != null) inputModule.allowMouseInput = mouseWasEnabled; } catch (Exception re) { restoreFailures.Add(re); }
            }
            if (restoreFailures.Count > 0)
            {
                // Rollback did not complete. Retain the exact owners and
                // baselines so the next fixture tick or transport restoration
                // can retry instead of permanently stranding native input.
                lowerHudFixtureInputHandler = handler;
                lowerHudFixtureEventSystem = eventSystem;
                lowerHudFixtureInputModule = inputModule;
                lowerHudFixtureInputWasAccepting = wasAccepting;
                lowerHudFixtureNavigationWasEnabled = navigationWasEnabled;
                lowerHudFixtureMouseWasEnabled = mouseWasEnabled;
                lowerHudFixtureInputLockHeld = true;
                if (!lowerHudFixtureInputRestoreFailureLogged)
                {
                    lowerHudFixtureInputRestoreFailureLogged = true;
                    Debug.LogError(new AggregateException("HKDS lower-HUD fixture acquisition rollback failed", restoreFailures));
                }
            }
            if (!lowerHudFixtureInputLockFailureLogged)
            {
                lowerHudFixtureInputLockFailureLogged = true;
                Debug.LogError("HKDS lower-HUD fixture refused: " + e.Message);
            }
            return false;
        }
    }

    void ReleaseLowerHudFixtureInputLock()
    {
        if (!lowerHudFixtureInputLockHeld) return;
        var failures = new List<Exception>();
        try { if (lowerHudFixtureInputHandler != null) lowerHudFixtureInputHandler.acceptingInput = lowerHudFixtureInputWasAccepting; }
        catch (Exception e) { failures.Add(e); }
        try { if (lowerHudFixtureEventSystem != null) lowerHudFixtureEventSystem.sendNavigationEvents = lowerHudFixtureNavigationWasEnabled; }
        catch (Exception e) { failures.Add(e); }
        try { if (lowerHudFixtureInputModule != null) lowerHudFixtureInputModule.allowMouseInput = lowerHudFixtureMouseWasEnabled; }
        catch (Exception e) { failures.Add(e); }
        if (failures.Count == 0)
        {
            lowerHudFixtureInputLockHeld = false;
            lowerHudFixtureInputHandler = null;
            lowerHudFixtureEventSystem = null;
            lowerHudFixtureInputModule = null;
            lowerHudFixtureInputRestoreFailureLogged = false;
        }
        else if (!lowerHudFixtureInputRestoreFailureLogged)
        {
            lowerHudFixtureInputRestoreFailureLogged = true;
            Debug.LogError(new AggregateException("HKDS lower-HUD fixture input restore failed", failures));
        }
    }

    void ReleaseLowerHudFixtureInputLockOrThrow()
    {
        ReleaseLowerHudFixtureInputLock();
        if (lowerHudFixtureInputLockHeld)
            throw new InvalidOperationException("lower-HUD fixture input restore remains pending");
    }

    // PRE-WARM: deep-cloning HK's ~1000-object panes is the one unavoidable hitch of a first tab open. Do it while the
    // player is NOT looking: as soon as the game session exists (inventoryFSM present = the load fade-in), build the
    // Inventory then the Charms clone, ONE per frame, PARKED far below compRoot (off every camera), and run their settle
    // windows there (PaneSettleTick with isCurrent=false); at settle-end each finalizes and STOWS itself. The first real
    // open then costs nothing (no clone, no 45-frame settle). A clone that stops being current mid-settle is parked (not
    // stowed) so its window completes in the background too — no more blank pane after a quick tab switch.
    bool prewarmDone;
    static readonly Vector3 PARK = new Vector3(0f, -100000f, 0f);   // local offset from compRoot while warming
    void PrewarmTick()
    {
        var gm = GameManager.instance; var invFsm = gm != null ? gm.inventoryFSM : null;
        if (invFsm == null) return;
        // Only once a SESSION is loading/playing: at the main menu the inventory FSM exists but PlayerData is not the
        // save's yet, so a clone made there is stale (its fingerprint mismatches in-game -> rebuilt on first open anyway).
        GlobalEnums.GameState st; try { st = gm.gameState; } catch { return; }
        bool entering = st == GlobalEnums.GameState.ENTERING_LEVEL;   // black screen: load fade-in AND every room transition
        bool playing  = st == GlobalEnums.GameState.PLAYING;
        if (!(entering || playing)) return;
        // 1) tick any warming clone (active, unfinished, not the shown tab)
        if (invCloneCache != null && invCloneCache != paneClone && !invRun.finalized) PaneSettleTick(invCloneCache, invRun, true, false);
        if (charmCloneCache != null && charmCloneCache != paneClone && !charmRun.finalized) PaneSettleTick(charmCloneCache, charmRun, false, false);
        // 2) while the screen is black (level transition), refresh a STALE finished cache (fingerprint changed since it was
        //    built — spell upgrade, new charm, key used...) so the deep re-clone happens here, not on the next tab open.
        if (entering)
        {
            if (invCloneCache != null && invCloneCache != paneClone && invRun.finalized && PaneStamp(false, invStamp) != invStamp) { Destroy(invCloneCache); invCloneCache = null; prewarmDone = false; Dbg("HKDS prewarm: Inventory cache stale -> rebuilding during transition"); }
            if (charmCloneCache != null && charmCloneCache != paneClone && charmRun.finalized && PaneStamp(true, charmStamp) != charmStamp) { Destroy(charmCloneCache); charmCloneCache = null; prewarmDone = false; Dbg("HKDS prewarm: Charms cache stale -> rebuilding during transition"); }
        }
        if (prewarmDone) return;
        // 3) build the missing clones, one deep clone per frame
        EnsureCompRoot();
        if (!ReferenceEquals(paneSrcRef, (UnityEngine.Object)invFsm))
        {   // inventory subtree rebuilt (save-load / scene reload) -> BOTH cached panes are stale (same rule as BuildCompanionTab)
            if (invCloneCache != null) { Destroy(invCloneCache); invCloneCache = null; }
            if (charmCloneCache != null) { Destroy(charmCloneCache); charmCloneCache = null; }
            paneSrcRef = invFsm;
        }
        if (invCloneCache == null)
        {
            var src = FindDeep(invFsm.transform, "Inv"); if (src == null) return;
            invCloneCache = BuildPaneClone(COMP_INV, src); invStamp = PaneStamp(false, invStamp);
            invCloneCache.transform.localPosition = PARK;
            Dbg("HKDS prewarm: Inventory clone built (parked)");
            return;
        }
        if (charmCloneCache == null)
        {
            var src = FindDeep(invFsm.transform, "Charms"); if (src == null) return;
            charmCloneCache = BuildPaneClone(COMP_CHARM, src); charmStamp = PaneStamp(true, charmStamp);
            charmCloneCache.transform.localPosition = PARK;
            Dbg("HKDS prewarm: Charms clone built (parked)");
            return;
        }
        prewarmDone = true;
    }

    void UpdateCompanion(Camera src)
    {
        EnsureCompRoot();

        // Effective tab: a bottom-panel tab-tap (touch) overrides the config tab. Rebuild on change.
        // HK instantiates gameMap lazily (only on first map-open), so keep retrying the Map build until
        // the clone actually exists — then it persists and stays visible thereafter.
        int prevTab = tab.cur;
        tab.cur = HKLowerLayout.NormalizeTab(tab.tap >= 0 ? tab.tap : cfg.compTab);
        SyncSupplementarySource();
        if (tab.cur != prevTab)
        {
            CharmActionVisibility(false);
            SetMapMarkerMode(false);
            // (pinch zoom/pan PERSIST across tab switches — only the RESET button / an area change clears them)
            if (slideOutClone != null) StowSlideClone();   // a switch mid-slide finishes the previous slide instantly
            var fromClone = CloneForTab(prevTab);
            bool fromReady = ReadyForTab(prevTab);
            if (cfg.compTabSlide == 1 && fromReady && prevTab >= 0 && attrCam != null)
            {
                slideOutClone = fromClone; slideT = 0f;
                slideDir = HKLowerLayout.SlideDirection(prevTab, tab.cur);
                slideStartCamPos = attrCam.transform.position;
                slideStartOrtho = attrCam.orthographicSize;
                slideStartPanePos = fromClone.transform.position;
                slideNormalLocalPos = fromClone.transform.localPosition;
                slideNormalLocalScale = fromClone.transform.localScale;
                slideCamValid = true;
            }
            lowerTabGesture.Cancel(); supplementaryDragValid = false;
            if (paneClone != null) HideControlPrompt(paneClone);
            sel.invKey = null; sel.charmN = 0; sel.item = null; sel.kind = -1;   // tab change clears the item selection
            if (invCloneCache != null) ClearInvDetail(invCloneCache);            // ...and blank the cached INV detail (cache reuse skips the settle-clear)
        }
        bool needBuild = tab.built != tab.cur || CloneForTab(tab.cur) == null;
        if (needBuild)
        {
            BuildCompanionTab(tab.cur);
            bool built = (tab.cur == COMP_MAP) ? mapClone != null
                       : CloneForTab(tab.cur) != null;
            if (built) tab.built = tab.cur;   // lock once built; retry next frame if the source wasn't ready
        }
        // Self-heal: guarantee the CURRENT tab's clone is actually shown every frame. A stow/show desync — or an
        // early-return before the SetActive on a transient GameManager/PlayerData null — could otherwise leave a
        // cached clone hidden (the intermittent "charms not rendering"). Cheap: one activeSelf check.
        // fix#3(157-fb): while the opening attribution draws over the live companion, the MAP CONTENT
        // stays stowed (frame/tabs/HUD keep rendering; the credit needs the centre clear) — the self-heal
        // below would otherwise flip it straight back on.
        if (creditNow)
        { if (mapClone != null && mapClone.activeSelf) { mapStowStamp = MapContentStamp(); mapClone.SetActive(false); } }
        else if (tab.cur == COMP_MAP) { if (mapClone != null && !mapClone.activeSelf) mapClone.SetActive(true); }
        else { var shown = CloneForTab(tab.cur); if(shown != null && !shown.activeSelf) shown.SetActive(true); }

        MapTick();   // B6: availability gate + deferred SetupMap + zone/room/compass watchers + Quill/pin watches (EVERY frame — it also clears mapAvailable off the Map tab)

        // FORCED COINCIDENCE (the only framing that reliably rendered): pin the clone at compRoot every
        // frame (overriding SetupMap/GameMap re-positioning), and sit attrCam at compRoot. Auto-frame on
        // the clone's content bounds when available (centres it); else fall back to compRoot (still
        // renders, just off-centre). compZoom = ortho zoom; compOff shifts the view.
        Vector3 frameCenter = compRoot.position;
        if (tab.cur == COMP_MAP && mapClone != null)
        {
            if (mapAvailable) MapFrameTick(ref frameCenter);   // selected map view is ready: pin + fit it
            if (mapAnyAvailable) MapPinchTick();              // keep FULL MAP tappable in a no-current-map zone
        }
        else if ((tab.cur == COMP_CHARM || tab.cur == COMP_INV) && paneClone != null)
        {
            paneClone.transform.localPosition = Vector3.zero;   // pin at compRoot
            PaneSettleTick(paneClone, RunFor(tab.cur == COMP_CHARM), tab.cur == COMP_INV, true);   // B4: open-kicks + settle/freeze window (INV finalize; charms detail re-assert)
            CharmsTick();       // B7: equip/unequip watch -> re-darken the charm grid in place
            if (tab.cur == COMP_INV && invRun.finalized && (Time.frameCount % 30) == 0) RefreshInvCounters(paneClone);   // B4: geo/relic/ore/egg/key numbers stay live (~2x/s)
            if (tab.cur == COMP_INV && invRun.finalized && equipGridDirty) { ReassertEquipment(paneClone);paneNeedsFit=true; }
            if(RunFor(tab.cur==COMP_CHARM).finalized && Time.frameCount%30==0)
            {
                var refs=Refs(paneClone);int language=(int)TeamCherry.Localization.Language.CurrentLanguage();
                if(refs.width!=BOTTOM_W || refs.height!=BOTTOM_H || refs.language!=language)
                {
                    if(refs.language!=language && sel.item!=null) RefreshSelectedDetail(paneClone);
                    paneNeedsFit=true;refs.width=BOTTOM_W;refs.height=BOTTOM_H;refs.language=language;
                }
                if(Time.frameCount%120==0 && ((tab.cur==COMP_INV && !refs.canonicalReady) ||
                    (tab.cur==COMP_CHARM && (nativeCharmName==null || nativeCharmDesc==null || nativeCharmGraphics==null)))) paneNeedsFit=true;
            }
            compFrameTick++;
            if (paneNeedsFit)
            {
                paneNeedsFit = false;
                if (tab.cur == COMP_CHARM)
                    ApplyFit(LayoutCharmsRedesign(paneClone));
                else if(invRun.finalized) { LayoutNativeInventory(paneClone);fit=new FitResult { center=compRoot.position,ortho=BOTTOM_H/2f,valid=true }; }
            }
            if (fit.valid) frameCenter = fit.center;
        }
        if (tab.cur == COMP_JOURNAL || tab.cur == COMP_GUIDE) SupplementaryTick();
        ApplyCompanionCamera(frameCenter);   // B2: box rect/aspect -> per-tab ortho -> position (BEFORE BuildFrame/PositionFrame: they read attrCam.orthographicSize)
        TabSlideTick();                      // B2: HK-style pane slide (offsets BOTH clones + eases the camera between the two tabs' framings)
        BuildFrame(); PositionFrame();
        ReassertControlPrompt();   // re-pin the control-prompt line at pre-render (beats ActionButtonIcon's Update)
        if (cfg.debug == 1 && (Time.frameCount % 90) == 0)   // frame-count gate: compFrameTick only advances on the pane tabs, so on the Map tab the old %90 was true EVERY frame -> logcat flood at debug=1
            Debug.Log($"HKDS mapframe attrPos={attrCam.transform.position} ortho={attrCam.orthographicSize:F1} centered={fit.valid} c={fit.center}");
    }

    // [B2] Companion camera: confine to the BOX (viewport sub-rect, aspect matched so nothing stretches) or
    // full-screen; per-tab zoom (Map=compZoom, Inventory=compPaneZoom, Charms=compCharmZoom) with the frame-fit
    // margin; then position at frameCenter with the per-tab offsets. Must run BEFORE BuildFrame/PositionFrame.
    void ApplyCompanionCamera(Vector3 frameCenter)
    {
        attrCam.orthographic = true;
        attrCam.rect = new Rect(0f,0f,1f,1f);
        attrCam.aspect = (float)BOTTOM_W / Mathf.Max(1,BOTTOM_H);
        if(tab.cur == COMP_INV || tab.cur == COMP_CHARM)
        {
            // Native occupied contents are placed in measured pixel regions;
            // legacy camera/grid nudges and whole-pane zoom are not fit authority.
            attrCam.orthographicSize=BOTTOM_H/2f;
            attrCam.transform.position=compRoot.position-new Vector3(0,0,10);
            attrCam.transform.rotation=Quaternion.identity;
            return;
        }
        if(tab.cur == COMP_JOURNAL || tab.cur == COMP_GUIDE)
        {
            attrCam.orthographicSize = BOTTOM_H / 2f;
            attrCam.transform.position = new Vector3(frameCenter.x,frameCenter.y,frameCenter.z-10f);
            attrCam.transform.rotation = Quaternion.identity;
            return;
        }
        // MAP tab (full-area fit): fit.ortho already has the equal margin against the inner rect baked in; centre the
        // area on the inner rect's vertical centre (mapInnerYc, cam-relative ortho fracs). compZoom>1 zooms in (the area
        // stays centred in the rect). compFrameFit / compMapCenterY do NOT apply here — the margin knob replaces them.
        if (tab.cur == COMP_MAP && mapFitIsArea && fit.valid && mapAvailable)
        {
            float baseOrtho = Mathf.Max(0.5f, fit.ortho / Mathf.Max(0.05f, cfg.compZoom));
            attrCam.orthographicSize = Mathf.Max(0.5f, baseOrtho / mapUserZoom);   // pinch zoom on top of the area fit
            float o = attrCam.orthographicSize;
            // clamp the pan so the view never scrolls past the fitted area's edges (available slack shrinks with zoom)
            float maxPanX = Mathf.Max(0f, baseOrtho * attrCam.aspect * (1f - 1f / mapUserZoom));
            float maxPanY = Mathf.Max(0f, baseOrtho * (1f - 1f / mapUserZoom));
            mapUserPan = new Vector2(Mathf.Clamp(mapUserPan.x, -maxPanX, maxPanX), Mathf.Clamp(mapUserPan.y, -maxPanY, maxPanY));
            attrCam.transform.position = new Vector3(frameCenter.x + cfg.compOffX + mapUserPan.x,
                                                     frameCenter.y - mapInnerYc * o + cfg.compOffY + mapUserPan.y, frameCenter.z - 10f);
            attrCam.transform.rotation = Quaternion.identity;
            return;
        }
        // Fit the pane to the box (fit.ortho sizes to the content bounds). When the context-box frame is on, zoom OUT
        // by compFrameFit so the content sits INSIDE the frame border rather than filling the box edge-to-edge under
        // the ornaments. compZoom>1 zooms in further, <1 out.
        float fitMargin = (cfg.compFrame == 1) ? Mathf.Max(1f, cfg.compFrameFit) : 1f;
        // Per-tab zoom: Map=compZoom, Inventory=compPaneZoom, Charms=compCharmZoom (they need different fills).
        float zoomDiv = tab.cur == COMP_CHARM ? cfg.compCharmZoom : tab.cur == COMP_INV ? cfg.compPaneZoom : cfg.compZoom;
        attrCam.orthographicSize = Mathf.Max(0.5f, (fit.valid ? fit.ortho * fitMargin : 8.71f) / Mathf.Max(0.05f, zoomDiv));
        float ortho = attrCam.orthographicSize;
        // when framed, shift the camera DOWN so the content sits in the UPPER part of the box (inside the frame).
        var geometry = LowerGeometry();
        float mapShift = (1f - 2f * geometry.BodyCenterY / geometry.Height) * ortho;
        attrCam.transform.position = new Vector3(frameCenter.x + cfg.compOffX,
                                                 frameCenter.y + cfg.compOffY - mapShift, frameCenter.z - 10f);
        attrCam.transform.rotation = Quaternion.identity;
    }

    // Fit the pane (Charms/Inventory) to the box. Frame the icon grid PLUS the right-side detail panel
    // (name/desc/cost) so the description is visible — the old fit excluded the detail TMP and culled the far
    // items, zooming into the left grid only. For CHARMS the equipped-charms + notch row is deliberately left
    // OUT (it's pulled to a top-right overlay). HK UI scripts aren't decompiled, so match by name/type.
    bool TryPaneBounds(GameObject go, out Vector3 center, out float fitOrtho, bool charmsGrid)
    {
        center = Vector3.zero; fitOrtho = 8f;
        Bounds b = new Bounds(); bool have = false; int kept = 0, total = 0;
        // Per-tab content measurement (B7 / B4 own their pane's fit rules); the shared tail below turns the bounds
        // into a centre + fit ortho for the box.
        if (charmsGrid) CharmsPaneBounds(go, ref b, ref have, ref kept);
        else if (!InvPaneBounds(go, ref b, ref have, ref kept, ref total)) return false;
        if (!have) return false;
        b.Expand(b.size * 0.06f);   // small margin
        center = b.center;
        float aspect = attrCam != null ? attrCam.aspect : (float)BOTTOM_W / BOTTOM_H;
        var geometry = LowerGeometry();
        fitOrtho = Mathf.Max(b.extents.y * geometry.Height / Mathf.Max(1f,geometry.BodyHeight), b.extents.x / aspect) * 1.05f;
        if (cfg.debug == 1) Dbg($"HKDS paneFit {(charmsGrid ? "CHARM" : "INV")} kept={kept}/{total} ext=({b.extents.x:F1},{b.extents.y:F1}) fitOrtho={fitOrtho:F1}");
        return have;
    }

    // fix#6(161-fb): skins changed — every cached clone holds materials frozen at clone time, so
    // destroy them all; the normal build/prewarm paths re-clone from the now-reskinned sources.
    void InvalidateCompanionClones()
    {
        try
        {
            TeardownCompanion();
            Dbg("HKDS companion clones invalidated (owner boundary)");
        }
        catch (Exception e) { WarnOnce("clone invalidate", e); }
    }

    void BuildCompanionTab(int tab)
    {
        if (compRoot == null) return;

        // Stow the clones we are NOT about to show. cache=1 -> SetActive(false): keep them for instant re-show,
        // so a tab tap never re-deep-clones an 849-object map / 100-FSM pane (the per-switch stutter).
        if (tab != COMP_MAP   && mapClone != null)        { if (mapClone.activeSelf) mapStowStamp = MapContentStamp(); mapClone.SetActive(false); }   // B6: remember what the map showed
        // A clone whose settle window hasn't finished stays ACTIVE (its FSMs must tick) but PARKED off-camera; PaneSettleTick
        // (via PrewarmTick, isCurrent=false) finalizes and stows it when done. Finished clones stow immediately.
        if (tab != COMP_INV   && invCloneCache != null)   { if (invRun.finalized)   invCloneCache.SetActive(false);   else invCloneCache.transform.localPosition = PARK; }
        if (tab != COMP_CHARM && charmCloneCache != null) { if (charmRun.finalized) charmCloneCache.SetActive(false); else charmCloneCache.transform.localPosition = PARK; }
        StowSupplementaryExcept(tab);
        fit.valid = false;

        if (tab == COMP_JOURNAL || tab == COMP_GUIDE) { paneClone = BuildSupplementaryPane(tab); return; }
        if (tab == COMP_MAP)
        {
            paneClone = null;
            var gm = GameManager.instance;
            var srcMap = gm != null ? gm.gameMap : null;   // runtime "Game Map" object (null until built)
            if (srcMap == null) { Dbg("HKDS companion: no gameMap yet"); return; }
            if (mapClone != null && !ReferenceEquals(mapSrcRef, (UnityEngine.Object)srcMap)) { Destroy(mapClone); mapClone = null; mapGm = null; }   // source rebuilt (save-load) -> invalidate cache
            if (mapClone == null) { BuildMapClone(srcMap); mapSrcRef = srcMap; }
            else
            {
                mapClone.SetActive(true);
                // Re-sync the cached map ONLY if the PlayerData its content depends on changed while it was hidden (rest,
                // area, map/quill/pin purchase, compass, markers) — that re-runs the deferred SetupMap + SetupQuickMap.
                // Otherwise the clone is still exact: a plain room move just needs the 'you are here' icon re-anchored
                // (GameMap.Update didn't run while inactive). The full-area fit is per area, so no re-fit either way.
                if (mapContentVisible && MapContentStamp() != mapStowStamp) mapNeedsSetup = true;
                compassPending = true;
                mapAreaBTries = 1;   // one re-measure on show (no-op in area mode; refreshes the legacy fallback fit)
            }
        }
        else if (tab == COMP_CHARM || tab == COMP_INV)
        {
            bool charms = tab == COMP_CHARM;
            var invFsm = GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
            if (invFsm == null) { Dbg("HKDS companion: no inventoryFSM yet"); return; }
            var srcT = FindDeep(invFsm.transform, charms ? "Charms" : "Inv");
            if (srcT == null) { Dbg("HKDS companion: inventory pane not found (retry)"); return; }
            // Inventory subtree rebuilt (save-load / scene reload) -> BOTH cached panes are stale.
            if (!ReferenceEquals(paneSrcRef, (UnityEngine.Object)invFsm))
            {
                if (invCloneCache != null) { Destroy(invCloneCache); invCloneCache = null; }
                if (charmCloneCache != null) { Destroy(charmCloneCache); charmCloneCache = null; }
                paneSrcRef = invFsm; prewarmDone = false;   // let PrewarmTick rebuild the other tab's clone in the background
            }
            GameObject cur = charms ? charmCloneCache : invCloneCache;
            int last = charms ? charmStamp : invStamp;
            int stamp = PaneStamp(charms, last);
            // Rebuild the clone only when it's missing or the PlayerData that drives its content changed
            // (equip/acquire/upgrade). Otherwise the cached clone's icons are still correct -> pure SetActive
            // toggle: no re-clone, no FSM re-run — the smooth path on rapid tab-switching.
            if (cur == null || stamp != last)
            {
                if (cur != null) Destroy(cur);
                cur = BuildPaneClone(tab, srcT);
                if (charms) { charmCloneCache = cur; charmStamp = stamp; }
                else        { invCloneCache = cur;   invStamp = stamp; }
            }
            else
            {
                cur.SetActive(true); paneNeedsFit = true;
                if (charms) PopulateCharmDetail(cur);   // a pre-warmed clone skipped its settle-end detail populate (would have drawn at compRoot while parked) -> set text now; the fit dispatch below re-lays + applies its fit
                else if (invRun.finalized) { ReassertEquipment(cur); RefreshInvCounters(cur); }   // cached INV re-show: equipment visibility (e.g. used Elegant Key) + counters current, no rebuild
            }
            paneClone = cur;
        }
    }

    // Build (Instantiate) a pane clone (Charms/Inv) once: enable the populate FSMs, arm the kick+settle, and for
    // charms populate the right-side detail. Returns the clone; the caller caches it.
    GameObject BuildPaneClone(int tab, Transform srcT)
    {
        bool charms = tab == COMP_CHARM;
        var staging = new GameObject("HKPaneCloneStaging");
        staging.SetActive(false);
        GameObject pane = null;
        try
        {
            // Instantiate below an inactive parent so no cloned behaviour gets
            // Awake/OnEnable before the resident pane has been sanitized. The
            // live inventory hierarchy is never deactivated or otherwise
            // mutated by this operation.
            pane = Instantiate(srcT.gameObject, staging.transform);
            pane.SetActive(false);
            pane.name = charms ? "HKCharmClone" : "HKInvClone";
            pane.transform.localPosition = Vector3.zero;
            SetLayerRecursive(pane.transform, ATTR_LAYER);
            // Runtime iTween instances carry launch arguments that are valid
            // only for the source menu. Their Awake dereferences those stale
            // arguments when a clone becomes active, so remove those visual
            // animation drivers before first activation.
            foreach (var tween in pane.GetComponentsInChildren<MonoBehaviour>(true))
                if (tween != null && tween.GetType().Name == "iTween") DestroyImmediate(tween);
            // POPULATE FSMs read PlayerData + lay out the dynamic lists — keep enabled (INV is kicked below). NAV/INPUT
            // FSMs fight the real menu -> stay disabled. "UI Charms" idles (enabled, not kicked). This is the
            // known-good enabled set (kicking/enabling more re-lays the charm grid to 67u).
            foreach (var fsm in pane.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                string n = fsm.FsmName;
                if (charms) fsm.enabled = (n == "UI Charms" || n == "charm_show_if_collected");
                else        fsm.enabled = (n == "Check Active" || n == "Build Equipment List" || n == "Set Pieces");
            }
            pane.transform.SetParent(compRoot, false);
            pane.SetActive(true);
            if (!charms) InvPaneInit(pane);      // B4: soul-vessel renderers on
            else         CharmsPaneInit(pane);   // B7: equipped row + default detail (needs an ACTIVE pane: it reads renderer bounds)
            paneNeedsFit = true;
            var run = RunFor(charms); run.finalized = false;
            run.kicks = charms ? 0 : 8;   // kick only INV's populate FSMs; charms need no kick (more kicks: let Build Equipment List finish activating the ability/key/consumable row, not just the trinkets)
            run.settle = charms ? 20 : 45;   // populate, THEN freeze all pane FSMs (stops the per-frame NRE leak source); INV needs longer for the equipment row to build
            if (cfg.debug == 1) Dbg($"HKDS pane clone built tab={tab} renderers={pane.GetComponentsInChildren<Renderer>(true).Length}");
            return pane;
        }
        finally { Destroy(staging); }
    }

    // Fingerprint the PlayerData that drives a pane's content, so the cached clone is rebuilt only when it changes.
    // Cheap: a handful of ints/bools read once, only on a tab SHOW. On failure returns an ever-changing value so
    // the caller rebuilds (correct, just not cached that time).
    int PaneStamp(bool charms, int fallback)
    {
        var pd = PlayerData.instance; if (pd == null) return fallback;   // transient PD-null (scene load) -> "unchanged", no rebuild
        int h = 17;
        try
        {
            h = charms ? CharmsStampHash(pd, h) : InvStampHash(pd, h);   // B7 / B4 own what drives their clone's content
        }
        catch { return fallback; }
        return h;
    }

    void RetireCompanionCaches()
    {
        CharmActionRetire();
        StowSlideClone();
        TeardownMapRenderRoles();
        if (mapClone != null) { Destroy(mapClone); mapClone = null; }
        if (invCloneCache != null) { Destroy(invCloneCache); invCloneCache = null; }
        if (charmCloneCache != null) { Destroy(charmCloneCache); charmCloneCache = null; }
        paneClone = null;
        // TeardownFrame owns supplementary labels/materials and resets retries
        // exactly once. A failed build never uses this owner-retirement path.
        TeardownFrame();
        tab.built = -1;
        iconRetry.Reset();
    }

    void TeardownCompanion()
    {
        bool attrCameraWasEnabled = attrCam != null && attrCam.enabled;
        bool hudCameraWasEnabled = hudCam2 != null && hudCam2.enabled;
        if (attrCam != null) { attrCam.enabled = false; attrCam.cullingMask = 0; }
        if (hudCam2 != null) hudCam2.enabled = false;
        if (attrCam != null) attrCam.cullingMask = 0;
        ReleaseLowerHudFixtureInputLock();
        RetireCompanionCaches();
        mapGm = null; mapContentVisible = false; mapAreaBValid = false; mapAreaBFor = null; mapFitIsArea = false;
        lowerTabGesture.Cancel(); supplementaryDragValid = false; lowerTouchDownBody = false;
        slideOutClone = null; slideT = 1f; slideCamValid = false;   // any in-flight tab slide dies with the clones
        refsInv = refsCharm = null;   // B4 PaneRefs caches die with their clones
        nudgedFocusFor = null; nudgedFocusAmt = 0f;   // don't pin a destroyed clone
        // Control-prompt overlay: its glyph/verb live under compRoot (NOT frameRoot), so they survive the
        // teardown — without this reset a session quit with a prompt showing left a stray PRESS/HOLD glyph
        // rendering into the next session, re-pinned against a destroyed pane.
        ctrlActive = false; ctrlSrcIconT = null; ctrlHkRs = null; ctrlHkRsFor = null; ctrlPlaced = false; ctrlLayoutPending = 0; ctrlGlyphPending = false;
        ctrlTnR = ctrlTdR = null; ctrlTdT = null;
        try { if (ctrlMyGlyph != null) ctrlMyGlyph.enabled = false; } catch { }
        try { if (ctrlMyVerbTmp != null) { var vr = (ctrlMyVerbTmp as Component).GetComponent<Renderer>(); if (vr != null) vr.enabled = false; } } catch { }
        prewarmDone = false;
        mapSrcRef = null; paneSrcRef = null; invStamp = int.MinValue; charmStamp = int.MinValue;
        supplementarySource = null; supplementaryPlayer = null; supplementaryMapSource = null;
        if (attrCam != null) attrCam.enabled = attrCameraWasEnabled;
        if (hudCam2 != null)
            hudCam2.enabled = directDisplayActive && hudCameraWasEnabled;
        tab.built = -1;
        fit.valid = false;
    }
}
