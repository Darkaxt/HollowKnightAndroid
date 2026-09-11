using System;

namespace DualSouls.Skins.Runtime
{
    public interface ISkinTeardownSession
    {
        bool TeardownComplete { get; }
        string LastError { get; }
        void TickTeardown();
    }

    public sealed class PendingSkinTeardown
    {
        ISkinTeardownSession session;

        public ISkinTeardownSession Session
        {
            get { ReleaseCompleted(); return session; }
        }
        public bool BlocksReplacement
        {
            get { ReleaseCompleted(); return session != null; }
        }
        public string LastError { get; private set; } = "";

        public bool TryRetain(ISkinTeardownSession value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ReleaseCompleted();
            if (value.TeardownComplete) return false;
            if (session != null) return ReferenceEquals(session, value);
            session = value;
            LastError = value.LastError ?? "";
            return true;
        }

        public bool Tick()
        {
            ReleaseCompleted();
            var current = session;
            if (current == null) return true;
            try
            {
                current.TickTeardown();
                LastError = current.LastError ?? "";
            }
            catch (Exception error)
            {
                LastError = "Pending skin teardown tick failed: " + error.Message;
                return false;
            }
            ReleaseCompleted();
            return session == null;
        }

        void ReleaseCompleted()
        {
            if (session == null || !session.TeardownComplete) return;
            session = null;
            LastError = "";
        }
    }
}
