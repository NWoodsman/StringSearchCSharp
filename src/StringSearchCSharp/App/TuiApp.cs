using StringSearch.Editing;
using StringSearch.Persistence;
using StringSearch.Tui;

namespace StringSearch.App;

/// <summary>The interactive loop: render, read a key, update state.</summary>
public static class TuiApp
{
    /// <param name="initialMessage">Shown in the status bar on the first screen (e.g. a .searches warning).</param>
    public static void Run(Session session, SearchStore store, string? initialMessage = null)
    {
        using var terminal = new Terminal();
        var controller = new Controller(session.Items, o => session.Sources.Get(o.FilePath));
        var state = AppState.Initial with { Message = initialMessage };
        Frame? previous = null;

        while (true)
        {
            var (width, height) = terminal.Size;
            var frame = Screen.Render(session, state, width, height);
            terminal.Write(AnsiWriter.Diff(previous, frame, terminal.ColorMode));
            previous = frame;

            if (state.Quit)
                return;

            var key = terminal.ReadKeyOrResize();
            if (key is null)
            {
                // Resized: redraw everything and keep the current string in view.
                previous = null;
                state = controller.Resize(state, new Layout(terminal.Size.Width, terminal.Size.Height).SnippetHeight);
                continue;
            }

            int viewHeight = new Layout(width, height).SnippetHeight;
            state = controller.Handle(state, key.Value, viewHeight);

            if (state.Effect == Effect.Commit)
            {
                var result = Committer.Commit(session, store, state.Index, state.Edit!);
                state = result.Success
                    ? controller.LeaveEdit(state with { Index = result.Index }, viewHeight) with { Message = result.Message }
                    : state with { Effect = Effect.None, Message = result.Message };
            }
        }
    }
}
