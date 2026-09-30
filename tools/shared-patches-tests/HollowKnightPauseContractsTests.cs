using HkPauseContracts;
using Xunit;

public sealed class HollowKnightPauseContractsTests
{
    static void AssertGameplayHidden(HKDualScreen lower)
    {
        foreach (GameObject role in new[] { lower.Health, lower.Soul, lower.Geo, lower.mapClone,
                 lower.invCloneCache, lower.charmCloneCache, lower.frameRoot, lower.Frame, lower.Controls, lower.Heal })
            Assert.False(lower.Drawn(role));
        Assert.False(lower.bgCaptureCam.enabled);
        Assert.False(lower.bgShow);
        Assert.Equal(0, lower.attrCam.cullingMask);
        Assert.Equal(0, lower.promptCam.cullingMask);
        Assert.Equal(0, lower.clearCam.cullingMask);
        Assert.Equal(CameraClearFlags.SolidColor, lower.clearCam.clearFlags);
        Assert.True(lower.Drawn(lower.logoGo));
    }

    [Fact]
    public void GameplayPauseShowsLogoWithoutLifeSoulGeoCompanionOrBackdrop()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        Assert.True(lower.Drawn(lower.Health));
        Assert.True(lower.Drawn(lower.frameRoot));
        Assert.True(lower.bgCaptureCam.enabled);
        Assert.False(lower.Drawn(lower.logoGo));
        lower.Step(true);
        AssertGameplayHidden(lower);
    }

    [Fact]
    public void RepeatedPauseAndPauseOwnedSubmenusRetainPageAndRestoreOnNextFrame()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        GameObject map = lower.mapClone, inventory = lower.invCloneCache, charms = lower.charmCloneCache,
                   frame = lower.frameRoot;
        int updates = lower.Updates, touches = lower.TouchPolls, prewarms = lower.Prewarms;
        for (int cycle = 0; cycle < 5; cycle++)
        {
            // Upper Pause -> Options -> Mods -> Options -> Skins -> Options -> Pause.
            // All are owned by the same native paused game (no separate lower owner).
            foreach (string route in new[] { "Pause", "Options", "Mods", "Options", "Skins", "Options", "Pause" })
            {
                lower.Manager.MenuState = route;
                lower.Step(true);
                AssertGameplayHidden(lower);
                Assert.Equal(updates, lower.Updates);
                Assert.Equal(touches, lower.TouchPolls);
                Assert.Equal(prewarms, lower.Prewarms);
                Assert.Same(map, lower.mapClone);
                Assert.Same(inventory, lower.invCloneCache);
                Assert.Same(charms, lower.charmCloneCache);
                Assert.Same(frame, lower.frameRoot);
                Assert.Equal(2, lower.tab.cur);
                Assert.Equal(2, lower.tab.tap);
                Assert.Equal(2, lower.tab.built);
            }
            lower.Step(false); // First orchestration frame after unpause, no settle/retry window.
            Assert.Equal(1 << HKDualScreen.HUD_LAYER, lower.hudCam2.cullingMask);
            Assert.Equal(1 << HKDualScreen.ATTR_LAYER, lower.attrCam.cullingMask);
            Assert.Equal(1 << HKDualScreen.TUT_LAYER, lower.promptCam.cullingMask);
            Assert.True(lower.Drawn(lower.Health));
            Assert.True(lower.Drawn(frame));
            Assert.True(lower.Drawn(lower.Heal));
            Assert.True(lower.bgCaptureCam.enabled);
            Assert.False(lower.Drawn(lower.logoGo));
            Assert.Equal(++updates, lower.Updates);
            Assert.Equal(++touches, lower.TouchPolls);
            Assert.Equal(++prewarms, lower.Prewarms);
        }
        Assert.Equal(0, lower.Teardowns);
        Assert.Equal(0, lower.FrameTeardowns);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void UnpauseRestoresSupportedRoleMasksNotEverything(bool companion, bool popup, bool hudFaded)
    {
        HKDualScreen lower = new();
        lower.cfg.companion = companion ? 1 : 0;
        lower.Popup = popup;
        lower.HudFaded = hudFaded;
        lower.Step(false);
        int hud = lower.hudCam2.cullingMask, attr = lower.attrCam.cullingMask, prompt = lower.promptCam.cullingMask;
        bool hudEnabled = lower.hudCam2.enabled, attrEnabled = lower.attrCam.enabled, promptEnabled = lower.promptCam.enabled;
        lower.Step(true);
        Assert.True(lower.Drawn(lower.logoGo)); // Even a paused popup must not blank the title's HUD camera.
        Assert.Equal(0, lower.attrCam.cullingMask);
        Assert.Equal(0, lower.promptCam.cullingMask);
        lower.Step(false);
        Assert.Equal(hud, lower.hudCam2.cullingMask);
        Assert.Equal(attr, lower.attrCam.cullingMask);
        Assert.Equal(prompt, lower.promptCam.cullingMask);
        Assert.Equal(hudEnabled, lower.hudCam2.enabled);
        Assert.Equal(attrEnabled, lower.attrCam.enabled);
        Assert.Equal(promptEnabled, lower.promptCam.enabled);
    }

    [Fact]
    public void NativeInventoryAloneRetainsExistingHudAndCompanionPolicy()
    {
        HKDualScreen lower = new() { HudFaded = true };
        lower.Step(false, inventory: true);
        Assert.True(lower.Drawn(lower.Health));
        Assert.True(lower.Drawn(lower.frameRoot));
        Assert.Equal(1 << HKDualScreen.TUT_LAYER, lower.promptCam.cullingMask);
        Assert.False(lower.bgCaptureCam.enabled);
        Assert.Equal(1, lower.Updates);
        Assert.Equal(0, lower.Teardowns);
        lower.Step(true, inventory: true);
        AssertGameplayHidden(lower);
        lower.Step(false, inventory: true);
        Assert.True(lower.Drawn(lower.Health));
        Assert.True(lower.Drawn(lower.frameRoot));
        Assert.Equal(2, lower.Updates);
    }

    [Fact]
    public void FirstUnpausedFrameKeepsExistingHudThenTutorialRoutingOrder()
    {
        HKDualScreen lower = new();
        lower.RoutedHudPrompt = new();
        lower.RoutedHudPrompt.transform.SetParent(lower.Cameras.hudCanvas.transform);
        lower.Step(true);
        Assert.False(lower.Drawn(lower.RoutedHudPrompt));
        lower.Step(false);
        Assert.Equal(HKDualScreen.TUT_LAYER, lower.RoutedHudPrompt.layer);
        Assert.True(lower.Drawn(lower.RoutedHudPrompt));
        Assert.Equal(1 << HKDualScreen.TUT_LAYER, lower.promptCam.cullingMask);
    }

    [Fact]
    public void PausedHudRootReplacementIsHiddenAndRestoredOnFirstUnpausedFrame()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        lower.Step(true);
        GameObject replacement = new();
        GameObject newHealth = new();
        newHealth.transform.SetParent(replacement.transform);
        lower.Cameras.hudCanvas = replacement;
        lower.Step(true);
        Assert.False(lower.Drawn(newHealth));
        Assert.False(lower.Drawn(lower.Health));
        Assert.True(lower.Health.activeInHierarchy); // Return ownership; do not destroy or deactivate native HUD.
        Assert.Equal(HKDualScreen.UI_LAYER, lower.Health.layer);
        Assert.True(lower.Drawn(lower.logoGo));
        lower.Step(false);
        Assert.True(lower.Drawn(newHealth));
        Assert.False(lower.Drawn(lower.Health));
        Assert.Equal(HKDualScreen.UI_LAYER, lower.Health.layer);
        Assert.Equal(HKDualScreen.HUD_LAYER, replacement.layer);
        Assert.Equal(0, lower.Teardowns);
    }

    [Fact]
    public void UnchangedPausedRenderingTickDoesNotAllocateOrRebuild()
    {
        HKDualScreen lower = new();
        lower.Step(false);
        lower.Step(true);
        AssertGameplayHidden(lower);
        int updates = lower.Updates, touches = lower.TouchPolls, prewarms = lower.Prewarms;
        for (int i = 0; i < 100; i++) lower.Step(true);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3600; i++) lower.Step(true);
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(updates, lower.Updates);
        Assert.Equal(touches, lower.TouchPolls);
        Assert.Equal(prewarms, lower.Prewarms);
        Assert.Equal(0, lower.Teardowns);
        Assert.Equal(0, lower.FrameTeardowns);
        AssertGameplayHidden(lower);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DualScreenOffRemainsDisabledAcrossPauseAndUnpause(bool transportLost)
    {
        HKDualScreen lower = new();
        lower.Step(false);
        if (transportLost) lower.directDisplayActive = false;
        else lower.cfg.dualScreen = 0;
        int hud = lower.hudCam2.cullingMask, attr = lower.attrCam.cullingMask, prompt = lower.promptCam.cullingMask;
        lower.Step(true);
        lower.Step(false);
        Assert.False(lower.hudCam2.enabled);
        Assert.False(lower.attrCam.enabled);
        Assert.False(lower.promptCam.enabled);
        Assert.False(lower.clearCam.enabled);
        Assert.Equal(hud, lower.hudCam2.cullingMask);
        Assert.Equal(attr, lower.attrCam.cullingMask);
        Assert.Equal(prompt, lower.promptCam.cullingMask);
        Assert.Equal(1, lower.Updates);
        Assert.Equal(0, lower.Teardowns);
    }
}
