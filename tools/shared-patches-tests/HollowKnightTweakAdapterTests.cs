using System;
using System.Collections.Generic;
using System.Linq;
using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using Xunit;

namespace SharedPatches.Tests;

public sealed class HollowKnightTweakAdapterTests
{
    private static readonly string[] RequiredContractIds =
    {
        "skins", "black_background",
        "run_speed", "fast_transitions", "auto_map", "innate_compass", "bench_teleport", "secret_radar",
        "nail_damage", "damage_taken", "damage_cap", "one_hit_kills", "unlimited_soul",
        "enemy_health_bars", "damage_numbers", "boss_retry",
        "equip_anywhere", "charm_costs", "unlimited_notches",
        "state_slot", "save_to_slot", "load_from_slot", "delete_slot",
        "geo_magnet", "keep_geo_on_death", "journal_one_kill", "geo_multiplier",
    };

    [Fact]
    public void CatalogStartsWithExactRequiredContractAndAppendsExtras()
    {
        var rows = new HollowKnightTweakAdapter(new RecordingApi()).Descriptors;
        TweakDescriptor[] required = rows.Take(27).ToArray();

        Assert.Equal(RequiredContractIds, required.Select(row => row.ContractId));
        Assert.Equal(
            new[] { "GENERAL", "GENERAL" }
                .Concat(Enumerable.Repeat("WORLD", 6))
                .Concat(Enumerable.Repeat("COMBAT", 5))
                .Concat(Enumerable.Repeat("ENCOUNTERS", 3))
                .Concat(Enumerable.Repeat("CHARMS", 3))
                .Concat(Enumerable.Repeat("SAVE STATES", 4))
                .Concat(Enumerable.Repeat("ECONOMY", 4)),
            required.Select(row => row.Group));
        Assert.Equal(TweakControlKind.Route, required[0].ControlKind);
        Assert.Equal(TweakControlKind.Route, required[6].ControlKind);
        Assert.Equal(TweakControlKind.Command, required[20].ControlKind);
        Assert.Equal(TweakControlKind.Command, required[21].ControlKind);
        Assert.Equal(TweakControlKind.Command, required[22].ControlKind);
        Assert.All(required.Where((_, index) => index is not (0 or 6 or 20 or 21 or 22)),
            row => Assert.Equal(TweakControlKind.Choice, row.ControlKind));
        Assert.Equal("companion_backdrop", required.Single(row => row.ContractId == "black_background").Id);
        Assert.Equal("damage_received", required.Single(row => row.ContractId == "damage_taken").Id);
        Assert.Equal("health_bars", required.Single(row => row.ContractId == "enemy_health_bars").Id);
        Assert.Equal(new[] { "lifeblood_flash" }, rows.Skip(27).Select(row => row.Id));
        Assert.Equal(6, required.Count(row => row.IsAvailable));
        Assert.All(required.Where(row => !row.IsAvailable), row =>
            Assert.False(string.IsNullOrWhiteSpace(row.UnavailableReason)));
        Assert.DoesNotContain(rows, row => row.Id == "state_slots");
    }

    [Fact]
    public void DamageReceivedAppliesNoMaskLossThroughTypedApi()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakDescriptor row = adapter.Descriptors.Single(item => item.Id == "damage_received");
        TweakActionResult result = adapter.Apply("damage_received", "no_mask_loss");

