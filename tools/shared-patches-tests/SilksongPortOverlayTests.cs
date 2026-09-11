using Xunit;

public sealed class SilksongPortOverlayTests
{
    [Theory] [InlineData("live", true)] [InlineData("cold", true)] [InlineData("foreign", false)]
    [InlineData("missing-live", false)] [InlineData("missing-local", false)]
    public void NativeFadeBridgeAdmissionRetainsOnlyExactLocalVisualReference(string change, bool expected)
    {
        var method = typeof(DsPortOverlayTargets).GetMethod("BridgeReference"); Assert.NotNull(method);
        var local = new object(); object cached = change == "foreign" ? new object() :
            change == "cold" || change == "missing-live" ? null : local;
        Assert.Equal(expected, (bool)method.Invoke(null, new object[] { cached,
            change == "missing-local" ? null : local, change != "cold" }));
    }

    [Theory] [InlineData("parent-after-write")] [InlineData("sibling-before-write")] [InlineData("sibling-after-write")]
    public void DialogueAndCreditsRetainOutstandingSiblingAcrossExactParentReturn(string failure)
    {
        var state = new DsPortOverlayParentRestore(); var original = new object(); var carrier = new object();
        object parent = carrier; int siblings = 8, parentWrites = 0, siblingWrites = 0, releases = 0;
        bool fail = true;
        void Restore()
        {
            state.Return(carrier, original, () => parent, () => { parent = original; parentWrites++;
                if (fail && failure == "parent-after-write") throw new InvalidOperationException(failure); });
            state.Sibling(() => { siblingWrites++; if (fail && failure == "sibling-before-write") throw new InvalidOperationException(failure);
                siblings = 2; if (fail && failure == "sibling-after-write") throw new InvalidOperationException(failure); });
        }
        var lease = new DsPortDialogueLease(() => true, () => carrier, _ => { }, _ => Restore(), _ => releases++);
        Assert.True(lease.Tick()); Assert.Throws<InvalidOperationException>(lease.Restore);
        Assert.Same(original, parent); Assert.True(lease.Pending); Assert.Equal(0, releases);
        fail = false; lease.Restore(); Assert.Equal(2, siblings); Assert.Equal(1, parentWrites);
        Assert.Equal(failure == "parent-after-write" ? 1 : 2, siblingWrites); Assert.Equal(1, releases);
    }

