using System;
using DualSouls.Mods.HollowKnight;
using UnityEngine;

// Explicit boundary for staged runtime delegation and joystick ownership.
static class HkStageHooks
{
    static KeyCode joyBase = KeyCode.Joystick1Button0;
    static int joySlot = 1;
    static float nextJoyPoll;
    static bool? _backdropBlackOverride;
    static HollowKnightFlashMode? _flashOverride;
    static HollowKnightFlashMode? _legacyFlashMode;
    static float? _legacyFlashAlpha;

    internal static bool TweaksAvailable =>
        HollowKnightModsRuntime.Current != null &&
        HollowKnightModsRuntime.Current.Session.IsReady;
    internal static bool TweaksMenuVisible =>
        TweaksAvailable && HollowKnightModsRuntime.Current.Session.Menu.IsOpen;
    internal static bool BlackBackground => _backdropBlackOverride == true;
    internal static HollowKnightFlashMode? FlashOverride => _flashOverride;
    internal static HollowKnightFlashMode? LegacyFlashMode => _legacyFlashMode;
    internal static float? LegacyFlashAlpha => _legacyFlashAlpha;
    internal static int SkinStamp => 0;

    internal static void SetBackdropOverride(bool black)
    {
        _backdropBlackOverride = black;
    }

    internal static void SetFlashOverride(HollowKnightFlashMode mode)
    {
        switch (mode)
        {
            case HollowKnightFlashMode.Soft:
            case HollowKnightFlashMode.Vanilla:
            case HollowKnightFlashMode.Off:
                _flashOverride = mode;
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mode), mode, "Unsupported Hollow Knight flash mode.");
        }
    }

    internal static void SetLegacyFlashMode(
        HollowKnightFlashMode mode,
        float softAlpha)
    {
        if (mode != HollowKnightFlashMode.Soft &&
            mode != HollowKnightFlashMode.Vanilla)
            throw new ArgumentOutOfRangeException(
                nameof(mode), mode, "Unsupported legacy Hollow Knight flash mode.");

        _legacyFlashMode = mode;
        _legacyFlashAlpha = softAlpha;
    }

    internal static void ClearLegacyFlashMode()
    {
        _legacyFlashMode = null;
        _legacyFlashAlpha = null;
    }

    internal static void ClearPresentationOverrides()
    {
        _backdropBlackOverride = null;
        _flashOverride = null;
    }

    internal static void Tick(HKLayout layout, bool debug)
    {
        if (Time.unscaledTime < nextJoyPoll) return;
        nextJoyPoll = Time.unscaledTime + 2f;
        try
        {
            string[] names = Input.GetJoystickNames();
            int slot = 1;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i])) continue;
                slot = i + 1;
                break;
            }
            if (slot == joySlot) return;
            joySlot = slot;
            joyBase = (KeyCode)((int)KeyCode.Joystick1Button0 +
                                (slot - 1) * 20);
            if (debug)
                Debug.Log("HKDS active pad -> joystick slot " + slot);
        }
        catch { }
    }
    internal static void PushInputSettings(HKLayout layout) { }
    internal static bool IsBenchRecorded(string scene) => false;
    internal static void BenchWarp(string scene) { }

    internal static KeyCode JoyBtn(int index)
    {
        return (KeyCode)((int)joyBase + index);
    }
}
