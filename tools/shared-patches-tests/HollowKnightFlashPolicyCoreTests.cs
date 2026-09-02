using DualSouls.Mods.HollowKnight;
using Xunit;

namespace SharedPatches.Tests;

public sealed class HollowKnightFlashPolicyCoreTests
{
    [Fact]
    public void SoftClampsPeakAndNeverLiftsNativeFade()
    {
        HollowKnightFlashSample nativePeak = Sample(true, 1f);
        var tracker = new HollowKnightFlashStateTracker(nativePeak);

        HollowKnightFlashTransition peak = tracker.Apply(
            nativePeak,
            Master(HollowKnightFlashMode.Soft));
        HollowKnightFlashSample softened = Apply(nativePeak, peak);

        Assert.False(peak.WriteEnabled);
        Assert.True(peak.WriteColor);
        Assert.Equal(HollowKnightFlashDecision.DefaultSoftAlpha, softened.Color.A);

        HollowKnightFlashSample nativeFade = Sample(true, 0.20f);
        HollowKnightFlashTransition faded = tracker.Apply(
            nativeFade,
            Master(HollowKnightFlashMode.Soft));

        Assert.False(faded.HasWrites);
        Assert.Equal(0.20f, faded.Sample.Color.A);
    }

    [Fact]
    public void VanillaRestoresOnceThenStopsWriting()
    {
        HollowKnightFlashSample native = Sample(true, 1f);
        var tracker = new HollowKnightFlashStateTracker(native);
        HollowKnightFlashSample softened = Apply(
            native,
            tracker.Apply(native, Master(HollowKnightFlashMode.Soft)));

        HollowKnightFlashTransition restore = tracker.Apply(
            softened,
            Master(HollowKnightFlashMode.Vanilla));
        HollowKnightFlashSample vanilla = Apply(softened, restore);

        Assert.False(restore.WriteEnabled);
        Assert.True(restore.WriteColor);
        Assert.Equal(1f, vanilla.Color.A);

        HollowKnightFlashTransition steady = tracker.Apply(
            vanilla,
            Master(HollowKnightFlashMode.Vanilla));
        Assert.False(steady.HasWrites);
    }

    [Fact]
    public void OffOwnsEnabledOnlyAndRetainsNativeColorUpdates()
    {
        HollowKnightFlashSample native = Sample(true, 1f, 0.1f);
        var tracker = new HollowKnightFlashStateTracker(native);

        HollowKnightFlashTransition off = tracker.Apply(
            native,
            Master(HollowKnightFlashMode.Off));
        HollowKnightFlashSample disabled = Apply(native, off);

        Assert.True(off.WriteEnabled);
        Assert.False(off.Sample.Enabled);
        Assert.False(off.WriteColor);

        HollowKnightFlashSample nativeColorUpdate = Sample(false, 0.40f, 0.8f);
        HollowKnightFlashTransition maintained = tracker.Apply(
            nativeColorUpdate,
            Master(HollowKnightFlashMode.Off));
        Assert.False(maintained.HasWrites);

        HollowKnightFlashTransition release = tracker.Release(nativeColorUpdate);
        HollowKnightFlashSample restored = Apply(nativeColorUpdate, release);
        Assert.True(release.WriteEnabled);
        Assert.False(release.WriteColor);
        Assert.True(restored.Enabled);
        Assert.Equal(nativeColorUpdate.Color, restored.Color);
    }

    [Fact]
    public void OffToSoftRestoresGameEnabledAndUsesLatestNativeColor()
    {
        HollowKnightFlashSample native = Sample(true, 1f, 0.1f);
        var tracker = new HollowKnightFlashStateTracker(native);
        HollowKnightFlashSample disabled = Apply(
            native,
            tracker.Apply(native, Master(HollowKnightFlashMode.Off)));
        HollowKnightFlashSample nativeFade = Sample(disabled.Enabled, 0.20f, 0.8f);
        tracker.Apply(nativeFade, Master(HollowKnightFlashMode.Off));

        HollowKnightFlashTransition soft = tracker.Apply(
            nativeFade,
            Master(HollowKnightFlashMode.Soft));
        HollowKnightFlashSample restored = Apply(nativeFade, soft);

        Assert.True(soft.WriteEnabled);
        Assert.True(restored.Enabled);
        Assert.False(soft.WriteColor);
        Assert.Equal(nativeFade.Color, restored.Color);
    }

