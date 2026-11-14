using StringSearch.App;
using StringSearch.Model;
using StringSearch.Scanning;
using StringSearch.Tui;

namespace StringSearch.Tests.App;

public class EditModeTests
{
    private const int View = 5;

    // 30 lines; the string is on line 20.
    private static readonly string Code = string.Join("\n",
        Enumerable.Range(1, 30).Select(i => i == 20 ? "    var s = \"hello\";" : $"// line {i}"));

    private static readonly SourceDocument Document = SourceDocument.FromContent("A.cs", Code);
    private static readonly IReadOnlyList<StringOccurrence> Items = StringScanner.ScanSource(Code, "A.cs", "A.cs");
    private static readonly Controller Sut = new(Items, _ => Document);

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0', bool control = false, bool alt = false) =>
        new(c, key, shift: false, alt, control);

    private static ConsoleKeyInfo Char(char c) => Key(ConsoleKey.NoName, c);

    private static ConsoleKeyInfo Ctrl(ConsoleKey key) => Key(key, (char)(key - ConsoleKey.A + 1), control: true);

    private static AppState Press(AppState state, params ConsoleKeyInfo[] keys) =>
        keys.Aggregate(state, (s, k) => Sut.Handle(s, k, View));

    private static AppState Type(AppState state, string text) => Press(state, text.Select(Char).ToArray());

    private static AppState InEdit() => Press(AppState.Initial, Char('1'), Char('2'));

    private static string EditedLine(AppState state) => state.Edit!.Text.Lines[19].ToString();

    [Fact]
    public void Two_enters_edit_mode_with_the_cursor_inside_the_quotes()
    {
        var state = InEdit();

        Assert.Equal(Mode.Edit, state.Mode);
        Assert.Equal(Code.IndexOf("hello", StringComparison.Ordinal), state.Edit!.Cursor);
        Assert.InRange(19, state.TopLine, state.TopLine + View - 1);
    }

    [Fact]
    public void Enter_in_scroll_mode_enters_edit_mode_like_two()
    {
        var viaEnter = Press(AppState.Initial, Char('1'), Key(ConsoleKey.Enter, '\r'));
        var viaTwo = InEdit();

        Assert.Equal(Mode.Edit, viaEnter.Mode);
        Assert.Equal(viaTwo.Edit!.Cursor, viaEnter.Edit!.Cursor);
        Assert.Equal(viaTwo.TopLine, viaEnter.TopLine);
        Assert.False(viaEnter.Edit.IsDirty); // the Enter itself is not typed into the buffer
    }

    [Fact]
    public void Two_from_waiting_mode_also_enters_edit_mode()
    {
        Assert.Equal(Mode.Edit, Press(AppState.Initial, Char('2')).Mode);
    }

    [Fact]
    public void Digits_and_q_are_typed_not_treated_as_commands()
    {
        var state = Type(InEdit(), "1q2");

        Assert.Equal(Mode.Edit, state.Mode);
        Assert.False(state.Quit);
        Assert.Equal("    var s = \"1q2hello\";", EditedLine(state));
        Assert.True(state.Edit!.IsDirty);
    }

    [Fact]
    public void Ctrl_and_alt_chords_are_not_typed()
    {
        var state = Press(InEdit(), Key(ConsoleKey.X, 'x', alt: true), Key(ConsoleKey.B, '\u0002', control: true));

        Assert.False(state.Edit!.IsDirty);
    }

    [Fact]
    public void Esc_without_changes_returns_to_scroll_mode()
    {
        var state = Press(InEdit(), Key(ConsoleKey.RightArrow), Key(ConsoleKey.Escape));

        Assert.Equal(Mode.Scroll, state.Mode);
        Assert.Null(state.Edit);
        Assert.Equal(Prompt.None, state.Prompt);
    }

    [Fact]
    public void Typing_then_deleting_it_again_counts_as_no_changes()
    {
        var state = Press(Type(InEdit(), "ab"), Key(ConsoleKey.Backspace), Key(ConsoleKey.Backspace));

        Assert.False(state.Edit!.IsDirty);
        Assert.Equal(Mode.Scroll, Press(state, Key(ConsoleKey.Escape)).Mode);
    }

    [Fact]
    public void Esc_with_changes_asks_then_enter_keeps_editing()
    {
        var state = Press(Type(InEdit(), "x"), Key(ConsoleKey.Escape));
        Assert.Equal(Prompt.ConfirmDiscard, state.Prompt);
        Assert.Equal(Mode.Edit, state.Mode);

        // Other keys don't type while the question is open.
        state = Press(state, Char('z'));
        Assert.Equal("    var s = \"xhello\";", EditedLine(state));

        state = Press(state, Key(ConsoleKey.Enter));
        Assert.Equal((Mode.Edit, Prompt.None), (state.Mode, state.Prompt));
        Assert.Equal("    var s = \"xhello\";", EditedLine(state));
    }

    [Fact]
    public void Second_esc_discards_and_returns_to_scroll_mode()
    {
        var state = Press(Type(InEdit(), "x"), Key(ConsoleKey.Escape), Key(ConsoleKey.Escape));

        Assert.Equal((Mode.Scroll, Prompt.None), (state.Mode, state.Prompt));
        Assert.Null(state.Edit);
    }

