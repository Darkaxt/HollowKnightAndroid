using System.Reflection;
using Xunit;

// Invoke the linked production seam, not a host reimplementation. Missing seam is
// executable RED before the five-route shell exists.
public class HollowKnightFiveRouteTests
{
    static Type Production(string suffix = "")
    {
        var type = typeof(HollowKnightFiveRouteTests).Assembly.GetType("HKLowerLayout" + suffix);
        Assert.NotNull(type);
        return type;
    }
    static object Call(string method, params object[] args) =>
        Production().GetMethod(method).Invoke(null, args);
    static float Field(object value, string name) => (float)value.GetType().GetField(name).GetValue(value);

    [Theory]
    [InlineData(-1, 0)] [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 2)]
    [InlineData(3, 3)] [InlineData(4, 4)] [InlineData(5, 0)] [InlineData(100, 0)]
    public void Persisted_ids_are_not_visual_columns(int id, int normalized) =>
        Assert.Equal(normalized, Call("NormalizeTab", id));

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 2)] [InlineData(2, 4)] [InlineData(3, 3)] [InlineData(4, 0)]
    public void Visual_order_and_inverse_are_distinct(int column, int id)
    {
        Assert.Equal(id, Call("TabAtColumn", column));
        Assert.Equal(column, Call("ColumnForTab", id));
    }

    [Fact]
    public void Every_pair_slides_by_visual_order()
    {
        int[] ids = { 1, 2, 4, 3, 0 };
        for (int a = 0; a < 5; a++)
            for (int b = 0; b < 5; b++)
                Assert.Equal(Math.Sign(b - a), Call("SlideDirection", ids[a], ids[b]));
    }

    [Theory]
    [InlineData(1240f,1080f,240f,700f,140f)]
    [InlineData(1600f,1200f,240f,820f,140f)]
    [InlineData(800f,720f,160f,466.66666f,93.33333f)]
    public void Measured_regions_are_disjoint_and_fill_canvas(float w, float h, float hud, float body, float tabs)
    {
        var geometry = Call("Measure", w, h);
        Assert.Equal(w, Field(geometry,"Width"));
        Assert.Equal(hud, Field(geometry,"HudHeight"),3);
        Assert.Equal(body, Field(geometry,"BodyHeight"),3);
        Assert.Equal(tabs, Field(geometry,"TabHeight"),3);
        Assert.Equal(h - tabs, Field(geometry,"TabTop"),3);
        Assert.Equal(hud + body, Field(geometry,"TabTop"),3);
        for (int col = 0; col < 5; col++)
        {
            float x = (col + .5f) * w / 5;
            Assert.Equal(col, geometry.GetType().GetMethod("HitColumn").Invoke(geometry, new object[]{x,h-tabs/2}));
        }
        Assert.Equal(-1, geometry.GetType().GetMethod("HitColumn").Invoke(geometry,new object[]{w,h}));
        Assert.Equal(-1, geometry.GetType().GetMethod("HitColumn").Invoke(geometry,new object[]{-1f,h-1}));
        Assert.Equal(-1, geometry.GetType().GetMethod("HitColumn").Invoke(geometry,new object[]{1f,hud+1}));
    }

    [Fact]
    public void Default_art_and_body_geometry_are_canonical()
    {
        var g = Call("Measure",1240f,1080f);
        Assert.Equal(590f,Field(g,"BodyCenterY"));
        Assert.Equal(248f,Field(g,"CellWidth"));
        Assert.Equal(88f,Field(g,"IconMax"));
    }

    [Fact]
    public void Touch_requires_same_cell_origin_and_respects_strip_and_slide_owners()
    {
        var type = Production("+TabGesture");
        var gesture = Activator.CreateInstance(type);
        void Down(int col) => type.GetMethod("Down").Invoke(gesture,new object[]{col});
        int Tap(int col,bool markers=false,bool sliding=false) => (int)type.GetMethod("Tap").Invoke(gesture,new object[]{col,markers,sliding});
        for(int col=0;col<5;col++){ Down(col); Assert.Equal(Call("TabAtColumn",col),Tap(col)); }
        Down(0); Assert.Equal(-1,Tap(1));
        Down(-1); Assert.Equal(-1,Tap(0));
        Down(2); type.GetMethod("Cancel").Invoke(gesture,null); Assert.Equal(-1,Tap(2));
        Down(4); Assert.Equal(-1,Tap(4,true));
        Down(3); Assert.Equal(-1,Tap(3,false,true));
        Assert.Equal(-1,Tap(3));
    }

    [Theory]
    [InlineData(false,5,false)] [InlineData(true,5,false)] [InlineData(true,0,true)] [InlineData(true,-2,true)]
    public void Journal_notes_use_remaining_requirements_not_cumulative_kills(bool killed,int remaining,bool complete)
    {
        Assert.Equal(complete,Call("NotesUnlocked",killed,remaining));
        Assert.Equal("killedCrawler",Call("JournalKilledKey","Crawler"));
        Assert.Equal("killsCrawler",Call("JournalKillsKey","Crawler"));
        Assert.Equal("NAME_CRAWLER",Call("JournalNameKey","CRAWLER"));
        Assert.Equal("DESC_CRAWLER",Call("JournalDescriptionKey","CRAWLER"));
        Assert.Equal("NOTE_CRAWLER",Call("JournalNotesKey","CRAWLER"));
    }

    [Fact]
    public void Native_guide_condition_is_read_only_and_fail_closed()
    {
        int reads=0;
        Func<string,bool> acquired = key => { reads++; return key=="hasPinBench"; };
        Assert.True((bool)Call("GuideVisible","hasPinBench",acquired));
        Assert.False((bool)Call("GuideVisible","hasPinStag",acquired));
        Assert.False((bool)Call("GuideVisible","",acquired));
        Assert.False((bool)Call("GuideVisible","inventedCondition",acquired));
        Assert.Equal(2,reads);
    }

    [Fact]
    public void Missing_donor_retries_are_bounded_and_retirement_rearms()
    {
        var type=Production("+Retry"); var retry=Activator.CreateInstance(type);
        bool Due(int frame) => (bool)type.GetMethod("Due").Invoke(retry,new object[]{frame});
        Assert.True(Due(0)); Assert.False(Due(1)); Assert.False(Due(119)); Assert.True(Due(120));
        type.GetMethod("Resolved").Invoke(retry,null); Assert.False(Due(240));
        type.GetMethod("Reset").Invoke(retry,null); Assert.True(Due(241));
    }
}
