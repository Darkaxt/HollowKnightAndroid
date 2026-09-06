using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Opaque authoritative runtime identities. Stamp is correlation only, NOT SkinStamp arithmetic authority.
    // Construction/binding belongs to the future adapter; prior and target may use different bindings.
    public sealed record ResourceLifetimePlan(string PlanId, string Binding, string GenerationId, string Stamp);
    public sealed record ResourceLifetimeClosure(long OperationId, string TransactionId, ResourceLifetimePlan Prior, ResourceLifetimePlan Target);
    public sealed record ResourceDisposalOperation(long OperationId, ResourceLifetimeClosure Closure, long AllocationId);
    // Retirement checkpoint only. No cancellation, decoder-failure or teardown authority.
    public sealed class ResourceLifetimeState
    {
        public ResourceState Ledger { get; }
        public ResourceLifetimePlan Active { get; }
        public ResourceLifetimeClosure Candidate { get; }
        public bool CandidateSealed { get; }
        public bool AwaitingDurableCompletion { get; }
        public ResourceLifetimeClosure AwaitingClones { get; }
        public ResourceLifetimeClosure Retirement { get; }
        public IReadOnlyList<ResourceDisposalOperation> PendingDisposals { get; }
        public long NextOperationId { get; }
        public ResourceLifetimeState(ResourceState ledger = null, ResourceLifetimePlan active = null,
            ResourceLifetimeClosure candidate = null, bool candidateSealed = false,
            ResourceLifetimeClosure awaitingClones = null, ResourceLifetimeClosure retirement = null,
            IEnumerable<ResourceDisposalOperation> pendingDisposals = null, long nextOperationId = 1, bool awaitingDurableCompletion = false)
        {
            Ledger = ledger ?? new ResourceState(); Active = active; Candidate = candidate; CandidateSealed = candidateSealed;
            AwaitingClones = awaitingClones; Retirement = retirement; NextOperationId = nextOperationId; AwaitingDurableCompletion = awaitingDurableCompletion;
            PendingDisposals = Array.AsReadOnly((pendingDisposals ?? Array.Empty<ResourceDisposalOperation>()).ToArray());
        }
    }
    public abstract record ResourceLifetimeEvent
    {
        private ResourceLifetimeEvent() { }
        public sealed record BeginCandidate(string TransactionId, ResourceLifetimePlan Target) : ResourceLifetimeEvent;
        public sealed record Admission(ResourceEvent Event) : ResourceLifetimeEvent;
        public sealed record SealCandidate(ResourceLifetimeClosure Closure) : ResourceLifetimeEvent;
        // Consume the value gate ONCE, BEFORE the caller starts visual execution. Performs no writes.
        // Failed/indeterminate outcomes stay blocked; cleanup belongs to the next slice.
        public sealed record AwaitDurableCompletion(ResourceLifetimeClosure Closure) : ResourceLifetimeEvent;
        // Trusted LIVE verification of durable successful completion of the exact stored closure;
        // NOT receipt/history lookup, command intent, or authority to commit a transaction.
        public sealed record DurableCompletionVerified(ResourceLifetimeClosure Closure) : ResourceLifetimeEvent;
        // Exact InvalidateCompanionClones acknowledgement: ALL prior binding/stamp clones destroyed.
        public sealed record CompanionClonesInvalidated(ResourceLifetimeClosure Closure) : ResourceLifetimeEvent;
        // Authoritative live registry count for the exact pending prior binding/stamp, not an estimate.
        public sealed record RegisteredCloneCountVerified(ResourceLifetimeClosure Closure, long Count) : ResourceLifetimeEvent;
        public sealed record DisposeAcknowledged(ResourceDisposalOperation Operation) : ResourceLifetimeEvent;
    }
    public abstract record ResourceLifetimeCommand
    {
        private ResourceLifetimeCommand() { }
        public sealed record Admission(ResourceCommand Command) : ResourceLifetimeCommand;
        public sealed record InvalidateCompanionClones(ResourceLifetimeClosure Closure) : ResourceLifetimeCommand;
        public sealed record Dispose(ResourceDisposalOperation Operation) : ResourceLifetimeCommand;
    }
    public sealed class ResourceLifetimeDecision
    {
        public ResourceLifetimeState State { get; }
        public bool Accepted { get; }
        public string Diagnosis { get; }
        public IReadOnlyList<ResourceLifetimeCommand> Commands { get; }
        public ResourceLifetimeDecision(ResourceLifetimeState state, bool accepted, string diagnosis, params ResourceLifetimeCommand[] commands)
        {
            State = state; Accepted = accepted; Diagnosis = diagnosis;
            Commands = Array.AsReadOnly((ResourceLifetimeCommand[])commands.Clone());
        }
    }
}
