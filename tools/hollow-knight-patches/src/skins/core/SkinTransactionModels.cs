using System;
using System.Collections.Generic;

namespace DualSouls.Skins.HollowKnight.Core
{
    public sealed record TransactionEnvelope(string TransactionId, SkinOperationKind Operation,
        string BaseGenerationId, string BaseGenerationSha256, ActivationSnapshot Prior, ActivationSnapshot Target,
        SkinBindingToken Binding, bool PriorEstablishedOnBinding);
    public sealed record TransactionCorrelation(string TransactionId, SkinBindingToken Binding);
    public sealed record RegistryCommitReceipt(string ExpectedGenerationId, string ExpectedGenerationSha256,
        string NewGenerationId, string NewGenerationSha256);
    // Trusted executor-supplied canonical-read result, NOT proof manufactured by this reducer.
    // Task4 owns authoritative event construction and disk verification.
    public sealed record VerifiedRegistryHead(string GenerationId, string GenerationSha256,
        ActivationSnapshot Activation, RotationInterlock Interlock);
    public abstract record SkinCommand
    {
        private SkinCommand() { }
        public sealed record Prepare(TransactionEnvelope Envelope, ActiveVisual Desired) : SkinCommand;
        public sealed record Arm(TransactionEnvelope Envelope) : SkinCommand;
        public sealed record Apply(TransactionCorrelation Correlation) : SkinCommand;
        public sealed record Rollback(TransactionCorrelation Correlation) : SkinCommand;
        public sealed record Commit(TransactionCorrelation Correlation, string ExpectedGenerationId,
            string ExpectedGenerationSha256, ActivationSnapshot Closure) : SkinCommand;
    }
    public enum TransactionPhase { IDLE, PREPARING, PREPARED, ARMED, APPLIED, ROLLBACK_PENDING, ROLLED_BACK, COMMITTED, BLOCKED }
    public sealed record TransactionState(TransactionPhase Phase = TransactionPhase.IDLE, RotationInterlock Interlock = null,
        SkinBindingToken Binding = null, RegistryCommitReceipt ArmCommitReceipt = null, ActivationSnapshot PendingClosure = null,
        TransactionEnvelope Envelope = null, ActivationSnapshot Activation = null, string OriginalFailure = null,
        string RollbackFailure = null, RegistryCommitReceipt CompletionReceipt = null, RegistryCommitReceipt FailureReceipt = null)
    {
        public RotationInterlock Interlock { get; init; } = Interlock ?? RotationInterlock.Clear();
    }
    public abstract record TransactionEvent
    {
        private TransactionEvent() { }
        public sealed record Begin(TransactionEnvelope Envelope) : TransactionEvent;
        public sealed record Prepared(TransactionCorrelation Correlation) : TransactionEvent;
        public sealed record ArmCommitted(TransactionCorrelation Correlation, RegistryCommitReceipt CommitReceipt, VerifiedRegistryHead VerifiedHead) : TransactionEvent;
        public sealed record ApplyVerified(TransactionCorrelation Correlation) : TransactionEvent;
        public sealed record ApplyFailed(TransactionCorrelation Correlation, string Code) : TransactionEvent;
        // Fresh means prior was not established on THIS envelope binding, never permission to rebind.
        public sealed record RollbackVerified(TransactionCorrelation Correlation, bool FreshBinding) : TransactionEvent;
        public sealed record RollbackFailed(TransactionCorrelation Correlation, string Code,
            RegistryCommitReceipt PersistedFailureReceipt = null, VerifiedRegistryHead VerifiedHead = null) : TransactionEvent;
        public sealed record CompletionCommitted(TransactionCorrelation Correlation, RegistryCommitReceipt CommitReceipt, VerifiedRegistryHead VerifiedHead) : TransactionEvent;
        public sealed record CompletionRejected(TransactionCorrelation Correlation, string Code) : TransactionEvent;
        public sealed record CompletionIndeterminate(TransactionCorrelation Correlation) : TransactionEvent;
    }
    public sealed class TransactionDecision
    {
        public TransactionState State { get; }
        public IReadOnlyList<SkinCommand> Commands { get; }
        public string Diagnosis { get; }
        public TransactionDecision(TransactionState state, string diagnosis, params SkinCommand[] commands)
        {
            State = state; Diagnosis = diagnosis;
            Commands = Array.AsReadOnly((SkinCommand[])commands.Clone());
        }
    }
}
