package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test

class SkinTransactionCoreTest {
    private val core = SkinTransactionCore()
    private fun id(n: Int) = "00000000-0000-0000-0000-${n.toString().padStart(12, '0')}"
    private fun hash(c: Char) = c.toString().repeat(64)
    private val binding = SkinBindingToken("skin/hero:1")
    private fun pack(id: String = "knight") = ActiveVisual.Pack(id, hash('a'), hash('b'), hash('c'))
    private fun envelope(fresh: Boolean = false, packPrior: Boolean = false): TransactionEnvelope {
        val prior = ActivationSnapshot(if (packPrior) SkinMode.ON else SkinMode.OFF, "knight",
            if (packPrior) pack() else ActiveVisual.Vanilla, 7)
        val target = if (fresh && packPrior) prior.copy(skinStamp = 8) else ActivationSnapshot(SkinMode.ON, "knight", pack(), 8)
        return TransactionEnvelope(id(1), if (fresh && packPrior) SkinOperationKind.REBIND_APPLY else SkinOperationKind.MODE_ON,
            id(2), hash('d'), prior, target, binding, !fresh)
    }
    private fun cor(e: TransactionEnvelope) = TransactionCorrelation(e.transactionId, e.binding)
    private fun idle(e: TransactionEnvelope) = TransactionState(binding = binding, activation = e.prior)
    private fun armReceipt(e: TransactionEnvelope) = RegistryCommitReceipt(e.baseGenerationId, e.baseGenerationSha256, id(3), hash('e'))
    private fun closeReceipt() = RegistryCommitReceipt(id(3), hash('e'), id(4), hash('f'))
    private fun armedLock(e: TransactionEnvelope) = RotationInterlock(InterlockState.ARMED, e.transactionId, e.operation,
        e.baseGenerationId, e.baseGenerationSha256, e.prior, e.target, e.binding, e.priorEstablishedOnBinding, null, null)
    private fun head(r: RegistryCommitReceipt, a: ActivationSnapshot, l: RotationInterlock) = VerifiedRegistryHead(r.newGenerationId, r.newGenerationSha256, a, l)
    private fun prepared(e: TransactionEnvelope) = core.decide(core.decide(idle(e), TransactionEvent.Begin(e)).state, TransactionEvent.Prepared(cor(e))).state
    private fun armed(e: TransactionEnvelope) = core.decide(prepared(e), TransactionEvent.ArmCommitted(cor(e), armReceipt(e), head(armReceipt(e), e.prior, armedLock(e)))).state
    private fun applied(e: TransactionEnvelope) = core.decide(armed(e), TransactionEvent.ApplyVerified(cor(e))).state
    private fun rolling(e: TransactionEnvelope) = core.decide(armed(e), TransactionEvent.ApplyFailed(cor(e), "APPLY_FAILED")).state
    private fun rolled(e: TransactionEnvelope) = core.decide(rolling(e), TransactionEvent.RollbackVerified(cor(e), !e.priorEstablishedOnBinding)).state
    private fun complete(s: TransactionState, e: TransactionEnvelope) = core.decide(s, TransactionEvent.CompletionCommitted(cor(e), closeReceipt(), head(closeReceipt(), requireNotNull(s.pendingClosure), RotationInterlock.clear())))
    private fun noOp(s: TransactionState, ev: TransactionEvent) {
        val d = core.decide(s, ev); assertEquals(s, d.state); assertTrue(d.commands.isEmpty())
    }

