namespace StringSearch.Tui;

/// <summary>
/// The header's progress bar: braille cells that fill left to right as the user moves through
/// the results, a marker at the tip, and an "XX/XXX" label on the right.
/// </summary>
public static class PositionBar
{
    /// <summary>Partial cell glyphs for 1..7 eighths: left column bottom-up, then right column.</summary>
    private static readonly string[] Partial = ["⡀", "⡄", "⡆", "⡇", "⣇", "⣧", "⣷"];
    private const string Full = "⣿";
    public const string Marker = "●";

    /// <summary>Draws the bar into <paramref name="frame"/> row <paramref name="row"/>, full width.</summary>
    /// <param name="index">0-based position of the current result.</param>
    /// <param name="count">Number of results.</param>
    public static void Draw(Frame frame, int row, int index, int count)
    {
        string label = Label(index, count);
        int labelCol = Math.Max(0, frame.Width - label.Length);
        frame.Write(row, labelCol, label, Theme.Path);

        // Cells left of the label: one space gap, one cell reserved for the marker.
        int fillCells = Math.Max(0, labelCol - 2);
        if (count == 0 || fillCells == 0)
            return;

        double fraction = (index + 1) / (double)count;
        int units = (int)Math.Round(fraction * fillCells * 8);
        int full = units / 8, partial = units % 8;

        for (int c = 0; c < full; c++)
            frame.Write(row, c, Full, new Style(Fg: Theme.BarColor((c + 1) / (double)fillCells)));

        int tip = full;
        if (partial > 0)
        {
            frame.Write(row, tip, Partial[partial - 1], new Style(Fg: Theme.BarColor((tip + 1) / (double)fillCells)));
            tip++;
        }

        frame.Write(row, tip, Marker, new Style(Fg: Theme.BarColor(fraction), Attr: TextAttr.Bold));
    }

    /// <summary>"current/total", padded so the label width does not change while moving.</summary>
    public static string Label(int index, int count)
    {
        string total = count.ToString();
        string current = (count == 0 ? 0 : index + 1).ToString().PadLeft(total.Length);
        return $"{current}/{total}";
    }
}
