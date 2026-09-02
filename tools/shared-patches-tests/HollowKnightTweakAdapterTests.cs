using System;
using System.Collections.Generic;
using System.Linq;
using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using Xunit;

namespace SharedPatches.Tests;

public sealed class HollowKnightTweakAdapterTests
{
    private static readonly string[] DeferredIds =
    {
        "damage_received",
        "nail_damage",
        "one_hit_kills",
        "run_speed",
        "unlimited_soul",
        "charm_costs",
        "unlimited_notches",
        "equip_anywhere",
        "geo_multiplier",
        "keep_geo_on_death",
        "journal_one_kill",
        "auto_map",
        "health_bars",
        "damage_numbers",
        "boss_retry",
        "secret_radar",
        "bench_teleport",
        "state_slots",
    };

    private static readonly string[] DeferredGroups =
    {
        "COMBAT",
        "COMBAT",
        "COMBAT",
        "PLAYER",
        "PLAYER",
        "CHARMS",
        "CHARMS",
        "CHARMS",
        "ECONOMY",
        "ECONOMY",
        "JOURNAL",
        "WORLD",
        "WORLD",
        "WORLD",
        "WORLD",
        "WORLD",
        "WORLD",
        "STATE",
    };

    [Fact]
    public void CatalogHasExactGameIdAndOrder()
    {
        var adapter = new HollowKnightTweakAdapter(new RecordingApi());
        string[] expectedIds = { "companion_backdrop", "lifeblood_flash" };

        Assert.Equal("hollow-knight", adapter.GameId);
        Assert.Equal(expectedIds.Concat(DeferredIds), adapter.Descriptors.Select(row => row.Id));
    }

    [Fact]
    public void CatalogStartsWithExactAvailablePresentationRows()
    {
        var adapter = new HollowKnightTweakAdapter(new RecordingApi());

        AssertDescriptor(
            adapter.Descriptors[0],
            "companion_backdrop",
            "PRESENTATION",
            "COMPANION BACKDROP",
            "Choose the accepted dimmed scenery wash or a black lower-screen backdrop.",
            "dimmed",
            "dimmed",
            "black");
        AssertDescriptor(
            adapter.Descriptors[1],
            "lifeblood_flash",
            "PRESENTATION",
            "LIFEBLOOD FLASH",
            "Use the accepted softened flash, the original flash, or no flash.",
            "soft",
            "soft",
            "vanilla",
            "off");
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
    public void EveryAllowedPresentationValueHasOneExactDispatch()
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
                ["soft"] = "flash:Soft",
                ["vanilla"] = "flash:Vanilla",
                ["off"] = "flash:Off",
            },
        };
        var catalog = new HollowKnightTweakAdapter(new RecordingApi());
        TweakDescriptor[] available = catalog.Descriptors.Where(row => row.IsAvailable).ToArray();

        Assert.Equal(expected.Keys, available.Select(row => row.Id));
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

    [Fact]
    public void DeferredRowsHaveExactUniqueTrackingMapAndRemainVisible()
    {
        var adapter = new HollowKnightTweakAdapter(new RecordingApi());
        var deferredRows = adapter.Descriptors.Skip(2).ToArray();
        string[] expectedTrackingIds = Enumerable.Range(1, 18)
            .Select(number => $"HKMOD-{number:000}")
            .ToArray();

        Assert.Equal(DeferredIds, deferredRows.Select(row => row.Id));
        Assert.Equal(DeferredGroups, deferredRows.Select(row => row.Group));
        Assert.Equal(expectedTrackingIds, deferredRows.Select(row => row.TrackingId));
        Assert.Equal(expectedTrackingIds.Length, deferredRows.Select(row => row.TrackingId).Distinct().Count());
        Assert.All(deferredRows, row =>
        {
            Assert.False(row.IsAvailable);
            Assert.Equal("off", row.DefaultValue);
            Assert.Equal(new[] { "off" }, row.Values);
            Assert.False(string.IsNullOrWhiteSpace(row.Title));
            Assert.Equal(row.Title.ToUpperInvariant(), row.Title);
            Assert.False(string.IsNullOrWhiteSpace(row.Description));
            Assert.False(string.IsNullOrWhiteSpace(row.UnavailableReason));
        });
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
    public void EveryDeferredDirectCallReportsTrackingReasonWithoutCallingApi()
    {
        var api = new RecordingApi();
        var adapter = new HollowKnightTweakAdapter(api);

        foreach (TweakDescriptor row in adapter.Descriptors.Skip(2))
        {
            TweakActionResult result = adapter.Apply(row.Id, row.DefaultValue);

            Assert.False(result.Success);
            Assert.Contains(row.TrackingId, result.Error);
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

    private static void AssertDescriptor(
        TweakDescriptor row,
        string id,
        string group,
        string title,
        string description,
        string defaultValue,
        params string[] values)
    {
        Assert.Equal(id, row.Id);
        Assert.Equal(group, row.Group);
        Assert.Equal(title, row.Title);
        Assert.Equal(description, row.Description);
        Assert.Equal(defaultValue, row.DefaultValue);
        Assert.Equal(values, row.Values);
        Assert.True(row.IsAvailable);
        Assert.Equal(string.Empty, row.TrackingId);
        Assert.Equal(string.Empty, row.UnavailableReason);
    }

    private sealed class RecordingApi : IHollowKnightTweakApi
    {
        public bool IsReady { get; set; } = true;
        public bool ThrowOnMutation { get; set; }
        public List<string> Calls { get; } = new();

        public void CaptureBaseline() => Calls.Add("capture");

        public void RestoreBaseline() => Calls.Add("restore");

        public void SetCompanionBackdropBlack(bool black)
        {
            Record($"backdrop:{black}");
        }

        public void SetLifebloodFlash(HollowKnightFlashMode mode)
        {
            Record($"flash:{mode}");
        }

        private void Record(string call)
        {
            Calls.Add(call);
            if (ThrowOnMutation) throw new InvalidOperationException("game rejected presentation change");
        }
    }
}
