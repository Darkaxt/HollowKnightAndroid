using Xunit;

public sealed class DsShellStatusFitTests
{
    [Fact]
    public void Status_stays_on_the_title_row_when_it_fits()
    {
        // Silksong map: title 340 wide centred in 1240 ends at 790; status 140 wide ends at 972.
        Assert.False(DsHeaderLayout.StatusFit(1240f, 340f, 972f, 140f, out float shift));
        Assert.Equal(0f, shift);
    }

    [Fact]
    public void A_near_miss_slides_the_title_left_instead_of_raising_the_status()
    {
        // Hollow Knight map: KING'S PASS ends at 790, FULL MAP pulls the status edge to 936.
        Assert.False(DsHeaderLayout.StatusFit(1240f, 340f, 936f, 140f, out float shift));
        Assert.Equal(10f, shift, 3);   // 790 + 16 - 796
    }

    [Fact]
    public void A_long_title_that_would_reach_the_left_fifth_raises_the_status()
    {
        // FORGOTTEN CROSSROADS: 760 wide, starts at 240, cannot slide past 248.
        Assert.True(DsHeaderLayout.StatusFit(1240f, 760f, 936f, 140f, out float shift));
        Assert.Equal(0f, shift);
    }
}
