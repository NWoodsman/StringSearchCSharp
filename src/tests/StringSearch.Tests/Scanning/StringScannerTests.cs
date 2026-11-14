using StringSearch.Model;
using StringSearch.Scanning;

namespace StringSearch.Tests.Scanning;

public class StringScannerTests
{
    [Fact]
    public void Finds_every_literal_syntax()
    {
        const string code = """"
            class C
            {
                string a = "regular";
                string b = @"C:\verbatim";
                string c = """raw""";
                System.ReadOnlySpan<byte> d => "bytes"u8;
                char e = 'x';
                // "comment"
            }
            """";

        var found = StringScanner.ScanSource(code);

        Assert.Equal(["regular", @"C:\verbatim", "raw", "bytes"], found.Select(o => o.Value));
    }

    [Fact]
    public void Reports_one_based_line_and_column_and_raw_text()
    {
        var found = StringScanner.ScanSource("class C\n{\n    string s = \"hi\";\n}\n");

        var occurrence = Assert.Single(found);
        Assert.Equal(3, occurrence.Line);
        Assert.Equal(16, occurrence.Column);
        Assert.Equal("\"hi\"", occurrence.RawText);
    }

    [Fact]
    public void Interpolated_string_is_one_result_with_holes_as_written()
    {
        // Escape sequences are resolved, but escaped braces stay doubled, so a literal "{{z}}"
        // can't be confused with a hole "{z}".
        var found = StringScanner.ScanSource("class C { string M(int n) => \"a\" + $\"x\\t{n,3:F1}y{{z}}\"; }");

        Assert.Equal(["a", "x\t{n,3:F1}y{{z}}"], found.Select(o => o.Value));
        Assert.Equal("$\"x\\t{n,3:F1}y{{z}}\"", found[1].RawText);
    }

    [Fact]
    public void Literals_inside_interpolation_holes_are_separate_results_in_source_order()
    {
        var found = StringScanner.ScanSource("class C { string M() => $\"<{Get(\"key\")}>\"; string Get(string k) => k; }");

        Assert.Equal(["<{Get(\"key\")}>", "key"], found.Select(o => o.Value));
    }

    [Fact]
    public void Context_holds_trimmed_line_and_nearest_non_blank_neighbors()
    {
        const string code = "class C\n{\n    int x;\n\n    string s = \"hi\";\n\n\n    int y;\n}\n";

        var occurrence = Assert.Single(StringScanner.ScanSource(code));

        Assert.Equal(new SnippetContext("int x;", "string s = \"hi\";", "int y;"), occurrence.Context);
    }

    [Fact]
    public void Context_of_multi_line_literal_covers_every_line()
    {
        const string code = "class C {\n  string s = @\"one\n    two\";\n}";

        var occurrence = Assert.Single(StringScanner.ScanSource(code));

        Assert.Equal(new SnippetContext("class C {", "string s = @\"one\ntwo\";", "}"), occurrence.Context);
    }

    [Fact]
    public void Context_is_empty_at_file_edges()
    {
        var occurrence = Assert.Single(StringScanner.ScanSource("class C { string s = \"hi\"; }"));

        Assert.Equal("", occurrence.Context.Before);
        Assert.Equal("", occurrence.Context.After);
    }

    [Fact]
    public void ScanDirectory_skips_build_and_test_files_and_uses_relative_paths()
    {
        using var dir = new TempDir();
        dir.Write("src/A.cs", "class A { string s = \"a\"; }");
        dir.Write("src/Latest.cs", "class L { string s = \"l\"; }");
        dir.Write("src/obj/G.cs", "class G { string s = \"g\"; }");
        dir.Write("bin/B.cs", "class B { string s = \"b\"; }");
        dir.Write("tests/T.cs", "class T { string s = \"t\"; }");
        dir.Write("src/FooTests.cs", "class F { string s = \"f\"; }");

        var result = StringScanner.ScanDirectory(dir.Path);

        Assert.Equal(["src/A.cs", "src/Latest.cs"], result.Occurrences.Select(o => o.RelativePath));
        Assert.Equal(["src/A.cs", "src/Latest.cs"], result.ScannedFiles);
        Assert.Equal(
            [
                new SkippedFile("bin/B.cs", SkipReason.BuildOutput),
                new SkippedFile("src/FooTests.cs", SkipReason.Test),
                new SkippedFile("src/obj/G.cs", SkipReason.BuildOutput),
                new SkippedFile("tests/T.cs", SkipReason.Test),
            ],
            result.SkippedFiles);
    }
}
