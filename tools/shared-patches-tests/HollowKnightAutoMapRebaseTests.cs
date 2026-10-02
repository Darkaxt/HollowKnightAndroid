using System;
using Native = ModsRuntimeNativeModel;
using Xunit;
namespace SharedPatches.Tests;
[Collection("Mods runtime native model")]
public sealed class HollowKnightAutoMapRebaseTests : IDisposable
{
    readonly Native.PlayerData player = new();
    readonly Native.GameManager game = new();
    public HollowKnightAutoMapRebaseTests() { Native.HollowKnightGameplayFeatures.RestoreAll(); Native.PlayerData.instance = player; player.scenesMapped.Add("original"); }
    [Theory]
    [InlineData("disable")][InlineData("reset")][InlineData("rollback")]
    public void GenuineNativeMapPurchaseAndVisitedProgressSurviveEveryRestore(string route)
    {
        Enable();
        player.SetBool("hasMap",true); player.SetBool("hasQuill",true); player.SetBool("mapCrossroads",true);
        player.scenesVisited.Add("Crossroads_mod"); player.scenesVisited.Add("Crossroads_genuine");
        Assert.True(player.UpdateGameMap());
        if (route == "disable") Native.HollowKnightGameplayFeatures.MapTick(game,player,false);
        else Native.HollowKnightGameplayFeatures.RestoreAll();
        Assert.True(player.hasMap); Assert.True(player.hasQuill); Assert.True(player.mapCrossroads);
        Assert.Equal(new[]{"original","Crossroads_mod","Crossroads_genuine"}, player.scenesMapped);
    }
    [Fact]
    public void BorrowedQuillAndZoneMapNeverBecomeAuthoritativeProgress()
    {
        Enable(); player.scenesVisited.Add("Crossroads_illegitimate");
        Assert.False(player.UpdateGameMap());
        Native.HollowKnightGameplayFeatures.RestoreAll();
        AssertOriginal();
    }
    [Fact]
    public void NoAuthoritativeUpdateRestoresOriginalAndOnlyModOwnedAdditions()
    {
        Enable(); Assert.True(player.hasMap); Assert.True(player.hasQuill);
        Native.HollowKnightGameplayFeatures.RestoreAll(); AssertOriginal();
    }
    [Fact]
    public void NativeMapUpdateExceptionStillRebasesCompletedAuthoritativeProgress()
    {
        Enable(); player.SetBool("hasQuill",true); player.SetBool("mapCrossroads",true);
        player.scenesVisited.Add("Crossroads_genuine");
        Assert.Throws<InvalidOperationException>(()=>player.UpdateGameMap(true));
        Native.HollowKnightGameplayFeatures.RestoreAll();
        Assert.True(player.hasQuill); Assert.True(player.mapCrossroads);
        Assert.False(player.hasMap);
        Assert.Equal(new[]{"original","Crossroads_genuine"},player.scenesMapped);
    }
    [Fact]
    public void UnrelatedSetterDoesNotRecaptureBorrowedState()
    {
        Enable(); player.SetBool("mapGreenpath",true);
        Native.HollowKnightGameplayFeatures.RestoreAll();
        Assert.False(player.hasMap); Assert.False(player.hasQuill); Assert.False(player.mapCrossroads);
        Assert.True(player.mapGreenpath); Assert.Equal(new[]{"original"},player.scenesMapped);
    }
    [Fact]
    public void ReplacementOwnerRetainsOutgoingAuthoritativeProgressWithoutLeakingIt()
    {
        Enable(); player.SetBool("hasQuill",true); player.SetBool("mapCrossroads",true);
        player.scenesVisited.Add("Crossroads_genuine"); player.UpdateGameMap();
        var replacement = new Native.PlayerData(); Native.PlayerData.instance = replacement;
        Native.HollowKnightGameplayFeatures.MapTick(game,replacement,true);
        Assert.Equal(new[]{"original","Crossroads_genuine"},player.scenesMapped);
        Native.HollowKnightGameplayFeatures.RestoreAll();
        Assert.Empty(replacement.scenesMapped); Assert.False(replacement.hasQuill);
    }
    [Fact]
    public void GenuineRemovalAndNestedMapUpdateKeepOnlyAuthoritativeState()
    {
        player.hasQuill = true; player.mapCrossroads = true; player.hasMap = true;
        Enable();
        Native.HollowKnightGameplayFeatures.Authority("BeginAuthoritativeMapUpdate",player);
        player.SetBool("hasMap",false); player.SetBool("mapGreenpath",true);
        player.scenesVisited.Add("Crossroads_genuine"); Assert.True(player.UpdateGameMap());
        Native.HollowKnightGameplayFeatures.Authority("EndAuthoritativeMapUpdate",player);
        Native.HollowKnightGameplayFeatures.RestoreAll();
        Assert.False(player.hasMap); Assert.True(player.hasQuill);
        Assert.True(player.mapGreenpath); Assert.True(player.mapCrossroads);
        Assert.Equal(new[]{"original","Crossroads_genuine"},player.scenesMapped);
    }
    [Fact]
    public void DisabledAuthorityCallbacksNeverBorrowOrCaptureNativeProgress()
    {
        player.SetBool("hasQuill",true); player.SetBool("mapCrossroads",true);
        player.scenesVisited.Add("Crossroads_genuine"); Assert.True(player.UpdateGameMap());
        Native.HollowKnightGameplayFeatures.RestoreAll();
        Assert.True(player.hasQuill); Assert.True(player.mapCrossroads);
        Assert.Equal(new[]{"original","Crossroads_genuine"},player.scenesMapped);
    }
    void Enable() => Native.HollowKnightGameplayFeatures.MapTick(game,player,true);
    void AssertOriginal() { Assert.False(player.hasMap); Assert.False(player.hasQuill); Assert.False(player.mapCrossroads); Assert.Equal(new[]{"original"},player.scenesMapped); }
    public void Dispose() => Native.HollowKnightGameplayFeatures.RestoreAll();
}
