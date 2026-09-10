using Xunit;
using DualSouls.DualScreen;

public sealed class SilksongHudRoutingTests
{
    sealed class Node
    {
        public Node Parent;
        public readonly List<Node> Children = new();
        public DsHudPose Pose = PoseAt(3);
        public int Layer = 9;
        public bool Alive = true, Active = true;
        public string Color = "native", Driver = Guid.NewGuid().ToString();
    }

    sealed class Nodes : IDsHudNodes
    {
        public Node FailParent;
        public readonly List<(Node Template, Node Instance)> Clones = new();
        public IEnumerable<DsHudClone> NativeClones(IEnumerable<object> roots) =>
            Clones.Select(x => new DsHudClone(x.Template, x.Instance));
        public bool Alive(object n) => n is Node node && node.Alive;
        public bool ActiveInHierarchy(object n) => n is Node node && node.Alive && node.Active &&
            (node.Parent == null || ActiveInHierarchy(node.Parent));
        public object Parent(object n) => ((Node)n).Parent;
        public int Sibling(object n) => ((Node)n).Parent?.Children.IndexOf((Node)n) ?? 0;
        public DsHudPose Pose(object n) => ((Node)n).Pose;
        public int Layer(object n) => ((Node)n).Layer;
        public IEnumerable<object> Children(object n) => ((Node)n).Children.ToArray();
        public void SetParent(object n, object parent)
        {
            if (ReferenceEquals(n, FailParent)) throw new InvalidOperationException("owned test parent fault");
            Attach((Node)n, (Node)parent);
        }
        public void SetSibling(object n, int index)
        {
            var node = (Node)n; var list = node.Parent?.Children;
            if (list == null) return;
            list.Remove(node); list.Insert(Math.Min(index, list.Count), node);
        }
        public void SetPose(object n, DsHudPose pose) => ((Node)n).Pose = pose;
        public void SetLayer(object n, int layer) => ((Node)n).Layer = layer;
    }

    sealed class Rig
    {
        public readonly Nodes Nodes = new();
        public readonly Node Vanilla = new(), Hud = new();
        public readonly object Owner = new();
        public readonly DsPortHudState State;
        public readonly DsHudRoute[] Routes;
        public Rig()
        {
            State = new DsPortHudState(Nodes);
            Routes = Enum.GetValues<DsHudRole>().Select((role, i) =>
            {
                var n = new Node { Pose = PoseAt(i + 1), Layer = i + 8 };
                Attach(n, Vanilla);
                return new DsHudRoute(role.ToString(), role, n, PoseAt(100 + i));
            }).ToArray();
        }
        public bool Bind(DsHudEligibility? eligibility = null) =>
            State.Bind(Owner, Routes, Hud, 6, eligibility ?? Eligible());
        public Node Root(int i = 0) => (Node)Routes[i].Node;
    }

    static DsHudPose PoseAt(float x) => new DsHudPose(x, x + 1, x + 2, 0, 0, 0, 1, 2, 3, 4);
    static DsHudEligibility Eligible() => new DsHudEligibility(true, true, true, false, false, false);
    static void Attach(Node n, Node parent)
    {
        n.Parent?.Children.Remove(n); n.Parent = parent; parent?.Children.Add(n);
    }

