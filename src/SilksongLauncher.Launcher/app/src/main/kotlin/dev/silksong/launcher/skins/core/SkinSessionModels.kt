package dev.silksong.launcher.skins.core

enum class SessionLeasePhase { LAUNCH_PENDING, GAME_OWNED, CLOSED }
enum class SessionMutationGate { UNKNOWN, ACTIVE, CLEAR }
enum class SessionClaimUse { AVAILABLE, IN_FLIGHT, CONSUMED, REJECTED }
enum class SessionOperation { CLAIM, CLOSE }
enum class SessionLiveness { UNKNOWN, ALIVE, DEAD }
enum class SessionPresence { UNKNOWN, PRESENT, ABSENT }
enum class SessionFailure { DURABILITY_UNAVAILABLE, INDETERMINATE, LIFECYCLE_BLOCKED, SESSION_RECOVERY_AMBIGUOUS, PROFILE_QUOTA_EXCEEDED, DOCUMENT_INVALID }
data class SessionProcessIdentity(val uid: Int, val pid: Int, val processStartToken: String)
/** TokenSha256 is the digest of decoded token bytes, never an executable credential. */
data class SessionBinding(val profileId: String, val descriptorId: String, val descriptorSha256: String, val descriptorPath: String,
    val leaseId: String, val tokenSha256: String, val sessionSequence: Long, val registryGenerationId: String, val registrySha256: String, val launcherOwner: SessionProcessIdentity)
data class SessionLeaseHead(val descriptorId: String, val leaseId: String, val transitionSequence: Long, val state: SessionLeasePhase, val sha256: String)
data class SessionLeaseDocument(val schemaVersion: Int, val binding: SessionBinding, val transitionSequence: Long, val transitionId: String,
    val parentTransitionId: String?, val state: SessionLeasePhase, val gameOwner: SessionProcessIdentity?, val closeReason: String?)
/** Trusted locked-store projection: canonical bytes verified against this digest. Construction proves no runtime truth. */
data class VerifiedSessionLease(val head: SessionLeaseHead, val document: SessionLeaseDocument, val canonicalDocumentSha256: String)
data class SessionCorrelation(val binding: SessionBinding?, val ordinal: Long)
sealed interface SessionBarrier {
    data class ActiveInstalled(val head: SessionLeaseHead) : SessionBarrier
    data class ActiveRemoved(val expectedParent: SessionLeaseHead) : SessionBarrier
}
/** Coordinator attests descriptor qualification, pending publication and the global active barrier. */
data class SessionAcquisitionEvidence(val pending: VerifiedSessionLease, val barrier: SessionBarrier.ActiveInstalled?)
data class SessionTransitionReceipt(val correlation: SessionCorrelation, val operation: SessionOperation, val parent: VerifiedSessionLease,
    val child: VerifiedSessionLease, val barrier: SessionBarrier?)
sealed interface SessionTransitionResult {
    data class Durable(val receipt: SessionTransitionReceipt?) : SessionTransitionResult
    data class Failed(val code: SessionFailure) : SessionTransitionResult
}
data class SessionTargetProcess(val packageName: String, val processName: String)
data class SessionOwnerEvidence(val correlation: SessionCorrelation, val binding: SessionBinding, val expectedOwner: SessionProcessIdentity,
    val liveness: SessionLiveness, val aliveOwner: SessionProcessIdentity?)
/** Verifier uses the exact build-configured target; missing configuration, denied queries and PID/start mismatches are UNKNOWN. */
data class SessionTargetEvidence(val correlation: SessionCorrelation, val binding: SessionBinding, val target: SessionTargetProcess,
    val presence: SessionPresence, val presentOwner: SessionProcessIdentity?)
