using System.Text;

namespace StringSearch.Tui;

public enum ColorMode
{
    TrueColor,
    Palette256,
}

/// <summary>Turns frames into ANSI escape output.</summary>
public static class AnsiWriter
{
    public const string Esc = "\u001b[";

    public static ColorMode DetectColorMode()
    {
        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
        return colorTerm.Contains("truecolor", StringComparison.OrdinalIgnoreCase) ||
               colorTerm.Contains("24bit", StringComparison.OrdinalIgnoreCase)
            ? ColorMode.TrueColor
            : ColorMode.Palette256;
    }

    /// <summary>
    /// Output that turns the screen showing <paramref name="previous"/> into <paramref name="next"/>.
    /// Changed rows are rewritten whole, which keeps wide glyphs consistent; rows are short, so
    /// this is cheap. A null or differently sized previous frame redraws everything.
    /// </summary>
    public static string Diff(Frame? previous, Frame next, ColorMode mode)
    {
        bool full = previous is null || previous.Width != next.Width || previous.Height != next.Height;
        var sb = new StringBuilder();
        if (full)
            sb.Append(Esc).Append("0m").Append(Esc).Append("2J");

        for (int row = 0; row < next.Height; row++)
        {
            if (!full && previous!.Row(row).SequenceEqual(next.Row(row)))
                continue;

            sb.Append(Esc).Append(row + 1).Append(";1H");
            Style? current = null;
            foreach (var cell in next.Row(row))
            {
                if (cell.Text.Length == 0)
                    continue; // second half of a wide glyph; the terminal already advanced
                if (current != cell.Style)
                {
                    AppendSgr(sb, cell.Style, mode);
                    current = cell.Style;
                }
                sb.Append(cell.Text);
            }
            sb.Append(Esc).Append("0m");
        }
        return sb.ToString();
    }

    private static void AppendSgr(StringBuilder sb, Style style, ColorMode mode)
    {
        sb.Append(Esc).Append('0');
        if (style.Attr.HasFlag(TextAttr.Bold)) sb.Append(";1");
        if (style.Attr.HasFlag(TextAttr.Dim)) sb.Append(";2");
        if (style.Attr.HasFlag(TextAttr.Underline)) sb.Append(";4");
        if (style.Attr.HasFlag(TextAttr.Inverse)) sb.Append(";7");
        if (style.Fg is { } fg) AppendColor(sb, 38, fg, mode);
        if (style.Bg is { } bg) AppendColor(sb, 48, bg, mode);
        sb.Append('m');
    }

    private static void AppendColor(StringBuilder sb, int code, Rgb c, ColorMode mode)
    {
        if (mode == ColorMode.TrueColor)
        {
            sb.Append(';').Append(code).Append(";2;").Append(c.R).Append(';').Append(c.G).Append(';').Append(c.B);
            return;
        }
        static int Level(byte v) => (int)Math.Round(v / 255.0 * 5);
        sb.Append(';').Append(code).Append(";5;").Append(16 + 36 * Level(c.R) + 6 * Level(c.G) + Level(c.B));
    }
}
