using DualSouls.DualScreen;
using Xunit;

namespace SharedPatches.Tests;

public sealed class CompanionShellLayoutTests
{
    static readonly string[] Pages = { "inventory", "loadout", "tasks", "journal", "map" };

    [Fact]
    public void Regions_follow_the_dual_souls_hud_frame_and_bottom_navigation_order()
    {
        var layout = CompanionShellLayout.Create(1240, 1080, Pages);

        Assert.Equal(0, layout.Hud.Top);
        Assert.Equal(layout.Hud.Bottom, layout.TopOrnament.Top);
        Assert.Equal(layout.TopOrnament.Bottom, layout.Content.Top);
        Assert.Equal(layout.Content.Bottom, layout.BottomOrnament.Top);
        Assert.Equal(layout.BottomOrnament.Bottom, layout.Navigation.Top);
        Assert.Equal(1080, layout.Navigation.Bottom);
        Assert.True(layout.Hud.Height >= CompanionShellLayout.MinimumTouchTarget);
        Assert.True(layout.Content.Height > layout.Hud.Height + layout.Navigation.Height);
    }

    [Fact]
    public void Tabs_are_bottom_centred_labels_with_reserved_status_gutters()
    {
        var layout = CompanionShellLayout.Create(1240, 1080, Pages);

        Assert.Equal(Pages, layout.Tabs.Select(tab => tab.Id));
        Assert.All(layout.Tabs, tab =>
        {
            Assert.True(tab.Bounds.Top >= layout.Navigation.Top);
            Assert.True(tab.Bounds.Height >= CompanionShellLayout.MinimumTouchTarget);
        });
        Assert.True(layout.Tabs[0].Bounds.Left > layout.Status.Bounds.Right);
        Assert.True(layout.Tabs[^1].Bounds.Right < layout.Battery.Bounds.Left);
    }

    [Fact]
    public void Selected_tab_has_two_fleurs_and_no_filled_tab_panel()
    {
        var layout = CompanionShellLayout.Create(1240, 1080, Pages);
        var selection = layout.SelectionFor(2);

        Assert.Equal(layout.Tabs[2].Bounds.CenterX, selection.Top.CenterX);
        Assert.Equal(layout.Tabs[2].Bounds.CenterX, selection.Bottom.CenterX);
        Assert.True(selection.Top.Bottom <= layout.Tabs[2].Bounds.Top + 18);
        Assert.True(selection.Bottom.Top >= layout.Tabs[2].Bounds.Bottom - 18);
        Assert.False(layout.Tabs[2].DrawFilledPanel);
    }

    [Fact]
    public void Mods_is_a_separate_gear_above_status_and_never_a_tab()
    {
        var layout = CompanionShellLayout.Create(1240, 1080, Pages);

        Assert.DoesNotContain(layout.Tabs, tab => tab.Id == "mods");
        Assert.True(layout.ModsGear.Bounds.Bottom <= layout.Status.Bounds.Top);
        Assert.True(layout.ModsGear.Bounds.Width >= CompanionShellLayout.MinimumTouchTarget);
        Assert.Equal(CompanionHitTarget.Mods, layout.HitTest(layout.ModsGear.Bounds.CenterX, layout.ModsGear.Bounds.CenterY).Target);
    }

    [Fact]
    public void Navigation_hit_testing_uses_real_tab_bounds_not_equal_screen_slices()
    {
        var layout = CompanionShellLayout.Create(1240, 1080, Pages);

        Assert.Equal(CompanionHitTarget.None, layout.HitTest(4, layout.Navigation.CenterY).Target);
        for (var i = 0; i < layout.Tabs.Count; i++)
        {
            var tab = layout.Tabs[i];
            var hit = layout.HitTest(tab.Bounds.CenterX, tab.Bounds.CenterY);
            Assert.Equal(CompanionHitTarget.Tab, hit.Target);
            Assert.Equal(i, hit.TabIndex);
        }
    }
}
