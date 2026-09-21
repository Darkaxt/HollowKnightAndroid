using System;
using System.IO;
using Xunit;

namespace SharedPatches.Tests;

public sealed class NativeSkinsSourceContractTests
{
    [Theory]
    [InlineData("HollowKnightNativeModsMenu.cs", "HollowKnightNativeModsMenu")]
    [InlineData("SilksongNativeModsMenu.cs", "SilksongNativeModsMenu")]
    public void ExistingOptionsOwnerOwnsBothRoutes(string file, string owner)
    {
        string source = Source(file);

        Assert.Contains("enum NativeMenuRoute { Mods, Skins }", source, StringComparison.Ordinal);
        Assert.Contains("NativeMenuRoute.Mods", source, StringComparison.Ordinal);
        Assert.Contains("NativeMenuRoute.Skins", source, StringComparison.Ordinal);
        Assert.Contains("\"MODS\"", source, StringComparison.Ordinal);
        Assert.Contains("\"SKINS\"", source, StringComparison.Ordinal);
        Assert.Contains("Open(NativeMenuRoute route)", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "public sealed class " + owner + " : MonoBehaviour"));
    }

    [Theory]
    [InlineData("HollowKnightNativeModsMenu.cs")]
    [InlineData("SilksongNativeModsMenu.cs")]
    public void SkinsRouteUsesBoundedProfileCheckedBridgeAndRefreshesAfterEveryMutation(string file)
    {
        string source = Source(file);

        Assert.Contains("SkinLibraryRuntimeBridge", source, StringComparison.Ordinal);
        Assert.Contains("readMenuSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("setMode", source, StringComparison.Ordinal);
        Assert.Contains("setSpriteScope", source, StringComparison.Ordinal);
        Assert.Contains("confirmPack", source, StringComparison.Ordinal);
        Assert.Contains("ApplySkinMutation", source, StringComparison.Ordinal);
        Assert.Contains("RefreshSkinMenu();", source, StringComparison.Ordinal);
        Assert.Contains("InvalidateSkinLibrary()", source, StringComparison.Ordinal);
        Assert.Contains("MaximumSkinSnapshotBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("library.json", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlayerPrefs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LineFileTweakStore", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnightNativeModsMenu.cs")]
    [InlineData("SilksongNativeModsMenu.cs")]
    public void SkinsRouteHasControllerPointerFocusAndNativeCancelSemantics(string file)
    {
        string source = Source(file);

        Assert.Contains("MoveDirection.Left", source, StringComparison.Ordinal);
        Assert.Contains("MoveDirection.Right", source, StringComparison.Ordinal);
        Assert.Contains("IPointerClickHandler", source, StringComparison.Ordinal);
        Assert.Contains("ISelectHandler", source, StringComparison.Ordinal);
        Assert.Contains("ICancelHandler", source, StringComparison.Ordinal);
        Assert.Contains("SelectSkin", source, StringComparison.Ordinal);
        Assert.Contains("FocusSkin", source, StringComparison.Ordinal);
        Assert.Contains("Close();", source, StringComparison.Ordinal);
        Assert.Contains("UIState.PAUSED", source, StringComparison.Ordinal);
        Assert.Contains("BindingIsAlive", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnightModsRuntime.cs")]
    [InlineData("SilksongModsRuntime.cs")]
    public void RuntimeExposesOnlyInvalidationNotSkinPersistence(string file)
    {
        string source = Source(file);

        Assert.Contains("internal void InvalidateSkinLibrary()", source, StringComparison.Ordinal);
        Assert.Contains("skinLibrary.Invalidate()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("library.json", source, StringComparison.OrdinalIgnoreCase);
    }

    static int Count(string value, string token)
    {
        int count = 0;
        for (int index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0;
             index += token.Length) count++;
        return count;
    }

    static string Source(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-skin-contracts", name));
}
