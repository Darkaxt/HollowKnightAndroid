package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import java.text.Normalizer

/** Pure value reducer. Caller owns authoritative binding and canonical verified-head events. */
class SkinTransactionCore {
    /** Read-only structural validation; grants no execution or recovery authority. */
    fun isStateValid(state: TransactionState): Boolean = valid(state)

    fun decide(state: TransactionState, event: TransactionEvent): TransactionDecision {
        val s = state
        if (!valid(s)) return d(s, "invalid-state")
        if (s.phase == TransactionPhase.BLOCKED || s.phase == TransactionPhase.COMMITTED) return d(s, "terminal")
        if (event is TransactionEvent.Begin) {
            if (s.phase != TransactionPhase.IDLE) return d(s, "transaction-in-progress")
            val e = event.envelope
            if (!envelopeValid(e) || e.binding != s.binding || e.prior != s.activation) return d(s, "invalid-envelope")
            return d(s.copy(phase = TransactionPhase.PREPARING, envelope = e), "prepare", SkinCommand.Prepare(e, e.target.active))
        }
        val c = correlation(event)
        val e = s.envelope
        if (e == null || c == null || !uuid(c.transactionId) || !token(c.binding) ||
            c.transactionId != e.transactionId || c.binding != s.binding) return d(s, "stale-correlation")
        return when {
            event is TransactionEvent.Prepared && s.phase == TransactionPhase.PREPARING ->
                d(s.copy(phase = TransactionPhase.PREPARED), "arm", SkinCommand.Arm(e))
            event is TransactionEvent.ArmCommitted && s.phase == TransactionPhase.PREPARED -> {
                val interlock = armed(e)
                if (!receipt(event.commitReceipt, e.baseGenerationId, e.baseGenerationSha256) ||
                    !head(event.verifiedHead, event.commitReceipt, e.prior, interlock)) d(s, "stale-receipt")
                else d(s.copy(phase = TransactionPhase.ARMED, interlock = interlock, armCommitReceipt = event.commitReceipt), "apply", SkinCommand.Apply(c))
            }
            event is TransactionEvent.ApplyVerified && s.phase == TransactionPhase.ARMED -> closure(s, c, e.target, TransactionPhase.APPLIED)
            event is TransactionEvent.ApplyFailed && s.phase == TransactionPhase.ARMED ->
                if (code(event.code)) reverse(s, c, event.code) else d(s, "invalid-code")
            event is TransactionEvent.RollbackVerified && s.phase == TransactionPhase.ROLLBACK_PENDING ->
                if (event.freshBinding == e.priorEstablishedOnBinding) d(s, "invalid-binding-proof")
                else closure(s, c, priorClosure(e), TransactionPhase.ROLLED_BACK)
            event is TransactionEvent.RollbackFailed && s.phase == TransactionPhase.ROLLBACK_PENDING -> {
                if (!code(event.code)) d(s, "invalid-code") else {
                    val failedLock = s.interlock.copy(state = InterlockState.ROLLBACK_FAILED,
                        originalFailure = s.originalFailure, rollbackFailure = event.code)
                    val persisted = followsArm(s, event.persistedFailureReceipt) &&
                        head(event.verifiedHead, event.persistedFailureReceipt, e.prior, failedLock)
                    d(s.copy(phase = TransactionPhase.BLOCKED, rollbackFailure = event.code,
                        interlock = if (persisted) failedLock else s.interlock,
                        failureReceipt = if (persisted) event.persistedFailureReceipt else null), "rollback-failed")
                }
            }
            event is TransactionEvent.CompletionCommitted && pending(s) ->
                if (!followsArm(s, event.commitReceipt) || !head(event.verifiedHead, event.commitReceipt,
                        s.pendingClosure, RotationInterlock.clear())) d(s, "stale-receipt")
                else d(s.copy(phase = TransactionPhase.COMMITTED, interlock = RotationInterlock.clear(),
                    activation = s.pendingClosure, completionReceipt = event.commitReceipt), "committed")
            event is TransactionEvent.CompletionRejected && pending(s) ->
                if (!code(event.code)) d(s, "invalid-code")
                else if (s.phase == TransactionPhase.APPLIED) reverse(s, c, event.code)
                else d(s.copy(phase = TransactionPhase.BLOCKED, rollbackFailure = event.code), "rollback-closure-rejected")
            event is TransactionEvent.CompletionIndeterminate && pending(s) -> d(s.copy(phase = TransactionPhase.BLOCKED), "completion-indeterminate")
            else -> d(s, "stale-phase")
        }
    }

