using System.Collections.Generic;
using HollowKnightPatches.Lower;
using Xunit;

public sealed class HkShellPolicyTests
{
    static List<HkShellTouchTracker.Sample> Step(HkShellTouchTracker tracker, bool fresh,
                                                 params HkShellTouchTracker.Contact[] contacts)
    {
        var output = new List<HkShellTouchTracker.Sample>();
        tracker.Update(contacts, fresh, 1240f, 1080f, 1080f, output);
        return output;
    }

    [Fact]
    public void New_contact_begins_held_contact_moves_and_missing_contact_ends_at_last_position()
    {
        var tracker = new HkShellTouchTracker();
        var began = Step(tracker, true, new HkShellTouchTracker.Contact(7, 0.5f, 0.25f));
        Assert.Single(began);
        Assert.Equal(HkShellTouchTracker.Phase.Began, began[0].Phase);
        Assert.Equal(620f, began[0].X); Assert.Equal(810f, began[0].Y);

        var moved = Step(tracker, true, new HkShellTouchTracker.Contact(7, 0.25f, 0.5f));
        Assert.Equal(HkShellTouchTracker.Phase.Moved, Assert.Single(moved).Phase);

        var ended = Step(tracker, true);
        var end = Assert.Single(ended);
        Assert.Equal(HkShellTouchTracker.Phase.Ended, end.Phase);
        Assert.Equal(7, end.Id); Assert.Equal(310f, end.X); Assert.Equal(540f, end.Y);
        Assert.Equal(0, tracker.HeldCount);
        Assert.Empty(Step(tracker, true));
    }

    [Fact]
    public void Stale_frame_ends_every_held_contact_and_two_fingers_track_independently()
    {
        var tracker = new HkShellTouchTracker();
        Step(tracker, true, new HkShellTouchTracker.Contact(1, 0f, 0f), new HkShellTouchTracker.Contact(2, 1f, 1f));
        var second = Step(tracker, true, new HkShellTouchTracker.Contact(2, 1f, 1f));
        Assert.Contains(second, s => s.Id == 2 && s.Phase == HkShellTouchTracker.Phase.Moved);
        Assert.Contains(second, s => s.Id == 1 && s.Phase == HkShellTouchTracker.Phase.Ended);

        var stale = Step(tracker, false, new HkShellTouchTracker.Contact(2, 0.5f, 0.5f));
        Assert.Equal(HkShellTouchTracker.Phase.Ended, Assert.Single(stale).Phase);
        Assert.Equal(0, tracker.HeldCount);
    }

    [Fact]
    public void Cancel_drops_held_contacts_and_bumps_the_generation()
    {
        var tracker = new HkShellTouchTracker();
        Step(tracker, true, new HkShellTouchTracker.Contact(3, 0.1f, 0.1f));
        long before = tracker.Generation;
        tracker.Cancel();
        Assert.Equal(before + 1, tracker.Generation);
        Assert.Equal(0, tracker.HeldCount);
        // The same finger after a cancel starts a new gesture rather than ending a dead one.
        Assert.Equal(HkShellTouchTracker.Phase.Began,
            Assert.Single(Step(tracker, true, new HkShellTouchTracker.Contact(3, 0.1f, 0.1f))).Phase);
    }

    [Fact]
    public void Layout_conversion_flips_y_from_the_input_height()
    {
        HkShellTouchTracker.ToLayout(0f, 0f, 1240f, 1080f, 1100f, out float x0, out float y0);
        HkShellTouchTracker.ToLayout(1f, 1f, 1240f, 1080f, 1100f, out float x1, out float y1);
        Assert.Equal(0f, x0); Assert.Equal(1100f, y0);
        Assert.Equal(1240f, x1); Assert.Equal(20f, y1);
    }

    [Theory]
    // companionOn, popupAny, hudFaded, paused, expected
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]   // companion display off: pages hidden
    [InlineData(true, true, false, false, false)]     // popup owns the panel
    [InlineData(true, false, true, false, false)]     // cutscene / lore tablet faded the HUD
    [InlineData(true, false, true, true, true)]       // pause keeps the shell (title card) up
    public void Visibility_follows_companion_popups_and_gameplay_fades(
        bool companionOn, bool popupAny, bool hudFaded, bool paused, bool expected)
    {
        Assert.Equal(expected, HkShellPolicy.Visible(companionOn, popupAny, hudFaded, paused));
    }

    [Fact]
    public void Idle_waits_for_the_grace_period_only_after_gameplay_was_seen()
    {
        Assert.False(HkShellPolicy.Idle(true, true, -1f, 10f, 0.75f));
        Assert.True(HkShellPolicy.Idle(false, false, 9.9f, 10f, 0.75f));   // title before any gameplay
        Assert.False(HkShellPolicy.Idle(false, true, 9.5f, 10f, 0.75f));   // scene load: still in grace
        Assert.True(HkShellPolicy.Idle(false, true, 9.0f, 10f, 0.75f));
    }

    [Fact]
    public void Hud_band_converts_header_space_to_bottom_panel_pixels()
    {
        Assert.True(HkShellPolicy.HudBand(1240f, 1080f, 1000f, 200f, 2480f, 2160f, 10f, 0.62f, 0.62f,
            out float x, out float y, out float w, out float h));
        Assert.Equal(20f, x); Assert.Equal(20f, y);
        Assert.Equal(1240f, w, 3); Assert.Equal(228f, h, 3);

        Assert.False(HkShellPolicy.HudBand(0f, 1080f, 1000f, 200f, 2480f, 2160f, 10f, 0.62f, 0.62f,
            out _, out _, out _, out _));
        Assert.False(HkShellPolicy.HudBand(1240f, 1080f, 1000f, 0f, 2480f, 2160f, 10f, 0.62f, 0.62f,
            out _, out _, out _, out _));
        // A narrow header keeps its side padding rather than the 62% share.
        Assert.True(HkShellPolicy.HudBand(1240f, 1080f, 30f, 200f, 1240f, 1080f, 10f, 0.62f, 0.62f,
            out _, out _, out float narrow, out _));
        Assert.Equal(10f, narrow, 3);
    }
}
