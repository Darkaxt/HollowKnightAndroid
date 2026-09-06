using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Origin retains exact hero+skin and issued readiness. Qualified durable recovery
    // retires it into one bounded historical audit.
    public sealed record RotationTransactionOperation(RotationModeState Origin, RotationReadyIntent Readiness,
        TransactionState Transaction, bool ReboundBlocked = false);
    public sealed record RotationTransactionState(RotationModeState Mode, RotationTransactionOperation Operation = null,
        bool ReadinessReboundBlocked = false, RotationRecoveryRecord LastRecovery = null);
    // Trusted current authority, not discovered by the reducer.
    public sealed record RotationRecoveryAuthority(HeroBindingToken Hero, SkinBindingToken Skin, HeroVerifiedVisual LiveProof = null);
    // Trusted serialized outcome attestation: ALL old execution and late callbacks are
    // irrevocably fenced; this exact parent was authoritative at the closure CAS. The core
    // validates value correlation/chain, not runtime truth or construction authority.
    // Optional LateFailureReceipt is ONE ARM-to-failure hop when both failure codes are
    // known and no receipt was stored. It can never replace stored failure authority.
    public sealed record RotationOutcomeFence(TransactionCorrelation Correlation, HeroBindingToken OriginalHero,
        VerifiedRegistryHead AuthoritativeParent, RegistryCommitReceipt LateFailureReceipt = null);
    public abstract record RotationRecoveryEvidence(RotationRecoveryAuthority Authority)
    {
        // Exact already-issued stored pending closure; not a caller-selected target.
        public sealed record IssuedClosureOutcome(RotationTransactionOperation Operation, RotationOutcomeFence Fence,
            RegistryCommitReceipt Receipt, VerifiedRegistryHead Head, RotationRecoveryAuthority Authority) : RotationRecoveryEvidence(Authority);
        // Trusted completed original-binding prior restoration serialized with the fence
        // and authoritative parent. Matching visuals cannot supersede a possibly committed
        // target: the verifier must establish the entire serialized outcome.
        public sealed record PriorRestoredAndFenced(RotationTransactionOperation Operation, RotationOutcomeFence Fence,
            HeroVerifiedVisual Restoration, RegistryCommitReceipt Receipt, VerifiedRegistryHead Head,
            RotationRecoveryAuthority Authority) : RotationRecoveryEvidence(Authority);
        public sealed record VisualClosure(RotationTransactionOperation Operation, TransactionEvent.CompletionCommitted Completion,
            RotationRecoveryAuthority Authority) : RotationRecoveryEvidence(Authority);
        // All old deliveries fenced, Apply never ran (Prepare/ARM may have occurred).
        // A head observation alone is insufficient.
        public sealed record VisualFencedNotExecuted(RotationTransactionOperation Operation, VerifiedRegistryHead Head,
            RotationRecoveryAuthority Authority, RegistryCommitReceipt Receipt = null) : RotationRecoveryEvidence(Authority);
        public sealed record ModeClosure(ModeOperation Operation, RotationModeEvent.ModeCommitted Completion,
            RotationRecoveryAuthority Authority) : RotationRecoveryEvidence(Authority);
        // Exact intent never consumed/executed; all its execution deliveries fenced.
        public sealed record ReadinessFencedNotExecuted(RotationReadyIntent Intent, VerifiedRegistryHead Head,
            RotationRecoveryAuthority Authority) : RotationRecoveryEvidence(Authority);
    }
    // One bounded snapshot: Before.LastRecovery must be null. No resource release authority.
    public sealed record RotationRecoveryRecord(RotationTransactionState Before, RotationRecoveryEvidence Evidence);
    public abstract record RotationTransactionEvent
    {
        private RotationTransactionEvent() { }
        public sealed record Recover(RotationRecoveryEvidence Evidence) : RotationTransactionEvent;
        public sealed record Mode(RotationModeEvent Event) : RotationTransactionEvent;
        public sealed record ConsumeReady(RotationReadyIntent Intent) : RotationTransactionEvent;
        public sealed record Transaction(HeroBindingToken Hero, TransactionEvent Event) : RotationTransactionEvent;
    }
    public abstract record RotationTransactionCommand
    {
        private RotationTransactionCommand() { }
        public sealed record Mode(RotationModeCommand Command) : RotationTransactionCommand;
        // Caller checks original hero AND the inner command's skin correlation.
        public sealed record Transaction(HeroBindingToken Hero, SkinCommand Command) : RotationTransactionCommand;
    }
    public sealed class RotationTransactionDecision
    {
        public RotationTransactionState State { get; }
        public string Diagnosis { get; }
        public IReadOnlyList<RotationTransactionCommand> Commands { get; }
        public RotationTransactionDecision(RotationTransactionState state, string diagnosis,
            IEnumerable<RotationTransactionCommand> commands = null)
        { State=state; Diagnosis=diagnosis; Commands=Array.AsReadOnly(commands?.ToArray() ?? Array.Empty<RotationTransactionCommand>()); }
    }
}
