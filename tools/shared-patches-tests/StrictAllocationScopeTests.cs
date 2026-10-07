using System.Runtime;
using Xunit;

[CollectionDefinition("HollowKnightPauseOwners", DisableParallelization = true)]
public sealed class HollowKnightPauseOwnersCollection { }

[Collection("HollowKnightPauseOwners")]
public sealed class StrictAllocationScopeTests
{
    [Fact]
    public void Guarded_empty_interval_remains_exactly_zero()
    {
        using var scope = new StrictAllocationScope();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 1000; i++) sum += i & 1;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(500, sum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Guard_does_not_hide_real_managed_allocations()
    {
        using var scope = new StrictAllocationScope();
        long before = GC.GetAllocatedBytesForCurrentThread();
        object value = new byte[37];
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(value);
        Assert.True(allocated > 0);
    }

    [Fact]
    public void Nested_admission_fails_without_releasing_the_owner()
    {
        using var scope = new StrictAllocationScope();
        Assert.Throws<InvalidOperationException>(() => new StrictAllocationScope());
        Assert.Equal(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
    }

    [Fact]
    public void Unexpected_collection_invalidates_the_measurement_instead_of_passing()
    {
        var scope = new StrictAllocationScope();
        GC.Collect();
        Assert.Throws<InvalidOperationException>(() => scope.Dispose());
        Assert.NotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
    }

    [Fact]
    public void Body_exception_still_releases_the_scope()
    {
        bool entered = false;
        void ThrowInsideScope()
        {
            using var scope = new StrictAllocationScope();
            entered = true;
            throw new InvalidOperationException("Owning body failed.");
        }
        Assert.Throws<InvalidOperationException>(ThrowInsideScope);
        Assert.True(entered);
        Assert.NotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        using var recovered = new StrictAllocationScope();
    }
}
