using System.Linq;
using System.Text.RegularExpressions;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Pure structural reducer. All evidence is supplied by a trusted serialized coordinator;
    // this class does not verify bytes, discover processes, execute credentials, or perform I/O.
    public sealed class SkinSessionCore
    {
        private static bool Match(string s, string pattern, int max) => s != null && s.Length <= max && Regex.IsMatch(s, "\\A(?:" + pattern + ")\\z");
        private static bool Id(string s) => Match(s, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", 36);
        private static bool Hash(string s) => Match(s, "[0-9a-f]{64}", 64);
        private static bool Reason(string s) => Match(s, "[A-Z][A-Z0-9_]{0,127}", 128);
        private static bool Owner(SessionProcessIdentity p) => p != null && p.Uid >= 0 && p.Pid > 0 && Match(p.ProcessStartToken, "0|[1-9][0-9]*", 20);
        private static bool Binding(SessionBinding b) => b != null && b.ProfileId == "hollow-knight" && Id(b.DescriptorId) && Id(b.LeaseId) &&
            b.DescriptorId != "00000000-0000-0000-0000-000000000000" && b.LeaseId != "00000000-0000-0000-0000-000000000000" && b.DescriptorId != b.LeaseId &&
            Hash(b.DescriptorSha256) && b.DescriptorPath == $"sessions/{b.DescriptorId}/descriptor.json" && Hash(b.TokenSha256) && b.SessionSequence >= 0 &&
            Id(b.RegistryGenerationId) && Hash(b.RegistrySha256) && Owner(b.LauncherOwner);
        private static bool Correlation(SessionCorrelation c) => c != null && c.Ordinal > 0 && (c.Binding == null || Binding(c.Binding));
        private static bool Failure(SessionFailure f) => f >= SessionFailure.DURABILITY_UNAVAILABLE && f <= SessionFailure.DOCUMENT_INVALID;
        private static bool Lease(VerifiedSessionLease v)
        {
            if (v?.Head == null || v.Document == null) return false;
            var d = v.Document; var h = v.Head;
            if (d.SchemaVersion != 1 || !Binding(d.Binding) || !Id(d.TransitionId) || !Hash(v.CanonicalDocumentSha256) || h.Sha256 != v.CanonicalDocumentSha256 ||
                h.DescriptorId != d.Binding.DescriptorId || h.LeaseId != d.Binding.LeaseId || h.TransitionSequence != d.TransitionSequence || h.State != d.State) return false;
            if (d.ParentTransitionId != null && (!Id(d.ParentTransitionId) || d.ParentTransitionId == d.TransitionId)) return false;
            if (d.GameOwner != null && !Owner(d.GameOwner)) return false;
            return d.State switch {
                SessionLeasePhase.LAUNCH_PENDING => d.TransitionSequence == 0 && d.ParentTransitionId == null && d.GameOwner == null && d.CloseReason == null,
                SessionLeasePhase.GAME_OWNED => d.TransitionSequence == 1 && d.ParentTransitionId != null && d.GameOwner != null && d.CloseReason == null,
                SessionLeasePhase.CLOSED => d.ParentTransitionId != null && Reason(d.CloseReason) && (d.TransitionSequence == 1 && d.GameOwner == null || d.TransitionSequence == 2 && d.GameOwner != null),
                _ => false
            };
        }
        private static bool Acquisition(SessionAcquisitionEvidence a) => a != null && Lease(a.Pending) && a.Pending.Document.State == SessionLeasePhase.LAUNCH_PENDING && a.Barrier?.Head == a.Pending.Head;
        private static bool Receipt(SessionTransitionReceipt r)
        {
            if (r == null || !Correlation(r.Correlation) || !Lease(r.Parent) || !Lease(r.Child)) return false;
            var p = r.Parent.Document; var d = r.Child.Document;
            if (p.Binding != d.Binding || r.Correlation.Binding != null && r.Correlation.Binding != p.Binding || p.State == SessionLeasePhase.CLOSED ||
                d.TransitionSequence != p.TransitionSequence + 1 || d.ParentTransitionId != p.TransitionId || r.Child.Head.Sha256 == r.Parent.Head.Sha256) return false;
            return r.Operation switch {
                SessionOperation.CLAIM => p.State == SessionLeasePhase.LAUNCH_PENDING && d.State == SessionLeasePhase.GAME_OWNED && r.Barrier is SessionBarrier.ActiveInstalled a && a.Head == r.Child.Head,
                SessionOperation.CLOSE => d.State == SessionLeasePhase.CLOSED && d.GameOwner == p.GameOwner && r.Barrier is SessionBarrier.ActiveRemoved a && a.ExpectedParent == r.Parent.Head,
                _ => false
            };
        }
        private static bool Profile(SessionProfileRecoveryEvidence p, SessionCorrelation c) => p != null && p.ProfileId == "hollow-knight" && p.Correlation == c;
        private static bool OwnerEvidence(SessionOwnerEvidence e, SessionCorrelation c, VerifiedSessionLease p) => e != null && e.Correlation == c && e.Binding == p.Document.Binding &&
            e.ExpectedOwner == (p.Document.GameOwner ?? p.Document.Binding.LauncherOwner) && e.Liveness >= SessionLiveness.UNKNOWN && e.Liveness <= SessionLiveness.DEAD &&
            (e.Liveness == SessionLiveness.ALIVE ? e.AliveOwner == e.ExpectedOwner : e.AliveOwner == null);
        private static bool Target(SessionTargetProcess t)
        {
            if (t == null || !Match(t.PackageName, "[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z][a-zA-Z0-9_]*)+", 255) || t.PackageName.Length < 3 || t.ProcessName == null || t.ProcessName.Length > 255) return false;
            return t.ProcessName == t.PackageName || t.ProcessName.StartsWith(t.PackageName + ":", System.StringComparison.Ordinal) && Match(t.ProcessName.Substring(t.PackageName.Length + 1), "[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z][a-zA-Z0-9_]*)*", 255);
        }
        private static bool TargetEvidence(SessionTargetEvidence e, SessionCorrelation c, VerifiedSessionLease p) => e != null && e.Correlation == c && e.Binding == p.Document.Binding && Target(e.Target) &&
            e.Presence >= SessionPresence.UNKNOWN && e.Presence <= SessionPresence.ABSENT && (e.Presence == SessionPresence.PRESENT ? Owner(e.PresentOwner) : e.PresentOwner == null);
        private static bool Scoped(VerifiedSessionLease p, SessionCorrelation c) => Lease(p) && p.Document.State != SessionLeasePhase.CLOSED && (c.Binding == null || p.Document.Binding == c.Binding);
        private static bool Liveness(VerifiedSessionLease p, SessionOwnerEvidence o, SessionTargetEvidence t, SessionCorrelation c) => OwnerEvidence(o, c, p) &&
            (p.Document.State == SessionLeasePhase.LAUNCH_PENDING ? TargetEvidence(t, c, p) : t == null);
        private static SessionMutationGate RecoveryGate(SessionRecoveryResult result, SessionCorrelation c)
        {
            if (!Correlation(c)) return SessionMutationGate.UNKNOWN;
            if (result is SessionRecoveryResult.NoOpenLease n) return Profile(n.Profile, c) ? SessionMutationGate.CLEAR : SessionMutationGate.UNKNOWN;
            if (result is SessionRecoveryResult.RetainedOpenLease r && Scoped(r.Lease, c) && r.Barrier?.Head == r.Lease.Head && Liveness(r.Lease, r.Owner, r.Target, c))
                return r.Owner.Liveness == SessionLiveness.ALIVE || r.Target?.Presence == SessionPresence.PRESENT ? SessionMutationGate.ACTIVE : SessionMutationGate.UNKNOWN;
            if (result is SessionRecoveryResult.RecoveredClosed x && Scoped(x.Parent, c) && Receipt(x.Receipt) && x.Receipt.Operation == SessionOperation.CLOSE && x.Receipt.Correlation == c && x.Receipt.Parent == x.Parent &&
                Liveness(x.Parent, x.Owner, x.Target, c) && x.Owner.Liveness == SessionLiveness.DEAD && (x.Target == null || x.Target.Presence == SessionPresence.ABSENT) && Profile(x.Profile, c) &&
                x.Receipt.Child.Document.CloseReason == (x.Parent.Document.State == SessionLeasePhase.LAUNCH_PENDING ? "RECOVERY_LAUNCHER_DEAD" : "RECOVERY_GAME_OWNER_DEAD")) return SessionMutationGate.CLEAR;
            return SessionMutationGate.UNKNOWN;
        }
        private static bool RecoveryShape(SessionRecoveryResult r, SessionCorrelation c)
        {
            if (r is SessionRecoveryResult.Deferred d) return Failure(d.Code);
            if (r is SessionRecoveryResult.NoOpenLease n) return Profile(n.Profile, c);
            if (r is SessionRecoveryResult.RecoveredClosed) return RecoveryGate(r, c) == SessionMutationGate.CLEAR;
            if (r is SessionRecoveryResult.RetainedOpenLease o) return Scoped(o.Lease, c) && o.Barrier?.Head == o.Lease.Head && Liveness(o.Lease, o.Owner, o.Target, c) &&
                (o.ObservedClaim == null || Receipt(o.ObservedClaim) && o.ObservedClaim.Operation == SessionOperation.CLAIM && o.ObservedClaim.Correlation == c && o.ObservedClaim.Child == o.Lease);
            return false;
        }
        public bool IsStateValid(SessionState s)
        {
            if (s == null || s.Ordinal < 0 || s.RecoveryHighWater < 0 || s.RecoveryHighWater > s.Ordinal || s.ClaimUse < SessionClaimUse.AVAILABLE || s.ClaimUse > SessionClaimUse.REJECTED || s.Gate < SessionMutationGate.UNKNOWN || s.Gate > SessionMutationGate.CLEAR) return false;
            var b = s.Acquisition?.Pending?.Document?.Binding;
            if (s.PinnedOwner != null && !Owner(s.PinnedOwner) || s.PinnedCloseReason != null && !Reason(s.PinnedCloseReason)) return false;
            if (s.Acquisition == null)
            { if (s.Lease != null || s.ClaimReceipt != null || s.ClosedReceipt != null || s.PinnedOwner != null || s.PinnedCloseReason != null || s.ClaimUse != SessionClaimUse.AVAILABLE) return false; }
            else
            {
                if (!Acquisition(s.Acquisition) || !Lease(s.Lease) || s.Lease.Document.Binding != b) return false;
                if (s.ClaimReceipt != null && (!Receipt(s.ClaimReceipt) || s.ClaimReceipt.Operation != SessionOperation.CLAIM || s.ClaimReceipt.Parent != s.Acquisition.Pending ||
                    s.ClaimReceipt.Correlation.Binding != b || s.ClaimReceipt.Correlation.Ordinal > s.Ordinal || s.ClaimReceipt.Child.Document.GameOwner != s.PinnedOwner)) return false;
                if (s.ClosedReceipt != null && (!Receipt(s.ClosedReceipt) || s.ClosedReceipt.Operation != SessionOperation.CLOSE || s.ClosedReceipt.Parent != (s.ClaimReceipt?.Child ?? s.Acquisition.Pending) ||
                    s.ClosedReceipt.Correlation.Binding != b || s.ClosedReceipt.Correlation.Ordinal > s.Ordinal ||
                    s.RecoveryClosure == null && s.ClosedReceipt.Child.Document.CloseReason != s.PinnedCloseReason)) return false;
                if (s.Lease != (s.ClosedReceipt?.Child ?? s.ClaimReceipt?.Child ?? s.Acquisition.Pending)) return false;
                if (s.ClaimUse == SessionClaimUse.CONSUMED && s.ClaimReceipt == null || s.ClaimReceipt != null && s.ClaimUse != SessionClaimUse.CONSUMED && s.ClaimUse != SessionClaimUse.REJECTED) return false;
            }
            if (s.Recovery != null && (!Correlation(s.Recovery.Correlation) || s.Recovery.Correlation.Binding != b || s.Recovery.Correlation.Ordinal > s.RecoveryHighWater || !RecoveryShape(s.Recovery.Result, s.Recovery.Correlation))) return false;
            if (s.RecoveryHighWater > 0 && s.Recovery == null && s.Pending is not SessionCommand.RecoverExistingSessionStore) return false;
            if (s.Pending != null)
            {
                if (!Correlation(s.Pending.Correlation) || s.Pending.Correlation.Binding != b || s.Pending.Correlation.Ordinal != s.Ordinal || s.Gate != SessionMutationGate.UNKNOWN) return false;
                switch (s.Pending)
                {
                    case SessionCommand.ClaimExistingBinding c:
                        if (b == null || c.Binding != b || c.Owner != s.PinnedOwner || !Owner(c.Owner) || s.ClaimUse != SessionClaimUse.IN_FLIGHT || s.PinnedCloseReason != null || s.Lease.Document.State != SessionLeasePhase.LAUNCH_PENDING) return false; break;
                    case SessionCommand.CloseExistingBinding c:
                        if (b == null || c.Binding != b || c.Reason != s.PinnedCloseReason || !Reason(c.Reason) || s.Lease.Document.State == SessionLeasePhase.CLOSED) return false; break;
                    case SessionCommand.RecoverExistingSessionStore c:
                        if (c.ExpectedBinding != b || s.RecoveryHighWater != s.Ordinal) return false; break;
                    default: return false;
                }
            }
            if (s.ClaimAttempt != null && (b == null || !Correlation(s.ClaimAttempt.Correlation) || s.ClaimAttempt.Correlation.Binding != b || s.ClaimAttempt.Binding != b ||
                s.ClaimAttempt.Owner != s.PinnedOwner || s.ClaimAttempt.Correlation.Ordinal > s.Ordinal)) return false;
            if ((s.PinnedOwner != null) != (s.ClaimAttempt != null)) return false;
            if (s.Pending is SessionCommand.ClaimExistingBinding && s.Pending != s.ClaimAttempt) return false;
            if (s.ClaimUse == SessionClaimUse.CONSUMED && s.ClaimAttempt?.Correlation != s.ClaimReceipt?.Correlation) return false;
            if (s.ClaimReceipt != null && s.RecoveryHighWater >= s.ClaimReceipt.Correlation.Ordinal && s.ClaimRecoveryFence == null) return false;
            if (s.ClaimRecoveryFence != null)
            {
                var fence = s.ClaimRecoveryFence;
                if (!Correlation(fence.RecoveryCorrelation) || fence.RecoveryCorrelation.Binding != b || fence.ClaimReceipt == null || fence.ClaimReceipt != s.ClaimReceipt ||
                    fence.RecoveryCorrelation.Ordinal > s.RecoveryHighWater || fence.RecoveryCorrelation.Ordinal < fence.ClaimReceipt.Correlation.Ordinal) return false;
                if (fence.RecoveryCorrelation.Ordinal == fence.ClaimReceipt.Correlation.Ordinal ? s.ClaimUse != SessionClaimUse.REJECTED : s.ClaimUse != SessionClaimUse.CONSUMED) return false;
            }
            if (s.ClaimReceipt != null && s.ClaimUse == SessionClaimUse.REJECTED && (s.ClaimRecoveryFence == null ||
                s.ClaimRecoveryFence.RecoveryCorrelation != s.ClaimReceipt.Correlation || s.ClaimAttempt == null || s.ClaimAttempt.Correlation.Ordinal >= s.ClaimReceipt.Correlation.Ordinal)) return false;
            if (s.CloseAttempt != null && (b == null || !Correlation(s.CloseAttempt.Correlation) || s.CloseAttempt.Correlation.Binding != b || s.CloseAttempt.Binding != b ||
                s.CloseAttempt.Reason != s.PinnedCloseReason || s.CloseAttempt.Correlation.Ordinal > s.Ordinal || s.CloseAttempt.Correlation.Ordinal <= (s.ClaimAttempt?.Correlation.Ordinal ?? 0))) return false;
            if ((s.PinnedCloseReason != null) != (s.CloseAttempt != null)) return false;
            if (s.Pending is SessionCommand.CloseExistingBinding && s.Pending != s.CloseAttempt) return false;
            if (s.ClosedReceipt != null && s.RecoveryClosure == null && s.CloseAttempt?.Correlation != s.ClosedReceipt.Correlation) return false;
            if (s.RecoveryClosure != null)
            {
                var closure = s.RecoveryClosure;
                if (b == null || s.ClosedReceipt == null || !Correlation(closure.Correlation) || closure.Correlation.Binding != b ||
                    closure.Result is not SessionRecoveryResult.RecoveredClosed recovered || !RecoveryShape(recovered, closure.Correlation) ||
                    recovered.Receipt != s.ClosedReceipt || closure.Correlation.Ordinal > s.RecoveryHighWater || s.Recovery == null || s.Recovery.Correlation.Ordinal < closure.Correlation.Ordinal ||
                    s.CloseAttempt != null && s.CloseAttempt.Correlation.Ordinal >= closure.Correlation.Ordinal) return false;
            }
            if (s.ClosedReceipt != null && s.ClosedReceipt.Correlation.Ordinal <= (s.ClaimReceipt?.Correlation.Ordinal ?? 0)) return false;
            if (s.Recovery != null && s.Pending is not SessionCommand.RecoverExistingSessionStore && s.Recovery.Correlation.Ordinal != s.RecoveryHighWater) return false;
            if (s.Recovery != null && b != null)
            {
                switch (s.Recovery.Result)
                {
                    case SessionRecoveryResult.NoOpenLease:
                        if (s.Lease.Document.State != SessionLeasePhase.CLOSED || s.ClosedReceipt.Correlation.Ordinal >= s.Recovery.Correlation.Ordinal) return false; break;
                    case SessionRecoveryResult.RecoveredClosed x:
                        if (x.Receipt != s.ClosedReceipt || x.Receipt.Child != s.Lease || s.RecoveryClosure != s.Recovery) return false; break;
                    case SessionRecoveryResult.RetainedOpenLease r:
                        if (r.Lease != s.Acquisition.Pending && r.Lease != s.ClaimReceipt?.Child) return false;
                        if (r.ObservedClaim != null && (r.ObservedClaim != s.ClaimReceipt || s.ClaimUse != SessionClaimUse.REJECTED)) return false;
                        if (s.ClaimReceipt != null && (r.Lease.Document.State == SessionLeasePhase.LAUNCH_PENDING
                            ? s.ClaimReceipt.Correlation.Ordinal <= s.Recovery.Correlation.Ordinal
                            : r.ObservedClaim == null && s.ClaimReceipt.Correlation.Ordinal >= s.Recovery.Correlation.Ordinal)) return false;
                        if (s.ClosedReceipt != null && s.ClosedReceipt.Correlation.Ordinal <= s.Recovery.Correlation.Ordinal) return false; break;
                }
            }
            if (s.Pending != null && s.Pending.Correlation.Ordinal <= (s.ClosedReceipt?.Correlation.Ordinal ?? s.ClaimReceipt?.Correlation.Ordinal ?? 0)) return false;
            if ((s.ClaimUse == SessionClaimUse.IN_FLIGHT) != (s.Pending is SessionCommand.ClaimExistingBinding)) return false;
            if (s.Gate == SessionMutationGate.CLEAR && (s.Pending != null || s.Recovery == null || s.Recovery.Correlation.Ordinal != s.RecoveryHighWater || RecoveryGate(s.Recovery.Result, s.Recovery.Correlation) != SessionMutationGate.CLEAR || b != null && s.Lease.Document.State != SessionLeasePhase.CLOSED)) return false;
            if (s.Gate == SessionMutationGate.ACTIVE && (s.Pending != null || s.Lease?.Document.State == SessionLeasePhase.CLOSED || b == null && (s.Recovery == null || RecoveryGate(s.Recovery.Result, s.Recovery.Correlation) != SessionMutationGate.ACTIVE))) return false;
            return true;
        }
        // This is only session eligibility. Catalog/transaction/interlock/resource gates and
        // per-commit locked-store validation remain mandatory and independent.
        public bool SessionWritesEligible(SessionState s) => IsStateValid(s) && s.ClaimUse == SessionClaimUse.CONSUMED && s.ClaimReceipt != null &&
            s.ClaimRecoveryFence == null && s.ClaimReceipt.Child == s.Lease && s.ClaimReceipt.Correlation.Ordinal > s.RecoveryHighWater && s.Pending == null && s.PinnedCloseReason == null && s.Gate == SessionMutationGate.ACTIVE;
        public SessionDecision Decide(SessionState s, SessionEvent input)
        {
            if (!IsStateValid(s)) return new(s, "INVALID_STATE");
            var b = s.Acquisition?.Pending.Document.Binding;
            if (input is SessionEvent.ClaimCompleted cc) return Complete(s, cc.Correlation, cc.Result, true);
            if (input is SessionEvent.CloseCompleted xc) return Complete(s, xc.Correlation, xc.Result, false);
            if (input is SessionEvent.RecoveryCompleted rc) return Recover(s, rc);
            if (s.Pending != null) return new(s, "IN_FLIGHT");
            if (input is SessionEvent.BindIssuedPending bind)
            {
                if (s.Acquisition != null || s.Ordinal != 0 || s.Recovery != null || !Acquisition(bind.Evidence)) return new(s, "BINDING_REJECTED");
                return new(s with { Acquisition = bind.Evidence, Lease = bind.Evidence.Pending, Gate = SessionMutationGate.ACTIVE }, "BOUND");
            }
            if (input is SessionEvent.CloseRequested && s.RecoveryClosure != null) return new(s, "REQUEST_REJECTED");
            if (input is SessionEvent.CloseRequested replay && b != null && replay.Binding == b && replay.Reason == s.PinnedCloseReason && s.ClosedReceipt != null)
                return new(s, "CLOSED_REPLAY", CompletedClose: s.ClosedReceipt);
            if (s.Ordinal == long.MaxValue) return new(s, "ORDINAL_EXHAUSTED");
            var cor = new SessionCorrelation(b, s.Ordinal + 1); SessionCommand command;
            if (input is SessionEvent.ClaimRequested claim && b != null && claim.Binding == b && Owner(claim.Owner) && s.ClaimUse == SessionClaimUse.AVAILABLE && s.PinnedCloseReason == null && s.Lease.Document.State == SessionLeasePhase.LAUNCH_PENDING && (s.PinnedOwner == null || s.PinnedOwner == claim.Owner))
            {
                var claimCommand = new SessionCommand.ClaimExistingBinding(cor, b, claim.Owner);
                return new(s with { Ordinal = cor.Ordinal, Pending = claimCommand, ClaimAttempt = claimCommand, PinnedOwner = claim.Owner, ClaimUse = SessionClaimUse.IN_FLIGHT, Gate = SessionMutationGate.UNKNOWN }, "CLAIM_REQUESTED", claimCommand);
            }
            if (input is SessionEvent.CloseRequested close && b != null && close.Binding == b && Reason(close.Reason) && s.Lease.Document.State != SessionLeasePhase.CLOSED && (s.PinnedCloseReason == null || s.PinnedCloseReason == close.Reason))
            {
                var closeCommand = new SessionCommand.CloseExistingBinding(cor, b, close.Reason);
                return new(s with { Ordinal = cor.Ordinal, Pending = closeCommand, CloseAttempt = closeCommand, PinnedCloseReason = close.Reason, Gate = SessionMutationGate.UNKNOWN }, "CLOSE_REQUESTED", closeCommand);
            }
            if (input is SessionEvent.RecoveryRequested)
            {
                command = new SessionCommand.RecoverExistingSessionStore(cor, b);
                var fence = s.ClaimRecoveryFence ?? (s.ClaimReceipt == null ? null : new SessionClaimRecoveryFence(cor, s.ClaimReceipt));
                return new(s with { Ordinal = cor.Ordinal, RecoveryHighWater = cor.Ordinal, ClaimRecoveryFence = fence, Pending = command, Gate = SessionMutationGate.UNKNOWN }, "RECOVERY_REQUESTED", command);
            }
            return new(s, "REQUEST_REJECTED");
        }
        private SessionDecision Complete(SessionState s, SessionCorrelation c, SessionTransitionResult result, bool claim)
        {
            if (c == null || s.Pending?.Correlation != c || (claim ? s.Pending is not SessionCommand.ClaimExistingBinding : s.Pending is not SessionCommand.CloseExistingBinding)) return new(s, "STALE_COMPLETION");
            if (result is SessionTransitionResult.Durable d && Receipt(d.Receipt) && d.Receipt.Correlation == c && d.Receipt.Parent == s.Lease &&
                d.Receipt.Operation == (claim ? SessionOperation.CLAIM : SessionOperation.CLOSE) && (claim ? d.Receipt.Child.Document.GameOwner == s.PinnedOwner : d.Receipt.Child.Document.CloseReason == s.PinnedCloseReason))
            {
                var next = claim ? s with { Pending = null, Lease = d.Receipt.Child, ClaimReceipt = d.Receipt, ClaimUse = SessionClaimUse.CONSUMED, Gate = SessionMutationGate.ACTIVE } :
                    s with { Pending = null, Lease = d.Receipt.Child, ClosedReceipt = d.Receipt, Gate = SessionMutationGate.UNKNOWN };
                return new(next, "DURABLE", CompletedClose: claim ? null : d.Receipt);
            }
            // Invalid evidence is not an operational failure: keep serialization and the
            // exact pending correlation available for a later trustworthy completion.
            if (result is not SessionTransitionResult.Failed f || !Failure(f.Code)) return new(s, "INVALID_COMPLETION");
            var retry = f.Code == SessionFailure.DURABILITY_UNAVAILABLE || f.Code == SessionFailure.INDETERMINATE;
            return new(s with { Pending = null, Gate = SessionMutationGate.UNKNOWN, ClaimUse = claim ? retry ? SessionClaimUse.AVAILABLE : SessionClaimUse.REJECTED : s.ClaimUse }, retry ? "RETRYABLE" : "REJECTED");
        }
        private SessionDecision Recover(SessionState s, SessionEvent.RecoveryCompleted e)
        {
            if (e.Correlation == null || s.Pending is not SessionCommand.RecoverExistingSessionStore || s.Pending.Correlation != e.Correlation) return new(s, "STALE_COMPLETION");
            var c = e.Correlation; var result = e.Result; var valid = RecoveryShape(result, c);
            if (valid && s.Acquisition != null)
            {
                if (result is SessionRecoveryResult.NoOpenLease) valid = s.Lease.Document.State == SessionLeasePhase.CLOSED;
                if (result is SessionRecoveryResult.RecoveredClosed x) valid = x.Parent == s.Lease;
                if (result is SessionRecoveryResult.RetainedOpenLease r) valid = r.Lease == s.Lease || r.ObservedClaim != null && r.ObservedClaim.Parent == s.Lease && r.ObservedClaim.Child.Document.GameOwner == s.PinnedOwner;
            }
            if (!valid) return new(s, "INVALID_COMPLETION");
            var next = s with { Pending = null, Recovery = new(c, result), Gate = RecoveryGate(result, c) };
            if (s.Acquisition != null && result is SessionRecoveryResult.RecoveredClosed closed)
                next = next with { Lease = closed.Receipt.Child, ClosedReceipt = closed.Receipt, RecoveryClosure = next.Recovery };
            if (s.Acquisition != null && result is SessionRecoveryResult.RetainedOpenLease observed && observed.Lease != s.Lease)
                next = next with { Lease = observed.Lease, ClaimReceipt = observed.ObservedClaim, ClaimUse = SessionClaimUse.REJECTED, ClaimRecoveryFence = new(c, observed.ObservedClaim) };
            return IsStateValid(next) ? new(next, "RECOVERED") : new(s, "INVALID_COMPLETION");
        }
    }
}
