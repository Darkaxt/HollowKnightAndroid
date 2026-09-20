using System;
using System.Collections.Generic;
using DualSouls.Mods;

namespace DualSouls.Mods.Silksong
{
    public enum SilksongDamageMode
    {
        PreventDeath,
        Invincible,
    }

    public interface ISilksongTweakApi
    {
        bool IsReady { get; }
        void CaptureBaseline();
        void RestoreBaseline();
        void SetDamageMode(SilksongDamageMode mode);
        void RestoreDamageMode();
        void SetUnlimitedSilk(bool enabled);
        void RestoreUnlimitedSilk();
        void SetOneHitKills(bool enabled);
        void RestoreOneHitKills();
        void SetEquipAnywhere(bool enabled);
        void RestoreEquipAnywhere();
        void SetInstantDialogue(bool enabled);
        void RestoreInstantDialogue();
        void SetWorldRumbleDisabled(bool enabled);
        void RestoreWorldRumbleDisabled();
        void SetFrostDisabled(bool enabled);
        void RestoreFrostDisabled();
        void RefillSilk();
    }

    /// <summary>Silksong adapter for the shared built-in Mods contract.</summary>
    public sealed class SilksongTweakAdapter : ITweakAdapter
    {
        const string Missing = "No Silksong adapter operation is connected for this required row yet.";

