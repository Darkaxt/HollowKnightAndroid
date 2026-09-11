using Xunit;

public sealed class SilksongPortSelectionTests
{
    [Fact]
    public void CompanionDismissalRequiresCurrentArmedGenerationAndConsumesOnce()
    {
        // Linked host policy mirrors the native-owned fields emitted by the Cecil pass;
        // this is managed state execution, not Unity coroutine or rendering execution.
        var type = typeof(DsPortOverlayTargets).Assembly.GetType("DsPortCompanionDismissState");
        Assert.NotNull(type);
        dynamic state = Activator.CreateInstance(type);
        Assert.False(state.Request(1)); // Pre-sequence and pre-show requests fail closed.
        state.Begin();
        Assert.False(state.Request(state.Observe()));
        state.Arm();
        int generation = state.Observe();
        Assert.NotEqual(0, generation);
        Assert.True(state.Request(generation));
        Assert.True(state.Request(generation)); // Repeated taps retain only this generation.
        Assert.True(state.Consume());
        Assert.False(state.Consume());
        Assert.False(state.Request(generation)); // One consumed request disarms the wait.
        state.Begin(); state.Arm(); generation = state.Observe(); Assert.True(state.Request(generation));
        state.Begin(); state.Arm();
        Assert.False(state.Request(generation)); // Reuse rejects an observed stale generation.
        Assert.False(state.Consume());
        generation = state.Observe(); Assert.True(state.Request(generation)); state.End();
        Assert.False(state.Consume());
        Assert.False(state.Request(generation)); // Cancellation/end invalidates the armed request.
    }

    [Theory] [InlineData("text")] [InlineData("plain")] [InlineData("foreign-font")]
    [InlineData("root-retry")] [InlineData("font-retry")] [InlineData("prepare-failure")]
    public void InventoryCommittedExtraFontOwnerSurvivesPresentReleaseAndCancelledHold(string fault)
    {
        // Actual linked graph/glyph-validation/commit/lifetime/page mechanics. Fonts, roots,
        // the poisoned TMP wrapper and signal arrival are managed fixtures, NOT Unity execution.
        var graph = new DsPortOwnedGraph(); var fonts = new HashSet<object>();
        var extras = new List<object>(); var textFonts = new Dictionary<object, object>();
        var extra = new object(); var sourceFont = new object(); var replacementRoot = new object();
        bool pageRootLive = true, poisoned = false, signal = false;
        int responses = 0, takes = 0, rootAttempts = 0, fontAttempts = 0, fontReleases = 0, validations = 0;
        var hold = new DsPortActionHold(); var lifetime = new DsPortConsumeVisualLifetime();
        var commit = new DsPortConsumeCommit();
        void Validate()
        {
            if (poisoned) throw new InvalidOperationException("partial owned text graph awaits page/detail retirement");
            foreach (var root in extras)
                if (textFonts.TryGetValue(root, out var font))
                {
                    Assert.Same(font, DsPortTextPreflight.Resolve(font, 'A', fonts.Contains,
                        (_, code) => code == 'A', _ => Array.Empty<object>()));
                    validations++;
                }
        }
        void ReleaseVisual()
        {
            Assert.DoesNotContain(replacementRoot, extras);
            rootAttempts++;
            if (fault == "root-retry" && rootAttempts == 1) throw new InvalidOperationException("exact extra root still live");
            extras.Clear(); textFonts.Clear(); signal = false;
        }
        var key = new object(); var token = new DsJournalToken(key, key, key, key, key, 1);
        var native = new ActionPage { Current = token, OnPresent = Validate,
            OnRelease = () => { lifetime.Retire(ReleaseVisual); pageRootLive = false; graph.Clear(); fonts.Clear(); } };
        var page = new DsPortJournalState(native); page.Tick(token, 1); page.Tick(token, 2);
        hold.Observe(true, false);
        commit.Commit(false, true, () => page.Ready, () => responses++, () => takes++);
        lifetime.Commit(); signal = true;
        Assert.Empty(extras); // No extra until the source-native signal continuation is reached.
        extras.Add(extra);
        void Prepare()
        {
            if (fault == "plain") return;
            try
            {
                textFonts[extra] = graph.Copy(sourceFont,
                    () => { var font = new object(); fonts.Add(font); return font; },
                    _ => { if (fault == "prepare-failure") throw new InvalidOperationException("partial font population"); },
                    font =>
                    {
                        Assert.False(pageRootLive); Assert.Empty(extras); Assert.Contains(font, fonts);
                        if (++fontAttempts == 1 && fault == "font-retry") throw new InvalidOperationException("exact font release");
                        fontReleases++;
                    });
            }
            catch { poisoned = true; throw; } // DsPortOwnedText.Prepare's source-checked wrapper.
        }
        if (fault == "prepare-failure") Assert.Throws<InvalidOperationException>(Prepare);
        else Prepare();
        hold.Observe(false, true); lifetime.ReleaseHold(ReleaseVisual);
        Assert.True(signal); Assert.Same(extra, Assert.Single(extras)); Assert.Equal(0, rootAttempts);
        if (fault == "foreign-font") textFonts[extra] = new object();
        if (fault == "prepare-failure" || fault == "foreign-font")
        {
            var error = Assert.Throws<InvalidOperationException>(Validate);
            Assert.Contains(fault == "prepare-failure" ? "partial owned text graph" : "escaped owned graph", error.Message);
            if (poisoned) Assert.Throws<InvalidOperationException>(() => graph.Copy(new object(), () => new object(), _ => { }, null));
            Assert.Throws<InvalidOperationException>(() => page.Tick(token, 3));
            Assert.False(page.Ready); Assert.Null(page.Owned);
            Assert.Equal(1, native.Released); Assert.Null(native.VisibleContent);
        }
        else
        {
            page.Tick(token, 3); Assert.True(page.Ready); Assert.Null(page.Problem);
            hold.Observe(true, false); hold.Observe(false, true); lifetime.ReleaseHold(ReleaseVisual);
            page.Tick(token, 4); Assert.True(page.Ready); Assert.Same(native.Page, page.Owned);
            Assert.Same(extra, Assert.Single(extras)); Assert.True(signal); Assert.Equal(0, rootAttempts);
            Assert.Equal(fault == "plain" ? 0 : 2, validations);
        }
        Assert.Throws<InvalidOperationException>(() => commit.Commit(false, true, () => true, () => responses++, () => takes++));
        if (fault == "root-retry" || fault == "font-retry")
        {
            Assert.Throws<InvalidOperationException>(page.Clear);
            Assert.Same(native.Page, page.Owned); Assert.Equal(0, native.Released); Assert.Equal(0, fontReleases);
            Assert.Equal(fault == "root-retry", pageRootLive);
            if (fault == "root-retry") Assert.Same(extra, Assert.Single(extras));
            else Assert.Empty(extras);
        }
        page.Clear(); page.Clear();
        Assert.Null(page.Owned); Assert.Equal(1, native.Released); Assert.False(pageRootLive); Assert.False(signal);
        Assert.Empty(extras); Assert.Empty(fonts); Assert.Equal(fault == "plain" ? 0 : 1, fontReleases);
        Assert.Equal(fault == "root-retry" ? 2 : 1, rootAttempts);
        Assert.Equal(1, responses); Assert.Equal(1, takes);
    }

    [Fact] public void InventorySeparateExtraFontOwnerReproducesTraversalMismatch()
    {
        // Negative control: a glyph-bearing font from another exact owner is not page-owned.
        var pageFonts = new HashSet<object>(); var separateGraph = new DsPortOwnedGraph();
        var font = separateGraph.Copy(new object(), () => new object(), _ => { }, _ => { });
        var error = Assert.Throws<InvalidOperationException>(() => DsPortTextPreflight.Resolve(font, 'A',
            pageFonts.Contains, (_, _) => true, _ => Array.Empty<object>()));
        Assert.Contains("escaped owned graph", error.Message); separateGraph.Clear();
    }

    [Theory] [InlineData("equip")] [InlineData("unequip")] [InlineData("borrowed-animator")] [InlineData("changed-variant")]
    public void ToolEntryNativeTransitionsRetainExactAnimatorAndControllerAuthority(string change)
    {
        // Linked authority/page policies; the state names are a native-source fixture, not Unity playback.
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortToolAnimatorAuthority"); Assert.NotNull(type);
        var animator = new object(); var regular = new object(); var attack = new object(); var skill = new object();
        object[] variants = { regular, attack, skill };
        dynamic authority = Activator.CreateInstance(type, animator, variants);
        var key = new object(); var token = new DsJournalToken(key, key, key, key, key, 1);
        var native = new ActionPage { Current = token }; var page = new DsPortJournalState(native);
        page.Tick(token, 1); page.Tick(token, 2);
        bool primaryInput = false; string visual = "Empty"; int commits = 0;
        var boundary = new DsPortActionBoundary();
        boundary.Run(() =>
        {
            Assert.True((bool)authority.Current(animator, variants, regular));
            commits++; visual = "Equip";
            Assert.True((bool)authority.Current(animator, (object[])variants.Clone(), attack));
            Assert.True(page.RefreshAfterAction(native.Page, token, token));
        });
        if (change == "unequip") { commits++; visual = "Unequip"; }
        var target = change == "borrowed-animator" ? new object() : animator;
        if (change == "changed-variant") variants[1] = new object();
        Assert.Equal(change == "equip" || change == "unequip", (bool)authority.Current(target, variants, attack));
        Assert.Same(native.Page, page.Owned); Assert.Equal(0, native.Released);
        Assert.Equal(change == "unequip" ? "Unequip" : "Equip", visual);
        Assert.Equal(change == "unequip" ? 2 : 1, commits); Assert.False(primaryInput);
        Assert.False((bool)authority.Current(animator, variants, new object()));
    }

