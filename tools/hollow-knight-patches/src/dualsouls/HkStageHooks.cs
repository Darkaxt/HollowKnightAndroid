using System;
using System.Collections.Generic;
using System.IO;
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
    static bool benchLoaded;
    static bool lastAtBench;
    static readonly Dictionary<string, BenchRecord> benches =
        new Dictionary<string, BenchRecord>(StringComparer.Ordinal);

    [Serializable]
    sealed class BenchRecord
    {
        public string scene;
        public string marker;
        public int type;
        public bool facingRight;
    }

    [Serializable]
    sealed class BenchEnvelope
    {
        public List<BenchRecord> records = new List<BenchRecord>();
    }

    static string BenchPath => Path.Combine(
        Application.persistentDataPath, "dualsouls-recorded-benches.json");

    internal static bool TweaksAvailable =>
        HollowKnightModsRuntime.Current != null &&
        HollowKnightModsRuntime.Current.Session.IsReady;
    internal static bool TweaksMenuVisible =>
        TweaksAvailable && HollowKnightModsRuntime.Current.Session.Menu.IsOpen;
    internal static bool BlackBackground => _backdropBlackOverride == true;
    internal static HollowKnightFlashMode? FlashOverride => _flashOverride;
    internal static HollowKnightFlashMode? LegacyFlashMode => _legacyFlashMode;
    internal static float? LegacyFlashAlpha => _legacyFlashAlpha;
    internal static int SkinStamp => DualSouls.Skins.HollowKnight.Runtime.HollowKnightSkinRuntime.SkinStamp;

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
        RecordBench();
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

    internal static void OpenSkins()
    {
        using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var intent = new AndroidJavaObject("android.content.Intent"))
        using (var component = new AndroidJavaObject(
            "android.content.ComponentName",
            activity.Call<string>("getPackageName"),
            "dev.silksong.launcher.skins.ui.SkinsActivity"))
        {
            intent.Call<AndroidJavaObject>("setComponent", component);
            activity.Call("startActivity", intent);
        }
    }

    internal static void OpenBenchTeleport()
    {
        HKDualScreen.OpenBenchTeleportRoute();
    }

    internal static bool IsBenchRecorded(string scene)
    {
        EnsureBenchesLoaded();
        return !string.IsNullOrEmpty(scene) && benches.ContainsKey(scene);
    }

    internal static void BenchWarp(string scene)
    {
        EnsureBenchesLoaded();
        if (string.IsNullOrEmpty(scene) || !benches.TryGetValue(scene, out BenchRecord record))
            throw new InvalidOperationException("That bench has not been recorded.");
        GameManager game = GameManager.UnsafeInstance;
        PlayerData player = game != null ? game.playerData : null;
        if (game == null || player == null)
            throw new InvalidOperationException("Hollow Knight is not ready to travel.");
        player.SetBenchRespawn(record.marker, record.scene, record.type, record.facingRight);
        game.ReadyForRespawn(false);
    }

    static void RecordBench()
    {
        EnsureBenchesLoaded();
        GameManager game = GameManager.UnsafeInstance;
        PlayerData player = game != null ? game.playerData : null;
        bool atBench = player != null && player.atBench;
        if (!atBench || lastAtBench)
        {
            lastAtBench = atBench;
            return;
        }
        lastAtBench = true;
        if (string.IsNullOrEmpty(player.respawnScene) ||
            string.IsNullOrEmpty(player.respawnMarkerName)) return;
        benches[player.respawnScene] = new BenchRecord
        {
            scene = player.respawnScene,
            marker = player.respawnMarkerName,
            type = player.respawnType,
            facingRight = player.respawnFacingRight,
        };
        SaveBenches();
    }

    static void EnsureBenchesLoaded()
    {
        if (benchLoaded) return;
        benchLoaded = true;
        try
        {
            if (!File.Exists(BenchPath)) return;
            var info = new FileInfo(BenchPath);
            if (info.Length > 512 * 1024)
                throw new InvalidDataException("The recorded-bench sidecar is too large.");
            BenchEnvelope envelope = JsonUtility.FromJson<BenchEnvelope>(File.ReadAllText(BenchPath));
            if (envelope == null || envelope.records == null) return;
            int count = Math.Min(envelope.records.Count, 256);
            for (int i = 0; i < count; i++)
            {
                BenchRecord record = envelope.records[i];
                if (record == null || string.IsNullOrEmpty(record.scene) ||
                    string.IsNullOrEmpty(record.marker)) continue;
                benches[record.scene] = record;
            }
        }
        catch (Exception error)
        {
            Debug.LogError("Dual Souls could not load recorded benches: " + error.Message);
            benches.Clear();
        }
    }

    static void SaveBenches()
    {
        var envelope = new BenchEnvelope();
        foreach (BenchRecord record in benches.Values)
        {
            if (envelope.records.Count == 256) break;
            envelope.records.Add(record);
        }
        try
        {
            File.WriteAllText(BenchPath, JsonUtility.ToJson(envelope));
        }
        catch (Exception error)
        {
            Debug.LogError("Dual Souls could not save recorded benches: " + error.Message);
        }
    }

    internal static KeyCode JoyBtn(int index)
    {
        return (KeyCode)((int)joyBase + index);
    }
}
