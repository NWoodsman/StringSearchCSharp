namespace StringSearch.Tui;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(uint hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    /// <summary>Linear interpolation, <paramref name="t"/> clamped to [0, 1].</summary>
    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}

[Flags]
public enum TextAttr
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Underline = 4,
    Inverse = 8,
}

/// <summary>Foreground/background color (null = terminal default) plus attributes.</summary>
public readonly record struct Style(Rgb? Fg = null, Rgb? Bg = null, TextAttr Attr = TextAttr.None)
{
    public static readonly Style Default = new();
}