/** COMPLETE bounded profile validation/reconciliation with no open lease. Constructor is not a verifier. */
data class SessionProfileRecoveryEvidence(val profileId: String, val correlation: SessionCorrelation)
sealed interface SessionRecoveryResult {
    data class NoOpenLease(val profile: SessionProfileRecoveryEvidence?) : SessionRecoveryResult
    data class RetainedOpenLease(val lease: VerifiedSessionLease, val barrier: SessionBarrier.ActiveInstalled?, val owner: SessionOwnerEvidence?,
        val target: SessionTargetEvidence?, val observedClaim: SessionTransitionReceipt? = null) : SessionRecoveryResult
    data class RecoveredClosed(val parent: VerifiedSessionLease, val receipt: SessionTransitionReceipt?, val owner: SessionOwnerEvidence?,
        val target: SessionTargetEvidence?, val profile: SessionProfileRecoveryEvidence?) : SessionRecoveryResult
    data class Deferred(val code: SessionFailure) : SessionRecoveryResult
}
data class SessionRecoveryRecord(val correlation: SessionCorrelation, val result: SessionRecoveryResult)
sealed interface SessionCommand {
    val correlation: SessionCorrelation
    data class ClaimExistingBinding(override val correlation: SessionCorrelation, val binding: SessionBinding, val owner: SessionProcessIdentity) : SessionCommand
    data class CloseExistingBinding(override val correlation: SessionCorrelation, val binding: SessionBinding, val reason: String) : SessionCommand
    data class RecoverExistingSessionStore(override val correlation: SessionCorrelation, val expectedBinding: SessionBinding?) : SessionCommand
}
sealed interface SessionEvent {
    data class BindIssuedPending(val evidence: SessionAcquisitionEvidence?) : SessionEvent
    data class ClaimRequested(val binding: SessionBinding?, val owner: SessionProcessIdentity?) : SessionEvent
    data class ClaimCompleted(val correlation: SessionCorrelation?, val result: SessionTransitionResult?) : SessionEvent
    data class CloseRequested(val binding: SessionBinding?, val reason: String?) : SessionEvent
    data class CloseCompleted(val correlation: SessionCorrelation?, val result: SessionTransitionResult?) : SessionEvent
    data object RecoveryRequested : SessionEvent
    data class RecoveryCompleted(val correlation: SessionCorrelation?, val result: SessionRecoveryResult?) : SessionEvent
}
/** Pending recovery preserves exact-owner retry. Observed owned recovery is close/recovery-only (REJECTED),
 * never a successful bridge claim. Consumed claims stay consumed but lose writes after recovery.
 * A fresh core belongs to integration, not a reset event on this state. */
/** One bounded observation survives later deferred recovery. Equal ordinals mean recovery-observed
 * ownership, not a successful bridge claim; later recovery revokes a consumed claim.
 * Neither shape establishes runtime truth by construction. */
data class SessionClaimRecoveryFence(val recoveryCorrelation: SessionCorrelation, val claimReceipt: SessionTransitionReceipt)
data class SessionState(val acquisition: SessionAcquisitionEvidence? = null, val lease: VerifiedSessionLease? = null,
    val claimUse: SessionClaimUse = SessionClaimUse.AVAILABLE, val pinnedOwner: SessionProcessIdentity? = null, val pinnedCloseReason: String? = null,
    val pending: SessionCommand? = null, val gate: SessionMutationGate = SessionMutationGate.UNKNOWN, val claimReceipt: SessionTransitionReceipt? = null,
    val closedReceipt: SessionTransitionReceipt? = null, val recovery: SessionRecoveryRecord? = null, val recoveryHighWater: Long = 0, val ordinal: Long = 0,
    val claimAttempt: SessionCommand.ClaimExistingBinding? = null, val claimRecoveryFence: SessionClaimRecoveryFence? = null,
    // Last explicit request: retry ordinals advance, binding/reason stay pinned.
    // Recovery preserves it and retains its independently qualified terminal audit;
    // that audit survives later profile recovery and is never an explicit-close success.
    val closeAttempt: SessionCommand.CloseExistingBinding? = null, val recoveryClosure: SessionRecoveryRecord? = null)
/** At most one immutable command; no mutable collection or captured I/O capability. */
data class SessionDecision(val state: SessionState?, val diagnosis: String, val command: SessionCommand? = null, val completedClose: SessionTransitionReceipt? = null)
