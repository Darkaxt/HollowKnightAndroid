#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Reflection;
using DualSouls.Mods;
using UnityEngine;

namespace DualSouls.Mods.HollowKnight
{
    /// <summary>
    /// #25 Hollow Knight backports of Nexus Silksong mod 19 (Simple Cheats). Choice
    /// effects are overlays restored by <see cref="RestoreAll"/>; commands are
    /// deliberate, persistent save edits like the reference mod's buttons.
    /// </summary>
    internal sealed class HollowKnightCheats
    {
        static readonly string[] Stations =
        {
            "openedTown", "openedCrossroads", "openedGreenpath", "openedRuins1", "openedRuins2",
            "openedFungalWastes", "openedRoyalGardens", "openedRestingGrounds", "openedDeepnest",
            "openedStagNest", "openedHiddenStation",
        };

        static readonly string[,] Abilities =
        {
            { "ability_double_jump", "hasDoubleJump" },
            { "ability_dash", "hasDash" },
            { "ability_wall_jump", "hasWalljump" },
            { "ability_crystal_heart", "hasSuperDash" },
            { "ability_shade_cloak", "hasShadowDash" },
            { "ability_ismas_tear", "hasAcidArmour" },
            { "ability_dream_nail", "hasDreamNail" },
        };

        float _gameSpeed = 1f;
        bool _speedApplied;
        bool _airJump;
        global::PlayerData _airJumpPlayer;
        bool _airJumpBaseline;
        bool _quickAttacks;
        global::HeroController _quickHero;
        float[] _quickBaseline;
        // #42 assists.
        // Static so the QA assists report can read them; there is one cheat owner.
        static float _regenInterval;
        static int _regenHealth = -1;
        static float _regenAt;
        static bool _dashInvincible;
        static global::HeroController _dashHero;
        static bool _dashOwned;
        static int _dashFrames;
        static int _regenGrants;

        public void SetChoice(string id, string value)
        {
            switch (id)
            {
                case "game_speed":
                    _gameSpeed = value == "x0_5" ? 0.5f : value == "x0_75" ? 0.75f :
                        value == "x1_5" ? 1.5f : value == "x2" ? 2f : 1f;
                    if (_gameSpeed == 1f) RestoreSpeed(); else MaintainSpeed();
                    break;
                case "infinite_air_jump":
                    _airJump = value == "on";
                    if (_airJump) MaintainAirJump(); else RestoreAirJump();
                    break;
                case "quick_attacks":
                    _quickAttacks = value == "on";
                    if (_quickAttacks) MaintainQuickAttacks(); else RestoreQuickAttacks();
                    break;
                case "all_damage":
                    HollowKnightOneHitDamagePatch.DamageMultiplier = value == "x2" ? 2 : value == "x3" ? 3 : value == "x5" ? 5 : value == "x10" ? 10 : 1;
                    break;
                case HeroTint.Id:
                    _tint.Set(value);
                    break;
                case HeroTint.SceneId:
                    _tint.SetScene(value == "on");
                    break;
                case HollowKnightItemPicker.AmountId:
                    _picker.SetAmount(value);
                    break;
                case CombatOverlay.BossBarsId:
                    CombatOverlay.BossBars = value == "on";
                    break;
                case "health_regen":
                    _regenInterval = value == "fast" ? 3f : value == "slow" ? 8f : 0f;
                    _regenHealth = -1;
                    break;
                case "dash_invincibility":
                    _dashInvincible = value == "on";
                    MaintainDashInvincibility();
                    break;
                case "rebirth_in_scene":
                    HollowKnightGameplayFeatures.RebirthEnabled = value == "on";
                    break;
                default:
                    throw new ArgumentException("Not a Hollow Knight cheat choice: " + id);
            }
        }

        readonly HeroTint _tint = new HeroTint();
        readonly HollowKnightItemPicker _picker = new HollowKnightItemPicker();

        public void Tick()
        {
            _tint.Tick(global::HeroController.UnsafeInstance);
            MaintainSpeed();
            MaintainAirJump();
            MaintainQuickAttacks();
            MaintainRegeneration();
            MaintainDashInvincibility();
        }

