#if (UNITY_ANDROID && !UNITY_EDITOR) || HOLLOW_KNIGHT_GAMEPLAY_TESTS
namespace DualSouls.Mods.HollowKnight
{
    public sealed class HollowKnightGameTweakApi : IHollowKnightTweakApi
    {
        bool _captured;
        HollowKnightDamageMode? _damageMode;
        global::PlayerData _damagePlayer;
        bool _damageInvincibilityBaseline;
        bool _damageInvincibilityCaptured;
        int _lastHealth = -1;
        int _nailMultiplier = 1;
        global::PlayerData _nailPlayer;
        int _nailBaseline;
        int _nailUpgradeBaseline;
        float _runSpeedMultiplier = 1f;
        global::HeroController _runHero;
        float _runSpeedBaseline;
        float _walkSpeedBaseline;
        bool _unlimitedSoul;
        float _nextSoulRefillAt;

        public bool IsReady => true;

        public void CaptureBaseline()
        {
            _captured = true;
        }

        public void RestoreBaseline()
        {
            if (!_captured) return;
            RestoreDamageMode();
            RestoreNailDamage();
            RestoreOneHitKills();
            RestoreRunSpeed();
            RestoreUnlimitedSoul();
            global::HkStageHooks.ClearPresentationOverrides();
        }

        public void SetCompanionBackdropBlack(bool black)
        {
            global::HkStageHooks.SetBackdropOverride(black);
        }

        public void SetLifebloodFlash(HollowKnightFlashMode mode)
        {
            global::HkStageHooks.SetFlashOverride(mode);
        }

        public void SetDamageMode(HollowKnightDamageMode mode)
        {
            _damageMode = mode;
            MaintainDamageMode();
        }

        public void RestoreDamageMode()
        {
            _damageMode = null;
            RestoreDamageOwner();
        }

        public void SetNailDamageMultiplier(int multiplier)
        {
            if (multiplier != 2 && multiplier != 3 && multiplier != 5)
                throw new System.ArgumentOutOfRangeException(nameof(multiplier));
            _nailMultiplier = multiplier;
            MaintainNailDamage();
        }

        public void RestoreNailDamage()
        {
            _nailMultiplier = 1;
            RestoreNailOwner();
        }

        public void SetOneHitKills(bool enabled)
        {
            HollowKnightOneHitDamagePatch.SetEnabled(enabled);
        }

        public void RestoreOneHitKills()
        {
            HollowKnightOneHitDamagePatch.SetEnabled(false);
        }

        public void SetRunSpeedMultiplier(float multiplier)
        {
            if (multiplier != 1.25f && multiplier != 1.5f)
                throw new System.ArgumentOutOfRangeException(nameof(multiplier));
            _runSpeedMultiplier = multiplier;
            MaintainRunSpeed();
        }

        public void RestoreRunSpeed()
        {
            _runSpeedMultiplier = 1f;
            RestoreRunSpeedOwner();
        }

        public void SetUnlimitedSoul(bool enabled)
        {
            _unlimitedSoul = enabled;
            if (enabled) MaintainUnlimitedSoul();
        }

        public void RestoreUnlimitedSoul()
        {
            _unlimitedSoul = false;
        }

        public void TickGameplay()
        {
            MaintainDamageMode();
            MaintainNailDamage();
            MaintainRunSpeed();
            MaintainUnlimitedSoul();
        }

        void MaintainUnlimitedSoul()
        {
            if (!_unlimitedSoul || UnityEngine.Time.unscaledTime < _nextSoulRefillAt) return;
            _nextSoulRefillAt = UnityEngine.Time.unscaledTime + 0.4f;
            global::GameManager game = global::GameManager.UnsafeInstance;
            global::PlayerData player = game != null ? game.playerData : null;
            global::HeroController hero = game != null ? game.hero_ctrl : null;
            if (player == null || hero == null || player.health <= 0 || player.MPCharge >= 99)
                return;
            hero.AddMPCharge(33);
        }

        void MaintainRunSpeed()
        {
            if (_runSpeedMultiplier == 1f) return;
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (hero == null) return;
            if (!object.ReferenceEquals(_runHero, hero))
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
            _runSpeedBaseline = 0f;
            _walkSpeedBaseline = 0f;
        }

