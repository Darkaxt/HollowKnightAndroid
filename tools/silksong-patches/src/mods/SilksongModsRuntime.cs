#if UNITY_ANDROID && !UNITY_EDITOR
using DualSouls.Mods;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    /// <summary>
    /// Process-owned Silksong Mods authority. Its lifetime is independent of the
    /// direct-display transport and any lower-screen presentation generation.
    /// </summary>
    public sealed class SilksongModsRuntime : MonoBehaviour
    {
        const string RuntimeObjectName = "__SilksongModsRuntime__";
        const int VisibleRows = 5;

        static bool _creating;

        public static SilksongModsRuntime Current { get; private set; }
        public TweakSession Session { get; private set; }

        public static void EnsureStarted()
        {
            if (Current != null || _creating || SilksongModsRestorePump.BlocksReplacement) return;

            _creating = true;
            GameObject runtimeObject = null;
            try
            {
                runtimeObject = new GameObject(RuntimeObjectName);
                DontDestroyOnLoad(runtimeObject);
                runtimeObject.AddComponent<SilksongModsRuntime>();
            }
            catch
            {
                if (runtimeObject != null) Destroy(runtimeObject);
                throw;
            }
            finally
            {
                _creating = false;
            }
        }

        void Awake()
        {
            if (Current != null && !ReferenceEquals(Current, this))
            {
                Destroy(gameObject);
                return;
            }
            if (ReferenceEquals(Current, this)) return;

            var api = new SilksongGameTweakApi();
            Session = new TweakSession(
                () => api.IsReady,
                () => new SilksongTweakAdapter(api),
                new PlayerPrefsTweakStore(),
                VisibleRows);
            Current = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            TweakSession session = Session;
            if (session != null) session.Tick();
        }

        void OnDestroy()
        {
            if (!ReferenceEquals(Current, this)) return;

            TweakSession session = Session;
            Session = null;
            Current = null;
            if (session == null) return;

            session.Dispose();
            if (!session.TeardownComplete)
            {
                Debug.LogError("[Silksong Mods] restoration pending; retained for retry: " + session.LastError);
                SilksongModsRestorePump.Create(session);
            }
        }
    }

    /// <summary>
    /// Restoration-only owner used when Unity tears down the runtime before a
    /// typed baseline restoration succeeds. It cannot create controllers, UI, or
    /// apply tweaks, and blocks runtime replacement until restoration completes.
    /// </summary>
    public sealed class SilksongModsRestorePump : MonoBehaviour
    {
        static SilksongModsRestorePump _owner;
        TweakSession _session;

        public static bool BlocksReplacement =>
            _owner != null && _owner._session != null && !_owner._session.TeardownComplete;

        public static void Create(TweakSession session)
        {
            if (session == null || session.TeardownComplete) return;
            if (BlocksReplacement) return;

            var restoreObject = new GameObject("__SilksongModsRestoreOwner__");
            DontDestroyOnLoad(restoreObject);
            var pump = restoreObject.AddComponent<SilksongModsRestorePump>();
            pump._session = session;
            _owner = pump;
        }

        void Update()
        {
            TweakSession session = _session;
            if (session == null) return;
            session.Tick();
            if (!session.TeardownComplete) return;

            _session = null;
            if (ReferenceEquals(_owner, this)) _owner = null;
            Destroy(gameObject);
        }
    }
}
#endif
