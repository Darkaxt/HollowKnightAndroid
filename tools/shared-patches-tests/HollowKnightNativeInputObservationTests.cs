using DualSouls.Mods;
using Xunit;
using H = NativeMenuFixture.Hk.Engine;

namespace SharedPatches.Tests;

// Executes the generated COMPLETE production native-menu bodies. EventSystem,
// raycast and engine identities are external boundary inputs, not a real Unity
// raycaster/geometry/native-action or optical acceptance result.
[Collection("Native menu production bodies")]
public sealed class HollowKnightNativeInputObservationTests
{
    static (NativeMenuFixture Fixture, H.HollowKnightNativeModsButton Row) OpenRow()
    {
        var f = new NativeMenuFixture(false); f.Bind(); f.Open(); f.SelectTweak("run_speed");
        return (f, (H.HollowKnightNativeModsButton)f.Row());
    }

    [Fact]
    public void PointerIngressDistinguishesActualRecipientFromSelectedAndRaycastObjects()
    {
        var (f, row) = OpenRow();
        var reset = (H.HollowKnightNativeModsButton)f.Button(TweakMenuPresenterLayout.ResetButtonIndex);
        H.EventSystems.EventSystem.current.currentSelectedGameObject = reset.gameObject;
        var raycast = new H.GameObject("observed-raycast-child");
        var pointer = new H.EventSystems.PointerEventData {
            position = new H.Vector2(321, 654), pointerPress = row.gameObject,
            pointerCurrentRaycast = new H.EventSystems.RaycastResult { gameObject = raycast }
        };
        H.World.Logs.Clear();
        ((H.EventSystems.IPointerClickHandler)row).OnPointerClick(pointer);
        string line = Assert.Single(H.World.Logs.Where(s => s.StartsWith("[HK Mods Input] event=pointer ")));
        Assert.Contains("recipient=" + row.gameObject.GetInstanceID() + " ", line);
        Assert.Contains("selected=" + reset.gameObject.GetInstanceID() + " ", line);
        Assert.Contains("press=" + row.gameObject.GetInstanceID() + " ", line);
        Assert.Contains("raycast=" + raycast.GetInstanceID() + " ", line);
        Assert.Contains("dataIndex=" + row.DataIndex + " ", line);
        Assert.Contains("pointerX=321 pointerY=654", line);
        Assert.Contains("current=True", line);
        Assert.DoesNotContain("success=true", line);
        Assert.Equal(row.DataIndex, f.Menu.SelectedRowIndex); // Existing recipient selection still ran.
        Assert.Single(H.World.Logs.Where(s => s.StartsWith("[HK Mods Input] event=submit ")));
    }

    [Fact]
    public void RetiredDriverIngressIsVisibleWithoutRearmingOrMutatingTheMenu()
    {
        var (f, row) = OpenRow(); int oldGeneration = row.Generation;
        f.Call("CancelAndClearBinding"); H.World.Logs.Clear(); int writes = f.Store.Writes;
        ((H.EventSystems.ISubmitHandler)row).OnSubmit(new H.EventSystems.BaseEventData());
        string line = Assert.Single(H.World.Logs.Where(s => s.StartsWith("[HK Mods Input] event=submit ")));
        Assert.Contains("generation=" + oldGeneration + " ", line);
        Assert.Contains("current=False", line);
        Assert.Equal(writes, f.Store.Writes);
        Assert.False((bool)f.Field("_nativeOpen"));
    }

