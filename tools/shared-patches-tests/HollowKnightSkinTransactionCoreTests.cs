using System;
using System.Collections.Generic;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinTransactionCoreTests
{
    private readonly SkinTransactionCore core = new();
    private static string Id(int n) => $"00000000-0000-0000-0000-{n:000000000000}";
    private static string Hash(char c) => new(c, 64);
    private readonly SkinBindingToken binding = new("skin/hero:1");
    private ActiveVisual.Pack Pack(string id = "knight") => new(id, Hash('a'), Hash('b'), Hash('c'));
    private TransactionEnvelope Envelope(bool fresh = false, bool packPrior = false)
    {
        var prior = new ActivationSnapshot(packPrior ? SkinMode.ON : SkinMode.OFF, "knight",
            packPrior ? Pack() : new ActiveVisual.Vanilla(), 7);
        var target = fresh && packPrior ? prior with { SkinStamp = 8 } :
            new ActivationSnapshot(SkinMode.ON, "knight", Pack(), 8);
        return new(Id(1), fresh && packPrior ? SkinOperationKind.REBIND_APPLY : SkinOperationKind.MODE_ON,
            Id(2), Hash('d'), prior, target, binding, !fresh);
    }
    private TransactionCorrelation Cor(TransactionEnvelope e) => new(e.TransactionId, e.Binding);
    private TransactionState Idle(TransactionEnvelope e) => new(Binding: binding, Activation: e.Prior);
    private RegistryCommitReceipt ArmReceipt(TransactionEnvelope e) => new(e.BaseGenerationId, e.BaseGenerationSha256, Id(3), Hash('e'));
    private RegistryCommitReceipt CloseReceipt() => new(Id(3), Hash('e'), Id(4), Hash('f'));
    private RotationInterlock ArmedLock(TransactionEnvelope e) => new(InterlockState.ARMED, e.TransactionId, e.Operation,
        e.BaseGenerationId, e.BaseGenerationSha256, e.Prior, e.Target, e.Binding, e.PriorEstablishedOnBinding, null, null);
    private VerifiedRegistryHead Head(RegistryCommitReceipt r, ActivationSnapshot a, RotationInterlock l) => new(r.NewGenerationId, r.NewGenerationSha256, a, l);
    private TransactionState Prepared(TransactionEnvelope e) => core.Decide(core.Decide(Idle(e), new TransactionEvent.Begin(e)).State, new TransactionEvent.Prepared(Cor(e))).State;
    private TransactionState Armed(TransactionEnvelope e) => core.Decide(Prepared(e), new TransactionEvent.ArmCommitted(Cor(e), ArmReceipt(e), Head(ArmReceipt(e), e.Prior, ArmedLock(e)))).State;
    private TransactionState Applied(TransactionEnvelope e) => core.Decide(Armed(e), new TransactionEvent.ApplyVerified(Cor(e))).State;
    private TransactionState Rolling(TransactionEnvelope e) => core.Decide(Armed(e), new TransactionEvent.ApplyFailed(Cor(e), "APPLY_FAILED")).State;
    private TransactionState Rolled(TransactionEnvelope e) => core.Decide(Rolling(e), new TransactionEvent.RollbackVerified(Cor(e), !e.PriorEstablishedOnBinding)).State;
    private TransactionDecision Complete(TransactionState s, TransactionEnvelope e) => core.Decide(s,
        new TransactionEvent.CompletionCommitted(Cor(e), CloseReceipt(), Head(CloseReceipt(), s.PendingClosure, RotationInterlock.Clear())));
    private void NoOp(TransactionState s, TransactionEvent ev)
    {
        var d = core.Decide(s, ev); Assert.Equal(s, d.State); Assert.Empty(d.Commands);
    }

    [Fact] public void beginAndArmCarryCompleteTransactionEnvelope()
    {
        var e = Envelope(); var begin = core.Decide(Idle(e), new TransactionEvent.Begin(e));
        Assert.Equal(new SkinCommand.Prepare(e, e.Target.Active), Assert.Single(begin.Commands));
        Assert.Equal(e, begin.State.Envelope); Assert.Equal(RotationInterlock.Clear(), begin.State.Interlock);
        var prepared = core.Decide(begin.State, new TransactionEvent.Prepared(Cor(e)));
        Assert.Equal(new SkinCommand.Arm(e), Assert.Single(prepared.Commands));
        Assert.Equal(RotationInterlock.Clear(), prepared.State.Interlock); Assert.Null(prepared.State.ArmCommitReceipt);
    }
    [Fact] public void postBeginEventsRequireExactCorrelation()
    {
        var e = Envelope(); var begun = core.Decide(Idle(e), new TransactionEvent.Begin(e)).State;
        foreach (var c in new[] { Cor(e) with { TransactionId = Id(9) }, Cor(e) with { Binding = new SkinBindingToken("other") } })
        {
            NoOp(begun, new TransactionEvent.Prepared(c)); NoOp(Armed(e), new TransactionEvent.ApplyVerified(c));
            NoOp(Prepared(e), new TransactionEvent.ArmCommitted(c, ArmReceipt(e), Head(ArmReceipt(e), e.Prior, ArmedLock(e))));
            NoOp(Armed(e), new TransactionEvent.ApplyFailed(c, "FAILED"));
            NoOp(Rolling(e), new TransactionEvent.RollbackFailed(c, "FAILED"));
            NoOp(Applied(e), new TransactionEvent.CompletionRejected(c, "FAILED"));
            NoOp(Applied(e), new TransactionEvent.CompletionCommitted(c, CloseReceipt(), Head(CloseReceipt(), e.Target, RotationInterlock.Clear())));
            NoOp(Rolling(e), new TransactionEvent.RollbackVerified(c, false));
            NoOp(Applied(e), new TransactionEvent.CompletionIndeterminate(c));
        }
    }
    [Fact] public void durableCasEventsCarryExactCommitReceipts()
    {
        var e = Envelope(); var armed = Armed(e); Assert.Equal(ArmReceipt(e), armed.ArmCommitReceipt);
        Assert.Equal(ArmedLock(e), armed.Interlock); Assert.Equal(e.Prior, armed.Activation);
        var d = core.Decide(armed, new TransactionEvent.ApplyVerified(Cor(e)));
        Assert.Equal(new SkinCommand.Commit(Cor(e), Id(3), Hash('e'), e.Target), Assert.Single(d.Commands));
        var done = Complete(d.State, e); Assert.Equal(CloseReceipt(), done.State.CompletionReceipt);
    }
    [Fact] public void mismatchedCorrelationOrReceiptIsStaleNoOp()
    {
        var e = Envelope(); var r = ArmReceipt(e);
        foreach (var bad in new[] { r with { ExpectedGenerationId = Id(8) }, r with { ExpectedGenerationSha256 = Hash('a') },
            r with { NewGenerationId = r.ExpectedGenerationId }, r with { NewGenerationSha256 = r.ExpectedGenerationSha256 },
            r with { NewGenerationId = "bad" }, r with { NewGenerationSha256 = "BAD" } })
            NoOp(Prepared(e), new TransactionEvent.ArmCommitted(Cor(e), bad, Head(bad, e.Prior, ArmedLock(e))));
        var close = CloseReceipt() with { ExpectedGenerationSha256 = Hash('a') };
        NoOp(Applied(e), new TransactionEvent.CompletionCommitted(Cor(e), close, Head(close, e.Target, RotationInterlock.Clear())));
    }
    [Fact] public void armsBeforeFirstWriteAndCommitsStampOnce()
    {
        var e = Envelope(); NoOp(Prepared(e), new TransactionEvent.ApplyVerified(Cor(e)));
        var arm = core.Decide(Prepared(e), new TransactionEvent.ArmCommitted(Cor(e), ArmReceipt(e), Head(ArmReceipt(e), e.Prior, ArmedLock(e))));
        Assert.Equal(new SkinCommand.Apply(Cor(e)), Assert.Single(arm.Commands));
        var applied = core.Decide(arm.State, new TransactionEvent.ApplyVerified(Cor(e))).State;
        Assert.Equal(e.Prior, applied.Activation); Assert.Equal(InterlockState.ARMED, applied.Interlock.State);
        var done = Complete(applied, e); Assert.Equal(TransactionPhase.COMMITTED, done.State.Phase);
        Assert.Equal(e.Target, done.State.Activation); Assert.Equal(8, done.State.Activation.SkinStamp);
        Assert.Equal(RotationInterlock.Clear(), done.State.Interlock); Assert.Empty(done.Commands);
        NoOp(done.State, new TransactionEvent.ApplyVerified(Cor(e)));
    }
    [Fact] public void rollbackVerificationAloneCannotClearArmedOrPublishStamp()
    {
        foreach (var e in new[] { Envelope(), Envelope(true, true), Envelope(true),
            Envelope() with { Operation = SkinOperationKind.DEATH_ROTATION,
                Prior = new ActivationSnapshot(SkinMode.ROTATE, "other", Pack("other"), 7),
                Target = Envelope().Target with { Mode = SkinMode.ROTATE } } })
        {
            var rolling = Rolling(e); NoOp(Armed(e), new TransactionEvent.RollbackVerified(Cor(e), !e.PriorEstablishedOnBinding));
            NoOp(rolling, new TransactionEvent.RollbackVerified(Cor(e), e.PriorEstablishedOnBinding));
            var rolled = core.Decide(rolling, new TransactionEvent.RollbackVerified(Cor(e), !e.PriorEstablishedOnBinding));
            var expected = !e.PriorEstablishedOnBinding && e.Prior.Active is ActiveVisual.Pack ? e.Prior with { SkinStamp = 8 } : e.Prior;
            Assert.Equal(TransactionPhase.ROLLED_BACK, rolled.State.Phase); Assert.Equal(e.Prior, rolled.State.Activation);
            Assert.Equal(ArmedLock(e), rolled.State.Interlock); Assert.Equal(expected, rolled.State.PendingClosure);
            Assert.Equal(new SkinCommand.Commit(Cor(e), Id(3), Hash('e'), expected), Assert.Single(rolled.Commands));
            Assert.Equal(expected, Complete(rolled.State, e).State.Activation);
            NoOp(rolled.State, new TransactionEvent.RollbackVerified(Cor(e), !e.PriorEstablishedOnBinding));
        }
    }
    [Fact] public void rollsBackReverseAndLeavesIndeterminateBlocked()
    {
        var e = Envelope(); var d = core.Decide(Armed(e), new TransactionEvent.ApplyFailed(Cor(e), "APPLY_FAILED"));
        Assert.Equal(new SkinCommand.Rollback(Cor(e)), Assert.Single(d.Commands)); Assert.Equal("APPLY_FAILED", d.State.OriginalFailure);
        var blocked = core.Decide(Rolled(e), new TransactionEvent.CompletionIndeterminate(Cor(e)));
        Assert.Equal(TransactionPhase.BLOCKED, blocked.State.Phase); Assert.Equal(ArmedLock(e), blocked.State.Interlock);
        Assert.Equal(e.Prior, blocked.State.Activation); Assert.Empty(blocked.Commands);
        NoOp(blocked.State, new TransactionEvent.RollbackVerified(Cor(e), false));
    }
    [Fact] public void rejectsWrongVerifiedHeadInterlockAndClosure()
    {
        var e = Envelope(); var r = ArmReceipt(e); var h = Head(r, e.Prior, ArmedLock(e));
        foreach (var bad in new[] { h with { GenerationId = Id(9) }, h with { GenerationSha256 = Hash('a') },
            h with { Activation = e.Target }, h with { Interlock = RotationInterlock.Clear() },
            h with { Interlock = ArmedLock(e) with { PriorEstablishedOnBinding = false } },
            h with { Activation = e.Prior with { SelectedPackId = null } } })
            NoOp(Prepared(e), new TransactionEvent.ArmCommitted(Cor(e), r, bad));
        r = CloseReceipt(); h = Head(r, e.Target, RotationInterlock.Clear());
        foreach (var bad in new[] { h with { GenerationId = Id(9) }, h with { GenerationSha256 = Hash('a') },
            h with { Interlock = ArmedLock(e) }, h with { Activation = e.Prior },
            h with { Activation = e.Target with { Active = Pack() with { ImportReceiptSha256 = Hash('d') } } } })
            NoOp(Applied(e), new TransactionEvent.CompletionCommitted(Cor(e), r, bad));
    }
    [Fact] public void rejectedTargetCompletionRequestsRollback()
    {
        var e = Envelope(); var d = core.Decide(Applied(e), new TransactionEvent.CompletionRejected(Cor(e), "CAS_REJECTED"));
        Assert.Equal(new SkinCommand.Rollback(Cor(e)), Assert.Single(d.Commands)); Assert.Null(d.State.PendingClosure);
        Assert.Equal(TransactionPhase.ROLLBACK_PENDING, d.State.Phase); Assert.Equal(e.Prior, d.State.Activation);
    }
    [Fact] public void rejectedRollbackClosureBlocks()
    {
        var e = Envelope(); var d = core.Decide(Rolled(e), new TransactionEvent.CompletionRejected(Cor(e), "CAS_REJECTED"));
        Assert.Equal(TransactionPhase.BLOCKED, d.State.Phase); Assert.Empty(d.Commands);
        Assert.Equal(ArmedLock(e), d.State.Interlock); Assert.Equal(e.Prior, d.State.Activation);
        Assert.Equal("APPLY_FAILED", d.State.OriginalFailure); Assert.Equal("CAS_REJECTED", d.State.RollbackFailure);
    }
    [Fact] public void indeterminateTargetCompletionDoesNotRollback()
    {
        var e = Envelope(); var d = core.Decide(Applied(e), new TransactionEvent.CompletionIndeterminate(Cor(e)));
        Assert.Equal(TransactionPhase.BLOCKED, d.State.Phase); Assert.Empty(d.Commands);
        Assert.Equal(e.Prior, d.State.Activation); Assert.Equal(ArmedLock(e), d.State.Interlock); Assert.Null(d.State.CompletionReceipt);
        NoOp(d.State, new TransactionEvent.CompletionRejected(Cor(e), "CAS_REJECTED"));
        NoOp(d.State, new TransactionEvent.CompletionCommitted(Cor(e), CloseReceipt(), Head(CloseReceipt(), e.Target, RotationInterlock.Clear())));
    }
    [Fact] public void failurePersistenceCannotBeInvented()
    {
        var e = Envelope(); var r = CloseReceipt(); var failLock = ArmedLock(e) with { State = InterlockState.ROLLBACK_FAILED, OriginalFailure = "APPLY_FAILED", RollbackFailure = "RESTORE_FAILED" };
        foreach (var evidence in new[] { null, Head(r, e.Prior, ArmedLock(e)), Head(r, e.Target, failLock), Head(r, e.Prior, failLock) with { GenerationId = Id(8) } })
        {
            var d = core.Decide(Rolling(e), new TransactionEvent.RollbackFailed(Cor(e), "RESTORE_FAILED", r, evidence));
            Assert.Equal(TransactionPhase.BLOCKED, d.State.Phase); Assert.Equal(ArmedLock(e), d.State.Interlock);
            Assert.Equal("APPLY_FAILED", d.State.OriginalFailure); Assert.Equal("RESTORE_FAILED", d.State.RollbackFailure); Assert.Null(d.State.FailureReceipt);
        }
        var valid = core.Decide(Rolling(e), new TransactionEvent.RollbackFailed(Cor(e), "RESTORE_FAILED", r, Head(r, e.Prior, failLock)));
        Assert.Equal(failLock, valid.State.Interlock); Assert.Equal(r, valid.State.FailureReceipt); Assert.Empty(valid.Commands);
        var absent = core.Decide(Rolling(e), new TransactionEvent.RollbackFailed(Cor(e), "RESTORE_FAILED"));
        Assert.Equal(ArmedLock(e), absent.State.Interlock);
    }
    [Fact] public void invalidStateAndOverflowStayFailClosed()
    {
        var e = Envelope();
        foreach (var bad in new[] { e with { TransactionId = "bad" }, e with { Binding = new SkinBindingToken(" ") },
            e with { Target = e.Target with { SkinStamp = -1 } }, e with { Prior = e.Prior with { SkinStamp = long.MaxValue }, Target = e.Target with { SkinStamp = long.MinValue } },
            e with { Target = e.Target with { SelectedPackId = "other" } }, e with { Target = e.Target with { Active = new ActiveVisual.Vanilla() } },
            e with { Operation = SkinOperationKind.MODE_OFF }, e with { Target = e.Target with { SkinStamp = 9 } } })
            NoOp(Idle(bad), new TransactionEvent.Begin(bad));
        foreach (var bad in new[] { Idle(e) with { Interlock = ArmedLock(e) }, Armed(e) with { ArmCommitReceipt = null },
            Armed(e) with { Binding = new SkinBindingToken("other") }, Armed(e) with { Phase = TransactionPhase.ROLLED_BACK },
            Applied(e) with { PendingClosure = e.Prior }, Prepared(e) with { Activation = e.Target } })
            NoOp(bad, new TransactionEvent.ApplyVerified(Cor(e)));
        NoOp(Armed(e), new TransactionEvent.ApplyFailed(Cor(e), "bad code"));
    }
    [Fact] public void cannotRunSecondTransactionOrReplayReceipt()
    {
        var e = Envelope(); var begin = core.Decide(Idle(e), new TransactionEvent.Begin(e)).State;
        foreach (var s in new[] { begin, Prepared(e), Armed(e), Applied(e), Rolling(e), Rolled(e), Complete(Applied(e), e).State })
        { NoOp(s, new TransactionEvent.Begin(e)); NoOp(s, new TransactionEvent.Begin(e with { TransactionId = Id(8) })); }
        NoOp(Armed(e), new TransactionEvent.ArmCommitted(Cor(e), ArmReceipt(e), Head(ArmReceipt(e), e.Prior, ArmedLock(e))));
        NoOp(Applied(e), new TransactionEvent.ApplyVerified(Cor(e))); NoOp(Rolling(e), new TransactionEvent.ApplyFailed(Cor(e), "APPLY_FAILED"));
        Assert.Throws<NotSupportedException>(() => ((IList<SkinCommand>)core.Decide(Idle(e), new TransactionEvent.Begin(e)).Commands).Clear());
    }
    [Fact] public void operationSpecificSnapshotsAndVisualEqualityAreQualified()
    {
        var e = Envelope(); var packPrior = e.Target with { SkinStamp = 7 };
        var off = e with { Operation = SkinOperationKind.MODE_OFF, Prior = packPrior with { Mode = SkinMode.ROTATE },
            Target = e.Prior with { SkinStamp = 8 } };
        var rotate = e with { Operation = SkinOperationKind.DEATH_ROTATION,
            Prior = packPrior with { Mode = SkinMode.ROTATE },
            Target = e.Target with { Mode = SkinMode.ROTATE, SelectedPackId = "other", Active = Pack("other") } };
        var startup = Envelope(true, true) with { Operation = SkinOperationKind.STARTUP_APPLY };
        var rebindSelected = startup with { Operation = SkinOperationKind.REBIND_APPLY,
            Prior = packPrior with { SelectedPackId = "other" }, Target = e.Target with { SelectedPackId = "other", Active = Pack("other") } };
        var rotateRebind = startup with { Operation = SkinOperationKind.REBIND_APPLY,
            Prior = packPrior with { Mode = SkinMode.ROTATE, SelectedPackId = "other" },
            Target = e.Target with { Mode = SkinMode.ROTATE, SelectedPackId = "other" } };
        foreach (var valid in new[] { off, rotate, startup, Envelope(true, true), rebindSelected, rotateRebind,
            rebindSelected with { Operation = SkinOperationKind.STARTUP_APPLY }, rotate with { PriorEstablishedOnBinding = false },
            e with { Prior = e.Prior with { Active = Pack() }, PriorEstablishedOnBinding = false } })
        {
            Assert.Equal(valid.Target, Complete(Applied(valid), valid).State.Activation);
            Assert.Equal(valid.Prior, Armed(valid).Activation);
        }
        foreach (var bad in new[] { rotate with { Target = rotate.Target with { SelectedPackId = "knight" } },
            e with { Prior = packPrior with { Mode = SkinMode.ROTATE, Active = Pack("other") } },
            e with { Prior = e.Prior with { SelectedPackId = "other" } },
            off with { Prior = packPrior },
            rotateRebind with { Target = rotateRebind.Target with { Active = Pack("other") } },
            startup with { PriorEstablishedOnBinding = true },
            startup with { Target = startup.Target with { Mode = SkinMode.ROTATE } },
            off with { Target = off.Target with { SelectedPackId = null } },
            e with { Prior = packPrior, Target = e.Target with { Active = Pack() with { ImportReceiptSha256 = Hash('d') } } },
            e with { Prior = packPrior } }) NoOp(Idle(bad), new TransactionEvent.Begin(bad));
    }
    [Fact] public void modeOffDoesNotMistakeDurableHistoryForLiveVanilla()
    {
        var seed = Envelope();
        foreach (var active in new ActiveVisual[] { new ActiveVisual.Vanilla(), Pack() })
        {
            var prior = new ActivationSnapshot(SkinMode.ROTATE, "knight", active, 7);
            var e = seed with { Operation = SkinOperationKind.MODE_OFF, Prior = prior,
                Target = prior with { Mode = SkinMode.OFF, Active = new ActiveVisual.Vanilla(), SkinStamp = 8 }, PriorEstablishedOnBinding = false };
            Assert.IsType<SkinCommand.Prepare>(Assert.Single(core.Decide(Idle(e), new TransactionEvent.Begin(e)).Commands));
            var applied = Applied(e); Assert.Equal(prior, applied.Activation); Assert.Equal(InterlockState.ARMED, applied.Interlock.State);
            Assert.Equal(e.Target, Complete(applied, e).State.Activation);
            Assert.Equal(prior.SkinStamp + (active is ActiveVisual.Pack ? 1 : 0), Complete(Rolled(e), e).State.Activation.SkinStamp);
            if (active is ActiveVisual.Vanilla) NoOp(Idle(e), new TransactionEvent.Begin(e with { PriorEstablishedOnBinding = true }));
        }
    }
    [Fact] public void registrySyntaxAndDecisionValuesStayBoundedAndImmutable()
    {
        var e = Envelope();
        foreach (var token in new[] { " skin", "skin ", " skin", "skin ", "a‮b", "\ud800", new string('x', 257), "a\0b", "Ａ" })
        {
            var bad = e with { Binding = new SkinBindingToken(token) };
            var s = Idle(e) with { Binding = bad.Binding };
            NoOp(s, new TransactionEvent.Begin(bad));
        }
        foreach (var token in new[] { "skin/hero:1", "é", string.Concat(System.Linq.Enumerable.Repeat("😀", 256)) })
        {
            var good = e with { Binding = new SkinBindingToken(token) };
            var s = Idle(e) with { Binding = good.Binding };
            var first = core.Decide(s, new TransactionEvent.Begin(good));
            var second = new SkinTransactionCore().Decide(s with { }, new TransactionEvent.Begin(good with { }));
            Assert.Equal(TransactionPhase.PREPARING, first.State.Phase);
            Assert.Equal(first.State, second.State); Assert.Equal(first.Commands, second.Commands); Assert.Equal(first.Diagnosis, second.Diagnosis);
            Assert.Null(s.Envelope);
        }
    }
    [Fact] public void illegalPhaseEventsCannotFabricateVerification()
    {
        var e = Envelope();
        foreach (var s in new[] { Idle(e), core.Decide(Idle(e), new TransactionEvent.Begin(e)).State, Prepared(e) })
            foreach (var ev in new TransactionEvent[] { new TransactionEvent.ApplyVerified(Cor(e)), new TransactionEvent.ApplyFailed(Cor(e), "FAILED"),
                new TransactionEvent.RollbackVerified(Cor(e), false), new TransactionEvent.RollbackFailed(Cor(e), "FAILED"),
                new TransactionEvent.CompletionRejected(Cor(e), "FAILED"), new TransactionEvent.CompletionIndeterminate(Cor(e)),
                new TransactionEvent.CompletionCommitted(Cor(e), CloseReceipt(), Head(CloseReceipt(), e.Target, RotationInterlock.Clear())) }) NoOp(s, ev);
        foreach (var s in new[] { Armed(e), Applied(e), Rolling(e), Rolled(e) }) NoOp(s, new TransactionEvent.Prepared(Cor(e)));
        NoOp(Rolled(e), new TransactionEvent.ApplyFailed(Cor(e), "FAILED"));
        NoOp(Applied(e), new TransactionEvent.RollbackFailed(Cor(e), "FAILED"));
        var done = Complete(Rolled(e), e).State;
        NoOp(done, new TransactionEvent.CompletionCommitted(Cor(e), CloseReceipt(), Head(CloseReceipt(), e.Prior, RotationInterlock.Clear())));
        NoOp(done, new TransactionEvent.Begin(e with { TransactionId = Id(8) }));
    }
    [Fact] public void malformedClrValuesFailClosed()
    {
        var e = Envelope(); NoOp(null, new TransactionEvent.Begin(e)); NoOp(Idle(e), null);
        foreach (var bad in new[] { e with { Operation = (SkinOperationKind)99 }, e with { Binding = null }, e with { Prior = null },
            e with { Target = e.Target with { Mode = (SkinMode)99 } }, e with { Target = e.Target with { Active = null } }, e with { TransactionId = null } })
            NoOp(Idle(e), new TransactionEvent.Begin(bad));
        NoOp(Idle(e) with { Phase = (TransactionPhase)99 }, new TransactionEvent.Begin(e));
        NoOp(Armed(e), new TransactionEvent.ApplyVerified(null));
        NoOp(Prepared(e), new TransactionEvent.ArmCommitted(Cor(e), null, null));
    }
}
