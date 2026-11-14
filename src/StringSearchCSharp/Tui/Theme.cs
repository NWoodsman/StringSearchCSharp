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

    // Syntax colors (foreground only, mid-tone so they work on dark and light backgrounds).
    public static readonly Style Keyword = new(Fg: Rgb.FromHex(0x569CD6));
    public static readonly Style ControlKeyword = new(Fg: Rgb.FromHex(0xC586C0));
    public static readonly Style TypeName = new(Fg: Rgb.FromHex(0x2BB5A0));
    public static readonly Style MethodName = new(Fg: Rgb.FromHex(0xC8A24A));
    public static readonly Style StringLiteral = new(Fg: Rgb.FromHex(0xCE9178));
    public static readonly Style NumberLiteral = new(Fg: Rgb.FromHex(0x8FBF7F));
    public static readonly Style Comment = new(Fg: Rgb.FromHex(0x6A9955), Attr: TextAttr.Italic);
    public static readonly Style Preprocessor = new(Fg: Rgb.FromHex(0x9B9B9B));

    // Committed strings: the whole snippet loses its syntax colors.
    public static readonly Style Committed = new(Fg: Rgb.FromHex(0x6E7681));
    public static readonly Style CommittedString = new(Fg: Rgb.FromHex(0xA0A8B0), Bg: Rgb.FromHex(0x2D333B), Attr: TextAttr.Strikethrough);
    // Ignored strings: a warmer, darker gray than committed, without strike-through.
    public static readonly Style Ignored = new(Fg: Rgb.FromHex(0x857A6E));
    public static readonly Style IgnoredString = new(Fg: Rgb.FromHex(0xB0A595), Bg: Rgb.FromHex(0x3A332C), Attr: TextAttr.Italic);
    public static readonly Style IgnoredBadge = new(Fg: Rgb.FromHex(0xB0A595), Attr: TextAttr.Bold);

    public static readonly Style EditedString = new(Fg: Rgb.FromHex(0xFFE9A8), Bg: Rgb.FromHex(0x1F3A2A));
    public static readonly Style ModifiedBadge = new(Fg: Rgb.FromHex(0xF5A524), Attr: TextAttr.Bold);
    public static readonly Style CommittedBadge = new(Fg: Rgb.FromHex(0x3FB950), Attr: TextAttr.Bold);

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
