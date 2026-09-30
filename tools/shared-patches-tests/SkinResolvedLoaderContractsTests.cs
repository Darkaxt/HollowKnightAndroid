using DualSouls.Skins.HollowKnight.Runtime;
using DualSouls.Skins.Runtime;
using DualSouls.Skins.Silksong.Runtime;
using Xunit;

public sealed class SkinResolvedLoaderContractsTests
{
    [Theory]
    [InlineData("hollow-knight")]
    [InlineData("silksong")]
    public void Mode_is_not_loader_or_cache_identity_and_imported_successor_is_atomic(string profile)
    {
        using var rig = new Rig(profile);
        var packs = new List<SkinPack>();
        var controller = rig.Controller(pack => { packs.Add(pack); return rig.Apply(pack); });
        controller.Tick();
        object a = rig.Character;
        rig.Request.Mode = "ROTATE";
        controller.Tick();
        Assert.Single(packs);
        Assert.Same(a, rig.Character);
        rig.Request.Mode = "ON";
        controller.Tick();
        Assert.Single(packs);

        rig.Request.Mode = "OFF";
        controller.Tick();
        rig.Request.Mode = "ROTATE";
        controller.Tick();
        Assert.Equal(2, packs.Count);
        Assert.Same(packs[0], packs[1]);
        rig.Restores = 0;
        rig.Writes.Clear();
        rig.Select("b", 'b');
        rig.Request.PendingOccurrence = 1;
        controller.Tick();
        Assert.Equal(0, rig.Restores);
        Assert.Equal("b", rig.Session.CurrentPack.Id);
        Assert.Equal(2, rig.Writes.Count);
        Assert.DoesNotContain(rig.Writes, value => ReferenceEquals(value, rig.OriginalCharacter) ||
            ReferenceEquals(value, rig.OriginalHud));

        rig.Default();
        controller.Tick();
        Assert.Equal(1, rig.Restores);
        Assert.Null(rig.Session.CurrentPack);
        Assert.Same(rig.OriginalCharacter, rig.Character);
        Assert.Same(rig.OriginalHud, rig.Hud);
    }

    [Theory]
    [InlineData("hollow-knight")]
    [InlineData("silksong")]
    public void Loaded_owner_scope_and_failed_successor_keep_exact_originals_and_rollback(string profile)
    {
        using var rig = new Rig(profile);
        var controller = rig.Controller(rig.Apply);
        controller.Tick();
        object a = rig.Character, hudA = rig.Hud;
        rig.Select("b", 'b');
        rig.Request.Mode = "ROTATE";
        rig.Request.PendingOccurrence = 1;
        rig.FailHudWrite = true;
        controller.Tick();
        Assert.Equal(SkinApplyStatus.Failed, rig.Last.Status);
        Assert.Same(a, rig.Character);
        Assert.Same(hudA, rig.Hud);
        Assert.Equal("a", rig.Session.CurrentPack.Id);
        Assert.Equal(0, rig.Restores);
        rig.FailHudWrite = false;
        controller.Tick();
        Assert.Equal("b", rig.Session.CurrentPack.Id);
        rig.Request.SpriteScope = "CHARACTER";
        controller.Tick();
        Assert.Same(rig.OriginalHud, rig.Hud);
        rig.Default();
        controller.Tick();
        Assert.Same(rig.OriginalCharacter, rig.Character);
        Assert.Same(rig.OriginalHud, rig.Hud);
    }

