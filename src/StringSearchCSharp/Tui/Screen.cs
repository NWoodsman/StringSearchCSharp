using StringSearch.App;
using StringSearch.Persistence;
using StringSearch.Scanning;

namespace StringSearch.Tui;

/// <summary>Composes a full frame from the session and the current state.</summary>
public static class Screen
{
    public static Frame Render(Session session, AppState state, int width, int height)
    {
        var frame = new Frame(width, height);
        var layout = new Layout(width, height);

        if (layout.TooSmall)
        {
            frame.Write(0, 0, "Terminal too small", Theme.Path);
            frame.Write(1, 0, $"{width}x{height}, need {Layout.MinWidth}x{Layout.MinHeight}", Theme.Muted);
            return frame;
        }

        var items = session.Items;
        PositionBar.Draw(frame, layout.BarRow, state.Index, items.Count);
        DrawLocation(frame, layout, session, state);
        DrawInfo(frame, layout, session);
        frame.Fill(layout.TopRuleRow, 0, width, Theme.Rule, "─");
        frame.Fill(layout.BottomRuleRow, 0, width, Theme.Rule, "─");

        if (state.Mode == Mode.Waiting || items.Count == 0)
        {
            DrawHelp(frame, layout, session);
        }
        else
        {
            DrawSnippet(frame, layout, session, state);
        }

        DrawStatus(frame, layout, session, state);
        return frame;
    }

    private static void DrawSnippet(Frame frame, Layout layout, Session session, AppState state)
    {
        var occurrence = session.Items[state.Index];

        if (state.Edit is { } edit)
        {
            var edited = edit.Document;
            var editClasses = Highlighter.For(edited);
            frame.Cursor = SnippetView.Draw(frame, layout, edited, state.TopLine, edit.StringSpan,
                position => position < editClasses.Length ? Highlighter.StyleOf(editClasses[position]) : Style.Default,
                Theme.EditedString, edit.Cursor);
            return;
        }

        var document = session.Sources.Get(occurrence.FilePath);

        if (session.IsCommitted(state.Index))
        {
            // Done: gray out the whole snippet so it reads as finished.
            SnippetView.Draw(frame, layout, document, state.TopLine, occurrence.Span,
                _ => Theme.Committed, Theme.CommittedString);
            return;
        }

        var classes = Highlighter.For(document);
        SnippetView.Draw(frame, layout, document, state.TopLine, occurrence.Span,
            position => position < classes.Length ? Highlighter.StyleOf(classes[position]) : Style.Default);
    }

    private static void DrawLocation(Frame frame, Layout layout, Session session, AppState state)
    {
        var badge = $" {state.Mode.ToString().ToUpperInvariant()} ";
        int badgeCol = layout.Width - badge.Length;
        frame.Write(layout.LocationRow, badgeCol, badge, Theme.ModeBadge(state.Mode));

        if (session.Items.Count == 0)
        {
            frame.Write(layout.LocationRow, 0, "No strings to review", Theme.Path, badgeCol - 1);
            return;
        }

        var occurrence = session.Items[state.Index];
        var (position, count) = session.FilePosition(state.Index);
        var location = TruncateStart($"{occurrence.RelativePath}:{occurrence.Line}:{occurrence.Column}", badgeCol - 1);
        int col = frame.Write(layout.LocationRow, 0, location, Theme.Path, badgeCol - 1);
        col = frame.Write(layout.LocationRow, col, $"   string {position}/{count} in file", Theme.Muted, badgeCol - 1);
        if (state.Edit is { IsDirty: true })
            frame.Write(layout.LocationRow, col, "   ● modified", Theme.ModifiedBadge, badgeCol - 1);
        else if (session.IsCommitted(state.Index))
            frame.Write(layout.LocationRow, col, "   ✓ committed", Theme.CommittedBadge, badgeCol - 1);
    }

    /// <summary>Keeps the end of <paramref name="text"/> (the file name and position), dropping the start.</summary>
    public static string TruncateStart(string text, int width)
    {
        if (width <= 0)
            return "";
        if (CellWidth.Of(text) <= width)
            return text;
        var runes = text.EnumerateRunes().ToList();
        int used = 1; // the ellipsis
        int start = runes.Count;
        while (start > 0 && used + CellWidth.Of(runes[start - 1]) <= width)
            used += CellWidth.Of(runes[--start]);
        return "…" + string.Concat(runes.Skip(start).Select(r => r.ToString()));
    }

