using System;
using System.Collections.Generic;

namespace DualSouls.Mods
{
    /// <summary>
    /// Process-owned lifecycle for one game's tweak controller. Presentation owners
    /// may attach and detach without changing the controller, adapter, or store.
    /// Failed initialization and restoration retain the old adapter until its
    /// captured baseline has been restored.
    /// </summary>
    public sealed class TweakSession : ITweakTeardownSession
    {
        public const int DefaultInitializationRetryReadyTicks = 60;

        readonly Func<bool> _isGameReady;
        readonly Func<ITweakAdapter> _createAdapter;
        readonly ITweakStore _store;
        readonly int _visibleRows;
        readonly int _retryReadyTicks;
        Pipeline _pipeline;
        int _retryReadyTicksRemaining;
        bool _retryPending;
        bool _restorationPending;
        bool _presenterAttached;

        public TweakSession(
            Func<bool> isGameReady,
            Func<ITweakAdapter> createAdapter,
            ITweakStore store,
            int visibleRows,
            int initializationRetryReadyTicks = DefaultInitializationRetryReadyTicks)
        {
            _isGameReady = isGameReady ?? throw new ArgumentNullException(nameof(isGameReady));
            _createAdapter = createAdapter ?? throw new ArgumentNullException(nameof(createAdapter));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (visibleRows <= 0) throw new ArgumentOutOfRangeException(nameof(visibleRows));
            if (initializationRetryReadyTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(initializationRetryReadyTicks));

            _visibleRows = visibleRows;
            _retryReadyTicks = initializationRetryReadyTicks;
        }

        public bool IsReady { get; private set; }
        public bool RestorationPending => _restorationPending;
        public bool TeardownRequested { get; private set; }
        public bool TeardownComplete { get; private set; }
        public string LastError { get; private set; } = "";
        public TweakController Controller => EnsurePipeline().Controller;
        public TweakMenuModel Menu => EnsurePipeline().Menu;

        public void Tick()
        {
            if (TeardownComplete) return;
            if (TeardownRequested)
            {
                RetryRestoration("session restore failed: ");
                return;
            }
            if (_restorationPending)
            {
                if (RetryRestoration("failed pipeline restore failed: "))
                    ScheduleInitializationRetry();
                return;
            }
            if (IsReady)
            {
                Controller.Tick();
                return;
            }
            if (!SafeIsGameReady()) return;

            if (_retryPending)
            {
                _retryReadyTicksRemaining--;
                if (_retryReadyTicksRemaining > 0) return;
                _pipeline = null;
                _retryPending = false;
            }

            Pipeline pipeline = EnsurePipeline();
            TweakActionResult result = pipeline.Initialize();
            if (!result.Success)
            {
                LastError = result.Error;
                IsReady = false;
                pipeline.BlockMutations();
                if (TryRestore(pipeline, out string restoreError))
                    ScheduleInitializationRetry();
                else
                {
                    _restorationPending = true;
                    AppendError("failed pipeline restore failed: " + restoreError);
                }
                return;
            }

            IsReady = true;
            LastError = "";
            Controller.Tick();
        }

        public void SetPresenterAttached(bool attached)
        {
            if (TeardownRequested || TeardownComplete || _presenterAttached == attached) return;
            _presenterAttached = attached;
        }

        public void Dispose()
        {
            if (TeardownComplete || TeardownRequested) return;
            TeardownRequested = true;
            IsReady = false;
            _retryPending = false;
            _presenterAttached = false;

            if (_pipeline == null)
            {
                TeardownComplete = true;
                return;
            }

            _pipeline.BlockMutations();
            if (TryRestore(_pipeline, out string restoreError))
                TeardownComplete = true;
            else
            {
                _restorationPending = true;
                AppendError("session restore failed: " + restoreError);
            }
        }

        Pipeline EnsurePipeline()
        {
            if (_pipeline == null)
            {
                ITweakAdapter adapter = _createAdapter();
                if (adapter == null)
                    throw new InvalidOperationException("The tweak adapter factory returned null.");
                _pipeline = new Pipeline(adapter, _store, _visibleRows);
            }
            return _pipeline;
        }

        bool SafeIsGameReady()
        {
            try { return _isGameReady(); }
            catch (Exception error)
            {
                LastError = "Could not determine game readiness: " + error.Message;
                return false;
            }
        }

        void ScheduleInitializationRetry()
        {
            _restorationPending = false;
            _retryPending = true;
            _retryReadyTicksRemaining = _retryReadyTicks;
        }

