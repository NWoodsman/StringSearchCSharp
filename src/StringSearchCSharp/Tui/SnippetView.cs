using System.Text;
using Microsoft.CodeAnalysis.Text;
using StringSearch.Model;

namespace StringSearch.Tui;

/// <summary>Draws source lines with a line-number gutter into the snippet pane.</summary>
public static class SnippetView
{
    public const int TabWidth = 4;

    /// <param name="highlight">Span of the current string.</param>
    /// <param name="baseStyle">Style for a text position outside the highlight (syntax colors); default style if null.</param>
    /// <param name="highlightStyle">Style of the highlighted span; <see cref="Theme.CurrentString"/> if null.</param>
    public static void Draw(Frame frame, Layout layout, SourceDocument document, int topLine,
        TextSpan? highlight, Func<int, Style>? baseStyle = null, Style? highlightStyle = null)
    {
        var spanStyle = highlightStyle ?? Theme.CurrentString;
        var lines = document.Text.Lines;
        int digits = Math.Max(3, lines.Count.ToString().Length);
        int codeLeft = digits + 3; // "123 │ "
        int codeWidth = Math.Max(1, frame.Width - codeLeft);

        int hlFirst = -1, hlLast = -1, left = 0;
        if (highlight is { } span)
        {
            var firstLine = lines.GetLineFromPosition(span.Start);
            hlFirst = firstLine.LineNumber;
            hlLast = lines.GetLineFromPosition(span.End).LineNumber;

            // Scroll horizontally when the string starts too far right to be seen.
            int startCol = DisplayColumn(document.Text.ToString(firstLine.Span), span.Start - firstLine.Start);
            if (startCol >= codeWidth - 4)
                left = startCol - codeWidth / 3;
        }

        for (int r = 0; r < layout.SnippetHeight; r++)
        {
            int row = layout.SnippetTop + r;
            int lineIndex = topLine + r;
            if (lineIndex < 0 || lineIndex >= lines.Count)
                continue;

            bool current = lineIndex >= hlFirst && lineIndex <= hlLast;
            frame.Write(row, 0, $"{(lineIndex + 1).ToString().PadLeft(digits)} │ ",
                current ? Theme.GutterCurrent : Theme.Gutter);

            var line = lines[lineIndex];
            var text = document.Text.ToString(line.Span);
            int col = 0;
            bool truncated = false;

            for (int i = 0; i < text.Length;)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out int consumed);
                int position = line.Start + i;
                var style = highlight is { } h && h.Contains(position)
                    ? spanStyle
                    : baseStyle?.Invoke(position) ?? Style.Default;

                if (rune.Value == '\t')
                {
                    int spaces = TabWidth - col % TabWidth;
                    for (int s = 0; s < spaces; s++, col++)
                        truncated |= !Put(frame, row, codeLeft, codeWidth, left, col, " ", 1, style);
                }
                else
                {
                    int w = CellWidth.Of(rune);
                    truncated |= !Put(frame, row, codeLeft, codeWidth, left, col, rune.ToString(), w, style);
                    col += w;
                }
                i += consumed;
            }

            if (left > 0 && col > left)
                frame.Write(row, codeLeft, "‹", Theme.Gutter);
            if (truncated && col - left > codeWidth)
                frame.Write(row, frame.Width - 1, "›", Theme.Gutter);
        }
    }

    /// <summary>Writes one glyph if it is inside the visible columns. Returns false if it was cut off on the right.</summary>
    private static bool Put(Frame frame, int row, int codeLeft, int codeWidth, int left, int col, string glyph, int width, Style style)
    {
        if (col < left)
            return true;
        if (width > 0 && col - left + width > codeWidth)
            return false;
        frame.Write(row, codeLeft + col - left, glyph, style);
        return true;
    }

    /// <summary>Display column of character offset <paramref name="offset"/> in a line, with tabs expanded.</summary>
    public static int DisplayColumn(string lineText, int offset)
    {
        int col = 0;
        for (int i = 0; i < offset && i < lineText.Length;)
        {
            Rune.DecodeFromUtf16(lineText.AsSpan(i), out var rune, out int consumed);
            col += rune.Value == '\t' ? TabWidth - col % TabWidth : CellWidth.Of(rune);
            i += consumed;
        }
        return col;
    }
}
