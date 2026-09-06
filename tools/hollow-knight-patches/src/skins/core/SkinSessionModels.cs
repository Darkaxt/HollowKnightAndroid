namespace DualSouls.Skins.HollowKnight.Core
{
    public enum SessionLeasePhase { LAUNCH_PENDING, GAME_OWNED, CLOSED }
    public enum SessionMutationGate { UNKNOWN, ACTIVE, CLEAR }
    public enum SessionClaimUse { AVAILABLE, IN_FLIGHT, CONSUMED, REJECTED }
    public enum SessionOperation { CLAIM, CLOSE }
    public enum SessionLiveness { UNKNOWN, ALIVE, DEAD }
    public enum SessionPresence { UNKNOWN, PRESENT, ABSENT }
    public enum SessionFailure { DURABILITY_UNAVAILABLE, INDETERMINATE, LIFECYCLE_BLOCKED, SESSION_RECOVERY_AMBIGUOUS, PROFILE_QUOTA_EXCEEDED, DOCUMENT_INVALID }
    public sealed record SessionProcessIdentity(int Uid, int Pid, string ProcessStartToken);
    // TokenSha256 is the digest of decoded token bytes, never an executable credential.
    public sealed record SessionBinding(string ProfileId, string DescriptorId, string DescriptorSha256, string DescriptorPath,
        string LeaseId, string TokenSha256, long SessionSequence, string RegistryGenerationId, string RegistrySha256, SessionProcessIdentity LauncherOwner);
    public sealed record SessionLeaseHead(string DescriptorId, string LeaseId, long TransitionSequence, SessionLeasePhase State, string Sha256);
    public sealed record SessionLeaseDocument(int SchemaVersion, SessionBinding Binding, long TransitionSequence, string TransitionId,
        string ParentTransitionId, SessionLeasePhase State, SessionProcessIdentity GameOwner, string CloseReason);
    // Trusted locked-store projection: canonical bytes were verified against this digest.
    // Construction establishes no runtime truth. No hashing, parsing, or unlocked enrichment here.
    public sealed record VerifiedSessionLease(SessionLeaseHead Head, SessionLeaseDocument Document, string CanonicalDocumentSha256);
    public sealed record SessionCorrelation(SessionBinding Binding, long Ordinal);
    public abstract record SessionBarrier
    {
        private SessionBarrier() { }
        public sealed record ActiveInstalled(SessionLeaseHead Head) : SessionBarrier;
        public sealed record ActiveRemoved(SessionLeaseHead ExpectedParent) : SessionBarrier;
    }
    // Coordinator attests completed descriptor qualification, pending publication and global active barrier.
    public sealed record SessionAcquisitionEvidence(VerifiedSessionLease Pending, SessionBarrier.ActiveInstalled Barrier);
    public sealed record SessionTransitionReceipt(SessionCorrelation Correlation, SessionOperation Operation, VerifiedSessionLease Parent,
        VerifiedSessionLease Child, SessionBarrier Barrier);
    public abstract record SessionTransitionResult
    {
        private SessionTransitionResult() { }
        public sealed record Durable(SessionTransitionReceipt Receipt) : SessionTransitionResult;
        public sealed record Failed(SessionFailure Code) : SessionTransitionResult;
    }
    public sealed record SessionTargetProcess(string PackageName, string ProcessName);
    public sealed record SessionOwnerEvidence(SessionCorrelation Correlation, SessionBinding Binding, SessionProcessIdentity ExpectedOwner,
        SessionLiveness Liveness, SessionProcessIdentity AliveOwner);
    // Trusted verifier must use the exact build-configured target for this binding and operation.
    // Missing configuration, denied queries and PID/start mismatches are UNKNOWN, never DEAD/ABSENT.
    public sealed record SessionTargetEvidence(SessionCorrelation Correlation, SessionBinding Binding, SessionTargetProcess Target,
        SessionPresence Presence, SessionProcessIdentity PresentOwner);
    // Attests COMPLETE bounded profile validation/reconciliation and no remaining open lease.
    // Denied/malformed/ambiguous scans cannot produce this evidence. Constructor is not a verifier.
    public sealed record SessionProfileRecoveryEvidence(string ProfileId, SessionCorrelation Correlation);
    public abstract record SessionRecoveryResult
    {
        private SessionRecoveryResult() { }
        public sealed record NoOpenLease(SessionProfileRecoveryEvidence Profile) : SessionRecoveryResult;
        public sealed record RetainedOpenLease(VerifiedSessionLease Lease, SessionBarrier.ActiveInstalled Barrier, SessionOwnerEvidence Owner,
            SessionTargetEvidence Target, SessionTransitionReceipt ObservedClaim = null) : SessionRecoveryResult;
        public sealed record RecoveredClosed(VerifiedSessionLease Parent, SessionTransitionReceipt Receipt, SessionOwnerEvidence Owner,
            SessionTargetEvidence Target, SessionProfileRecoveryEvidence Profile) : SessionRecoveryResult;
        public sealed record Deferred(SessionFailure Code) : SessionRecoveryResult;
    }
    public sealed record SessionRecoveryRecord(SessionCorrelation Correlation, SessionRecoveryResult Result);
    public abstract record SessionCommand(SessionCorrelation Correlation)
    {
        public sealed record ClaimExistingBinding(SessionCorrelation Correlation, SessionBinding Binding, SessionProcessIdentity Owner) : SessionCommand(Correlation);
        public sealed record CloseExistingBinding(SessionCorrelation Correlation, SessionBinding Binding, string Reason) : SessionCommand(Correlation);
        public sealed record RecoverExistingSessionStore(SessionCorrelation Correlation, SessionBinding ExpectedBinding) : SessionCommand(Correlation);
    }
    public abstract record SessionEvent
    {
        private SessionEvent() { }
        public sealed record BindIssuedPending(SessionAcquisitionEvidence Evidence) : SessionEvent;
        public sealed record ClaimRequested(SessionBinding Binding, SessionProcessIdentity Owner) : SessionEvent;
        public sealed record ClaimCompleted(SessionCorrelation Correlation, SessionTransitionResult Result) : SessionEvent;
        public sealed record CloseRequested(SessionBinding Binding, string Reason) : SessionEvent;
        public sealed record CloseCompleted(SessionCorrelation Correlation, SessionTransitionResult Result) : SessionEvent;
        public sealed record RecoveryRequested : SessionEvent;
        public sealed record RecoveryCompleted(SessionCorrelation Correlation, SessionRecoveryResult Result) : SessionEvent;
    }
    // Pending recovery preserves exact-owner retry eligibility. Observed owned recovery is
    // close/recovery-only (REJECTED), never a successful bridge claim. A consumed claim stays
    // consumed after recovery but loses write eligibility; a fresh core is integration-owned.
    // One bounded observation retained even when later recovery is deferred. Equal ordinals
    // identify recovery-observed ownership, not a successful bridge claim; later recovery
    // revokes a previously consumed claim. Neither shape is runtime proof by construction.
    public sealed record SessionClaimRecoveryFence(SessionCorrelation RecoveryCorrelation, SessionTransitionReceipt ClaimReceipt);
    public sealed record SessionState(SessionAcquisitionEvidence Acquisition = null, VerifiedSessionLease Lease = null,
        SessionClaimUse ClaimUse = SessionClaimUse.AVAILABLE, SessionProcessIdentity PinnedOwner = null, string PinnedCloseReason = null,
        SessionCommand Pending = null, SessionMutationGate Gate = SessionMutationGate.UNKNOWN, SessionTransitionReceipt ClaimReceipt = null,
        SessionTransitionReceipt ClosedReceipt = null, SessionRecoveryRecord Recovery = null, long RecoveryHighWater = 0, long Ordinal = 0,
        SessionCommand.ClaimExistingBinding ClaimAttempt = null, SessionClaimRecoveryFence ClaimRecoveryFence = null,
        // Last explicit request: retry ordinals advance, but binding/reason stay pinned.
        // Recovery preserves it and retains its independently qualified terminal audit;
        // that audit survives later profile recovery and is never an explicit-close success.
        SessionCommand.CloseExistingBinding CloseAttempt = null, SessionRecoveryRecord RecoveryClosure = null);
    // At most one immutable command; no mutable collection or captured I/O capability.
    public sealed record SessionDecision(SessionState State, string Diagnosis, SessionCommand Command = null, SessionTransitionReceipt CompletedClose = null);
}
