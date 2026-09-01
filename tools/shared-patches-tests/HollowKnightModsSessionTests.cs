using System;
using System.Collections.Generic;
using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using Xunit;

namespace SharedPatches.Tests;

public sealed class HollowKnightModsSessionTests
{
    private const string Prefix = "dualsouls.mods.hollow-knight.";

    [Fact]
    public void WaitsForApiReadinessWithoutCapturingApplyingOrReadingStore()
    {
        var api = new RecordingApi { IsReady = false };
        var store = new RecordingStore
        {
            [Prefix + "master"] = "1",
            [Prefix + "value.companion_backdrop"] = "black",
        };
        using var session = new HollowKnightModsSession(api, store, visibleRows: 5);

        session.Tick();
        session.Tick();

        Assert.False(session.IsReady);
        Assert.Equal(string.Empty, session.LastError);
        Assert.Empty(api.Calls);
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(5, session.Menu.VisibleRows);
    }

    [Fact]
    public void InitializesOnceAndAppliesPersistedMasterAndAvailableSelections()
    {
        var api = new RecordingApi();
        var store = new RecordingStore
        {
            [Prefix + "master"] = "1",
            [Prefix + "value.companion_backdrop"] = "black",
            [Prefix + "value.lifeblood_flash"] = "vanilla",
        };
        using var session = new HollowKnightModsSession(api, store, visibleRows: 5);

        session.Tick();
        session.Tick();
        session.Tick();

        Assert.True(session.IsReady);
        Assert.Equal(string.Empty, session.LastError);
        Assert.True(session.Controller.MasterEnabled);
        Assert.Equal("black", session.Controller.Value("companion_backdrop"));
        Assert.Equal("vanilla", session.Controller.Value("lifeblood_flash"));
        Assert.Equal(new[] { "capture", "backdrop:True", "flash:Vanilla" }, api.Calls);
    }

    [Fact]
    public void TransientInitializationFailureRecoversAfterReadyTickBackoffWithFreshPipeline()
    {
        const int retryReadyTicks = 60;
        var api = new RecordingApi { BackdropFailuresRemaining = 1 };
        var store = new RecordingStore
        {
            [Prefix + "master"] = "1",
            [Prefix + "value.companion_backdrop"] = "black",
        };
        using var session = new HollowKnightModsSession(api, store, visibleRows: 5);

        session.Tick();
        TweakController failedController = session.Controller;
        TweakMenuModel failedMenu = session.Menu;
        string initializationError = session.LastError;

        api.IsReady = false;
        for (int i = 0; i < retryReadyTicks * 2; i++) session.Tick();
        Assert.Equal(1, api.CaptureCount);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal("1", store[Prefix + "master"]);
        Assert.Equal("black", store[Prefix + "value.companion_backdrop"]);
        Assert.Equal(initializationError, session.LastError);

        api.IsReady = true;
        for (int i = 0; i < retryReadyTicks - 1; i++) session.Tick();
        Assert.False(session.IsReady);
        Assert.Equal(1, api.CaptureCount);
        Assert.Equal(initializationError, session.LastError);

        session.Tick();

        Assert.True(session.IsReady);
        Assert.Equal(string.Empty, session.LastError);
        Assert.Equal(2, api.CaptureCount);
        Assert.Equal(2, api.RestoreCount);
        Assert.NotSame(failedController, session.Controller);
        Assert.NotSame(failedMenu, session.Menu);
        Assert.True(session.Controller.MasterEnabled);
        Assert.Equal("black", session.Controller.Value("companion_backdrop"));
        Assert.Equal(
            new[]
            {
                "capture", "backdrop:True", "restore", "restore",
                "capture", "backdrop:True",
            },
            api.Calls);
    }

    [Fact]
    public void PersistentInitializationFailureRetriesOnlyAtReadyTickBoundaries()
    {
        const int retryReadyTicks = 60;
        var api = new RecordingApi { AlwaysFailCapture = true };
        using var session = new HollowKnightModsSession(
            api, new RecordingStore(), visibleRows: 5);

        session.Tick();
        string firstError = session.LastError;
        for (int i = 0; i < retryReadyTicks - 1; i++) session.Tick();

        Assert.False(session.IsReady);
        Assert.Equal(1, api.CaptureCount);
        Assert.Equal(firstError, session.LastError);

        session.Tick();
        string secondError = session.LastError;
        for (int i = 0; i < retryReadyTicks - 1; i++) session.Tick();

        Assert.False(session.IsReady);
        Assert.Equal(2, api.CaptureCount);
        Assert.Equal(2, api.RestoreCount);
        Assert.Equal(secondError, session.LastError);
    }

