using System;
using System.Collections.Generic;
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    public sealed class HollowKnightModsSession : IDisposable
    {
        const int InitializationRetryReadyTicks = 60;

        readonly IHollowKnightTweakApi _api;
        readonly ITweakStore _store;
        readonly int _visibleRows;
        Pipeline _pipeline;
        int _retryReadyTicksRemaining;
        bool _retryPending;
        bool _presenterAttached;
        bool _disposed;

        public HollowKnightModsSession(
            IHollowKnightTweakApi api,
            ITweakStore store,
            int visibleRows)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (visibleRows <= 0) throw new ArgumentOutOfRangeException(nameof(visibleRows));

            _api = api;
            _store = store;
            _visibleRows = visibleRows;
            _pipeline = CreatePipeline();
        }

        public bool IsReady { get; private set; }
        public string LastError { get; private set; } = "";
        public TweakController Controller => _pipeline.Controller;
        public TweakMenuModel Menu => _pipeline.Menu;

        public void Tick()
        {
            if (_disposed) return;
            if (IsReady)
            {
                Controller.Tick();
                return;
            }
            if (!_api.IsReady) return;

            if (_retryPending)
            {
                _retryReadyTicksRemaining--;
                if (_retryReadyTicksRemaining > 0) return;

                _pipeline = CreatePipeline();
                _retryPending = false;
            }

            TweakActionResult result = _pipeline.Initialize();
            if (!result.Success)
            {
                LastError = result.Error;
                string restoreError = _pipeline.RestoreAndDisable();
                if (!string.IsNullOrEmpty(restoreError))
                    LastError += "; failed pipeline restore failed: " + restoreError;
                _retryPending = true;
                _retryReadyTicksRemaining = InitializationRetryReadyTicks;
                return;
            }

            IsReady = true;
            LastError = "";
            Controller.Tick();
        }

        public void SetPresenterAttached(bool attached)
        {
            if (_disposed || _presenterAttached == attached) return;
            _presenterAttached = attached;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _retryPending = false;
            _presenterAttached = false;

            string restoreError = _pipeline.RestoreAndDisable();
            if (!string.IsNullOrEmpty(restoreError))
            {
                if (!string.IsNullOrEmpty(LastError)) LastError += "; ";
                LastError += "session restore failed: " + restoreError;
            }
        }

        Pipeline CreatePipeline()
        {
            return new Pipeline(_api, _store, _visibleRows);
        }

        sealed class Pipeline
        {
            readonly SessionTweakApi _api;
            readonly SessionTweakStore _store;
            readonly HollowKnightTweakAdapter _adapter;
            bool _baselineMayHaveBeenCaptured;
            bool _disabled;

            internal Pipeline(
                IHollowKnightTweakApi api,
                ITweakStore store,
                int visibleRows)
            {
                _api = new SessionTweakApi(api);
                _store = new SessionTweakStore(store);
                _adapter = new HollowKnightTweakAdapter(_api);
                Controller = new TweakController(_adapter, _store);
                Menu = new TweakMenuModel(Controller, visibleRows);
            }

            internal TweakController Controller { get; }
            internal TweakMenuModel Menu { get; }

            internal TweakActionResult Initialize()
            {
                _baselineMayHaveBeenCaptured = true;
                TweakActionResult result = Controller.Initialize();
                if (!result.Success) return result;

                try
                {
                    _store.Commit();
                    return result;
                }
                catch (Exception e)
                {
                    return TweakActionResult.Fail(
                        "Could not commit initialized Mods settings: " + e.Message);
                }
            }

            internal string RestoreAndDisable()
            {
                if (_disabled) return "";

                string error = "";
                try
                {
                    if (_baselineMayHaveBeenCaptured)
                        _adapter.RestoreBaseline();
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                finally
                {
                    _api.Disable();
                    _store.Disable();
                    _disabled = true;
                }
                return error;
            }
        }

        sealed class SessionTweakApi : IHollowKnightTweakApi
        {
            readonly IHollowKnightTweakApi _inner;
            bool _active = true;

            internal SessionTweakApi(IHollowKnightTweakApi inner)
            {
                _inner = inner;
            }

            public bool IsReady => _active && _inner.IsReady;

            public void CaptureBaseline()
            {
                if (_active) _inner.CaptureBaseline();
            }

            public void RestoreBaseline()
            {
                if (_active) _inner.RestoreBaseline();
            }

            public void SetCompanionBackdropBlack(bool black)
            {
                if (_active) _inner.SetCompanionBackdropBlack(black);
            }

            public void SetLifebloodFlash(HollowKnightFlashMode mode)
            {
                if (_active) _inner.SetLifebloodFlash(mode);
            }

            internal void Disable()
            {
                _active = false;
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
