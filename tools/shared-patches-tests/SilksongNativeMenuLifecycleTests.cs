using DualSouls.Mods.Silksong;
using Xunit;

namespace SharedPatches.Tests;

public sealed class SilksongNativeMenuLifecycleTests
{
    [Fact]
    public void ClearCancelsTransitionAndRejectsItsLease()
    {
        var lifecycle = new SilksongNativeMenuLifecycle<object>();
        var first = new object();
        lifecycle.Bind(first);

        Assert.True(lifecycle.TryBegin(out var transition));
        Assert.True(lifecycle.IsCurrent(transition, first));

        lifecycle.Clear();

        Assert.Null(lifecycle.Current);
        Assert.False(lifecycle.Transitioning);
        Assert.False(lifecycle.IsCurrent(transition, first));
        Assert.False(lifecycle.Complete(transition, first));
    }

    [Fact]
    public void RebindRejectsOldLeaseWithoutMutatingNewTransition()
    {
        var lifecycle = new SilksongNativeMenuLifecycle<object>();
        var first = new object();
        var second = new object();
        lifecycle.Bind(first);
        Assert.True(lifecycle.TryBegin(out var stale));

        lifecycle.Clear();
        lifecycle.Bind(second);
        Assert.True(lifecycle.TryBegin(out var current));

        Assert.False(lifecycle.IsCurrent(stale, first));
        Assert.False(lifecycle.IsCurrent(stale, second));
        Assert.False(lifecycle.Complete(stale, first));
        Assert.True(lifecycle.Transitioning);
        Assert.Same(second, lifecycle.Current);

        Assert.True(lifecycle.IsCurrent(current, second));
        Assert.True(lifecycle.Complete(current, second));
        Assert.False(lifecycle.Transitioning);
        Assert.Same(second, lifecycle.Current);
    }

    [Fact]
    public void SameBindingPauseLossCanCancelTransitionWithoutClearingBinding()
    {
        var lifecycle = new SilksongNativeMenuLifecycle<object>();
        var binding = new object();
        lifecycle.Bind(binding);
        Assert.True(lifecycle.TryBegin(out var transition));

        var sessionReady = true;
        var paused = false;
        var canContinue = lifecycle.IsCurrent(transition, binding) &&
                          sessionReady && paused;
        Assert.False(canContinue);
        Assert.True(lifecycle.Cancel(transition, binding));

        Assert.False(lifecycle.Transitioning);
        Assert.Same(binding, lifecycle.Current);
        Assert.True(lifecycle.TryBegin(out _));
    }

    [Fact]
    public void ExactBindingIdentityIsRequiredThroughoutTransition()
    {
        var lifecycle = new SilksongNativeMenuLifecycle<object>();
        var bound = new object();
        var lookalike = new object();
        lifecycle.Bind(bound);
        Assert.True(lifecycle.TryBegin(out var transition));

        Assert.True(lifecycle.IsCurrent(transition, bound));
        Assert.False(lifecycle.IsCurrent(transition, lookalike));
        Assert.False(lifecycle.Complete(transition, lookalike));
        Assert.True(lifecycle.Transitioning);
    }
}
