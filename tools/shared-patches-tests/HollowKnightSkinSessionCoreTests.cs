using System;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinSessionCoreTests
{
    private readonly SkinSessionCore core = new();
    private static string Id(int n) => $"00000000-0000-0000-0000-{n:x12}";
    private static string Hash(int n) => n.ToString("x64");
    private static readonly SessionProcessIdentity Launcher = new(1000, 10, "18446744073709551615");
    private static readonly SessionProcessIdentity Game = new(1000, 20, "99999999999999999999");
    private static SessionBinding Binding() => new("hollow-knight", Id(1), Hash(1), $"sessions/{Id(1)}/descriptor.json", Id(2), Hash(2), long.MaxValue, Id(3), Hash(3), Launcher);
    private static VerifiedSessionLease Lease(SessionBinding b = null, SessionLeasePhase phase = SessionLeasePhase.LAUNCH_PENDING, bool ownedClose = false, string reason = "GAME_EXIT")
    {
        b ??= Binding(); var seq = phase == SessionLeasePhase.LAUNCH_PENDING ? 0 : phase == SessionLeasePhase.GAME_OWNED || !ownedClose ? 1 : 2;
        var d = new SessionLeaseDocument(1, b, seq, Id(10 + seq), seq == 0 ? null : Id(9 + seq), phase,
            phase == SessionLeasePhase.GAME_OWNED || ownedClose ? Game : null, phase == SessionLeasePhase.CLOSED ? reason : null);
        return new(new(b.DescriptorId, b.LeaseId, seq, phase, Hash(10 + seq)), d, Hash(10 + seq));
    }
    private SessionState Bound() { var p = Lease(); return Step(new(), new SessionEvent.BindIssuedPending(new(p, new SessionBarrier.ActiveInstalled(p.Head)))); }
    private SessionState Step(SessionState s, SessionEvent e) => core.Decide(s, e).State;
    private SessionState Claiming(SessionState s = null) => Step(s ?? Bound(), new SessionEvent.ClaimRequested(Binding(), Game));
    private static SessionTransitionReceipt Receipt(SessionCommand cmd, VerifiedSessionLease parent, VerifiedSessionLease child, SessionOperation op) => new(cmd.Correlation, op, parent, child,
        op == SessionOperation.CLAIM ? new SessionBarrier.ActiveInstalled(child.Head) : new SessionBarrier.ActiveRemoved(parent.Head));
    private SessionState Owned()
    {
        var s = Claiming(); return Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM))));
    }
    private static SessionProfileRecoveryEvidence Profile(SessionCorrelation c) => new("hollow-knight", c);
    private static SessionOwnerEvidence Owner(SessionCorrelation c, VerifiedSessionLease p, SessionLiveness l) => new(c, p.Document.Binding, p.Document.GameOwner ?? Launcher, l, l == SessionLiveness.ALIVE ? p.Document.GameOwner ?? Launcher : null);
    private static SessionTargetEvidence Target(SessionCorrelation c, VerifiedSessionLease p, SessionPresence presence) => new(c, p.Document.Binding, new("dev.game.hk", "dev.game.hk"), presence, presence == SessionPresence.PRESENT ? Game : null);
    [Fact] public void PendingOwnedClosedRequiresExactDurableCompletionsAndCachesClose()
    {
        var p = Bound(); Assert.NotNull(p.Acquisition); Assert.False(core.SessionWritesEligible(p));
        var s = Claiming(p); Assert.Equal(p.Lease, s.Lease); Assert.IsType<SessionCommand.ClaimExistingBinding>(s.Pending);
        var owned = Owned(); Assert.True(core.SessionWritesEligible(owned)); Assert.Null(core.Decide(owned, new SessionEvent.ClaimRequested(Binding(), Game)).Command);
        var closing = Step(owned, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); Assert.False(core.SessionWritesEligible(closing)); Assert.Equal(owned.Lease, closing.Lease);
        var r = Receipt(closing.Pending, closing.Lease, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: true), SessionOperation.CLOSE);
        var done = Step(closing, new SessionEvent.CloseCompleted(closing.Pending.Correlation, new SessionTransitionResult.Durable(r)));
        Assert.Equal(r.Child, done.Lease); Assert.Equal(SessionMutationGate.UNKNOWN, done.Gate);
        var replay = core.Decide(done, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); Assert.Equal(r, replay.CompletedClose); Assert.Null(replay.Command);
        Assert.Null(core.Decide(done, new SessionEvent.CloseRequested(Binding(), "OTHER")).CompletedClose);
        Assert.Equal(done, Step(done, new SessionEvent.BindIssuedPending(p.Acquisition))); Assert.False(core.SessionWritesEligible(done));
    }
    [Fact] public void PendingCanCloseWithoutGameOwner()
    {
        var s = Step(Bound(), new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); var r = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.CLOSED), SessionOperation.CLOSE);
        var done = Step(s, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(r)));
        Assert.Equal(SessionLeasePhase.CLOSED, done.Lease.Document.State); Assert.Null(done.Lease.Document.GameOwner); Assert.True(core.IsStateValid(done));
    }
    [Fact] public void RetryableClaimPinsOwnerAndRejectsOldDuplicateCallbacks()
    {
        foreach (var code in new[] { SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE })
        {
            var s = Claiming(); var old = s.Pending; var failure = new SessionTransitionResult.Failed(code);
            var retry = Step(s, new SessionEvent.ClaimCompleted(old.Correlation, failure)); Assert.Equal(SessionClaimUse.AVAILABLE, retry.ClaimUse); Assert.False(core.SessionWritesEligible(retry));
            Assert.Null(core.Decide(retry, new SessionEvent.ClaimRequested(Binding(), Game with { Pid = 21 })).Command);
            var next = Claiming(retry); Assert.True(next.Ordinal > old.Correlation.Ordinal); Assert.Equal(next, Step(next, new SessionEvent.ClaimCompleted(old.Correlation, failure)));
            Assert.Equal(next, Step(next, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")));
            var r = Receipt(next.Pending, next.Lease, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM);
            var done = Step(next, new SessionEvent.ClaimCompleted(next.Pending.Correlation, new SessionTransitionResult.Durable(r)));
            Assert.True(core.SessionWritesEligible(done)); Assert.Equal(done, Step(done, new SessionEvent.ClaimCompleted(next.Pending.Correlation, new SessionTransitionResult.Durable(r))));
        }
    }
    [Fact] public void DefinitiveClaimRejectionStillClosesAndCloseFailurePinsReason()
    {
        foreach (var code in new[] { SessionFailure.LIFECYCLE_BLOCKED, SessionFailure.SESSION_RECOVERY_AMBIGUOUS, SessionFailure.PROFILE_QUOTA_EXCEEDED })
        {
            var s = Claiming(); s = Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Failed(code)));
            Assert.Equal(SessionClaimUse.REJECTED, s.ClaimUse); Assert.Null(core.Decide(s, new SessionEvent.ClaimRequested(Binding(), Game)).Command);
            s = Step(s, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); Assert.NotNull(s.Pending);
            s = Step(s, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Failed(code)));
            Assert.Null(core.Decide(s, new SessionEvent.CloseRequested(Binding(), "OTHER")).Command); Assert.Null(core.Decide(s, new SessionEvent.ClaimRequested(Binding(), Game)).Command);
            Assert.NotNull(core.Decide(s, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
        }
    }
    [Fact] public void ClaimReceiptRejectsBindingDocumentHeadParentOwnerAndBarrierMismatches()
    {
        var s = Claiming(); var r = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM);
        foreach (var bad in new[] {
            r with { Correlation = r.Correlation with { Ordinal = 99 } }, r with { Operation = SessionOperation.CLOSE },
            r with { Parent = r.Parent with { CanonicalDocumentSha256 = Hash(99) } },
            r with { Child = r.Child with { Head = r.Child.Head with { Sha256 = Hash(99) } } },
            r with { Child = r.Child with { Document = r.Child.Document with { ParentTransitionId = Id(99) } } },
            r with { Child = r.Child with { Document = r.Child.Document with { GameOwner = Game with { Pid = 99 } } } },
            r with { Child = r.Child with { Document = r.Child.Document with { Binding = Binding() with { RegistrySha256 = Hash(99) } } } },
            r with { Barrier = new SessionBarrier.ActiveInstalled(r.Parent.Head) }, r with { Barrier = new SessionBarrier.ActiveRemoved(r.Parent.Head) }, r with { Barrier = null } })
        { var d = core.Decide(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(bad))); Assert.False(core.SessionWritesEligible(d.State)); Assert.Equal(s.Lease, d.State.Lease); Assert.Null(d.Command); }
    }
    [Fact] public void AcquisitionRejectsEveryBindingFieldAndHeadOnlyShapes()
    {
        var p = Lease(); var b = Binding();
        foreach (var bad in new[] { b with { ProfileId = "silksong" }, b with { DescriptorId = "BAD" }, b with { DescriptorSha256 = "A" }, b with { DescriptorPath = "descriptor.json" }, b with { LeaseId = b.DescriptorId }, b with { TokenSha256 = "raw" }, b with { SessionSequence = -1 }, b with { RegistryGenerationId = "bad" }, b with { RegistrySha256 = "bad" }, b with { LauncherOwner = Launcher with { ProcessStartToken = "01" } } })
            Assert.Null(Step(new(), new SessionEvent.BindIssuedPending(new(Lease(bad), new SessionBarrier.ActiveInstalled(Lease(bad).Head)))).Acquisition);
        Assert.Null(Step(new(), new SessionEvent.BindIssuedPending(new(p, new SessionBarrier.ActiveInstalled(p.Head with { Sha256 = Hash(99) })))).Acquisition);
    }
    [Fact] public void PendingRecoveryCoversAllNineLivenessCombinationsWithoutMintingTransfer()
    {
        foreach (SessionLiveness l in Enum.GetValues(typeof(SessionLiveness))) foreach (SessionPresence t in Enum.GetValues(typeof(SessionPresence)))
        {
            var s = Step(new(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var p = Lease();
            var result = new SessionRecoveryResult.RetainedOpenLease(p, new SessionBarrier.ActiveInstalled(p.Head), Owner(c, p, l), Target(c, p, t));
            var done = Step(s, new SessionEvent.RecoveryCompleted(c, result));
            Assert.Equal(l == SessionLiveness.ALIVE || t == SessionPresence.PRESENT ? SessionMutationGate.ACTIVE : SessionMutationGate.UNKNOWN, done.Gate);
            Assert.Null(done.Acquisition); Assert.Null(done.Lease); Assert.False(core.SessionWritesEligible(done)); Assert.Null(core.Decide(done, new SessionEvent.ClaimRequested(Binding(), Game)).Command);
        }
    }
    [Fact] public void OwnedRecoveryUsesOnlyScopedOwnerAndNeverGrantsGameWrites()
    {
        foreach (SessionLiveness l in Enum.GetValues(typeof(SessionLiveness)))
        {
            var s = Step(Owned(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var p = s.Lease;
            var done = Step(s, new SessionEvent.RecoveryCompleted(c, new SessionRecoveryResult.RetainedOpenLease(p, new SessionBarrier.ActiveInstalled(p.Head), Owner(c, p, l), null)));
            Assert.Equal(l == SessionLiveness.ALIVE ? SessionMutationGate.ACTIVE : SessionMutationGate.UNKNOWN, done.Gate); Assert.False(core.SessionWritesEligible(done));
            Assert.NotNull(core.Decide(done, new SessionEvent.RecoveryRequested()).Command);
        }
    }
    [Fact] public void RecoveredClosedRequiresDeadOwnerAbsentPendingTargetExactReceiptAndProfileEvidence()
    {
        foreach (var owned in new[] { false, true })
        {
            var s = Step(owned ? Owned() : Bound(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var p = s.Lease;
            var reason = owned ? "RECOVERY_GAME_OWNER_DEAD" : "RECOVERY_LAUNCHER_DEAD";
            var r = Receipt(s.Pending, p, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: owned, reason: reason), SessionOperation.CLOSE);
            var good = new SessionRecoveryResult.RecoveredClosed(p, r, Owner(c, p, SessionLiveness.DEAD), owned ? null : Target(c, p, SessionPresence.ABSENT), Profile(c));
            foreach (var bad in new[] { good with { Profile = null }, good with { Owner = Owner(c, p, SessionLiveness.UNKNOWN) }, good with { Receipt = r with { Barrier = new SessionBarrier.ActiveRemoved(r.Child.Head) } }, good with { Profile = Profile(c with { Ordinal = 99 }) } })
            { var blocked = Step(s, new SessionEvent.RecoveryCompleted(c, bad)); Assert.Equal(SessionMutationGate.UNKNOWN, blocked.Gate); Assert.False(core.SessionWritesEligible(blocked)); }
            var done = Step(s, new SessionEvent.RecoveryCompleted(c, good)); Assert.Equal(SessionMutationGate.CLEAR, done.Gate); Assert.Equal(r.Child, done.Lease); Assert.True(core.IsStateValid(done)); Assert.False(core.SessionWritesEligible(done));
        }
    }
    [Fact] public void NoOpenAndDeferredRecoveryAreRetryableNeverWriteAuthority()
    {
        var s = Step(new(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation;
        var done = Step(s, new SessionEvent.RecoveryCompleted(c, new SessionRecoveryResult.NoOpenLease(Profile(c)))); Assert.Equal(SessionMutationGate.CLEAR, done.Gate); Assert.False(core.SessionWritesEligible(done));
        for (var n = 0; n < 3; n++) { done = Step(done, new SessionEvent.RecoveryRequested()); done = Step(done, new SessionEvent.RecoveryCompleted(done.Pending.Correlation, new SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE))); Assert.Equal(SessionMutationGate.UNKNOWN, done.Gate); }
        Assert.Equal(4, done.Ordinal);
    }
    [Fact] public void ForgedRecoveryRecordCannotClearDifferentTerminalChain()
    {
        var s = Step(Bound(), new SessionEvent.CloseRequested(Binding(), "GAME_EXIT"));
        var r = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.CLOSED), SessionOperation.CLOSE);
        s = Step(s, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(r)));
        s = Step(s, new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation;
        var done = Step(s, new SessionEvent.RecoveryCompleted(c, new SessionRecoveryResult.NoOpenLease(Profile(c))));
        var parent = Lease() with { Document = Lease().Document with { TransitionId = Id(90) } };
        var child = Lease(phase: SessionLeasePhase.CLOSED, reason: "RECOVERY_LAUNCHER_DEAD"); child = child with { Document = child.Document with { ParentTransitionId = Id(90) } };
        var other = Receipt(s.Pending, parent, child, SessionOperation.CLOSE);
        var evidence = new SessionRecoveryResult.RecoveredClosed(parent, other, Owner(c, parent, SessionLiveness.DEAD), Target(c, parent, SessionPresence.ABSENT), Profile(c));
        Assert.False(core.IsStateValid(done with { Recovery = new(c, evidence) }));
    }
    [Fact] public void ClosedReceiptOrdinalMustFollowClaimAndRecoveryHighWaterMustHaveRecord()
    {
        var s = Step(Owned(), new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); var r = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: true), SessionOperation.CLOSE);
        var done = Step(s, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(r)));
        Assert.False(core.IsStateValid(done with { ClosedReceipt = r with { Correlation = r.Correlation with { Ordinal = 1 } } }));
        var recovery = Step(Owned(), new SessionEvent.RecoveryRequested()); var c = recovery.Pending.Correlation;
        var retained = new SessionRecoveryResult.RetainedOpenLease(recovery.Lease, new SessionBarrier.ActiveInstalled(recovery.Lease.Head), Owner(c, recovery.Lease, SessionLiveness.ALIVE), null);
        var confirmed = Step(recovery, new SessionEvent.RecoveryCompleted(c, retained));
        Assert.False(core.IsStateValid(confirmed with { RecoveryHighWater = 3, Ordinal = 3 }));
    }
    [Fact] public void UncertainClaimRecoveryRetainsRetryOrQualifiesOwnedForCloseOnly()
    {
        var claiming = Claiming(); var old = claiming.Pending; var s = Step(claiming, new SessionEvent.ClaimCompleted(old.Correlation, new SessionTransitionResult.Failed(SessionFailure.INDETERMINATE)));
        var recovering = Step(s, new SessionEvent.RecoveryRequested()); var c = recovering.Pending.Correlation; var pending = s.Lease;
        var retained = new SessionRecoveryResult.RetainedOpenLease(pending, new SessionBarrier.ActiveInstalled(pending.Head), Owner(c, pending, SessionLiveness.ALIVE), Target(c, pending, SessionPresence.UNKNOWN));
        var retryable = Step(recovering, new SessionEvent.RecoveryCompleted(c, retained)); Assert.NotNull(core.Decide(retryable, new SessionEvent.ClaimRequested(Binding(), Game)).Command);
        var owned = Lease(phase: SessionLeasePhase.GAME_OWNED); var receipt = Receipt(recovering.Pending, pending, owned, SessionOperation.CLAIM);
        var observed = new SessionRecoveryResult.RetainedOpenLease(owned, new SessionBarrier.ActiveInstalled(owned.Head), Owner(c, owned, SessionLiveness.ALIVE), null, receipt);
        var done = Step(recovering, new SessionEvent.RecoveryCompleted(c, observed)); Assert.Equal(owned, done.Lease); Assert.Equal(SessionClaimUse.REJECTED, done.ClaimUse); Assert.False(core.SessionWritesEligible(done));
        Assert.Null(core.Decide(done, new SessionEvent.ClaimRequested(Binding(), Game)).Command); Assert.NotNull(core.Decide(done, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
        Assert.False(core.IsStateValid(done with { ClaimUse = SessionClaimUse.CONSUMED }));
        Assert.Equal(done, Step(done, new SessionEvent.ClaimCompleted(old.Correlation, new SessionTransitionResult.Durable(Receipt(old, pending, owned, SessionOperation.CLAIM)))));
    }
    [Fact] public void EveryValidButDifferentBindingIsRejectedWithoutCommands()
    {
        var s = Claiming(); var b = Binding(); var good = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM);
        foreach (var other in new[] { b with { DescriptorId = Id(40), DescriptorPath = $"sessions/{Id(40)}/descriptor.json" }, b with { DescriptorSha256 = Hash(40) }, b with { LeaseId = Id(40) }, b with { TokenSha256 = Hash(40) }, b with { SessionSequence = 0 }, b with { RegistryGenerationId = Id(40) }, b with { RegistrySha256 = Hash(40) }, b with { LauncherOwner = Launcher with { Pid = 40 } } })
        {
            var wrong = good with { Child = Lease(other, SessionLeasePhase.GAME_OWNED) };
            var done = Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(wrong))); Assert.Equal(s.Lease, done.Lease); Assert.False(core.SessionWritesEligible(done));
            Assert.Equal(s, Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation with { Binding = other }, new SessionTransitionResult.Durable(good))));
            Assert.Null(core.Decide(Bound(), new SessionEvent.ClaimRequested(other, Game)).Command); Assert.Null(core.Decide(Bound(), new SessionEvent.CloseRequested(other, "GAME_EXIT")).Command);
        }
    }
    [Fact] public void CloseReceiptRejectsWrongReasonParentOwnerAndBarrierAndLateClaimCannotRevive()
    {
        var s = Step(Owned(), new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); var r = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: true), SessionOperation.CLOSE);
        foreach (var bad in new[] { r with { Parent = Lease() }, r with { Child = r.Child with { Document = r.Child.Document with { CloseReason = "OTHER" } } },
            r with { Child = r.Child with { Document = r.Child.Document with { GameOwner = Game with { ProcessStartToken = "9" } } } },
            r with { Barrier = new SessionBarrier.ActiveInstalled(r.Child.Head) }, r with { Barrier = new SessionBarrier.ActiveRemoved(r.Child.Head) }, r with { Child = r.Child with { Head = r.Child.Head with { TransitionSequence = 1 } } } })
        { var done = Step(s, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(bad))); Assert.Equal(s, done); Assert.Null(done.ClosedReceipt); Assert.False(core.SessionWritesEligible(done)); Assert.Null(core.Decide(done, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command); Assert.Equal(r, Step(done, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(r))).ClosedReceipt); }
        var closed = Step(s, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(r)));
        Assert.Equal(closed, Step(closed, new SessionEvent.ClaimCompleted(s.ClaimReceipt.Correlation, new SessionTransitionResult.Durable(s.ClaimReceipt))));
        Assert.Equal(closed, Step(closed, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(r))));
    }
    [Fact] public void MissingContradictoryWrongScopeAndPidReuseEvidenceRemainUnknown()
    {
        var s = Step(Bound(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var p = s.Lease;
        var good = new SessionRecoveryResult.RetainedOpenLease(p, new SessionBarrier.ActiveInstalled(p.Head), Owner(c, p, SessionLiveness.ALIVE), Target(c, p, SessionPresence.ABSENT));
        foreach (var bad in new[] { good with { Owner = null }, good with { Target = null }, good with { Owner = good.Owner with { AliveOwner = Launcher with { ProcessStartToken = "0" } } },
            good with { Owner = good.Owner with { ExpectedOwner = Game } }, good with { Owner = good.Owner with { Correlation = c with { Ordinal = 99 } } },
            good with { Target = good.Target with { PresentOwner = Game } }, good with { Target = good.Target with { Binding = Binding() with { SessionSequence = 0 } } },
            good with { Target = good.Target with { Target = new("bad", "bad") } }, good with { Barrier = null } })
        { var done = Step(s, new SessionEvent.RecoveryCompleted(c, bad)); Assert.Equal(s, done); Assert.Equal(SessionMutationGate.UNKNOWN, done.Gate); Assert.False(core.SessionWritesEligible(done)); Assert.Null(core.Decide(done, new SessionEvent.RecoveryRequested()).Command); Assert.Equal(SessionMutationGate.ACTIVE, Step(done, new SessionEvent.RecoveryCompleted(c, good)).Gate); }
        var noOpen = Step(s, new SessionEvent.RecoveryCompleted(c, new SessionRecoveryResult.NoOpenLease(Profile(c)))); Assert.Equal(SessionMutationGate.UNKNOWN, noOpen.Gate);
        var owned = Step(Owned(), new SessionEvent.RecoveryRequested()); c = owned.Pending.Correlation; p = owned.Lease;
        var extraneousTarget = new SessionRecoveryResult.RetainedOpenLease(p, new SessionBarrier.ActiveInstalled(p.Head), Owner(c, p, SessionLiveness.ALIVE), Target(c, p, SessionPresence.PRESENT));
        Assert.Equal(SessionMutationGate.UNKNOWN, Step(owned, new SessionEvent.RecoveryCompleted(c, extraneousTarget)).Gate);
    }
    [Fact] public void CanonicalBoundsAreExactAndOutputsAreImmutableValues()
    {
        var before = Bound(); var decision = core.Decide(before, new SessionEvent.ClaimRequested(Binding(), Game)); Assert.Null(before.Pending); Assert.Equal(0, before.Ordinal);
        var command = Assert.IsType<SessionCommand.ClaimExistingBinding>(decision.Command); Assert.Equal(Binding(), command.Binding); Assert.Equal(Game, command.Owner); Assert.Equal(command, decision.State.Pending);
        var changed = command with { Owner = Game with { Pid = 1 } }; Assert.NotEqual(changed, decision.Command); Assert.Equal(Game, command.Owner);
        foreach (var token in new[] { "", "01", "-1", "+1", " 1", "1\n", "١", "100000000000000000000" })
            Assert.Null(core.Decide(before, new SessionEvent.ClaimRequested(Binding(), Game with { ProcessStartToken = token })).Command);
        Assert.NotNull(core.Decide(before, new SessionEvent.ClaimRequested(Binding(), new(int.MaxValue, int.MaxValue, "0"))).Command);
        foreach (var reason in new[] { "", "lower", "A\n", new string('A', 129) }) Assert.Null(core.Decide(before, new SessionEvent.CloseRequested(Binding(), reason)).Command);
        Assert.NotNull(core.Decide(before, new SessionEvent.CloseRequested(Binding(), new string('A', 128))).Command);
    }
    [Fact] public void NullAndUndefinedCompletionValuesAreTotalAndNeverAuthorize()
    {
        var s = Claiming(); var c = s.Pending.Correlation; var r = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM);
        foreach (var bad in new SessionTransitionResult[] { null, new SessionTransitionResult.Durable(null), new SessionTransitionResult.Failed((SessionFailure)99), new SessionTransitionResult.Durable(r with { Child = null }), new SessionTransitionResult.Durable(r with { Parent = null }), new SessionTransitionResult.Durable(r with { Correlation = null }), new SessionTransitionResult.Durable(r with { Child = r.Child with { Document = null } }), new SessionTransitionResult.Durable(r with { Child = r.Child with { Head = null } }), new SessionTransitionResult.Durable(r with { Child = r.Child with { Document = r.Child.Document with { State = (SessionLeasePhase)99 } } }) })
            Assert.False(core.SessionWritesEligible(Step(s, new SessionEvent.ClaimCompleted(c, bad))));
        Assert.Equal(s, Step(s, new SessionEvent.ClaimCompleted(null, new SessionTransitionResult.Durable(r))));
        var recovery = Step(Bound(), new SessionEvent.RecoveryRequested()); Assert.Equal(SessionMutationGate.UNKNOWN, Step(recovery, new SessionEvent.RecoveryCompleted(recovery.Pending.Correlation, null)).Gate);
    }
    [Fact] public void RecoveryIsSerializedStaleCallbacksCannotReplaceNewAttempt()
    {
        var s = Step(Owned(), new SessionEvent.RecoveryRequested()); var old = s.Pending.Correlation;
        Assert.Null(core.Decide(s, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command); Assert.Null(core.Decide(s, new SessionEvent.RecoveryRequested()).Command);
        s = Step(s, new SessionEvent.RecoveryCompleted(old, new SessionRecoveryResult.Deferred(SessionFailure.DURABILITY_UNAVAILABLE))); s = Step(s, new SessionEvent.RecoveryRequested());
        Assert.Equal(s, Step(s, new SessionEvent.RecoveryCompleted(old, new SessionRecoveryResult.NoOpenLease(Profile(old))))); Assert.False(core.SessionWritesEligible(s));
    }
    [Fact] public void MalformedClaimCompletionsPreservePendingUntilExactValidCompletion()
    {
        var s = Claiming(); var c = s.Pending.Correlation; var good = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM);
        foreach (var bad in new SessionTransitionResult[] { null, new SessionTransitionResult.Durable(null), new SessionTransitionResult.Failed((SessionFailure)99),
            new SessionTransitionResult.Durable(good with { Barrier = null }), new SessionTransitionResult.Durable(good with { Barrier = new SessionBarrier.ActiveInstalled(good.Parent.Head) }),
            new SessionTransitionResult.Durable(good with { Child = good.Child with { Head = good.Child.Head with { Sha256 = Hash(90) } } }),
            new SessionTransitionResult.Durable(good with { Child = Lease(Binding() with { TokenSha256 = Hash(90) }, SessionLeasePhase.GAME_OWNED) }),
            new SessionTransitionResult.Durable(good with { Parent = good.Parent with { Document = good.Parent.Document with { TransitionId = Id(90) } } }) })
        {
            var ignored = core.Decide(s, new SessionEvent.ClaimCompleted(c, bad)); Assert.Equal(s, ignored.State); Assert.Null(ignored.Command); Assert.Null(ignored.CompletedClose);
            Assert.Null(core.Decide(ignored.State, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
            var done = Step(ignored.State, new SessionEvent.ClaimCompleted(c, new SessionTransitionResult.Durable(good))); Assert.True(core.SessionWritesEligible(done)); Assert.Null(done.Pending);
        }
        var failed = Step(s, new SessionEvent.ClaimCompleted(c, new SessionTransitionResult.Failed(SessionFailure.LIFECYCLE_BLOCKED)));
        Assert.Null(failed.Pending); Assert.Equal(SessionClaimUse.REJECTED, failed.ClaimUse); Assert.NotNull(core.Decide(failed, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
    }
    [Fact] public void MalformedCloseCompletionsPreservePendingAndExactReasonUntilValidCompletion()
    {
        var s = Step(Owned(), new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); var c = s.Pending.Correlation;
        var good = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: true), SessionOperation.CLOSE);
        foreach (var bad in new SessionTransitionResult[] { null, new SessionTransitionResult.Durable(null), new SessionTransitionResult.Failed((SessionFailure)99),
            new SessionTransitionResult.Durable(good with { Barrier = null }), new SessionTransitionResult.Durable(good with { Barrier = new SessionBarrier.ActiveRemoved(good.Child.Head) }),
            new SessionTransitionResult.Durable(good with { Child = good.Child with { Document = good.Child.Document with { CloseReason = "OTHER" } } }),
            new SessionTransitionResult.Durable(good with { Child = good.Child with { Head = good.Child.Head with { DescriptorId = Id(90) } } }) })
        {
            var ignored = core.Decide(s, new SessionEvent.CloseCompleted(c, bad)); Assert.Equal(s, ignored.State); Assert.Null(ignored.Command);
            Assert.Null(core.Decide(ignored.State, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
            var done = Step(ignored.State, new SessionEvent.CloseCompleted(c, new SessionTransitionResult.Durable(good))); Assert.Equal(good, done.ClosedReceipt); Assert.Null(done.Pending);
        }
        var failed = Step(s, new SessionEvent.CloseCompleted(c, new SessionTransitionResult.Failed(SessionFailure.DURABILITY_UNAVAILABLE)));
        Assert.Null(failed.Pending); Assert.NotNull(core.Decide(failed, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
    }
    [Fact] public void MalformedRecoveryCompletionsPreserveSerializationUntilValidCompletion()
    {
        var s = Step(Bound(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var p = s.Lease;
        var good = new SessionRecoveryResult.RetainedOpenLease(p, new SessionBarrier.ActiveInstalled(p.Head), Owner(c, p, SessionLiveness.ALIVE), Target(c, p, SessionPresence.ABSENT));
        foreach (var bad in new SessionRecoveryResult[] { null, good with { Owner = null }, good with { Barrier = null }, good with { Target = null },
            good with { Lease = Lease(Binding() with { RegistrySha256 = Hash(90) }) }, new SessionRecoveryResult.NoOpenLease(Profile(c)), new SessionRecoveryResult.Deferred((SessionFailure)99) })
        {
            var ignored = core.Decide(s, new SessionEvent.RecoveryCompleted(c, bad)); Assert.Equal(s, ignored.State); Assert.Null(ignored.Command);
            Assert.Null(core.Decide(ignored.State, new SessionEvent.RecoveryRequested()).Command);
            var done = Step(ignored.State, new SessionEvent.RecoveryCompleted(c, good)); Assert.Equal(SessionMutationGate.ACTIVE, done.Gate); Assert.Null(done.Pending);
        }
        var failed = Step(s, new SessionEvent.RecoveryCompleted(c, new SessionRecoveryResult.Deferred(SessionFailure.SESSION_RECOVERY_AMBIGUOUS)));
        Assert.Null(failed.Pending); Assert.Equal(SessionMutationGate.UNKNOWN, failed.Gate); Assert.NotNull(core.Decide(failed, new SessionEvent.RecoveryRequested()).Command);
    }
    [Fact] public void RecoveryOwnedObservationCannotPrecedeItsSuccessfulClaimReceipt()
    {
        var s = Step(Owned(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation;
        var good = new SessionRecoveryResult.RetainedOpenLease(s.Lease, new SessionBarrier.ActiveInstalled(s.Lease.Head), Owner(c, s.Lease, SessionLiveness.ALIVE), null);
        var done = Step(s, new SessionEvent.RecoveryCompleted(c, good)); Assert.True(core.IsStateValid(done)); Assert.False(core.SessionWritesEligible(done));
        foreach (var ordinal in new[] { 2L, 3L, long.MaxValue })
        {
            var forged = done with { ClaimReceipt = done.ClaimReceipt with { Correlation = done.ClaimReceipt.Correlation with { Ordinal = ordinal } }, Ordinal = ordinal };
            Assert.False(core.IsStateValid(forged)); Assert.False(core.SessionWritesEligible(forged)); Assert.Null(core.Decide(forged, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
        }
        done = Step(done, new SessionEvent.RecoveryRequested()); done = Step(done, new SessionEvent.RecoveryCompleted(done.Pending.Correlation, new SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)));
        var afterDeferred = done with { ClaimReceipt = done.ClaimReceipt with { Correlation = done.ClaimReceipt.Correlation with { Ordinal = 4 } }, Ordinal = 4 };
        Assert.False(core.IsStateValid(afterDeferred)); Assert.False(core.SessionWritesEligible(afterDeferred));
    }
    [Fact] public void ExactPendingRecoveryAllowsLaterActualClaimAndPreservesOneUseAcrossRecovery()
    {
        var s = Claiming(); s = Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Failed(SessionFailure.INDETERMINATE)));
        s = Step(s, new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var p = s.Lease;
        var pending = new SessionRecoveryResult.RetainedOpenLease(p, new SessionBarrier.ActiveInstalled(p.Head), Owner(c, p, SessionLiveness.ALIVE), Target(c, p, SessionPresence.UNKNOWN));
        s = Step(s, new SessionEvent.RecoveryCompleted(c, pending)); s = Claiming(s); var receipt = Receipt(s.Pending, p, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM);
        var owned = Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(receipt))); Assert.True(core.IsStateValid(owned)); Assert.True(core.SessionWritesEligible(owned)); Assert.Equal(3, owned.ClaimReceipt.Correlation.Ordinal);
        var recovery = Step(owned, new SessionEvent.RecoveryRequested()); c = recovery.Pending.Correlation;
        var retained = new SessionRecoveryResult.RetainedOpenLease(owned.Lease, new SessionBarrier.ActiveInstalled(owned.Lease.Head), Owner(c, owned.Lease, SessionLiveness.ALIVE), null);
        var blocked = Step(recovery, new SessionEvent.RecoveryCompleted(c, retained)); Assert.False(core.SessionWritesEligible(blocked)); Assert.Null(core.Decide(blocked, new SessionEvent.ClaimRequested(Binding(), Game)).Command); Assert.NotNull(core.Decide(blocked, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command);
    }
    [Fact] public void CoordinatedClaimAuditShiftAndMissingFenceCannotReviveAfterRepeatedDeferredRecovery()
    {
        var s = Step(Owned(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation;
        var retained = new SessionRecoveryResult.RetainedOpenLease(s.Lease, new SessionBarrier.ActiveInstalled(s.Lease.Head), Owner(c, s.Lease, SessionLiveness.ALIVE), null);
        var done = Step(s, new SessionEvent.RecoveryCompleted(c, retained)); Assert.NotNull(done.ClaimAttempt); Assert.NotNull(done.ClaimRecoveryFence);
        var fence = done.ClaimRecoveryFence;
        for (var n = 0; n < 4; n++)
        {
            var future = done.Ordinal + 1;
            var shifted = done with { Ordinal = future, ClaimReceipt = done.ClaimReceipt with { Correlation = done.ClaimReceipt.Correlation with { Ordinal = future } }, ClaimAttempt = done.ClaimAttempt with { Correlation = done.ClaimAttempt.Correlation with { Ordinal = future } } };
            foreach (var malformed in new[] { shifted, done with { ClaimRecoveryFence = null }, done with { ClaimRecoveryFence = fence with { RecoveryCorrelation = fence.RecoveryCorrelation with { Ordinal = 1 } } } })
            { Assert.False(core.IsStateValid(malformed)); Assert.False(core.SessionWritesEligible(malformed)); }
            done = Step(done, new SessionEvent.RecoveryRequested()); done = Step(done, new SessionEvent.RecoveryCompleted(done.Pending.Correlation, new SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)));
            Assert.Equal(fence, done.ClaimRecoveryFence); Assert.False(core.SessionWritesEligible(done)); Assert.True(core.IsStateValid(done));
        }
    }
    [Fact] public void RecoveryObservationChronologyRejectsPendingAfterClaimAndNoOpenBeforeClose()
    {
        var s = Step(Bound(), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var p = s.Lease;
        var retained = new SessionRecoveryResult.RetainedOpenLease(p, new SessionBarrier.ActiveInstalled(p.Head), Owner(c, p, SessionLiveness.ALIVE), Target(c, p, SessionPresence.ABSENT));
        s = Step(s, new SessionEvent.RecoveryCompleted(c, retained)); s = Claiming(s); var receipt = Receipt(s.Pending, p, Lease(phase: SessionLeasePhase.GAME_OWNED), SessionOperation.CLAIM);
        var owned = Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Durable(receipt))); Assert.True(core.SessionWritesEligible(owned));
        var future = c with { Ordinal = 3 }; var shifted = retained with { Owner = retained.Owner with { Correlation = future }, Target = retained.Target with { Correlation = future } };
        var forged = owned with { Recovery = new(future, shifted), RecoveryHighWater = 3, Ordinal = 3, ClaimRecoveryFence = new(future, receipt) }; Assert.False(core.IsStateValid(forged));
        var closing = Step(Owned(), new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); var close = Receipt(closing.Pending, closing.Lease, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: true), SessionOperation.CLOSE);
        var closed = Step(closing, new SessionEvent.CloseCompleted(closing.Pending.Correlation, new SessionTransitionResult.Durable(close))); closed = Step(closed, new SessionEvent.RecoveryRequested()); c = closed.Pending.Correlation;
        var clear = Step(closed, new SessionEvent.RecoveryCompleted(c, new SessionRecoveryResult.NoOpenLease(Profile(c)))); Assert.True(core.IsStateValid(clear));
        Assert.False(core.IsStateValid(clear with { ClosedReceipt = close with { Correlation = close.Correlation with { Ordinal = 4 } }, Ordinal = 4 }));
    }
    [Fact] public void ObservedOwnedFenceNeverBecomesSuccessfulBridgeConsumptionAfterDeferred()
    {
        var s = Claiming(); s = Step(s, new SessionEvent.ClaimCompleted(s.Pending.Correlation, new SessionTransitionResult.Failed(SessionFailure.INDETERMINATE)));
        s = Step(s, new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var owned = Lease(phase: SessionLeasePhase.GAME_OWNED); var observed = Receipt(s.Pending, s.Lease, owned, SessionOperation.CLAIM);
        var result = new SessionRecoveryResult.RetainedOpenLease(owned, new SessionBarrier.ActiveInstalled(owned.Head), Owner(c, owned, SessionLiveness.ALIVE), null, observed);
        var done = Step(s, new SessionEvent.RecoveryCompleted(c, result)); Assert.Equal(SessionClaimUse.REJECTED, done.ClaimUse); Assert.NotNull(done.ClaimRecoveryFence);
        done = Step(done, new SessionEvent.RecoveryRequested()); done = Step(done, new SessionEvent.RecoveryCompleted(done.Pending.Correlation, new SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)));
        var minted = done with { ClaimUse = SessionClaimUse.CONSUMED, ClaimAttempt = done.ClaimAttempt with { Correlation = observed.Correlation } };
        Assert.False(core.IsStateValid(minted)); Assert.False(core.SessionWritesEligible(minted)); Assert.False(core.IsStateValid(done with { ClaimRecoveryFence = null }));
    }
    private SessionState FailedClosing(bool owned, SessionFailure code, string reason = "GAME_EXIT")
    {
        var s = Step(owned ? Owned() : Bound(), new SessionEvent.CloseRequested(Binding(), reason));
        return Step(s, new SessionEvent.CloseCompleted(s.Pending.Correlation, new SessionTransitionResult.Failed(code)));
    }
    private static SessionRecoveryResult.RecoveredClosed RecoveryClose(SessionState s)
    {
        var p = s.Lease; var c = s.Pending.Correlation; var owned = p.Document.State == SessionLeasePhase.GAME_OWNED;
        var reason = owned ? "RECOVERY_GAME_OWNER_DEAD" : "RECOVERY_LAUNCHER_DEAD";
        return new(p, Receipt(s.Pending, p, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: owned, reason: reason), SessionOperation.CLOSE),
            Owner(c, p, SessionLiveness.DEAD), owned ? null : Target(c, p, SessionPresence.ABSENT), Profile(c));
    }
    [Fact] public void FailedExplicitCloseAllowsIndependentRecoveryClosureForPendingAndOwned()
    {
        foreach (var owned in new[] { false, true }) foreach (var code in new[] { SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE })
        {
            var failed = FailedClosing(owned, code); Assert.Equal("GAME_EXIT", failed.PinnedCloseReason);
            Assert.NotNull(core.Decide(failed, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).Command); Assert.Null(core.Decide(failed, new SessionEvent.CloseRequested(Binding(), "OTHER")).Command);
            var recovering = Step(failed, new SessionEvent.RecoveryRequested()); var good = RecoveryClose(recovering);
            var decision = core.Decide(recovering, new SessionEvent.RecoveryCompleted(recovering.Pending.Correlation, good));
            Assert.Equal("RECOVERED", decision.Diagnosis); Assert.Equal(good.Receipt.Child, decision.State.Lease); Assert.Equal(SessionMutationGate.CLEAR, decision.State.Gate);
            Assert.Null(decision.State.Pending); Assert.True(core.IsStateValid(decision.State)); Assert.False(core.SessionWritesEligible(decision.State)); Assert.Equal("GAME_EXIT", decision.State.PinnedCloseReason);
            Assert.Equal(failed.CloseAttempt, decision.State.CloseAttempt);
            foreach (var reason in new[] { "GAME_EXIT", good.Receipt.Child.Document.CloseReason })
            { var terminal = core.Decide(decision.State, new SessionEvent.CloseRequested(Binding(), reason)); Assert.Equal("REQUEST_REJECTED", terminal.Diagnosis); Assert.Null(terminal.Command); Assert.Null(terminal.CompletedClose); }
        }
    }
    [Fact] public void RecoveryAfterFailedCloseRejectsWrongAuthorityThenAcceptsExactCompletion()
    {
        foreach (var owned in new[] { false, true }) foreach (var code in new[] { SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE })
        {
            var s = Step(FailedClosing(owned, code), new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var good = RecoveryClose(s); var r = good.Receipt;
            foreach (var bad in new[] { good with { Receipt = r with { Child = r.Child with { Document = r.Child.Document with { CloseReason = "GAME_EXIT" } } } },
                good with { Receipt = r with { Child = r.Child with { Document = r.Child.Document with { CloseReason = owned ? "RECOVERY_LAUNCHER_DEAD" : "RECOVERY_GAME_OWNER_DEAD" } } } },
                good with { Owner = null }, good with { Owner = Owner(c, s.Lease, SessionLiveness.UNKNOWN) }, good with { Profile = null },
                good with { Receipt = r with { Barrier = null } }, good with { Receipt = r with { Barrier = new SessionBarrier.ActiveRemoved(r.Child.Head) } },
                good with { Parent = Lease(Binding() with { RegistrySha256 = Hash(90) }) }, good with { Receipt = r with { Parent = r.Parent with { Head = r.Parent.Head with { Sha256 = Hash(90) } } } } })
            {
                var rejected = core.Decide(s, new SessionEvent.RecoveryCompleted(c, bad)); Assert.Equal(s, rejected.State); Assert.Null(rejected.Command); Assert.Null(rejected.CompletedClose);
                var done = Step(rejected.State, new SessionEvent.RecoveryCompleted(c, good)); Assert.Equal(good.Receipt.Child, done.Lease); Assert.Null(done.Pending); Assert.True(core.IsStateValid(done));
            }
        }
    }
    [Fact] public void ExplicitCloseRetryStillRequiresItsOwnPinnedReasonAndCachesOnlyItsSuccess()
    {
        foreach (var owned in new[] { false, true }) foreach (var code in new[] { SessionFailure.DURABILITY_UNAVAILABLE, SessionFailure.INDETERMINATE })
        {
            var failed = FailedClosing(owned, code); var s = Step(failed, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")); var c = s.Pending.Correlation;
            var good = Receipt(s.Pending, s.Lease, Lease(phase: SessionLeasePhase.CLOSED, ownedClose: owned), SessionOperation.CLOSE);
            var wrong = good with { Child = good.Child with { Document = good.Child.Document with { CloseReason = owned ? "RECOVERY_GAME_OWNER_DEAD" : "RECOVERY_LAUNCHER_DEAD" } } };
            Assert.Equal(s, Step(s, new SessionEvent.CloseCompleted(c, new SessionTransitionResult.Durable(wrong))));
            var done = Step(s, new SessionEvent.CloseCompleted(c, new SessionTransitionResult.Durable(good))); Assert.True(core.IsStateValid(done)); Assert.Null(done.RecoveryClosure);
            Assert.Equal(good, core.Decide(done, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).CompletedClose); Assert.Null(core.Decide(done, new SessionEvent.CloseRequested(Binding(), wrong.Child.Document.CloseReason)).CompletedClose);
            Assert.NotNull(done.CloseAttempt); Assert.Equal(c, done.CloseAttempt.Correlation); Assert.Equal("GAME_EXIT", done.CloseAttempt.Reason);
        }
    }
    [Fact] public void IndependentRecoveryClosureRetainsExactExplicitPinAndTerminalAuditAcrossLaterRecovery()
    {
        foreach (var owned in new[] { false, true }) foreach (var explicitReason in new[] { "GAME_EXIT", owned ? "RECOVERY_GAME_OWNER_DEAD" : "RECOVERY_LAUNCHER_DEAD" })
        {
            var failed = FailedClosing(owned, SessionFailure.INDETERMINATE, explicitReason); var s = Step(failed, new SessionEvent.RecoveryRequested()); var c = s.Pending.Correlation; var good = RecoveryClose(s);
            var done = Step(s, new SessionEvent.RecoveryCompleted(c, good)); Assert.NotNull(done.RecoveryClosure); Assert.Equal(new SessionRecoveryRecord(c, good), done.RecoveryClosure); Assert.NotNull(done.CloseAttempt);
            var audit = done.RecoveryClosure; var attempt = done.CloseAttempt;
            for (var n = 0; n < 3; n++)
            {
                done = Step(done, new SessionEvent.RecoveryRequested()); var next = done.Pending.Correlation;
                done = Step(done, new SessionEvent.RecoveryCompleted(next, n == 0 ? new SessionRecoveryResult.NoOpenLease(Profile(next)) : new SessionRecoveryResult.Deferred(SessionFailure.INDETERMINATE)));
                Assert.True(core.IsStateValid(done)); Assert.Equal(audit, done.RecoveryClosure); Assert.Equal(attempt, done.CloseAttempt); Assert.Equal(explicitReason, done.PinnedCloseReason); Assert.False(core.SessionWritesEligible(done));
                foreach (var bad in new[] { done with { RecoveryClosure = null }, done with { PinnedCloseReason = "OTHER" }, done with { CloseAttempt = null },
                    done with { CloseAttempt = attempt with { Reason = "OTHER" } }, done with { CloseAttempt = attempt with { Correlation = c } },
                    done with { RecoveryClosure = audit with { Result = good with { Owner = null } } }, done with { RecoveryClosure = audit with { Result = good with { Profile = null } } },
                    done with { RecoveryClosure = audit with { Result = good with { Receipt = good.Receipt with { Barrier = null } } } },
                    done with { RecoveryClosure = audit with { Correlation = c with { Ordinal = c.Ordinal + 1 } } } })
                { Assert.False(core.IsStateValid(bad)); Assert.False(core.SessionWritesEligible(bad)); Assert.Null(core.Decide(bad, new SessionEvent.CloseRequested(Binding(), "GAME_EXIT")).CompletedClose); }
            }
        }
    }
    [Fact] public void RecoveryWithoutExplicitCloseNeverMintsAnExplicitPinOrCachedCloseResponse()
    {
        foreach (var owned in new[] { false, true })
        {
            var s = Step(owned ? Owned() : Bound(), new SessionEvent.RecoveryRequested()); var good = RecoveryClose(s);
            var done = Step(s, new SessionEvent.RecoveryCompleted(s.Pending.Correlation, good)); Assert.Equal(SessionMutationGate.CLEAR, done.Gate); Assert.Null(done.Pending);
            Assert.Null(done.PinnedCloseReason); Assert.Null(done.CloseAttempt); Assert.NotNull(done.RecoveryClosure); Assert.True(core.IsStateValid(done));
            var request = core.Decide(done, new SessionEvent.CloseRequested(Binding(), good.Receipt.Child.Document.CloseReason)); Assert.Equal("REQUEST_REJECTED", request.Diagnosis); Assert.Null(request.Command); Assert.Null(request.CompletedClose);
        }
    }
    [Fact] public void MalformedStatesNullEnumsAndOrdinalOverflowFailClosedWithoutReset()
    {
        var owned = Owned();
        foreach (var bad in new[] { owned with { ClaimReceipt = null }, owned with { Lease = Lease() }, owned with { ClaimUse = (SessionClaimUse)99 }, owned with { Gate = SessionMutationGate.CLEAR }, owned with { Ordinal = -1 }, owned with { PinnedOwner = null } })
        { Assert.False(core.IsStateValid(bad)); Assert.False(core.SessionWritesEligible(bad)); Assert.Null(core.Decide(bad, new SessionEvent.RecoveryRequested()).Command); }
        Assert.Null(core.Decide(null, null).Command); Assert.False(core.SessionWritesEligible(null));
        var max = Bound() with { Ordinal = long.MaxValue }; Assert.Null(core.Decide(max, new SessionEvent.ClaimRequested(Binding(), Game)).Command); Assert.Equal(long.MaxValue, core.Decide(max, new SessionEvent.RecoveryRequested()).State.Ordinal);
        Assert.Equal(owned, Step(owned, new SessionEvent.BindIssuedPending(Bound().Acquisition)));
    }
}