    [Theory]
    [InlineData("hollow-knight", "OFF")]
    [InlineData("hollow-knight", "ON")]
    [InlineData("hollow-knight", "ROTATE")]
    [InlineData("silksong", "OFF")]
    [InlineData("silksong", "ON")]
    [InlineData("silksong", "ROTATE")]
    public void Healthy_idle_has_zero_transport_io_discovery_transactions_and_allocations(string profile, string mode)
    {
        using var rig = new Rig(profile);
        rig.Request.Mode = mode;
        rig.Request.RotationRun = "run";
        var hero = new object(); var manager = new object(); var hud = new object();
        var saveIdentity = new SkinSaveIdentity();
        rig.Request.SaveSlot = 0;
        Func<int> sampleSave = () => saveIdentity.Sample(manager, 0, true, false);
        int currentFrame = 0;
        SkinDeathFrame hkFrame = null;
        SilksongDeathFrame ssFrame = null;
        var ring = new SilksongDeathRingSample(0);
        var heroes = new object[32]; var managers = new object[32]; var tokens = new long[32];
        using var hkDeath = new HollowKnightSkinDeathAdapter(frame => {
            if (hkFrame != null && !ReferenceEquals(hkFrame, frame)) throw new Exception("frame recreated");
            hkFrame = frame; frame.Frame = currentFrame; frame.Hero = hero; frame.Manager = manager; frame.Hud = hud;
        });
        using var ssDeath = new SilksongSkinDeathAdapter(frame => {
            if (ssFrame != null && !ReferenceEquals(ssFrame, frame)) throw new Exception("frame recreated");
            ssFrame = frame; frame.Frame = currentFrame; frame.Hero = hero; frame.Manager = manager; frame.HudOwners = hud;
            ring.Capture(frame, heroes, managers, tokens);
        });
        using var hk = profile == "hollow-knight" ? new HollowKnightSkinLibrary(rig.Read, rig.Apply,
            rig.Restore, rig.Report, () => rig.Last, hkDeath, (_, _) => throw new Exception("idle confirm"),
            _ => throw new Exception("idle cancellation"), saveIdentity: sampleSave) : null;
        using var ss = profile == "silksong" ? new SilksongSkinLibrary(rig.Read, rig.Apply,
            rig.Restore, rig.Report, () => rig.Last, ssDeath, (_, _) => throw new Exception("idle confirm"),
            (_, _) => throw new Exception("idle occurrence cancellation"), _ => throw new Exception("idle cancellation"), sampleSave) : null;
        var identities = new List<object>(3) { hero, manager, hud };
        var ownerRefresh = new SilksongOwnerRefreshState(1);
        ownerRefresh.UpdateOwners(identities);
        ownerRefresh.MarkVisualRefresh();
        SkinRuntimeRefreshSchedule hkSchedule = null;
        hkSchedule = new SkinRuntimeRefreshSchedule(() => { }, () => hk == null || hk.CanRefresh,
            () => hkSchedule.Publish(rig.Last = rig.Session.Refresh()));
        bool measuring = false;
        long visualAllocated = 0, libraryAllocated = 0;
        // Production order: scalar save boundary, gated visuals, then library/death admission.
        void Tick(int frame)
        {
            currentFrame = frame;
            float now = frame / 60f;
            long before = measuring ? GC.GetAllocatedBytesForCurrentThread() : 0;
            if (hk != null) hk.AdmitSaveBoundary(); else ss.AdmitSaveBoundary();
            if (hk != null) { if (hk.SaveVisualRefreshAllowed) hkSchedule.Tick(now, hero, hud); }
            else if (ss.SaveVisualRefreshAllowed && ownerRefresh.ShouldPoll(now))
            {
                identities.Clear(); identities.Add(hero); identities.Add(manager); identities.Add(hud);
                ownerRefresh.UpdateOwners(identities);
                if (ownerRefresh.VisualRefreshRequired(ss.CanRefresh)) rig.Last = rig.Session.Refresh();
            }
            if (measuring)
            {
                long after = GC.GetAllocatedBytesForCurrentThread();
                visualAllocated += after - before; before = after;
            }
            if (hk != null) hk.Tick(now); else ss.Tick(now);
            if (measuring) libraryAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        // Warm and measure the exact production loop on the same dedicated thread. The
        // thread-local counter excludes allocations on other threads; the contract stays zero.
        long allocated = -1;
        Exception measurementFailure = null;
        var measurement = new System.Threading.Thread(() => {
            try
            {
                for (int frame = 0; frame < 1200; frame++) Tick(frame);
                rig.ResetCounts();
                measuring = true;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 1200; frame < 4800; frame++) Tick(frame);
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { measurementFailure = error; }
        }) { IsBackground = true };
        measurement.Start();
        Assert.True(measurement.Join(TimeSpan.FromSeconds(30)), "Idle measurement exceeded its bound.");
        Assert.Null(measurementFailure);
        Assert.Equal(0, rig.Reads);
        Assert.Equal(0, rig.Reports);
        Assert.Equal(0, rig.DiskReads + rig.Stats + rig.Syncs + rig.DiskWrites + rig.Json);
        Assert.Equal(0, rig.Discoveries);
        Assert.Equal(0, rig.Applies);
        Assert.Equal(0, rig.Restores);
        Assert.True(allocated == 0, $"Idle allocated {allocated} bytes; visual checks={visualAllocated}, library/death={libraryAllocated}.");
        Assert.Equal(0, allocated);
        Assert.Equal(0, hkDeath.Occurrence);
        Assert.Equal(0, ssDeath.Occurrence);
    }

    [Fact]
    public void Silksong_allowed_refresh_is_not_a_refresh_request()
    {
        var state = new SilksongOwnerRefreshState(1);
        var identity = new SilksongOwnerIdentityStamp(new[] { new object(), new object() });
        state.Update(identity);
        Assert.True(state.VisualRefreshRequired(false)); // replacement must bootstrap respawn targets
        state.MarkVisualRefresh();
        for (int second = 0; second < 20; second++)
        {
            Assert.True(state.ShouldPoll(second));
            Assert.False(state.Update(identity));
            Assert.False(state.VisualRefreshRequired(true));
        }
        state.InvalidateVisuals();
        Assert.True(state.VisualRefreshRequired(false));
    }

    [Theory]
    [InlineData("hollow-knight")]
    [InlineData("silksong")]
    public void Profile_fixture_loaded_A_to_B_to_default_advances_only_exact_death_authority(string profile)
    {
        using var rig = new Rig(profile);
        rig.Request.Mode = "ROTATE"; rig.Request.RotationRun = "run";
        var hero = new object(); var manager = new object(); var hud = new object();
        var hkFrame = new SkinDeathFrame { Hero = hero, Manager = manager, Hud = hud, Gameplay = true,
            Playing = true, AcceptingInput = true, InPosition = true, WaitingToTransition = true, TargetsAvailable = true };
        var ssFrame = new SilksongDeathFrame { Hero = hero, Manager = manager, HudOwners = hud, Gameplay = true,
            Playing = true, AcceptingInput = true, HeroInPosition = true, SceneComplete = true, TargetsAvailable = true };
        var ring = new SilksongDeathRingSample(0);
        var heroes = new object[32]; var managers = new object[32]; var tokens = new long[32];
        using var hkDeath = new HollowKnightSkinDeathAdapter(() => hkFrame);
        using var ssDeath = new SilksongSkinDeathAdapter(() => {
            ring.Capture(ssFrame, heroes, managers, tokens); return ssFrame;
        });
        // Authority fixture freezes candidates only. Default is always present for both exact profiles;
        // it is not a pack, decoded payload, death heuristic, or a new loader execution path.
        string[] candidates = { "b", null, "a" };
        Assert.Contains(null, candidates);
        int confirms = 0, completions = 0;
        bool Confirm(string run, long occurrence)
        {
            Assert.Equal("run", run);
            Assert.Equal(confirms + 1, occurrence);
            string candidate = candidates[confirms++];
            rig.Request.LastDeath = rig.Request.PendingOccurrence = occurrence;
            if (candidate == null) rig.Default(); else rig.Select(candidate, 'b');
            return true;
        }
        bool Report(SkinLibraryObservation observation)
        {
            if (observation.PendingOccurrence > 0 && (observation.Status == "Applied" || observation.Status == "Restored"))
            {
                completions++; rig.Request.PendingOccurrence = 0;
            }
            return true;
        }
        using var hk = profile == "hollow-knight" ? new HollowKnightSkinLibrary(() => rig.Request, rig.Apply,
            rig.Restore, Report, () => rig.Last, hkDeath, Confirm, _ => throw new Exception("unexpected cancellation")) : null;
        using var ss = profile == "silksong" ? new SilksongSkinLibrary(() => rig.Request, rig.Apply,
            rig.Restore, Report, () => rig.Last, ssDeath, Confirm, (_, _) => throw new Exception("unexpected cancellation"),
            _ => throw new Exception("unexpected cancellation")) : null;
        void Tick(float time) { hkFrame.Frame++; ssFrame.Frame++; if (hk != null) hk.Tick(time); else ss.Tick(time); }
        Tick(0);
        object a = rig.Character;
        // Non-death notifications, hazards, pause, and scene transitions never create occurrences.
        hkDeath.HeroInPosition(hero, manager); hkDeath.SceneCompleted(hero, manager);
        hkFrame.Hazard = ssFrame.Hazard = true; Tick(.1f);
        hkFrame.Hazard = ssFrame.Hazard = false; hkFrame.Paused = ssFrame.Paused = true; Tick(.2f);
        hkFrame.Paused = ssFrame.Paused = false; hkFrame.Transitioning = ssFrame.Transitioning = true; Tick(.3f);
        hkFrame.Transitioning = ssFrame.Transitioning = false; Tick(1);
        Assert.Equal(0, confirms);
        for (int occurrence = 1; occurrence <= 2; occurrence++)
        {
            float start = occurrence * 10;
            if (hk != null)
            {
                hkDeath.OnDeath(hero, manager); // bound typed OnDeath callback arms, dead observation confirms
                hkDeath.OnDeath(hero, manager); // duplicate callback cannot create another occurrence
            }
            else
            {
                int index = occurrence - 1;
                heroes[index] = hero; managers[index] = manager; tokens[index] = occurrence;
            }
            hkFrame.Dead = ssFrame.Dead = true; Tick(start);
            Assert.Equal(occurrence, confirms);
            Assert.Equal(occurrence - 1, completions);
            Assert.Equal(0, rig.Restores);
            hkFrame.Dead = ssFrame.Dead = false;
            hkDeath.HeroInPosition(hero, manager); hkDeath.SceneCompleted(hero, manager);
            Tick(start + .1f);
            Assert.False(hk != null ? hk.CanRefresh : ss.CanRefresh);
            Tick(start + .2f);
            Assert.True(hk != null ? hk.CanRefresh : ss.CanRefresh);
            Tick(start + 1);
            Assert.Equal(occurrence, completions);
            if (occurrence == 1)
            {
                Assert.Equal("b", rig.Session.CurrentPack.Id);
                Assert.NotSame(a, rig.Character);
                Assert.Equal(0, rig.Restores);
            }
        }
        Assert.Equal(2, rig.Applies);
        Assert.Equal(1, rig.Restores);
        Assert.Same(rig.OriginalCharacter, rig.Character); Assert.Same(rig.OriginalHud, rig.Hud);
        Assert.Null(rig.Session.CurrentPack);
        Tick(100);
        Assert.Equal(2, confirms); Assert.Equal(2, completions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Silksong_current_visual_observation_invalidates_success_and_retries_are_bounded(bool ownerReplacement)
    {
        var state = new SilksongOwnerRefreshState(1);
        var owners = new List<object> { new object(), new object() };
        state.UpdateOwners(owners);
        state.Publish(new SkinApplyResult(SkinApplyStatus.Applied));
        state.ShouldPoll(0);
        if (ownerReplacement) { owners[0] = new object(); Assert.True(state.UpdateOwners(owners)); }
        else state.InvalidateVisuals();
        Assert.Equal(SkinApplyStatus.AwaitingTargets, state.LastResult.Status);
        Assert.False(state.VisualCurrent);
        if (!ownerReplacement) Assert.True(state.ShouldPoll(.1f));
        // Discovering owners is not a successful visual transaction. Failure must stay visible,
        // request a retry, and not turn a one-second schedule into every-frame discovery.
        state.Publish(new SkinApplyResult(SkinApplyStatus.Failed));
        for (int tick = 1; tick < 50; tick++) Assert.False(state.ShouldPoll(.1f + tick / 60f));
        Assert.Equal(SkinApplyStatus.Failed, state.LastResult.Status);
        Assert.True(state.VisualRefreshRequired(true));
        Assert.True(state.ShouldPoll(2));
        state.Publish(new SkinApplyResult(SkinApplyStatus.Unchanged));
        Assert.True(state.VisualCurrent);
        Assert.False(state.VisualRefreshRequired(true));
    }

    [Fact]
    public void Silksong_reusable_ring_keeps_queued_order_and_reaches_last_token_without_overflow()
    {
        var hero = new object(); var manager = new object();
        var heroes = new object[32]; var managers = new object[32]; var tokens = new long[32];
        var ring = new SilksongDeathRingSample(long.MaxValue - 2);
        var frame = new SilksongDeathFrame { Hero = hero, Manager = manager };
        ring.Capture(frame, heroes, managers, tokens);
        using var death = new SilksongSkinDeathAdapter(() => frame);
        death.Configure("ROTATE", "run");
        for (long offset = 1; offset <= 2; offset++)
        {
            long occurrence = long.MaxValue - 2 + offset;
            int index = (int)((occurrence - 1) % 32);
            heroes[index] = hero; managers[index] = manager; tokens[index] = occurrence;
        }
        ring.Capture(frame, heroes, managers, tokens);
        death.Tick();
        Assert.False(death.BacklogFaulted);
        Assert.Equal(long.MaxValue - 1, death.Occurrence);
        Assert.Equal(1, death.PendingOccurrences);
        var snapshot = frame.BridgeOccurrences;
        ring.Capture(frame, heroes, managers, tokens);
        Assert.Same(snapshot, frame.BridgeOccurrences);
        death.Configure("ROTATE", "run", long.MaxValue - 1, 0);
        Assert.Equal(long.MaxValue, death.Occurrence);
        death.Configure("ROTATE", "run", long.MaxValue, 0);
        Assert.Equal(0, death.Occurrence);
    }

    [Fact]
    public void Detailed_managed_getters_are_event_gated_without_losing_death_or_stable_respawn()
    {
        var hero = new object(); var manager = new object(); var hud = new object();
        long frameNumber = 0;
        bool dead = false;
        int hkDetails = 0, ssDetails = 0;
        HollowKnightSkinDeathAdapter hk = null;
        SilksongSkinDeathAdapter ss = null;
        hk = new HollowKnightSkinDeathAdapter(frame => {
            frame.Frame = frameNumber; frame.Hero = hero; frame.Manager = manager; frame.Hud = hud; frame.SaveId = 1;
            if (!hk.DetailedSampleRequired) return;
            hkDetails++;
            frame.MapZone = "CROSSROADS"; frame.Dead = dead; frame.Gameplay = frame.Playing = true;
            frame.InPosition = frame.WaitingToTransition = frame.AcceptingInput = frame.TargetsAvailable = true;
        });
        var ring = new SilksongDeathRingSample(0);
        var heroes = new object[32]; var managers = new object[32]; var tokens = new long[32];
        ss = new SilksongSkinDeathAdapter(frame => {
            frame.Frame = frameNumber; frame.Hero = hero; frame.Manager = manager; frame.HudOwners = hud;
            ring.Capture(frame, heroes, managers, tokens);
            if (!ss.DetailedSampleRequired(frame.BridgeOccurrence)) return;
            ssDetails++;
            frame.Dead = dead; frame.Gameplay = frame.Playing = true;
            frame.HeroInPosition = frame.SceneComplete = frame.AcceptingInput = frame.TargetsAvailable = true;
        });
        using (hk) using (ss)
        {
            hk.Configure("ROTATE", "run"); ss.Configure("ROTATE", "run");
            hkDetails = ssDetails = 0;
            for (int tick = 0; tick < 600; tick++) { frameNumber++; hk.Tick(); ss.Tick(); }
            Assert.Equal(0, hkDetails); Assert.Equal(0, ssDetails);
            hk.OnDeath(hero, manager);
            heroes[0] = hero; managers[0] = manager; tokens[0] = 1;
            dead = true; frameNumber++; hk.Tick(); ss.Tick();
            Assert.Equal(1, hk.Occurrence); Assert.Equal(1, ss.Occurrence);
            Assert.Equal(2, hkDetails); Assert.Equal(1, ssDetails);
            hk.Configure("ROTATE", "run", 1, 1); ss.Configure("ROTATE", "run", 1, 1);
            dead = false; hk.HeroInPosition(hero, manager); hk.SceneCompleted(hero, manager);
            frameNumber++; hk.Tick(); ss.Tick();
            Assert.False(hk.Ready); Assert.False(ss.Ready);
            frameNumber++; hk.Tick(); ss.Tick();
            Assert.True(hk.Ready); Assert.True(ss.Ready);
            hk.Configure("ROTATE", "run", 1, 0); ss.Configure("ROTATE", "run", 1, 0);
            hkDetails = ssDetails = 0;
            for (int tick = 0; tick < 600; tick++) { frameNumber++; hk.Tick(); ss.Tick(); }
            Assert.Equal(0, hkDetails); Assert.Equal(0, ssDetails);
        }
    }

    sealed class Rig : IDisposable
    {
        public readonly object OriginalCharacter = new object(), OriginalHud = new object();
        public object Character, Hud;
        public readonly List<object> Writes = new List<object>();
        public readonly SkinRuntimeRules Rules;
        public readonly SkinRuntimeSession Session;
        public SkinLibraryRequest Request;
        public SkinApplyResult Last = new SkinApplyResult(SkinApplyStatus.Unchanged);
        public int Reads, Reports, DiskReads, Stats, Syncs, DiskWrites, Json, Discoveries, Applies, Restores;
        public bool FailHudWrite;
        readonly string root;
        public Rig(string profile)
        {
            root = Path.GetFullPath(Path.Combine("D:/Temp", "resolved-loader-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            byte[] png = new byte[45];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
            png[11] = 13; png[12] = (byte)'I'; png[13] = (byte)'H'; png[14] = (byte)'D'; png[15] = (byte)'R';
            png[19] = png[23] = 2;
            File.WriteAllBytes(Path.Combine(root, "atlas.png"), png);
            Rules = profile == "silksong" ? SilksongSkinTargets.RuntimeRules : HollowKnightSkinPolicy.RuntimeRules;
            string character = profile == "silksong" ? SilksongSkinTargets.All.First(x => !x.IsHud).CanonicalPath : "Knight.png";
            string hud = profile == "silksong" ? SilksongSkinTargets.All.First(x => x.IsHud).CanonicalPath : "Hud.png";
            Character = OriginalCharacter; Hud = OriginalHud;
            var owner = new object();
            var slots = new[] {
                new SkinSlot(owner, "character", character, () => true, () => Character,
                    value => { Writes.Add(value); Character = value; }, (texture, _) => texture.Value),
                new SkinSlot(owner, "hud", hud, () => true, () => Hud,
                    value => { Writes.Add(value); Hud = value;
                        if (FailHudWrite && !ReferenceEquals(value, OriginalHud)) { FailHudWrite = false; throw new IOException("setter mutated"); }
                    }, (texture, _) => texture.Value)
            };
            Session = new SkinRuntimeSession(new Decoder(), () => { Discoveries++; return slots; }, 1024 * 1024, Rules);
            Request = new SkinLibraryRequest { ProfileId = profile, Mode = "ON", ConfigSha256 = new string('a', 64),
                PackId = "a", TreeSha256 = new string('a', 64), Root = root,
                Textures = new Dictionary<string, string> { [character] = "atlas.png", [hud] = "atlas.png" } };
        }
        public SkinLibraryRuntimeController Controller(Func<SkinPack, SkinApplyResult> apply) =>
            new SkinLibraryRuntimeController(Rules, () => Request, apply, Restore, _ => { }, () => Last, _ => true);
        public SkinLibraryRequest Read() { Reads++; DiskReads++; Stats++; Syncs++; Json++; return Request; }
        public bool Report(SkinLibraryObservation observation) { Reports++; DiskWrites++; Syncs++; Json++; return true; }
        public SkinApplyResult Apply(SkinPack pack) { Applies++; return Last = Session.TryApply(pack); }
        public SkinApplyResult Restore() { Restores++; return Last = Session.TryRestore(); }
        public void Select(string id, char digest) { Request.PackId = id; Request.TreeSha256 = Request.ConfigSha256 = new string(digest, 64); }
        public void Default() { Request.Mode = "ROTATE"; Request.Vanilla = true; Request.PackId = Request.TreeSha256 = Request.Root = null; Request.Textures = null; }
        public void ResetCounts() { Reads = Reports = DiskReads = Stats = Syncs = DiskWrites = Json = Discoveries = Applies = Restores = 0; }
        public void Dispose() { Session.Dispose(); Directory.Delete(root, true); }
        sealed class Decoder : ISkinTextureDecoder
        {
            public SkinTexture Decode(byte[] bytes, int width, int height) => new SkinTexture(new object(), width, height, () => { }, () => true);
        }
    }
}