        static readonly IReadOnlyList<TweakDescriptor> Rows = Array.AsReadOnly(new[]
        {
            Unavailable("skins", "skins", TweakControlKind.Route, "GENERAL", "SKINS", "Open the installed skin library.", "open", new[] { "open" }),
            Unavailable("black_background", "black_background", TweakControlKind.Choice, "GENERAL", "BLACK BACKGROUND", "Use a black lower-screen background instead of the dimmed scenery wash.", "off", new[] { "off", "on" }),

            Unavailable("run_speed", "run_speed", TweakControlKind.Choice, "WORLD", "RUN SPEED", "Choose Hornet's normal, +25%, or +50% movement pace.", "vanilla", new[] { "vanilla", "plus_25", "plus_50" }),
            Unavailable("fast_transitions", "fast_transitions", TweakControlKind.Choice, "WORLD", "FAST TRANSITIONS", "Shorten supported scene transitions.", "off", new[] { "off", "on" }),
            Unavailable("auto_map", "auto_map", TweakControlKind.Choice, "WORLD", "AUTO MAP", "Reveal visited rooms on the map automatically.", "off", new[] { "off", "on" }),
            Unavailable("innate_compass", "innate_compass", TweakControlKind.Choice, "WORLD", "INNATE COMPASS", "Show Hornet on the map without requiring a compass tool.", "off", new[] { "off", "on" }),
            Unavailable("bench_teleport", "bench_teleport", TweakControlKind.Route, "WORLD", "BENCH TELEPORT", "Open the recorded-bench destination list.", "open", new[] { "open" }),
            Unavailable("secret_radar", "secret_radar", TweakControlKind.Choice, "WORLD", "SECRET RADAR", "Signal nearby secrets without changing progression.", "off", new[] { "off", "on" }),

            Unavailable("nail_damage", "nail_damage", TweakControlKind.Choice, "COMBAT", "NEEDLE DAMAGE", "Multiply Needle damage while preserving upgrades.", "x1", new[] { "x1", "x2", "x3", "x5" }),
            Available("damage_received", "damage_taken", "COMBAT", "DAMAGE TAKEN", "Choose normal damage, prevent death, or full invincibility.", "vanilla", new[] { "vanilla", "prevent_death", "invincible" }),
            Unavailable("damage_cap", "damage_cap", TweakControlKind.Choice, "COMBAT", "DAMAGE CAP", "Limit damage received from a single hit.", "off", new[] { "off", "on" }),
            Available("one_hit_kills", "one_hit_kills", "COMBAT", "ONE-HIT KILLS", "Use Silksong's managed instant-kill damage state.", "off", new[] { "off", "on" }),
            Available("unlimited_silk", "unlimited_soul", "COMBAT", "UNLIMITED SILK", "Keep Silk available using Silksong's own drain and refill paths.", "off", new[] { "off", "on" }),

            Unavailable("health_bars", "enemy_health_bars", TweakControlKind.Choice, "ENCOUNTERS", "ENEMY HEALTH BARS", "Show health bars for eligible enemies and bosses.", "off", new[] { "off", "on" }),
            Unavailable("damage_numbers", "damage_numbers", TweakControlKind.Choice, "ENCOUNTERS", "DAMAGE NUMBERS", "Show the damage dealt by supported attacks.", "off", new[] { "off", "on" }),
            Unavailable("boss_retry", "boss_retry", TweakControlKind.Choice, "ENCOUNTERS", "BOSS RETRY", "Retry supported boss encounters from a safe checkpoint.", "off", new[] { "off", "on" }),

            Available("equip_anywhere", "equip_anywhere", "CRESTS & TOOLS", "EQUIP ANYWHERE", "Allow Crest and Tool changes away from benches.", "off", new[] { "off", "on" }),
            Unavailable("charm_costs", "charm_costs", TweakControlKind.Choice, "CRESTS & TOOLS", "TOOL COSTS", "Remove Tool slot costs while enabled.", "vanilla", new[] { "vanilla", "free" }),
            Unavailable("unlimited_notches", "unlimited_notches", TweakControlKind.Choice, "CRESTS & TOOLS", "UNLIMITED TOOL SLOTS", "Equip Tools without the normal Crest slot limit.", "off", new[] { "off", "on" }),

            Unavailable("state_slot", "state_slot", TweakControlKind.Choice, "SAVE STATES", "SLOT", "Choose the save-state slot used by the commands below.", "1", new[] { "1", "2", "3", "4", "5" }),
            Unavailable("save_to_slot", "save_to_slot", TweakControlKind.Command, "SAVE STATES", "SAVE TO SLOT", "Capture the current state in the selected slot.", "run", new[] { "run" }),
            Unavailable("load_from_slot", "load_from_slot", TweakControlKind.Command, "SAVE STATES", "LOAD FROM SLOT", "Restore the state stored in the selected slot.", "run", new[] { "run" }),
            Unavailable("delete_slot", "delete_slot", TweakControlKind.Command, "SAVE STATES", "DELETE SLOT", "Delete the state stored in the selected slot.", "run", new[] { "run" }),

            Unavailable("geo_magnet", "geo_magnet", TweakControlKind.Choice, "ECONOMY", "ROSARY MAGNET", "Collect nearby Rosaries without requiring a Tool.", "off", new[] { "off", "on" }),
            Unavailable("keep_geo_on_death", "keep_geo_on_death", TweakControlKind.Choice, "ECONOMY", "KEEP ROSARIES ON DEATH", "Keep Rosaries through death without duplicate recovery awards.", "off", new[] { "off", "on" }),
            Unavailable("journal_one_kill", "journal_one_kill", TweakControlKind.Choice, "ECONOMY", "JOURNAL IN ONE KILL", "Complete eligible Journal entries after one kill.", "off", new[] { "off", "on" }),
            Unavailable("geo_multiplier", "geo_multiplier", TweakControlKind.Choice, "ECONOMY", "ROSARY MULTIPLIER", "Multiply supported Rosary awards.", "x1", new[] { "x1", "x2", "x3", "x5" }),

            Available("instant_dialogue", "instant_dialogue", "PRESENTATION", "INSTANT DIALOGUE", "Show dialogue text immediately instead of printing it over time.", "off", new[] { "off", "on" }),
            Available("disable_world_rumble", "disable_world_rumble", "PRESENTATION", "DISABLE WORLD RUMBLE", "Prevent ambient world rumble effects.", "off", new[] { "off", "on" }),
            Available("ignore_frost_slowdown", "ignore_frost_slowdown", "PLAYER", "IGNORE FROST SLOWDOWN", "Prevent frost buildup from slowing Hornet.", "off", new[] { "off", "on" }),
        });

        readonly ISilksongTweakApi _api;
        bool _unlimitedSilk;