    [Theory]
    [InlineData(HollowKnightFlashMode.Soft)]
    [InlineData(HollowKnightFlashMode.Off)]
    public void OwnershipReleaseRestoresOnceAndIsIdempotent(
        HollowKnightFlashMode mode)
    {
        HollowKnightFlashSample native = Sample(true, 1f);
        var tracker = new HollowKnightFlashStateTracker(native);
        HollowKnightFlashSample policyOutput = Apply(
            native,
            tracker.Apply(native, Master(mode)));

        HollowKnightFlashTransition release = tracker.Apply(
            policyOutput,
            HollowKnightFlashDecision.None);
        HollowKnightFlashSample restored = Apply(policyOutput, release);
        HollowKnightFlashTransition repeated = tracker.Release(restored);

        Assert.True(release.HasWrites);
        Assert.Equal(native, restored);
        Assert.False(repeated.HasWrites);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FinalNativeFieldUpdatesAreReconciledBeforeRelease(int updateKind)
    {
        HollowKnightFlashSample native = Sample(true, 1f, 0.1f);
        var tracker = new HollowKnightFlashStateTracker(native);
        HollowKnightFlashSample softened = Apply(
            native,
            tracker.Apply(native, Master(HollowKnightFlashMode.Soft)));

        bool enabled = updateKind == 0 ? softened.Enabled : false;
        HollowKnightFlashRgba color = updateKind == 1
            ? softened.Color
            : Color(0.20f, 0.8f);
        var finalNative = new HollowKnightFlashSample(enabled, color);

        HollowKnightFlashTransition release = tracker.Release(finalNative);
        HollowKnightFlashSample restored = Apply(finalNative, release);

        Assert.Equal(enabled, restored.Enabled);
        Assert.Equal(updateKind == 1 ? native.Color : color, restored.Color);
        if (updateKind == 1)
        {
            Assert.False(release.WriteEnabled);
            Assert.True(release.WriteColor);
        }
        else
        {
            Assert.False(release.HasWrites);
        }
    }

    [Fact]
    public void FreshBindingStartsWithoutStateFromDestroyedBinding()
    {
        HollowKnightFlashSample oldNative = Sample(true, 1f);
        var oldTracker = new HollowKnightFlashStateTracker(oldNative);
        HollowKnightFlashSample oldDisabled = Apply(
            oldNative,
            oldTracker.Apply(oldNative, Master(HollowKnightFlashMode.Off)));
        Assert.False(oldDisabled.Enabled);

        HollowKnightFlashSample replacementNative = Sample(false, 0.10f, 0.9f);
        var replacementTracker = new HollowKnightFlashStateTracker(replacementNative);
        HollowKnightFlashTransition replacement = replacementTracker.Apply(
            replacementNative,
            Master(HollowKnightFlashMode.Soft));

        Assert.False(replacement.HasWrites);
        Assert.Equal(replacementNative, replacement.Sample);
    }

    private static HollowKnightFlashDecision Master(HollowKnightFlashMode mode)
    {
        string value = mode == HollowKnightFlashMode.Soft
            ? "soft"
            : mode == HollowKnightFlashMode.Off ? "off" : "vanilla";
        return HollowKnightFlashDecisionResolver.Resolve(
            true,
            true,
            value,
            HollowKnightFlashMode.Soft,
            0.17f);
    }

    private static HollowKnightFlashSample Sample(
        bool enabled,
        float alpha,
        float red = 0.25f)
    {
        return new HollowKnightFlashSample(enabled, Color(alpha, red));
    }

    private static HollowKnightFlashRgba Color(float alpha, float red)
    {
        return new HollowKnightFlashRgba(red, 0.5f, 0.75f, alpha);
    }

    private static HollowKnightFlashSample Apply(
        HollowKnightFlashSample live,
        HollowKnightFlashTransition transition)
    {
        return new HollowKnightFlashSample(
            transition.WriteEnabled ? transition.Sample.Enabled : live.Enabled,
            transition.WriteColor ? transition.Sample.Color : live.Color);
    }
}
