using StringSearch.Tui;

namespace StringSearch.App;

/// <summary>The interactive loop: render, read a key, update state.</summary>
public static class TuiApp
{
    public static void Run(Session session)
    {
        using var terminal = new Terminal();
        var controller = new Controller(session.Items, o => session.Sources.Get(o.FilePath).LineCount);
        var state = AppState.Initial;
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

            state = controller.Handle(state, key.Value, new Layout(width, height).SnippetHeight);
        }
    }
}
