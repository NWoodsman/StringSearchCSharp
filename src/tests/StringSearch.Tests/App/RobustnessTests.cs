using StringSearch.App;
using StringSearch.Model;
using StringSearch.Scanning;
using StringSearch.Tui;

namespace StringSearch.Tests.App;

public class RobustnessTests
{
    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.NoName, false, false, false);

    [Fact]
    public void Unreadable_file_is_skipped_with_a_reason_instead_of_failing_the_scan()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            return; // chmod 000 doesn't block reads there
        using var dir = new TempDir();
        dir.Write("A.cs", "class A { string a = \"a\"; }");
        var locked = dir.Write("B.cs", "class B { string b = \"b\"; }");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            var result = StringScanner.ScanDirectory(dir.Path);

            Assert.Equal(["A.cs"], result.ScannedFiles);
            var skipped = Assert.Single(result.SkippedFiles);
            Assert.Equal(("B.cs", SkipReason.Unreadable), (skipped.RelativePath, skipped.Reason));
            Assert.NotNull(skipped.Detail);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Missing_file_becomes_a_placeholder_document()
    {
        var document = SourceCache.LoadOrPlaceholder("/definitely/not/here/A.cs");

        Assert.NotNull(document.LoadError);
        Assert.Equal(1, document.LineCount);
    }

    [Fact]
    public void Edit_is_refused_for_a_vanished_or_changed_file_and_the_screen_says_why()
    {
        const string code = "class A { string a = \"hello\"; }";
        var items = StringScanner.ScanSource(code, "A.cs", "A.cs");
        var scan = new ScanResult(items, ["A.cs"], []);

        // Vanished.
        var gone = SourceDocument.Unreadable("A.cs", "Could not find file");
        var refused = new Controller(items, _ => gone).Handle(AppState.Initial, Char('2'), 5);
        Assert.Equal(Mode.Waiting, refused.Mode);
        Assert.Equal("Cannot edit: Could not find file", refused.Message);

        var frame = Screen.Render(new Session(items, scan, 0, new SourceCache(_ => gone)), new AppState(Mode.Scroll, 0, 0), 60, 12);
        Assert.Equal("Could not read this file:", frame.RowText(4).Trim());

        // Changed: the literal is no longer at the scanned position.
        var changed = SourceDocument.FromContent("A.cs", "// new header line\n" + code);
        refused = new Controller(items, _ => changed).Handle(AppState.Initial, Char('2'), 5);
        Assert.Equal(Mode.Waiting, refused.Mode);
        Assert.Contains("changed since the scan", refused.Message);

        frame = Screen.Render(new Session(items, scan, 0, new SourceCache(_ => changed)), new AppState(Mode.Scroll, 0, 0), 80, 12);
        Assert.Contains("⚠ changed since scan", frame.RowText(1));
    }

    [Fact]
    public void Unchanged_file_matches_its_occurrences()
    {
        const string code = "class A { string a = \"hello\"; }";
        var occurrence = StringScanner.ScanSource(code, "A.cs", "A.cs").Single();

        Assert.True(SourceDocument.FromContent("A.cs", code).Matches(occurrence));
    }
}
