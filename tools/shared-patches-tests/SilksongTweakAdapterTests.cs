using System.Linq;
using DualSouls.Mods;
using DualSouls.Mods.Silksong;
using Xunit;

namespace SharedPatches.Tests;

public sealed class SilksongTweakAdapterTests
{
    [Fact]
    public void CatalogStartsWithExactRequiredContractAndSilksongNamesThenAppendsExtras()
    {
        var rows = new SilksongTweakAdapter(new RecordingApi()).Descriptors;
        TweakDescriptor[] required = rows.Take(27).ToArray();
        string[] contractIds =
        {
            "skins", "black_background",
            "run_speed", "fast_transitions", "auto_map", "innate_compass", "bench_teleport", "secret_radar",
            "nail_damage", "damage_taken", "damage_cap", "one_hit_kills", "unlimited_soul",
            "enemy_health_bars", "damage_numbers", "boss_retry",
            "equip_anywhere", "charm_costs", "unlimited_notches",
            "state_slot", "save_to_slot", "load_from_slot", "delete_slot",
            "geo_magnet", "keep_geo_on_death", "journal_one_kill", "geo_multiplier",
        };

        Assert.Equal(contractIds, required.Select(row => row.ContractId));
        Assert.Equal("NEEDLE DAMAGE", required.Single(row => row.ContractId == "nail_damage").Title);
        Assert.Equal("UNLIMITED SILK", required.Single(row => row.ContractId == "unlimited_soul").Title);
        Assert.All(required.Skip(16).Take(3), row => Assert.Equal("CRESTS & TOOLS", row.Group));
        Assert.Equal("ROSARY MAGNET", required.Single(row => row.ContractId == "geo_magnet").Title);
        Assert.Equal("KEEP ROSARIES ON DEATH", required.Single(row => row.ContractId == "keep_geo_on_death").Title);
        Assert.Equal("ROSARY MULTIPLIER", required.Single(row => row.ContractId == "geo_multiplier").Title);
        Assert.Equal("damage_received", required.Single(row => row.ContractId == "damage_taken").Id);
        Assert.Equal("unlimited_silk", required.Single(row => row.ContractId == "unlimited_soul").Id);
        Assert.Equal(new[] { "instant_dialogue", "disable_world_rumble", "ignore_frost_slowdown" }, rows.Skip(27).Select(row => row.Id));
        Assert.Equal(4, required.Count(row => row.IsAvailable));
        Assert.All(required.Where(row => !row.IsAvailable), row =>
            Assert.False(string.IsNullOrWhiteSpace(row.UnavailableReason)));
        Assert.DoesNotContain(rows, row => row.Id == "state_slots");
    }

    [Fact]
    public void EveryUnavailableAndUnknownDispatchFailsWithoutCallingTheGame()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        foreach (TweakDescriptor row in adapter.Descriptors.Where(row => !row.IsAvailable))
            Assert.False(adapter.Apply(row.Id, row.DefaultValue).Success);
        Assert.False(adapter.Apply("unknown", "off").Success);

