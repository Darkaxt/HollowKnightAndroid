using System;
using System.Collections.Generic;
using System.Reflection;
using DualSouls.Mods;
using DualSouls.Skins.Runtime;

// Distinct namespace: these model typed native/Unity dependencies, not published HK/SS fixtures.
// The production API, runtime, adapters, session, controller, and teardown pump execute unchanged.
namespace ModsRuntimeNativeModel;
public class MonoBehaviour
{
    public GameObject gameObject;
    public static void DontDestroyOnLoad(GameObject value) { }
    public static void Destroy(GameObject value) { value.Destroy(); }
}
public sealed class GameObject
{
    public static readonly List<object> Components = new();
    readonly List<object> components = new();
    public GameObject(string name) { }
    public T AddComponent<T>() where T : MonoBehaviour, new()
    {
        var item = new T { gameObject = this };
        components.Add(item); Components.Add(item);
        Invoke(item, "Awake");
        return item;
    }
    public static void Invoke(object item, string method) => item.GetType().GetMethod(method,
        BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(item, null);
    public void Destroy() { foreach (var item in components.ToArray()) { Invoke(item,"OnDestroy"); Components.Remove(item); } components.Clear(); }
}
public sealed class SilksongNativeModsMenu : MonoBehaviour
{
    public static void OpenBenchTeleportRoute(long token) { }
}
public sealed class SilksongSkinLibrary : IDisposable
{
    public SilksongSkinLibrary(SilksongSkinRuntime skins) { }
    public void AdmitSaveBoundary() { }
    public void Tick() { }
    public void Invalidate() { }
    public void Dispose() { }
}
public sealed class SilksongSkinRuntime : IDisposable, ISkinTeardownSession
{
    public static SilksongSkinRuntime Current => null;
    public bool TeardownComplete { get; private set; }
    public string LastError => "";
    public void Tick() { }
    public void TickTeardown() => Dispose();
    public void Dispose() => TeardownComplete = true;
}
public sealed class LineFileTweakStore : ITweakStore
{
    public static bool FailFlush;
    readonly Dictionary<string,string> values = new();
    public LineFileTweakStore(string path) { }
    public static string ProfilePath(string root, string profile) => profile;
    public string Read(string key) => values.TryGetValue(key, out var value) ? value : null;
    public void Write(string key, string value) => values[key] = value;
    public void Flush() { if (FailFlush) throw new InvalidOperationException("modeled persistence failure"); }
}
public static class Application { public static string persistentDataPath => "modeled-no-io"; }
public static class Debug { public static void LogError(object value) { } }
public static class Time { public static float unscaledTime; }
public sealed class AndroidJavaClass : IDisposable
{
    public AndroidJavaClass(string name) { }
    public T GetStatic<T>(string name) => default;
    public void Dispose() { }
}
public sealed class AndroidJavaObject : IDisposable
{
    public AndroidJavaObject(string name, params object[] args) { }
    public T Call<T>(string name, params object[] args) => default;
    public void Call(string name, params object[] args) { }
    public void Dispose() { }
}
public static class CheatManager
{
    public enum InvincibilityStates { Off, PreventDeath, FullInvincible }
    public enum NailDamageStates { Normal, InstaKill }
    public static InvincibilityStates Invincibility;
    public static NailDamageStates NailDamage;
    public static bool IsSilkDrainDisabled, CanChangeEquipsAnywhere, IsTextPrintSkipEnabled,
        IsWorldRumbleDisabled, IsFrostDisabled;
}
public class PlayerData
{
    public static PlayerData instance;
    public static bool HasInstance => instance != null;
    public int silk, silkParts, health = 5;
    public int CurrentSilkMax => 10;
    public bool hasMap, hasQuill, mapDirtmouth, mapCrossroads, mapGreenpath, mapFogCanyon,
        mapFungalWastes, mapRoyalGardens, mapCity, mapWaterways, mapMines, mapDeepnest,
        mapCliffs, mapOutskirts, mapRestingGrounds, mapAbyss;
    public List<string> scenesMapped = new(), scenesVisited = new();
    public void SetBool(string field, bool value)
    {
        HollowKnightGameplayFeatures.Authority("BeforeAuthoritativeMapBoolSet", this, field, value);
        typeof(PlayerData).GetField(field).SetValue(this,value);
    }
    // Native IL inspection: UpdateGameMap gates on hasQuill and per-room region ownership.
    // This bounded model covers Crossroads/Greenpath, not Unity rendering or save serialization.
    public bool UpdateGameMap(bool fail = false)
    {
        HollowKnightGameplayFeatures.Authority("BeginAuthoritativeMapUpdate", this);
        try {
            bool changed = false;
            if (hasQuill) foreach (string scene in scenesVisited)
                if (!scenesMapped.Contains(scene) && (scene.StartsWith("Crossroads") ? mapCrossroads : mapGreenpath))
                { scenesMapped.Add(scene); changed = true; }
            if (fail) throw new InvalidOperationException("modeled native map failure");
            return changed;
        }
        finally { HollowKnightGameplayFeatures.Authority("EndAuthoritativeMapUpdate", this); }
    }
}
public sealed class HeroController
{
    public static HeroController instance;
    public float RUN_SPEED = 8, WALK_SPEED = 4;
    public void RefillSilkToMaxSilent() { PlayerData.instance.silk = 10; }
}
public sealed class GameManager
{
    public static GameManager SilentInstance;
    public string sceneName = "Crossroads_mod";
    public string Zone = "CROSSROADS";
    public string GetCurrentMapZone() => Zone;
    public bool IsGameplayScene() => true;
}
public sealed class GameCameras
{
    public static GameCameras SilentInstance;
    public SilkSpool silkSpool;
}
public sealed class SilkSpool { public void RefreshSilk() { } }
public static class DsPortSceneryState { public static bool BlackBackground; }
public static class SilksongGameplayHooks
{
    public static bool InnateCompassEnabled, DamageCapEnabled, ToolCostsFreeEnabled,
        KeepRosariesEnabled, JournalOneKillEnabled;
    public static int NeedleMultiplier = 1, RosaryMultiplier = 1;
    public static void CompleteDeathHandling() { }
    public static void Reset() { NeedleMultiplier = RosaryMultiplier = 1; }
}
public static class SilksongGameplayFeatures
{
    public static bool FailRestore;
    public static Action StateTransferPreparation;
    public static void SetStateTransferPreparation(Action value) => StateTransferPreparation = value;
    public static void RestoreAll() { if (FailRestore) throw new InvalidOperationException("modeled native restore failure"); }
    public static void RestorePlayerOwnedForStateTransfer() { }
    public static void SaveState(int slot) { }
    public static void LoadState(int slot, Action<string> done) { }
    public static void DeleteState(int slot) { }
    public static bool StateExists(int slot) => false;
    public static void Tick(bool fast, bool map, bool compass, bool radar, bool bars,
        bool numbers, bool retry, bool slots, bool magnet, bool keep) { }
}
internal static partial class HollowKnightGameplayFeatures
{
    static int[] charmCostBaseline;
    static bool charmCostsApplied, notchCaptured, compassOwned, magnetOwned, benchOwned;
    static int compassCostBaseline, magnetCostBaseline;
    static string bossScene, inspectedBossScene;
    static bool bossDeathArmed;
    public static void MapTick(GameManager game, PlayerData player, bool enabled)
    {
        if (!ReferenceEquals(owner,player)) { RestorePlayerOwned(); owner = player; }
        MaintainAutoMap(game,player,enabled);
    }
    public static void Authority(string method, params object[] args) => typeof(HollowKnightGameplayFeatures)
        .GetMethod(method,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static)?.Invoke(null,args);
    static void RestoreFastTransitions() { }
    static void RemoveRadar() { }
    static void RemoveCombatOverlays() { }
    static void ReleaseVisualResources() { }
    static void MaintainInnateCharm(PlayerData p, int i, bool on, ref bool owned, ref int cost) { }
    static void MaintainCharmCosts(PlayerData p,bool enabled) { }
    static void MaintainNotches(PlayerData p,bool enabled) { }
    static void MaintainEquipAnywhere(PlayerData p,bool enabled) { }
}
