using HkPauseContracts;
using Xunit;

// Exact owning production methods run against explicitly supplied native-shaped buffers.
// These coordinates prove logic only, not a native/GPU/font-pair optical golden.
[Collection("HollowKnightPauseOwners")]
public sealed class HollowKnightNativeLayoutAuthorityContractsTests
{
    static Component Glyph(HKDualScreen lower)
    {
        var owner=lower.TextDonor(Vector3.one,false);
        var tmp=owner.TextComponents.Single(c=>c.GetType()==typeof(Component));
        tmp.AutomaticGlyphInput=false;tmp.text="M";
        // Pinned GetTextBounds authority: descender=-4, ascender=6, xAdvance=9.
        tmp.LegacyMetricBounds=new(new Vector3(3.5f,1,0),new Vector3(11,10,0));
        Vector3[] q={new(-2,-1,0),new(-1,2,0),new(4,3,0),new(5,0,0)};
        tmp.textInfo=new() {textComponent=tmp,characterCount=1,materialCount=1,
            characterInfo=new[]{new TMProOld.TMP_CharacterInfo {isVisible=true,vertexIndex=0,materialReferenceIndex=0,
                descender=-4,ascender=6,xAdvance=9,
                vertex_BL=new(){position=q[0]},vertex_TL=new(){position=q[1]},vertex_TR=new(){position=q[2]},vertex_BR=new(){position=q[3]}}},
            meshInfo=new[]{new TMProOld.TMP_MeshInfo {mesh=new(),vertexCount=4,vertices=q}}};
        return tmp;
    }
    static void Point(Vector3 expected,Vector3 actual)
    {
        Assert.Equal(expected.x,actual.x,5);Assert.Equal(expected.y,actual.y,5);Assert.Equal(expected.z,actual.z,5);
    }
    [Fact]
    public void T01GeneratedFourVerticesNotAscenderDescenderAdvanceDefineGlyphBounds()
    {
        HKDualScreen lower=new();var tmp=Glyph(lower);
        Assert.True(lower.GlyphBoundsStep(tmp.transform,out var min,out var max));
        Point(new(-2,-1,0),min);Point(new(5,3,0),max);
    }
    [Fact]
    public void T01AllFourGeneratedCornersSurviveParentRotationNonuniformScaleAndOverhang()
    {
        HKDualScreen lower=new();var tmp=Glyph(lower);var parent=new GameObject().transform;
        parent.localScale=new(2,.5f,3);parent.localRotation=Quaternion.Euler(0,0,37);parent.localPosition=new(12,-8,2);
        tmp.transform.SetParent(parent);tmp.transform.localRotation=Quaternion.Euler(0,0,-19);tmp.transform.localScale=new(.7f,1.8f,1);
        var expected=tmp.textInfo.meshInfo[0].vertices.Select(tmp.transform.TransformPoint).ToArray();
        Vector3 min=expected[0],max=expected[0];
        foreach(var p in expected.Skip(1)) {min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
        Assert.True(lower.GlyphBoundsStep(tmp.transform,out var actualMin,out var actualMax));Point(min,actualMin);Point(max,actualMax);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void T01MissingStaleNonfiniteOrUnfinishedGlyphBuffersFailHonestly(int fault)
    {
        HKDualScreen lower=new();var tmp=Glyph(lower);var info=tmp.textInfo;
        switch(fault)
        {
            case 0:tmp.textInfo=null;break;
            case 1:info.textComponent=new Component();break;
            case 2:info.characterInfo=null;break;
            case 3:info.characterCount=2;break;
            case 4:info.characterInfo[0].vertex_TR.position.x=float.NaN;break;
            case 5:info.meshInfo=null;break;
            case 6:info.meshInfo[0].vertexCount=3;break;
            case 7:info.characterInfo[0].materialReferenceIndex=1;break;
            case 8:tmp.ThrowGeneration=true;break;
            case 9:tmp.transform.localScale=new(float.PositiveInfinity,1,1);break;
            case 10:
                info.characterInfo[0].vertex_BL.position.x=info.meshInfo[0].vertices[0].x=-float.MaxValue;
                info.characterInfo[0].vertex_TR.position.x=info.meshInfo[0].vertices[2].x=float.MaxValue;
                break;
        }
        Assert.False(lower.GlyphBoundsStep(tmp.transform,out var min,out var max));Point(Vector3.zero,min);Point(Vector3.zero,max);
    }
    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("\n")]
    public void T01ExplicitEmptyOrInvisibleTextHasNoContainerGlyphDomain(string text)
    {
        HKDualScreen lower=new();var tmp=Glyph(lower);tmp.text=text;
        if(text.Length>0) tmp.textInfo.characterInfo[0].isVisible=false;
        Assert.False(lower.GlyphBoundsStep(tmp.transform,out var min,out var max));Point(Vector3.zero,min);Point(Vector3.zero,max);
    }
    [Fact]
    public void T01FallbackMaterialUsesCharacterQuadNotDecorationOrMaterialRenderExtent()
    {
        HKDualScreen lower=new();var tmp=Glyph(lower);var info=tmp.textInfo;
        var fallback=info.meshInfo[0];fallback.vertices=fallback.vertices.Concat(new[]{new Vector3(-20,-30,0),new Vector3(40,50,0)}).ToArray();fallback.vertexCount=6;
        info.materialCount=2;info.meshInfo=new[]{new TMProOld.TMP_MeshInfo {mesh=new(),vertices=Array.Empty<Vector3>(),vertexCount=0},fallback};
        info.characterInfo[0].materialReferenceIndex=1;
        Assert.True(lower.GlyphBoundsStep(tmp.transform,out var min,out var max));Point(new(-2,-1,0),min);Point(new(5,3,0),max);
        Assert.Same(fallback.mesh,info.meshInfo[1].mesh);Assert.Same(Resources.BodyFont,tmp.font);Assert.Same(Resources.BodyFont.material,tmp.fontSharedMaterial);
    }
    [Fact]
    public void T01CopyCalibratesGeneratedQuadAndPreservesNativePairedFontAndMaterial()
    {
        HKDualScreen lower=new();var tmp=Glyph(lower);
        var label=lower.CopyLabelStep(tmp.transform,32);
        Assert.NotNull(label);Assert.Equal(8,label.UnitScale); // Explicit input quad height=4, not native optical equality.
        Assert.Same(tmp.font,label.Tmp.font);Assert.Same(tmp.fontSharedMaterial,label.Tmp.fontSharedMaterial);
        Assert.True(label.Tmp.enableWordWrapping);Assert.Equal(TextOverflow.Overflow,label.Tmp.overflowMode);
    }
    [Fact]
    public void T01UnreadyLabelNeverFallsBackToContainerAndCanRetrySameText()
    {
        HKDualScreen lower=new();var donor=Glyph(lower);var label=lower.CopyLabelStep(donor.transform,32);
        Assert.NotNull(label);label.Tmp.textInfo.meshInfo=null;
        lower.LabelStep(label,"same",new(20,260,300,60));
        Assert.False(label.Renderer.enabled);Assert.Equal(0,label.ScrollMax);
        label.Tmp.textInfo=Glyph(lower).textInfo;label.Tmp.textInfo.textComponent=label.Tmp;
        lower.LabelStep(label,"same",new(20,260,300,60));Assert.True(label.Renderer.enabled);
    }
    [Fact]
    public void H04NativeHiddenWithoutCanvasGroupSuppressesCompanionAndNeverRecentersHiddenTransform()
    {
        HKDualScreen lower=new();Assert.Null(lower.Cameras.hudCanvas.GetComponent<CanvasGroup>());
        var root=lower.Cameras.hudCanvas.transform;root.localPosition=new(0,-200,0);root.localScale=new(.85f,.85f,1);
        HudGlobalHide.IsReduced=true;HudGlobalHide.IsHidden=true;
        Assert.True(lower.NativeHudSuppressedStep());lower.Step(false);
        Assert.Equal(0,lower.attrCam.cullingMask);Point(new(0,-200,0),root.localPosition);Point(new(.85f,.85f,1),root.localScale);
    }
    [Fact]
    public void H04NormalReducedThenHiddenKeepsNativeReductionAndHiddenRetainedScale()
    {
        HKDualScreen lower=new();var root=lower.Cameras.hudCanvas.transform;
        lower.NativeHudCameraStep();Point(Vector3.zero,root.localPosition);Point(Vector3.one,root.localScale);
        HudGlobalHide.IsReduced=true;root.localPosition=new(-2.1f,1.2f,0);root.localScale=new(.85f,.85f,1);
        lower.NativeHudCameraStep();Point(new(-2.1f,1.2f,0),root.localPosition);Point(new(.85f,.85f,1),root.localScale);
        float reducedCameraSize=lower.hudCam2.orthographicSize;
        lower.NativeHudCameraStep();Assert.Equal(reducedCameraSize,lower.hudCam2.orthographicSize);
        HudGlobalHide.IsHidden=true;root.localPosition=new(0,-200,0);lower.NativeHudCameraStep();
        Point(new(0,-200,0),root.localPosition);Point(new(.85f,.85f,1),root.localScale);
    }
    readonly record struct CameraState(Rect Rect,float Aspect,float Size,bool Orthographic,float Near,float Far,Vector3 Position,Quaternion Rotation,Matrix4x4 Projection)
    {
        internal static CameraState Read(Camera cam)=>new(cam.rect,cam.aspect,cam.orthographicSize,cam.orthographic,cam.nearClipPlane,cam.farClipPlane,cam.transform.position,cam.transform.rotation,cam.projectionMatrix);
        internal void Check(Camera cam)
        {
            Assert.Equal(Rect,cam.rect);Assert.Equal(Aspect,cam.aspect);Assert.Equal(Size,cam.orthographicSize);Assert.Equal(Orthographic,cam.orthographic);
            Assert.Equal(Near,cam.nearClipPlane);Assert.Equal(Far,cam.farClipPlane);Point(Position,cam.transform.position);Assert.Equal(Rotation,cam.transform.rotation);Assert.Equal(Projection,cam.projectionMatrix);
        }
    }
    static CameraState Prime(HKDualScreen lower,bool custom=false)
    {
        var cam=lower.hudCam2;cam.rect=new(.1f,.2f,.7f,.6f);cam.aspect=1.37f;cam.orthographicSize=12;cam.orthographic=true;
        cam.nearClipPlane=.7f;cam.farClipPlane=317;cam.transform.position=new(-15,23,-40);cam.transform.rotation=Quaternion.Euler(0,0,17);
        if(custom)cam.projectionMatrix=new(){m00=7,m11=8,m22=9,m23=4,m33=1};
        var state=CameraState.Read(cam);
        lower.Cameras.hudCamera.orthographicSize=4;lower.Cameras.hudCamera.transform.position=new(9,-6,-20);lower.Cameras.hudCamera.transform.rotation=Quaternion.Euler(0,0,-31);
        lower.NativeHudCameraStep();Assert.NotEqual(state.Size,cam.orthographicSize);Assert.NotEqual(state.Position,cam.transform.position);
        return state;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void H04FirstPauseRestoresFullOwnedCameraBeforeLogoAndSubmenuBypass(bool custom)
    {
        HKDualScreen lower=new();var state=Prime(lower,custom);
        lower.NativeHudCameraStep();lower.Step(true);state.Check(lower.hudCam2);Assert.True(lower.Drawn(lower.logoGo));
        foreach(var menu in new[]{"Options","Mods","Skins","Pause"}) {lower.Manager.MenuState=menu;lower.Step(true);state.Check(lower.hudCam2);}
        if(!custom)Assert.False(lower.hudCam2.HasCustomProjection); // Automatic projection must not become locked by restoration.
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void H04MenuProductOffDisplayLossManagerFailureAndSourceReplacementRetireCameraIdentity(int boundary)
    {
        HKDualScreen lower=new();var state=Prime(lower);
        switch(boundary)
        {
            case 0:lower.MenuStep();break;
            case 1:lower.cfg.dualScreen=0;lower.Step(false);break;
            case 2:lower.LoseDisplay();break;
            case 3:lower.ManagersAvailable=false;lower.Step(false);break;
            case 4:lower.Cameras.hudCamera=new();lower.Step(true);break;
        }
        state.Check(lower.hudCam2);
    }
    [Fact]
    public void H04HudRootRetirementRestoresCameraBeforeFirstPausedRelayer()
    {
        HKDualScreen lower=new();var state=Prime(lower);lower.Cameras.hudCanvas=new();lower.Step(true);state.Check(lower.hudCam2);
    }
    [Fact]
    public void H04CameraReplacementRestoresOnlyOriginalOwnedCameraNeverReplacement()
    {
        HKDualScreen lower=new();var original=lower.hudCam2;var originalState=Prime(lower);
        var replacement=new Camera {aspect=1.6f,orthographicSize=29};replacement.transform.position=new(60,70,-30);var replacementState=CameraState.Read(replacement);
        lower.hudCam2=replacement;lower.Step(true);replacementState.Check(replacement);originalState.Check(original);
    }
}
