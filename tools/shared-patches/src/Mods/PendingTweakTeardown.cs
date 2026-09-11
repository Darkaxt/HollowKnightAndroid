using System;

namespace DualSouls.Mods
{
    /// <summary>
    /// Minimal contract for a process session whose failed teardown must keep its
    /// exact restoration owner alive until a later tick completes restoration.
    /// </summary>
    public interface ITweakTeardownSession : IDisposable
    {
        bool TeardownComplete { get; }
        string LastError { get; }
        void Tick();
    }

    /// <summary>
    /// Unity-independent ownership state used by restoration-only process pumps.
    /// It cannot create a replacement session or perform normal Mods actions.
    /// </summary>
    public sealed class PendingTweakTeardown
    {
        ITweakTeardownSession _session;

        public ITweakTeardownSession Session
        {
            get
            {
                ReleaseCompletedSession();
                return _session;
            }
        }

        public bool BlocksReplacement
        {
            get
            {
                ReleaseCompletedSession();
                return _session != null;
            }
        }

        public string LastError { get; private set; } = "";

        public bool TryRetain(ITweakTeardownSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            ReleaseCompletedSession();
            if (session.TeardownComplete) return false;
            if (_session != null) return ReferenceEquals(_session, session);

            _session = session;
            LastError = session.LastError ?? "";
            return true;
        }

        public bool Tick()
        {
            ReleaseCompletedSession();
            ITweakTeardownSession session = _session;
            if (session == null) return true;

            try
            {
                session.Tick();
                LastError = session.LastError ?? "";
            }
            catch (Exception error)
            {
                LastError = "Pending Mods teardown tick failed: " + error.Message;
                return false;
            }

            ReleaseCompletedSession();
            return _session == null;
        }

        void ReleaseCompletedSession()
        {
            if (_session == null || !_session.TeardownComplete) return;
            _session = null;
            LastError = "";
        }
    }
}
