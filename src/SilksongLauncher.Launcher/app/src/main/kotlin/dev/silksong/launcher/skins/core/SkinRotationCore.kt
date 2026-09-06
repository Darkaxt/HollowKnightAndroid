package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*

/** Task64 selector only. Task65 owns mode/proof/transaction composition and correlated retirement.
 * INTENT_ISSUED is deliberately parked: no new death, second delivery or outcome authority here. */
class SkinRotationCore {
    /** H1 emits mode-only CAS values; task66 must delegate all visual execution to TransactionCore. */
    fun decide(s: RotationModeState, event: RotationModeEvent): RotationModeDecision {
        if (!isModeStateValid(s)) return mode(s, "invalid-state")
        if (event is RotationModeEvent.Selector) {
            if (!validEvent(event.event)) return mode(s, "invalid-event")
            if (event.event is RotationEvent.Rebind) {
                val next = decide(s.rotation, event.event).state
                if (next == s.rotation) return mode(s, "same-binding")
                return mode(s.copy(rotation = next, liveProof = null,
                    operation = s.operation?.copy(reboundBlocked = true)), "rebound")
            }
            if (s.offRequested || s.operation != null) return mode(s, "mode-busy")
            val next = decide(s.rotation, event.event)
            return mode(s.copy(rotation = next.state), next.diagnosis)
        }
        if (event is RotationModeEvent.VerifiedVisual) {
            if (!proofValid(event.value) || event.value.hero != s.rotation.currentHero ||
                event.value.proof.binding != s.rotation.currentSkin) return mode(s, "stale-proof")
            if (s.operation != null) return mode(s, "mode-busy")
            return mode(s.copy(liveProof = event.value), "visual-verified")
        }
        if (event is RotationModeEvent.AdvanceMode) return advance(s)
        val p = s.operation ?: return mode(s, "no-mode-operation")
        val correlation = when (event) {
            is RotationModeEvent.ModeCommitted -> event.correlation
            is RotationModeEvent.ModeIndeterminate -> event.correlation
            is RotationModeEvent.ModeFailed -> event.correlation
            is RotationModeEvent.RetryModeCommit -> event.correlation
            else -> return mode(s, "invalid-event")
        }
        if (correlation != p.correlation) return mode(s, "stale-correlation")
        if (p.reboundBlocked) return mode(s, "rebound-resolution-required")
        return when (event) {
            is RotationModeEvent.ModeCommitted -> {
                val r = event.receipt; val h = event.head
                if (!uuid(r.newGenerationId) || !digest(r.newGenerationSha256) ||
                    r.expectedGenerationId != p.baseHead.generationId || r.expectedGenerationSha256 != p.baseHead.generationSha256 ||
                    r.newGenerationId == r.expectedGenerationId || r.newGenerationSha256 == r.expectedGenerationSha256 ||
                    !headValid(h) || h.generationId != r.newGenerationId || h.generationSha256 != r.newGenerationSha256 ||
                    h.activation != p.target) mode(s, "stale-receipt")
                else mode(s.copy(rotation = s.rotation.copy(activation = p.target), head = h,
                    operation = null, offRequested = false, canceledPending = null), "mode-committed")
            }
            is RotationModeEvent.ModeIndeterminate -> mode(s.copy(operation = p.copy(phase = ModeCommitPhase.INDETERMINATE)), "mode-indeterminate")
            is RotationModeEvent.ModeFailed -> mode(s.copy(operation = p.copy(phase = ModeCommitPhase.DEFINITIVE_FAILURE)), "mode-failed")
            is RotationModeEvent.RetryModeCommit -> mode(s, "retry-exact-cas", command(p))
            else -> mode(s, "invalid-event")
        }
    }
    private fun advance(initial: RotationModeState): RotationModeDecision {
        if (initial.operation != null) return mode(initial, "mode-busy")
        if (initial.rotation.currentHero == null) return mode(initial, "unbound")
        val a = initial.rotation.activation
        if (a.mode == SkinMode.OFF) {
            if (a.selectedPackId == null) return mode(initial, "NO_SELECTED_SKIN")
            val selected = initial.rotation.ring.entries.singleOrNull { it.currentObject.id == a.selectedPackId }
                ?: return mode(initial, "selected-object-unavailable")
            if (!selectedEstablished(initial.liveProof, a.active, selected.currentObject)) return mode(initial, "transaction-required")
            if (initial.operationHighWater == Long.MAX_VALUE) return mode(initial, "operation-id-exhausted")
            val next = initial.operationHighWater + 1
            // Visual proof ignores import identity; the exact CAS publishes CURRENT identity.
            // No visual write means no new skin stamp, including at stamp exhaustion.
            val p = ModeOperation(ModeCorrelation(operationId(next), initial.rotation.currentHero, initial.rotation.currentSkin!!),
                initial.head, a.copy(mode = SkinMode.ON, active = selected.currentObject), selectedProof = initial.liveProof)
            return mode(initial.copy(operationHighWater = next, operation = p), "commit-mode", command(p))
        }
        var s = initial
        if (a.mode == SkinMode.ROTATE) {
            val pending = s.rotation.pending
            s = s.copy(offRequested = true)
            if (pending?.phase == RotationPendingPhase.INTENT_ISSUED) return mode(s, "issued-rotation-busy")
            if (pending != null) s = s.copy(rotation = s.rotation.copy(pending = null), canceledPending = pending)
            if (s.liveProof?.proof !is VerifiedLiveVisualProof.Vanilla) return mode(s, "transaction-required")
        }
        if (s.operationHighWater == Long.MAX_VALUE) return mode(s, "operation-id-exhausted")
        val next = s.operationHighWater + 1
        val p = ModeOperation(ModeCorrelation(operationId(next), s.rotation.currentHero!!, s.rotation.currentSkin!!),
            s.head, a.copy(mode = if (a.mode == SkinMode.ON) SkinMode.ROTATE else SkinMode.OFF),
            if (a.mode == SkinMode.ROTATE) s.liveProof else null)
        return mode(s.copy(operationHighWater = next, operation = p), "commit-mode", command(p))
    }
    /** Validates the entire composed state; this is NOT an Apply permission query. */
    fun isModeStateValid(s: RotationModeState): Boolean {
        if (!valid(s.rotation) || !headValid(s.head) || s.head.activation != s.rotation.activation || s.operationHighWater < 0) return false
        if (s.liveProof != null && (!proofValid(s.liveProof) || s.liveProof.hero != s.rotation.currentHero ||
                s.liveProof.proof.binding != s.rotation.currentSkin)) return false
        if (s.offRequested && s.rotation.activation.mode != SkinMode.ROTATE) return false
        val canceled = s.canceledPending
        if (canceled != null) {
            if (!s.offRequested || s.rotation.pending != null || canceled.phase != RotationPendingPhase.AWAITING_STABILITY ||
                !valid(s.rotation.copy(currentHero = canceled.hero, currentSkin = canceled.skin, pending = canceled))) return false
        }
        if (s.offRequested && s.rotation.pending?.phase == RotationPendingPhase.AWAITING_STABILITY) return false
        val p = s.operation ?: return true
        if (s.rotation.currentHero == null || s.rotation.pending != null || s.operationHighWater == 0L ||
            p.correlation.operationId != operationId(s.operationHighWater) || !RotationValues.text(p.correlation.hero.value, 256) ||
            !RotationValues.text(p.correlation.skin.value, 256) || p.baseHead != s.head || !RotationValues.activation(p.target)) return false
        if (!p.reboundBlocked && (p.correlation.hero != s.rotation.currentHero || p.correlation.skin != s.rotation.currentSkin)) return false
        if (p.reboundBlocked && s.liveProof != null) return false
        if (!p.reboundBlocked && p.vanillaProof != null && s.liveProof != p.vanillaProof) return false
        if (!p.reboundBlocked && p.selectedProof != null && s.liveProof != p.selectedProof) return false
        val a = s.rotation.activation
        if (a.mode != SkinMode.OFF && p.selectedProof != null) return false
        return when (a.mode) {
            SkinMode.ON -> !s.offRequested && p.vanillaProof == null && p.target == a.copy(mode = SkinMode.ROTATE)
            SkinMode.ROTATE -> s.offRequested && p.target == a.copy(mode = SkinMode.OFF) && p.vanillaProof?.let {
                proofValid(it) && it.proof is VerifiedLiveVisualProof.Vanilla && it.hero == p.correlation.hero && it.proof.binding == p.correlation.skin
            } == true
            SkinMode.OFF -> s.rotation.ring.entries.singleOrNull { it.currentObject.id == a.selectedPackId }?.let {
                !s.offRequested && p.vanillaProof == null && p.target == a.copy(mode = SkinMode.ON, active = it.currentObject) &&
                    p.selectedProof?.hero == p.correlation.hero && p.selectedProof.proof.binding == p.correlation.skin &&
                    selectedEstablished(p.selectedProof, a.active, it.currentObject)
            } == true
        }
    }
    fun isPriorEstablished(s: RotationModeState): Boolean {
        if (!isModeStateValid(s)) return false
        val proof = s.liveProof?.proof ?: return false
        val active = s.rotation.activation.active
        return proof is VerifiedLiveVisualProof.Vanilla && active is ActiveVisual.Vanilla ||
            proof is VerifiedLiveVisualProof.Pack && active is ActiveVisual.Pack && proof.visual.id == active.id &&
            proof.visual.treeSha256 == active.treeSha256 && proof.visual.contentSha256 == active.contentSha256
    }
    private fun selectedEstablished(p: HeroVerifiedVisual?, prior: ActiveVisual, target: ActiveVisual.Pack): Boolean =
        p != null && proofValid(p) && p.proof is VerifiedLiveVisualProof.Pack && prior is ActiveVisual.Pack &&
            samePackVisual(p.proof.visual, prior) && samePackVisual(p.proof.visual, target)
    private fun samePackVisual(a: ActiveVisual.Pack, b: ActiveVisual.Pack) =
        a.id == b.id && a.treeSha256 == b.treeSha256 && a.contentSha256 == b.contentSha256
    private fun proofValid(p: HeroVerifiedVisual) = RotationValues.text(p.hero.value, 256) &&
        RotationValues.text(p.proof.binding.value, 256) && when (val v = p.proof) {
            is VerifiedLiveVisualProof.Vanilla -> true
            is VerifiedLiveVisualProof.Pack -> RotationValues.pack(v.visual)
        }
    private fun headValid(h: VerifiedRegistryHead) = uuid(h.generationId) && digest(h.generationSha256) &&
        RotationValues.activation(h.activation) && h.interlock == RotationInterlock.clear()
    private fun uuid(s: String) = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").matches(s)
    private fun digest(s: String) = Regex("[0-9a-f]{64}").matches(s)
    private fun operationId(n: Long): String {
        val hex = n.toString(16).padStart(16, '0')
        return "00000000-0000-${hex.substring(0, 4)}-${hex.substring(4, 8)}-0000${hex.substring(8)}"
    }
    private fun command(p: ModeOperation): RotationModeCommand {
        val issued = p.copy(phase = ModeCommitPhase.ISSUED)
        return if (p.target.mode == SkinMode.ON) RotationModeCommand.CommitVerifiedSelectedOn(issued)
        else if (p.target.mode == SkinMode.ROTATE) RotationModeCommand.CommitOnToRotate(issued)
        else RotationModeCommand.CommitVerifiedVanillaOff(issued)
    }
    private fun mode(s: RotationModeState, diagnosis: String, vararg commands: RotationModeCommand) = RotationModeDecision(s, diagnosis, *commands)

