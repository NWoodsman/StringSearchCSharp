namespace StringSearch.Tui;

/// <summary>
/// Screen rows: position bar, location line, info line, rule, snippet pane, rule, status bar.
/// </summary>
public readonly record struct Layout(int Width, int Height)
{
    public const int MinWidth = 40;
    public const int MinHeight = 10;

    public bool TooSmall => Width < MinWidth || Height < MinHeight;

    public int BarRow => 0;
    public int LocationRow => 1;
    public int InfoRow => 2;
    public int TopRuleRow => 3;
    public int SnippetTop => 4;
    public int SnippetHeight => Math.Max(1, Height - 6);
    public int BottomRuleRow => Height - 2;
    public int StatusRow => Height - 1;
}
