using System.Reflection;
using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using DualSouls.Mods.Silksong;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakMenuPresenterLayoutTests
{
    [Fact]
    public void HollowKnightPresenterMapsEveryCatalogNodeThroughItsNativeWindowExactlyOnce()
    {
        AssertProfileTraversal(HollowKnightRows());
    }

    [Fact]
    public void SilksongPresenterMapsEveryCatalogNodeThroughItsNativeWindowExactlyOnce()
    {
        AssertProfileTraversal(SilksongRows());
    }

    [Fact]
    public void HollowKnightGameBCancelsTheNativeModsMenu()
    {
        Assert.Equal(
            TweakMenuCancelTarget.CloseMenu,
            TweakMenuPresenterLayout.CancelTarget(nestedRouteOpen: false));
    }

    [Fact]
    public void SilksongGameBCancelsItsNestedRouteBeforeTheNativeModsMenu()
    {
        Assert.Equal(
            TweakMenuCancelTarget.CloseRoute,
            TweakMenuPresenterLayout.CancelTarget(nestedRouteOpen: true));
        Assert.Equal(
            TweakMenuCancelTarget.CloseMenu,
            TweakMenuPresenterLayout.CancelTarget(nestedRouteOpen: false));
    }

    [Fact]
    public void HollowKnightLongestCatalogDescriptionFitsAtNativeScaleWithoutOverlap()
    {
        AssertDescriptionLayout(
            HollowKnightRows(),
            rowStep: TweakMenuPresenterLayout.MinimumAdaptiveRowStep,
            rowButtonHeight: TweakMenuPresenterLayout.MinimumAdaptiveRowStep -
                TweakMenuPresenterLayout.RowVerticalPadding);
    }

    [Fact]
    public void SilksongLongestCatalogDescriptionFitsAtNativeScaleWithoutOverlap()
    {
        AssertDescriptionLayout(
            SilksongRows(),
            rowStep: TweakMenuPresenterLayout.FixedRowStep,
            rowButtonHeight: TweakMenuPresenterLayout.FixedRowButtonHeight);
    }

    [Fact]
    public void MaximumOperationEvidenceFitsAtTheBoundedAutosizedDescriptionScale()
    {
        string maximumEvidence = new string('W', 240);

        Assert.True(
            TweakMenuPresenterLayout.OperationDescriptionFits(maximumEvidence));
        Assert.Equal(16, TweakMenuPresenterLayout.DescriptionMinimumFontSize);
    }

    static void AssertProfileTraversal(IReadOnlyList<TweakDescriptor> catalog)
    {
        var controller = new TweakController(
            new CatalogAdapter(catalog),
            new MemoryStore());
        Assert.True(controller.Initialize().Success);
        var model = new TweakMenuModel(
            controller,
            TweakMenuPresenterLayout.VisibleRows);

        for (int groupIndex = 0; groupIndex < model.Groups.Count; groupIndex++)
        {
            model.MoveGroup(groupIndex - model.SelectedGroupIndex);
            int rowCount = model.CurrentRows.Count;
            var visitedTargets = new List<TweakMenuFocusTarget>();
            TweakMenuFocusTarget current = TweakMenuFocusTarget.Group;

            do
            {
                Assert.DoesNotContain(current, visitedTargets);
                visitedTargets.Add(current);
                if (current.Kind == TweakMenuFocusKind.Row)
                {
                    model.MoveRow(current.RowIndex - model.SelectedRowIndex);
                    Assert.Equal(current.RowIndex, model.SelectedRowIndex);
                }

                int buttonIndex = TweakMenuPresenterLayout.ButtonIndex(
                    current, model.WindowStart, rowCount);
                Assert.InRange(buttonIndex, 0, TweakMenuPresenterLayout.ButtonCount - 1);

                if (current.Kind == TweakMenuFocusKind.Row)
                    Assert.Equal(
                        current.RowIndex,
                        TweakMenuPresenterLayout.DataIndexForRowButton(
                            buttonIndex, model.WindowStart, rowCount));

                current = TweakMenuFocusGraph.Move(current, 1, rowCount);
            }
            while (current != TweakMenuFocusTarget.Group);

            Assert.Equal(rowCount + 3, visitedTargets.Count);
        }
    }

    static void AssertDescriptionLayout(
        IReadOnlyList<TweakDescriptor> rows,
        float rowStep,
        float rowButtonHeight)
    {
        TweakDescriptor longest = rows
            .OrderByDescending(row => row.Description.Length)
            .First();

        Assert.True(
            TweakMenuPresenterLayout.DescriptionFits(longest.Description),
            $"{longest.Id} needs {TweakMenuPresenterLayout.DescriptionLineCount(longest.Description)} lines");
        Assert.True(
            TweakMenuPresenterLayout.ControlsDoNotOverlap(rowStep, rowButtonHeight));
        Assert.Equal(24, TweakMenuPresenterLayout.DescriptionFontSize);
    }

    static IReadOnlyList<TweakDescriptor> HollowKnightRows() =>
        new HollowKnightTweakAdapter(Proxy<IHollowKnightTweakApi>()).Descriptors;

    static IReadOnlyList<TweakDescriptor> SilksongRows() =>
        new SilksongTweakAdapter(Proxy<ISilksongTweakApi>()).Descriptors;

    static T Proxy<T>() where T : class =>
        DispatchProxy.Create<T, DefaultDispatchProxy>();

    sealed class CatalogAdapter : ITweakAdapter
    {
        public CatalogAdapter(IReadOnlyList<TweakDescriptor> descriptors)
        {
            Descriptors = descriptors;
        }

        public string GameId => "presenter-contract";
        public IReadOnlyList<TweakDescriptor> Descriptors { get; }
        public void CaptureBaseline() { }
        public TweakActionResult Apply(string id, string value) => TweakActionResult.Ok();
        public void RestoreBaseline() { }
        public void Tick() { }
    }

    sealed class MemoryStore : Dictionary<string, string>, ITweakStore
    {
        public string Read(string key) => TryGetValue(key, out string value) ? value : null;
        public void Write(string key, string value) => this[key] = value;
        public void Flush() { }
    }

    public class DefaultDispatchProxy : DispatchProxy
    {
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            Type type = targetMethod.ReturnType;
            return type == typeof(void) || !type.IsValueType
                ? null
                : Activator.CreateInstance(type);
        }
    }
}