    fun decide(state: RotationState, event: RotationEvent): RotationDecision {
        if (!valid(state)) return d(state, "invalid-state")
        if (!validEvent(event)) return d(state, "invalid-event")
        if (event is RotationEvent.Rebind) {
            if (event.hero == state.currentHero && event.skin == state.currentSkin) return d(state, "same-binding")
            return d(state.copy(currentHero = event.hero, currentSkin = event.skin,
                pending = state.pending?.copy(hero = event.hero, skin = event.skin)), "rebound")
        }
        if (state.currentHero == null) return d(state, "unbound")
        return when (event) {
            is RotationEvent.ConfirmDeath -> confirm(state, event)
            is RotationEvent.StableRespawn -> stable(state, event.token)
            is RotationEvent.Rebind -> d(state, "invalid-event")
        }
    }
    private fun confirm(s: RotationState, e: RotationEvent.ConfirmDeath): RotationDecision {
        if (e.hero != s.currentHero || e.skin != s.currentSkin) return d(s, "stale-binding")
        if (s.activation.mode != SkinMode.ROTATE) return d(s, "rotation-inactive")
        if (e.epoch.value == 0uL || e.epoch.value <= s.epochHighWater.value) return d(s, "consumed-epoch")
        if (s.pending != null) return d(s, "pending-rotation")
        val candidate = successor(s) ?: return d(s, "insufficient-eligible")
        return d(s.copy(epochHighWater = e.epoch, pending = PendingRotation(candidate, e.epoch, e.hero, e.skin)), "candidate-confirmed")
    }
    private fun stable(s: RotationState, token: StableRespawnToken): RotationDecision {
        if (token.hero != s.currentHero || token.skin != s.currentSkin) return d(s, "stale-binding")
        val p = s.pending ?: return d(s, "no-pending")
        if (token.deathEpoch != p.epoch) return d(s, "stale-epoch")
        if (p.phase == RotationPendingPhase.INTENT_ISSUED) return d(s, "intent-already-issued")
        val intent = RotationReadyIntent(p.epoch, p.hero, p.skin, p.candidate, s.activation)
        return RotationDecision(s.copy(pending = p.copy(phase = RotationPendingPhase.INTENT_ISSUED, issuedIntent = intent)), "rotation-ready", intent)
    }
    private fun successor(s: RotationState): RotationDescriptor? {
        val eligible = s.ring.entries.filter { it.eligible }
        if (eligible.size <= 1) return null
        val active = (s.activation.active as? ActiveVisual.Pack)?.id
        var anchor = eligible.indexOfFirst { it.currentObject.id == active }
        if (anchor < 0) anchor = eligible.indexOfFirst { it.currentObject.id == s.activation.selectedPackId }
        return eligible[(anchor + 1) % eligible.size]
    }
    private fun valid(s: RotationState): Boolean {
        if (!RotationValues.activation(s.activation) || (s.currentHero == null) != (s.currentSkin == null)) return false
        if (s.currentHero == null) return s.pending == null && s.epochHighWater.value == 0uL
        if (!RotationValues.text(s.currentHero.value, 256) || !RotationValues.text(s.currentSkin!!.value, 256)) return false
        val p = s.pending ?: return true
        if (s.activation.mode != SkinMode.ROTATE || p.epoch.value == 0uL || p.epoch != s.epochHighWater ||
            p.hero != s.currentHero || p.skin != s.currentSkin || p.candidate != successor(s)) return false
        return when (p.phase) {
            RotationPendingPhase.AWAITING_STABILITY -> p.issuedIntent == null
            RotationPendingPhase.INTENT_ISSUED -> p.issuedIntent?.let {
                it.epoch == p.epoch && it.candidate == p.candidate && it.prior == s.activation &&
                    RotationValues.text(it.hero.value, 256) && RotationValues.text(it.skin.value, 256)
            } == true
        }
    }
    private fun validEvent(e: RotationEvent): Boolean = when (e) {
        is RotationEvent.Rebind -> RotationValues.text(e.hero.value, 256) && RotationValues.text(e.skin.value, 256)
        is RotationEvent.ConfirmDeath -> RotationValues.text(e.hero.value, 256) && RotationValues.text(e.skin.value, 256)
        is RotationEvent.StableRespawn -> RotationValues.text(e.token.hero.value, 256) && RotationValues.text(e.token.skin.value, 256)
    }
    private fun d(s: RotationState, diagnosis: String) = RotationDecision(s, diagnosis)
}