    static Node CloneNative(Node source, Node parent)
    {
        var copy = new Node { Layer = source.Layer, Pose = source.Pose, Active = source.Active, Color = source.Color };
        Attach(copy, parent);
        foreach (var child in source.Children) CloneNative(child, copy);
        return copy;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Routed_mixed_layer_template_clones_restore_native_layers_even_before_late_tick(bool lateTick)
    {
        var r = new Rig(); var template = new Node { Layer = 13, Active = false };
        var child = new Node { Layer = 17 }; var grandchild = new Node { Layer = 21 };
        Attach(template, r.Root(5)); Attach(child, template); Attach(grandchild, child);
        Assert.True(r.Bind()); Assert.Equal(6, template.Layer); Assert.Equal(6, grandchild.Layer);
        var clone = CloneNative(template, r.Root(5));
        r.Nodes.Clones.Add((template, clone));
        Assert.Equal(6, clone.Children[0].Children[0].Layer);
        if (lateTick) r.State.LateTick(Eligible());
        r.State.Restore();
        Assert.Equal(13, clone.Layer); Assert.Equal(17, clone.Children[0].Layer);
        Assert.Equal(21, clone.Children[0].Children[0].Layer);
        Assert.Equal(13, template.Layer); Assert.Equal(17, child.Layer); Assert.Equal(21, grandchild.Layer);
        Assert.False(clone.Active); Assert.Same(r.Root(5), clone.Parent);
        Assert.Same(r.Vanilla, r.Root(5).Parent);
    }

    [Fact]
    public void Known_clone_survivors_release_after_destroyed_clone_child_on_repeated_restore()
    {
        var r = new Rig(); var template = new Node { Layer = 13, Active = false };
        var child = new Node { Layer = 17 }; var grandchild = new Node { Layer = 21 };
        var sibling = new Node { Layer = 29 };
        Attach(template, r.Root(5)); Attach(child, template); Attach(grandchild, child); Attach(sibling, template);
        Assert.True(r.Bind());
        var clone = CloneNative(template, r.Root(5)); r.Nodes.Clones.Add((template, clone));
        r.State.LateTick(Eligible()); // All clone originals are recorded before native destruction.
        var survivor = clone.Children[1]; var destroyed = clone.Children[0];
        DestroyTree(destroyed); Attach(destroyed, null);
        Assert.Equal(6, clone.Layer); Assert.Equal(6, survivor.Layer);
        int releases = 0; var errors = new List<Exception>();
        for (int attempt = 0; attempt < 2; attempt++)
            errors.Add(Record.Exception(() => r.State.RestoreBefore(() =>
            {
                Assert.False(r.State.IsBound);
                Assert.Same(r.Vanilla, r.Root(5).Parent); Assert.Equal(PoseAt(6), r.Root(5).Pose);
                Assert.Equal(13, clone.Layer); Assert.Equal(29, survivor.Layer);
                Assert.True(clone.Alive); Assert.True(survivor.Alive); Assert.False(clone.Active);
                DestroyTree(r.Hud); releases++;
            })));
        Assert.All(errors, error => Assert.Null(error));
        Assert.Equal(2, releases); Assert.False(r.State.IsBound); Assert.False(r.Hud.Alive);
        Assert.False(destroyed.Alive); Assert.True(clone.Alive); Assert.True(survivor.Alive);
        Assert.Equal(13, template.Layer); Assert.Equal(17, child.Layer);
        Assert.Equal(21, grandchild.Layer); Assert.Equal(29, sibling.Layer);
        Assert.Same(clone, survivor.Parent); Assert.Same(r.Root(5), clone.Parent);
    }

    [Fact]
    public void Known_clone_does_not_authorize_unknown_private_layer_descendants()
    {
        var r = new Rig(); var template = new Node { Layer = 13 }; var child = new Node { Layer = 17 };
        Attach(template, r.Root(5)); Attach(child, template); Assert.True(r.Bind());
        var clone = CloneNative(template, r.Root(5)); r.Nodes.Clones.Add((template, clone));
        r.State.LateTick(Eligible());
        var unknown = new Node { Layer = 6 }; Attach(unknown, clone);
        bool released = false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.Throws<AggregateException>(() => r.State.RestoreBefore(() => released = true));
            Assert.False(released); Assert.True(r.State.IsBound); Assert.Equal(6, unknown.Layer);
            Assert.Equal(13, clone.Layer); Assert.Equal(17, clone.Children[0].Layer);
        }
        DestroyTree(unknown); Attach(unknown, null);
        r.State.RestoreBefore(() => released = true);
        Assert.True(released); Assert.False(r.State.IsBound);
    }

