package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*

/** Pure composition. TransactionCore alone reduces execution events.
 * Qualified bounded recovery validates value authority without rewriting execution history. */
class SkinRotationTransactionCore {
    private val rotation = SkinRotationCore()
    private val transaction = SkinTransactionCore()

    fun decide(s: RotationTransactionState, event: RotationTransactionEvent): RotationTransactionDecision {
        if (!isStateValid(s)) return d(s, "invalid-state")
        if (event is RotationTransactionEvent.Recover) return recover(s, event.evidence)
        val p = s.operation
        if (event is RotationTransactionEvent.Mode) {
            if (event.event is RotationModeEvent.Selector && event.event.event is RotationEvent.Rebind) {
                val rebound = rotation.decide(s.mode, event.event)
                if (rebound.state == s.mode) return d(s, rebound.diagnosis)
                return d(s.copy(mode = rebound.state, operation = p?.copy(reboundBlocked = true),
                    readinessReboundBlocked = s.readinessReboundBlocked || p == null && s.mode.rotation.pending?.phase == RotationPendingPhase.INTENT_ISSUED), rebound.diagnosis)
            }
            if (p != null) {
                if (event.event is RotationModeEvent.AdvanceMode && s.mode.rotation.activation.mode == SkinMode.ROTATE)
                    return d(s.copy(mode = s.mode.copy(offRequested = true)), "transaction-disarmed-restoration-needed")
                return d(s, "transaction-busy")
            }
            val result = rotation.decide(s.mode, event.event)
            val next = s.copy(mode = result.state)
            if (event.event is RotationModeEvent.AdvanceMode && result.diagnosis == "transaction-required") return begin(next, null)
            return RotationTransactionDecision(next, result.diagnosis, result.commands.map { RotationTransactionCommand.Mode(it) })
        }
        if (event is RotationTransactionEvent.ConsumeReady) {
            if (s.readinessReboundBlocked) return d(s, "task67-readiness-rebound-resolution-required")
            if (p != null || s.mode.operation != null) return d(s, "operation-busy")
            if (!ready(s.mode, event.intent)) return d(s, "stale-readiness")
            return begin(s, event.intent)
        }
        if (event !is RotationTransactionEvent.Transaction) return d(s, "invalid-event")
        if (p == null) return d(s, "no-transaction")
        if (p.reboundBlocked) return d(s, "task67-rebound-resolution-required")
        if (event.hero != p.origin.rotation.currentHero || event.hero != s.mode.rotation.currentHero) return d(s, "stale-hero")
        if (event.event is TransactionEvent.Begin) return d(s, "transaction-busy")
        val result = transaction.decide(p.transaction, event.event)
        if (result.state == p.transaction) return d(s, result.diagnosis)
        if (result.state.phase == TransactionPhase.COMMITTED) {
            // Only accepted exact verified closure events publish, never supplied terminal states.
            val complete = event.event as TransactionEvent.CompletionCommitted
            val activation = requireNotNull(result.state.activation)
            val m = s.mode.copy(head = complete.verifiedHead, liveProof = null,
                rotation = s.mode.rotation.copy(activation = activation, pending = null),
                offRequested = activation.mode == SkinMode.ROTATE && s.mode.offRequested, canceledPending = null)
            return d(RotationTransactionState(m, lastRecovery = s.lastRecovery), "transaction-committed")
        }
        return RotationTransactionDecision(s.copy(operation = p.copy(transaction = result.state)), result.diagnosis,
            result.commands.map { RotationTransactionCommand.Transaction(event.hero, it) })
    }

