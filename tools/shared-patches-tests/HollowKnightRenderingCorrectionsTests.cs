using Xunit;
using HkPauseContracts;
using HK=HkPauseContracts.HKDualScreen;
using GameObject=HkPauseContracts.GameObject;
using Renderer=HkPauseContracts.Renderer;
using Bounds=HkPauseContracts.Bounds;
using HkPlayerData=HkPauseContracts.PlayerData;

[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightRenderingCorrectionsTests
{
    [Theory]
    [InlineData(180,1)] [InlineData(180,-1)] [InlineData(0,-1)] [InlineData(90,1)]
    public void ActualFitSpriteCentersAffineOffPivotArt(float angle,float reflection)
    {
        HK lower=new();var parent=new GameObject("NativeCornerParent");parent.transform.localScale=new(reflection*2,3,1);
        var art=new SpriteRenderer();art.transform.SetParent(parent.transform);art.sprite.bounds=new(new(.25f,.4f,0),new(2,1,0));art.transform.localRotation=Quaternion.Euler(0,0,angle);
        lower.NativeFitSpriteStep(art,new(200,300,0),22,22);Assert.Equal(200,art.bounds.center.x,3);Assert.Equal(300,art.bounds.center.y,3);
        Assert.True(art.bounds.size.x<=22.001f);Assert.True(art.bounds.size.y<=22.001f);
    }
    [Fact]
    public void ActualNativeSelectionGlowUsesCanonicalPixelsAndCenteredArt()
    {
        HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(1);
        lower.NativeSelectionStep(lower.refsInv.slots[0].Root);var glow=cursor.Find("Glow").GetComponent<SpriteRenderer>();
        Assert.Equal(110,glow.bounds.size.x,3);Assert.Equal(110,glow.bounds.size.y,3);Assert.Equal(cursor.position.x,glow.bounds.center.x,3);Assert.Equal(cursor.position.y,glow.bounds.center.y,3);
    }

    [Fact]
    public void ActualCopyPaneLabelKeepsNativeDerivedContainerButRemovesGameplayDriver()
    {
        HK lower=new();var donor=lower.TextDonor(new(1,1,1),true);
        donor.TextComponents.Add(new GameplayLabelDriver(donor.GetComponentsInChildren<Renderer>(true)[0]));
        var label=lower.CopyLabelStep(donor,37.44f);Assert.NotNull(label.Container);
        Assert.IsType<TMProOld.TextContainer>(label.Container);Assert.Empty(label.Root.GetComponentsInChildren<GameplayLabelDriver>(true));
        lower.LabelStep(label,"Measured text",new(900,256,320,56));
        Assert.Equal(320/label.UnitScale,label.Container.size.x,3);
        Assert.Equal(label.Container.size.x,label.Tmp.LastMeshSize.x,3);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualSelectedCursorIdleDoesNotAllocate(bool child)
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.AddNativeCursor(pane);
        if(child) {var root=pane.transform.Find("Equipment").Children[0];root.gameObject.RemoveRenderer();var art=new GameObject("ChildGraphic");art.transform.SetParent(root);art.AddComponent<Renderer>();}
        lower.NativeLayoutRouteStep(1);lower.NativeSelectionStep(lower.refsInv.slots[0].Root);
        for(int i=0;i<150;i++) lower.NativeCursorStep();long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<300;i++) lower.NativeCursorStep();Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualSelectionHidesScrolledSlotAndRestoresSameDetail(bool child)
    {
        HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);
        if(child) { var item=pane.transform.Find("Equipment").Children[0];item.gameObject.RemoveRenderer();var icon=new GameObject("ChildGraphic");icon.transform.SetParent(item);icon.AddComponent<Renderer>().BoundsSize=new(2,2,1); }
        lower.NativeLayoutRouteStep(1);var r=lower.refsInv;var selected=r.slots[0].Root;
        lower.NativeDetailStep(4,selected);lower.NativeSelectionStep(selected);Assert.True(cursor.gameObject.activeSelf);
        var detail=r.nameLabel.Text;lower.NativeScrollStep(0,-.2f);lower.NativeCursorStep();
        Assert.All(r.slots[0].Renderers,x=>Assert.False(x.enabled));Assert.Same(selected,lower.SelectedItem);
        Assert.Equal(detail,r.nameLabel.Text);Assert.False(cursor.gameObject.activeSelf);
        lower.NativeScrollStep(0,.2f);lower.NativeCursorStep();Assert.True(cursor.gameObject.activeSelf);
        Assert.Same(selected,lower.SelectedItem);Assert.Equal(detail,r.nameLabel.Text);
        Assert.False(cursor.Find("TR").gameObject.activeSelf);Assert.False(cursor.Find("BL").gameObject.activeSelf);
    }
    [Fact]
    public void ActualSelectionRejectsInactiveDirectItem()
    {
        HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(1);
        var item=lower.refsInv.slots[0].Root;lower.NativeSelectionStep(item);item.gameObject.SetActive(false);lower.NativeCursorStep();Assert.False(cursor.gameObject.activeSelf);
    }
    [Fact]
    public void ActualTapCannotChooseHiddenInventoryAtStaleBounds()
    {
        HK lower=new();var pane=lower.NewNativePane(false);lower.NativeLayoutRouteStep(1);var hidden=lower.refsInv.slots[0].Root;
        var old=hidden.position;lower.NativeScrollStep(0,-.2f);lower.NativeTapStep(old);
        Assert.NotSame(hidden,lower.SelectedItem);Assert.True(ReferenceEquals(lower.refsInv.slots[3].Root,lower.SelectedItem),lower.LastDiagnostic+" camera "+lower.attrCam.orthographicSize+" rect "+lower.attrCam.rect.width);
    }
    [Fact]
    public void ActualCharmTapRejectsDisabledIconIndependentlyOfCursor()
    {
        HK lower=new();lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        var icon=lower.nativeCharmGrid.Children.First(t=>t.name=="Icon1");var point=icon.position;
        icon.GetComponent<SpriteRenderer>().enabled=false;lower.NativeTapStep(point);Assert.Null(lower.SelectedItem);
        icon.GetComponent<SpriteRenderer>().enabled=true;lower.NativeTapStep(point);Assert.Equal("CharmBoard1",lower.SelectedItem?.name);
    }
    [Fact]
    public void ActualSelectionKeepsNativePointFifteenSecondMoveTiming()
    {
        float previous=Time.unscaledDeltaTime;try
        {
            Time.unscaledDeltaTime=.05f;HK lower=new();var pane=lower.NewNativePane(false);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(1);
            var a=lower.refsInv.slots[0].Root;var b=lower.refsInv.slots[1].Root;lower.NativeSelectionStep(a);float from=cursor.position.x;
            lower.NativeSelectionStep(b);Assert.Equal(from+(b.position.x-from)/3,cursor.position.x,3);
            lower.NativeCursorStep();lower.NativeCursorStep();Assert.Equal(b.position.x,cursor.position.x,3);
        }
        finally {Time.unscaledDeltaTime=previous;}
    }

    [Fact]
    public void ActualCharmSelectionAndTapRejectDisabledIcon()
    {
        HK lower=new();var pane=lower.NewNativePane(true);var cursor=lower.AddNativeCursor(pane);lower.NativeLayoutRouteStep(2);
        var board=lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard1");var icon=lower.nativeCharmGrid.Children.First(t=>t.name=="Icon1");
        lower.NativeSelectionStep(board,1);Assert.True(cursor.gameObject.activeSelf);var point=icon.position;
        icon.GetComponent<SpriteRenderer>().enabled=false;lower.NativeCursorStep();Assert.False(cursor.gameObject.activeSelf);
        lower.NativeSelectionStep(null);lower.NativeTapStep(point);Assert.Null(lower.SelectedItem);
        icon.GetComponent<SpriteRenderer>().enabled=true;lower.NativeTapStep(point);Assert.Same(board,lower.SelectedItem);lower.NativeCursorStep();Assert.True(cursor.gameObject.activeSelf);
    }
    [Fact]
    public void ActualHeightLimitedCharmsChooserFitsExactCanonicalMask()
    {
        HK lower=new();lower.BOTTOM_W=2400;lower.BOTTOM_H=480;lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        var p=HKLowerLayout.NativeColumns(lower.LowerGeometry(),true);Bounds occupied=default;bool any=false;
        foreach(var r in lower.nativeCharmGrid.GetComponentsInChildren<Renderer>(true))
        { if(!r.enabled || !r.gameObject.activeInHierarchy)continue;if(!any){occupied=r.bounds;any=true;}else occupied.Encapsulate(r.bounds); }
        Assert.True(any);Assert.Equal(p.Height,occupied.size.y,3);
        float top=lower.BOTTOM_H/2f-occupied.max.y,bottom=lower.BOTTOM_H/2f-occupied.min.y;
        Assert.True(top>=p.Top-.001f,$"top {top} < {p.Top}");Assert.True(bottom<=p.Top+p.Height+.001f,$"bottom {bottom} > {p.Top+p.Height}");
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ActualNativeCharmCostRendersExactFilledNotches(int cost)
    {
        HK lower=new();lower.NewNativePane(true);lower.NativeLayoutRouteStep(2);
        HkPlayerData.instance.Ints["charmCost_36"]=cost;var item=lower.nativeCharmGrid.Children.First(t=>t.name=="CharmBoard36");
        lower.NativeDetailStep(1,item,36);Assert.Equal(cost,lower.CostPips?.Children.Count(t=>t.gameObject.activeSelf) ?? 0);
        HkPlayerData.instance.Ints["charmCost_36"]=3;lower.NativeDetailStep(1,item,36);
        HkPlayerData.instance.Ints["charmCost_36"]=0;lower.NativeDetailStep(1,item,36);Assert.Equal(0,lower.CostPips.Children.Count(t=>t.gameObject.activeSelf));
    }
}
