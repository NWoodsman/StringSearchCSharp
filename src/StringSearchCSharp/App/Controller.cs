using StringSearch.Editing;
using StringSearch.Model;

namespace StringSearch.App;

/// <summary>
/// The modal state machine: maps (state, key) to the next state. It never touches the console
/// or the file system (side effects are requested through <see cref="AppState.Effect"/>), so
/// every transition can be unit tested.
/// </summary>
public sealed class Controller(IReadOnlyList<StringOccurrence> items, Func<StringOccurrence, SourceDocument> documents)
{
    /// <summary>Lines moved by PgUp/PgDn.</summary>
    public const int PageLines = 6;

    public const string NoChanges = "No changes to commit";

    /// <param name="viewHeight">Number of source lines the snippet pane shows.</param>
    public AppState Handle(AppState state, ConsoleKeyInfo key, int viewHeight)
    {
        state = state with { Message = null };

        if (IsCtrl(key, ConsoleKey.C))
        {
            bool unsaved = state.Edit is { IsDirty: true };
            return !unsaved || state.Prompt == Prompt.ConfirmQuit
                ? state with { Quit = true }
                : state with { Prompt = Prompt.ConfirmQuit };
        }

        if (state.Prompt != Prompt.None)
            return HandlePrompt(state, key, viewHeight);

        return state.Mode switch
        {
            Mode.Waiting => HandleWaiting(state, key, viewHeight),
            Mode.Scroll => HandleScroll(state, key, viewHeight),
            Mode.Edit => HandleEdit(state, key, viewHeight),
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
        return state with { Index = index, TopLine = ClampTop(line - viewHeight / 2, LineCount(index), viewHeight) };
    }

    /// <summary>
    /// After the pane height changed: keeps the scroll position inside the file, and brings the
    /// current string (or the cursor, when editing) back into view.
    /// </summary>
    public AppState Resize(AppState state, int viewHeight)
    {
        if (items.Count == 0)
            return state;
        if (state.Edit is not null)
            return FollowCursor(state, viewHeight);

        int top = ClampTop(state.TopLine, LineCount(state.Index), viewHeight);
        int line = items[state.Index].Line - 1;
        return line >= top && line < top + viewHeight
            ? state with { TopLine = top }
            : Focus(state, state.Index, viewHeight);
    }

    /// <summary>Called by the app loop after it carried out a commit (or failed to).</summary>
    public AppState LeaveEdit(AppState state, int viewHeight) =>
        state with
        {
            Mode = Mode.Scroll,
            Edit = null,
            Prompt = Prompt.None,
            Effect = Effect.None,
            TopLine = ClampTop(state.TopLine, LineCount(state.Index), viewHeight),
        };

    private AppState HandleWaiting(AppState state, ConsoleKeyInfo key, int viewHeight) => key.KeyChar switch
    {
        '1' => Focus(state with { Mode = Mode.Scroll }, state.Index, viewHeight),
        '2' => EnterEdit(state, viewHeight),
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

        return key.KeyChar == '2' ? EnterEdit(state, viewHeight) : state;
    }

    private AppState EnterEdit(AppState state, int viewHeight)
    {
        if (items.Count == 0)
            return state with { Message = "No string to edit" };
        var occurrence = items[state.Index];
        var edit = EditSession.Start(documents(occurrence), occurrence);
        var entered = state.Mode == Mode.Waiting ? Focus(state, state.Index, viewHeight) : state;
        return FollowCursor(entered with { Mode = Mode.Edit, Edit = edit }, viewHeight);
    }

    /// <summary>Edit mode: only Esc leaves; every printable key, digits included, is typed.</summary>
    private AppState HandleEdit(AppState state, ConsoleKeyInfo key, int viewHeight)
    {
        var edit = state.Edit!;

        if (IsCtrl(key, ConsoleKey.S))
            return edit.IsDirty ? state with { Prompt = Prompt.ConfirmCommit } : state with { Message = NoChanges };

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return edit.IsDirty ? state with { Prompt = Prompt.ConfirmDiscard } : LeaveEdit(state, viewHeight);
            case ConsoleKey.PageUp:
            case ConsoleKey.PageDown:
                int delta = key.Key == ConsoleKey.PageUp ? -PageLines : PageLines;
                // Move the view and the cursor together, so the cursor keeps its place on screen.
                state = state with { TopLine = ClampTop(state.TopLine + delta, edit.Document.LineCount, viewHeight) };
                return FollowCursor(state with { Edit = edit.MoveLines(delta) }, viewHeight);
        }

        var next = key.Key switch
        {
            ConsoleKey.LeftArrow => edit.Left(),
            ConsoleKey.RightArrow => edit.Right(),
            ConsoleKey.UpArrow => edit.MoveLines(-1),
            ConsoleKey.DownArrow => edit.MoveLines(+1),
            ConsoleKey.Home => edit.Home(),
            ConsoleKey.End => edit.End(),
            ConsoleKey.Backspace => edit.Backspace(),
            ConsoleKey.Delete => edit.Delete(),
            ConsoleKey.Enter => edit.Insert(edit.NewLine),
            ConsoleKey.Tab => edit.Insert("\t"),
            _ when IsTypable(key) => edit.Insert(key.KeyChar.ToString()),
            _ => edit,
        };

        return ReferenceEquals(next, edit) ? state : FollowCursor(state with { Edit = next }, viewHeight);
    }

    private AppState HandlePrompt(AppState state, ConsoleKeyInfo key, int viewHeight)
    {
        bool enter = key.Key == ConsoleKey.Enter, escape = key.Key == ConsoleKey.Escape;
        return state.Prompt switch
        {
            Prompt.ConfirmCommit when enter => state with { Prompt = Prompt.None, Effect = Effect.Commit },
            Prompt.ConfirmCommit when escape => state with { Prompt = Prompt.None },
            Prompt.ConfirmDiscard when escape => LeaveEdit(state, viewHeight),
            Prompt.ConfirmDiscard when enter => state with { Prompt = Prompt.None },
            Prompt.ConfirmQuit when escape => state with { Prompt = Prompt.None },
            _ => state, // any other key: the question stays open
        };
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
        return state with { TopLine = ClampTop(state.TopLine + delta, LineCount(state.Index), viewHeight) };
    }

    /// <summary>Scrolls just enough to keep the edit cursor's line inside the pane.</summary>
    private static AppState FollowCursor(AppState state, int viewHeight)
    {
        var edit = state.Edit!;
        int line = edit.CursorLine;
        int top = state.TopLine;
        if (line < top)
            top = line;
        else if (line >= top + viewHeight)
            top = line - viewHeight + 1;
        return state with { TopLine = ClampTop(top, edit.Document.LineCount, viewHeight) };
    }

    private int LineCount(int index) => documents(items[index]).LineCount;

    /// <summary>Keeps the pane inside the file: top >= 0, and no blank space below the last line.</summary>
    private static int ClampTop(int top, int lineCount, int viewHeight) =>
        Math.Clamp(top, 0, Math.Max(0, lineCount - viewHeight));

    private static bool IsCtrl(ConsoleKeyInfo key, ConsoleKey k) =>
        key.Key == k && key.Modifiers.HasFlag(ConsoleModifiers.Control);

    /// <summary>A key that produces text: no Ctrl/Alt chord, and not a control character.</summary>
    private static bool IsTypable(ConsoleKeyInfo key) =>
        !key.Modifiers.HasFlag(ConsoleModifiers.Control) &&
        !key.Modifiers.HasFlag(ConsoleModifiers.Alt) &&
        key.KeyChar != '\0' && !char.IsControl(key.KeyChar);
}
