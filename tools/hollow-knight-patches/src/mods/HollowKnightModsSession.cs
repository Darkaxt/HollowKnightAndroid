using System;
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    /// <summary>
    /// Hollow Knight compatibility surface over the shared process-session owner.
    /// Readiness remains the exact typed Hollow Knight API signal.
    /// </summary>
    public sealed class HollowKnightModsSession : ITweakTeardownSession
    {
        readonly TweakSession _session;

        public HollowKnightModsSession(
            IHollowKnightTweakApi api,
            ITweakStore store,
            int visibleRows)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (visibleRows <= 0) throw new ArgumentOutOfRangeException(nameof(visibleRows));

            _session = new TweakSession(
                () => api.IsReady,
                () => new HollowKnightTweakAdapter(api),
                store,
                visibleRows);
        }

        public bool IsReady => _session.IsReady;
        public bool RestorationPending => _session.RestorationPending;
        public bool TeardownComplete => _session.TeardownComplete;
        public string LastError => _session.LastError;
        public TweakController Controller => _session.Controller;
        public TweakMenuModel Menu => _session.Menu;

        public void Tick()
        {
            _session.Tick();
        }

        public void SetPresenterAttached(bool attached)
        {
            _session.SetPresenterAttached(attached);
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}
