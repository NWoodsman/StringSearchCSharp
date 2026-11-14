using System.Text;
using StringSearch.App;
using StringSearch.Model;
using StringSearch.Scanning;
using StringSearch.Tui;

namespace StringSearch.Tests.Model;

public class SourceDocumentTests
{
    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 }, "UTF-8 BOM")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0 }, "UTF-16 LE")]
    [InlineData(new byte[] { 0xFE, 0xFF, 0, 0x61 }, "UTF-16 BE")]
    [InlineData(new byte[] { 0x61 }, "UTF-8")]
    [InlineData(new byte[0], "UTF-8")]
    public void Describes_encoding_from_the_byte_order_mark(byte[] bytes, string expected) =>
        Assert.Equal(expected, SourceDocument.DescribeEncoding(bytes));

    [Theory]
    [InlineData("a\nb\n", "LF")]
    [InlineData("a\r\nb\r\n", "CRLF")]
    [InlineData("a\rb", "CR")]
    [InlineData("a\r\nb\n", "Mixed")]
    [InlineData("a", "None")]
    public void Describes_line_endings(string content, string expected) =>
        Assert.Equal(expected, SourceDocument.DescribeLineEndings(content));

    [Fact]
    public void Loaded_text_lines_up_with_scanner_spans_for_bom_and_crlf_files()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A.cs");
        File.WriteAllText(path, "class C\r\n{\r\n    string s = \"hi\";\r\n}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var occurrence = Assert.Single(StringScanner.ScanDirectory(dir.Path).Occurrences);
        var document = SourceDocument.Load(path);

        Assert.Equal("UTF-8 BOM", document.EncodingName);
        Assert.Equal("CRLF", document.LineEnding);
        Assert.Equal(occurrence.RawText, document.Text.ToString(occurrence.Span));
    }

    [Fact]
    public void Cache_loads_each_file_once()
    {
        int loads = 0;
        var cache = new SourceCache(p => { loads++; return SourceDocument.FromContent(p, ""); });

        cache.Get("A.cs");
        cache.Get("A.cs");
        cache.Get("B.cs");

        Assert.Equal(2, loads);
    }
}

public class SessionAndScreenTests
{
    [Fact]
    public void File_position_counts_strings_within_each_file()
    {
        using var dir = new TempDir();
        dir.Write("A.cs", "class A { string a = \"1\", b = \"2\"; }");
        dir.Write("B.cs", "class B { string c = \"3\"; }");
        var scan = StringScanner.ScanDirectory(dir.Path);

        var session = new Session(scan.Occurrences, scan, 0, new SourceCache());

        Assert.Equal([(1, 2), (2, 2), (1, 1)], Enumerable.Range(0, 3).Select(session.FilePosition));
    }

    [Theory]
    [InlineData("abcdef", 10, "abcdef")]
    [InlineData("abcdef", 4, "…def")]
    [InlineData("abcdef", 1, "…")]
    [InlineData("abcdef", 0, "")]
    public void TruncateStart_keeps_the_end(string text, int width, string expected) =>
        Assert.Equal(expected, Screen.TruncateStart(text, width));

    [Fact]
    public void Too_small_terminal_shows_a_message_instead_of_the_ui()
    {
        var scan = new ScanResult([], [], []);
        var frame = Screen.Render(new Session([], scan, 0, new SourceCache()), StringSearch.App.AppState.Initial, 30, 8);

        Assert.Equal("Terminal too small", frame.RowText(0).TrimEnd());
    }
}
