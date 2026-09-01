using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    /// <summary>
    /// Owns menu state without knowing either game. Mutations are transactional:
    /// any failed apply restores the captured baseline and turns master off.
    /// </summary>
    public sealed class TweakController
    {
        readonly ITweakAdapter _adapter;
        readonly ITweakStore _store;
        readonly Dictionary<string, TweakDescriptor> _byId = new Dictionary<string, TweakDescriptor>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly string _prefix;
        bool _initialized;

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
        public bool MasterEnabled { get; private set; }

        public TweakActionResult Initialize()
        {
            if (_initialized) return TweakActionResult.Ok();
            _initialized = true;

            try { _adapter.CaptureBaseline(); }
            catch (Exception e) { return TweakActionResult.Fail("Could not capture the game baseline: " + e.Message); }

            bool corrected = false;
            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
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

            string storedMaster = SafeRead(MasterKey);
            MasterEnabled = string.Equals(storedMaster, "1", StringComparison.Ordinal);
            if (storedMaster != "0" && storedMaster != "1")
            {
                MasterEnabled = false;
                SafeWrite(MasterKey, "0");
                corrected = true;
            }
            if (corrected) SafeFlush();

            if (!MasterEnabled) return TweakActionResult.Ok();
            return ApplySelectedSet();
        }

        public string Value(string id)
        {
            string value;
            if (_values.TryGetValue(id, out value)) return value;
            TweakDescriptor descriptor;
            return _byId.TryGetValue(id, out descriptor) ? descriptor.DefaultValue : "";
        }

        public TweakActionResult SetMaster(bool enabled)
        {
            EnsureInitialized();
            if (enabled == MasterEnabled) return TweakActionResult.Ok();

            if (!enabled)
            {
                try { _adapter.RestoreBaseline(); }
                catch (Exception e) { return FailClosed("Could not restore the game baseline: " + e.Message); }
                MasterEnabled = false;
                TweakActionResult persisted = PersistMaster();
                return persisted.Success ? persisted : FailClosed(persisted.Error, false);
            }

            MasterEnabled = true;
            return ApplySelectedSet();
        }

        public TweakActionResult Cycle(string id)
        {
            EnsureInitialized();
            TweakDescriptor descriptor;
            if (!_byId.TryGetValue(id, out descriptor)) return TweakActionResult.Fail("Unknown tweak: " + id);
            if (!descriptor.IsAvailable)
                return TweakActionResult.Fail(descriptor.Title + " is unavailable (" + descriptor.TrackingId + "): " + descriptor.UnavailableReason);
            if (!MasterEnabled) return TweakActionResult.Fail("Enable MASTER before changing gameplay tweaks.");

            string previous = Value(id);
            string next = descriptor.Next(previous);
            TweakActionResult result;
            try { result = _adapter.Apply(id, next); }
            catch (Exception e) { result = TweakActionResult.Fail(e.Message); }

            if (!result.Success) return FailClosed(result.Error);

            try
            {
                _store.Write(ValueKey(id), next);
                _store.Flush();
            }
            catch (Exception e)
            {
                _values[id] = previous;
                BestEffortWrite(ValueKey(id), previous);
                return FailClosed("Could not persist " + descriptor.Title + ": " + e.Message);
            }
            _values[id] = next;
            return TweakActionResult.Ok();
        }

        public TweakActionResult Reset()
        {
            EnsureInitialized();
            try { _adapter.RestoreBaseline(); }
            catch (Exception e) { return FailClosed("Could not restore the game baseline: " + e.Message); }

            var previousValues = new Dictionary<string, string>(_values, StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < Descriptors.Count; i++)
                {
                    TweakDescriptor descriptor = Descriptors[i];
                    _store.Write(ValueKey(descriptor.Id), descriptor.DefaultValue);
                }
                _store.Write(MasterKey, MasterEnabled ? "1" : "0");
                _store.Flush();
            }
            catch (Exception e)
            {
                foreach (KeyValuePair<string, string> pair in previousValues)
                    BestEffortWrite(ValueKey(pair.Key), pair.Value);
                return FailClosed("Could not persist reset settings: " + e.Message, false);
            }

            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
                _values[descriptor.Id] = descriptor.DefaultValue;
            }
            return TweakActionResult.Ok();
        }

        public void Tick()
        {
            if (!_initialized || !MasterEnabled) return;
            try { _adapter.Tick(); }
            catch (Exception e) { FailClosed("Tweak maintenance failed: " + e.Message); }
        }

        TweakActionResult ApplySelectedSet()
        {
            for (int i = 0; i < Descriptors.Count; i++)
            {
                TweakDescriptor descriptor = Descriptors[i];
                if (!descriptor.IsAvailable) continue;
                string value = Value(descriptor.Id);
                if (string.Equals(value, descriptor.DefaultValue, StringComparison.Ordinal)) continue;

                TweakActionResult result;
                try { result = _adapter.Apply(descriptor.Id, value); }
                catch (Exception e) { result = TweakActionResult.Fail(e.Message); }
                if (!result.Success) return FailClosed(result.Error);
            }

            MasterEnabled = true;
            TweakActionResult persisted = PersistMaster();
            return persisted.Success ? persisted : FailClosed(persisted.Error);
        }

        TweakActionResult FailClosed(string error, bool restoreBaseline = true)
        {
            if (restoreBaseline)
            {
                try { _adapter.RestoreBaseline(); }
                catch (Exception restoreError) { error += "; baseline restore failed: " + restoreError.Message; }
            }
            MasterEnabled = false;
            BestEffortWrite(MasterKey, "0");
            return TweakActionResult.Fail(error);
        }

        TweakActionResult PersistMaster()
        {
            try
            {
                _store.Write(MasterKey, MasterEnabled ? "1" : "0");
                _store.Flush();
                return TweakActionResult.Ok();
            }
            catch (Exception e)
            {
                return TweakActionResult.Fail("Could not persist the Mods master state: " + e.Message);
            }
        }

        string MasterKey => _prefix + "master";
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

        void EnsureInitialized()
        {
            if (!_initialized) throw new InvalidOperationException("Initialize the tweak controller first.");
        }
    }
}
