using Microsoft.CodeAnalysis.Text;
using StringSearch.App;
using StringSearch.Model;

namespace StringSearch.Tests.App;

public class ControllerTests
{
    private const int View = 10;

    private static StringOccurrence At(string file, int line) =>
        new(file, file, new TextSpan(0, 1), line, 1, "\"\"", "", new SnippetContext("", "", ""));

    // A.cs has 100 lines, B.cs has 5.
    private static readonly StringOccurrence[] Items = [At("A.cs", 50), At("A.cs", 3), At("B.cs", 2)];

    private static readonly Controller Sut = new(Items, o => o.FilePath == "A.cs" ? 100 : 5);

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0', bool control = false) =>
        new(c, key, shift: false, alt: false, control);

    private static ConsoleKeyInfo Char(char c) => Key(ConsoleKey.NoName, c);

    private static AppState Press(AppState state, params ConsoleKeyInfo[] keys) =>
        keys.Aggregate(state, (s, k) => Sut.Handle(s, k, View));

    private static AppState InScroll() => Press(AppState.Initial, Char('1'));

    [Fact]
    public void One_enters_scroll_mode_centered_on_the_current_string()
    {
        var state = InScroll();

        Assert.Equal(Mode.Scroll, state.Mode);
        Assert.Equal(0, state.Index);
        Assert.Equal(44, state.TopLine); // line 50 -> index 49, minus half the view
    }

    [Fact]
    public void Right_and_left_move_between_strings_and_recenter()
    {
        var state = Press(InScroll(), Key(ConsoleKey.RightArrow));
        Assert.Equal((1, 0), (state.Index, state.TopLine)); // line 3 near the top: clamped to 0

        state = Press(state, Key(ConsoleKey.RightArrow));
        Assert.Equal((2, 0), (state.Index, state.TopLine)); // 5-line file fits in the view

        state = Press(state, Key(ConsoleKey.LeftArrow), Key(ConsoleKey.LeftArrow));
        Assert.Equal((0, 44), (state.Index, state.TopLine));
    }

    [Fact]
    public void Moving_past_either_end_shows_a_message_and_stays()
    {
        var state = Press(InScroll(), Key(ConsoleKey.LeftArrow));
        Assert.Equal(0, state.Index);
        Assert.Equal("Already at the first string", state.Message);

        state = Press(state, Key(ConsoleKey.RightArrow), Key(ConsoleKey.RightArrow), Key(ConsoleKey.RightArrow));
        Assert.Equal(2, state.Index);
        Assert.Equal("Already at the last string", state.Message);
    }

    [Fact]
    public void Arrows_scroll_one_line_and_page_keys_six_within_the_file()
    {
        var state = InScroll();
        Assert.Equal(45, Press(state, Key(ConsoleKey.DownArrow)).TopLine);
        Assert.Equal(43, Press(state, Key(ConsoleKey.UpArrow)).TopLine);
        Assert.Equal(50, Press(state, Key(ConsoleKey.PageDown)).TopLine);
        Assert.Equal(38, Press(state, Key(ConsoleKey.PageUp)).TopLine);

        var bottom = Press(state, Enumerable.Repeat(Key(ConsoleKey.PageDown), 20).ToArray());
        Assert.Equal(90, bottom.TopLine); // 100 lines - 10 visible

        var top = Press(state, Enumerable.Repeat(Key(ConsoleKey.PageUp), 20).ToArray());
        Assert.Equal(0, top.TopLine);
    }

    [Fact]
    public void Home_recenters_on_the_current_string()
    {
        var state = Press(InScroll(), Key(ConsoleKey.PageDown), Key(ConsoleKey.PageDown), Key(ConsoleKey.Home));

        Assert.Equal(44, state.TopLine);
    }

    [Fact]
    public void Escape_returns_to_waiting_and_keeps_the_position()
    {
        var state = Press(InScroll(), Key(ConsoleKey.RightArrow), Key(ConsoleKey.Escape));

        Assert.Equal(Mode.Waiting, state.Mode);
        Assert.Equal(1, state.Index);
        Assert.Equal(1, Press(state, Char('1')).Index);
    }

    [Fact]
    public void Two_reports_edit_mode_unavailable_and_the_message_clears_on_the_next_key()
    {
        var state = Press(AppState.Initial, Char('2'));
        Assert.Equal(Mode.Waiting, state.Mode);
        Assert.Equal(Controller.EditNotAvailable, state.Message);

        Assert.Null(Press(state, Char('x')).Message);
        Assert.Equal(Controller.EditNotAvailable, Press(InScroll(), Char('2')).Message);
    }

    [Fact]
    public void Q_quits_from_waiting_only_and_ctrl_c_quits_anywhere()
    {
        Assert.True(Press(AppState.Initial, Char('q')).Quit);
        Assert.False(Press(InScroll(), Char('q')).Quit);
        Assert.True(Press(InScroll(), Key(ConsoleKey.C, '\u0003', control: true)).Quit);
    }

    [Fact]
    public void Resize_recenters_only_when_the_string_left_the_view()
    {
        var state = InScroll(); // top 44, string on line 50 (index 49)

        Assert.Equal(44, Sut.Resize(state, 20).TopLine);   // still visible in a taller pane
        Assert.Equal(47, Sut.Resize(state, 4).TopLine);    // 44..47 would hide it: recenter to 49 - 4/2
        // 95 is clamped to 80 (100 lines - 20), which no longer shows line 50: recenter to 49 - 10.
        Assert.Equal(39, Sut.Resize(state with { TopLine = 95 }, 20).TopLine);
        // Still visible after the resize: the scroll position is kept.
        Assert.Equal(40, Sut.Resize(state with { TopLine = 40 }, 20).TopLine);
    }

    [Fact]
    public void Empty_list_does_not_crash()
    {
        var sut = new Controller([], _ => 0);
        var state = sut.Handle(AppState.Initial, Char('1'), View);
        foreach (var key in new[] { ConsoleKey.RightArrow, ConsoleKey.LeftArrow, ConsoleKey.PageDown, ConsoleKey.Home })
            state = sut.Handle(state, Key(key), View);

        Assert.Equal(Mode.Scroll, state.Mode);
        Assert.Equal(0, sut.Resize(state, 3).TopLine);
    }
}