    [Fact]
    public void Ctrl_s_asks_then_enter_requests_a_commit()
    {
        var state = Press(Type(InEdit(), "x"), Ctrl(ConsoleKey.S));
        Assert.Equal(Prompt.ConfirmCommit, state.Prompt);

        state = Press(state, Key(ConsoleKey.Enter));
        Assert.Equal((Prompt.None, Effect.Commit), (state.Prompt, state.Effect));
        Assert.Equal(Mode.Edit, state.Mode);
    }

    [Fact]
    public void Ctrl_s_then_esc_returns_to_the_modified_buffer()
    {
        var state = Press(Type(InEdit(), "x"), Ctrl(ConsoleKey.S), Key(ConsoleKey.Escape));

        Assert.Equal((Mode.Edit, Prompt.None, Effect.None), (state.Mode, state.Prompt, state.Effect));
        Assert.True(state.Edit!.IsDirty);
    }

    [Fact]
    public void Ctrl_s_without_changes_only_shows_a_message()
    {
        var state = Press(InEdit(), Ctrl(ConsoleKey.S));

        Assert.Equal(Prompt.None, state.Prompt);
        Assert.Equal(Controller.NoChanges, state.Message);
    }

    [Fact]
    public void Ctrl_c_with_changes_asks_and_a_second_ctrl_c_quits()
    {
        var state = Press(Type(InEdit(), "x"), Ctrl(ConsoleKey.C));
        Assert.Equal(Prompt.ConfirmQuit, state.Prompt);
        Assert.False(state.Quit);

        Assert.Equal(Prompt.None, Press(state, Key(ConsoleKey.Escape)).Prompt);
        Assert.True(Press(state, Ctrl(ConsoleKey.C)).Quit);
        Assert.True(Press(InEdit(), Ctrl(ConsoleKey.C)).Quit); // nothing unsaved: quit at once
    }

    [Fact]
    public void Leaving_after_commit_returns_to_scroll_mode()
    {
        var state = Press(Type(InEdit(), "x"), Ctrl(ConsoleKey.S), Key(ConsoleKey.Enter));

        state = Sut.LeaveEdit(state, View);

        Assert.Equal((Mode.Scroll, Effect.None), (state.Mode, state.Effect));
        Assert.Null(state.Edit);
    }

    [Fact]
    public void Enter_inserts_a_line_break_and_the_view_follows_the_cursor()
    {
        var state = InEdit();
        for (int i = 0; i < 8; i++)
            state = Press(state, Key(ConsoleKey.Enter));

        int cursorLine = state.Edit!.CursorLine;
        Assert.Equal(27, cursorLine);
        Assert.InRange(cursorLine, state.TopLine, state.TopLine + View - 1);
        Assert.Equal(38, state.Edit.Text.Lines.Count);
    }

    [Fact]
    public void Arrow_and_page_keys_move_the_cursor_and_keep_it_visible()
    {
        var state = InEdit();

        state = Press(state, Key(ConsoleKey.UpArrow));
        Assert.Equal(18, state.Edit!.CursorLine);

        state = Press(state, Key(ConsoleKey.PageUp), Key(ConsoleKey.PageUp));
        Assert.Equal(6, state.Edit!.CursorLine);
        Assert.InRange(6, state.TopLine, state.TopLine + View - 1);

        state = Press(state, Enumerable.Repeat(Key(ConsoleKey.PageDown), 10).ToArray());
        Assert.Equal(29, state.Edit!.CursorLine);
        Assert.Equal(25, state.TopLine);
    }

    [Fact]
    public void Resize_in_edit_mode_keeps_the_cursor_visible()
    {
        var state = InEdit() with { TopLine = 0 };

        var resized = Sut.Resize(state, 3);

        Assert.InRange(19, resized.TopLine, resized.TopLine + 2);
    }

    [Fact]
    public void Edit_screen_shows_cursor_delta_and_modified_marker()
    {
        var state = Type(InEdit(), "ab");
        var session = new Session(Items, new ScanResult(Items, ["A.cs"], []), 0, new SourceCache(_ => Document));

        var frame = Screen.Render(session, state, 80, 11);

        Assert.Contains("● modified", frame.RowText(1));
        Assert.Contains("Ln 20, Col 16 │ Δ +2 −0", frame.RowText(10));
        var (row, col) = frame.Cursor!.Value;
        Assert.Equal("h", frame[row, col].Text); // cursor sits on the char after the typed text
        Assert.Contains("\"abhello\"", frame.RowText(row));
    }

    [Theory]
    [InlineData(Prompt.ConfirmCommit, "Commit changes?")]
    [InlineData(Prompt.ConfirmDiscard, "Esc discards them · Enter stays here editing")]
    [InlineData(Prompt.ConfirmQuit, "Ctrl+C again quits")]
    public void Prompts_replace_the_status_bar(Prompt prompt, string expected)
    {
        var state = Type(InEdit(), "x") with { Prompt = prompt };
        var session = new Session(Items, new ScanResult(Items, ["A.cs"], []), 0, new SourceCache(_ => Document));

        var frame = Screen.Render(session, state, 80, 11);

        Assert.Contains(expected, frame.RowText(10));
        Assert.Equal(Theme.StatusMessage, frame[10, 0].Style);
    }
}
