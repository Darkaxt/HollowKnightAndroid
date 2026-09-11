using System;
using System.Collections.Generic;
using DualSouls.Mods;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakSessionTests
{
    private const string Prefix = "dualsouls.mods.silksong.";

    [Fact]
    public void ReadinessGatesInitializationAndFirstRunMasterDefaultsOff()
    {
        bool ready = false;
        var store = new RecordingStore();
        var adapters = new List<RecordingAdapter>();
        using var session = NewSession(() => ready, store, adapters);

        session.Tick();

        Assert.False(session.IsReady);
        Assert.Empty(adapters);
        Assert.Equal(0, store.ReadCount);

        ready = true;
        session.Tick();

        Assert.True(session.IsReady);
        Assert.False(session.Controller.MasterEnabled);
        Assert.Equal("0", store[Prefix + "master"]);
        Assert.Single(adapters);
        Assert.Equal(1, adapters[0].CaptureCount);
    }

    [Fact]
    public void InitialCaptureFailureRetriesWithoutAttemptingInvalidRestoration()
    {
        var store = EnabledStore();
        var adapters = new List<RecordingAdapter>();
        int owner = 0;
        using var session = new TweakSession(
            () => true,
            () =>
            {
                var adapter = new RecordingAdapter
                {
                    CaptureFailuresRemaining = owner++ == 0 ? 1 : 0,
                    RejectRestoreBeforeCapture = true,
                };
                adapters.Add(adapter);
                return adapter;
            },
            store,
            visibleRows: 5);

        session.Tick();

        Assert.False(session.IsReady);
        Assert.False(session.RestorationPending);
        Assert.Single(adapters);
        Assert.Equal(1, adapters[0].CaptureCount);
        Assert.Equal(0, adapters[0].RestoreCount);
        Assert.Equal(0, adapters[0].InvalidRestoreCount);

        for (int i = 0; i < 59; i++) session.Tick();
        Assert.Single(adapters);

        session.Tick();

        Assert.True(session.IsReady);
        Assert.Equal(2, adapters.Count);
        Assert.Equal(1, adapters[1].CaptureCount);
        Assert.Equal(0, adapters[1].RestoreCount);
        Assert.True(session.Controller.MasterEnabled);
    }

    [Fact]
    public void PersistedApplyFailureRetriesAfterSixtyReadyTicksWithNewOwner()
    {
        var store = EnabledStore();
        var adapters = new List<RecordingAdapter>();
        int owner = 0;
        using var session = new TweakSession(
            () => true,
            () =>
            {
                var adapter = new RecordingAdapter { FailApply = owner++ == 0 };
                adapters.Add(adapter);
                return adapter;
            },
            store,
            visibleRows: 5);

        session.Tick();
        Assert.False(session.IsReady);
        Assert.Single(adapters);
        Assert.Equal(2, adapters[0].RestoreCount);

        for (int i = 0; i < 59; i++) session.Tick();
        Assert.Single(adapters);

        session.Tick();

        Assert.True(session.IsReady);
        Assert.Equal(2, adapters.Count);
        Assert.True(session.Controller.MasterEnabled);
        Assert.Equal(1, adapters[1].ApplyCount);
    }

    [Fact]
    public void FailedRestorationBlocksOwnerReplacementUntilARetrySucceeds()
    {
        var store = EnabledStore();
        var adapters = new List<RecordingAdapter>();
        int owner = 0;
        using var session = new TweakSession(
            () => true,
            () =>
            {
                var adapter = new RecordingAdapter
                {
                    FailApply = owner == 0,
                    RestoreFailuresRemaining = owner++ == 0 ? 2 : 0,
                };
                adapters.Add(adapter);
                return adapter;
            },
            store,
            visibleRows: 5);

        session.Tick();
        Assert.False(session.IsReady);
        Assert.Single(adapters);
        Assert.True(session.RestorationPending);

        session.Tick();
        Assert.Single(adapters);
        Assert.False(session.RestorationPending);

        for (int i = 0; i < 60; i++) session.Tick();

        Assert.True(session.IsReady);
        Assert.Equal(2, adapters.Count);
        Assert.Equal(3, adapters[0].RestoreCount);
    }

    [Fact]
    public void TeardownFailureRetainsRestoreOwnerAndRejectsActionsUntilRetryCompletes()
    {
        var store = EnabledStore();
        var adapters = new List<RecordingAdapter>();
        var session = NewSession(() => true, store, adapters);
        session.Tick();
        adapters[0].RestoreFailuresRemaining = 1;
        int writes = store.WriteCount;

        session.Dispose();

        Assert.True(session.TeardownRequested);
        Assert.False(session.TeardownComplete);
        Assert.True(session.RestorationPending);
        session.Menu.ToggleMaster();
        session.Menu.Reset();
        Assert.Equal(writes, store.WriteCount);
        Assert.Equal(1, adapters[0].RestoreCount);

        session.Tick();

        Assert.True(session.TeardownComplete);
        Assert.False(session.RestorationPending);
        Assert.Equal(2, adapters[0].RestoreCount);
        Assert.Single(adapters);
    }

    [Fact]
    public void PresentationAttachDetachDoesNotDisposeSessionOrCloseMenu()
    {
        var store = EnabledStore();
        var adapters = new List<RecordingAdapter>();
        using var session = NewSession(() => true, store, adapters);
        session.Tick();
        session.Menu.Open();
        int restores = adapters[0].RestoreCount;

        session.SetPresenterAttached(true);
        session.SetPresenterAttached(false);
        session.Tick();

        Assert.True(session.IsReady);
        Assert.True(session.Menu.IsOpen);
        Assert.True(session.Controller.MasterEnabled);
        Assert.Equal(restores, adapters[0].RestoreCount);
    }

    [Fact]
    public void SilksongKeysNeverReadOrWriteHollowKnightProfileState()
    {
        var store = new RecordingStore
        {
            ["dualsouls.mods.hollow-knight.master"] = "1",
            ["dualsouls.mods.hollow-knight.value.proven"] = "on",
        };
        var adapters = new List<RecordingAdapter>();
        using var session = NewSession(() => true, store, adapters);

        session.Tick();

        Assert.False(session.Controller.MasterEnabled);
        Assert.All(store.ReadKeys, key => Assert.StartsWith(Prefix, key));
        Assert.All(store.WriteKeys, key => Assert.StartsWith(Prefix, key));
        Assert.Equal("1", store["dualsouls.mods.hollow-knight.master"]);
        Assert.Equal("on", store["dualsouls.mods.hollow-knight.value.proven"]);
    }

    private static TweakSession NewSession(
        Func<bool> ready,
        RecordingStore store,
        List<RecordingAdapter> adapters)
    {
        return new TweakSession(
            ready,
            () =>
            {
                var adapter = new RecordingAdapter();
                adapters.Add(adapter);
                return adapter;
            },
            store,
            visibleRows: 5);
    }

    private static RecordingStore EnabledStore() => new()
    {
        [Prefix + "master"] = "1",
        [Prefix + "value.proven"] = "on",
    };

    private sealed class RecordingAdapter : ITweakAdapter
    {
        static readonly IReadOnlyList<TweakDescriptor> Rows = new[]
        {
            new TweakDescriptor("proven", "GROUP", "PROVEN", "Proven tweak.", "off", new[] { "off", "on" }),
        };

        public string GameId => "silksong";
        public IReadOnlyList<TweakDescriptor> Descriptors => Rows;
        public bool FailApply { get; set; }
        public int CaptureFailuresRemaining { get; set; }
        public bool RejectRestoreBeforeCapture { get; set; }
        public int RestoreFailuresRemaining { get; set; }
        public int CaptureCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int InvalidRestoreCount { get; private set; }
        public bool BaselineCaptured { get; private set; }

        public void CaptureBaseline()
        {
            CaptureCount++;
            if (CaptureFailuresRemaining > 0)
            {
                CaptureFailuresRemaining--;
                throw new InvalidOperationException("capture failed");
            }
            BaselineCaptured = true;
        }

        public TweakActionResult Apply(string id, string value)
        {
            ApplyCount++;
            return FailApply ? TweakActionResult.Fail("apply failed") : TweakActionResult.Ok();
        }

        public void RestoreBaseline()
        {
            RestoreCount++;
            if (RejectRestoreBeforeCapture && !BaselineCaptured)
            {
                InvalidRestoreCount++;
                throw new InvalidOperationException("baseline was not captured");
            }
            if (RestoreFailuresRemaining > 0)
            {
                RestoreFailuresRemaining--;
                throw new InvalidOperationException("restore failed");
            }
        }

        public void Tick() { }
    }

    private sealed class RecordingStore : Dictionary<string, string>, ITweakStore
    {
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public List<string> ReadKeys { get; } = new();
        public List<string> WriteKeys { get; } = new();

        public string Read(string key)
        {
            ReadCount++;
            ReadKeys.Add(key);
            return TryGetValue(key, out string value) ? value : null;
        }

        public void Write(string key, string value)
        {
            WriteCount++;
            WriteKeys.Add(key);
            this[key] = value;
        }

        public void Flush() { }
    }
}