        // #42: one mask back after a quiet spell. Scaled time, so pause stops the clock;
        // any change of health, including a hit, starts it again.
        void MaintainRegeneration()
        {
            if (_regenInterval <= 0f) return;
            global::HeroController hero = global::HeroController.UnsafeInstance;
            global::PlayerData data = global::PlayerData.instance;
            if (hero == null || data == null) return;
            int health = data.health;
            if (health <= 0 || health >= data.maxHealth || hero.cState.dead || health != _regenHealth)
            {
                _regenHealth = health;
                _regenAt = Time.time + _regenInterval;
                return;
            }
            if (Time.time < _regenAt) return;
            hero.AddHealth(1);
            _regenGrants++;
            _regenHealth = data.health;
            _regenAt = Time.time + _regenInterval;
        }

        // #42: Hollow Knight has no invincibility set, so the hero's own invulnerable
        // flag is held for the dash. It is taken only when the game had not already
        // set it, and given back only if it is still ours.
        void MaintainDashInvincibility()
        {
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (!ReferenceEquals(hero, _dashHero)) { ReleaseDashInvincibility(); _dashHero = hero; }
            if (hero == null) return;
            bool want = _dashInvincible && hero.cState.dashing && !hero.cState.dead;
            if (want && !_dashOwned && !hero.cState.invulnerable)
            {
                hero.cState.invulnerable = true;
                _dashOwned = true;
            }
            else if (!want && _dashOwned) ReleaseDashInvincibility();
            if (_dashOwned) _dashFrames++;
        }

        void ReleaseDashInvincibility()
        {
            if (_dashOwned && _dashHero != null) _dashHero.cState.invulnerable = false;
            _dashOwned = false;
        }

        internal static string AssistReport()
        {
            global::HeroController hero = global::HeroController.UnsafeInstance;
            return "regen=" + _regenInterval + " regenHealth=" + _regenHealth + " dash=" + _dashInvincible +
                " dashOwned=" + _dashOwned + " dashFrames=" + _dashFrames + " regenGrants=" + _regenGrants +
                " invulnerable=" + (hero != null && hero.cState.invulnerable);
        }

        public void RestoreAll()
        {
            _regenInterval = 0f;
            _regenHealth = -1;
            _dashInvincible = false;
            ReleaseDashInvincibility();
            HollowKnightGameplayFeatures.RebirthEnabled = false;
            _tint.Set("off");
            _tint.SetScene(false);
            _picker.RestoreAll();
            CombatOverlay.BossBars = false;
            HollowKnightOneHitDamagePatch.DamageMultiplier = 1;
            _gameSpeed = 1f;
            RestoreSpeed();
            _airJump = false;
            RestoreAirJump();
            _quickAttacks = false;
            RestoreQuickAttacks();
        }

        public void PrepareForStateTransfer()
        {
            ReleaseDashInvincibility();
            RestoreAirJump();
            RestoreQuickAttacks();
        }

        // TimeManager clamps computed scales above 1 and owns pause/hit-stop; only
        // the game's own normal-speed frames are adjusted.
        void MaintainSpeed()
        {
            if (_gameSpeed == 1f) return;
            if (Mathf.Approximately(Time.timeScale, 1f))
            {
                Time.timeScale = _gameSpeed;
                _speedApplied = true;
            }
        }

        void RestoreSpeed()
        {
            if (!_speedApplied) return;
            _speedApplied = false;
            global::TimeManager.TimeScale = global::TimeManager.TimeScale;
        }

        void MaintainAirJump()
        {
            if (!_airJump) return;
            global::PlayerData data = global::PlayerData.instance;
            if (data == null) return;
            if (!ReferenceEquals(_airJumpPlayer, data))
            {
                RestoreAirJump();
                _airJumpPlayer = data;
                _airJumpBaseline = data.infiniteAirJump;
            }
            data.infiniteAirJump = true;
        }

        void RestoreAirJump()
        {
            if (_airJumpPlayer != null) _airJumpPlayer.infiniteAirJump = _airJumpBaseline;
            _airJumpPlayer = null;
            _airJumpBaseline = false;
        }

        // Mod 19's quick-attack values, applied to the Knight's own timing fields.
        void MaintainQuickAttacks()
        {
            if (!_quickAttacks) return;
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (hero == null || ReferenceEquals(_quickHero, hero)) return;
            RestoreQuickAttacks();
            _quickHero = hero;
            _quickBaseline = new[]
            {
                hero.ATTACK_COOLDOWN_TIME, hero.ATTACK_COOLDOWN_TIME_CH, hero.ATTACK_DURATION,
                hero.ATTACK_DURATION_CH, hero.ATTACK_RECOVERY_TIME,
            };
            hero.ATTACK_COOLDOWN_TIME = 0.01f;
            hero.ATTACK_COOLDOWN_TIME_CH = 0.01f;
            hero.ATTACK_DURATION = 0.01f;
            hero.ATTACK_DURATION_CH = 0.01f;
            hero.ATTACK_RECOVERY_TIME = 0.01f;
        }

