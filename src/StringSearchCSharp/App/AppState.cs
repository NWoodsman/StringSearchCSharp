using StringSearch.Editing;

namespace StringSearch.App;

public enum Mode
{
    /// <summary>Mode 0: help screen, waiting for 1 or 2.</summary>
    Waiting,
    /// <summary>Mode 1: browse found strings.</summary>
    Scroll,
    /// <summary>Mode 2: edit the current string and its context.</summary>
    Edit,
}

/// <summary>A question shown in the status bar; it captures keys until answered.</summary>
public enum Prompt
{
    None,
    /// <summary>Ctrl+S: Enter commits, Esc returns to editing.</summary>
    ConfirmCommit,
    /// <summary>Esc with changes: Esc discards them, Enter stays editing.</summary>
    ConfirmDiscard,
    /// <summary>Ctrl+C with changes: Ctrl+C quits without saving, Esc returns.</summary>
    ConfirmQuit,
}

/// <summary>Work the controller asks the app loop to do (it has side effects, so it is not done in the controller).</summary>
public enum Effect
{
    None,
    /// <summary>Write the edit to disk and update .searches (milestone 6).</summary>
    Commit,
}

/// <summary>Everything the UI needs to know about where the user is. Immutable.</summary>
/// <param name="Mode">Current mode.</param>
/// <param name="Index">Index of the current string in the visible list.</param>
/// <param name="TopLine">0-based first source line shown in the snippet pane.</param>
/// <param name="Message">One-shot message shown in the status bar; cleared by the next key.</param>
/// <param name="Quit">Set when the app should exit.</param>
/// <param name="Edit">The edit in progress (edit mode only).</param>
/// <param name="Prompt">Open question, if any.</param>
/// <param name="Effect">Pending side effect for the app loop.</param>
public sealed record AppState(
    Mode Mode,
    int Index,
    int TopLine,
    string? Message = null,
    bool Quit = false,
    EditSession? Edit = null,
    Prompt Prompt = Prompt.None,
    Effect Effect = Effect.None)
{
    public static readonly AppState Initial = new(Mode.Waiting, 0, 0);
}