    private fun closure(s: TransactionState, c: TransactionCorrelation, a: ActivationSnapshot, phase: TransactionPhase): TransactionDecision {
        val r = requireNotNull(s.armCommitReceipt) // valid(state) and phase establish this invariant.
        return d(s.copy(phase = phase, pendingClosure = a), "commit-closure", SkinCommand.Commit(c, r.newGenerationId, r.newGenerationSha256, a))
    }
    private fun reverse(s: TransactionState, c: TransactionCorrelation, code: String) =
        d(s.copy(phase = TransactionPhase.ROLLBACK_PENDING, pendingClosure = null, originalFailure = code), "rollback", SkinCommand.Rollback(c))
    private fun pending(s: TransactionState) = s.phase == TransactionPhase.APPLIED || s.phase == TransactionPhase.ROLLED_BACK
    private fun correlation(e: TransactionEvent): TransactionCorrelation? = when (e) {
        is TransactionEvent.Begin -> null
        is TransactionEvent.Prepared -> e.correlation
        is TransactionEvent.ArmCommitted -> e.correlation
        is TransactionEvent.ApplyVerified -> e.correlation
        is TransactionEvent.ApplyFailed -> e.correlation
        is TransactionEvent.RollbackVerified -> e.correlation
        is TransactionEvent.RollbackFailed -> e.correlation
        is TransactionEvent.CompletionCommitted -> e.correlation
        is TransactionEvent.CompletionRejected -> e.correlation
        is TransactionEvent.CompletionIndeterminate -> e.correlation
    }
    private fun armed(e: TransactionEnvelope) = RotationInterlock(InterlockState.ARMED, e.transactionId, e.operation,
        e.baseGenerationId, e.baseGenerationSha256, e.prior, e.target, e.binding, e.priorEstablishedOnBinding, null, null)
    private fun priorClosure(e: TransactionEnvelope) = if (!e.priorEstablishedOnBinding && e.prior.active is ActiveVisual.Pack)
        e.prior.copy(skinStamp = e.prior.skinStamp + 1) else e.prior
    private fun followsArm(s: TransactionState, r: RegistryCommitReceipt?): Boolean {
        val arm = s.armCommitReceipt ?: return false
        val e = s.envelope ?: return false
        return receipt(r, arm.newGenerationId, arm.newGenerationSha256) && r != null &&
            r.newGenerationId != e.baseGenerationId && r.newGenerationSha256 != e.baseGenerationSha256
    }
    private fun receipt(r: RegistryCommitReceipt?, id: String, digest: String) = r != null &&
        uuid(r.expectedGenerationId) && digest(r.expectedGenerationSha256) && uuid(r.newGenerationId) && digest(r.newGenerationSha256) &&
        r.expectedGenerationId == id && r.expectedGenerationSha256 == digest && r.newGenerationId != id && r.newGenerationSha256 != digest
    private fun head(h: VerifiedRegistryHead?, r: RegistryCommitReceipt?, a: ActivationSnapshot?, l: RotationInterlock) =
        h != null && r != null && h.generationId == r.newGenerationId && h.generationSha256 == r.newGenerationSha256 && h.activation == a && h.interlock == l

