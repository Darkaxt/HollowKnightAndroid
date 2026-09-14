using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakPresenterTests
{
    [Fact]
    public void TopLeftTouchMapsIntoViewportLocalCoordinatesAndRejectsOutsidePoints()
    {
        var viewport = new TweakPresenterRect(0.2f, 0.25f, 0.5f, 0.5f);

        Assert.True(TweakPresenterInteraction.TryMapNormalizedTopLeft(
            0.45f, 0.5f, viewport, out var middle));
        Assert.Equal(0.5f, middle.X, 4);
        Assert.Equal(0.5f, middle.Y, 4);

        Assert.True(TweakPresenterInteraction.TryMapNormalizedTopLeft(
            0.45f, 0.25f, viewport, out var top));
        Assert.Equal(1f, top.Y, 4);
        Assert.False(TweakPresenterInteraction.TryMapNormalizedTopLeft(
            0.45f, 0.9f, viewport, out _));
        Assert.False(TweakPresenterInteraction.TryMapNormalizedTopLeft(
            -0.1f, 0.5f, viewport, out _));
    }

    [Fact]
    public void ActionResolutionUsesCloseMasterGroupsRowsResetPrecedence()
    {
        var fixture = PresenterFixture.Create();
        var everything = new TweakPresenterRect(0f, 0f, 1f, 1f);
        var overlapping = new TweakPresenterHitMap(
            everything, everything, everything, everything, everything,
            new[] { everything });

        var action = TweakPresenterInteraction.ResolveAction(
            new TweakPresenterPoint(0.5f, 0.5f), overlapping, fixture.Menu);
        Assert.Equal(TweakPresenterActionKind.Close, action.Kind);

        var hits = DistinctHits();
        Assert.Equal(TweakPresenterActionKind.ToggleMaster,
            Resolve(hits, fixture.Menu, 0.15f).Kind);
        Assert.Equal(TweakPresenterActionKind.PreviousGroup,
            Resolve(hits, fixture.Menu, 0.25f).Kind);
        Assert.Equal(TweakPresenterActionKind.NextGroup,
            Resolve(hits, fixture.Menu, 0.35f).Kind);
        Assert.Equal(TweakPresenterActionKind.SelectRow,
            Resolve(hits, fixture.Menu, 0.55f).Kind);
        Assert.Equal(TweakPresenterActionKind.Reset,
            Resolve(hits, fixture.Menu, 0.95f).Kind);
    }

    [Fact]
    public void RowTapSelectsFirstThenCyclesOnlyAnAlreadySelectedAvailableRow()
    {
        var fixture = PresenterFixture.Create();
        var hits = DistinctHits();

        var first = Resolve(hits, fixture.Menu, 0.55f);
        Assert.Equal(TweakPresenterActionKind.SelectRow, first.Kind);
        Assert.Equal(1, first.RowIndex);
        fixture.Menu.MoveRow(first.RowIndex - fixture.Menu.SelectedRowIndex);

        var second = Resolve(hits, fixture.Menu, 0.55f);
        Assert.Equal(TweakPresenterActionKind.CycleSelected, second.Kind);
        Assert.Equal(1, second.RowIndex);
    }

    [Fact]
    public void DeferredRowsNeverProduceTouchablePresenterEntries()
    {
        var fixture = PresenterFixture.Create(visibleRows: 1);

        Assert.All(fixture.Menu.CurrentRows, row => Assert.True(row.IsAvailable));
        Assert.DoesNotContain(fixture.Menu.CurrentRows, row => row.Id == "deferred");
        Assert.Equal(6, TweakPresenterListLayout.EntryCount(fixture.Menu));
    }

    [Fact]
    public void CleanTapSequenceIsAcceptedExactlyOnceUntilReset()
    {
        var interaction = new TweakPresenterInteraction();

        Assert.True(interaction.IsNewCleanTap(7));
        Assert.True(interaction.TryAcceptCleanTap(7));
        Assert.False(interaction.IsNewCleanTap(7));
        Assert.False(interaction.TryAcceptCleanTap(7));
        Assert.True(interaction.TryAcceptCleanTap(8));

        interaction.ResetCleanTap(11);
        Assert.False(interaction.TryAcceptCleanTap(11));
        Assert.True(interaction.TryAcceptCleanTap(12));
    }

    [Fact]
    public void RebindInvalidatesViewAndSynchronizesOpenWithoutOwningMenuBehavior()
    {
        var paint = new TweakPresenterPaintInvalidation();
        var lifecycle = new TweakPresenterLifecycle(paint);
        var owner1 = new object();
        var menu1 = new object();

        var first = lifecycle.Rebind(owner1, menu1, true);
        Assert.True(first.Changed);
        Assert.False(first.ClosePreviousMenu);
        Assert.True(lifecycle.IsOpen);
        Assert.False(lifecycle.ViewValid);
        Assert.True(paint.ShouldPaint(1, 1));

        lifecycle.MarkViewBuilt();
        paint.Acknowledge(1, 1);
        Assert.False(paint.ShouldPaint(1, 1));

        lifecycle.SynchronizeOpen(false);
        Assert.False(lifecycle.IsOpen);
        Assert.True(paint.ShouldPaint(2, 1));

        var owner2 = new object();
        var menu2 = new object();
        lifecycle.MarkPresenterAttached();
        var changed = lifecycle.Rebind(owner2, menu2, false);
        Assert.True(changed.Changed);
        Assert.True(changed.ClosePreviousMenu);
        Assert.True(changed.DetachPreviousPresenter);
        Assert.False(lifecycle.IsOpen);
        Assert.False(lifecycle.PresenterAttached);
        Assert.False(lifecycle.ViewValid);
    }

    [Fact]
    public void SurfaceOwnershipReassertsExclusiveVisibilityAndRestoresExactBaseline()
    {
        var visible = new SurfaceNode { State = true };
        var alreadyHidden = new SurfaceNode { State = false };
        var ownership = new TweakPresenterSurfaceOwnership<SurfaceNode, bool>(
            node => node.State,
            (node, state) => node.State = state,
            false);

        Assert.True(ownership.CaptureAndHide(visible));
        Assert.True(ownership.CaptureAndHide(alreadyHidden));
        Assert.False(visible.State);
        Assert.False(alreadyHidden.State);

        visible.State = true;
        Assert.False(ownership.CaptureAndHide(visible));
        Assert.False(visible.State);

        ownership.Restore();
        Assert.True(visible.State);
        Assert.False(alreadyHidden.State);
        Assert.False(ownership.HasCapturedState);
    }

    [Fact]
    public void BackdropSurfaceOwnershipRestoresLastAndPreservesExactCameraBaselines()
    {
        var enabledDepth = new BackdropCameraNode
        {
            Enabled = true,
            ClearFlags = BackdropClearFlags.Depth,
        };
        var disabledSolid = new BackdropCameraNode
        {
            Enabled = false,
            ClearFlags = BackdropClearFlags.SolidColor,
        };
        var companion = new SurfaceNode { State = true };
        var hud = new SurfaceNode { State = true };
        bool throwHudRestoreOnce = true;
        var companionOwnership =
            new TweakPresenterSurfaceOwnership<SurfaceNode, bool>(
                surface => surface.State,
                (surface, visible) => surface.State = visible,
                false);
        var hudOwnership =
            new TweakPresenterSurfaceOwnership<SurfaceNode, bool>(
                surface => surface.State,
                (surface, visible) =>
                {
                    if (visible && throwHudRestoreOnce)
                    {
                        throwHudRestoreOnce = false;
                        throw new InvalidOperationException("transient HUD restore");
                    }
                    surface.State = visible;
                },
                false);
        var enabledOwnership =
            new TweakPresenterSurfaceOwnership<BackdropCameraNode, bool>(
                camera => camera.Enabled,
                (camera, enabled) => camera.Enabled = enabled,
                false);
        var clearFlagsOwnership =
            new TweakPresenterSurfaceOwnership<BackdropCameraNode, BackdropClearFlags>(
                camera => camera.ClearFlags,
                (camera, clearFlags) => camera.ClearFlags = clearFlags,
                BackdropClearFlags.SolidColor);

        Assert.True(enabledOwnership.CaptureAndHide(enabledDepth));
        Assert.True(enabledOwnership.CaptureAndHide(disabledSolid));
        Assert.True(clearFlagsOwnership.CaptureAndHide(enabledDepth));
        Assert.True(clearFlagsOwnership.CaptureAndHide(disabledSolid));
        Assert.True(companionOwnership.CaptureAndHide(companion));
        Assert.True(hudOwnership.CaptureAndHide(hud));
        Assert.False(enabledDepth.Enabled);
        Assert.Equal(BackdropClearFlags.SolidColor, enabledDepth.ClearFlags);
        Assert.False(disabledSolid.Enabled);
        Assert.Equal(BackdropClearFlags.SolidColor, disabledSolid.ClearFlags);

        enabledDepth.Enabled = true;
        enabledDepth.ClearFlags = BackdropClearFlags.Depth;
        disabledSolid.Enabled = true;
        disabledSolid.ClearFlags = BackdropClearFlags.Depth;
        Assert.False(enabledOwnership.CaptureAndHide(enabledDepth));
        Assert.False(enabledOwnership.CaptureAndHide(disabledSolid));
        Assert.False(clearFlagsOwnership.CaptureAndHide(enabledDepth));
        Assert.False(clearFlagsOwnership.CaptureAndHide(disabledSolid));

        Assert.Throws<InvalidOperationException>(() =>
        {
            companionOwnership.Restore();
            hudOwnership.Restore();
            clearFlagsOwnership.Restore();
            enabledOwnership.Restore();
        });
        Assert.True(companion.State);
        Assert.False(hud.State);
        Assert.False(enabledDepth.Enabled);
        Assert.Equal(BackdropClearFlags.SolidColor, enabledDepth.ClearFlags);
        Assert.False(disabledSolid.Enabled);
        Assert.Equal(BackdropClearFlags.SolidColor, disabledSolid.ClearFlags);

        companionOwnership.Restore();
        hudOwnership.Restore();
        clearFlagsOwnership.Restore();
        enabledOwnership.Restore();
        Assert.True(hud.State);
        Assert.True(enabledDepth.Enabled);
        Assert.Equal(BackdropClearFlags.Depth, enabledDepth.ClearFlags);
        Assert.False(disabledSolid.Enabled);
        Assert.Equal(BackdropClearFlags.SolidColor, disabledSolid.ClearFlags);
    }

    [Fact]
    public void HollowKnightCoveredSurfacePlanIncludesBackdropSlideAndDetachedPrompts()
    {
        var surfaces = new List<HollowKnightModsCoveredSurface>();
        for (int i = 0;
             i < HollowKnightModsPresentationFlow.CoveredSurfaceCount;
             i++)
            surfaces.Add(HollowKnightModsPresentationFlow.CoveredSurfaceAt(i));

        Assert.Equal(new[]
        {
            HollowKnightModsCoveredSurface.BackdropComposite,
            HollowKnightModsCoveredSurface.FrameRoot,
            HollowKnightModsCoveredSurface.MapPage,
            HollowKnightModsCoveredSurface.InterruptedSlide,
            HollowKnightModsCoveredSurface.InventoryPage,
            HollowKnightModsCoveredSurface.CharmsPage,
            HollowKnightModsCoveredSurface.AreaName,
            HollowKnightModsCoveredSurface.EquipmentRow,
            HollowKnightModsCoveredSurface.NoMap,
            HollowKnightModsCoveredSurface.Notches,
            HollowKnightModsCoveredSurface.MapMasks,
            HollowKnightModsCoveredSurface.MapReset,
            HollowKnightModsCoveredSurface.Selection,
            HollowKnightModsCoveredSurface.DetachedPromptGlyph,
            HollowKnightModsCoveredSurface.DetachedPromptVerb,
            HollowKnightModsCoveredSurface.NativeHud,
        }, surfaces);
    }

    [Fact]
    public void HollowKnightOverlappingTabHitsChooseClosestHorizontalMidpoint()
    {
        var candidates = new[]
        {
            (new TweakPresenterRect(0.10f, 0.80f, 0.50f, 0.20f), 1),
            (new TweakPresenterRect(0.40f, 0.80f, 0.50f, 0.20f), 2),
        };

        Assert.Equal(2, HollowKnightModsPresentationFlow.ResolveClosestHorizontalHit(
            new TweakPresenterPoint(0.55f, 0.90f), candidates));
    }

    [Fact]
    public void HollowKnightGearTapFallsBackToLiveGeometryOnlyForVisibleOrdinaryFrame()
    {
        var staleGear = new TweakPresenterRect(0.02f, 0.80f, 0.04f, 0.04f);
        var staleFps = new TweakPresenterRect(0.02f, 0.94f, 0.04f, 0.02f);
        var visibleGearTap = new TweakPresenterPoint(105f / 1240f, 920f / 1080f);

        Assert.Equal(
            HollowKnightModsGearHitDisposition.LiveFallback,
            HollowKnightModsPresentationFlow.ResolveGearHit(
                visibleGearTap, true, staleGear, staleFps,
                modsOpen: false, ordinaryFrameVisible: true));
        Assert.Equal(
            HollowKnightModsGearHitDisposition.Miss,
            HollowKnightModsPresentationFlow.ResolveGearHit(
                visibleGearTap, true, staleGear, staleFps,
                modsOpen: true, ordinaryFrameVisible: false));
        Assert.Equal(
            HollowKnightModsGearHitDisposition.Miss,
            HollowKnightModsPresentationFlow.ResolveGearHit(
                visibleGearTap, true, staleGear, staleFps,
                modsOpen: false, ordinaryFrameVisible: false));
        Assert.Equal(
            HollowKnightModsGearHitDisposition.Cached,
            HollowKnightModsPresentationFlow.ResolveGearHit(
                new TweakPresenterPoint(0.04f, 0.82f), true, staleGear, staleFps,
                modsOpen: true, ordinaryFrameVisible: false));
    }

    [Fact]
    public void HollowKnightOwnedDebugSimulationIsConsumedWithoutDelayedDispatch()
    {
        int lastSimulationSequence = 8;

        Assert.False(HollowKnightModsPresentationFlow.TryAcceptDebugSimulation(
            9, ownsInput: true, ref lastSimulationSequence));
        Assert.Equal(9, lastSimulationSequence);
        Assert.False(HollowKnightModsPresentationFlow.TryAcceptDebugSimulation(
            9, ownsInput: false, ref lastSimulationSequence));
        Assert.True(HollowKnightModsPresentationFlow.TryAcceptDebugSimulation(
            10, ownsInput: false, ref lastSimulationSequence));
        Assert.Equal(10, lastSimulationSequence);
    }

    [Fact]
    public void HollowKnightExternalCloseGatesSurfaceRestoreAndRevealAroundLayout()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();
        var events = new List<string>();

        var disposition = HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: true);

        Assert.Equal(HollowKnightModsRestoreDisposition.Deferred, disposition);
        Assert.False(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: true,
            () => events.Add("drain input"),
            () => events.Add("reveal")));
        Assert.Empty(events);
        Assert.True(HollowKnightModsPresentationFlow.BeginCoveredContentRestore(
            lifecycle,
            () => events.Add("hide cameras"),
            () => events.Add("restore surfaces")));
        Assert.Equal(new[] { "hide cameras", "restore surfaces" }, events);
        Assert.False(HollowKnightModsPresentationFlow.BeginCoveredContentRestore(
            lifecycle,
            () => events.Add("hide cameras"),
            () => events.Add("restore surfaces again")));
        Assert.Equal(
            new[] { "hide cameras", "restore surfaces", "hide cameras" },
            events);
        Assert.False(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: false,
            () => events.Add("drain input"),
            () => events.Add("reveal")));
        Assert.DoesNotContain("reveal", events);
        Assert.True(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: true,
            () => events.Add("drain input"),
            () => events.Add("reveal")));
        Assert.Equal(
            new[] {
                "hide cameras", "restore surfaces", "hide cameras",
                "drain input", "reveal",
            },
            events);
        Assert.False(lifecycle.OwnsInput);
    }

    [Fact]
    public void HollowKnightCoveredContentRemainsOwnedUntilClosingContactReleases()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();
        HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: true);
        Assert.True(HollowKnightModsPresentationFlow.BeginCoveredContentRestore(
            lifecycle, () => { }, () => { }));
        var events = new List<string>();

        Assert.False(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: true,
            () => events.Add("drain release"),
            () => events.Add("reveal"),
            lowerScreenContactReleased: false));

        Assert.Empty(events);
        Assert.True(lifecycle.OwnsInput);
        Assert.True(lifecycle.CoveredContentRestorePending);

        Assert.True(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: true,
            () => events.Add("drain release"),
            () => events.Add("reveal"),
            lowerScreenContactReleased: true));
        Assert.Equal(new[] { "drain release", "reveal" }, events);
        Assert.False(lifecycle.OwnsInput);
    }

    [Fact]
    public void HollowKnightSurfaceRestoreFailureRemainsOwnedAndRetriesBeforeReveal()
    {
        var first = new SurfaceNode { State = true };
        var transient = new SurfaceNode { State = true };
        var last = new SurfaceNode { State = true };
        bool throwOnce = true;
        int firstRestoreWrites = 0;
        int transientRestoreWrites = 0;
        int lastRestoreWrites = 0;
        var ownership = new TweakPresenterSurfaceOwnership<SurfaceNode, bool>(
            node => node.State,
            (node, state) =>
            {
                if (state)
                {
                    if (ReferenceEquals(node, first)) firstRestoreWrites++;
                    if (ReferenceEquals(node, transient)) transientRestoreWrites++;
                    if (ReferenceEquals(node, last)) lastRestoreWrites++;
                    if (ReferenceEquals(node, transient) && throwOnce)
                    {
                        throwOnce = false;
                        throw new InvalidOperationException("transient restore failure");
                    }
                }
                node.State = state;
            },
            false);
        ownership.CaptureAndHide(first);
        ownership.CaptureAndHide(transient);
        ownership.CaptureAndHide(last);
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();
        HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: true);
        int reveals = 0;

        Assert.Throws<InvalidOperationException>(() =>
            HollowKnightModsPresentationFlow.BeginCoveredContentRestore(
                lifecycle,
                () => { },
                ownership.Restore));

        Assert.True(ownership.HasCapturedState);
        Assert.False(lifecycle.CoveredContentRestoreStarted);
        Assert.True(lifecycle.CoveredContentRestorePending);
        Assert.True(lifecycle.OwnsInput);
        Assert.False(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: true,
            () => { },
            () => reveals++));
        Assert.Equal(0, reveals);

        Assert.True(HollowKnightModsPresentationFlow.BeginCoveredContentRestore(
            lifecycle,
            () => { },
            ownership.Restore));
        Assert.False(ownership.HasCapturedState);
        Assert.Equal(1, firstRestoreWrites);
        Assert.Equal(2, transientRestoreWrites);
        Assert.Equal(1, lastRestoreWrites);
        Assert.True(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: true,
            () => { },
            () => reveals++));
        Assert.Equal(1, reveals);
        Assert.False(lifecycle.OwnsInput);
    }

    [Fact]
    public void HollowKnightActivePresenterTeardownKeepsReleasePendingAfterDetach()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.MarkPresenterAttached();
        lifecycle.RequestCoveredContentStow();

        var disposition = HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: true);
        Assert.True(lifecycle.Detach());

        Assert.Equal(HollowKnightModsRestoreDisposition.Deferred, disposition);
        Assert.False(lifecycle.PresenterAttached);
        Assert.False(lifecycle.IsOpen);
        Assert.True(lifecycle.CoveredContentStowed);
        Assert.True(lifecycle.CoveredContentRestorePending);
        Assert.True(lifecycle.OwnsInput);
    }

    [Fact]
    public void HollowKnightClosedOwnerRebindKeepsActiveCameraReleasePending()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.MarkPresenterAttached();
        lifecycle.RequestCoveredContentStow();

        var decision = lifecycle.Rebind(new object(), new object(), false);
        var disposition = HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: true);

        Assert.True(decision.RestoreCoveredContent);
        Assert.Equal(HollowKnightModsRestoreDisposition.Deferred, disposition);
        Assert.False(lifecycle.PresenterAttached);
        Assert.False(lifecycle.IsOpen);
        Assert.True(lifecycle.CoveredContentStowed);
        Assert.True(lifecycle.CoveredContentRestorePending);
        Assert.True(lifecycle.OwnsInput);
    }

    [Fact]
    public void HollowKnightDisabledCameraTeardownMayRestoreImmediately()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();

        var disposition = HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: false);
        lifecycle.Detach();

        Assert.Equal(HollowKnightModsRestoreDisposition.Immediate, disposition);
        Assert.True(lifecycle.CoveredContentStowed);
        Assert.True(lifecycle.OwnsInput);
        Assert.Throws<InvalidOperationException>(() =>
            HollowKnightModsPresentationFlow.RestoreCoveredContentImmediately(
                lifecycle,
                () => throw new InvalidOperationException("transient restore failure"),
                () => { }));
        Assert.True(lifecycle.CoveredContentStowed);
        Assert.True(lifecycle.OwnsInput);
        Assert.True(HollowKnightModsPresentationFlow.RestoreCoveredContentImmediately(
            lifecycle, () => { }, () => { }));
        Assert.False(lifecycle.CoveredContentStowed);
        Assert.False(lifecycle.CoveredContentRestorePending);
        Assert.False(lifecycle.OwnsInput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HollowKnightImmediateRestoreDrainsInputBeforeOwnershipRelease(
        bool alreadyPending)
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();
        if (alreadyPending)
            HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
                lifecycle, coveredContentCanRender: true);
        Assert.Equal(
            HollowKnightModsRestoreDisposition.Immediate,
            HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
                lifecycle, coveredContentCanRender: false));
        int lastTapSequence = 14;
        int lastCleanTapSequence = 28;
        float pinchLastDistance = 0.75f;
        bool mapDragValid = true;
        bool modsDragValid = true;
        var events = new List<string>();

        Assert.True(HollowKnightModsPresentationFlow.RestoreCoveredContentImmediately(
            lifecycle,
            () => events.Add("restore surfaces"),
            () =>
            {
                events.Add("drain input");
                Assert.True(lifecycle.OwnsInput);
                Assert.True(
                    HollowKnightModsPresentationFlow.RejectAndDrainLowerScreenInput(
                        lifecycle,
                        tapSequence: 15,
                        cleanTapSequence: 29,
                        ref lastTapSequence,
                        ref lastCleanTapSequence,
                        ref pinchLastDistance,
                        ref mapDragValid,
                        ref modsDragValid));
            }));

        Assert.Equal(new[] { "restore surfaces", "drain input" }, events);
        Assert.False(lifecycle.OwnsInput);
        Assert.Equal(15, lastTapSequence);
        Assert.Equal(29, lastCleanTapSequence);
        Assert.Equal(-1f, pinchLastDistance);
        Assert.False(mapDragValid);
        Assert.False(modsDragValid);
        int replayedEvents = 0;
        if (15 != lastTapSequence) replayedEvents++;
        if (29 != lastCleanTapSequence) replayedEvents++;
        Assert.Equal(0, replayedEvents);
    }

    [Fact]
    public void HollowKnightPendingReleaseMayFinishImmediatelyAfterCamerasDisable()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();
        Assert.Equal(
            HollowKnightModsRestoreDisposition.Deferred,
            HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
                lifecycle, coveredContentCanRender: true));

        Assert.Equal(
            HollowKnightModsRestoreDisposition.Immediate,
            HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
                lifecycle, coveredContentCanRender: false));
        Assert.True(HollowKnightModsPresentationFlow.RestoreCoveredContentImmediately(
            lifecycle, () => { }, () => { }));
        Assert.False(lifecycle.CoveredContentStowed);
        Assert.False(lifecycle.CoveredContentRestorePending);
        Assert.False(lifecycle.OwnsInput);
    }

    [Fact]
    public void HollowKnightPendingReleaseRejectsEveryInputAndCannotCloseAgain()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();
        Assert.False(HollowKnightModsPresentationFlow.RejectAllLowerScreenInput(
            lifecycle));
        Assert.True(HollowKnightModsPresentationFlow.CanCloseFromLowerScreenInput(
            lifecycle));

        HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: true);

        Assert.True(HollowKnightModsPresentationFlow.RejectAllLowerScreenInput(
            lifecycle));
        Assert.False(HollowKnightModsPresentationFlow.CanCloseFromLowerScreenInput(
            lifecycle));
    }

    [Fact]
    public void HollowKnightPendingInputIsDrainedAndCannotReplayAfterRelease()
    {
        var lifecycle = OpenHollowKnightPresenter();
        lifecycle.RequestCoveredContentStow();
        HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            lifecycle, coveredContentCanRender: true);
        int lastTapSequence = 40;
        int lastCleanTapSequence = 70;
        float pinchLastDistance = 0.5f;
        bool mapDragValid = true;
        bool modsDragValid = true;

        Assert.True(HollowKnightModsPresentationFlow.BeginCoveredContentRestore(
            lifecycle, () => { }, () => { }));
        var events = new List<string>();
        Assert.True(HollowKnightModsPresentationFlow.CompleteCoveredContentRestore(
            lifecycle,
            ordinaryLayoutReady: true,
            () =>
            {
                events.Add("drain");
                Assert.True(
                    HollowKnightModsPresentationFlow.RejectAndDrainLowerScreenInput(
                        lifecycle,
                        tapSequence: 41,
                        cleanTapSequence: 71,
                        ref lastTapSequence,
                        ref lastCleanTapSequence,
                        ref pinchLastDistance,
                        ref mapDragValid,
                        ref modsDragValid));
            },
            () => events.Add("reveal")));

        Assert.Equal(new[] { "drain", "reveal" }, events);
        Assert.Equal(41, lastTapSequence);
        Assert.Equal(71, lastCleanTapSequence);
        Assert.Equal(-1f, pinchLastDistance);
        Assert.False(mapDragValid);
        Assert.False(modsDragValid);
        Assert.False(lifecycle.OwnsInput);
        Assert.False(HollowKnightModsPresentationFlow.RejectAndDrainLowerScreenInput(
            lifecycle,
            tapSequence: 41,
            cleanTapSequence: 71,
            ref lastTapSequence,
            ref lastCleanTapSequence,
            ref pinchLastDistance,
            ref mapDragValid,
            ref modsDragValid));
        int replayedEvents = 0;
        if (41 != lastTapSequence) replayedEvents++;
        if (71 != lastCleanTapSequence) replayedEvents++;
        Assert.Equal(0, replayedEvents);
    }

    [Fact]
    public void HollowKnightPendingReleaseDoesNotAlterSilksongFrameSelectionPath()
    {
        var hollowKnight = OpenHollowKnightPresenter();
        hollowKnight.RequestCoveredContentStow();
        HollowKnightModsPresentationFlow.RequestCoveredContentRelease(
            hollowKnight, coveredContentCanRender: true);

        DsPortFrameDecision silksong = DsPortFrameState.Initial(3, 0);
        silksong = DsPortFrameState.BeginSelection(silksong, 2);

        Assert.True(hollowKnight.CoveredContentRestorePending);
        Assert.True(silksong.Sliding);
        Assert.Equal(0, silksong.OutgoingIndex);
        Assert.Equal(2, silksong.IncomingIndex);
        Assert.True(DsPortFrameState.IsHostActive(silksong, 0));
        Assert.True(DsPortFrameState.IsHostActive(silksong, 2));

        silksong = DsPortFrameState.CompleteSelection(silksong);
        Assert.False(silksong.Sliding);
        Assert.Equal(2, silksong.SelectedIndex);
    }

    static TweakPresenterLifecycle OpenHollowKnightPresenter()
    {
        var lifecycle = new TweakPresenterLifecycle(
            new TweakPresenterPaintInvalidation());
        lifecycle.Rebind(new object(), new object(), true);
        return lifecycle;
    }

    [Theory]
    [InlineData(0f, 1f, -1f, 1f, true)]
    [InlineData(0f, 1.01f, -1f, 1f, false)]
    [InlineData(-1.01f, 1f, -1f, 1f, false)]
    public void RowFitsRequiresTheWholeRowInsideTheOwnedRegion(
        float rowBottom,
        float rowTop,
        float regionBottom,
        float regionTop,
        bool expected)
    {
        Assert.Equal(expected, TweakPresenterListLayout.RowFits(
            rowBottom, rowTop, regionBottom, regionTop));
    }

    enum BackdropClearFlags
    {
        Depth,
        SolidColor,
    }

    sealed class BackdropCameraNode
    {
        public bool Enabled { get; set; }
        public BackdropClearFlags ClearFlags { get; set; }
    }

    sealed class SurfaceNode
    {
        public bool State { get; set; }
    }

    [Fact]
    public void CoveredContentAndDetachLifecycleDecisionsAreIdempotent()
    {
        var paint = new TweakPresenterPaintInvalidation();
        var lifecycle = new TweakPresenterLifecycle(paint);
        var owner = new object();
        var menu = new object();
        lifecycle.Rebind(owner, menu, true);
        lifecycle.MarkViewBuilt();
        lifecycle.MarkPresenterAttached();

        Assert.True(lifecycle.RequestCoveredContentStow());
        Assert.False(lifecycle.RequestCoveredContentStow());
        Assert.True(lifecycle.RequestCoveredContentRestore());
        Assert.False(lifecycle.RequestCoveredContentRestore());

        Assert.True(lifecycle.Detach());
        Assert.False(lifecycle.Detach());
        Assert.False(lifecycle.PresenterAttached);
        Assert.False(lifecycle.ViewValid);
        Assert.False(lifecycle.CoveredContentStowed);
        Assert.True(lifecycle.Rebind(owner, menu, false).Changed);
    }

    [Fact]
    public void ModelAndLayoutStampsSkipSteadyPaintAndInvalidateEveryRenderedChange()
    {
        var fixture = PresenterFixture.Create();
        fixture.Menu.Open();
        var paint = new TweakPresenterPaintInvalidation();
        long geometry = TweakPresenterGeometryPaintStamp.WithLayoutRevision(
            GeometryStamp(bottom: -3f, top: 4f), layoutRevision: 7);
        long model = TweakPresenterModelPaintStamp.Compute(
            fixture, fixture.Menu, fixture.Controller);

        Assert.True(paint.ShouldPaint(model, geometry));
        paint.Acknowledge(model, geometry);
        Assert.False(paint.ShouldPaint(
            TweakPresenterModelPaintStamp.Compute(fixture, fixture.Menu, fixture.Controller),
            TweakPresenterGeometryPaintStamp.WithLayoutRevision(
                GeometryStamp(bottom: -3f, top: 4f), layoutRevision: 7)));

        fixture.Menu.MoveRow(1);
        long selection = TweakPresenterModelPaintStamp.Compute(
            fixture, fixture.Menu, fixture.Controller);
        Assert.NotEqual(model, selection);
        Assert.True(paint.ShouldPaint(selection, geometry));
        paint.Acknowledge(selection, geometry);

        fixture.Menu.ToggleMaster();
        long master = TweakPresenterModelPaintStamp.Compute(
            fixture, fixture.Menu, fixture.Controller);
        Assert.NotEqual(selection, master);
        Assert.True(paint.ShouldPaint(master, geometry));
        paint.Acknowledge(master, geometry);

        fixture.Menu.CycleSelected();
        long valueAndStatus = TweakPresenterModelPaintStamp.Compute(
            fixture, fixture.Menu, fixture.Controller);
        Assert.NotEqual(master, valueAndStatus);
        Assert.True(paint.ShouldPaint(valueAndStatus, geometry));
        paint.Acknowledge(valueAndStatus, geometry);

        long relayout = TweakPresenterGeometryPaintStamp.WithLayoutRevision(
            GeometryStamp(bottom: -3f, top: 4f), layoutRevision: 8);
        Assert.NotEqual(geometry, relayout);
        Assert.True(paint.ShouldPaint(valueAndStatus, relayout));
        paint.Acknowledge(valueAndStatus, relayout);

        long resized = TweakPresenterGeometryPaintStamp.WithLayoutRevision(
            GeometryStamp(bottom: -3f, top: 4.25f), layoutRevision: 8);
        Assert.NotEqual(relayout, resized);
        Assert.True(paint.ShouldPaint(valueAndStatus, resized));
    }

    [Fact]
    public void FlatListModelStampIncludesRowsOutsideTheSelectedGroup()
    {
        var controller = new TweakController(new MultiGroupAdapter(), new MemoryStore());
        Assert.True(controller.Initialize().Success);
        Assert.True(controller.SetMaster(true).Success);
        var menu = new TweakMenuModel(controller, visibleRows: 3);
        long before = TweakPresenterModelPaintStamp.Compute(this, menu, controller);

        Assert.True(controller.Cycle("other").Success);
        long after = TweakPresenterModelPaintStamp.Compute(this, menu, controller);

        Assert.Equal(0, menu.SelectedGroupIndex);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void FlatListLayoutIncludesGeneralActionsAndGroupedRowsInStableOrder()
    {
        var fixture = PresenterFixture.Create();

        Assert.Equal(0.65f, TweakPresenterListLayout.LeftFraction);
        Assert.Equal(6, TweakPresenterListLayout.EntryCount(fixture.Menu));
        Assert.Equal(TweakPresenterListEntryKind.Header,
            TweakPresenterListLayout.EntryAt(fixture.Menu, 0).Kind);
        Assert.Equal(TweakPresenterListEntryKind.Master,
            TweakPresenterListLayout.EntryAt(fixture.Menu, 1).Kind);
        Assert.Equal(TweakPresenterListEntryKind.Reset,
            TweakPresenterListLayout.EntryAt(fixture.Menu, 2).Kind);

        var group = TweakPresenterListLayout.EntryAt(fixture.Menu, 3);
        Assert.Equal(TweakPresenterListEntryKind.Header, group.Kind);
        Assert.Equal(0, group.GroupIndex);
        for (int row = 0; row < fixture.Menu.RowsForGroup(0).Count; row++)
        {
            var entry = TweakPresenterListLayout.EntryAt(fixture.Menu, 4 + row);
            Assert.Equal(TweakPresenterListEntryKind.Row, entry.Kind);
            Assert.Equal(0, entry.GroupIndex);
            Assert.Equal(row, entry.RowIndex);
        }
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            TweakPresenterListLayout.EntryAt(fixture.Menu, 6));
    }

    [Fact]
    public void FlatListScrollIsClampedToContentAndViewportGeometry()
    {
        Assert.Equal(0f, TweakPresenterListLayout.ClampScroll(-2f, 10, 12f, 48f));
        Assert.Equal(36f, TweakPresenterListLayout.ClampScroll(36f, 10, 12f, 48f));
        Assert.Equal(72f, TweakPresenterListLayout.ClampScroll(90f, 10, 12f, 48f));
        Assert.Equal(0f, TweakPresenterListLayout.ClampScroll(8f, 3, 12f, 48f));
    }

    [Fact]
    public void PaintInvalidationTracksModelGeometryRebindBuildAndAcknowledgment()
    {
        var paint = new TweakPresenterPaintInvalidation();
        var lifecycle = new TweakPresenterLifecycle(paint);

        Assert.True(paint.ShouldPaint(10, 20));
        paint.Acknowledge(10, 20);
        Assert.False(paint.ShouldPaint(10, 20));
        Assert.True(paint.HasCurrentGeometry(20));
        Assert.True(paint.ShouldPaint(11, 20));
        Assert.True(paint.ShouldPaint(10, 21));

        paint.Acknowledge(11, 21);
        Assert.False(paint.ShouldPaint(11, 21));
        lifecycle.Rebind(new object(), new object(), true);
        Assert.True(paint.ShouldPaint(11, 21));

        paint.Acknowledge(11, 21);
        lifecycle.MarkViewBuilt();
        Assert.True(paint.ShouldPaint(11, 21));
        paint.Acknowledge(11, 21);
        Assert.False(paint.ShouldPaint(11, 21));
    }

    [Fact]
    public void ResolvedFallbackBoundsInvalidatePaintOnlyWhenGeometryChanges()
    {
        var paint = new TweakPresenterPaintInvalidation();
        const long modelStamp = 17;
        long initial = GeometryStamp(bottom: -3f, top: 4f);

        paint.Acknowledge(modelStamp, initial);
        long unchanged = GeometryStamp(bottom: -3f, top: 4f);
        Assert.Equal(initial, unchanged);
        Assert.False(paint.ShouldPaint(modelStamp, unchanged));

        long changedTop = GeometryStamp(bottom: -3f, top: 4.25f);
        Assert.NotEqual(initial, changedTop);
        Assert.True(paint.ShouldPaint(modelStamp, changedTop));

        paint.Acknowledge(modelStamp, changedTop);
        long changedBottom = GeometryStamp(bottom: -2.75f, top: 4.25f);
        Assert.NotEqual(changedTop, changedBottom);
        Assert.True(paint.ShouldPaint(modelStamp, changedBottom));
    }

    static long GeometryStamp(float bottom, float top)
    {
        return TweakPresenterGeometryPaintStamp.Compute(
            -5f, 5f, bottom, top, 4f,
            0f, 0.5f, 1f, 0.5f,
            0f, 0.5f, -10f, 4f, 1.6f);
    }

    static TweakPresenterAction Resolve(
        TweakPresenterHitMap hits,
        TweakMenuModel menu,
        float x)
    {
        return TweakPresenterInteraction.ResolveAction(
            new TweakPresenterPoint(x, 0.5f), hits, menu);
    }

    static TweakPresenterHitMap DistinctHits()
    {
        return new TweakPresenterHitMap(
            new TweakPresenterRect(0f, 0f, 0.1f, 1f),
            new TweakPresenterRect(0.1f, 0f, 0.1f, 1f),
            new TweakPresenterRect(0.2f, 0f, 0.1f, 1f),
            new TweakPresenterRect(0.3f, 0f, 0.1f, 1f),
            new TweakPresenterRect(0.9f, 0f, 0.1f, 1f),
            new[]
            {
                new TweakPresenterRect(0.4f, 0f, 0.1f, 1f),
                new TweakPresenterRect(0.5f, 0f, 0.1f, 1f),
                new TweakPresenterRect(0.6f, 0f, 0.1f, 1f),
            });
    }

    sealed class PresenterFixture
    {
        PresenterFixture(TweakController controller, int visibleRows)
        {
            Controller = controller;
            Menu = new TweakMenuModel(controller, visibleRows);
        }

        public TweakController Controller { get; }
        public TweakMenuModel Menu { get; }

        public static PresenterFixture Create(int visibleRows = 3)
        {
            var controller = new TweakController(new PresenterAdapter(), new MemoryStore());
            Assert.True(controller.Initialize().Success);
            return new PresenterFixture(controller, visibleRows);
        }
    }

    sealed class PresenterAdapter : ITweakAdapter
    {
        public string GameId => "presenter-test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor(
                "first", "GROUP", "FIRST", "First available row.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "second", "GROUP", "SECOND", "Second available row.",
                "off", new[] { "off", "on" }),
            TweakDescriptor.Deferred(
                "deferred", "GROUP", "DEFERRED", "Deferred row.",
                "TEST-001", "No supported behavior seam."),
        };

        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value) => TweakActionResult.Ok();
        public void RestoreBaseline() { }
        public void Tick() { }
    }

    sealed class MultiGroupAdapter : ITweakAdapter
    {
        public string GameId => "multi-group-presenter-test";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; } = new[]
        {
            new TweakDescriptor(
                "first", "FIRST GROUP", "FIRST", "First group row.",
                "off", new[] { "off", "on" }),
            new TweakDescriptor(
                "other", "OTHER GROUP", "OTHER", "Other group row.",
                "off", new[] { "off", "on" }),
        };

        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value) => TweakActionResult.Ok();
        public void RestoreBaseline() { }
        public void Tick() { }
    }

    sealed class MemoryStore : Dictionary<string, string>, ITweakStore
    {
        public string Read(string key) => TryGetValue(key, out var value) ? value : null;
        public void Write(string key, string value) => this[key] = value;
        public void Flush() { }
    }
}
