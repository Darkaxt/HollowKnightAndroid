using System.Reflection;
using System.Text.Json;
using HkPauseContracts;
using Xunit;

public sealed class HollowKnightLowerLayoutContractsTests
{
    [Theory]
    [InlineData(1, -4f, 8f)]
    [InlineData(1, .3f, .15f)]
    [InlineData(2, -4f, 8f)]
    [InlineData(2, .3f, .15f)]
    public void LegacyOffsetsCannotMoveMeasuredPaneCenter(int tab, float x, float y)
    {
        HKDualScreen lower = new();
        lower.tab.cur = tab;
        lower.cfg.compInvOffX = x;
        lower.cfg.compCharmOffY = y;
        lower.fit = new() { valid = true, ortho = 10 };
        lower.FramePane(new(120, 240, 0));
        Assert.Equal(120, lower.attrCam.transform.position.x);
        Assert.Equal(240, lower.attrCam.transform.position.y);
    }

    [Fact]
    public void LegacyTelemetryJsonKeysAreUnknownInsteadOfLiveConfig()
    {
        string[] keys = { "compStats", "compStatsX", "compFpsX", "compStatsScale", "compBattScale", "compBattOffY", "compBattGap" };
        var layout = JsonSerializer.Deserialize<HKLayout>("{\"compStats\":1,\"compStatsX\":-90,\"compFpsX\":90,\"compStatsScale\":40,\"compBattScale\":50,\"compBattOffY\":60,\"compBattGap\":70,\"debug\":1}", new JsonSerializerOptions { IncludeFields = true });
        Assert.Equal(1, layout.debug);
        Assert.Equal(1, layout.companion);
        foreach (string key in keys) Assert.Null(typeof(HKLayout).GetField(key, BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void GameplayBlackGroundDoesNotChangeLogoAuthorityOrPauseOwnership()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        Assert.True(lower.bgShow); // LogoTick authority is still normal gameplay, NOT menu/title.
        Assert.Equal(0, lower.CaptureSetups); // A black shell must not construct an unused scenery camera.
        Assert.False(lower.bgCaptureCam.enabled);
        Assert.Equal(CameraClearFlags.SolidColor, lower.clearCam.clearFlags);
        Assert.False(lower.Drawn(lower.logoGo));
        GameObject pane = lower.frameRoot;
        lower.Step(true);
        Assert.True(lower.Drawn(lower.logoGo));
        Assert.False(lower.Drawn(pane));
        lower.Step(false);
        Assert.Same(pane, lower.frameRoot);
        Assert.True(lower.Drawn(pane));
        Assert.True(lower.bgShow);
        Assert.False(lower.bgCaptureCam.enabled);
        Assert.False(lower.Drawn(lower.logoGo));
    }
}
