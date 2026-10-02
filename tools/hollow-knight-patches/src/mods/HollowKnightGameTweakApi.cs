#if (UNITY_ANDROID && !UNITY_EDITOR) || HOLLOW_KNIGHT_GAMEPLAY_TESTS
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    public sealed class HollowKnightGameTweakApi : IHollowKnightTweakApi
    {
        readonly bool _verifiedHooks = ReadVerifiedHookCapability();
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
        bool _fastTransitions;
        bool _autoMap;
        bool _innateCompass;
        bool _secretRadar;
        bool _healthBars;
        bool _damageNumbers;
        bool _bossRetry;
        bool _equipAnywhere;
        bool _charmCostsFree;
        bool _unlimitedNotches;
        bool _geoMagnet;
        bool _keepGeoOnDeath;
        bool _journalOneKill;
        bool _companionBackdropBlack;
        bool _oneHitKills;
        int _geoMultiplier = 1;
        HollowKnightFlashMode _lifebloodFlash = HollowKnightFlashMode.Vanilla;
        int _stateSlot = 1;
        readonly TweakDeferredOperation _benchOperation =
            new TweakDeferredOperation("bench_teleport", 120f);
        long _benchRequestToken;
        int _benchBindingGeneration;
        readonly TweakDeferredOperation _stateLoadOperation =
            new TweakDeferredOperation("load_from_slot", 30f);
        readonly System.Collections.Generic.Queue<TweakAdapterCompletion> _completedOperations =
            new System.Collections.Generic.Queue<TweakAdapterCompletion>();

        public bool IsReady => true;

        // The weaver publishes this immutable game-assembly proof only after all
        // nine typed call sites pass preflight. Resolve once per API generation;
        // no IL inspection, process scanning, or healthy-frame capability polling.
        static bool ReadVerifiedHookCapability()
        {
            try
            {
                System.Type gate = typeof(global::PlayerData).Assembly.GetType(
                    "DualSoulsHollowKnightHookGate", false);
                if (gate == null || !gate.IsPublic || !gate.IsAbstract || !gate.IsSealed)
                    return false;
                System.Reflection.MethodInfo proof = gate.GetMethod(
                    "GetVerifiedMask", System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static, null, System.Type.EmptyTypes, null);
                return proof != null && !proof.IsGenericMethod && proof.ReturnType == typeof(int) &&
                    (int)proof.Invoke(null, null) == 63;
            }
            catch { return false; }
        }

        public bool IsHookAvailable(string id)
        {
            switch (id)
            {
                case "damage_cap": case "one_hit_kills": case "keep_geo_on_death":
                case "journal_one_kill": case "geo_multiplier": case "auto_map":
                    return _verifiedHooks;
                default: return true;
            }
        }

        void RequireHook(string id)
        {
            if (!IsHookAvailable(id))
                throw new System.InvalidOperationException(
                    "The required Hollow Knight native gameplay weave is unavailable for " + id + ".");
        }

        public void CaptureBaseline()
        {
            _captured = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureStateTransferPreparation();
#endif
        }

        public void RestoreBaseline()
        {
            if (!_captured) return;
            RestoreDamageMode();
            RestoreNailDamage();
            RestoreOneHitKills();
            RestoreRunSpeed();
            RestoreUnlimitedSoul();
            _fastTransitions = false;
            _autoMap = false;
            _innateCompass = false;
            _secretRadar = false;
            _healthBars = false;
            _damageNumbers = false;
            _bossRetry = false;
            _equipAnywhere = false;
            _charmCostsFree = false;
            _unlimitedNotches = false;
            _geoMagnet = false;
            _keepGeoOnDeath = false;
            _journalOneKill = false;
            _companionBackdropBlack = false;
            _oneHitKills = false;
            _geoMultiplier = 1;
            _lifebloodFlash = HollowKnightFlashMode.Vanilla;
            _stateSlot = 1;
            _completedOperations.Clear();
            Enqueue(_benchOperation.Cancel(
                "Hollow Knight Bench Teleport was canceled by baseline restoration."));
            RetireBenchChooserAuthority();
            Enqueue(_stateLoadOperation.Cancel(
                "Hollow Knight save-state load was canceled by baseline restoration."));
            HollowKnightGameplayHooks.Reset();
#if UNITY_ANDROID && !UNITY_EDITOR
            HollowKnightGameplayFeatures.RestoreAll();
            HollowKnightGameplayFeatures.SetStateTransferPreparation(null);
#endif
            global::HkStageHooks.ClearPresentationOverrides();
        }

        public void OpenSkins()
        {
            global::HkStageHooks.OpenSkins();
        }

        public void SetCompanionBackdropBlack(bool black)
        {
            _companionBackdropBlack = black;
            global::HkStageHooks.SetBackdropOverride(black);
        }

        public void SetLifebloodFlash(HollowKnightFlashMode mode)
        {
            _lifebloodFlash = mode;
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
            if (enabled) RequireHook("one_hit_kills");
            _oneHitKills = enabled;
            HollowKnightOneHitDamagePatch.SetEnabled(enabled);
        }

        public void RestoreOneHitKills()
        {
            _oneHitKills = false;
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

        public void SetFastTransitions(bool enabled) { _fastTransitions = enabled; }
        public void SetAutoMap(bool enabled)
        {
            if (enabled) RequireHook("auto_map");
            _autoMap = enabled;
        }
        public void SetInnateCompass(bool enabled) { _innateCompass = enabled; }
        public void OpenBenchTeleport(long operationToken)
        {
            _benchOperation.Begin(operationToken, UnityEngine.Time.unscaledTime);
            _benchRequestToken = operationToken;
            try
            {
                _benchBindingGeneration = global::HkStageHooks.OpenBenchTeleport(
                    operationToken,
                    result => {
                        Enqueue(_benchOperation.Complete(operationToken, result));
                        if (_benchRequestToken == operationToken) {
                            _benchRequestToken = 0;
                            _benchBindingGeneration = 0;
                        }
                    });
            }
            catch
            {
                RetireBenchChooserAuthority();
                _benchOperation.Cancel(
                    "Hollow Knight Bench Teleport failed before it was accepted.");
                throw;
            }
        }
        public void SetSecretRadar(bool enabled) { _secretRadar = enabled; }
        public void SetDamageCap(bool enabled)
        {
            if (enabled) RequireHook("damage_cap");
            HollowKnightGameplayHooks.DamageCapEnabled = enabled;
        }
        public void SetEnemyHealthBars(bool enabled) { _healthBars = enabled; }
        public void SetDamageNumbers(bool enabled) { _damageNumbers = enabled; }
        public void SetBossRetry(bool enabled)
        {
            _bossRetry = enabled;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (enabled) EnsureStateTransferPreparation();
#endif
        }
        public void SetEquipAnywhere(bool enabled) { _equipAnywhere = enabled; }
        public void SetCharmCostsFree(bool enabled) { _charmCostsFree = enabled; }
        public void SetUnlimitedNotches(bool enabled) { _unlimitedNotches = enabled; }
        public void SetStateSlot(int slot)
        {
            if (slot < 1 || slot > 5) throw new System.ArgumentOutOfRangeException(nameof(slot));
            _stateSlot = slot;
        }
        public void SaveState()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureStateTransferPreparation();
            HollowKnightGameplayFeatures.SaveState(_stateSlot);
#else
            throw new System.InvalidOperationException("Save states require the Hollow Knight runtime.");
#endif
        }
        public void LoadState(long operationToken)
        {
            _stateLoadOperation.Begin(operationToken, UnityEngine.Time.unscaledTime);
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                EnsureStateTransferPreparation();
                int slot = _stateSlot;
                HollowKnightGameplayFeatures.LoadState(
                    slot,
                    scene => Enqueue(_stateLoadOperation.Complete(
                        operationToken,
                        TweakActionResult.Ok(TweakReadback.Text(
                            "slot " + slot + " loaded in " +
                            (scene ?? "unknown scene"))))));
#else
                throw new System.InvalidOperationException(
                    "Save states require the Hollow Knight runtime.");
#endif
            }
            catch
            {
                _stateLoadOperation.Cancel(
                    "Hollow Knight save-state load failed before it was accepted.");
                throw;
            }
        }
        public void DeleteState()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            HollowKnightGameplayFeatures.DeleteState(_stateSlot);
