using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Pure resource ownership closure: value evidence only, no executor or transaction writes.
    public sealed class SkinResourceCleanupCore
    {
        private readonly SkinResourceLifetimeCore lifetime = new();
        private readonly SkinResourceCore admission = new();
        private static bool Restored(ResourceLifetimeClosure c, ResourceLifetimePlan p)
        {
            if (c == null || c.Target == null) return false;
            var prior = c.Prior;
            if (prior == null) return p == null;
            if (p == null || p.PlanId != prior.PlanId || new[] { p.PlanId, p.Binding, p.GenerationId, p.Stamp }.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 128)) return false;
            return p.Binding == c.Target.Binding;
        }
        private bool Valid(ResourceCleanupState s)
        {
            if (s == null || !lifetime.IsValidState(s.Lifetime)) return false;
            var l = s.Lifetime; var r = s.Rollback; var h = s.Shutdown;
            // Bind public cleanup evidence to the validated closure before inspecting nested identities.
            if (s.Cancellation != null && s.Cancellation != l.Candidate) return false;
            if (r != null && (!l.CandidateSealed || r.Closure == null || r.Closure != s.Cancellation || !Restored(r.Closure, r.RestoredPrior) ||
                (r.ClonePhase != ResourceRollbackClonePhase.CANDIDATE && r.ClonePhase != ResourceRollbackClonePhase.OLD_PRIOR && r.ClonePhase != ResourceRollbackClonePhase.QUALIFIED) ||
                (r.ClonePhase == ResourceRollbackClonePhase.OLD_PRIOR && (r.Closure.Prior == null || r.Closure.Prior == r.RestoredPrior)) ||
                (r.ClonePhase != ResourceRollbackClonePhase.QUALIFIED && s.PendingDisposals.Count != 0))) return false;
            if (h == null) { if (s.ShutdownClonesQualified || s.ShutdownDisposals.Count != 0) return false; }
            else
            {
                if (!s.TeardownRequested || h.Plan == null || h.Plan != l.Active || h.OperationId <= 0 || h.OperationId >= l.NextOperationId ||
                    l.Candidate != null || l.AwaitingClones != null || l.Retirement != null || s.Cancellation != null || r != null ||
                    s.ShutdownDisposals.Count > SkinResourceCore.PROCESS_ALLOCATIONS || (!s.ShutdownClonesQualified && s.ShutdownDisposals.Count != 0)) return false;
                var shutdownIds = new HashSet<long>(); var shutdownAllocations = new HashSet<long>();
                foreach (var op in s.ShutdownDisposals)
                {
                    if (op == null || op.Shutdown != h || op.OperationId <= h.OperationId || op.OperationId >= l.NextOperationId || !shutdownIds.Add(op.OperationId) || !shutdownAllocations.Add(op.AllocationId)) return false;
                    var a = l.Ledger.Allocations.FirstOrDefault(a => a.Id == op.AllocationId);
                    if (a == null || a.Ownership != ResourceOwnership.OWNED || a.Phase != ResourceAllocationPhase.READY || !a.PlanIds.SequenceEqual(new[] { h.Plan.PlanId }) || l.Ledger.References.Any(v => v.AllocationId == a.Id)) return false;
                }
            }
            var c = s.Cancellation;
            if (c == null) return s.Decoder == null && s.PendingDisposals.Count == 0 && r == null;
            if (c != l.Candidate || l.AwaitingDurableCompletion || s.PendingDisposals.Count > SkinResourceCore.PROCESS_ALLOCATIONS) return false;
            var scratch = l.Ledger.Scratch; var d = s.Decoder;
            if ((scratch == null) != (d == null)) return false;
            if (d != null && (d.Operation == null || d.Operation != scratch.Operation ||
                (d.Outcome != ResourceDecodeOutcome.PENDING && d.Outcome != ResourceDecodeOutcome.CREATED && d.Outcome != ResourceDecodeOutcome.NEVER_CREATED) ||
                (d.Outcome == ResourceDecodeOutcome.CREATED) != scratch.DecodeCompleted)) return false;
            var ids = new HashSet<long>(); var allocations = new HashSet<long>();
            foreach (var op in s.PendingDisposals)
            {
                if (op == null || op.Closure != c || op.OperationId <= c.OperationId || op.OperationId >= l.NextOperationId || !ids.Add(op.OperationId) || !allocations.Add(op.AllocationId)) return false;
                var a = l.Ledger.Allocations.FirstOrDefault(a => a.Id == op.AllocationId);
                if (a == null || a.Ownership != ResourceOwnership.OWNED || a.Phase != ResourceAllocationPhase.READY || !a.PlanIds.SequenceEqual(new[] { c.Target.PlanId }) || scratch?.Operation.AllocationId == a.Id || l.Ledger.References.Any(v => v.AllocationId == a.Id)) return false;
            }
            return true;
        }
        public bool CanBeginCandidate(ResourceCleanupState s) => Valid(s) && !s.TeardownRequested && s.Cancellation == null && lifetime.CanBeginCandidate(s.Lifetime);
        public bool CanApply(ResourceCleanupState s) => Valid(s) && !s.TeardownRequested && s.Cancellation == null && lifetime.CanApply(s.Lifetime);
        public ResourceCleanupDecision Decide(ResourceCleanupState s, ResourceCleanupEvent e)
        {
            ResourceCleanupDecision Reject(string code) => new(s, false, code);
            if (!Valid(s)) return Reject("INVALID_STATE");
            try
            {
                var commands = new List<ResourceCleanupCommand>(); ResourceCleanupState next;
                switch (e)
                {
                    case ResourceCleanupEvent.Teardown:
                        next = Copy(s, teardownRequested: true); break;
                    case ResourceCleanupEvent.PostTransactionOutcome outcome:
                    {
                        var c = s.Lifetime.Candidate;
                        if (c == null || c != outcome.Closure || !s.Lifetime.AwaitingDurableCompletion || s.Rollback != null) return Reject("STALE_OUTCOME");
                        if (outcome.Outcome == ResourcePostTransactionOutcome.UNKNOWN || outcome.Outcome == ResourcePostTransactionOutcome.VISUAL_ONLY_ROLLBACK) return new ResourceCleanupDecision(s, true, "OWNERSHIP_UNRESOLVED");
                        if (outcome.Outcome != ResourcePostTransactionOutcome.VERIFIED_DURABLE_ROLLBACK) return Reject("INVALID_EVENT");
                        if (!Restored(c, outcome.RestoredPrior)) return Reject("INVALID_RESTORED_PRIOR");
                        commands.Add(new ResourceCleanupCommand.InvalidateRollbackClones(c, c.Target));
                        var l = s.Lifetime;
                        next = Copy(s, lifetime: new ResourceLifetimeState(l.Ledger, l.Active, l.Candidate, l.CandidateSealed, nextOperationId: l.NextOperationId), cancellation: c, rollback: new ResourceRollbackResolution(c, outcome.RestoredPrior));
                        break;
                    }
                    case ResourceCleanupEvent.RollbackClonesInvalidated ack:
                        next = QualifyRollback(s, ack.Closure, ack.Plan, commands);
                        if (next == null) return Reject("STALE_ROLLBACK_CLONES");
                        break;
                    case ResourceCleanupEvent.RollbackCloneCountVerified count:
                        if (count.Count != 0) return Reject("CLONES_NOT_CLOSED");
                        next = QualifyRollback(s, count.Closure, count.Plan, commands);
                        if (next == null) return Reject("STALE_ROLLBACK_CLONES");
                        break;
                    case ResourceCleanupEvent.ShutdownClonesInvalidated ack:
                        if (s.Shutdown == null || s.Shutdown != ack.Operation || s.ShutdownClonesQualified) return Reject("STALE_SHUTDOWN_CLONES");
                        next = Copy(s, shutdownClonesQualified: true); break;
                    case ResourceCleanupEvent.ShutdownCloneCountVerified count:
                        if (count.Count != 0 || s.Shutdown == null || s.Shutdown != count.Operation || s.ShutdownClonesQualified) return Reject("STALE_SHUTDOWN_CLONES");
                        next = Copy(s, shutdownClonesQualified: true); break;
                    case ResourceCleanupEvent.ShutdownDisposeAcknowledged disposed:
                    {
                        if (disposed.Operation == null || !s.ShutdownDisposals.Contains(disposed.Operation)) return Reject("STALE_SHUTDOWN_DISPOSE");
                        var l = s.Lifetime; var ledger = l.Ledger;
                        next = Copy(s, lifetime: WithLedger(l, new ResourceState(ledger.Allocations.Where(a => a.Id != disposed.Operation.AllocationId), ledger.References, ledger.Scratch, ledger.NextId)), shutdownDisposals: s.ShutdownDisposals.Where(op => op != disposed.Operation));
                        break;
                    }
                    case ResourceCleanupEvent.Lifetime input:
                    {
                        var release = input.Event is ResourceLifetimeEvent.Admission a && a.Event is ResourceEvent.ReleaseReference;
                        if (s.Cancellation != null && !release) return Reject("CANCELLATION_PENDING");
                        if (s.TeardownRequested && (input.Event is ResourceLifetimeEvent.BeginCandidate || input.Event is ResourceLifetimeEvent.SealCandidate || input.Event is ResourceLifetimeEvent.AwaitDurableCompletion || (input.Event is ResourceLifetimeEvent.Admission && !release))) return Reject("TEARDOWN_PENDING");
                        var d = lifetime.Decide(s.Lifetime, input.Event);
                        if (!d.Accepted) return Reject(d.Diagnosis);
                        commands.AddRange(d.Commands.Select(c => new ResourceCleanupCommand.Lifetime(c)));
                        next = Copy(s, lifetime: d.State); break;
                    }
                    case ResourceCleanupEvent.CancelPreparation cancel:
                        if (cancel.Closure == null || s.Cancellation != null || s.Lifetime.Candidate != cancel.Closure || s.Lifetime.AwaitingDurableCompletion) return Reject("CANCEL_NOT_QUALIFIED");
                        next = Cancel(s, cancel.Closure, commands); break;
                    case ResourceCleanupEvent.DecodeResolved resolved:
                    {
                        var d = s.Decoder;
                        if (s.Cancellation != resolved.Closure || d == null || d.Operation != resolved.Operation || d.Outcome != ResourceDecodeOutcome.PENDING || (resolved.Outcome != ResourceDecodeOutcome.CREATED && resolved.Outcome != ResourceDecodeOutcome.NEVER_CREATED)) return Reject("STALE_DECODE_OUTCOME");
                        var l = s.Lifetime;
                        if (resolved.Outcome == ResourceDecodeOutcome.CREATED)
                        {
                            var result = admission.Decide(l.Ledger, new ResourceEvent.DecodeCompleted(resolved.Operation));
                            if (!result.Accepted) return Reject(result.Diagnosis);
                            l = WithLedger(l, result.State);
                        }
                        if (!d.ScratchReleased) commands.Add(new ResourceCleanupCommand.ReleaseUploadScratch(resolved.Closure, resolved.Operation));
                        next = Copy(s, lifetime: l, decoder: d with { Outcome = resolved.Outcome }); break;
                    }
                    case ResourceCleanupEvent.ScratchReleased released:
                    {
                        var d = s.Decoder;
                        if (s.Cancellation != released.Closure || d == null || d.Operation != released.Operation || d.ScratchReleased) return Reject("STALE_SCRATCH_ACK");
                        next = Copy(s, decoder: d with { ScratchReleased = true }); break;
                    }
                    case ResourceCleanupEvent.DisposeAcknowledged disposed:
                    {
                        if (disposed.Operation == null || !s.PendingDisposals.Contains(disposed.Operation)) return Reject("STALE_DISPOSE_ACK");
                        var ledger = s.Lifetime.Ledger;
                        next = Copy(s, lifetime: WithLedger(s.Lifetime, new ResourceState(ledger.Allocations.Where(a => a.Id != disposed.Operation.AllocationId), ledger.References, ledger.Scratch, ledger.NextId)), pendingDisposals: s.PendingDisposals.Where(op => op != disposed.Operation));
                        break;
                    }
                    default: return Reject("INVALID_EVENT");
                }
                var swept = Advance(next, commands);
                return !Valid(swept) ? Reject("INCONSISTENT_TRANSITION") : new ResourceCleanupDecision(swept, true, "ACCEPTED", commands.ToArray());
            }
            catch (OverflowException) { return Reject("OVERFLOW"); }
        }
        private static ResourceCleanupState QualifyRollback(ResourceCleanupState s, ResourceLifetimeClosure c, ResourceLifetimePlan p, List<ResourceCleanupCommand> commands)
        {
            var r = s.Rollback;
            if (r == null || c == null || p == null) return null;
            var expected = r.ClonePhase == ResourceRollbackClonePhase.CANDIDATE ? r.Closure.Target : r.ClonePhase == ResourceRollbackClonePhase.OLD_PRIOR ? r.Closure.Prior : null;
            if (r.Closure != c || expected != p) return null;
            var phase = r.ClonePhase == ResourceRollbackClonePhase.CANDIDATE && c.Prior != null && c.Prior != r.RestoredPrior ? ResourceRollbackClonePhase.OLD_PRIOR : ResourceRollbackClonePhase.QUALIFIED;
            if (phase == ResourceRollbackClonePhase.OLD_PRIOR) commands.Add(new ResourceCleanupCommand.InvalidateRollbackClones(c, c.Prior));
            return Copy(s, rollback: r with { ClonePhase = phase });
        }
        // Omitted nullable arguments retain existing state; completed closures use explicit constructors.
        private static ResourceCleanupState Copy(ResourceCleanupState s, ResourceLifetimeState lifetime = null, ResourceLifetimeClosure cancellation = null,
            ResourceCleanupDecoder decoder = null, IEnumerable<ResourceDisposalOperation> pendingDisposals = null, bool? teardownRequested = null,
            ResourceRollbackResolution rollback = null, ResourceShutdownOperation shutdown = null, bool? shutdownClonesQualified = null, IEnumerable<ResourceShutdownDisposal> shutdownDisposals = null) =>
            new(lifetime ?? s.Lifetime, cancellation ?? s.Cancellation, decoder ?? s.Decoder, pendingDisposals ?? s.PendingDisposals,
                teardownRequested ?? s.TeardownRequested, rollback ?? s.Rollback, shutdown ?? s.Shutdown, shutdownClonesQualified ?? s.ShutdownClonesQualified, shutdownDisposals ?? s.ShutdownDisposals);
        private static ResourceLifetimeState WithLedger(ResourceLifetimeState l, ResourceState ledger, long? next = null) =>
            new(ledger, l.Active, l.Candidate, l.CandidateSealed, l.AwaitingClones, l.Retirement, l.PendingDisposals, next ?? l.NextOperationId, l.AwaitingDurableCompletion);
        private static ResourceCleanupState Cancel(ResourceCleanupState s, ResourceLifetimeClosure c, List<ResourceCleanupCommand> commands)
        {
            var scratch = s.Lifetime.Ledger.Scratch;
            var decoder = scratch == null ? null : new ResourceCleanupDecoder(scratch.Operation, scratch.DecodeCompleted ? ResourceDecodeOutcome.CREATED : ResourceDecodeOutcome.PENDING);
            if (decoder?.Outcome == ResourceDecodeOutcome.PENDING) commands.Add(new ResourceCleanupCommand.RequestDecoderStop(c, decoder.Operation));
            return Copy(s, cancellation: c, decoder: decoder);
        }
        private static ResourceCleanupState Advance(ResourceCleanupState input, List<ResourceCleanupCommand> commands)
        {
            var s = input;
            if (s.TeardownRequested && s.Cancellation == null && s.Lifetime.Candidate != null && !s.Lifetime.AwaitingDurableCompletion) s = Cancel(s, s.Lifetime.Candidate, commands);
            s = Sweep(s, commands);
            if (!s.TeardownRequested || s.Cancellation != null || s.Lifetime.Candidate != null || s.Lifetime.AwaitingClones != null || s.Lifetime.Retirement != null) return s;
            var l = s.Lifetime;
            if (l.Active == null) return s; // Validation ensures no unknown allocation can be wiped.
            if (s.Shutdown == null)
            {
                var op = new ResourceShutdownOperation(l.NextOperationId, l.Active);
                commands.Add(new ResourceCleanupCommand.InvalidateShutdownClones(op));
                return Copy(s, lifetime: WithLedger(l, l.Ledger, checked(l.NextOperationId + 1)), shutdown: op);
            }
            if (!s.ShutdownClonesQualified) return s;
            var h = s.Shutdown; var next = l.NextOperationId; var pending = s.ShutdownDisposals.ToList(); var allocations = new List<ResourceAllocation>();
            foreach (var a in l.Ledger.Allocations)
            {
                if (l.Ledger.References.Any(r => r.AllocationId == a.Id)) { allocations.Add(a); continue; }
                if (a.Ownership == ResourceOwnership.OWNED)
                {
                    allocations.Add(a);
                    if (!pending.Any(op => op.AllocationId == a.Id))
                    {
                        var op = new ResourceShutdownDisposal(next, h, a.Id); next = checked(next + 1);
                        pending.Add(op); commands.Add(new ResourceCleanupCommand.DisposeShutdown(op));
                    }
                }
                // Borrowed defaults untrack only after exact clones and all references close; never Dispose.
            }
            var ledger = new ResourceState(allocations, l.Ledger.References, l.Ledger.Scratch, l.Ledger.NextId);
            if (allocations.Count == 0) return new ResourceCleanupState(new ResourceLifetimeState(ledger, nextOperationId: next), teardownRequested: true);
            return Copy(s, lifetime: WithLedger(l, ledger, next), shutdownDisposals: pending);
        }
        private static ResourceCleanupState Sweep(ResourceCleanupState s, List<ResourceCleanupCommand> commands)
        {
            var c = s.Cancellation;
            if (c == null || (s.Rollback != null && s.Rollback.ClonePhase != ResourceRollbackClonePhase.QUALIFIED)) return s;
            var ledger = s.Lifetime.Ledger; var decoder = s.Decoder;
            // Early scratch ACK never resolves a decoder that can still create.
            if (decoder != null && decoder.ScratchReleased)
            {
                if (decoder.Outcome == ResourceDecodeOutcome.CREATED) { ledger = new ResourceState(ledger.Allocations, ledger.References, null, ledger.NextId); decoder = null; }
                else if (decoder.Outcome == ResourceDecodeOutcome.NEVER_CREATED && !ledger.References.Any(r => r.AllocationId == decoder.Operation.AllocationId))
                { ledger = new ResourceState(ledger.Allocations.Where(a => a.Id != decoder.Operation.AllocationId), ledger.References, null, ledger.NextId); decoder = null; }
            }
            var pending = s.PendingDisposals.ToList(); var next = s.Lifetime.NextOperationId; var allocations = new List<ResourceAllocation>();
            foreach (var a in ledger.Allocations)
            {
                if (!a.PlanIds.Contains(c.Target.PlanId) || ledger.Scratch?.Operation.AllocationId == a.Id || ledger.References.Any(r => r.AllocationId == a.Id && r.PlanId == c.Target.PlanId)) { allocations.Add(a); continue; }
                var retained = a.PlanIds.Where(p => p != c.Target.PlanId).ToArray();
                if (retained.Length != 0) allocations.Add(new ResourceAllocation(a.Id, a.Ownership, a.Width, a.Height, a.Sources, retained, a.BorrowedKey, a.Phase));
                else if (a.Ownership == ResourceOwnership.OWNED)
                {
                    allocations.Add(a);
                    if (!pending.Any(op => op.AllocationId == a.Id))
                    {
                        var op = new ResourceDisposalOperation(next, c, a.Id); next = checked(next + 1);
                        pending.Add(op); commands.Add(new ResourceCleanupCommand.Dispose(op));
                    }
                }
            }
            ledger = new ResourceState(allocations, ledger.References, ledger.Scratch, ledger.NextId);
            if (!allocations.Any(a => a.PlanIds.Contains(c.Target.PlanId)))
            {
                var active = s.Rollback != null ? s.Rollback.RestoredPrior : s.Lifetime.Active;
                return new ResourceCleanupState(new ResourceLifetimeState(ledger, active, nextOperationId: next), teardownRequested: s.TeardownRequested);
            }
            return new ResourceCleanupState(WithLedger(s.Lifetime, ledger, next), c, decoder, pending, s.TeardownRequested, s.Rollback, s.Shutdown, s.ShutdownClonesQualified, s.ShutdownDisposals);
        }
    }
}