        bool RetryRestoration(string errorPrefix)
        {
            if (_pipeline == null)
            {
                _restorationPending = false;
                if (TeardownRequested) TeardownComplete = true;
                return true;
            }
            if (!TryRestore(_pipeline, out string restoreError))
            {
                _restorationPending = true;
                AppendError(errorPrefix + restoreError);
                return false;
            }

            _restorationPending = false;
            if (TeardownRequested) TeardownComplete = true;
            return true;
        }

        static bool TryRestore(Pipeline pipeline, out string error)
        {
            try
            {
                pipeline.RestoreAndDisable();
                error = "";
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        void AppendError(string error)
        {
            if (string.IsNullOrEmpty(error)) return;
            if (!string.IsNullOrEmpty(LastError)) LastError += "; ";
            LastError += error;
        }

        sealed class Pipeline
        {
            readonly SessionTweakAdapter _adapter;
            readonly SessionTweakStore _store;
            bool _disabled;

            internal Pipeline(ITweakAdapter adapter, ITweakStore store, int visibleRows)
            {
                _adapter = new SessionTweakAdapter(adapter);
                _store = new SessionTweakStore(store);
                Controller = new TweakController(_adapter, _store);
                Menu = new TweakMenuModel(Controller, visibleRows);
            }

            internal TweakController Controller { get; }
            internal TweakMenuModel Menu { get; }

            internal TweakActionResult Initialize()
            {
                TweakActionResult result = Controller.Initialize();
                if (!result.Success) return result;

                try
                {
                    _store.Commit();
                    return result;
                }
                catch (Exception error)
                {
                    return TweakActionResult.Fail(
                        "Could not commit initialized Mods settings: " + error.Message);
                }
            }

            internal void BlockMutations()
            {
                _adapter.BlockMutations();
                _store.Disable();
            }

            internal void RestoreAndDisable()
            {
                if (_disabled) return;
                BlockMutations();
                if (_adapter.BaselineCaptured) _adapter.RestoreForSession();
                _adapter.DisableRestoration();
                _disabled = true;
            }
        }

        sealed class SessionTweakAdapter : ITweakAdapter
        {
            readonly ITweakAdapter _inner;
            bool _mutationsAllowed = true;
            bool _restorationAllowed = true;

            internal SessionTweakAdapter(ITweakAdapter inner)
            {
                _inner = inner;
            }

            public string GameId => _inner.GameId;
            public IReadOnlyList<TweakDescriptor> Descriptors => _inner.Descriptors;
            internal bool BaselineCaptured { get; private set; }

            public void CaptureBaseline()
            {
                if (!_mutationsAllowed) return;
                _inner.CaptureBaseline();
                BaselineCaptured = true;
            }

            public TweakActionResult Apply(string id, string value)
            {
                return _mutationsAllowed
                    ? _inner.Apply(id, value)
                    : TweakActionResult.Fail("The Mods session is inactive.");
            }

            public void RestoreBaseline()
            {
                if (_mutationsAllowed && _restorationAllowed) _inner.RestoreBaseline();
            }

            public void Tick()
            {
                if (_mutationsAllowed) _inner.Tick();
            }

            internal void RestoreForSession()
            {
                if (_restorationAllowed) _inner.RestoreBaseline();
            }

            internal void BlockMutations()
            {
                _mutationsAllowed = false;
            }

            internal void DisableRestoration()
            {
                _restorationAllowed = false;
            }
        }

        sealed class SessionTweakStore : ITweakStore
        {
            readonly ITweakStore _inner;
            readonly Dictionary<string, string> _pending =
                new Dictionary<string, string>(StringComparer.Ordinal);
            bool _active = true;
            bool _committed;
            bool _flushPending;

            internal SessionTweakStore(ITweakStore inner)
            {
                _inner = inner;
            }

            public string Read(string key)
            {
                if (!_active) return null;
                string value;
                return !_committed && _pending.TryGetValue(key, out value)
                    ? value
                    : _inner.Read(key);
            }

            public void Write(string key, string value)
            {
                if (!_active) return;
                if (_committed) _inner.Write(key, value);
                else _pending[key] = value;
            }

            public void Flush()
            {
                if (!_active) return;
                if (_committed) _inner.Flush();
                else _flushPending = true;
            }

            internal void Commit()
            {
                if (!_active) throw new InvalidOperationException("The Mods pipeline is inactive.");
                if (_committed) return;

                foreach (KeyValuePair<string, string> pair in _pending)
                    _inner.Write(pair.Key, pair.Value);
                if (_flushPending) _inner.Flush();

                _pending.Clear();
                _flushPending = false;
                _committed = true;
            }

            internal void Disable()
            {
                _pending.Clear();
                _flushPending = false;
                _active = false;
            }
        }
    }
}