        Assert.True(row.IsAvailable);
        Assert.Equal(new[] { "vanilla", "no_mask_loss", "invincible" }, row.Values);
        Assert.True(result.Success);
        Assert.Equal(new[] { "damage:NoMaskLoss" }, api.Calls);
    }

    [Fact]
    public void NailDamageAppliesMultiplierThroughTypedApi()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakDescriptor row = adapter.Descriptors.Single(item => item.Id == "nail_damage");
        TweakActionResult result = adapter.Apply("nail_damage", "x3");

        Assert.True(row.IsAvailable);
        Assert.Equal(new[] { "x1", "x2", "x3", "x5" }, row.Values);
        Assert.True(result.Success);
        Assert.Equal(new[] { "nail:3" }, api.Calls);
    }

    [Fact]
    public void OneHitKillsEnablesThroughTypedApi()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakDescriptor row = adapter.Descriptors.Single(item => item.Id == "one_hit_kills");
        TweakActionResult result = adapter.Apply("one_hit_kills", "on");

        Assert.True(row.IsAvailable);
        Assert.Equal(new[] { "off", "on" }, row.Values);
        Assert.True(result.Success);
        Assert.Equal(new[] { "one-hit:True" }, api.Calls);
    }

    [Fact]
    public void RunSpeedAppliesMultiplierThroughTypedApi()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakDescriptor row = adapter.Descriptors.Single(item => item.Id == "run_speed");
        TweakActionResult result = adapter.Apply("run_speed", "plus_50");

        Assert.True(row.IsAvailable);
        Assert.Equal(new[] { "vanilla", "plus_25", "plus_50" }, row.Values);
        Assert.True(result.Success);
        Assert.Equal(new[] { "run:1.5" }, api.Calls);
    }

    [Fact]
    public void UnlimitedSoulEnablesThroughTypedApi()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakDescriptor row = adapter.Descriptors.Single(item => item.Id == "unlimited_soul");
        TweakActionResult result = adapter.Apply("unlimited_soul", "on");

        Assert.True(row.IsAvailable);
        Assert.Equal(new[] { "off", "on" }, row.Values);
        Assert.True(result.Success);
        Assert.Equal(new[] { "soul:True" }, api.Calls);
    }

    [Fact]
    public void CatalogRejectsMutationThroughListInterfaceAndKeepsOrder()
    {
        var adapter = new HollowKnightTweakAdapter(new RecordingApi());
        var rows = Assert.IsAssignableFrom<IList<TweakDescriptor>>(adapter.Descriptors);
        string[] expectedOrder = adapter.Descriptors.Select(row => row.Id).ToArray();
        TweakDescriptor first = rows[0];
        Exception mutationError = null;

        try
        {
            mutationError = Record.Exception(() => rows[0] = rows[1]);
        }
        finally
        {
            if (!ReferenceEquals(rows[0], first)) rows[0] = first;
        }

        Assert.IsType<NotSupportedException>(mutationError);
        Assert.Equal(expectedOrder, adapter.Descriptors.Select(row => row.Id));
    }

    [Fact]
    public void EveryAllowedValueHasOneExactTypedDispatch()
    {
        var expected = new Dictionary<string, Dictionary<string, string>>
        {
            ["companion_backdrop"] = new()
            {
                ["dimmed"] = "backdrop:False",
                ["black"] = "backdrop:True",
            },
            ["lifeblood_flash"] = new()
            {
                ["vanilla"] = "flash:Vanilla",
                ["soft"] = "flash:Soft",
                ["off"] = "flash:Off",
            },
            ["damage_received"] = new()
            {
                ["vanilla"] = "damage:restore",
                ["no_mask_loss"] = "damage:NoMaskLoss",
                ["invincible"] = "damage:Invincible",
            },
            ["nail_damage"] = new()
            {
                ["x1"] = "nail:restore",
                ["x2"] = "nail:2",
                ["x3"] = "nail:3",
                ["x5"] = "nail:5",
            },
            ["one_hit_kills"] = new()
            {
                ["off"] = "one-hit:restore",
                ["on"] = "one-hit:True",
            },
            ["run_speed"] = new()
            {
                ["vanilla"] = "run:restore",
                ["plus_25"] = "run:1.25",
                ["plus_50"] = "run:1.5",
            },
            ["unlimited_soul"] = new()
            {
                ["off"] = "soul:restore",
                ["on"] = "soul:True",
            },
        };
        var catalog = new HollowKnightTweakAdapter(new RecordingApi());
        TweakDescriptor[] available = catalog.Descriptors.Where(row => row.IsAvailable).ToArray();

        Assert.Equal(expected.Keys.OrderBy(id => id), available.Select(row => row.Id).OrderBy(id => id));
        foreach (TweakDescriptor row in available)
        {
            Assert.Equal(expected[row.Id].Keys, row.Values);
            foreach (string value in row.Values)
            {
                var api = new RecordingApi();
                var adapter = new HollowKnightTweakAdapter(api);

                TweakActionResult result = adapter.Apply(row.Id, value);

                Assert.True(result.Success);
                Assert.Equal(new[] { expected[row.Id][value] }, api.Calls);
            }
        }
    }

    [Theory]
    [InlineData("dimmed", false)]
    [InlineData("black", true)]
    public void CompanionBackdropValuesMapToTypedApiIncludingDefault(string value, bool expectedBlack)
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakActionResult result = adapter.Apply("companion_backdrop", value);

        Assert.True(result.Success);
        Assert.Equal(new[] { $"backdrop:{expectedBlack}" }, api.Calls);
    }

    [Theory]
    [InlineData("soft", HollowKnightFlashMode.Soft)]
    [InlineData("vanilla", HollowKnightFlashMode.Vanilla)]
    [InlineData("off", HollowKnightFlashMode.Off)]
    public void LifebloodFlashValuesMapToTypedApiIncludingDefault(string value, HollowKnightFlashMode expectedMode)
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakActionResult result = adapter.Apply("lifeblood_flash", value);

        Assert.True(result.Success);
        Assert.Equal(new[] { $"flash:{expectedMode}" }, api.Calls);
    }

    [Fact]
    public void MasterDefaultSoftIgnoresLegacyAlphaAcrossDisplayLoss()
    {
        HollowKnightFlashDecision withDisplay = HollowKnightFlashDecisionResolver.Resolve(
            true,
            true,
            "soft",
            HollowKnightFlashMode.Soft,
            0.17f);
        HollowKnightFlashDecision afterDisplayLoss = HollowKnightFlashDecisionResolver.Resolve(
            true,
            true,
            "soft",
            null,
            null);

        AssertMasterDecision(withDisplay, HollowKnightFlashMode.Soft);
        AssertMasterDecision(afterDisplayLoss, HollowKnightFlashMode.Soft);
        Assert.Equal(HollowKnightFlashDecision.DefaultSoftAlpha, withDisplay.SoftAlpha);
        Assert.Equal(withDisplay.SoftAlpha, afterDisplayLoss.SoftAlpha);
    }

    [Theory]
    [InlineData("soft", HollowKnightFlashMode.Soft)]
    [InlineData("vanilla", HollowKnightFlashMode.Vanilla)]
    [InlineData("off", HollowKnightFlashMode.Off)]
    public void ReadyEnabledMasterMapsEveryControllerValue(
        string value,
        HollowKnightFlashMode expected)
    {
        HollowKnightFlashDecision resolved = HollowKnightFlashDecisionResolver.Resolve(
            true,
            true,
            value,
            HollowKnightFlashMode.Soft,
            0.17f);

        AssertMasterDecision(resolved, expected);
        Assert.Equal(HollowKnightFlashDecision.DefaultSoftAlpha, resolved.SoftAlpha);
    }

    [Fact]
    public void MasterOffUsesLiveLegacyModeAndAlpha()
    {
        HollowKnightFlashDecision resolved = HollowKnightFlashDecisionResolver.Resolve(
            true,
            false,
            "off",
            HollowKnightFlashMode.Soft,
            0.17f);

        Assert.True(resolved.HasOwner);
        Assert.Equal(HollowKnightFlashAuthority.Legacy, resolved.Authority);
        Assert.Equal(HollowKnightFlashMode.Soft, resolved.Mode);
        Assert.Equal(0.17f, resolved.SoftAlpha);
    }

    [Fact]
    public void SessionNotReadyUsesLiveLegacyMode()
    {
        HollowKnightFlashDecision resolved = HollowKnightFlashDecisionResolver.Resolve(
            false,
            true,
            "off",
            HollowKnightFlashMode.Vanilla,
            0.17f);

        Assert.True(resolved.HasOwner);
        Assert.Equal(HollowKnightFlashAuthority.Legacy, resolved.Authority);
        Assert.Equal(HollowKnightFlashMode.Vanilla, resolved.Mode);
    }

    [Fact]
    public void NoMasterAndNoLiveReferenceReleasesOwnership()
    {
        HollowKnightFlashDecision resolved = HollowKnightFlashDecisionResolver.Resolve(
            true,
            false,
            "soft",
            null,
            0.17f);

        Assert.False(resolved.HasOwner);
        Assert.Equal(HollowKnightFlashAuthority.None, resolved.Authority);
    }

    [Fact]
    public void MasterEnabledOffRemainsOwnedWithoutLegacyReference()
    {
        HollowKnightFlashDecision resolved = HollowKnightFlashDecisionResolver.Resolve(
            true,
            true,
            "off",
            null,
            null);

        AssertMasterDecision(resolved, HollowKnightFlashMode.Off);
    }

    [Fact]
    public void InvalidMasterValueFailsClosedToVanilla()
    {
        HollowKnightFlashDecision resolved = HollowKnightFlashDecisionResolver.Resolve(
            true,
            true,
            "unexpected",
            HollowKnightFlashMode.Soft,
            0.17f);

        AssertMasterDecision(resolved, HollowKnightFlashMode.Vanilla);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(float.NaN, HollowKnightFlashDecision.DefaultSoftAlpha)]
    [InlineData(float.PositiveInfinity, HollowKnightFlashDecision.DefaultSoftAlpha)]
    public void LegacySoftAlphaIsClampedSafely(float alpha, float expected)
    {
        HollowKnightFlashDecision resolved = HollowKnightFlashDecisionResolver.Resolve(
            false,
            false,
            null,
            HollowKnightFlashMode.Soft,
            alpha);

        Assert.Equal(expected, resolved.SoftAlpha);
    }

    [Theory]
    [InlineData("damage_received")]
    [InlineData("nail_damage")]
    [InlineData("one_hit_kills")]
    [InlineData("run_speed")]
    [InlineData("unlimited_soul")]
    public void GameplayFeatureDirectDefaultRestoresItsBaseline(string id)
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);
        TweakDescriptor row = adapter.Descriptors.Single(item => item.Id == id);

        Assert.True(adapter.Apply(id, row.Values[1]).Success);
        Assert.Contains(id, api.ActiveGameplay);

        Assert.True(adapter.Apply(id, row.DefaultValue).Success);
        Assert.DoesNotContain(id, api.ActiveGameplay);
    }

    [Theory]
    [InlineData("damage_received")]
    [InlineData("nail_damage")]
    [InlineData("one_hit_kills")]
    [InlineData("run_speed")]
    [InlineData("unlimited_soul")]
    public void GameplayFeatureMasterOffRestoresCapturedBaseline(string id)
    {
        var api = new RecordingApi();
        var controller = new TweakController(
            new HollowKnightTweakAdapter(api), new MemoryStore());
        Assert.True(controller.Initialize().Success);
        Assert.True(controller.SetMaster(true).Success);
        Assert.True(controller.Cycle(id).Success);
        Assert.Contains(id, api.ActiveGameplay);

        Assert.True(controller.SetMaster(false).Success);

        Assert.Empty(api.ActiveGameplay);
        Assert.Equal("restore", api.Calls.Last());
    }

    [Theory]
    [InlineData("damage_received")]
    [InlineData("nail_damage")]
    [InlineData("one_hit_kills")]
    [InlineData("run_speed")]
    [InlineData("unlimited_soul")]
    public void GameplayFeatureResetRestoresCapturedBaseline(string id)
    {
        var api = new RecordingApi();
        var controller = new TweakController(
            new HollowKnightTweakAdapter(api), new MemoryStore());
        Assert.True(controller.Initialize().Success);
        Assert.True(controller.SetMaster(true).Success);
        Assert.True(controller.Cycle(id).Success);
        Assert.Contains(id, api.ActiveGameplay);

        Assert.True(controller.Reset().Success);

        Assert.Empty(api.ActiveGameplay);
        Assert.Equal("restore", api.Calls.Last());
        Assert.Equal(
            controller.Descriptors.Single(item => item.Id == id).DefaultValue,
            controller.Value(id));
    }

    [Theory]
    [InlineData("damage_received")]
    [InlineData("nail_damage")]
    [InlineData("one_hit_kills")]
    [InlineData("run_speed")]
    [InlineData("unlimited_soul")]
    public void GameplayFeatureSessionTeardownRestoresCapturedBaseline(string id)
    {
        var api = new RecordingApi();
        var session = new HollowKnightModsSession(api, new MemoryStore(), visibleRows: 5);
        session.Tick();
        Assert.True(session.Controller.SetMaster(true).Success);
        Assert.True(session.Controller.Cycle(id).Success);
        Assert.Contains(id, api.ActiveGameplay);

        session.Dispose();

        Assert.True(session.TeardownComplete);
        Assert.Empty(api.ActiveGameplay);
        Assert.Equal("restore", api.Calls.Last());
    }

    [Fact]
    public void CaptureApplyAndRestorePreserveApiCallOrder()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        adapter.CaptureBaseline();
        Assert.True(adapter.Apply("companion_backdrop", "black").Success);
        adapter.RestoreBaseline();

        Assert.Equal(new[] { "capture", "backdrop:True", "restore" }, api.Calls);
    }

    [Theory]
    [InlineData("missing", "off")]
    [InlineData("companion_backdrop", "DIMMED")]
    [InlineData("lifeblood_flash", "none")]
    public void UnknownIdsAndValuesFailWithoutCallingApi(string id, string value)
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        TweakActionResult result = adapter.Apply(id, value);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void ApiExceptionBecomesContextualApplyFailure()
    {
        var api = new RecordingApi { ThrowOnMutation = true };
        var adapter = new HollowKnightTweakAdapter(api);

        TweakActionResult result = adapter.Apply("companion_backdrop", "black");

        Assert.False(result.Success);
        Assert.Contains("companion_backdrop", result.Error);
        Assert.Contains("game rejected presentation change", result.Error);
        Assert.Equal(new[] { "backdrop:True" }, api.Calls);
    }

    [Fact]
    public void EveryUnavailableDirectCallReportsReasonWithoutCallingApi()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        foreach (TweakDescriptor row in adapter.Descriptors.Where(item => !item.IsAvailable))
        {
            TweakActionResult result = adapter.Apply(row.Id, row.DefaultValue);

            Assert.False(result.Success);
            Assert.Contains("currently unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(row.UnavailableReason, result.Error);
        }

        Assert.Empty(api.Calls);
    }

    [Fact]
    public void NotReadyApiRejectsAvailableChangeWithoutMutation()
    {
        var api = new RecordingApi { IsReady = false };
        var adapter = new HollowKnightTweakAdapter(api);

        TweakActionResult result = adapter.Apply("lifeblood_flash", "soft");

        Assert.False(result.Success);
        Assert.Contains("not ready", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void TickPerformsNoApiMutation()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        adapter.Tick();

        Assert.Empty(api.Calls);
    }

    [Fact]
    public void ConstructorRejectsNullApi()
    {
        Assert.Throws<ArgumentNullException>(() => new HollowKnightTweakAdapter(null!));
    }

    private static void AssertMasterDecision(
        HollowKnightFlashDecision decision,
        HollowKnightFlashMode expectedMode)
    {
        Assert.True(decision.HasOwner);
        Assert.Equal(HollowKnightFlashAuthority.Master, decision.Authority);
        Assert.Equal(expectedMode, decision.Mode);
    }

    private sealed class RecordingApi : IHollowKnightTweakApi
    {
        public bool IsReady { get; set; } = true;
        public bool ThrowOnMutation { get; set; }
        public List<string> Calls { get; } = new();
        public HashSet<string> ActiveGameplay { get; } = new();

        public void CaptureBaseline() => Calls.Add("capture");

        public void RestoreBaseline()
        {
            Calls.Add("restore");
            ActiveGameplay.Clear();
        }

        public void SetCompanionBackdropBlack(bool black)
        {
            Record($"backdrop:{black}");
        }

        public void SetLifebloodFlash(HollowKnightFlashMode mode)
        {
            Record($"flash:{mode}");
        }

        public void SetDamageMode(HollowKnightDamageMode mode)
        {
            Record($"damage:{mode}");
            ActiveGameplay.Add("damage_received");
        }

        public void RestoreDamageMode()
        {
            Record("damage:restore");
            ActiveGameplay.Remove("damage_received");
        }

        public void SetNailDamageMultiplier(int multiplier)
        {
            Record($"nail:{multiplier}");
            ActiveGameplay.Add("nail_damage");
        }

        public void RestoreNailDamage()
        {
            Record("nail:restore");
            ActiveGameplay.Remove("nail_damage");
        }

        public void SetOneHitKills(bool enabled)
        {
            Record($"one-hit:{enabled}");
            if (enabled) ActiveGameplay.Add("one_hit_kills");
        }

        public void RestoreOneHitKills()
        {
            Record("one-hit:restore");
            ActiveGameplay.Remove("one_hit_kills");
        }

        public void SetRunSpeedMultiplier(float multiplier)
        {
            Record($"run:{multiplier}");
            ActiveGameplay.Add("run_speed");
        }

        public void RestoreRunSpeed()
        {
            Record("run:restore");
            ActiveGameplay.Remove("run_speed");
        }

        public void SetUnlimitedSoul(bool enabled)
        {
            Record($"soul:{enabled}");
            if (enabled) ActiveGameplay.Add("unlimited_soul");
        }

        public void RestoreUnlimitedSoul()
        {
            Record("soul:restore");
            ActiveGameplay.Remove("unlimited_soul");
        }

        public void TickGameplay()
        {
        }

        private void Record(string call)
        {
            Calls.Add(call);
            if (ThrowOnMutation) throw new InvalidOperationException("game rejected presentation change");
        }
    }

    private sealed class MemoryStore : Dictionary<string, string>, ITweakStore
    {
        public string Read(string key) => TryGetValue(key, out string value) ? value : null;

        public void Write(string key, string value)
        {
            this[key] = value;
        }

        public void Flush()
        {
        }
    }
}
