using System.Collections.Generic;
using UnityEngine;

// Explicit boundary for plan stages H3 (Mods/tweaks) and H4 (skins).
// It is deliberately inert in H2 and is replaced, not expanded into a second
// UI implementation, when those source modules are ported.
static class HkStageHooks
{
    static KeyCode joyBase = KeyCode.Joystick1Button0;
    static int joySlot = 1;
    static float nextJoyPoll;

    internal const bool TweaksAvailable = false;
    internal static bool TweaksMenuVisible => false;
    internal static bool BlackBackground => false;
    internal static int SkinStamp => 0;

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

public partial class HKDualScreen
{
    bool tweaksOpen;
    GameObject tweaksRoot;
    readonly List<GameObject> tweakRows = new List<GameObject>();
    Transform gearT;
    SpriteRenderer gearSR;
    Texture2D gearTex;
    Vector3 hudGearAnchor;
    float hudGearH;
    bool hudGearOk;
    Bounds hudFpsB;

    void TweaksPaneTick(Camera source) { }
    void PositionGear(float scale, float aspect, float tabY) { }
    bool GearTapN(float x, float y) => false;
    void ToggleTweaksPane() { }
    void CloseTweaksPane() { tweaksOpen = false; }
}
