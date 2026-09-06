using System;
using System.Collections.Generic;
using System.Linq;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinResourceCoreTests
{
    private readonly SkinResourceCore core = new();
    private const long MiB = 1024L * 1024;
    private static VerifiedResourceSource Source(string key = "a", long bytes = 4) =>
        new("verification-1", key, 100, new string(key[0], 64), bytes / 4, 1);
    private ResourceDecision Acquire(ResourceState s = null, string p = "p", VerifiedResourceSource v = null) =>
        core.Decide(s ?? new ResourceState(), new ResourceEvent.Acquire(p, v ?? Source(), ResourceReferenceKind.TARGET));
    private ResourceState Ready(ResourceState s)
    {
        var op = s.Scratch.Operation;
        return core.Decide(core.Decide(s, new ResourceEvent.DecodeCompleted(op)).State, new ResourceEvent.ScratchReleased(op)).State;
    }
    private void Denied(ResourceState s, ResourceEvent e)
    {
        var d = core.Decide(s, e); Assert.False(d.Accepted); Assert.Same(s, d.State); Assert.Empty(d.Commands);
    }
    [Fact] public void reservesBeforeDecodeAndChargesScratchAndResident()
    {
        var d = Acquire(v: Source(bytes: 32 * MiB)); Assert.True(d.Accepted);
        Assert.Equal(32 * MiB, core.Totals(d.State).ResidentBytes);
        Assert.Equal(64 * MiB, core.Totals(d.State).ProcessBytes);
        Assert.Equal(1, core.Totals(d.State).OwnedAllocations);
        Assert.Contains(d.Commands, x => x is ResourceCommand.Decode);
    }
    [Fact] public void textureLimitPlusOnePixelAndArithmeticOverflowRejectWithoutMutation()
    {
        foreach (var v in new[] { Source(bytes: 32 * MiB + 4), Source() with { Width = long.MaxValue },
            Source() with { Width = long.MaxValue / 4 + 1 }, Source() with { Width = 0 }, Source() with { Height = -1 } })
            Denied(new ResourceState(), new ResourceEvent.Acquire("p", v, ResourceReferenceKind.TARGET));
    }
    [Fact] public void verifiedFullContentSharesAcrossCanonicalSourcesAndPlans()
    {
        var s = Ready(Acquire().State);
        var d = Acquire(s, "q", Source() with { CanonicalSource = "other", ProofScope = "verification-2" });
        Assert.True(d.Accepted); Assert.Single(d.State.Allocations); Assert.Equal(2, d.State.References.Count); Assert.Null(d.State.Scratch);
        var t = core.Totals(d.State); Assert.Equal(4, t.ResidentBytes); Assert.Equal(4, t.PlanBytes["p"]); Assert.Equal(4, t.PlanBytes["q"]);
        Assert.DoesNotContain(d.Commands, x => x is ResourceCommand.Decode);
    }
    [Fact] public void sameCanonicalSourceAcquisitionAddsDistinctReferencesWhileDecoding()
    {
        var s = Acquire().State; var d = Acquire(s); Assert.True(d.Accepted);
        Assert.Equal(2, d.State.References.Count); Assert.Equal(2, d.State.References.Select(x => x.Id).Distinct().Count());
        Assert.Equal(s.Scratch, d.State.Scratch); Assert.DoesNotContain(d.Commands, x => x is ResourceCommand.Decode);
    }
    [Fact] public void mismatchedIdentityDoesNotShareAndCanonicalContradictionRejects()
    {
        var v = Source(); var s = Ready(Acquire(v: v).State);
        foreach (var other in new[] { v with { EncodedLength = 101 }, v with { Sha256 = new string('b', 64) },
            v with { Width = 2 }, v with { Height = 2 } })
        {
            Denied(s, new ResourceEvent.Acquire("p", other, ResourceReferenceKind.TARGET));
            var d = Acquire(s, v: other with { CanonicalSource = "different" });
            Assert.True(d.Accepted); Assert.Equal(2, d.State.Allocations.Count);
        }
        Denied(s, new ResourceEvent.Acquire("p", v with { Format = "RGB24" }, ResourceReferenceKind.TARGET));
    }
    [Fact] public void previewAndIncompleteVerificationNeverDecode()
    {
        foreach (var v in new[] { Source() with { Usage = ResourceUsage.PREVIEW }, Source() with { Sha256 = "" },
            Source() with { ProofScope = "" }, Source() with { EncodedLength = 0 } })
            Denied(new ResourceState(), new ResourceEvent.Acquire("p", v, ResourceReferenceKind.TARGET));
    }
    [Fact] public void nextDecodeWaitsForExactCpuScratchReleaseAcknowledgement()
    {
        var s = Acquire().State; var op = s.Scratch.Operation;
        Denied(s, new ResourceEvent.Acquire("p", Source("b"), ResourceReferenceKind.TARGET));
        Denied(s, new ResourceEvent.ScratchReleased(op));
        Denied(s, new ResourceEvent.DecodeCompleted(op with { AllocationId = op.AllocationId + 1 }));
        var d = core.Decide(s, new ResourceEvent.DecodeCompleted(op)); Assert.True(d.Accepted);
        Assert.IsType<ResourceCommand.ReleaseUploadScratch>(Assert.Single(d.Commands));
        Denied(d.State, new ResourceEvent.DecodeCompleted(op));
        Denied(d.State, new ResourceEvent.Acquire("p", Source("b"), ResourceReferenceKind.TARGET));
        Denied(d.State, new ResourceEvent.ScratchReleased(op with { OperationId = op.OperationId + 1 }));
        var released = core.Decide(d.State, new ResourceEvent.ScratchReleased(op));
        Assert.True(released.Accepted); Assert.Null(released.State.Scratch);
        Assert.Equal(1, core.Totals(released.State).OwnedAllocations); Assert.Equal(4, core.Totals(released.State).ResidentBytes);
        Denied(released.State, new ResourceEvent.ScratchReleased(op)); Assert.True(Acquire(released.State, v: Source("b")).Accepted);
    }
    [Fact] public void referenceReleaseIsExactAndNeverUnderflowsOrDropsSharedAllocation()
    {
        var s = Acquire(Acquire().State).State; var r = s.References.First();
        Denied(s, new ResourceEvent.ReleaseReference(r with { PlanId = "wrong" }));
        var one = core.Decide(s, new ResourceEvent.ReleaseReference(r)).State;
        Assert.Single(one.References); Denied(one, new ResourceEvent.ReleaseReference(r));
        var zero = core.Decide(one, new ResourceEvent.ReleaseReference(one.References.Single())).State;
        Assert.Empty(zero.References); Assert.Single(zero.Allocations); Assert.Equal(4, core.Totals(zero).ResidentBytes);
    }
    [Fact] public void borrowedIdentityRemainsBorrowedAndNeverDecodes()
    {
        var e = new ResourceEvent.AcquireBorrowed("p", "game-default", 16, 16, ResourceReferenceKind.ROLLBACK);
        var d = core.Decide(new ResourceState(), e); Assert.True(d.Accepted); Assert.Null(d.State.Scratch);
        Assert.Equal(0, core.Totals(d.State).OwnedAllocations); Assert.Equal(0, core.Totals(d.State).ProcessBytes);
        Assert.DoesNotContain(d.Commands, x => x is ResourceCommand.Decode);
        var second = core.Decide(d.State, e).State; Assert.Single(second.Allocations); Assert.Equal(2, second.References.Count);
        Denied(second, e with { Width = 17 }); var s = second;
        foreach (var r in second.References) s = core.Decide(s, new ResourceEvent.ReleaseReference(r)).State;
        Assert.Equal(ResourceOwnership.BORROWED, s.Allocations.Single().Ownership);
    }
    [Fact] public void planResidentExactLimitAndPlusOnePixel()
    {
        var s = new ResourceState(); foreach (var key in new[] { "a", "b", "c" }) s = Ready(Acquire(s, v: Source(key, 32 * MiB)).State);
        Assert.Equal(96 * MiB, core.Totals(s).PlanBytes["p"]);
        Denied(s, new ResourceEvent.Acquire("p", Source("d"), ResourceReferenceKind.TARGET));
    }
    [Fact] public void planAllocationExactLimitAndPlusOne()
    {
        var s = new ResourceState();
        for (var i = 0; i < 205; i++) s = Ready(Acquire(s, v: Source() with { CanonicalSource = $"s{i}", EncodedLength = i + 1L }).State);
        Assert.Equal(205, core.Totals(s).PlanAllocations["p"]);
        Denied(s, new ResourceEvent.Acquire("p", Source("b"), ResourceReferenceKind.TARGET));
    }
    [Fact] public void processAllocationExactLimitAndPlusOne()
    {
        var s = new ResourceState();
        for (var i = 0; i < 410; i++) s = Ready(Acquire(s, i < 205 ? "p" : "q", Source() with { CanonicalSource = $"s{i}", EncodedLength = i + 1L }).State);
        Assert.Equal(410, core.Totals(s).OwnedAllocations);
        Denied(s, new ResourceEvent.Acquire("r", Source("b"), ResourceReferenceKind.TARGET));
    }
    [Fact] public void processPeakExactLimitAndPlusOnePixel()
    {
        var s = new ResourceState(); var keys = new[] { "a", "b", "c", "d", "e", "f" };
        for (var i = 0; i < keys.Length; i++) s = Ready(Acquire(s, i < 3 ? "p" : "q", Source(keys[i], 32 * MiB)).State);
        var exact = Acquire(s, "r", Source("1", 16 * MiB)); Assert.True(exact.Accepted); Assert.Equal(224 * MiB, core.Totals(exact.State).ProcessBytes);
        Denied(s, new ResourceEvent.Acquire("r", Source("1", 16 * MiB + 4), ResourceReferenceKind.TARGET));
    }
    [Fact] public void snapshotsDefensivelyCopyCollectionsAndRejectMalformedLedger()
    {
        var valid = Acquire().State; var allocations = valid.Allocations.ToList(); var refs = valid.References.ToList();
        var snapshot = new ResourceState(allocations, refs, valid.Scratch, valid.NextId); allocations.Clear(); refs.Clear();
        Assert.Single(snapshot.Allocations); Assert.Single(snapshot.References);
        var malformed = new ResourceState(valid.Allocations, new[] { valid.References.Single() with { AllocationId = 999 } }, valid.Scratch, valid.NextId);
        Assert.Null(core.Totals(malformed)); Denied(malformed, new ResourceEvent.DecodeCompleted(valid.Scratch.Operation));
        var duplicate = new ResourceState(valid.Allocations.Concat(valid.Allocations), valid.References, valid.Scratch, valid.NextId);
        Denied(duplicate, new ResourceEvent.DecodeCompleted(valid.Scratch.Operation));
    }
    [Fact] public void releasedReferencesDoNotErasePlanMembershipOrItsBudget()
    {
        var s = new ResourceState(); foreach (var key in new[] { "a", "b", "c" }) s = Ready(Acquire(s, v: Source(key, 32 * MiB)).State);
        foreach (var r in s.References.ToArray()) s = core.Decide(s, new ResourceEvent.ReleaseReference(r)).State;
        Assert.Empty(s.References); Assert.Equal(96 * MiB, core.Totals(s).PlanBytes["p"]);
        Denied(s, new ResourceEvent.Acquire("p", Source("d"), ResourceReferenceKind.MATERIAL));
    }
    [Fact] public void sharedAllocationStillRequiresDestinationPlanBudget()
    {
        var s = new ResourceState(); foreach (var key in new[] { "a", "b", "c" }) s = Ready(Acquire(s, v: Source(key, 32 * MiB)).State);
        s = Ready(Acquire(s, "q", Source("d", 32 * MiB)).State);
        Denied(s, new ResourceEvent.Acquire("p", Source("d", 32 * MiB), ResourceReferenceKind.MATERIAL));
        Assert.Equal(128 * MiB, core.Totals(s).ResidentBytes);
    }
    [Fact] public void sharedAliasesKeepCanonicalContradictionEvidence()
    {
        var original = Ready(Acquire().State); var alias = Source() with { CanonicalSource = "alias", ProofScope = "second-verifier" };
        var shared = Acquire(original, v: alias).State;
        Denied(shared, new ResourceEvent.Acquire("p", alias with { EncodedLength = 101 }, ResourceReferenceKind.TARGET));
        Assert.Single(original.Allocations.Single().Sources); Assert.Equal(2, shared.Allocations.Single().Sources.Count);
    }
    [Fact] public void oldScratchAcknowledgementsCannotReleaseNewOperation()
    {
        var first = Acquire().State; var old = first.Scratch.Operation; var next = Acquire(Ready(first), v: Source("b")).State;
        Denied(next, new ResourceEvent.DecodeCompleted(old)); Denied(next, new ResourceEvent.ScratchReleased(old));
        Denied(next, new ResourceEvent.DecodeCompleted(next.Scratch.Operation with { PlanId = "wrong" }));
    }
    [Fact] public void nestedStateCommandsAndTotalsAreImmutableSnapshots()
    {
        var d = Acquire(); var a = d.State.Allocations.Single(); var sources = a.Sources.ToList(); var plans = a.PlanIds.ToList();
        var allocation = new ResourceAllocation(a.Id, a.Ownership, a.Width, a.Height, sources, plans); sources.Clear(); plans.Clear();
        Assert.Single(allocation.Sources); Assert.Equal(new[] { "p" }, allocation.PlanIds);
        var commands = d.Commands.ToArray(); var decision = new ResourceDecision(d.State, true, "test", commands);
        commands[0] = null; Assert.NotNull(decision.Commands[0]); Assert.Equal(2, decision.Commands.Count);
        var bytes = new Dictionary<string, long> { ["p"] = 4 }; var counts = new Dictionary<string, int> { ["p"] = 1 };
        var totals = new ResourceTotals(4, 8, 1, bytes, counts); bytes.Clear(); counts.Clear();
        Assert.Equal(4, totals.PlanBytes["p"]); Assert.Equal(1, totals.PlanAllocations["p"]);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceCommand>)decision.Commands).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<VerifiedResourceSource>)allocation.Sources).Clear());
    }
    [Fact] public void malformedScratchIdsAndAllocationFactsFailClosed()
    {
        var s = Acquire().State; var a = s.Allocations.Single(); var scratch = s.Scratch;
        var bad = new[] {
            new ResourceState(s.Allocations, s.References, scratch with { Bytes = scratch.Bytes + 1 }, s.NextId),
            new ResourceState(s.Allocations, s.References, scratch with { DecodeCompleted = true }, s.NextId),
            new ResourceState(s.Allocations, s.References, null, s.NextId),
            new ResourceState(s.Allocations, s.References.Concat(s.References), scratch, s.NextId),
            new ResourceState(s.Allocations, s.References, scratch, a.Id),
            new ResourceState(new[] { new ResourceAllocation(a.Id, a.Ownership, long.MaxValue, 2, a.Sources, a.PlanIds) }, s.References, scratch, s.NextId),
            new ResourceState(new[] { new ResourceAllocation(a.Id, a.Ownership, a.Width, a.Height, a.Sources.Concat(a.Sources), a.PlanIds) }, s.References, scratch, s.NextId)
        };
        foreach (var invalid in bad) { Assert.Null(core.Totals(invalid)); Denied(invalid, new ResourceEvent.DecodeCompleted(scratch.Operation)); }
    }
    [Fact] public void malformedClrValuesFailClosed()
    {
        var empty = new ResourceState(); var s = Acquire().State; var a = s.Allocations.Single();
        Denied(empty, null);
        Denied(empty, new ResourceEvent.Acquire("p", null, ResourceReferenceKind.TARGET));
        Denied(empty, new ResourceEvent.Acquire("p", Source(), (ResourceReferenceKind)999));
        Denied(empty, new ResourceEvent.Acquire("p", Source() with { Usage = (ResourceUsage)999 }, ResourceReferenceKind.TARGET));
        Denied(empty, new ResourceEvent.ReleaseReference(null));
        foreach (var invalid in new[] {
            new ResourceState(new ResourceAllocation[] { null }),
            new ResourceState(references: new ResourceReference[] { null }),
            new ResourceState(s.Allocations, s.References, s.Scratch with { Operation = null }, s.NextId),
            new ResourceState(new[] { new ResourceAllocation(a.Id, (ResourceOwnership)999, a.Width, a.Height, a.Sources, a.PlanIds) }, s.References, s.Scratch, s.NextId),
            new ResourceState(new[] { new ResourceAllocation(a.Id, a.Ownership, a.Width, a.Height, new VerifiedResourceSource[] { null }, a.PlanIds) }, s.References, s.Scratch, s.NextId),
            new ResourceState(new[] { new ResourceAllocation(a.Id, a.Ownership, a.Width, a.Height, a.Sources, a.PlanIds, phase: (ResourceAllocationPhase)999) }, s.References, s.Scratch, s.NextId)
        }) { Assert.Null(core.Totals(invalid)); Denied(invalid, new ResourceEvent.DecodeCompleted(s.Scratch.Operation)); }
    }
    [Fact] public void allocationAndOperationSequenceOverflowFailsClosed()
    {
        Denied(new ResourceState(nextId: long.MaxValue), new ResourceEvent.Acquire("p", Source(), ResourceReferenceKind.TARGET));
    }
}
