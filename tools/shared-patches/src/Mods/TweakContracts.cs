using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    public enum TweakMutationAvailability
    {
        Available,
        Unavailable,
        RestorationPending,
    }

    public enum TweakOperationKind
    {
        InitializeApply,
        Apply,
        Reset,
        Route,
        Command,
        Deferred,
        BaselineRestore,
    }

    public enum TweakReadbackKind
    {
        None,
        Choice,
        Boolean,
        Integer,
        Text,
    }

    public readonly struct TweakReadback
    {
        TweakReadback(TweakReadbackKind kind, string value)
        {
            Kind = kind;
            Value = value ?? "";
        }

        public TweakReadbackKind Kind { get; }
        public string Value { get; }
        public bool HasValue => Kind != TweakReadbackKind.None;

        public static TweakReadback Choice(string value) =>
            new TweakReadback(TweakReadbackKind.Choice, value);
        public static TweakReadback Boolean(bool value) =>
            new TweakReadback(TweakReadbackKind.Boolean, value ? "true" : "false");
        public static TweakReadback Integer(int value) =>
            new TweakReadback(
                TweakReadbackKind.Integer,
                value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public static TweakReadback Text(string value) =>
            new TweakReadback(TweakReadbackKind.Text, value);
    }

    public readonly struct TweakOperationEvidence
    {
        internal TweakOperationEvidence(
            long sequence,
            string gameId,
            string featureId,
            string rowId,
            TweakOperationKind operation,
            bool success,
            string error,
            TweakReadback readback)
        {
            Sequence = sequence;
            GameId = gameId ?? "";
            FeatureId = featureId ?? "";
            RowId = rowId ?? "";
            Operation = operation;
            Success = success;
            Error = error ?? "";
            Readback = readback;
        }

        public long Sequence { get; }
        public string GameId { get; }
        public string FeatureId { get; }
        public string RowId { get; }
        public TweakOperationKind Operation { get; }
        public bool Success { get; }
        public string Error { get; }
        public TweakReadback Readback { get; }
    }

    public enum TweakControlKind
    {
        Choice,
        Command,
        Route,
    }

    /// <summary>A stable, game-neutral row in the built-in Mods menu.</summary>
    public sealed class TweakDescriptor
    {
        public TweakDescriptor(
            string id,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values)
            : this(id, id, TweakControlKind.Choice, group, title, description, defaultValue, values)
        {
        }

        public TweakDescriptor(
            string id,
            string contractId,
            TweakControlKind controlKind,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values)
            : this(id, contractId, controlKind, group, title, description, defaultValue, values, true, "")
        {
        }

        TweakDescriptor(
            string id,
            string contractId,
            TweakControlKind controlKind,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values,
            bool isAvailable,
            string unavailableReason)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A persistence id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(contractId)) throw new ArgumentException("A contract id is required.", nameof(contractId));
            if (!Enum.IsDefined(typeof(TweakControlKind), controlKind)) throw new ArgumentOutOfRangeException(nameof(controlKind));
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("A tweak group is required.", nameof(group));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A tweak title is required.", nameof(title));
            if (!isAvailable && string.IsNullOrWhiteSpace(unavailableReason)) throw new ArgumentException("An unavailable reason is required for unavailable tweaks.", nameof(unavailableReason));
            if (values == null || values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));

            var copy = new string[values.Count];
            bool foundDefault = false;
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Tweak values cannot be blank.", nameof(values));
                for (int j = 0; j < i; j++)
                    if (string.Equals(copy[j], value, StringComparison.Ordinal))
                        throw new ArgumentException("Tweak values must be unique.", nameof(values));
                copy[i] = value;
                if (string.Equals(value, defaultValue, StringComparison.Ordinal)) foundDefault = true;
            }
            if (!foundDefault) throw new ArgumentException("The default must be one of the allowed values.", nameof(defaultValue));

            Id = id;
            ContractId = contractId;
            ControlKind = controlKind;
            Group = group;
            Title = title;
            Description = description ?? "";
            DefaultValue = defaultValue;
            Values = Array.AsReadOnly(copy);
            IsAvailable = isAvailable;
            UnavailableReason = unavailableReason ?? "";
        }

        public static TweakDescriptor Unavailable(
            string id,
            string contractId,
            TweakControlKind controlKind,
            string group,
            string title,
            string description,
            string defaultValue,
            IReadOnlyList<string> values,
            string unavailableReason)
        {
            return new TweakDescriptor(
                id, contractId, controlKind, group, title, description,
                defaultValue, values, false, unavailableReason);
        }

        /// <summary>Stable persistence key used by existing profile state.</summary>
        public string Id { get; }
        /// <summary>Canonical identity shared by both game catalogs.</summary>
        public string ContractId { get; }
        public TweakControlKind ControlKind { get; }
        public string Group { get; }
        public string Title { get; }
        public string Description { get; }
        public string DefaultValue { get; }
        public IReadOnlyList<string> Values { get; }
        public bool IsAvailable { get; }
        public string UnavailableReason { get; }

        public bool Allows(string value)
        {
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        public string Next(string current)
        {
            for (int i = 0; i < Values.Count; i++)
                if (string.Equals(Values[i], current, StringComparison.Ordinal))
                    return Values[(i + 1) % Values.Count];
            return DefaultValue;
        }
    }

    public readonly struct TweakActionResult
    {
        TweakActionResult(
            bool success,
            bool pending,
            long operationToken,
            string error,
            TweakReadback readback)
        {
            Success = success;
            Pending = pending;
            OperationToken = operationToken;
            Error = error ?? "";
            Readback = readback;
        }

        public bool Success { get; }
        public bool Pending { get; }
        public long OperationToken { get; }
        public string Error { get; }
        public TweakReadback Readback { get; }

        public static TweakActionResult Ok() =>
            new TweakActionResult(true, false, 0, "", default(TweakReadback));
        public static TweakActionResult Ok(TweakReadback readback) =>
            new TweakActionResult(true, false, 0, "", readback);
        public static TweakActionResult PendingResult(long operationToken)
        {
            if (operationToken <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationToken));
            return new TweakActionResult(
                true, true, operationToken, "", default(TweakReadback));
        }
        public static TweakActionResult Fail(string error) =>
            new TweakActionResult(
                false,
                false,
                0,
                string.IsNullOrWhiteSpace(error) ? "The tweak could not be applied." : error,
                default(TweakReadback));
    }

    public readonly struct TweakAdapterCompletion
    {
        public TweakAdapterCompletion(
            string rowId,
            long operationToken,
            TweakActionResult result)
        {
            if (string.IsNullOrWhiteSpace(rowId))
                throw new ArgumentException("A completed row id is required.", nameof(rowId));
            if (operationToken <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationToken));
            if (result.Pending)
                throw new ArgumentException("A completion cannot still be pending.", nameof(result));
            RowId = rowId;
            OperationToken = operationToken;
            Result = result;
        }

        public string RowId { get; }
        public long OperationToken { get; }
        public TweakActionResult Result { get; }
    }

    public sealed class TweakDeferredOperation
    {
        readonly string _rowId;
        readonly float _timeoutSeconds;
        long _operationToken;
        float _startedAt;

        public TweakDeferredOperation(string rowId, float timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(rowId))
                throw new ArgumentException("A deferred row id is required.", nameof(rowId));
            if (timeoutSeconds <= 0f || float.IsNaN(timeoutSeconds) ||
                float.IsInfinity(timeoutSeconds))
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            _rowId = rowId;
            _timeoutSeconds = timeoutSeconds;
        }

        public bool IsPending => _operationToken > 0;

        public void Begin(long operationToken, float startedAt)
        {
            if (operationToken <= 0)
                throw new ArgumentOutOfRangeException(nameof(operationToken));
            if (IsPending)
                throw new InvalidOperationException(
                    _rowId + " already has a pending runtime operation.");
            _operationToken = operationToken;
            _startedAt = startedAt;
        }

        public TweakAdapterCompletion? Complete(
            long operationToken,
            TweakActionResult result)
        {
            if (!IsPending || operationToken != _operationToken)
                return null;
            return Finish(result);
        }

        public TweakAdapterCompletion? Cancel(string error)
        {
            return IsPending
                ? Finish(TweakActionResult.Fail(error))
                : (TweakAdapterCompletion?)null;
        }

        public TweakAdapterCompletion? Poll(float now, string timeoutError)
        {
            if (!IsPending || now - _startedAt < _timeoutSeconds)
                return null;
            return Finish(TweakActionResult.Fail(timeoutError));
        }

        TweakAdapterCompletion Finish(TweakActionResult result)
        {
            long operationToken = _operationToken;
            _operationToken = 0;
            _startedAt = 0f;
            return new TweakAdapterCompletion(_rowId, operationToken, result);
        }
    }

    /// <summary>
    /// Optional production evidence boundary. It supplies owner readback and outcomes
    /// that resolve only after deferred gameplay maintenance has actually run.
    /// </summary>
    public interface ITweakOperationAdapter
    {
        TweakActionResult Readback(string id);
        IReadOnlyList<TweakAdapterCompletion> TickWithOutcomes();
    }

    /// <summary>The game-specific side of the shared controller.</summary>
    public interface ITweakAdapter
    {
        string GameId { get; }
        IReadOnlyList<TweakDescriptor> Descriptors { get; }

        void CaptureBaseline();
        TweakActionResult Apply(string id, string value);
        void RestoreBaseline();
        void Tick();
    }

    /// <summary>Small persistence boundary; implementations supply the game-qualified backing store.</summary>
    public interface ITweakStore
    {
        string Read(string key);
        void Write(string key, string value);
        void Flush();
    }
}
