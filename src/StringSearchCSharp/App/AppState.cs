namespace StringSearch.App;

public enum Mode
{
    /// <summary>Mode 0: help screen, waiting for 1 or 2.</summary>
    Waiting,
    /// <summary>Mode 1: browse found strings.</summary>
    Scroll,
    /// <summary>Mode 2: edit the current string (milestone 5).</summary>
    Edit,
}

/// <summary>Everything the UI needs to know about where the user is. Immutable.</summary>
/// <param name="Mode">Current mode.</param>
/// <param name="Index">Index of the current string in the visible list.</param>
/// <param name="TopLine">0-based first source line shown in the snippet pane.</param>
/// <param name="Message">One-shot message shown in the status bar; cleared by the next key.</param>
/// <param name="Quit">Set when the app should exit.</param>
public sealed record AppState(Mode Mode, int Index, int TopLine, string? Message = null, bool Quit = false)
{
    public static readonly AppState Initial = new(Mode.Waiting, 0, 0);
}