    [Fact]
    public void PresenterAttachDetachDoesNotChangeActiveSettingsOrPersistence()
    {
        var api = new RecordingApi();
        var store = new RecordingStore
        {
            [Prefix + "master"] = "1",
            [Prefix + "value.companion_backdrop"] = "black",
        };
        using var session = new HollowKnightModsSession(api, store, visibleRows: 5);
        session.Tick();
        session.Menu.Open();
        int writesAfterInitialization = store.WriteCount;
        int flushesAfterInitialization = store.FlushCount;

        session.SetPresenterAttached(true);
        session.SetPresenterAttached(true);
        session.Tick();
        session.SetPresenterAttached(false);
        session.SetPresenterAttached(false);
        session.Tick();

        Assert.True(session.IsReady);
        Assert.True(session.Controller.MasterEnabled);
        Assert.True(session.Menu.IsOpen);
        Assert.Equal("black", session.Controller.Value("companion_backdrop"));
        Assert.Equal(new[] { "capture", "backdrop:True" }, api.Calls);
        Assert.Equal(writesAfterInitialization, store.WriteCount);
        Assert.Equal(flushesAfterInitialization, store.FlushCount);
    }

    [Fact]
    public void DisposeRestoresExactlyOnceWithoutChangingPersistedState()
    {
        var api = new RecordingApi();
        var store = new RecordingStore
        {
            [Prefix + "master"] = "1",
            [Prefix + "value.companion_backdrop"] = "black",
        };
        var session = new HollowKnightModsSession(api, store, visibleRows: 5);
        session.Tick();
        int writesAfterInitialization = store.WriteCount;
        int flushesAfterInitialization = store.FlushCount;

        session.Dispose();
        session.Dispose();
        session.SetPresenterAttached(false);
        session.Tick();

        Assert.Equal(new[] { "capture", "backdrop:True", "restore" }, api.Calls);
        Assert.Equal(writesAfterInitialization, store.WriteCount);
        Assert.Equal(flushesAfterInitialization, store.FlushCount);
        Assert.Equal("1", store[Prefix + "master"]);
        Assert.Equal("black", store[Prefix + "value.companion_backdrop"]);
        Assert.True(session.Controller.MasterEnabled);
        Assert.Equal("black", session.Controller.Value("companion_backdrop"));
    }

    [Fact]
    public void ActionsAfterDisposeCannotMutateGameOrPersistedSettings()
    {
        var api = new RecordingApi();
        var store = new RecordingStore
        {
            [Prefix + "master"] = "1",
            [Prefix + "value.companion_backdrop"] = "black",
        };
        var session = new HollowKnightModsSession(api, store, visibleRows: 5);
        session.Tick();
        session.Dispose();
        string[] callsAfterDispose = api.Calls.ToArray();
        var valuesAfterDispose = new Dictionary<string, string>(store);
        int writesAfterDispose = store.WriteCount;
        int flushesAfterDispose = store.FlushCount;

        session.Menu.CycleSelected();
        session.Menu.ToggleMaster();
        session.Menu.Reset();
        session.Tick();

        Assert.Equal(callsAfterDispose, api.Calls);
        Assert.Equal(valuesAfterDispose, store);
        Assert.Equal(writesAfterDispose, store.WriteCount);
        Assert.Equal(flushesAfterDispose, store.FlushCount);
    }

    [Fact]
    public void DisposeBeforeInitializationDoesNotRestoreAndPreventsLaterInitialization()
    {
        var api = new RecordingApi { IsReady = false };
        var store = new RecordingStore();
        var session = new HollowKnightModsSession(api, store, visibleRows: 5);

        session.Dispose();
        api.IsReady = true;
        session.Tick();
        session.SetPresenterAttached(true);

        Assert.False(session.IsReady);
        Assert.Empty(api.Calls);
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void ConstructorValidatesDependenciesAndVisibleRows()
    {
        var api = new RecordingApi();
        var store = new RecordingStore();

        Assert.Throws<ArgumentNullException>(() => new HollowKnightModsSession(null!, store, 5));
        Assert.Throws<ArgumentNullException>(() => new HollowKnightModsSession(api, null!, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HollowKnightModsSession(api, store, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HollowKnightModsSession(api, store, -1));
    }

    private sealed class RecordingApi : IHollowKnightTweakApi
    {
        public bool IsReady { get; set; } = true;
        public int BackdropFailuresRemaining { get; set; }
        public bool AlwaysFailCapture { get; set; }
        public int CaptureCount { get; private set; }
        public int RestoreCount { get; private set; }
        public List<string> Calls { get; } = new();

        public void CaptureBaseline()
        {
            CaptureCount++;
            Calls.Add("capture");
            if (AlwaysFailCapture)
                throw new InvalidOperationException("capture failed");
        }

        public void RestoreBaseline()
        {
            RestoreCount++;
            Calls.Add("restore");
        }

        public void SetCompanionBackdropBlack(bool black)
        {
            Calls.Add($"backdrop:{black}");
            if (BackdropFailuresRemaining > 0)
            {
                BackdropFailuresRemaining--;
                throw new InvalidOperationException("backdrop failed");
            }
        }

        public void SetLifebloodFlash(HollowKnightFlashMode mode) =>
            Calls.Add($"flash:{mode}");
    }

    private sealed class RecordingStore : Dictionary<string, string>, ITweakStore
    {
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int FlushCount { get; private set; }

        public string Read(string key)
        {
            ReadCount++;
            return TryGetValue(key, out string value) ? value : null;
        }

        public void Write(string key, string value)
        {
            WriteCount++;
            this[key] = value;
        }

        public void Flush()
        {
            FlushCount++;
        }
    }
}
