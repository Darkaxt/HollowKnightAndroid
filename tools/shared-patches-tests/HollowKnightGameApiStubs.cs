using System;
using System.Collections.Generic;
using DualSouls.Mods.HollowKnight;

internal static class HkStageHooks
{
    internal static void ClearPresentationOverrides() { }
    internal static void SetBackdropOverride(bool black) { }
    internal static void SetFlashOverride(HollowKnightFlashMode mode) { }
}

public sealed class PlayerData
{
    public static PlayerData instance;

    public bool isInvincible;
    public int health;
    public int nailDamage;
    public int nailSmithUpgrades;
    public int MPCharge;
}

public sealed class HeroController : UnityEngine.Object
{
    public static HeroController instance;
    public static HeroController UnsafeInstance => instance;

    public float RUN_SPEED;
    public float WALK_SPEED;
    public int HealthAdded { get; private set; }
    public int SoulAdded { get; private set; }

    public void AddHealth(int amount)
    {
        HealthAdded += amount;
        if (PlayerData.instance != null) PlayerData.instance.health += amount;
    }

    public void AddMPCharge(int amount)
    {
        SoulAdded += amount;
        if (PlayerData.instance != null)
            PlayerData.instance.MPCharge = Math.Min(99, PlayerData.instance.MPCharge + amount);
    }
}

public sealed class GameManager : UnityEngine.Object
{
    public static GameManager UnsafeInstance { get; set; }

    public PlayerData playerData;
    public HeroController hero_ctrl;
}

public static class CheatManager
{
    public static bool IsInstaKillEnabled { get; set; }
}

public static class PlayMakerFSM
{
    public static List<string> Broadcasts { get; } = new List<string>();

    public static void BroadcastEvent(string eventName)
    {
        Broadcasts.Add(eventName);
    }
}

namespace UnityEngine
{
    public class Object
    {
    }
}
