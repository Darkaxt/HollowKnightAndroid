// Proof that our code runs inside the game.
//
// Everything the port adds -- the second screen, the overlay, resolution,
// skipping the intro -- depends on one thing: that an assembly of ours can be
// added to the player and have Unity call into it at startup. That has never
// been demonstrated on the on-device pipeline, and it is not something to
// assume while writing a feature on top of it, because the failure modes are
// silent. An assembly missing from ScriptingAssemblies.json is simply not
// there; an entry point missing from RuntimeInitializeOnLoads.json is never
// called; and a stale unity_app_guid runs new code against cached metadata
// from an older build, where the type indices no longer line up.
//
// So this logs, and does nothing else. It reports every initialization phase
// separately, which is worth more than a single line: it says not only that
// injection works but WHEN our code gets control, and the answer decides
// where the real patches can hook. SubsystemRegistration is before the engine
// has a scene; AfterSceneLoad is after the game's own objects exist.
//
// It is deliberately free of game references. This has to be able to fail for
// exactly one reason -- injection not working -- so it does not depend on
// anything that could fail for a different one.

using UnityEngine;

namespace SilksongPatches
{
    public static class InjectionProbe
    {
        const string Tag = "[SilksongPatches] probe: ";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void SubsystemRegistration()
        {
            Debug.Log(Tag + "SubsystemRegistration");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void AfterAssembliesLoaded()
        {
            Debug.Log(Tag + "AfterAssembliesLoaded");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void BeforeSplashScreen()
        {
            Debug.Log(Tag + "BeforeSplashScreen");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void BeforeSceneLoad()
        {
            Debug.Log(Tag + "BeforeSceneLoad");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AfterSceneLoad()
        {
            // The one line that says the whole chain worked, including the
            // parts that only exist once the game has built a scene.
            Debug.Log(Tag + "AfterSceneLoad -- injection works");
            // And what the launcher asked for. Reported from inside the game
            // rather than trusted from the launcher's side, because the whole
            // question is whether the settings crossed the process boundary.
            Debug.Log(Tag + "settings: " + Settings.Describe());
        }
    }
}
