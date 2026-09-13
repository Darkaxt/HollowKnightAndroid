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

    /// <summary>Typed boundary for fork-owned Hollow Knight presentation and gameplay capabilities.</summary>
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
        static readonly IReadOnlyList<TweakDescriptor> Rows = Array.AsReadOnly(new[]
        {
            new TweakDescriptor(
                "companion_backdrop", "PRESENTATION", "COMPANION BACKDROP",
                "Choose the accepted dimmed scenery wash or a black lower-screen backdrop.",
                "dimmed", new[] { "dimmed", "black" }),
            new TweakDescriptor(
                "lifeblood_flash", "PRESENTATION", "LIFEBLOOD FLASH",
                "Use the accepted softened flash, the original flash, or no flash.",
                "soft", new[] { "soft", "vanilla", "off" }),
            new TweakDescriptor(
                "damage_received", "COMBAT", "DAMAGE RECEIVED",
                "Choose normal damage, keep masks, or ignore damage entirely.",
                "vanilla", new[] { "vanilla", "no_mask_loss", "invincible" }),
            new TweakDescriptor(
                "nail_damage", "COMBAT", "NAIL DAMAGE",
                "Multiply nail damage while preserving smith upgrades.",
                "x1", new[] { "x1", "x2", "x3", "x5" }),
            new TweakDescriptor(
                "one_hit_kills", "COMBAT", "ONE-HIT KILLS",
                "Defeat regular enemies in one hit while excluding boss-scale targets.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "run_speed", "PLAYER", "RUN SPEED",
                "Choose the Knight's normal, +25%, or +50% walking and running pace.",
                "vanilla", new[] { "vanilla", "plus_25", "plus_50" }),
            new TweakDescriptor(
                "unlimited_soul", "PLAYER", "UNLIMITED SOUL",
                "Keep Soul available through the game's normal refill path.",
                "off", new[] { "off", "on" }),
            TweakDescriptor.Deferred(
                "charm_costs", "CHARMS", "CHARM COSTS",
                "Adjust charm costs while preserving the complete loadout.",
                "HKMOD-006", "All-cost snapshots and equipped/save lifecycle rollback are not proven."),
            TweakDescriptor.Deferred(
                "unlimited_notches", "CHARMS", "UNLIMITED NOTCHES",
                "Equip charms without violating notch and overcharm rules.",
                "HKMOD-007", "Overcharm invariants, legal unequip, and exact lifecycle restoration are not proven."),
            TweakDescriptor.Deferred(
                "equip_anywhere", "CHARMS", "EQUIP ANYWHERE",
                "Change charms away from benches through legal game actions.",
                "HKMOD-008", "Safe managed inventory actions and bench/scene/save legality are not proven."),
            TweakDescriptor.Deferred(
                "geo_multiplier", "ECONOMY", "GEO MULTIPLIER",
                "Multiply Geo awards without changing unrelated balances.",
                "HKMOD-009", "Authoritative pickup and reward-source interception is not proven."),
            TweakDescriptor.Deferred(
                "keep_geo_on_death", "ECONOMY", "KEEP GEO ON DEATH",
                "Keep Geo through death without duplicate Shade awards.",
                "HKMOD-010", "Shade, death-pool, respawn, and duplicate-award rollback are not proven."),
            TweakDescriptor.Deferred(
                "journal_one_kill", "JOURNAL", "JOURNAL ONE KILL",
                "Complete eligible Hunter's Journal entries after one kill.",
                "HKMOD-011", "Progression-write authority, event idempotence, and reload rollback are not proven."),
            TweakDescriptor.Deferred(
                "auto_map", "WORLD", "AUTO MAP",
                "Reveal only visited rooms in the current mapped area.",
                "HKMOD-012", "Bounded area-only reveal and progression-safe scene/save rollback are not proven."),
            TweakDescriptor.Deferred(
                "health_bars", "WORLD", "HEALTH BARS",
                "Show health bars for eligible enemies and bosses.",
                "HKMOD-013", "Spawned and pooled enemy renderer/event lifecycle handling is not proven."),
            TweakDescriptor.Deferred(
                "damage_numbers", "WORLD", "DAMAGE NUMBERS",
                "Show authoritative damage dealt for every supported attack source.",
                "HKMOD-014", "A complete dealt-damage event and pooled UI teardown are not proven."),
            TweakDescriptor.Deferred(
                "boss_retry", "WORLD", "BOSS RETRY",
                "Retry bosses through a safe checkpoint transition.",
                "HKMOD-015", "Scene reset and save-checkpoint semantics are not proven."),
            TweakDescriptor.Deferred(
                "secret_radar", "WORLD", "SECRET RADAR",
                "Detect nearby secrets without changing progression.",
                "HKMOD-016", "Secret identity, range, and non-progression authority are not proven."),
            TweakDescriptor.Deferred(
                "bench_teleport", "WORLD", "BENCH TELEPORT",
                "Travel only to recorded benches through safe transitions.",
                "HKMOD-017", "Recorded-bench validation and transition failure rollback are not proven."),
            TweakDescriptor.Deferred(
                "state_slots", "STATE", "STATE SLOTS",
                "Capture and restore transactional, versioned state snapshots.",
                "HKMOD-018", "A versioned checksummed snapshot format and atomic failure rollback are not proven."),
        });

        readonly IHollowKnightTweakApi _api;

        public HollowKnightTweakAdapter(IHollowKnightTweakApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public string GameId => "hollow-knight";
        public IReadOnlyList<TweakDescriptor> Descriptors => Rows;

        public void CaptureBaseline()
        {
            _api.CaptureBaseline();
        }

        public TweakActionResult Apply(string id, string value)
        {
            TweakDescriptor descriptor = Find(id);
            if (descriptor == null)
                return TweakActionResult.Fail("Unknown Hollow Knight tweak: " + id);
            if (!descriptor.IsAvailable)
                return TweakActionResult.Fail(descriptor.TrackingId + " is deferred: " + descriptor.UnavailableReason);
            if (!descriptor.Allows(value))
                return TweakActionResult.Fail("Unsupported value for " + id + ": " + value);

            try
            {
                if (!_api.IsReady)
                    return TweakActionResult.Fail("Hollow Knight tweak API is not ready for " + id + ".");

                if (id == "damage_received")
                {
                    if (value == "vanilla")
                        _api.RestoreDamageMode();
                    else if (value == "no_mask_loss")
                        _api.SetDamageMode(HollowKnightDamageMode.NoMaskLoss);
                    else if (value == "invincible")
                        _api.SetDamageMode(HollowKnightDamageMode.Invincible);
                    else
                        return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");
                }
                else if (id == "nail_damage")
                {
                    if (value == "x1")
                        _api.RestoreNailDamage();
                    else if (value == "x2")
                        _api.SetNailDamageMultiplier(2);
                    else if (value == "x3")
                        _api.SetNailDamageMultiplier(3);
                    else if (value == "x5")
                        _api.SetNailDamageMultiplier(5);
                    else
                        return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");
                }
                else if (id == "one_hit_kills")
                {
                    if (value == "off")
                        _api.RestoreOneHitKills();
                    else if (value == "on")
                        _api.SetOneHitKills(true);
                    else
                        return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");
                }
                else if (id == "run_speed")
                {
                    if (value == "vanilla")
                        _api.RestoreRunSpeed();
                    else if (value == "plus_25")
                        _api.SetRunSpeedMultiplier(1.25f);
                    else if (value == "plus_50")
                        _api.SetRunSpeedMultiplier(1.5f);
                    else
                        return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");
                }
                else if (id == "unlimited_soul")
                {
                    if (value == "off")
                        _api.RestoreUnlimitedSoul();
                    else if (value == "on")
                        _api.SetUnlimitedSoul(true);
                    else
                        return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");
                }
                else if (id == "companion_backdrop")
                {
                    if (value == "dimmed")
                        _api.SetCompanionBackdropBlack(false);
                    else if (value == "black")
                        _api.SetCompanionBackdropBlack(true);
                    else
                        return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");
                }
                else if (id == "lifeblood_flash")
                {
                    if (value == "soft")
                        _api.SetLifebloodFlash(HollowKnightFlashMode.Soft);
                    else if (value == "vanilla")
                        _api.SetLifebloodFlash(HollowKnightFlashMode.Vanilla);
                    else if (value == "off")
                        _api.SetLifebloodFlash(HollowKnightFlashMode.Off);
                    else
                        return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + " value " + value + ".");
                }
                else
                {
                    return TweakActionResult.Fail("No Hollow Knight dispatch exists for " + id + ".");
                }
                return TweakActionResult.Ok();
            }
            catch (Exception e)
            {
                return TweakActionResult.Fail("Hollow Knight rejected " + id + ": " + e.Message);
            }
        }

        public void RestoreBaseline()
        {
            _api.RestoreBaseline();
        }

        public void Tick()
        {
            _api.TickGameplay();
        }

        static TweakDescriptor Find(string id)
        {
            for (int i = 0; i < Rows.Count; i++)
                if (string.Equals(Rows[i].Id, id, StringComparison.Ordinal)) return Rows[i];
            return null;
        }
    }
}
