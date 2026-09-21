using System;
using System.Linq;
using DualSouls.Skins;
using Xunit;

namespace SharedPatches.Tests;

public sealed class NativeSkinMenuModelTests
{
    [Fact]
    public void RowsExposeExactModeScopePacksAndBackChoices()
    {
        var model = new NativeSkinMenuModel(Snapshot(
            mode: "OFF", scope: "CHARACTER_HUD", selected: "moss",
            eligible: Array.Empty<string>(),
            new NativeSkinPackDescriptor("moss", "Moss Knight", "A"),
            new NativeSkinPackDescriptor("silk", "Silk Red", "B")), 5);

        Assert.Equal(new[] { "OFF", "ON", "ROTATE" }, NativeSkinMenuModel.ModeChoices);
        Assert.Equal(new[] { "ALL", "CHARACTER + HUD", "CHARACTER" }, NativeSkinMenuModel.SpriteChoices);
        Assert.Equal(
            new[] { "MODE", "SPRITES", "Moss Knight", "Silk Red", "BACK" },
            model.Rows.Select(row => row.Label));
        Assert.Equal(
            new[] { "OFF", "CHARACTER + HUD", "SELECTED", "DISABLED", "" },
            model.Rows.Select(row => row.Value));
        Assert.Equal(
            new[] { NativeSkinMenuRowKind.Mode, NativeSkinMenuRowKind.Sprites,
                NativeSkinMenuRowKind.Skin, NativeSkinMenuRowKind.Skin,
                NativeSkinMenuRowKind.Back },
            model.Rows.Select(row => row.Kind));
    }

    [Fact]
    public void ModeAndScopeCycleThroughAllExactBridgeValues()
    {
        var model = new NativeSkinMenuModel(Snapshot("OFF", "ALL", null, Array.Empty<string>()), 3);

        Assert.Equal("ON", model.CycleMode(1).Value);
        model.Replace(Snapshot("ON", "ALL", null, Array.Empty<string>()));
        Assert.Equal("ROTATE", model.CycleMode(1).Value);
        model.Replace(Snapshot("ROTATE", "ALL", null, Array.Empty<string>()));
        Assert.Equal("OFF", model.CycleMode(1).Value);

        Assert.Equal("CHARACTER_HUD", model.CycleSprites(1).Value);
        model.Replace(Snapshot("ROTATE", "CHARACTER_HUD", null, Array.Empty<string>()));
        Assert.Equal("CHARACTER", model.CycleSprites(1).Value);
        model.Replace(Snapshot("ROTATE", "CHARACTER", null, Array.Empty<string>()));
        Assert.Equal("ALL", model.CycleSprites(1).Value);
    }

    [Fact]
    public void ScrollingAndReplacementRetainFocusedPackIdentity()
    {
        var packs = Enumerable.Range(0, 7)
            .Select(index => new NativeSkinPackDescriptor("p" + index, "Pack " + index, "A"))
            .ToArray();
        var model = new NativeSkinMenuModel(Snapshot("ROTATE", "ALL", "p0", new[] { "p0" }, packs), 3);

        model.SelectPack(5);
        Assert.Equal("p5", model.Selected.PackId);
        Assert.Equal(3, model.SkinWindowStart);
        Assert.Equal(new[] { "MODE", "SPRITES", "Pack 3", "Pack 4", "Pack 5", "BACK" },
            model.VisibleRows.Select(row => row.Label));

        model.Replace(Snapshot("ROTATE", "CHARACTER", "p0", new[] { "p0", "p5" }, packs));
        Assert.Equal("p5", model.Selected.PackId);
        Assert.Equal(3, model.SkinWindowStart);
        Assert.Equal("ENABLED", model.Selected.Value);
    }

    [Fact]
    public void EmptyStateKeepsModeScopeAndBackFunctional()
    {
        var model = new NativeSkinMenuModel(Snapshot("OFF", "ALL", null, Array.Empty<string>()), 5);

        Assert.Equal(new[] { "MODE", "SPRITES", "NO COMPATIBLE SKINS", "BACK" },
            model.Rows.Select(row => row.Label));
        Assert.False(model.Rows[2].IsActionable);
        model.SelectRow(1);
        model.Move(1);
        Assert.Equal(NativeSkinMenuRowKind.Back, model.Selected.Kind);
        model.Move(1);
        Assert.Equal(NativeSkinMenuRowKind.Mode, model.Selected.Kind);
    }

    [Theory]
    [InlineData("OFF", NativeSkinConfirmationIntent.SelectForLater)]
    [InlineData("ON", NativeSkinConfirmationIntent.EnableSole)]
    [InlineData("ROTATE", NativeSkinConfirmationIntent.ToggleRotationMembership)]
    public void SkinConfirmationDescribesModeDependentIntent(
        string mode, NativeSkinConfirmationIntent expected)
    {
        var model = new NativeSkinMenuModel(Snapshot(mode, "ALL", "p0", new[] { "p0" },
            new NativeSkinPackDescriptor("p0", "Pack 0", "A"),
            new NativeSkinPackDescriptor("p1", "Pack 1", "B")), 3);
        model.SelectPack(1);

        NativeSkinMutation mutation = model.ConfirmSelected();

        Assert.Equal(NativeSkinMutationKind.ConfirmPack, mutation.Kind);
        Assert.Equal("p1", mutation.Value);
        Assert.Equal(expected, mutation.ConfirmationIntent);
    }

    [Fact]
    public void SkinValuesNeverClaimOffSelectionIsApplied()
    {
        var off = new NativeSkinMenuModel(Snapshot("OFF", "ALL", "p0", new[] { "p0" },
            new NativeSkinPackDescriptor("p0", "Pack 0", "A")), 3);
        var on = new NativeSkinMenuModel(Snapshot("ON", "ALL", "p0", new[] { "p0" },
            new NativeSkinPackDescriptor("p0", "Pack 0", "A")), 3);
        var rotate = new NativeSkinMenuModel(Snapshot("ROTATE", "ALL", "p0", new[] { "p0" },
            new NativeSkinPackDescriptor("p0", "Pack 0", "A")), 3);

        Assert.Equal("SELECTED", off.Rows[2].Value);
        Assert.Equal("ENABLED", on.Rows[2].Value);
        Assert.Equal("ENABLED", rotate.Rows[2].Value);
    }

    static NativeSkinMenuSnapshot Snapshot(
        string mode,
        string scope,
        string selected,
        string[] eligible,
        params NativeSkinPackDescriptor[] packs) =>
        new NativeSkinMenuSnapshot("hollow-knight", "configuration", mode, scope,
            selected, eligible, packs);
}
