using StringSearch.App;
using StringSearch.Model;
using StringSearch.Persistence;
using StringSearch.Scanning;

const string Usage = "Usage: StringSearch <directory> [--list]";
const string Help = $"""
    {Usage}

    Finds the string literals in the C# files under <directory> and lets you review and edit
    them in a full-screen terminal UI. Strings you commit are recorded in <directory>/.searches
    and skipped on later runs; delete that file to review everything again.

    Skipped: bin/, obj/ and .git/, and test code (a folder or file name that is "test" or
    "tests", ends in Test/Tests, or starts with test(s) followed by a capital letter or . _ -).

    Options:
      --list       print the strings instead of opening the UI (also used when not in a terminal)
      -h, --help   show this help

    Keys:
      1            scroll mode: ←/→ previous/next string, ↑/↓ one line, PgUp/PgDn six lines,
                   Home re-center, Esc back
      2            edit mode: type to edit, Ctrl+S commit, Esc leave (both ask first)
      q / Ctrl+C   quit
    """;

if (args.Any(a => a is "-h" or "--help"))
{
    Console.WriteLine(Help);
    return 0;
}

var positional = args.Where(a => !a.StartsWith('-')).ToList();
var flags = args.Where(a => a.StartsWith('-')).ToHashSet();
var unknown = flags.Except(["--list"]).ToList();

if (unknown.Count > 0 || positional.Count != 1)
{
    Console.Error.WriteLine(unknown.Count > 0
        ? $"Unknown option: {string.Join(", ", unknown)}"
        : positional.Count == 0 ? "No directory given." : "Only one directory can be given.");
    Console.Error.WriteLine(Usage);
    Console.Error.WriteLine("Run with --help for details.");
    return 1;
}

string targetDirectory = Path.GetFullPath(positional[0]);

if (!Directory.Exists(targetDirectory))
{
    Console.Error.WriteLine($"Directory not found: {targetDirectory}");
    return 1;
}

bool interactive = !flags.Contains("--list") && !Console.IsInputRedirected && !Console.IsOutputRedirected;

if (interactive)
    Console.Error.Write("Scanning …"); // short, so it never wraps and the \r below clears it

ScanResult scan;
try
{
    scan = StringScanner.ScanDirectory(targetDirectory);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Could not scan {targetDirectory}: {ex.Message}");
    return 1;
}

var store = SearchStore.Load(targetDirectory);
var filtered = CommittedFilter.Apply(scan.Occurrences, store.Entries);

if (interactive)
{
    Console.Error.Write("\r\u001b[K");
    var session = new Session(filtered.Visible, scan, filtered.Ignored.Count, new SourceCache());
    try
    {
        TuiApp.Run(session, store, initialMessage: store.LoadWarning);
    }
    catch (Exception ex)
    {
        // The terminal is already restored (TuiApp disposes it before the exception gets here).
        Console.Error.WriteLine($"StringSearch stopped because of an unexpected error: {ex.Message}");
        Console.Error.WriteLine("Committed edits up to this point were saved. Details:");
        Console.Error.WriteLine(ex);
        return 2;
    }

    Console.WriteLine(session.CommittedCount == 0
        ? "No strings committed."
        : $"{session.CommittedCount} string{(session.CommittedCount == 1 ? "" : "s")} committed; recorded in {store.FilePath}.");
    return 0;
}

if (store.LoadWarning is not null)
    Console.Error.WriteLine($"warning: {store.LoadWarning} ({store.LoadWarningDetail})");
if (!flags.Contains("--list"))
    Console.Error.WriteLine("Not running in a terminal; printing the list instead.");

foreach (var occurrence in filtered.Visible)
    Console.WriteLine($"{occurrence.RelativePath}:{occurrence.Line}:{occurrence.Column}  {Display.Quote(occurrence.Value)}");

int Count(SkipReason reason) => scan.SkippedFiles.Count(f => f.Reason == reason);
Console.WriteLine();
Console.WriteLine(
    $"{filtered.Visible.Count} strings in {scan.ScannedFiles.Count} files; " +
    $"{filtered.Ignored.Count} ignored via {SearchStore.FileName}; " +
    $"{scan.SkippedFiles.Count} files skipped ({Count(SkipReason.Test)} test, {Count(SkipReason.BuildOutput)} build output" +
    (Count(SkipReason.Unreadable) > 0 ? $", {Count(SkipReason.Unreadable)} unreadable" : "") + ")");

if (flags.Contains("--list"))
{
    foreach (var skipped in scan.SkippedFiles)
    {
        var reason = skipped.Reason switch
        {
            SkipReason.Test => "test",
            SkipReason.BuildOutput => "build",
            _ => "unreadable",
        };
        Console.WriteLine($"  skipped ({reason}): {skipped.RelativePath}{(skipped.Detail is null ? "" : $" ({skipped.Detail})")}");
    }
}

return 0;
