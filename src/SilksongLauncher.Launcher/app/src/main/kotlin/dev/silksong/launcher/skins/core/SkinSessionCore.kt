package dev.silksong.launcher.skins.core

/** Pure structural reducer. Trusted serialized evidence is not verified runtime truth. No I/O or credentials. */
class SkinSessionCore {
    private fun match(s: String?, pattern: String, max: Int) = s != null && s.length <= max && Regex(pattern).matches(s)
    private fun id(s: String?) = match(s, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", 36)
    private fun hash(s: String?) = match(s, "[0-9a-f]{64}", 64)
    private fun reason(s: String?) = match(s, "[A-Z][A-Z0-9_]{0,127}", 128)
    private fun owner(p: SessionProcessIdentity?) = p != null && p.uid >= 0 && p.pid > 0 && match(p.processStartToken, "0|[1-9][0-9]*", 20)
    private fun binding(b: SessionBinding?) = b != null && b.profileId == "hollow-knight" && id(b.descriptorId) && id(b.leaseId) &&
        b.descriptorId != "00000000-0000-0000-0000-000000000000" && b.leaseId != "00000000-0000-0000-0000-000000000000" && b.descriptorId != b.leaseId &&
        hash(b.descriptorSha256) && b.descriptorPath == "sessions/${b.descriptorId}/descriptor.json" && hash(b.tokenSha256) && b.sessionSequence >= 0 &&
        id(b.registryGenerationId) && hash(b.registrySha256) && owner(b.launcherOwner)
    private fun correlation(c: SessionCorrelation?) = c != null && c.ordinal > 0 && (c.binding == null || binding(c.binding))
    private fun lease(v: VerifiedSessionLease?): Boolean {
        if (v == null) return false
        val d = v.document; val h = v.head
        if (d.schemaVersion != 1 || !binding(d.binding) || !id(d.transitionId) || !hash(v.canonicalDocumentSha256) || h.sha256 != v.canonicalDocumentSha256 ||
            h.descriptorId != d.binding.descriptorId || h.leaseId != d.binding.leaseId || h.transitionSequence != d.transitionSequence || h.state != d.state) return false
        if (d.parentTransitionId != null && (!id(d.parentTransitionId) || d.parentTransitionId == d.transitionId)) return false
        if (d.gameOwner != null && !owner(d.gameOwner)) return false
        return when (d.state) {
            SessionLeasePhase.LAUNCH_PENDING -> d.transitionSequence == 0L && d.parentTransitionId == null && d.gameOwner == null && d.closeReason == null
            SessionLeasePhase.GAME_OWNED -> d.transitionSequence == 1L && d.parentTransitionId != null && d.gameOwner != null && d.closeReason == null
            SessionLeasePhase.CLOSED -> d.parentTransitionId != null && reason(d.closeReason) && (d.transitionSequence == 1L && d.gameOwner == null || d.transitionSequence == 2L && d.gameOwner != null)
        }
    }
    private fun acquisition(a: SessionAcquisitionEvidence?) = a != null && lease(a.pending) && a.pending.document.state == SessionLeasePhase.LAUNCH_PENDING && a.barrier?.head == a.pending.head
    private fun receipt(r: SessionTransitionReceipt?): Boolean {
        if (r == null || !correlation(r.correlation) || !lease(r.parent) || !lease(r.child)) return false
        val p = r.parent.document; val d = r.child.document
        if (p.binding != d.binding || r.correlation.binding != null && r.correlation.binding != p.binding || p.state == SessionLeasePhase.CLOSED ||
            d.transitionSequence != p.transitionSequence + 1 || d.parentTransitionId != p.transitionId || r.child.head.sha256 == r.parent.head.sha256) return false
        return when (r.operation) {
            SessionOperation.CLAIM -> p.state == SessionLeasePhase.LAUNCH_PENDING && d.state == SessionLeasePhase.GAME_OWNED && (r.barrier as? SessionBarrier.ActiveInstalled)?.head == r.child.head
            SessionOperation.CLOSE -> d.state == SessionLeasePhase.CLOSED && d.gameOwner == p.gameOwner && (r.barrier as? SessionBarrier.ActiveRemoved)?.expectedParent == r.parent.head
        }
    }
    private fun profile(p: SessionProfileRecoveryEvidence?, c: SessionCorrelation) = p != null && p.profileId == "hollow-knight" && p.correlation == c
    private fun ownerEvidence(e: SessionOwnerEvidence?, c: SessionCorrelation, p: VerifiedSessionLease) = e != null && e.correlation == c && e.binding == p.document.binding &&
        e.expectedOwner == (p.document.gameOwner ?: p.document.binding.launcherOwner) && (if (e.liveness == SessionLiveness.ALIVE) e.aliveOwner == e.expectedOwner else e.aliveOwner == null)
    private fun target(t: SessionTargetProcess?): Boolean {
        if (t == null || !match(t.packageName, "[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z][a-zA-Z0-9_]*)+", 255) || t.packageName.length < 3 || t.processName.length > 255) return false
        return t.processName == t.packageName || t.processName.startsWith(t.packageName + ":") && match(t.processName.substring(t.packageName.length + 1), "[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z][a-zA-Z0-9_]*)*", 255)
    }
    private fun targetEvidence(e: SessionTargetEvidence?, c: SessionCorrelation, p: VerifiedSessionLease) = e != null && e.correlation == c && e.binding == p.document.binding && target(e.target) &&
        (if (e.presence == SessionPresence.PRESENT) owner(e.presentOwner) else e.presentOwner == null)
    private fun scoped(p: VerifiedSessionLease, c: SessionCorrelation) = lease(p) && p.document.state != SessionLeasePhase.CLOSED && (c.binding == null || p.document.binding == c.binding)
    private fun liveness(p: VerifiedSessionLease, o: SessionOwnerEvidence?, t: SessionTargetEvidence?, c: SessionCorrelation) = ownerEvidence(o, c, p) &&
        (if (p.document.state == SessionLeasePhase.LAUNCH_PENDING) targetEvidence(t, c, p) else t == null)
    private fun recoveryGate(result: SessionRecoveryResult?, c: SessionCorrelation): SessionMutationGate {
        if (!correlation(c)) return SessionMutationGate.UNKNOWN
        if (result is SessionRecoveryResult.NoOpenLease) return if (profile(result.profile, c)) SessionMutationGate.CLEAR else SessionMutationGate.UNKNOWN
        if (result is SessionRecoveryResult.RetainedOpenLease && scoped(result.lease, c) && result.barrier?.head == result.lease.head && liveness(result.lease, result.owner, result.target, c))
            return if (result.owner!!.liveness == SessionLiveness.ALIVE || result.target?.presence == SessionPresence.PRESENT) SessionMutationGate.ACTIVE else SessionMutationGate.UNKNOWN
        if (result is SessionRecoveryResult.RecoveredClosed && scoped(result.parent, c) && receipt(result.receipt) && result.receipt!!.operation == SessionOperation.CLOSE && result.receipt.correlation == c && result.receipt.parent == result.parent &&
            liveness(result.parent, result.owner, result.target, c) && result.owner!!.liveness == SessionLiveness.DEAD && (result.target == null || result.target.presence == SessionPresence.ABSENT) && profile(result.profile, c) &&
            result.receipt.child.document.closeReason == (if (result.parent.document.state == SessionLeasePhase.LAUNCH_PENDING) "RECOVERY_LAUNCHER_DEAD" else "RECOVERY_GAME_OWNER_DEAD")) return SessionMutationGate.CLEAR
        return SessionMutationGate.UNKNOWN
    }
    private fun recoveryShape(r: SessionRecoveryResult?, c: SessionCorrelation): Boolean = when (r) {
        is SessionRecoveryResult.Deferred -> true
        is SessionRecoveryResult.NoOpenLease -> profile(r.profile, c)
        is SessionRecoveryResult.RecoveredClosed -> recoveryGate(r, c) == SessionMutationGate.CLEAR
        is SessionRecoveryResult.RetainedOpenLease -> scoped(r.lease, c) && r.barrier?.head == r.lease.head && liveness(r.lease, r.owner, r.target, c) &&
            (r.observedClaim == null || receipt(r.observedClaim) && r.observedClaim.operation == SessionOperation.CLAIM && r.observedClaim.correlation == c && r.observedClaim.child == r.lease)
        null -> false
    }
    fun isStateValid(s: SessionState?): Boolean {
        if (s == null || s.ordinal < 0 || s.recoveryHighWater < 0 || s.recoveryHighWater > s.ordinal) return false
        val b = s.acquisition?.pending?.document?.binding
        if (s.pinnedOwner != null && !owner(s.pinnedOwner) || s.pinnedCloseReason != null && !reason(s.pinnedCloseReason)) return false
        if (s.acquisition == null) {
            if (s.lease != null || s.claimReceipt != null || s.closedReceipt != null || s.pinnedOwner != null || s.pinnedCloseReason != null || s.claimUse != SessionClaimUse.AVAILABLE) return false
        } else {
            if (!acquisition(s.acquisition) || !lease(s.lease) || s.lease!!.document.binding != b) return false
            if (s.claimReceipt != null && (!receipt(s.claimReceipt) || s.claimReceipt.operation != SessionOperation.CLAIM || s.claimReceipt.parent != s.acquisition.pending ||
                    s.claimReceipt.correlation.binding != b || s.claimReceipt.correlation.ordinal > s.ordinal || s.claimReceipt.child.document.gameOwner != s.pinnedOwner)) return false
            if (s.closedReceipt != null && (!receipt(s.closedReceipt) || s.closedReceipt.operation != SessionOperation.CLOSE || s.closedReceipt.parent != (s.claimReceipt?.child ?: s.acquisition.pending) ||
                    s.closedReceipt.correlation.binding != b || s.closedReceipt.correlation.ordinal > s.ordinal ||
                    s.recoveryClosure == null && s.closedReceipt.child.document.closeReason != s.pinnedCloseReason)) return false
            if (s.lease != (s.closedReceipt?.child ?: s.claimReceipt?.child ?: s.acquisition.pending)) return false
            if (s.claimUse == SessionClaimUse.CONSUMED && s.claimReceipt == null || s.claimReceipt != null && s.claimUse != SessionClaimUse.CONSUMED && s.claimUse != SessionClaimUse.REJECTED) return false
        }
        if (s.recovery != null && (!correlation(s.recovery.correlation) || s.recovery.correlation.binding != b || s.recovery.correlation.ordinal > s.recoveryHighWater || !recoveryShape(s.recovery.result, s.recovery.correlation))) return false
        if (s.recoveryHighWater > 0 && s.recovery == null && s.pending !is SessionCommand.RecoverExistingSessionStore) return false
        if (s.pending != null) {
            if (!correlation(s.pending.correlation) || s.pending.correlation.binding != b || s.pending.correlation.ordinal != s.ordinal || s.gate != SessionMutationGate.UNKNOWN) return false
            when (val c = s.pending) {
                is SessionCommand.ClaimExistingBinding -> if (b == null || c.binding != b || c.owner != s.pinnedOwner || !owner(c.owner) || s.claimUse != SessionClaimUse.IN_FLIGHT || s.pinnedCloseReason != null || s.lease!!.document.state != SessionLeasePhase.LAUNCH_PENDING) return false
                is SessionCommand.CloseExistingBinding -> if (b == null || c.binding != b || c.reason != s.pinnedCloseReason || !reason(c.reason) || s.lease!!.document.state == SessionLeasePhase.CLOSED) return false
                is SessionCommand.RecoverExistingSessionStore -> if (c.expectedBinding != b || s.recoveryHighWater != s.ordinal) return false
                null -> return false
            }
        }
        if (s.claimAttempt != null && (b == null || !correlation(s.claimAttempt.correlation) || s.claimAttempt.correlation.binding != b || s.claimAttempt.binding != b ||
            s.claimAttempt.owner != s.pinnedOwner || s.claimAttempt.correlation.ordinal > s.ordinal)) return false
        if ((s.pinnedOwner != null) != (s.claimAttempt != null)) return false
        if (s.pending is SessionCommand.ClaimExistingBinding && s.pending != s.claimAttempt) return false
        if (s.claimUse == SessionClaimUse.CONSUMED && s.claimAttempt?.correlation != s.claimReceipt?.correlation) return false
        if (s.claimReceipt != null && s.recoveryHighWater >= s.claimReceipt.correlation.ordinal && s.claimRecoveryFence == null) return false
        if (s.claimRecoveryFence != null) {
            val fence = s.claimRecoveryFence
            if (!correlation(fence.recoveryCorrelation) || fence.recoveryCorrelation.binding != b || fence.claimReceipt != s.claimReceipt ||
                fence.recoveryCorrelation.ordinal > s.recoveryHighWater || fence.recoveryCorrelation.ordinal < fence.claimReceipt.correlation.ordinal) return false
            if (if (fence.recoveryCorrelation.ordinal == fence.claimReceipt.correlation.ordinal) s.claimUse != SessionClaimUse.REJECTED else s.claimUse != SessionClaimUse.CONSUMED) return false
        }
        if (s.claimReceipt != null && s.claimUse == SessionClaimUse.REJECTED && (s.claimRecoveryFence == null ||
            s.claimRecoveryFence.recoveryCorrelation != s.claimReceipt.correlation || s.claimAttempt == null || s.claimAttempt.correlation.ordinal >= s.claimReceipt.correlation.ordinal)) return false
        if (s.closeAttempt != null && (b == null || !correlation(s.closeAttempt.correlation) || s.closeAttempt.correlation.binding != b || s.closeAttempt.binding != b ||
            s.closeAttempt.reason != s.pinnedCloseReason || s.closeAttempt.correlation.ordinal > s.ordinal || s.closeAttempt.correlation.ordinal <= (s.claimAttempt?.correlation?.ordinal ?: 0))) return false
        if ((s.pinnedCloseReason != null) != (s.closeAttempt != null)) return false
        if (s.pending is SessionCommand.CloseExistingBinding && s.pending != s.closeAttempt) return false
        if (s.closedReceipt != null && s.recoveryClosure == null && s.closeAttempt?.correlation != s.closedReceipt.correlation) return false
        if (s.recoveryClosure != null) {
            val closure = s.recoveryClosure; val recovered = closure.result
            if (b == null || s.closedReceipt == null || !correlation(closure.correlation) || closure.correlation.binding != b ||
                recovered !is SessionRecoveryResult.RecoveredClosed || !recoveryShape(recovered, closure.correlation) ||
                recovered.receipt != s.closedReceipt || closure.correlation.ordinal > s.recoveryHighWater || s.recovery == null || s.recovery.correlation.ordinal < closure.correlation.ordinal ||
                s.closeAttempt != null && s.closeAttempt.correlation.ordinal >= closure.correlation.ordinal) return false
        }
        if (s.closedReceipt != null && s.closedReceipt.correlation.ordinal <= (s.claimReceipt?.correlation?.ordinal ?: 0)) return false
        if (s.recovery != null && s.pending !is SessionCommand.RecoverExistingSessionStore && s.recovery.correlation.ordinal != s.recoveryHighWater) return false
        if (s.recovery != null && b != null) {
            when (val r = s.recovery.result) {
                is SessionRecoveryResult.NoOpenLease -> if (s.lease!!.document.state != SessionLeasePhase.CLOSED || s.closedReceipt!!.correlation.ordinal >= s.recovery.correlation.ordinal) return false
                is SessionRecoveryResult.RecoveredClosed -> if (r.receipt != s.closedReceipt || r.receipt!!.child != s.lease || s.recoveryClosure != s.recovery) return false
                is SessionRecoveryResult.RetainedOpenLease -> {
                    if (r.lease != s.acquisition.pending && r.lease != s.claimReceipt?.child) return false
                    if (r.observedClaim != null && (r.observedClaim != s.claimReceipt || s.claimUse != SessionClaimUse.REJECTED)) return false
                    if (s.claimReceipt != null && (if (r.lease.document.state == SessionLeasePhase.LAUNCH_PENDING)
                        s.claimReceipt.correlation.ordinal <= s.recovery.correlation.ordinal
                        else r.observedClaim == null && s.claimReceipt.correlation.ordinal >= s.recovery.correlation.ordinal)) return false
                    if (s.closedReceipt != null && s.closedReceipt.correlation.ordinal <= s.recovery.correlation.ordinal) return false
                }
                is SessionRecoveryResult.Deferred -> Unit
            }
        }
        if (s.pending != null && s.pending.correlation.ordinal <= (s.closedReceipt?.correlation?.ordinal ?: s.claimReceipt?.correlation?.ordinal ?: 0)) return false
        if ((s.claimUse == SessionClaimUse.IN_FLIGHT) != (s.pending is SessionCommand.ClaimExistingBinding)) return false
        if (s.gate == SessionMutationGate.CLEAR && (s.pending != null || s.recovery == null || s.recovery.correlation.ordinal != s.recoveryHighWater || recoveryGate(s.recovery.result, s.recovery.correlation) != SessionMutationGate.CLEAR || b != null && s.lease!!.document.state != SessionLeasePhase.CLOSED)) return false
        if (s.gate == SessionMutationGate.ACTIVE && (s.pending != null || s.lease?.document?.state == SessionLeasePhase.CLOSED || b == null && (s.recovery == null || recoveryGate(s.recovery.result, s.recovery.correlation) != SessionMutationGate.ACTIVE))) return false
        return true
    }
    /** Only session eligibility; catalog/transaction/interlock/resources and per-commit store validation still apply. */
    fun sessionWritesEligible(s: SessionState?) = isStateValid(s) && s!!.claimUse == SessionClaimUse.CONSUMED && s.claimReceipt != null &&
        s.claimRecoveryFence == null && s.claimReceipt.child == s.lease && s.claimReceipt.correlation.ordinal > s.recoveryHighWater && s.pending == null && s.pinnedCloseReason == null && s.gate == SessionMutationGate.ACTIVE
    fun decide(s: SessionState?, input: SessionEvent?): SessionDecision {
        if (!isStateValid(s)) return SessionDecision(s, "INVALID_STATE")
        s!!; val b = s.acquisition?.pending?.document?.binding
        if (input is SessionEvent.ClaimCompleted) return complete(s, input.correlation, input.result, true)
        if (input is SessionEvent.CloseCompleted) return complete(s, input.correlation, input.result, false)
        if (input is SessionEvent.RecoveryCompleted) return recover(s, input)
        if (s.pending != null) return SessionDecision(s, "IN_FLIGHT")
        if (input is SessionEvent.BindIssuedPending) {
            if (s.acquisition != null || s.ordinal != 0L || s.recovery != null || !acquisition(input.evidence)) return SessionDecision(s, "BINDING_REJECTED")
            return SessionDecision(s.copy(acquisition = input.evidence, lease = input.evidence!!.pending, gate = SessionMutationGate.ACTIVE), "BOUND")
        }
        if (input is SessionEvent.CloseRequested && s.recoveryClosure != null) return SessionDecision(s, "REQUEST_REJECTED")
        if (input is SessionEvent.CloseRequested && b != null && input.binding == b && input.reason == s.pinnedCloseReason && s.closedReceipt != null)
            return SessionDecision(s, "CLOSED_REPLAY", completedClose = s.closedReceipt)
        if (s.ordinal == Long.MAX_VALUE) return SessionDecision(s, "ORDINAL_EXHAUSTED")
        val c = SessionCorrelation(b, s.ordinal + 1)
        if (input is SessionEvent.ClaimRequested && b != null && input.binding == b && owner(input.owner) && s.claimUse == SessionClaimUse.AVAILABLE && s.pinnedCloseReason == null && s.lease!!.document.state == SessionLeasePhase.LAUNCH_PENDING && (s.pinnedOwner == null || s.pinnedOwner == input.owner)) {
            val command = SessionCommand.ClaimExistingBinding(c, b, input.owner!!)
            return SessionDecision(s.copy(ordinal = c.ordinal, pending = command, claimAttempt = command, pinnedOwner = input.owner, claimUse = SessionClaimUse.IN_FLIGHT, gate = SessionMutationGate.UNKNOWN), "CLAIM_REQUESTED", command)
        }
        if (input is SessionEvent.CloseRequested && b != null && input.binding == b && reason(input.reason) && s.lease!!.document.state != SessionLeasePhase.CLOSED && (s.pinnedCloseReason == null || s.pinnedCloseReason == input.reason)) {
            val command = SessionCommand.CloseExistingBinding(c, b, input.reason!!)
            return SessionDecision(s.copy(ordinal = c.ordinal, pending = command, closeAttempt = command, pinnedCloseReason = input.reason, gate = SessionMutationGate.UNKNOWN), "CLOSE_REQUESTED", command)
        }
        if (input is SessionEvent.RecoveryRequested) {
            val command = SessionCommand.RecoverExistingSessionStore(c, b)
            val fence = s.claimRecoveryFence ?: s.claimReceipt?.let { SessionClaimRecoveryFence(c, it) }
            return SessionDecision(s.copy(ordinal = c.ordinal, recoveryHighWater = c.ordinal, claimRecoveryFence = fence, pending = command, gate = SessionMutationGate.UNKNOWN), "RECOVERY_REQUESTED", command)
        }
        return SessionDecision(s, "REQUEST_REJECTED")
    }
    private fun complete(s: SessionState, c: SessionCorrelation?, result: SessionTransitionResult?, claim: Boolean): SessionDecision {
        if (c == null || s.pending?.correlation != c || (if (claim) s.pending !is SessionCommand.ClaimExistingBinding else s.pending !is SessionCommand.CloseExistingBinding)) return SessionDecision(s, "STALE_COMPLETION")
        if (result is SessionTransitionResult.Durable && receipt(result.receipt) && result.receipt!!.correlation == c && result.receipt.parent == s.lease &&
            result.receipt.operation == (if (claim) SessionOperation.CLAIM else SessionOperation.CLOSE) && (if (claim) result.receipt.child.document.gameOwner == s.pinnedOwner else result.receipt.child.document.closeReason == s.pinnedCloseReason)) {
            val next = if (claim) s.copy(pending = null, lease = result.receipt.child, claimReceipt = result.receipt, claimUse = SessionClaimUse.CONSUMED, gate = SessionMutationGate.ACTIVE)
                else s.copy(pending = null, lease = result.receipt.child, closedReceipt = result.receipt, gate = SessionMutationGate.UNKNOWN)
            return SessionDecision(next, "DURABLE", completedClose = if (claim) null else result.receipt)
        }
        // Invalid evidence is not an operational failure. Preserve serialization and the
        // exact correlation for a later trustworthy completion.
        if (result !is SessionTransitionResult.Failed) return SessionDecision(s, "INVALID_COMPLETION")
        val retry = result.code in listOf(SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE)
        return SessionDecision(s.copy(pending = null, gate = SessionMutationGate.UNKNOWN, claimUse = if (claim) (if (retry) SessionClaimUse.AVAILABLE else SessionClaimUse.REJECTED) else s.claimUse), if (retry) "RETRYABLE" else "REJECTED")
    }
    private fun recover(s: SessionState, e: SessionEvent.RecoveryCompleted): SessionDecision {
        if (e.correlation == null || s.pending !is SessionCommand.RecoverExistingSessionStore || s.pending.correlation != e.correlation) return SessionDecision(s, "STALE_COMPLETION")
        val c = e.correlation; var result = e.result; var valid = recoveryShape(result, c)
        if (valid && s.acquisition != null) {
            when (val r = result) {
                is SessionRecoveryResult.NoOpenLease -> valid = s.lease!!.document.state == SessionLeasePhase.CLOSED
                is SessionRecoveryResult.RecoveredClosed -> valid = r.parent == s.lease
                is SessionRecoveryResult.RetainedOpenLease -> valid = r.lease == s.lease || r.observedClaim != null && r.observedClaim.parent == s.lease && r.observedClaim.child.document.gameOwner == s.pinnedOwner
                else -> Unit
            }
        }
        if (!valid) return SessionDecision(s, "INVALID_COMPLETION")
        var next = s.copy(pending = null, recovery = SessionRecoveryRecord(c, result!!), gate = recoveryGate(result, c))
        if (s.acquisition != null && result is SessionRecoveryResult.RecoveredClosed)
            next = next.copy(lease = result.receipt!!.child, closedReceipt = result.receipt, recoveryClosure = next.recovery)
        if (s.acquisition != null && result is SessionRecoveryResult.RetainedOpenLease && result.lease != s.lease)
            next = next.copy(lease = result.lease, claimReceipt = result.observedClaim, claimUse = SessionClaimUse.REJECTED, claimRecoveryFence = SessionClaimRecoveryFence(c, result.observedClaim!!))
        return if (isStateValid(next)) SessionDecision(next, "RECOVERED") else SessionDecision(s, "INVALID_COMPLETION")
    }
}
