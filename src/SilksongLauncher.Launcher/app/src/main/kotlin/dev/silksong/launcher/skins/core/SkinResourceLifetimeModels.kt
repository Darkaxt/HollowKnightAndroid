package dev.silksong.launcher.skins.core

import java.util.Collections

/** Opaque authoritative runtime identity values. Stamp is correlation only, NOT SkinStamp arithmetic
 * authority. Construction/binding belongs to the future adapter; prior and target may use different bindings. */
data class ResourceLifetimePlan(val planId: String, val binding: String, val generationId: String, val stamp: String)
data class ResourceLifetimeClosure(val operationId: Long, val transactionId: String,
    val prior: ResourceLifetimePlan?, val target: ResourceLifetimePlan)
data class ResourceDisposalOperation(val operationId: Long, val closure: ResourceLifetimeClosure, val allocationId: Long)

/** Retirement checkpoint only. No cancellation, decoder-failure or teardown authority. */
class ResourceLifetimeState(
    val ledger: ResourceState = ResourceState(), val active: ResourceLifetimePlan? = null,
    val candidate: ResourceLifetimeClosure? = null, val candidateSealed: Boolean = false,
    val awaitingClones: ResourceLifetimeClosure? = null, val retirement: ResourceLifetimeClosure? = null,
    pendingDisposals: Collection<ResourceDisposalOperation> = emptyList(), val nextOperationId: Long = 1,
    val awaitingDurableCompletion: Boolean = false,
) {
    val pendingDisposals: List<ResourceDisposalOperation> = Collections.unmodifiableList(pendingDisposals.toList())
}
sealed interface ResourceLifetimeEvent {
    data class BeginCandidate(val transactionId: String, val target: ResourceLifetimePlan) : ResourceLifetimeEvent
    data class Admission(val event: ResourceEvent) : ResourceLifetimeEvent
    data class SealCandidate(val closure: ResourceLifetimeClosure) : ResourceLifetimeEvent
    /** Consume the value gate ONCE, BEFORE the caller starts visual execution. No writes are performed.
     * Failed/indeterminate outcomes stay blocked; their cleanup belongs to the next slice. */
    data class AwaitDurableCompletion(val closure: ResourceLifetimeClosure) : ResourceLifetimeEvent
    /** Trusted live verification of durable successful completion of this exact stored closure,
     * not a receipt/history lookup, command intent, or permission to commit a transaction. */
    data class DurableCompletionVerified(val closure: ResourceLifetimeClosure) : ResourceLifetimeEvent
    /** Exact acknowledgement of InvalidateCompanionClones: ALL prior binding/stamp clones destroyed. */
    data class CompanionClonesInvalidated(val closure: ResourceLifetimeClosure) : ResourceLifetimeEvent
    /** Authoritative live registry count for the exact pending prior binding/stamp, not an estimate. */
    data class RegisteredCloneCountVerified(val closure: ResourceLifetimeClosure, val count: Long) : ResourceLifetimeEvent
    data class DisposeAcknowledged(val operation: ResourceDisposalOperation) : ResourceLifetimeEvent
}
sealed interface ResourceLifetimeCommand {
    data class Admission(val command: ResourceCommand) : ResourceLifetimeCommand
    data class InvalidateCompanionClones(val closure: ResourceLifetimeClosure) : ResourceLifetimeCommand
    data class Dispose(val operation: ResourceDisposalOperation) : ResourceLifetimeCommand
}
class ResourceLifetimeDecision(val state: ResourceLifetimeState, val accepted: Boolean, val diagnosis: String,
    commands: Collection<ResourceLifetimeCommand> = emptyList()) {
    val commands: List<ResourceLifetimeCommand> = Collections.unmodifiableList(commands.toList())
}