    private static void DrawInfo(Frame frame, Layout layout, Session session)
    {
        var skipped = session.Scan.SkippedFiles;
        int tests = skipped.Count(f => f.Reason == SkipReason.Test);
        frame.Write(layout.InfoRow, 0,
            $"{session.CommittedCount} committed this session · {session.IgnoredCount} ignored via {SearchStore.FileName} · " +
            $"{session.Scan.ScannedFiles.Count} files scanned · skipped {tests} test, {skipped.Count - tests} build files",
            Theme.Muted);
    }

    private static void DrawHelp(Frame frame, Layout layout, Session session)
    {
        string[] lines = session.Items.Count == 0
            ?
            [
                "Nothing to review: every string is in a skipped file or listed in .searches.",
                "",
                "q          quit",
            ]
            :
            [
                $"{session.Items.Count} strings to review.",
                "",
                "1          scroll mode: browse the found strings",
                "2          edit mode: edit the current string",
                "Ctrl+C     quit from anywhere",
                "q          quit",
                "",
                "In scroll mode:",
                "← →        previous / next string",
                "↑ ↓        scroll one line",
                "PgUp PgDn  scroll six lines",
                "Home       re-center on the current string",
                "Esc        back to this screen",
                "",
                "In edit mode:",
                "type       insert text (digits too); Enter, Tab, Backspace, Delete",
                "arrows     move the cursor; Home/End line start/end; PgUp PgDn six lines",
                "Ctrl+S     commit (asks first)",
                "Esc        leave (asks first if there are changes)",
            ];

        for (int i = 0; i < lines.Length && i < layout.SnippetHeight; i++)
            frame.Write(layout.SnippetTop + i, 2, lines[i], i == 0 ? Theme.Path : Style.Default);
    }

    private static void DrawStatus(Frame frame, Layout layout, Session session, AppState state)
    {
        int row = layout.StatusRow;
        frame.Fill(row, 0, layout.Width, Theme.StatusBar);

        if (PromptText(state.Prompt) is { } prompt)
        {
            frame.Fill(row, 0, layout.Width, Theme.StatusMessage);
            frame.Write(row, 1, prompt, Theme.StatusMessage);
            return;
        }

        string left;
        if (state.Edit is { } edit)
        {
            var line = edit.Text.Lines.GetLineFromPosition(edit.Cursor);
            var (inserted, deleted) = edit.Size;
            left = $" Ln {line.LineNumber + 1}, Col {edit.Cursor - line.Start + 1} │ Δ +{inserted} −{deleted} │ " +
                   $"{edit.Original.EncodingName} │ {edit.Original.LineEnding} │ {edit.Document.LineCount} lines";
        }
        else if (state.Mode == Mode.Scroll && session.Items.Count > 0)
        {
            var occurrence = session.Items[state.Index];
            var document = session.Sources.Get(occurrence.FilePath);
            left = $" Ln {occurrence.Line}, Col {occurrence.Column} │ {document.EncodingName} │ {document.LineEnding} │ {document.LineCount} lines";
        }
        else
        {
            left = $" {session.Items.Count} strings";
        }
        int leftEnd = frame.Write(row, 0, left, Theme.StatusBar);

        var (text, style) = state.Message is { } message
            ? ($" {message} ", Theme.StatusMessage)
            : (Hints(state.Mode) + " ", Theme.StatusHint);
        int col = Math.Max(leftEnd + 2, layout.Width - CellWidth.Of(text));
        frame.Write(row, col, text, style);
    }

    private static string Hints(Mode mode) => mode switch
    {
        Mode.Scroll => "←→ string  ↑↓ PgUp PgDn scroll  Home center  2 edit  Esc back",
        Mode.Edit => "type to edit  Ctrl+S commit  Esc leave",
        _ => "1 scroll  2 edit  q quit",
    };

    public static string? PromptText(Prompt prompt) => prompt switch
    {
        Prompt.ConfirmCommit => "Commit changes?  Enter commits · Esc returns to editing",
        Prompt.ConfirmDiscard => "Unsaved changes.  Esc discards them · Enter stays here editing",
        Prompt.ConfirmQuit => "Unsaved changes.  Ctrl+C again quits without saving · Esc returns",
        _ => null,
    };
}