    // Exact CAS equality includes receipt hashes. Visual equality excludes the import receipt identity.
    private fun sameVisual(a: ActiveVisual, b: ActiveVisual) = a is ActiveVisual.Vanilla && b is ActiveVisual.Vanilla ||
        a is ActiveVisual.Pack && b is ActiveVisual.Pack && a.id == b.id && a.treeSha256 == b.treeSha256 && a.contentSha256 == b.contentSha256
    private fun envelopeValid(e: TransactionEnvelope): Boolean {
        if (!uuid(e.transactionId) || !uuid(e.baseGenerationId) || !digest(e.baseGenerationSha256) ||
            !token(e.binding) || !snapshot(e.prior) || !snapshot(e.target) || e.prior.skinStamp == Long.MAX_VALUE ||
            e.target.skinStamp != e.prior.skinStamp + 1) return false
        val p = e.prior; val t = e.target
        // History is not live proof. In particular unestablished vanilla still requires MODE_OFF restoration.
        if (sameVisual(p.active, t.active) && e.priorEstablishedOnBinding) return false
        fun selectedTarget() = t.active is ActiveVisual.Pack && t.selectedPackId == t.active.id
        fun establishModeTarget() = !e.priorEstablishedOnBinding && t.mode == p.mode && t.selectedPackId == p.selectedPackId &&
            (t.mode == SkinMode.ON && selectedTarget() || t.mode == SkinMode.ROTATE && t.active is ActiveVisual.Pack && t.active == p.active)
        return when (e.operation) {
            SkinOperationKind.MODE_ON -> p.mode == SkinMode.OFF && t.mode == SkinMode.ON &&
                t.selectedPackId == p.selectedPackId && selectedTarget()
            SkinOperationKind.MODE_OFF -> p.mode == SkinMode.ROTATE && t.mode == SkinMode.OFF &&
                t.active is ActiveVisual.Vanilla && t.selectedPackId == p.selectedPackId
            SkinOperationKind.DEATH_ROTATION -> p.mode == SkinMode.ROTATE && t.mode == SkinMode.ROTATE &&
                selectedTarget() && !sameVisual(p.active, t.active)
            SkinOperationKind.REBIND_APPLY -> establishModeTarget()
            SkinOperationKind.STARTUP_APPLY -> establishModeTarget()
        }
    }
    private fun snapshot(a: ActivationSnapshot?) = a != null && a.skinStamp >= 0 &&
        (a.selectedPackId == null || packId(a.selectedPackId)) &&
        (a.active is ActiveVisual.Vanilla || a.active is ActiveVisual.Pack &&
            packId(a.active.id) && digest(a.active.treeSha256) && digest(a.active.contentSha256) && digest(a.active.importReceiptSha256))
    private fun valid(s: TransactionState): Boolean {
        if (!token(s.binding) || !snapshot(s.activation) ||
            (s.originalFailure != null && !code(s.originalFailure)) || (s.rollbackFailure != null && !code(s.rollbackFailure))) return false
        if (s.phase == TransactionPhase.IDLE) return s.interlock == RotationInterlock.clear() && s.envelope == null &&
            s.armCommitReceipt == null && s.pendingClosure == null && s.originalFailure == null && s.rollbackFailure == null &&
            s.completionReceipt == null && s.failureReceipt == null
        val e = s.envelope ?: return false
        if (!envelopeValid(e) || e.binding != s.binding) return false
        if (s.phase == TransactionPhase.PREPARING || s.phase == TransactionPhase.PREPARED)
            return s.interlock == RotationInterlock.clear() && s.activation == e.prior && s.armCommitReceipt == null &&
                s.pendingClosure == null && s.originalFailure == null && s.rollbackFailure == null && s.completionReceipt == null && s.failureReceipt == null
        if (!receipt(s.armCommitReceipt, e.baseGenerationId, e.baseGenerationSha256)) return false
        val closure = if (s.originalFailure == null) e.target else priorClosure(e)
        if (s.phase == TransactionPhase.COMMITTED) return s.interlock == RotationInterlock.clear() &&
            s.pendingClosure == closure && s.activation == closure && followsArm(s, s.completionReceipt) && s.failureReceipt == null && s.rollbackFailure == null
        if (s.activation != e.prior || s.completionReceipt != null) return false
        if (s.phase == TransactionPhase.BLOCKED) {
            if (s.pendingClosure != null && s.pendingClosure != closure) return false
            if (s.pendingClosure == null && (s.originalFailure == null || s.rollbackFailure == null)) return false
            return if (s.failureReceipt == null) s.interlock == armed(e) else
                s.pendingClosure == null && s.originalFailure != null && s.rollbackFailure != null && followsArm(s, s.failureReceipt) &&
                    s.interlock == armed(e).copy(state = InterlockState.ROLLBACK_FAILED, originalFailure = s.originalFailure, rollbackFailure = s.rollbackFailure)
        }
        if (s.interlock != armed(e) || s.failureReceipt != null || s.rollbackFailure != null) return false
        return when (s.phase) {
            TransactionPhase.ARMED -> s.pendingClosure == null && s.originalFailure == null
            TransactionPhase.APPLIED -> s.pendingClosure == e.target && s.originalFailure == null
            TransactionPhase.ROLLBACK_PENDING -> s.pendingClosure == null && s.originalFailure != null
            TransactionPhase.ROLLED_BACK -> s.pendingClosure == priorClosure(e) && s.originalFailure != null
            else -> false
        }
    }
    // Registry syntax, without codec/catalog/IO dependencies.
    private fun uuid(s: String) = s.length == 36 && UUID_TEXT.matches(s)
    private fun digest(s: String) = s.length == 64 && DIGEST.matches(s)
    private fun packId(s: String) = s.length <= 64 && PACK_ID.matches(s)
    private fun code(s: String) = s.length <= 128 && CODE.matches(s)
    private fun token(t: SkinBindingToken?): Boolean {
        val s = t?.value ?: return false
        if (s.isEmpty() || s.length > 512 || s.first().isWhitespace() || s.last().isWhitespace()) return false
        var count = 0; var i = 0
        while (i < s.length) {
            val c = s[i]
            if (c.isISOControl() || c == '؜' || c == '‎' || c == '‏' ||
                c in '‪'..'‮' || c in '⁦'..'⁩') return false
            if (c.isHighSurrogate()) { i++; if (i >= s.length || !s[i].isLowSurrogate()) return false }
            else if (c.isLowSurrogate()) return false
            i++; count++
        }
        return count <= 256 && Normalizer.isNormalized(s, Normalizer.Form.NFKC)
    }
    private fun d(s: TransactionState, diagnosis: String, vararg commands: SkinCommand) = TransactionDecision(s, diagnosis, *commands)
    private companion object {
        val UUID_TEXT = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
        val DIGEST = Regex("[0-9a-f]{64}")
        val PACK_ID = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
        val CODE = Regex("[A-Z][A-Z0-9_]{0,127}")
    }
}
