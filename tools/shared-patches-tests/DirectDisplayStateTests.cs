using System;
using System.Collections.Generic;
using DualSouls.DualScreen;
using Xunit;

public sealed class DirectDisplayStateTests
{
    [Fact]
    public void RequestsActivationOncePerPresenceGeneration()
    {
        var recording = new RecordingTransport();

        recording.Host.SetDisplayPresent(true);
        recording.Host.SetDisplayPresent(true);

        Assert.Equal(1, recording.ActivationRequests);

        recording.Host.SetDisplayPresent(false);
        recording.Host.SetDisplayPresent(false);
        recording.Host.SetDisplayPresent(true);
        recording.Host.SetDisplayPresent(true);

        Assert.Equal(2, recording.ActivationRequests);
    }

    [Fact]
    public void RemainsInactiveUntilPresentationIsReady()
    {
        var recording = new RecordingTransport();
        var content = recording.AttachContent();

        recording.Host.SetDisplayPresent(true);

        Assert.False(recording.Host.IsActive);
        Assert.True(recording.Host.IsFallback);
        Assert.Equal(new[] { "activation:request" }, recording.Events);

        recording.Host.SetPresentationReady(true, 1240f, 1080f);

        Assert.True(recording.Host.IsActive);
        Assert.False(recording.Host.IsFallback);
        Assert.Equal((1240f, 1080f), Assert.Single(content.Geometries));
        Assert.Equal(
            new[]
            {
                "activation:request",
                "content:geometry:1240x1080",
                "presentation:true",
                "content:true",
                "touch:true",
            },
            recording.Events);
    }

    [Fact]
    public void PauseAndResumeApplyTheOrderedActiveState()
    {
        var recording = RecordingTransport.CreateActive();
        recording.Events.Clear();

        recording.Host.SetPaused(true);

        Assert.False(recording.Host.IsActive);
        Assert.Equal(
            new[] { "touch:false", "content:false", "presentation:false" },
            recording.Events);

        recording.Host.SetPaused(false);

        Assert.True(recording.Host.IsActive);
        Assert.Equal(
            new[]
            {
                "touch:false",
                "content:false",
                "presentation:false",
                "presentation:true",
                "content:true",
                "touch:true",
            },
            recording.Events);
    }

    [Fact]
    public void DisplayLossDeactivatesAndFallsBack()
    {
        var recording = RecordingTransport.CreateActive();
        recording.Events.Clear();

        recording.Host.SetDisplayPresent(false);

        Assert.False(recording.Host.IsActive);
        Assert.False(recording.Host.PresentationReady);
        Assert.True(recording.Host.IsFallback);
        Assert.Equal(
            new[] { "touch:false", "content:false", "presentation:false" },
            recording.Events);
    }

    [Fact]
    public void ReactivationWaitsForTheNewPresentationReadiness()
    {
        var recording = RecordingTransport.CreateActive();
        recording.Host.SetDisplayPresent(false);
        recording.Events.Clear();

        recording.Host.SetDisplayPresent(true);

        Assert.False(recording.Host.IsActive);
        Assert.Equal(new[] { "activation:request" }, recording.Events);

        recording.Host.SetPresentationReady(true, 1200f, 900f);

        Assert.True(recording.Host.IsActive);
        Assert.Equal(
            new[]
            {
                "activation:request",
                "content:geometry:1200x900",
                "presentation:true",
                "content:true",
                "touch:true",
            },
            recording.Events);
    }

    [Fact]
    public void SingleDisplayStartupIsANoOp()
    {
        var recording = new RecordingTransport();

        recording.Host.SetDisplayPresent(false);
        recording.Host.SetDisplayPresent(false);

        Assert.False(recording.Host.IsActive);
        Assert.True(recording.Host.IsFallback);
        Assert.Equal(0, recording.ActivationRequests);
        Assert.Empty(recording.Events);
    }

    [Fact]
    public void TeardownIsOrderedAndIdempotent()
    {
        var recording = RecordingTransport.CreateActive();
        var content = recording.Content;
        recording.Events.Clear();

        recording.Host.Dispose();
        recording.Host.Dispose();

        Assert.True(recording.Host.IsDisposed);
        Assert.Equal(1, content.DisposeCount);
        Assert.Equal(
            new[]
            {
                "touch:false",
                "content:false",
                "presentation:false",
                "content:dispose",
                "presentation:release",
            },
            recording.Events);
    }

    sealed class RecordingTransport
    {
        public readonly List<string> Events = new List<string>();
        public readonly DirectDisplayHost Host;

        public RecordingContent Content { get; private set; }
        public int ActivationRequests { get; private set; }

        public RecordingTransport()
        {
            Host = new DirectDisplayHost(
                requestActivation: () =>
                {
                    ActivationRequests++;
                    Events.Add("activation:request");
                },
                setPresentationVisible: active => Events.Add("presentation:" + Lower(active)),
                setTouchFenceActive: active => Events.Add("touch:" + Lower(active)),
                releasePresentation: () => Events.Add("presentation:release"));
        }

        public static RecordingTransport CreateActive()
        {
            var recording = new RecordingTransport();
            recording.AttachContent();
            recording.Host.SetDisplayPresent(true);
            recording.Host.SetPresentationReady(true, 1240f, 1080f);
            Assert.True(recording.Host.IsActive);
            return recording;
        }

        public RecordingContent AttachContent()
        {
            Content = new RecordingContent(Events);
            Host.AttachContent(Content);
            return Content;
        }

        static string Lower(bool value) => value ? "true" : "false";
    }

    sealed class RecordingContent : IDirectDisplayContent
    {
        readonly List<string> _events;

        public readonly List<(float Width, float Height)> Geometries =
            new List<(float Width, float Height)>();

        public int DisposeCount { get; private set; }

        public RecordingContent(List<string> events)
        {
            _events = events;
        }

        public void SetTransportActive(bool active)
        {
            _events.Add("content:" + (active ? "true" : "false"));
        }

        public void OnPanelGeometry(float width, float height)
        {
            Geometries.Add((width, height));
            _events.Add("content:geometry:" + width + "x" + height);
        }

        public void Dispose()
        {
            DisposeCount++;
            _events.Add("content:dispose");
        }
    }
}
