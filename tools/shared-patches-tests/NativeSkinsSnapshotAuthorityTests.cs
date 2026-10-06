using System.Collections;
using System.Text.Json;
using DualSouls.Skins;
using Xunit;

namespace SharedPatches.Tests;

[Collection("Native menu production bodies")]
public sealed class NativeSkinsSnapshotAuthorityTests
{
    [Theory]
    [InlineData(false, "blocked")] [InlineData(true, "blocked")]
    [InlineData(false, "wrong-profile")] [InlineData(true, "wrong-profile")]
    [InlineData(false, "empty")] [InlineData(true, "empty")]
    [InlineData(false, "malformed")] [InlineData(true, "malformed")]
    [InlineData(false, "oversized")] [InlineData(true, "oversized")]
    [InlineData(false, "failed")] [InlineData(true, "failed")]
    public void Failed_refresh_retires_mutation_authority_but_keeps_native_back(bool ss, string failure)
    {
        var f = Open(ss);
        try {
            SetSnapshot(f, Failure(ss, failure));
            f.Call("RefreshSkinMenu"); f.Call("PaintSkins");
            int before = MutationCalls(f);
            foreach (NativeSkinMenuRowKind kind in new[] { NativeSkinMenuRowKind.Mode, NativeSkinMenuRowKind.Sprites, NativeSkinMenuRowKind.Skin }) {
                dynamic button = Button(f, kind);
                f.Call("SubmitSkin", button);
                f.Call("MoveSkin", button, Enum.Parse(f.Type("EventSystems.MoveDirection"), "Right"));
                f.Call("MoveSkin", button, Enum.Parse(f.Type("EventSystems.MoveDirection"), "Left"));
            }
            Assert.Equal(before, MutationCalls(f));
            foreach (NativeSkinMenuRowKind kind in new[] { NativeSkinMenuRowKind.Mode, NativeSkinMenuRowKind.Sprites, NativeSkinMenuRowKind.Skin })
                Assert.False((bool)Button(f, kind).Selectable.interactable);
            dynamic back = Button(f, NativeSkinMenuRowKind.Back);
            Assert.True((bool)back.Selectable.interactable);
            f.Static("World", "Submit", back.gameObject); f.Static("World", "Pump", 100);
            Assert.False((bool)f.Field("_nativeOpen"));
            Assert.Equal(before, MutationCalls(f));
        }
        finally { Retire(f); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Valid_snapshot_still_dispatches_each_native_control(bool ss)
    {
        var f = Open(ss);
        try {
            int before = MutationCalls(f);
            foreach (NativeSkinMenuRowKind kind in new[] { NativeSkinMenuRowKind.Mode, NativeSkinMenuRowKind.Sprites, NativeSkinMenuRowKind.Skin }) {
                dynamic button = Button(f, kind);
                Assert.True((bool)button.Selectable.interactable);
                f.Static("World", "Submit", button.gameObject);
            }
            Assert.Equal(before + 3, MutationCalls(f));
        }
        finally { Retire(f); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Same_configuration_recovers_only_after_a_checked_snapshot(bool ss)
    {
        var f = Open(ss);
        try {
            string digest = ((NativeSkinMenuModel)f.Field("_skinMenu")).Snapshot.ConfigSha256;
            SetSnapshot(f, Failure(ss, "blocked")); f.Call("RefreshSkinMenu"); f.Call("PaintSkins");
            int before = MutationCalls(f);
            f.Call("SubmitSkin", Button(f, NativeSkinMenuRowKind.Mode));
            Assert.Equal(before, MutationCalls(f));
            f.SkinSnapshot(); f.Call("RefreshSkinMenu"); f.Call("PaintSkins");
            Assert.Equal(digest, ((NativeSkinMenuModel)f.Field("_skinMenu")).Snapshot.ConfigSha256);
            Assert.True((bool)Button(f, NativeSkinMenuRowKind.Mode).Selectable.interactable);
            f.Static("World", "Submit", Button(f, NativeSkinMenuRowKind.Mode).gameObject);
            Assert.Equal(before + 1, MutationCalls(f));
        }
        finally { Retire(f); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Blocked_window_expires_without_mutation_or_rearming_and_reopen_can_recover(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind();
        try {
            f.SkinSnapshot("PENDING"); f.Open(true);
            SetSnapshot(f, Failure(ss, "blocked"));
            NativeMenuFixture.SetStatic(f.Type("Time"), "unscaledTime", 1f); f.Call("Update");
            int before = MutationCalls(f);
            f.Call("SubmitSkin", Button(f, NativeSkinMenuRowKind.Mode));
            Assert.Equal(before, MutationCalls(f));
            NativeMenuFixture.SetStatic(f.Type("Time"), "unscaledTime", 61f); f.Call("Update");
            var transport = (SkinNativeTransportWindow)f.Field("_skinTransport");
            Assert.True(transport.TimedOut); Assert.False(transport.Pending);
            int calls = Calls(f).Count;
            for (int frame = 0; frame < 100; frame++) {
                NativeMenuFixture.SetStatic(f.Type("Time"), "unscaledTime", 62f + frame); f.Call("Update");
            }
            Assert.Equal(calls, Calls(f).Count);
            f.Call("Close"); f.Static("World", "Pump", 100); f.SkinSnapshot(); f.Open(true);
            Assert.True((bool)Button(f, NativeSkinMenuRowKind.Mode).Selectable.interactable);
            f.Static("World", "Submit", Button(f, NativeSkinMenuRowKind.Mode).gameObject);
            Assert.Equal(before + 1, MutationCalls(f));
        }
        finally { Retire(f); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void Snapshot_loss_keeps_native_cancel_on_an_actionable_back_control(bool ss, bool reopen)
    {
        var f = Open(ss);
        try {
            if (reopen) { f.Call("Close"); f.Static("World", "Pump", 100); }
            SetSnapshot(f, Failure(ss, "blocked"));
            if (reopen) f.Open(true);
            else { f.Call("RefreshSkinMenu"); f.Call("PaintSkins"); }
            dynamic back = Button(f, NativeSkinMenuRowKind.Back);
            Assert.True((bool)back.Selectable.interactable);
            dynamic selected = f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject;
            Assert.Same((object)back.gameObject, (object)selected);
            f.Static("World", "Cancel", selected); f.Static("World", "Pump", 100);
            Assert.False((bool)f.Field("_nativeOpen")); Assert.Equal(0, MutationCalls(f));
            Assert.Equal(0, f.Count("ForeignCancelActions"));
        }
        finally { Retire(f); }
    }

    [Theory]
    [InlineData(false, "blocked")] [InlineData(true, "blocked")]
    [InlineData(false, "wrong-profile")] [InlineData(true, "wrong-profile")]
    [InlineData(false, "empty")] [InlineData(true, "empty")]
    [InlineData(false, "malformed")] [InlineData(true, "malformed")]
    [InlineData(false, "oversized")] [InlineData(true, "oversized")]
    [InlineData(false, "failed")] [InlineData(true, "failed")]
    public void First_snapshot_failure_keeps_native_back_without_fabricating_a_model(bool ss, string failure)
    {
        var f = new NativeMenuFixture(ss); f.Bind();
        try {
            SetSnapshot(f, Failure(ss, failure)); f.Open(true);
            Assert.Null(f.Field("_skinMenu"));
            dynamic back = ColdBack(f);
            f.Static("World", "Cancel", back.gameObject); f.Static("World", "Pump", 100);
            Assert.False((bool)f.Field("_nativeOpen"));
            f.Open(true); back = ColdBack(f);
            f.Static("World", "Submit", back.gameObject); f.Static("World", "Pump", 100);
            Assert.False((bool)f.Field("_nativeOpen"));
            Assert.Null(f.Field("_skinMenu"));
            Assert.Equal(0, MutationCalls(f)); Assert.Equal(0, f.Count("ForeignCancelActions"));
            f.SkinSnapshot(); f.Open(true);
            Assert.True((bool)Button(f, NativeSkinMenuRowKind.Mode).Selectable.interactable);
            f.Static("World", "Submit", Button(f, NativeSkinMenuRowKind.Mode).gameObject);
            Assert.Equal(1, MutationCalls(f));
        }
        finally { Retire(f); }
    }

    [Theory]
    [InlineData(false, "Up")] [InlineData(true, "Up")]
    [InlineData(false, "Down")] [InlineData(true, "Down")]
    public void Unavailable_snapshot_navigation_cannot_leave_actionable_native_back(bool ss, string direction)
    {
        var f = Open(ss);
        try {
            SetSnapshot(f, Failure(ss, "blocked")); f.Call("RefreshSkinMenu");
            dynamic back = Button(f, NativeSkinMenuRowKind.Back);
            f.Call("MoveSkin", back, Enum.Parse(f.Type("EventSystems.MoveDirection"), direction));
            Assert.Equal(NativeSkinMenuRowKind.Back, ((NativeSkinMenuModel)f.Field("_skinMenu")).Selected.Kind);
            dynamic selected = f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject;
            Assert.Same((object)back.gameObject, (object)selected);
            Assert.True((bool)back.Selectable.interactable);
            f.Static("World", "Cancel", selected); f.Static("World", "Pump", 100);
            Assert.False((bool)f.Field("_nativeOpen"));
            Assert.Equal(0, MutationCalls(f)); Assert.Equal(0, f.Count("ForeignCancelActions"));
        }
        finally { Retire(f); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Cold_blocked_retry_recovers_without_retiring_the_selected_native_control(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind();
        try {
            SetSnapshot(f, Failure(ss, "blocked")); f.Open(true); ColdBack(f);
            Assert.Null(f.Field("_skinMenu"));
            f.SkinSnapshot();
            NativeMenuFixture.SetStatic(f.Type("Time"), "unscaledTime", 1f); f.Call("Update");
            Assert.NotNull(f.Field("_skinMenu"));
            dynamic selected = f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject;
            Assert.NotNull((object)selected);
            Assert.True((bool)selected.activeInHierarchy);
            dynamic button = ((IEnumerable)f.Field("_skinButtons")).Cast<dynamic>()
                .Single(b => ReferenceEquals((object)b.gameObject, (object)selected));
            Assert.True((bool)button.Selectable.interactable);
            f.Static("World", "Cancel", selected); f.Static("World", "Pump", 100);
            Assert.False((bool)f.Field("_nativeOpen"));
            Assert.Equal(0, MutationCalls(f)); Assert.Equal(0, f.Count("ForeignCancelActions"));
        }
        finally { Retire(f); }
    }

    static dynamic ColdBack(NativeMenuFixture f)
    {
        dynamic back = Assert.Single(((IEnumerable)f.Field("_skinButtons")).Cast<dynamic>()
            .Where(b => (bool)b.gameObject.activeInHierarchy));
        Assert.True((bool)back.Selectable.interactable);
        dynamic selected = f.Static("EventSystems.EventSystem", "current").currentSelectedGameObject;
        Assert.Same((object)back.gameObject, (object)selected);
        return back;
    }

    static NativeMenuFixture Open(bool ss)
    {
        var f = new NativeMenuFixture(ss); f.Bind(); f.SkinSnapshot(); f.Open(true);
        Assert.NotNull(f.Field("_skinMenu"));
        return f;
    }
    static dynamic Button(NativeMenuFixture f, NativeSkinMenuRowKind kind)
    {
        var model = (NativeSkinMenuModel)f.Field("_skinMenu");
        return ((IEnumerable)f.Field("_skinButtons")).Cast<dynamic>()
            .Single(b => b.DataIndex >= 0 && model.Rows[(int)b.DataIndex].Kind == kind);
    }
    static IList Calls(NativeMenuFixture f) => (IList)f.Static("World", "BridgeCalls");
    static int MutationCalls(NativeMenuFixture f) => Calls(f).Cast<string>()
        .Count(c => c == "setMode" || c == "setSpriteScope" || c == "confirmPack");
    static void SetSnapshot(NativeMenuFixture f, string json) =>
        NativeMenuFixture.SetStatic(f.Type("AndroidJavaClass"), "Snapshot", json);
    static string Failure(bool ss, string failure) => failure switch {
        "blocked" => JsonSerializer.Serialize(new { ok = false, code = "LIFECYCLE_BLOCKED" }),
        "failed" => JsonSerializer.Serialize(new { ok = false, code = "DURABILITY_UNAVAILABLE" }),
        "wrong-profile" => JsonSerializer.Serialize(new { ok = true, profileId = ss ? "hollow-knight" : "silksong" }),
        "empty" => "",
        "malformed" => "{",
        "oversized" => new string('x', 262145),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
    static void Retire(NativeMenuFixture f)
    {
        f.Call("CancelAndClearBinding"); f.AssertNoActiveOwned();
        f.Static("World", "FlushDestroy"); f.AssertNoActiveOwned();
    }
}
