using Xunit;
using HkPauseContracts;
using HK=HkPauseContracts.HKDualScreen;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightNativeSelectionProvenanceTests
{
    [Fact]
    public void ActualFitSpriteKeepsGenuineNativeBrSignedOrientationAndFits22Pixels()
    {
        HK lower=new();var native=NativeSelectionInputs.Attach(lower.NewNativePane(false),false);
        var br=native.BR;var parent=br.transform.parent;var scale=parent.localScale;
        Assert.Equal("Inv_0014_selection_cursor",br.sprite.name);
        Assert.True(scale.x<0 && scale.y<0);Assert.Equal(0f,parent.localRotation.Angle);
        lower.NativeFitSpriteStep(br,new(200,300,0),22,22);
        Assert.Equal(200,br.bounds.center.x,3);Assert.Equal(300,br.bounds.center.y,3);
        Assert.True(br.bounds.size.x<=22.001f && br.bounds.size.y<=22.001f);
        Assert.Equal(22,Math.Max(br.bounds.size.x,br.bounds.size.y),3);
        Assert.Equal(scale.x,parent.localScale.x);Assert.Equal(scale.y,parent.localScale.y);
        Assert.Equal(0f,parent.localRotation.Angle);Assert.Equal(0f,br.transform.localRotation.Angle);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualPositionSelectionCentersGenuineTrimmedGlowAfterFixedCameraAdmission(bool charms)
    {
        HK lower=new();var pane=lower.NewNativePane(charms);var native=NativeSelectionInputs.Attach(pane,charms);
        lower.NativeLayoutRouteStep(charms ? 2 : 1);
        Assert.Equal(lower.BOTTOM_H/2f,lower.attrCam.orthographicSize,3);
        var item=charms ? lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard1") : lower.refsInv.slots[0].Root;
        lower.NativeSelectionStep(item,charms ? 1 : 0);
        var glow=native.Glow;Assert.Same(native.Cursor,lower.NativeCursor);
        Assert.Equal("light_effect_v02",glow.sprite.name);Assert.Equal("Back",glow.transform.parent.name);
        Assert.Equal(native.Cursor.position.x,glow.bounds.center.x,3);Assert.Equal(native.Cursor.position.y,glow.bounds.center.y,3);
        var target=charms ? lower.nativeCharmGrid.Children.First(t=>t.name=="Icon1").GetComponent<SpriteRenderer>().bounds : lower.refsInv.slots[0].Renderers[0].bounds;
        Assert.Equal(target.size.x+24,glow.bounds.size.x,3);Assert.Equal(target.size.y+24,glow.bounds.size.y,3);
        Assert.Equal(.6029411554336548f,glow.color.r);Assert.Equal(.7535496950149536f,glow.color.g);Assert.Equal(1,glow.color.b);Assert.Equal(.5372549295425415f,glow.color.a);
        Assert.True(glow.enabled && glow.gameObject.activeInHierarchy);
        Assert.True(native.BR.transform.parent.localScale.x<0 && native.BR.transform.parent.localScale.y<0);
        Assert.Equal(0f,native.BR.transform.parent.localRotation.Angle);
        Assert.Equal(0f,native.BR.transform.localRotation.Angle);
    }

    [Fact]
    public void ActualFitSpriteCentersGenuineNativeTlPositiveControl()
    {
        HK lower=new();var native=NativeSelectionInputs.Attach(lower.NewNativePane(false),false);
        var tl=native.TL;var scale=tl.transform.parent.localScale;
        Assert.Equal("Inv_0014_selection_cursor",tl.sprite.name);
        lower.NativeFitSpriteStep(tl,new(-75,125,0),22,22);
        Assert.Equal(-75,tl.bounds.center.x,3);Assert.Equal(125,tl.bounds.center.y,3);
        Assert.True(tl.bounds.size.x<=22.001f && tl.bounds.size.y<=22.001f);
        Assert.Equal(22,Math.Max(tl.bounds.size.x,tl.bounds.size.y),3);
        Assert.Equal(scale.x,tl.transform.parent.localScale.x);Assert.Equal(scale.y,tl.transform.parent.localScale.y);
        Assert.Equal(0f,tl.transform.parent.localRotation.Angle);
    }
}

// Serialized input only, not renderer/selection logic or GPU proof. Exact Inventory/Inv
// and Inventory/Charms Cursor donors from converted resources.assets SHA256:
// b3d64dcad88f06a1c4e7f4d0e26ccd1f3f7c02c27e6edd031f23681c304d784d.
// Retained provenance.json SHA256:
// b6e63d62a231d7481fe56cd6098afaee44be381fc7068ad4e44ab8062403ed1c.
// Bounds are typed float32 mesh extrema, already in local units; do not reapply
// PPU, pivot, trim or atlas offsets. All admitted quaternions are identity.
internal static class NativeSelectionInputs
{
    internal sealed class Input
    {
        internal Transform Cursor;
        internal SpriteRenderer TL,BR,Glow;
    }
    static Transform Node(Transform parent,string name,Vector3 position,Vector3 scale)
    {
        var go=new GameObject(name);go.transform.SetParent(parent);
        go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=Quaternion.identity;
        return go.transform;
    }
    static SpriteRenderer Graphic(Transform t,string name,Bounds bounds,Rect rect,bool enabled,int order,Color color)
    {
        var sr=t.gameObject.AddComponent<SpriteRenderer>();sr.sprite=new(){name=name,bounds=bounds,rect=rect};
        sr.enabled=enabled;sr.sortingLayerName="Inventory";sr.sortingOrder=order;sr.color=color;return sr;
    }
    internal static Input Attach(GameObject pane,bool charms)
    {
        var cursor=Node(pane.transform,"Cursor",charms ? new(-7.269999980926514f,-7.570000171661377f,-4.5f) : new(-7.019999980926514f,-4.019999980926514f,-4.157301902770996f),
            new(1.0719870328903198f,1.0458416938781738f,1.0719876289367676f));
        var tl=Node(cursor,"TL",new(-.5f,.5f,-4),new(1.399999976158142f,1.399999976158142f,1));
        var br=Node(cursor,"BR",new(.5f,-.5f,-4),new(-1.5399973392486572f,-1.412500262260437f,1));
        // Native BR orientation is two negative parent scales, not a quaternion half-turn.
        var tlArt=Node(tl,charms ? "Sprite" : "TL Sprite",Vector3.zero,Vector3.one);
        var brArt=Node(br,charms ? "Sprite" : "BR Sprite",Vector3.zero,Vector3.one);
        var back=Node(cursor,"Back",new(0,0,charms ? 0 : 1),Vector3.one);
        var glow=Node(back,"Glow",Vector3.zero,new(.3438728451728821f,.350935697555542f,.7550150752067566f));
        Bounds cornerBounds=new(Vector3.zero,new(.550000011920929f,.550000011920929f,0));
        Rect cornerRect=new(0,0,55,55);Color white=new(1,1,1,1);
        var result=new Input
        {
            Cursor=cursor,
            TL=Graphic(tlArt,"Inv_0014_selection_cursor",cornerBounds,cornerRect,charms,10,white),
            BR=Graphic(brArt,"Inv_0014_selection_cursor",cornerBounds,cornerRect,charms,10,white),
            Glow=Graphic(glow,"light_effect_v02",new(new(-.07244312763214111f,.04829549789428711f,0),new(5.843749761581421f,5.167613506317139f,0)),
                new(0,0,128,121.00000762939453f),true,0,new(.6029411554336548f,.7535496950149536f,1,.5372549295425415f))
        };
        // Name-only inactive TR/BL admit the existing two-corner branch; no geometry claim.
        foreach(var name in new[]{"TR","BL"}) Node(cursor,name,Vector3.zero,Vector3.one).gameObject.SetActive(false);
        return result;
    }
}
