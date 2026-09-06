package dev.silksong.launcher.skins.core

/** Pure, bounded admission/reference accounting. No lifetime or apply authority is granted here. */
class SkinResourceCore {
    companion object {
        const val TEXTURE_BYTES = 32L * 1024 * 1024
        const val PLAN_BYTES = 96L * 1024 * 1024
        const val PROCESS_BYTES = 224L * 1024 * 1024
        const val PLAN_ALLOCATIONS = 205
        const val PROCESS_ALLOCATIONS = 410
        // Bounds on bookkeeping, not texture budgets. Lifetime scheduling belongs to the next slice.
        const val MAX_PLANS = 3
        const val MAX_REFERENCES = 16384
        const val MAX_SOURCE_FACTS = 16384
    }
    private data class Content(val length: Long, val hash: String, val width: Long, val height: Long, val format: String)
    private fun content(v: VerifiedResourceSource) = Content(v.encodedLength, v.sha256, v.width, v.height, v.format)
    private fun text(v: String, max: Int) = v.isNotBlank() && v.length <= max
    private fun bytes(width: Long, height: Long): Long {
        require(width > 0 && height > 0)
        return Math.multiplyExact(Math.multiplyExact(width, height), 4L)
    }
    private fun valid(v: VerifiedResourceSource): Boolean =
        text(v.proofScope, 128) && text(v.canonicalSource, 4096) && v.encodedLength > 0 &&
            v.sha256.length == 64 && v.sha256.all { it in '0'..'9' || it in 'a'..'f' } &&
            v.format == "RGBA32" && v.usage == ResourceUsage.GAME && bytes(v.width, v.height) <= TEXTURE_BYTES

    /** Null means inconsistent, malformed, overflowing, or over-budget input. Never trusts cached totals. */
    fun totals(state: ResourceState): ResourceTotals? = try {
        require(state.nextId > 0 && state.allocations.size <= PROCESS_ALLOCATIONS * 2 && state.references.size <= MAX_REFERENCES)
        val ids = HashSet<Long>()
        fun id(v: Long) { require(v > 0 && v < state.nextId && ids.add(v)) }
        val byId = HashMap<Long, ResourceAllocation>()
        val plans = HashSet<String>()
        val canonical = HashSet<Pair<String, String>>()
        val contents = HashSet<Content>()
        val borrowed = HashSet<String>()
        val planBytes = HashMap<String, Long>()
        val planCounts = HashMap<String, Int>()
        var resident = 0L
        var owned = 0
        var borrowedCount = 0
        var facts = 0
        for (a in state.allocations) {
            id(a.id); byId[a.id] = a
            require(a.planIds.isNotEmpty() && a.planIds.size <= MAX_PLANS && a.planIds.toSet().size == a.planIds.size)
            for (p in a.planIds) { require(text(p, 128)); plans.add(p) }
            val size = bytes(a.width, a.height)
            when (a.ownership) {
                ResourceOwnership.OWNED -> {
                    require(a.borrowedKey == null && a.sources.isNotEmpty() && size <= TEXTURE_BYTES)
                    facts = Math.addExact(facts, a.sources.size); require(facts <= MAX_SOURCE_FACTS)
                    val c = content(a.sources.first()); require(contents.add(c))
                    for (v in a.sources) {
                        require(valid(v) && content(v) == c && v.width == a.width && v.height == a.height)
                        require(canonical.add(v.proofScope to v.canonicalSource))
                    }
                    resident = Math.addExact(resident, size); owned = Math.addExact(owned, 1)
                    for (p in a.planIds) {
                        planBytes[p] = Math.addExact(planBytes[p] ?: 0, size)
                        planCounts[p] = Math.addExact(planCounts[p] ?: 0, 1)
                    }
                }
                ResourceOwnership.BORROWED -> {
                    require(a.sources.isEmpty() && a.borrowedKey != null && text(a.borrowedKey, 4096) && borrowed.add(a.borrowedKey))
                    require(a.phase == ResourceAllocationPhase.READY)
                    borrowedCount = Math.addExact(borrowedCount, 1)
                }
            }
        }
        require(plans.size <= MAX_PLANS && owned <= PROCESS_ALLOCATIONS && borrowedCount <= PROCESS_ALLOCATIONS)
        require(planBytes.values.all { it <= PLAN_BYTES } && planCounts.values.all { it <= PLAN_ALLOCATIONS })
        for (r in state.references) {
            id(r.id)
            val a = byId[r.allocationId]
            require(a != null && r.planId in a.planIds)
        }
        val scratch = state.scratch
        if (scratch != null) {
            id(scratch.operation.operationId)
            val a = byId[scratch.operation.allocationId]
            require(a != null && a.ownership == ResourceOwnership.OWNED && scratch.operation.planId in a.planIds)
            require(scratch.bytes == bytes(a.width, a.height) && scratch.bytes <= TEXTURE_BYTES)
            require(a.phase == if (scratch.decodeCompleted) ResourceAllocationPhase.READY else ResourceAllocationPhase.RESERVED)
        }
        require(state.allocations.filter { it.phase == ResourceAllocationPhase.RESERVED }.all {
            scratch != null && !scratch.decodeCompleted && scratch.operation.allocationId == it.id
        })
        val peak = Math.addExact(resident, scratch?.bytes ?: 0)
        require(peak <= PROCESS_BYTES)
        ResourceTotals(resident, peak, owned, planBytes, planCounts)
    } catch (_: IllegalArgumentException) { null } catch (_: ArithmeticException) { null }

