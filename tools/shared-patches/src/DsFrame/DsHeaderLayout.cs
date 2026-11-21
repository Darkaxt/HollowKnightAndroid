// Unity-free header layout decisions for DsShell. Host tests compile this file.
public static class DsHeaderLayout
{
    public const float StatusTitleGap = 16f;

    /// <summary>
    /// Whether the status must move up a row, for a panel width, the centred title's
    /// width, the status's right edge and its width. The status stays on the title's
    /// row whenever it can, as Silksong shows it ("BONE BOTTOM  DEATHS 0"): a near
    /// miss slides the title left by <paramref name="titleShift"/> instead, as long
    /// as the title keeps clear of the row's left fifth (Silksong's needle sits
    /// there). Only a title that still will not fit ("FORGOTTEN CROSSROADS") raises it.
    /// </summary>
    public static bool StatusFit(float panelW, float titleW, float statusRight, float statusW, out float titleShift)
    {
        titleShift = 0f;
        float overlap = panelW * 0.5f + titleW * 0.5f + StatusTitleGap - (statusRight - statusW);
        if (overlap <= 0f) return false;
        if (panelW * 0.5f - titleW * 0.5f - overlap >= panelW * 0.2f)
        {
            titleShift = overlap;
            return false;
        }
        return true;
    }
}
