using System;
using Xunit;
using DualSouls.DualScreen;

public sealed class SilksongHudLifecycleTests
{
    [Theory]
    [InlineData("state", false)]
    [InlineData("pause", false)]
    [InlineData("unload", false)]
    [InlineData("completion", false)]
    [InlineData("state", true)]
    [InlineData("pause", true)]
    [InlineData("unload", true)]
    [InlineData("completion", true)]
    public void Manager_callbacks_reject_stale_captured_owner_before_and_after_rebind(string kind, bool rebind)
    {
        object oldOwner = new object(), next = new object(), current = oldOwner;
        int restores = 0, rearms = 0;
        var callbacks = new DsHudManagerCallbacks(() => current, () => restores++, () => restores++, () => rearms++);
        var old = callbacks.Bind(oldOwner, false);
        old.Unloading(); Assert.True(callbacks.TransitionPending); Assert.Equal(1, restores);
        current = next; // replacement has happened, even if V2.Update has not bound it yet
        if (rebind) callbacks.Bind(next, true);
        int expectedRestores = restores, expectedRearms = rearms;
        if (kind == "state") old.State(false, false);
        if (kind == "pause") old.Pause(true);
        if (kind == "unload") old.Unloading();
        if (kind == "completion") old.Finished();
        Assert.Equal(expectedRestores, restores); Assert.Equal(expectedRearms, rearms);
        Assert.True(callbacks.TransitionPending);
    }

    [Fact]
    public void Current_manager_completion_only_rearms_a_pending_boundary()
    {
        object current = new object(); int restores = 0, rearms = 0;
        var callbacks = new DsHudManagerCallbacks(() => current, () => restores++, () => restores++, () => rearms++);
        var live = callbacks.Bind(current, true);
        Assert.True(callbacks.TransitionPending); Assert.Equal(1, restores);
        live.Finished(); Assert.False(callbacks.TransitionPending); Assert.Equal(1, rearms);
        live.Finished(); Assert.Equal(1, rearms);
        live.Pause(true); Assert.Equal(2, restores);
        live.State(true, false); Assert.True(callbacks.TransitionPending); Assert.Equal(3, restores);
        callbacks.Unbind(); int count = restores;
        live.Unloading(); live.Pause(true); live.State(false, false); live.Finished();
        Assert.Equal(count, restores); Assert.True(callbacks.TransitionPending);
    }

    [Fact]
    public void Release_failure_stays_pending_and_retry_completes_in_order()
    {
        var calls = new System.Collections.Generic.List<string>();
        bool failRestore = true;
        var release = new DsHudReleaseState(
            () => { calls.Add("restore"); if (failRestore) throw new InvalidOperationException("restore failed"); },
            () => calls.Add("dispose"), () => calls.Add("release"), () => calls.Add("retire"));
        Assert.True(release.CanRoute); Assert.True(release.BlocksReplacement);

        release.RequestShutdown(release.ReleasePresentation);
        Assert.NotNull(release.LastFailure); Assert.True(release.Pending); Assert.False(release.CanRoute);
        Assert.Equal(new[] { "restore" }, calls);

        failRestore = false;
        Assert.True(release.Retry());
        Assert.True(release.Completed); Assert.False(release.Pending); Assert.False(release.BlocksReplacement);
        Assert.Null(release.LastFailure);
        Assert.Equal(new[] { "restore", "restore", "dispose", "release", "retire" }, calls);
        Assert.True(release.Retry()); Assert.Equal(5, calls.Count);
    }
}
