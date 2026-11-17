#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using DualSouls.Mods;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    /// <summary>
    /// #25 cheat parity with Nexus mod 19 (Simple Cheats). Choice rows are overlays
    /// restored by <see cref="RestoreAll"/>; command rows are deliberate, persistent
    /// save edits, exactly like the reference mod's buttons.
    /// </summary>
    internal sealed class SilksongCheats
    {
        // Mod 19's display name -> the game's ToolItem name, minus Compass, which
        // the existing INNATE COMPASS row already forces through the same hook.
        internal static readonly string[,] ActiveTools =
        {
            { "active_magnetite_brooch", "Rosary Magnet" },
            { "active_shard_pendant", "Bone Necklace" },
            { "active_weighted_belt", "Weighted Anklet" },
            { "active_barbed_bracelet", "Barbed Wire" },
            { "active_dead_bugs_purse", "Dead Mans Purse" },
            { "active_shell_satchel", "Shell Satchel" },
            { "active_magnetite_dice", "Magnetite Dice" },
            { "active_scuttlebrace", "Scuttlebrace" },
            { "active_ascendants_grip", "Wallcling" },
            { "active_spider_strings", "Musician Charm" },
            { "active_silkspeed_anklets", "Sprintmaster" },
            { "active_thiefs_mark", "Thief Charm" },
            { "active_longclaw", "Longneedle" },
            { "active_pin_badge", "Pinstress Tool" },
            { "active_quick_sling", "Quick Sling" },
            { "active_weavelight", "White Ring" },
            { "active_warding_bell", "Bell Bind" },
        };

        static readonly string[] Stations =
        {
            "UnlockedFastTravel", "UnlockedDocksStation", "UnlockedBoneforestEastStation",
            "UnlockedGreymoorStation", "UnlockedBelltownStation", "UnlockedCoralTowerStation",
            "UnlockedCityStation", "UnlockedPeakStation", "UnlockedShellwoodStation",
            "UnlockedShadowStation", "UnlockedAqueductStation", "UnlockedFastTravelTeleport",
            "UnlockedSongTube", "UnlockedUnderTube", "UnlockedCityBellwayTube",
            "UnlockedHangTube", "UnlockedEnclaveTube", "UnlockedArboriumTube",
        };

        // Reference values from mod 19's quick-attack toggle.
        static readonly string[] QuickFields =
        {
            "canTurnWhileSlashing", "attackCooldownTime", "attackDuration", "attackRecoveryTime",
            "quickAttackCooldownTime", "quickAttackSpeedMult", "rageAttackCooldownTime",
            "rageAttackDuration", "rageQuickAttackCooldownTime",
        };
        static readonly object[] QuickValues =
        {
            true, 0.01f, 0.01f, 0.01f, 0.01f, 99f, 0.01f, 0.01f, 0.001f,
        };

        // Forced-equipped tools, consulted from the woven ToolItem.IsEquipped hook.
        // Reference comparison only: this runs on a hot path and must not allocate.
        static ToolItem[] _forcedTools = new ToolItem[0];

        readonly Dictionary<string, string> _choices = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<HeroControllerConfig, object[]> _quickOriginals =
            new Dictionary<HeroControllerConfig, object[]>();
        static readonly Dictionary<Type, FieldInfo[]> QuickFieldCache = new Dictionary<Type, FieldInfo[]>();

        float _gameSpeed = 1f;
        bool _speedApplied;
        bool _airJump;
        PlayerData _airJumpPlayer;
        bool _airJumpBaseline;
        bool _quickAttacks;
        bool _infiniteTools;
        bool _infiniteToolsBaseline;
        bool _captured;

        internal static bool IsForcedTool(ToolItem tool)
        {
            ToolItem[] forced = _forcedTools;
            for (int index = 0; index < forced.Length; index++)
                if (ReferenceEquals(forced[index], tool)) return true;
            return false;
        }

        public void Capture()
        {
            if (_captured) return;
            _infiniteToolsBaseline = ToolItemManager.IsInfiniteToolUseEnabled;
            _captured = true;
        }

        public bool IsChoice(string id) =>
            id == "game_speed" || id == "infinite_air_jump" || id == "quick_attacks" || id == "all_damage" ||
            id == "infinite_tools" || id == HeroTint.Id || id == HeroTint.SceneId || id == SilksongItemPickers.AmountId || id == CombatOverlay.BossBarsId || id.StartsWith("active_", StringComparison.Ordinal);

        public void SetChoice(string id, string value)
        {
            switch (id)
            {
                case "game_speed":
                    _gameSpeed = SpeedFor(value);
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
                case "infinite_tools":
                    _infiniteTools = value == "on";
                    ToolItemManager.IsInfiniteToolUseEnabled = _infiniteTools || _infiniteToolsBaseline;
                    break;
                case "all_damage":
                    SilksongGameplayHooks.AllDamageMultiplier = value == "x2" ? 2 : value == "x3" ? 3 : value == "x5" ? 5 : value == "x10" ? 10 : 1;
                    break;
                case HeroTint.Id:
                    _tint.Set(value);
                    break;
                case HeroTint.SceneId:
                    _tint.SetScene(value == "on");
                    break;
                case SilksongItemPickers.AmountId:
                    _pickers.SetAmount(value);
                    break;
                case CombatOverlay.BossBarsId:
                    CombatOverlay.BossBars = value == "on";
                    break;
                default:
                    SetActiveTool(id, value == "on");
                    break;
            }
            _choices[id] = value;
        }

        public TweakActionResult Readback(string id)
        {
            string value;
            if (!_choices.TryGetValue(id, out value))
                value = id == "game_speed" ? "vanilla" : id == SilksongItemPickers.AmountId ? "1" : "off";
            return TweakActionResult.Ok(TweakReadback.Choice(value));
        }

        readonly HeroTint _tint = new HeroTint();
        readonly SilksongItemPickers _pickers = new SilksongItemPickers();

        public void Tick()
        {
            _tint.Tick(HeroController.SilentInstance);
            MaintainSpeed();
            MaintainAirJump();
            MaintainQuickAttacks();
        }

        public void RestoreAll()
        {
            _tint.Set("off");
            _tint.SetScene(false);
            _pickers.RestoreAll();
            CombatOverlay.BossBars = false;
            SilksongGameplayHooks.AllDamageMultiplier = 1;
            _gameSpeed = 1f;
            RestoreSpeed();
            _airJump = false;
            RestoreAirJump();
            _quickAttacks = false;
            RestoreQuickAttacks();
            if (_captured) ToolItemManager.IsInfiniteToolUseEnabled = _infiniteToolsBaseline;
            _infiniteTools = false;
            bool hadForced = _forcedTools.Length != 0;
            _forcedTools = new ToolItem[0];
            if (hadForced) RefreshEquips();
            _choices.Clear();
        }

        // Runs before a save-state transfer so the captured state holds no overlay.
        public void PrepareForStateTransfer()
        {
            RestoreAirJump();
            RestoreQuickAttacks();
        }

        static float SpeedFor(string value) =>
            value == "x0_5" ? 0.5f : value == "x0_75" ? 0.75f :
            value == "x1_5" ? 1.5f : value == "x2" ? 2f : 1f;

        // TimeManager clamps any computed scale above 1 and owns pause/hit-stop.
        // Adjust only the game's own normal-speed frames, so pause (0) and
        // hit-stop values keep their native meaning.
        void MaintainSpeed()
        {
            if (_gameSpeed == 1f) return;
            float current = Time.timeScale;
            if (Mathf.Approximately(current, 1f))
            {
                Time.timeScale = _gameSpeed;
                _speedApplied = true;
            }
        }

        void RestoreSpeed()
        {
            if (!_speedApplied) return;
            _speedApplied = false;
            // Re-run the game's own computation; it rewrites Time.timeScale when ours differs.
            TimeManager.TimeScale = TimeManager.TimeScale;
        }

        void MaintainAirJump()
        {
            if (!_airJump) return;
            PlayerData data = PlayerData.HasInstance ? PlayerData.instance : null;
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

        void MaintainQuickAttacks()
        {
            if (!_quickAttacks) return;
            HeroController hero = HeroController.instance;
            HeroControllerConfig config = hero != null ? hero.Config : null;
            if (config == null || _quickOriginals.ContainsKey(config)) return;
            FieldInfo[] fields = QuickFieldsFor(config.GetType());
            var originals = new object[fields.Length];
            for (int index = 0; index < fields.Length; index++)
            {
                if (fields[index] == null) continue;
                originals[index] = fields[index].GetValue(config);
                fields[index].SetValue(config, QuickValues[index]);
            }
            _quickOriginals.Add(config, originals);
        }

        void RestoreQuickAttacks()
        {
            foreach (KeyValuePair<HeroControllerConfig, object[]> entry in _quickOriginals)
            {
                if (entry.Key == null) continue;
                FieldInfo[] fields = QuickFieldsFor(entry.Key.GetType());
                for (int index = 0; index < fields.Length; index++)
                    if (fields[index] != null) fields[index].SetValue(entry.Key, entry.Value[index]);
            }
            _quickOriginals.Clear();
        }

        static FieldInfo[] QuickFieldsFor(Type type)
        {
            FieldInfo[] fields;
            if (QuickFieldCache.TryGetValue(type, out fields)) return fields;
            fields = new FieldInfo[QuickFields.Length];
            for (int index = 0; index < QuickFields.Length; index++)
            {
                for (Type owner = type; owner != null && fields[index] == null; owner = owner.BaseType)
                    fields[index] = owner.GetField(QuickFields[index],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (fields[index] != null && fields[index].FieldType != QuickValues[index].GetType())
                    fields[index] = null;
            }
            QuickFieldCache.Add(type, fields);
            return fields;
        }

        void SetActiveTool(string id, bool enabled)
        {
            string toolName = null;
            for (int index = 0; index < ActiveTools.GetLength(0); index++)
                if (ActiveTools[index, 0] == id) toolName = ActiveTools[index, 1];
            if (toolName == null) throw new ArgumentException("Unknown always-active tool row " + id + ".");
            ToolItem tool = ToolItemManager.GetToolByName(toolName);
            if (tool == null) throw new InvalidOperationException(toolName + " is not available in this game build.");
            var next = new List<ToolItem>(_forcedTools);
            next.Remove(tool);
            if (enabled) next.Add(tool);
            _forcedTools = next.ToArray();
            RefreshEquips();
        }

        static void RefreshEquips()
        {
            ToolItemManager.SendEquippedChangedEvent(true);
        }

        // ---- Commands (persistent save edits, mirroring mod 19's buttons) ----

        // Achievements live in the platform's roaming shared data on Android (no
        // online subsystem); the game's own ResetAllAchievements only clears that
        // subsystem, so reset clears the local records directly.
        static TweakActionResult Achievements(bool award)
        {
            GameManager game = GameManager.instance;
            AchievementHandler handler = game == null ? null : game.achievementHandler;
            if (handler == null || handler.AchievementsList == null)
                return TweakActionResult.Fail("The achievement list is not loaded yet.");
            Platform.ISharedData shared = Platform.Current.RoamingSharedData;
            if (award) handler.AwardAllAchievements();
            int unlocked = 0, total = 0;
            foreach (Achievement achievement in handler.AchievementsList.Achievements)
            {
                total++;
                if (!award && shared.HasKey(achievement.PlatformKey)) shared.DeleteKey(achievement.PlatformKey);
                if (handler.AchievementWasAwarded(achievement.PlatformKey)) unlocked++;
            }
            shared.Save();
            return Text(unlocked + "/" + total + " achievements unlocked");
        }

        public bool IsCommand(string id)
        {
            switch (id)
            {
                case "refill_health": case "max_health_up": case "max_health_down":
                case "add_rosaries": case "add_shards": case "refill_tools":
                case "unlock_all_tools": case "unlock_all_crests": case "unlock_fast_travel":
                case "unlock_maps": case "max_collectables":
                case "award_all_achievements": case "reset_achievements":
                    return true;
                default:
                    return id.StartsWith("ability_", StringComparison.Ordinal) || SilksongItemPickers.IsCommand(id);
            }
        }

        public TweakActionResult Run(string id)
        {
            if (id == "award_all_achievements" || id == "reset_achievements")
                return Achievements(id == "award_all_achievements");
            HeroController hero = HeroController.instance;
            PlayerData data = PlayerData.HasInstance ? PlayerData.instance : null;
            if (hero == null || data == null)
                return TweakActionResult.Fail("Load a save before using " + id + ".");
            if (SilksongItemPickers.IsCommand(id)) return _pickers.Run(id);
            switch (id)
            {
                case "refill_health":
                    hero.RefillHealthToMax();
                    return Text("health " + data.health + "/" + data.CurrentMaxHealth);
                case "max_health_up":
                case "max_health_down":
                {
                    int delta = id == "max_health_up" ? 1 : -1;
                    if (delta < 0 && data.maxHealthBase <= 1)
                        return TweakActionResult.Fail("Max health is already 1.");
                    // PlayerData's own path: no FIRST_MASK/ALL_MASKS achievement is queued.
                    data.AddToMaxHealth(delta);
                    hero.RefillHealthToMax();
                    return Text("max health " + data.CurrentMaxHealth);
                }
                // CurrencyManager queues the change and applies it on its next Update, so the
                // balance read here is still the old one.
                case "add_rosaries":
                    AddCurrency(1000, CurrencyType.Money);
                    return Text("+1000 rosaries (had " + CurrencyManager.GetCurrencyAmount(CurrencyType.Money) + ")");
                case "add_shards":
                    AddCurrency(100, CurrencyType.Shard);
                    return Text("+100 shards (had " + CurrencyManager.GetCurrencyAmount(CurrencyType.Shard) + ")");
                case "refill_tools":
                    return Text("refilled " + RefillTools() + " tools");
                case "unlock_all_tools":
                    ToolItemManager.UnlockAllTools();
                    RefreshEquips();
                    return Text("tools unlocked");
                case "unlock_all_crests":
                    ToolItemManager.UnlockAllCrests();
                    RefreshEquips();
                    return Text("crests unlocked");
                case "unlock_fast_travel":
                    for (int index = 0; index < Stations.Length; index++) data.SetBool(Stations[index], true);
                    return Text(Stations.Length + " stations unlocked");
                case "unlock_maps":
                    return Text(UnlockMaps(data) + " maps unlocked");
                case "max_collectables":
                    return Text(MaxCollectables() + " collectables filled");
                default:
                    return ToggleAbility(id, data);
            }
        }

        static TweakActionResult Text(string value) => TweakActionResult.Ok(TweakReadback.Text(value));

        static void AddCurrency(int amount, CurrencyType type)
        {
            // An explicit grant is exact: the Rosary multiplier applies to earned awards only.
            int multiplier = SilksongGameplayHooks.RosaryMultiplier;
            SilksongGameplayHooks.RosaryMultiplier = 1;
            try { CurrencyManager.ChangeCurrency(amount, type, true); }
            finally { SilksongGameplayHooks.RosaryMultiplier = multiplier; }
        }

        static int RefillTools()
        {
            ToolItemsData tools = PlayerData.instance.Tools;
            int count = 0;
            foreach (ToolItem tool in ToolItemManager.GetUnlockedTools())
            {
                ToolItemsData.Data saved = tools.GetData(tool.name);
                int storage = ToolItemManager.GetToolStorageAmount(tool);
                if (saved.AmountLeft >= storage) continue;
                saved.AmountLeft = storage;
                tools.SetData(tool.name, saved);
                count++;
            }
            ToolItemManager.ReportAllBoundAttackToolsUpdated();
            return count;
        }

        static int UnlockMaps(PlayerData data)
        {
            int count = 0;
            foreach (FieldInfo field in typeof(PlayerData).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType != typeof(bool) || !field.Name.StartsWith("Has", StringComparison.Ordinal) ||
                    !field.Name.EndsWith("Map", StringComparison.Ordinal) || field.Name == "HasAnyMap" ||
                    field.Name == "HasAllMaps") continue;
                if ((bool)field.GetValue(data)) continue;
                data.SetBool(field.Name, true);
                count++;
            }
            return count;
        }

        static int MaxCollectables()
        {
            CollectableItemManager manager = ManagerSingleton<CollectableItemManager>.Instance;
            if (manager == null) throw new InvalidOperationException("Collectables are not loaded.");
            int count = 0;
            foreach (CollectableItem item in manager.GetAllCollectables())
            {
                // Mod 19 skips Growstone; it is consumed by its own quest flow.
                if (item == null || item.name == "Growstone" || item.IsAtMax()) continue;
                item.Collect(99, false);
                count++;
            }
            return count;
        }

        static readonly string[,] Abilities =
        {
            { "ability_double_jump", "hasDoubleJump" },
            { "ability_dash", "hasDash" },
            { "ability_wall_jump", "hasWalljump" },
            { "ability_glide", "hasBrolly" },
            { "ability_charge_attack", "hasChargeSlash" },
            { "ability_needolin", "hasNeedolin" },
            { "ability_clawline", "hasHarpoonDash" },
            { "ability_super_jump", "hasSuperJump" },
        };

        static TweakActionResult ToggleAbility(string id, PlayerData data)
        {
            for (int index = 0; index < Abilities.GetLength(0); index++)
            {
                if (Abilities[index, 0] != id) continue;
                string field = Abilities[index, 1];
                bool next = !data.GetBool(field);
                data.SetBool(field, next);
                // Silk Soar launches from Clawline, as in mod 19.
                if (next && field == "hasSuperJump") data.SetBool("hasHarpoonDash", true);
                return Text(field + " " + (next ? "on" : "off"));
            }
            return TweakActionResult.Fail("No Silksong cheat command exists for " + id + ".");
        }
    }
}
#endif