        void RestoreQuickAttacks()
        {
            global::HeroController hero = _quickHero;
            if (hero != null && _quickBaseline != null)
            {
                hero.ATTACK_COOLDOWN_TIME = _quickBaseline[0];
                hero.ATTACK_COOLDOWN_TIME_CH = _quickBaseline[1];
                hero.ATTACK_DURATION = _quickBaseline[2];
                hero.ATTACK_DURATION_CH = _quickBaseline[3];
                hero.ATTACK_RECOVERY_TIME = _quickBaseline[4];
            }
            _quickHero = null;
            _quickBaseline = null;
        }

        public TweakActionResult Run(string id)
        {
            if (id == "award_all_achievements" || id == "reset_achievements")
                return Achievements(id == "award_all_achievements");
            if (id.StartsWith("achievement_", StringComparison.Ordinal))
                return AchievementPicker(id);
            global::HeroController hero = global::HeroController.UnsafeInstance;
            global::PlayerData data = global::PlayerData.instance;
            if (hero == null || data == null)
                return TweakActionResult.Fail("Load a save before using " + id + ".");
            if (HollowKnightItemPicker.IsCommand(id)) return _picker.Run(id, data);
            switch (id)
            {
                case "refill_health":
                    hero.MaxHealth();
                    return Text("health " + data.health + "/" + data.maxHealth);
                case "max_health_up":
                case "max_health_down":
                {
                    int delta = id == "max_health_up" ? 1 : -1;
                    if (delta < 0 && data.maxHealthBase <= 1)
                        return TweakActionResult.Fail("Max health is already 1.");
                    // PlayerData's own path: no PROTECTED/MASKED achievement is awarded.
                    data.AddToMaxHealth(delta);
                    data.heartPieceMax = data.maxHealthBase >= data.maxHealthCap;
                    hero.MaxHealth();
                    global::PlayMakerFSM.BroadcastEvent(delta > 0 ? "MAX HP UP" : "MAX HP DOWN");
                    return Text("max health " + data.maxHealth);
                }
                case "add_geo":
                {
                    // An explicit grant is exact: the Geo multiplier applies to earned awards only.
                    int multiplier = HollowKnightGameplayHooks.GeoMultiplier;
                    HollowKnightGameplayHooks.GeoMultiplier = 1;
                    try { hero.AddGeo(1000); }
                    finally { HollowKnightGameplayHooks.GeoMultiplier = multiplier; }
                    return Text("geo " + data.geo);
                }
                case "unlock_stags":
                {
                    int opened = 0;
                    for (int index = 0; index < Stations.Length; index++)
                    {
                        if (!data.GetBool(Stations[index])) data.SetBool(Stations[index], true);
                        opened++;
                    }
                    if (!data.metStag) data.SetBool("metStag", true);
                    if (data.stationsOpened < opened) data.SetInt("stationsOpened", opened);
                    return Text(opened + " stag stations open");
                }
                case "unlock_maps":
                    return Text(UnlockMaps(data) + " maps unlocked");
                case "unlock_all_charms":
                {
                    int granted = 0;
                    for (int charm = 1; charm <= 40; charm++)
                    {
                        if (data.GetBool("gotCharm_" + charm)) continue;
                        data.SetBool("gotCharm_" + charm, true);
                        data.SetBool("newCharm_" + charm, true);
                        granted++;
                    }
                    for (int charm = 23; charm <= 25; charm++)
                        if (data.GetBool("brokenCharm_" + charm)) data.SetBool("brokenCharm_" + charm, false);
                    // Charm icons read these states: Kingsoul whole (not Void Heart) and
                    // Grimmchild at its first level, so both quests continue from there.
                    if (data.royalCharmState < 3) data.SetInt("royalCharmState", 3);
                    if (data.grimmChildLevel < 1) data.SetInt("grimmChildLevel", 1);
                    if (!data.hasCharm) data.SetBool("hasCharm", true);
                    data.CountCharms();
                    return Text(granted + " charms added, " + data.charmsOwned + "/40 owned");
                }
                default:
                    return ToggleAbility(id, data);
            }
        }

