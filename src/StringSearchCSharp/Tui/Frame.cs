using System.Text;

namespace StringSearch.Tui;

/// <summary>
/// One screen cell. <see cref="Text"/> is a single glyph (possibly with combining marks), or
/// empty for the second half of a wide glyph.
/// </summary>
public readonly record struct Cell(string Text, Style Style)
{
    public static readonly Cell Blank = new(" ", Style.Default);
}

/// <summary>A width x height grid of cells that is composed off-screen and then flushed.</summary>
public sealed class Frame
{
    private readonly Cell[] _cells;

    public Frame(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new Cell[width * height];
        Array.Fill(_cells, Cell.Blank);
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Where the terminal's text cursor is shown; null hides it.</summary>
    public (int Row, int Col)? Cursor { get; set; }

    public Cell this[int row, int col] => _cells[row * Width + col];

    public ReadOnlySpan<Cell> Row(int row) => _cells.AsSpan(row * Width, Width);

    /// <summary>Paints <paramref name="width"/> blank cells with <paramref name="style"/>.</summary>
    public void Fill(int row, int col, int width, Style style, string glyph = " ")
    {
        if (row < 0 || row >= Height)
            return;
        int end = Math.Min(Width, col + width);
        for (int c = Math.Max(0, col); c < end; c++)
            _cells[row * Width + c] = new Cell(glyph, style);
    }

    /// <summary>
    /// Writes <paramref name="text"/> starting at <paramref name="col"/>, stopping before
    /// <paramref name="maxCol"/> (default: frame width). Wide glyphs take two cells and are
    /// dropped if only one cell is left. Zero-width runes join the previous glyph. Control
    /// characters are shown as '·'. Returns the column after the last written cell.
    /// </summary>
    public int Write(int row, int col, string text, Style style, int maxCol = int.MaxValue)
    {
        if (row < 0 || row >= Height)
            return col;
        maxCol = Math.Min(maxCol, Width);

        foreach (var rune in text.EnumerateRunes())
        {
            int w = CellWidth.Of(rune);
            if (w == 0)
            {
                if (col > 0 && col <= maxCol)
                {
                    int lead = col - 1;
                    while (lead > 0 && _cells[row * Width + lead].Text.Length == 0)
                        lead--;
                    ref var prev = ref _cells[row * Width + lead];
                    prev = prev with { Text = prev.Text + rune };
                }
                continue;
            }

            if (col + w > maxCol)
                break;

            var glyph = IsControl(rune) ? "·" : rune.ToString();
            _cells[row * Width + col] = new Cell(glyph, style);
            if (w == 2)
                _cells[row * Width + col + 1] = new Cell("", style);
            col += w;
        }
        return col;
    }

    /// <summary>The visible text of a row, for tests and debugging.</summary>
    public string RowText(int row)
    {
        var sb = new StringBuilder();
        foreach (var cell in Row(row))
            sb.Append(cell.Text);
        return sb.ToString();
    }

    private static bool IsControl(Rune rune) => rune.Value < 0x20 || (rune.Value >= 0x7F && rune.Value < 0xA0);
}
