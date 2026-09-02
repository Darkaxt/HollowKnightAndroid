using System;
using System.Collections.Generic;
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    public enum HollowKnightFlashMode
    {
        Soft,
        Vanilla,
        Off,
    }

    internal static class HollowKnightFlashModeResolver
    {
        internal static HollowKnightFlashMode? Resolve(
            bool sessionReady,
            bool masterEnabled,
            string controllerValue,
            HollowKnightFlashMode? legacyMode)
        {
            if (!sessionReady || !masterEnabled) return legacyMode;

            switch (controllerValue)
            {
                case "soft":
                    return HollowKnightFlashMode.Soft;
                case "vanilla":
                    return HollowKnightFlashMode.Vanilla;
                case "off":
                    return HollowKnightFlashMode.Off;
                default:
                    return HollowKnightFlashMode.Vanilla;
            }
        }
    }

    /// <summary>Typed boundary for the two fork-owned, presentation-only Hollow Knight capabilities.</summary>
    public interface IHollowKnightTweakApi
    {
        bool IsReady { get; }
        void CaptureBaseline();
        void RestoreBaseline();
        void SetCompanionBackdropBlack(bool black);
        void SetLifebloodFlash(HollowKnightFlashMode mode);
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
            TweakDescriptor.Deferred(
                "damage_received", "COMBAT", "DAMAGE RECEIVED",
                "Control how much damage the Knight receives.",
                "HKMOD-001", "Prevent-death semantics and a scene-safe baseline are not proven."),
            TweakDescriptor.Deferred(
                "nail_damage", "COMBAT", "NAIL DAMAGE",
                "Adjust nail damage without breaking smith upgrades.",
                "HKMOD-002", "Smith-upgrade recomputation ownership and rollback are not proven."),
            TweakDescriptor.Deferred(
                "one_hit_kills", "COMBAT", "ONE-HIT KILLS",
                "Defeat eligible enemies with one authoritative hit.",
                "HKMOD-003", "Enemy-only typed hit interception and boss/script controls are not proven."),
            TweakDescriptor.Deferred(
                "run_speed", "PLAYER", "RUN SPEED",
                "Adjust the Knight's running pace across scenes.",
                "HKMOD-004", "Hero replacement maintenance and transition/death baseline restoration are not proven."),
            TweakDescriptor.Deferred(
                "unlimited_soul", "PLAYER", "UNLIMITED SOUL",
                "Keep Soul available through normal resource behavior.",
                "HKMOD-005", "Live Soul ownership and focus/spell/death/scene rollback are not proven."),
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
                    return TweakActionResult.Fail("Hollow Knight presentation API is not ready for " + id + ".");

                if (id == "companion_backdrop")
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
        }

        static TweakDescriptor Find(string id)
        {
            for (int i = 0; i < Rows.Count; i++)
                if (string.Equals(Rows[i].Id, id, StringComparison.Ordinal)) return Rows[i];
            return null;
        }
    }
}
