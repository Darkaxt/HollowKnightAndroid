using System;
using System.Collections.Generic;
using DualSouls.Mods;

namespace DualSouls.Mods.Silksong
{
    public enum SilksongDamageMode { PreventDeath, Invincible }

    /// <summary>
    /// Tracks the Mods choices applied over the captured runtime baseline. Readback
    /// must describe the requested overlay even when that baseline already had the
    /// same cheat enabled before Mods captured it.
    /// </summary>
    internal sealed class SilksongAppliedChoiceState
    {
        SilksongDamageMode? _damageMode;
        bool _oneHitKills;
        bool _equipAnywhere;
        bool _instantDialogue;
        bool _worldRumbleDisabled;
        bool _frostDisabled;

        public void SetDamageMode(SilksongDamageMode mode) => _damageMode = mode;
        public void RestoreDamageMode() => _damageMode = null;
        public void SetOneHitKills(bool enabled) => _oneHitKills = enabled;
        public void SetEquipAnywhere(bool enabled) => _equipAnywhere = enabled;
        public void SetInstantDialogue(bool enabled) => _instantDialogue = enabled;
        public void SetWorldRumbleDisabled(bool enabled) => _worldRumbleDisabled = enabled;
        public void SetFrostDisabled(bool enabled) => _frostDisabled = enabled;

        public TweakActionResult Readback(string id)
        {
            switch (id)
            {
                case "damage_received":
                    return Choice(!_damageMode.HasValue ? "vanilla" :
                        _damageMode.Value == SilksongDamageMode.PreventDeath
                            ? "prevent_death"
                            : "invincible");
                case "one_hit_kills": return OnOff(_oneHitKills);
                case "equip_anywhere": return OnOff(_equipAnywhere);
                case "instant_dialogue": return OnOff(_instantDialogue);
                case "disable_world_rumble": return OnOff(_worldRumbleDisabled);
                case "ignore_frost_slowdown": return OnOff(_frostDisabled);
                default:
                    return TweakActionResult.Fail(
                        "No applied Silksong choice state exists for " + id + ".");
            }
        }

        static TweakActionResult Choice(string value) =>
            TweakActionResult.Ok(TweakReadback.Choice(value));
        static TweakActionResult OnOff(bool enabled) =>
            Choice(enabled ? "on" : "off");
    }

    public interface ISilksongTweakApi
    {
        bool IsReady { get; }
        void CaptureBaseline();
        void RestoreBaseline();
        void OpenSkins();
        void SetCompanionBackdropBlack(bool black);
        void SetRunSpeedMultiplier(float multiplier);
        void RestoreRunSpeed();
        void SetFastTransitions(bool enabled);
        void SetAutoMap(bool enabled);
        void SetInnateCompass(bool enabled);
        void OpenBenchTeleport(long operationToken);
        void SetSecretRadar(bool enabled);
        void SetNeedleDamageMultiplier(int multiplier);
        void RestoreNeedleDamage();
        void SetDamageMode(SilksongDamageMode mode);
        void RestoreDamageMode();
        void SetDamageCap(bool enabled);
        void SetOneHitKills(bool enabled);
        void RestoreOneHitKills();
        void SetUnlimitedSilk(bool enabled);
        void RestoreUnlimitedSilk();
        void SetEnemyHealthBars(bool enabled);
        void SetDamageNumbers(bool enabled);
        void SetBossRetry(bool enabled);
        void SetEquipAnywhere(bool enabled);
        void RestoreEquipAnywhere();
        void SetToolCostsFree(bool enabled);
        void SetUnlimitedToolSlots(bool enabled);
        void SetStateSlot(int slot);
        void SaveState();
        void LoadState(long operationToken);
        void DeleteState();
        void SetRosaryMagnet(bool enabled);
        void SetKeepRosariesOnDeath(bool enabled);
        void SetJournalOneKill(bool enabled);
        void SetRosaryMultiplier(int multiplier);
        void SetInstantDialogue(bool enabled);
        void RestoreInstantDialogue();
        void SetWorldRumbleDisabled(bool enabled);
        void RestoreWorldRumbleDisabled();
        void SetFrostDisabled(bool enabled);
        void RestoreFrostDisabled();
        void TickGameplay();
        TweakActionResult Readback(string id);
        IReadOnlyList<TweakAdapterCompletion> DrainCompletedOperations();
    }

