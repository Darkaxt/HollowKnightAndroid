package dev.silksong.launcher.skins.core

/** Pure resource ownership closure. Verification events are values; no executor or transaction writes. */
class SkinResourceCleanupCore {
    private val lifetime = SkinResourceLifetimeCore()
    private val admission = SkinResourceCore()
    private fun restored(c: ResourceLifetimeClosure, p: ResourceLifetimePlan?): Boolean {
        val prior=c.prior ?: return p==null
        if(p==null || p.planId!=prior.planId) return false
        if(listOf(p.planId,p.binding,p.generationId,p.stamp).any { it.isBlank() || it.length>128 }) return false
        return p.binding==c.target.binding
    }
    private fun valid(s: ResourceCleanupState): Boolean {
        val l=s.lifetime
        if(!lifetime.isValidState(l)) return false
        val r=s.rollback; val h=s.shutdown
        // Bind public cleanup evidence to the validated closure before inspecting nested identities.
        if(s.cancellation!=null && s.cancellation!=l.candidate) return false
        if(r!=null && (!l.candidateSealed || r.closure!=s.cancellation || !restored(r.closure,r.restoredPrior) ||
            (r.clonePhase==ResourceRollbackClonePhase.OLD_PRIOR && (r.closure.prior==null || r.closure.prior==r.restoredPrior)) ||
            (r.clonePhase!=ResourceRollbackClonePhase.QUALIFIED && s.pendingDisposals.isNotEmpty()))) return false
        if(h==null) { if(s.shutdownClonesQualified || s.shutdownDisposals.isNotEmpty()) return false }
        else {
            if(!s.teardownRequested || h.plan!=l.active || h.operationId<=0 || h.operationId>=l.nextOperationId ||
                l.candidate!=null || l.awaitingClones!=null || l.retirement!=null || s.cancellation!=null || r!=null ||
                s.shutdownDisposals.size>SkinResourceCore.PROCESS_ALLOCATIONS || (!s.shutdownClonesQualified && s.shutdownDisposals.isNotEmpty())) return false
            val ids=HashSet<Long>(); val allocations=HashSet<Long>()
            for(op in s.shutdownDisposals) {
                if(op.shutdown!=h || op.operationId<=h.operationId || op.operationId>=l.nextOperationId || !ids.add(op.operationId) || !allocations.add(op.allocationId)) return false
                val a=l.ledger.allocations.firstOrNull { it.id==op.allocationId } ?: return false
                if(a.ownership!=ResourceOwnership.OWNED || a.phase!=ResourceAllocationPhase.READY || a.planIds!=listOf(h.plan.planId) || l.ledger.references.any { it.allocationId==a.id }) return false
            }
        }
        val c=s.cancellation
        if(c==null) return s.decoder==null && s.pendingDisposals.isEmpty() && r==null
        if(c!=l.candidate || l.awaitingDurableCompletion || s.pendingDisposals.size>SkinResourceCore.PROCESS_ALLOCATIONS) return false
        val scratch=l.ledger.scratch; val d=s.decoder
        if((scratch==null)!=(d==null)) return false
        if(d!=null && (d.operation!=scratch!!.operation || (d.outcome==ResourceDecodeOutcome.CREATED)!=scratch.decodeCompleted)) return false
        val ids=HashSet<Long>(); val allocations=HashSet<Long>()
        for(op in s.pendingDisposals) {
            if(op.closure!=c || op.operationId<=c.operationId || op.operationId>=l.nextOperationId || !ids.add(op.operationId) || !allocations.add(op.allocationId)) return false
            val a=l.ledger.allocations.firstOrNull { it.id==op.allocationId } ?: return false
            if(a.ownership!=ResourceOwnership.OWNED || a.phase!=ResourceAllocationPhase.READY || a.planIds!=listOf(c.target.planId) || scratch?.operation?.allocationId==a.id || l.ledger.references.any { it.allocationId==a.id }) return false
        }
        return true
    }
    fun canBeginCandidate(s: ResourceCleanupState)=valid(s) && !s.teardownRequested && s.cancellation==null && lifetime.canBeginCandidate(s.lifetime)
    fun canApply(s: ResourceCleanupState)=valid(s) && !s.teardownRequested && s.cancellation==null && lifetime.canApply(s.lifetime)

