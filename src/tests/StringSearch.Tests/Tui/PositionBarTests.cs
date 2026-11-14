using StringSearch.Tui;

namespace StringSearch.Tests.Tui;

public class PositionBarTests
{
    private static string Draw(int index, int count, int width = 20)
    {
        var frame = new Frame(width, 1);
        PositionBar.Draw(frame, 0, index, count);
        return frame.RowText(0);
    }

    [Fact]
    public void Label_is_padded_to_the_width_of_the_total()
    {
        Assert.Equal(" 1/10", PositionBar.Label(0, 10));
        Assert.Equal("10/10", PositionBar.Label(9, 10));
        Assert.Equal("0/0", PositionBar.Label(0, 0));
    }

    [Fact]
    public void First_of_ten_fills_a_tenth_with_sub_cell_precision()
    {
        // 20 wide: label " 1/10" at col 15, 13 fill cells; 0.1 * 13 * 8 = 10.4 -> 10 eighths = 1 full + 2/8.
        Assert.Equal("⣿⡄●" + new string(' ', 12) + " 1/10", Draw(0, 10));
    }

    [Fact]
    public void Last_fills_the_whole_bar_with_the_marker_at_the_end()
    {
        Assert.Equal(new string('⣿', 13) + "●" + " " + "10/10", Draw(9, 10));
    }

    [Fact]
    public void Bar_grows_with_every_step_even_with_many_results()
    {
        int Filled(int index) => Draw(index, 2000, width: 120).IndexOf(PositionBar.Marker, StringComparison.Ordinal);
        var bars = Enumerable.Range(0, 2000).Select(i => Draw(i, 2000, 120)).ToList();

        Assert.True(Filled(1999) > Filled(1000));
        Assert.True(Filled(1000) > Filled(0));
        // Sub-cell steps: the bar text changes at least every 3 steps (108 cells * 8 = 864 units for 2000 results).
        Assert.True(bars.Zip(bars.Skip(3)).All(p => p.First != p.Second));
    }

    [Fact]
    public void Empty_list_shows_only_the_label()
    {
        Assert.Equal(new string(' ', 17) + "0/0", Draw(0, 0));
    }

    [Fact]
    public void Color_runs_from_start_to_end_color()
    {
        var frame = new Frame(20, 1);
        PositionBar.Draw(frame, 0, 9, 10);

        Assert.Equal(Theme.BarEnd, frame[0, 13].Style.Fg); // marker at fraction 1
        Assert.Equal(Theme.BarColor(1 / 13.0), frame[0, 0].Style.Fg);
        Assert.Equal(Theme.BarStart, Theme.BarColor(0));
        Assert.Equal(Theme.BarMiddle, Theme.BarColor(0.5));
    }
}
