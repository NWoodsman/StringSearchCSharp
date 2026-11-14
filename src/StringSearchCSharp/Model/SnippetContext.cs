using Microsoft.CodeAnalysis.Text;

namespace StringSearch.Model;

/// <summary>
/// The source lines around a string literal, trimmed. Line numbers are deliberately not part of
/// the context, so it still matches after lines are inserted or removed elsewhere in the file.
/// </summary>
/// <param name="Before">Nearest non-blank line above the literal, or empty at the start of the file.</param>
/// <param name="Text">The line(s) holding the literal, each trimmed, joined with '\n'.</param>
/// <param name="After">Nearest non-blank line below the literal, or empty at the end of the file.</param>
public sealed record SnippetContext(string Before, string Text, string After)
{
    /// <summary>
    /// Loose match used to recognize a committed string: the literal's own line(s) must be equal,
    /// and at least one neighbor line must be equal.
    /// </summary>
    public bool Matches(SnippetContext other) =>
        Text == other.Text && (Before == other.Before || After == other.After);

    public static SnippetContext FromSource(SourceText source, TextSpan span)
    {
        var lines = source.Lines;
        int first = lines.GetLineFromPosition(span.Start).LineNumber;
        int last = lines.GetLineFromPosition(span.End).LineNumber;

        var text = string.Join('\n',
            Enumerable.Range(first, last - first + 1).Select(i => lines[i].ToString().Trim()));

        return new SnippetContext(
            NearestNonBlank(lines, first - 1, step: -1),
            text,
            NearestNonBlank(lines, last + 1, step: +1));
    }

    private static string NearestNonBlank(TextLineCollection lines, int start, int step)
    {
        for (int i = start; i >= 0 && i < lines.Count; i += step)
        {
            var trimmed = lines[i].ToString().Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }
        return "";
    }
}