    [Fact]
    public void DiagnosticFailureDoesNotInterceptDispatchAndDisablesOnlyThatObserver()
    {
        var (f, row) = OpenRow();
        int selected = f.Menu.SelectedRowIndex;
        Assert.True(selected + 1 < f.Menu.CurrentRows.Count);
        Assert.True(row.Selectable.interactable);
        Assert.True((bool)f.Call("DriverIsCurrent", row, row.Generation));
        H.World.ThrowInputTraceLog = true;
        int attempts = H.World.InputTraceAttempts;
        var move = new H.EventSystems.AxisEventData { moveDir = H.EventSystems.MoveDirection.Down };
        ((H.EventSystems.IMoveHandler)row).OnMove(move);
        Assert.Equal(selected + 1, f.Menu.SelectedRowIndex);
        int afterFirstMove = H.World.InputTraceAttempts;
        // Move also selects another native driver through EventSystem; each
        // observer may fail once, but neither may intercept navigation.
        Assert.InRange(afterFirstMove - attempts, 1, 2);
        ((H.EventSystems.IMoveHandler)row).OnMove(move);
        Assert.Equal(afterFirstMove, H.World.InputTraceAttempts);
        H.World.ThrowInputTraceLog = false;
        var reset = (H.HollowKnightNativeModsButton)f.Button(TweakMenuPresenterLayout.ResetButtonIndex);
        ((H.EventSystems.ISelectHandler)reset).OnSelect(new H.EventSystems.BaseEventData());
        Assert.Equal(afterFirstMove + 1, H.World.InputTraceAttempts);
        Assert.True(f.Menu.IsOpen);
    }

    [Theory]
    [InlineData("move")] [InlineData("cancel")] [InlineData("select")]
    public void RejectedIngressStillNamesActualHandlerWithoutChangingRetiredState(string kind)
    {
        var (f, row) = OpenRow(); f.Call("CancelAndClearBinding");
        int writes = f.Store.Writes; H.World.Logs.Clear();
        if (kind == "move") ((H.EventSystems.IMoveHandler)row).OnMove(new H.EventSystems.AxisEventData { moveDir = H.EventSystems.MoveDirection.Down });
        else if (kind == "cancel") ((H.EventSystems.ICancelHandler)row).OnCancel(new H.EventSystems.BaseEventData());
        else ((H.EventSystems.ISelectHandler)row).OnSelect(new H.EventSystems.BaseEventData());
        string line = Assert.Single(H.World.Logs.Where(s => s.StartsWith("[HK Mods Input] event=" + kind + " ")));
        Assert.Contains("recipient=" + row.gameObject.GetInstanceID() + " ", line);
        Assert.Contains("current=False", line); Assert.Equal(writes, f.Store.Writes);
        Assert.False((bool)f.Field("_nativeOpen"));
    }

    [Fact]
    public void RepeatedIngressIsCappedPerDriverWithoutSuppressingExistingDispatch()
    {
        var (f, row) = OpenRow(); H.World.Logs.Clear();
        // This is actual callback ingress, not an Update-loop diagnostic poll.
        for (int i = 0; i < 100; i++) ((H.EventSystems.ISelectHandler)row).OnSelect(new H.EventSystems.BaseEventData());
        int count = H.World.Logs.Count(s => s.StartsWith("[HK Mods Input]"));
        Assert.InRange(count, 1, 32);
        Assert.Equal(row.DataIndex, f.Menu.SelectedRowIndex);
        Assert.True(f.Menu.IsOpen);
    }

    [Fact]
    public void PointerObservationLabelsRawRecipientTransformRatherThanClaimingHitGeometry()
    {
        var (f, row) = OpenRow();
        var rect = (H.RectTransform)row.transform;
        rect.anchoredPosition = new H.Vector2(123, -456); rect.sizeDelta = new H.Vector2(789, 55);
        H.World.Logs.Clear();
        ((H.EventSystems.IPointerClickHandler)row).OnPointerClick(new H.EventSystems.PointerEventData());
        string line = Assert.Single(H.World.Logs.Where(s => s.StartsWith("[HK Mods Input] event=pointer ")));
        Assert.Contains("rectSpace=recipient-parent-local anchoredX=123 anchoredY=-456 sizeDeltaX=789 sizeDeltaY=55", line);
        Assert.Contains("hitGeometry=unavailable", line);
    }

    [Fact]
    public void IdleNativeMenuUpdatesDoNotEmitOrAttemptInputDiagnostics()
    {
        var (f, _) = OpenRow(); int attempts = H.World.InputTraceAttempts;
        H.World.Logs.Clear();
        for (int i = 0; i < 20; i++) f.Call("Update");
        Assert.Equal(attempts, H.World.InputTraceAttempts);
        Assert.DoesNotContain(H.World.Logs, s => s.StartsWith("[HK Mods Input]"));
    }
}