    [Theory] [InlineData("precommit")] [InlineData("signal")] [InlineData("extra")] [InlineData("next-hold")] [InlineData("retirement-retry")] [InlineData("owner-loss")]
    public void InventoryReleaseKeepsCommittedVisualUntilExactPageRetirement(string phase)
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortConsumeVisualLifetime"); Assert.NotNull(type);
        dynamic lifetime = Activator.CreateInstance(type);
        int responses = 0, takes = 0, releases = 0; bool visual = false, callback = false;
        var extras = new List<object>();
        var commit = new DsPortConsumeCommit(); var hold = new DsPortActionHold(); hold.Observe(true, false);
        long generation = hold.Capture();
        if (phase != "precommit")
        {
            commit.Commit(false, true, () => true, () => responses++, () => takes++);
            lifetime.Commit(); visual = true; callback = phase == "signal";
            if (!callback) extras.Add(new object()); // Native post-signal branch only.
        }
        Action release = () => { releases++; if (phase == "retirement-retry" && releases == 1) throw new InvalidOperationException("owned cleanup"); visual = false; extras.Clear(); };
        var key = new object(); var token = new DsJournalToken(key, key, key, key, key, 1);
        var native = new ActionPage { Current = token, OnRelease = () => lifetime.Retire(release) };
        var pageState = new DsPortJournalState(native); pageState.Tick(token, 1); pageState.Tick(token, 2);
        hold.Observe(false, true); callback = false; // Production detaches wait, never invokes it.
        lifetime.ReleaseHold(release);
        Assert.False(hold.Current(generation)); Assert.False(callback);
        Assert.Equal(phase != "precommit", visual); Assert.Equal(phase == "precommit" ? 1 : 0, releases);
        Assert.Equal(phase == "precommit" || phase == "signal" ? 0 : 1, extras.Count);
        if (phase == "next-hold")
        {
            hold.Observe(true, false); // New hold cancelled BEFORE its commit; same page-owned visual survives.
            hold.Observe(false, true); lifetime.ReleaseHold(release);
            Assert.True(visual); Assert.Equal(0, releases); var previousExtra = Assert.Single(extras);
            var next = new DsPortConsumeCommit();
            next.Commit(false, true, () => true, () => responses++, () => takes++);
            lifetime.Commit(); extras.Add(new object()); lifetime.ReleaseHold(release);
            Assert.Same(previousExtra, extras[0]); Assert.NotSame(extras[0], extras[1]);
            Assert.True(visual); Assert.Equal(0, releases);
        }
        if (phase == "retirement-retry")
        {
            Assert.Throws<InvalidOperationException>(() => { lifetime.Retire(release); });
            Assert.True(visual);
            Assert.Throws<InvalidOperationException>(() => { lifetime.Commit(); });
        }
        if (phase == "owner-loss") native.Current = new DsJournalToken(new object(), key, key, key, key, 1);
        Assert.Same(native.Page, pageState.Owned); Assert.Equal(0, native.Released);
        pageState.Clear(); pageState.Clear();
        Assert.Equal(1, native.Released); Assert.Null(pageState.Owned);
        Assert.False(visual); Assert.Empty(extras); Assert.Equal(phase == "retirement-retry" ? 2 : 1, releases);
        Assert.Equal(phase == "precommit" ? 0 : phase == "next-hold" ? 2 : 1, responses); Assert.Equal(responses, takes);
        if (phase != "precommit") Assert.Throws<InvalidOperationException>(() => commit.Commit(false, true, () => true, () => responses++, () => takes++));
    }

    [Theory] [InlineData("owner")] [InlineData("retirement")] [InlineData("presentation")]
    public void OrdinarySubmitRetainsExactTargetThroughCallbacksAndNeverReplaysCommit(string failure)
    {
        // Linked boundary/page policies, not execution of Unity SetEquipped.
        var boundary = new DsPortActionBoundary();
        var key = new object();
        var before = new DsJournalToken(key, key, key, key, key, 1, new DsPortPageSnapshot(new object[] { 1 }));
        var native = new ActionPage { Current = before };
        var state = new DsPortJournalState(native);
        state.Tick(before, 1); state.Tick(before, 2);
        var target = new object(); var retainedTarget = target;
        int commits = 0, nativeTail = 0, followingOperations = 0;
        Assert.Throws<InvalidOperationException>(() => boundary.Run(() =>
        {
            commits++;
            if (failure == "owner")
                native.Current = new DsJournalToken(new object(), key, key, key, key, 1, new DsPortPageSnapshot(new object[] { 2 }));
            if (failure == "retirement") Assert.True(boundary.DeferRetirement());
            Assert.Same(retainedTarget, target); Assert.Same(native.Page, state.Owned);
            Assert.Equal(0, native.Released); nativeTail++;
            if (failure == "presentation") throw new InvalidOperationException("native presentation after save");
            if (boundary.Pending || !state.RefreshAfterAction(native.Page, before, native.Current))
                throw new InvalidOperationException("post-callback authority lost");
            followingOperations++;
        }));
        Assert.Equal(1, commits); Assert.Equal(1, nativeTail); Assert.Equal(0, followingOperations);
        Assert.Equal(0, native.Released); Assert.Same(retainedTarget, target);
        Assert.False(boundary.Active); Assert.True(boundary.Pending);
        Assert.Throws<InvalidOperationException>(() => boundary.Run(() => commits++));
        Assert.False(boundary.DeferRetirement()); state.Clear(); target = null;
        Assert.Equal(1, native.Released); Assert.Equal(1, commits);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ActionCallbackRetirementWaitsForUnwindWithoutReplayingCommit(bool presentationThrows)
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortActionBoundary");
        Assert.NotNull(type); var boundary = Activator.CreateInstance(type); int commits = 0;
        bool Active() => (bool)type.GetProperty("Active").GetValue(boundary);
        Action action = () =>
        {
            commits++; Assert.True(Active());
            Assert.True((bool)type.GetMethod("DeferRetirement").Invoke(boundary, null));
            Assert.True((bool)type.GetProperty("Pending").GetValue(boundary));
            if (presentationThrows) throw new InvalidOperationException("owned effect");
        };
        if (presentationThrows) Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("Run").Invoke(boundary, new object[] { action }));
        else type.GetMethod("Run").Invoke(boundary, new object[] { action });
        Assert.False(Active()); Assert.Equal(1, commits);
        Assert.True((bool)type.GetProperty("Pending").GetValue(boundary));
        Assert.False((bool)type.GetMethod("DeferRetirement").Invoke(boundary, null));
        Assert.False((bool)type.GetProperty("Pending").GetValue(boundary));
        Assert.Equal(1, commits);
    }

    [Theory] [InlineData("member")] [InlineData("value")] [InlineData("order")]
    public void NativeMapFreshnessRejectsSameOwnerSameCountRecipeChanges(string change)
    {
        var type = typeof(DsPortMapTransaction).Assembly.GetType("DsPortMapFreshness");
        Assert.NotNull(type); object[] values = { new object(), "room", 1, 2 };
        Func<IEnumerable<object>> read = () => values;
        var state = Activator.CreateInstance(type, new object[] { read });
        Assert.True((bool)type.GetMethod("Current").Invoke(state, null));
        if (change == "member") values[0] = new object();
        else if (change == "value") values[2] = 9;
        else (values[2], values[3]) = (values[3], values[2]);
        Assert.False((bool)type.GetMethod("Current").Invoke(state, null));
        var next = Activator.CreateInstance(type, new object[] { read });
        Assert.True((bool)type.GetMethod("Current").Invoke(next, null));
    }

    sealed class RetainedMapGraph { }

    static DsPortMapAuthorityToken MapAuthority(object owner = null, object map = null, object host = null,
        object camera = null, object viewport = null, string scene = "scene", long epoch = 7, int width = 800, int height = 480) =>
        new DsPortMapAuthorityToken(owner ?? AuthorityOwner, map ?? AuthorityMap, host ?? AuthorityHost,
            camera ?? AuthorityCamera, viewport ?? AuthorityViewport, scene, epoch, width, height);
    static readonly object AuthorityOwner = new object(), AuthorityMap = new object(), AuthorityHost = new object(),
        AuthorityCamera = new object(), AuthorityViewport = new object();

    [Fact]
    public void NativeMapFailedPartialConstructionBlocksReplacementUntilExactRetirementSucceeds()
    {
        var open = typeof(DsPortMapRetainedGraph<>).Assembly.GetType("DsPortMapPartialGraph`1");
        Assert.NotNull(open);
        var type = open.MakeGenericType(typeof(RetainedMapGraph));
        var state = Activator.CreateInstance(type); var hold = type.GetMethod("Hold"); var retire = type.GetMethod("Retire");
        var graph = new RetainedMapGraph(); var replacement = new RetainedMapGraph(); int attempts = 0;
        hold.Invoke(state, new object[] { graph });
        var failedRetirement = Assert.Throws<System.Reflection.TargetInvocationException>(() => retire.Invoke(state,
            new object[] { (Action<RetainedMapGraph>)(_ =>
            {
                attempts++;
                throw new InvalidOperationException("partial release");
            }) }));
        Assert.IsType<InvalidOperationException>(failedRetirement.InnerException);
        Assert.Same(graph, type.GetProperty("Graph").GetValue(state));
        var blockedReplacement = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            hold.Invoke(state, new object[] { replacement }));
        Assert.IsType<InvalidOperationException>(blockedReplacement.InnerException);
        retire.Invoke(state, new object[] { (Action<RetainedMapGraph>)(exact =>
        {
            Assert.Same(graph, exact);
            attempts++;
        }) });
        Assert.Null(type.GetProperty("Graph").GetValue(state));
        hold.Invoke(state, new object[] { replacement });
        Assert.Same(replacement, type.GetProperty("Graph").GetValue(state));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void NativeMapRetainsOneDirectHierarchyAcrossManySteadyTicks()
    {
        var state = new DsPortMapRetainedGraph<RetainedMapGraph>(.125);
        var graph = new RetainedMapGraph(); object[] values = { "room", 1 }; int reads = 0, releases = 0;
        int sourcePose = 0, donorPose = 0;
        state.Admit(graph, MapAuthority(), values, 0);
        for (int frame = 1; frame <= 1000; frame++)
        {
            Assert.True(state.TryReuse(MapAuthority(), true, false, frame / 1000d,
                () => { reads++; return values; }, _ => releases++, out var retained));
            Assert.Same(graph, retained);
            sourcePose = frame;
            donorPose = sourcePose;
            state.RecordDynamicRefresh(retained);
            Assert.Equal(sourcePose, donorPose);
        }
        Assert.Equal(1, state.ConstructionCount);
        Assert.Equal(0, state.RetirementCount);
        Assert.InRange(reads, 7, 8);
        Assert.Equal(reads, state.SnapshotCount);
        Assert.Equal(1000, state.DynamicRefreshCount);
        Assert.Equal(0, releases);
    }

    [Fact]
    public void NativeMapSnapshotChangeRetiresAndRebuildsExactlyOnce()
    {
        var state = new DsPortMapRetainedGraph<RetainedMapGraph>(.125);
        object[] values = { "room", 1 }; int releases = 0;
        state.Admit(new RetainedMapGraph(), MapAuthority(), values, 0);
        values = new object[] { "room", 2 };
        Assert.False(state.TryReuse(MapAuthority(), true, false, .125,
            () => values, _ => releases++, out _));
        state.Admit(new RetainedMapGraph(), MapAuthority(), values, .125);
        for (int frame = 126; frame < 250; frame++)
            Assert.True(state.TryReuse(MapAuthority(), true, false, frame / 1000d,
                () => values, _ => releases++, out _));
        Assert.Equal(2, state.ConstructionCount);
        Assert.Equal(1, state.RetirementCount);
        Assert.Equal(1, releases);
    }

    [Theory]
    [InlineData("owner")] [InlineData("map")] [InlineData("host")] [InlineData("camera")]
    [InlineData("viewport")] [InlineData("scene")] [InlineData("epoch")] [InlineData("size")]
    [InlineData("visibility")] [InlineData("primary")]
    public void NativeMapAuthorityLossRetiresImmediatelyWithoutSnapshotPolling(string change)
    {
        var state = new DsPortMapRetainedGraph<RetainedMapGraph>(.125); int reads = 0, releases = 0;
        state.Admit(new RetainedMapGraph(), MapAuthority(), new object[] { 1 }, 0);
        var current = change == "owner" ? MapAuthority(owner: new object()) :
            change == "map" ? MapAuthority(map: new object()) : change == "host" ? MapAuthority(host: new object()) :
            change == "camera" ? MapAuthority(camera: new object()) : change == "viewport" ? MapAuthority(viewport: new object()) :
            change == "scene" ? MapAuthority(scene: "next") : change == "epoch" ? MapAuthority(epoch: 8) :
            change == "size" ? MapAuthority(width: 801) : MapAuthority();
        Assert.False(state.TryReuse(current, change != "visibility", change == "primary", .001,
            () => { reads++; return new object[] { 1 }; }, _ => releases++, out _));
        Assert.Equal(0, reads);
        Assert.Equal(1, releases);
        Assert.Equal(1, state.RetirementCount);
        Assert.Null(state.Graph);
    }

    [Fact]
    public void NativeMapFailedRetirementBlocksReplacementAndRetriesRelease()
    {
        var state = new DsPortMapRetainedGraph<RetainedMapGraph>(.125);
        var graph = new RetainedMapGraph(); int attempts = 0;
        state.Admit(graph, MapAuthority(), new object[] { 1 }, 0);
        Assert.Throws<InvalidOperationException>(() => state.TryReuse(MapAuthority(epoch: 8), true, false, .001,
            () => throw new Exception("must not poll"), _ => { attempts++; throw new InvalidOperationException("release"); }, out _));
        Assert.Same(graph, state.Graph);
        Assert.Equal(1, state.ConstructionCount);
        Assert.Equal(0, state.RetirementCount);
        Assert.Throws<InvalidOperationException>(() => state.Admit(new RetainedMapGraph(), MapAuthority(epoch: 8), new object[] { 1 }, .001));
        Assert.False(state.TryReuse(MapAuthority(), true, false, .002,
            () => throw new Exception("must not poll"), _ => attempts++, out _));
        state.Admit(new RetainedMapGraph(), MapAuthority(epoch: 8), new object[] { 1 }, .002);
        Assert.Equal(2, attempts);
        Assert.Equal(2, state.ConstructionCount);
        Assert.Equal(1, state.RetirementCount);
    }

    [Theory] [InlineData("selected", true)] [InlineData("other-selected", false)] [InlineData("foreign-source", false)]
    [InlineData("locked-crest", false)] [InlineData("invisible", false)] [InlineData("hidden", false)]
    public void SelectedCrestSlotActionsBindSelectedSourceNotGloballyEquippedCrest(string change, bool expected)
    {
        var method = typeof(DsPortSlotAction).GetMethod("CanTargetCrest"); Assert.NotNull(method);
        var globallyEquipped = new object(); var selected = new object(); var source = new object();
        Assert.NotSame(globallyEquipped, selected);
        Assert.Equal(expected, (bool)method.Invoke(null, new object[] { selected, change == "other-selected" ? globallyEquipped : selected,
            source, change == "foreign-source" ? new object() : source, change != "locked-crest", change != "invisible", change == "hidden" }));
    }

    sealed class Native : IDsPortSelection
    {
        public object Owner = new object(), Item = new object(), Data = new object();
        public bool Legal = true, Available = true;
        public int Displays, Submits, Checks;
        public Action DuringLegalCheck;
        public bool IsCurrent(object owner, object item, object data) =>
            Available && ReferenceEquals(owner, Owner) && ReferenceEquals(item, Item) && ReferenceEquals(data, Data);
        public void Display(object owner, object item) { Displays++; }
        public bool CanSubmit(object owner, object item) { Checks++; DuringLegalCheck?.Invoke(); return Legal; }
        public bool Submit(object owner, object item) { Submits++; return true; }
    }

    static DsPortSelectState Bound(Native native)
    {
        var state = new DsPortSelectState(native);
        Assert.True(state.Select(native.Owner, native.Item, native.Data));
        Assert.True(state.HasSelection);
        return state;
    }

    static bool DetailAttempt(Func<bool> current, Func<bool> display, Action clear, Action<Exception> report)
    {
        var method = typeof(DsPortSelectState).Assembly.GetType("DsPortDetailAttempt")?.GetMethod("Try");
        Assert.NotNull(method);
        return (bool)method.Invoke(null, new object[] { current, display, clear, report });
    }

    [Fact] public void InventoryDetailValidationThrowRetainsListForNextSelection()
    {
        var native = new Native(); var selection = Bound(native); var list = native.Owner;
        int clears = 0, reports = 0, attempts = 0;
        bool Display() { if (++attempts == 1) throw new InvalidOperationException("auxiliary fade escaped scope"); return true; }
        Assert.False(DetailAttempt(() => native.Available, Display, () => { clears++; selection.Clear(); },
            e => { Assert.Equal("auxiliary fade escaped scope", e.Message); reports++; }));
        Assert.Same(list, native.Owner); Assert.True(native.Available); Assert.False(selection.HasSelection);
        native.Item = new object(); Assert.True(selection.Select(native.Owner, native.Item, native.Data));
        Assert.True(DetailAttempt(() => native.Available, Display, () => clears++, _ => reports++));
        Assert.True(selection.HasSelection); Assert.Equal(1, clears); Assert.Equal(1, reports); Assert.Equal(0, native.Submits);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void InventoryDetailOwnerLossIsNotDowngradedToOptionalFailure(bool duringCleanup)
    {
        bool current = true; int reports = 0;
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => DetailAttempt(() => current,
            () => { if (!duringCleanup) current = false; throw new InvalidOperationException("detail"); },
            () => current = false, _ => reports++));
        Assert.IsType<InvalidOperationException>(error.InnerException); Assert.Equal(0, reports);
    }

    [Fact] public void InventoryDetailCleanupFailureEscapesInsteadOfClaimingRetainedList()
    {
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => DetailAttempt(() => true,
            () => throw new InvalidOperationException("detail"), () => throw new InvalidOperationException("cleanup"), _ => { }));
        Assert.Equal("cleanup", error.InnerException.Message);
    }

    static object OwnedDetail()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortOwnedDetail");
        Assert.NotNull(type); return Activator.CreateInstance(type);
    }
    static void ShowDetail(object state, object owner, object item, object prefab, object destination,
        Func<bool> current, Func<object> create, Action<object> present, Action<object> release) =>
        state.GetType().GetMethod("Show").Invoke(state, new object[] { owner, item, prefab, destination, current, create, present, release });
    static void ClearDetail(object state) => state.GetType().GetMethod("Clear").Invoke(state, null);

    [Fact] public void TasksCustomCounterNeverSharesAnotherPagesPrefabKeyedInstance()
    {
        var prefab = new object(); var primary = new object(); var cache = new Dictionary<object, object> { [prefab] = primary };
        var left = OwnedDetail(); var right = OwnedDetail(); var a = new object(); var b = new object();
        object shownA = null, shownB = null; int releases = 0;
        ShowDetail(left, new object(), new object(), prefab, new object(), () => true, () => a, x => shownA = x, _ => releases++);
        ShowDetail(right, new object(), new object(), prefab, new object(), () => true, () => b, x => shownB = x, _ => releases++);
        Assert.Same(a, shownA); Assert.Same(b, shownB); Assert.Same(primary, cache[prefab]);
        ClearDetail(left); Assert.Equal(1, releases); Assert.Same(primary, cache[prefab]);
        ClearDetail(right); Assert.Equal(2, releases); Assert.Same(primary, cache[prefab]);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void TasksCustomCounterRebindsOnlyAfterExactPriorDetailRetires(int changed)
    {
        var state = OwnedDetail(); object[] keys = { new object(), new object(), new object(), new object() };
        var first = new object(); var next = new object(); int creates = 0, releases = 0;
        void Show() => ShowDetail(state, keys[0], keys[1], keys[2], keys[3], () => true,
            () => { creates++; return creates == 1 ? first : next; }, _ => { }, x => { Assert.Same(first, x); releases++; });
        Show(); Show(); Assert.Equal(1, creates); Assert.Equal(0, releases);
        keys[changed] = new object(); Show(); Assert.Equal(2, creates); Assert.Equal(1, releases);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TasksCounterFailureClearsOnlyOwnedDetailAndAllowsSameSelectionRetry(bool ownerLost)
    {
        var state = OwnedDetail(); var owner = new object(); var item = new object(); var prefab = new object(); var destination = new object();
        bool current = true; int released = 0, creates = 0;
        Action<object> fail = _ => { current = !ownerLost; throw new InvalidOperationException("native counter graph"); };
        Assert.Throws<System.Reflection.TargetInvocationException>(() => ShowDetail(state, owner, item, prefab, destination,
            () => current, () => { creates++; return new object(); }, fail, _ => released++));
        Assert.Equal(1, released); current = true;
        ShowDetail(state, owner, item, prefab, destination, () => current, () => { creates++; return new object(); }, _ => { }, _ => released++);
        Assert.Equal(2, creates); Assert.Equal(1, released);
    }

    [Fact] public void TasksCounterFailedCleanupRetainsInstanceAndBlocksReplacementUntilRetry()
    {
        var state = OwnedDetail(); var owner = new object(); var item = new object(); var prefab = new object(); var destination = new object();
        var retained = new object(); int released = 0, created = 0;
        ShowDetail(state, owner, item, prefab, destination, () => true, () => retained, _ => { },
            x => { Assert.Same(retained, x); if (++released < 3) throw new InvalidOperationException("cleanup"); });
        Assert.Throws<System.Reflection.TargetInvocationException>(() => ClearDetail(state));
        Assert.Throws<System.Reflection.TargetInvocationException>(() => ShowDetail(state, owner, new object(), prefab, destination,
            () => true, () => { created++; return new object(); }, _ => { }, _ => { }));
        Assert.Equal(0, created); ClearDetail(state); Assert.Equal(3, released);
    }

    [Fact] public void TasksCounterRejectsBorrowedPrefabWithoutDestroyingIt()
    {
        var state = OwnedDetail(); var prefab = new object(); int releases = 0;
        Assert.Throws<System.Reflection.TargetInvocationException>(() => ShowDetail(state, new object(), new object(), prefab, new object(),
            () => true, () => prefab, _ => { }, _ => releases++));
        ClearDetail(state); Assert.Equal(0, releases);
    }

    static bool RefreshSlots(object[] slots, object target, Func<object, bool> admitted,
        Func<object, object> saved, Func<object, object> presented, Action<object, object> refresh)
    {
        var method = typeof(DsPortSelectState).Assembly.GetType("DsPortSlotRefresh")?.GetMethod("Try")?.MakeGenericMethod(typeof(object), typeof(object));
        Assert.NotNull(method);
        return (bool)method.Invoke(null, new object[] { slots, target, admitted, saved, presented, refresh });
    }

    [Fact] public void FloatingSlotRefreshRejectsStaleTargetBeforeChangingAnyOwnedSlot()
    {
        var target = new object(); var other = new object(); var equipped = new object(); int refreshes = 0;
        Assert.False(RefreshSlots(new[] { other, target }, target, _ => true, _ => equipped, _ => null, (_, _) => refreshes++));
        Assert.Equal(0, refreshes);
    }

    [Fact] public void FloatingSlotRefreshUpdatesEveryNativeCallbackPeerWithoutSaving()
    {
        var target = new object(); var other = new object(); var equipped = new object(); var stale = new object();
        var presentation = new Dictionary<object, object> { [target] = null, [other] = stale }; int refreshes = 0;
        Assert.True(RefreshSlots(new[] { other, target }, target, _ => true, s => s == target ? null : equipped,
            s => presentation[s], (s, item) => { presentation[s] = item; refreshes++; }));
        Assert.Same(equipped, presentation[other]); Assert.Null(presentation[target]); Assert.Equal(2, refreshes);
    }

    [Theory] [InlineData("config")] [InlineData("save")] [InlineData("duplicate")] [InlineData("foreign-target")]
    public void FloatingSlotRefreshRejectsAuthorityReplacementAndMalformedSlotSet(string change)
    {
        var target = new object(); var other = new object(); var replacement = new object(); bool current = true, savedChanged = false;
        int refreshed = 0;
        var slots = change == "duplicate" ? new[] { target, target } : new[] { target, other };
        Assert.False(RefreshSlots(slots, change == "foreign-target" ? new object() : target, _ => current,
            _ => savedChanged ? replacement : null, _ => null, (_, _) =>
            { refreshed++; if (change == "config") current = false; if (change == "save") savedChanged = true; }));
        if (change == "duplicate" || change == "foreign-target") Assert.Equal(0, refreshed);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void CrestFactoryRestoresExactSourceAuthorityBeforeMetadataRelease(bool setupThrows)
    {
        var method = typeof(DsPortSelectState).Assembly.GetType("DsPortCrestSetup")?.GetMethod("Run")?.MakeGenericMethod(typeof(object));
        Assert.NotNull(method);
        var source = new object(); var metadata = new object(); object bound = null;
        var calls = new List<string>();
        Func<object> create = () => metadata;
        Action<object> setup = asset => { Assert.Same(metadata, asset); bound = asset; calls.Add("setup"); if (setupThrows) throw new InvalidOperationException("native setup"); };
        Action<object> rebind = asset => { Assert.Same(source, asset); bound = asset; calls.Add("rebind"); };
        Action<object> release = asset => { Assert.Same(metadata, asset); Assert.Same(source, bound); calls.Add("release"); };
        if (setupThrows) Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, new object[] { source, create, setup, rebind, release }));
        else method.Invoke(null, new object[] { source, create, setup, rebind, release });
        Assert.Same(source, bound); Assert.Equal(new[] { "setup", "rebind", "release" }, calls);
    }

    [Theory] [InlineData(-1)] [InlineData(4097)] [InlineData(int.MaxValue)]
    public void NativeDetailBudgetRejectsInvalidCountsWithoutTruncationOrLosingRemainingCapacity(int invalid)
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortDetailBudget"); Assert.NotNull(type);
        var budget = Activator.CreateInstance(type); var reserve = type.GetMethod("Reserve");
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => reserve.Invoke(budget, new object[] { invalid }));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        reserve.Invoke(budget, new object[] { 4096 });
        Assert.Throws<System.Reflection.TargetInvocationException>(() => reserve.Invoke(budget, new object[] { 1 }));
    }
    [Fact] public void NativeDetailBudgetBoundsCombinedNativeFactoriesNotJustIndividualCounterCounts()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortDetailBudget"); Assert.NotNull(type);
        var budget = Activator.CreateInstance(type); var reserve = type.GetMethod("Reserve");
        reserve.Invoke(budget, new object[] { 2000 }); reserve.Invoke(budget, new object[] { 2000 });
        Assert.Throws<System.Reflection.TargetInvocationException>(() => reserve.Invoke(budget, new object[] { 97 }));
        reserve.Invoke(budget, new object[] { 96 }); reserve.Invoke(budget, new object[] { 0 });
    }

    sealed class FontNode { public FontNode Fallback; public object Glyph; public object Dictionary; }
    [Fact] public void OwnedNativeFontGraphPreservesCyclesAndSharedGlyphsWithoutInitializingSourceDictionary()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortOwnedGraph"); Assert.NotNull(type);
        var graph = Activator.CreateInstance(type); var method = type.GetMethod("Copy");
        var a = new FontNode { Glyph = new object() }; var b = new FontNode { Glyph = a.Glyph }; a.Fallback = b; b.Fallback = a;
        int allocations = 0, releases = 0;
        FontNode Copy(FontNode source) => (FontNode)method.Invoke(graph, new object[] { source,
            (Func<object>)(() => { allocations++; return new FontNode(); }),
            (Action<object>)(owned => { var n = (FontNode)owned; n.Glyph = method.Invoke(graph, new object[] { source.Glyph, (Func<object>)(() => new object()), (Action<object>)(_ => { }), null }); n.Fallback = Copy(source.Fallback); n.Dictionary = new object(); }),
            (Action<object>)(_ => releases++) });
        var result = Copy(a);
        Assert.NotSame(a, result); Assert.Same(result, result.Fallback.Fallback);
        Assert.Same(result.Glyph, result.Fallback.Glyph); Assert.NotSame(a.Glyph, result.Glyph);
        Assert.Null(a.Dictionary); Assert.Null(b.Dictionary); Assert.Equal(2, allocations);
        type.GetMethod("Clear").Invoke(graph, null); Assert.Equal(2, releases);
    }
    [Fact] public void OwnedNativeFontGraphRetainsFailedResourceAndBlocksNewAllocationUntilExactRetry()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortOwnedGraph"); Assert.NotNull(type);
        var graph = Activator.CreateInstance(type); var copy = type.GetMethod("Copy"); var clear = type.GetMethod("Clear");
        var source = new object(); var owned = new object(); int releases = 0, newAllocations = 0;
        copy.Invoke(graph, new object[] { source, (Func<object>)(() => owned), (Action<object>)(_ => { }),
            (Action<object>)(value => { Assert.Same(owned, value); if (++releases == 1) throw new InvalidOperationException("owned font release"); }) });
        Assert.Throws<System.Reflection.TargetInvocationException>(() => clear.Invoke(graph, null));
        Assert.Throws<System.Reflection.TargetInvocationException>(() => copy.Invoke(graph, new object[] { new object(),
            (Func<object>)(() => { newAllocations++; return new object(); }), (Action<object>)(_ => { }), null }));
        Assert.Equal(0, newAllocations); clear.Invoke(graph, null); Assert.Equal(2, releases);
    }
    [Fact] public void OwnedNativeFontGraphAdoptsAllocationBeforePartialPopulationFails()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortOwnedGraph"); Assert.NotNull(type);
        var graph = Activator.CreateInstance(type); var owned = new object(); int releases = 0;
        Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("Copy").Invoke(graph, new object[] { new object(),
            (Func<object>)(() => owned), (Action<object>)(_ => throw new InvalidOperationException("font population")),
            (Action<object>)(value => { Assert.Same(owned, value); releases++; }) }));
        type.GetMethod("Clear").Invoke(graph, null); Assert.Equal(1, releases);
    }

    [Theory]
    [InlineData("<font=Other>A</font>", "font")]
    [InlineData("<MATERIAL=Other>A</MATERIAL>", "material")]
    [InlineData("<sprite=0>", "sprite")]
    [InlineData(@"\" + "u003cfont=Other>A", "font")]
    [InlineData(@"\U0000003cSPRITE=0>", "sprite")]
    public void OwnedNativeTextPreflightRejectsDirectAndEscapedResourceResolution(string input, string resource)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DsPortTextPreflight.Check(input, null));
        Assert.Contains("<" + resource + ">", error.Message);
    }
    [Theory] [InlineData("font")] [InlineData("material")] [InlineData("sprite")]
    public void OwnedNativeTextPreflightChecksNestedOpeningAndClosingStyles(string resource)
    {
        var styles = new Dictionary<int, string[]>
        {
            [DsPortTextPreflight.StyleHash("outer")] = new[] { "<style=inner>", "</style>" },
            [DsPortTextPreflight.StyleHash("inner")] = new[] { "<b>", "</b><" + resource + "=Other>" }
        };
        var error = Assert.Throws<InvalidOperationException>(() => DsPortTextPreflight.Check("<style=\"outer\" color=red>Label</style>", h => styles.GetValueOrDefault(h)));
        Assert.Contains("native style expansion", error.Message);
    }
    [Fact] public void OwnedNativeTextPreflightPreservesOrdinaryFormattingAndNestedResidentStyles()
    {
        const string input = "<style=outer><color=#ff0000>Label</color></style>";
        string[] Style(int hash) => hash == DsPortTextPreflight.StyleHash("outer") ? new[] { "<style=inner>", "</style>" } :
            hash == DsPortTextPreflight.StyleHash("inner") ? new[] { "<b><i>", "</i></b>" } : null;
        Assert.True(DsPortTextPreflight.Check(input, Style).SetEquals("Label".Select(c => (int)c)));
        Assert.Equal("<style=outer><color=#ff0000>Label</color></style>", input);
    }
    [Fact] public void OwnedNativeTextPreflightNoParseAndPlainTextDoNotResolveLiteralResourceTags()
    {
        Assert.Contains((int)'<', DsPortTextPreflight.Check("<noparse><font=literal></noparse>", null));
        Assert.Contains((int)'<', DsPortTextPreflight.Check("<font=literal>", null, true, false));
        Assert.Throws<InvalidOperationException>(() => DsPortTextPreflight.Check("<noparse>literal</noparse><font=Other>", null));
    }
    [Fact] public void OwnedNativeTextPreflightClosingStyleCannotHideResourceBehindEarlierNoParse()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DsPortTextPreflight.Check(
            "<style=literal>Label</noparse></style>", _ => new[] { "<noparse>", "<font=Other>" }));
        Assert.Contains("<font>", error.Message);
    }
    [Fact] public void OwnedNativeTextPreflightBoundsCyclesAndExpansionButAllowsLongNormalLabels()
    {
        Assert.Throws<InvalidOperationException>(() => DsPortTextPreflight.Check("<style=loop>", _ => new[] { "<style=loop>", "" }));
        Assert.Throws<InvalidOperationException>(() => DsPortTextPreflight.Check("<style=large>", _ => new[] { new string('A', 65536), "" }));
        Assert.True(DsPortTextPreflight.Check(new string('A', 40000), null).SetEquals(new[] { (int)'A' }));
    }
    sealed class GlyphFont
    {
        public bool Owned = true;
        public HashSet<int> Glyphs = new HashSet<int>();
        public List<object> Fallbacks = new List<object>();
    }
    static object ResolveGlyph(GlyphFont font, int character, List<object> visited = null) =>
        DsPortTextPreflight.Resolve(font, character, f => ((GlyphFont)f).Owned,
            (f, c) => { visited?.Add(f); return ((GlyphFont)f).Glyphs.Contains(c); }, f => ((GlyphFont)f).Fallbacks);
    [Fact] public void OwnedNativeTextFallbackPreservesBaseLocalThenSettingsPrecedenceAndAliases()
    {
        var primary = new GlyphFont(); var local = new GlyphFont(); var global = new GlyphFont();
        primary.Fallbacks.AddRange(new object[] { null, local, local, global });
        local.Fallbacks = primary.Fallbacks; global.Glyphs.Add('A');
        var visits = new List<object>(); Assert.Same(global, ResolveGlyph(primary, 'A', visits));
        Assert.Equal(new object[] { primary, local, local, global }, visits);
        local.Glyphs.Add('A'); Assert.Same(local, ResolveGlyph(primary, 'A'));
        primary.Glyphs.Add('A'); Assert.Same(primary, ResolveGlyph(primary, 'A'));
        Assert.Same(primary.Fallbacks, local.Fallbacks);
    }
    [Fact] public void OwnedNativeTextFallbackDoesNotRecursivelySearchCycleOrBorrowedFont()
    {
        var primary = new GlyphFont(); var local = new GlyphFont(); var nested = new GlyphFont(); nested.Glyphs.Add('A');
        primary.Fallbacks.Add(local); local.Fallbacks.AddRange(new object[] { primary, nested });
        Assert.Throws<InvalidOperationException>(() => ResolveGlyph(primary, 'A'));
        local.Owned = false; local.Glyphs.Add('A');
        Assert.Contains("escaped owned graph", Assert.Throws<InvalidOperationException>(() => ResolveGlyph(primary, 'A')).Message);
    }
    [Fact] public void OwnedNativeTextCoverageUsesOnlyChosenTextTypefaceNotOtherPageFonts()
    {
        var latin = new GlyphFont(); latin.Glyphs.Add('A');
        var cjk = new GlyphFont(); cjk.Glyphs.Add(0x4e00);
        Assert.Same(latin, ResolveGlyph(latin, 'A')); Assert.Same(cjk, ResolveGlyph(cjk, 0x4e00));
        Assert.Throws<InvalidOperationException>(() => ResolveGlyph(cjk, 'A'));
    }
    [Fact] public void OwnedNativeTextCompositeMapRetirementBlocksFontsUntilExactRootRetry()
    {
        var queue = new DsPortMapRestoreQueue(); int rootAttempts = 0, fonts = 0, peers = 0;
        var graph = new DsPortOwnedGraph(); graph.Copy(new object(), () => new object(), _ => { }, _ => fonts++);
        queue.Own(() => { if (++rootAttempts == 1) throw new InvalidOperationException("root still live"); graph.Clear(); });
        queue.Own(() => peers++);
        Assert.Throws<AggregateException>(queue.Restore); Assert.Equal(0, fonts); Assert.Equal(1, peers);
        queue.Restore(); Assert.Equal(1, fonts); Assert.Equal(2, rootAttempts); Assert.Equal(1, peers);
    }

    [Theory]
    [InlineData(true, true, false, true)] [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)] [InlineData(true, false, true, false)]
    [InlineData(false, false, true, true)] [InlineData(false, true, false, true)]
    public void LockedSlotNativeRemovalIsNotBlanketRejectedOrConvertedToPlacement(bool locked, bool equipped, bool pending, bool expected)
    {
        var method = typeof(DsPortSelectState).Assembly.GetType("DsPortSlotAction")?.GetMethod("CanPlaceOrRemove");
        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method.Invoke(null, new object[] { locked, equipped, pending }));
    }

    sealed class ActionPage : IDsPortJournalNative
    {
        public DsJournalToken Current;
        public Action DuringCheck, OnRelease, OnPresent;
        public readonly object Page = new object();
        public int Clones, Released;
        public string VisibleContent, VisibleDetail;
        public bool IsCurrent(DsJournalToken token) { DuringCheck?.Invoke(); return token.Same(Current); }
        public string Inspect(DsJournalToken token) => null;
        public object CloneInactive(DsJournalToken token) { Clones++; return Page; }
        public void BindAndVerify(DsJournalToken token, object clone) { }
        public void ActivateForLayout(DsJournalToken token, object clone) { }
        public bool TrySettle(DsJournalToken token, object clone) => true;
        public void Present(DsJournalToken token, object clone) { OnPresent?.Invoke(); VisibleContent = "owned native rows"; VisibleDetail = "owned selected detail"; }
        public void ClearSelection(object clone) { VisibleDetail = null; }
        public void DestroyOwned(object clone) { Assert.Same(Page, clone); OnRelease?.Invoke(); Released++; VisibleContent = null; }
    }
    [Theory] [InlineData("completion")] [InlineData("interruption")] [InlineData("owner-loss")] [InlineData("hide")]
    public void OutgoingPageRetainsVisibleOwnedContentWithoutInteractionUntilRetirement(string reason)
    {
        // Managed page-lifecycle execution with visible-content fixture; not Unity rendering.
        var key = new object();
        var token = new DsJournalToken(key, key, key, key, key, 1);
        var native = new ActionPage { Current = token };
        var state = new DsPortJournalState(native);
        state.Tick(token, 1); state.Tick(token, 2);
        var retain = typeof(DsPortJournalState).GetMethod("RetainOutgoingPresentation");
        Assert.NotNull(retain);
        Assert.True((bool)retain.Invoke(state, null));
        Assert.False(state.Ready);
        Assert.Same(native.Page, state.Owned);
        Assert.Equal("owned native rows", native.VisibleContent);
        Assert.Equal("owned selected detail", native.VisibleDetail);
        Assert.False(state.RefreshAfterAction(native.Page, token, token));
        Assert.Equal(0, native.Released);
        // Each terminal event routes to existing Clear, not a clone/rebind/visual reset.
        if (reason == "owner-loss") native.Current = null;
        state.Clear();
        Assert.Null(native.VisibleContent); Assert.Null(native.VisibleDetail);
        Assert.Null(state.Owned); Assert.Equal(1, native.Clones); Assert.Equal(1, native.Released);
        state.Clear(); Assert.Equal(1, native.Released);
    }
    [Theory] [InlineData("success")] [InlineData("owner")] [InlineData("page")] [InlineData("stale")] [InlineData("reentrant")]
    public void ActionRefreshPreservesExactPageOnlyAfterExplicitCurrentNativeCommit(string change)
    {
        var key = new object();
        var before = new DsJournalToken(key, key, key, key, key, 1, new DsPortPageSnapshot(new object[] { 1 }));
        var after = new DsJournalToken(change == "owner" ? new object() : key, key, key, key, key, 1,
            new DsPortPageSnapshot(new object[] { 2 }));
        var native = new ActionPage { Current = before }; var state = new DsPortJournalState(native);
        state.Tick(before, 1); state.Tick(before, 2); Assert.True(state.Ready);
        var method = typeof(DsPortJournalState).GetMethod("RefreshAfterAction"); Assert.NotNull(method);
        if (change != "stale") native.Current = after;
        if (change == "reentrant") native.DuringCheck = () => state.Clear();
        bool accepted = (bool)method.Invoke(state, new object[] { change == "page" ? new object() : native.Page, before, after });
        Assert.Equal(change == "success", accepted);
        if (accepted) { state.Tick(after, 3); Assert.True(state.Ready); Assert.Same(native.Page, state.Owned); Assert.Equal(1, native.Clones); Assert.Equal(0, native.Released); }
    }

    [Theory] [InlineData("response-loss")] [InlineData("take-loss")] [InlineData("close-event-loss")] [InlineData("close-use-throw")] [InlineData("cancel-pending")]
    public void InventoryConsumeCommitNeverReplaysAndRetainsPendingNativeClose(string fault)
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortConsumeCommit"); Assert.NotNull(type);
        dynamic commit = Activator.CreateInstance(type);
        bool current = true, closing = fault != "response-loss";
        int responses = 0, takes = 0, cancels = 0, uses = 0;
        Action response = () => { responses++; if (fault == "response-loss") current = false; };
        Action take = () => { takes++; if (fault == "take-loss") current = false; };
        try { commit.Commit(closing, true, (Func<bool>)(() => current), response, take); }
        catch (InvalidOperationException) { }
        Assert.Throws<InvalidOperationException>(() => { commit.Commit(closing, true, (Func<bool>)(() => true), response, take); });
        Assert.Equal(closing ? 0 : 1, responses); Assert.Equal(closing ? 1 : 0, takes);
        if (!closing) { Assert.False((bool)commit.PendingClose); return; }
        Assert.True((bool)commit.PendingClose);
        Action cancel = () => { cancels++; if (fault == "close-event-loss") current = false; };
        Action use = () => { uses++; if (fault == "close-use-throw") throw new InvalidOperationException("native tail"); };
        current = true;
        try { commit.FinishClose((Func<bool>)(() => current), cancel, use, fault == "cancel-pending"); }
        catch (InvalidOperationException) { }
        if (fault == "close-event-loss") { Assert.True((bool)commit.PendingClose); current = true; }
        commit.FinishClose((Func<bool>)(() => current), cancel, use, fault == "cancel-pending");
        Assert.False((bool)commit.PendingClose);
        Assert.Equal(fault == "cancel-pending" ? 0 : 1, cancels); Assert.Equal(1, uses);
    }

    static object ActionHold()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortActionHold");
        Assert.NotNull(type); return Activator.CreateInstance(type);
    }
    static object HoldCall(object hold, string method, params object[] args) => hold.GetType().GetMethod(method).Invoke(hold, args);

    [Theory] [InlineData("up")] [InlineData("lost")] [InlineData("down")]
    public void ActionHoldRawObservationInvalidatesContinuationBeforeOverlayConsumption(string change)
    {
        var hold = ActionHold(); HoldCall(hold, "Observe", true, false);
        long generation = (long)HoldCall(hold, "Capture"); int commits = 0;
        if (change == "lost") HoldCall(hold, "Liveness", false);
        else HoldCall(hold, "Observe", change == "down", change == "up");
        Assert.False((bool)HoldCall(hold, "Step", generation, (Func<bool>)(() => true), (Action)(() => commits++)));
        Assert.Equal(0, commits);
    }
    [Fact] public void ActionHoldTapWithoutAcceptedDownCannotStartPaidContinuation()
    {
        var hold = ActionHold(); int calls = 0;
        long generation = (long)HoldCall(hold, "Capture");
        Assert.False((bool)HoldCall(hold, "Step", generation, (Func<bool>)(() => true), (Action)(() => calls++)));
        Assert.Equal(0, calls);
    }
    [Theory] [InlineData("owner")] [InlineData("resource")] [InlineData("selection")] [InlineData("hide")]
    public void ActionHoldRechecksDeferredLegalStateBeforeNativeStep(string lost)
    {
        var hold = ActionHold(); HoldCall(hold, "Observe", true, false);
        long generation = (long)HoldCall(hold, "Capture"); int steps = 0;
        var native = new Native(); var selection = Bound(native);
        var owner = native.Owner; var item = native.Item;
        // Step consumes fresh production selection authority, not a shared false
        // flag. These model ActionBase's owner/selection/visibility inputs and
        // ExtraLegal's resource-dependent CanReload decision; no Unity execution.
        Func<bool> legal = () => selection.ActionAvailable;
        Assert.True((bool)HoldCall(hold, "Step", generation, legal, (Action)(() => steps++)));
        switch (lost)
        {
            case "owner": native.Owner = new object(); break;
            case "resource": native.Legal = false; break;
            case "selection": native.Item = new object(); break;
            case "hide": native.Available = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(lost));
        }
        Assert.Equal(lost != "owner", ReferenceEquals(owner, native.Owner));
        Assert.Equal(lost != "selection", ReferenceEquals(item, native.Item));
        Assert.Equal(lost != "resource", native.Legal);
        Assert.Equal(lost != "hide", native.Available);
        Assert.True((bool)HoldCall(hold, "Current", generation)); // No fabricated release masks the legality loss.
        Assert.False((bool)HoldCall(hold, "Step", generation, legal, (Action)(() => steps++)));
        Assert.Equal(1, steps); Assert.Equal(0, native.Submits);
    }
    [Fact] public void ActionHoldReentrantLegalCheckCannotReuseReleasedGeneration()
    {
        var hold = ActionHold(); HoldCall(hold, "Observe", true, false);
        long generation = (long)HoldCall(hold, "Capture"); int steps = 0;
        Assert.False((bool)HoldCall(hold, "Step", generation,
            (Func<bool>)(() => { HoldCall(hold, "Observe", false, true); return true; }), (Action)(() => steps++)));
        Assert.Equal(0, steps);
    }
    static System.Collections.IEnumerator GuardedRoutine(Func<bool> current, System.Collections.IEnumerator native, Action cancel)
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortGuardedRoutine"); Assert.NotNull(type);
        return (System.Collections.IEnumerator)Activator.CreateInstance(type, current, native, cancel);
    }
    [Fact] public void ActionHoldPassesNativeWaitUnchangedAndCancelsBeforeNextMove()
    {
        var wait = new object(); int commits = 0, cancels = 0; bool current = true;
        System.Collections.IEnumerator NativeRoutine() { yield return wait; commits++; }
        var routine = GuardedRoutine(() => current, NativeRoutine(), () => cancels++);
        Assert.True(routine.MoveNext()); Assert.Same(wait, routine.Current);
        current = false; Assert.False(routine.MoveNext()); Assert.False(routine.MoveNext());
        Assert.Equal(0, commits); Assert.Equal(1, cancels);
    }
    [Fact] public void ActionHoldFailedNativeCancellationBlocksContinuationAndRetriesExactCleanup()
    {
        bool current = true; int cancels = 0, commits = 0;
        System.Collections.IEnumerator NativeRoutine() { yield return new object(); commits++; }
        var routine = GuardedRoutine(() => current, NativeRoutine(), () => { if (++cancels == 1) throw new InvalidOperationException("cancel"); });
        Assert.True(routine.MoveNext()); current = false;
        Assert.Throws<InvalidOperationException>(() => routine.MoveNext());
        current = true; Assert.False(routine.MoveNext());
        Assert.Equal(2, cancels); Assert.Equal(0, commits);
    }

    [Fact] public void BrowsingDisplaysWithoutSubmitting()
    {
        var native = new Native(); var state = Bound(native);
        Assert.Equal(1, native.Displays);
        Assert.Equal(0, native.Submits);
        Assert.Equal(0, native.Checks);
    }

    [Fact] public void ActionRechecksLegalityRatherThanCachedPrompt()
    {
        var native = new Native(); var state = Bound(native);
        Assert.True(state.ActionAvailable);
        native.Legal = false;
        Assert.False(state.TrySubmit());
        Assert.Equal(2, native.Checks);
        Assert.Equal(0, native.Submits);
    }

    [Fact] public void LegalActionDispatchesOnce()
    {
        var native = new Native(); var state = Bound(native);
        Assert.True(state.TrySubmit());
        Assert.Equal(1, native.Submits);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ReplacementOrUnavailableItemInvalidatesSelection(int replacement)
    {
        var native = new Native(); var state = Bound(native);
        if (replacement == 0) native.Owner = new object();
        else if (replacement == 1) native.Item = new object();
        else if (replacement == 2) native.Data = new object();
        else native.Available = false;
        Assert.False(state.TrySubmit());
        Assert.False(state.HasSelection);
        Assert.Equal(0, native.Submits);
    }

    [Fact] public void ClearingSelectionDoesNotDispatchNativeActions()
    {
        var native = new Native(); var state = Bound(native);
        state.Clear();
        Assert.False(state.HasSelection);
        Assert.False(state.TrySubmit());
        Assert.Equal(0, native.Submits);
        // This proves core clearing only, not a Unity page switch or HUD routing.
        Assert.Equal(1, native.Displays);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void GestureStopsAtFirstConsumer(int winner)
    {
        var calls = new List<int>();
        bool Consume(int id) { calls.Add(id); return id == winner; }
        Assert.True(DsPortGesturePrecedence.Consume(() => Consume(0), () => Consume(1), () => Consume(2), () => Consume(3)));
        Assert.Equal(Enumerable.Range(0, winner + 1), calls);
    }

    [Fact] public void ReentrantSelectionCannotReuseAnotherItemsLegalDecision()
    {
        var native = new Native(); var state = Bound(native);
        native.DuringLegalCheck = () =>
        {
            native.Item = new object(); native.Data = new object();
            Assert.True(state.Select(native.Owner, native.Item, native.Data));
        };
        Assert.False(state.TrySubmit());
        Assert.Equal(0, native.Submits);
    }

    [Theory] [InlineData("success")] [InlineData("setup")] [InlineData("render")]
    public void NativeMapTransactionRestoresPresentationIncludingSetupFailure(string failure)
    {
        var method = typeof(DsPortSelectState).Assembly.GetType("DsPortMapTransaction")?.GetMethod("Draw");
        Assert.NotNull(method);
        int primaryTarget = 7, renderCalls = 0, restoreCalls = 0;
        Func<bool> eligible = () => true;
        Func<Action> capture = () => { int retained = primaryTarget; return () => { primaryTarget = retained; restoreCalls++; }; };
        Action setup = () => { primaryTarget = 99; if (failure == "setup") throw new InvalidOperationException("setup"); };
        Action render = () => { renderCalls++; Assert.Equal(99, primaryTarget); if (failure == "render") throw new InvalidOperationException("render"); };
        if (failure == "success") Assert.True((bool)method.Invoke(null, new object[] { eligible, capture, setup, render }));
        else
        {
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, new object[] { eligible, capture, setup, render }));
            Assert.Equal(failure, error.InnerException.Message);
        }
        Assert.Equal(7, primaryTarget); Assert.Equal(1, restoreCalls);
        Assert.Equal(failure == "setup" ? 0 : 1, renderCalls);
    }

    [Theory] [InlineData("before")] [InlineData("capture")] [InlineData("setup")]
    public void NativeMapTransactionYieldsToPrimaryOrReplacementAtEveryBoundary(string boundary)
    {
        var method = typeof(DsPortSelectState).Assembly.GetType("DsPortMapTransaction")?.GetMethod("Draw");
        Assert.NotNull(method);
        bool allowed = boundary != "before"; var calls = new List<string>();
        Func<bool> eligible = () => allowed;
        Func<Action> capture = () => { calls.Add("capture"); if (boundary == "capture") allowed = false; return () => calls.Add("restore"); };
        Action setup = () => { calls.Add("setup"); allowed = false; };
        Action render = () => calls.Add("render");
        Assert.False((bool)method.Invoke(null, new object[] { eligible, capture, setup, render }));
        Assert.Equal(boundary == "before" ? Array.Empty<string>() : boundary == "capture" ? new[] { "capture" } : new[] { "capture", "setup", "restore" }, calls);
    }

    [Fact] public void NativeMapMarkerSnapshotDoesNotInsertOrShareMissingSavedLists()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortMapTransaction");
        var copy = type?.GetMethod("CopyMarkers")?.MakeGenericMethod(typeof(int));
        Assert.NotNull(copy);
        IList<int>[] saved = { new List<int> { 1, 2, 3 }, null };
        int reads = 0;
        Func<int, IList<int>> read = index => { reads++; return index < saved.Length ? saved[index] : null; };
        var owned = (int[][])copy.Invoke(null, new object[] { 3, 2, read });
        Assert.Equal(3, reads); Assert.Equal(new[] { 1, 2 }, owned[0]);
        Assert.Empty(owned[1]); Assert.Empty(owned[2]); Assert.Null(saved[1]);
        saved[0][0] = 99; Assert.Equal(1, owned[0][0]);
        owned[0][1] = 88; Assert.Equal(2, saved[0][1]); Assert.Equal(3, saved[0].Count);
    }

    static object MapRestoreQueue()
    {
        var type = typeof(DsPortSelectState).Assembly.GetType("DsPortMapRestoreQueue");
        Assert.NotNull(type);
        return Activator.CreateInstance(type);
    }
    static void MapQueueCall(object queue, string method, params Action[] actions) =>
        queue.GetType().GetMethod(method).Invoke(queue, actions.Cast<object>().ToArray());

    [Fact] public void NativeMapCaptureWithoutSetupReleasesOnlyOwnedResources()
    {
        var queue = MapRestoreQueue(); int native = 7, releases = 0;
        MapQueueCall(queue, "Own", () => releases++);
        native = 11; // The primary producer changed after capture; no adapter write.
        MapQueueCall(queue, "Restore"); MapQueueCall(queue, "Restore");
        Assert.Equal(11, native); Assert.Equal(1, releases);
    }

    [Fact] public void NativeMapPartialRestoreRetriesOnlyFailedFieldsBeforeRetirement()
    {
        var queue = MapRestoreQueue(); int first = 7, second = 8, releases = 0, attempts = 0;
        MapQueueCall(queue, "Own", () => releases++);
        MapQueueCall(queue, "Change", () => first = 7, () => first = 99);
        MapQueueCall(queue, "Change", () => { if (++attempts == 1) throw new InvalidOperationException("restore"); second = 8; }, () => second = 99);
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => MapQueueCall(queue, "Restore"));
        Assert.IsType<AggregateException>(error.InnerException);
        Assert.Equal(7, first); Assert.Equal(99, second); Assert.Equal(0, releases);
        first = 12; // New primary presentation after this field has been restored.
        MapQueueCall(queue, "Restore");
        Assert.Equal(12, first); Assert.Equal(8, second); Assert.Equal(2, attempts); Assert.Equal(1, releases);
    }

    [Fact] public void NativeMapPartialSetupRestoresExactRetainedOwnerNotReplacement()
    {
        var queue = MapRestoreQueue(); int[] oldOwner = { 7 }; var currentOwner = oldOwner;
        var retained = currentOwner; int releases = 0;
        MapQueueCall(queue, "Own", () => releases++);
        Assert.Throws<System.Reflection.TargetInvocationException>(() => MapQueueCall(queue, "Change",
            () => retained[0] = 7, () => { retained[0] = 99; throw new InvalidOperationException("partial setup"); }));
        currentOwner = new[] { 23 };
        MapQueueCall(queue, "Restore");
        Assert.Equal(7, oldOwner[0]); Assert.Equal(23, currentOwner[0]); Assert.Equal(1, releases);
    }

    [Fact] public void NativeMapFailedRetirementRetainsOnlyUnreleasedResource()
    {
        var queue = MapRestoreQueue(); int first = 0, second = 0;
        MapQueueCall(queue, "Own", () => first++);
        MapQueueCall(queue, "Own", () => { if (++second == 1) throw new InvalidOperationException("release"); });
        Assert.Throws<System.Reflection.TargetInvocationException>(() => MapQueueCall(queue, "Restore"));
        MapQueueCall(queue, "Restore");
        Assert.Equal(1, first); Assert.Equal(2, second);
    }

    [Fact] public void NativeMapPendingWritesBlockOuterReleaseAndReplacementUntilRestorationOnlyRetry()
    {
        var queue = new DsPortMapRestoreQueue(); int old = 7, replacement = 23, attempts = 0, buffers = 0;
        int retired = 0, releasedParent = 0, disposedContent = 0;
        queue.Own(() => buffers++);
        queue.Change(() => { if (++attempts < 3) throw new InvalidOperationException("live old target"); old = 7; }, () => old = 99);
        var state = new DsHudReleaseState(queue.Restore, () => disposedContent++, () => releasedParent++, () => retired++);
        state.RequestShutdown(state.ReleasePresentation);
        Assert.True(state.Pending); Assert.True(state.BlocksReplacement); Assert.False(state.CanRoute);
        Assert.Equal(0, buffers + disposedContent + releasedParent + retired);
        Assert.False(state.Retry());
        Assert.Equal(0, buffers + disposedContent + releasedParent + retired);
        Assert.True(state.Retry());
        Assert.Equal(7, old); Assert.Equal(23, replacement);
        Assert.Equal(1, buffers); Assert.Equal(1, disposedContent); Assert.Equal(1, releasedParent); Assert.Equal(1, retired);
        Assert.False(state.BlocksReplacement); Assert.True(state.Completed);
        Assert.Throws<InvalidOperationException>(() => queue.Change(() => { }, () => replacement = 99));
        Assert.Equal(23, replacement);
    }

    [Theory]
    [InlineData(true, false, true, false, false, false, false, true)]
    [InlineData(false, false, true, false, false, false, false, false)]
    [InlineData(true, true, true, false, false, false, false, false)]
    [InlineData(true, false, false, false, false, false, false, false)]
    [InlineData(true, false, true, true, true, false, true, true)]
    [InlineData(true, false, true, true, false, true, false, true)]
    [InlineData(true, false, true, true, false, false, false, false)]
    [InlineData(true, false, true, true, false, true, true, false)]
    public void NativeMapWorldPinsUseReadOnlyNativeActivityPolicy(bool active, bool other, bool zone, bool parent, bool mapped, bool visited, bool hidden, bool expected)
    {
        var method = typeof(DsPortMapTransaction).GetMethod("PinCanBeActive");
        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method.Invoke(null, new object[] { active, other, zone, parent, mapped, visited, hidden }));
    }

    [Theory] [InlineData("override", true, "override")] [InlineData(null, true, "DustMazeCompassMarker")] [InlineData(null, false, "room")]
    public void NativeMapCompassScenePreservesOverrideAndMazePrecedence(string overridden, bool maze, string expected)
    {
        var method = typeof(DsPortMapTransaction).GetMethod("CompassScene");
        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, new object[] { overridden, maze, "room" }));
    }

    [Fact] public void NativeMapColdResidencyRetriesBoundedlyAndOwnerChangeRearmsImmediately()
    {
        var type = typeof(DsPortMapTransaction).Assembly.GetType("DsPortMapResidency");
        Assert.NotNull(type); var retry = Activator.CreateInstance(type); var owner = new object();
        bool Probe(object current, long epoch, double now) => (bool)type.GetMethod("Probe").Invoke(retry, new object[] { current, "room", epoch, now });
        void Wait() => type.GetMethod("Wait").Invoke(retry, null);
        Assert.True(Probe(owner, 1, 10)); Wait();
        Assert.False(Probe(owner, 1, 10.49)); Assert.True(Probe(owner, 1, 10.5)); Wait();
        Assert.False(Probe(owner, 1, 10.6)); Assert.True(Probe(owner, 2, 10.6)); Wait();
        owner = new object(); Assert.True(Probe(owner, 2, 10.7)); Wait();
        Assert.False(Probe(owner, 2, 10.8)); Assert.True(Probe(owner, 2, 11.2));
        type.GetMethod("Ready").Invoke(retry, null);
        Assert.True(Probe(owner, 2, 11.21)); Assert.True(Probe(owner, 2, 11.22));
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f)] [InlineData(4f, 9f, 4f, 9f)]
    [InlineData(8f, 12f, 4f, 9f)] [InlineData(-8f, -12f, -4f, -9f)]
    [InlineData(1f, -12f, 1f, -9f)]
    public void NativeMapCorpseArrowProjectsOntoOwnedViewportWithoutPrimaryCollider(float x, float y, float expectedX, float expectedY)
    {
        DsPortMapTransaction.ProjectToViewport(x, y, -4f, -9f, 4f, 9f,
            out float projectedX, out float projectedY);
        Assert.Equal(expectedX, projectedX); Assert.Equal(expectedY, projectedY);
    }

    static object MapPolicy(string name, params object[] args)
    {
        var method = typeof(DsPortMapTransaction).GetMethod(name);
        Assert.NotNull(method);
        return method.Invoke(null, args);
    }
    static T Recipe<T>(object recipe, string field) => (T)recipe.GetType().GetField(field).GetValue(recipe);

    [Theory]
    [InlineData(0, false, false, false, true, false, -1, false)]
    [InlineData(1, false, false, false, true, false, 0, false)]
    [InlineData(1, true, true, false, true, false, 1, true)]
    [InlineData(1, true, true, false, true, true, 0, false)]
    [InlineData(2, false, false, false, true, true, 0, true)]
    [InlineData(0, false, false, true, true, false, 0, true)]
    public void NativeMapColdRoomRecipeHasNoInitializedCacheDependency(int state, bool full, bool savedMapped,
        bool inherited, bool quill, bool hidden, int sprite, bool localMapped)
    {
        var recipe = MapPolicy("RoomRecipe", state, full, savedMapped, false, inherited, false,
            quill, hidden, Array.Empty<bool>(), Array.Empty<bool>(), false);
        Assert.Equal(sprite, Recipe<int>(recipe, "Sprite"));
        Assert.Equal(localMapped, Recipe<bool>(recipe, "LocalMapped"));
        Assert.Equal(localMapped || inherited, Recipe<bool>(recipe, "Mapped"));
        Assert.Equal(savedMapped || inherited, Recipe<bool>(recipe, "Visited"));
    }

    [Fact] public void NativeMapRoomRecipeUsesFirstConditionsAndIndependentVisitedInheritance()
    {
        var recipe = MapPolicy("RoomRecipe", 1, true, true, false, false, true, true, false,
            new[] { false, true, true }, new[] { true, true }, true);
        Assert.Equal(3, Recipe<int>(recipe, "Sprite")); Assert.Equal(1, Recipe<int>(recipe, "Color"));
        Assert.True(Recipe<bool>(recipe, "Visited")); Assert.False(Recipe<bool>(recipe, "Visible"));
        var visited = MapPolicy("RoomRecipe", 0, false, false, false, false, true, true, false,
            Array.Empty<bool>(), Array.Empty<bool>(), false);
        Assert.False(Recipe<bool>(visited, "LocalVisited")); Assert.True(Recipe<bool>(visited, "Visited"));
        Assert.False(Recipe<bool>(visited, "AllChildren"));
    }

    [Fact] public void NativeMapSameCountMembershipAndConditionChangesRecomputeRecipe()
    {
        var mapped = new HashSet<string> { "A" }; var dependencies = new string[] { null, "A" };
        Assert.True((bool)MapPolicy("OtherMapped", dependencies, mapped));
        mapped.Remove("A"); mapped.Add("B");
        Assert.False((bool)MapPolicy("OtherMapped", dependencies, mapped));
        Assert.False((bool)MapPolicy("OtherMapped", new string[] { null }, mapped));
        var first = MapPolicy("RoomRecipe", 1, true, true, false, false, false, true, false,
            new[] { true }, Array.Empty<bool>(), false);
        var next = MapPolicy("RoomRecipe", 1, true, true, false, false, false, true, false,
            new[] { false }, Array.Empty<bool>(), false);
        Assert.Equal(2, Recipe<int>(first, "Sprite")); Assert.Equal(1, Recipe<int>(next, "Sprite"));
    }

    [Theory] [InlineData(0, 3, -4f)] [InlineData(1, 3, 0f)] [InlineData(2, 3, 4f)] [InlineData(0, 1, 0f)]
    public void NativeMapPinLayoutUsesVisibleChildOrderOnly(int index, int count, float expected)
    {
        Assert.Equal(expected, (float)MapPolicy("LayoutOffset", 4f, index, count));
    }

    [Fact] public void NativeMapDisabledTextRetainsGeometryWithoutMeshGetterOrLifecycle()
    {
        var retained = new object(); var filter = new object();
        Assert.Same(retained, MapPolicy("TextGeometry", null, retained, "Label"));
        Assert.Same(filter, MapPolicy("TextGeometry", filter, retained, "Label"));
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => MapPolicy("TextGeometry", null, null, "Zone/Label"));
        Assert.Contains("NEEDS_CONTEXT ungenerated native text: Zone/Label", error.InnerException.Message);
    }

    [Fact] public void AbsentModsConsumerDoesNotBlockPage()
    {
        int pages = 0;
        Assert.True(DsPortGesturePrecedence.Consume(() => false, null, () => false, () => { pages++; return true; }));
        Assert.Equal(1, pages);
    }
}
