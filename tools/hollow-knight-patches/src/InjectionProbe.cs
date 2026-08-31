using UnityEngine;

namespace HollowKnightPatches
{
    public static class InjectionProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Start()
        {
            Debug.Log("[DualSouls][HK] injection probe loaded");
        }
    }
}
