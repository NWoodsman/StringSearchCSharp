using Microsoft.CodeAnalysis.Text;
using StringSearch.Model;
using StringSearch.Tui;

namespace StringSearch.Tests.Tui;

public class SnippetViewTests
{
    private static readonly Layout Layout = new(40, 10); // snippet rows 4..7

    [Fact]
    public void Draws_gutter_expands_tabs_and_highlights_the_string()
    {
        const string code = "a\n\tb = \"x\";\nc";
        var document = SourceDocument.FromContent("A.cs", code);
        int start = code.IndexOf('"');
        var frame = new Frame(Layout.Width, Layout.Height);

        SnippetView.Draw(frame, Layout, document, topLine: 0, new TextSpan(start, 3));

        Assert.Equal("  1 │ a", frame.RowText(4).TrimEnd());
        Assert.Equal("  2 │     b = \"x\";", frame.RowText(5).TrimEnd());
        Assert.Equal("  3 │ c", frame.RowText(6).TrimEnd());
        Assert.Equal("", frame.RowText(7).Trim());

        // Code starts at column 6; the tab expands to 4 columns, so the opening quote is at 6 + 8.
        Assert.Equal(Theme.CurrentString, frame[5, 14].Style);
        Assert.Equal(Theme.CurrentString, frame[5, 16].Style);
        Assert.NotEqual(Theme.CurrentString, frame[5, 17].Style);
        Assert.Equal(Theme.GutterCurrent, frame[5, 2].Style);
        Assert.Equal(Theme.Gutter, frame[4, 2].Style);
    }

    [Fact]
    public void Scrolls_horizontally_to_a_string_far_to_the_right()
    {
        var code = new string(' ', 100) + "\"far\";";
        var document = SourceDocument.FromContent("A.cs", code);
        var frame = new Frame(Layout.Width, Layout.Height);

        SnippetView.Draw(frame, Layout, document, topLine: 0, new TextSpan(100, 5));

        var row = frame.RowText(4);
        Assert.Contains("\"far\";", row);
        Assert.Equal("‹", frame[4, 6].Text);
    }

    [Fact]
    public void Long_lines_are_cut_with_a_marker()
    {
        var document = SourceDocument.FromContent("A.cs", "var s = \"x\"; " + new string('y', 60));
        var frame = new Frame(Layout.Width, Layout.Height);

        SnippetView.Draw(frame, Layout, document, topLine: 0, new TextSpan(8, 3));

        Assert.Equal("›", frame[4, 39].Text);
    }

    [Fact]
    public void Returns_the_cursor_cell_including_at_line_end()
    {
        const string code = "a\n\tb = \"x\";";
        var document = SourceDocument.FromContent("A.cs", code);
        var frame = new Frame(Layout.Width, Layout.Height);

        var onQuote = SnippetView.Draw(frame, Layout, document, 0, null, cursor: code.IndexOf('"'));
        var atEnd = SnippetView.Draw(frame, Layout, document, 0, null, cursor: code.Length);
        var offScreen = SnippetView.Draw(frame, new Layout(40, 7), document, topLine: 0, null, cursor: code.Length);

        Assert.Equal((5, 14), onQuote);
        Assert.Equal((5, 6 + 4 + 8), atEnd); // gutter + tab + `b = "x";`
        Assert.Null(SnippetView.Draw(new Frame(40, 10), Layout, document, topLine: 1, null, cursor: 0));
        Assert.Null(offScreen); // a 1-line pane shows only line 1
    }

    [Fact]
    public void Scrolls_horizontally_to_follow_the_cursor()
    {
        var code = "var s = \"" + new string('a', 80) + "\";";
        var document = SourceDocument.FromContent("A.cs", code);
        var frame = new Frame(Layout.Width, Layout.Height);

        var cell = SnippetView.Draw(frame, Layout, document, 0, new TextSpan(8, 82), cursor: 85);

        Assert.NotNull(cell);
        Assert.InRange(cell!.Value.Col, 6, Layout.Width - 1);
        Assert.Equal("‹", frame[4, 6].Text);
    }

    [Theory]
    [InlineData("abc", 2, 2)]
    [InlineData("\tx", 1, 4)]
    [InlineData("ab\tx", 3, 4)]
    [InlineData("界x", 1, 2)]
    public void DisplayColumn_expands_tabs_and_wide_glyphs(string line, int offset, int expected) =>
        Assert.Equal(expected, SnippetView.DisplayColumn(line, offset));
}
