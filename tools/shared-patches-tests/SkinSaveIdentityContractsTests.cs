using System.Text.Json;
using DualSouls.Skins;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.HollowKnight.Runtime;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SkinSaveIdentityContractsTests
{
    [Fact]
    public void Slot_zero_requires_loaded_lifecycle_pause_load_retain_and_menu_or_new_slot_retire()
    {
        var identity = new SkinSaveIdentity(); var manager = new object();
        Assert.Equal(-1, identity.Sample(manager, 0, false, false));
        Assert.Equal(-1, identity.Sample(manager, 0, false, true));
        Assert.Equal(0, identity.Sample(manager, 0, true, false));
        for (int frame = 0; frame < 1000; frame++) Assert.Equal(0, identity.Sample(manager, 0, false, false));
        Assert.Equal(-1, identity.Sample(manager, 2, false, false));
        Assert.Equal(2, identity.Sample(manager, 2, true, false));
        Assert.Equal(-1, identity.Sample(manager, 2, false, true));
        Assert.Equal(-1, identity.Sample(manager, 5, true, false));
        Assert.Equal(4, identity.Sample(manager, 4, true, false));
        Assert.Equal(-1, identity.Sample(new object(), 0, false, false));
    }

    [Fact]
    public void Native_transport_window_is_event_gated_bounded_and_explicitly_recovers()
    {
        var window = new SkinNativeTransportWindow();
        for (int time = 0; time < 1000; time++) Assert.False(window.Due(time));
        window.Start(1000); Assert.True(window.Due(1000)); Assert.False(window.Due(1000.1f));
        Assert.True(window.Due(1001)); window.Complete(false); Assert.False(window.Due(1002));
        window.Start(2000); Assert.False(window.Due(2060)); Assert.True(window.TimedOut);
        window.Start(2061); Assert.True(window.Due(2061)); Assert.False(window.TimedOut);
        window.Cancel(); Assert.False(window.Due(2062));
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("STALE")]
    [InlineData("UNREADABLE")]
    public void Native_pending_evidence_does_not_reset_deadline_and_terminal_open_menu_has_zero_transport_work(string evidence)
    {
        var window = new SkinNativeTransportWindow();
        window.Start(0);
        for (int second = 0; second < 60; second++) {
            Assert.True(window.Due(second));
            window.CompleteEvidence(evidence);
        }
        Assert.False(window.Due(60)); Assert.True(window.TimedOut);
        window.Start(61); Assert.True(window.Due(61));
        window.CompleteEvidence("PENDING"); Assert.True(window.Due(62));
        window.CompleteEvidence("TERMINAL");
        int bridgeCalls = 0;
        long allocated = -1;
        Exception measurementError = null;
        // Measure settled production work on its own thread, not the xUnit worker.
        // Warm only this terminal path/counter; never re-arm or replace the window.
        var allocationThread = new Thread(() => {
            try {
                for (int frame = 0; frame < 1000; frame++)
                    if (window.Due(63 + frame)) bridgeCalls++;
                _ = GC.GetAllocatedBytesForCurrentThread();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < 100000; frame++)
                    if (window.Due(63 + frame)) bridgeCalls++;
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { measurementError = error; }
        }) { IsBackground = true };
        allocationThread.Start();
        Assert.True(allocationThread.Join(TimeSpan.FromSeconds(30)), "Allocation measurement did not finish.");
        Assert.Null(measurementError);
        Assert.Equal(0, bridgeCalls); Assert.Equal(0, allocated);
        Assert.False(window.Pending); Assert.False(window.TimedOut);
    }

    [Theory]
    [InlineData("hollow-knight")]
    [InlineData("silksong")]
    public void Actual_profile_owner_bounds_retry_publishes_cancelled_and_recovers_without_reload(string profile)
    {
        int reads = 0, reports = 0, restores = 0;
        bool recover = false;
        var request = new SkinLibraryRequest { ProfileId = profile, Mode = "OFF", ConfigSha256 = new string('a',64), SaveSlot = 0 };
        var frame = new SkinDeathFrame(); var ssFrame = new SilksongDeathFrame();
        using var hkDeath = new HollowKnightSkinDeathAdapter(() => frame);
        using var ssDeath = new SilksongSkinDeathAdapter(() => ssFrame);
        SkinLibraryObservation last = null;
        SkinLibraryRequest Read() { reads++; return request; }
        SkinApplyResult Restore() { restores++; return new SkinApplyResult(recover ? SkinApplyStatus.Restored : SkinApplyStatus.RestoreFailed); }
        bool Report(SkinLibraryObservation value) { reports++; last = value; return true; }
        using var hk = profile == "hollow-knight" ? new HollowKnightSkinLibrary(Read, _ => throw new Exception(), Restore,
            Report, null, hkDeath, (_,_) => false, _ => false, saveIdentity: () => 0) : null;
        using var ss = profile == "silksong" ? new SilksongSkinLibrary(Read, _ => throw new Exception(), Restore,
            Report, null, ssDeath, (_,_) => false, (_,_) => false, _ => false, () => 0) : null;
        void Tick(float time) { if (hk != null) hk.Tick(time); else ss.Tick(time); }
        for (int second = 0; second <= 60; second++) Tick(second);
        Assert.True(hk != null ? hk.RecoveryRequired : ss.RecoveryRequired);
        Assert.Equal("Cancelled", last.Status); Assert.Equal(0, last.SaveSlot);
        Assert.False(hk != null ? hk.CanRefresh : ss.CanRefresh);
        int before = reads + reports + restores;
        for (int second = 61; second < 1000; second++) Tick(second);
        Assert.Equal(before, reads + reports + restores);
        recover = true;
        if (hk != null) hk.Invalidate(); else ss.Invalidate();
        Tick(1001);
        Assert.Equal("Restored", last.Status);
        Assert.False(hk != null ? hk.RecoveryRequired : ss.RecoveryRequired);
        Assert.True(hk != null ? hk.CanRefresh : ss.CanRefresh);
    }

    [Theory]
    [InlineData("hollow-knight")]
    [InlineData("silksong")]
    public void Save_edge_rejects_stale_wire_and_apply_completion_even_if_configuration_was_read_before_edge(string profile)
    {
        int slot = 1, applies = 0, reports = 0;
        var request = new SkinLibraryRequest { ProfileId = profile, SaveSlot = 0, Mode = "ON", ConfigSha256 = new string('a',64),
            PackId = "a", TreeSha256 = new string('a',64), Root = "fixture", Textures = new Dictionary<string,string> {
                [profile == "silksong" ? SilksongSkinTargets.All[0].CanonicalPath : "Knight.png"] = "atlas.png" } };
        using var hkDeath = new HollowKnightSkinDeathAdapter(() => new SkinDeathFrame());
        using var ssDeath = new SilksongSkinDeathAdapter(() => new SilksongDeathFrame());
        SkinApplyResult Apply(SkinPack _) { applies++; slot = 2; return new SkinApplyResult(SkinApplyStatus.Applied); }
        bool Report(SkinLibraryObservation _) { reports++; return true; }
        using var hk = profile == "hollow-knight" ? new HollowKnightSkinLibrary(() => request, Apply,
            () => new SkinApplyResult(SkinApplyStatus.Restored), Report, null, hkDeath, (_,_) => false, _ => false, saveIdentity: () => slot) : null;
        using var ss = profile == "silksong" ? new SilksongSkinLibrary(() => request, Apply,
            () => new SkinApplyResult(SkinApplyStatus.Restored), Report, null, ssDeath, (_,_) => false, (_,_) => false, _ => false, () => slot) : null;
        void Tick(float time) { if (hk != null) hk.Tick(time); else ss.Tick(time); }
        Tick(0); Assert.Equal(0, applies); Assert.Equal(0, reports);
        request.SaveSlot = 1; Tick(1); Assert.Equal(1, applies); Assert.Equal(0, reports);
        Tick(2); Assert.Equal(1, applies); Assert.Equal(0, reports);
        request.SaveSlot = 2; request.Mode = "OFF"; Tick(3);
        Assert.Equal(1, reports); Assert.True(hk != null ? hk.CanRefresh : ss.CanRefresh);
    }

    [Theory]
    [InlineData("hollow-knight", false)]
    [InlineData("hollow-knight", true)]
    [InlineData("silksong", false)]
    [InlineData("silksong", true)]
    public void Production_frame_boundary_prevents_old_pack_on_replacement_before_current_save_choice(string profile, bool imported)
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "skin-runtime-chain.json")));
        var fixture = fixtures.RootElement.EnumerateArray().Single(f => f.GetProperty("profile").GetString() == profile);
        var rules = profile == "silksong" ? SilksongSkinTargets.RuntimeRules : HollowKnightSkinPolicy.RuntimeRules;
        string root = Path.Combine(Path.GetTempPath(), "skin-save-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            foreach (var payload in fixture.GetProperty("payloads").EnumerateObject()) {
                string file = Path.Combine(root, payload.Name); Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, Convert.FromBase64String(payload.Value.GetString()));
            }
            string target = profile == "silksong" ? SilksongSkinTargets.All[0].CanonicalPath : "Knight.png";
            var original = new object(); object visible = original;
            var owner = new object();
            var slots = new[] { new SkinSlot(owner, "character", target, () => true, () => visible,
                value => visible = value, (texture, _) => texture.Value) };
            using var session = new SkinRuntimeSession(new Decoder(), () => slots, 128L*1024*1024, rules);
            var request = SkinLibraryTransport.Decode(fixture.GetProperty("a").GetRawText(), rules); request.Root = root;
            int save = 0;
            SkinApplyResult current = new SkinApplyResult(SkinApplyStatus.Unchanged);
            using var hkDeath = new HollowKnightSkinDeathAdapter(() => new SkinDeathFrame());
            using var ssDeath = new SilksongSkinDeathAdapter(() => new SilksongDeathFrame());
            using var hk = profile == "hollow-knight" ? new HollowKnightSkinLibrary(() => request,
                p => current = session.TryApply(p), () => current = session.TryRestore(), _ => true, () => current,
                hkDeath, (_,_) => throw new Exception("ordinary load advanced rotation"), _ => false, saveIdentity: () => save) : null;
            using var ss = profile == "silksong" ? new SilksongSkinLibrary(() => request,
                p => current = session.TryApply(p), () => current = session.TryRestore(), _ => true, () => current,
                ssDeath, (_,_) => throw new Exception("ordinary load advanced rotation"), (_,_) => false, _ => false, () => save) : null;
            void Library(float time) { if (hk != null) hk.Tick(time); else ss.Tick(time); }
            Library(0); Assert.NotSame(original, visible);
            var freshOriginal = new object(); object freshVisible = freshOriginal;
            owner = new object();
            slots = new[] { new SkinSlot(owner, "character", target, () => true, () => freshVisible,
                value => freshVisible = value, (texture, _) => texture.Value) };
            save = 1; request = null; // real async configuration has not completed yet
            void Frame(float time) {
                if (hk != null) hk.AdmitSaveBoundary(); else ss.AdmitSaveBoundary();
                if (hk != null ? hk.SaveVisualRefreshAllowed : ss.SaveVisualRefreshAllowed)
                    current = session.Refresh();
                Library(time);
            }
            Frame(1);
            Assert.Same(freshOriginal, freshVisible);
            request = SkinLibraryTransport.Decode(fixture.GetProperty(imported ? "otherSaveImported" : "otherSaveDefault").GetRawText(), rules);
            if (imported) request.Root = root;
            Frame(2);
            if (imported) { Assert.NotSame(freshOriginal, freshVisible); Assert.Equal(SkinApplyStatus.Applied, current.Status); }
            else { Assert.Same(freshOriginal, freshVisible); Assert.Null(session.CurrentPack); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Kotlin_native_mutations_shared_production_decoder_both_profile_owners_and_same_loaded_session_round_trip()
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "skin-runtime-chain.json")));
        var feedback = new List<object>();
        foreach (var fixture in fixtures.RootElement.EnumerateArray())
        {
            string profile = fixture.GetProperty("profile").GetString();
            var rules = profile == "silksong" ? SilksongSkinTargets.RuntimeRules : HollowKnightSkinPolicy.RuntimeRules;
            string aId = fixture.GetProperty("aId").GetString(), bId = fixture.GetProperty("bId").GetString();
            var native = new NativeSkinMenuModel(new NativeSkinMenuSnapshot(profile, fixture.GetProperty("initialConfig").GetString(),
                "OFF", "ALL", null, Array.Empty<string>(), new[] { new NativeSkinPackDescriptor(aId,"A","Unknown"),
                new NativeSkinPackDescriptor(bId,"B","Unknown") }), 5);
            native.SelectPack(0);
            var selection = native.ConfirmSelected();
            Assert.Equal(NativeSkinMutationKind.ConfirmPack, selection.Kind);
            Assert.Equal(NativeSkinConfirmationIntent.SelectForLater, selection.ConfirmationIntent);
            Assert.Equal(aId, selection.Value);
            Assert.Equal("ON", native.CycleMode(1).Value);
            string root = Path.Combine(Path.GetTempPath(), "skin-production-chain-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (var payload in fixture.GetProperty("payloads").EnumerateObject()) {
                    string file = Path.GetFullPath(Path.Combine(root, payload.Name));
                    Assert.StartsWith(root + Path.DirectorySeparatorChar, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, Convert.FromBase64String(payload.Value.GetString()));
                }
                var owner = new object();
                var targets = profile == "silksong" ? SilksongSkinTargets.All.Select(t => t.CanonicalPath).ToArray() : new[] { "Knight.png", "Hud.png" };
                var original = targets.Select(_ => new object()).ToArray();
                var visible = original.ToArray();
                var slots = targets.Select((target,index) => new SkinSlot(owner, "slot"+index, target, () => true,
                    () => visible[index], value => visible[index] = value, (texture,_) => {
                        if (profile == "silksong") {
                            Assert.True(SilksongSkinTargets.TryGetByPath(target, out var exact));
                            Assert.Equal(exact.Width, texture.Width); Assert.Equal(exact.Height, texture.Height);
                        }
                        return texture.Value;
                    })).ToArray();
                using var session = new SkinRuntimeSession(new Decoder(), () => slots, 128L*1024*1024, rules);
                var request = SkinLibraryTransport.Decode(fixture.GetProperty("a").GetRawText(), rules); request.Root = root;
                int activeSave = 0;
                var frame = new SkinDeathFrame { SaveId = 0 }; var ssFrame = new SilksongDeathFrame();
                using var hkDeath = new HollowKnightSkinDeathAdapter(() => frame);
                using var ssDeath = new SilksongSkinDeathAdapter(() => ssFrame);
                SkinApplyResult current = new SkinApplyResult(SkinApplyStatus.Unchanged);
                SkinLibraryObservation outcome = null;
                bool Report(SkinLibraryObservation value) { outcome = value; return true; }
                using var hk = profile == "hollow-knight" ? new HollowKnightSkinLibrary(() => request,
                    pack => current = session.TryApply(pack), () => current = session.TryRestore(), Report, () => current,
                    hkDeath, (_,_) => throw new Exception("non-death advanced"), _ => false, saveIdentity: () => activeSave) : null;
                using var ss = profile == "silksong" ? new SilksongSkinLibrary(() => request,
                    pack => current = session.TryApply(pack), () => current = session.TryRestore(), Report, () => current,
                    ssDeath, (_,_) => throw new Exception("non-death advanced"), (_,_) => false, _ => false, () => activeSave) : null;
                void Tick(float time) { if (hk != null) hk.Tick(time); else ss.Tick(time); }
                void Invalidate() { if (hk != null) hk.Invalidate(); else ss.Invalidate(); }
                object Outcome() => new { configSha256 = outcome.ConfigSha256, saveSlot = outcome.SaveSlot,
                    status = outcome.Status, activePackId = outcome.ActivePackId ?? "", activeTreeSha256 = outcome.ActiveTreeSha256 ?? "" };
                Tick(0);
                Assert.Equal("Applied", outcome.Status); Assert.Equal(aId, session.CurrentPack.Id);
                Assert.NotSame(original[0], visible[0]); var first = visible[0]; var resultA = Outcome();
                native.Replace(new NativeSkinMenuSnapshot(profile, request.ConfigSha256, "ON", "ALL", aId, Array.Empty<string>(), native.Snapshot.Packs));
                native.SelectPack(1); selection = native.ConfirmSelected();
                Assert.Equal(NativeSkinConfirmationIntent.EnableSole, selection.ConfirmationIntent); Assert.Equal(bId, selection.Value);
                request = SkinLibraryTransport.Decode(fixture.GetProperty("b").GetRawText(), rules); request.Root = root;
                Invalidate(); Tick(1);
                Assert.Equal("Applied", outcome.Status); Assert.Equal(bId, session.CurrentPack.Id); Assert.NotSame(first, visible[0]);
                var resultB = Outcome(); Assert.Same(owner, slots[0].Owner);
                request = SkinLibraryTransport.Decode(fixture.GetProperty("default").GetRawText(), rules);
                Invalidate(); Tick(2);
                Assert.Equal("Restored", outcome.Status); Assert.Null(session.CurrentPack);
                for (int index = 0; index < original.Length; index++) Assert.Same(original[index], visible[index]);
                if (profile == "silksong") Assert.Equal(11, original.Length); // exact SS1.0.29980 canonical vanilla targets
                var resultDefault = Outcome();
                activeSave = 1;
                request = SkinLibraryTransport.Decode(fixture.GetProperty("otherSaveDefault").GetRawText(), rules);
                Tick(3);
                Assert.Equal("Unchanged", outcome.Status); Assert.Equal(1, outcome.SaveSlot);
                Assert.Null(session.CurrentPack); var otherDefault = Outcome();
                request = SkinLibraryTransport.Decode(fixture.GetProperty("otherSaveImported").GetRawText(), rules); request.Root = root;
                Invalidate(); Tick(4);
                Assert.Equal("Applied", outcome.Status); Assert.Equal(1, outcome.SaveSlot);
                Assert.Equal(aId, session.CurrentPack.Id); Assert.Same(owner, slots[0].Owner);
                feedback.Add(new { profile, a = resultA, b = resultB, @default = resultDefault,
                    otherSaveDefault = otherDefault, otherSaveImported = Outcome() });
            }
            finally { Directory.Delete(root, true); }
        }
        var output = Environment.GetEnvironmentVariable("SKIN_RUNTIME_FEEDBACK_OUTPUT");
        if (output != null) File.WriteAllText(output, JsonSerializer.Serialize(feedback));
    }

    sealed class Decoder : ISkinTextureDecoder
    {
        public SkinTexture Decode(byte[] bytes, int width, int height) => new SkinTexture(new object(), width, height, () => {}, () => true);
    }
}
