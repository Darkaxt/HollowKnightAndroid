using System;
using System.Collections.Generic;
using DualSouls.DualScreen;
using Xunit;

public sealed class DirectDisplayFaultTests
{
    [Theory]
    [InlineData("presentation:true")]
    [InlineData("content:true")]
    [InlineData("touch:true")]
    public void ActivationFailureRollsBackEveryLayerAndSameStateCanRetry(string failedStep)
    {
        var rig = new FaultRig();
        rig.Host.AttachContent(rig.Content);
        rig.Host.SetDisplayPresent(true);
        rig.Events.Clear();
        rig.FailStep = failedStep;

        var failure = Assert.Throws<InvalidOperationException>(
            () => rig.Host.SetPresentationReady(true, 1240f, 1080f));

        Assert.Equal("fault:" + failedStep, failure.Message);
        Assert.False(rig.Host.IsActive);
        Assert.Equal(ExpectedActivationFailure(failedStep), rig.Events);

        rig.FailStep = null;
        rig.Events.Clear();
        rig.Host.SetPresentationReady(true, 1240f, 1080f);

        Assert.True(rig.Host.IsActive);
        Assert.Equal(
            new[] { "presentation:true", "content:true", "touch:true" },
            rig.Events);
    }

    [Theory]
    [InlineData("touch:false", "pause")]
    [InlineData("content:false", "presence")]
    [InlineData("presentation:false", "readiness")]
    public void DeactivationFailureAttemptsEveryLayerAndSameStateCanRetry(
        string failedStep,
        string stateSetter)
    {
        var rig = FaultRig.CreateActiveWithContent();
        rig.Events.Clear();
        rig.FailStep = failedStep;
        Action transition = DeactivationTransition(rig.Host, stateSetter);

        var failure = Assert.Throws<InvalidOperationException>(transition);

        Assert.Equal("fault:" + failedStep, failure.Message);
        Assert.True(rig.Host.IsActive);
        Assert.Equal(
            new[] { "touch:false", "content:false", "presentation:false" },
            rig.Events);

        rig.FailStep = null;
        rig.Events.Clear();
        transition();

        Assert.False(rig.Host.IsActive);
        Assert.Equal(
            new[] { "touch:false", "content:false", "presentation:false" },
            rig.Events);
    }

    [Fact]
    public void ProductDisableFailureCanRetryTheSameDesiredValue()
    {
        var rig = FaultRig.CreateActiveWithContent();
        rig.Events.Clear();
        rig.FailStep = "content:false";

        Assert.Throws<InvalidOperationException>(() => rig.Host.SetEnabled(false));
        Assert.False(rig.Host.IsEnabled);
        Assert.True(rig.Host.IsActive);

        rig.FailStep = null;
        rig.Events.Clear();
        rig.Host.SetEnabled(false);

        Assert.False(rig.Host.IsActive);
        Assert.Equal(
            new[] { "touch:false", "content:false", "presentation:false" },
            rig.Events);
    }

    [Fact]
    public void RecoveredContentDeactivationCommitsHostInactiveState()
    {
        var rig = FaultRig.CreateActiveWithContent();
        rig.FailStep = "content:false";
        Assert.Throws<InvalidOperationException>(() => rig.Host.SetPaused(true));
        Assert.True(rig.Host.IsActive);

        // The content owner completed its independent restoration retry.
        rig.FailStep = null;
        rig.Events.Clear();
        rig.Host.AcknowledgeContentInactiveAndReconcile();

        Assert.False(rig.Host.IsActive);
        Assert.Equal(
            new[] { "touch:false", "content:false", "presentation:false" },
            rig.Events);
    }

    [Fact]
    public void RecoveredContentDeactivationReactivatesIfDesiredStateResumed()
    {
        var rig = FaultRig.CreateActiveWithContent();
        rig.FailStep = "content:false";
        Assert.Throws<InvalidOperationException>(() => rig.Host.SetPaused(true));
        rig.Host.SetPaused(false);
        Assert.True(rig.Host.IsActive);

        rig.FailStep = null;
        rig.Events.Clear();
        rig.Host.AcknowledgeContentInactiveAndReconcile();

        Assert.True(rig.Host.IsActive);
        Assert.Equal(
            new[] { "presentation:true", "content:true", "touch:true" },
            rig.Events);
    }

    [Fact]
    public void SynchronousActivationRequestFailureRearmsThePresenceGeneration()
    {
        var rig = new FaultRig { FailStep = "activation:request" };

        var failure = Assert.Throws<InvalidOperationException>(
            () => rig.Host.SetDisplayPresent(true));

        Assert.Equal("fault:activation:request", failure.Message);
        Assert.True(rig.Host.DisplayPresent);
        Assert.Equal(1, rig.ActivationRequests);

        rig.FailStep = null;
        rig.Events.Clear();
        rig.Host.SetDisplayPresent(true);

        Assert.Equal(2, rig.ActivationRequests);
        Assert.Equal(new[] { "activation:request" }, rig.Events);
    }

