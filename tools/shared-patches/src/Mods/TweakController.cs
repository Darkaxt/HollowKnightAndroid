using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    /// <summary>
    /// Owns menu state without knowing either game. Mutations are transactional:
    /// any failed apply restores the captured baseline and marks mutation health unavailable.
    /// </summary>
    public sealed class TweakController
    {
        public const int MaximumOperationEvidence = 128;
        const int MaximumEvidenceTextLength = 240;

        readonly ITweakAdapter _adapter;
        readonly ITweakStore _store;
        readonly Dictionary<string, TweakDescriptor> _byId = new Dictionary<string, TweakDescriptor>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<TweakOperationEvidence> _operationEvidence = new List<TweakOperationEvidence>();
        readonly Dictionary<string, PendingOperation> _pendingOperations =
            new Dictionary<string, PendingOperation>(StringComparer.Ordinal);
        readonly string _prefix;
        bool _initialized;
        long _nextEvidenceSequence = 1;

        sealed class PendingOperation
        {
            public PendingOperation(
                TweakDescriptor descriptor,
                string previousValue,
                string requestedValue,
                TweakOperationKind operation,
                long operationToken)
            {
                Descriptor = descriptor;
                PreviousValue = previousValue;
                RequestedValue = requestedValue;
                Operation = operation;
                OperationToken = operationToken;
            }

            public TweakDescriptor Descriptor { get; }
            public string PreviousValue { get; }
            public string RequestedValue { get; }
            public TweakOperationKind Operation { get; }
            public long OperationToken { get; }
        }

        public TweakController(ITweakAdapter adapter, ITweakStore store)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(adapter.GameId)) throw new ArgumentException("The adapter game id is required.", nameof(adapter));
            if (adapter.Descriptors == null) throw new ArgumentException("The adapter descriptors are required.", nameof(adapter));

            _prefix = "dualsouls.mods." + adapter.GameId + ".";
            Descriptors = adapter.Descriptors;
            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i] ?? throw new ArgumentException("A tweak descriptor cannot be null.", nameof(adapter));
                if (_byId.ContainsKey(descriptor.Id)) throw new ArgumentException("Duplicate tweak id: " + descriptor.Id, nameof(adapter));
                _byId.Add(descriptor.Id, descriptor);
            }
        }

        public IReadOnlyList<TweakDescriptor> Descriptors { get; }
        public IReadOnlyList<TweakOperationEvidence> OperationEvidence =>
            Array.AsReadOnly(_operationEvidence.ToArray());
        public TweakMutationAvailability MutationAvailability { get; private set; } =
            TweakMutationAvailability.Unavailable;
        public bool MutationsAvailable =>
            MutationAvailability == TweakMutationAvailability.Available;
        public bool HasCapturedBaseline { get; private set; }
        public bool RestorationPending =>
            MutationAvailability == TweakMutationAvailability.RestorationPending;

        public TweakActionResult Initialize()
        {
            if (_initialized) return TweakActionResult.Ok();
            _initialized = true;

            try { _adapter.CaptureBaseline(); }
            catch (Exception e) { return TweakActionResult.Fail("Could not capture the game baseline: " + e.Message); }
            HasCapturedBaseline = true;
            MutationAvailability = TweakMutationAvailability.Available;

            bool corrected = false;
            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
                if (descriptor.ControlKind != TweakControlKind.Choice)
                {
                    _values[descriptor.Id] = descriptor.DefaultValue;
                    continue;
                }

                string key = ValueKey(descriptor.Id);
                string value = SafeRead(key);
                if (value == null)
                {
                    value = descriptor.DefaultValue;
                }
                else if (!descriptor.Allows(value))
                {
                    value = descriptor.DefaultValue;
                    SafeWrite(key, value);
                    corrected = true;
                }
                _values[descriptor.Id] = value;
            }

            if (corrected) SafeFlush();

            return ApplySelectedSet();
        }

        public string Value(string id)
        {
            string value;
            if (_values.TryGetValue(id, out value)) return value;
            TweakDescriptor descriptor;
            return _byId.TryGetValue(id, out descriptor) ? descriptor.DefaultValue : "";
        }

        public void ClearOperationEvidence()
        {
            _operationEvidence.Clear();
        }

        public bool TryGetLatestOperationEvidence(out TweakOperationEvidence evidence)
        {
            if (_operationEvidence.Count == 0)
            {
                evidence = default(TweakOperationEvidence);
                return false;
            }
            evidence = _operationEvidence[_operationEvidence.Count - 1];
            return true;
        }

        public TweakActionResult Cycle(string id)
        {
            EnsureInitialized();
            TweakDescriptor descriptor;
            if (!_byId.TryGetValue(id, out descriptor)) return TweakActionResult.Fail("Unknown tweak: " + id);
            if (descriptor.ControlKind != TweakControlKind.Choice)
                return TweakActionResult.Fail(descriptor.Title + " is a " + descriptor.ControlKind.ToString().ToLowerInvariant() + " and cannot be cycled.");
            return Set(id, descriptor.Next(Value(id)));
        }

        public TweakActionResult Set(string id, string value)
        {
            EnsureInitialized();
            TweakDescriptor descriptor;
            if (!_byId.TryGetValue(id, out descriptor))
                return TweakActionResult.Fail("Unknown tweak: " + id);

            TweakOperationKind operation = OperationFor(descriptor.ControlKind);
            if (_pendingOperations.ContainsKey(id))
            {
                TweakActionResult alreadyPending = TweakActionResult.Fail(
                    descriptor.Title + " already has an unresolved runtime operation.");
                RecordEvidence(descriptor, operation, alreadyPending);
                return alreadyPending;
            }
            if (!MutationsAvailable)
            {
                TweakActionResult unavailable = TweakActionResult.Fail(
                    "Mods mutations are unavailable until RESET ALL MODS restores the runtime baseline.");
                RecordEvidence(descriptor, operation, unavailable);
                return unavailable;
            }
            if (!descriptor.IsAvailable)
            {
                TweakActionResult unavailable = Unavailable(descriptor);
                RecordEvidence(descriptor, operation, unavailable);
                return unavailable;
            }
            if (!descriptor.Allows(value))
            {
                TweakActionResult unsupported = TweakActionResult.Fail(
                    "Unsupported value for " + id + ": " + value);
                RecordEvidence(descriptor, operation, unsupported);
                return unsupported;
            }

            string previous = Value(id);
            TweakActionResult result;
            try { result = _adapter.Apply(id, value); }
            catch (Exception e) { result = TweakActionResult.Fail(e.Message); }

            if (!result.Success)
            {
                TweakActionResult failed = descriptor.ControlKind == TweakControlKind.Choice
                    ? FailClosed(result.Error)
                    : result;
                RecordEvidence(descriptor, operation, failed);
                return failed;
            }

            if (result.Pending)
            {
                _pendingOperations.Add(
                    id,
                    new PendingOperation(
                        descriptor,
                        previous,
                        value,
                        operation,
                        result.OperationToken));
                if (descriptor.ControlKind == TweakControlKind.Choice)
                    _values[id] = value;
                return result;
            }

            if (descriptor.ControlKind != TweakControlKind.Choice)
            {
                RecordEvidence(descriptor, operation, result);
                return result;
            }

            try
            {
                _store.Write(ValueKey(id), value);
                _store.Flush();
            }
            catch (Exception e)
            {
                _values[id] = previous;
                BestEffortWrite(ValueKey(id), previous);
                TweakActionResult failed = FailClosed(
                    "Could not persist " + descriptor.Title + ": " + e.Message);
                RecordEvidence(descriptor, operation, failed);
                return failed;
            }
            _values[id] = value;
            RecordEvidence(descriptor, operation, result);
            return result;
        }

        public TweakActionResult CompletePending(
            string id,
            long operationToken,
            TweakActionResult completion)
        {
            EnsureInitialized();
            PendingOperation pending;
            if (string.IsNullOrWhiteSpace(id) ||
                !_pendingOperations.TryGetValue(id, out pending) ||
                pending.OperationToken != operationToken)
                return TweakActionResult.Fail(
                    "The runtime operation is stale or no longer pending: " + (id ?? ""));
            if (completion.Pending)
                return TweakActionResult.Fail(
                    "The runtime operation has not completed yet: " + id);

            _pendingOperations.Remove(id);
            TweakDescriptor descriptor = pending.Descriptor;
            TweakOperationKind evidenceOperation =
                descriptor.ControlKind == TweakControlKind.Choice
                    ? TweakOperationKind.Deferred
                    : pending.Operation;

            if (!completion.Success)
            {
                if (descriptor.ControlKind == TweakControlKind.Choice)
                    return FailPendingChoice(
                        id,
                        pending,
                        evidenceOperation,
                        completion.Error);
                RecordEvidence(descriptor, evidenceOperation, completion);
                return completion;
            }

            if (descriptor.ControlKind != TweakControlKind.Choice)
            {
                RecordEvidence(descriptor, evidenceOperation, completion);
                return completion;
            }

            if (!completion.Readback.HasValue ||
                completion.Readback.Kind != TweakReadbackKind.Choice ||
                !string.Equals(
                    completion.Readback.Value,
                    pending.RequestedValue,
                    StringComparison.Ordinal))
            {
                return FailPendingChoice(
                    id,
                    pending,
                    evidenceOperation,
                    descriptor.Title +
                        " runtime readback was missing or did not match the requested value.");
            }

            try
            {
                _store.Write(ValueKey(id), pending.RequestedValue);
                _store.Flush();
            }
            catch (Exception e)
            {
                BestEffortWrite(ValueKey(id), pending.PreviousValue);
                return FailPendingChoice(
                    id,
                    pending,
                    evidenceOperation,
                    "Could not persist " + descriptor.Title + ": " + e.Message);
            }

            _values[id] = pending.RequestedValue;
            RecordEvidence(descriptor, evidenceOperation, completion);
            return completion;
        }

        TweakActionResult FailPendingChoice(
            string id,
            PendingOperation pending,
            TweakOperationKind operation,
            string error)
        {
            _values[id] = pending.PreviousValue;
            TweakActionResult failure = FailClosed(error);
            RecordEvidence(pending.Descriptor, operation, failure);

            CancelPendingOperations(
                "The deferred Mods operation was canceled because another deferred Mods choice failed and the runtime baseline was restored.");
            return failure;
        }

        void CancelPendingOperations(string error)
        {
            if (_pendingOperations.Count == 0) return;
            PendingOperation[] pending = new PendingOperation[_pendingOperations.Count];
            _pendingOperations.Values.CopyTo(pending, 0);
            _pendingOperations.Clear();
            for (int index = 0; index < pending.Length; index++)
            {
                PendingOperation operation = pending[index];
                if (operation.Descriptor.ControlKind == TweakControlKind.Choice)
                    _values[operation.Descriptor.Id] = operation.PreviousValue;
                RecordEvidence(
                    operation.Descriptor,
                    operation.Descriptor.ControlKind == TweakControlKind.Choice
                        ? TweakOperationKind.Deferred
                        : operation.Operation,
                    TweakActionResult.Fail(error));
            }
        }

        public TweakActionResult Reset()
        {
            EnsureInitialized();
            if (RestorationPending)
            {
                TweakActionResult pending = TweakActionResult.Fail(
                    "The game baseline restoration is still pending; RESET ALL MODS will be available after recovery.");
                RecordResetOutcomes(pending);
                return pending;
            }
            CancelPendingOperations(
                "The deferred Mods operation was canceled because RESET ALL MODS restored the runtime baseline.");
            try
            {
                _adapter.RestoreBaseline();
                MutationAvailability = TweakMutationAvailability.Available;
            }
            catch (Exception e)
            {
                TweakActionResult failed = MarkRestorationPending(
                    "Could not restore the game baseline: " + e.Message);
                RecordResetOutcomes(failed);
                return failed;
            }

            TweakActionResult[] resetOutcomes = BuildResetOutcomes(TweakActionResult.Ok());
            TweakActionResult resetFailure = FirstFailure(resetOutcomes);
            if (!resetFailure.Success)
            {
                MutationAvailability = TweakMutationAvailability.Unavailable;
                RecordResetOutcomes(resetOutcomes);
                return resetFailure;
            }

            var previousValues = new Dictionary<string, string>(_values, StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < Descriptors.Count; i++)
                {
                    TweakDescriptor descriptor = Descriptors[i];
                    if (descriptor.ControlKind == TweakControlKind.Choice)
                        _store.Write(ValueKey(descriptor.Id), descriptor.DefaultValue);
                }
                _store.Flush();
            }
            catch (Exception e)
            {
                for (int i = 0; i < Descriptors.Count; i++)
                {
                    TweakDescriptor descriptor = Descriptors[i];
                    string previousValue;
                    if (descriptor.ControlKind == TweakControlKind.Choice &&
                        previousValues.TryGetValue(descriptor.Id, out previousValue))
                        BestEffortWrite(ValueKey(descriptor.Id), previousValue);
                }
                TweakActionResult failed = FailClosed(
                    "Could not persist reset settings: " + e.Message,
                    false);
                RecordResetOutcomes(failed);
                return failed;
            }

            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
                _values[descriptor.Id] = descriptor.DefaultValue;
            }
            MutationAvailability = TweakMutationAvailability.Available;
            TweakActionResult result = TweakActionResult.Ok();
            RecordResetOutcomes(resetOutcomes);
            return result;
        }

        public TweakActionResult RetryRestoration()
        {
            EnsureInitialized();
            if (!RestorationPending) return TweakActionResult.Ok();
            TweakActionResult result;
            try
            {
                _adapter.RestoreBaseline();
                MutationAvailability = TweakMutationAvailability.Unavailable;
                result = TweakActionResult.Ok();
            }
            catch (Exception e)
            {
                result = TweakActionResult.Fail(
                    "Could not restore the game baseline: " + e.Message);
            }
            RecordSystemEvidence(TweakOperationKind.BaselineRestore, result);
            return result;
        }

        internal void MarkSessionUnavailable()
        {
            MutationAvailability = TweakMutationAvailability.Unavailable;
        }

        public void Tick()
        {
            if (!_initialized) return;
            if (RestorationPending)
            {
                RetryRestoration();
                return;
            }
            if (!MutationsAvailable) return;

            IReadOnlyList<TweakAdapterCompletion> completions =
                Array.Empty<TweakAdapterCompletion>();
            try
            {
                ITweakOperationAdapter operationAdapter =
                    _adapter as ITweakOperationAdapter;
                if (operationAdapter != null)
                    completions = operationAdapter.TickWithOutcomes() ??
                        Array.Empty<TweakAdapterCompletion>();
                else
                    _adapter.Tick();
            }
            catch (Exception e)
            {
                TweakActionResult failure = TweakActionResult.Fail(
                    "Tweak maintenance failed: " + e.Message);
                if (_pendingOperations.Count == 0)
                {
                    TweakActionResult failed = FailClosed(failure.Error);
                    RecordSystemEvidence(TweakOperationKind.Deferred, failed);
                    return;
                }

                string[] pendingIds = new string[_pendingOperations.Count];
                _pendingOperations.Keys.CopyTo(pendingIds, 0);
                for (int index = 0; index < pendingIds.Length; index++)
                {
                    PendingOperation pending;
                    if (_pendingOperations.TryGetValue(pendingIds[index], out pending))
                        CompletePending(
                            pendingIds[index],
                            pending.OperationToken,
                            failure);
                }
                return;
            }

            for (int index = 0; index < completions.Count; index++)
            {
                TweakAdapterCompletion completion = completions[index];
                CompletePending(
                    completion.RowId,
                    completion.OperationToken,
                    completion.Result);
            }
        }

        TweakActionResult ApplySelectedSet()
        {
            long firstPendingToken = 0;
            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
                if (!descriptor.IsAvailable || descriptor.ControlKind != TweakControlKind.Choice) continue;
                string value = Value(descriptor.Id);
                if (string.Equals(value, descriptor.DefaultValue, StringComparison.Ordinal)) continue;

                TweakActionResult result;
                try { result = _adapter.Apply(descriptor.Id, value); }
                catch (Exception e) { result = TweakActionResult.Fail(e.Message); }
                if (!result.Success)
                {
                    TweakActionResult failed = FailClosed(result.Error);
                    RecordEvidence(
                        descriptor,
                        TweakOperationKind.InitializeApply,
                        failed);
                    return failed;
                }
                if (result.Pending)
                {
                    _pendingOperations.Add(
                        descriptor.Id,
                        new PendingOperation(
                            descriptor,
                            value,
                            value,
                            TweakOperationKind.InitializeApply,
                            result.OperationToken));
                    if (firstPendingToken == 0)
                        firstPendingToken = result.OperationToken;
                    continue;
                }
                RecordEvidence(
                    descriptor,
                    TweakOperationKind.InitializeApply,
                    result);
            }

            MutationAvailability = TweakMutationAvailability.Available;
            return firstPendingToken != 0
                ? TweakActionResult.PendingResult(firstPendingToken)
                : TweakActionResult.Ok();
        }

        static TweakActionResult Unavailable(TweakDescriptor descriptor)
        {
            return TweakActionResult.Fail(
                descriptor.Title + " is currently unavailable: " + descriptor.UnavailableReason);
        }

        TweakActionResult FailClosed(string error, bool restoreBaseline = true)
        {
            if (restoreBaseline)
            {
                try
                {
                    _adapter.RestoreBaseline();
                    MutationAvailability = TweakMutationAvailability.Available;
                }
                catch (Exception restoreError)
                {
                    MutationAvailability = TweakMutationAvailability.RestorationPending;
                    error += "; baseline restore failed: " + restoreError.Message;
                }
            }
            if (MutationAvailability != TweakMutationAvailability.RestorationPending)
                MutationAvailability = TweakMutationAvailability.Unavailable;
            return TweakActionResult.Fail(error);
        }

        TweakActionResult MarkRestorationPending(string error)
        {
            MutationAvailability = TweakMutationAvailability.RestorationPending;
            return TweakActionResult.Fail(error);
        }

        string ValueKey(string id) => _prefix + "value." + id;

        string SafeRead(string key)
        {
            try { return _store.Read(key); }
            catch { return null; }
        }

        void SafeWrite(string key, string value)
        {
            try { _store.Write(key, value); } catch { }
        }

        void SafeFlush()
        {
            try { _store.Flush(); } catch { }
        }

        void BestEffortWrite(string key, string value)
        {
            try
            {
                _store.Write(key, value);
                _store.Flush();
            }
            catch { }
        }

        TweakActionResult[] BuildResetOutcomes(TweakActionResult result)
        {
            var outcomes = new List<TweakActionResult>();
            ITweakOperationAdapter operationAdapter =
                result.Success ? _adapter as ITweakOperationAdapter : null;
            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
                if (descriptor.ControlKind != TweakControlKind.Choice) continue;

                TweakActionResult outcome = result;
                if (operationAdapter != null)
                {
                    try { outcome = operationAdapter.Readback(descriptor.Id); }
                    catch (Exception error)
                    {
                        outcome = TweakActionResult.Fail(
                            "Could not read back " + descriptor.Title + ": " + error.Message);
                    }
                    if (outcome.Pending)
                        outcome = TweakActionResult.Fail(
                            descriptor.Title + " reset readback is still pending.");
                    else if (outcome.Success &&
                             (!outcome.Readback.HasValue ||
                              outcome.Readback.Kind != TweakReadbackKind.Choice ||
                              !string.Equals(
                                  outcome.Readback.Value,
                                  descriptor.DefaultValue,
                                  StringComparison.Ordinal)))
                        outcome = TweakActionResult.Fail(
                            descriptor.Title + " reset readback was missing or did not match the default.");
                }
                outcomes.Add(outcome);
            }
            return outcomes.ToArray();
        }

        static TweakActionResult FirstFailure(TweakActionResult[] outcomes)
        {
            for (int index = 0; index < outcomes.Length; index++)
                if (!outcomes[index].Success) return outcomes[index];
            return TweakActionResult.Ok();
        }

        void RecordResetOutcomes(TweakActionResult result)
        {
            RecordResetOutcomes(BuildResetOutcomes(result));
        }

        void RecordResetOutcomes(TweakActionResult[] outcomes)
        {
            int outcomeIndex = 0;
            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
                if (descriptor.ControlKind != TweakControlKind.Choice) continue;
                RecordEvidence(
                    descriptor,
                    TweakOperationKind.Reset,
                    outcomes[outcomeIndex++]);
            }
        }

        void RecordEvidence(
            TweakDescriptor descriptor,
            TweakOperationKind operation,
            TweakActionResult result)
        {
            TweakReadback readback = result.Readback.HasValue
                ? SafeReadback(descriptor, result.Readback)
                : default(TweakReadback);
            AddEvidence(new TweakOperationEvidence(
                _nextEvidenceSequence++,
                _adapter.GameId,
                FeatureId(_adapter.GameId, descriptor.ContractId),
                descriptor.Id,
                operation,
                result.Success,
                result.Success ? "" : SanitizeEvidenceText(result.Error),
                result.Success ? readback : default(TweakReadback)));
        }

        void RecordSystemEvidence(
            TweakOperationKind operation,
            TweakActionResult result)
        {
            AddEvidence(new TweakOperationEvidence(
                _nextEvidenceSequence++,
                _adapter.GameId,
                "MOD-TRANSACTION-SAFETY",
                "runtime",
                operation,
                result.Success,
                result.Success ? "" : SanitizeEvidenceText(result.Error),
                default(TweakReadback)));
        }

        void AddEvidence(TweakOperationEvidence evidence)
        {
            if (_operationEvidence.Count == MaximumOperationEvidence)
                _operationEvidence.RemoveAt(0);
            _operationEvidence.Add(evidence);
        }

        static TweakOperationKind OperationFor(TweakControlKind controlKind)
        {
            if (controlKind == TweakControlKind.Command) return TweakOperationKind.Command;
            if (controlKind == TweakControlKind.Route) return TweakOperationKind.Route;
            return TweakOperationKind.Apply;
        }

        static string FeatureId(string gameId, string contractId)
        {
            if (string.Equals(gameId, "hollow-knight", StringComparison.Ordinal) &&
                string.Equals(contractId, "lifeblood_flash", StringComparison.Ordinal))
                return "MOD-HK-LIFEBLOOD-FLASH";
            if (string.Equals(gameId, "silksong", StringComparison.Ordinal))
            {
                switch (contractId)
                {
                    case "instant_dialogue": return "MOD-SS-INSTANT-DIALOGUE";
                    case "disable_world_rumble": return "MOD-SS-DISABLE-WORLD-RUMBLE";
                    case "ignore_frost_slowdown": return "MOD-SS-IGNORE-FROST-SLOWDOWN";
                }
            }
            return "MOD-" + (contractId ?? "unknown")
                .Replace('_', '-')
                .ToUpperInvariant();
        }

        static TweakReadback SafeReadback(
            TweakDescriptor descriptor,
            TweakReadback readback)
        {
            if (!readback.HasValue) return default(TweakReadback);
            switch (readback.Kind)
            {
                case TweakReadbackKind.Choice:
                    return descriptor.Allows(readback.Value)
                        ? readback
                        : default(TweakReadback);
                case TweakReadbackKind.Boolean:
                    return string.Equals(readback.Value, "true", StringComparison.Ordinal) ||
                           string.Equals(readback.Value, "false", StringComparison.Ordinal)
                        ? readback
                        : default(TweakReadback);
                case TweakReadbackKind.Integer:
                    int parsed;
                    return int.TryParse(
                        readback.Value,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out parsed)
                        ? TweakReadback.Integer(parsed)
                        : default(TweakReadback);
                case TweakReadbackKind.Text:
                    return TweakReadback.Text(SanitizeEvidenceText(readback.Value));
                default:
                    return default(TweakReadback);
            }
        }

        static string SanitizeEvidenceText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "The operation failed.";
            var safe = new System.Text.StringBuilder();
            for (int index = 0; index < value.Length && safe.Length < MaximumEvidenceTextLength;)
            {
                bool windowsPath = index + 2 < value.Length &&
                    char.IsLetter(value[index]) && value[index + 1] == ':' &&
                    (value[index + 2] == '\\' || value[index + 2] == '/');
                bool networkPath = index + 1 < value.Length &&
                    ((value[index] == '\\' && value[index + 1] == '\\') ||
                     (value[index] == '/' && value[index + 1] == '/'));
                bool rootedPath =
                    (value[index] == '/' || value[index] == '\\') &&
                    (index == 0 || !char.IsLetterOrDigit(value[index - 1]));
                if (windowsPath || networkPath || rootedPath)
                {
                    const string marker = "[path]";
                    for (int markerIndex = 0;
                         markerIndex < marker.Length &&
                         safe.Length < MaximumEvidenceTextLength;
                         markerIndex++)
                        safe.Append(marker[markerIndex]);
                    index += windowsPath ? 3 : networkPath ? 2 : 1;
                    while (index < value.Length && value[index] != ';' &&
                           value[index] != ',')
                        index++;
                    continue;
                }

                char next = value[index++];
                safe.Append(char.IsControl(next) ? ' ' : next);
            }
            return safe.ToString().Trim();
        }

        void EnsureInitialized()
        {
            if (!_initialized) throw new InvalidOperationException("Initialize the tweak controller first.");
        }
    }
}
