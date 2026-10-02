// Host-only native capability marker model. Production reads this from PlayerData's
// game assembly; mod-weaver creates it only after all exact required sites verify.
public static class DualSoulsHollowKnightHookGate
{
    public static int Mask = 63;
    public static int Reads;
    public static bool FailRead;
    public static int GetVerifiedMask()
    {
        Reads++;
        if (FailRead) throw new System.InvalidOperationException("modeled unavailable native proof");
        return Mask;
    }
}
