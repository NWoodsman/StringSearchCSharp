using Microsoft.CodeAnalysis.Text;
using StringSearch.App;
using StringSearch.Model;
using StringSearch.Scanning;
using StringSearch.Tui;

namespace StringSearch.Tests.Tui;

public class HighlighterTests
{
    private const string Code = """
        #if DEBUG
        using System.Collections.Generic;
        #endif
        /// <summary>Doc</summary>
        [Obsolete]
        public sealed class Greeter<T> : IDisposable
        {
            public Greeter() { }
            // line comment
            public async Task<int> Run(List<string> items, dynamic d)
            {
                var count = 42;
                var name = nameof(Run);
                var s = $"Hello {count} world";
                char c = 'x';
                if (count > 0) return Helper(count);
                await Task.Delay(1);
                var list = new List<int>();
                object o = typeof(Greeter<T>);
                /* block */ return default;
            }
            private static int Helper(int v) => v;
        }
        """;

    private static readonly TokenClass[] Classes = Highlighter.Classify(SourceText.From(Code));

    /// <summary>Class of the first character of the <paramref name="nth"/> occurrence of <paramref name="text"/>.</summary>
    private static TokenClass At(string text, int nth = 0)
    {
        int index = -1;
        for (int i = 0; i <= nth; i++)
            index = Code.IndexOf(text, index + 1, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{text}' not found");
        return Classes[index];
    }

    [Theory]
    [InlineData("public", TokenClass.Keyword)]
    [InlineData("sealed", TokenClass.Keyword)]
    [InlineData("async", TokenClass.Keyword)]
    [InlineData("int>", TokenClass.Keyword)]
    [InlineData("var count", TokenClass.Keyword)]
    [InlineData("dynamic", TokenClass.Keyword)]
    [InlineData("nameof", TokenClass.Keyword)]
    [InlineData("default", TokenClass.Keyword)]
    [InlineData("if (", TokenClass.ControlKeyword)]
    [InlineData("return Helper", TokenClass.ControlKeyword)]
    [InlineData("await", TokenClass.ControlKeyword)]
    [InlineData("Greeter<T>", TokenClass.Type)]
    [InlineData("T>", TokenClass.Type)]
    [InlineData("IDisposable", TokenClass.Type)]
    [InlineData("Greeter()", TokenClass.Type)]
    [InlineData("Task<int>", TokenClass.Type)]
    [InlineData("List<string>", TokenClass.Type)]
    [InlineData("List<int>()", TokenClass.Type)]
    [InlineData("Obsolete", TokenClass.Type)]
    [InlineData("Run(List", TokenClass.Method)]
    [InlineData("Helper(count)", TokenClass.Method)]
    [InlineData("Delay", TokenClass.Method)]
    [InlineData("Helper(int", TokenClass.Method)]
    [InlineData("$\"Hello", TokenClass.String)]
    [InlineData(" world\"", TokenClass.String)]
    [InlineData("'x'", TokenClass.String)]
    [InlineData("42", TokenClass.Number)]
    [InlineData("// line", TokenClass.Comment)]
    [InlineData("/// <summary>", TokenClass.Comment)]
    [InlineData("Doc", TokenClass.Comment)]
    [InlineData("/* block */", TokenClass.Comment)]
    [InlineData("#if DEBUG", TokenClass.Preprocessor)]
    [InlineData("DEBUG", TokenClass.Preprocessor)]
    [InlineData("#endif", TokenClass.Preprocessor)]
    public void Classifies_tokens(string text, TokenClass expected) => Assert.Equal(expected, At(text));

    [Theory]
    [InlineData("count}")]   // interpolation hole
    [InlineData("items,")]   // parameter name
    [InlineData("count = 42")]
    [InlineData("v;")]
    [InlineData("{ }")]
    public void Plain_identifiers_and_punctuation_are_unclassified(string text) =>
        Assert.Equal(TokenClass.None, At(text));

    [Fact]
    public void Disabled_code_is_shown_as_comment()
    {
        var code = "#if NEVER\nint x = 1;\n#endif\n";
        var classes = Highlighter.Classify(SourceText.From(code));

        Assert.Equal(TokenClass.Comment, classes[code.IndexOf("int", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Classification_is_cached_per_document()
    {
        var document = SourceDocument.FromContent("A.cs", "class A { }");

        Assert.Same(Highlighter.For(document), Highlighter.For(document));
    }
}

public class ScreenHighlightTests
{
    private const string Code = "class A\n{\n    string s = \"hi\";\n}\n";

    private static (Frame Frame, int Row) Render(bool committed)
    {
        var occurrences = StringScanner.ScanSource(Code, "A.cs", "A.cs");
        var cache = new SourceCache(p => SourceDocument.FromContent(p, Code));
        var session = new Session(occurrences, new ScanResult(occurrences, ["A.cs"], []), 0, cache);
        if (committed)
            session.MarkCommitted(0);

        var frame = Screen.Render(session, new AppState(Mode.Scroll, 0, 0), 60, 12);
        return (frame, 4 + 2); // snippet starts at row 4; the string is on line 3
    }

    private static int Col(Frame frame, int row, string text) => frame.RowText(row).IndexOf(text, StringComparison.Ordinal);

    [Fact]
    public void Snippet_uses_syntax_colors_and_the_current_string_highlight()
    {
        var (frame, row) = Render(committed: false);

        Assert.Equal(Theme.Keyword, frame[row, Col(frame, row, "string")].Style);
        Assert.Equal(Theme.CurrentString, frame[row, Col(frame, row, "\"hi\"")].Style);
        Assert.Equal(Theme.Keyword, frame[4, Col(frame, 4, "class")].Style);
        Assert.DoesNotContain("committed", frame.RowText(1));
    }

    [Fact]
    public void Committed_string_grays_out_the_snippet_and_is_marked_in_the_header()
    {
        var (frame, row) = Render(committed: true);

        Assert.Equal(Theme.Committed, frame[row, Col(frame, row, "string")].Style);
        Assert.Equal(Theme.Committed, frame[4, Col(frame, 4, "class")].Style);
        Assert.Equal(Theme.CommittedString, frame[row, Col(frame, row, "\"hi\"")].Style);
        Assert.Contains("✓ committed", frame.RowText(1));
        Assert.Contains("1 committed · 0 ignored this session", frame.RowText(2));
    }
}
