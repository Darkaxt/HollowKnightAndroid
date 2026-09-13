using DualSouls.Mods;
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
    public void RepeatedTapOnSelectedDeferredRowNeverResolvesToCycle()
    {
        var fixture = PresenterFixture.Create(visibleRows: 1);
        fixture.Menu.MoveRow(2);
        var deferredRow = new TweakPresenterHitMap(
            default, default, default, default, default,
            new[] { new TweakPresenterRect(0f, 0f, 1f, 1f) });
        fixture.Menu.MoveRow(0);
        Assert.Equal(2, fixture.Menu.SelectedRowIndex);
        Assert.Equal(2, fixture.Menu.WindowStart);

        var action = TweakPresenterInteraction.ResolveAction(
            new TweakPresenterPoint(0.5f, 0.5f), deferredRow, fixture.Menu);

        Assert.Equal(TweakPresenterActionKind.None, action.Kind);
        Assert.False(fixture.Menu.Selected.IsAvailable);
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
        Assert.Equal(7, TweakPresenterListLayout.EntryCount(fixture.Menu));
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
            TweakPresenterListLayout.EntryAt(fixture.Menu, 7));
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