    /** Recovery retires wrapper ownership only; no retokening, execution, historical phase
     * rewrite or resource release. Rejected evidence leaves a later exact Recover reachable. */
    private fun recover(s: RotationTransactionState, evidence: RotationRecoveryEvidence): RotationTransactionDecision {
        val authority = evidence.authority
        if (authority.hero != s.mode.rotation.currentHero || authority.skin != s.mode.rotation.currentSkin)
            return d(s, "recovery-current-authority-required")
        val p = s.operation
        val head: VerifiedRegistryHead
        when (evidence) {
            is RotationRecoveryEvidence.IssuedClosureOutcome -> {
                if (p == null || evidence.operation != p) return d(s, "recovery-original-operation-required")
                val t = p.transaction
                if (t.phase != TransactionPhase.BLOCKED || t.pendingClosure == null)
                    return d(s, "recovery-issued-outcome-required")
                if (!outcomeChild(p, evidence.fence, evidence.receipt, evidence.head, t.pendingClosure))
                    return d(s, "recovery-exact-serialized-outcome-required")
                head = evidence.head
            }
            is RotationRecoveryEvidence.PriorRestoredAndFenced -> {
                if (p == null || evidence.operation != p) return d(s, "recovery-original-operation-required")
                val t = p.transaction; val e = requireNotNull(t.envelope)
                if (t.phase != TransactionPhase.ROLLBACK_PENDING && t.phase != TransactionPhase.BLOCKED)
                    return d(s, "recovery-prior-outcome-required")
                // Exact TransactionCore prior-closure semantics, without driving its phase.
                val prior = if (!e.priorEstablishedOnBinding && e.prior.active is ActiveVisual.Pack) {
                    if (e.prior.skinStamp == Long.MAX_VALUE) return d(s, "skin-stamp-exhausted")
                    e.prior.copy(skinStamp = e.prior.skinStamp + 1)
                } else e.prior
                if (!proofMatches(evidence.restoration, p.origin.rotation.currentHero!!, e.binding, prior.active) ||
                    !outcomeChild(p, evidence.fence, evidence.receipt, evidence.head, prior))
                    return d(s, "recovery-exact-serialized-prior-required")
                head = evidence.head
            }
            is RotationRecoveryEvidence.VisualClosure -> {
                if (p == null || !p.reboundBlocked || evidence.operation != p) return d(s, "recovery-original-operation-required")
                if (p.transaction.phase != TransactionPhase.APPLIED && p.transaction.phase != TransactionPhase.ROLLED_BACK)
                    return d(s, "task68-qualified-prior-or-terminal-resolution-required")
                // Validate the already-issued closure privately. Keep the historical transaction
                // unchanged in the audit; do not install the returned terminal state.
                val checked = transaction.decide(p.transaction, evidence.completion)
                if (checked.state.phase != TransactionPhase.COMMITTED) return d(s, "recovery-exact-closure-required")
                head = evidence.completion.verifiedHead
            }
            is RotationRecoveryEvidence.VisualFencedNotExecuted -> {
                if (p == null || !p.reboundBlocked || evidence.operation != p) return d(s, "recovery-original-operation-required")
                val t = p.transaction; val e = requireNotNull(t.envelope)
                when (t.phase) {
                    TransactionPhase.PREPARING, TransactionPhase.PREPARED ->
                        if (evidence.receipt != null || evidence.head != p.origin.head) return d(s, "recovery-unchanged-base-required")
                    TransactionPhase.ARMED -> {
                        val arm = requireNotNull(t.armCommitReceipt)
                        val prior = if (!e.priorEstablishedOnBinding && e.prior.active is ActiveVisual.Pack)
                            e.prior.copy(skinStamp = e.prior.skinStamp + 1) else e.prior
                        if (!exactChild(evidence.receipt, evidence.head, arm.newGenerationId, arm.newGenerationSha256, prior) ||
                            evidence.head.generationId == e.baseGenerationId || evidence.head.generationSha256 == e.baseGenerationSha256)
                            return d(s, "recovery-exact-armed-prior-closure-required")
                    }
                    else -> return d(s, "task68-qualified-prior-or-terminal-resolution-required")
                }
                head = evidence.head
            }
            is RotationRecoveryEvidence.ModeClosure -> {
                val operation = s.mode.operation
                if (p != null || operation == null || !operation.reboundBlocked || evidence.operation != operation ||
                    evidence.completion.correlation != operation.correlation) return d(s, "recovery-original-operation-required")
                if (!exactChild(evidence.completion.receipt, evidence.completion.head, operation.baseHead.generationId,
                        operation.baseHead.generationSha256, operation.target)) return d(s, "recovery-exact-closure-required")
                // Original-binding Vanilla is not current-binding OFF publication authority.
                if (operation.target.mode == SkinMode.OFF && authority.liveProof?.proof !is VerifiedLiveVisualProof.Vanilla)
                    return d(s, "recovery-current-vanilla-required")
                if (operation.selectedProof != null && !proofMatches(authority.liveProof, authority.hero, authority.skin, operation.target.active))
                    return d(s, "recovery-current-selected-proof-required")
                head = evidence.completion.head
            }
            is RotationRecoveryEvidence.ReadinessFencedNotExecuted -> {
                if (p != null || s.mode.operation != null || !(s.readinessReboundBlocked || s.mode.offRequested) ||
                    evidence.intent != s.mode.rotation.pending?.issuedIntent) return d(s, "recovery-original-readiness-required")
                if (evidence.head != s.mode.head) return d(s, "recovery-unchanged-base-required")
                head = evidence.head
            }
        }
        val m = s.mode.copy(head = head, operation = null, liveProof = authority.liveProof,
            rotation = s.mode.rotation.copy(activation = head.activation, pending = null),
            offRequested = head.activation.mode == SkinMode.ROTATE && s.mode.offRequested, canceledPending = null)
        if (!rotation.isModeStateValid(m)) return d(s, "recovery-current-proof-required")
        return d(RotationTransactionState(m, lastRecovery = RotationRecoveryRecord(s.copy(lastRecovery = null), evidence)), "recovery-resolved")
    }
    private fun outcomeChild(p: RotationTransactionOperation, fence: RotationOutcomeFence, receipt: RegistryCommitReceipt,
        head: VerifiedRegistryHead, target: ActivationSnapshot): Boolean {
        val t = p.transaction; val e = requireNotNull(t.envelope); val arm = requireNotNull(t.armCommitReceipt)
        val parentReceipt = t.failureReceipt ?: arm
        var parent = VerifiedRegistryHead(parentReceipt.newGenerationId, parentReceipt.newGenerationSha256, e.prior, t.interlock)
        val late = fence.lateFailureReceipt
        if (late != null) {
            // One bounded learned failure hop, never a replacement of stored authority.
            if (t.phase != TransactionPhase.BLOCKED || t.failureReceipt != null || t.pendingClosure != null ||
                t.originalFailure == null || t.rollbackFailure == null ||
                !exactReceipt(late, arm.newGenerationId, arm.newGenerationSha256) ||
                late.newGenerationId == e.baseGenerationId || late.newGenerationSha256 == e.baseGenerationSha256) return false
            parent = VerifiedRegistryHead(late.newGenerationId, late.newGenerationSha256, e.prior,
                t.interlock.copy(state = InterlockState.ROLLBACK_FAILED, originalFailure = t.originalFailure, rollbackFailure = t.rollbackFailure))
        }
        return fence.correlation == TransactionCorrelation(e.transactionId, e.binding) && fence.originalHero == p.origin.rotation.currentHero &&
            fence.authoritativeParent == parent && exactChild(receipt, head, parent.generationId, parent.generationSha256, target) &&
            head.generationId != e.baseGenerationId && head.generationSha256 != e.baseGenerationSha256 &&
            head.generationId != arm.newGenerationId && head.generationSha256 != arm.newGenerationSha256
    }
    private fun proofMatches(p: HeroVerifiedVisual?, hero: HeroBindingToken, skin: SkinBindingToken, visual: ActiveVisual): Boolean {
        if (p == null || p.hero != hero || p.proof.binding != skin || !RotationValues.text(p.hero.value, 256) ||
            !RotationValues.text(p.proof.binding.value, 256)) return false
        return when (val v = p.proof) {
            is VerifiedLiveVisualProof.Vanilla -> visual is ActiveVisual.Vanilla
            is VerifiedLiveVisualProof.Pack -> visual is ActiveVisual.Pack && RotationValues.pack(v.visual) &&
                v.visual.id == visual.id && v.visual.treeSha256 == visual.treeSha256 && v.visual.contentSha256 == visual.contentSha256
        }
    }
    private fun exactReceipt(r: RegistryCommitReceipt?, parentId: String, parentHash: String) = r != null &&
        Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").matches(r.newGenerationId) &&
        Regex("[0-9a-f]{64}").matches(r.newGenerationSha256) && r.expectedGenerationId == parentId &&
        r.expectedGenerationSha256 == parentHash && r.newGenerationId != parentId && r.newGenerationSha256 != parentHash
    private fun exactChild(r: RegistryCommitReceipt?, h: VerifiedRegistryHead, parentId: String,
        parentHash: String, target: ActivationSnapshot) = r != null && exactReceipt(r, parentId, parentHash) &&
        h.generationId == r.newGenerationId && h.generationSha256 == r.newGenerationSha256 &&
        h.activation == target && h.interlock == RotationInterlock.clear()