    [Theory]
    [InlineData("state", false)]
    [InlineData("pause", false)]
    [InlineData("unload", false)]
    [InlineData("completion", false)]
    [InlineData("state", true)]
    [InlineData("pause", true)]
    [InlineData("unload", true)]
    [InlineData("completion", true)]
    public void Manager_callbacks_reject_stale_captured_owner_before_and_after_rebind(string kind, bool rebind)
    {
        object oldOwner = new object(), next = new object(), current = oldOwner;
        int restores = 0, rearms = 0;
        var callbacks = new DsHudManagerCallbacks(() => current, () => restores++, () => restores++, () => rearms++);
        var old = callbacks.Bind(oldOwner, false);
        old.Unloading(); Assert.True(callbacks.TransitionPending); Assert.Equal(1, restores);
        current = next; // replacement has happened, even if V2.Update has not bound it yet
        if (rebind) callbacks.Bind(next, true);
        int expectedRestores = restores, expectedRearms = rearms;
        if (kind == "state") old.State(false, false);
        if (kind == "pause") old.Pause(true);
        if (kind == "unload") old.Unloading();
        if (kind == "completion") old.Finished();
        Assert.Equal(expectedRestores, restores); Assert.Equal(expectedRearms, rearms);
        Assert.True(callbacks.TransitionPending);
    }

    [Fact]
    public void Current_manager_completion_only_rearms_pending_boundary_and_still_requires_native_readiness()
    {
        var r = new Rig(); object current = new object(); int restores = 0, rearms = 0;
        var callbacks = new DsHudManagerCallbacks(() => current, () => restores++, () => restores++, () => rearms++);
        var live = callbacks.Bind(current, true);
        Assert.True(callbacks.TransitionPending); Assert.Equal(1, restores);
        live.Finished(); Assert.False(callbacks.TransitionPending); Assert.Equal(1, rearms);
        Assert.False(r.State.IsBound);
        Assert.False(r.Bind(new DsHudEligibility(true, true, true, false, false, true)));
        Assert.True(r.Bind()); // only subsequent native-ready sampling permits routing
        live.Finished(); Assert.Equal(1, rearms);
        live.Pause(true); Assert.Equal(2, restores);
        live.State(true, false); Assert.True(callbacks.TransitionPending); Assert.Equal(3, restores);
        callbacks.Unbind(); int count = restores;
        live.Unloading(); live.Pause(true); live.State(false, false); live.Finished();
        Assert.Equal(count, restores); Assert.True(callbacks.TransitionPending);
    }

    sealed class NativeHudContent : IDirectDisplayContent
    {
        readonly Rig _rig;
        public NativeHudContent(Rig rig) { _rig = rig; }
        public void SetTransportActive(bool active) { if (!active) _rig.State.Restore(); }
        public void OnPanelGeometry(float width, float height) { }
        public void Dispose() { _rig.State.Restore(); }
    }

