package dev.silksong.launcher.skins.core

import java.util.Collections
import dev.silksong.launcher.skins.registry.*

/** Origin is retained unchanged, including both authorities and exact issued readiness.
 * Qualified durable recovery retires it into one bounded historical audit. */
data class RotationTransactionOperation(val origin: RotationModeState, val readiness: RotationReadyIntent?,
    val transaction: TransactionState, val reboundBlocked: Boolean = false)
data class RotationTransactionState(val mode: RotationModeState, val operation: RotationTransactionOperation? = null,
    val readinessReboundBlocked: Boolean = false, val lastRecovery: RotationRecoveryRecord? = null)
/** Exact current authority supplied by the trusted caller, not discovered by this reducer. */
data class RotationRecoveryAuthority(val hero: HeroBindingToken, val skin: SkinBindingToken,
    val liveProof: HeroVerifiedVisual? = null)
/** Trusted serialized outcome attestation: ALL old execution and late callbacks are
 * irrevocably fenced, and this exact parent was authoritative at the closure CAS.
 * The reducer verifies correlation/chain only; constructing this value is not runtime proof.
 * Optional lateFailureReceipt is ONE ARM-to-failure hop, only if no failure receipt was
 * stored and both original failure codes are known. It never replaces stored authority. */
data class RotationOutcomeFence(val correlation: TransactionCorrelation, val originalHero: HeroBindingToken,
    val authoritativeParent: VerifiedRegistryHead, val lateFailureReceipt: RegistryCommitReceipt? = null)
sealed interface RotationRecoveryEvidence {
    val authority: RotationRecoveryAuthority
    /** Resolution of the exact already-issued pending closure, never an outcome selector. */
    data class IssuedClosureOutcome(val operation: RotationTransactionOperation, val fence: RotationOutcomeFence,
        val receipt: RegistryCommitReceipt, val head: VerifiedRegistryHead,
        override val authority: RotationRecoveryAuthority) : RotationRecoveryEvidence
    /** Trusted completed original-binding prior restoration, serialized with the fence and
     * exact authoritative parent. A possibly committed target cannot be superseded by a
     * visual observation: the verifier must establish this whole serialized outcome. */
    data class PriorRestoredAndFenced(val operation: RotationTransactionOperation, val fence: RotationOutcomeFence,
        val restoration: HeroVerifiedVisual, val receipt: RegistryCommitReceipt, val head: VerifiedRegistryHead,
        override val authority: RotationRecoveryAuthority) : RotationRecoveryEvidence
    data class VisualClosure(val operation: RotationTransactionOperation,
        val completion: TransactionEvent.CompletionCommitted, override val authority: RotationRecoveryAuthority) : RotationRecoveryEvidence
    /** All old execution deliveries irrevocably fenced; visual Apply never ran.
     * Prepare/ARM may have occurred. A head observation alone is insufficient. */
    data class VisualFencedNotExecuted(val operation: RotationTransactionOperation, val head: VerifiedRegistryHead,
        val receipt: RegistryCommitReceipt? = null, override val authority: RotationRecoveryAuthority) : RotationRecoveryEvidence
    data class ModeClosure(val operation: ModeOperation, val completion: RotationModeEvent.ModeCommitted,
        override val authority: RotationRecoveryAuthority) : RotationRecoveryEvidence
    /** Exact intent never consumed/executed; all its execution deliveries fenced. */
    data class ReadinessFencedNotExecuted(val intent: RotationReadyIntent, val head: VerifiedRegistryHead,
        override val authority: RotationRecoveryAuthority) : RotationRecoveryEvidence
}
/** One bounded audit snapshot: before.lastRecovery must be null. No resource release authority. */
data class RotationRecoveryRecord(val before: RotationTransactionState, val evidence: RotationRecoveryEvidence)
sealed interface RotationTransactionEvent {
    data class Recover(val evidence: RotationRecoveryEvidence) : RotationTransactionEvent
    data class Mode(val event: RotationModeEvent) : RotationTransactionEvent
    data class ConsumeReady(val intent: RotationReadyIntent) : RotationTransactionEvent
    data class Transaction(val hero: HeroBindingToken, val event: TransactionEvent) : RotationTransactionEvent
}
sealed interface RotationTransactionCommand {
    data class Mode(val command: RotationModeCommand) : RotationTransactionCommand
    /** Caller must check the original hero as well as the inner command's skin correlation. */
    data class Transaction(val hero: HeroBindingToken, val command: SkinCommand) : RotationTransactionCommand
}
class RotationTransactionDecision(val state: RotationTransactionState, val diagnosis: String,
    commands: List<RotationTransactionCommand> = emptyList()) {
    val commands: List<RotationTransactionCommand> = Collections.unmodifiableList(commands.toList())
}
