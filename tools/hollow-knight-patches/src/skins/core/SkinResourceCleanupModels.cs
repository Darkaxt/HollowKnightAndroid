using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Trusted terminal decoder facts, not stop-request acknowledgement.
    public enum ResourceDecodeOutcome { PENDING, CREATED, NEVER_CREATED }
    public sealed record ResourceCleanupDecoder(ResourceDecodeOperation Operation,
        ResourceDecodeOutcome Outcome = ResourceDecodeOutcome.PENDING, bool ScratchReleased = false);
    // Exact resource-only shutdown identity; never a fabricated transaction.
    public sealed record ResourceShutdownOperation(long OperationId, ResourceLifetimePlan Plan);
    public sealed record ResourceShutdownDisposal(long OperationId, ResourceShutdownOperation Shutdown, long AllocationId);
    public enum ResourcePostTransactionOutcome { UNKNOWN, VISUAL_ONLY_ROLLBACK, VERIFIED_DURABLE_ROLLBACK }
    public enum ResourceRollbackClonePhase { CANDIDATE, OLD_PRIOR, QUALIFIED }
    public sealed record ResourceRollbackResolution(ResourceLifetimeClosure Closure, ResourceLifetimePlan RestoredPrior, ResourceRollbackClonePhase ClonePhase = ResourceRollbackClonePhase.CANDIDATE);
    // This wrapper is the gate authority. Verification events are trusted live adapter facts.
    public sealed class ResourceCleanupState
    {
        public ResourceLifetimeState Lifetime { get; }
        public ResourceLifetimeClosure Cancellation { get; }
        public ResourceCleanupDecoder Decoder { get; }
        public IReadOnlyList<ResourceDisposalOperation> PendingDisposals { get; }
        public bool TeardownRequested { get; }
        public ResourceRollbackResolution Rollback { get; }
        public ResourceShutdownOperation Shutdown { get; }
        public bool ShutdownClonesQualified { get; }
        public IReadOnlyList<ResourceShutdownDisposal> ShutdownDisposals { get; }
        public bool Closed => TeardownRequested && Lifetime.NextOperationId > 0 && Lifetime.Ledger.NextId > 0 &&
            !Lifetime.CandidateSealed && !Lifetime.AwaitingDurableCompletion && !ShutdownClonesQualified && Lifetime.Active == null && Lifetime.Candidate == null &&
            Lifetime.AwaitingClones == null && Lifetime.Retirement == null && Lifetime.PendingDisposals.Count == 0 &&
            Cancellation == null && Decoder == null && PendingDisposals.Count == 0 && Rollback == null && Shutdown == null &&
            ShutdownDisposals.Count == 0 && Lifetime.Ledger.Allocations.Count == 0 && Lifetime.Ledger.References.Count == 0 && Lifetime.Ledger.Scratch == null;
        public ResourceCleanupState(ResourceLifetimeState lifetime = null, ResourceLifetimeClosure cancellation = null,
            ResourceCleanupDecoder decoder = null, IEnumerable<ResourceDisposalOperation> pendingDisposals = null,
            bool teardownRequested = false, ResourceRollbackResolution rollback = null,
            ResourceShutdownOperation shutdown = null, bool shutdownClonesQualified = false,
            IEnumerable<ResourceShutdownDisposal> shutdownDisposals = null)
        {
            Lifetime = lifetime ?? new ResourceLifetimeState(); Cancellation = cancellation; Decoder = decoder;
            PendingDisposals = Array.AsReadOnly((pendingDisposals ?? Array.Empty<ResourceDisposalOperation>()).ToArray());
            TeardownRequested = teardownRequested; Rollback = rollback; Shutdown = shutdown; ShutdownClonesQualified = shutdownClonesQualified;
            ShutdownDisposals = Array.AsReadOnly((shutdownDisposals ?? Array.Empty<ResourceShutdownDisposal>()).ToArray());
        }
    }
    public abstract record ResourceCleanupEvent
    {
        private ResourceCleanupEvent() { }
        public sealed record Teardown : ResourceCleanupEvent;
        // Live adapter verification of durable restored-prior ownership for this exact consumed closure,
        // not visual rollback, command completion, or retained registry history. Restored PlanId retains
        // prior ownership; binding/generation/stamp are supplied, never derived numerically.
        // UNKNOWN/visual-only preserve ownership; later exact success/durable rollback remains reachable.
        public sealed record PostTransactionOutcome(ResourceLifetimeClosure Closure, ResourcePostTransactionOutcome Outcome, ResourceLifetimePlan RestoredPrior) : ResourceCleanupEvent;
        // ALL clones of the requested exact plan, not one clone or the restored plan.
        public sealed record RollbackClonesInvalidated(ResourceLifetimeClosure Closure, ResourceLifetimePlan Plan) : ResourceCleanupEvent;
        // Authoritative live registry count for the same full closure and requested plan identity.
        public sealed record RollbackCloneCountVerified(ResourceLifetimeClosure Closure, ResourceLifetimePlan Plan, long Count) : ResourceCleanupEvent;
        // ALL current plan clones; old rollback/retirement ACK cannot qualify this new operation.
        public sealed record ShutdownClonesInvalidated(ResourceShutdownOperation Operation) : ResourceCleanupEvent;
        // Authoritative live registry count for the exact shutdown operation and current plan.
        public sealed record ShutdownCloneCountVerified(ResourceShutdownOperation Operation, long Count) : ResourceCleanupEvent;
        public sealed record ShutdownDisposeAcknowledged(ResourceShutdownDisposal Operation) : ResourceCleanupEvent;
        public sealed record Lifetime(ResourceLifetimeEvent Event) : ResourceCleanupEvent;
        public sealed record CancelPreparation(ResourceLifetimeClosure Closure) : ResourceCleanupEvent;
        // NEVER_CREATED guarantees no late allocation can be created; trusted terminal adapter fact.
        public sealed record DecodeResolved(ResourceLifetimeClosure Closure, ResourceDecodeOperation Operation, ResourceDecodeOutcome Outcome) : ResourceCleanupEvent;
        // Release of ALL upload/CPU scratch, independently arriving from decoder outcome.
        public sealed record ScratchReleased(ResourceLifetimeClosure Closure, ResourceDecodeOperation Operation) : ResourceCleanupEvent;
        public sealed record DisposeAcknowledged(ResourceDisposalOperation Operation) : ResourceCleanupEvent;
    }
    public abstract record ResourceCleanupCommand
    {
        private ResourceCleanupCommand() { }
        public sealed record InvalidateRollbackClones(ResourceLifetimeClosure Closure, ResourceLifetimePlan Plan) : ResourceCleanupCommand;
        public sealed record InvalidateShutdownClones(ResourceShutdownOperation Operation) : ResourceCleanupCommand;
        public sealed record DisposeShutdown(ResourceShutdownDisposal Operation) : ResourceCleanupCommand;
        public sealed record Lifetime(ResourceLifetimeCommand Command) : ResourceCleanupCommand;
        public sealed record RequestDecoderStop(ResourceLifetimeClosure Closure, ResourceDecodeOperation Operation) : ResourceCleanupCommand;
        public sealed record ReleaseUploadScratch(ResourceLifetimeClosure Closure, ResourceDecodeOperation Operation) : ResourceCleanupCommand;
        public sealed record Dispose(ResourceDisposalOperation Operation) : ResourceCleanupCommand;
    }
    public sealed class ResourceCleanupDecision
    {
        public ResourceCleanupState State { get; }
        public bool Accepted { get; }
        public string Diagnosis { get; }
        public IReadOnlyList<ResourceCleanupCommand> Commands { get; }
        public ResourceCleanupDecision(ResourceCleanupState state, bool accepted, string diagnosis, params ResourceCleanupCommand[] commands)
        { State = state; Accepted = accepted; Diagnosis = diagnosis; Commands = Array.AsReadOnly((ResourceCleanupCommand[])commands.Clone()); }
    }
}
