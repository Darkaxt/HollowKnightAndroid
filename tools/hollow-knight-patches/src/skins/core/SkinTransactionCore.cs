using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DualSouls.Skins.HollowKnight.Core
{
    /// <summary>Pure value reducer. Caller owns authoritative binding and canonical verified-head events.</summary>
    public sealed class SkinTransactionCore
    {
        // Read-only structural validation; grants no execution or recovery authority.
        public bool IsStateValid(TransactionState state) => Valid(state);

        public TransactionDecision Decide(TransactionState s, TransactionEvent ev)
        {
            if (!Valid(s)) return D(s, "invalid-state");
            if (ev == null) return D(s, "invalid-event");
            if (s.Phase == TransactionPhase.BLOCKED || s.Phase == TransactionPhase.COMMITTED) return D(s, "terminal");
            if (ev is TransactionEvent.Begin begin)
            {
                if (s.Phase != TransactionPhase.IDLE) return D(s, "transaction-in-progress");
                var e = begin.Envelope;
                if (!EnvelopeValid(e) || e.Binding != s.Binding || e.Prior != s.Activation) return D(s, "invalid-envelope");
                return D(s with { Phase = TransactionPhase.PREPARING, Envelope = e }, "prepare", new SkinCommand.Prepare(e, e.Target.Active));
            }
            var c = Correlation(ev);
            if (s.Envelope == null || c == null || !Uuid(c.TransactionId) || !Token(c.Binding) ||
                c.TransactionId != s.Envelope.TransactionId || c.Binding != s.Binding) return D(s, "stale-correlation");
            var envelope = s.Envelope;
            switch (ev)
            {
                case TransactionEvent.Prepared _ when s.Phase == TransactionPhase.PREPARING:
                    return D(s with { Phase = TransactionPhase.PREPARED }, "arm", new SkinCommand.Arm(envelope));
                case TransactionEvent.ArmCommitted arm when s.Phase == TransactionPhase.PREPARED:
                    var interlock = Armed(envelope);
                    if (!Receipt(arm.CommitReceipt, envelope.BaseGenerationId, envelope.BaseGenerationSha256) ||
                        !Head(arm.VerifiedHead, arm.CommitReceipt, envelope.Prior, interlock)) return D(s, "stale-receipt");
                    return D(s with { Phase = TransactionPhase.ARMED, Interlock = interlock, ArmCommitReceipt = arm.CommitReceipt },
                        "apply", new SkinCommand.Apply(c));
                case TransactionEvent.ApplyVerified _ when s.Phase == TransactionPhase.ARMED:
                    return Closure(s, c, envelope.Target, TransactionPhase.APPLIED);
                case TransactionEvent.ApplyFailed fail when s.Phase == TransactionPhase.ARMED:
                    return Code(fail.Code) ? Reverse(s, c, fail.Code) : D(s, "invalid-code");
                case TransactionEvent.RollbackVerified rollback when s.Phase == TransactionPhase.ROLLBACK_PENDING:
                    if (rollback.FreshBinding == envelope.PriorEstablishedOnBinding) return D(s, "invalid-binding-proof");
                    return Closure(s, c, PriorClosure(envelope), TransactionPhase.ROLLED_BACK);
                case TransactionEvent.RollbackFailed fail when s.Phase == TransactionPhase.ROLLBACK_PENDING:
                    if (!Code(fail.Code)) return D(s, "invalid-code");
                    var failedLock = s.Interlock with { State = InterlockState.ROLLBACK_FAILED,
                        OriginalFailure = s.OriginalFailure, RollbackFailure = fail.Code };
                    var persisted = FollowsArm(s, fail.PersistedFailureReceipt) &&
                        Head(fail.VerifiedHead, fail.PersistedFailureReceipt, envelope.Prior, failedLock);
                    return D(s with { Phase = TransactionPhase.BLOCKED, RollbackFailure = fail.Code,
                        Interlock = persisted ? failedLock : s.Interlock, FailureReceipt = persisted ? fail.PersistedFailureReceipt : null }, "rollback-failed");
                case TransactionEvent.CompletionCommitted complete when Pending(s):
                    if (!FollowsArm(s, complete.CommitReceipt) || !Head(complete.VerifiedHead, complete.CommitReceipt,
                        s.PendingClosure, RotationInterlock.Clear())) return D(s, "stale-receipt");
                    return D(s with { Phase = TransactionPhase.COMMITTED, Interlock = RotationInterlock.Clear(),
                        Activation = s.PendingClosure, CompletionReceipt = complete.CommitReceipt }, "committed");
                case TransactionEvent.CompletionRejected rejected when Pending(s):
                    if (!Code(rejected.Code)) return D(s, "invalid-code");
                    return s.Phase == TransactionPhase.APPLIED ? Reverse(s, c, rejected.Code) :
                        D(s with { Phase = TransactionPhase.BLOCKED, RollbackFailure = rejected.Code }, "rollback-closure-rejected");
                case TransactionEvent.CompletionIndeterminate _ when Pending(s):
                    return D(s with { Phase = TransactionPhase.BLOCKED }, "completion-indeterminate");
                default: return D(s, "stale-phase");
            }
        }

        private static TransactionDecision Closure(TransactionState s, TransactionCorrelation c, ActivationSnapshot a, TransactionPhase phase) =>
            D(s with { Phase = phase, PendingClosure = a }, "commit-closure",
                new SkinCommand.Commit(c, s.ArmCommitReceipt.NewGenerationId, s.ArmCommitReceipt.NewGenerationSha256, a));
        private static TransactionDecision Reverse(TransactionState s, TransactionCorrelation c, string code) =>
            D(s with { Phase = TransactionPhase.ROLLBACK_PENDING, PendingClosure = null, OriginalFailure = code }, "rollback", new SkinCommand.Rollback(c));
        private static bool Pending(TransactionState s) => s.Phase == TransactionPhase.APPLIED || s.Phase == TransactionPhase.ROLLED_BACK;
        private static TransactionCorrelation Correlation(TransactionEvent e) => e switch
        {
            TransactionEvent.Prepared v => v.Correlation, TransactionEvent.ArmCommitted v => v.Correlation,
            TransactionEvent.ApplyVerified v => v.Correlation, TransactionEvent.ApplyFailed v => v.Correlation,
            TransactionEvent.RollbackVerified v => v.Correlation, TransactionEvent.RollbackFailed v => v.Correlation,
            TransactionEvent.CompletionCommitted v => v.Correlation, TransactionEvent.CompletionRejected v => v.Correlation,
            TransactionEvent.CompletionIndeterminate v => v.Correlation, _ => null
        };
        private static RotationInterlock Armed(TransactionEnvelope e) => new(InterlockState.ARMED, e.TransactionId, e.Operation,
            e.BaseGenerationId, e.BaseGenerationSha256, e.Prior, e.Target, e.Binding, e.PriorEstablishedOnBinding, null, null);
        private static ActivationSnapshot PriorClosure(TransactionEnvelope e) =>
            !e.PriorEstablishedOnBinding && e.Prior.Active is ActiveVisual.Pack ? e.Prior with { SkinStamp = e.Prior.SkinStamp + 1 } : e.Prior;
        private static bool FollowsArm(TransactionState s, RegistryCommitReceipt r) =>
            s.ArmCommitReceipt != null && Receipt(r, s.ArmCommitReceipt.NewGenerationId, s.ArmCommitReceipt.NewGenerationSha256) &&
            r.NewGenerationId != s.Envelope.BaseGenerationId && r.NewGenerationSha256 != s.Envelope.BaseGenerationSha256;
        private static bool Receipt(RegistryCommitReceipt r, string id, string digest) => r != null &&
            Uuid(r.ExpectedGenerationId) && Digest(r.ExpectedGenerationSha256) && Uuid(r.NewGenerationId) && Digest(r.NewGenerationSha256) &&
            r.ExpectedGenerationId == id && r.ExpectedGenerationSha256 == digest && r.NewGenerationId != id && r.NewGenerationSha256 != digest;
        private static bool Head(VerifiedRegistryHead h, RegistryCommitReceipt r, ActivationSnapshot a, RotationInterlock l) =>
            h != null && r != null && h.GenerationId == r.NewGenerationId && h.GenerationSha256 == r.NewGenerationSha256 && h.Activation == a && h.Interlock == l;

        // Exact CAS equality includes receipt hashes. Visual equality excludes the import receipt identity.
        private static bool SameVisual(ActiveVisual a, ActiveVisual b) => a is ActiveVisual.Vanilla && b is ActiveVisual.Vanilla ||
            a is ActiveVisual.Pack x && b is ActiveVisual.Pack y && x.Id == y.Id && x.TreeSha256 == y.TreeSha256 && x.ContentSha256 == y.ContentSha256;
        private static bool EnvelopeValid(TransactionEnvelope e)
        {
            if (e == null || !Uuid(e.TransactionId) || !Uuid(e.BaseGenerationId) || !Digest(e.BaseGenerationSha256) ||
                !Token(e.Binding) || !Snapshot(e.Prior) || !Snapshot(e.Target) || e.Prior.SkinStamp == long.MaxValue ||
                e.Target.SkinStamp != e.Prior.SkinStamp + 1) return false;
            var p = e.Prior; var t = e.Target;
            // History is not live proof. In particular unestablished vanilla still requires MODE_OFF restoration.
            if (SameVisual(p.Active, t.Active) && e.PriorEstablishedOnBinding) return false;
            bool SelectedTarget() => t.Active is ActiveVisual.Pack pack && t.SelectedPackId == pack.Id;
            bool EstablishModeTarget() => !e.PriorEstablishedOnBinding && t.Mode == p.Mode && t.SelectedPackId == p.SelectedPackId &&
                (t.Mode == SkinMode.ON && SelectedTarget() || t.Mode == SkinMode.ROTATE && t.Active is ActiveVisual.Pack && t.Active == p.Active);
            return e.Operation switch
            {
                SkinOperationKind.MODE_ON => p.Mode == SkinMode.OFF && t.Mode == SkinMode.ON &&
                    t.SelectedPackId == p.SelectedPackId && SelectedTarget(),
                SkinOperationKind.MODE_OFF => p.Mode == SkinMode.ROTATE && t.Mode == SkinMode.OFF &&
                    t.Active is ActiveVisual.Vanilla && t.SelectedPackId == p.SelectedPackId,
                SkinOperationKind.DEATH_ROTATION => p.Mode == SkinMode.ROTATE && t.Mode == SkinMode.ROTATE &&
                    SelectedTarget() && !SameVisual(p.Active, t.Active),
                SkinOperationKind.REBIND_APPLY => EstablishModeTarget(),
                SkinOperationKind.STARTUP_APPLY => EstablishModeTarget(),
                _ => false
            };
        }
        private static bool Snapshot(ActivationSnapshot a) => a != null && a.SkinStamp >= 0 &&
            (a.Mode == SkinMode.OFF || a.Mode == SkinMode.ON || a.Mode == SkinMode.ROTATE) &&
            (a.SelectedPackId == null || PackId(a.SelectedPackId)) &&
            (a.Active is ActiveVisual.Vanilla || a.Active is ActiveVisual.Pack p &&
                PackId(p.Id) && Digest(p.TreeSha256) && Digest(p.ContentSha256) && Digest(p.ImportReceiptSha256));
        private static bool Valid(TransactionState s)
        {
            if (s == null || !Token(s.Binding) || !Snapshot(s.Activation) || s.Interlock == null ||
                (s.OriginalFailure != null && !Code(s.OriginalFailure)) || (s.RollbackFailure != null && !Code(s.RollbackFailure))) return false;
            if (s.Phase == TransactionPhase.IDLE) return s.Interlock == RotationInterlock.Clear() && s.Envelope == null &&
                s.ArmCommitReceipt == null && s.PendingClosure == null && s.OriginalFailure == null && s.RollbackFailure == null &&
                s.CompletionReceipt == null && s.FailureReceipt == null;
            var e = s.Envelope;
            if (!EnvelopeValid(e) || e.Binding != s.Binding) return false;
            if (s.Phase == TransactionPhase.PREPARING || s.Phase == TransactionPhase.PREPARED)
                return s.Interlock == RotationInterlock.Clear() && s.Activation == e.Prior && s.ArmCommitReceipt == null &&
                    s.PendingClosure == null && s.OriginalFailure == null && s.RollbackFailure == null && s.CompletionReceipt == null && s.FailureReceipt == null;
            if (!Receipt(s.ArmCommitReceipt, e.BaseGenerationId, e.BaseGenerationSha256)) return false;
            var closure = s.OriginalFailure == null ? e.Target : PriorClosure(e);
            if (s.Phase == TransactionPhase.COMMITTED) return s.Interlock == RotationInterlock.Clear() &&
                s.PendingClosure == closure && s.Activation == closure && FollowsArm(s, s.CompletionReceipt) && s.FailureReceipt == null && s.RollbackFailure == null;
            if (s.Activation != e.Prior || s.CompletionReceipt != null) return false;
            if (s.Phase == TransactionPhase.BLOCKED)
            {
                if (s.PendingClosure != null && s.PendingClosure != closure) return false;
                if (s.PendingClosure == null && (s.OriginalFailure == null || s.RollbackFailure == null)) return false;
                return s.FailureReceipt == null ? s.Interlock == Armed(e) :
                    s.PendingClosure == null && s.OriginalFailure != null && s.RollbackFailure != null && FollowsArm(s, s.FailureReceipt) &&
                    s.Interlock == (Armed(e) with { State = InterlockState.ROLLBACK_FAILED, OriginalFailure = s.OriginalFailure, RollbackFailure = s.RollbackFailure });
            }
            if (s.Interlock != Armed(e) || s.FailureReceipt != null || s.RollbackFailure != null) return false;
            return s.Phase switch
            {
                TransactionPhase.ARMED => s.PendingClosure == null && s.OriginalFailure == null,
                TransactionPhase.APPLIED => s.PendingClosure == e.Target && s.OriginalFailure == null,
                TransactionPhase.ROLLBACK_PENDING => s.PendingClosure == null && s.OriginalFailure != null,
                TransactionPhase.ROLLED_BACK => s.PendingClosure == PriorClosure(e) && s.OriginalFailure != null,
                _ => false
            };
        }
        // Registry syntax, without codec/catalog/IO dependencies.
        private static bool Match(string s, string pattern, int max) => s != null && s.Length <= max && Regex.IsMatch(s, "\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant);
        private static bool Uuid(string s) => Match(s, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", 36);
        private static bool Digest(string s) => Match(s, "[0-9a-f]{64}", 64);
        private static bool PackId(string s) => Match(s, "[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?", 64);
        private static bool Code(string s) => Match(s, "[A-Z][A-Z0-9_]{0,127}", 128);
        private static bool Token(SkinBindingToken t)
        {
            var s = t?.Value;
            if (string.IsNullOrEmpty(s) || s.Length > 512 || char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1])) return false;
            var count = 0;
            for (var i = 0; i < s.Length; i++, count++)
            {
                var c = s[i];
                if (char.IsControl(c) || c == '؜' || c == '‎' || c == '‏' ||
                    c >= '‪' && c <= '‮' || c >= '⁦' && c <= '⁩') return false;
                if (char.IsHighSurrogate(c)) { if (++i >= s.Length || !char.IsLowSurrogate(s[i])) return false; }
                else if (char.IsLowSurrogate(c)) return false;
            }
            // Retain every existing bound/scalar/control/edge-whitespace guard above.
            // The shared validator additionally segments NFKC-inert noncharacters, matching Kotlin.
            return count <= 256 && RotationValues.Text(s, 256);
        }
        private static TransactionDecision D(TransactionState s, string diagnosis, params SkinCommand[] commands) => new(s, diagnosis, commands);
    }
}
