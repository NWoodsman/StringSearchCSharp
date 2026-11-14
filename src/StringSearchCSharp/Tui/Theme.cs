namespace StringSearch.Tui;

/// <summary>
/// Colors used by the UI. Foreground and background are always set together where a
/// background is used, so text stays readable on both light and dark terminals.
/// </summary>
public static class Theme
{
    // Position bar gradient: start -> middle -> end of the result list.
    public static readonly Rgb BarStart = Rgb.FromHex(0x4F8CFF);
    public static readonly Rgb BarMiddle = Rgb.FromHex(0xF5A524);
    public static readonly Rgb BarEnd = Rgb.FromHex(0x3FB950);

    public static readonly Style Muted = new(Attr: TextAttr.Dim);
    public static readonly Style Rule = new(Fg: Rgb.FromHex(0x6E7681));
    public static readonly Style Path = new(Attr: TextAttr.Bold);
    public static readonly Style Gutter = new(Fg: Rgb.FromHex(0x6E7681));
    public static readonly Style GutterCurrent = new(Fg: Rgb.FromHex(0xF5A524), Attr: TextAttr.Bold);
    public static readonly Style CurrentString = new(Fg: Rgb.FromHex(0xFFE9A8), Bg: Rgb.FromHex(0x4A3B00));
    public static readonly Style StatusBar = new(Fg: Rgb.FromHex(0xE6EDF3), Bg: Rgb.FromHex(0x30363D));
    public static readonly Style StatusHint = new(Fg: Rgb.FromHex(0xA0A8B0), Bg: Rgb.FromHex(0x30363D));
    public static readonly Style StatusMessage = new(Fg: Rgb.FromHex(0x1F1A00), Bg: Rgb.FromHex(0xF5A524), Attr: TextAttr.Bold);

    public static Style ModeBadge(App.Mode mode) => mode switch
    {
        App.Mode.Scroll => new(Fg: Rgb.FromHex(0x0D1117), Bg: Rgb.FromHex(0x4F8CFF), Attr: TextAttr.Bold),
        App.Mode.Edit => new(Fg: Rgb.FromHex(0x0D1117), Bg: Rgb.FromHex(0x3FB950), Attr: TextAttr.Bold),
        _ => new(Fg: Rgb.FromHex(0x0D1117), Bg: Rgb.FromHex(0xA0A8B0), Attr: TextAttr.Bold),
    };

    /// <summary>Gradient color at <paramref name="fraction"/> (0 = first result, 1 = last).</summary>
    public static Rgb BarColor(double fraction) => fraction < 0.5
        ? Rgb.Lerp(BarStart, BarMiddle, fraction * 2)
        : Rgb.Lerp(BarMiddle, BarEnd, (fraction - 0.5) * 2);
}