    class MessageOwner { public bool Live = true; }
    sealed class DerivedMessageOwner : MessageOwner { }
    static object RegisteredMessage(IEnumerable<object> owners, Func<object, bool> live)
    {
        var method = typeof(DsPortOverlayTargets).GetMethod("Message");
        Assert.NotNull(method);
        return method.Invoke(null, new object[] { typeof(MessageOwner), owners, live });
    }
    [Fact] public void PowerUpMessageSelectsExactLiveRegisteredTypeWithoutSceneScanning()
    {
        var owner = new MessageOwner();
        var dead = new MessageOwner { Live = false };
        Assert.Same(owner, RegisteredMessage(new object[] { new object(), new DerivedMessageOwner(), dead, owner, owner },
            value => ((MessageOwner)value).Live));
        Assert.Null(RegisteredMessage(new object[] { dead, new DerivedMessageOwner() }, value => ((MessageOwner)value).Live));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PowerUpMessageRejectsAmbiguousOrOverBoundRegistrationEvenAfterFirstMatch(bool overBound)
    {
        var owner = new MessageOwner();
        var registrations = new List<object> { owner };
        if (overBound) for (int i = 0; i < 4096; i++) registrations.Add(new object());
        else registrations.Add(new MessageOwner());
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => RegisteredMessage(registrations, _ => true));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PowerUpMessageUnregistrationOrReplacementRestoresBeforeNewOwner(bool replace)
    {
        var owner = new MessageOwner(); var replacement = new MessageOwner();
        var registrations = new List<object> { owner };
        int restored = 0, released = 0, nativeSequence = 0;
        var lease = new DsPortDialogueLease(() => ReferenceEquals(owner, RegisteredMessage(registrations, _ => true)),
            () => owner, _ => { nativeSequence++; }, value => { Assert.Same(owner, value); restored++; }, _ => released++);
        Assert.True(lease.Tick());
        registrations.Clear(); if (replace) registrations.Add(replacement);
        Assert.False(lease.Tick());
        Assert.Equal(1, restored); Assert.Equal(1, released); Assert.Equal(1, nativeSequence);
        Assert.Same(replace ? replacement : null, RegisteredMessage(registrations, _ => true));
    }

    [Fact] public void SkillGetMessageKeepsExactOwnerGenerationAndNeverReplaysProgression()
    {
        var owner = new MessageOwner(); var replacement = new MessageOwner();
        var registrations = new List<object> { owner };
        int presented = 0, restored = 0, released = 0, progression = 17;
        var generation = new DsPortCompanionDismissState();
        generation.Begin(); generation.Arm(); int observed = generation.Observe();
        var lease = new DsPortDialogueLease(
            () => ReferenceEquals(owner, RegisteredMessage(registrations, _ => true)),
            () => owner,
            value => { Assert.Same(owner, value); presented++; },
            value => { Assert.Same(owner, value); restored++; },
            value => { Assert.Same(owner, value); released++; });
        Assert.True(lease.Tick());
        Assert.True(generation.Request(observed));
        Assert.True(generation.Consume());
        registrations[0] = replacement;
        Assert.False(lease.Tick());
        Assert.False(generation.Request(observed));
        Assert.Equal(1, presented); Assert.Equal(1, restored); Assert.Equal(1, released);
        Assert.Equal(17, progression);
    }

    static object PopupIsland(object root, object target, Func<object, object> parent)
    {
        var method = typeof(DsPortOverlayTargets).GetMethod("Island");
        Assert.NotNull(method);
        return method.Invoke(null, new object[] { root, target, parent });
    }
    [Fact] public void ItemPopupRoutesOnlyVisualIslandNeverNativePositionAuthority()
    {
        var root = new object(); var island = new object(); var nested = new object();
        Func<object, object> parent = node => ReferenceEquals(node, nested) ? island : ReferenceEquals(node, island) ? root : null;
        Assert.Same(island, PopupIsland(root, nested, parent));
        Assert.Same(island, PopupIsland(root, island, parent));
        Assert.Null(PopupIsland(root, root, parent));
        Assert.Null(PopupIsland(root, new object(), parent));
        Assert.Null(PopupIsland(root, nested, _ => nested));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ItemPopupPeerIslandRestoresIndependentlyBeforeAnyCarrierRelease(bool afterWrite)
    {
        object root = new object(), first = new object(), second = new object();
        object firstParent = first, secondParent = second;
        int rootY = 7, releases = 0; bool fail = true;
        var queue = new DsPortMapRestoreQueue(); var returned = new DsPortOverlayParentRestore();
        queue.Change(() => returned.Return(first, root, () => firstParent, () =>
        {
            if (afterWrite) firstParent = root;
            if (fail) throw new InvalidOperationException("native parent callback");
            firstParent = root;
        }), () => { });
        queue.Change(() => secondParent = root, () => { });
        queue.Own(() => releases++); queue.Own(() => releases++);
        Assert.Throws<AggregateException>(() => queue.Restore());
        Assert.Same(root, secondParent); Assert.Equal(0, releases); Assert.Equal(7, rootY);
        fail = false; queue.Restore();
        Assert.Same(root, firstParent); Assert.Equal(2, releases); Assert.Equal(7, rootY);
    }

    [Fact] public void ItemPopupRootLayoutKeepsActualTargetsAndRoutesTheirVisualDescendants()
    {
        var method = typeof(DsPortOverlayTargets).GetMethod("VisualIsland");
        Assert.NotNull(method);
        object root = new object(), controlled = new object(), visual = new object(), renderer = new object();
        Func<object, object> parent = value => ReferenceEquals(value, renderer) ? visual :
            ReferenceEquals(value, visual) ? controlled : ReferenceEquals(value, controlled) ? root : null;
        Func<object, bool> anchored = value => ReferenceEquals(value, root) || ReferenceEquals(value, controlled);
        object Resolve(object target) => method.Invoke(null, new object[] { root, target, parent, anchored });
        Assert.Same(visual, Resolve(renderer));
        Assert.Null(Resolve(controlled)); // Exact native layout target cannot move.
        Assert.Null(Resolve(root)); // Exact stack/spawn-limit authority cannot move.
        Assert.Null(Resolve(new object()));
    }

    [Fact] public void ItemPopupFixedAnchorMovementAndRectPivotResizeUseCurrentNativeBasis()
    {
        var sameRect = typeof(DsPortOverlayPlane).GetMethod("SameRect");
        Assert.NotNull(sameRect);
        bool Match(float width, float height, float pivotX, float pivotY, float carrierWidth, float carrierHeight, float carrierX, float carrierY) =>
            (bool)sameRect.Invoke(null, new object[] { width, height, pivotX, pivotY, carrierWidth, carrierHeight, carrierX, carrierY });
        Assert.True(Match(200, 100, .5f, .5f, 200, 100, .5f, .5f));
        Assert.False(Match(300, 150, .2f, .8f, 200, 100, .5f, .5f));
        Assert.True(Match(300, 150, .2f, .8f, 300, 150, .2f, .8f));
        Assert.False(Match(float.NaN, 150, .2f, .8f, 300, 150, .2f, .8f));
        foreach (var frame in new[] { (origin: 3f, width: 200f, pivot: .5f), (origin: -7f, width: 300f, pivot: .2f) })
        {
            const float parentScale = 2f, cameraX = 11f;
            float childLocal = (.75f - frame.pivot) * frame.width + 4f;
            float nativeWorld = frame.origin + parentScale * childLocal;
            Assert.True(DsPortOverlayPlane.TryMap(5, 2, 200, 100, parentScale, 1, (cameraX - frame.origin) / parentScale, 0,
                out float sx, out _, out float offset, out _));
            Assert.InRange(Math.Abs(sx * childLocal + offset - (nativeWorld - cameraX) * 9.4f), 0, .001f);
        }
    }

    static bool LoreVisible(object owner, object current, bool live, bool running, bool hiding, float alpha)
    {
        var method = typeof(DsPortOverlayTargets).GetMethod("LoreVisible");
        Assert.NotNull(method);
        return (bool)method.Invoke(null, new object[] { owner, current, live, running, hiding, alpha });
    }
    [Theory] [InlineData(true, false, 0f)] [InlineData(false, true, 0f)] [InlineData(false, false, .3f)]
    public void LoreKeepsExactNativeOwnerThroughSequenceHideAndResidualFade(bool running, bool hiding, float alpha)
    {
        var owner = new object();
        Assert.True(LoreVisible(owner, owner, true, running, hiding, alpha));
        Assert.False(LoreVisible(owner, new object(), true, running, hiding, alpha));
        Assert.False(LoreVisible(owner, owner, false, running, hiding, alpha));
    }
    [Theory] [InlineData(0f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void LoreDoesNotInventVisibilityAfterNativeSequenceAndFadeEnd(float alpha)
    {
        var owner = new object();
        Assert.False(LoreVisible(owner, owner, true, false, false, alpha));
        Assert.False(LoreVisible(null, null, true, true, true, alpha));
    }
    [Fact] public void LoreNativeOwnerLossDuringPresentationRestoresWithoutContinuingTextOrAnimation()
    {
        object owner = new object(), current = owner; bool hide = true;
        int restored = 0, released = 0;
        var lease = new DsPortDialogueLease(() => LoreVisible(owner, current, true, false, hide, 0),
            () => owner, _ => current = new object(), _ => restored++, _ => released++);
        Assert.False(lease.Tick());
        Assert.Equal(1, restored); Assert.Equal(1, released); Assert.False(lease.Pending);
    }

    sealed class Native : IDsPortFade
    {
        public object Owner = new object(), PresentedOwner;
        public float Alpha = .4f, PresentedAlpha;
        public bool Available = true;
        public int Reads, Presents, Clears;
        public bool TryRead(out object owner, out float alpha)
        { Reads++; owner = Owner; alpha = Alpha; return Available; }
        public void Present(object owner, float alpha)
        { Presents++; PresentedOwner = owner; PresentedAlpha = alpha; }
        public void Clear() { Clears++; PresentedOwner = null; }
    }

    [Theory]
    [InlineData(1240, 1080, 207, 180)]
    [InlineData(3840, 2160, 256, 144)]
    [InlineData(1, 1, 1, 1)]
    public void SceneryPassUsesBoundedOneSixthResolution(int panelW, int panelH, int expectedW, int expectedH)
    {
        Assert.True(DsPortSceneryState.TrySize(true, true, panelW, panelH, out int w, out int h));
        Assert.Equal(expectedW, w); Assert.Equal(expectedH, h);
    }

    [Theory] [InlineData(false, true, 1240, 1080)] [InlineData(true, false, 1240, 1080)]
    [InlineData(true, true, 0, 1080)] [InlineData(true, true, 1240, -1)]
    public void MissingNativeBackgroundOrBlurNeverFallsBackToGameplay(bool nativeBackground, bool blur, int w, int h)
    {
        Assert.False(DsPortSceneryState.TrySize(nativeBackground, blur, w, h, out int outputW, out int outputH));
        Assert.Equal(0, outputW); Assert.Equal(0, outputH);
    }

    sealed class Scenery : IDsPortScenery
    {
        public object Owner = new object(), Texture = new object(), PresentedOwner, PresentedTexture;
        public bool Available = true, NativeBackground = true, Blurred = true;
        public int Presents, Clears, Width, Height;
        public float Brightness;
        public bool TryRead(out object owner, out object texture, out bool nativeBackground, out bool blurred)
        { owner=Owner; texture=Texture; nativeBackground=NativeBackground; blurred=Blurred; return Available; }
        public void Present(object owner, object texture, int width, int height, float brightness)
        { Presents++; PresentedOwner=owner; PresentedTexture=texture; Width=width; Height=height; Brightness=brightness; }
        public void Clear() { Clears++; PresentedOwner=PresentedTexture=null; }
    }

    [Fact] public void SceneryWaitsForLaterFrameAndUsesDimmedNativeBlurredOutput()
    {
        var native = new Scenery(); var state = new DsPortSceneryState(native);
        state.Tick(true, 10, 1240, 1080);
        state.Tick(true, 10, 1240, 1080);
        Assert.Equal(0, native.Presents);
        state.Tick(true, 11, 1240, 1080);
        Assert.True(state.HasVisibleScenery);
        Assert.Same(native.Owner, native.PresentedOwner);
        Assert.Same(native.Texture, native.PresentedTexture);
        Assert.Equal(.08f, native.Brightness);
        Assert.Equal(207, native.Width); Assert.Equal(180, native.Height);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void SceneryReplacementOrUnavailabilityCannotLeaveStaleOutput(int change)
    {
        var native = new Scenery(); var state = new DsPortSceneryState(native);
        state.Tick(true, 1, 1240, 1080); state.Tick(true, 2, 1240, 1080);
        Assert.True(state.HasVisibleScenery);
        if (change == 0) native.Owner = new object();
        if (change == 1) native.Texture = new object();
        if (change == 2) native.Available = false;
        if (change == 3) native.Blurred = false;
        state.Tick(change != 4, 3, 1240, 1080);
        Assert.False(state.HasVisibleScenery);
        Assert.Null(native.PresentedTexture);
        native.Available = native.Blurred = true;
        state.Tick(true, 4, 1240, 1080); state.Tick(true, 5, 1240, 1080);
        Assert.True(state.HasVisibleScenery);
        Assert.Same(native.Owner, native.PresentedOwner);
        Assert.Same(native.Texture, native.PresentedTexture);
    }

    [Fact] public void NativeOpacityIsPresentedWithoutChangingItsSource()
    {
        var native = new Native(); var state = new DsPortFadeState(native);
        state.Tick(true);
        Assert.Same(native.Owner, native.PresentedOwner);
        Assert.Equal(.4f, native.PresentedAlpha);
        Assert.Equal(.4f, native.Alpha);
        Assert.True(state.HasVisibleFade);
    }

    [Fact] public void ReplacementOwnerIsReadAgainBeforeGestureConsumption()
    {
        var native = new Native(); var state = new DsPortFadeState(native);
        state.Tick(true);
        native.Owner = new object(); native.Alpha = .8f;
        Assert.True(state.ConsumeGesture(true));
        Assert.Same(native.Owner, native.PresentedOwner);
        Assert.Equal(.8f, native.PresentedAlpha);
        Assert.Equal(2, native.Reads);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void SourceLossZeroOpacityAndTransportOffClearThePriorFade(int reason)
    {
        var native = new Native(); var state = new DsPortFadeState(native);
        state.Tick(true);
        if (reason == 0) native.Available = false;
        if (reason == 1) native.Alpha = 0;
        if (reason == 2) native.Owner = null;
        Assert.False(state.ConsumeGesture(reason != 3));
        Assert.False(state.HasVisibleFade);
        Assert.Null(native.PresentedOwner);
        Assert.Equal(1, native.Clears);
    }

    [Theory] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-.2f)]
    public void InvalidNativeOpacityDoesNotInventAFade(float alpha)
    {
        var native = new Native { Alpha = alpha }; var state = new DsPortFadeState(native);
        state.Tick(true);
        Assert.False(state.HasVisibleFade);
        Assert.Equal(0, native.Presents);
        Assert.Equal(1, native.Clears);
    }

    static object DialogueLease(Func<bool> current, Func<object> acquire, Action<object> present, Action<object> restore, Action<object> release)
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortDialogueLease");
        Assert.NotNull(type);
        return Activator.CreateInstance(type, current, acquire, present, restore, release);
    }
    static bool DialogueTick(object lease) => (bool)lease.GetType().GetMethod("Tick").Invoke(lease, null);
    static void DialogueRestore(object lease) => lease.GetType().GetMethod("Restore").Invoke(lease, null);

    [Fact] public void DialogueNativePoseChangesDuringRouteAndRestoreAreNeverRolledBack()
    {
        var originalParent = new object(); var carrier = new object(); object parent = originalParent;
        int pose = 7, fade = 3, releases = 0;
        var lease = DialogueLease(() => true, () => carrier,
            _ => { parent = carrier; pose += 5; },
            _ => { parent = originalParent; pose += 11; }, _ => releases++);
        Assert.True(DialogueTick(lease)); Assert.Equal(12, pose);
        pose = 29; fade = 8; // Native conversation/animation progresses independently.
        Assert.True(DialogueTick(lease)); Assert.Equal(34, pose);
        DialogueRestore(lease);
        Assert.Equal(45, pose); Assert.Equal(8, fade); Assert.Same(originalParent, parent); Assert.Equal(1, releases);
    }

    [Fact] public void DialogueRestoreFailureBlocksRebindAndCarrierDestructionUntilRetry()
    {
        var owner = new object(); var retained = owner; int presentations = 0, restores = 0, releases = 0, captures = 0;
        var lease = DialogueLease(() => ReferenceEquals(owner, retained), () => { captures++; return retained; },
            _ => presentations++, _ => { if (++restores == 1) throw new InvalidOperationException("live parent restore"); }, _ => releases++);
        Assert.True(DialogueTick(lease)); owner = new object();
        Assert.Throws<System.Reflection.TargetInvocationException>(() => DialogueTick(lease));
        Assert.Equal(0, releases); Assert.Equal(1, presentations);
        Assert.False(DialogueTick(lease));
        Assert.Equal(1, releases); Assert.Equal(1, captures); Assert.Equal(1, presentations);
    }

    [Fact] public void DialogueOwnerLossDuringRouteRestoresExactLeaseBeforePublication()
    {
        bool current = true; var retained = new object(); int restores = 0, releases = 0;
        var lease = DialogueLease(() => current, () => retained, _ => current = false,
            owner => { Assert.Same(retained, owner); restores++; }, owner => { Assert.Same(retained, owner); releases++; });
        Assert.False(DialogueTick(lease)); Assert.Equal(1, restores); Assert.Equal(1, releases);
    }

    [Fact] public void DialoguePartialRouteFailureRetainsRecoveryWithoutSecondPresentation()
    {
        int attempts = 0, released = 0;
        var lease = DialogueLease(() => true, () => new object(), _ => throw new InvalidOperationException("route"),
            _ => { if (++attempts == 1) throw new InvalidOperationException("restore"); }, _ => released++);
        Assert.Throws<System.Reflection.TargetInvocationException>(() => DialogueTick(lease));
        Assert.Equal(0, released);
        Assert.False(DialogueTick(lease)); Assert.Equal(1, released); Assert.Equal(2, attempts);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void NativeOverlayFamiliesRestoreIndependentlyAndRetainOnlyFailedLeases(int failed)
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortOverlayRestoration");
        Assert.NotNull(type);
        var owners = new[] { new object(), new object(), new object() };
        var restores = new int[3]; var releases = new int[3];
        var leases = Enumerable.Range(0, 3).Select(i => DialogueLease(() => true, () => owners[i], _ => { },
            value => { Assert.Same(owners[i], value); if (++restores[i] == 1 && i == failed) throw new InvalidOperationException("native family restore"); },
            value => { Assert.Same(owners[i], value); releases[i]++; })).ToArray();
        foreach (var lease in leases) Assert.True(DialogueTick(lease));
        Action[] actions = leases.Select(lease => (Action)(() => DialogueRestore(lease))).ToArray();
        var method = type.GetMethod("All");
        Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, new object[] { actions }));
        for (int i = 0; i < 3; i++) { Assert.Equal(1, restores[i]); Assert.Equal(i == failed ? 0 : 1, releases[i]); }
        method.Invoke(null, new object[] { actions });
        for (int i = 0; i < 3; i++) { Assert.Equal(i == failed ? 2 : 1, restores[i]); Assert.Equal(1, releases[i]); }
    }

    static (bool Ok, float X, float Y, float OffsetX, float OffsetY) OverlayPlane(float halfHeight, float aspect,
        float panelW, float panelH, float parentX, float parentY, float centerX, float centerY)
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortOverlayPlane");
        Assert.NotNull(type);
        object[] args = { halfHeight, aspect, panelW, panelH, parentX, parentY, centerX, centerY, 0f, 0f, 0f, 0f };
        bool ok = (bool)type.GetMethod("TryMap").Invoke(null, args);
        return (ok, (float)args[8], (float)args[9], (float)args[10], (float)args[11]);
    }