    [Fact]
    public void GeometryFailureDoesNotCommitAndSamePublicationCanRetry()
    {
        var rig = new FaultRig();
        rig.Host.AttachContent(rig.Content);
        rig.Host.SetDisplayPresent(true);
        rig.Events.Clear();
        rig.FailStep = "content:geometry";

        Assert.Throws<InvalidOperationException>(
            () => rig.Host.SetPresentationReady(true, 1240f, 1080f));

        Assert.False(rig.Host.PresentationReady);
        Assert.False(rig.Host.IsActive);
        Assert.Equal(1, rig.Content.GeometryCalls);

        rig.FailStep = null;
        rig.Events.Clear();
        rig.Host.SetPresentationReady(true, 1240f, 1080f);

        Assert.True(rig.Host.PresentationReady);
        Assert.True(rig.Host.IsActive);
        Assert.Equal(2, rig.Content.GeometryCalls);
        Assert.Equal(
            new[]
            {
                "content:geometry",
                "presentation:true",
                "content:true",
                "touch:true",
            },
            rig.Events);
    }

    [Fact]
    public void FailedLateAttachmentDoesNotTakeOwnership()
    {
        var rig = FaultRig.CreateActiveWithoutContent();
        var failedContent = rig.CreateContent();
        rig.Events.Clear();
        rig.FailStep = "content:geometry";

        Assert.Throws<InvalidOperationException>(
            () => rig.Host.AttachContent(failedContent));

        Assert.True(rig.Host.IsActive);
        Assert.Equal(0, failedContent.DisposeCount);

        rig.FailStep = null;
        rig.Events.Clear();
        var healthyContent = rig.CreateContent();
        rig.Host.AttachContent(healthyContent);
        rig.Host.Dispose();

        Assert.Equal(0, failedContent.DisposeCount);
        Assert.Equal(1, healthyContent.DisposeCount);
    }

    [Theory]
    [InlineData("touch:false")]
    [InlineData("content:false")]
    [InlineData("presentation:false")]
    [InlineData("content:dispose")]
    public void DisposeRunsEveryLaterCleanupAfterAnEarlierFailure(string failedStep)
    {
        var rig = FaultRig.CreateActiveWithContent();
        rig.Events.Clear();
        rig.FailStep = failedStep;

        Assert.Throws<InvalidOperationException>(() => rig.Host.Dispose());

        Assert.True(rig.Host.IsDisposed);
        Assert.Equal(
            new[]
            {
                "touch:false",
                "content:false",
                "presentation:false",
                "content:dispose",
                "presentation:release",
            },
            rig.Events);

        int eventCount = rig.Events.Count;
        rig.Host.Dispose();
        Assert.Equal(eventCount, rig.Events.Count);
    }

    static IReadOnlyList<string> ExpectedActivationFailure(string failedStep)
    {
        var expected = new List<string> { "content:geometry" };
        foreach (var step in new[] { "presentation:true", "content:true", "touch:true" })
        {
            expected.Add(step);
            if (step == failedStep) break;
        }
        expected.Add("touch:false");
        expected.Add("content:false");
        expected.Add("presentation:false");
        return expected;
    }

    static Action DeactivationTransition(DirectDisplayHost host, string stateSetter)
    {
        switch (stateSetter)
        {
            case "pause": return () => host.SetPaused(true);
            case "presence": return () => host.SetDisplayPresent(false);
            case "readiness": return () => host.SetPresentationReady(false);
            default: throw new ArgumentOutOfRangeException(nameof(stateSetter));
        }
    }

    sealed class FaultRig
    {
        public readonly List<string> Events = new List<string>();
        public readonly DirectDisplayHost Host;
        public readonly FaultContent Content;

        public string FailStep { get; set; }
        public int ActivationRequests { get; private set; }

        public FaultRig()
        {
            Content = CreateContent();
            Host = new DirectDisplayHost(
                requestActivation: () =>
                {
                    ActivationRequests++;
                    Step("activation:request");
                },
                setPresentationVisible: active => Step("presentation:" + Lower(active)),
                setTouchFenceActive: active => Step("touch:" + Lower(active)),
                releasePresentation: () => Step("presentation:release"));
        }

        public static FaultRig CreateActiveWithContent()
        {
            var rig = new FaultRig();
            rig.Host.AttachContent(rig.Content);
            rig.Host.SetDisplayPresent(true);
            rig.Host.SetPresentationReady(true, 1240f, 1080f);
            Assert.True(rig.Host.IsActive);
            return rig;
        }

        public static FaultRig CreateActiveWithoutContent()
        {
            var rig = new FaultRig();
            rig.Host.SetDisplayPresent(true);
            rig.Host.SetPresentationReady(true, 1240f, 1080f);
            Assert.True(rig.Host.IsActive);
            return rig;
        }

        public FaultContent CreateContent()
        {
            return new FaultContent(this);
        }

        public void Step(string step)
        {
            Events.Add(step);
            if (FailStep == step)
                throw new InvalidOperationException("fault:" + step);
        }

        static string Lower(bool value) => value ? "true" : "false";
    }

    sealed class FaultContent : IDirectDisplayContent
    {
        readonly FaultRig _rig;

        public int GeometryCalls { get; private set; }
        public int DisposeCount { get; private set; }

        public FaultContent(FaultRig rig)
        {
            _rig = rig;
        }

        public void SetTransportActive(bool active)
        {
            _rig.Step("content:" + (active ? "true" : "false"));
        }

        public void OnPanelGeometry(float width, float height)
        {
            GeometryCalls++;
            _rig.Step("content:geometry");
        }

        public void Dispose()
        {
            DisposeCount++;
            _rig.Step("content:dispose");
        }
    }
}
