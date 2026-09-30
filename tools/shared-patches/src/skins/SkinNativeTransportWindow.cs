namespace DualSouls.Skins
{
    // Only startup/menu mutation/pending evidence arms this bounded window. Settled frames
    // perform no bridge calls, discovery, diagnostics or allocations.
    public sealed class SkinNativeTransportWindow
    {
        public bool Pending { get; private set; }
        public bool TimedOut { get; private set; }
        float deadline, next;
        public void Start(float now) { Pending = true; TimedOut = false; deadline = now + 60f; next = now; }
        public bool Due(float now)
        {
            if (!Pending) return false;
            if (now >= deadline) { Pending = false; TimedOut = true; return false; }
            if (now < next) return false;
            next = now + 0.5f;
            return true;
        }
        public void Complete(bool stillPending) { Pending = stillPending; }
        public void CompleteEvidence(string state) { Complete(state == "PENDING" || state == "STALE" || state == "UNREADABLE"); }
        public void Cancel() { Pending = false; TimedOut = false; }
    }
}
