using System.Text;

namespace StringSearch.Tui;

/// <summary>
/// Owns the real console while the UI runs: alternate screen, hidden cursor, no auto-wrap and
/// raw key input. Everything is restored on <see cref="Dispose"/>, which <see cref="Program"/>
/// calls from a finally block, so a crash does not leave the user's terminal broken.
/// </summary>
public sealed class Terminal : IDisposable
{
    private const string Esc = AnsiWriter.Esc;
    private readonly bool _savedTreatControlC;
    private readonly Encoding _savedOutputEncoding;
    private bool _disposed;

    public Terminal()
    {
        _savedTreatControlC = Console.TreatControlCAsInput;
        _savedOutputEncoding = Console.OutputEncoding;
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.TreatControlCAsInput = true;
        // Alternate screen, hide cursor, disable auto-wrap (so writing the last column never scrolls).
        Write($"{Esc}?1049h{Esc}?25l{Esc}?7l{Esc}2J");
        LastSize = Size;
    }

    public ColorMode ColorMode { get; } = AnsiWriter.DetectColorMode();

    public (int Width, int Height) Size => (Console.WindowWidth, Console.WindowHeight);

    public (int Width, int Height) LastSize { get; private set; }

    /// <summary>
    /// Waits for a key. Returns null when the window size changed instead, so the caller can
    /// redraw.
    /// </summary>
    public ConsoleKeyInfo? ReadKeyOrResize()
    {
        while (true)
        {
            if (Console.KeyAvailable)
                return Console.ReadKey(intercept: true);

            var size = Size;
            if (size != LastSize)
            {
                LastSize = size;
                return null;
            }
            Thread.Sleep(20);
        }
    }

    public void Write(string text)
    {
        Console.Out.Write(text);
        Console.Out.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Write($"{Esc}0m{Esc}?7h{Esc}?25h{Esc}?1049l");
        Console.TreatControlCAsInput = _savedTreatControlC;
        Console.OutputEncoding = _savedOutputEncoding;
    }
}
