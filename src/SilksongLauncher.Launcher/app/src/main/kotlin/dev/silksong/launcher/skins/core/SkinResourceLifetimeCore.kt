package dev.silksong.launcher.skins.core

/** Pure resource ownership wrapper. Gates are values, never live apply/transaction authority. */
class SkinResourceLifetimeCore {
    private val admission = SkinResourceCore()
    private fun text(v: String) = v.isNotBlank() && v.length <= 128
    private fun valid(p: ResourceLifetimePlan) = text(p.planId) && text(p.binding) && text(p.generationId) && text(p.stamp)
    private fun distinct(prior: ResourceLifetimePlan?, target: ResourceLifetimePlan) = prior == null ||
        (prior.planId != target.planId && (prior.binding != target.binding ||
            (prior.generationId != target.generationId && prior.stamp != target.stamp)))
    private fun valid(c: ResourceLifetimeClosure, next: Long) = c.operationId > 0 && c.operationId < next && text(c.transactionId) &&
        valid(c.target) && (c.prior == null || valid(c.prior)) && distinct(c.prior, c.target)

    private fun valid(s: ResourceLifetimeState): Boolean {
        if (admission.totals(s.ledger) == null || s.nextOperationId <= 0 || (s.active != null && (!valid(s.active) || s.nextOperationId == 1L))) return false
        val closures = listOfNotNull(s.candidate, s.awaitingClones, s.retirement)
        if (closures.size > 1 || closures.any { !valid(it, s.nextOperationId) }) return false
        if (s.candidateSealed && (s.candidate == null || s.ledger.scratch != null)) return false
        if (s.awaitingDurableCompletion && !s.candidateSealed) return false
        if (s.candidate != null && s.candidate.prior != s.active) return false
        val displaced = s.awaitingClones ?: s.retirement
        if (displaced != null && (displaced.prior == null || displaced.target != s.active)) return false
        val owners = listOfNotNull(s.active?.planId, s.candidate?.target?.planId, displaced?.prior?.planId).toSet()
        if (s.ledger.allocations.any { a -> a.planIds.any { it !in owners } }) return false
        val scratch = s.ledger.scratch
        if (scratch != null && (s.candidate == null || scratch.operation.planId != s.candidate.target.planId ||
                s.ledger.allocations.first { it.id == scratch.operation.allocationId }.planIds != listOf(s.candidate.target.planId))) return false
        if (s.retirement == null && s.pendingDisposals.isNotEmpty()) return false
        if (s.retirement != null && s.ledger.allocations.none { s.retirement.prior!!.planId in it.planIds }) return false
        val ids = HashSet<Long>(); val allocations = HashSet<Long>()
        for (op in s.pendingDisposals) {
            if (op.closure != s.retirement || op.operationId <= op.closure.operationId || op.operationId >= s.nextOperationId ||
                !ids.add(op.operationId) || !allocations.add(op.allocationId)) return false
            val a = s.ledger.allocations.firstOrNull { it.id == op.allocationId } ?: return false
            if (a.ownership != ResourceOwnership.OWNED || a.phase != ResourceAllocationPhase.READY ||
                a.planIds != listOf(s.retirement!!.prior!!.planId) || s.ledger.references.any { it.allocationId == a.id }) return false
        }
        return true
    }
    /** Read-only composition seam; grants no gate or transition authority. */
    fun isValidState(state: ResourceLifetimeState) = valid(state)
    fun canBeginCandidate(state: ResourceLifetimeState) = valid(state) && state.candidate == null && state.awaitingClones == null && state.retirement == null
    fun canApply(state: ResourceLifetimeState) = valid(state) && state.candidate != null && state.candidateSealed && !state.awaitingDurableCompletion