    private fun begin(s: RotationTransactionState, readiness: RotationReadyIntent?): RotationTransactionDecision {
        if (s.mode.operationHighWater == Long.MAX_VALUE) return d(s, "operation-id-exhausted")
        if (s.mode.rotation.activation.skinStamp == Long.MAX_VALUE) return d(s, "skin-stamp-exhausted")
        val e = envelope(s.mode, readiness) ?: return d(s, "transaction-target-unavailable")
        val result = transaction.decide(TransactionState(binding = e.binding, activation = e.prior), TransactionEvent.Begin(e))
        if (result.state.phase != TransactionPhase.PREPARING) return d(s, result.diagnosis)
        val next = RotationTransactionState(s.mode.copy(operationHighWater = s.mode.operationHighWater + 1, liveProof = null),
            RotationTransactionOperation(s.mode, readiness, result.state), lastRecovery = s.lastRecovery)
        return RotationTransactionDecision(next, result.diagnosis,
            result.commands.map { RotationTransactionCommand.Transaction(requireNotNull(s.mode.rotation.currentHero), it) })
    }

    fun isStateValid(s: RotationTransactionState): Boolean {
        if (!rotation.isModeStateValid(s.mode)) return false
        val audit = s.lastRecovery
        if (audit != null) {
            // Reject nesting before recursion: at most one prior, audit-free state is checked.
            if (audit.before.lastRecovery != null || !isStateValid(audit.before) ||
                recover(audit.before, audit.evidence).diagnosis != "recovery-resolved" ||
                s.mode.operationHighWater < audit.before.mode.operationHighWater ||
                s.mode.rotation.epochHighWater.value < audit.before.mode.rotation.epochHighWater.value) return false
        }
        if (s.readinessReboundBlocked && (s.operation != null || s.mode.operation != null ||
                s.mode.rotation.pending?.phase != RotationPendingPhase.INTENT_ISSUED)) return false
        val issued = s.mode.rotation.pending?.issuedIntent
        if (s.operation == null && issued != null && !s.readinessReboundBlocked &&
            (issued.hero != s.mode.rotation.currentHero || issued.skin != s.mode.rotation.currentSkin)) return false
        val p = s.operation ?: return true
        if (s.mode.rotation.currentHero == null || !rotation.isModeStateValid(p.origin) || p.origin.operation != null || s.mode.operation != null ||
            p.origin.operationHighWater == Long.MAX_VALUE || s.mode.operationHighWater != p.origin.operationHighWater + 1 ||
            !transaction.isStateValid(p.transaction) || p.transaction.phase == TransactionPhase.IDLE || p.transaction.phase == TransactionPhase.COMMITTED) return false
        val e = envelope(p.origin, p.readiness) ?: return false
        if (e != p.transaction.envelope) return false
        val current = s.mode.rotation
        if (!p.reboundBlocked && (current.currentHero != p.origin.rotation.currentHero || current.currentSkin != p.origin.rotation.currentSkin)) return false
        // During execution only bindings, proof invalidation and one-way disarm can change.
        val expected = p.origin.copy(operationHighWater = s.mode.operationHighWater, liveProof = null,
            offRequested = p.origin.offRequested || s.mode.offRequested,
            rotation = p.origin.rotation.copy(currentHero = current.currentHero, currentSkin = current.currentSkin,
                pending = p.origin.rotation.pending?.copy(hero = requireNotNull(current.currentHero), skin = requireNotNull(current.currentSkin))))
        return expected == s.mode
    }

