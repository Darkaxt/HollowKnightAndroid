package dev.silksong.launcher.skins.core

import java.util.Collections

/** Facts supplied by a trusted verifier; this value does not verify files or canonical paths. */
data class VerifiedResourceSource(
    val proofScope: String, val canonicalSource: String, val encodedLength: Long,
    val sha256: String, val width: Long, val height: Long,
    val format: String = "RGBA32", val usage: ResourceUsage = ResourceUsage.GAME,
)
enum class ResourceUsage { GAME, PREVIEW }
enum class ResourceOwnership { OWNED, BORROWED }
enum class ResourceReferenceKind { TARGET, MATERIAL, ROLLBACK }
enum class ResourceAllocationPhase { RESERVED, READY }
data class ResourceReference(val id: Long, val planId: String, val allocationId: Long, val kind: ResourceReferenceKind)
data class ResourceDecodeOperation(val operationId: Long, val allocationId: Long, val planId: String)
data class ResourceScratch(val operation: ResourceDecodeOperation, val bytes: Long, val decodeCompleted: Boolean = false)

class ResourceAllocation(
    val id: Long, val ownership: ResourceOwnership, val width: Long, val height: Long,
    sources: Collection<VerifiedResourceSource> = emptyList(), planIds: Collection<String> = emptyList(),
    val borrowedKey: String? = null, val phase: ResourceAllocationPhase = ResourceAllocationPhase.RESERVED,
) {
    val sources: List<VerifiedResourceSource> = Collections.unmodifiableList(sources.toList())
    val planIds: List<String> = Collections.unmodifiableList(planIds.toList())
}

/** Admission ledger only, NOT proof of preparation, commit, retirement or apply readiness.
 * Plan membership retains its budget charge even when its last reference is released.
 * Zero-reference allocations remain process-charged: lifetime/disposal is a separate slice.
 */
class ResourceState(
    allocations: Collection<ResourceAllocation> = emptyList(), references: Collection<ResourceReference> = emptyList(),
    val scratch: ResourceScratch? = null, val nextId: Long = 1,
) {
    val allocations: List<ResourceAllocation> = Collections.unmodifiableList(allocations.toList())
    val references: List<ResourceReference> = Collections.unmodifiableList(references.toList())
}
class ResourceTotals(
    val residentBytes: Long, val processBytes: Long, val ownedAllocations: Int,
    planBytes: Map<String, Long>, planAllocations: Map<String, Int>,
) {
    val planBytes: Map<String, Long> = Collections.unmodifiableMap(planBytes.toMap())
    val planAllocations: Map<String, Int> = Collections.unmodifiableMap(planAllocations.toMap())
}
sealed interface ResourceEvent {
    data class Acquire(val planId: String, val source: VerifiedResourceSource, val kind: ResourceReferenceKind) : ResourceEvent
    data class AcquireBorrowed(val planId: String, val borrowedKey: String, val width: Long, val height: Long, val kind: ResourceReferenceKind) : ResourceEvent
    data class ReleaseReference(val reference: ResourceReference) : ResourceEvent
    /** Trusted adapter acknowledgement of this exact allocation operation, not simulated proof. */
    data class DecodeCompleted(val operation: ResourceDecodeOperation) : ResourceEvent
    /** Acknowledges release of ALL readable CPU upload/scratch for this operation. */
    data class ScratchReleased(val operation: ResourceDecodeOperation) : ResourceEvent
}
sealed interface ResourceCommand {
    /** Fixed RGBA32, no mipmaps. Value command only; does not perform decoding. */
    data class Decode(val operation: ResourceDecodeOperation, val source: VerifiedResourceSource) : ResourceCommand
    data class ReferenceGranted(val reference: ResourceReference) : ResourceCommand
    data class ReleaseUploadScratch(val operation: ResourceDecodeOperation) : ResourceCommand
}
class ResourceDecision(val state: ResourceState, val accepted: Boolean, val diagnosis: String, commands: Collection<ResourceCommand> = emptyList()) {
    val commands: List<ResourceCommand> = Collections.unmodifiableList(commands.toList())
}