    static void DestroyTree(Node node)
    {
        node.Alive = false;
        foreach (var child in node.Children) DestroyTree(child);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Faulted_outer_host_disposal_retains_native_survivors_and_live_retry_after_V2_destruction(bool destroyV2)
    {
        var r = new Rig(); var v2 = new Node(); var releaseOwner = new Node();
        // Production places the presentation under its independent DDOL release
        // owner, not under V2. The fake models Unity's recursive parent destruction.
        Attach(r.Hud, releaseOwner);
        var content = new NativeHudContent(r); bool retired = false;
        var release = new DsHudReleaseState(r.State.Restore, content.Dispose,
            () => DestroyTree(r.Hud), () => retired = true);
        Action retryOwnerUpdate = () => release.Retry();
        var host = new DirectDisplayHost(() => { }, _ => { }, _ => { }, release.ReleasePresentation);
        host.AttachContent(content); host.SetDisplayPresent(true); host.SetPresentationReady(true, 1240, 1080);
        Assert.True(host.IsActive); Assert.True(release.CanRoute); Assert.True(r.Bind());
        r.Nodes.FailParent = r.Root();
        release.RequestShutdown(host.Dispose); // ACTUAL shared best-effort Dispose reaches release despite failures
        if (destroyV2) DestroyTree(v2);
        Assert.True(host.IsDisposed); Assert.NotNull(release.LastFailure);
        Assert.True(r.Hud.Alive); Assert.True(r.Root().Alive); Assert.False(retired);
        Assert.True(release.Pending); Assert.True(release.BlocksReplacement); Assert.False(release.CanRoute);
        Assert.Same(r.Vanilla, r.Root(1).Parent); Assert.Same(r.Hud, r.Root().Parent);
        host.Dispose(); host.SetEnabled(true); Assert.False(host.IsActive); // disposed host cannot perform the retry
        retryOwnerUpdate(); Assert.True(release.Pending); Assert.True(r.Hud.Alive);
        r.Nodes.FailParent = null;
        retryOwnerUpdate(); // still reachable even after V2's Unity object has gone
        Assert.True(release.Completed); Assert.False(release.Pending); Assert.False(release.BlocksReplacement);
        Assert.False(release.CanRoute); Assert.True(retired); Assert.False(r.Hud.Alive);
        Assert.All(r.Routes, route =>
        {
            Assert.True(((Node)route.Node).Alive); Assert.Same(r.Vanilla, ((Node)route.Node).Parent);
        });
        Assert.Equal(PoseAt(1), r.Root().Pose); Assert.Equal(8, r.Root().Layer);
        retryOwnerUpdate(); Assert.True(r.Root().Alive);
    }

    [Fact]
    public void Routes_same_native_objects_and_drivers_without_touching_active_or_color()
    {
        var r = new Rig(); var root = r.Root(); root.Active = false;
        var driver = root.Driver; var child = new Node(); Attach(child, root);
        var childPose = child.Pose;
        Assert.True(r.Bind());
        Assert.Same(root, r.Hud.Children[0]); Assert.Equal(driver, root.Driver);
        Assert.False(root.Active); Assert.Equal("native", root.Color);
        Assert.Equal(r.Routes[0].Target, root.Pose); Assert.Equal(childPose, child.Pose);
        Assert.Equal(6, child.Layer);
    }

    [Theory]
    [InlineData(false, true, true, false, false, false)] // full off / display loss
    [InlineData(true, false, true, false, false, false)] // frame unavailable
    [InlineData(true, true, false, false, false, false)] // not gameplay
    [InlineData(true, true, true, true, false, false)] // native pause
    [InlineData(true, true, true, false, true, false)] // native inventory
    [InlineData(true, true, true, false, false, true)] // transition
    public void Native_eligibility_restores_even_while_gameplay_flag_is_true(
        bool transport, bool frame, bool gameplay, bool pause, bool inventory, bool transition)
    {
        var r = new Rig(); Assert.True(r.Bind());
        r.State.LateTick(new DsHudEligibility(transport, frame, gameplay, pause, inventory, transition));
        Assert.False(r.State.IsBound);
        Assert.All(r.Routes, route => Assert.Same(r.Vanilla, ((Node)route.Node).Parent));
        Assert.Equal(PoseAt(1), r.Root().Pose); Assert.Equal(8, r.Root().Layer);
    }

    [Fact]
    public void Child_spawn_and_driver_reparent_do_not_replace_original_routing_snapshot()
    {
        var r = new Rig(); Assert.True(r.Bind()); var n = r.Root();
        var child = new Node { Layer = 13 }; Attach(child, n);
        Attach(n, r.Vanilla); n.Pose = PoseAt(777);
        r.State.LateTick(Eligible());
        Assert.Same(r.Hud, n.Parent); Assert.Equal(r.Routes[0].Target, n.Pose);
        Assert.Equal(6, child.Layer);
        child.Pose = PoseAt(999); child.Color = "updated";
        r.State.Restore(); r.State.Restore();
        Assert.Equal(13, child.Layer); Assert.Equal(PoseAt(999), child.Pose);
        Assert.Equal("updated", child.Color); Assert.Equal(PoseAt(1), n.Pose);
        Assert.Equal(0, r.Vanilla.Children.IndexOf(n));
    }

    [Fact]
    public void Same_scene_owner_replacement_restores_old_before_binding_new()
    {
        var old = new Rig(); var next = new Rig(); Assert.True(old.Bind());
        Assert.True(old.State.Bind(next.Owner, next.Routes, old.Hud, 6, Eligible()));
        Assert.Same(old.Vanilla, old.Root().Parent);
        Assert.Same(old.Hud, next.Root().Parent);
        old.State.Restore(); Assert.Same(next.Vanilla, next.Root().Parent);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-role")]
    [InlineData("duplicate-node")]
    [InlineData("nested-root")]
    public void Invalid_essential_sources_never_partially_route_and_later_valid_retry_works(string invalid)
    {
        var r = new Rig(); var routes = r.Routes.ToList();
        if (invalid == "missing") routes.RemoveAt(0);
        if (invalid == "duplicate-role") routes.Add(new DsHudRoute("duplicate", DsHudRole.Health, new Node(), PoseAt(50)));
        if (invalid == "duplicate-node") routes[1] = new DsHudRoute("other", DsHudRole.Silk, r.Root(), PoseAt(50));
        if (invalid == "nested-root") Attach(r.Root(1), r.Root());
        Assert.False(r.State.Bind(r.Owner, routes.ToArray(), r.Hud, 6, Eligible()));
        Assert.Empty(r.Hud.Children); Assert.Equal(PoseAt(1), r.Root().Pose); Assert.Equal(8, r.Root().Layer);
        if (invalid == "nested-root") Attach(r.Root(1), r.Vanilla);
        Assert.True(r.Bind()); Assert.Equal(r.Routes.Length, r.Hud.Children.Count);
    }

    [Fact]
    public void Rebinding_same_owner_for_page_or_geometry_changes_keeps_originals()
    {
        var r = new Rig(); Assert.True(r.Bind());
        // Companion page selection/visibility is deliberately not native inventory eligibility.
        Assert.True(r.Bind()); r.State.LateTick(Eligible());
        var revised = r.Routes.Select(x => new DsHudRoute(x.Key, x.Role, x.Node, PoseAt(500))).ToArray();
        Assert.True(r.State.Bind(r.Owner, revised, r.Hud, 6, Eligible()));
        Assert.Equal(PoseAt(500), r.Root().Pose);
        r.State.Restore(); Assert.Equal(PoseAt(1), r.Root().Pose);
    }

    [Fact]
    public void Restore_before_container_callback_and_destroyed_children_preserve_survivors()
    {
        var r = new Rig(); var child = new Node(); Attach(child, r.Root()); Assert.True(r.Bind());
        child.Alive = false; r.Root(2).Alive = false;
        r.State.RestoreBefore(() =>
        {
            Assert.DoesNotContain(r.Hud.Children, x => x.Alive);
            r.Hud.Alive = false;
        });
        r.State.Restore();
        Assert.Same(r.Vanilla, r.Root().Parent); Assert.Equal(8, r.Root().Layer);
        Assert.Equal(PoseAt(6), r.Root(5).Pose);
    }

    [Fact]
    public void Inactive_native_parent_cannot_be_bypassed_by_routing()
    {
        var r = new Rig(); r.Vanilla.Active = false;
        Assert.False(r.Bind()); Assert.Empty(r.Hud.Children);
        r.Vanilla.Active = true; Assert.True(r.Bind());
        r.Vanilla.Active = false; r.State.LateTick(Eligible());
        Assert.False(r.State.IsBound); Assert.Same(r.Vanilla, r.Root().Parent);
        Assert.True(r.Root().Active); // no stale activeSelf restoration
    }

    [Fact]
    public void Failed_restore_blocks_teardown_restores_survivors_and_retries_original_snapshot()
    {
        var r = new Rig(); Assert.True(r.Bind()); r.Nodes.FailParent = r.Root();
        bool destroyed = false;
        Assert.Throws<AggregateException>(() => r.State.RestoreBefore(() => destroyed = true));
        Assert.False(destroyed); Assert.True(r.State.IsBound);
        Assert.Same(r.Vanilla, r.Root(1).Parent);
        Assert.Equal(PoseAt(2), r.Root(1).Pose);
        r.Nodes.FailParent = null;
        r.State.RestoreBefore(() => destroyed = true);
        Assert.True(destroyed); Assert.False(r.State.IsBound);
        Assert.Equal(PoseAt(1), r.Root().Pose); Assert.Equal(8, r.Root().Layer);
    }

    [Fact]
    public void Intervening_native_siblings_and_escaped_descendant_layers_restore_exactly()
    {
        var r = new Rig(); var sibling = new Node(); Attach(sibling, r.Vanilla);
        r.Nodes.SetSibling(sibling, 2);
        var order = r.Vanilla.Children.ToArray();
        var child = new Node { Layer = 17 }; Attach(child, r.Root());
        Assert.True(r.Bind()); Attach(child, sibling); child.Pose = PoseAt(678);
        r.State.Restore();
        Assert.Equal(order, r.Vanilla.Children); Assert.Equal(17, child.Layer);
        Assert.Equal(PoseAt(678), child.Pose); Assert.Same(sibling, child.Parent);
    }

    [Fact]
    public void Destroyed_original_parent_restores_survivors_outside_companion()
    {
        var r = new Rig(); Assert.True(r.Bind()); r.Vanilla.Alive = false;
        r.State.LateTick(Eligible());
        Assert.False(r.State.IsBound); Assert.Null(r.Root().Parent);
        Assert.Equal(PoseAt(1), r.Root().Pose); Assert.Empty(r.Hud.Children);
    }

    [Fact]
    public void Companion_tab_hide_reopen_and_selection_keep_native_hud_routed()
    {
        var r = new Rig(); Assert.True(r.Bind());
        bool pages = DsPortFrameState.PagesVisibleAfterTab(true, true);
        Assert.False(pages); r.State.LateTick(Eligible()); Assert.Same(r.Hud, r.Root().Parent);
        pages = DsPortFrameState.PagesVisibleAfterTab(pages, true);
        Assert.True(pages); r.State.LateTick(Eligible()); Assert.Same(r.Hud, r.Root().Parent);
        Assert.True(DsPortFrameState.PagesVisibleAfterTab(false, false));
        Assert.True(DsPortFrameState.PagesVisibleAfterTab(true, false));
        r.State.Restore(); Assert.Equal(PoseAt(1), r.Root().Pose);
    }

    [Fact]
    public void Actual_frame_state_keeps_labels_boundaries_and_interrupted_slide_decisions()
    {
        var state = DsPortFrameState.Initial(5, 0);
        Assert.Equal(1f, DsPortFrameState.LabelAlpha(true));
        Assert.Equal(.6f, DsPortFrameState.LabelAlpha(false));
        Assert.True(DsPortFrameState.ContainsHit(.2f, .2f, .4f));
        Assert.True(DsPortFrameState.ContainsHit(.4f, .2f, .4f));
        Assert.False(DsPortFrameState.ContainsHit(.199f, .2f, .4f));
        state = DsPortFrameState.BeginSelection(state, 4);
        Assert.Equal(1, state.Direction); Assert.Equal(4, state.SelectedIndex);
        Assert.True(DsPortFrameState.IsHostActive(state, 0));
        Assert.True(DsPortFrameState.IsHostActive(state, 4));
        Assert.False(DsPortFrameState.IsHostActive(state, 2));
        state = DsPortFrameState.BeginSelection(state, 1);
        Assert.Equal(-1, state.Direction); Assert.Equal(1, state.SelectedIndex);
        Assert.False(DsPortFrameState.IsHostActive(state, 0));
        Assert.True(DsPortFrameState.IsHostActive(state, 4));
        Assert.True(DsPortFrameState.IsHostActive(state, 1));
        state = DsPortFrameState.CompleteSelection(state);
        for (int i = 0; i < 5; i++) Assert.Equal(i == 1, DsPortFrameState.IsHostActive(state, i));
    }

    [Fact]
    public void Driver_changes_to_child_animation_and_native_visibility_survive_restore()
    {
        var r = new Rig(); var child = new Node(); Attach(child, r.Root()); Assert.True(r.Bind());
        child.Pose = PoseAt(234); child.Active = false; r.Root().Color = "damage";
        r.State.LateTick(Eligible()); r.State.Restore();
        Assert.Equal(PoseAt(234), child.Pose); Assert.False(child.Active);
        Assert.Equal("damage", r.Root().Color);
    }
}
