using System;
using System.Runtime;

// Background GC in affected CoreCLR versions can charge unused allocation-context
// bytes to a thread. Keep the strict counter valid; actual allocations still count.
internal sealed class StrictAllocationScope : IDisposable
{
    public StrictAllocationScope()
    {
        // Reject before calling CoreCLR: failed nested admission can retire its owner.
        if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            throw new InvalidOperationException("Strict allocation measurement already has an active owner.");
        if (!GC.TryStartNoGCRegion(16 * 1024 * 1024))
            throw new InvalidOperationException("Strict allocation measurement could not enter its bounded no-GC region.");
    }

    // A collection or exhausted budget invalidates the measurement and must fail it.
    public void Dispose() => GC.EndNoGCRegion();
}