    @Test fun beginAndArmCarryCompleteTransactionEnvelope() {
        val e = envelope(); val begin = core.decide(idle(e), TransactionEvent.Begin(e))
        assertEquals(SkinCommand.Prepare(e, e.target.active), begin.commands.single())
        assertEquals(e, begin.state.envelope); assertEquals(RotationInterlock.clear(), begin.state.interlock)
        val prepared = core.decide(begin.state, TransactionEvent.Prepared(cor(e)))
        assertEquals(SkinCommand.Arm(e), prepared.commands.single())
        assertEquals(RotationInterlock.clear(), prepared.state.interlock); assertNull(prepared.state.armCommitReceipt)
    }
    @Test fun postBeginEventsRequireExactCorrelation() {
        val e = envelope(); val begun = core.decide(idle(e), TransactionEvent.Begin(e)).state
        for (c in listOf(cor(e).copy(transactionId = id(9)), cor(e).copy(binding = SkinBindingToken("other")))) {
            noOp(begun, TransactionEvent.Prepared(c)); noOp(armed(e), TransactionEvent.ApplyVerified(c))
            noOp(prepared(e), TransactionEvent.ArmCommitted(c, armReceipt(e), head(armReceipt(e), e.prior, armedLock(e))))
            noOp(armed(e), TransactionEvent.ApplyFailed(c, "FAILED")); noOp(rolling(e), TransactionEvent.RollbackFailed(c, "FAILED"))
            noOp(applied(e), TransactionEvent.CompletionRejected(c, "FAILED"))
            noOp(applied(e), TransactionEvent.CompletionCommitted(c, closeReceipt(), head(closeReceipt(), e.target, RotationInterlock.clear())))
            noOp(rolling(e), TransactionEvent.RollbackVerified(c, false)); noOp(applied(e), TransactionEvent.CompletionIndeterminate(c))
        }
    }
    @Test fun durableCasEventsCarryExactCommitReceipts() {
        val e = envelope(); val armed = armed(e)
        assertEquals(armReceipt(e), armed.armCommitReceipt); assertEquals(armedLock(e), armed.interlock); assertEquals(e.prior, armed.activation)
        val d = core.decide(armed, TransactionEvent.ApplyVerified(cor(e)))
        assertEquals(SkinCommand.Commit(cor(e), id(3), hash('e'), e.target), d.commands.single())
        assertEquals(closeReceipt(), complete(d.state, e).state.completionReceipt)
    }
    @Test fun mismatchedCorrelationOrReceiptIsStaleNoOp() {
        val e = envelope(); val r = armReceipt(e)
        for (bad in listOf(r.copy(expectedGenerationId = id(8)), r.copy(expectedGenerationSha256 = hash('a')),
            r.copy(newGenerationId = r.expectedGenerationId), r.copy(newGenerationSha256 = r.expectedGenerationSha256),
            r.copy(newGenerationId = "bad"), r.copy(newGenerationSha256 = "BAD")))
            noOp(prepared(e), TransactionEvent.ArmCommitted(cor(e), bad, head(bad, e.prior, armedLock(e))))
        val close = closeReceipt().copy(expectedGenerationSha256 = hash('a'))
        noOp(applied(e), TransactionEvent.CompletionCommitted(cor(e), close, head(close, e.target, RotationInterlock.clear())))
    }
    @Test fun armsBeforeFirstWriteAndCommitsStampOnce() {
        val e = envelope(); noOp(prepared(e), TransactionEvent.ApplyVerified(cor(e)))
        val arm = core.decide(prepared(e), TransactionEvent.ArmCommitted(cor(e), armReceipt(e), head(armReceipt(e), e.prior, armedLock(e))))
        assertEquals(SkinCommand.Apply(cor(e)), arm.commands.single())
        val applied = core.decide(arm.state, TransactionEvent.ApplyVerified(cor(e))).state
        assertEquals(e.prior, applied.activation); assertEquals(InterlockState.ARMED, applied.interlock.state)
        val done = complete(applied, e)
        assertEquals(TransactionPhase.COMMITTED, done.state.phase); assertEquals(e.target, done.state.activation)
        assertEquals(8L, done.state.activation!!.skinStamp); assertEquals(RotationInterlock.clear(), done.state.interlock)
        assertTrue(done.commands.isEmpty()); noOp(done.state, TransactionEvent.ApplyVerified(cor(e)))
    }
    @Test fun rollbackVerificationAloneCannotClearArmedOrPublishStamp() {
        for (e in listOf(envelope(), envelope(true, true), envelope(true),
            envelope().copy(operation = SkinOperationKind.DEATH_ROTATION,
                prior = ActivationSnapshot(SkinMode.ROTATE, "other", pack("other"), 7),
                target = envelope().target.copy(mode = SkinMode.ROTATE)))) {
            val rolling = rolling(e)
            noOp(armed(e), TransactionEvent.RollbackVerified(cor(e), !e.priorEstablishedOnBinding))
            noOp(rolling, TransactionEvent.RollbackVerified(cor(e), e.priorEstablishedOnBinding))
            val rolled = core.decide(rolling, TransactionEvent.RollbackVerified(cor(e), !e.priorEstablishedOnBinding))
            val expected = if (!e.priorEstablishedOnBinding && e.prior.active is ActiveVisual.Pack) e.prior.copy(skinStamp = 8) else e.prior
            assertEquals(TransactionPhase.ROLLED_BACK, rolled.state.phase); assertEquals(e.prior, rolled.state.activation)
            assertEquals(armedLock(e), rolled.state.interlock); assertEquals(expected, rolled.state.pendingClosure)
            assertEquals(SkinCommand.Commit(cor(e), id(3), hash('e'), expected), rolled.commands.single())
            assertEquals(expected, complete(rolled.state, e).state.activation)
            noOp(rolled.state, TransactionEvent.RollbackVerified(cor(e), !e.priorEstablishedOnBinding))
        }
    }
    @Test fun rollsBackReverseAndLeavesIndeterminateBlocked() {
        val e = envelope(); val d = core.decide(armed(e), TransactionEvent.ApplyFailed(cor(e), "APPLY_FAILED"))
        assertEquals(SkinCommand.Rollback(cor(e)), d.commands.single()); assertEquals("APPLY_FAILED", d.state.originalFailure)
        val blocked = core.decide(rolled(e), TransactionEvent.CompletionIndeterminate(cor(e)))
        assertEquals(TransactionPhase.BLOCKED, blocked.state.phase); assertEquals(armedLock(e), blocked.state.interlock)
        assertEquals(e.prior, blocked.state.activation); assertTrue(blocked.commands.isEmpty())
        noOp(blocked.state, TransactionEvent.RollbackVerified(cor(e), false))
    }
    @Test fun rejectsWrongVerifiedHeadInterlockAndClosure() {
        val e = envelope(); val r = armReceipt(e); val h = head(r, e.prior, armedLock(e))
        for (bad in listOf(h.copy(generationId = id(9)), h.copy(generationSha256 = hash('a')), h.copy(activation = e.target),
            h.copy(interlock = RotationInterlock.clear()), h.copy(interlock = armedLock(e).copy(priorEstablishedOnBinding = false)),
            h.copy(activation = e.prior.copy(selectedPackId = null))))
            noOp(prepared(e), TransactionEvent.ArmCommitted(cor(e), r, bad))
        val cr = closeReceipt(); val ch = head(cr, e.target, RotationInterlock.clear())
        for (bad in listOf(ch.copy(generationId = id(9)), ch.copy(generationSha256 = hash('a')), ch.copy(interlock = armedLock(e)),
            ch.copy(activation = e.prior), ch.copy(activation = e.target.copy(active = pack().copy(importReceiptSha256 = hash('d'))))))
            noOp(applied(e), TransactionEvent.CompletionCommitted(cor(e), cr, bad))
    }
    @Test fun rejectedTargetCompletionRequestsRollback() {
        val e = envelope(); val d = core.decide(applied(e), TransactionEvent.CompletionRejected(cor(e), "CAS_REJECTED"))
        assertEquals(SkinCommand.Rollback(cor(e)), d.commands.single()); assertNull(d.state.pendingClosure)
        assertEquals(TransactionPhase.ROLLBACK_PENDING, d.state.phase); assertEquals(e.prior, d.state.activation)
    }
    @Test fun rejectedRollbackClosureBlocks() {
        val e = envelope(); val d = core.decide(rolled(e), TransactionEvent.CompletionRejected(cor(e), "CAS_REJECTED"))
        assertEquals(TransactionPhase.BLOCKED, d.state.phase); assertTrue(d.commands.isEmpty())
        assertEquals(armedLock(e), d.state.interlock); assertEquals(e.prior, d.state.activation)
        assertEquals("APPLY_FAILED", d.state.originalFailure); assertEquals("CAS_REJECTED", d.state.rollbackFailure)
    }
    @Test fun indeterminateTargetCompletionDoesNotRollback() {
        val e = envelope(); val d = core.decide(applied(e), TransactionEvent.CompletionIndeterminate(cor(e)))
        assertEquals(TransactionPhase.BLOCKED, d.state.phase); assertTrue(d.commands.isEmpty())
        assertEquals(e.prior, d.state.activation); assertEquals(armedLock(e), d.state.interlock); assertNull(d.state.completionReceipt)
        noOp(d.state, TransactionEvent.CompletionRejected(cor(e), "CAS_REJECTED"))
        noOp(d.state, TransactionEvent.CompletionCommitted(cor(e), closeReceipt(), head(closeReceipt(), e.target, RotationInterlock.clear())))
    }
    @Test fun failurePersistenceCannotBeInvented() {
        val e = envelope(); val r = closeReceipt()
        val failedLock = armedLock(e).copy(state = InterlockState.ROLLBACK_FAILED, originalFailure = "APPLY_FAILED", rollbackFailure = "RESTORE_FAILED")
        for (evidence in listOf(null, head(r, e.prior, armedLock(e)), head(r, e.target, failedLock), head(r, e.prior, failedLock).copy(generationId = id(8)))) {
            val d = core.decide(rolling(e), TransactionEvent.RollbackFailed(cor(e), "RESTORE_FAILED", r, evidence))
            assertEquals(TransactionPhase.BLOCKED, d.state.phase); assertEquals(armedLock(e), d.state.interlock)
            assertEquals("APPLY_FAILED", d.state.originalFailure); assertEquals("RESTORE_FAILED", d.state.rollbackFailure); assertNull(d.state.failureReceipt)
        }
        val valid = core.decide(rolling(e), TransactionEvent.RollbackFailed(cor(e), "RESTORE_FAILED", r, head(r, e.prior, failedLock)))
        assertEquals(failedLock, valid.state.interlock); assertEquals(r, valid.state.failureReceipt); assertTrue(valid.commands.isEmpty())
        assertEquals(armedLock(e), core.decide(rolling(e), TransactionEvent.RollbackFailed(cor(e), "RESTORE_FAILED")).state.interlock)
    }
    @Test fun invalidStateAndOverflowStayFailClosed() {
        val e = envelope()
        for (bad in listOf(e.copy(transactionId = "bad"), e.copy(binding = SkinBindingToken(" ")),
            e.copy(target = e.target.copy(skinStamp = -1)), e.copy(prior = e.prior.copy(skinStamp = Long.MAX_VALUE), target = e.target.copy(skinStamp = Long.MIN_VALUE)),
            e.copy(target = e.target.copy(selectedPackId = "other")), e.copy(target = e.target.copy(active = ActiveVisual.Vanilla)),
            e.copy(operation = SkinOperationKind.MODE_OFF), e.copy(target = e.target.copy(skinStamp = 9))))
            noOp(idle(bad), TransactionEvent.Begin(bad))
        for (bad in listOf(idle(e).copy(interlock = armedLock(e)), armed(e).copy(armCommitReceipt = null),
            armed(e).copy(binding = SkinBindingToken("other")), armed(e).copy(phase = TransactionPhase.ROLLED_BACK),
            applied(e).copy(pendingClosure = e.prior), prepared(e).copy(activation = e.target)))
            noOp(bad, TransactionEvent.ApplyVerified(cor(e)))
        noOp(armed(e), TransactionEvent.ApplyFailed(cor(e), "bad code"))
    }
    @Test fun operationSpecificSnapshotsAndVisualEqualityAreQualified() {
        val e = envelope(); val packPrior = e.target.copy(skinStamp = 7)
        val off = e.copy(operation = SkinOperationKind.MODE_OFF, prior = packPrior.copy(mode = SkinMode.ROTATE), target = e.prior.copy(skinStamp = 8))
        val rotate = e.copy(operation = SkinOperationKind.DEATH_ROTATION, prior = packPrior.copy(mode = SkinMode.ROTATE),
            target = e.target.copy(mode = SkinMode.ROTATE, selectedPackId = "other", active = pack("other")))
        val startup = envelope(true, true).copy(operation = SkinOperationKind.STARTUP_APPLY)
        val rebindSelected = startup.copy(operation = SkinOperationKind.REBIND_APPLY,
            prior = packPrior.copy(selectedPackId = "other"), target = e.target.copy(selectedPackId = "other", active = pack("other")))
        val rotateRebind = startup.copy(operation = SkinOperationKind.REBIND_APPLY,
            prior = packPrior.copy(mode = SkinMode.ROTATE, selectedPackId = "other"),
            target = e.target.copy(mode = SkinMode.ROTATE, selectedPackId = "other"))
        for (valid in listOf(off, rotate, startup, envelope(true, true), rebindSelected, rotateRebind,
            rebindSelected.copy(operation = SkinOperationKind.STARTUP_APPLY), rotate.copy(priorEstablishedOnBinding = false),
            e.copy(prior = e.prior.copy(active = pack()), priorEstablishedOnBinding = false))) {
            assertEquals(valid.target, complete(applied(valid), valid).state.activation)
            assertEquals(valid.prior, armed(valid).activation)
        }
        for (bad in listOf(rotate.copy(target = rotate.target.copy(selectedPackId = "knight")),
            e.copy(prior = packPrior.copy(mode = SkinMode.ROTATE, active = pack("other"))),
            e.copy(prior = e.prior.copy(selectedPackId = "other")), off.copy(prior = packPrior),
            rotateRebind.copy(target = rotateRebind.target.copy(active = pack("other"))),
            startup.copy(priorEstablishedOnBinding = true), startup.copy(target = startup.target.copy(mode = SkinMode.ROTATE)),
            off.copy(target = off.target.copy(selectedPackId = null)),
            e.copy(prior = packPrior, target = e.target.copy(active = pack().copy(importReceiptSha256 = hash('d')))), e.copy(prior = packPrior)))
            noOp(idle(bad), TransactionEvent.Begin(bad))
    }
    @Test fun modeOffDoesNotMistakeDurableHistoryForLiveVanilla() {
        val seed = envelope()
        for (active in listOf(ActiveVisual.Vanilla, pack())) {
            val prior = ActivationSnapshot(SkinMode.ROTATE, "knight", active, 7)
            val e = seed.copy(operation = SkinOperationKind.MODE_OFF, prior = prior,
                target = prior.copy(mode = SkinMode.OFF, active = ActiveVisual.Vanilla, skinStamp = 8), priorEstablishedOnBinding = false)
            assertTrue(core.decide(idle(e), TransactionEvent.Begin(e)).commands.single() is SkinCommand.Prepare)
            val applied = applied(e); assertEquals(prior, applied.activation); assertEquals(InterlockState.ARMED, applied.interlock.state)
            assertEquals(e.target, complete(applied, e).state.activation)
            assertEquals(prior.skinStamp + (if (active is ActiveVisual.Pack) 1 else 0), complete(rolled(e), e).state.activation!!.skinStamp)
            if (active is ActiveVisual.Vanilla) noOp(idle(e), TransactionEvent.Begin(e.copy(priorEstablishedOnBinding = true)))
        }
    }
    @Test fun registrySyntaxAndDecisionValuesStayBoundedAndImmutable() {
        val e = envelope()
        for (token in listOf(" skin", "skin ", " skin", "skin ", "a‮b", "\ud800", "x".repeat(257), "a${0.toChar()}b", "Ａ")) {
            val bad = e.copy(binding = SkinBindingToken(token)); val s = idle(e).copy(binding = bad.binding)
            noOp(s, TransactionEvent.Begin(bad))
        }
        for (token in listOf("skin/hero:1", "é", "😀".repeat(256))) {
            val good = e.copy(binding = SkinBindingToken(token)); val s = idle(e).copy(binding = good.binding)
            val first = core.decide(s, TransactionEvent.Begin(good))
            val second = SkinTransactionCore().decide(s.copy(), TransactionEvent.Begin(good.copy()))
            assertEquals(TransactionPhase.PREPARING, first.state.phase)
            assertEquals(first.state, second.state); assertEquals(first.commands, second.commands); assertEquals(first.diagnosis, second.diagnosis)
            assertNull(s.envelope)
        }
    }
    @Test fun illegalPhaseEventsCannotFabricateVerification() {
        val e = envelope()
        for (s in listOf(idle(e), core.decide(idle(e), TransactionEvent.Begin(e)).state, prepared(e)))
            for (ev in listOf(TransactionEvent.ApplyVerified(cor(e)), TransactionEvent.ApplyFailed(cor(e), "FAILED"),
                TransactionEvent.RollbackVerified(cor(e), false), TransactionEvent.RollbackFailed(cor(e), "FAILED"),
                TransactionEvent.CompletionRejected(cor(e), "FAILED"), TransactionEvent.CompletionIndeterminate(cor(e)),
                TransactionEvent.CompletionCommitted(cor(e), closeReceipt(), head(closeReceipt(), e.target, RotationInterlock.clear())))) noOp(s, ev)
        for (s in listOf(armed(e), applied(e), rolling(e), rolled(e))) noOp(s, TransactionEvent.Prepared(cor(e)))
        noOp(rolled(e), TransactionEvent.ApplyFailed(cor(e), "FAILED")); noOp(applied(e), TransactionEvent.RollbackFailed(cor(e), "FAILED"))
        val done = complete(rolled(e), e).state
        noOp(done, TransactionEvent.CompletionCommitted(cor(e), closeReceipt(), head(closeReceipt(), e.prior, RotationInterlock.clear())))
        noOp(done, TransactionEvent.Begin(e.copy(transactionId = id(8))))
    }
    @Test fun cannotRunSecondTransactionOrReplayReceipt() {
        val e = envelope(); val begin = core.decide(idle(e), TransactionEvent.Begin(e)).state
        // Completion is checked separately so the red baseline still exercises replay guards.
        for (s in listOf(begin, prepared(e), armed(e), applied(e), rolling(e), rolled(e))) {
            noOp(s, TransactionEvent.Begin(e)); noOp(s, TransactionEvent.Begin(e.copy(transactionId = id(8))))
        }
        noOp(armed(e), TransactionEvent.ArmCommitted(cor(e), armReceipt(e), head(armReceipt(e), e.prior, armedLock(e))))
        noOp(applied(e), TransactionEvent.ApplyVerified(cor(e))); noOp(rolling(e), TransactionEvent.ApplyFailed(cor(e), "APPLY_FAILED"))
        val commands = core.decide(idle(e), TransactionEvent.Begin(e)).commands
        assertThrows(UnsupportedOperationException::class.java) { (commands as MutableList<SkinCommand>).clear() }
    }
}
