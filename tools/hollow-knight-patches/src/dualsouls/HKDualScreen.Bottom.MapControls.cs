using System;
using System.Collections.Generic;
using UnityEngine;

// Canonical production DsShell Map presentation on HK's existing native map.
// Typed PlayerData/GameMap actions stay below; controller input stays in game.
public partial class HKDualScreen
{
    sealed class MapActionButton
    {
        public Transform Root;
        public Component Label;
        public Renderer LabelRenderer;
        public SpriteRenderer Plate;
        public NativePaneLabel Graphic;
        public Bounds Hit, Ink;
        public bool InkReady;
        public readonly HKLowerLayout.Retry InkRetry = new HKLowerLayout.Retry();
        public string Text;
        public float Alpha = -1f;
        public int SortUntil = -1, SortFrame = -1;
    }
    MapActionButton mapViewAction, mapMarkerAction, mapResetAction;
    SpriteRenderer mapZoomTrack, mapZoomThumb, mapEdgeFade;
    Transform mapMaskLeftT, mapMaskRightT;
    Renderer mapMaskLeftR, mapMaskRightR;
    Sprite mapControlPill;
    readonly HKLowerLayout.Retry mapControlRetry = new HKLowerLayout.Retry();
    readonly HKLowerLayout.Retry mapStripRetry = new HKLowerLayout.Retry();
    bool mapControlsReady, mapStripReady;
    bool mapMarkerMode, mapMarkerErase;
    int mapMarkerType = -1;
    bool mapZoomHeld;
    int mapControlContacts, mapMarkerDownCell=-1;
    float mapZoomGrabY, mapZoomGrabPosition;
    float mapZoomX, mapZoomTopY, mapZoomBottomY, mapZoomHitHalfWidth;
    float mapControlLeftX, mapControlRightX, mapControlTopY, mapControlBottomY;
    float mapControlsTouched;
    bool mapControlsShown;
    readonly SpriteRenderer[] mapStripIcons = new SpriteRenderer[5];
    readonly NativePaneLabel[] mapStripCounts = new NativePaneLabel[4];
    readonly Bounds[] mapStripInk = new Bounds[4];
    readonly bool[] mapStripInkReady = new bool[4];
    readonly HKLowerLayout.Retry[] mapStripInkRetry = { new HKLowerLayout.Retry(), new HKLowerLayout.Retry(), new HKLowerLayout.Retry(), new HKLowerLayout.Retry() };
    readonly ShellSpriteFit[] mapStripFits = new ShellSpriteFit[5];
    ShellSpriteFit mapThumbFit;
    const double MarkerCaretMoveSeconds = 0.15;
    double mapCaretElapsed = MarkerCaretMoveSeconds;
    Bounds mapCaretFrom,mapCaretNow; float mapCaretTravel=1; int mapCaretKey=-1; bool mapCaretShown;
    readonly int[] mapStripSpare = { -1, -1, -1, -1 };
    readonly string[] mapMarkerSpriteNames = { "map_mark_0000_scarab", "map_mark_0001_pill", "map_mark_0002_chit", "map_mark_0003_shell" };
    // Existing commissioned production SS art, byte-identical reuse, not new art.
    const string MapTrashPng = "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";
    const string MapTrackPng = "iVBORw0KGgoAAAANSUhEUgAAAB0AAAKeCAYAAABZKBBQAAAFqElEQVR42u3XTagVZRzH8e+517C6WgSlCVJkRWRItTAMLAi3CdHCLpQkFWGbokWv5MaVFbUJeoGkFmUQ0qJFQkQgahqIpVSQUeSFCl8gtBeUvP7azIHTaWbuzNx7avP9w3CZZ+Z5Pvd5+89zeknoEKuBQ8CpLpXH6BZPAhs71qXXoadXAt8Dx4GrgNP/RU8fBsaBy4EN/0VP5wNTwKLi/gfgOuDsKHt69wAIsAxY17qrSdpcu/LvOJik16adNuCKVMedbdA2w/tIzbOnR7GQFgI/FX+r4jZg91wupPtmAAGemeuFdCgzx7kkN87VnK4GVjSZKuCpuerptjSPs0munm1PFxcJoWmMA0/MdiE9WKS+NrEBWNIVHS+Se1l8Aeyryc+Pd53TtRXzNp3kliSrihVbFqeSXNIlDe6oaPD1hovsubbosqJHw3F0qAdXJPmzAj2W5MI2q3djxXw/Afw6cD8FvFzRxmXAQ03ndH6S4yX/+c6KT9iCJD9X9PZIkvOaDO/6kspnkiyvmf8HauZ2w/D7ZV+Zz4Bbh8q2AK8Ak8A1Rdk0cAD4pPgC7QduLhnMb4o0eq5qeG+qSOS7ixRXFd8m2Vfz/K664X0jo4l9VcN7cTFME4wm1gCfDqfB+0cI/uNI0+9pD/gauL4oPwycLL4ySzscVU8A3wE3ABcNlK8E9vfH+Y5i7L9KMplkbGAO1hTZpUlsTXLpQN3zk9yTZG/xfPvgQnovyaYk4xX7cGmSPTOAL9Scf3tJ7k0yleRaioyxssEJ4oKazLO54VlrQZKJtif8x0rAP9qe8Nv+gJoAfq84lI3092lmi47xP4SoqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKio6FyhE/8H+lBFea9VK0nOS7IyCTNcFyT5OeWxuUF9kixIMtG/eS/JpiTjFS8vTbIn9fFCkl5F/V6Se5NMJbm2X3hHUfGrJJNJxgYqrElyLM1ia5JLB+qen+SeJHuL59uT0EvSn5OvgeuLUT8MnAQWA0s7zP0J4DvgBuCigfKVwP7BIXg0o42P+1a/pwAXAz/N1bYoiTXAp8Nb5iTw7ojAz/tg2T59rWxXAXuA6ZpGDxcNV8WWwZt5Qw+/BPYCtw5t/F3AOmASuKYonwYOAJ8U07K/AvwG+HA4OQxf60sWwZkky2s2/QM1C2jD8PtlDcxPcryk8s6Kzb+gJlMdKTLeP+qU7b8zwFsl5bcD60vKnwaWVAztS8BfZbm37FqWZLrkPz+a5JKB965I8mdFL48lubCs/brkvKOisdcH3tlWM5fPVbVdh66taGw6yS1JViU5V/HOqaERaYyOJ/mxotEDA0m86otDF5Qkz3bIsaeTLKlrd6avx9ZiNbeJt4FfZnNcOQp80AKcBl6cizPSqy3Q94Hvm5yRmlyHGszluSQ3Nmmv6YngtQbv7AAONmls8CNeFwuLL8nCmnduA3bP5bn3N+Cdmud7moJt5pQkK2rm884W7bRCSbKrBDxYc96d1UKq2z7PF0eaxtF0IfVjPjAFLCrufwCuA86O8gfUGeDNgfsX24JdegpwZZF1jgNXAafbNjCvwxn2CPBRsUVOd6jfqacAq4FDwKkulf8Gd8LOWD3dXaAAAAAASUVORK5CYII=";
    const string MapThumbPng = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAPCAYAAACWV43jAAAAtElEQVR42tXVQUoCUBCA4e+9TWh4irCoVUiXENq18AxCnsZFRxDBfcdQV5LSKSLT1bh5gieQ6T/Bx8DMlIjQusUEb3hC13XbY4MFpviF0oB3+ERfjnYY4rtERBerRLhzXxhUvCfEwQMmJSKWeJazdYmIA26SAo8lLtY4Y1Xy/gXwkNh3rO16Z21TMUsMnJ0/yRL32aaHl9qe9BDbRLgdXrEvF2ewgzFGeETvyqifNrU5PvAHJ+zOOCKybEshAAAAAElFTkSuQmCC";