    /// <summary>Silksong adapter for the shared built-in Mods contract.</summary>
    public sealed class SilksongTweakAdapter : ITweakAdapter, ITweakOperationAdapter
    {
        static readonly IReadOnlyList<TweakDescriptor> Rows = Array.AsReadOnly(new[]
        {
            Row("black_background", "black_background", TweakControlKind.Choice, "GENERAL", "BLACK BACKGROUND", "Use a black lower-screen background instead of the dimmed scenery wash.", "off", "off", "on"),

            Row("run_speed", "run_speed", TweakControlKind.Choice, "WORLD", "RUN SPEED", "Choose Hornet's normal, +25%, or +50% movement pace.", "vanilla", "vanilla", "plus_25", "plus_50"),
            Row("fast_transitions", "fast_transitions", TweakControlKind.Choice, "WORLD", "FAST TRANSITIONS", "Shorten supported scene transitions.", "off", "off", "on"),
            Row("auto_map", "auto_map", TweakControlKind.Choice, "WORLD", "AUTO MAP", "Reveal visited rooms on the map automatically.", "off", "off", "on"),
            Row("innate_compass", "innate_compass", TweakControlKind.Choice, "WORLD", "INNATE COMPASS", "Show Hornet on the map without requiring a compass tool.", "off", "off", "on"),
            Row("bench_teleport", "bench_teleport", TweakControlKind.Route, "WORLD", "BENCH TELEPORT", "Open the recorded-bench destination list.", "open", "open"),
            Row("secret_radar", "secret_radar", TweakControlKind.Choice, "WORLD", "SECRET RADAR", "Signal nearby secrets without changing progression.", "off", "off", "on"),

            Row("nail_damage", "nail_damage", TweakControlKind.Choice, "COMBAT", "NEEDLE DAMAGE", "Multiply Needle damage while preserving upgrades.", "x1", "x1", "x2", "x3", "x5"),
            Row("damage_received", "damage_taken", TweakControlKind.Choice, "COMBAT", "DAMAGE TAKEN", "Choose normal damage, prevent death, or full invincibility.", "vanilla", "vanilla", "prevent_death", "invincible"),
            Row("damage_cap", "damage_cap", TweakControlKind.Choice, "COMBAT", "DAMAGE CAP", "Limit damage received from a single hit.", "off", "off", "on"),
            Row("one_hit_kills", "one_hit_kills", TweakControlKind.Choice, "COMBAT", "ONE-HIT KILLS", "Use Silksong's managed instant-kill damage state.", "off", "off", "on"),
            Row("unlimited_silk", "unlimited_soul", TweakControlKind.Choice, "COMBAT", "UNLIMITED SILK", "Keep Silk available using Silksong's own drain and refill paths.", "off", "off", "on"),

            Row("health_bars", "enemy_health_bars", TweakControlKind.Choice, "ENCOUNTERS", "ENEMY HEALTH BARS", "Show health bars for eligible enemies and bosses.", "off", "off", "on"),
            Row("damage_numbers", "damage_numbers", TweakControlKind.Choice, "ENCOUNTERS", "DAMAGE NUMBERS", "Show the damage dealt by supported attacks.", "off", "off", "on"),
            Row("boss_retry", "boss_retry", TweakControlKind.Choice, "ENCOUNTERS", "BOSS RETRY", "Retry supported boss encounters from a safe checkpoint.", "off", "off", "on"),

            Row("equip_anywhere", "equip_anywhere", TweakControlKind.Choice, "CRESTS & TOOLS", "EQUIP ANYWHERE", "Allow Crest and Tool changes away from benches.", "off", "off", "on"),
            Row("charm_costs", "charm_costs", TweakControlKind.Choice, "CRESTS & TOOLS", "TOOL COSTS", "Remove Tool replenishment costs while enabled.", "vanilla", "vanilla", "free"),
            Row("unlimited_notches", "unlimited_notches", TweakControlKind.Choice, "CRESTS & TOOLS", "UNLIMITED TOOL SLOTS", "Equip Tools without the normal Crest slot limit.", "off", "off", "on"),

            Row("state_slot", "state_slot", TweakControlKind.Choice, "SAVE STATES", "SLOT", "Choose the save-state slot used by the commands below.", "1", "1", "2", "3", "4", "5"),
            Row("save_to_slot", "save_to_slot", TweakControlKind.Command, "SAVE STATES", "SAVE TO SLOT", "Capture the current state in the selected slot.", "run", "run"),
            Row("load_from_slot", "load_from_slot", TweakControlKind.Command, "SAVE STATES", "LOAD FROM SLOT", "Restore the state stored in the selected slot.", "run", "run"),
            Row("delete_slot", "delete_slot", TweakControlKind.Command, "SAVE STATES", "DELETE SLOT", "Delete the state stored in the selected slot.", "run", "run"),

            Row("geo_magnet", "geo_magnet", TweakControlKind.Choice, "ECONOMY", "ROSARY MAGNET", "Collect nearby Rosaries without requiring a Tool.", "off", "off", "on"),
            Row("keep_geo_on_death", "keep_geo_on_death", TweakControlKind.Choice, "ECONOMY", "KEEP ROSARIES ON DEATH", "Keep Rosaries through death without duplicate recovery awards.", "off", "off", "on"),
            Row("journal_one_kill", "journal_one_kill", TweakControlKind.Choice, "ECONOMY", "JOURNAL IN ONE KILL", "Complete eligible Journal entries after one kill.", "off", "off", "on"),
            Row("geo_multiplier", "geo_multiplier", TweakControlKind.Choice, "ECONOMY", "ROSARY MULTIPLIER", "Multiply supported Rosary awards.", "x1", "x1", "x2", "x3", "x5"),

            Row("instant_dialogue", "instant_dialogue", TweakControlKind.Choice, "PRESENTATION", "INSTANT DIALOGUE", "Show dialogue text immediately instead of printing it over time.", "off", "off", "on"),
            Row("disable_world_rumble", "disable_world_rumble", TweakControlKind.Choice, "PRESENTATION", "DISABLE WORLD RUMBLE", "Prevent ambient world rumble effects.", "off", "off", "on"),
            Row("ignore_frost_slowdown", "ignore_frost_slowdown", TweakControlKind.Choice, "PLAYER", "IGNORE FROST SLOWDOWN", "Prevent frost buildup from slowing Hornet.", "off", "off", "on"),
        });

