using System;
using System.Collections.Generic;
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    public enum HollowKnightDamageMode { NoMaskLoss, Invincible }
    public enum HollowKnightFlashMode { Soft, Vanilla, Off }

    public interface IHollowKnightTweakApi
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
        void OpenBenchTeleport();
        void SetSecretRadar(bool enabled);
        void SetNailDamageMultiplier(int multiplier);
        void RestoreNailDamage();
        void SetDamageMode(HollowKnightDamageMode mode);
        void RestoreDamageMode();
        void SetDamageCap(bool enabled);
        void SetOneHitKills(bool enabled);
        void RestoreOneHitKills();
        void SetUnlimitedSoul(bool enabled);
        void RestoreUnlimitedSoul();
        void SetEnemyHealthBars(bool enabled);
        void SetDamageNumbers(bool enabled);
        void SetBossRetry(bool enabled);
        void SetEquipAnywhere(bool enabled);
        void SetCharmCostsFree(bool enabled);
        void SetUnlimitedNotches(bool enabled);
        void SetStateSlot(int slot);
        void SaveState();
        void LoadState();
        void DeleteState();
        void SetGeoMagnet(bool enabled);
        void SetKeepGeoOnDeath(bool enabled);
        void SetJournalOneKill(bool enabled);
        void SetGeoMultiplier(int multiplier);
        void SetLifebloodFlash(HollowKnightFlashMode mode);
        void TickGameplay();
    }

    public sealed class HollowKnightTweakAdapter : ITweakAdapter
    {
        static readonly IReadOnlyList<TweakDescriptor> Rows = Array.AsReadOnly(new[]
        {
            Row("skins", "skins", TweakControlKind.Route, "GENERAL", "SKINS", "Open the installed skin library.", "open", "open"),
            Row("companion_backdrop", "black_background", TweakControlKind.Choice, "GENERAL", "BLACK BACKGROUND", "Use a black lower-screen background instead of the dimmed scenery wash.", "dimmed", "dimmed", "black"),
            Row("run_speed", "run_speed", TweakControlKind.Choice, "WORLD", "RUN SPEED", "Choose the Knight's normal, +25%, or +50% movement pace.", "vanilla", "vanilla", "plus_25", "plus_50"),
            Row("fast_transitions", "fast_transitions", TweakControlKind.Choice, "WORLD", "FAST TRANSITIONS", "Shorten supported scene transitions.", "off", "off", "on"),
            Row("auto_map", "auto_map", TweakControlKind.Choice, "WORLD", "AUTO MAP", "Reveal visited rooms on the map automatically.", "off", "off", "on"),
            Row("innate_compass", "innate_compass", TweakControlKind.Choice, "WORLD", "INNATE COMPASS", "Show the Knight on the map without requiring Wayward Compass.", "off", "off", "on"),
            Row("bench_teleport", "bench_teleport", TweakControlKind.Route, "WORLD", "BENCH TELEPORT", "Open the recorded-bench destination list.", "open", "open"),
            Row("secret_radar", "secret_radar", TweakControlKind.Choice, "WORLD", "SECRET RADAR", "Signal nearby secrets without changing progression.", "off", "off", "on"),
            Row("nail_damage", "nail_damage", TweakControlKind.Choice, "COMBAT", "NAIL DAMAGE", "Multiply nail damage while preserving smith upgrades.", "x1", "x1", "x2", "x3", "x5"),
            Row("damage_received", "damage_taken", TweakControlKind.Choice, "COMBAT", "DAMAGE TAKEN", "Choose normal damage, keep masks, or ignore damage entirely.", "vanilla", "vanilla", "no_mask_loss", "invincible"),
            Row("damage_cap", "damage_cap", TweakControlKind.Choice, "COMBAT", "DAMAGE CAP", "Limit damage received from a single hit.", "off", "off", "on"),
            Row("one_hit_kills", "one_hit_kills", TweakControlKind.Choice, "COMBAT", "ONE-HIT KILLS", "Defeat regular enemies in one hit while excluding boss-scale targets.", "off", "off", "on"),
            Row("unlimited_soul", "unlimited_soul", TweakControlKind.Choice, "COMBAT", "UNLIMITED SOUL", "Keep Soul available through the game's normal refill path.", "off", "off", "on"),
            Row("health_bars", "enemy_health_bars", TweakControlKind.Choice, "ENCOUNTERS", "ENEMY HEALTH BARS", "Show health bars for eligible enemies and bosses.", "off", "off", "on"),
            Row("damage_numbers", "damage_numbers", TweakControlKind.Choice, "ENCOUNTERS", "DAMAGE NUMBERS", "Show the damage dealt by supported attacks.", "off", "off", "on"),
            Row("boss_retry", "boss_retry", TweakControlKind.Choice, "ENCOUNTERS", "BOSS RETRY", "Retry supported boss encounters from a safe checkpoint.", "off", "off", "on"),
            Row("equip_anywhere", "equip_anywhere", TweakControlKind.Choice, "CHARMS", "EQUIP ANYWHERE", "Change charms away from benches through legal game actions.", "off", "off", "on"),
            Row("charm_costs", "charm_costs", TweakControlKind.Choice, "CHARMS", "CHARM COSTS", "Remove charm notch costs while enabled.", "vanilla", "vanilla", "free"),
            Row("unlimited_notches", "unlimited_notches", TweakControlKind.Choice, "CHARMS", "UNLIMITED NOTCHES", "Equip charms without the normal notch limit.", "off", "off", "on"),
            Row("state_slot", "state_slot", TweakControlKind.Choice, "SAVE STATES", "SLOT", "Choose the save-state slot used by the commands below.", "1", "1", "2", "3", "4", "5"),
            Row("save_to_slot", "save_to_slot", TweakControlKind.Command, "SAVE STATES", "SAVE TO SLOT", "Capture the current state in the selected slot.", "run", "run"),
            Row("load_from_slot", "load_from_slot", TweakControlKind.Command, "SAVE STATES", "LOAD FROM SLOT", "Restore the state stored in the selected slot.", "run", "run"),
            Row("delete_slot", "delete_slot", TweakControlKind.Command, "SAVE STATES", "DELETE SLOT", "Delete the state stored in the selected slot.", "run", "run"),
            Row("geo_magnet", "geo_magnet", TweakControlKind.Choice, "ECONOMY", "GEO MAGNET", "Collect nearby Geo without requiring Gathering Swarm.", "off", "off", "on"),
            Row("keep_geo_on_death", "keep_geo_on_death", TweakControlKind.Choice, "ECONOMY", "KEEP GEO ON DEATH", "Keep Geo through death without duplicate Shade awards.", "off", "off", "on"),
            Row("journal_one_kill", "journal_one_kill", TweakControlKind.Choice, "ECONOMY", "JOURNAL IN ONE KILL", "Complete eligible Hunter's Journal entries after one kill.", "off", "off", "on"),
            Row("geo_multiplier", "geo_multiplier", TweakControlKind.Choice, "ECONOMY", "GEO MULTIPLIER", "Multiply supported Geo awards.", "x1", "x1", "x2", "x3", "x5"),
            Row("lifeblood_flash", "lifeblood_flash", TweakControlKind.Choice, "PRESENTATION", "LIFEBLOOD FLASH", "Use the original flash, a softened flash, or no flash.", "vanilla", "vanilla", "soft", "off"),
        });

        readonly IHollowKnightTweakApi _api;
        public HollowKnightTweakAdapter(IHollowKnightTweakApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));
        public string GameId => "hollow-knight";
        public IReadOnlyList<TweakDescriptor> Descriptors => Rows;
        public void CaptureBaseline() => _api.CaptureBaseline();
        public void RestoreBaseline() => _api.RestoreBaseline();
        public void Tick() => _api.TickGameplay();

        public TweakActionResult Apply(string id, string value)
        {
            TweakDescriptor row = Find(id);
            if (row == null) return TweakActionResult.Fail("Unknown Hollow Knight tweak: " + id);
            if (!row.Allows(value)) return TweakActionResult.Fail("Unsupported value for " + id + ": " + value);
            if (!_api.IsReady) return TweakActionResult.Fail("Hollow Knight tweak API is not ready for " + id + ".");
            try
            {
                switch (id)
                {
                    case "skins": _api.OpenSkins(); break;
                    case "companion_backdrop": _api.SetCompanionBackdropBlack(value == "black"); break;
                    case "run_speed":
                        if (value == "vanilla") _api.RestoreRunSpeed();
                        else _api.SetRunSpeedMultiplier(value == "plus_25" ? 1.25f : 1.5f);
                        break;
                    case "fast_transitions": _api.SetFastTransitions(On(value)); break;
                    case "auto_map": _api.SetAutoMap(On(value)); break;
                    case "innate_compass": _api.SetInnateCompass(On(value)); break;
                    case "bench_teleport": _api.OpenBenchTeleport(); break;
                    case "secret_radar": _api.SetSecretRadar(On(value)); break;
                    case "nail_damage":
                        if (value == "x1") _api.RestoreNailDamage(); else _api.SetNailDamageMultiplier(Multiplier(value));
                        break;
                    case "damage_received":
                        if (value == "vanilla") _api.RestoreDamageMode();
                        else _api.SetDamageMode(value == "no_mask_loss" ? HollowKnightDamageMode.NoMaskLoss : HollowKnightDamageMode.Invincible);
                        break;
                    case "damage_cap": _api.SetDamageCap(On(value)); break;
                    case "one_hit_kills": if (On(value)) _api.SetOneHitKills(true); else _api.RestoreOneHitKills(); break;
                    case "unlimited_soul": if (On(value)) _api.SetUnlimitedSoul(true); else _api.RestoreUnlimitedSoul(); break;
                    case "health_bars": _api.SetEnemyHealthBars(On(value)); break;
                    case "damage_numbers": _api.SetDamageNumbers(On(value)); break;
                    case "boss_retry": _api.SetBossRetry(On(value)); break;
                    case "equip_anywhere": _api.SetEquipAnywhere(On(value)); break;
                    case "charm_costs": _api.SetCharmCostsFree(value == "free"); break;
                    case "unlimited_notches": _api.SetUnlimitedNotches(On(value)); break;
                    case "state_slot": _api.SetStateSlot(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); break;
                    case "save_to_slot": _api.SaveState(); break;
                    case "load_from_slot": _api.LoadState(); break;
                    case "delete_slot": _api.DeleteState(); break;
                    case "geo_magnet": _api.SetGeoMagnet(On(value)); break;
                    case "keep_geo_on_death": _api.SetKeepGeoOnDeath(On(value)); break;
                    case "journal_one_kill": _api.SetJournalOneKill(On(value)); break;
                    case "geo_multiplier": _api.SetGeoMultiplier(Multiplier(value)); break;
                    case "lifeblood_flash": _api.SetLifebloodFlash(value == "soft" ? HollowKnightFlashMode.Soft : value == "off" ? HollowKnightFlashMode.Off : HollowKnightFlashMode.Vanilla); break;
                    default: return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + ".");
                }
                return TweakActionResult.Ok();
            }
            catch (Exception e) { return TweakActionResult.Fail("Hollow Knight rejected " + id + ": " + e.Message); }
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
