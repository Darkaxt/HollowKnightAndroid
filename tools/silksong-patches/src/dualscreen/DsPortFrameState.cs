// Pure frame decisions shared by production and the executable host contract.
// Keep Unity objects and rendering concerns in DsPortFrame.

public struct DsPortFrameDecision
{
    public int SelectedIndex;
    public int OutgoingIndex;
    public int IncomingIndex;
    public int Direction;
    public bool Sliding;
}

public static class DsPortFrameState
{
    public static DsPortFrameDecision Initial(int pageCount, int selectedIndex)
    {
        int selected = ClampIndex(pageCount, selectedIndex);
        return new DsPortFrameDecision
        {
            SelectedIndex = selected,
            OutgoingIndex = selected,
            IncomingIndex = selected,
            Direction = 0,
            Sliding = false,
        };
    }

    public static float LabelAlpha(bool selected)
    {
        return selected ? 1f : 0.6f;
    }

    public static bool ContainsHit(float position, float minimum, float maximum)
    {
        return position >= minimum && position <= maximum;
    }

    public static DsPortFrameDecision BeginSelection(DsPortFrameDecision current,
                                                      int targetIndex)
    {
        if (targetIndex == current.SelectedIndex) return current;
        int outgoing = current.SelectedIndex;
        return new DsPortFrameDecision
        {
            SelectedIndex = targetIndex,
            OutgoingIndex = outgoing,
            IncomingIndex = targetIndex,
            Direction = targetIndex >= outgoing ? 1 : -1,
            Sliding = true,
        };
    }

    public static DsPortFrameDecision CompleteSelection(DsPortFrameDecision current)
    {
        current.OutgoingIndex = current.SelectedIndex;
        current.IncomingIndex = current.SelectedIndex;
        current.Direction = 0;
        current.Sliding = false;
        return current;
    }

    public static bool IsHostActive(DsPortFrameDecision state, int hostIndex)
    {
        if (!state.Sliding) return hostIndex == state.SelectedIndex;
        return hostIndex == state.OutgoingIndex || hostIndex == state.IncomingIndex;
    }

    static int ClampIndex(int pageCount, int index)
    {
        if (pageCount <= 0) return -1;
        if (index < 0) return 0;
        if (index >= pageCount) return pageCount - 1;
        return index;
    }
}
