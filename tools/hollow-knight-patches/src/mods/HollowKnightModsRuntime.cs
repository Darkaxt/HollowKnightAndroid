#if UNITY_ANDROID && !UNITY_EDITOR
using DualSouls.Mods;
using UnityEngine;

namespace DualSouls.Mods.HollowKnight
{
    public sealed class HollowKnightModsRuntime : MonoBehaviour
    {
        const string RuntimeObjectName = "__HollowKnightModsRuntime__";
        const int VisibleRows = 5;

        static bool _creating;

        public static HollowKnightModsRuntime Current { get; private set; }
        public HollowKnightModsSession Session { get; private set; }

        public static void EnsureStarted()
        {
            if (Current != null || _creating) return;

            _creating = true;
            GameObject runtimeObject = null;
            try
            {
                runtimeObject = new GameObject(RuntimeObjectName);
                DontDestroyOnLoad(runtimeObject);
                runtimeObject.AddComponent<HollowKnightModsRuntime>();
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

            Session = new HollowKnightModsSession(
                new HollowKnightGameTweakApi(),
                new PlayerPrefsTweakStore(),
                VisibleRows);
            Current = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (Session != null) Session.Tick();
        }

        void OnDestroy()
        {
            if (!ReferenceEquals(Current, this)) return;

            HollowKnightModsSession session = Session;
            Session = null;
            Current = null;
            if (session != null) session.Dispose();
        }
    }
}
#endif
