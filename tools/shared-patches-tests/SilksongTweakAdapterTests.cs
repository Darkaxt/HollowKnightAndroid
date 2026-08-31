using DualSouls.Mods;
using DualSouls.Mods.Silksong;
using Xunit;

namespace SharedPatches.Tests;

public sealed class SilksongTweakAdapterTests
{
    [Fact]
    public void DescriptorsExposeOnlyProvenInitialCapabilities()
    {
        var adapter = new SilksongTweakAdapter(new RecordingApi());

        Assert.Equal("silksong", adapter.GameId);
        Assert.Collection(
            adapter.Descriptors,
            row => AssertDescriptor(row, "damage_received", "vanilla", "vanilla", "prevent_death", "invincible"),
            row => AssertDescriptor(row, "unlimited_silk", "off", "off", "on"),
            row => AssertDescriptor(row, "one_hit_kills", "off", "off", "on"),
            row => AssertDescriptor(row, "equip_anywhere", "off", "off", "on"));
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
        public int CaptureCount { get; private set; }
        public int RestoreAllCount { get; private set; }
        public int RestoreDamageCount { get; private set; }
        public int RestoreSilkCount { get; private set; }
        public int RestoreOneHitCount { get; private set; }
        public int RestoreEquipCount { get; private set; }
        public int RefillCount { get; private set; }
        public int TotalMutationCount { get; private set; }
        public SilksongDamageMode? DamageMode { get; private set; }
        public bool UnlimitedSilk { get; private set; }
        public bool OneHitKills { get; private set; }
        public bool EquipAnywhere { get; private set; }
        public bool ThrowOnMutation { get; set; }

        public void CaptureBaseline() => CaptureCount++;

        public void RestoreBaseline()
        {
            RestoreAllCount++;
            UnlimitedSilk = false;
            OneHitKills = false;
            EquipAnywhere = false;
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

        public void RefillSilk() => RefillCount++;

        public int IndividualRestoreCount(string id) => id switch
        {
            "unlimited_silk" => RestoreSilkCount,
            "one_hit_kills" => RestoreOneHitCount,
            "equip_anywhere" => RestoreEquipCount,
            _ => 0,
        };

        void Mutate()
        {
            TotalMutationCount++;
            if (ThrowOnMutation) throw new InvalidOperationException("game rejected mutation");
        }
    }
}