        readonly struct PendingChoice
        {
            public PendingChoice(string id, long operationToken)
            {
                Id = id;
                OperationToken = operationToken;
            }

            public string Id { get; }
            public long OperationToken { get; }
        }

        readonly ISilksongTweakApi _api;
        readonly List<PendingChoice> _pendingChoiceReadbacks = new List<PendingChoice>();
        long _nextOperationToken;
        public SilksongTweakAdapter(ISilksongTweakApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));
        public string GameId => "silksong";
        public IReadOnlyList<TweakDescriptor> Descriptors => Rows;
        public void CaptureBaseline() => _api.CaptureBaseline();
        public void RestoreBaseline()
        {
            _pendingChoiceReadbacks.Clear();
            _api.RestoreBaseline();
        }
        public void Tick() => _api.TickGameplay();
        public TweakActionResult Readback(string id) => _api.Readback(id);

        public IReadOnlyList<TweakAdapterCompletion> TickWithOutcomes()
        {
            _api.TickGameplay();
            IReadOnlyList<TweakAdapterCompletion> runtime =
                _api.DrainCompletedOperations() ?? Array.Empty<TweakAdapterCompletion>();
            int completionCount = runtime.Count + _pendingChoiceReadbacks.Count;
            if (completionCount == 0)
                return Array.Empty<TweakAdapterCompletion>();

            var completed = new TweakAdapterCompletion[completionCount];
            int completedIndex = 0;
            for (int index = 0; index < runtime.Count; index++)
                completed[completedIndex++] = runtime[index];

            for (int index = 0; index < _pendingChoiceReadbacks.Count; index++)
            {
                PendingChoice pending = _pendingChoiceReadbacks[index];
                TweakActionResult readback = _api.Readback(pending.Id);
                if (readback.Pending)
                    readback = TweakActionResult.Fail(
                        "Silksong runtime readback is still pending for " + pending.Id + ".");
                completed[completedIndex++] = new TweakAdapterCompletion(
                    pending.Id,
                    pending.OperationToken,
                    readback);
            }
            _pendingChoiceReadbacks.Clear();
            return completed;
        }

