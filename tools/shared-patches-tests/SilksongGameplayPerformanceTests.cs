using SsShellContracts;
using SsShellContracts.Engine;
using Xunit;
namespace SharedPatches.Tests;

[Collection("SS production shell")]
public partial class SilksongGameplayPerformanceTests
{
    sealed class Screen : IDsScreen, IDsHeaderTitle, IDsActionBar, IDsTabStrip
    {
        public string Id => "perf";
        public string Title => "MAP";
        public string HeaderTitle { get; set; } = "Choral Chambers";
        public bool Available => true;
        public bool ThrowTick, ThrowActions, ThrowPane, ThrowStrip, OverrideStrip;
        public int Ticks, Invocations;
        public Rect Pane;
        public Action Callback;
        public Screen() { Callback = Invoke; }
        void Invoke() { Invocations++; }
        public void Build(RectTransform host) { }
        public void OnShow() { }
        public void OnHide() { }
        public void Tick(float dt) { if (ThrowTick) throw new InvalidOperationException("tick fault"); Ticks++; }
        public void OnGesture(DsGesture gesture) { }
        public void CollectActions(List<DsAction> into)
        {
            if (ThrowActions) throw new InvalidOperationException("actions fault");
            into.Add(new DsAction("FULL MAP", Callback));
        }
        public Rect ActionPane => ThrowPane ? throw new InvalidOperationException("pane fault") : Pane;
        public bool StripOverride => OverrideStrip;
        public void CollectStrip(List<DsStripItem> into)
        {
            if (ThrowStrip) throw new InvalidOperationException("strip fault");
            into.Add(new DsStripItem { Badge = "1" });
        }
        public void OnStripSelect(int index) { }
    }
    static (DsShell, Screen) Shell()
    {
        DsGameData.InGame = true;
        var root = new GameObject("perf-shell").AddComponent<RectTransform>();
        var shell = new DsShell(root);
        var page = new Screen();
        shell.Register(page, InventoryPaneList.PaneTypes.Map);
        shell.Finish("perf"); shell.SetIdle(false); shell.SetVisible(true);
        for (int i = 0; i < 10; i++) shell.Tick(.016f);
        return (shell, page);
    }
    [Fact]
    public void Active_settled_shell_does_not_allocate_guard_or_title_work()
    {
        var (shell, page) = Shell();
        try
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120; i++) shell.Tick(.016f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
            Assert.Equal(130, page.Ticks);
        }
        finally { shell.Dispose(); }
    }
    [Fact]
    public void Static_action_measurement_is_retained_and_typography_rearms()
    {
        var host = new GameObject("action-host").AddComponent<RectTransform>();
        var bar = new DsActionBar(); bar.Build(host, 1240, 300);
        Action callback = () => { };
        var actions = new List<DsAction> { new("FULL MAP", callback) };
        bar.Set(actions, default);
        var label = host.GetChild(0).GetChild(1).GetComponent<TextMeshProUGUI>();
        int measures = label.Measurements;
        for (int i = 0; i < 120; i++) bar.Set(actions, default);
        Assert.Equal(measures, label.Measurements);
        label.fontSize += 4; bar.Set(actions, default); Assert.Equal(++measures, label.Measurements);
        label.font = new FontAsset(); bar.Set(actions, default); Assert.Equal(++measures, label.Measurements);
        label.fontStyle = FontStyles.Bold; bar.Set(actions, default); Assert.Equal(++measures, label.Measurements);
        label.characterSpacing = 2; bar.Set(actions, default); Assert.Equal(++measures, label.Measurements);
        actions[0] = new("RESET", callback); bar.Set(actions, default); Assert.Equal(++measures, label.Measurements);
    }
    [Fact]
    public void Unchanged_label_uses_current_callback_availability_and_geometry()
    {
        var host = new GameObject("action-host").AddComponent<RectTransform>();
        var bar = new DsActionBar(); bar.Build(host, 1240, 300);
        int old = 0, current = 0;
        var actions = new List<DsAction> { new("USE", () => old++, place: DsActionPlace.Pane) };
        var pane = new Rect(0, 0, 300, 300); bar.Set(actions, pane);
        actions[0] = new("USE", () => current++, place: DsActionPlace.Pane, alpha: .5f);
        pane.x = 400; bar.Set(actions, pane);
        Assert.False(bar.OnTap(new Vector2(150, 250)));
        Assert.True(bar.OnTap(new Vector2(550, 250))); Assert.Equal(0, old); Assert.Equal(1, current);
        actions[0] = new("USE", () => current++, disabled: true, place: DsActionPlace.Pane);
        bar.Set(actions, pane); Assert.False(bar.OnTap(new Vector2(550, 250)));
        bar.Clear(); Assert.False(bar.OnTap(new Vector2(550, 250)));
    }
    [Theory]
    [InlineData("tick")]
    [InlineData("actions")]
    [InlineData("pane")]
    [InlineData("strip")]
    public void Page_failure_clears_actions_even_after_partial_collection(string fault)
    {
        var (shell, page) = Shell();
        try
        {
            var actions = FixtureAccess.Field<DsActionBar>(shell, "_actions");
            var rows = FixtureAccess.Field<System.Collections.IList>(actions, "_rows");
            Vector2 point = FixtureAccess.Field<Rect>(rows[0], "Hit").center;
            if (fault == "strip") { page.OverrideStrip = true; shell.Tick(.016f); Assert.True(FixtureAccess.Field<bool>(shell, "_stripShown")); }
            page.ThrowTick = fault == "tick"; page.ThrowActions = fault == "actions"; page.ThrowPane = fault == "pane";
            page.ThrowStrip = fault == "strip";
            shell.Tick(.016f);
            Assert.False(actions.OnTap(point));
            Assert.Equal(0, page.Invocations);
            if (fault == "strip") Assert.False(FixtureAccess.Field<bool>(shell, "_stripShown"));
        }
        finally { shell.Dispose(); }
    }
    [Fact]
    public void Healthy_inventory_is_reused_and_visibility_is_live()
    {
        var hud = new GameObject("hud").AddComponent<DsHudView>();
        hud.Build(new GameObject("host").AddComponent<RectTransform>(), 900, 230);
        var collect = FixtureAccess.Delegate(hud, "CollectRenderers");
        collect(); int queries = Component.InventoryQueries;
        int targets = FixtureAccess.Field<List<GameObject>>(hud, "_targets").Count;
        for (int i = 0; i < 20; i++) { Time.frameCount++; collect(); }
        Assert.Equal(queries, Component.InventoryQueries);
        var native = hud.NativeTargets[0]; native.GetComponent<Renderer>().enabled = false; collect();
        Assert.Equal(targets - 1, FixtureAccess.Field<List<GameObject>>(hud, "_targets").Count);
        native.GetComponent<Renderer>().enabled = true; collect();
        Assert.Equal(targets, FixtureAccess.Field<List<GameObject>>(hud, "_targets").Count);
        hud.Stop();
    }
    [Fact]
    public void Inventory_recovers_structural_addition_and_component_only_fallback()
    {
        var hud = new GameObject("hud").AddComponent<DsHudView>();
        hud.Build(new GameObject("host").AddComponent<RectTransform>(), 900, 230);
        var collect = FixtureAccess.Delegate(hud, "CollectRenderers"); collect();
        int targets = FixtureAccess.Field<List<GameObject>>(hud, "_targets").Count;
        var child = new GameObject("late-art") { layer = 5 };
        child.transform.SetParent(hud.NativeTargets[0].transform); child.AddComponent<Renderer>();
        Time.frameCount++; collect(); Assert.Equal(targets + 1, FixtureAccess.Field<List<GameObject>>(hud, "_targets").Count);
        var empty = new GameObject("empty") { layer = 5 }; empty.transform.SetParent(child.transform);
        Time.frameCount++; collect(); empty.AddComponent<Renderer>();
        Time.frameCount += 31; collect(); Assert.Equal(targets + 2, FixtureAccess.Field<List<GameObject>>(hud, "_targets").Count);
        hud.SetVisible(false); int queries = Component.InventoryQueries;
        Time.frameCount++; collect(); Assert.True(Component.InventoryQueries > queries);
        hud.Stop();
    }
}
