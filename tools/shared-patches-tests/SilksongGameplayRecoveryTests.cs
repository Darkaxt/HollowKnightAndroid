using SsShellContracts;
using SsShellContracts.Engine;
using Xunit;
using static SsShellContracts.FixtureAccess;
namespace SharedPatches.Tests;

public partial class SilksongGameplayPerformanceTests
{
    static DsHudView Hud()
    {
        var hud = new GameObject("hud").AddComponent<DsHudView>();
        hud.Build(new GameObject("host").AddComponent<RectTransform>(), 900, 230);
        return hud;
    }
    [Fact]
    public void All_native_inventories_settle_while_mask_tool_and_lifeblood_values_stay_live()
    {
        var hud = Hud();
        try
        {
            var health = Field<Transform>(hud, "_health");
            var tools = Field<Transform>(hud, "_tools");
            var third = new GameObject("mask3").AddComponent<SsShellContracts.PlayMakerFSM>();
            third.transform.SetParent(health); third.transform.position = new Vector3(2, 0, 0);
            third.FsmVariables.Number.Value = 3;
            var blue = new GameObject("blue").AddComponent<BlueHealth>(); blue.transform.SetParent(health);
            blue.transform.position = new Vector3(4, 0, 0);
            var icon = new GameObject("tool").AddComponent<ToolHudIcon>(); icon.transform.SetParent(tools);
            icon.transform.position = new Vector3(6, -1, 0); icon.CurrentTool = new ToolItem();
            var canvas = new GameObject("tool-canvas").AddComponent<Canvas>(); canvas.transform.SetParent(tools);
            canvas.renderMode = RenderMode.WorldSpace; canvas.gameObject.layer = 5;
            var graphic = new GameObject("tool-graphic").AddComponent<Graphic>(); graphic.transform.SetParent(canvas.transform);
            graphic.canvas = canvas; graphic.gameObject.layer = 5;
            var collect = Delegate(hud, "CollectRenderers"); var prepare = Delegate(hud, "PrepareCanvasScope");
            var frame = (Func<bool>)typeof(DsHudView).GetMethod("FrameCamera", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.CreateDelegate(typeof(Func<bool>), hud);
            var scope = Field<DsHudRenderScope<GameObject>>(hud, "_canvasScope");
            collect(); prepare(); scope.Restore(); Assert.True(frame()); int queries = Component.InventoryQueries;
            for (int i = 0; i < 20; i++) { Time.frameCount++; prepare(); scope.Restore(); collect(); Assert.True(frame()); }
            Assert.Equal(queries, Component.InventoryQueries);
            Assert.Equal(1, Field<int>(hud, "_activeTools")); Assert.Equal(1, Field<int>(hud, "_activeToolCanvases"));
            var before = Field<Bounds>(hud, "_bounds"); icon.CurrentTool = null; blue.gameObject.SetActive(false);
            Assert.Equal(3, Field<List<Vector3>>(hud, "_maskPositions").Count);
            third.FsmVariables.Number.Value = 6;
            collect(); Assert.True(frame());
            Assert.Equal(0, Field<int>(hud, "_activeTools")); Assert.Equal(2, Field<List<Vector3>>(hud, "_maskPositions").Count);
            Assert.True(Field<Bounds>(hud, "_bounds").size.x < before.size.x);
            Assert.Equal(queries, Component.InventoryQueries);
            prepare(); Assert.Equal(3, canvas.gameObject.layer); Assert.Equal(3, graphic.gameObject.layer);
            scope.Restore(); Assert.Equal(5, canvas.gameObject.layer); Assert.Equal(5, graphic.gameObject.layer);
            // A temporary routing layer is never baked into inventory authority.
            collect(); Assert.All(Field<List<GameObject>>(hud, "_targets"), go => Assert.Equal(5, go.layer));
        }
        finally { hud.Stop(); SsShellContracts.PlayerData.instance.CurrentMaxHealth = 5; }
    }
    [Fact]
    public void Retired_renderers_rebind_without_test_repairing_inventory_lists()
    {
        var hud = Hud();
        try
        {
            var collect = Delegate(hud, "CollectRenderers"); collect();
            var native = hud.NativeTargets[0]; var old = native.GetComponent<Renderer>();
            int queries = Component.InventoryQueries; old.Retired = true;
            var replacement = native.AddComponent<Renderer>();
            collect();
            Assert.True(Component.InventoryQueries > queries);
            Assert.Contains(replacement, Field<List<Renderer>>(hud, "_renderers"));
            Assert.DoesNotContain(old, Field<List<Renderer>>(hud, "_renderers"));
        }
        finally { hud.Stop(); }
    }
    [Fact]
    public void Reparent_removal_equal_count_replacement_and_late_effect_roots_rearm()
    {
        var hud = Hud();
        try
        {
            var collect = Delegate(hud, "CollectRenderers"); collect();
            var root = Field<Transform>(hud, "_hudRoot");
            var effect = new GameObject("Crest Get Effects") { layer = 5 }; effect.transform.SetParent(root);
            var art = effect.AddComponent<Renderer>(); collect(); Assert.Contains(art, Field<List<Renderer>>(hud, "_renderers"));
            effect.transform.SetParent(null);
            var replacement = new GameObject("Crest Get Effects") { layer = 5 }; replacement.transform.SetParent(root);
            var newArt = replacement.AddComponent<Renderer>(); collect();
            Assert.Contains(newArt, Field<List<Renderer>>(hud, "_renderers")); Assert.DoesNotContain(art, Field<List<Renderer>>(hud, "_renderers"));
            var health = new GameObject("replacement-health") { layer = 5 }; health.transform.SetParent(root);
            var newHealthArt = health.AddComponent<Renderer>(); Set(hud, "_health", health.transform); collect();
            Assert.Contains(newHealthArt, Field<List<Renderer>>(hud, "_renderers"));
            Assert.DoesNotContain(hud.NativeTargets[0].GetComponent<Renderer>(), Field<List<Renderer>>(hud, "_renderers"));
        }
        finally { hud.Stop(); }
    }
    [Fact]
    public void Actual_failure_path_restores_canvas_and_overlay_scopes()
    {
        var hud = Hud();
        try
        {
            var tools = Field<Transform>(hud, "_tools");
            var canvas = new GameObject("tool-canvas").AddComponent<Canvas>(); canvas.transform.SetParent(tools);
            canvas.gameObject.layer = 5; canvas.renderMode = RenderMode.WorldSpace;
            Call(hud, "PrepareCanvasScope"); Call(hud, "SuppressOverlay");
            Assert.Equal(3, canvas.gameObject.layer);
            var overlay = Field<Transform>(hud, "_overlayRoot"); Assert.True(overlay.GetComponent<Renderer>().forceRenderingOff);
            Call(hud, "Fail", new InvalidOperationException("injected capture fault"));
            Assert.Equal(5, canvas.gameObject.layer); Assert.False(overlay.GetComponent<Renderer>().forceRenderingOff);
            Assert.False(overlay.GetComponent<CanvasRenderer>().cull);
            Assert.True(Field<bool>(hud, "_failed")); Assert.False(Field<Camera>(hud, "_capture").enabled);
        }
        finally { hud.Stop(); }
    }
    [Fact]
    public void Title_raw_language_and_header_geometry_changes_update_promptly()
    {
        var (shell, page) = Shell();
        try
        {
            var title = Field<TextMeshProUGUI>(shell, "_headerTitle"); string first = title.text;
            for (int i = 0; i < 20; i++) { shell.Tick(.016f); Assert.Same(first, title.text); }
            page.HeaderTitle = "Citadelle"; shell.Tick(.016f); Assert.Equal("CITADELLE", title.text);
            var hud = Field<DsHudView>(shell, "_hud"); hud.TitleRegion = new Rect(0, 100, 900, 60);
            shell.Tick(.016f); Assert.Equal(-100, title.rectTransform.anchoredPosition.y);
            Assert.Equal(54, title.rectTransform.sizeDelta.y);
        }
        finally { shell.Dispose(); }
    }
    [Fact]
    public void Map_callbacks_remain_instance_bound_through_marker_mode_and_reset_fade()
    {
        var map = new DsMapScreen(); map.Build(new GameObject("map-host").AddComponent<RectTransform>()); map.Tick(.016f);
        var actions = new List<DsAction>(); map.CollectActions(actions);
        var toggle = actions[0].Invoke; var enter = actions.Find(a => a.Label == "MARKERS").Invoke;
        Assert.NotNull(enter);
        for (int i = 0; i < 120; i++) { actions.Clear(); map.CollectActions(actions); Assert.Same(toggle, actions[0].Invoke); }
        enter(); actions.Clear(); map.CollectActions(actions); Assert.Equal("EXIT", actions[0].Label);
        var exit = actions[0].Invoke;
        for (int i = 0; i < 120; i++) { actions.Clear(); map.CollectActions(actions); Assert.Same(exit, actions[0].Invoke); }
        exit(); Assert.False(map.StripOverride);
        var view = Field<DsMapView>(map, "_view"); view.Pan(new Vector2(10, 0)); Set(map, "_lastTouch", Time.unscaledTime);
        actions.Clear(); map.CollectActions(actions); var reset = actions.Find(a => a.Label == "RESET"); Assert.NotNull(reset.Invoke);
        Time.unscaledTime += 3.3f; actions.Clear(); map.CollectActions(actions);
        var faded = actions.Find(a => a.Label == "RESET"); Assert.Same(reset.Invoke, faded.Invoke); Assert.InRange(faded.Alpha, .49f, .51f);
        reset.Invoke(); Assert.False(view.ViewMoved);
    }
}