#else
            throw new System.InvalidOperationException("Save states require the Hollow Knight runtime.");
#endif
        }
        public void SetGeoMagnet(bool enabled) { _geoMagnet = enabled; }
        public void SetKeepGeoOnDeath(bool enabled)
        {
            if (enabled) RequireHook("keep_geo_on_death");
            _keepGeoOnDeath = enabled;
            HollowKnightGameplayHooks.KeepGeoEnabled = enabled;
            if (!enabled) HollowKnightGameplayHooks.CompleteDeathHandling();
        }
        public void SetJournalOneKill(bool enabled)
        {
            if (enabled) RequireHook("journal_one_kill");
            _journalOneKill = enabled;
            HollowKnightGameplayHooks.JournalOneKillEnabled = enabled;
        }
        public void SetGeoMultiplier(int multiplier)
        {
            if (multiplier != 1 && multiplier != 2 && multiplier != 3 && multiplier != 5)
                throw new System.ArgumentOutOfRangeException(nameof(multiplier));
            if (multiplier > 1) RequireHook("geo_multiplier");
            _geoMultiplier = multiplier;
            HollowKnightGameplayHooks.GeoMultiplier = multiplier;
        }

        public TweakActionResult Readback(string id)
        {
            if (!IsHookAvailable(id)) return TweakActionResult.Fail(
                "The required Hollow Knight native gameplay weave is unavailable for " + id + ".");
            switch (id)
            {
                case "companion_backdrop":
                    return Choice(_companionBackdropBlack ? "black" : "dimmed");
                case "run_speed":
                    return Choice(_runSpeedMultiplier == 1.5f ? "plus_50" :
                                  _runSpeedMultiplier == 1.25f ? "plus_25" : "vanilla");
                case "fast_transitions": return OnOff(_fastTransitions);
                case "auto_map": return OnOff(_autoMap);
                case "innate_compass": return OnOff(_innateCompass);
                case "secret_radar": return OnOff(_secretRadar);
                case "nail_damage": return Choice(MultiplierChoice(_nailMultiplier));
                case "damage_received":
                    return Choice(!_damageMode.HasValue ? "vanilla" :
                        _damageMode.Value == HollowKnightDamageMode.NoMaskLoss
                            ? "no_mask_loss"
                            : "invincible");
                case "damage_cap": return OnOff(HollowKnightGameplayHooks.DamageCapEnabled);
                case "one_hit_kills": return OnOff(_oneHitKills);
                case "unlimited_soul": return OnOff(_unlimitedSoul);
                case "health_bars": return OnOff(_healthBars);
                case "damage_numbers": return OnOff(_damageNumbers);
                case "boss_retry": return OnOff(_bossRetry);
                case "equip_anywhere": return OnOff(_equipAnywhere);
                case "charm_costs": return Choice(_charmCostsFree ? "free" : "vanilla");
                case "unlimited_notches": return OnOff(_unlimitedNotches);
                case "state_slot":
                    return Choice(_stateSlot.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                case "geo_magnet": return OnOff(_geoMagnet);
                case "keep_geo_on_death": return OnOff(_keepGeoOnDeath);
                case "journal_one_kill": return OnOff(_journalOneKill);
                case "geo_multiplier": return Choice(MultiplierChoice(_geoMultiplier));
                case "lifeblood_flash":
                    return Choice(_lifebloodFlash == HollowKnightFlashMode.Soft ? "soft" :
                                  _lifebloodFlash == HollowKnightFlashMode.Off ? "off" : "vanilla");
                case "save_to_slot":
#if UNITY_ANDROID && !UNITY_EDITOR
                    return TweakActionResult.Ok(TweakReadback.Boolean(
                        HollowKnightGameplayFeatures.StateExists(_stateSlot)));
#else
                    return TweakActionResult.Ok(TweakReadback.Integer(_stateSlot));
#endif
                case "delete_slot":
#if UNITY_ANDROID && !UNITY_EDITOR
                    return TweakActionResult.Ok(TweakReadback.Boolean(
                        !HollowKnightGameplayFeatures.StateExists(_stateSlot)));
#else
                    return TweakActionResult.Ok(TweakReadback.Integer(_stateSlot));
#endif
                default:
                    return TweakActionResult.Fail(
                        "No safe Hollow Knight runtime readback exists for " + id + ".");
            }
        }

        TweakAdapterCompletion? PollBenchOperation()
        {
            TweakAdapterCompletion? completion = _benchOperation.Poll(
                UnityEngine.Time.unscaledTime,
                "Hollow Knight Bench Teleport timed out before a destination completed.");
            if (completion.HasValue)
                RetireBenchChooserAuthority();
            return completion;
        }

        void RetireBenchChooserAuthority()
        {
            long token = _benchRequestToken;
            int generation = _benchBindingGeneration;
            _benchRequestToken = 0;
            _benchBindingGeneration = 0;
            global::HkStageHooks.CancelBenchTeleport(token, generation);
        }

        void Enqueue(TweakAdapterCompletion? completion)
        {
            if (completion.HasValue)
                _completedOperations.Enqueue(completion.Value);
        }

        public System.Collections.Generic.IReadOnlyList<TweakAdapterCompletion>
            DrainCompletedOperations()
        {
            if (_completedOperations.Count == 0)
                return System.Array.Empty<TweakAdapterCompletion>();
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
            Enqueue(PollBenchOperation());
            Enqueue(_stateLoadOperation.Poll(
                UnityEngine.Time.unscaledTime,
                "Hollow Knight save-state load timed out before scene completion."));
            MaintainDamageMode();
            MaintainNailDamage();
            MaintainRunSpeed();
            MaintainUnlimitedSoul();
#if UNITY_ANDROID && !UNITY_EDITOR
            HollowKnightGameplayFeatures.Tick(
                _fastTransitions, _autoMap, _innateCompass, _secretRadar,
                _healthBars, _damageNumbers, _bossRetry, _equipAnywhere,
                _charmCostsFree, _unlimitedNotches, _geoMagnet,
                _keepGeoOnDeath, _journalOneKill);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        void EnsureStateTransferPreparation()
        {
            HollowKnightGameplayFeatures.SetStateTransferPreparation(PrepareForStateTransfer);
        }

        void PrepareForStateTransfer()
        {
            RestoreDamageOwner();
            RestoreNailOwner();
            RestoreRunSpeedOwner();
            HollowKnightGameplayFeatures.RestorePlayerOwnedForStateTransfer();
        }
#endif

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

    public static class HollowKnightGameplayHooks
    {
        public static bool DamageCapEnabled { get; set; }
        public static bool KeepGeoEnabled { get; set; }
        public static bool JournalOneKillEnabled { get; set; }
        public static int GeoMultiplier { get; set; } = 1;

        public static void BeforeAuthoritativeMapBoolSet(global::PlayerData player, string field, bool value)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            HollowKnightGameplayFeatures.BeforeAuthoritativeMapBoolSet(player, field, value);
#endif
        }

        public static void BeginAuthoritativeMapUpdate(global::PlayerData player)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            HollowKnightGameplayFeatures.BeginAuthoritativeMapUpdate(player);
#endif
        }

        public static void EndAuthoritativeMapUpdate(global::PlayerData player)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            HollowKnightGameplayFeatures.EndAuthoritativeMapUpdate(player);
#endif
        }

        public static void BeforeTakeDamage(ref int damageAmount)
        {
            if (DamageCapEnabled && damageAmount > 1) damageAmount = 1;
        }

        public static void BeforeAddGeo(ref int amount)
        {
            if (amount <= 0 || GeoMultiplier <= 1) return;
            long scaled = (long)amount * GeoMultiplier;
            amount = scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }

        static bool _deathObserved;

        public static void BeforeJournalSetInt(global::PlayerData playerData, string fieldName, ref int value)
        {
            if (!JournalOneKillEnabled || playerData == null || value <= 0 ||
                string.IsNullOrEmpty(fieldName) || !fieldName.StartsWith("kills", System.StringComparison.Ordinal)) return;
            string killedField = "killed" + fieldName.Substring(5);
            if (playerData.GetBool(killedField)) value = 0;
        }

        public static bool DeathObserved => _deathObserved;

        public static void BeforeDeath()
        {
            if (KeepGeoEnabled) _deathObserved = true;
        }

        public static void CompleteDeathHandling()
        {
            _deathObserved = false;
        }

        public static void Reset()
        {
            DamageCapEnabled = false;
            KeepGeoEnabled = false;
            JournalOneKillEnabled = false;
            GeoMultiplier = 1;
            _deathObserved = false;
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
