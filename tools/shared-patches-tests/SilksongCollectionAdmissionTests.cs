using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SilksongCollectionAdmissionTests
{
    [Fact]
    public void Exact_collection_pages_are_admitted_and_structural_drift_is_omitted()
    {
        var shared = new object();
        var target = Find("Hornet CrestWeapon Scythe");
        SilksongMaterialPage Page(object texture, int w, int h, string name = "atlas0") => new SilksongMaterialPage(texture, name, w, h);
        Assert.Null(SilksongCollectionAdmission.Validate(target, "Hornet CrestWeapon Scythe", new[] {
            Page(shared, 2048, 4096), Page(shared, 2048, 4096)
        }));
        Assert.Contains("collection name", SilksongCollectionAdmission.Validate(target, "Scythe", new[] {
            Page(shared, 2048, 4096), Page(shared, 2048, 4096)
        }));
        Assert.Contains("material/page count", SilksongCollectionAdmission.Validate(target, target.CollectionName, new[] {
            Page(shared, 2048, 4096)
        }));
        Assert.Contains("dimensions", SilksongCollectionAdmission.Validate(target, target.CollectionName, new[] {
            Page(shared, 2048, 2048), Page(shared, 2048, 2048)
        }));
        Assert.Contains("shared original", SilksongCollectionAdmission.Validate(target, target.CollectionName, new[] {
            Page(new object(), 2048, 4096), Page(new object(), 2048, 4096)
        }));
        Assert.Contains("No material page draws", SilksongCollectionAdmission.Validate(target, target.CollectionName, new[] {
            Page(shared, 2048, 4096, "atlas1"), Page(shared, 2048, 4096, "atlas1")
        }));

        // Pages of other textures may have any size: only the target texture's pages are checked.
        var knight = SilksongSkinTargets.All.Single(x => x.CanonicalPath == "Assets/Collections/Knight Data/atlas2.png");
        Assert.Null(SilksongCollectionAdmission.Validate(knight, "Knight", new[] {
            Page(new object(), 4096, 4096, "atlas0"), Page(new object(), 256, 512, "atlas1"),
            Page(shared, 4096, 4096, "atlas2"), Page(new object(), 4096, 4096, "atlas3")
        }));
    }

    [Fact]
    public void Paired_material_failure_rolls_back_every_slot_to_exact_original()
    {
        var target = Find("Hornet CrestWeapon Shaman Cln");
        var original = new object();
        var first = new MaterialSlot(target.CanonicalPath, original);
        var second = new MaterialSlot(target.CanonicalPath, original) { FailAfterWrite = true };
        var root = Path.Combine(AppContext.BaseDirectory, "runtime-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "atlas0.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));
        using var session = new SkinRuntimeSession(new Decoder(), () => new[] { first.Binding(), second.Binding() },
            512L * 1024 * 1024, SilksongSkinTargets.RuntimeRules);

        var result = session.TryApply(new SkinPack("shaman", root,
            new Dictionary<string, string> { [target.CanonicalPath] = "atlas0.png" }));

        Assert.Equal(SkinApplyStatus.Failed, result.Status);
        Assert.Same(original, first.Value);
        Assert.Same(original, second.Value);
        Assert.Null(session.CurrentPack);
    }

    static SilksongSkinTarget Find(string collection) =>
        SilksongSkinTargets.All.FindForTest(collection);

    sealed class MaterialSlot
    {
        readonly string target;
        public object Value;
        public bool FailAfterWrite;
        public MaterialSlot(string target, object original) { this.target = target; Value = original; }
        public SkinSlot Binding() => new SkinSlot(this, "mainTexture", target, () => true, () => Value,
            value => {
                Value = value;
                if (FailAfterWrite) { FailAfterWrite = false; throw new IOException("injected paired write failure"); }
            },
            (texture, _) => texture.Value);
    }

    sealed class Decoder : ISkinTextureDecoder
    {
        public SkinTexture Decode(byte[] bytes, int width, int height)
        {
            bool released = false;
            return new SkinTexture(new object(), width, height, () => released = true, () => released);
        }
    }
}

static class SilksongTargetTestExtensions
{
    public static SilksongSkinTarget FindForTest(this IReadOnlyList<SilksongSkinTarget> targets, string collection)
    {
        foreach (var target in targets) if (target.CollectionName == collection) return target;
        throw new InvalidOperationException(collection);
    }
}