        public TweakActionResult Apply(string id, string value)
        {
            TweakDescriptor row = Find(id);
            if (row == null) return TweakActionResult.Fail("Unknown Silksong tweak: " + id);
            if (!row.Allows(value)) return TweakActionResult.Fail("Unsupported value for " + id + ": " + value);
            if (!_api.IsReady) return TweakActionResult.Fail("Silksong gameplay owners are not ready for Mods actions.");
            long operationToken = row.ControlKind == TweakControlKind.Choice ||
                                  row.ControlKind == TweakControlKind.Route ||
                                  id == "load_from_slot"
                ? NextOperationToken()
                : 0;
            try
            {
                switch (id)
                {
                    case "black_background": _api.SetCompanionBackdropBlack(On(value)); break;
                    case "run_speed": if (value == "vanilla") _api.RestoreRunSpeed(); else _api.SetRunSpeedMultiplier(value == "plus_25" ? 1.25f : 1.5f); break;
                    case "fast_transitions": _api.SetFastTransitions(On(value)); break;
                    case "auto_map": _api.SetAutoMap(On(value)); break;
                    case "innate_compass": _api.SetInnateCompass(On(value)); break;
                    case "bench_teleport": _api.OpenBenchTeleport(operationToken); break;
                    case "secret_radar": _api.SetSecretRadar(On(value)); break;
                    case "nail_damage": if (value == "x1") _api.RestoreNeedleDamage(); else _api.SetNeedleDamageMultiplier(Multiplier(value)); break;
                    case "damage_received": if (value == "vanilla") _api.RestoreDamageMode(); else _api.SetDamageMode(value == "prevent_death" ? SilksongDamageMode.PreventDeath : SilksongDamageMode.Invincible); break;
                    case "damage_cap": _api.SetDamageCap(On(value)); break;
                    case "one_hit_kills": if (On(value)) _api.SetOneHitKills(true); else _api.RestoreOneHitKills(); break;
                    case "unlimited_silk": if (On(value)) _api.SetUnlimitedSilk(true); else _api.RestoreUnlimitedSilk(); break;
                    case "health_bars": _api.SetEnemyHealthBars(On(value)); break;
                    case "damage_numbers": _api.SetDamageNumbers(On(value)); break;
                    case "boss_retry": _api.SetBossRetry(On(value)); break;
                    case "equip_anywhere": if (On(value)) _api.SetEquipAnywhere(true); else _api.RestoreEquipAnywhere(); break;
                    case "charm_costs": _api.SetToolCostsFree(value == "free"); break;
                    case "unlimited_notches": _api.SetUnlimitedToolSlots(On(value)); break;
                    case "state_slot": _api.SetStateSlot(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); break;
                    case "save_to_slot": _api.SaveState(); break;
                    case "load_from_slot": _api.LoadState(operationToken); break;
                    case "delete_slot": _api.DeleteState(); break;
                    case "geo_magnet": _api.SetRosaryMagnet(On(value)); break;
                    case "keep_geo_on_death": _api.SetKeepRosariesOnDeath(On(value)); break;
                    case "journal_one_kill": _api.SetJournalOneKill(On(value)); break;
                    case "geo_multiplier": _api.SetRosaryMultiplier(Multiplier(value)); break;
                    case "instant_dialogue": if (On(value)) _api.SetInstantDialogue(true); else _api.RestoreInstantDialogue(); break;
                    case "disable_world_rumble": if (On(value)) _api.SetWorldRumbleDisabled(true); else _api.RestoreWorldRumbleDisabled(); break;
                    case "ignore_frost_slowdown": if (On(value)) _api.SetFrostDisabled(true); else _api.RestoreFrostDisabled(); break;
                    default: return TweakActionResult.Fail("No Silksong dispatch exists for " + id + ".");
                }
                if (row.ControlKind == TweakControlKind.Choice)
                {
                    _pendingChoiceReadbacks.Add(new PendingChoice(id, operationToken));
                    return TweakActionResult.PendingResult(operationToken);
                }
                if (row.ControlKind == TweakControlKind.Route || id == "load_from_slot")
                    return TweakActionResult.PendingResult(operationToken);
                return _api.Readback(id);
            }
            catch (Exception e) { return TweakActionResult.Fail("Silksong rejected " + id + ": " + e.Message); }
        }

        long NextOperationToken()
        {
            _nextOperationToken++;
            if (_nextOperationToken <= 0) _nextOperationToken = 1;
            return _nextOperationToken;
        }

        static bool On(string value) => value == "on";
        static int Multiplier(string value) => value == "x2" ? 2 : value == "x3" ? 3 : value == "x5" ? 5 : 1;
        static TweakDescriptor Row(string id, string contract, TweakControlKind kind, string group, string title, string description, string defaultValue, params string[] values) =>
            new TweakDescriptor(id, contract, kind, group, title, description, defaultValue, values);
        static TweakDescriptor Find(string id)
        {
            for (int i = 0; i < Rows.Count; i++) if (Rows[i].Id == id) return Rows[i];
            return null;
        }
    }
}
