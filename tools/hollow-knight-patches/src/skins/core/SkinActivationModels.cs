namespace DualSouls.Skins.HollowKnight.Core
{
    // Value-only mirror of Kotlin registry/SkinActivation.kt; binding is shared with lifecycle.
    public enum SkinMode { OFF, ON, ROTATE }
    public enum InterlockState { CLEAR, ARMED, ROLLBACK_FAILED }
    public enum SkinOperationKind { STARTUP_APPLY, MODE_ON, MODE_OFF, DEATH_ROTATION, REBIND_APPLY }
    public abstract record ActiveVisual
    {
        private ActiveVisual() { }
        public sealed record Vanilla : ActiveVisual;
        public sealed record Pack(string Id, string TreeSha256, string ContentSha256, string ImportReceiptSha256) : ActiveVisual;
    }
    public sealed record ActivationSnapshot(SkinMode Mode, string SelectedPackId, ActiveVisual Active, long SkinStamp);
    public sealed record RotationInterlock(InterlockState State, string TransactionId, SkinOperationKind? Operation,
        string BaseGenerationId, string BaseGenerationSha256, ActivationSnapshot Prior, ActivationSnapshot Target,
        SkinBindingToken BindingToken, bool? PriorEstablishedOnBinding, string OriginalFailure, string RollbackFailure)
    {
        public static RotationInterlock Clear() => new(InterlockState.CLEAR, null, null, null, null, null, null, null, null, null, null);
    }
}