        void MaintainNailDamage()
        {
            if (_nailMultiplier == 1) return;
            global::GameManager game = global::GameManager.UnsafeInstance;
            global::PlayerData player = game != null ? game.playerData : null;
            if (player == null) return;
            if (!object.ReferenceEquals(_nailPlayer, player))
            {
                RestoreNailOwner();
                _nailPlayer = player;
                _nailBaseline = player.nailDamage;
                _nailUpgradeBaseline = player.nailSmithUpgrades;
            }
            else if (player.nailSmithUpgrades != _nailUpgradeBaseline)
            {
                _nailBaseline += 4 * (player.nailSmithUpgrades - _nailUpgradeBaseline);
                _nailUpgradeBaseline = player.nailSmithUpgrades;
            }

            int wanted = _nailBaseline * _nailMultiplier;
            if (player.nailDamage == wanted) return;
            player.nailDamage = wanted;
            global::PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");
        }

        void RestoreNailOwner()
        {
            if (_nailPlayer != null && _nailPlayer.nailDamage != _nailBaseline)
            {
                _nailPlayer.nailDamage = _nailBaseline;
                global::PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");
            }
            _nailPlayer = null;
            _nailBaseline = 0;
            _nailUpgradeBaseline = 0;
        }

        void MaintainDamageMode()
        {
            if (!_damageMode.HasValue) return;
            global::GameManager game = global::GameManager.UnsafeInstance;
            global::PlayerData player = game != null ? game.playerData : null;
            if (player == null) return;
            if (!object.ReferenceEquals(_damagePlayer, player))
            {
                RestoreDamageOwner();
                _damagePlayer = player;
                _lastHealth = player.health;
            }

            if (_damageMode.Value == HollowKnightDamageMode.Invincible)
            {
                if (!_damageInvincibilityCaptured)
                {
                    _damageInvincibilityBaseline = player.isInvincible;
                    _damageInvincibilityCaptured = true;
                }
                player.isInvincible = true;
                _lastHealth = player.health;
                return;
            }

            RestoreDamageInvincibility();
            int health = player.health;
            if (_lastHealth > 0 && health > 0 && health < _lastHealth)
            {
                int damage = _lastHealth - health;
                if (damage <= 4)
                {
                    global::HeroController hero = game.hero_ctrl;
                    if (hero != null)
                    {
                        hero.AddHealth(damage);
                        health = player.health;
                    }
                }
            }
            _lastHealth = health;
        }

        void RestoreDamageOwner()
        {
            RestoreDamageInvincibility();
            _damagePlayer = null;
            _lastHealth = -1;
        }

        void RestoreDamageInvincibility()
        {
            if (_damagePlayer != null && _damageInvincibilityCaptured)
                _damagePlayer.isInvincible = _damageInvincibilityBaseline;
            _damageInvincibilityCaptured = false;
        }
    }

    public static class HollowKnightOneHitDamagePatch
    {
        sealed class TargetEligibility
        {
            public TargetEligibility(bool regularEnemy)
            {
                RegularEnemy = regularEnemy;
            }

            public bool RegularEnemy { get; }
        }

        static bool _enabled;
        static System.Runtime.CompilerServices.ConditionalWeakTable<
            global::HealthManager, TargetEligibility> _targets = NewTargetCache();

        public static void SetEnabled(bool enabled)
        {
            if (_enabled == enabled)
            {
                if (!enabled) _targets = NewTargetCache();
                return;
            }
            _enabled = enabled;
            _targets = NewTargetCache();
        }

        public static void BeforeHit(global::HealthManager target, ref global::HitInstance hitInstance)
        {
            if (!_enabled || target == null || target.isDead || hitInstance.DamageDealt <= 0)
                return;

            if (!_targets.TryGetValue(target, out TargetEligibility eligibility))
            {
                eligibility = new TargetEligibility(target.hp > 1 && target.hp < 200);
                _targets.Add(target, eligibility);
            }
            if (eligibility.RegularEnemy && hitInstance.DamageDealt < 9999)
                hitInstance.DamageDealt = 9999;
        }

        static System.Runtime.CompilerServices.ConditionalWeakTable<
            global::HealthManager, TargetEligibility> NewTargetCache()
        {
            return new System.Runtime.CompilerServices.ConditionalWeakTable<
                global::HealthManager, TargetEligibility>();
        }
    }
}
#endif
