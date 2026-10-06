namespace SkinUnityContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.Silksong.Runtime;
using SkinUnityNativeModel;
using SkinUnityNativeModel.SceneManagement;
using Xunit;
using UObject = SkinUnityNativeModel.Object;
using PlayMakerFSM = SkinUnityNativeModel.PlayMakerFSM;
using HeroController = SkinUnityNativeModel.HeroController;
using GameManager = SkinUnityNativeModel.GameManager;

[CollectionDefinition("Skin Unity callers", DisableParallelization = true)]
public sealed class SkinUnityCallerCollection { }

[Collection("Skin Unity callers")]
public sealed class SkinUnityCallerContractsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Imported_A_B_default_restores_exact_native_material_original(bool ss)
    {
        using var rig = new Rig(ss);
        var vanilla = rig.Material.mainTexture;
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        var a = rig.Material.mainTexture;
        Assert.NotSame(vanilla, a);
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("B")).Status);
        var b = rig.Material.mainTexture;
        Assert.NotSame(a, b);
        Assert.True(a.DestroyRequested);
        Assert.False(a.Destroyed);
        Assert.Equal(SkinApplyStatus.Restored, rig.Restore().Status);
        Assert.Same(vanilla, rig.Material.mainTexture);
        Assert.True(b.DestroyRequested);
        UObject.FlushDestroy();
        Assert.True(a.Destroyed);
        Assert.True(b.Destroyed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Missing_bindings_report_pending_without_writes_then_rebind(bool ss)
    {
        using var rig = new Rig(ss);
        rig.Collection.material = null;
        rig.Collection.materials = rig.Collection.materialInsts = Array.Empty<Material>();
        Assert.Equal(SkinApplyStatus.AwaitingTargets, rig.Apply(rig.Pack("A")).Status);
        Assert.Equal(1, ImageConversion.Decodes);
        Assert.Same(rig.Vanilla, rig.Material.mainTexture);
        UObject.FlushDestroy();
        rig.Collection.material = rig.Material;
        rig.Collection.materials = rig.Collection.materialInsts = new[] { rig.Material };
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        Assert.NotSame(rig.Vanilla, rig.Material.mainTexture);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Owner_loss_retires_success_before_current_owner_recovery(bool ss)
    {
        using var rig = new Rig(ss);
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        var owner = GameManager.instance;
        GameManager.instance = null;
        Time.unscaledTime += 3;
        rig.Tick();
        Assert.Equal(SkinApplyStatus.AwaitingTargets, rig.Last().Status);
        GameManager.instance = owner;
        Time.unscaledTime += 3;
        rig.Tick();
        Assert.Contains(rig.Last().Status, new[] { SkinApplyStatus.Applied, SkinApplyStatus.Unchanged });
        Assert.NotSame(rig.Vanilla, rig.Material.mainTexture);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Scene_rebind_applies_to_new_real_collection_without_capturing_old_skin_as_vanilla(bool ss)
    {
        using var rig = new Rig(ss);
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        var old = rig.Collection;
        var oldMaterial = rig.Material;
        rig.BindHero();
        UObject.Destroy(old); UObject.FlushDestroy();
        SceneManager.Load();
        Assert.Equal(SkinApplyStatus.AwaitingTargets, rig.Last().Status);
        Time.unscaledTime += 3; rig.Tick();
        Assert.Equal(SkinApplyStatus.Applied, rig.Last().Status);
        Assert.NotSame(rig.Vanilla, rig.Material.mainTexture);
        Assert.Equal(SkinApplyStatus.Restored, rig.Restore().Status);
        Assert.Same(rig.Vanilla, rig.Material.mainTexture);
        Assert.True(oldMaterial.mainTexture == null || oldMaterial.mainTexture != rig.Material.mainTexture);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Setter_failure_uses_real_session_undo_and_keeps_original(bool ss)
    {
        using var rig = new Rig(ss);
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        var previous = rig.Material.mainTexture;
        rig.Material.FailNextWrite = true;
        var result = rig.Apply(rig.Pack("B"));
        Assert.Equal(SkinApplyStatus.Failed, result.Status);
        Assert.False(result.PreviousVisualsRestored);
        Assert.Same(previous, rig.Material.mainTexture);
        Assert.Contains(UObject.All.OfType<Texture2D>(), x => x != rig.Vanilla && x.DestroyRequested);
        Assert.Equal(SkinApplyStatus.Restored, rig.Restore().Status);
        Assert.Same(rig.Vanilla, rig.Material.mainTexture);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Restore_failure_stays_visible_and_owned_until_explicit_retry(bool ss)
    {
        using var rig = new Rig(ss);
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        var applied = rig.Material.mainTexture;
        rig.Material.FailNextWrite = true;
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Restore().Status);
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Last().Status);
        Assert.False(applied.DestroyRequested);
        Assert.Equal(SkinApplyStatus.Restored, rig.Restore().Status);
        Assert.Same(rig.Vanilla, rig.Material.mainTexture);
        Assert.True(applied.DestroyRequested);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Teardown_failure_blocks_replacement_and_releases_events_only_after_restore_and_destroy(bool ss)
    {
        using var rig = new Rig(ss);
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        var applied = rig.Material.mainTexture;
        rig.Material.FailNextWrite = true;
        Assert.Throws<InvalidOperationException>(rig.Close);
        Assert.Equal(2, SceneManager.Subscribers);
        Assert.Throws<InvalidOperationException>(() => { using var second = rig.NewRuntime(); });
        Assert.False(applied.DestroyRequested);
        Assert.Throws<InvalidOperationException>(rig.Close);
        Assert.True(applied.DestroyRequested);
        Assert.False(applied.Destroyed);
        UObject.FlushDestroy();
        rig.Close();
        Assert.Equal(0, SceneManager.Subscribers);
        using var fresh = rig.NewRuntime();
        Assert.Equal(2, SceneManager.Subscribers);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Decoder_failure_returns_allocated_texture_for_deferred_retirement(bool ss)
    {
        using var rig = new Rig(ss);
        ImageConversion.FailDecode = true;
        Assert.Equal(SkinApplyStatus.Failed, rig.Apply(rig.Pack("A")).Status);
        Assert.Same(rig.Vanilla, rig.Material.mainTexture);
        var failed = Assert.Single(UObject.All.OfType<Texture2D>().Where(x => x != rig.Vanilla));
        Assert.True(failed.DestroyRequested); Assert.False(failed.Destroyed);
        UObject.FlushDestroy(); Assert.True(failed.Destroyed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Recovery_suspension_and_save_gate_do_not_scan_or_decode(bool ss)
    {
        using var rig = new Rig(ss);
        rig.SetSuspended(true, true);
        var scans = Resources.Scans; var decodes = ImageConversion.Decodes;
        SceneManager.Unload(); Time.unscaledTime += 3; rig.Tick();
        Assert.Equal(scans, Resources.Scans); Assert.Equal(decodes, ImageConversion.Decodes);
        rig.SetSuspended(false, false); Time.unscaledTime += 3; rig.Tick();
        Assert.Equal(scans, Resources.Scans); Assert.Equal(decodes, ImageConversion.Decodes);
        rig.SetSuspended(false, true); Time.unscaledTime += 3; rig.Tick();
        Assert.NotEqual(SkinApplyStatus.Failed, rig.Last().Status);
    }

    [Fact]
    public void HK_companion_collection_is_never_an_original_owner()
    {
        using var rig = new Rig(false);
        rig.Collection.transform.SetParent(new GameObject("HKCompanionRoot").transform);
        Assert.Equal(SkinApplyStatus.AwaitingTargets, rig.Apply(rig.Pack("A")).Status);
        Assert.Equal(1, ImageConversion.Decodes);
        Assert.Same(rig.Vanilla, rig.Material.mainTexture);
    }

    [Fact]
    public void SS_unrelated_resource_collection_is_not_a_persistent_owner()
    {
        using var rig = new Rig(true);
        var decoy = new GameObject("unowned").Add<tk2dSpriteCollectionData>();
        decoy.spriteCollectionName = rig.Collection.spriteCollectionName;
        var material = new Material { mainTexture = rig.Vanilla };
        decoy.material = material; decoy.materials = decoy.materialInsts = new[] { material };
        Assert.Equal(SkinApplyStatus.Applied, rig.Apply(rig.Pack("A")).Status);
        Assert.Same(rig.Vanilla, material.mainTexture);
        Assert.Equal(0, Resources.Scans);
    }

    [Fact]
    public void SS_divergent_material_instance_is_omitted_without_binding_writes()
    {
        using var rig = new Rig(true);
        rig.Collection.materialInsts = new[] { new Material { mainTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false) } };
        var result = rig.Apply(rig.Pack("A"));
        Assert.Equal(SkinApplyStatus.AwaitingTargets, result.Status);
        Assert.Contains("material and materialInsts", result.Detail);
        Assert.Equal(1, ImageConversion.Decodes);
        Assert.Same(rig.Vanilla, rig.Material.mainTexture);
    }

    sealed class Rig : IDisposable
    {
        readonly bool ss;
        readonly string root;
        readonly SkinUnityHk.HollowKnightSkinRuntime hk;
        readonly SkinUnitySs.SilksongSkinRuntime silk;
        readonly string target;
        readonly int width, height;
        public Material Material;
        public Texture2D Vanilla;
        public tk2dSpriteCollectionData Collection;
        public Rig(bool ss)
        {
            this.ss = ss;
            root = Path.Combine(Environment.GetEnvironmentVariable("DUALSOULS_HOST_TEST_TEMP") ??
                throw new InvalidOperationException("Set DUALSOULS_HOST_TEST_TEMP for disposable fixture payloads."), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            UObject.Reset(); ImageConversion.FailDecode = Graphics.FailBlit = false;
            ImageConversion.Decodes = Resources.Scans = 0; Time.unscaledTime = 0;
            HeroController.instance = null; GameCameras.instance = null; CharmIconList.Instance = null;
            GameManager.instance = new GameManager();
            target = ss ? SilksongSkinTargets.All.First(x => !x.IsHud).CanonicalPath : "Knight.png";
            var spec = ss ? SilksongSkinTargets.All.First(x => x.CanonicalPath == target) : null;
            width = spec?.Width ?? 8; height = spec?.Height ?? 8;
            BindHero(); BindHud();
            if (ss) silk = new SkinUnitySs.SilksongSkinRuntime(); else { hk = new SkinUnityHk.HollowKnightSkinRuntime(); hk.Tick(); }
        }
        public void BindHero()
        {
            var go = new GameObject("Hero"); HeroController.instance = go.Add<HeroController>();
            go.Add<MeshRenderer>();
            Collection = go.Add<tk2dSpriteCollectionData>();
            Collection.spriteCollectionName = ss ? SilksongSkinTargets.All.First(x => !x.IsHud).CollectionName : "Knight";
            Vanilla = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "vanilla" };
            Material = new Material { mainTexture = Vanilla };
            Collection.material = Material; Collection.materials = Collection.materialInsts = new[] { Material };
            go.Add<tk2dSprite>().Collection = Collection;
            var library = new tk2dSpriteAnimation { clips = new[] { new Clip { frames = new[] { new Frame { spriteCollection = Collection } } } } };
            go.Add<tk2dSpriteAnimator>().Library = library;
            go.Add<HeroAnimationController>();
        }
        void BindHud()
        {
            var go = new GameObject("Cameras"); var cameras = go.Add<GameCameras>(); GameCameras.instance = cameras;
            cameras.hudCanvas = new GameObject("HUD");
            cameras.hudCamera = new GameObject("HUD Camera").Add<Camera>();
            var gameplay = new GameObject("Gameplay"); gameplay.Add<HudCanvas>();
            cameras.hudCamera.gameObject.Add<HUDCamera>().GameplayChild = gameplay;
            cameras.hudCanvasSlideOut = cameras.hudCanvas.Add<PlayMakerFSM>();
            cameras.silkSpool = new GameObject("SilkSpool").Add<SilkSpool>();
        }
        public SkinPack Pack(string name)
        {
            byte[] png = new byte[45];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
            png[11] = 13; png[12] = (byte)'I'; png[13] = (byte)'H'; png[14] = (byte)'D'; png[15] = (byte)'R';
            for (int i = 0; i < 4; i++) { png[16 + i] = (byte)(width >> (24 - 8 * i)); png[20 + i] = (byte)(height >> (24 - 8 * i)); }
            File.WriteAllBytes(Path.Combine(root, name + ".png"), png);
            // Already normalized caller input; actual importer is covered by paired launcher tests.
            return new SkinPack(name, root, new Dictionary<string, string> { [target] = name + ".png" });
        }
        public SkinApplyResult Apply(SkinPack pack) => ss ? silk.TryApply(pack) : hk.TryApply(pack);
        public SkinApplyResult Restore() => ss ? silk.TryRestore() : hk.TryRestore();
        public SkinApplyResult Last() => ss ? silk.LastResult : hk.LastResult;
        public void Tick() { if (ss) silk.Tick(); else hk.Tick(); }
        public void SetSuspended(bool suspended, bool saveAllowed)
        {
            if (ss) { silk.RecoverySuspended = () => suspended; silk.SaveVisualRefreshAllowed = () => saveAllowed; }
            else { hk.RecoverySuspended = () => suspended; hk.SaveVisualRefreshAllowed = () => saveAllowed; }
        }
        public IDisposable NewRuntime() => ss ? new SkinUnitySs.SilksongSkinRuntime() : new SkinUnityHk.HollowKnightSkinRuntime();
        public void Close() { if (ss) silk.Dispose(); else hk.Dispose(); }
        public void Dispose()
        {
            ImageConversion.FailDecode = false; Material.FailNextWrite = false;
            // Explicit deterministic deferred-destruction frames, never an acceptance retry loop.
            UObject.FlushDestroy();
            try { Close(); } catch (InvalidOperationException) { UObject.FlushDestroy(); Close(); }
            foreach (var file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
}
