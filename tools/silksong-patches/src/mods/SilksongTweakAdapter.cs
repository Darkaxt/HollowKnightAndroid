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
            if (_unlimitedSilk) _api.RefillSilk();
        }

        static TweakDescriptor Find(string id)
        {
            for (int i = 0; i < Rows.Count; i++)
                if (string.Equals(Rows[i].Id, id, StringComparison.Ordinal)) return Rows[i];
            return null;
        }
    }
}