        public SilksongTweakAdapter(ISilksongTweakApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public string GameId => "silksong";
        public IReadOnlyList<TweakDescriptor> Descriptors => Rows;

        public void CaptureBaseline() => _api.CaptureBaseline();

        public TweakActionResult Apply(string id, string value)
        {
            TweakDescriptor descriptor = Find(id);
            if (descriptor == null) return TweakActionResult.Fail("Unknown Silksong tweak: " + id);
            if (!descriptor.IsAvailable)
                return TweakActionResult.Fail(descriptor.Title + " is currently unavailable: " + descriptor.UnavailableReason);
            if (!descriptor.Allows(value)) return TweakActionResult.Fail("Unsupported value for " + id + ": " + value);
            if (!_api.IsReady) return TweakActionResult.Fail("Silksong gameplay owners are not ready for Mods actions.");

            try
            {
                if (id == "damage_received")
                {
                    if (value == "vanilla") _api.RestoreDamageMode();
                    else if (value == "prevent_death") _api.SetDamageMode(SilksongDamageMode.PreventDeath);
                    else if (value == "invincible") _api.SetDamageMode(SilksongDamageMode.Invincible);
                    else return NoDispatch(id, value);
                }
                else if (id == "unlimited_silk")
                {
                    if (value == "off")
                    {
                        _api.RestoreUnlimitedSilk();
                        _unlimitedSilk = false;
                    }
                    else if (value == "on")
                    {
                        _api.SetUnlimitedSilk(true);
                        _unlimitedSilk = true;
                    }
                    else return NoDispatch(id, value);
                }
                else if (id == "one_hit_kills")
                {
                    if (value == "off") _api.RestoreOneHitKills();
                    else if (value == "on") _api.SetOneHitKills(true);
                    else return NoDispatch(id, value);
                }
                else if (id == "equip_anywhere")
                {
                    if (value == "off") _api.RestoreEquipAnywhere();
                    else if (value == "on") _api.SetEquipAnywhere(true);
                    else return NoDispatch(id, value);
                }
                else if (id == "instant_dialogue")
                {
                    if (value == "off") _api.RestoreInstantDialogue();
                    else if (value == "on") _api.SetInstantDialogue(true);
                    else return NoDispatch(id, value);
                }
                else if (id == "disable_world_rumble")
                {
                    if (value == "off") _api.RestoreWorldRumbleDisabled();
                    else if (value == "on") _api.SetWorldRumbleDisabled(true);
                    else return NoDispatch(id, value);
                }
                else if (id == "ignore_frost_slowdown")
                {
                    if (value == "off") _api.RestoreFrostDisabled();
                    else if (value == "on") _api.SetFrostDisabled(true);
                    else return NoDispatch(id, value);
                }
                else return TweakActionResult.Fail("No Silksong dispatch exists for " + id + ".");
                return TweakActionResult.Ok();
            }
            catch (Exception e)
            {
                return TweakActionResult.Fail("Silksong rejected " + id + ": " + e.Message);
            }
        }

        public void RestoreBaseline()
        {
            _api.RestoreBaseline();
            _unlimitedSilk = false;
        }

        public void Tick()
        {
            if (_unlimitedSilk && _api.IsReady) _api.RefillSilk();
        }

        static TweakDescriptor Available(
            string id, string contractId, string group, string title, string description,
            string defaultValue, IReadOnlyList<string> values) =>
            new TweakDescriptor(id, contractId, TweakControlKind.Choice, group, title, description, defaultValue, values);

        static TweakDescriptor Unavailable(
            string id, string contractId, TweakControlKind kind, string group, string title,
            string description, string defaultValue, IReadOnlyList<string> values) =>
            TweakDescriptor.Unavailable(id, contractId, kind, group, title, description, defaultValue, values, Missing);

        static TweakActionResult NoDispatch(string id, string value) =>
            TweakActionResult.Fail("No Silksong dispatch exists for " + id + " value " + value + ".");

        static TweakDescriptor Find(string id)
        {
            for (int i = 0; i < Rows.Count; i++)
                if (string.Equals(Rows[i].Id, id, StringComparison.Ordinal)) return Rows[i];
            return null;
        }
    }
}