        // Achievements live in the platform's roaming shared data on Android (no
        // online subsystem); the game's own ResetAllAchievements only clears that
        // subsystem, so reset clears the local records directly.
        static int _achievement = -1;

        // #25 mod 19 picker: one achievement at a time, and the game's own popup setting.
        static TweakActionResult AchievementPicker(string id)
        {
            global::GameManager game = global::GameManager.instance;
            global::AchievementHandler handler = game == null ? null : game.achievementHandler;
            if (handler == null || handler.AchievementsList == null)
                return TweakActionResult.Fail("The achievement list is not loaded yet.");
            if (id == "achievement_popups")
            {
                global::GameSettings settings = game.gameSettings;
                settings.showNativeAchievementPopups = settings.showNativeAchievementPopups == 1 ? 0 : 1;
                settings.SaveGameOptionsSettings();
                return Text("achievement popups " + (settings.showNativeAchievementPopups == 1 ? "on" : "off"));
            }
            var list = new System.Collections.Generic.List<global::Achievement>(handler.AchievementsList.Achievements);
            if (list.Count == 0) return TweakActionResult.Fail("This game has no achievements loaded.");
            if (id == "achievement_award")
            {
                if (_achievement < 0 || _achievement >= list.Count) return TweakActionResult.Fail("Pick an achievement first with NEXT ACHIEVEMENT.");
            }
            else
            {
                int step = id == "achievement_next" ? 1 : -1;
                _achievement = _achievement < 0 ? (step > 0 ? 0 : list.Count - 1) : ((_achievement + step) % list.Count + list.Count) % list.Count;
            }
            global::Achievement achievement = list[_achievement];
            if (id == "achievement_award") handler.AwardAchievementToPlayer(achievement.key);
            string title = TeamCherry.Localization.Language.Get(achievement.localizedTitle, "Achievements");
            return Text((_achievement + 1) + "/" + list.Count + " " + title + ": " + (handler.AchievementWasAwarded(achievement.key) ? "unlocked" : "locked"));
        }

        static TweakActionResult Achievements(bool award)
        {
            global::GameManager game = global::GameManager.instance;
            global::AchievementHandler handler = game == null ? null : game.achievementHandler;
            if (handler == null || handler.AchievementsList == null)
                return TweakActionResult.Fail("The achievement list is not loaded yet.");
            global::Platform.ISharedData shared = global::Platform.Current.RoamingSharedData;
            if (award) handler.AwardAllAchievements();
            int unlocked = 0, total = 0;
            foreach (global::Achievement achievement in handler.AchievementsList.Achievements)
            {
                total++;
                if (!award && shared.HasKey(achievement.key)) shared.DeleteKey(achievement.key);
                if (handler.AchievementWasAwarded(achievement.key)) unlocked++;
            }
            shared.Save();
            return Text(unlocked + "/" + total + " achievements unlocked");
        }

        static TweakActionResult Text(string value) => TweakActionResult.Ok(TweakReadback.Text(value));

        static int UnlockMaps(global::PlayerData data)
        {
            int count = 0;
            if (!data.hasMap) { data.SetBool("hasMap", true); count++; }
            foreach (FieldInfo field in typeof(global::PlayerData).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType != typeof(bool) || field.Name.Length <= 3 ||
                    !field.Name.StartsWith("map", StringComparison.Ordinal) ||
                    !char.IsUpper(field.Name[3]) || field.Name == "mapAllRooms") continue;
                if ((bool)field.GetValue(data)) continue;
                data.SetBool(field.Name, true);
                count++;
            }
            return count;
        }

        static TweakActionResult ToggleAbility(string id, global::PlayerData data)
        {
            for (int index = 0; index < Abilities.GetLength(0); index++)
            {
                if (Abilities[index, 0] != id) continue;
                string field = Abilities[index, 1];
                bool next = !data.GetBool(field);
                data.SetBool(field, next);
                // Shade Cloak upgrades Mothwing Cloak, so granting it grants the dash.
                if (next && field == "hasShadowDash") data.SetBool("hasDash", true);
                return Text(field + " " + (next ? "on" : "off"));
            }
            return TweakActionResult.Fail("No Hollow Knight cheat command exists for " + id + ".");
        }
    }
}
#endif
