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
        HollowKnightLifebloodFlashPolicy _lifebloodFlashPolicy;

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
            _lifebloodFlashPolicy = new HollowKnightLifebloodFlashPolicy();
            Current = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            HollowKnightModsSession session = Session;
            if (session != null) session.Tick();

            HollowKnightLifebloodFlashPolicy policy = _lifebloodFlashPolicy;
            if (policy == null) return;

            bool sessionReady = session != null && session.IsReady;
            bool masterEnabled = sessionReady && session.Controller.MasterEnabled;
            string controllerValue = masterEnabled
                ? session.Controller.Value("lifeblood_flash")
                : null;
            HollowKnightFlashMode? desiredMode = HollowKnightFlashModeResolver.Resolve(
                sessionReady,
                masterEnabled,
                controllerValue,
                global::HkStageHooks.LegacyFlashMode);
            float softAlpha = global::HkStageHooks.LegacyFlashAlpha ??
                              HollowKnightLifebloodFlashPolicy.DefaultSoftAlpha;
            policy.Tick(desiredMode, softAlpha);
        }

        void OnDestroy()
        {
            if (!ReferenceEquals(Current, this)) return;

            HollowKnightModsSession session = Session;
            HollowKnightLifebloodFlashPolicy policy = _lifebloodFlashPolicy;
            Session = null;
            _lifebloodFlashPolicy = null;
            Current = null;
            try
            {
                if (session != null) session.Dispose();
            }
            finally
            {
                if (policy != null) policy.Dispose();
            }
        }
    }
}
#endif