    fun decide(state: ResourceLifetimeState, event: ResourceLifetimeEvent): ResourceLifetimeDecision {
        fun reject(code: String) = ResourceLifetimeDecision(state, false, code)
        if (!valid(state)) return reject("INVALID_STATE")
        return try {
            val commands = mutableListOf<ResourceLifetimeCommand>()
            val next = when (event) {
                is ResourceLifetimeEvent.AwaitDurableCompletion -> {
                    if (!canApply(state) || state.candidate != event.closure) return reject("STALE_APPLY_GATE")
                    ResourceLifetimeState(state.ledger, state.active, state.candidate, true,
                        nextOperationId = state.nextOperationId, awaitingDurableCompletion = true)
                }
                is ResourceLifetimeEvent.BeginCandidate -> {
                    if (!canBeginCandidate(state)) return reject("LIFETIME_BUSY")
                    if (!text(event.transactionId) || !valid(event.target) || !distinct(state.active, event.target)) return reject("INVALID_CANDIDATE")
                    val c = ResourceLifetimeClosure(state.nextOperationId, event.transactionId, state.active, event.target)
                    ResourceLifetimeState(state.ledger, state.active, c, nextOperationId = Math.addExact(state.nextOperationId, 1))
                }
                is ResourceLifetimeEvent.Admission -> {
                    val p = when (val e = event.event) {
                        is ResourceEvent.Acquire -> e.planId
                        is ResourceEvent.AcquireBorrowed -> e.planId
                        else -> null
                    }
                    // Freeze acquisitions at seal and throughout durable/clone/disposal closure.
                    // This also prevents dedup from reacquiring an allocation already commanded for disposal.
                    if (p != null && (state.candidate == null || state.candidateSealed || p != state.candidate.target.planId)) return reject("ACQUISITION_CLOSED")
                    val d = admission.decide(state.ledger, event.event)
                    if (!d.accepted) return reject(d.diagnosis)
                    commands.addAll(d.commands.map { ResourceLifetimeCommand.Admission(it) })
                    ResourceLifetimeState(d.state, state.active, state.candidate, state.candidateSealed,
                        state.awaitingClones, state.retirement, state.pendingDisposals, state.nextOperationId, state.awaitingDurableCompletion)
                }
                is ResourceLifetimeEvent.SealCandidate -> {
                    if (state.candidate != event.closure || state.candidateSealed || state.ledger.scratch != null) return reject("NOT_PREPARED")
                    ResourceLifetimeState(state.ledger, state.active, state.candidate, true, nextOperationId = state.nextOperationId)
                }
                is ResourceLifetimeEvent.DurableCompletionVerified -> {
                    if (!state.awaitingDurableCompletion || state.candidate != event.closure) return reject("STALE_CLOSURE")
                    val c = event.closure
                    if (c.prior != null) commands.add(ResourceLifetimeCommand.InvalidateCompanionClones(c))
                    ResourceLifetimeState(state.ledger, c.target, awaitingClones = if (c.prior != null) c else null, nextOperationId = state.nextOperationId)
                }
                is ResourceLifetimeEvent.CompanionClonesInvalidated -> {
                    if (state.awaitingClones != event.closure) return reject("STALE_CLONE_ACK")
                    ResourceLifetimeState(state.ledger, state.active, retirement = event.closure, nextOperationId = state.nextOperationId)
                }
                is ResourceLifetimeEvent.RegisteredCloneCountVerified -> {
                    if (state.awaitingClones != event.closure || event.count != 0L) return reject("CLONES_NOT_CLOSED")
                    ResourceLifetimeState(state.ledger, state.active, retirement = event.closure, nextOperationId = state.nextOperationId)
                }
                is ResourceLifetimeEvent.DisposeAcknowledged -> {
                    if (event.operation !in state.pendingDisposals) return reject("STALE_DISPOSE_ACK")
                    val ledger = ResourceState(state.ledger.allocations.filterNot { it.id == event.operation.allocationId },
                        state.ledger.references, state.ledger.scratch, state.ledger.nextId)
                    ResourceLifetimeState(ledger, state.active, retirement = state.retirement,
                        pendingDisposals = state.pendingDisposals.filterNot { it == event.operation }, nextOperationId = state.nextOperationId)
                }
            }
            val swept = sweep(next, commands)
            if (!valid(swept)) reject("INCONSISTENT_TRANSITION") else ResourceLifetimeDecision(swept, true, "ACCEPTED", commands)
        } catch (_: ArithmeticException) { reject("OVERFLOW") }
    }

    /** Qualified membership closure only; command emission never removes an owned allocation. */
    private fun sweep(s: ResourceLifetimeState, commands: MutableList<ResourceLifetimeCommand>): ResourceLifetimeState {
        val closure = s.retirement ?: return s
        val old = closure.prior!!.planId
        val pending = s.pendingDisposals.toMutableList()
        var next = s.nextOperationId
        val allocations = mutableListOf<ResourceAllocation>()
        for (a in s.ledger.allocations) {
            if (old !in a.planIds || s.ledger.references.any { it.allocationId == a.id && it.planId == old }) {
                allocations.add(a); continue
            }
            val retained = a.planIds.filterNot { it == old }
            if (retained.isNotEmpty()) {
                allocations.add(ResourceAllocation(a.id, a.ownership, a.width, a.height, a.sources, retained, a.borrowedKey, a.phase))
            } else if (a.ownership == ResourceOwnership.OWNED) {
                allocations.add(a)
                if (pending.none { it.allocationId == a.id }) {
                    val op = ResourceDisposalOperation(next, closure, a.id)
                    next = Math.addExact(next, 1)
                    pending.add(op); commands.add(ResourceLifetimeCommand.Dispose(op))
                }
            }
            // Borrowed defaults are merely untracked after qualified closure, never disposed.
        }
        val ledger = ResourceState(allocations, s.ledger.references, s.ledger.scratch, s.ledger.nextId)
        val retirement = if (allocations.any { old in it.planIds }) closure else null
        return ResourceLifetimeState(ledger, s.active, retirement = retirement, pendingDisposals = pending, nextOperationId = next)
    }
}
