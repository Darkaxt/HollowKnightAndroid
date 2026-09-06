using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Descriptor-supplied membership/current object; not catalog or live-visual proof.
    public sealed record RotationDescriptor(string Name, ActiveVisual.Pack CurrentObject, bool Eligible);
    public sealed class RotationRing
    {
        public IReadOnlyList<RotationDescriptor> Entries { get; }
        private RotationRing(IEnumerable<RotationDescriptor> entries) { Entries = Array.AsReadOnly(entries.ToArray()); }
        public static RotationRing TryCreate(IReadOnlyList<RotationDescriptor> entries)
        {
            if (entries == null || entries.Count > 64) return null;
            var snapshot = entries.ToArray();
            if (snapshot.Any(x => !RotationValues.Descriptor(x)) || snapshot.Select(x => x.CurrentObject.Id).Distinct().Count() != snapshot.Length) return null;
            Array.Sort(snapshot, (a,b) => {
                var name = RotationValues.Utf8Compare(a.Name,b.Name);
                return name != 0 ? name : string.CompareOrdinal(a.CurrentObject.Id,b.CurrentObject.Id);
            });
            return new RotationRing(snapshot);
        }
    }
    public enum RotationPendingPhase { AWAITING_STABILITY, INTENT_ISSUED }
    // Readiness only: grants no Prepare/Arm/Apply or publication authority.
    public sealed record RotationReadyIntent(DeathEpoch Epoch, HeroBindingToken Hero, SkinBindingToken Skin,
        RotationDescriptor Candidate, ActivationSnapshot Prior);
    public sealed record PendingRotation(RotationDescriptor Candidate, DeathEpoch Epoch, HeroBindingToken Hero,
        SkinBindingToken Skin, RotationPendingPhase Phase = RotationPendingPhase.AWAITING_STABILITY,
        RotationReadyIntent IssuedIntent = null);
    // Activation is supplied history. This slice never changes modes or establishes live proof.
    public sealed record RotationState(RotationRing Ring, ActivationSnapshot Activation, HeroBindingToken CurrentHero = null,
        SkinBindingToken CurrentSkin = null, DeathEpoch EpochHighWater = null, PendingRotation Pending = null)
    {
        public DeathEpoch EpochHighWater { get; init; } = EpochHighWater ?? new DeathEpoch(0);
    }
    public abstract record RotationEvent
    {
        private RotationEvent() { }
        public sealed record Rebind(HeroBindingToken Hero, SkinBindingToken Skin) : RotationEvent;
        public sealed record ConfirmDeath(DeathEpoch Epoch, HeroBindingToken Hero, SkinBindingToken Skin) : RotationEvent;
        public sealed record StableRespawn(StableRespawnToken Token) : RotationEvent;
    }
    internal static class RotationValues
    {
        private static readonly Regex Id = new("\\A[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?\\z");
        private static readonly Regex Hash = new("\\A[0-9a-f]{64}\\z");
        internal static bool PackId(string value) => value != null && value.Length <= 64 && Id.IsMatch(value);
        internal static bool Digest(string value) => value != null && value.Length == 64 && Hash.IsMatch(value);
        internal static bool Pack(ActiveVisual.Pack value) => value != null && PackId(value.Id) &&
            Digest(value.TreeSha256) && Digest(value.ContentSha256) && Digest(value.ImportReceiptSha256);
        // Same scalar/name restrictions as Kotlin CanonicalJson.requireDisplayName.
        internal static bool Text(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum * 2 || value != value.Trim()) return false;
            var count = 0;
            for (var i=0; i<value.Length; i++,count++)
            {
                var c=value[i];
                if (char.IsControl(c) || c=='؜' || c=='‎' || c=='‏' || c>='‪' && c<='‮' || c>='⁦' && c<='⁩') return false;
                if (char.IsHighSurrogate(c)) { i++; if (i>=value.Length || !char.IsLowSurrogate(value[i])) return false; }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return count <= maximum && NormalizedScalarText(value);
        }
        // Called only after the bounded full-string surrogate/control validation above.
        // CLR normalization rejects some valid noncharacters (notably U+FFFE), unlike
        // Java NFKC. All 66 noncharacters have no decomposition and CCC=0, so they
        // separate normalization runs without changing the original text or UTF-8 order.
        private static bool NormalizedScalarText(string value)
        {
            var start = 0;
            for (var i = 0; i < value.Length;)
            {
                var scalar = char.ConvertToUtf32(value, i);
                var width = scalar > 0xFFFF ? 2 : 1;
                var noncharacter = (scalar >= 0xFDD0 && scalar <= 0xFDEF) || (scalar & 0xFFFF) >= 0xFFFE;
                if (noncharacter)
                {
                    if (!NormalizedSegment(value, start, i - start)) return false;
                    start = i + width;
                }
                i += width; // A supplementary boundary consumes the complete surrogate pair.
            }
            return NormalizedSegment(value, start, value.Length - start);
        }
        private static bool NormalizedSegment(string value, int start, int length)
        {
            if (length == 0) return true;
            try
            {
                return value.Substring(start, length).IsNormalized(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // An unexpected ordinary-segment rejection never becomes normalization proof.
                return false;
            }
        }
        internal static bool Descriptor(RotationDescriptor value) => value != null && Text(value.Name,80) &&
            value.Name.IndexOf('/') < 0 && value.Name.IndexOf('\\') < 0 && Pack(value.CurrentObject);
        internal static bool Activation(ActivationSnapshot value) => value != null &&
            (value.Mode==SkinMode.OFF || value.Mode==SkinMode.ON || value.Mode==SkinMode.ROTATE) && value.SkinStamp>=0 &&
            (value.SelectedPackId==null || PackId(value.SelectedPackId)) &&
            (value.Active is ActiveVisual.Vanilla || value.Active is ActiveVisual.Pack p && Pack(p));
        internal static int Utf8Compare(string a,string b)
        {
            var left=Encoding.UTF8.GetBytes(a);var right=Encoding.UTF8.GetBytes(b);
            for(var i=0;i<Math.Min(left.Length,right.Length);i++) { var difference=left[i]-right[i];if(difference!=0)return difference; }
            return left.Length.CompareTo(right.Length);
        }
    }
    // Value mirror of registry proof. Freshness is a trusted event boundary, not persisted history.
    public abstract record VerifiedLiveVisualProof(SkinBindingToken Binding)
    {
        public sealed record Vanilla(SkinBindingToken Binding) : VerifiedLiveVisualProof(Binding);
        public sealed record Pack(SkinBindingToken Binding, ActiveVisual.Pack Visual) : VerifiedLiveVisualProof(Binding);
    }
    public sealed record HeroVerifiedVisual(HeroBindingToken Hero, VerifiedLiveVisualProof Proof);
    public sealed record ModeCorrelation(string OperationId, HeroBindingToken Hero, SkinBindingToken Skin);
    public enum ModeCommitPhase { ISSUED, INDETERMINATE, DEFINITIVE_FAILURE }
    public sealed record ModeOperation(ModeCorrelation Correlation, VerifiedRegistryHead BaseHead,
        ActivationSnapshot Target, HeroVerifiedVisual VanillaProof = null,
        ModeCommitPhase Phase = ModeCommitPhase.ISSUED, bool ReboundBlocked = false, HeroVerifiedVisual SelectedProof = null);
    // H1 owns only mode-only CAS. Visual transactions and rebound resolution belong to task66.
    public sealed record RotationModeState(RotationState Rotation, VerifiedRegistryHead Head,
        HeroVerifiedVisual LiveProof = null, long OperationHighWater = 0, ModeOperation Operation = null,
        bool OffRequested = false, PendingRotation CanceledPending = null);
    public abstract record RotationModeEvent
    {
        private RotationModeEvent() { }
        public sealed record AdvanceMode : RotationModeEvent;
        public sealed record VerifiedVisual(HeroVerifiedVisual Value) : RotationModeEvent;
        public sealed record Selector(RotationEvent Event) : RotationModeEvent;
        public sealed record ModeCommitted(ModeCorrelation Correlation, RegistryCommitReceipt Receipt, VerifiedRegistryHead Head) : RotationModeEvent;
        public sealed record ModeIndeterminate(ModeCorrelation Correlation) : RotationModeEvent;
        public sealed record ModeFailed(ModeCorrelation Correlation) : RotationModeEvent;
        // Retry is the identical CAS value, never a changed parent or reset of uncertainty.
        public sealed record RetryModeCommit(ModeCorrelation Correlation) : RotationModeEvent;
    }
    public abstract record RotationModeCommand(ModeOperation Operation)
    {
        public sealed record CommitVerifiedSelectedOn(ModeOperation Operation) : RotationModeCommand(Operation);
        public sealed record CommitOnToRotate(ModeOperation Operation) : RotationModeCommand(Operation);
        public sealed record CommitVerifiedVanillaOff(ModeOperation Operation) : RotationModeCommand(Operation);
    }
    public sealed class RotationModeDecision
    {
        public RotationModeState State { get; }
        public string Diagnosis { get; }
        public IReadOnlyList<RotationModeCommand> Commands { get; }
        public RotationModeDecision(RotationModeState state, string diagnosis, params RotationModeCommand[] commands)
        { State = state; Diagnosis = diagnosis; Commands = Array.AsReadOnly((RotationModeCommand[])commands.Clone()); }
    }
    public sealed class RotationDecision
    {
        public RotationState State { get; }
        public string Diagnosis { get; }
        public IReadOnlyList<RotationReadyIntent> Intents { get; }
        public RotationDecision(RotationState state, string diagnosis, params RotationReadyIntent[] intents)
        { State = state; Diagnosis = diagnosis; Intents = Array.AsReadOnly((RotationReadyIntent[])intents.Clone()); }
    }
}