    [Theory] [InlineData(1, 1, 0, 0)] [InlineData(2, 3, 8, -3)] [InlineData(.5f, 4, -12, 9)]
    public void OpeningCreditsMappingPreservesNativeWorldAspectAndDriverCoordinates(float parentX, float parentY, float centerX, float centerY)
    {
        var map = OverlayPlane(5, 2, 200, 150, parentX, parentY, centerX, centerY);
        Assert.True(map.Ok);
        Assert.Equal(0, centerX * map.X + map.OffsetX, 3);
        Assert.Equal(0, centerY * map.Y + map.OffsetY, 3);
        Assert.Equal(94, (centerX + 10 / parentX) * map.X + map.OffsetX, 3);
        Assert.Equal(47, (centerY + 5 / parentY) * map.Y + map.OffsetY, 3);
        // One native world unit retains the same X/Y scale even when its
        // original parent is anisotropic. Native pose is never rewritten.
        Assert.Equal(map.X / parentX, map.Y / parentY, 3);
    }

    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void OpeningCreditsMappingRejectsInvalidCameraAndParentBeforePresentation(float invalid)
    {
        Assert.False(OverlayPlane(invalid, 1, 100, 100, 1, 1, 0, 0).Ok);
        Assert.False(OverlayPlane(5, invalid, 100, 100, 1, 1, 0, 0).Ok);
        Assert.False(OverlayPlane(5, 1, invalid, 100, 1, 1, 0, 0).Ok);
        Assert.False(OverlayPlane(5, 1, 100, 100, invalid, 1, 0, 0).Ok);
        Assert.False(OverlayPlane(5, 1, 100, 100, 1, invalid, 0, 0).Ok);
    }

