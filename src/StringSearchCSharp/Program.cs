using System.Text;
using StringSearch.Persistence;
using StringSearch.Scanning;

const string Usage = "Usage: StringSearch <directory> [--list]";

var positional = args.Where(a => !a.StartsWith("--")).ToList();
var flags = args.Where(a => a.StartsWith("--")).ToHashSet();
var unknown = flags.Except(["--list"]).ToList();

if (positional.Count != 1 || unknown.Count > 0)
{
    Console.Error.WriteLine(unknown.Count > 0 ? $"Unknown option: {string.Join(", ", unknown)}" : "No arg provided for the target path");
    Console.Error.WriteLine(Usage);
    return 1;
}

string targetDirectory = positional[0];

if (!Directory.Exists(targetDirectory))
{
    Console.Error.WriteLine("Directory not found.");
    return 1;
}

var scan = StringScanner.ScanDirectory(targetDirectory);
var store = SearchStore.Load(targetDirectory);
if (store.LoadWarning is not null)
    Console.Error.WriteLine($"warning: {store.LoadWarning}");

var filtered = CommittedFilter.Apply(scan.Occurrences, store.Entries);

// The interactive UI arrives in milestone 3; until then both modes print the list.
foreach (var occurrence in filtered.Visible)
    Console.WriteLine($"{occurrence.RelativePath}:{occurrence.Line}:{occurrence.Column}  {Quote(occurrence.Value)}");

int testSkips = scan.SkippedFiles.Count(f => f.Reason == SkipReason.Test);
Console.WriteLine();
Console.WriteLine(
    $"{filtered.Visible.Count} strings in {scan.ScannedFiles.Count} files; " +
    $"{filtered.Ignored.Count} ignored via {SearchStore.FileName}; " +
    $"{scan.SkippedFiles.Count} files skipped ({testSkips} test, {scan.SkippedFiles.Count - testSkips} build output)");

if (flags.Contains("--list"))
{
    foreach (var skipped in scan.SkippedFiles)
        Console.WriteLine($"  skipped ({(skipped.Reason == SkipReason.Test ? "test" : "build")}): {skipped.RelativePath}");
}

return 0;

static string Quote(string value)
{
    var sb = new StringBuilder("\"");
    foreach (var c in value)
    {
        sb.Append(c switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ when char.IsControl(c) => $"\\u{(int)c:X4}",
            _ => c.ToString(),
        });
    }
    return sb.Append('"').ToString();
}
