using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Pure bounded admission/reference accounting. Does not grant lifetime or apply authority.
    public sealed class SkinResourceCore
    {
        public const long TEXTURE_BYTES = 32L * 1024 * 1024;
        public const long PLAN_BYTES = 96L * 1024 * 1024;
        public const long PROCESS_BYTES = 224L * 1024 * 1024;
        public const int PLAN_ALLOCATIONS = 205;
        public const int PROCESS_ALLOCATIONS = 410;
        // Bookkeeping bounds, not texture budgets. Lifetime scheduling is a separate slice.
        public const int MAX_PLANS = 3;
        public const int MAX_REFERENCES = 16384;
        public const int MAX_SOURCE_FACTS = 16384;
        private sealed record Content(long Length, string Hash, long Width, long Height, string Format);
        private static Content Identity(VerifiedResourceSource v) => new(v.EncodedLength, v.Sha256, v.Width, v.Height, v.Format);
        private static bool Text(string v, int max) => !string.IsNullOrWhiteSpace(v) && v.Length <= max;
        private static void Require(bool condition) { if (!condition) throw new ArgumentException("Invalid resource value"); }
        private static long Bytes(long width, long height) { Require(width > 0 && height > 0); return checked(checked(width * height) * 4L); }
        private static bool Valid(VerifiedResourceSource v) => v != null && Text(v.ProofScope, 128) && Text(v.CanonicalSource, 4096) &&
            v.EncodedLength > 0 && v.Sha256 != null && v.Sha256.Length == 64 && v.Sha256.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f') &&
            v.Format == "RGBA32" && v.Usage == ResourceUsage.GAME && Bytes(v.Width, v.Height) <= TEXTURE_BYTES;
        private static bool Kind(ResourceReferenceKind k) => k == ResourceReferenceKind.TARGET || k == ResourceReferenceKind.MATERIAL || k == ResourceReferenceKind.ROLLBACK;

        // Null means inconsistent, malformed, overflowing or over-budget input. No cached totals are trusted.
        public ResourceTotals Totals(ResourceState state)
        {
            try
            {
                Require(state != null && state.NextId > 0 && state.Allocations.Count <= PROCESS_ALLOCATIONS * 2 && state.References.Count <= MAX_REFERENCES);
                var ids = new HashSet<long>();
                void Id(long value) => Require(value > 0 && value < state.NextId && ids.Add(value));
                var byId = new Dictionary<long, ResourceAllocation>();
                var plans = new HashSet<string>(); var canonical = new HashSet<(string, string)>();
                var contents = new HashSet<Content>(); var borrowed = new HashSet<string>();
                var planBytes = new Dictionary<string, long>(); var planCounts = new Dictionary<string, int>();
                long resident = 0; int owned = 0, borrowedCount = 0, facts = 0;
                foreach (var a in state.Allocations)
                {
                    Require(a != null); Id(a.Id); byId[a.Id] = a;
                    Require(a.Phase == ResourceAllocationPhase.RESERVED || a.Phase == ResourceAllocationPhase.READY);
                    Require(a.PlanIds.Count > 0 && a.PlanIds.Count <= MAX_PLANS && a.PlanIds.Distinct().Count() == a.PlanIds.Count);
                    foreach (var p in a.PlanIds) { Require(Text(p, 128)); plans.Add(p); }
                    var size = Bytes(a.Width, a.Height);
                    switch (a.Ownership)
                    {
                        case ResourceOwnership.OWNED:
                            Require(a.BorrowedKey == null && a.Sources.Count > 0 && size <= TEXTURE_BYTES);
                            facts = checked(facts + a.Sources.Count); Require(facts <= MAX_SOURCE_FACTS);
                            Require(a.Sources[0] != null); var c = Identity(a.Sources[0]); Require(contents.Add(c));
                            foreach (var v in a.Sources)
                            {
                                Require(Valid(v) && Identity(v) == c && v.Width == a.Width && v.Height == a.Height);
                                Require(canonical.Add((v.ProofScope, v.CanonicalSource)));
                            }
                            resident = checked(resident + size); owned = checked(owned + 1);
                            foreach (var p in a.PlanIds)
                            {
                                planBytes.TryGetValue(p, out var b); planCounts.TryGetValue(p, out var n);
                                planBytes[p] = checked(b + size); planCounts[p] = checked(n + 1);
                            }
                            break;
                        case ResourceOwnership.BORROWED:
                            Require(a.Sources.Count == 0 && Text(a.BorrowedKey, 4096) && borrowed.Add(a.BorrowedKey));
                            Require(a.Phase == ResourceAllocationPhase.READY); borrowedCount = checked(borrowedCount + 1);
                            break;
                        default: Require(false); break;
                    }
                }
                Require(plans.Count <= MAX_PLANS && owned <= PROCESS_ALLOCATIONS && borrowedCount <= PROCESS_ALLOCATIONS);
                Require(planBytes.Values.All(x => x <= PLAN_BYTES) && planCounts.Values.All(x => x <= PLAN_ALLOCATIONS));
                foreach (var r in state.References)
                {
                    Require(r != null); Id(r.Id);
                    Require(Kind(r.Kind) && byId.TryGetValue(r.AllocationId, out var a) && a.PlanIds.Contains(r.PlanId));
                }
                var scratch = state.Scratch;
                if (scratch != null)
                {
                    Require(scratch.Operation != null); Id(scratch.Operation.OperationId);
                    Require(byId.TryGetValue(scratch.Operation.AllocationId, out var a) && a.Ownership == ResourceOwnership.OWNED && a.PlanIds.Contains(scratch.Operation.PlanId));
                    Require(scratch.Bytes == Bytes(a.Width, a.Height) && scratch.Bytes <= TEXTURE_BYTES);
                    Require(a.Phase == (scratch.DecodeCompleted ? ResourceAllocationPhase.READY : ResourceAllocationPhase.RESERVED));
                }
                Require(state.Allocations.Where(a => a.Phase == ResourceAllocationPhase.RESERVED).All(a =>
                    scratch != null && !scratch.DecodeCompleted && scratch.Operation.AllocationId == a.Id));
                var peak = checked(resident + (scratch?.Bytes ?? 0)); Require(peak <= PROCESS_BYTES);
                return new ResourceTotals(resident, peak, owned, planBytes, planCounts);
            }
            catch (ArgumentException) { return null; }
            catch (OverflowException) { return null; }
        }
        public ResourceDecision Decide(ResourceState state, ResourceEvent e)
        {
            ResourceDecision Reject(string code) => new(state, false, code);
            if (Totals(state) == null) return Reject("INVALID_STATE");
            try
            {
                switch (e)
                {
                    case ResourceEvent.Acquire acquire:
                    {
                        if (!Text(acquire.PlanId, 128) || !Valid(acquire.Source) || !Kind(acquire.Kind)) return Reject("INVALID_SOURCE");
                        var c = Identity(acquire.Source); var owned = state.Allocations.Where(a => a.Ownership == ResourceOwnership.OWNED).ToArray();
                        if (owned.Any(a => a.Sources.Any(v => v.ProofScope == acquire.Source.ProofScope && v.CanonicalSource == acquire.Source.CanonicalSource && Identity(v) != c)))
                            return Reject("CONTRADICTORY_SOURCE");
                        var existing = owned.FirstOrDefault(a => Identity(a.Sources[0]) == c);
                        if (existing == null && state.Scratch != null) return Reject("SCRATCH_BUSY");
                        var aid = existing?.Id ?? state.NextId; var rid = existing == null ? checked(state.NextId + 1) : state.NextId;
                        var reference = new ResourceReference(rid, acquire.PlanId, aid, acquire.Kind);
                        var commands = new List<ResourceCommand> { new ResourceCommand.ReferenceGranted(reference) };
                        var updated = new ResourceAllocation(aid, ResourceOwnership.OWNED, acquire.Source.Width, acquire.Source.Height,
                            (existing?.Sources ?? Array.Empty<VerifiedResourceSource>()).Append(acquire.Source).Distinct(),
                            (existing?.PlanIds ?? Array.Empty<string>()).Append(acquire.PlanId).Distinct(), phase: existing?.Phase ?? ResourceAllocationPhase.RESERVED);
                        var scratch = state.Scratch; var next = checked(rid + 1);
                        if (existing == null)
                        {
                            var op = new ResourceDecodeOperation(next, aid, acquire.PlanId); next = checked(next + 1);
                            scratch = new ResourceScratch(op, Bytes(updated.Width, updated.Height));
                            commands.Add(new ResourceCommand.Decode(op, acquire.Source));
                        }
                        return Admit(state, new ResourceState(state.Allocations.Where(a => a.Id != aid).Append(updated),
                            state.References.Append(reference), scratch, next), commands.ToArray());
                    }
                    case ResourceEvent.AcquireBorrowed acquire:
                    {
                        if (!Text(acquire.PlanId, 128) || !Text(acquire.BorrowedKey, 4096) || !Kind(acquire.Kind)) return Reject("INVALID_BORROWED");
                        Bytes(acquire.Width, acquire.Height);
                        var existing = state.Allocations.FirstOrDefault(a => a.Ownership == ResourceOwnership.BORROWED && a.BorrowedKey == acquire.BorrowedKey);
                        if (existing != null && (existing.Width != acquire.Width || existing.Height != acquire.Height)) return Reject("CONTRADICTORY_BORROWED");
                        var aid = existing?.Id ?? state.NextId; var rid = existing == null ? checked(state.NextId + 1) : state.NextId;
                        var reference = new ResourceReference(rid, acquire.PlanId, aid, acquire.Kind);
                        var allocation = new ResourceAllocation(aid, ResourceOwnership.BORROWED, acquire.Width, acquire.Height,
                            planIds: (existing?.PlanIds ?? Array.Empty<string>()).Append(acquire.PlanId).Distinct(),
                            borrowedKey: acquire.BorrowedKey, phase: ResourceAllocationPhase.READY);
                        return Admit(state, new ResourceState(state.Allocations.Where(a => a.Id != aid).Append(allocation),
                            state.References.Append(reference), state.Scratch, checked(rid + 1)), new ResourceCommand.ReferenceGranted(reference));
                    }
                    case ResourceEvent.ReleaseReference release:
                        if (release.Reference == null || !state.References.Contains(release.Reference)) return Reject("STALE_REFERENCE");
                        return Admit(state, new ResourceState(state.Allocations, state.References.Where(r => r != release.Reference), state.Scratch, state.NextId));
                    case ResourceEvent.DecodeCompleted completed:
                    {
                        var scratch = state.Scratch;
                        if (scratch == null || scratch.Operation != completed.Operation || scratch.DecodeCompleted) return Reject("STALE_DECODE");
                        var allocations = state.Allocations.Select(a => a.Id != completed.Operation.AllocationId ? a :
                            new ResourceAllocation(a.Id, a.Ownership, a.Width, a.Height, a.Sources, a.PlanIds, a.BorrowedKey, ResourceAllocationPhase.READY));
                        return Admit(state, new ResourceState(allocations, state.References, scratch with { DecodeCompleted = true }, state.NextId),
                            new ResourceCommand.ReleaseUploadScratch(completed.Operation));
                    }
                    case ResourceEvent.ScratchReleased released:
                    {
                        var scratch = state.Scratch;
                        if (scratch == null || scratch.Operation != released.Operation || !scratch.DecodeCompleted) return Reject("STALE_SCRATCH");
                        return Admit(state, new ResourceState(state.Allocations, state.References, null, state.NextId));
                    }
                    default: return Reject("INVALID_EVENT");
                }
            }
            catch (ArgumentException) { return Reject("INVALID_EVENT"); }
            catch (OverflowException) { return Reject("OVERFLOW"); }
        }
        private ResourceDecision Admit(ResourceState prior, ResourceState next, params ResourceCommand[] commands) =>
            Totals(next) == null ? new ResourceDecision(prior, false, "LIMIT_OR_INCONSISTENT") : new ResourceDecision(next, true, "ACCEPTED", commands);
    }
}
