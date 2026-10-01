using System;

// The measured lower-shell contract. No engine, input, font or PlayerData side
// effects: linked unchanged into the host suite and consumed by rendering/input.
public static class HKLowerLayout
{
    public static readonly int[] COL_TO_TAB = { 1, 2, 4, 3, 0 };
    public static readonly int[] TAB_TO_COL = { 4, 0, 1, 3, 2 };
    public static int NormalizeTab(int id) { return id >= 0 && id < 5 ? id : 0; }
    public static int TabAtColumn(int col) { return col >= 0 && col < 5 ? COL_TO_TAB[col] : 0; }
    public static int ColumnForTab(int id) { return TAB_TO_COL[NormalizeTab(id)]; }
    public static int SlideDirection(int from, int to) { return Math.Sign(ColumnForTab(to) - ColumnForTab(from)); }
    public struct Geometry
    {
        public float Width, Height, HudHeight, BodyHeight, TabHeight, TabTop, BodyCenterY, CellWidth, IconMax;
        public int HitColumn(float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || x < 0 || x >= Width || y < TabTop || y >= Height) return -1;
            return (int)(x / CellWidth);
        }
        public bool InBody(float x, float y) { return x >= 0 && x < Width && y >= HudHeight && y < TabTop; }
    }
    public static Geometry Measure(float width, float height)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        float scale = Math.Min(1, height / 1080f), hud = 240 * scale, tabs = 140 * scale;
        return new Geometry { Width = width, Height = height, HudHeight = hud,
            BodyHeight = height - hud - tabs, TabHeight = tabs, TabTop = height - tabs,
            BodyCenterY = (hud + height - tabs) / 2, CellWidth = width / 5, IconMax = 88 * scale };
    }
    public struct NativePaneColumns
    {
        public float SubjectX,SubjectWidth,ChooserX,ChooserWidth,DetailX,DetailWidth;
        public float LeftGutter,RightGutter,Top,Height,Cell,Gap;
    }
    public static NativePaneColumns NativeColumns(Geometry g,bool charms)
    {
        float sx=g.Width/1240f, width=(charms ? 350 : 400)*sx, gap=10*sx;
        return new NativePaneColumns { SubjectX=20*sx,SubjectWidth=(charms ? 470 : 420)*sx,
            ChooserX=(charms ? 520 : 470)*sx,ChooserWidth=width,DetailX=900*sx,DetailWidth=320*sx,
            LeftGutter=(charms ? 505 : 455)*sx,RightGutter=885*sx,
            Top=g.HudHeight+(charms ? 16 : 20),Height=Math.Max(1,g.BodyHeight-(charms ? 36 : 40)),
            Cell=(width-2*gap)/3,Gap=gap };
    }
    public sealed class TabGesture
    {
        int origin = -1;
        public void Down(int column) { origin = column; }
        public void Cancel() { origin = -1; }
        public int Tap(int column, bool markerOwner, bool sliding)
        {
            int down = origin; origin = -1;
            return !markerOwner && !sliding && column >= 0 && column < 5 && down == column ? COL_TO_TAB[column] : -1;
        }
    }
    // One bounded scan every two seconds at 60Hz while pending; none while
    // healthy. Reset at source/save/skin/display boundaries, including pause.
    public sealed class Retry
    {
        int next; bool ready;
        public bool Due(int frame) { if (ready || frame < next) return false; next = frame + 120; return true; }
        public void Resolved() { ready = true; }
        public void Reset() { ready = false; next = 0; }
    }
    public static bool NotesUnlocked(bool killed, int remaining) { return killed && remaining <= 0; }
    public static string JournalKilledKey(string name) { return "killed" + name; }
    public static string JournalKillsKey(string name) { return "kills" + name; }
    public static string JournalNameKey(string convo) { return "NAME_" + convo; }
    public static string JournalDescriptionKey(string convo) { return "DESC_" + convo; }
    public static string JournalNotesKey(string convo) { return "NOTE_" + convo; }
    public static bool GuideVisible(string condition, Func<string, bool> readBool)
    {
        if(readBool == null) return false;
        switch(condition)
        {
            case "hasPinBench": case "hasPinStag": case "hasPinTram": case "hasPinSpa":
            case "hasPinShop": case "hasPinGuardian": case "hasPinDreamPlant":
            case "hasPinCocoon": case "hasPinGhost": case "hasPinGrub": case "hasPinBlackEgg":
                return readBool(condition);
            default: return false;
        }
    }
}
