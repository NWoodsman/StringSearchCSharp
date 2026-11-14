using Microsoft.CodeAnalysis.Text;
using StringSearch.Editing;
using StringSearch.Model;
using StringSearch.Scanning;

namespace StringSearch.Tests.Editing;

public class EditSessionTests
{
    private static EditSession Start(string code)
    {
        var document = SourceDocument.FromContent("A.cs", code);
        var occurrence = StringScanner.ScanSource(code).First();
        return EditSession.Start(document, occurrence);
    }

    [Theory]
    [InlineData("\"a\"", 1)]
    [InlineData("@\"a\"", 2)]
    [InlineData("$\"a\"", 2)]
    [InlineData("$@\"a\"", 3)]
    [InlineData("\"\"\"a\"\"\"", 3)]
    [InlineData("$$\"\"\"\"a\"\"\"\"", 6)]
    [InlineData("\"a\"u8", 1)]
    public void Opening_delimiter_length(string raw, int expected) =>
        Assert.Equal(expected, EditSession.OpeningDelimiterLength(raw));

    [Fact]
    public void Insert_and_backspace_update_text_cursor_span_and_patch()
    {
        const string code = "var s = \"ab\"; var t = 1;";
        var session = Start(code).Insert("XY");

        Assert.Equal("var s = \"XYab\"; var t = 1;", session.Text.ToString());
        Assert.Equal(11, session.Cursor);
        Assert.Equal("\"XYab\"", session.Text.ToString(session.StringSpan));
        Assert.True(session.IsDirty);
        Assert.Equal((2, 0), session.Size);
        Assert.Equal(new TextChange(new TextSpan(9, 0), "XY"), session.Patch);
        Assert.Single(session.Changes);

        session = session.Backspace();
        Assert.Equal("\"Xab\"", session.Text.ToString(session.StringSpan));
        Assert.Equal((1, 0), session.Size);
    }

    [Fact]
    public void Patch_is_minimal_even_after_many_keystrokes()
    {
        var session = Start("x = \"hello\";").Right().Right().Delete().Delete().Insert("LL");

        Assert.Equal("x = \"heLLo\";", session.Text.ToString());
        Assert.Equal(new TextChange(new TextSpan(7, 2), "LL"), session.Patch);
        Assert.Equal((2, 2), session.Size);
        Assert.Equal(3, session.Changes.Count);
    }

    [Fact]
    public void Undoing_by_hand_is_not_dirty()
    {
        var session = Start("x = \"a\";").Insert("b").Backspace();

        Assert.False(session.IsDirty);
        Assert.Equal((0, 0), session.Size);
    }

    [Fact]
    public void Backspace_and_delete_treat_crlf_and_surrogate_pairs_as_one_unit()
    {
        var session = Start("x = \"a\";\r\ny = 1;");
        int lineEnd = session.Text.Lines[0].End;

        var joined = session with { Cursor = lineEnd };
        Assert.Equal("x = \"a\";y = 1;", joined.Delete().Text.ToString());
        Assert.Equal("x = \"a\";y = 1;", (joined with { Cursor = lineEnd + 2 }).Backspace().Text.ToString());

        var emoji = Start("x = \"🎉\";");
        Assert.Equal("x = \"\";", emoji.Delete().Text.ToString());
        Assert.Equal(7, emoji.Right().Cursor);
    }

    [Fact]
    public void Enter_uses_the_file_line_ending()
    {
        Assert.Equal("\r\n", Start("x = \"a\";\r\n").NewLine);
        Assert.Equal("\n", Start("x = \"a\";\n").NewLine);
        Assert.Equal("\n", Start("x = \"a\";").NewLine);
    }

    [Fact]
    public void Home_is_smart_and_end_goes_to_line_end()
    {
        var session = Start("    x = \"a\";");

        Assert.Equal(4, session.Home().Cursor);
        Assert.Equal(0, session.Home().Home().Cursor);
        Assert.Equal(12, session.End().Cursor);
    }

    [Fact]
    public void Vertical_moves_keep_the_goal_column_across_short_lines_and_tabs()
    {
        const string code = "x = \"abcdefgh\";\nab\n\tcdefghijkl";
        var session = Start(code) with { Cursor = 10 }; // column 10

        var down1 = session.MoveLines(1);
        Assert.Equal(code.IndexOf("ab\n", StringComparison.Ordinal) + 2, down1.Cursor); // clamped to end of "ab"

        var down2 = down1.MoveLines(1);
        int line3 = code.LastIndexOf('\n') + 1;
        Assert.Equal(line3 + 7, down2.Cursor); // tab = 4 columns, so column 10 is 'i' at offset 7

        Assert.Equal(10, down2.MoveLines(-2).Cursor);
        Assert.Equal(2, down2.MoveLines(5).CursorLine); // clamped at the last line
    }

    [Theory]
    [InlineData(0, 0, "zz", 12, 17)]   // insert before: shift
    [InlineData(10, 0, "zz", 12, 17)]  // insert at the start (before the opening quote): shift
    [InlineData(11, 0, "zz", 10, 17)]  // insert inside: grow
    [InlineData(15, 0, "zz", 10, 15)]  // insert at end (after closing quote): outside
    [InlineData(20, 0, "zz", 10, 15)]  // insert after: unchanged
    [InlineData(11, 2, "", 10, 13)]    // delete inside: shrink
    [InlineData(8, 4, "", 8, 11)]      // delete across the start
    [InlineData(13, 5, "", 10, 13)]    // delete across the end
    [InlineData(5, 20, "q", 5, 6)]     // delete everything around it
    public void AdjustSpan_maps_the_string_through_edits(int start, int length, string text, int expectedStart, int expectedEnd)
    {
        var adjusted = EditSession.AdjustSpan(TextSpan.FromBounds(10, 15), new TextChange(new TextSpan(start, length), text));

        Assert.Equal(TextSpan.FromBounds(expectedStart, expectedEnd), adjusted);
    }

    [Fact]
    public void Document_is_rebuilt_only_when_the_text_changes()
    {
        var session = Start("x = \"a\";");
        var document = session.Document;

        Assert.Same(document, session.Right().Document);
        Assert.NotSame(document, session.Insert("b").Document);
        Assert.Equal("UTF-8", session.Insert("b").Document.EncodingName);
    }
}
