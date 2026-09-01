using DualSouls.DualScreen;
using Xunit;

public sealed class DirectDisplayGestureTrackerTests
{
    [Fact]
    public void DownEdgeAndCleanReleaseMatchTheReferenceGestureContract()
    {
        var tracker = new DirectDisplayGestureTracker();

        tracker.Update(new[] { new DirectDisplayContact(7, 0.25f, 0.40f) }, 10f);

        Assert.Equal(1, tracker.TapSequence);
        Assert.Equal(1, tracker.TouchCount);
        Assert.Equal(0.25f, tracker.TouchX);
        Assert.Equal(0.40f, tracker.TouchY);

        tracker.Update(System.Array.Empty<DirectDisplayContact>(), 10.2f);

        Assert.Equal(1, tracker.CleanTapSequence);
        Assert.Equal(0.25f, tracker.CleanTapX);
        Assert.Equal(0.40f, tracker.CleanTapY);
        Assert.Equal(0, tracker.TouchCount);
    }

    [Fact]
    public void DragPinchAndCancelNeverPublishACleanTap()
    {
        var tracker = new DirectDisplayGestureTracker();
        tracker.Update(new[] { new DirectDisplayContact(1, 0.1f, 0.1f) }, 1f);
        tracker.Update(new[] { new DirectDisplayContact(1, 0.2f, 0.1f) }, 1.1f);
        tracker.Update(System.Array.Empty<DirectDisplayContact>(), 1.2f);
        Assert.Equal(0, tracker.CleanTapSequence);

        tracker.Update(new[] { new DirectDisplayContact(2, 0.3f, 0.3f) }, 2f);
        tracker.Update(
            new[]
            {
                new DirectDisplayContact(2, 0.3f, 0.3f),
                new DirectDisplayContact(3, 0.7f, 0.7f),
            },
            2.1f);
        tracker.Update(System.Array.Empty<DirectDisplayContact>(), 2.2f);
        Assert.Equal(0, tracker.CleanTapSequence);

        tracker.Update(new[] { new DirectDisplayContact(4, 0.5f, 0.5f) }, 3f);
        tracker.Update(System.Array.Empty<DirectDisplayContact>(), 3.1f, canceled: true);
        Assert.Equal(0, tracker.CleanTapSequence);
    }

    [Fact]
    public void ActiveContactsPreserveTwoPointerCoordinates()
    {
        var tracker = new DirectDisplayGestureTracker();
        tracker.Update(
            new[]
            {
                new DirectDisplayContact(9, 0.2f, 0.3f),
                new DirectDisplayContact(10, 0.8f, 0.9f),
            },
            5f);

        Assert.Equal(2, tracker.TouchCount);
        Assert.Equal(0.2f, tracker.T0X);
        Assert.Equal(0.3f, tracker.T0Y);
        Assert.Equal(0.8f, tracker.T1X);
        Assert.Equal(0.9f, tracker.T1Y);
    }

    [Fact]
    public void TransportCancellationDropsTheOldGestureBeforeResume()
    {
        var tracker = new DirectDisplayGestureTracker();
        tracker.Update(new[] { new DirectDisplayContact(1, 0.2f, 0.3f) }, 1f);

        tracker.Cancel();
        tracker.Update(new[] { new DirectDisplayContact(2, 0.7f, 0.8f) }, 2f);
        tracker.Update(System.Array.Empty<DirectDisplayContact>(), 2.1f);

        Assert.Equal(2, tracker.TapSequence);
        Assert.Equal(1, tracker.CleanTapSequence);
        Assert.Equal(0.7f, tracker.CleanTapX);
        Assert.Equal(0.8f, tracker.CleanTapY);
    }
}
