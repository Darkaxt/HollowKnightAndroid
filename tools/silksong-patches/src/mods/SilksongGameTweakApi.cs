#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using DualSouls.Mods;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    /// <summary>Silksong 1.0.29980 managed tweak implementation.</summary>
    public sealed class SilksongGameTweakApi : ISilksongTweakApi
    {
        static SilksongGameTweakApi _baselineOwner;
        bool _captured;
        CheatManager.InvincibilityStates _invincibility;
        CheatManager.NailDamageStates _nailDamage;
        bool _silkDrainDisabled;
        bool _equipAnywhere;
        bool _instantDialogue;
        bool _worldRumbleDisabled;
        bool _frostDisabled;
        static SilksongGameTweakApi _silkGrantOwner;

        float _runSpeedMultiplier = 1f;
        HeroController _runHero;
        float _runSpeedBaseline;
        float _walkSpeedBaseline;
        bool _unlimitedSilk;
        float _nextSilkRefillAt;
        PlayerData _silkPlayer;
        int _silkBaseline;
        int _silkPartsBaseline;
        int _silkGrantDepth;
        int _silkLive;
        int _silkPartsLive;
        bool _silkOwned;
        bool _silkRefillInProgress;
        bool _fastTransitions;
        bool _autoMap;
        bool _innateCompass;
        bool _secretRadar;
        bool _healthBars;
        bool _damageNumbers;
        bool _bossRetry;
        bool _unlimitedToolSlots;
        bool _rosaryMagnet;
        bool _keepRosaries;
        int _stateSlot = 1;
        readonly SilksongAppliedChoiceState _appliedChoices =
            new SilksongAppliedChoiceState();
        readonly TweakDeferredOperation _stateLoadOperation =
            new TweakDeferredOperation("load_from_slot", 30f);
        readonly Queue<TweakAdapterCompletion> _completedOperations =
            new Queue<TweakAdapterCompletion>();

        public bool IsReady
        {
            get
            {
                try
                {
                    if (!PlayerData.HasInstance || HeroController.instance == null) return false;
                    GameManager manager = GameManager.SilentInstance;
                    return manager != null && manager.IsGameplayScene();
                }
                catch { return false; }
            }
        }

        public void CaptureBaseline()
        {
            if (_baselineOwner != null && !ReferenceEquals(_baselineOwner, this))
                throw new InvalidOperationException("The previous Silksong Mods owner has not retired.");
            if (!_captured)
            {
                _invincibility = CheatManager.Invincibility;
                _nailDamage = CheatManager.NailDamage;
                _silkDrainDisabled = CheatManager.IsSilkDrainDisabled;
                _equipAnywhere = CheatManager.CanChangeEquipsAnywhere;
                _instantDialogue = CheatManager.IsTextPrintSkipEnabled;
                _worldRumbleDisabled = CheatManager.IsWorldRumbleDisabled;
                _frostDisabled = CheatManager.IsFrostDisabled;
                _captured = true;
            }
            _baselineOwner = this;
            _silkGrantOwner = this;
            SilksongGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer);
        }

        public void RestoreBaseline()
        {
            if (!_captured || !ReferenceEquals(_baselineOwner, this)) return;
            RestoreDamageMode();
            RestoreOneHitKills();
            RestoreUnlimitedSilk();
            RestoreEquipAnywhere();
            RestoreInstantDialogue();
            RestoreWorldRumbleDisabled();
            RestoreFrostDisabled();
            RestoreRunSpeed();
            _fastTransitions = _autoMap = _innateCompass = _secretRadar = false;
            _healthBars = _damageNumbers = _bossRetry = _unlimitedToolSlots = false;
            _rosaryMagnet = _keepRosaries = _unlimitedSilk = false;
            _stateSlot = 1;
            _completedOperations.Clear();
            Enqueue(_stateLoadOperation.Cancel(
                "Silksong save-state load was canceled by baseline restoration."));
            DsPortSceneryState.BlackBackground = false;
            SilksongGameplayHooks.Reset();
            SilksongGameplayFeatures.RestoreAll();
            SilksongGameplayFeatures.SetStateTransferPreparation(null);
            if (ReferenceEquals(_silkGrantOwner, this)) _silkGrantOwner = null;
        }

        public void OpenSkins()
        {
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject("android.content.Intent"))
            using (var component = new AndroidJavaObject("android.content.ComponentName",
                activity.Call<string>("getPackageName"), "dev.silksong.launcher.skins.ui.SkinsActivity"))
            {
                intent.Call<AndroidJavaObject>("setComponent", component);
                activity.Call("startActivity", intent);
            }
        }

        public void SetCompanionBackdropBlack(bool black) { DsPortSceneryState.BlackBackground = black; }

        public void SetRunSpeedMultiplier(float multiplier)
        {
            if (multiplier != 1.25f && multiplier != 1.5f) throw new ArgumentOutOfRangeException(nameof(multiplier));
            _runSpeedMultiplier = multiplier;
            MaintainRunSpeed();
        }
        public void RestoreRunSpeed() { _runSpeedMultiplier = 1f; RestoreRunSpeedOwner(); }
        public void SetFastTransitions(bool enabled) { _fastTransitions = enabled; }
        public void SetAutoMap(bool enabled) { _autoMap = enabled; }
        public void SetInnateCompass(bool enabled) { _innateCompass = enabled; SilksongGameplayHooks.InnateCompassEnabled = enabled; }
        public void OpenBenchTeleport(long operationToken)
        {
            SilksongNativeModsMenu.OpenBenchTeleportRoute(operationToken);
        }
        public void SetSecretRadar(bool enabled) { _secretRadar = enabled; }

        public void SetNeedleDamageMultiplier(int multiplier)
        {
            if (multiplier != 2 && multiplier != 3 && multiplier != 5) throw new ArgumentOutOfRangeException(nameof(multiplier));
            SilksongGameplayHooks.NeedleMultiplier = multiplier;
        }
        public void RestoreNeedleDamage() { SilksongGameplayHooks.NeedleMultiplier = 1; }

        public void SetDamageMode(SilksongDamageMode mode)
        {
            EnsureCaptured();
            CheatManager.Invincibility = mode == SilksongDamageMode.PreventDeath
                ? CheatManager.InvincibilityStates.PreventDeath
                : CheatManager.InvincibilityStates.FullInvincible;
            _appliedChoices.SetDamageMode(mode);
        }
        public void RestoreDamageMode()
        {
            EnsureCaptured();
            CheatManager.Invincibility = _invincibility;
            _appliedChoices.RestoreDamageMode();
        }
        public void SetDamageCap(bool enabled) { SilksongGameplayHooks.DamageCapEnabled = enabled; }
        public void SetOneHitKills(bool enabled)
        {
            EnsureCaptured();
            CheatManager.NailDamage = enabled ? CheatManager.NailDamageStates.InstaKill : _nailDamage;
            _appliedChoices.SetOneHitKills(enabled);
        }
        public void RestoreOneHitKills()
        {
            EnsureCaptured();
            CheatManager.NailDamage = _nailDamage;
            _appliedChoices.SetOneHitKills(false);
        }

        public void SetUnlimitedSilk(bool enabled)
        {
            EnsureCaptured();
            _unlimitedSilk = enabled;
            CheatManager.IsSilkDrainDisabled = enabled ? true : _silkDrainDisabled;
            if (enabled) MaintainUnlimitedSilk();
            else RestoreSilkOwner();
        }
        public void RestoreUnlimitedSilk()
        {
            EnsureCaptured();
            _unlimitedSilk = false;
            RestoreSilkOwner();
            CheatManager.IsSilkDrainDisabled = _silkDrainDisabled;
        }

        public void SetEnemyHealthBars(bool enabled) { _healthBars = enabled; }
        public void SetDamageNumbers(bool enabled) { _damageNumbers = enabled; }
        public void SetBossRetry(bool enabled)
        {
            _bossRetry = enabled;
            if (enabled) SilksongGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer);
        }
        public void SetEquipAnywhere(bool enabled)
        {
            EnsureCaptured();
            CheatManager.CanChangeEquipsAnywhere = enabled ? true : _equipAnywhere;
            _appliedChoices.SetEquipAnywhere(enabled);
        }
        public void RestoreEquipAnywhere()
        {
            EnsureCaptured();
            CheatManager.CanChangeEquipsAnywhere = _equipAnywhere;
            _appliedChoices.SetEquipAnywhere(false);
        }
        public void SetToolCostsFree(bool enabled) { SilksongGameplayHooks.ToolCostsFreeEnabled = enabled; }
        public void SetUnlimitedToolSlots(bool enabled) { _unlimitedToolSlots = enabled; }

        public void SetStateSlot(int slot)
        {
            if (slot < 1 || slot > 5) throw new ArgumentOutOfRangeException(nameof(slot));
            _stateSlot = slot;
        }
        public void SaveState() { SilksongGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer); SilksongGameplayFeatures.SaveState(_stateSlot); }
        public void LoadState(long operationToken)
        {
            _stateLoadOperation.Begin(operationToken, Time.unscaledTime);
            try
            {
                SilksongGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer);
                int slot = _stateSlot;
                SilksongGameplayFeatures.LoadState(
                    slot,
                    scene => Enqueue(_stateLoadOperation.Complete(
                        operationToken,
                        TweakActionResult.Ok(TweakReadback.Text(
                            "slot " + slot + " loaded in " +
                            (scene ?? "unknown scene"))))));
            }
            catch
            {
                _stateLoadOperation.Cancel(
                    "Silksong save-state load failed before it was accepted.");
                throw;
            }
        }
        public void DeleteState() { SilksongGameplayFeatures.DeleteState(_stateSlot); }
        public void SetRosaryMagnet(bool enabled) { _rosaryMagnet = enabled; }
        public void SetKeepRosariesOnDeath(bool enabled)
        {
            _keepRosaries = enabled;
            SilksongGameplayHooks.KeepRosariesEnabled = enabled;
            if (!enabled) SilksongGameplayHooks.CompleteDeathHandling();
        }
        public void SetJournalOneKill(bool enabled) { SilksongGameplayHooks.JournalOneKillEnabled = enabled; }
        public void SetRosaryMultiplier(int multiplier)
        {
            if (multiplier != 1 && multiplier != 2 && multiplier != 3 && multiplier != 5) throw new ArgumentOutOfRangeException(nameof(multiplier));
            SilksongGameplayHooks.RosaryMultiplier = multiplier;
        }

        public void SetInstantDialogue(bool enabled)
        {
            EnsureCaptured();
            CheatManager.IsTextPrintSkipEnabled = enabled ? true : _instantDialogue;
            _appliedChoices.SetInstantDialogue(enabled);
        }
        public void RestoreInstantDialogue()
        {
            EnsureCaptured();
            CheatManager.IsTextPrintSkipEnabled = _instantDialogue;
            _appliedChoices.SetInstantDialogue(false);
        }
        public void SetWorldRumbleDisabled(bool enabled)
        {
            EnsureCaptured();
            CheatManager.IsWorldRumbleDisabled = enabled ? true : _worldRumbleDisabled;
            _appliedChoices.SetWorldRumbleDisabled(enabled);
        }
        public void RestoreWorldRumbleDisabled()
        {
            EnsureCaptured();
            CheatManager.IsWorldRumbleDisabled = _worldRumbleDisabled;
            _appliedChoices.SetWorldRumbleDisabled(false);
        }
        public void SetFrostDisabled(bool enabled)
        {
            EnsureCaptured();
            CheatManager.IsFrostDisabled = enabled ? true : _frostDisabled;
            _appliedChoices.SetFrostDisabled(enabled);
        }
        public void RestoreFrostDisabled()
        {
            EnsureCaptured();
            CheatManager.IsFrostDisabled = _frostDisabled;
            _appliedChoices.SetFrostDisabled(false);
        }

        public TweakActionResult Readback(string id)
        {
            switch (id)
            {
                case "black_background": return OnOff(DsPortSceneryState.BlackBackground);
                case "run_speed":
                    return Choice(_runSpeedMultiplier == 1.5f ? "plus_50" :
                                  _runSpeedMultiplier == 1.25f ? "plus_25" : "vanilla");
                case "fast_transitions": return OnOff(_fastTransitions);
                case "auto_map": return OnOff(_autoMap);
                case "innate_compass": return OnOff(_innateCompass);
                case "secret_radar": return OnOff(_secretRadar);
                case "nail_damage":
                    return Choice(MultiplierChoice(SilksongGameplayHooks.NeedleMultiplier));
                case "damage_received":
                case "one_hit_kills":
                case "equip_anywhere":
                case "instant_dialogue":
                case "disable_world_rumble":
                case "ignore_frost_slowdown":
                    return _appliedChoices.Readback(id);
                case "damage_cap": return OnOff(SilksongGameplayHooks.DamageCapEnabled);
                case "unlimited_silk": return OnOff(_unlimitedSilk);
                case "health_bars": return OnOff(_healthBars);
                case "damage_numbers": return OnOff(_damageNumbers);
                case "boss_retry": return OnOff(_bossRetry);
                case "charm_costs":
                    return Choice(SilksongGameplayHooks.ToolCostsFreeEnabled ? "free" : "vanilla");
                case "unlimited_notches": return OnOff(_unlimitedToolSlots);
                case "state_slot":
                    return Choice(_stateSlot.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                case "geo_magnet": return OnOff(_rosaryMagnet);
                case "keep_geo_on_death": return OnOff(_keepRosaries);
                case "journal_one_kill":
                    return OnOff(SilksongGameplayHooks.JournalOneKillEnabled);
                case "geo_multiplier":
                    return Choice(MultiplierChoice(SilksongGameplayHooks.RosaryMultiplier));
                case "save_to_slot":
                    return TweakActionResult.Ok(TweakReadback.Boolean(
                        SilksongGameplayFeatures.StateExists(_stateSlot)));
                case "delete_slot":
                    return TweakActionResult.Ok(TweakReadback.Boolean(
                        !SilksongGameplayFeatures.StateExists(_stateSlot)));
                default:
                    return TweakActionResult.Fail(
                        "No safe Silksong runtime readback exists for " + id + ".");
            }
        }

        void Enqueue(TweakAdapterCompletion? completion)
        {
            if (completion.HasValue)
                _completedOperations.Enqueue(completion.Value);
        }

        public IReadOnlyList<TweakAdapterCompletion> DrainCompletedOperations()
        {
            if (_completedOperations.Count == 0)
                return Array.Empty<TweakAdapterCompletion>();
            var completed = new TweakAdapterCompletion[_completedOperations.Count];
            for (int index = 0; index < completed.Length; index++)
                completed[index] = _completedOperations.Dequeue();
            return completed;
        }

        static TweakActionResult Choice(string value) =>
            TweakActionResult.Ok(TweakReadback.Choice(value));
        static TweakActionResult OnOff(bool enabled) =>
            Choice(enabled ? "on" : "off");
        static string MultiplierChoice(int multiplier) =>
            multiplier == 2 ? "x2" : multiplier == 3 ? "x3" :
            multiplier == 5 ? "x5" : "x1";

        public void TickGameplay()
        {
            Enqueue(_stateLoadOperation.Poll(
                Time.unscaledTime,
                "Silksong save-state load timed out before scene completion."));
            MaintainRunSpeed();
            MaintainUnlimitedSilk();
            SilksongGameplayFeatures.Tick(_fastTransitions, _autoMap, _innateCompass,
                _secretRadar, _healthBars, _damageNumbers, _bossRetry,
                _unlimitedToolSlots, _rosaryMagnet, _keepRosaries);
        }

        void MaintainRunSpeed()
        {
            if (_runSpeedMultiplier == 1f) return;
            HeroController hero = HeroController.instance;
            if (hero == null) return;
            if (!ReferenceEquals(_runHero, hero))
            {
                RestoreRunSpeedOwner();
                _runHero = hero;
                _runSpeedBaseline = hero.RUN_SPEED;
                _walkSpeedBaseline = hero.WALK_SPEED;
            }
            hero.RUN_SPEED = _runSpeedBaseline * _runSpeedMultiplier;
            hero.WALK_SPEED = _walkSpeedBaseline * _runSpeedMultiplier;
        }

        void RestoreRunSpeedOwner()
        {
            if (_runHero != null)
            {
                _runHero.RUN_SPEED = _runSpeedBaseline;
                _runHero.WALK_SPEED = _walkSpeedBaseline;
            }
            _runHero = null;
            _runSpeedBaseline = _walkSpeedBaseline = 0f;
        }

        void MaintainUnlimitedSilk()
        {
            if (!_unlimitedSilk) { RestoreSilkOwner(); return; }
            PlayerData data = PlayerData.HasInstance ? PlayerData.instance : null;
            if (!ReferenceEquals(_silkPlayer, data)) RestoreSilkOwner();
            if (Time.unscaledTime < _nextSilkRefillAt) return;
            _nextSilkRefillAt = Time.unscaledTime + 0.4f;
            HeroController hero = HeroController.instance;
            if (hero == null || data == null || data.health <= 0 || data.silk >= data.CurrentSilkMax) return;
            if (!_silkOwned)
            {
                _silkPlayer = data;
                _silkBaseline = data.silk;
                _silkPartsBaseline = data.silkParts;
                _silkOwned = true;
            }
            _silkRefillInProgress = true;
            try { hero.RefillSilkToMaxSilent(); }
            finally { _silkRefillInProgress = false; }
        }

        internal static void BeginAuthoritativeSilkGrant(PlayerData player)
        {
            SilksongGameTweakApi owner = _silkGrantOwner;
            if (owner != null) owner.BeginSilkGrant(player);
        }

        internal static void EndAuthoritativeSilkGrant(PlayerData player)
        {
            SilksongGameTweakApi owner = _silkGrantOwner;
            if (owner != null) owner.EndSilkGrant(player);
        }

        internal static void BeginAuthoritativeSilkPartsGrant(PlayerData player)
        {
            BeginAuthoritativeSilkGrant(player);
        }

        internal static void EndAuthoritativeSilkPartsGrant(PlayerData player)
        {
            EndAuthoritativeSilkGrant(player);
        }

        void BeginSilkGrant(PlayerData player)
        {
            if (_silkRefillInProgress || !_silkOwned || player == null ||
                !ReferenceEquals(_silkPlayer, player)) return;
            if (_silkGrantDepth == 0)
            {
                _silkLive = player.silk;
                _silkPartsLive = player.silkParts;
                player.silk = _silkBaseline;
                player.silkParts = _silkPartsBaseline;
            }
            _silkGrantDepth++;
        }

        void EndSilkGrant(PlayerData player)
        {
            if (_silkGrantDepth <= 0 || player == null || !ReferenceEquals(_silkPlayer, player)) return;
            _silkGrantDepth--;
            if (_silkGrantDepth != 0) return;
            _silkBaseline = player.silk;
            _silkPartsBaseline = player.silkParts;
            player.silk = _silkLive;
            player.silkParts = _silkPartsLive;
            _silkLive = 0;
            _silkPartsLive = 0;
            RefreshSilkDisplay(player);
        }

        void RestoreSilkOwner()
        {
            PlayerData player = _silkPlayer;
            if (_silkOwned && player != null)
            {
                player.silk = _silkBaseline;
                player.silkParts = _silkPartsBaseline;
                RefreshSilkDisplay(player);
            }
            _silkPlayer = null;
            _silkBaseline = 0;
            _silkPartsBaseline = 0;
            _silkGrantDepth = 0;
            _silkLive = 0;
            _silkPartsLive = 0;
            _silkOwned = false;
        }

        static void RefreshSilkDisplay(PlayerData player)
        {
            if (!PlayerData.HasInstance || !ReferenceEquals(PlayerData.instance, player)) return;
            GameCameras cameras = GameCameras.SilentInstance;
            if (cameras != null && cameras.silkSpool != null) cameras.silkSpool.RefreshSilk();
        }

        void PrepareForStateTransfer()
        {
            RestoreRunSpeedOwner();
            RestoreSilkOwner();
            SilksongGameplayFeatures.RestorePlayerOwnedForStateTransfer();
        }

        // Only the runtime/restore pump may retire a generation, after the session
        // has completed restoration. RESET ALL MODS keeps this same baseline.
        internal void RetireBaseline()
        {
            if (!ReferenceEquals(_baselineOwner, this)) return;
            _baselineOwner = null;
            if (ReferenceEquals(_silkGrantOwner, this)) _silkGrantOwner = null;
        }

        void EnsureCaptured()
        {
            if (!_captured || !ReferenceEquals(_baselineOwner, this))
                throw new InvalidOperationException("CaptureBaseline must run for the current Silksong Mods owner before applying tweaks.");
        }
    }
}
#endif