    fun decide(s: ResourceCleanupState,e: ResourceCleanupEvent): ResourceCleanupDecision {
        fun reject(code: String)=ResourceCleanupDecision(s,false,code)
        if(!valid(s)) return reject("INVALID_STATE")
        return try {
            val commands=mutableListOf<ResourceCleanupCommand>()
            val next=when(e) {
                ResourceCleanupEvent.Teardown -> copy(s,teardownRequested=true)
                is ResourceCleanupEvent.PostTransactionOutcome -> {
                    val c=s.lifetime.candidate
                    if(c==null || c!=e.closure || !s.lifetime.awaitingDurableCompletion || s.rollback!=null) return reject("STALE_OUTCOME")
                    if(e.outcome!=ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK) return ResourceCleanupDecision(s,true,"OWNERSHIP_UNRESOLVED")
                    if(!restored(c,e.restoredPrior)) return reject("INVALID_RESTORED_PRIOR")
                    commands.add(ResourceCleanupCommand.InvalidateRollbackClones(c,c.target))
                    val l=s.lifetime
                    copy(s,lifetime=ResourceLifetimeState(l.ledger,l.active,l.candidate,l.candidateSealed,nextOperationId=l.nextOperationId),cancellation=c,rollback=ResourceRollbackResolution(c,e.restoredPrior))
                }
                is ResourceCleanupEvent.RollbackClonesInvalidated -> qualifyRollback(s,e.closure,e.plan,commands) ?: return reject("STALE_ROLLBACK_CLONES")
                is ResourceCleanupEvent.RollbackCloneCountVerified -> {
                    if(e.count!=0L) return reject("CLONES_NOT_CLOSED")
                    qualifyRollback(s,e.closure,e.plan,commands) ?: return reject("STALE_ROLLBACK_CLONES")
                }
                is ResourceCleanupEvent.ShutdownClonesInvalidated -> {
                    if(s.shutdown==null || s.shutdown!=e.operation || s.shutdownClonesQualified) return reject("STALE_SHUTDOWN_CLONES")
                    copy(s,shutdownClonesQualified=true)
                }
                is ResourceCleanupEvent.ShutdownCloneCountVerified -> {
                    if(e.count!=0L || s.shutdown==null || s.shutdown!=e.operation || s.shutdownClonesQualified) return reject("STALE_SHUTDOWN_CLONES")
                    copy(s,shutdownClonesQualified=true)
                }
                is ResourceCleanupEvent.ShutdownDisposeAcknowledged -> {
                    if(e.operation !in s.shutdownDisposals) return reject("STALE_SHUTDOWN_DISPOSE")
                    val l=s.lifetime; val ledger=l.ledger
                    copy(s,lifetime=withLedger(l,ResourceState(ledger.allocations.filterNot { it.id==e.operation.allocationId },ledger.references,ledger.scratch,ledger.nextId)),shutdownDisposals=s.shutdownDisposals.filterNot { it==e.operation })
                }
                is ResourceCleanupEvent.Lifetime -> {
                    val release=e.event is ResourceLifetimeEvent.Admission && e.event.event is ResourceEvent.ReleaseReference
                    if(s.cancellation!=null && !release) return reject("CANCELLATION_PENDING")
                    if(s.teardownRequested && (e.event is ResourceLifetimeEvent.BeginCandidate || e.event is ResourceLifetimeEvent.SealCandidate || e.event is ResourceLifetimeEvent.AwaitDurableCompletion || (e.event is ResourceLifetimeEvent.Admission && !release))) return reject("TEARDOWN_PENDING")
                    val d=lifetime.decide(s.lifetime,e.event)
                    if(!d.accepted) return reject(d.diagnosis)
                    commands.addAll(d.commands.map { ResourceCleanupCommand.Lifetime(it) })
                    copy(s,lifetime=d.state)
                }
                is ResourceCleanupEvent.CancelPreparation -> {
                    if(s.cancellation!=null || s.lifetime.candidate!=e.closure || s.lifetime.awaitingDurableCompletion) return reject("CANCEL_NOT_QUALIFIED")
                    cancel(s,e.closure,commands)
                }
                is ResourceCleanupEvent.DecodeResolved -> {
                    val d=s.decoder
                    if(s.cancellation!=e.closure || d==null || d.operation!=e.operation || d.outcome!=ResourceDecodeOutcome.PENDING || e.outcome==ResourceDecodeOutcome.PENDING) return reject("STALE_DECODE_OUTCOME")
                    var l=s.lifetime
                    if(e.outcome==ResourceDecodeOutcome.CREATED) {
                        val result=admission.decide(l.ledger,ResourceEvent.DecodeCompleted(e.operation))
                        if(!result.accepted) return reject(result.diagnosis)
                        l=withLedger(l,result.state)
                    }
                    if(!d.scratchReleased) commands.add(ResourceCleanupCommand.ReleaseUploadScratch(e.closure,e.operation))
                    copy(s,lifetime=l,decoder=d.copy(outcome=e.outcome))
                }
                is ResourceCleanupEvent.ScratchReleased -> {
                    val d=s.decoder
                    if(s.cancellation!=e.closure || d==null || d.operation!=e.operation || d.scratchReleased) return reject("STALE_SCRATCH_ACK")
                    copy(s,decoder=d.copy(scratchReleased=true))
                }
                is ResourceCleanupEvent.DisposeAcknowledged -> {
                    if(e.operation !in s.pendingDisposals) return reject("STALE_DISPOSE_ACK")
                    val ledger=s.lifetime.ledger
                    copy(s,lifetime=withLedger(s.lifetime,ResourceState(ledger.allocations.filterNot { it.id==e.operation.allocationId },ledger.references,ledger.scratch,ledger.nextId)),pendingDisposals=s.pendingDisposals.filterNot { it==e.operation })
                }
            }
            val swept=advance(next,commands)
            if(!valid(swept)) reject("INCONSISTENT_TRANSITION") else ResourceCleanupDecision(swept,true,"ACCEPTED",commands)
        } catch(_: ArithmeticException) { reject("OVERFLOW") }
    }
    private fun qualifyRollback(s: ResourceCleanupState,c: ResourceLifetimeClosure,p: ResourceLifetimePlan,commands: MutableList<ResourceCleanupCommand>): ResourceCleanupState? {
        val r=s.rollback ?: return null
        val expected=when(r.clonePhase) { ResourceRollbackClonePhase.CANDIDATE -> r.closure.target; ResourceRollbackClonePhase.OLD_PRIOR -> r.closure.prior; ResourceRollbackClonePhase.QUALIFIED -> null }
        if(r.closure!=c || expected!=p) return null
        val phase=if(r.clonePhase==ResourceRollbackClonePhase.CANDIDATE && c.prior!=null && c.prior!=r.restoredPrior) ResourceRollbackClonePhase.OLD_PRIOR else ResourceRollbackClonePhase.QUALIFIED
        if(phase==ResourceRollbackClonePhase.OLD_PRIOR) commands.add(ResourceCleanupCommand.InvalidateRollbackClones(c,c.prior!!))
        return copy(s,rollback=r.copy(clonePhase=phase))
    }
    private fun copy(s: ResourceCleanupState,lifetime: ResourceLifetimeState=s.lifetime,cancellation: ResourceLifetimeClosure?=s.cancellation,
        decoder: ResourceCleanupDecoder?=s.decoder,pendingDisposals: Collection<ResourceDisposalOperation> = s.pendingDisposals,
        teardownRequested: Boolean=s.teardownRequested,rollback: ResourceRollbackResolution?=s.rollback,shutdown: ResourceShutdownOperation?=s.shutdown,
        shutdownClonesQualified: Boolean=s.shutdownClonesQualified,shutdownDisposals: Collection<ResourceShutdownDisposal> = s.shutdownDisposals)=
        ResourceCleanupState(lifetime,cancellation,decoder,pendingDisposals,teardownRequested,rollback,shutdown,shutdownClonesQualified,shutdownDisposals)
    private fun withLedger(l: ResourceLifetimeState,ledger: ResourceState,next: Long=l.nextOperationId)=
        ResourceLifetimeState(ledger,l.active,l.candidate,l.candidateSealed,l.awaitingClones,l.retirement,l.pendingDisposals,next,l.awaitingDurableCompletion)
    private fun cancel(s: ResourceCleanupState,c: ResourceLifetimeClosure,commands: MutableList<ResourceCleanupCommand>): ResourceCleanupState {
        val decoder=s.lifetime.ledger.scratch?.let { ResourceCleanupDecoder(it.operation,if(it.decodeCompleted)ResourceDecodeOutcome.CREATED else ResourceDecodeOutcome.PENDING) }
        if(decoder?.outcome==ResourceDecodeOutcome.PENDING) commands.add(ResourceCleanupCommand.RequestDecoderStop(c,decoder.operation))
        return copy(s,cancellation=c,decoder=decoder)
    }
    private fun advance(input: ResourceCleanupState,commands: MutableList<ResourceCleanupCommand>): ResourceCleanupState {
        var s=input
        if(s.teardownRequested && s.cancellation==null && s.lifetime.candidate!=null && !s.lifetime.awaitingDurableCompletion) s=cancel(s,s.lifetime.candidate!!,commands)
        s=sweep(s,commands)
        if(!s.teardownRequested || s.cancellation!=null || s.lifetime.candidate!=null || s.lifetime.awaitingClones!=null || s.lifetime.retirement!=null) return s
        val l=s.lifetime
        if(l.active==null) return s // Validity ensures no unknown allocations can be wiped here.
        if(s.shutdown==null) {
            val op=ResourceShutdownOperation(l.nextOperationId,l.active)
            commands.add(ResourceCleanupCommand.InvalidateShutdownClones(op))
            return copy(s,lifetime=withLedger(l,l.ledger,Math.addExact(l.nextOperationId,1)),shutdown=op)
        }
        if(!s.shutdownClonesQualified) return s
        val h=s.shutdown!!; var next=l.nextOperationId; val pending=s.shutdownDisposals.toMutableList(); val allocations=mutableListOf<ResourceAllocation>()
        for(a in l.ledger.allocations) {
            if(l.ledger.references.any { it.allocationId==a.id }) { allocations.add(a); continue }
            if(a.ownership==ResourceOwnership.OWNED) {
                allocations.add(a)
                if(pending.none { it.allocationId==a.id }) { val op=ResourceShutdownDisposal(next,h,a.id); next=Math.addExact(next,1); pending.add(op); commands.add(ResourceCleanupCommand.DisposeShutdown(op)) }
            }
            // Borrowed defaults untrack only after exact clones and all references close; never Dispose.
        }
        val ledger=ResourceState(allocations,l.ledger.references,l.ledger.scratch,l.ledger.nextId)
        if(allocations.isEmpty()) return ResourceCleanupState(ResourceLifetimeState(ledger,nextOperationId=next),teardownRequested=true)
        return copy(s,lifetime=withLedger(l,ledger,next),shutdownDisposals=pending)
    }
    private fun sweep(s: ResourceCleanupState,commands: MutableList<ResourceCleanupCommand>): ResourceCleanupState {
        val c=s.cancellation ?: return s
        if(s.rollback!=null && s.rollback.clonePhase!=ResourceRollbackClonePhase.QUALIFIED) return s
        var ledger=s.lifetime.ledger; var decoder=s.decoder
        // An early scratch ACK never resolves a decoder that can still create.
        if(decoder!=null && decoder.scratchReleased) {
            if(decoder.outcome==ResourceDecodeOutcome.CREATED) { ledger=ResourceState(ledger.allocations,ledger.references,null,ledger.nextId); decoder=null }
            else if(decoder.outcome==ResourceDecodeOutcome.NEVER_CREATED && ledger.references.none { it.allocationId==decoder!!.operation.allocationId }) { ledger=ResourceState(ledger.allocations.filterNot { it.id==decoder!!.operation.allocationId },ledger.references,null,ledger.nextId); decoder=null }
        }
        val pending=s.pendingDisposals.toMutableList(); var next=s.lifetime.nextOperationId; val allocations=mutableListOf<ResourceAllocation>()
        for(a in ledger.allocations) {
            if(c.target.planId !in a.planIds || ledger.scratch?.operation?.allocationId==a.id || ledger.references.any { it.allocationId==a.id && it.planId==c.target.planId }) { allocations.add(a); continue }
            val retained=a.planIds.filterNot { it==c.target.planId }
            if(retained.isNotEmpty()) allocations.add(ResourceAllocation(a.id,a.ownership,a.width,a.height,a.sources,retained,a.borrowedKey,a.phase))
            else if(a.ownership==ResourceOwnership.OWNED) {
                allocations.add(a)
                if(pending.none { it.allocationId==a.id }) { val op=ResourceDisposalOperation(next,c,a.id); next=Math.addExact(next,1); pending.add(op); commands.add(ResourceCleanupCommand.Dispose(op)) }
            }
        }
        ledger=ResourceState(allocations,ledger.references,ledger.scratch,ledger.nextId)
        if(allocations.none { c.target.planId in it.planIds }) {
            val active=if(s.rollback!=null)s.rollback.restoredPrior else s.lifetime.active
            return ResourceCleanupState(ResourceLifetimeState(ledger,active,nextOperationId=next),teardownRequested=s.teardownRequested)
        }
        return copy(s,lifetime=withLedger(s.lifetime,ledger,next),decoder=decoder,pendingDisposals=pending)
    }
}
