// Silksong compatibility surface for shared display-attributed touch routing.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using DualSouls.DualScreen;
using UnityEngine;

public static class DsTouch
{
    public const int SECOND_DISPLAY = 1;

    public static bool Enabled
    {
        get
        {
            Configure();
            return DirectDisplayTouch.Enabled;
        }
        set
        {
            Configure();
            DirectDisplayTouch.Enabled = value;
        }
    }

    public static bool IsSecondScreen(Touch touch)
    {
        Configure();
        return DirectDisplayTouch.IsTargetDisplay(touch);
    }

    public static void CollectSecondScreen(List<Touch> into)
    {
        Configure();
        DirectDisplayTouch.CollectTargetDisplay(into);
    }

    public static void InstallFence(GameObject host)
    {
        Configure();
        DirectDisplayTouch.InstallFence(host);
    }

    public static void RemoveFence()
    {
        Configure();
        DirectDisplayTouch.RemoveFence();
    }

    static void Configure()
    {
        DirectDisplayTouch.ConfigureTargetDisplay(SECOND_DISPLAY);
    }
}
#endif
