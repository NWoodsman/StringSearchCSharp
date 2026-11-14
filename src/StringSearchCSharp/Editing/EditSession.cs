using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StringSearch.Model;
using StringSearch.Tui;

namespace StringSearch.Editing;

/// <summary>
/// An edit in progress on one file. Immutable: every edit or cursor move returns a new
/// session, so the controller stays a pure function. The original text is kept, so the
/// minimal patch against it can be computed at any time.
/// </summary>
public sealed record EditSession
{
    private SourceDocument? _document;
    private (SourceText Text, bool Value)? _addsSyntaxErrors;

    /// <summary>The file as it was when editing started.</summary>
    public required SourceDocument Original { get; init; }

    /// <summary>The edited text.</summary>
    public required SourceText Text { get; init; }

    /// <summary>Cursor as a character offset into <see cref="Text"/>.</summary>
    public required int Cursor { get; init; }

    /// <summary>Display column that Up/Down try to keep; null until a vertical move.</summary>
    public int? GoalColumn { get; init; }

    /// <summary>The edited string literal's span in <see cref="Text"/>, kept in step with edits.</summary>
    public required TextSpan StringSpan { get; init; }

    /// <summary>Every change made, in order (the raw edit history).</summary>
    public ImmutableList<TextChange> Changes { get; init; } = [];

    /// <summary>The edited text as a document (for rendering and highlighting); built once per text.</summary>
    public SourceDocument Document =>
        _document is { } d && ReferenceEquals(d.Text, Text) ? d : (_document = Original.WithText(Text));

    /// <summary>Starts editing <paramref name="occurrence"/> with the cursor just inside its opening quote(s).</summary>
    public static EditSession Start(SourceDocument document, StringOccurrence occurrence)
    {
        int cursor = occurrence.Span.Start + OpeningDelimiterLength(occurrence.RawText);
        return new EditSession
        {
            Original = document,
            Text = document.Text,
            Cursor = Math.Min(cursor, occurrence.Span.End),
            StringSpan = occurrence.Span,
        };
    }

    /// <summary>Length of $, @ and quote characters before a literal's content: "a" -> 1, $@"a" -> 3, """a""" -> 3.</summary>
    public static int OpeningDelimiterLength(string rawText)
    {
        int i = 0;
        while (i < rawText.Length && rawText[i] is '$' or '@')
            i++;
        while (i < rawText.Length && rawText[i] == '"')
            i++;
        return i;
    }

    public bool IsDirty => !Text.ContentEquals(Original.Text);

    /// <summary>True when the edited text has more syntax errors than the original (e.g. an unclosed quote).</summary>
    public bool AddsSyntaxErrors
    {
        get
        {
            if (_addsSyntaxErrors is { } cached && ReferenceEquals(cached.Text, Text))
                return cached.Value;
            bool value = ErrorCount(Text) > ErrorCount(Original.Text);
            _addsSyntaxErrors = (Text, value);
            return value;
        }
    }

    private static int ErrorCount(SourceText text) =>
        CSharpSyntaxTree.ParseText(text).GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// The single change that turns the original text into the edited text (common prefix and
    /// suffix removed). Empty span and text when nothing changed.
    /// </summary>
    public TextChange Patch
    {
        get
        {
            string before = Original.Text.ToString(), after = Text.ToString();
            int prefix = 0, max = Math.Min(before.Length, after.Length);
            while (prefix < max && before[prefix] == after[prefix])
                prefix++;
            int suffix = 0;
            while (suffix < max - prefix && before[^(suffix + 1)] == after[^(suffix + 1)])
                suffix++;
            return new TextChange(
                new TextSpan(prefix, before.Length - prefix - suffix),
                after.Substring(prefix, after.Length - prefix - suffix));
        }
    }

    /// <summary>Characters inserted and deleted by <see cref="Patch"/>.</summary>
    public (int Inserted, int Deleted) Size
    {
        get
        {
            var patch = Patch;
            return (patch.NewText!.Length, patch.Span.Length);
        }
    }

    /// <summary>The file's line break, used when Enter is pressed.</summary>
    public string NewLine => Original.LineEnding switch
    {
        "CRLF" => "\r\n",
        "CR" => "\r",
        _ => "\n",
    };

    public int CursorLine => Text.Lines.GetLineFromPosition(Cursor).LineNumber;

