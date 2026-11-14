using StringSearch.Model;

namespace StringSearch.App;

/// <summary>
/// The modal state machine: maps (state, key) to the next state. It never touches the console,
/// so every transition can be unit tested.
/// </summary>
public sealed class Controller(IReadOnlyList<StringOccurrence> items, Func<StringOccurrence, int> lineCount)
{
    /// <summary>Lines moved by PgUp/PgDn.</summary>
    public const int PageLines = 6;

    public const string EditNotAvailable = "Edit mode is not implemented yet";

    /// <param name="viewHeight">Number of source lines the snippet pane shows.</param>
    public AppState Handle(AppState state, ConsoleKeyInfo key, int viewHeight)
    {
        state = state with { Message = null };

        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            return state with { Quit = true };

        return state.Mode switch
        {
            Mode.Waiting => HandleWaiting(state, key, viewHeight),
            Mode.Scroll => HandleScroll(state, key, viewHeight),
            _ => state,
        };
    }

    /// <summary>Selects string <paramref name="index"/> and centers its first line in the pane.</summary>
    public AppState Focus(AppState state, int index, int viewHeight)
    {
        if (items.Count == 0)
            return state;
        index = Math.Clamp(index, 0, items.Count - 1);
        int line = items[index].Line - 1;
        return state with { Index = index, TopLine = ClampTop(line - viewHeight / 2, items[index], viewHeight) };
    }

    /// <summary>
    /// After the pane height changed: keeps the scroll position inside the file, and re-centers
    /// if the current string's first line is no longer visible.
    /// </summary>
    public AppState Resize(AppState state, int viewHeight)
    {
        if (items.Count == 0)
            return state;
        var occurrence = items[state.Index];
        int top = ClampTop(state.TopLine, occurrence, viewHeight);
        int line = occurrence.Line - 1;
        return line >= top && line < top + viewHeight
            ? state with { TopLine = top }
            : Focus(state, state.Index, viewHeight);
    }

    private AppState HandleWaiting(AppState state, ConsoleKeyInfo key, int viewHeight) => key.KeyChar switch
    {
        '1' => Focus(state with { Mode = Mode.Scroll }, state.Index, viewHeight),
        '2' => state with { Message = EditNotAvailable },
        'q' or 'Q' => state with { Quit = true },
        _ => state,
    };

    private AppState HandleScroll(AppState state, ConsoleKeyInfo key, int viewHeight)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape: return state with { Mode = Mode.Waiting };
            case ConsoleKey.RightArrow: return Move(state, +1, viewHeight);
            case ConsoleKey.LeftArrow: return Move(state, -1, viewHeight);
            case ConsoleKey.UpArrow: return ScrollBy(state, -1, viewHeight);
            case ConsoleKey.DownArrow: return ScrollBy(state, +1, viewHeight);
            case ConsoleKey.PageUp: return ScrollBy(state, -PageLines, viewHeight);
            case ConsoleKey.PageDown: return ScrollBy(state, +PageLines, viewHeight);
            case ConsoleKey.Home: return Focus(state, state.Index, viewHeight);
        }

        return key.KeyChar == '2' ? state with { Message = EditNotAvailable } : state;
    }

    private AppState Move(AppState state, int delta, int viewHeight)
    {
        if (items.Count == 0)
            return state;
        int target = state.Index + delta;
        if (target < 0)
            return state with { Message = "Already at the first string" };
        if (target >= items.Count)
            return state with { Message = "Already at the last string" };
        return Focus(state, target, viewHeight);
    }

    private AppState ScrollBy(AppState state, int delta, int viewHeight)
    {
        if (items.Count == 0)
            return state;
        return state with { TopLine = ClampTop(state.TopLine + delta, items[state.Index], viewHeight) };
    }

    /// <summary>Keeps the pane inside the file: top >= 0, and no blank space below the last line.</summary>
    private int ClampTop(int top, StringOccurrence occurrence, int viewHeight) =>
        Math.Clamp(top, 0, Math.Max(0, lineCount(occurrence) - viewHeight));
}
