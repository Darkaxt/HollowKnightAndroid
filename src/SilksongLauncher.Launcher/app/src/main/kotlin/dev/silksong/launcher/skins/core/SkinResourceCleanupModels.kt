package dev.silksong.launcher.skins.core

import java.util.Collections

/** Trusted terminal decoder facts, not stop-request acknowledgement. */
enum class ResourceDecodeOutcome { PENDING, CREATED, NEVER_CREATED }
data class ResourceCleanupDecoder(val operation: ResourceDecodeOperation,
    val outcome: ResourceDecodeOutcome = ResourceDecodeOutcome.PENDING, val scratchReleased: Boolean = false)

/** Exact resource-only shutdown identity, never a fabricated transaction. */
data class ResourceShutdownOperation(val operationId: Long, val plan: ResourceLifetimePlan)
data class ResourceShutdownDisposal(val operationId: Long, val shutdown: ResourceShutdownOperation, val allocationId: Long)
enum class ResourcePostTransactionOutcome { UNKNOWN, VISUAL_ONLY_ROLLBACK, VERIFIED_DURABLE_ROLLBACK }
enum class ResourceRollbackClonePhase { CANDIDATE, OLD_PRIOR, QUALIFIED }
data class ResourceRollbackResolution(val closure: ResourceLifetimeClosure, val restoredPrior: ResourceLifetimePlan?, val clonePhase: ResourceRollbackClonePhase = ResourceRollbackClonePhase.CANDIDATE)

/** This wrapper is the gate authority; never bypass it through its nested lifetime snapshot.
 * All verification events are trusted live adapter facts, not disk proof manufactured here. */
class ResourceCleanupState(val lifetime: ResourceLifetimeState = ResourceLifetimeState(),
    val cancellation: ResourceLifetimeClosure? = null, val decoder: ResourceCleanupDecoder? = null,
    pendingDisposals: Collection<ResourceDisposalOperation> = emptyList(),
    val teardownRequested: Boolean = false, val rollback: ResourceRollbackResolution? = null,
    val shutdown: ResourceShutdownOperation? = null, val shutdownClonesQualified: Boolean = false,
    shutdownDisposals: Collection<ResourceShutdownDisposal> = emptyList()) {
    val pendingDisposals: List<ResourceDisposalOperation> = Collections.unmodifiableList(pendingDisposals.toList())
    val shutdownDisposals: List<ResourceShutdownDisposal> = Collections.unmodifiableList(shutdownDisposals.toList())
    val closed: Boolean get() = teardownRequested && lifetime.nextOperationId > 0 && lifetime.ledger.nextId > 0 &&
        !lifetime.candidateSealed && !lifetime.awaitingDurableCompletion && !shutdownClonesQualified && lifetime.active == null && lifetime.candidate == null &&
        lifetime.awaitingClones == null && lifetime.retirement == null && lifetime.pendingDisposals.isEmpty() &&
        cancellation == null && decoder == null && pendingDisposals.isEmpty() && rollback == null && shutdown == null &&
        shutdownDisposals.isEmpty() && lifetime.ledger.allocations.isEmpty() && lifetime.ledger.references.isEmpty() && lifetime.ledger.scratch == null
}
sealed interface ResourceCleanupEvent {
    data object Teardown : ResourceCleanupEvent
    /** Live adapter verification of durable restored-prior ownership for the exact consumed closure,
     * not visual rollback, command completion, or retained registry history. Restored planId retains
     * the prior ownership group; binding/generation/stamp are supplied, never derived numerically.
     * UNKNOWN/visual-only retain ownership and allow a later exact success or durable rollback event. */
    data class PostTransactionOutcome(val closure: ResourceLifetimeClosure, val outcome: ResourcePostTransactionOutcome,
        val restoredPrior: ResourceLifetimePlan?) : ResourceCleanupEvent
    /** ALL clones of the currently requested exact plan, not just one clone or the restored plan. */
    data class RollbackClonesInvalidated(val closure: ResourceLifetimeClosure, val plan: ResourceLifetimePlan) : ResourceCleanupEvent
    /** Authoritative live registry count for that same full closure and requested plan identity. */
    data class RollbackCloneCountVerified(val closure: ResourceLifetimeClosure, val plan: ResourceLifetimePlan, val count: Long) : ResourceCleanupEvent
    /** ALL current plan clones; an old rollback/retirement ACK cannot qualify this new operation. */
    data class ShutdownClonesInvalidated(val operation: ResourceShutdownOperation) : ResourceCleanupEvent
    /** Authoritative live registry count for the exact shutdown operation and current plan. */
    data class ShutdownCloneCountVerified(val operation: ResourceShutdownOperation, val count: Long) : ResourceCleanupEvent
    data class ShutdownDisposeAcknowledged(val operation: ResourceShutdownDisposal) : ResourceCleanupEvent
    data class Lifetime(val event: ResourceLifetimeEvent) : ResourceCleanupEvent
    data class CancelPreparation(val closure: ResourceLifetimeClosure) : ResourceCleanupEvent
    /** Exact trusted terminal outcome: NEVER_CREATED guarantees no late allocation can be created. */
    data class DecodeResolved(val closure: ResourceLifetimeClosure, val operation: ResourceDecodeOperation,
        val outcome: ResourceDecodeOutcome) : ResourceCleanupEvent
    /** Trusted release of ALL upload/CPU scratch for this operation, independent of outcome arrival. */
    data class ScratchReleased(val closure: ResourceLifetimeClosure, val operation: ResourceDecodeOperation) : ResourceCleanupEvent
    data class DisposeAcknowledged(val operation: ResourceDisposalOperation) : ResourceCleanupEvent
}
sealed interface ResourceCleanupCommand {
    data class InvalidateRollbackClones(val closure: ResourceLifetimeClosure, val plan: ResourceLifetimePlan) : ResourceCleanupCommand
    data class InvalidateShutdownClones(val operation: ResourceShutdownOperation) : ResourceCleanupCommand
    data class DisposeShutdown(val operation: ResourceShutdownDisposal) : ResourceCleanupCommand
    data class Lifetime(val command: ResourceLifetimeCommand) : ResourceCleanupCommand
    data class RequestDecoderStop(val closure: ResourceLifetimeClosure, val operation: ResourceDecodeOperation) : ResourceCleanupCommand
    data class ReleaseUploadScratch(val closure: ResourceLifetimeClosure, val operation: ResourceDecodeOperation) : ResourceCleanupCommand
    data class Dispose(val operation: ResourceDisposalOperation) : ResourceCleanupCommand
}
class ResourceCleanupDecision(val state: ResourceCleanupState, val accepted: Boolean, val diagnosis: String,
    commands: Collection<ResourceCleanupCommand> = emptyList()) {
    val commands: List<ResourceCleanupCommand> = Collections.unmodifiableList(commands.toList())
}
