using System;
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    public sealed class HollowKnightModsSession : IDisposable
    {
        readonly SessionTweakApi _api;
        readonly SessionTweakStore _store;
        readonly HollowKnightTweakAdapter _adapter;
        bool _initializationAttempted;
        bool _baselineMayHaveBeenCaptured;
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

            _api = new SessionTweakApi(api);
            _store = new SessionTweakStore(store);
            _adapter = new HollowKnightTweakAdapter(_api);
            Controller = new TweakController(_adapter, _store);
            Menu = new TweakMenuModel(Controller, visibleRows);
        }

        public bool IsReady { get; private set; }
        public string LastError { get; private set; } = "";
        public TweakController Controller { get; }
        public TweakMenuModel Menu { get; }

        public void Tick()
        {
            if (_disposed) return;

            if (!IsReady)
            {
                if (_initializationAttempted || !_api.IsReady) return;

                _initializationAttempted = true;
                _baselineMayHaveBeenCaptured = true;
                TweakActionResult result = Controller.Initialize();
                if (!result.Success)
                {
                    LastError = result.Error;
                    return;
                }

                IsReady = true;
                LastError = "";
            }

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
            _presenterAttached = false;

            try
            {
                if (_baselineMayHaveBeenCaptured)
                    _adapter.RestoreBaseline();
            }
            finally
            {
                _api.Disable();
                _store.Disable();
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
            bool _active = true;

            internal SessionTweakStore(ITweakStore inner)
            {
                _inner = inner;
            }

            public string Read(string key)
            {
                return _active ? _inner.Read(key) : null;
            }

            public void Write(string key, string value)
            {
                if (_active) _inner.Write(key, value);
            }

            public void Flush()
            {
                if (_active) _inner.Flush();
            }

            internal void Disable()
            {
                _active = false;
            }
        }
    }
}
