using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Pure resource ownership wrapper. Gates are values, never live apply/transaction authority.
    public sealed class SkinResourceLifetimeCore
    {
        private readonly SkinResourceCore admission = new();
        private static bool Text(string v) => !string.IsNullOrWhiteSpace(v) && v.Length <= 128;
        private static bool Valid(ResourceLifetimePlan p) => p != null && Text(p.PlanId) && Text(p.Binding) && Text(p.GenerationId) && Text(p.Stamp);
        private static bool Distinct(ResourceLifetimePlan prior, ResourceLifetimePlan target) => prior == null ||
            prior.PlanId != target.PlanId && (prior.Binding != target.Binding ||
                prior.GenerationId != target.GenerationId && prior.Stamp != target.Stamp);
        private static bool Valid(ResourceLifetimeClosure c, long next) => c != null && c.OperationId > 0 && c.OperationId < next && Text(c.TransactionId) &&
            Valid(c.Target) && (c.Prior == null || Valid(c.Prior)) && Distinct(c.Prior, c.Target);
        private bool Valid(ResourceLifetimeState s)
        {
            if (s == null || admission.Totals(s.Ledger) == null || s.NextOperationId <= 0 || s.Active != null && (!Valid(s.Active) || s.NextOperationId == 1)) return false;
            var closures = new[] { s.Candidate, s.AwaitingClones, s.Retirement }.Where(c => c != null).ToArray();
            if (closures.Length > 1 || closures.Any(c => !Valid(c, s.NextOperationId))) return false;
            if (s.CandidateSealed && (s.Candidate == null || s.Ledger.Scratch != null)) return false;
            if (s.AwaitingDurableCompletion && !s.CandidateSealed) return false;
            if (s.Candidate != null && s.Candidate.Prior != s.Active) return false;
            var displaced = s.AwaitingClones ?? s.Retirement;
            if (displaced != null && (displaced.Prior == null || displaced.Target != s.Active)) return false;
            var owners = new HashSet<string>(new[] { s.Active?.PlanId, s.Candidate?.Target?.PlanId, displaced?.Prior?.PlanId }.Where(p => p != null));
            if (s.Ledger.Allocations.Any(a => a.PlanIds.Any(p => !owners.Contains(p)))) return false;
            var scratch = s.Ledger.Scratch;
            if (scratch != null && (s.Candidate == null || scratch.Operation.PlanId != s.Candidate.Target.PlanId ||
                !s.Ledger.Allocations.First(a => a.Id == scratch.Operation.AllocationId).PlanIds.SequenceEqual(new[] { s.Candidate.Target.PlanId }))) return false;
            if (s.Retirement == null && s.PendingDisposals.Count != 0) return false;
            if (s.Retirement != null && !s.Ledger.Allocations.Any(a => a.PlanIds.Contains(s.Retirement.Prior.PlanId))) return false;
            var ids = new HashSet<long>(); var allocations = new HashSet<long>();
            foreach (var op in s.PendingDisposals)
            {
                if (op == null || op.Closure != s.Retirement || op.OperationId <= op.Closure.OperationId || op.OperationId >= s.NextOperationId ||
                    !ids.Add(op.OperationId) || !allocations.Add(op.AllocationId)) return false;
                var a = s.Ledger.Allocations.FirstOrDefault(a => a.Id == op.AllocationId);
                if (a == null || a.Ownership != ResourceOwnership.OWNED || a.Phase != ResourceAllocationPhase.READY ||
                    !a.PlanIds.SequenceEqual(new[] { s.Retirement.Prior.PlanId }) || s.Ledger.References.Any(r => r.AllocationId == a.Id)) return false;
            }
            return true;
        }
        // Read-only composition seam; grants no gate or transition authority.
        public bool IsValidState(ResourceLifetimeState state) => Valid(state);
        public bool CanBeginCandidate(ResourceLifetimeState state) => Valid(state) && state.Candidate == null && state.AwaitingClones == null && state.Retirement == null;
        public bool CanApply(ResourceLifetimeState state) => Valid(state) && state.Candidate != null && state.CandidateSealed && !state.AwaitingDurableCompletion;

        public ResourceLifetimeDecision Decide(ResourceLifetimeState state, ResourceLifetimeEvent e)
        {
            ResourceLifetimeDecision Reject(string code) => new(state, false, code);
            if (!Valid(state)) return Reject("INVALID_STATE");
            try
            {
                var commands = new List<ResourceLifetimeCommand>(); ResourceLifetimeState next;
                switch (e)
                {
                    case ResourceLifetimeEvent.AwaitDurableCompletion awaiting:
                        if (!CanApply(state) || state.Candidate != awaiting.Closure) return Reject("STALE_APPLY_GATE");
                        next = new ResourceLifetimeState(state.Ledger, state.Active, state.Candidate, true,
                            nextOperationId: state.NextOperationId, awaitingDurableCompletion: true);
                        break;
                    case ResourceLifetimeEvent.BeginCandidate begin:
                    {
                        if (!CanBeginCandidate(state)) return Reject("LIFETIME_BUSY");
                        if (!Text(begin.TransactionId) || !Valid(begin.Target) || !Distinct(state.Active, begin.Target)) return Reject("INVALID_CANDIDATE");
                        var c = new ResourceLifetimeClosure(state.NextOperationId, begin.TransactionId, state.Active, begin.Target);
                        next = new ResourceLifetimeState(state.Ledger, state.Active, c, nextOperationId: checked(state.NextOperationId + 1));
                        break;
                    }
                    case ResourceLifetimeEvent.Admission input:
                    {
                        var p = input.Event switch { ResourceEvent.Acquire a => a.PlanId, ResourceEvent.AcquireBorrowed a => a.PlanId, _ => null };
                        // Freeze acquisitions at seal and throughout durable/clone/disposal closure.
                        // Dedup cannot reacquire an allocation already commanded for disposal.
                        if (p != null && (state.Candidate == null || state.CandidateSealed || p != state.Candidate.Target.PlanId)) return Reject("ACQUISITION_CLOSED");
                        var d = admission.Decide(state.Ledger, input.Event);
                        if (!d.Accepted) return Reject(d.Diagnosis);
                        commands.AddRange(d.Commands.Select(c => new ResourceLifetimeCommand.Admission(c)));
                        next = new ResourceLifetimeState(d.State, state.Active, state.Candidate, state.CandidateSealed,
                            state.AwaitingClones, state.Retirement, state.PendingDisposals, state.NextOperationId, state.AwaitingDurableCompletion);
                        break;
                    }
                    case ResourceLifetimeEvent.SealCandidate seal:
                        if (seal.Closure == null || state.Candidate != seal.Closure || state.CandidateSealed || state.Ledger.Scratch != null) return Reject("NOT_PREPARED");
                        next = new ResourceLifetimeState(state.Ledger, state.Active, state.Candidate, true, nextOperationId: state.NextOperationId);
                        break;
                    case ResourceLifetimeEvent.DurableCompletionVerified completed:
                    {
                        if (!state.AwaitingDurableCompletion || state.Candidate != completed.Closure) return Reject("STALE_CLOSURE");
                        var c = completed.Closure;
                        if (c.Prior != null) commands.Add(new ResourceLifetimeCommand.InvalidateCompanionClones(c));
                        next = new ResourceLifetimeState(state.Ledger, c.Target, awaitingClones: c.Prior != null ? c : null, nextOperationId: state.NextOperationId);
                        break;
                    }
                    case ResourceLifetimeEvent.CompanionClonesInvalidated clones:
                        if (clones.Closure == null || state.AwaitingClones != clones.Closure) return Reject("STALE_CLONE_ACK");
                        next = new ResourceLifetimeState(state.Ledger, state.Active, retirement: clones.Closure, nextOperationId: state.NextOperationId);
                        break;
                    case ResourceLifetimeEvent.RegisteredCloneCountVerified count:
                        if (count.Closure == null || state.AwaitingClones != count.Closure || count.Count != 0) return Reject("CLONES_NOT_CLOSED");
                        next = new ResourceLifetimeState(state.Ledger, state.Active, retirement: count.Closure, nextOperationId: state.NextOperationId);
                        break;
                    case ResourceLifetimeEvent.DisposeAcknowledged disposed:
                    {
                        if (disposed.Operation == null || !state.PendingDisposals.Contains(disposed.Operation)) return Reject("STALE_DISPOSE_ACK");
                        var ledger = new ResourceState(state.Ledger.Allocations.Where(a => a.Id != disposed.Operation.AllocationId),
                            state.Ledger.References, state.Ledger.Scratch, state.Ledger.NextId);
                        next = new ResourceLifetimeState(ledger, state.Active, retirement: state.Retirement,
                            pendingDisposals: state.PendingDisposals.Where(op => op != disposed.Operation), nextOperationId: state.NextOperationId);
                        break;
                    }
                    default: return Reject("INVALID_EVENT");
                }
                var swept = Sweep(next, commands);
                return !Valid(swept) ? Reject("INCONSISTENT_TRANSITION") : new ResourceLifetimeDecision(swept, true, "ACCEPTED", commands.ToArray());
            }
            catch (OverflowException) { return Reject("OVERFLOW"); }
        }

        // Qualified membership closure only; command emission never removes an owned allocation.
        private static ResourceLifetimeState Sweep(ResourceLifetimeState s, List<ResourceLifetimeCommand> commands)
        {
            var closure = s.Retirement; if (closure == null) return s;
            var old = closure.Prior.PlanId; var pending = s.PendingDisposals.ToList(); var next = s.NextOperationId;
            var allocations = new List<ResourceAllocation>();
            foreach (var a in s.Ledger.Allocations)
            {
                if (!a.PlanIds.Contains(old) || s.Ledger.References.Any(r => r.AllocationId == a.Id && r.PlanId == old))
                { allocations.Add(a); continue; }
                var retained = a.PlanIds.Where(p => p != old).ToArray();
                if (retained.Length != 0)
                    allocations.Add(new ResourceAllocation(a.Id, a.Ownership, a.Width, a.Height, a.Sources, retained, a.BorrowedKey, a.Phase));
                else if (a.Ownership == ResourceOwnership.OWNED)
                {
                    allocations.Add(a);
                    if (!pending.Any(op => op.AllocationId == a.Id))
                    {
                        var op = new ResourceDisposalOperation(next, closure, a.Id); next = checked(next + 1);
                        pending.Add(op); commands.Add(new ResourceLifetimeCommand.Dispose(op));
                    }
                }
                // Borrowed defaults are merely untracked after qualified closure, never disposed.
            }
            var ledger = new ResourceState(allocations, s.Ledger.References, s.Ledger.Scratch, s.Ledger.NextId);
            var retirement = allocations.Any(a => a.PlanIds.Contains(old)) ? closure : null;
            return new ResourceLifetimeState(ledger, s.Active, retirement: retirement, pendingDisposals: pending, nextOperationId: next);
        }
    }
}