    public EditSession Insert(string text) =>
        Apply(new TextChange(new TextSpan(Cursor, 0), text), Cursor + text.Length);

    public EditSession Backspace()
    {
        if (Cursor == 0)
            return this;
        int start = PreviousBoundary(Cursor);
        return Apply(new TextChange(TextSpan.FromBounds(start, Cursor), ""), start);
    }

    public EditSession Delete()
    {
        if (Cursor >= Text.Length)
            return this;
        return Apply(new TextChange(TextSpan.FromBounds(Cursor, NextBoundary(Cursor)), ""), Cursor);
    }

    public EditSession Left() => this with { Cursor = Cursor > 0 ? PreviousBoundary(Cursor) : 0, GoalColumn = null };

    public EditSession Right() => this with { Cursor = Cursor < Text.Length ? NextBoundary(Cursor) : Cursor, GoalColumn = null };

    /// <summary>Smart home: first non-blank character, or column 0 if already there.</summary>
    public EditSession Home()
    {
        var line = Text.Lines.GetLineFromPosition(Cursor);
        int firstNonBlank = line.Start;
        while (firstNonBlank < line.End && Text[firstNonBlank] is ' ' or '\t')
            firstNonBlank++;
        return this with { Cursor = Cursor == firstNonBlank ? line.Start : firstNonBlank, GoalColumn = null };
    }

    public EditSession End() => this with { Cursor = Text.Lines.GetLineFromPosition(Cursor).End, GoalColumn = null };

    /// <summary>Moves the cursor <paramref name="delta"/> lines, keeping the display column where possible.</summary>
    public EditSession MoveLines(int delta)
    {
        var lines = Text.Lines;
        var line = lines.GetLineFromPosition(Cursor);
        int goal = GoalColumn ?? SnippetView.DisplayColumn(Text.ToString(line.Span), Cursor - line.Start);
        var target = lines[Math.Clamp(line.LineNumber + delta, 0, lines.Count - 1)];
        int offset = OffsetForColumn(Text.ToString(target.Span), goal);
        return this with { Cursor = target.Start + offset, GoalColumn = goal };
    }

    private EditSession Apply(TextChange change, int cursor) => this with
    {
        Text = Text.WithChanges(change),
        Cursor = cursor,
        GoalColumn = null,
        StringSpan = AdjustSpan(StringSpan, change),
        Changes = Changes.Add(change),
    };

    /// <summary>
    /// Maps <paramref name="span"/> through <paramref name="change"/>. Text inserted at the
    /// span's end (after the closing quote) is outside it; text inserted at its start shifts it.
    /// </summary>
    public static TextSpan AdjustSpan(TextSpan span, TextChange change)
    {
        int p = change.Span.Start, deleted = change.Span.Length, inserted = change.NewText!.Length;
        int delta = inserted - deleted;

        if (p + deleted <= span.Start)
            return new TextSpan(span.Start + delta, span.Length);
        if (p >= span.End)
            return span;

        int start = Math.Min(span.Start, p);
        int end = p + deleted >= span.End ? p + inserted : span.End + delta;
        return TextSpan.FromBounds(start, Math.Max(start, end));
    }

    /// <summary>Character offset in a line whose display column is closest to, but not past, <paramref name="column"/>.</summary>
    public static int OffsetForColumn(string lineText, int column)
    {
        int offset = 0;
        while (offset < lineText.Length)
        {
            int next = offset + (char.IsSurrogatePair(lineText, offset) ? 2 : 1);
            if (SnippetView.DisplayColumn(lineText, next) > column)
                break;
            offset = next;
        }
        return offset;
    }

    /// <summary>Next cursor stop: steps over CRLF and surrogate pairs as one unit.</summary>
    private int NextBoundary(int position)
    {
        if (position + 1 < Text.Length &&
            ((Text[position] == '\r' && Text[position + 1] == '\n') ||
             (char.IsHighSurrogate(Text[position]) && char.IsLowSurrogate(Text[position + 1]))))
            return position + 2;
        return position + 1;
    }

    private int PreviousBoundary(int position)
    {
        if (position >= 2 &&
            ((Text[position - 2] == '\r' && Text[position - 1] == '\n') ||
             (char.IsHighSurrogate(Text[position - 2]) && char.IsLowSurrogate(Text[position - 1]))))
            return position - 2;
        return position - 1;
    }
}
