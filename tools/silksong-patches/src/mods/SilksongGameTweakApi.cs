#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    /// <summary>Silksong 1.0.29980 managed tweak implementation.</summary>
    public sealed class SilksongGameTweakApi : ISilksongTweakApi
    {
        static bool _captured;
        static CheatManager.InvincibilityStates _invincibility;
        static CheatManager.NailDamageStates _nailDamage;
        static bool _silkDrainDisabled;
        static bool _equipAnywhere;
        static bool _instantDialogue;
        static bool _worldRumbleDisabled;
        static bool _frostDisabled;
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
            _silkGrantOwner = this;
            SilksongGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer);
        }

        public void RestoreBaseline()
        {
            if (!_captured) return;
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
        public void OpenBenchTeleport() { SilksongGameplayFeatures.RequestBenchRoute(); }
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
        }
        public void RestoreDamageMode() { EnsureCaptured(); CheatManager.Invincibility = _invincibility; }
        public void SetDamageCap(bool enabled) { SilksongGameplayHooks.DamageCapEnabled = enabled; }
        public void SetOneHitKills(bool enabled)
        {
            EnsureCaptured();
            CheatManager.NailDamage = enabled ? CheatManager.NailDamageStates.InstaKill : _nailDamage;
        }
        public void RestoreOneHitKills() { EnsureCaptured(); CheatManager.NailDamage = _nailDamage; }

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
        }
        public void RestoreEquipAnywhere() { EnsureCaptured(); CheatManager.CanChangeEquipsAnywhere = _equipAnywhere; }
        public void SetToolCostsFree(bool enabled) { SilksongGameplayHooks.ToolCostsFreeEnabled = enabled; }
        public void SetUnlimitedToolSlots(bool enabled) { _unlimitedToolSlots = enabled; }

        public void SetStateSlot(int slot)
        {
            if (slot < 1 || slot > 5) throw new ArgumentOutOfRangeException(nameof(slot));
            _stateSlot = slot;
        }
        public void SaveState() { SilksongGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer); SilksongGameplayFeatures.SaveState(_stateSlot); }
        public void LoadState() { SilksongGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer); SilksongGameplayFeatures.LoadState(_stateSlot); }
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
            EnsureCaptured(); CheatManager.IsTextPrintSkipEnabled = enabled ? true : _instantDialogue;
        }
        public void RestoreInstantDialogue() { EnsureCaptured(); CheatManager.IsTextPrintSkipEnabled = _instantDialogue; }
        public void SetWorldRumbleDisabled(bool enabled)
        {
            EnsureCaptured(); CheatManager.IsWorldRumbleDisabled = enabled ? true : _worldRumbleDisabled;
        }
        public void RestoreWorldRumbleDisabled() { EnsureCaptured(); CheatManager.IsWorldRumbleDisabled = _worldRumbleDisabled; }
        public void SetFrostDisabled(bool enabled)
        {
            EnsureCaptured(); CheatManager.IsFrostDisabled = enabled ? true : _frostDisabled;
        }
        public void RestoreFrostDisabled() { EnsureCaptured(); CheatManager.IsFrostDisabled = _frostDisabled; }

        public void TickGameplay()
        {
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

        static void EnsureCaptured()
        {
            if (!_captured) throw new InvalidOperationException("CaptureBaseline must run before applying Silksong tweaks.");
        }
    }
}
#endif
