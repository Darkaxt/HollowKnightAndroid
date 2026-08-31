#if UNITY_ANDROID && !UNITY_EDITOR
using System;

namespace DualSouls.Mods.Silksong
{
    /// <summary>
    /// Silksong 1.0.29980 managed implementation. All mutations go through
    /// game-owned properties and methods which are checked at patch compile time.
    /// </summary>
    public sealed class SilksongGameTweakApi : ISilksongTweakApi
    {
        // Process-wide on purpose. The second-screen shell can be rebuilt when
        // the game font appears or the panel is reattached; recapturing then
        // would mistake an already-enabled tweak for the vanilla baseline.
        static bool _captured;
        static CheatManager.InvincibilityStates _invincibility;
        static CheatManager.NailDamageStates _nailDamage;
        static bool _silkDrainDisabled;
        static bool _equipAnywhere;

        public void CaptureBaseline()
        {
            if (_captured) return;
            _invincibility = CheatManager.Invincibility;
            _nailDamage = CheatManager.NailDamage;
            _silkDrainDisabled = CheatManager.IsSilkDrainDisabled;
            _equipAnywhere = CheatManager.CanChangeEquipsAnywhere;
            _captured = true;
        }

        public void RestoreBaseline()
        {
            EnsureCaptured();
            CheatManager.Invincibility = _invincibility;
            CheatManager.NailDamage = _nailDamage;
            CheatManager.IsSilkDrainDisabled = _silkDrainDisabled;
            CheatManager.CanChangeEquipsAnywhere = _equipAnywhere;
        }

        public void SetDamageMode(SilksongDamageMode mode)
        {
            EnsureCaptured();
            CheatManager.Invincibility = mode == SilksongDamageMode.PreventDeath
                ? CheatManager.InvincibilityStates.PreventDeath
                : CheatManager.InvincibilityStates.FullInvincible;
        }

        public void RestoreDamageMode()
        {
            EnsureCaptured();
            CheatManager.Invincibility = _invincibility;
        }

        public void SetUnlimitedSilk(bool enabled)
        {
            EnsureCaptured();
            CheatManager.IsSilkDrainDisabled = enabled;
        }

        public void RestoreUnlimitedSilk()
        {
            EnsureCaptured();
            CheatManager.IsSilkDrainDisabled = _silkDrainDisabled;
        }

        public void SetOneHitKills(bool enabled)
        {
            EnsureCaptured();
            CheatManager.NailDamage = enabled
                ? CheatManager.NailDamageStates.InstaKill
                : _nailDamage;
        }

        public void RestoreOneHitKills()
        {
            EnsureCaptured();
            CheatManager.NailDamage = _nailDamage;
        }

        public void SetEquipAnywhere(bool enabled)
        {
            EnsureCaptured();
            CheatManager.CanChangeEquipsAnywhere = enabled;
        }

        public void RestoreEquipAnywhere()
        {
            EnsureCaptured();
            CheatManager.CanChangeEquipsAnywhere = _equipAnywhere;
        }

        public void RefillSilk()
        {
            if (!PlayerData.HasInstance) return;
            HeroController hero = HeroController.instance;
            if (hero == null) return;
            PlayerData data = PlayerData.instance;
            if (data.silk < data.CurrentSilkMax) hero.RefillSilkToMaxSilent();
        }

        void EnsureCaptured()
        {
            if (!_captured) throw new InvalidOperationException("CaptureBaseline must run before applying Silksong tweaks.");
        }
    }
}
#endif