    private fun envelope(m: RotationModeState, readiness: RotationReadyIntent?): TransactionEnvelope? {
        if (m.rotation.currentHero == null || m.operation != null || m.operationHighWater == Long.MAX_VALUE || m.rotation.activation.skinStamp == Long.MAX_VALUE) return null
        val a = m.rotation.activation
        val target: ActivationSnapshot
        val kind: SkinOperationKind
        if (readiness != null) {
            if (!ready(m, readiness)) return null
            kind = SkinOperationKind.DEATH_ROTATION
            target = a.copy(selectedPackId = readiness.candidate.currentObject.id, active = readiness.candidate.currentObject, skinStamp = a.skinStamp + 1)
        } else if (a.mode == SkinMode.OFF) {
            val selected = m.rotation.ring.entries.singleOrNull { it.currentObject.id == a.selectedPackId } ?: return null
            kind = SkinOperationKind.MODE_ON
            target = a.copy(mode = SkinMode.ON, active = selected.currentObject, skinStamp = a.skinStamp + 1)
        } else if (a.mode == SkinMode.ROTATE && m.offRequested && m.rotation.pending == null && m.liveProof?.proof !is VerifiedLiveVisualProof.Vanilla) {
            kind = SkinOperationKind.MODE_OFF
            target = a.copy(mode = SkinMode.OFF, active = ActiveVisual.Vanilla, skinStamp = a.skinStamp + 1)
        } else return null
        return TransactionEnvelope(operationId(m.operationHighWater + 1), kind, m.head.generationId, m.head.generationSha256,
            a, target, requireNotNull(m.rotation.currentSkin), rotation.isPriorEstablished(m))
    }
    private fun ready(m: RotationModeState, r: RotationReadyIntent) = !m.offRequested && m.operation == null &&
        m.rotation.pending?.phase == RotationPendingPhase.INTENT_ISSUED && m.rotation.pending.issuedIntent == r &&
        r.hero == m.rotation.currentHero && r.skin == m.rotation.currentSkin && r.prior == m.rotation.activation
    // Shares H1's monotonic counter, with a distinct visual operation prefix.
    private fun operationId(n: Long): String {
        val hex = n.toString(16).padStart(16, '0')
        return "10000000-0000-${hex.substring(0, 4)}-${hex.substring(4, 8)}-0000${hex.substring(8)}"
    }
    private fun d(s: RotationTransactionState, diagnosis: String) = RotationTransactionDecision(s, diagnosis)
}