    sealed class TitleNode
    {
        public TitleNode Parent;
        public override bool Equals(object other) => other is TitleNode;
        public override int GetHashCode() => 1;
    }
    static bool TitleLocal(object root, object target, bool local)
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortOverlayTargets");
        Assert.NotNull(type);
        return (bool)type.GetMethod("Local").Invoke(null, new object[] {
            root, target, local, (Func<object, object>)(node => ((TitleNode)node).Parent) });
    }
    [Fact] public void AreaTitleLocalActionsRequireExactVisualTargetsNotNpcOrEqualObjects()
    {
        var visual = new TitleNode(); var child = new TitleNode { Parent = visual };
        Assert.True(TitleLocal(visual, visual, true));
        Assert.True(TitleLocal(visual, child, true));
        Assert.False(TitleLocal(visual, new TitleNode(), true));
        Assert.False(TitleLocal(visual, null, true));
        Assert.False(TitleLocal(null, child, true));
        child.Parent = new TitleNode();
        Assert.False(TitleLocal(visual, child, true)); // Current action target rebound.
    }
    [Fact] public void AreaTitleWorldCoordinatesAndCyclicTargetsFailBeforeRouting()
    {
        var visual = new TitleNode(); var child = new TitleNode { Parent = visual };
        Assert.False(TitleLocal(visual, visual, false));
        Assert.False(TitleLocal(visual, child, false));
        child.Parent = child;
        Assert.False(TitleLocal(visual, child, true));
    }

    [Fact] public void AreaTitleParentReturnRetriesFailureAfterNativeWriteWithoutLosingSibling()
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortOverlayParentRestore");
        Assert.NotNull(type);
        var state = Activator.CreateInstance(type); var original = new object(); var carrier = new object();
        object parent = carrier; int writes = 0, sibling = 0;
        object[] args = { carrier, original, (Func<object>)(() => parent), (Action)(() => {
            parent = original; writes++; throw new InvalidOperationException("after SetParent"); }) };
        Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("Return").Invoke(state, args));
        Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("Sibling").Invoke(state, new object[] { (Action)(() => sibling++) }));
        type.GetMethod("Return").Invoke(state, args);
        type.GetMethod("Sibling").Invoke(state, new object[] { (Action)(() => sibling++) });
        Assert.Equal(1, writes); Assert.Equal(1, sibling); Assert.Same(original, parent);
    }
    [Fact] public void AreaTitleParentReturnRespectsNativeRebindInsteadOfRestoringOldSibling()
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortOverlayParentRestore");
        Assert.NotNull(type);
        var state = Activator.CreateInstance(type); var original = new object(); var carrier = new object();
        int writes = 0, sibling = 0; object parent = original; // Native reclaimed the original parent itself.
        type.GetMethod("Return").Invoke(state, new object[] { carrier, original, (Func<object>)(() => parent), (Action)(() => writes++) });
        type.GetMethod("Sibling").Invoke(state, new object[] { (Action)(() => sibling++) });
        Assert.Equal(0, writes); Assert.Equal(0, sibling);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AreaTitleFailedParentOrClipRetainsCarrierWhileOtherLayersRestore(bool failClip)
    {
        var state = new DsPortOverlayParentRestore(); var queue = new DsPortMapRestoreQueue();
        var original = new object(); var carrier = new object(); object parent = carrier;
        int parentAttempts = 0, clipAttempts = 0, layers = 0, released = 0, siblings = 0;
        queue.Change(() => state.Return(carrier, original, () => parent, () => {
            parent = original;
            if (++parentAttempts == 1 && !failClip) throw new InvalidOperationException("after parent write");
        }), () => { });
        queue.Change(() => state.Sibling(() => siblings++), () => { });
        queue.Change(() => layers++, () => { });
        queue.Change(() => {
            if (!state.Returned) throw new InvalidOperationException("clip awaits original coordinates");
            if (++clipAttempts == 1 && failClip) throw new InvalidOperationException("clip refresh");
        }, () => { });
        queue.Own(() => released++);
        Assert.ThrowsAny<Exception>(() => queue.Restore());
        Assert.Equal(1, layers); Assert.Equal(0, released);
        queue.Restore(); Assert.Equal(1, released); Assert.Equal(1, layers); Assert.Equal(1, siblings);
        Assert.Equal(1, parentAttempts); Assert.Same(original, parent);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AreaTitleTweenAdmissionChecksActualCoordinateInputs(int unsafeInput)
    {
        var visual = new TitleNode(); var child = new TitleNode { Parent = visual };
        Func<object, object> parent = value => ((TitleNode)value).Parent;
        Assert.True(DsPortOverlayTargets.LocalTween(visual, child, true, false, false, false, parent));
        Assert.False(DsPortOverlayTargets.LocalTween(visual, child, unsafeInput != 0,
            unsafeInput == 1, unsafeInput == 2, unsafeInput == 3, parent));
        child.Parent = new TitleNode();
        Assert.False(DsPortOverlayTargets.LocalTween(visual, child, true, false, false, false, parent));
    }

    [Fact] public void NativeTutorialRequiresExactActiveNativeCoroutineRegistration()
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortOverlayTargets");
        var method = type.GetMethod("Registered"); Assert.NotNull(method);
        var owner = new TitleNode(); var equalOther = new TitleNode();
        bool Has(object value, object[] registrations) => (bool)method.Invoke(null, new object[] { value, registrations });
        Assert.False(Has(owner, new object[] { equalOther }));
        Assert.True(Has(owner, new object[] { equalOther, owner }));
        Assert.False(Has(null, new object[] { owner }));
        Assert.False(Has(owner, null));
        Assert.False(Has(owner, new object[0])); // Native coroutine has removed its own registration.
    }
    [Fact] public void NativeTutorialRegistrationLookupIsBoundedWithoutBroadPauseInference()
    {
        var type = typeof(DsPortFadeState).Assembly.GetType("DsPortOverlayTargets");
        var method = type.GetMethod("Registered"); Assert.NotNull(method);
        int visits = 0; var owner = new object();
        IEnumerable<object> Endless() { while (true) { visits++; yield return new object(); } }
        Assert.False((bool)method.Invoke(null, new object[] { owner, Endless() }));
        Assert.InRange(visits, 1, 4097);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativeTutorialUnregisterOrReplacementRestoresOnlyTheRunningExactOwner(bool replace)
    {
        object owner = new object(), replacement = new object(); object[] registered = { owner };
        int presented = 0, restored = 0, released = 0;
        var lease = new DsPortDialogueLease(() => DsPortOverlayTargets.Registered(owner, registered), () => owner,
            value => { Assert.Same(owner, value); presented++; },
            value => { Assert.Same(owner, value); restored++; },
            value => { Assert.Same(owner, value); released++; });
        Assert.True(lease.Tick());
        registered = replace ? new[] { replacement } : Array.Empty<object>();
        Assert.False(lease.Tick()); Assert.False(lease.Pending);
        Assert.Equal(1, presented); Assert.Equal(1, restored); Assert.Equal(1, released);
    }
    [Fact] public void NativeTutorialUnregisterDuringPresentationCannotPublishStaleModal()
    {
        var owner = new object(); object[] registered = { owner }; int restored = 0;
        var lease = new DsPortDialogueLease(() => DsPortOverlayTargets.Registered(owner, registered), () => owner,
            _ => registered = Array.Empty<object>(), _ => restored++, _ => { });
        Assert.False(lease.Tick()); Assert.False(lease.Pending); Assert.Equal(1, restored);
    }

    [Fact] public void VisibleFadeShortCircuitsModsTabsAndPageExactlyOnce()
    {
        var native = new Native(); var state = new DsPortFadeState(native);
        int later = 0;
        Assert.True(DsPortGesturePrecedence.Consume(() => state.ConsumeGesture(true),
            () => { later++; return true; }, () => { later++; return true; }, () => { later++; return true; }));
        Assert.Equal(0, later);
        Assert.Equal(1, native.Reads);
        Assert.Equal(1, native.Presents);
    }
}
