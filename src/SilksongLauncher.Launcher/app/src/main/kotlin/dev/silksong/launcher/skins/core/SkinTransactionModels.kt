package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import java.util.Collections

data class TransactionEnvelope(
    val transactionId: String, val operation: SkinOperationKind,
    val baseGenerationId: String, val baseGenerationSha256: String,
    val prior: ActivationSnapshot, val target: ActivationSnapshot,
    val binding: SkinBindingToken, val priorEstablishedOnBinding: Boolean,
)
data class TransactionCorrelation(val transactionId: String, val binding: SkinBindingToken)
data class RegistryCommitReceipt(val expectedGenerationId: String, val expectedGenerationSha256: String,
    val newGenerationId: String, val newGenerationSha256: String)
/** Trusted executor-supplied canonical-read result, NOT proof manufactured by this reducer.
 * Task4 owns authoritative event construction and disk verification. */
data class VerifiedRegistryHead(val generationId: String, val generationSha256: String,
    val activation: ActivationSnapshot, val interlock: RotationInterlock)
sealed interface SkinCommand {
    data class Prepare(val envelope: TransactionEnvelope, val desired: ActiveVisual) : SkinCommand
    data class Arm(val envelope: TransactionEnvelope) : SkinCommand
    data class Apply(val correlation: TransactionCorrelation) : SkinCommand
    data class Rollback(val correlation: TransactionCorrelation) : SkinCommand
    data class Commit(val correlation: TransactionCorrelation, val expectedGenerationId: String,
        val expectedGenerationSha256: String, val closure: ActivationSnapshot) : SkinCommand
}
enum class TransactionPhase { IDLE, PREPARING, PREPARED, ARMED, APPLIED, ROLLBACK_PENDING, ROLLED_BACK, COMMITTED, BLOCKED }
data class TransactionState(
    val phase: TransactionPhase = TransactionPhase.IDLE,
    val interlock: RotationInterlock = RotationInterlock.clear(),
    val binding: SkinBindingToken? = null,
    val armCommitReceipt: RegistryCommitReceipt? = null,
    val pendingClosure: ActivationSnapshot? = null,
    val envelope: TransactionEnvelope? = null,
    val activation: ActivationSnapshot? = null,
    val originalFailure: String? = null,
    val rollbackFailure: String? = null,
    val completionReceipt: RegistryCommitReceipt? = null,
    val failureReceipt: RegistryCommitReceipt? = null,
)
sealed interface TransactionEvent {
    data class Begin(val envelope: TransactionEnvelope) : TransactionEvent
    data class Prepared(val correlation: TransactionCorrelation) : TransactionEvent
    data class ArmCommitted(val correlation: TransactionCorrelation, val commitReceipt: RegistryCommitReceipt,
        val verifiedHead: VerifiedRegistryHead) : TransactionEvent
    data class ApplyVerified(val correlation: TransactionCorrelation) : TransactionEvent
    data class ApplyFailed(val correlation: TransactionCorrelation, val code: String) : TransactionEvent
    /** Fresh means prior was not established on THIS envelope binding, never permission to rebind. */
    data class RollbackVerified(val correlation: TransactionCorrelation, val freshBinding: Boolean) : TransactionEvent
    data class RollbackFailed(val correlation: TransactionCorrelation, val code: String,
        val persistedFailureReceipt: RegistryCommitReceipt? = null, val verifiedHead: VerifiedRegistryHead? = null) : TransactionEvent
    data class CompletionCommitted(val correlation: TransactionCorrelation, val commitReceipt: RegistryCommitReceipt,
        val verifiedHead: VerifiedRegistryHead) : TransactionEvent
    data class CompletionRejected(val correlation: TransactionCorrelation, val code: String) : TransactionEvent
    data class CompletionIndeterminate(val correlation: TransactionCorrelation) : TransactionEvent
}
class TransactionDecision(val state: TransactionState, val diagnosis: String, vararg commands: SkinCommand) {
    val commands: List<SkinCommand> = Collections.unmodifiableList(commands.toList())
}
