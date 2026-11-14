using System.Globalization;
using System.Text;

namespace StringSearch.Tui;

/// <summary>Number of terminal columns a code point occupies (0, 1 or 2).</summary>
public static class CellWidth
{
    // East Asian Wide/Fullwidth and common emoji blocks. Not exhaustive, but covers the
    // characters likely to appear in string literals.
    private static readonly (int Start, int End)[] Wide =
    [
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC), (0x23F0, 0x23F0),
        (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F),
        (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5),
        (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA), (0x26F2, 0x26F5), (0x26FA, 0x26FD),
        (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728), (0x274C, 0x274C), (0x2753, 0x2755),
        (0x2757, 0x2757), (0x2795, 0x2797), (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C),
        (0x2B50, 0x2B50), (0x2B55, 0x2B55), (0x2E80, 0x303E), (0x3041, 0x33FF), (0x3400, 0x4DBF),
        (0x4E00, 0x9FFF), (0xA000, 0xA4CF), (0xA960, 0xA97F), (0xAC00, 0xD7A3), (0xF900, 0xFAFF),
        (0xFE10, 0xFE19), (0xFE30, 0xFE6F), (0xFF00, 0xFF60), (0xFFE0, 0xFFE6), (0x16FE0, 0x18AFF),
        (0x1B000, 0x1B2FF), (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF), (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A), (0x1F200, 0x1F2FF), (0x1F300, 0x1F64F), (0x1F680, 0x1F6FF),
        (0x1F7E0, 0x1F7EB), (0x1F90C, 0x1F9FF), (0x1FA70, 0x1FAFF), (0x20000, 0x3FFFD),
    ];

    public static int Of(Rune rune)
    {
        int value = rune.Value;
        if (value < 0x20 || (value >= 0x7F && value < 0xA0))
            return 1; // control: rendered as a replacement glyph by the caller

        switch (Rune.GetUnicodeCategory(rune))
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.Format: // ZWJ, variation selectors, ...
                return 0;
        }

        foreach (var (start, end) in Wide)
        {
            if (value < start)
                break;
            if (value <= end)
                return 2;
        }
        return 1;
    }

    /// <summary>Total display width of <paramref name="text"/> (no tab handling).</summary>
    public static int Of(string text)
    {
        int width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += Of(rune);
        return width;
    }
}
