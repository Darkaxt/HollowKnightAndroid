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

    /// <summary>
    /// Typed boundary around Silksong's managed gameplay APIs. The adapter does not
    /// know about IL2CPP addresses or process memory.
    /// </summary>
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

    /// <summary>Initial Silksong implementation of the shared built-in Mods contract.</summary>
    public sealed class SilksongTweakAdapter : ITweakAdapter
    {
        static readonly IReadOnlyList<TweakDescriptor> Rows = new[]
        {
            new TweakDescriptor(
                "damage_received", "COMBAT", "DAMAGE RECEIVED",
                "Choose normal damage, prevent death, or full invincibility.",
                "vanilla", new[] { "vanilla", "prevent_death", "invincible" }),
            new TweakDescriptor(
                "unlimited_silk", "COMBAT", "UNLIMITED SILK",
                "Keep Silk available using Silksong's own drain and refill paths.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "one_hit_kills", "COMBAT", "ONE-HIT KILLS",
                "Use Silksong's managed instant-kill damage state.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "equip_anywhere", "LOADOUT", "EQUIP ANYWHERE",
                "Allow tool and crest changes away from benches.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "instant_dialogue", "PRESENTATION", "INSTANT DIALOGUE",
                "Show dialogue text immediately instead of printing it over time.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "disable_world_rumble", "PRESENTATION", "DISABLE WORLD RUMBLE",
                "Prevent ambient world rumble effects.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "ignore_frost_slowdown", "PLAYER", "IGNORE FROST SLOWDOWN",
                "Prevent frost buildup from slowing Hornet.",
                "off", new[] { "off", "on" }),
        };

        readonly ISilksongTweakApi _api;
        bool _unlimitedSilk;

        public SilksongTweakAdapter(ISilksongTweakApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public string GameId => "silksong";
        public IReadOnlyList<TweakDescriptor> Descriptors => Rows;

        public void CaptureBaseline()
        {
            _api.CaptureBaseline();
        }

        public TweakActionResult Apply(string id, string value)
        {
            if (!_api.IsReady)
                return TweakActionResult.Fail("Silksong gameplay owners are not ready for Mods actions.");
            TweakDescriptor descriptor = Find(id);
            if (descriptor == null) return TweakActionResult.Fail("Unknown Silksong tweak: " + id);
            if (!descriptor.Allows(value)) return TweakActionResult.Fail("Unsupported value for " + id + ": " + value);

            try
            {
                if (id == "damage_received")
                {
                    if (value == "vanilla") _api.RestoreDamageMode();
                    else if (value == "prevent_death") _api.SetDamageMode(SilksongDamageMode.PreventDeath);
                    else _api.SetDamageMode(SilksongDamageMode.Invincible);
                }
                else if (id == "unlimited_silk")
                {
                    if (value == "off")
                    {
                        _api.RestoreUnlimitedSilk();
                        _unlimitedSilk = false;
                    }
                    else
                    {
                        _api.SetUnlimitedSilk(true);
                        _unlimitedSilk = true;
                    }
                }
                else if (id == "one_hit_kills")
                {
                    if (value == "off") _api.RestoreOneHitKills();
                    else _api.SetOneHitKills(true);
                }
                else if (id == "equip_anywhere")
                {
                    if (value == "off") _api.RestoreEquipAnywhere();
                    else _api.SetEquipAnywhere(true);
                }
                else if (id == "instant_dialogue")
                {
                    if (value == "off") _api.RestoreInstantDialogue();
                    else _api.SetInstantDialogue(true);
                }
                else if (id == "disable_world_rumble")
                {
                    if (value == "off") _api.RestoreWorldRumbleDisabled();
                    else _api.SetWorldRumbleDisabled(true);
                }
                else if (id == "ignore_frost_slowdown")
                {
                    if (value == "off") _api.RestoreFrostDisabled();
                    else _api.SetFrostDisabled(true);
                }
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

        static TweakDescriptor Find(string id)
        {
            for (int i = 0; i < Rows.Count; i++)
                if (string.Equals(Rows[i].Id, id, StringComparison.Ordinal)) return Rows[i];
            return null;
        }
    }
}
