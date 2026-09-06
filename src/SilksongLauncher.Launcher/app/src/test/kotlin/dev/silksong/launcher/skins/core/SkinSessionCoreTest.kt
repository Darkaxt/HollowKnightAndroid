package dev.silksong.launcher.skins.core

import org.junit.Assert.*
import org.junit.Test

class SkinSessionCoreTest {
    private val core = SkinSessionCore()
    private fun id(n: Int) = "00000000-0000-0000-0000-%012x".format(n)
    private fun hash(n: Int) = "%064x".format(n)
    private val launcher = SessionProcessIdentity(1000, 10, "18446744073709551615")
    private val game = SessionProcessIdentity(1000, 20, "99999999999999999999")
    private fun binding() = SessionBinding("hollow-knight", id(1), hash(1), "sessions/${id(1)}/descriptor.json", id(2), hash(2), Long.MAX_VALUE, id(3), hash(3), launcher)
    private fun lease(b: SessionBinding = binding(), phase: SessionLeasePhase = SessionLeasePhase.LAUNCH_PENDING, ownedClose: Boolean = false, reason: String = "GAME_EXIT"): VerifiedSessionLease {
        val seq = if (phase == SessionLeasePhase.LAUNCH_PENDING) 0L else if (phase == SessionLeasePhase.GAME_OWNED || !ownedClose) 1L else 2L
        val d = SessionLeaseDocument(1, b, seq, id(10 + seq.toInt()), if (seq == 0L) null else id(9 + seq.toInt()), phase,
            if (phase == SessionLeasePhase.GAME_OWNED || ownedClose) game else null, if (phase == SessionLeasePhase.CLOSED) reason else null)
        return VerifiedSessionLease(SessionLeaseHead(b.descriptorId, b.leaseId, seq, phase, hash(10 + seq.toInt())), d, hash(10 + seq.toInt()))
    }
    private fun step(s: SessionState, e: SessionEvent) = core.decide(s, e).state!!
    private fun bound(): SessionState { val p = lease(); val s = step(SessionState(), SessionEvent.BindIssuedPending(SessionAcquisitionEvidence(p, SessionBarrier.ActiveInstalled(p.head)))); assertNotNull(s.acquisition); return s }
    private fun claiming(s: SessionState = bound()) = step(s, SessionEvent.ClaimRequested(binding(), game)).also { assertNotNull(it.pending) }
    private fun receipt(cmd: SessionCommand, parent: VerifiedSessionLease, child: VerifiedSessionLease, op: SessionOperation) = SessionTransitionReceipt(cmd.correlation, op, parent, child,
        if (op == SessionOperation.CLAIM) SessionBarrier.ActiveInstalled(child.head) else SessionBarrier.ActiveRemoved(parent.head))
    private fun owned(): SessionState { val s = claiming(); return step(s, SessionEvent.ClaimCompleted(s.pending!!.correlation, SessionTransitionResult.Durable(receipt(s.pending, s.lease!!, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)))) }
    private fun profile(c: SessionCorrelation) = SessionProfileRecoveryEvidence("hollow-knight", c)
    private fun owner(c: SessionCorrelation, p: VerifiedSessionLease, l: SessionLiveness) = SessionOwnerEvidence(c, p.document.binding, p.document.gameOwner ?: launcher, l, if (l == SessionLiveness.ALIVE) p.document.gameOwner ?: launcher else null)
    private fun target(c: SessionCorrelation, p: VerifiedSessionLease, t: SessionPresence) = SessionTargetEvidence(c, p.document.binding, SessionTargetProcess("dev.game.hk", "dev.game.hk"), t, if (t == SessionPresence.PRESENT) game else null)
    @Test fun pendingOwnedClosedRequiresExactDurableCompletionsAndCachesClose() {
        val p = bound(); assertFalse(core.sessionWritesEligible(p)); val s = claiming(p); assertEquals(p.lease, s.lease)
        val o = owned(); assertTrue(core.sessionWritesEligible(o)); assertNull(core.decide(o, SessionEvent.ClaimRequested(binding(), game)).command)
        val closing = step(o, SessionEvent.CloseRequested(binding(), "GAME_EXIT")); assertFalse(core.sessionWritesEligible(closing)); assertEquals(o.lease, closing.lease)
        val r = receipt(closing.pending!!, closing.lease!!, lease(phase = SessionLeasePhase.CLOSED, ownedClose = true), SessionOperation.CLOSE)
        val done = step(closing, SessionEvent.CloseCompleted(closing.pending.correlation, SessionTransitionResult.Durable(r)))
        assertEquals(r.child, done.lease); assertEquals(SessionMutationGate.UNKNOWN, done.gate)
        val replay = core.decide(done, SessionEvent.CloseRequested(binding(), "GAME_EXIT")); assertEquals(r, replay.completedClose); assertNull(replay.command)
        assertNull(core.decide(done, SessionEvent.CloseRequested(binding(), "OTHER")).completedClose)
        assertEquals(done, step(done, SessionEvent.BindIssuedPending(p.acquisition))); assertFalse(core.sessionWritesEligible(done))
    }
    @Test fun pendingCanCloseWithoutGameOwner() {
        val s = step(bound(), SessionEvent.CloseRequested(binding(), "GAME_EXIT")); val r = receipt(s.pending!!, s.lease!!, lease(phase = SessionLeasePhase.CLOSED), SessionOperation.CLOSE)
        val done = step(s, SessionEvent.CloseCompleted(s.pending.correlation, SessionTransitionResult.Durable(r)))
        assertEquals(SessionLeasePhase.CLOSED, done.lease!!.document.state); assertNull(done.lease.document.gameOwner); assertTrue(core.isStateValid(done))
    }
    @Test fun retryableClaimPinsOwnerAndRejectsOldDuplicateCallbacks() {
        for (code in listOf(SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE)) {
            val s = claiming(); val old = s.pending!!; val failure = SessionTransitionResult.Failed(code)
            val retry = step(s, SessionEvent.ClaimCompleted(old.correlation, failure)); assertEquals(SessionClaimUse.AVAILABLE, retry.claimUse); assertFalse(core.sessionWritesEligible(retry))
            assertNull(core.decide(retry, SessionEvent.ClaimRequested(binding(), game.copy(pid = 21))).command)
            val next = claiming(retry); assertTrue(next.ordinal > old.correlation.ordinal); assertEquals(next, step(next, SessionEvent.ClaimCompleted(old.correlation, failure)))
            assertEquals(next, step(next, SessionEvent.CloseRequested(binding(), "GAME_EXIT")))
            val r = receipt(next.pending!!, next.lease!!, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)
            val done = step(next, SessionEvent.ClaimCompleted(next.pending.correlation, SessionTransitionResult.Durable(r)))
            assertTrue(core.sessionWritesEligible(done)); assertEquals(done, step(done, SessionEvent.ClaimCompleted(next.pending.correlation, SessionTransitionResult.Durable(r))))
        }
    }
    @Test fun definitiveClaimRejectionStillClosesAndCloseFailurePinsReason() {
        for (code in listOf(SessionFailure.LIFECYCLE_BLOCKED, SessionFailure.SESSION_RECOVERY_AMBIGUOUS, SessionFailure.PROFILE_QUOTA_EXCEEDED)) {
            var s = claiming(); s = step(s, SessionEvent.ClaimCompleted(s.pending!!.correlation, SessionTransitionResult.Failed(code)))
            assertEquals(SessionClaimUse.REJECTED, s.claimUse); assertNull(core.decide(s, SessionEvent.ClaimRequested(binding(), game)).command)
            s = step(s, SessionEvent.CloseRequested(binding(), "GAME_EXIT")); assertNotNull(s.pending)
            s = step(s, SessionEvent.CloseCompleted(s.pending!!.correlation, SessionTransitionResult.Failed(code)))
            assertNull(core.decide(s, SessionEvent.CloseRequested(binding(), "OTHER")).command); assertNull(core.decide(s, SessionEvent.ClaimRequested(binding(), game)).command)
            assertNotNull(core.decide(s, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
        }
    }
    @Test fun claimReceiptRejectsBindingDocumentHeadParentOwnerAndBarrierMismatches() {
        val s = claiming(); val r = receipt(s.pending!!, s.lease!!, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)
        for (bad in listOf(r.copy(correlation = r.correlation.copy(ordinal = 99)), r.copy(operation = SessionOperation.CLOSE),
            r.copy(parent = r.parent.copy(canonicalDocumentSha256 = hash(99))), r.copy(child = r.child.copy(head = r.child.head.copy(sha256 = hash(99)))),
            r.copy(child = r.child.copy(document = r.child.document.copy(parentTransitionId = id(99)))),
            r.copy(child = r.child.copy(document = r.child.document.copy(gameOwner = game.copy(pid = 99)))),
            r.copy(child = r.child.copy(document = r.child.document.copy(binding = binding().copy(registrySha256 = hash(99))))),
            r.copy(barrier = SessionBarrier.ActiveInstalled(r.parent.head)), r.copy(barrier = SessionBarrier.ActiveRemoved(r.parent.head)), r.copy(barrier = null))) {
            val d = core.decide(s, SessionEvent.ClaimCompleted(s.pending.correlation, SessionTransitionResult.Durable(bad))); assertFalse(core.sessionWritesEligible(d.state)); assertEquals(s.lease, d.state!!.lease); assertNull(d.command)
        }
    }
    @Test fun acquisitionRejectsEveryBindingFieldAndHeadOnlyShapes() {
        val p = lease(); val b = binding()
        for (bad in listOf(b.copy(profileId = "silksong"), b.copy(descriptorId = "BAD"), b.copy(descriptorSha256 = "A"), b.copy(descriptorPath = "descriptor.json"), b.copy(leaseId = b.descriptorId), b.copy(tokenSha256 = "raw"), b.copy(sessionSequence = -1), b.copy(registryGenerationId = "bad"), b.copy(registrySha256 = "bad"), b.copy(launcherOwner = launcher.copy(processStartToken = "01"))))
            assertNull(step(SessionState(), SessionEvent.BindIssuedPending(SessionAcquisitionEvidence(lease(bad), SessionBarrier.ActiveInstalled(lease(bad).head)))).acquisition)
        assertNull(step(SessionState(), SessionEvent.BindIssuedPending(SessionAcquisitionEvidence(p, SessionBarrier.ActiveInstalled(p.head.copy(sha256 = hash(99)))))).acquisition)
    }
    @Test fun pendingRecoveryCoversAllNineLivenessCombinationsWithoutMintingTransfer() {
        for (l in SessionLiveness.entries) for (t in SessionPresence.entries) {
            val s = step(SessionState(), SessionEvent.RecoveryRequested); assertNotNull(s.pending); val c = s.pending!!.correlation; val p = lease()
            val result = SessionRecoveryResult.RetainedOpenLease(p, SessionBarrier.ActiveInstalled(p.head), owner(c, p, l), target(c, p, t))
            val done = step(s, SessionEvent.RecoveryCompleted(c, result))
            assertEquals(if (l == SessionLiveness.ALIVE || t == SessionPresence.PRESENT) SessionMutationGate.ACTIVE else SessionMutationGate.UNKNOWN, done.gate)
            assertNull(done.acquisition); assertNull(done.lease); assertFalse(core.sessionWritesEligible(done)); assertNull(core.decide(done, SessionEvent.ClaimRequested(binding(), game)).command)
        }
    }
    @Test fun ownedRecoveryUsesOnlyScopedOwnerAndNeverGrantsGameWrites() {
        for (l in SessionLiveness.entries) {
            val s = step(owned(), SessionEvent.RecoveryRequested); val c = s.pending!!.correlation; val p = s.lease!!
            val done = step(s, SessionEvent.RecoveryCompleted(c, SessionRecoveryResult.RetainedOpenLease(p, SessionBarrier.ActiveInstalled(p.head), owner(c, p, l), null)))
            assertEquals(if (l == SessionLiveness.ALIVE) SessionMutationGate.ACTIVE else SessionMutationGate.UNKNOWN, done.gate); assertFalse(core.sessionWritesEligible(done)); assertNotNull(core.decide(done, SessionEvent.RecoveryRequested).command)
        }
    }
    @Test fun recoveredClosedRequiresDeadOwnerAbsentPendingTargetExactReceiptAndProfileEvidence() {
        for (o in listOf(false, true)) {
            val s = step(if (o) owned() else bound(), SessionEvent.RecoveryRequested); val c = s.pending!!.correlation; val p = s.lease!!
            val reason = if (o) "RECOVERY_GAME_OWNER_DEAD" else "RECOVERY_LAUNCHER_DEAD"
            val r = receipt(s.pending, p, lease(phase = SessionLeasePhase.CLOSED, ownedClose = o, reason = reason), SessionOperation.CLOSE)
            val good = SessionRecoveryResult.RecoveredClosed(p, r, owner(c, p, SessionLiveness.DEAD), if (o) null else target(c, p, SessionPresence.ABSENT), profile(c))
            for (bad in listOf(good.copy(profile = null), good.copy(owner = owner(c, p, SessionLiveness.UNKNOWN)), good.copy(receipt = r.copy(barrier = SessionBarrier.ActiveRemoved(r.child.head))), good.copy(profile = profile(c.copy(ordinal = 99))))) {
                val blocked = step(s, SessionEvent.RecoveryCompleted(c, bad)); assertEquals(SessionMutationGate.UNKNOWN, blocked.gate); assertFalse(core.sessionWritesEligible(blocked))
            }
            val done = step(s, SessionEvent.RecoveryCompleted(c, good)); assertEquals(SessionMutationGate.CLEAR, done.gate); assertEquals(r.child, done.lease); assertTrue(core.isStateValid(done)); assertFalse(core.sessionWritesEligible(done))
        }
    }
    @Test fun noOpenAndDeferredRecoveryAreRetryableNeverWriteAuthority() {
        val s = step(SessionState(), SessionEvent.RecoveryRequested); assertNotNull(s.pending); val c = s.pending!!.correlation
        var done = step(s, SessionEvent.RecoveryCompleted(c, SessionRecoveryResult.NoOpenLease(profile(c)))); assertEquals(SessionMutationGate.CLEAR, done.gate); assertFalse(core.sessionWritesEligible(done))
        repeat(3) { done = step(done, SessionEvent.RecoveryRequested); done = step(done, SessionEvent.RecoveryCompleted(done.pending!!.correlation, SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE))); assertEquals(SessionMutationGate.UNKNOWN, done.gate) }
        assertEquals(4L, done.ordinal)
    }
    @Test fun forgedRecoveryRecordCannotClearDifferentTerminalChain() {
        var s = step(bound(), SessionEvent.CloseRequested(binding(), "GAME_EXIT"))
        val r = receipt(s.pending!!, s.lease!!, lease(phase = SessionLeasePhase.CLOSED), SessionOperation.CLOSE)
        s = step(s, SessionEvent.CloseCompleted(s.pending!!.correlation, SessionTransitionResult.Durable(r)))
        s = step(s, SessionEvent.RecoveryRequested); val c = s.pending!!.correlation
        val done = step(s, SessionEvent.RecoveryCompleted(c, SessionRecoveryResult.NoOpenLease(profile(c))))
        val parent = lease().copy(document = lease().document.copy(transitionId = id(90)))
        var child = lease(phase = SessionLeasePhase.CLOSED, reason = "RECOVERY_LAUNCHER_DEAD"); child = child.copy(document = child.document.copy(parentTransitionId = id(90)))
        val other = receipt(s.pending!!, parent, child, SessionOperation.CLOSE)
        val evidence = SessionRecoveryResult.RecoveredClosed(parent, other, owner(c, parent, SessionLiveness.DEAD), target(c, parent, SessionPresence.ABSENT), profile(c))
        assertFalse(core.isStateValid(done.copy(recovery = SessionRecoveryRecord(c, evidence))))
    }
    @Test fun closedReceiptOrdinalMustFollowClaimAndRecoveryHighWaterMustHaveRecord() {
        val s = step(owned(), SessionEvent.CloseRequested(binding(), "GAME_EXIT")); val r = receipt(s.pending!!, s.lease!!, lease(phase = SessionLeasePhase.CLOSED, ownedClose = true), SessionOperation.CLOSE)
        val done = step(s, SessionEvent.CloseCompleted(s.pending.correlation, SessionTransitionResult.Durable(r)))
        assertFalse(core.isStateValid(done.copy(closedReceipt = r.copy(correlation = r.correlation.copy(ordinal = 1)))))
        val recovery = step(owned(), SessionEvent.RecoveryRequested); val c = recovery.pending!!.correlation
        val retained = SessionRecoveryResult.RetainedOpenLease(recovery.lease!!, SessionBarrier.ActiveInstalled(recovery.lease.head), owner(c, recovery.lease, SessionLiveness.ALIVE), null)
        val confirmed = step(recovery, SessionEvent.RecoveryCompleted(c, retained))
        assertFalse(core.isStateValid(confirmed.copy(recoveryHighWater = 3, ordinal = 3)))
    }
    @Test fun uncertainClaimRecoveryRetainsRetryOrQualifiesOwnedForCloseOnly() {
        val claiming = claiming(); val old = claiming.pending!!; val s = step(claiming, SessionEvent.ClaimCompleted(old.correlation, SessionTransitionResult.Failed(SessionFailure.INDETERMINATE)))
        val recovering = step(s, SessionEvent.RecoveryRequested); val c = recovering.pending!!.correlation; val pending = s.lease!!
        val retained = SessionRecoveryResult.RetainedOpenLease(pending, SessionBarrier.ActiveInstalled(pending.head), owner(c, pending, SessionLiveness.ALIVE), target(c, pending, SessionPresence.UNKNOWN))
        val retryable = step(recovering, SessionEvent.RecoveryCompleted(c, retained)); assertNotNull(core.decide(retryable, SessionEvent.ClaimRequested(binding(), game)).command)
        val o = lease(phase = SessionLeasePhase.GAME_OWNED); val r = receipt(recovering.pending, pending, o, SessionOperation.CLAIM)
        val observed = SessionRecoveryResult.RetainedOpenLease(o, SessionBarrier.ActiveInstalled(o.head), owner(c, o, SessionLiveness.ALIVE), null, r)
        val done = step(recovering, SessionEvent.RecoveryCompleted(c, observed)); assertEquals(o, done.lease); assertEquals(SessionClaimUse.REJECTED, done.claimUse); assertFalse(core.sessionWritesEligible(done))
        assertNull(core.decide(done, SessionEvent.ClaimRequested(binding(), game)).command); assertNotNull(core.decide(done, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
        assertFalse(core.isStateValid(done.copy(claimUse = SessionClaimUse.CONSUMED)))
        assertEquals(done, step(done, SessionEvent.ClaimCompleted(old.correlation, SessionTransitionResult.Durable(receipt(old, pending, o, SessionOperation.CLAIM)))))
    }
    @Test fun everyValidButDifferentBindingIsRejectedWithoutCommands() {
        val s = claiming(); val b = binding(); val good = receipt(s.pending!!, s.lease!!, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)
        for (other in listOf(b.copy(descriptorId = id(40), descriptorPath = "sessions/${id(40)}/descriptor.json"), b.copy(descriptorSha256 = hash(40)), b.copy(leaseId = id(40)), b.copy(tokenSha256 = hash(40)), b.copy(sessionSequence = 0), b.copy(registryGenerationId = id(40)), b.copy(registrySha256 = hash(40)), b.copy(launcherOwner = launcher.copy(pid = 40)))) {
            val wrong = good.copy(child = lease(other, SessionLeasePhase.GAME_OWNED))
            val done = step(s, SessionEvent.ClaimCompleted(s.pending.correlation, SessionTransitionResult.Durable(wrong))); assertEquals(s.lease, done.lease); assertFalse(core.sessionWritesEligible(done))
            assertEquals(s, step(s, SessionEvent.ClaimCompleted(s.pending.correlation.copy(binding = other), SessionTransitionResult.Durable(good))))
            assertNull(core.decide(bound(), SessionEvent.ClaimRequested(other, game)).command); assertNull(core.decide(bound(), SessionEvent.CloseRequested(other, "GAME_EXIT")).command)
        }
    }
    @Test fun closeReceiptRejectsWrongReasonParentOwnerAndBarrierAndLateClaimCannotRevive() {
        val s = step(owned(), SessionEvent.CloseRequested(binding(), "GAME_EXIT")); val r = receipt(s.pending!!, s.lease!!, lease(phase = SessionLeasePhase.CLOSED, ownedClose = true), SessionOperation.CLOSE)
        for (bad in listOf(r.copy(parent = lease()), r.copy(child = r.child.copy(document = r.child.document.copy(closeReason = "OTHER"))),
            r.copy(child = r.child.copy(document = r.child.document.copy(gameOwner = game.copy(processStartToken = "9")))),
            r.copy(barrier = SessionBarrier.ActiveInstalled(r.child.head)), r.copy(barrier = SessionBarrier.ActiveRemoved(r.child.head)), r.copy(child = r.child.copy(head = r.child.head.copy(transitionSequence = 1))))) {
            val done = step(s, SessionEvent.CloseCompleted(s.pending.correlation, SessionTransitionResult.Durable(bad))); assertEquals(s, done); assertNull(done.closedReceipt); assertFalse(core.sessionWritesEligible(done)); assertNull(core.decide(done, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command); assertEquals(r, step(done, SessionEvent.CloseCompleted(s.pending.correlation, SessionTransitionResult.Durable(r))).closedReceipt)
        }
        val closed = step(s, SessionEvent.CloseCompleted(s.pending.correlation, SessionTransitionResult.Durable(r)))
        assertEquals(closed, step(closed, SessionEvent.ClaimCompleted(s.claimReceipt!!.correlation, SessionTransitionResult.Durable(s.claimReceipt))))
        assertEquals(closed, step(closed, SessionEvent.CloseCompleted(s.pending.correlation, SessionTransitionResult.Durable(r))))
    }
    @Test fun missingContradictoryWrongScopeAndPidReuseEvidenceRemainUnknown() {
        val s = step(bound(), SessionEvent.RecoveryRequested); var c = s.pending!!.correlation; var p = s.lease!!
        val good = SessionRecoveryResult.RetainedOpenLease(p, SessionBarrier.ActiveInstalled(p.head), owner(c, p, SessionLiveness.ALIVE), target(c, p, SessionPresence.ABSENT))
        for (bad in listOf(good.copy(owner = null), good.copy(target = null), good.copy(owner = good.owner!!.copy(aliveOwner = launcher.copy(processStartToken = "0"))),
            good.copy(owner = good.owner.copy(expectedOwner = game)), good.copy(owner = good.owner.copy(correlation = c.copy(ordinal = 99))),
            good.copy(target = good.target!!.copy(presentOwner = game)), good.copy(target = good.target.copy(binding = binding().copy(sessionSequence = 0))),
            good.copy(target = good.target.copy(target = SessionTargetProcess("bad", "bad"))), good.copy(barrier = null))) {
            val done = step(s, SessionEvent.RecoveryCompleted(c, bad)); assertEquals(s, done); assertEquals(SessionMutationGate.UNKNOWN, done.gate); assertFalse(core.sessionWritesEligible(done)); assertNull(core.decide(done, SessionEvent.RecoveryRequested).command); assertEquals(SessionMutationGate.ACTIVE, step(done, SessionEvent.RecoveryCompleted(c, good)).gate)
        }
        val noOpen = step(s, SessionEvent.RecoveryCompleted(c, SessionRecoveryResult.NoOpenLease(profile(c)))); assertEquals(SessionMutationGate.UNKNOWN, noOpen.gate)
        val o = step(owned(), SessionEvent.RecoveryRequested); c = o.pending!!.correlation; p = o.lease!!
        val extraneousTarget = SessionRecoveryResult.RetainedOpenLease(p, SessionBarrier.ActiveInstalled(p.head), owner(c, p, SessionLiveness.ALIVE), target(c, p, SessionPresence.PRESENT))
        assertEquals(SessionMutationGate.UNKNOWN, step(o, SessionEvent.RecoveryCompleted(c, extraneousTarget)).gate)
    }
    @Test fun canonicalBoundsAreExactAndOutputsAreImmutableValues() {
        val before = bound(); val decision = core.decide(before, SessionEvent.ClaimRequested(binding(), game)); assertNull(before.pending); assertEquals(0L, before.ordinal)
        val command = decision.command as SessionCommand.ClaimExistingBinding; assertEquals(binding(), command.binding); assertEquals(game, command.owner); assertEquals(command, decision.state!!.pending)
        val changed = command.copy(owner = game.copy(pid = 1)); assertNotEquals(changed, decision.command); assertEquals(game, command.owner)
        for (token in listOf("", "01", "-1", "+1", " 1", "1\n", "١", "100000000000000000000")) assertNull(core.decide(before, SessionEvent.ClaimRequested(binding(), game.copy(processStartToken = token))).command)
        assertNotNull(core.decide(before, SessionEvent.ClaimRequested(binding(), SessionProcessIdentity(Int.MAX_VALUE, Int.MAX_VALUE, "0"))).command)
        for (r in listOf("", "lower", "A\n", "A".repeat(129))) assertNull(core.decide(before, SessionEvent.CloseRequested(binding(), r)).command)
        assertNotNull(core.decide(before, SessionEvent.CloseRequested(binding(), "A".repeat(128))).command)
    }
    @Test fun nullAndUndefinedCompletionValuesAreTotalAndNeverAuthorize() {
        val s = claiming(); val c = s.pending!!.correlation; val r = receipt(s.pending, s.lease!!, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)
        for (bad in listOf(null, SessionTransitionResult.Durable(null))) assertFalse(core.sessionWritesEligible(step(s, SessionEvent.ClaimCompleted(c, bad))))
        assertEquals(s, step(s, SessionEvent.ClaimCompleted(null, SessionTransitionResult.Durable(r))))
        val recovery = step(bound(), SessionEvent.RecoveryRequested); assertEquals(SessionMutationGate.UNKNOWN, step(recovery, SessionEvent.RecoveryCompleted(recovery.pending!!.correlation, null)).gate)
    }
    @Test fun recoveryIsSerializedStaleCallbacksCannotReplaceNewAttempt() {
        var s = step(owned(), SessionEvent.RecoveryRequested); val old = s.pending!!.correlation
        assertNull(core.decide(s, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command); assertNull(core.decide(s, SessionEvent.RecoveryRequested).command)
        s = step(s, SessionEvent.RecoveryCompleted(old, SessionRecoveryResult.Deferred(SessionFailure.DURABILITY_UNAVAILABLE))); s = step(s, SessionEvent.RecoveryRequested)
        assertEquals(s, step(s, SessionEvent.RecoveryCompleted(old, SessionRecoveryResult.NoOpenLease(profile(old))))); assertFalse(core.sessionWritesEligible(s))
    }
    @Test fun malformedClaimCompletionsPreservePendingUntilExactValidCompletion() {
        val s = claiming(); val c = s.pending!!.correlation; val good = receipt(s.pending, s.lease!!, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)
        for (bad in listOf(null, SessionTransitionResult.Durable(null), SessionTransitionResult.Durable(good.copy(barrier = null)),
            SessionTransitionResult.Durable(good.copy(barrier = SessionBarrier.ActiveInstalled(good.parent.head))),
            SessionTransitionResult.Durable(good.copy(child = good.child.copy(head = good.child.head.copy(sha256 = hash(90))))),
            SessionTransitionResult.Durable(good.copy(child = lease(binding().copy(tokenSha256 = hash(90)), SessionLeasePhase.GAME_OWNED))),
            SessionTransitionResult.Durable(good.copy(parent = good.parent.copy(document = good.parent.document.copy(transitionId = id(90))))))) {
            val ignored = core.decide(s, SessionEvent.ClaimCompleted(c, bad)); assertEquals(s, ignored.state); assertNull(ignored.command); assertNull(ignored.completedClose)
            assertNull(core.decide(ignored.state, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
            val done = step(ignored.state!!, SessionEvent.ClaimCompleted(c, SessionTransitionResult.Durable(good))); assertTrue(core.sessionWritesEligible(done)); assertNull(done.pending)
        }
        val failed = step(s, SessionEvent.ClaimCompleted(c, SessionTransitionResult.Failed(SessionFailure.LIFECYCLE_BLOCKED)))
        assertNull(failed.pending); assertEquals(SessionClaimUse.REJECTED, failed.claimUse); assertNotNull(core.decide(failed, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
    }
    @Test fun malformedCloseCompletionsPreservePendingAndExactReasonUntilValidCompletion() {
        val s = step(owned(), SessionEvent.CloseRequested(binding(), "GAME_EXIT")); val c = s.pending!!.correlation
        val good = receipt(s.pending, s.lease!!, lease(phase = SessionLeasePhase.CLOSED, ownedClose = true), SessionOperation.CLOSE)
        for (bad in listOf(null, SessionTransitionResult.Durable(null), SessionTransitionResult.Durable(good.copy(barrier = null)),
            SessionTransitionResult.Durable(good.copy(barrier = SessionBarrier.ActiveRemoved(good.child.head))),
            SessionTransitionResult.Durable(good.copy(child = good.child.copy(document = good.child.document.copy(closeReason = "OTHER")))),
            SessionTransitionResult.Durable(good.copy(child = good.child.copy(head = good.child.head.copy(descriptorId = id(90))))))) {
            val ignored = core.decide(s, SessionEvent.CloseCompleted(c, bad)); assertEquals(s, ignored.state); assertNull(ignored.command)
            assertNull(core.decide(ignored.state, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
            val done = step(ignored.state!!, SessionEvent.CloseCompleted(c, SessionTransitionResult.Durable(good))); assertEquals(good, done.closedReceipt); assertNull(done.pending)
        }
        val failed = step(s, SessionEvent.CloseCompleted(c, SessionTransitionResult.Failed(SessionFailure.DURABILITY_UNAVAILABLE)))
        assertNull(failed.pending); assertNotNull(core.decide(failed, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
    }
    @Test fun malformedRecoveryCompletionsPreserveSerializationUntilValidCompletion() {
        val s = step(bound(), SessionEvent.RecoveryRequested); val c = s.pending!!.correlation; val p = s.lease!!
        val good = SessionRecoveryResult.RetainedOpenLease(p, SessionBarrier.ActiveInstalled(p.head), owner(c, p, SessionLiveness.ALIVE), target(c, p, SessionPresence.ABSENT))
        for (bad in listOf(null, good.copy(owner = null), good.copy(barrier = null), good.copy(target = null), good.copy(lease = lease(binding().copy(registrySha256 = hash(90)))), SessionRecoveryResult.NoOpenLease(profile(c)))) {
            val ignored = core.decide(s, SessionEvent.RecoveryCompleted(c, bad)); assertEquals(s, ignored.state); assertNull(ignored.command)
            assertNull(core.decide(ignored.state, SessionEvent.RecoveryRequested).command)
            val done = step(ignored.state!!, SessionEvent.RecoveryCompleted(c, good)); assertEquals(SessionMutationGate.ACTIVE, done.gate); assertNull(done.pending)
        }
        val failed = step(s, SessionEvent.RecoveryCompleted(c, SessionRecoveryResult.Deferred(SessionFailure.SESSION_RECOVERY_AMBIGUOUS)))
        assertNull(failed.pending); assertEquals(SessionMutationGate.UNKNOWN, failed.gate); assertNotNull(core.decide(failed, SessionEvent.RecoveryRequested).command)
    }
    @Test fun recoveryOwnedObservationCannotPrecedeItsSuccessfulClaimReceipt() {
        val s = step(owned(), SessionEvent.RecoveryRequested); val c = s.pending!!.correlation
        val good = SessionRecoveryResult.RetainedOpenLease(s.lease!!, SessionBarrier.ActiveInstalled(s.lease.head), owner(c, s.lease, SessionLiveness.ALIVE), null)
        var done = step(s, SessionEvent.RecoveryCompleted(c, good)); assertTrue(core.isStateValid(done)); assertFalse(core.sessionWritesEligible(done))
        for (ordinal in listOf(2L, 3L, Long.MAX_VALUE)) {
            val forged = done.copy(claimReceipt = done.claimReceipt!!.copy(correlation = done.claimReceipt!!.correlation.copy(ordinal = ordinal)), ordinal = ordinal)
            assertFalse(core.isStateValid(forged)); assertFalse(core.sessionWritesEligible(forged)); assertNull(core.decide(forged, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
        }
        done = step(done, SessionEvent.RecoveryRequested); done = step(done, SessionEvent.RecoveryCompleted(done.pending!!.correlation, SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)))
        val afterDeferred = done.copy(claimReceipt = done.claimReceipt!!.copy(correlation = done.claimReceipt!!.correlation.copy(ordinal = 4)), ordinal = 4)
        assertFalse(core.isStateValid(afterDeferred)); assertFalse(core.sessionWritesEligible(afterDeferred))
    }
    @Test fun exactPendingRecoveryAllowsLaterActualClaimAndPreservesOneUseAcrossRecovery() {
        var s = claiming(); s = step(s, SessionEvent.ClaimCompleted(s.pending!!.correlation, SessionTransitionResult.Failed(SessionFailure.INDETERMINATE)))
        s = step(s, SessionEvent.RecoveryRequested); var c = s.pending!!.correlation; val p = s.lease!!
        val pending = SessionRecoveryResult.RetainedOpenLease(p, SessionBarrier.ActiveInstalled(p.head), owner(c, p, SessionLiveness.ALIVE), target(c, p, SessionPresence.UNKNOWN))
        s = step(s, SessionEvent.RecoveryCompleted(c, pending)); s = claiming(s); val r = receipt(s.pending!!, p, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)
        val o = step(s, SessionEvent.ClaimCompleted(s.pending!!.correlation, SessionTransitionResult.Durable(r))); assertTrue(core.isStateValid(o)); assertTrue(core.sessionWritesEligible(o)); assertEquals(3L, o.claimReceipt!!.correlation.ordinal)
        val recovery = step(o, SessionEvent.RecoveryRequested); c = recovery.pending!!.correlation
        val retained = SessionRecoveryResult.RetainedOpenLease(o.lease!!, SessionBarrier.ActiveInstalled(o.lease.head), owner(c, o.lease, SessionLiveness.ALIVE), null)
        val blocked = step(recovery, SessionEvent.RecoveryCompleted(c, retained)); assertFalse(core.sessionWritesEligible(blocked)); assertNull(core.decide(blocked, SessionEvent.ClaimRequested(binding(), game)).command); assertNotNull(core.decide(blocked, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command)
    }
    @Test fun coordinatedClaimAuditShiftAndMissingFenceCannotReviveAfterRepeatedDeferredRecovery() {
        val s = step(owned(), SessionEvent.RecoveryRequested); val c = s.pending!!.correlation
        val retained = SessionRecoveryResult.RetainedOpenLease(s.lease!!, SessionBarrier.ActiveInstalled(s.lease.head), owner(c, s.lease, SessionLiveness.ALIVE), null)
        var done = step(s, SessionEvent.RecoveryCompleted(c, retained)); assertNotNull(done.claimAttempt); assertNotNull(done.claimRecoveryFence)
        val fence = done.claimRecoveryFence!!
        repeat(4) {
            val future = done.ordinal + 1
            val shifted = done.copy(ordinal = future, claimReceipt = done.claimReceipt!!.copy(correlation = done.claimReceipt!!.correlation.copy(ordinal = future)), claimAttempt = done.claimAttempt!!.copy(correlation = done.claimAttempt!!.correlation.copy(ordinal = future)))
            for (malformed in listOf(shifted, done.copy(claimRecoveryFence = null), done.copy(claimRecoveryFence = fence.copy(recoveryCorrelation = fence.recoveryCorrelation.copy(ordinal = 1))))) {
                assertFalse(core.isStateValid(malformed)); assertFalse(core.sessionWritesEligible(malformed))
            }
            done = step(done, SessionEvent.RecoveryRequested); done = step(done, SessionEvent.RecoveryCompleted(done.pending!!.correlation, SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)))
            assertEquals(fence, done.claimRecoveryFence); assertFalse(core.sessionWritesEligible(done)); assertTrue(core.isStateValid(done))
        }
    }
    @Test fun recoveryObservationChronologyRejectsPendingAfterClaimAndNoOpenBeforeClose() {
        var s = step(bound(), SessionEvent.RecoveryRequested); var c = s.pending!!.correlation; val p = s.lease!!
        val retained = SessionRecoveryResult.RetainedOpenLease(p, SessionBarrier.ActiveInstalled(p.head), owner(c, p, SessionLiveness.ALIVE), target(c, p, SessionPresence.ABSENT))
        s = step(s, SessionEvent.RecoveryCompleted(c, retained)); s = claiming(s); val r = receipt(s.pending!!, p, lease(phase = SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM)
        val o = step(s, SessionEvent.ClaimCompleted(s.pending!!.correlation, SessionTransitionResult.Durable(r))); assertTrue(core.sessionWritesEligible(o))
        val future = c.copy(ordinal = 3); val shifted = retained.copy(owner = retained.owner!!.copy(correlation = future), target = retained.target!!.copy(correlation = future))
        val forged = o.copy(recovery = SessionRecoveryRecord(future, shifted), recoveryHighWater = 3, ordinal = 3, claimRecoveryFence = SessionClaimRecoveryFence(future, r)); assertFalse(core.isStateValid(forged))
        val closing = step(owned(), SessionEvent.CloseRequested(binding(), "GAME_EXIT")); val close = receipt(closing.pending!!, closing.lease!!, lease(phase = SessionLeasePhase.CLOSED, ownedClose = true), SessionOperation.CLOSE)
        var closed = step(closing, SessionEvent.CloseCompleted(closing.pending.correlation, SessionTransitionResult.Durable(close))); closed = step(closed, SessionEvent.RecoveryRequested); c = closed.pending!!.correlation
        val clear = step(closed, SessionEvent.RecoveryCompleted(c, SessionRecoveryResult.NoOpenLease(profile(c)))); assertTrue(core.isStateValid(clear))
        assertFalse(core.isStateValid(clear.copy(closedReceipt = close.copy(correlation = close.correlation.copy(ordinal = 4)), ordinal = 4)))
    }
    @Test fun observedOwnedFenceNeverBecomesSuccessfulBridgeConsumptionAfterDeferred() {
        var s = claiming(); s = step(s, SessionEvent.ClaimCompleted(s.pending!!.correlation, SessionTransitionResult.Failed(SessionFailure.INDETERMINATE)))
        s = step(s, SessionEvent.RecoveryRequested); val c = s.pending!!.correlation; val o = lease(phase = SessionLeasePhase.GAME_OWNED); val observed = receipt(s.pending!!, s.lease!!, o, SessionOperation.CLAIM)
        val result = SessionRecoveryResult.RetainedOpenLease(o, SessionBarrier.ActiveInstalled(o.head), owner(c, o, SessionLiveness.ALIVE), null, observed)
        var done = step(s, SessionEvent.RecoveryCompleted(c, result)); assertEquals(SessionClaimUse.REJECTED, done.claimUse); assertNotNull(done.claimRecoveryFence)
        done = step(done, SessionEvent.RecoveryRequested); done = step(done, SessionEvent.RecoveryCompleted(done.pending!!.correlation, SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)))
        val minted = done.copy(claimUse = SessionClaimUse.CONSUMED, claimAttempt = done.claimAttempt!!.copy(correlation = observed.correlation))
        assertFalse(core.isStateValid(minted)); assertFalse(core.sessionWritesEligible(minted)); assertFalse(core.isStateValid(done.copy(claimRecoveryFence = null)))
    }
    private fun failedClosing(o: Boolean, code: SessionFailure, reason: String = "GAME_EXIT"): SessionState {
        val s = step(if (o) owned() else bound(), SessionEvent.CloseRequested(binding(), reason))
        return step(s, SessionEvent.CloseCompleted(s.pending!!.correlation, SessionTransitionResult.Failed(code)))
    }
    private fun recoveryClose(s: SessionState): SessionRecoveryResult.RecoveredClosed {
        val p = s.lease!!; val c = s.pending!!.correlation; val o = p.document.state == SessionLeasePhase.GAME_OWNED
        val reason = if (o) "RECOVERY_GAME_OWNER_DEAD" else "RECOVERY_LAUNCHER_DEAD"
        return SessionRecoveryResult.RecoveredClosed(p, receipt(s.pending, p, lease(phase = SessionLeasePhase.CLOSED, ownedClose = o, reason = reason), SessionOperation.CLOSE),
            owner(c, p, SessionLiveness.DEAD), if (o) null else target(c, p, SessionPresence.ABSENT), profile(c))
    }
    @Test fun failedExplicitCloseAllowsIndependentRecoveryClosureForPendingAndOwned() {
        for (o in listOf(false, true)) for (code in listOf(SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE)) {
            val failed = failedClosing(o, code); assertEquals("GAME_EXIT", failed.pinnedCloseReason)
            assertNotNull(core.decide(failed, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).command); assertNull(core.decide(failed, SessionEvent.CloseRequested(binding(), "OTHER")).command)
            val recovering = step(failed, SessionEvent.RecoveryRequested); val good = recoveryClose(recovering)
            val decision = core.decide(recovering, SessionEvent.RecoveryCompleted(recovering.pending!!.correlation, good)); val done = decision.state!!
            assertEquals("RECOVERED", decision.diagnosis); assertEquals(good.receipt!!.child, done.lease); assertEquals(SessionMutationGate.CLEAR, done.gate)
            assertNull(done.pending); assertTrue(core.isStateValid(done)); assertFalse(core.sessionWritesEligible(done)); assertEquals("GAME_EXIT", done.pinnedCloseReason); assertEquals(failed.closeAttempt, done.closeAttempt)
            for (reason in listOf("GAME_EXIT", good.receipt.child.document.closeReason)) {
                val terminal = core.decide(done, SessionEvent.CloseRequested(binding(), reason)); assertEquals("REQUEST_REJECTED", terminal.diagnosis); assertNull(terminal.command); assertNull(terminal.completedClose)
            }
        }
    }
    @Test fun recoveryAfterFailedCloseRejectsWrongAuthorityThenAcceptsExactCompletion() {
        for (o in listOf(false, true)) for (code in listOf(SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE)) {
            val s = step(failedClosing(o, code), SessionEvent.RecoveryRequested); val c = s.pending!!.correlation; val good = recoveryClose(s); val r = good.receipt!!
            for (bad in listOf(good.copy(receipt = r.copy(child = r.child.copy(document = r.child.document.copy(closeReason = "GAME_EXIT")))),
                good.copy(receipt = r.copy(child = r.child.copy(document = r.child.document.copy(closeReason = if (o) "RECOVERY_LAUNCHER_DEAD" else "RECOVERY_GAME_OWNER_DEAD")))),
                good.copy(owner = null), good.copy(owner = owner(c, s.lease!!, SessionLiveness.UNKNOWN)), good.copy(profile = null),
                good.copy(receipt = r.copy(barrier = null)), good.copy(receipt = r.copy(barrier = SessionBarrier.ActiveRemoved(r.child.head))),
                good.copy(parent = lease(binding().copy(registrySha256 = hash(90)))), good.copy(receipt = r.copy(parent = r.parent.copy(head = r.parent.head.copy(sha256 = hash(90))))))) {
                val rejected = core.decide(s, SessionEvent.RecoveryCompleted(c, bad)); assertEquals(s, rejected.state); assertNull(rejected.command); assertNull(rejected.completedClose)
                val done = step(rejected.state!!, SessionEvent.RecoveryCompleted(c, good)); assertEquals(good.receipt.child, done.lease); assertNull(done.pending); assertTrue(core.isStateValid(done))
            }
        }
    }
    @Test fun explicitCloseRetryStillRequiresItsOwnPinnedReasonAndCachesOnlyItsSuccess() {
        for (o in listOf(false, true)) for (code in listOf(SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE)) {
            val failed = failedClosing(o, code); val s = step(failed, SessionEvent.CloseRequested(binding(), "GAME_EXIT")); val c = s.pending!!.correlation
            val good = receipt(s.pending, s.lease!!, lease(phase = SessionLeasePhase.CLOSED, ownedClose = o), SessionOperation.CLOSE)
            val wrong = good.copy(child = good.child.copy(document = good.child.document.copy(closeReason = if (o) "RECOVERY_GAME_OWNER_DEAD" else "RECOVERY_LAUNCHER_DEAD")))
            assertEquals(s, step(s, SessionEvent.CloseCompleted(c, SessionTransitionResult.Durable(wrong))))
            val done = step(s, SessionEvent.CloseCompleted(c, SessionTransitionResult.Durable(good))); assertTrue(core.isStateValid(done)); assertNull(done.recoveryClosure)
            assertEquals(good, core.decide(done, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).completedClose); assertNull(core.decide(done, SessionEvent.CloseRequested(binding(), wrong.child.document.closeReason)).completedClose)
            assertNotNull(done.closeAttempt); assertEquals(c, done.closeAttempt!!.correlation); assertEquals("GAME_EXIT", done.closeAttempt.reason)
        }
    }
    @Test fun independentRecoveryClosureRetainsExactExplicitPinAndTerminalAuditAcrossLaterRecovery() {
        for (o in listOf(false, true)) for (explicitReason in listOf("GAME_EXIT", if (o) "RECOVERY_GAME_OWNER_DEAD" else "RECOVERY_LAUNCHER_DEAD")) {
            val failed = failedClosing(o, SessionFailure.INDETERMINATE, explicitReason); val s = step(failed, SessionEvent.RecoveryRequested); val c = s.pending!!.correlation; val good = recoveryClose(s)
            var done = step(s, SessionEvent.RecoveryCompleted(c, good)); assertNotNull(done.recoveryClosure); assertEquals(SessionRecoveryRecord(c, good), done.recoveryClosure); assertNotNull(done.closeAttempt)
            val audit = done.recoveryClosure!!; val attempt = done.closeAttempt!!
            repeat(3) { n ->
                done = step(done, SessionEvent.RecoveryRequested); val next = done.pending!!.correlation
                done = step(done, SessionEvent.RecoveryCompleted(next, if (n == 0) SessionRecoveryResult.NoOpenLease(profile(next)) else SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)))
                assertTrue(core.isStateValid(done)); assertEquals(audit, done.recoveryClosure); assertEquals(attempt, done.closeAttempt); assertEquals(explicitReason, done.pinnedCloseReason); assertFalse(core.sessionWritesEligible(done))
                for (bad in listOf(done.copy(recoveryClosure = null), done.copy(pinnedCloseReason = "OTHER"), done.copy(closeAttempt = null),
                    done.copy(closeAttempt = attempt.copy(reason = "OTHER")), done.copy(closeAttempt = attempt.copy(correlation = c)),
                    done.copy(recoveryClosure = audit.copy(result = good.copy(owner = null))), done.copy(recoveryClosure = audit.copy(result = good.copy(profile = null))),
                    done.copy(recoveryClosure = audit.copy(result = good.copy(receipt = good.receipt!!.copy(barrier = null)))),
                    done.copy(recoveryClosure = audit.copy(correlation = c.copy(ordinal = c.ordinal + 1))))) {
                    assertFalse(core.isStateValid(bad)); assertFalse(core.sessionWritesEligible(bad)); assertNull(core.decide(bad, SessionEvent.CloseRequested(binding(), "GAME_EXIT")).completedClose)
                }
            }
        }
    }
    @Test fun recoveryWithoutExplicitCloseNeverMintsAnExplicitPinOrCachedCloseResponse() {
        for (o in listOf(false, true)) {
            val s = step(if (o) owned() else bound(), SessionEvent.RecoveryRequested); val good = recoveryClose(s)
            val done = step(s, SessionEvent.RecoveryCompleted(s.pending!!.correlation, good)); assertEquals(SessionMutationGate.CLEAR, done.gate); assertNull(done.pending)
            assertNull(done.pinnedCloseReason); assertNull(done.closeAttempt); assertNotNull(done.recoveryClosure); assertTrue(core.isStateValid(done))
            val request = core.decide(done, SessionEvent.CloseRequested(binding(), good.receipt!!.child.document.closeReason)); assertEquals("REQUEST_REJECTED", request.diagnosis); assertNull(request.command); assertNull(request.completedClose)
        }
    }
    @Test fun malformedStatesNullEnumsAndOrdinalOverflowFailClosedWithoutReset() {
        val o = owned()
        for (bad in listOf(o.copy(claimReceipt = null), o.copy(lease = lease()), o.copy(gate = SessionMutationGate.CLEAR), o.copy(ordinal = -1), o.copy(pinnedOwner = null))) {
            assertFalse(core.isStateValid(bad)); assertFalse(core.sessionWritesEligible(bad)); assertNull(core.decide(bad, SessionEvent.RecoveryRequested).command)
        }
        assertNull(core.decide(null, null).command); assertFalse(core.sessionWritesEligible(null))
        val max = bound().copy(ordinal = Long.MAX_VALUE); assertNull(core.decide(max, SessionEvent.ClaimRequested(binding(), game)).command); assertEquals(Long.MAX_VALUE, core.decide(max, SessionEvent.RecoveryRequested).state!!.ordinal)
        assertEquals(o, step(o, SessionEvent.BindIssuedPending(bound().acquisition)))
    }
}
