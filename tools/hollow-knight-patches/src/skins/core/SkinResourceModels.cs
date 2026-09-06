using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Trusted verifier facts; this value does not verify files or canonical paths.
    public sealed record VerifiedResourceSource(string ProofScope, string CanonicalSource, long EncodedLength,
        string Sha256, long Width, long Height, string Format = "RGBA32", ResourceUsage Usage = ResourceUsage.GAME);
    public enum ResourceUsage { GAME, PREVIEW }
    public enum ResourceOwnership { OWNED, BORROWED }
    public enum ResourceReferenceKind { TARGET, MATERIAL, ROLLBACK }
    public enum ResourceAllocationPhase { RESERVED, READY }
    public sealed record ResourceReference(long Id, string PlanId, long AllocationId, ResourceReferenceKind Kind);
    public sealed record ResourceDecodeOperation(long OperationId, long AllocationId, string PlanId);
    public sealed record ResourceScratch(ResourceDecodeOperation Operation, long Bytes, bool DecodeCompleted = false);
    public sealed class ResourceAllocation
    {
        public long Id { get; }
        public ResourceOwnership Ownership { get; }
        public long Width { get; }
        public long Height { get; }
        public IReadOnlyList<VerifiedResourceSource> Sources { get; }
        public IReadOnlyList<string> PlanIds { get; }
        public string BorrowedKey { get; }
        public ResourceAllocationPhase Phase { get; }
        public ResourceAllocation(long id, ResourceOwnership ownership, long width, long height,
            IEnumerable<VerifiedResourceSource> sources = null, IEnumerable<string> planIds = null,
            string borrowedKey = null, ResourceAllocationPhase phase = ResourceAllocationPhase.RESERVED)
        {
            Id = id; Ownership = ownership; Width = width; Height = height; BorrowedKey = borrowedKey; Phase = phase;
            Sources = Array.AsReadOnly((sources ?? Array.Empty<VerifiedResourceSource>()).ToArray());
            PlanIds = Array.AsReadOnly((planIds ?? Array.Empty<string>()).ToArray());
        }
    }
    // Admission ledger only: NOT preparation, commit, retirement or apply readiness proof.
    // Plan membership retains its charge at zero references. Lifetime/disposal is separate.
    public sealed class ResourceState
    {
        public IReadOnlyList<ResourceAllocation> Allocations { get; }
        public IReadOnlyList<ResourceReference> References { get; }
        public ResourceScratch Scratch { get; }
        public long NextId { get; }
        public ResourceState(IEnumerable<ResourceAllocation> allocations = null, IEnumerable<ResourceReference> references = null,
            ResourceScratch scratch = null, long nextId = 1)
        {
            Allocations = Array.AsReadOnly((allocations ?? Array.Empty<ResourceAllocation>()).ToArray());
            References = Array.AsReadOnly((references ?? Array.Empty<ResourceReference>()).ToArray());
            Scratch = scratch; NextId = nextId;
        }
    }
    public sealed class ResourceTotals
    {
        public long ResidentBytes { get; }
        public long ProcessBytes { get; }
        public int OwnedAllocations { get; }
        public IReadOnlyDictionary<string, long> PlanBytes { get; }
        public IReadOnlyDictionary<string, int> PlanAllocations { get; }
        public ResourceTotals(long residentBytes, long processBytes, int ownedAllocations,
            IDictionary<string, long> planBytes, IDictionary<string, int> planAllocations)
        {
            ResidentBytes = residentBytes; ProcessBytes = processBytes; OwnedAllocations = ownedAllocations;
            PlanBytes = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(planBytes));
            PlanAllocations = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(planAllocations));
        }
    }
    public abstract record ResourceEvent
    {
        private ResourceEvent() { }
        public sealed record Acquire(string PlanId, VerifiedResourceSource Source, ResourceReferenceKind Kind) : ResourceEvent;
        public sealed record AcquireBorrowed(string PlanId, string BorrowedKey, long Width, long Height, ResourceReferenceKind Kind) : ResourceEvent;
        public sealed record ReleaseReference(ResourceReference Reference) : ResourceEvent;
        // Trusted acknowledgement of the exact decode operation, not simulated proof.
        public sealed record DecodeCompleted(ResourceDecodeOperation Operation) : ResourceEvent;
        // Acknowledges release of ALL readable CPU upload/scratch for this operation.
        public sealed record ScratchReleased(ResourceDecodeOperation Operation) : ResourceEvent;
    }
    public abstract record ResourceCommand
    {
        private ResourceCommand() { }
        // Fixed RGBA32, no mipmaps. Value command only; does not decode.
        public sealed record Decode(ResourceDecodeOperation Operation, VerifiedResourceSource Source) : ResourceCommand;
        public sealed record ReferenceGranted(ResourceReference Reference) : ResourceCommand;
        public sealed record ReleaseUploadScratch(ResourceDecodeOperation Operation) : ResourceCommand;
    }
    public sealed class ResourceDecision
    {
        public ResourceState State { get; }
        public bool Accepted { get; }
        public string Diagnosis { get; }
        public IReadOnlyList<ResourceCommand> Commands { get; }
        public ResourceDecision(ResourceState state, bool accepted, string diagnosis, params ResourceCommand[] commands)
        {
            State = state; Accepted = accepted; Diagnosis = diagnosis;
            Commands = Array.AsReadOnly((ResourceCommand[])commands.Clone());
        }
    }
}
