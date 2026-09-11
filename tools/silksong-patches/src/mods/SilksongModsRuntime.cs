#if UNITY_ANDROID && !UNITY_EDITOR
using DualSouls.Mods;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.Silksong.Runtime;
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
        public SilksongSkinRuntime Skins { get; private set; }
        SilksongSkinLibrary skinLibrary;

        public static void EnsureStarted()
        {
            if (Current != null || _creating || SilksongModsRestorePump.BlocksReplacement ||
                SilksongSkinRestorePump.BlocksReplacement) return;

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
            Skins = SilksongSkinRuntime.Current ?? new SilksongSkinRuntime();
            skinLibrary = new SilksongSkinLibrary(Skins);
            Current = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (Skins != null) Skins.Tick();
            if (skinLibrary != null) skinLibrary.Tick();
            TweakSession session = Session;
            if (session != null) session.Tick();
        }

        void OnDestroy()
        {
            if (!ReferenceEquals(Current, this)) return;
            if (skinLibrary != null) { skinLibrary.Dispose(); skinLibrary = null; }

            TweakSession session = Session;
            Session = null;
            Current = null;
            var skins = Skins;
            Skins = null;
            try
            {
                if (session != null)
                {
                    session.Dispose();
                    if (!session.TeardownComplete)
                    {
                        Debug.LogError("[Silksong Mods] restoration pending; retained for retry: " + session.LastError);
                        SilksongModsRestorePump.Create(session);
                    }
                }
            }
            finally
            {
                if (skins != null)
                {
                    try { skins.Dispose(); }
                    catch (System.Exception error)
                    {
                        Debug.LogError("[Silksong skins] teardown blocked; exact owner retained for restore retry: " + error);
                        SilksongSkinRestorePump.Create(skins);
                    }
                }
            }
        }
    }

    public sealed class SilksongSkinRestorePump : MonoBehaviour
    {
        static SilksongSkinRestorePump _owner;
        readonly PendingSkinTeardown _pending = new PendingSkinTeardown();

        public static bool BlocksReplacement => _owner != null && _owner._pending.BlocksReplacement;

        public static void Create(SilksongSkinRuntime session)
        {
            if (session == null || session.TeardownComplete) return;
            if (_owner != null)
            {
                _owner._pending.TryRetain(session);
                return;
            }
            var restoreObject = new GameObject("__SilksongSkinRestoreOwner__");
            DontDestroyOnLoad(restoreObject);
            var pump = restoreObject.AddComponent<SilksongSkinRestorePump>();
            if (!pump._pending.TryRetain(session))
            {
                Destroy(restoreObject);
                return;
            }
            _owner = pump;
        }

        void Update()
        {
            if (!_pending.Tick()) return;
            if (ReferenceEquals(_owner, this)) _owner = null;
            Destroy(gameObject);
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
        readonly PendingTweakTeardown _pending = new PendingTweakTeardown();

        public static bool BlocksReplacement =>
            _owner != null && _owner._pending.BlocksReplacement;

        public static void Create(TweakSession session)
        {
            if (session == null || session.TeardownComplete) return;
            if (_owner != null)
            {
                _owner._pending.TryRetain(session);
                return;
            }

            var restoreObject = new GameObject("__SilksongModsRestoreOwner__");
            DontDestroyOnLoad(restoreObject);
            var pump = restoreObject.AddComponent<SilksongModsRestorePump>();
            if (!pump._pending.TryRetain(session))
            {
                Destroy(restoreObject);
                return;
            }
            _owner = pump;
        }

        void Update()
        {
            if (!_pending.Tick()) return;
            if (ReferenceEquals(_owner, this)) _owner = null;
            Destroy(gameObject);
        }
    }
}
#endif