    void BuildMapControls(Transform nativeRoot)
    {
        if (frameRoot == null) return;
        BuildMapMarkerStrip(nativeRoot);
        bool ready = ValidButton(mapViewAction) && ValidButton(mapMarkerAction) && ValidButton(mapResetAction) && mapZoomTrack != null && mapZoomTrack.sprite != null && mapZoomThumb != null && mapZoomThumb.sprite != null;
        if (ready) return;
        if (mapControlsReady) { mapControlRetry.Reset(); mapControlsReady = false; }
        if (!mapControlRetry.Due(Time.frameCount)) return;
        if (nativeRoot == null)
        {
            var inv = GameManager.instance != null ? GameManager.instance.inventoryFSM : null;
            nativeRoot = inv != null ? inv.transform.root : null;
        }
        if (nativeRoot == null) return;
        if (mapControlPill == null) mapControlPill = CreateMapRounded();
        if (!ValidButton(mapViewAction)) mapViewAction = RetryMapAction(mapViewAction, nativeRoot, "F_MapView", "FULL MAP");
        if (!ValidButton(mapMarkerAction)) mapMarkerAction = RetryMapAction(mapMarkerAction, nativeRoot, "F_MapMarkers", "MARKERS");
        if (!ValidButton(mapResetAction)) mapResetAction = RetryMapAction(mapResetAction, nativeRoot, "F_MapReset", "RESET");
        if (mapZoomTrack == null) mapZoomTrack = ShellSprite("F_MapZoomTrack",frameRoot.transform,null,30040);
        if (mapZoomTrack.sprite == null) mapZoomTrack.sprite=DecodeMapArt(MapTrackPng,"MapSliderTrack",25);
        if (mapZoomThumb == null) mapZoomThumb = ShellSprite("F_MapZoomThumb",frameRoot.transform,null,30050);
        if (mapZoomThumb.sprite == null) mapZoomThumb.sprite=DecodeMapArt(MapThumbPng,"MapSliderThumb",0);
        mapControlsReady = ValidButton(mapViewAction) && ValidButton(mapMarkerAction) && ValidButton(mapResetAction) && mapZoomTrack != null && mapZoomTrack.sprite != null && mapZoomThumb != null && mapZoomThumb.sprite != null;
        if (mapControlsReady) mapControlRetry.Resolved();
    }
    MapActionButton RetryMapAction(MapActionButton failed, Transform root, string name, string text)
    {
        if (failed != null) { if (failed.Root != null) Destroy(failed.Root.gameObject); if(failed.Plate != null) Destroy(failed.Plate.gameObject); }
        return BuildMapActionButton(root,name,text);
    }
    MapActionButton BuildMapActionButton(Transform nativeRoot, string name, string text)
    {
        NativePaneLabel label = null; SpriteRenderer plate = null;
        try
        {
            label = CopyPaneLabel(FindDeep(nativeRoot,"Pane Name"),frameRoot.transform,name,34,true);
            if (label == null) return null;
            plate = ShellSprite(name+"Plate",frameRoot.transform,mapControlPill,30045);
            return new MapActionButton { Root=label.Root, Label=label.Tmp, LabelRenderer=label.Renderer, Graphic=label, Plate=plate };
        }
        catch(Exception e)
        {
            if(label != null && label.Root != null) Destroy(label.Root.gameObject);
            if(plate != null) Destroy(plate.gameObject);
            WarnOnce("map action build",e); return null;
        }
    }
    static bool ValidButton(MapActionButton button)
    {
        return button != null && button.Root != null && button.Label != null && button.LabelRenderer != null && button.Plate != null && button.Plate.sprite != null;
    }
    bool TryMapLabelInk(NativePaneLabel label, out Bounds ink)
    {
        ink=default;
        Vector3 min, max;
        if (label == null || !TryTmpGlyphBoundsWorld(label.Root, out min, out max)) return false;
        min=label.Root.InverseTransformPoint(min);max=label.Root.InverseTransformPoint(max);
        ink=new Bounds((min+max)*.5f,max-min);
        return ink.size.x>=0 && ink.size.y>0 &&
            !float.IsNaN(ink.center.x) && !float.IsInfinity(ink.center.x) &&
            !float.IsNaN(ink.center.y) && !float.IsInfinity(ink.center.y) &&
            !float.IsNaN(ink.size.x) && !float.IsInfinity(ink.size.x) &&
            !float.IsNaN(ink.size.y) && !float.IsInfinity(ink.size.y);
    }
    Bounds MapLabelWorldInk(NativePaneLabel label, Bounds ink)
    {
        var min=label.Root.TransformPoint(ink.min);var max=label.Root.TransformPoint(ink.max);
        return new Bounds((min+max)*.5f,max-min);
    }
    void SortMapAction(MapActionButton button)
    {
        NeutralizeDetachedTmpClip(button.Root.gameObject);
        var renderers=button.Root.GetComponentsInChildren<Renderer>(true);
        button.Graphic.ClipRenderers=renderers;
        for(int i=0;i<renderers.Length;i++) if(renderers[i] != null) { renderers[i].sortingLayerName="Inventory";renderers[i].sortingOrder=30050; }
        button.SortFrame=Time.frameCount;
    }
    void SetMapAction(MapActionButton button, bool show, string text, Vector3 right, float alpha, float fixedWidth=0)
    {
        if (!ValidButton(button)) return;
        if(button.Root.gameObject.activeSelf != show) button.Root.gameObject.SetActive(show);
        button.Plate.enabled=show;
        if(!show) return;
        if(button.Text != text)
        {
            button.Text=text;button.InkReady=false;button.Ink=button.Hit=default;button.InkRetry.Reset();
            if(button.Graphic.Container != null) TcSetSize(button.Graphic.Container,new Vector2((LowerGeometry().Width-80)/Mathf.Max(.001f,button.Graphic.UnitScale),54/Mathf.Max(.001f,button.Graphic.UnitScale)));
            TmpProp(button.Label,"text")?.SetValue(button.Label,text,null);
            TmpProp(button.Label,"enableWordWrapping")?.SetValue(button.Label,false,null);
        }
        if(!button.InkReady && button.InkRetry.Due(Time.frameCount))
        {
            button.InkReady=TryMapLabelInk(button.Graphic,out button.Ink);
            SortMapAction(button);button.SortUntil=Time.frameCount+2;
            if(button.InkReady) button.InkRetry.Resolved();
        }
        if(Time.frameCount<=button.SortUntil && button.SortFrame != Time.frameCount) SortMapAction(button);
        button.LabelRenderer.enabled=button.InkReady;button.Plate.enabled=button.InkReady;
        var renderers=button.Graphic.ClipRenderers;
        if(renderers != null) for(int i=0;i<renderers.Length;i++) if(renderers[i] != null) renderers[i].enabled=button.InkReady;
        if(!button.InkReady) { button.Hit=default;return; }
        button.Root.localScale=Vector3.one*button.Graphic.UnitScale*ShellPixel;
        var glyph=MapLabelWorldInk(button.Graphic,button.Ink);
        float width=fixedWidth>0 ? fixedWidth : Mathf.Max(130,glyph.size.x/ShellPixel+60);
        Vector3 center=right-new Vector3(width*ShellPixel/2,0,0);
        button.Root.position += center-glyph.center;
        if(button.Alpha != alpha) { button.Alpha=alpha;SetTmpColor(button.Label,new Color(0,0,0,alpha));button.Plate.color=new Color(1,1,1,alpha); }
        button.LabelRenderer.enabled=true;
        PositionMapPlate(button.Plate,center,width,54);
        button.Hit=new Bounds(center,new Vector3(width*ShellPixel,54*ShellPixel,100));
    }
    void PositionMapPlate(SpriteRenderer plate,Vector3 center,float width,float height)
    {
        if(plate == null || plate.sprite == null) return;
        plate.drawMode=SpriteDrawMode.Sliced;
        plate.transform.localScale=Vector3.one*(100*ShellPixel);
        plate.size=new Vector2(width/100,height/100);
        plate.transform.position=center;
    }
    Rect MapBodyRect()
    {
        var g=LowerGeometry();return new Rect(20,g.HudHeight+20,Mathf.Max(1,g.Width-40),Mathf.Max(1,g.BodyHeight-24));
    }
    void PositionMapControls(float s,float asp,float innerTop,float innerBottom,bool onMap)
    {
        bool view=onMap && !mapMarkerMode && mapAnyAvailable && mapClone != null && mapGm != null;
        bool map=onMap && mapAvailable && mapClone != null && mapGm != null && mapContentVisible && !mapNeedsSetup;
        if(!map) SetMapMarkerMode(false);
        bool markers=map && AnyMarkerUnlocked();
        if(map && !mapControlsShown) mapControlsTouched=Time.unscaledTime;
        mapControlsShown=map;
        float alpha=MapControlsAlpha();
        var g=LowerGeometry();var rect=MapBodyRect();
        int headerCount=mapMarkerMode ? 1 : (view ? 1 : 0)+(markers ? 1 : 0);
        float gap=headerCount>0 ? Mathf.Max(10,(g.HudHeight-headerCount*54)/(headerCount+1)) : 0;
        SetMapAction(mapViewAction,view,mapWorldMode ? "AREA MAP" : "FULL MAP",ShellPoint(g.Width-40,gap+27,3.4f),1);
        SetMapAction(mapMarkerAction,markers,mapMarkerMode ? "EXIT" : "MARKERS",ShellPoint(g.Width-40,mapMarkerMode ? gap+27 : gap*2+81,3.4f),1);
        PositionMapMarkerStrip(markers && mapMarkerMode);
        mapControlLeftX=ShellPoint(rect.x,rect.y).x;mapControlRightX=ShellPoint(rect.x+rect.width,rect.y).x;
        mapControlTopY=ShellPoint(rect.x,rect.y).y;mapControlBottomY=ShellPoint(rect.x,rect.y+rect.height).y;
        mapZoomX=ShellPoint(rect.x+rect.width-10-29f/2,rect.y).x;
        mapZoomTopY=ShellPoint(0,rect.y+12).y;mapZoomBottomY=ShellPoint(0,rect.y+rect.height-12).y;
        mapZoomHitHalfWidth=29f/2*ShellPixel;
        // A 220x78 action pane, inset 24 from the body. Its 192x54 plate
        // owns the same hit rectangle and stays left of the actual slider hit.
        float actionRight=Mathf.Min(ShellPoint(rect.x+rect.width-24,rect.y).x,mapZoomX-mapZoomHitHalfWidth)-14*ShellPixel;
        float actionY=ShellPoint(0,rect.y+rect.height-24-78+12+27,3.4f).y;
        bool moved=Mathf.Abs(mapUserZoom-1)>.001f || mapUserPan.sqrMagnitude>ShellPixel*ShellPixel*.25f;
        SetMapAction(mapResetAction,map && !mapMarkerMode && moved && alpha>0,"RESET",new Vector3(actionRight,actionY,attrCam.transform.position.z+3.4f),alpha,192);
        bool slider=map && alpha>0;
        if(mapZoomTrack != null)
        {
            mapZoomTrack.enabled=slider;mapZoomTrack.color=new Color(1,1,1,alpha);
            PositionMapPlate(mapZoomTrack,new Vector3(mapZoomX,(mapZoomTopY+mapZoomBottomY)/2,attrCam.transform.position.z+3.5f),29,rect.height-24);
        }
        if(mapZoomThumb != null)
        {
            mapZoomThumb.enabled=slider;mapZoomThumb.color=new Color(1,1,1,alpha);
            if(slider) FitShellSprite(mapZoomThumb,new Vector3(mapZoomX,Mathf.Lerp(mapZoomBottomY,mapZoomTopY,MapZoomPosition(mapUserZoom,Mathf.Max(1.5f,cfg.compMapZoomMax))),attrCam.transform.position.z+3.35f),40*ShellPixel,15*ShellPixel,ref mapThumbFit);
        }
        if(!map) mapZoomHeld=false;
    }
    float MapControlsAlpha()
    {
        return 1-Mathf.Clamp01((Time.unscaledTime-mapControlsTouched-3)/.6f);
    }
    void BuildMapEdges()
    {
        mapEdgeFade=ShellSprite("F_MapEdgeFade",frameRoot.transform,CreateMapEdgeFade(),MapArtFadeOrder);
        mapMaskLeftT=BuildMapMask("HKDS MapMaskLeft");mapMaskRightT=BuildMapMask("HKDS MapMaskRight");
        mapMaskLeftR=mapMaskLeftT.GetComponent<Renderer>();mapMaskRightR=mapMaskRightT.GetComponent<Renderer>();
    }
    void PositionMapEdges()
    {
        MapRenderRolesTick();
        bool map=tab.cur==COMP_MAP;var g=LowerGeometry();var r=MapBodyRect();float pixel=ShellPixel;
        if(mapEdgeFade != null) { mapEdgeFade.enabled=map;PositionMapPlate(mapEdgeFade,ShellPoint(r.x+r.width/2,r.y+r.height/2),r.width,r.height); }
        if(mapMaskLeftR != null) { mapMaskLeftR.enabled=map;mapMaskLeftT.position=ShellPoint(r.x/2,r.y+r.height/2,4.5f);mapMaskLeftT.localScale=new Vector3(r.x*pixel,r.height*pixel,1); }
        if(mapMaskRightR != null) { mapMaskRightR.enabled=map;mapMaskRightT.position=ShellPoint(r.x+r.width+(g.Width-r.x-r.width)/2,r.y+r.height/2,4.5f);mapMaskRightT.localScale=new Vector3((g.Width-r.x-r.width)*pixel,r.height*pixel,1); }
        if(!map) return;
        if(mapMaskTopT != null) { mapMaskTopT.position=ShellPoint(g.Width/2,r.y/2,4.5f);mapMaskTopT.localScale=new Vector3(g.Width*pixel,r.y*pixel,1); }
        if(mapMaskBotT != null) { mapMaskBotT.position=ShellPoint(g.Width/2,r.y+r.height+(g.Height-r.y-r.height)/2,4.5f);mapMaskBotT.localScale=new Vector3(g.Width*pixel,(g.Height-r.y-r.height)*pixel,1); }
    }
    Sprite DecodeMapArt(string png,string name,float cap)
    {
        var tex=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            if(!tex.LoadImage(Convert.FromBase64String(png))) { Destroy(tex);return null; }
            tex.name=name;tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Bilinear;
            var sprite=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(0,cap,0,cap));
            sprite.name=name;Own(tex);return Own(sprite);
        }
        catch { Destroy(tex);return null; }
    }
    Sprite CreateMapRounded()
    {
        // DsTheme.Rounded: fixed 10px radius, 12px nine-slice borders.
        const int size=48;const float radius=10;
        var tex=Own(new Texture2D(size,size,TextureFormat.RGBA32,false));var px=new Color32[size*size];
        for(int y=0;y<size;y++) for(int x=0;x<size;x++)
        {
            float dx=Mathf.Max(Mathf.Max(radius-(x+.5f),(x+.5f)-(size-radius)),0);
            float dy=Mathf.Max(Mathf.Max(radius-(y+.5f),(y+.5f)-(size-radius)),0);
            px[y*size+x]=new Color32(255,255,255,(byte)(Mathf.Clamp01(radius-Mathf.Sqrt(dx*dx+dy*dy))*255));
        }
        tex.SetPixels32(px);tex.Apply(false);tex.wrapMode=TextureWrapMode.Clamp;
        return Own(Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(12,12,12,12)));
    }
    Sprite CreateMapEdgeFade()
    {
        // DsTheme.EdgeFade exactly: 40px cubic ramp on all edges/corners,
        // transparent 2px core stretched by nine-slicing, never a map-wide tint.
        const int fade=40,size=82;var tex=Own(new Texture2D(size,size,TextureFormat.RGBA32,false));var px=new Color32[size*size];
        for(int y=0;y<size;y++) for(int x=0;x<size;x++)
        {
            int d=Mathf.Min(Mathf.Min(x,y),Mathf.Min(size-1-x,size-1-y));
            float t=Mathf.Clamp01(1-d/(float)fade);
            px[y*size+x]=new Color32(0,0,0,(byte)(Mathf.Pow(t,3)*255));
        }
        tex.SetPixels32(px);tex.Apply(false);tex.wrapMode=TextureWrapMode.Clamp;
        return Own(Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(fade,fade,fade,fade)));
    }
    void BuildMapMarkerStrip(Transform root)
    {
        bool ready=mapStripIcons[4] != null && mapStripIcons[4].sprite != null;
        for(int i=0;i<4;i++) ready &= mapStripIcons[i] != null && mapStripIcons[i].sprite != null && mapStripCounts[i] != null;
        if(ready) return;
        if(mapStripReady) { mapStripReady=false;mapStripRetry.Reset(); }
        if(!mapStripRetry.Due(Time.frameCount)) return;
        if(root == null) { var inv=GameManager.instance != null ? GameManager.instance.inventoryFSM : null;root=inv != null ? inv.transform.root : null; }
        var sprites=Resources.FindObjectsOfTypeAll<Sprite>();
        for(int i=0;i<4;i++)
        {
            if(mapStripIcons[i] == null) mapStripIcons[i]=ShellSprite("F_MapMarker"+i,frameRoot.transform,null);
            if(mapStripIcons[i].sprite == null)
                foreach(var sp in sprites) if(sp != null && sp.name==mapMarkerSpriteNames[i]) { mapStripIcons[i].sprite=sp;break; }
            if(mapStripCounts[i] == null && root != null)
            {
                mapStripCounts[i]=CopyPaneLabel(FindDeep(root,"Pane Name"),frameRoot.transform,"F_MapMarkerCount"+i,34);
                if(mapStripCounts[i] != null)
                {
                    var alignment=TmpProp(mapStripCounts[i].Tmp,"alignment");
                    if(alignment != null) alignment.SetValue(mapStripCounts[i].Tmp,Enum.Parse(alignment.PropertyType,"TopRight"),null);
                    TmpProp(mapStripCounts[i].Tmp,"enableWordWrapping")?.SetValue(mapStripCounts[i].Tmp,false,null);
                }
                mapStripSpare[i]=-1;mapStripInk[i]=default;mapStripInkReady[i]=false;mapStripInkRetry[i].Reset();
            }
        }
        if(mapStripIcons[4] == null) mapStripIcons[4]=ShellSprite("F_MapMarker4",frameRoot.transform,null);
        if(mapStripIcons[4].sprite == null) mapStripIcons[4].sprite=DecodeMapArt(MapTrashPng,"DsTrash",0);
        mapStripReady=mapStripIcons[4] != null && mapStripIcons[4].sprite != null;
        for(int i=0;i<4;i++) mapStripReady &= mapStripIcons[i] != null && mapStripIcons[i].sprite != null && mapStripCounts[i] != null;
        if(mapStripReady) mapStripRetry.Resolved();
    }
    void PositionMapMarkerStrip(bool show)
    {
        var g=LowerGeometry();int count=1;for(int i=0;i<4;i++) if(MarkerUnlocked(i)) count++;
        float cell=g.Width/count;int col=0;EnsureSelectedMarkerType();
        float iconSize=Mathf.Max(0,Mathf.Min(cell,Mathf.Min(96,g.TabHeight-40)));
        Bounds caret=default;int caretKey=-1;
        for(int i=0;i<5;i++)
        {
            bool on=show && (i==4 || MarkerUnlocked(i));
            var sr=mapStripIcons[i];if(sr != null) sr.enabled=on && sr.sprite != null;
            var label=i<4 ? mapStripCounts[i] : null;
            if(label != null) SetPaneLabelVisible(label,on && mapStripInkReady[i]);
            if(!on) continue;
            Vector3 center=ShellPoint((col+.5f)*cell,g.TabTop+g.TabHeight/2,3.8f);col++;
            if(sr != null)
            {
                FitShellSprite(sr,center,iconSize*ShellPixel,iconSize*ShellPixel,ref mapStripFits[i]);
                bool chosen=i==4 ? mapMarkerErase : !mapMarkerErase && i==mapMarkerType;
                sr.color=new Color(1,1,1,i<4 && MarkerSpare(i)<=0 ? .25f : chosen ? 1f : .45f);
            }
            if(label != null)
            {
                int spare=MarkerSpare(i);
                if(mapStripSpare[i] != spare)
                {
                    mapStripSpare[i]=spare;mapStripInkReady[i]=false;mapStripInk[i]=default;mapStripInkRetry[i].Reset();
                    label.Text=spare.ToString();TmpProp(label.Tmp,"text")?.SetValue(label.Tmp,label.Text,null);
                    SetTmpColor(label.Tmp,Color.white);
                }
                if(!mapStripInkReady[i] && mapStripInkRetry[i].Due(Time.frameCount))
                {
                    mapStripInkReady[i]=TryMapLabelInk(label,out mapStripInk[i]);
                    NeutralizeDetachedTmpClip(label.Root.gameObject);
                    label.ClipRenderers=label.Root.GetComponentsInChildren<Renderer>(true);
                    for(int n=0;n<label.ClipRenderers.Length;n++) { label.ClipRenderers[n].sortingLayerName="Inventory";label.ClipRenderers[n].sortingOrder=30090; }
                    if(mapStripInkReady[i]) mapStripInkRetry[i].Resolved();
                }
                SetPaneLabelVisible(label,mapStripInkReady[i]);
                if(mapStripInkReady[i])
                {
                    label.Root.localScale=Vector3.one*label.UnitScale*ShellPixel;
                    label.Root.position+=ShellPoint(col*cell-6,g.TabTop+6,3.8f)-MapLabelWorldInk(label,mapStripInk[i]).max;
                }
            }
            bool selected=i==4 ? mapMarkerErase : !mapMarkerErase && i==mapMarkerType;
            if(selected && sr != null && sr.sprite != null)
            { caret=mapStripFits[i].Ink;caretKey=i; }
        }
        if(show && tabGlow != null) tabGlow.enabled=false;
        if(!show || caretKey<0) { mapCaretShown=false;mapCaretKey=-1;return; }
        if(!mapCaretShown) { mapCaretFrom=mapCaretNow=caret;mapCaretElapsed=MarkerCaretMoveSeconds; }
        else if(mapCaretKey!=caretKey) { mapCaretFrom=mapCaretNow;mapCaretElapsed=0; }
        mapCaretKey=caretKey;mapCaretShown=true;
        // Accumulate elapsed seconds, not rounded normalized frame increments.
        // Double retains small/unequal partitions until the one float fraction.
        mapCaretElapsed=Math.Min(MarkerCaretMoveSeconds,mapCaretElapsed+Time.unscaledDeltaTime);
        mapCaretTravel=(float)(mapCaretElapsed/MarkerCaretMoveSeconds);
        mapCaretNow=mapCaretTravel<1 ? new Bounds(Vector3.Lerp(mapCaretFrom.center,caret.center,mapCaretTravel),Vector3.Lerp(mapCaretFrom.size,caret.size,mapCaretTravel)) : caret;
        if(tabTL!=null) tabTL.enabled=true;if(tabBR!=null) tabBR.enabled=true;
        PositionShellCursor(tabTL,tabBR,null,mapCaretNow,ShellPixel,52,10,ref shellTLFit,ref shellBRFit,ref shellGlowFit,false);
    }
    int MapMarkerCell(Vector3 world)
    {
        if(!mapMarkerMode || attrCam == null) return -1;
        var g=LowerGeometry();Vector3 top=ShellPoint(0,g.TabTop),bottom=ShellPoint(g.Width,g.Height);
        if(world.x<top.x || world.x>=bottom.x || world.y>top.y || world.y<=bottom.y) return -1;
        int count=1;for(int i=0;i<4;i++) if(MarkerUnlocked(i)) count++;
        return (int)((world.x-top.x)/(g.Width*ShellPixel/count));
    }
    bool MapMarkerStripTap(Vector3 world)
    {
        int column=MapMarkerCell(world), down=mapMarkerDownCell;
        mapMarkerDownCell=-1;
        if(column<0) return false;
        if(column!=down) return true; // Strip owns rejected taps; never switch tabs/place a marker.
        int seen=0;
        for(int i=0;i<4;i++) if(MarkerUnlocked(i))
        {
            if(seen++==column) { mapMarkerType=i;mapMarkerErase=false;return true; }
        }
        mapMarkerErase=true;return true;
    }
    static float MapZoomPosition(float zoom, float maxZoom)
    {
        if (zoom <= 1f || maxZoom <= 1f) return 0f;
        return Mathf.Clamp01(Mathf.Log(zoom) / Mathf.Log(maxZoom));
    }

    static float MapZoomForPosition(float position, float maxZoom)
    {
        return Mathf.Exp(Mathf.Log(maxZoom) * position);
    }

    bool MapControlTouchTick(int touchCount)
    {
        bool down = touchCount == 1 && mapControlContacts == 0;
        mapControlContacts = touchCount;
        if (transport == null || attrCam == null || tab.cur != COMP_MAP || !mapAvailable ||
            !mapContentVisible || mapNeedsSetup || mapGm == null || slideT < 1f)
        {
            mapZoomHeld = false;mapMarkerDownCell=-1;
            return false;
        }
        if (touchCount != 1)
        {
            if(touchCount>=2) mapMarkerDownCell=-1;
            bool released = mapZoomHeld && touchCount == 0;
            mapZoomHeld = false;
            if (released)
            {
                lastCleanTapSeq = transport.CleanTapSequence;
                return true;
            }
            return false;
        }

        Vector3 world = TouchToWorld(transport.T0X, transport.T0Y);
        if(down) mapMarkerDownCell=MapMarkerCell(world);
        bool inMap=world.x>=mapControlLeftX && world.x<=mapControlRightX && world.y>=mapControlBottomY && world.y<=mapControlTopY;
        bool wasVisible=MapControlsAlpha()>0;
        if(inMap) mapControlsTouched=Time.unscaledTime;
        bool over = Mathf.Abs(world.x - mapZoomX) <= mapZoomHitHalfWidth &&
                    world.y >= mapZoomBottomY && world.y <= mapZoomTopY;
        if (!mapZoomHeld && (!down || !over || !wasVisible)) return false;
        if (!mapZoomHeld)
        {
            mapZoomHeld = true;
            mapZoomGrabY = world.y;
            mapZoomGrabPosition = MapZoomPosition(mapUserZoom,
                Mathf.Max(1.5f, cfg.compMapZoomMax));
        }
        float travel = Mathf.Max(0.001f, mapZoomTopY - mapZoomBottomY);
        float position = Mathf.Clamp01(mapZoomGrabPosition + (world.y - mapZoomGrabY) / travel);
        mapUserZoom = MapZoomForPosition(position, Mathf.Max(1.5f, cfg.compMapZoomMax));
        resetAnimT = 1f;
        pinchLastDist = -1f;
        dragLastValid = false;
        return true;
    }

    bool HandleMapControlTap(Vector3 world)
    {
        if (tab.cur != COMP_MAP || slideT < 1f || !mapAnyAvailable || mapGm == null) return false;
        if (MapMarkerStripTap(world)) return true;
        if (ValidButton(mapViewAction) && mapViewAction.InkReady && mapViewAction.Root.gameObject.activeSelf &&
            mapViewAction.Hit.Contains(world))
        {
            SetWorldMapMode(!mapWorldMode);
            return true;
        }
        if (!mapAvailable || !mapContentVisible || mapNeedsSetup) return false;
        if (!mapMarkerMode && ValidButton(mapResetAction) && mapResetAction.InkReady && mapResetAction.Root.gameObject.activeSelf && mapResetAction.Hit.Contains(world))
        {
            ResetMapViewAnimated();
            return true;
        }
        if (ValidButton(mapMarkerAction) && mapMarkerAction.InkReady && mapMarkerAction.Root.gameObject.activeSelf &&
            mapMarkerAction.Hit.Contains(world))
        {
            SetMapMarkerMode(!mapMarkerMode);
            return true;
        }
        if (!mapMarkerMode) return false;
        if (mapResetR != null && mapResetR.enabled)
        {
            Bounds reset = mapResetR.bounds;
            reset.Expand(new Vector3(0.6f, 0.6f, 10f));
            if (reset.Contains(world)) return false;   // the normal RESET action keeps priority
        }
        if (world.x < mapControlLeftX || world.x > mapControlRightX ||
            world.y < mapControlBottomY || world.y > mapControlTopY) return false;
        return PlaceOrRemoveMarker(world);
    }

    void SetWorldMapMode(bool world)
    {
        if (mapWorldMode == world) return;
        mapWorldMode = world;
        mapNeedsSetup = true;
        mapAreaBValid = false;
        mapAreaBFor = null;
        mapAreaBTries = 3;
        ResetMapView();
        Dbg(world ? "HKDS map view -> full map" : "HKDS map view -> current area");
    }

    void SetMapMarkerMode(bool active)
    {
        mapMarkerMode = active && AnyMarkerUnlocked();
        if (mapMarkerMode && !mapWorldMode) SetWorldMapMode(true);
        if (mapMarkerMode) { EnsureSelectedMarkerType(); mapMarkerErase=false; }
    }

    bool AnyMarkerUnlocked()
    {
        for (int i = 0; i < 4; i++) if (MarkerUnlocked(i)) return true;
        return false;
    }

    void EnsureSelectedMarkerType()
    {
        if (mapMarkerType >= 0 && MarkerUnlocked(mapMarkerType)) return;
        mapMarkerType = -1;
        for (int i = 0; i < 4; i++)
            if (MarkerUnlocked(i)) { mapMarkerType = i; break; }
    }

    static bool MarkerUnlocked(int type)
    {
        var pd = PlayerData.instance;
        if (pd == null) return false;
        switch (type)
        {
            case 0: return pd.hasMarker_b;
            case 1: return pd.hasMarker_r;
            case 2: return pd.hasMarker_y;
            case 3: return pd.hasMarker_w;
            default: return false;
        }
    }

    static List<Vector3> MarkerList(PlayerData pd, int type)
    {
        if (pd == null) return null;
        switch (type)
        {
            case 0: return pd.placedMarkers_b;
            case 1: return pd.placedMarkers_r;
            case 2: return pd.placedMarkers_y;
            case 3: return pd.placedMarkers_w;
            default: return null;
        }
    }

    static int MarkerSpare(int type)
    {
        var pd = PlayerData.instance;
        if (pd == null) return 0;
        switch (type)
        {
            case 0: return pd.spareMarkers_b;
            case 1: return pd.spareMarkers_r;
            case 2: return pd.spareMarkers_y;
            case 3: return pd.spareMarkers_w;
            default: return 0;
        }
    }

    static void SetMarkerSpare(PlayerData pd, int type, int value)
    {
        value = Mathf.Max(0, value);
        switch (type)
        {
            case 0: pd.spareMarkers_b = value; break;
            case 1: pd.spareMarkers_r = value; break;
            case 2: pd.spareMarkers_y = value; break;
            case 3: pd.spareMarkers_w = value; break;
        }
    }

    GameObject[] MarkerObjects(int type)
    {
        if (mapGm == null) return null;
        switch (type)
        {
            case 0: return mapGm.mapMarkersBlue;
            case 1: return mapGm.mapMarkersRed;
            case 2: return mapGm.mapMarkersYellow;
            case 3: return mapGm.mapMarkersWhite;
            default: return null;
        }
    }

    bool PlaceOrRemoveMarker(Vector3 world)
    {
        var pd = PlayerData.instance;
        if (pd == null || mapClone == null || mapGm == null) return false;
        float radius = Mathf.Max(0.3f, attrCam.orthographicSize * 0.055f);
        float best = radius * radius;
        int removeType = -1, removeIndex = -1;
        for (int type = 0; type < 4; type++)
        {
            List<Vector3> list = MarkerList(pd, type);
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 markerWorld = mapClone.transform.TransformPoint(list[i]);
                float distance = (new Vector2(markerWorld.x, markerWorld.y) -
                                  new Vector2(world.x, world.y)).sqrMagnitude;
                if (distance < best) { best = distance; removeType = type; removeIndex = i; }
            }
        }
        if (removeType >= 0)
        {
            List<Vector3> list = MarkerList(pd, removeType);
            list.RemoveAt(removeIndex);
            SetMarkerSpare(pd, removeType, MarkerSpare(removeType) + 1);
            RefreshNativeMarkers();
            return true;
        }

        if (mapMarkerErase) return true;
        EnsureSelectedMarkerType();
        if (mapMarkerType < 0 || !MarkerUnlocked(mapMarkerType)) return true;
        List<Vector3> selected = MarkerList(pd, mapMarkerType);
        GameObject[] slots = MarkerObjects(mapMarkerType);
        if (selected == null || slots == null || MarkerSpare(mapMarkerType) <= 0 ||
            selected.Count >= slots.Length) return true;
        Vector3 local = mapClone.transform.InverseTransformPoint(world);
        local.z = 0f;
        selected.Add(local);
        SetMarkerSpare(pd, mapMarkerType, MarkerSpare(mapMarkerType) - 1);
        RefreshNativeMarkers();
        return true;
    }

    void RefreshNativeMarkers()
    {
        try
        {
            mapGm.SetupMapMarkers();
            RequestMapRenderRoles();
            lastPinStamp = PinStamp();
        }
        catch (Exception e) { WarnOnce("map marker refresh", e); }
    }

    void TeardownMapControls()
    {
        mapViewAction=mapResetAction=mapMarkerAction=null;
        mapZoomTrack=mapZoomThumb=mapEdgeFade=null;mapControlPill=null;
        mapMaskLeftT=mapMaskRightT=null;mapMaskLeftR=mapMaskRightR=null;
        mapZoomHeld=mapMarkerMode=mapMarkerErase=mapControlsShown=false;mapControlContacts=0;mapMarkerDownCell=-1;
        mapControlsReady=mapStripReady=false;mapControlRetry.Reset();mapStripRetry.Reset();
        for(int i=0;i<5;i++) { mapStripIcons[i]=null;mapStripFits[i]=default; }
        mapThumbFit=default;mapCaretShown=false;mapCaretKey=-1;mapCaretTravel=1;mapCaretElapsed=MarkerCaretMoveSeconds;
        for(int i=0;i<4;i++) { mapStripCounts[i]=null;mapStripInk[i]=default;mapStripSpare[i]=-1;mapStripInkReady[i]=false;mapStripInkRetry[i].Reset(); }
    }
}
