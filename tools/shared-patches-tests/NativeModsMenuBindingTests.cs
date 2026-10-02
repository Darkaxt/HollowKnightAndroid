using System.Collections;
using System.Reflection;
using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using DualSouls.Mods.Silksong;
using Xunit;
using H = NativeMenuFixture.Hk.Engine;
using S = NativeMenuFixture.Ss.Engine;

namespace SharedPatches.Tests;

[CollectionDefinition("Native menu production bodies", DisableParallelization = true)]
public sealed class NativeMenuProductionCollection { }

// Fixture construction/reflection is test-only. All presenter/Stage/API orchestration
// under test is generated from complete actual declarations, not duplicated here.
internal sealed class NativeMenuFixture
{
    internal readonly bool Ss;
    internal readonly Type WorldType;
    internal readonly string Prefix;
    internal readonly dynamic Owner, Manager, Screen, Content, GameButton;
    internal readonly MenuStore Store = new();
    internal readonly object Session;
    internal readonly TweakMenuModel Menu;
    internal readonly TweakController Controller;
    internal readonly H.HollowKnightGameTweakApi HkApi;
    internal readonly MenuApiProxy SsApi;
    internal readonly List<object> OriginalRoots = new();
    internal readonly List<object> OriginalButtons = new();
    internal readonly List<object> OriginalNavigation = new();
    internal HashSet<object> BaselineObjects;
    internal NativeMenuFixture(bool ss)
    {
        Ss = ss; Prefix = "NativeMenuFixture." + (ss ? "Ss" : "Hk") + ".Engine.";
        WorldType = Type("World"); Static("World", "Reset");
        if (!ss)
        {
            foreach (string field in new[] { "pendingBenchCompletion", "pendingBenchToken", "pendingBenchGeneration", "pendingBenchDeadline", "lastAtBench" })
                ResetStatic(typeof(H.HkStageHooks), field);
            SetStatic(typeof(H.HkStageHooks), "benchLoaded", true);
            ((IDictionary)GetStatic(typeof(H.HkStageHooks), "benches")).Clear();
        }
        dynamic canvas = Go("Canvas");
        Manager = Go("UIManager").AddComponent(Type("UIManager"));
        Manager.UICanvas = canvas.AddComponent(Type("Canvas"));
        Screen = Go("OptionsMenuScreen", canvas.transform).AddComponent(Type("MenuScreen"));
        Manager.optionsMenuScreen = Screen;
        SetStatic(Type("UIManager"), "instance", Manager);
        dynamic title = Go("Title", Screen.transform); title.AddComponent(Type("UI.Text"));
        dynamic controls = Go("Controls", Screen.transform);
        Content = Go("Content", Screen.transform);
        if (!ss) {
            Screen.title = title.AddComponent(Type("CanvasGroup"));
            Screen.controls = controls.AddComponent(Type("CanvasGroup"));
            Screen.content = Content.AddComponent(Type("CanvasGroup"));
        }
        string[] names = { "GameOptions", "AudioOptions", "VideoOptions", "ControllerOptions", "KeyboardOptions" };
        string[] buttons = { "GameOptionsButton", "AudioOptionsButton", "VideoOptionsButton", "GamepadOptionsButton", "KeyboardOptionsButton" };
        var originalButtons = new List<dynamic>();
        for (int i = 0; i < names.Length; i++)
        {
            dynamic wrapper = Go(names[i], Content.transform); OriginalRoots.Add(wrapper);
            wrapper.transform.anchoredPosition = (dynamic)Activator.CreateInstance(Type("Vector2"), 0f, -25f - 88f * i);
            dynamic buttonGo = Go(buttons[i], wrapper.transform);
            dynamic button = buttonGo.AddComponent(Type("UI.MenuButton")); originalButtons.Add(button);
            button.buttonType = (dynamic)Enum.Parse(Type("UI.MenuButton+MenuButtonType"), "Proceed");
            button.cancelAction = (dynamic)Enum.Parse(Type("GlobalEnums.CancelAction"), "LeaveOptionsMenu");
            dynamic label = Go("Menu Button Text", buttonGo.transform); label.AddComponent(Type("UI.Text")).text = i == 0 ? "LOCALIZED GAME LABEL" : names[i];
            if (i == 0)
            {
                GameButton = button;
                dynamic trigger = buttonGo.AddComponent(Type("EventSystems.EventTrigger"));
                dynamic submit = Activator.CreateInstance(Type("EventSystems.EventTrigger+Entry"));
                submit.eventID = (dynamic)Enum.Parse(Type("EventSystems.EventTriggerType"), "Submit");
                dynamic listener = Activator.CreateInstance(Type("Events.UnityEvent+Listener"));
                listener.Target = Manager; listener.Method = "UIGoToGameOptionsMenu";
                submit.callback.Listeners.Add(listener); trigger.triggers.Add(submit);
                dynamic cancel = Activator.CreateInstance(Type("EventSystems.EventTrigger+Entry"));
                cancel.eventID = (dynamic)Enum.Parse(Type("EventSystems.EventTriggerType"), "Cancel");
                dynamic foreign = Activator.CreateInstance(Type("Events.UnityEvent+Listener"));
                foreign.Target = Manager; foreign.Method = "UILeaveOptionsMenu";
                cancel.callback.Listeners.Add(foreign); trigger.triggers.Add(cancel);
                if (Ss)
                    button.OnSubmitPressed.AddListener((Action)(() => Increment("ForeignSubmitActions")));
            }
        }
        for (int i = 0; i < originalButtons.Count; i++)
        {
            dynamic nav = Activator.CreateInstance(Type("UI.Navigation"));
            nav.mode = (dynamic)Enum.Parse(Type("UI.Navigation+Mode"), "Explicit");
            nav.selectOnUp = originalButtons[(i + 4) % 5]; nav.selectOnDown = originalButtons[(i + 1) % 5];
            originalButtons[i].navigation = nav;
            OriginalButtons.Add(originalButtons[i]); OriginalNavigation.Add(nav);
        }
        Screen.defaultHighlight = GameButton;
        Screen.gameObject.AddComponent(Type("MenuButtonList"));
        if (!ss)
        {
            HkApi = new H.HollowKnightGameTweakApi();
            var session = new HollowKnightModsSession(HkApi, Store, TweakMenuPresenterLayout.VisibleRows); session.Tick();
            Session = session; Menu = session.Menu; Controller = session.Controller;
            H.HollowKnightModsRuntime.Current = new H.HollowKnightModsRuntime { Session = session };
        }
        else
        {
            var api = DispatchProxy.Create<ISilksongTweakApi, MenuApiProxy>(); SsApi = (MenuApiProxy)(object)api;
            var adapter = new SilksongTweakAdapter(api); SsApi.Descriptors = adapter.Descriptors;
            var session = new TweakSession(() => true, () => adapter, Store, TweakMenuPresenterLayout.VisibleRows); session.Tick();
            Session = session; Menu = session.Menu; Controller = session.Controller;
            S.SilksongModsRuntime.Current = new S.SilksongModsRuntime { Session = session };
        }
        Owner = Go("NativePresenter").AddComponent(Type((ss ? "Silksong" : "HollowKnight") + "NativeModsMenu"));
        BaselineObjects = Objects.Cast<object>().ToHashSet();
        Static("World", "Points").Clear();
    }
    internal Type Type(string suffix) => typeof(NativeModsMenuBindingTests).Assembly.GetType(Prefix + suffix, true);
    internal dynamic Go(string name, dynamic parent = null)
    {
        dynamic go = Activator.CreateInstance(Type("GameObject"), new object[] { name, Array.Empty<Type>() });
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }
    internal dynamic Static(string type, string name, params object[] args)
    {
        Type t = Type(type); FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return f != null ? f.GetValue(null) : t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
    internal static object GetStatic(Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    internal static void SetStatic(Type type, string name, object value) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
    static void ResetStatic(Type type, string name)
    {
        var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (f != null) f.SetValue(null, f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
    }
    internal void Increment(string name) => SetStatic(WorldType, name, (int)GetStatic(WorldType, name) + 1);
    internal int Count(string name) => (int)GetStatic(WorldType, name);
    internal object Field(string name) => ((object)Owner).GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Owner);
    internal dynamic Call(string name, params object[] args)
    {
        MethodInfo m = ((object)Owner).GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return m.Invoke(Owner, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    internal void Bind() { Call("TryBind"); Call("Update"); }
    internal void Open(bool skins = false)
    {
        Call("OpenEntry", skins ? 1 : 0); Static("World", "Pump", 100);
        Assert.True((bool)Field("_nativeOpen"), string.Join("\n", ((IEnumerable)Static("World", "Logs")).Cast<object>()));
    }
    internal dynamic Entry(bool skins = false) => Field(skins ? "_skinsEntryRoot" : "_entryRoot");
    internal dynamic Row(int slot = -1)
    {
        if (slot >= 0) return ((IList)Field("_buttons"))[TweakMenuPresenterLayout.FirstRowButtonIndex + slot];
        return ((IEnumerable)Field("_buttons")).Cast<dynamic>().First(b => b.DataIndex == Menu.SelectedRowIndex);
    }
    internal dynamic Button(int index) => ((IList)Field("_buttons"))[index];
    internal List<dynamic> Objects => ((IEnumerable)Static("World", "Objects")).Cast<dynamic>().ToList();
    internal List<dynamic> OwnedRoots => Objects.Where(o => !BaselineObjects.Contains((object)o) && (o.name == "MODS" || o.name == "SKINS" || o.name == "ModsMenuScreen" || o.name == "SkinsMenuScreen" || o.name == "NativeModsMenuStaging")).ToList();
    internal bool Transitioning {
        get {
            if (!Ss) return (bool)Field("_transitioning");
            object lifecycle = Field("_lifecycle");
            return (bool)lifecycle.GetType().GetProperty("Transitioning").GetValue(lifecycle);
        }
    }
    internal void AssertNoActiveOwned()
    {
        Assert.DoesNotContain(OwnedRoots, o => (bool)o.activeInHierarchy);
        Assert.False((bool)Field("_nativeOpen"));
        Assert.Null(Field("_transitionCoroutine"));
        dynamic transport = Field("_skinTransport");
        Assert.False(transport.Due(1000f));
        var queued = ((IEnumerable)Static("World", "DestroyQueue")).Cast<object>().ToHashSet();
        foreach (dynamic go in Objects.Where(o => !BaselineObjects.Contains((object)o) && !(bool)o.Dead))
        {
            Assert.False((bool)go.activeInHierarchy, "active allocated clone: " + (string)go.name);
            dynamic ancestor = go.transform;
            bool covered = false;
            while (ancestor != null) { if (queued.Contains((object)ancestor.gameObject)) { covered = true; break; } ancestor = ancestor.parent; }
            Assert.True(covered, "unowned allocated clone: " + (string)go.name);
        }
        for (int i = 0; i < OriginalButtons.Count; i++)
            Assert.Equal(OriginalNavigation[i], ((dynamic)OriginalButtons[i]).navigation);
        dynamic selected = Static("EventSystems.EventSystem", "current").currentSelectedGameObject;
        if (selected != null) Assert.True(BaselineObjects.Contains((object)selected));
        if (!Ss) Assert.Null(GetStatic(typeof(H.HkStageHooks), "pendingBenchCompletion"));
    }
    internal void SelectTweak(string id)
    {
        for (int g = 0; g < Menu.Groups.Count; g++)
        {
            for (int r = 0; r < Menu.CurrentRows.Count; r++)
                if (Menu.CurrentRows[r].Id == id) { Menu.MoveRow(r - Menu.SelectedRowIndex); Call("Paint"); return; }
            Menu.MoveGroup(1);
        }
        throw new InvalidOperationException("missing descriptor " + id);
    }
    internal void Record(string scene, string marker = "bench", int type = 1, bool facing = true)
    {
        Type hooks = typeof(H.HkStageHooks); Type record = hooks.GetNestedType("BenchRecord", BindingFlags.NonPublic);
        object value = Activator.CreateInstance(record, true);
        record.GetField("scene").SetValue(value, scene); record.GetField("marker").SetValue(value, marker);
        record.GetField("type").SetValue(value, type); record.GetField("facingRight").SetValue(value, facing);
        ((IDictionary)GetStatic(hooks, "benches"))[scene] = value;
    }
    internal string NativeText(object root)
    {
        dynamic ancestor = root;
        object component = Objects.Where(go => (bool)go.transform.IsChildOf(ancestor.transform))
            .SelectMany(go => ((IEnumerable)go.Components).Cast<object>()).First(c => c.GetType() == Type("UI.Text"));
        return (string)component.GetType().GetProperty("text").GetValue(component);
    }
    internal void SkinSnapshot(string evidence = "TERMINAL")
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new {
            ok = true, profileId = Ss ? "silksong" : "hollow-knight", configSha256 = new string('a', 64),
            mode = "OFF", spriteScope = "ALL", selectedPackId = "host-model-pack", eligiblePackIds = new[] { "host-model-pack" },
            packs = new[] { new { id = "host-model-pack", name = "HOST MODEL PACK", author = "BOUNDARY FIXTURE" } }, evidenceState = evidence,
            observation = new { status = "HOST_MODEL_ONLY", featureId = "SKIN-MODE", operationId = "modeled-menu", operationGeneration = 1 }
        });
        SetStatic(Type("AndroidJavaClass"), "Snapshot", json);
    }
    internal void Receipt(string name, object value)
    {
        string root = Environment.GetEnvironmentVariable("NATIVE_MENU_EVIDENCE_ROOT");
        if (string.IsNullOrEmpty(root)) return;
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, (Ss ? "ss-" : "hk-") + name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    internal void Tick() { if (Session is HollowKnightModsSession hk) hk.Tick(); else ((TweakSession)Session).Tick(); Call("Update"); }
}

public sealed class MenuStore : ITweakStore
{
    readonly Dictionary<string, string> _values = new();
    public int Writes, Flushes;
    public string Read(string key) => _values.TryGetValue(key, out string value) ? value : null;
    public void Write(string key, string value) { Writes++; _values[key] = value; }
    public void Flush() { Flushes++; }
}
public class MenuApiProxy : DispatchProxy
{
    public IReadOnlyList<TweakDescriptor> Descriptors;
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "get_IsReady") return true;
        if (method.Name == "Readback") return TweakActionResult.Ok(TweakReadback.Choice(Descriptors.Single(d => d.Id == (string)args[0]).DefaultValue));
        if (method.Name == "DrainCompletedOperations") return Array.Empty<TweakAdapterCompletion>();
        if (method.Name == "OpenBenchTeleport") S.SilksongNativeModsMenu.OpenBenchTeleportRoute((long)args[0]);
        return null;
    }
}

[Collection("Native menu production bodies")]
public sealed class NativeModsMenuBindingTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HealthyNativeOptionsModsResetAndSkinsControls(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind(); Assert.NotNull(f.Entry());
        f.Static("World", "Submit", f.GameButton.gameObject);
        Assert.Equal(1, f.Count("NativeGameActions"));
        f.Open(); f.SelectTweak("run_speed"); f.Call("Submit", f.Row());
        f.Call("Submit", f.Button(TweakMenuPresenterLayout.ResetButtonIndex));
        Assert.True(f.Menu.IsOpen);
        f.Call("Close"); f.Static("World", "Pump", 100); f.Open(skins: true);
        Assert.True((bool)f.Field("_nativeOpen"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClonedNativeSubmitNeverInvokesOriginalGameOptionsOrForeignSubmit(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind();
        dynamic entry = f.Field("_entrySelectable"); f.Static("World", "Submit", entry.gameObject); f.Static("World", "Pump", 100);
        Assert.Equal(0, f.Count("NativeGameActions")); Assert.Equal(0, f.Count("ForeignSubmitActions"));
        Assert.True((bool)f.Field("_nativeOpen"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TeardownImmediatelyDeactivatesOwnedRootsBeforeDeferredDestroy(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind(); f.Open(); f.Call("CancelAndClearBinding");
        Assert.DoesNotContain(f.OwnedRoots, o => (bool)o.activeInHierarchy);
        Assert.All(f.OwnedRoots, o => Assert.Contains((object)o, ((IEnumerable)f.Static("World", "DestroyQueue")).Cast<object>()));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EveryFallibleBindingBoundaryRetiresAllAllocationsAndRetriesBeforeDestroyFlush(bool ss)
    {
        var healthy = new NativeMenuFixture(ss); healthy.Call("TryBind"); Assert.NotNull(healthy.Entry());
        string[] points = ((IEnumerable)healthy.Static("World", "Points")).Cast<string>().ToArray();
        Assert.True(points.Length > 100);
        var fired = new List<object>();
        try {
            for (int ordinal = 1; ordinal <= points.Length; ordinal++)
            {
                var f = new NativeMenuFixture(ss);
                NativeMenuFixture.SetStatic(f.WorldType, "FailAt", ordinal);
                try { f.Call("TryBind"); }
                catch (Exception e) { throw new InvalidOperationException("bind fault ordinal " + ordinal, e); }
                string[] actual = ((IEnumerable)f.Static("World", "FiredFailures")).Cast<string>().ToArray();
                Assert.Single(actual); Assert.Equal(points[ordinal - 1], actual[0]);
                fired.Add(new { ordinal, point = actual[0] });
                f.Call("CancelAndClearBinding"); f.AssertNoActiveOwned();
                f.Bind(); Assert.NotNull(f.Entry());
                Assert.Equal(1, f.Objects.Count(o => o.name == "MODS" && (bool)o.activeInHierarchy));
                Assert.Equal(1, f.Objects.Count(o => o.name == "SKINS" && (bool)o.activeInHierarchy));
                f.Static("World", "FlushDestroy"); f.Call("Update"); Assert.NotNull(f.Entry());
                f.Call("CancelAndClearBinding"); f.AssertNoActiveOwned();
                f.Static("World", "FlushDestroy"); f.AssertNoActiveOwned();
            }
        }
        finally { healthy.Receipt("binding-faults", new { expected = points.Length, fired, silentlySkipped = 0 }); }
        Assert.Equal(points.Length, fired.Count);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RejectsDuplicateGameOptionsTypedRoute(bool ss)
    {
        var f = new NativeMenuFixture(ss);
        f.Static("Object", "Instantiate", f.OriginalRoots[0], f.Content.transform, false).name = "GameOptions";
        f.Bind(); Assert.Null(f.Entry());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RejectsTemplateWithMoreThanOneSelectable(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.GameButton.gameObject.AddComponent(f.Type("UI.MenuButton"));
        f.Bind(); Assert.Null(f.Entry());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingMenuScreenOnAllocatedCloneCannotOrphanRoot(bool ss)
    {
        var f = new NativeMenuFixture(ss);
        NativeMenuFixture.SetStatic(f.WorldType, "CloneWithoutComponent", "MenuScreen");
        f.Call("TryBind"); Assert.Null(f.Entry()); f.AssertNoActiveOwned();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeAndCustomCancelDispatchCannotInvokeForeignOptionsAction(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind();
        dynamic entry = f.Field("_entrySelectable"); f.Static("World", "Cancel", entry.gameObject);
        Assert.Equal(0, f.Count("ForeignCancelActions"));
        f.Open(); f.Static("World", "Cancel", f.Row(0).Selectable.gameObject); f.Static("World", "Pump", 100);
        Assert.Equal(0, f.Count("ForeignCancelActions"));
    }
    [Theory]
    [InlineData(false, "missing-authority")] [InlineData(true, "missing-authority")]
    [InlineData(false, "foreign-authority")] [InlineData(true, "foreign-authority")]
    [InlineData(false, "wrong-selectable")] [InlineData(true, "wrong-selectable")]
    [InlineData(false, "foreign-skins")] [InlineData(true, "foreign-skins")]
    [InlineData(false, "ambiguous-manager")] [InlineData(true, "ambiguous-manager")]
    [InlineData(false, "wrong-text")] [InlineData(true, "wrong-text")]
    public void RejectedTopologyPreservesAllUnrelatedNativeControls(bool ss, string problem)
    {
        var f = new NativeMenuFixture(ss);
        dynamic trigger = ((IEnumerable)f.GameButton.gameObject.Components).Cast<object>().FirstOrDefault(c => c.GetType() == f.Type("EventSystems.EventTrigger"));
        if (problem == "missing-authority") trigger.triggers.Clear();
        if (problem == "foreign-authority") trigger.triggers[0].callback.Listeners[0].Target = f.Go("ForeignUi").AddComponent(f.Type("UIManager"));
        if (problem == "wrong-selectable") {
            f.GameButton.gameObject.Components.Remove(f.GameButton);
            f.GameButton.gameObject.AddComponent(f.Type("UI.Selectable"));
        }
        if (problem == "foreign-skins") f.Go("SKINS", f.Content.transform).AddComponent(f.Type("UI.MenuButton"));
        if (problem == "ambiguous-manager") {
            dynamic manager = f.Go("SecondLoadedUi").AddComponent(f.Type("UIManager"));
            manager.UICanvas = f.Manager.UICanvas; manager.optionsMenuScreen = f.Screen;
        }
        if (problem == "wrong-text") {
            dynamic label = f.GameButton.transform.Find("Menu Button Text").gameObject;
            foreach (object text in ((IEnumerable)label.Components).Cast<object>().Where(c => c.GetType() == f.Type("UI.Text")).ToArray()) label.Components.Remove((dynamic)text);
            label.AddComponent(f.Type("UI.WrongText")).text = "NOT NATIVE UI TEXT";
        }
        f.BaselineObjects = f.Objects.Cast<object>().ToHashSet();
        f.Bind(); Assert.Null(f.Entry()); f.AssertNoActiveOwned();
        Assert.All(f.BaselineObjects, o => Assert.False((bool)((dynamic)o).Dead));
        Assert.All(f.OriginalRoots, o => { Assert.False((bool)((dynamic)o).Dead); Assert.True((bool)((dynamic)o).activeInHierarchy); });
        f.Static("World", "FlushDestroy"); f.AssertNoActiveOwned();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EveryCustomControlRetainsOneNativeSelectableAudioCursorAndNeutralEvents(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind();
        foreach (dynamic root in new object[] { f.Entry(), f.Entry(true), ((dynamic)f.Field("_modsScreen")).gameObject, ((dynamic)f.Field("_skinsScreen")).gameObject }) {
            foreach (dynamic go in f.Objects.Where(o => (bool)o.transform.IsChildOf(root.transform))) {
                object[] custom = ((IEnumerable)go.Components).Cast<object>().Where(c => c.GetType().Name.Contains("NativeMods") || c.GetType().Name.Contains("NativeSkinButton")).ToArray();
                if (custom.Length == 0) continue;
                object[] selectable = ((IEnumerable)go.Components).Cast<object>().Where(c => f.Type("UI.Selectable").IsInstanceOfType(c)).ToArray();
                Assert.Single(selectable); Assert.IsAssignableFrom(f.Type("UI.MenuButton"), selectable[0]);
                Assert.All(custom, c => Assert.False(f.Type("UI.Selectable").IsInstanceOfType(c)));
            }
        }
        f.Static("World", "Submit", f.GameButton.gameObject); Assert.Equal(1, f.Count("NativeGameActions"));
        int audio = f.Count("AudioSubmit"), cursor = f.Count("CursorSelect"), inheritedSubmit = f.Count("ForeignSubmitActions");
        dynamic entry = f.Field("_entrySelectable"); entry.Select(); f.Static("World", "Submit", entry.gameObject); f.Static("World", "Pump", 100);
        Assert.True(f.Count("AudioSubmit") > audio); Assert.True(f.Count("CursorSelect") > cursor);
        Assert.Equal(1, f.Count("NativeGameActions")); Assert.Equal(inheritedSubmit, f.Count("ForeignSubmitActions"));
        f.Static("World", "Cancel", f.Row(0).gameObject); f.Static("World", "Pump", 100);
        Assert.Equal(0, f.Count("ForeignCancelActions"));
    }
    [Theory]
    [InlineData(false, "screen")] [InlineData(true, "screen")]
    [InlineData(false, "manager")] [InlineData(true, "manager")]
    [InlineData(false, "session")] [InlineData(true, "session")]
    public void NativeAuthorityReplacementRetiresOldCallbacksAndRetriesBeforeAndAfterDestroy(bool ss, string replacement)
    {
        var f = new NativeMenuFixture(ss);
        dynamic replacementScreen = null;
        if (replacement == "screen") {
            replacementScreen = f.Static("Object", "Instantiate", f.Screen.gameObject, f.Manager.UICanvas.transform, false);
            replacementScreen.SetActive(false);
        }
        f.Bind(); f.Open();
        dynamic stale = f.Row(0); int writes = f.Store.Writes;
        if (replacement == "screen") {
            f.Manager.optionsMenuScreen = (dynamic)((IEnumerable)replacementScreen.Components).Cast<object>().Single(c => c.GetType() == f.Type("MenuScreen"));
            replacementScreen.SetActive(true);
        }
        if (replacement == "manager") {
            dynamic manager = f.Go("ReplacementUi").AddComponent(f.Type("UIManager"));
            manager.UICanvas = f.Manager.UICanvas; manager.optionsMenuScreen = f.Screen;
            dynamic trigger = ((IEnumerable)f.GameButton.gameObject.Components).Cast<object>().Single(c => c.GetType() == f.Type("EventSystems.EventTrigger"));
            foreach (dynamic entry in trigger.triggers)
                foreach (dynamic listener in entry.callback.Listeners) listener.Target = manager;
            f.Manager.gameObject.scene.isLoaded = false; f.Manager.Dead = true;
            NativeMenuFixture.SetStatic(f.Type("UIManager"), "instance", manager);
        }
        if (replacement == "session") {
            dynamic runtime = f.Static(ss ? "SilksongModsRuntime" : "HollowKnightModsRuntime", "Current");
            if (ss) {
                var api = DispatchProxy.Create<ISilksongTweakApi, MenuApiProxy>();
                var adapter = new SilksongTweakAdapter(api); ((MenuApiProxy)(object)api).Descriptors = adapter.Descriptors;
                runtime.Session = new TweakSession(() => true, () => adapter, f.Store, TweakMenuPresenterLayout.VisibleRows);
            }
            else runtime.Session = new HollowKnightModsSession(f.HkApi, f.Store, TweakMenuPresenterLayout.VisibleRows);
            runtime.Session.Tick();
        }
        f.Call("Update");
        int actions = f.Count("NativeGameActions"); f.Static("World", "Submit", stale.gameObject); f.Static("World", "Cancel", stale.gameObject);
        Assert.Equal(writes, f.Store.Writes); Assert.Equal(actions, f.Count("NativeGameActions"));
        Assert.False((bool)stale.gameObject.activeInHierarchy);
        f.Screen.gameObject.SetActive(true);
        f.Call("TryBind"); f.Call("Update"); Assert.NotNull(f.Entry());
        Assert.Equal(1, f.Objects.Count(o => o.name == "MODS" && (bool)o.activeInHierarchy));
        Assert.Equal(1, f.Objects.Count(o => o.name == "SKINS" && (bool)o.activeInHierarchy));
        f.Static("World", "FlushDestroy"); f.Call("Update"); Assert.NotNull(f.Entry());
        f.Call("CancelAndClearBinding");
    }
    [Theory]
    [InlineData(false, "hide")] [InlineData(true, "hide")]
    [InlineData(false, "show")] [InlineData(true, "show")]
    public void PauseLossDuringNativeTransitionsStopsAllRoutesAndCanReopen(bool ss, string boundary)
    {
        var f = new NativeMenuFixture(ss); f.Bind(); f.Call("OpenEntry", 0);
        if (boundary == "show") f.Static("World", "Pump", 1);
        f.Manager.uiState = (dynamic)Enum.Parse(f.Type("GlobalEnums.UIState"), "PLAYING"); f.Call("Update"); f.Static("World", "Pump", 100);
        Assert.False((bool)f.Field("_nativeOpen")); Assert.False(f.Transitioning); Assert.Null(f.Field("_transitionCoroutine"));
        Assert.False((bool)((dynamic)f.Field("_modsScreen")).gameObject.activeInHierarchy);
        Assert.False((bool)((dynamic)f.Field("_skinsScreen")).gameObject.activeInHierarchy);
        Assert.False(((dynamic)f.Field("_skinTransport")).Due(1000f));
        f.Manager.uiState = (dynamic)Enum.Parse(f.Type("GlobalEnums.UIState"), "PAUSED"); f.Call("Update");
        f.Screen.gameObject.SetActive(true); f.Open(); f.Call("Close"); f.Static("World", "Pump", 100); f.Open(true);
        f.Call("CancelAndClearBinding"); f.AssertNoActiveOwned();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompleteNativeTraversalAllCategoriesRowsDescriptionsResetBackSkinsAndRepeatedCycles(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind(); f.Open();
        var traversed = new List<string>();
        int groups = f.Menu.Groups.Count;
        for (int group = 0; group < groups; group++) {
            int count = f.Menu.CurrentRows.Count;
            for (int row = 0; row < count; row++) {
                f.Menu.MoveRow(row - f.Menu.SelectedRowIndex); f.Menu.DismissMessage(); f.Call("Paint");
                dynamic button = f.Row(); f.Call("Select", button);
                Assert.Equal(row, (int)button.DataIndex); Assert.True((bool)button.gameObject.activeInHierarchy);
                TweakDescriptor descriptor = f.Menu.Selected; traversed.Add(descriptor.Id);
                string description = f.NativeText(f.Field("_descriptionRoot"));
                Assert.Contains(descriptor.Description, description);
                dynamic direction = Enum.Parse(f.Type("EventSystems.MoveDirection"), "Right");
                f.Call("Move", button, direction); f.Call("Move", button, Enum.Parse(f.Type("EventSystems.MoveDirection"), "Left"));
            }
            f.Call("Submit", f.Button(TweakMenuPresenterLayout.GroupButtonIndex));
        }
        Assert.Equal(f.Controller.Descriptors.Count, traversed.Distinct().Count());
        f.Call("Submit", f.Button(TweakMenuPresenterLayout.ResetButtonIndex)); Assert.True(f.Menu.IsOpen);
        for (int cycle = 0; cycle < 3; cycle++) {
            f.Call("Submit", f.Button(TweakMenuPresenterLayout.BackButtonIndex)); f.Static("World", "Pump", 100);
            Assert.False((bool)f.Field("_nativeOpen")); f.SkinSnapshot(); f.Open(true);
            Assert.True((bool)((dynamic)f.Field("_skinsScreen")).gameObject.activeInHierarchy);
            var skinMenu = (DualSouls.Skins.NativeSkinMenuModel)f.Field("_skinMenu"); Assert.NotNull(skinMenu);
            foreach (string kind in new[] { "Mode", "Sprites", "Skin", "Back" }) {
                dynamic skinButton = ((IEnumerable)f.Field("_skinButtons")).Cast<dynamic>().Single(b => b.DataIndex >= 0 && skinMenu.Rows[(int)b.DataIndex].Kind.ToString() == kind);
                f.Call("SelectSkin", skinButton);
                Assert.False(string.IsNullOrWhiteSpace(f.NativeText(f.Field("_skinsDescriptionRoot"))));
                f.Call("SubmitSkin", skinButton);
            }
            f.Static("World", "Pump", 100); f.Open();
            f.Call("TryBind"); Assert.Equal(1, f.Objects.Count(o => o.name == "MODS" && (bool)o.activeInHierarchy == false && !(bool)o.Dead && !((IEnumerable)f.Static("World", "DestroyQueue")).Cast<object>().Contains((object)o)));
        }
        f.Receipt("traversal", new { categories = groups, descriptors = traversed, repeatedCycles = 3 });
        f.Call("CancelAndClearBinding"); f.AssertNoActiveOwned(); f.Static("World", "FlushDestroy"); f.AssertNoActiveOwned();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClosingPendingSkinsTransportLeavesOrdinaryHealthyFramesBridgeIdle(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind(); f.SkinSnapshot("PENDING"); f.Open(true);
        Assert.True(((dynamic)f.Field("_skinTransport")).Pending);
        f.Call("Close"); f.Static("World", "Pump", 100);
        Assert.False(((dynamic)f.Field("_skinTransport")).Pending);
        int calls = ((IList)f.Static("World", "BridgeCalls")).Count;
        for (int frame = 0; frame < 100; frame++) {
            NativeMenuFixture.SetStatic(f.Type("Time"), "unscaledTime", frame * 0.1f);
            f.Call("Update");
        }
        Assert.Equal(calls, ((IList)f.Static("World", "BridgeCalls")).Count);
        Assert.Equal(0, f.Count("BenchWrites"));
    }
    [Fact]
    public void SilksongAcceptsExactSerializedSubmitAuthorityWithoutInheritedTrigger()
    {
        var f = new NativeMenuFixture(true);
        var button = (S.UI.MenuButton)f.GameButton;
        button.gameObject.GetComponent<S.EventSystems.EventTrigger>().triggers.Clear();
        button.OnSubmitPressed = new S.Events.UnityEvent();
        button.OnSubmitPressed.Listeners.Add(new S.Events.UnityEvent.Listener { Target = (S.UIManager)f.Manager, Method = "UIGoToGameOptionsMenu" });
        f.Bind(); Assert.NotNull(f.Entry()); f.Static("World", "Submit", button.gameObject);
        Assert.Equal(1, f.Count("NativeGameActions"));
        dynamic custom = f.Field("_entrySelectable"); f.Static("World", "Submit", custom.gameObject); f.Static("World", "Pump", 100);
        Assert.Equal(1, f.Count("NativeGameActions")); Assert.True((bool)f.Field("_nativeOpen"));
    }
    static dynamic OpenThroughOwnedEntry(NativeMenuFixture f, bool skins)
    {
        f.SkinSnapshot("PENDING");
        dynamic entry = f.Field(skins ? "_skinsEntrySelectable" : "_entrySelectable");
        entry.Select();
        f.Static("World", "Submit", entry.gameObject);
        f.Static("World", "Pump", 100);
        Assert.True((bool)f.Field("_nativeOpen"));
        Assert.Null(f.Field("_selectionBeforeOpen"));
        dynamic row = skins ? ((IList)f.Field("_skinButtons"))[0] : f.Row(0);
        row.Selectable.Select();
        Assert.Same((object)row.gameObject, (object)f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject);
        return row;
    }
    static dynamic PrepareOriginalReplacement(NativeMenuFixture f)
    {
        dynamic replacement = f.Static("Object", "Instantiate", f.Screen.gameObject, f.Manager.UICanvas.transform, false);
        replacement.SetActive(false);
        f.BaselineObjects = f.Objects.Cast<object>().ToHashSet();
        return replacement;
    }
    static void DestroyNativeOriginal(NativeMenuFixture f, string original)
    {
        dynamic target = original == "screen" ? f.Screen.gameObject : f.GameButton;
        f.Static("Object", "Destroy", target);
        f.Static("World", "FlushDestroy");
        Assert.True((bool)f.GameButton.Dead);
        Assert.False((bool)f.Manager.UICanvas.Dead);
    }
    static void AssertOriginalDestructionCleanup(NativeMenuFixture f)
    {
        Assert.DoesNotContain(f.OwnedRoots, o => (bool)o.activeInHierarchy);
        Assert.False((bool)f.Field("_nativeOpen")); Assert.False(f.Transitioning);
        Assert.Null(f.Field("_transitionCoroutine"));
        object lifecycle = f.Ss ? f.Field("_lifecycle") : null;
        Assert.Null(f.Ss ? lifecycle.GetType().GetProperty("Current").GetValue(lifecycle) : f.Field("_binding"));
        Assert.False(((dynamic)f.Field("_skinTransport")).Pending);
        Assert.False(((dynamic)f.Field("_skinTransport")).Due(1000f));
        var queued = ((IEnumerable)f.Static("World", "DestroyQueue")).Cast<object>().ToHashSet();
        foreach (dynamic go in f.Objects.Where(o => !f.BaselineObjects.Contains((object)o) && !(bool)o.Dead)) {
            Assert.False((bool)go.activeInHierarchy, "active allocated clone: " + (string)go.name);
            dynamic ancestor = go.transform; bool covered = false;
            while (ancestor != null) { if (queued.Contains((object)ancestor.gameObject)) { covered = true; break; } ancestor = ancestor.parent; }
            Assert.True(covered, "unowned allocated clone: " + (string)go.name);
        }
        // Destroyed native controls cannot be restored and their managed cached
        // navigation is no authority. Every surviving original is still exact.
        for (int i = 0; i < f.OriginalButtons.Count; i++)
            if (!(bool)((dynamic)f.OriginalButtons[i]).Dead)
                Assert.Equal(f.OriginalNavigation[i], ((dynamic)f.OriginalButtons[i]).navigation);
        dynamic selected = f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject;
        if (selected != null) Assert.True(f.BaselineObjects.Contains((object)selected));
        if (!f.Ss) Assert.Null(NativeMenuFixture.GetStatic(typeof(H.HkStageHooks), "pendingBenchCompletion"));
    }
    static HashSet<object> OldSelectables(NativeMenuFixture f)
    {
        return f.Objects.Where(o => !f.BaselineObjects.Contains((object)o))
            .SelectMany(o => ((IEnumerable)o.Components).Cast<object>())
            .Where(c => f.Type("UI.Selectable").IsInstanceOfType(c)).ToHashSet();
    }
    static void AssertSuccessorAuthorityAndNavigation(NativeMenuFixture f, dynamic replacement, HashSet<object> oldSelectables)
    {
        Assert.NotNull(f.Entry());
        Assert.Equal(1, f.Objects.Count(o => !(bool)o.Dead && o.name == "MODS" && (bool)o.activeInHierarchy));
        Assert.Equal(1, f.Objects.Count(o => !(bool)o.Dead && o.name == "SKINS" && (bool)o.activeInHierarchy));
        var nativeButtons = f.Objects.Where(o => !(bool)o.Dead && (bool)o.transform.IsChildOf(replacement.transform))
            .SelectMany(o => ((IEnumerable)o.Components).Cast<object>())
            .Where(c => c.GetType() == f.Type("UI.MenuButton") && !(bool)((dynamic)c).Dead).ToArray();
        int gameOptions = 0;
        foreach (dynamic button in nativeButtons) {
            dynamic trigger = ((IEnumerable)button.gameObject.Components).Cast<object>().FirstOrDefault(c => c.GetType() == f.Type("EventSystems.EventTrigger"));
            if (trigger != null)
                foreach (dynamic entry in trigger.triggers)
                    foreach (dynamic listener in entry.callback.Listeners)
                        if (ReferenceEquals((object)listener.Target, (object)f.Manager) && listener.Method == "UIGoToGameOptionsMenu") gameOptions++;
            dynamic nav = button.navigation;
            foreach (object target in new object[] { nav.selectOnUp, nav.selectOnDown, nav.selectOnLeft, nav.selectOnRight })
                Assert.DoesNotContain(target, oldSelectables);
        }
        Assert.Equal(1, gameOptions);
        dynamic selected = f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject;
        if (selected != null)
            Assert.DoesNotContain((object)selected, oldSelectables.Select(c => (object)((dynamic)c).NativeGameObject));
    }
    static void ReplayRetiredDriver(NativeMenuFixture f, object stale, bool skins)
    {
        // Invoke the retained native callback directly, including after deferred
        // destruction. Driver lifetime/generation guards are production bodies.
        if (f.Ss) {
            ((S.EventSystems.ISubmitHandler)stale).OnSubmit(new S.EventSystems.BaseEventData());
            ((S.EventSystems.ICancelHandler)stale).OnCancel(new S.EventSystems.BaseEventData());
        }
        else {
            ((H.EventSystems.ISubmitHandler)stale).OnSubmit(new H.EventSystems.BaseEventData());
            ((H.EventSystems.ICancelHandler)stale).OnCancel(new H.EventSystems.BaseEventData());
        }
    }
    [Theory]
    [InlineData(false, "screen")] [InlineData(true, "screen")]
    [InlineData(false, "button")] [InlineData(true, "button")]
    public void DestroyedNativeComponentGetterControlIsNotAnEngineCleanupError(bool ss, string original)
    {
        var f = new NativeMenuFixture(ss); f.Bind();
        dynamic stale = OpenThroughOwnedEntry(f, false);
        object liveButtonGo = f.GameButton.gameObject;
        dynamic target = original == "screen" ? f.Screen.gameObject : f.GameButton;
        f.Static("Object", "Destroy", target);
        Assert.Same(liveButtonGo, (object)f.GameButton.gameObject);
        // Engine teardown must complete without touching a dead public getter.
        f.Static("World", "FlushDestroy");
        Assert.True((bool)f.GameButton.Dead);
        Exception getter = Record.Exception(() => { object ignored = f.GameButton.gameObject; });
        Assert.NotNull(getter); Assert.Equal(f.Type("MissingReferenceException"), getter.GetType());
        Assert.Contains("Component.gameObject: native MenuButton has been destroyed.", getter.Message);
        Assert.Contains("get_gameObject", getter.StackTrace);
        Assert.DoesNotContain("DestroyNow", getter.StackTrace);
        Assert.False((bool)f.Manager.UICanvas.Dead);
        Assert.True((bool)stale.gameObject.activeInHierarchy);
        Assert.Same((object)stale.gameObject, (object)f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject);
        f.Receipt("native-getter-control-" + original, new { original, deferredGetterAlive = true, flushCompleted = true, getterType = getter.GetType().FullName, getter.Message, getter.StackTrace, ownedSubmenuSurvives = true });
    }
    [Theory]
    [InlineData(false, false, "screen")] [InlineData(true, false, "screen")]
    [InlineData(false, true, "screen")] [InlineData(true, true, "screen")]
    [InlineData(false, false, "button")] [InlineData(true, false, "button")]
    [InlineData(false, true, "button")] [InlineData(true, true, "button")]
    public void NativeOriginalSelectionDestructionCleansAndAutomaticallyRebindsBeforeAndAfterCloneFlush(bool ss, bool skins, string original)
    {
        var f = new NativeMenuFixture(ss); dynamic replacement = PrepareOriginalReplacement(f);
        f.Bind(); object stale = OpenThroughOwnedEntry(f, skins);
        var oldRoots = f.OwnedRoots.Cast<object>().ToArray(); var oldSelectables = OldSelectables(f);
        int writes = f.Store.Writes;
        DestroyNativeOriginal(f, original);
        // No fixture orchestration substitutes for normal production Update.
        f.Tick();
        AssertOriginalDestructionCleanup(f); Assert.Empty((IEnumerable)f.Field("_ownedRoots"));
        Assert.Null((object)f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject);
        Assert.False(((dynamic)f.Field("_skinTransport")).Pending);
        Assert.All(oldSelectables, c => Assert.False((bool)((dynamic)c).isActiveAndEnabled));
        Assert.All(oldRoots, r => Assert.False((bool)((dynamic)r).activeInHierarchy));
        ReplayRetiredDriver(f, stale, skins);
        Assert.Equal(writes, f.Store.Writes);
        f.Manager.optionsMenuScreen = (dynamic)((IEnumerable)replacement.Components).Cast<object>().Single(c => c.GetType() == f.Type("MenuScreen"));
        replacement.SetActive(true); NativeMenuFixture.SetStatic(f.Type("Time"), "unscaledTime", 0.6f);
        f.Tick(); f.Tick(); AssertSuccessorAuthorityAndNavigation(f, replacement, oldSelectables);
        ReplayRetiredDriver(f, stale, skins); Assert.Equal(writes, f.Store.Writes);
        f.Static("World", "FlushDestroy");
        f.Tick(); AssertSuccessorAuthorityAndNavigation(f, replacement, oldSelectables);
        ReplayRetiredDriver(f, stale, skins); Assert.Equal(writes, f.Store.Writes);
        Assert.Equal(0, f.Count("NativeGameActions")); Assert.Equal(0, f.Count("ForeignCancelActions"));
        f.Receipt("native-original-destruction-" + original + "-" + skins, new { original, skins, cleanupCompleted = true, retiredSelectionCleared = true, staleCallbacksRejected = true, rebindBeforeAndAfterCloneFlush = true, gameOptions = 1, mods = 1, skinsEntries = 1 });
        f.Call("CancelAndClearBinding"); AssertOriginalDestructionCleanup(f); f.Static("World", "FlushDestroy"); AssertOriginalDestructionCleanup(f);
    }
    [Theory]
    [InlineData(false, false, "native-alive")] [InlineData(true, false, "native-alive")]
    [InlineData(false, true, "native-alive")] [InlineData(true, true, "native-alive")]
    [InlineData(false, false, "foreign-before")] [InlineData(true, false, "foreign-before")]
    [InlineData(false, true, "foreign-before")] [InlineData(true, true, "foreign-before")]
    [InlineData(false, false, "foreign-current")] [InlineData(true, false, "foreign-current")]
    [InlineData(false, true, "foreign-current")] [InlineData(true, true, "foreign-current")]
    public void NativeOriginalSelectionRestorationKeepsAliveFallbackAndNeverStealsForeignSelection(bool ss, bool skins, string selection)
    {
        var f = new NativeMenuFixture(ss); dynamic foreign = f.Go("ForeignSelectable").AddComponent(f.Type("UI.MenuButton"));
        f.BaselineObjects = f.Objects.Cast<object>().ToHashSet(); f.Bind(); f.SkinSnapshot();
        dynamic events = f.Static("EventSystems.EventSystem", "current");
        if (selection == "foreign-before") {
            foreign.Select(); f.Open(skins);
            Assert.Same((object)foreign.gameObject, f.Field("_selectionBeforeOpen"));
        }
        else OpenThroughOwnedEntry(f, skins);
        if (selection == "native-alive") {
            f.Screen.gameObject.SetActive(true);
            f.Call("CancelAndClearBinding");
        }
        else {
            if (selection == "foreign-current") foreign.Select();
            DestroyNativeOriginal(f, "screen"); f.Tick();
        }
        Assert.Same(selection == "native-alive" ? (object)f.GameButton.gameObject : (object)foreign.gameObject, (object)events.currentSelectedGameObject);
        AssertOriginalDestructionCleanup(f); f.Static("World", "FlushDestroy");
        Assert.Same(selection == "native-alive" ? (object)f.GameButton.gameObject : (object)foreign.gameObject, (object)events.currentSelectedGameObject);
    }
    [Theory]
    [InlineData("screen", false)] [InlineData("button", false)]
    [InlineData("screen", true)] [InlineData("button", true)]
    public void NativeOriginalSelectionDestructionRetiresPendingUpperBenchExactlyOnceAndCannotAffectSuccessor(string original, bool reset)
    {
        var f = new NativeMenuFixture(false); dynamic replacement = PrepareOriginalReplacement(f);
        f.Record("Room_A"); f.Bind(); OpenThroughOwnedEntry(f, false);
        f.SelectTweak("bench_teleport"); f.Controller.ClearOperationEvidence(); f.Call("Submit", f.Row());
        long oldToken = (long)f.Field("_benchOperationToken"); int oldGeneration = (int)f.Field("_benchGeneration");
        Assert.True(H.HkStageHooks.BenchOperationMatches(oldToken, oldGeneration));
        object stale = f.Row(0); var oldSelectables = OldSelectables(f);
        var queue = (Queue<TweakAdapterCompletion>)f.HkApi.GetType().GetField("_completedOperations", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.HkApi);
        DestroyNativeOriginal(f, original);
        if (reset) f.Menu.Reset();
        f.Call("Update"); AssertOriginalDestructionCleanup(f);
        var completion = Assert.Single(queue); Assert.Equal(oldToken, completion.OperationToken); Assert.False(completion.Result.Success);
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", oldToken, oldGeneration));
        H.HkStageHooks.CancelBenchChoice(oldToken, oldGeneration, "stale predecessor");
        f.Call("Update"); Assert.Single(queue);
        Assert.Equal(0, H.GameManager.UnsafeInstance.playerData.RespawnCalls);
        f.Tick(); Assert.Empty(queue);
        var evidence = Assert.Single(f.Controller.OperationEvidence, e => e.RowId == "bench_teleport"); Assert.False(evidence.Success);
        f.Manager.optionsMenuScreen = (dynamic)((IEnumerable)replacement.Components).Cast<object>().Single(c => c.GetType() == f.Type("MenuScreen"));
        replacement.SetActive(true); H.Time.unscaledTime = 0.6f; f.Tick(); f.Tick();
        AssertSuccessorAuthorityAndNavigation(f, replacement, oldSelectables);
        OpenThroughOwnedEntry(f, false); f.SelectTweak("bench_teleport"); f.Controller.ClearOperationEvidence(); f.Call("Submit", f.Row());
        long token = (long)f.Field("_benchOperationToken"); int generation = (int)f.Field("_benchGeneration");
        Assert.NotEqual(oldToken, token);
        H.HkStageHooks.CancelBenchChoice(oldToken, oldGeneration, "late original close");
        H.HkStageHooks.CancelBenchTeleport(oldToken, oldGeneration);
        H.HollowKnightNativeModsMenu.RetireBenchTeleportRoute(oldToken, oldGeneration);
        Assert.False(H.HkStageHooks.BenchWarp("Room_A", oldToken, oldGeneration)); ReplayRetiredDriver(f, stale, false);
        Assert.True(H.HkStageHooks.BenchOperationMatches(token, generation)); Assert.Empty(queue);
        f.Static("World", "FlushDestroy"); ReplayRetiredDriver(f, stale, false);
        Assert.True(H.HkStageHooks.BenchOperationMatches(token, generation)); Assert.Empty(queue);
        f.Call("Submit", f.Row(0)); Assert.Equal(token, Assert.Single(queue).OperationToken);
        f.Tick(); Assert.Empty(queue);
        evidence = Assert.Single(f.Controller.OperationEvidence, e => e.RowId == "bench_teleport"); Assert.True(evidence.Success);
        Assert.Equal(1, H.GameManager.UnsafeInstance.playerData.RespawnCalls); Assert.Equal(1, H.GameManager.UnsafeInstance.ReadyCalls);
        Assert.Equal(0, H.World.BenchWrites);
        f.Receipt("native-original-bench-" + original + "-" + reset, new { original, reset, predecessorToken = oldToken, predecessorGeneration = oldGeneration, terminalExactlyOnce = true, successorToken = token, successorGeneration = generation, staleTupleRejected = true, typedRespawn = 1, typedReady = 1 });
        f.Call("CancelAndClearBinding"); f.Static("World", "FlushDestroy");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RejectsForeignDirectModsEntryWithoutDeletingNativeControls(bool ss)
    {
        var f = new NativeMenuFixture(ss);
        dynamic foreign = f.Go("MODS", f.Content.transform);
        foreign.transform.anchoredPosition = (dynamic)Activator.CreateInstance(f.Type("Vector2"), 0f, -900f);
        foreign.AddComponent(f.Type("UI.MenuButton")); f.Bind();
        Assert.Null(f.Entry()); Assert.All(f.OriginalRoots, o => Assert.False((bool)((dynamic)o).Dead));
    }
}