    fun decide(state: ResourceState, event: ResourceEvent): ResourceDecision {
        fun reject(code: String) = ResourceDecision(state, false, code)
        if (totals(state) == null) return reject("INVALID_STATE")
        return try {
            when (event) {
                is ResourceEvent.Acquire -> {
                    if (!text(event.planId, 128) || !valid(event.source)) return reject("INVALID_SOURCE")
                    val c = content(event.source)
                    val owned = state.allocations.filter { it.ownership == ResourceOwnership.OWNED }
                    if (owned.any { a -> a.sources.any { it.proofScope == event.source.proofScope &&
                            it.canonicalSource == event.source.canonicalSource && content(it) != c } }) return reject("CONTRADICTORY_SOURCE")
                    val existing = owned.firstOrNull { content(it.sources.first()) == c }
                    if (existing == null && state.scratch != null) return reject("SCRATCH_BUSY")
                    val allocationId = existing?.id ?: state.nextId
                    val referenceId = if (existing == null) Math.addExact(state.nextId, 1) else state.nextId
                    val reference = ResourceReference(referenceId, event.planId, allocationId, event.kind)
                    val commands = mutableListOf<ResourceCommand>(ResourceCommand.ReferenceGranted(reference))
                    val updated = ResourceAllocation(allocationId, ResourceOwnership.OWNED, event.source.width, event.source.height,
                        ((existing?.sources ?: emptyList()) + event.source).distinct(),
                        ((existing?.planIds ?: emptyList()) + event.planId).distinct(), phase = existing?.phase ?: ResourceAllocationPhase.RESERVED)
                    var scratch = state.scratch
                    var next = Math.addExact(referenceId, 1)
                    if (existing == null) {
                        val op = ResourceDecodeOperation(next, allocationId, event.planId)
                        next = Math.addExact(next, 1)
                        scratch = ResourceScratch(op, bytes(updated.width, updated.height))
                        commands.add(ResourceCommand.Decode(op, event.source))
                    }
                    admit(state, ResourceState(state.allocations.filterNot { it.id == allocationId } + updated,
                        state.references + reference, scratch, next), commands)
                }
                is ResourceEvent.AcquireBorrowed -> {
                    if (!text(event.planId, 128) || !text(event.borrowedKey, 4096)) return reject("INVALID_BORROWED")
                    bytes(event.width, event.height)
                    val existing = state.allocations.firstOrNull { it.ownership == ResourceOwnership.BORROWED && it.borrowedKey == event.borrowedKey }
                    if (existing != null && (existing.width != event.width || existing.height != event.height)) return reject("CONTRADICTORY_BORROWED")
                    val aid = existing?.id ?: state.nextId
                    val rid = if (existing == null) Math.addExact(state.nextId, 1) else state.nextId
                    val reference = ResourceReference(rid, event.planId, aid, event.kind)
                    val allocation = ResourceAllocation(aid, ResourceOwnership.BORROWED, event.width, event.height,
                        planIds = ((existing?.planIds ?: emptyList()) + event.planId).distinct(), borrowedKey = event.borrowedKey, phase = ResourceAllocationPhase.READY)
                    admit(state, ResourceState(state.allocations.filterNot { it.id == aid } + allocation, state.references + reference,
                        state.scratch, Math.addExact(rid, 1)), listOf(ResourceCommand.ReferenceGranted(reference)))
                }
                is ResourceEvent.ReleaseReference -> {
                    if (event.reference !in state.references) return reject("STALE_REFERENCE")
                    admit(state, ResourceState(state.allocations, state.references.filterNot { it == event.reference }, state.scratch, state.nextId))
                }
                is ResourceEvent.DecodeCompleted -> {
                    val scratch = state.scratch
                    if (scratch == null || scratch.operation != event.operation || scratch.decodeCompleted) return reject("STALE_DECODE")
                    val allocations = state.allocations.map { a -> if (a.id != event.operation.allocationId) a else
                        ResourceAllocation(a.id, a.ownership, a.width, a.height, a.sources, a.planIds, a.borrowedKey, ResourceAllocationPhase.READY) }
                    admit(state, ResourceState(allocations, state.references, scratch.copy(decodeCompleted = true), state.nextId),
                        listOf(ResourceCommand.ReleaseUploadScratch(event.operation)))
                }
                is ResourceEvent.ScratchReleased -> {
                    val scratch = state.scratch
                    if (scratch == null || scratch.operation != event.operation || !scratch.decodeCompleted) return reject("STALE_SCRATCH")
                    admit(state, ResourceState(state.allocations, state.references, null, state.nextId))
                }
            }
        } catch (_: IllegalArgumentException) { reject("INVALID_EVENT") } catch (_: ArithmeticException) { reject("OVERFLOW") }
    }
    private fun admit(prior: ResourceState, next: ResourceState, commands: List<ResourceCommand> = emptyList()): ResourceDecision =
        if (totals(next) == null) ResourceDecision(prior, false, "LIMIT_OR_INCONSISTENT")
        else ResourceDecision(next, true, "ACCEPTED", commands)
}
