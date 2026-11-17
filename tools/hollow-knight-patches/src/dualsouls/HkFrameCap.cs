// HkFrameCap -- Hollow Knight's frame rate on Android, held off the 30 fps sentinel.
//
// Hollow Knight defaults to VSync on (GameSettings: LoadInt("VidVSync", ref vSync, 1)),
// and its VSync option sets Application.targetFrameRate = -1. On a desktop that
// means "follow the monitor"; on Android Unity ignores vSyncCount and reads a
// negative target as its mobile default of 30, so the game ran at 30 fps on a
// 120 Hz panel. Silksong has the same trap and the same answer (see
// ResolutionConfigurator / FrameCapHolder): the values are guarded every frame,
// because the game re-applies them from menu code, cinematics and quality changes.
// A positive target is the player's cap (or a cinematic's) and is left alone.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace HollowKnightPatches
{
    public sealed class HkFrameCap : MonoBehaviour
    {
        static HkFrameCap _instance;
        int _cap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (_instance != null) return;
            int cap = PanelRate();
            var go = new GameObject("__HkFrameCap__");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HkFrameCap>();
            _instance._cap = cap;
            Debug.Log("[HkFrameCap] holding the unset frame target at " + cap + " fps (was " +
                      Application.targetFrameRate + ", vSync " + QualitySettings.vSyncCount + ")");
        }

        // The highest refresh rate the panel offers; a 120 Hz panel may report a 60 Hz current mode.
        static int PanelRate()
        {
            int max = 0;
            foreach (var mode in Screen.resolutions)
            {
                int hz = (int)Mathf.Round((float)mode.refreshRateRatio.value);
                if (hz > max) max = hz;
            }
            if (max <= 0) max = (int)Mathf.Round((float)Screen.currentResolution.refreshRateRatio.value);
            return max > 0 ? max : 60;
        }

        void Update()
        {
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate <= 0) Application.targetFrameRate = _cap;
        }
    }
}
#endif
