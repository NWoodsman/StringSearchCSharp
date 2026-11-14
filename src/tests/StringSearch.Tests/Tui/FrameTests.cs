using StringSearch.Tui;

namespace StringSearch.Tests.Tui;

public class FrameTests
{
    [Fact]
    public void Wide_glyph_takes_two_cells_with_an_empty_continuation()
    {
        var frame = new Frame(10, 1);

        int end = frame.Write(0, 0, "a界b", Style.Default);

        Assert.Equal(4, end);
        Assert.Equal(["a", "界", "", "b"], Enumerable.Range(0, 4).Select(c => frame[0, c].Text));
    }

    [Fact]
    public void Wide_glyph_that_does_not_fit_is_dropped()
    {
        var frame = new Frame(10, 1);

        int end = frame.Write(0, 0, "ab界", Style.Default, maxCol: 3);

        Assert.Equal(2, end);
        Assert.Equal(" ", frame[0, 2].Text);
    }

    [Fact]
    public void Combining_mark_joins_the_previous_cell_and_controls_become_dots()
    {
        var frame = new Frame(10, 1);

        frame.Write(0, 0, "éx\u0007", Style.Default);

        Assert.Equal("é", frame[0, 0].Text);
        Assert.Equal("x", frame[0, 1].Text);
        Assert.Equal("·", frame[0, 2].Text);
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("界", 2)]
    [InlineData("🎉", 2)]
    [InlineData("́", 0)]
    [InlineData("‍", 0)]
    [InlineData("⣿", 1)]
    public void CellWidth_of_common_characters(string text, int expected) =>
        Assert.Equal(expected, CellWidth.Of(text));
}

public class AnsiWriterTests
{
    [Fact]
    public void First_frame_clears_and_draws_every_row()
    {
        var frame = new Frame(4, 2);
        frame.Write(0, 0, "ab", Style.Default);

        var output = AnsiWriter.Diff(null, frame, ColorMode.TrueColor);

        Assert.StartsWith("\u001b[0m\u001b[2J", output);
        Assert.Contains("\u001b[1;1H", output);
        Assert.Contains("\u001b[2;1H", output);
    }

    [Fact]
    public void Only_changed_rows_are_rewritten()
    {
        var a = new Frame(4, 3);
        var b = new Frame(4, 3);
        b.Write(1, 0, "x", Style.Default);

        Assert.Equal("", AnsiWriter.Diff(a, new Frame(4, 3), ColorMode.TrueColor));
        var output = AnsiWriter.Diff(a, b, ColorMode.TrueColor);
        Assert.Contains("\u001b[2;1H", output);
        Assert.DoesNotContain("\u001b[1;1H", output);
        Assert.DoesNotContain("\u001b[3;1H", output);
    }

    [Fact]
    public void Colors_use_24_bit_or_the_256_color_cube()
    {
        var frame = new Frame(1, 1);
        frame.Write(0, 0, "x", new Style(Fg: new Rgb(255, 0, 0), Attr: TextAttr.Bold));

        Assert.Contains("\u001b[0;1;38;2;255;0;0m", AnsiWriter.Diff(null, frame, ColorMode.TrueColor));
        Assert.Contains("\u001b[0;1;38;5;196m", AnsiWriter.Diff(null, frame, ColorMode.Palette256));
    }

    [Fact]
    public void Continuation_cells_are_not_written()
    {
        var frame = new Frame(3, 1);
        frame.Write(0, 0, "界x", Style.Default);

        var output = AnsiWriter.Diff(null, frame, ColorMode.TrueColor);

        Assert.Contains("界x", output);
    }
}
