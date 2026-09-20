using System;
using System.Collections.Generic;
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    public enum HollowKnightDamageMode
    {
        NoMaskLoss,
        Invincible,
    }

    public enum HollowKnightFlashMode
    {
        Soft,
        Vanilla,
        Off,
    }

    public interface IHollowKnightTweakApi
    {
        bool IsReady { get; }
        void CaptureBaseline();
        void RestoreBaseline();
        void SetCompanionBackdropBlack(bool black);
        void SetLifebloodFlash(HollowKnightFlashMode mode);
        void SetDamageMode(HollowKnightDamageMode mode);
        void RestoreDamageMode();
        void SetNailDamageMultiplier(int multiplier);
        void RestoreNailDamage();
        void SetOneHitKills(bool enabled);
        void RestoreOneHitKills();
        void SetRunSpeedMultiplier(float multiplier);
        void RestoreRunSpeed();
        void SetUnlimitedSoul(bool enabled);
        void RestoreUnlimitedSoul();
        void TickGameplay();
    }

    /// <summary>Hollow Knight catalog adapter for the shared built-in Mods contract.</summary>
    public sealed class HollowKnightTweakAdapter : ITweakAdapter
    {
        const string Missing = "No Hollow Knight adapter operation is connected for this required row yet.";

        static readonly IReadOnlyList<TweakDescriptor> Rows = Array.AsReadOnly(new[]
        {
            Unavailable("skins", "skins", TweakControlKind.Route, "GENERAL", "SKINS", "Open the installed skin library.", "open", new[] { "open" }),
            Available("companion_backdrop", "black_background", "GENERAL", "BLACK BACKGROUND", "Use a black lower-screen background instead of the dimmed scenery wash.", "dimmed", new[] { "dimmed", "black" }),

            Available("run_speed", "run_speed", "WORLD", "RUN SPEED", "Choose the Knight's normal, +25%, or +50% movement pace.", "vanilla", new[] { "vanilla", "plus_25", "plus_50" }),
            Unavailable("fast_transitions", "fast_transitions", TweakControlKind.Choice, "WORLD", "FAST TRANSITIONS", "Shorten supported scene transitions.", "off", new[] { "off", "on" }),
            Unavailable("auto_map", "auto_map", TweakControlKind.Choice, "WORLD", "AUTO MAP", "Reveal visited rooms on the map automatically.", "off", new[] { "off", "on" }),
            Unavailable("innate_compass", "innate_compass", TweakControlKind.Choice, "WORLD", "INNATE COMPASS", "Show the Knight on the map without requiring Wayward Compass.", "off", new[] { "off", "on" }),
            Unavailable("bench_teleport", "bench_teleport", TweakControlKind.Route, "WORLD", "BENCH TELEPORT", "Open the recorded-bench destination list.", "open", new[] { "open" }),
            Unavailable("secret_radar", "secret_radar", TweakControlKind.Choice, "WORLD", "SECRET RADAR", "Signal nearby secrets without changing progression.", "off", new[] { "off", "on" }),

            Available("nail_damage", "nail_damage", "COMBAT", "NAIL DAMAGE", "Multiply nail damage while preserving smith upgrades.", "x1", new[] { "x1", "x2", "x3", "x5" }),
            Available("damage_received", "damage_taken", "COMBAT", "DAMAGE TAKEN", "Choose normal damage, keep masks, or ignore damage entirely.", "vanilla", new[] { "vanilla", "no_mask_loss", "invincible" }),
            Unavailable("damage_cap", "damage_cap", TweakControlKind.Choice, "COMBAT", "DAMAGE CAP", "Limit damage received from a single hit.", "off", new[] { "off", "on" }),
            Available("one_hit_kills", "one_hit_kills", "COMBAT", "ONE-HIT KILLS", "Defeat regular enemies in one hit while excluding boss-scale targets.", "off", new[] { "off", "on" }),
            Available("unlimited_soul", "unlimited_soul", "COMBAT", "UNLIMITED SOUL", "Keep Soul available through the game's normal refill path.", "off", new[] { "off", "on" }),

            Unavailable("health_bars", "enemy_health_bars", TweakControlKind.Choice, "ENCOUNTERS", "ENEMY HEALTH BARS", "Show health bars for eligible enemies and bosses.", "off", new[] { "off", "on" }),
            Unavailable("damage_numbers", "damage_numbers", TweakControlKind.Choice, "ENCOUNTERS", "DAMAGE NUMBERS", "Show the damage dealt by supported attacks.", "off", new[] { "off", "on" }),
            Unavailable("boss_retry", "boss_retry", TweakControlKind.Choice, "ENCOUNTERS", "BOSS RETRY", "Retry supported boss encounters from a safe checkpoint.", "off", new[] { "off", "on" }),

            Unavailable("equip_anywhere", "equip_anywhere", TweakControlKind.Choice, "CHARMS", "EQUIP ANYWHERE", "Change charms away from benches through legal game actions.", "off", new[] { "off", "on" }),
            Unavailable("charm_costs", "charm_costs", TweakControlKind.Choice, "CHARMS", "CHARM COSTS", "Remove charm notch costs while enabled.", "vanilla", new[] { "vanilla", "free" }),
            Unavailable("unlimited_notches", "unlimited_notches", TweakControlKind.Choice, "CHARMS", "UNLIMITED NOTCHES", "Equip charms without the normal notch limit.", "off", new[] { "off", "on" }),

            Unavailable("state_slot", "state_slot", TweakControlKind.Choice, "SAVE STATES", "SLOT", "Choose the save-state slot used by the commands below.", "1", new[] { "1", "2", "3", "4", "5" }),
            Unavailable("save_to_slot", "save_to_slot", TweakControlKind.Command, "SAVE STATES", "SAVE TO SLOT", "Capture the current state in the selected slot.", "run", new[] { "run" }),
            Unavailable("load_from_slot", "load_from_slot", TweakControlKind.Command, "SAVE STATES", "LOAD FROM SLOT", "Restore the state stored in the selected slot.", "run", new[] { "run" }),
            Unavailable("delete_slot", "delete_slot", TweakControlKind.Command, "SAVE STATES", "DELETE SLOT", "Delete the state stored in the selected slot.", "run", new[] { "run" }),

            Unavailable("geo_magnet", "geo_magnet", TweakControlKind.Choice, "ECONOMY", "GEO MAGNET", "Collect nearby Geo without requiring Gathering Swarm.", "off", new[] { "off", "on" }),
            Unavailable("keep_geo_on_death", "keep_geo_on_death", TweakControlKind.Choice, "ECONOMY", "KEEP GEO ON DEATH", "Keep Geo through death without duplicate Shade awards.", "off", new[] { "off", "on" }),
            Unavailable("journal_one_kill", "journal_one_kill", TweakControlKind.Choice, "ECONOMY", "JOURNAL IN ONE KILL", "Complete eligible Hunter's Journal entries after one kill.", "off", new[] { "off", "on" }),
            Unavailable("geo_multiplier", "geo_multiplier", TweakControlKind.Choice, "ECONOMY", "GEO MULTIPLIER", "Multiply supported Geo awards.", "x1", new[] { "x1", "x2", "x3", "x5" }),

            Available("lifeblood_flash", "lifeblood_flash", "PRESENTATION", "LIFEBLOOD FLASH", "Use the original flash, a softened flash, or no flash.", "vanilla", new[] { "vanilla", "soft", "off" }),
        });

        readonly IHollowKnightTweakApi _api;

        public HollowKnightTweakAdapter(IHollowKnightTweakApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public string GameId => "hollow-knight";
        public IReadOnlyList<TweakDescriptor> Descriptors => Rows;

        public void CaptureBaseline() => _api.CaptureBaseline();

        public TweakActionResult Apply(string id, string value)
        {
            TweakDescriptor descriptor = Find(id);
            if (descriptor == null) return TweakActionResult.Fail("Unknown Hollow Knight tweak: " + id);
            if (!descriptor.IsAvailable)
                return TweakActionResult.Fail(descriptor.Title + " is currently unavailable: " + descriptor.UnavailableReason);
            if (!descriptor.Allows(value)) return TweakActionResult.Fail("Unsupported value for " + id + ": " + value);

            try
            {
                if (!_api.IsReady) return TweakActionResult.Fail("Hollow Knight tweak API is not ready for " + id + ".");

                if (id == "damage_received")
                {
                    if (value == "vanilla") _api.RestoreDamageMode();
                    else if (value == "no_mask_loss") _api.SetDamageMode(HollowKnightDamageMode.NoMaskLoss);
                    else if (value == "invincible") _api.SetDamageMode(HollowKnightDamageMode.Invincible);
                    else return NoDispatch(id, value);
                }
                else if (id == "nail_damage")
                {
                    if (value == "x1") _api.RestoreNailDamage();
                    else if (value == "x2") _api.SetNailDamageMultiplier(2);
                    else if (value == "x3") _api.SetNailDamageMultiplier(3);
                    else if (value == "x5") _api.SetNailDamageMultiplier(5);
                    else return NoDispatch(id, value);
                }
                else if (id == "one_hit_kills")
                {
                    if (value == "off") _api.RestoreOneHitKills();
                    else if (value == "on") _api.SetOneHitKills(true);
                    else return NoDispatch(id, value);
                }
                else if (id == "run_speed")
                {
                    if (value == "vanilla") _api.RestoreRunSpeed();
                    else if (value == "plus_25") _api.SetRunSpeedMultiplier(1.25f);
                    else if (value == "plus_50") _api.SetRunSpeedMultiplier(1.5f);
                    else return NoDispatch(id, value);
                }
                else if (id == "unlimited_soul")
                {
                    if (value == "off") _api.RestoreUnlimitedSoul();
                    else if (value == "on") _api.SetUnlimitedSoul(true);
                    else return NoDispatch(id, value);
                }
                else if (id == "companion_backdrop")
                {
                    if (value == "dimmed") _api.SetCompanionBackdropBlack(false);
                    else if (value == "black") _api.SetCompanionBackdropBlack(true);
                    else return NoDispatch(id, value);
                }
                else if (id == "lifeblood_flash")
                {
                    if (value == "soft") _api.SetLifebloodFlash(HollowKnightFlashMode.Soft);
                    else if (value == "vanilla") _api.SetLifebloodFlash(HollowKnightFlashMode.Vanilla);
                    else if (value == "off") _api.SetLifebloodFlash(HollowKnightFlashMode.Off);
                    else return NoDispatch(id, value);
                }
                else return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + ".");
                return TweakActionResult.Ok();
            }
            catch (Exception e)
            {
                return TweakActionResult.Fail("Hollow Knight rejected " + id + ": " + e.Message);
            }
        }

        public void RestoreBaseline() => _api.RestoreBaseline();
        public void Tick() => _api.TickGameplay();

        static TweakDescriptor Available(
            string id, string contractId, string group, string title, string description,
            string defaultValue, IReadOnlyList<string> values) =>
            new TweakDescriptor(id, contractId, TweakControlKind.Choice, group, title, description, defaultValue, values);

        static TweakDescriptor Unavailable(
            string id, string contractId, TweakControlKind kind, string group, string title,
            string description, string defaultValue, IReadOnlyList<string> values) =>
            TweakDescriptor.Unavailable(id, contractId, kind, group, title, description, defaultValue, values, Missing);

        static TweakActionResult NoDispatch(string id, string value) =>
            TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");

        static TweakDescriptor Find(string id)
        {
            for (int i = 0; i < Rows.Count; i++)
                if (string.Equals(Rows[i].Id, id, StringComparison.Ordinal)) return Rows[i];
            return null;
        }
    }
}