        Assert.Equal(0, api.TotalMutationCount);
    }

    [Theory]
    [InlineData("prevent_death", SilksongDamageMode.PreventDeath)]
    [InlineData("invincible", SilksongDamageMode.Invincible)]
    public void DamageModesMapToTheTypedApi(string value, SilksongDamageMode expected)
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("damage_received", value);

        Assert.True(result.Success);
        Assert.Equal(expected, api.DamageMode);
        Assert.Equal(0, api.RestoreDamageCount);
    }

    [Fact]
    public void DamageVanillaRestoresCapturedDamageBaseline()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("damage_received", "vanilla");

        Assert.True(result.Success);
        Assert.Equal(1, api.RestoreDamageCount);
    }

    [Theory]
    [InlineData("unlimited_silk", nameof(RecordingApi.UnlimitedSilk))]
    [InlineData("one_hit_kills", nameof(RecordingApi.OneHitKills))]
    [InlineData("equip_anywhere", nameof(RecordingApi.EquipAnywhere))]
    [InlineData("instant_dialogue", nameof(RecordingApi.InstantDialogue))]
    [InlineData("disable_world_rumble", nameof(RecordingApi.WorldRumbleDisabled))]
    [InlineData("ignore_frost_slowdown", nameof(RecordingApi.FrostDisabled))]
    public void BooleanOptionsMapOnAndDefaultToIndividualRestore(string id, string property)
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        Assert.True(adapter.Apply(id, "on").Success);
        Assert.True((bool)typeof(RecordingApi).GetProperty(property)!.GetValue(api)!);

        Assert.True(adapter.Apply(id, "off").Success);
        Assert.Equal(1, api.IndividualRestoreCount(id));
    }

    [Fact]
    public void CaptureAndFullRestoreDelegateToTheTypedApi()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        adapter.CaptureBaseline();
        Assert.True(adapter.Apply("unlimited_silk", "on").Success);
        adapter.RestoreBaseline();

        Assert.Equal(1, api.CaptureCount);
        Assert.Equal(1, api.RestoreAllCount);
        adapter.Tick();
        Assert.Equal(0, api.RefillCount);
    }

    [Fact]
    public void TickRefillsOnlyWhileUnlimitedSilkIsEnabledByThisAdapter()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        adapter.Tick();
        Assert.True(adapter.Apply("unlimited_silk", "on").Success);
        adapter.Tick();
        Assert.True(adapter.Apply("unlimited_silk", "off").Success);
        adapter.Tick();

        Assert.Equal(1, api.RefillCount);
    }

    [Fact]
    public void UnknownIdsAndValuesFailWithoutCallingTheGame()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        Assert.False(adapter.Apply("missing", "on").Success);
        Assert.False(adapter.Apply("one_hit_kills", "maybe").Success);
        Assert.Equal(0, api.TotalMutationCount);
    }

    [Fact]
    public void ActionsFailClosedWhileTypedOwnersAreUnavailable()
    {
        var api = new RecordingApi { IsReady = false };
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("damage_received", "invincible");
        adapter.Tick();

        Assert.False(result.Success);
        Assert.Contains("not ready", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, api.TotalMutationCount);
        Assert.Equal(0, api.RefillCount);
    }

    [Fact]
    public void TypedApiExceptionsBecomeVisibleApplyFailures()
    {
        var api = new RecordingApi { ThrowOnMutation = true };
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("one_hit_kills", "on");

        Assert.False(result.Success);
        Assert.Contains("game rejected", result.Error);
    }

    static void AssertDescriptor(TweakDescriptor row, string id, string defaultValue, params string[] values)
    {
        Assert.Equal(id, row.Id);
        Assert.Equal(defaultValue, row.DefaultValue);
        Assert.Equal(values, row.Values);
    }

    private sealed class RecordingApi : ISilksongTweakApi
    {
        public bool IsReady { get; set; } = true;
        public int CaptureCount { get; private set; }
        public int RestoreAllCount { get; private set; }
        public int RestoreDamageCount { get; private set; }
        public int RestoreSilkCount { get; private set; }
        public int RestoreOneHitCount { get; private set; }
        public int RestoreEquipCount { get; private set; }
        public int RestoreInstantDialogueCount { get; private set; }
        public int RestoreWorldRumbleCount { get; private set; }
        public int RestoreFrostCount { get; private set; }
        public int RefillCount { get; private set; }
        public int TotalMutationCount { get; private set; }
        public SilksongDamageMode? DamageMode { get; private set; }
        public bool UnlimitedSilk { get; private set; }
        public bool OneHitKills { get; private set; }
        public bool EquipAnywhere { get; private set; }
        public bool InstantDialogue { get; private set; }
        public bool WorldRumbleDisabled { get; private set; }
        public bool FrostDisabled { get; private set; }
        public bool ThrowOnMutation { get; set; }

        public void CaptureBaseline() => CaptureCount++;

        public void RestoreBaseline()
        {
            RestoreAllCount++;
            UnlimitedSilk = false;
            OneHitKills = false;
            EquipAnywhere = false;
            InstantDialogue = false;
            WorldRumbleDisabled = false;
            FrostDisabled = false;
        }

        public void SetDamageMode(SilksongDamageMode mode)
        {
            Mutate();
            DamageMode = mode;
        }

        public void RestoreDamageMode()
        {
            Mutate();
            RestoreDamageCount++;
            DamageMode = null;
        }

        public void SetUnlimitedSilk(bool enabled)
        {
            Mutate();
            UnlimitedSilk = enabled;
        }

        public void RestoreUnlimitedSilk()
        {
            Mutate();
            RestoreSilkCount++;
            UnlimitedSilk = false;
        }

        public void SetOneHitKills(bool enabled)
        {
            Mutate();
            OneHitKills = enabled;
        }

        public void RestoreOneHitKills()
        {
            Mutate();
            RestoreOneHitCount++;
            OneHitKills = false;
        }

        public void SetEquipAnywhere(bool enabled)
        {
            Mutate();
            EquipAnywhere = enabled;
        }

        public void RestoreEquipAnywhere()
        {
            Mutate();
            RestoreEquipCount++;
            EquipAnywhere = false;
        }

        public void SetInstantDialogue(bool enabled)
        {
            Mutate();
            InstantDialogue = enabled;
        }

        public void RestoreInstantDialogue()
        {
            Mutate();
            RestoreInstantDialogueCount++;
            InstantDialogue = false;
        }

        public void SetWorldRumbleDisabled(bool enabled)
        {
            Mutate();
            WorldRumbleDisabled = enabled;
        }

        public void RestoreWorldRumbleDisabled()
        {
            Mutate();
            RestoreWorldRumbleCount++;
            WorldRumbleDisabled = false;
        }

        public void SetFrostDisabled(bool enabled)
        {
            Mutate();
            FrostDisabled = enabled;
        }

        public void RestoreFrostDisabled()
        {
            Mutate();
            RestoreFrostCount++;
            FrostDisabled = false;
        }

        public void RefillSilk() => RefillCount++;

        public int IndividualRestoreCount(string id) => id switch
        {
            "unlimited_silk" => RestoreSilkCount,
            "one_hit_kills" => RestoreOneHitCount,
            "equip_anywhere" => RestoreEquipCount,
            "instant_dialogue" => RestoreInstantDialogueCount,
            "disable_world_rumble" => RestoreWorldRumbleCount,
            "ignore_frost_slowdown" => RestoreFrostCount,
            _ => 0,
        };

        void Mutate()
        {
            TotalMutationCount++;
            if (ThrowOnMutation) throw new InvalidOperationException("game rejected mutation");
        }
    }
}
